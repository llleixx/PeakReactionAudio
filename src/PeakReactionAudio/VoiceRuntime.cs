using System;
using System.Reflection;
using HarmonyLib;
using PeakReactionAudio.Core;
using Photon.Pun;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace PeakReactionAudio;

// Unity binding only. VoiceSession owns playback, PTT/VAD coordination and retirement.
internal sealed class VoiceRuntime : IDisposable
{
    private static readonly FieldInfo VoiceField = Field(typeof(Recorder), "voice");
    private static readonly FieldInfo ConnectionField = Field(typeof(Recorder), "voiceConnection");
    private static readonly FieldInfo PermissionField = Field(typeof(CharacterVoiceHandler), "m_allowedToCommunicate");
    private static readonly FieldInfo PlayerDataField = Field(typeof(CharacterVoiceHandler), "m_characterData");
    private readonly Plugin _plugin;
    private readonly AudioLibrary _library;
    private VoiceSession? _session;
    private RecorderControl? _control;
    private LocalMonitor? _monitor;
    private bool _failed;
    private int _loggedMixedFrames;
    private double _nextSendLog;

    public VoiceRuntime(Plugin plugin, AudioLibrary library) { _plugin = plugin; _library = library; }
    private static FieldInfo Field(Type type, string name)
        => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

    public void Update(Character? character)
    {
        if (_session == null) return;
        try
        {
            if (character == null || !_plugin.Options.Enabled.Value || !PhotonNetwork.InRoom ||
                _control!.Recorder == null || _control.Recorder.GetComponentInParent<Character>() != character)
                Unbind();
            else if (_control.CurrentVoice != _session.Voice) Unbind(false);
            else if (_session.Processor.Faulted)
                throw new InvalidOperationException("Voice audio callback failed.");
            else if (_monitor?.Faulted == true)
            {
                _monitor.Dispose(); _monitor = null;
                _plugin.Log.LogWarning("Local monitor disabled after an audio callback error; voice transmission continues.");
            }
            LogSendProgress();
        }
        catch (Exception ex) { Fail(ex); }
    }

    // The only gate-update entry: original PushToTalk has just computed its state.
    public void OnPushToTalk(CharacterVoiceHandler handler, bool requested)
    {
        if (_failed) return;
        Character character = handler.GetComponentInParent<Character>();
        if (character == null || !character.IsLocal || !handler.Initialized) return;
        if (!_plugin.Options.Enabled.Value || !PhotonNetwork.InRoom)
        {
            if (_control?.Handler == handler)
            {
                _session!.ClearPlayback();
                _session.ApplyGate(requested, _control.Allowed);
                Unbind();
            }
            return;
        }
        Recorder recorder = handler.GetComponent<Recorder>();
        if (recorder == null || !recorder.RecordingEnabled) return;
        LocalVoice? voice = VoiceField.GetValue(recorder) as LocalVoice;
        if (!(voice is LocalVoiceAudio<float>) && !(voice is LocalVoiceAudio<short>)) return;
        if (_session?.Voice != voice)
        {
            Unbind(_control != null && _control.Recorder != recorder);
            _control = new RecorderControl(recorder, handler, voice);
            _session = new VoiceSession(voice, _control, requested, _control.Allowed);
            _loggedMixedFrames = 0; _nextSendLog = 0;
            try { _monitor = new LocalMonitor(_plugin.gameObject, _session.LocalBus, _session.SampleRate); }
            catch (Exception ex) { _plugin.Log.LogWarning($"Local monitor unavailable; voice transmission continues: {ex.Message}"); }
            _library.RequestRate(_session.SampleRate);
            _plugin.Log.LogInfo($"Bound {voice.GetType().Name}: {_session.SampleRate} Hz, {voice.Info.Channels} channel(s); {_control.State}.");
        }
        _session.Processor.MasterGain = ModConfig.Safe(_plugin.Options.MasterVolume, 0.5f);
        _monitor?.SetGain(_session.Processor.MasterGain);
        _session.ApplyGate(requested, _control!.Allowed, _control.Ready, localMonitorReady: _monitor != null);
    }

    private void LogSendProgress()
    {
        VoiceSession? session = _session;
        if (session == null || !_plugin.Options.Debug.Value) return;
        int mixed = session.Processor.MixedFrames;
        if (mixed == _loggedMixedFrames || Time.unscaledTimeAsDouble < _nextSendLog) return;
        _loggedMixedFrames = mixed; _nextSendLog = Time.unscaledTimeAsDouble + 1;
        _plugin.Log.LogInfo($"VoiceSend: mixedFrames={mixed}, micBlockedFrames={session.Processor.MicBlockedFrames}; " +
            $"sdkSentFrames={session.Voice.FramesSent}, sdkSentBytes={session.Voice.FramesSentBytes}; " +
            $"transmit={session.Voice.TransmitEnabled}, {_control!.State}. SDK totals include normal voice/EOS; receiver delivery unverified.");
    }

    public bool TryPlay(ReactionId id, float volume, out AudioAsset? selected, out string reason)
    {
        selected = null;
        reason = string.Empty;
        try
        {
            if (_failed) { reason = "voice integration stopped after an error"; return false; }
            if (_session == null) { reason = "no local voice session"; return false; }
            bool pcmReady = _library.IsPrepared(_session.SampleRate);
            if (!PhotonNetwork.InRoom || !pcmReady || !_control!.Allowed)
            {
                reason = $"playback blocked: gameRoom={PhotonNetwork.InRoom}, pcmReady={pcmReady}, {_control!.State}";
                return false;
            }
            AudioAsset? asset = _library.Select(id, _session.SampleRate);
            if (asset == null) { reason = $"no clip prepared at {_session.SampleRate} Hz"; return false; }
            if (!_session.RequestPlay(id, asset.Variants[_session.SampleRate], volume))
            { reason = "same event already pending/playing, or processor stopped"; return false; }
            if (!_control.Ready) reason = "local monitor only; network voice is not joined";
            _library.Accepted(id, asset); selected = asset;
            return true;
        }
        catch (Exception ex) { reason = ex.Message; Fail(ex); return false; }
    }

    public void RecorderStopping(Recorder recorder) { if (_control?.Recorder == recorder) Unbind(false); }
    public void DspChanged(WebRtcAudioDsp dsp)
    {
        // Removing/reappending a live processor would create the same unguarded
        // frame window as teardown. A DSP rebuild instead retires this session.
        if (_control?.Dsp == dsp) Unbind();
    }
    public void Unbind(bool recreateRecording = true)
    {
        VoiceSession? session = _session;
        RecorderControl? control = _control;
        LocalMonitor? monitor = _monitor;
        _session = null; _control = null; _monitor = null; // native StopRecording may re-enter
        try { session?.Close(recreateRecording && control!.CurrentVoice == session.Voice); }
        finally { monitor?.Dispose(); }
    }
    public void Fail(Exception ex)
    {
        if (_failed) return;
        _failed = true;
        _plugin.Log.LogError($"Reaction voice disabled: {ex}");
        try { Unbind(); }
        catch (Exception cleanup) { _plugin.Log.LogError($"Recording retirement error: {cleanup}"); }
    }
    public void Dispose() => Unbind();

    private sealed class RecorderControl : IVoiceControl
    {
        private readonly LocalVoice _voice;
        public RecorderControl(Recorder recorder, CharacterVoiceHandler handler, LocalVoice voice)
        { Recorder = recorder; Handler = handler; _voice = voice; Dsp = recorder.GetComponent<WebRtcAudioDsp>(); }
        public Recorder Recorder { get; }
        public CharacterVoiceHandler Handler { get; }
        public WebRtcAudioDsp? Dsp { get; }
        public LocalVoice? CurrentVoice => Recorder != null ? VoiceField.GetValue(Recorder) as LocalVoice : null;
        public bool Ready => Recorder != null && Recorder.RecordingEnabled && Handler.Initialized &&
            ConnectionField.GetValue(Recorder) is VoiceConnection c && c != null && c.ClientState == Photon.Realtime.ClientState.Joined;
        public bool Allowed => PermissionField.GetValue(Handler) is bool allowed && allowed &&
            (!(PlayerDataField.GetValue(Handler) is CharacterData data) || !data.isBlocked);
        public string State => $"recording={Recorder.RecordingEnabled}, initialized={Handler.Initialized}, " +
            $"voiceRoom={(ConnectionField.GetValue(Recorder) is VoiceConnection c && c != null ? c.ClientState.ToString() : "unavailable")}, allowed={Allowed}";
        public bool TransmitEnabled
        { set { if (CurrentVoice == _voice) Recorder.TransmitEnabled = value; } }
        public bool VoiceDetection
        { get => Recorder != null && Recorder.VoiceDetection; set { if (Recorder != null) Recorder.VoiceDetection = value; } }
        public bool DspVoiceDetection
        { get => Dsp != null && Dsp.VAD; set { if (Dsp != null) Dsp.VAD = value; } }
        public void RecreateRecording(bool vanillaTransmit)
        {
            if (CurrentVoice != _voice) return;
            bool recording = Recorder.RecordingEnabled;
            Recorder.RecordingEnabled = false; // synchronous native retirement under Photon's encoder lock
            Recorder.TransmitEnabled = vanillaTransmit;
            Recorder.RecordingEnabled = recording; // original Recorder schedules a fresh stream
        }
    }
}
