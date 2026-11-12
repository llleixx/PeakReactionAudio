using System;
using PeakReactionAudio.Core;
using Photon.Voice;

namespace PeakReactionAudio;

// The small Unity boundary makes the real session lifecycle testable against
// Photon, without loading a Unity scene or replacing its processing pipeline.
internal interface IVoiceControl
{
    bool TransmitEnabled { set; }
    bool VoiceDetection { get; set; }
    bool DspVoiceDetection { get; set; }
    void RecreateRecording(bool vanillaTransmit);
}

internal sealed class VoiceSession
{
    private readonly IVoiceControl _control;
    private readonly DetectionOverride _detection = new DetectionOverride();
    private readonly float[]?[] _pending = new float[]?[Limits.ReactionCount];
    private readonly float[] _gains = new float[Limits.ReactionCount];
    private bool _vanillaTransmit;
    public VoiceSession(LocalVoice voice, IVoiceControl control, bool microphoneRequested, bool allowed)
    {
        Voice = voice; _control = control;
        Processor = voice is LocalVoiceAudio<float>
            ? (VoiceProcessor)new FloatVoiceProcessor(voice.Info.SamplingRate, voice.Info.Channels, voice.Info.FrameSize)
            : new ShortVoiceProcessor(voice.Info.SamplingRate, voice.Info.Channels, voice.Info.FrameSize);
        LocalBus = new MixBus(voice.Info.SamplingRate);
        _vanillaTransmit = microphoneRequested && allowed;
        Processor.MicrophoneAllowed = _vanillaTransmit;
        Processor.Attach(voice);
    }
    public LocalVoice Voice { get; }
    public VoiceProcessor Processor { get; }
    public MixBus LocalBus { get; }
    public int SampleRate => Voice.Info.SamplingRate;
    public bool Closed { get; private set; }
    public bool Busy(ReactionId id) => _pending[(int)id] != null || Processor.Bus.IsActive(id) || LocalBus.IsActive(id);

    // Game events only enqueue. Fresh vanilla PTT state is applied before
    // starting the sound, so there is no reflective/reentrant PTT invocation.
    public bool RequestPlay(ReactionId id, float[] pcm, float gain)
    {
        if (Closed || Processor.Faulted || pcm.Length == 0 || Busy(id)) return false;
        _pending[(int)id] = pcm; _gains[(int)id] = gain;
        return true;
    }
    public void ApplyGate(bool microphoneRequested, bool allowed, bool networkReady = true, bool localMonitorReady = true)
    {
        if (Closed) return;
        _vanillaTransmit = microphoneRequested && allowed;
        Processor.MicrophoneAllowed = _vanillaTransmit;
        if (!allowed) ClearPlayback();
        else
        {
            if (!networkReady) Processor.Bus.Clear();
            for (int i = 0; i < _pending.Length; i++)
            {
                float[]? pcm = _pending[i];
                if (pcm == null) continue;
                if (networkReady) Processor.Bus.TryStart((ReactionId)i, pcm, _gains[i]);
                if (localMonitorReady) LocalBus.TryStart((ReactionId)i, pcm, _gains[i]);
                _pending[i] = null;
            }
        }
        bool effect = Processor.Bus.ActiveCount > 0;
        if (effect)
        {
            _detection.Enter(_control.VoiceDetection, _control.DspVoiceDetection);
            _control.VoiceDetection = false;
            _control.DspVoiceDetection = false;
        }
        else RestoreDetection();
        _control.TransmitEnabled = new TransmissionGate(microphoneRequested, allowed, effect).TransmitEnabled;
    }
    public void ClearPlayback()
    {
        Array.Clear(_pending, 0, _pending.Length);
        Processor.Bus.Clear(); LocalBus.Clear();
    }
    private void RestoreDetection()
    {
        if (!_detection.Release(out bool detection, out bool dsp)) return;
        _control.VoiceDetection = detection;
        _control.DspVoiceDetection = dsp;
    }
    public void Close(bool recreateRecording)
    {
        if (Closed) return;
        Closed = true;
        Processor.Stop(); // first: queued/in-flight frames can no longer gain mic access
        ClearPlayback();
        Voice.TransmitEnabled = false;
        try { RestoreDetection(); }
        finally
        {
            // Do not RemoveProcessor here. Photon retires it with the old
            // stream under its encoder lock, including frames past the outer
            // TransmitEnabled check. Manual shutdown recreates vanilla capture;
            // the Recorder.StopRecording hook already has retirement underway.
            if (recreateRecording) _control.RecreateRecording(_vanillaTransmit);
            else _control.TransmitEnabled = _vanillaTransmit;
        }
    }
}
