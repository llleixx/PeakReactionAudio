using System;
using System.Threading;
using PeakReactionAudio.Core;
using Photon.Voice;

namespace PeakReactionAudio;

internal abstract class VoiceProcessor : IDisposable
{
    private int _microphoneAllowed;
    private int _faulted;
    protected readonly float[] Work;
    protected readonly int Channels;
    private int _stopped;
    public VoiceProcessor(int rate, int channels, int frameSize)
    { Bus = new MixBus(rate); Channels = channels; Work = new float[frameSize]; }
    public MixBus Bus { get; }
    private int _mixedFrames, _micBlockedFrames;
    public int MixedFrames => Volatile.Read(ref _mixedFrames);
    public int MicBlockedFrames => Volatile.Read(ref _micBlockedFrames);
    protected void CountMixedFrame(bool microphoneAllowed)
    {
        Interlocked.Increment(ref _mixedFrames);
        if (!microphoneAllowed) Interlocked.Increment(ref _micBlockedFrames);
    }
    public float MasterGain { get; set; } = 0.5f;
    public bool MicrophoneAllowed { get => Volatile.Read(ref _microphoneAllowed) != 0; set => Volatile.Write(ref _microphoneAllowed, value ? 1 : 0); }
    public bool Faulted => Volatile.Read(ref _faulted) != 0;
    protected void Fault() => Volatile.Write(ref _faulted, 1);
    protected bool Stopped => Volatile.Read(ref _stopped) != 0;
    // A retired processor remains a terminal gate until Photon disposes its
    // stream. Never turn it into microphone passthrough during teardown.
    public void Stop() { Interlocked.Exchange(ref _stopped, 1); Bus.Clear(); }
    public void Dispose() => Stop();
    public abstract void Attach(LocalVoice voice);
}

internal sealed class FloatVoiceProcessor : VoiceProcessor, IProcessor<float>
{
    public FloatVoiceProcessor(int rate, int channels, int frameSize) : base(rate, channels, frameSize) { }
    public float[] Process(float[] buffer)
    {
        if (Stopped || Faulted) return null!;
        bool mic = MicrophoneAllowed;
        if (Bus.ActiveCount == 0) { if (!mic) Array.Clear(buffer, 0, buffer.Length); return buffer; }
        try
        {
            if (buffer.Length != Work.Length) throw new InvalidOperationException("Voice frame size changed without rebinding.");
            Array.Copy(buffer, Work, buffer.Length);
            Bus.Render(Work, Channels, mic, MasterGain);
            Array.Copy(Work, buffer, buffer.Length);
            CountMixedFrame(mic);
        }
        catch { Fault(); return null!; }
        return Stopped ? null! : buffer;
    }
    public override void Attach(LocalVoice voice) => ((LocalVoiceAudio<float>)voice).AddPostProcessor(this);
}

internal sealed class ShortVoiceProcessor : VoiceProcessor, IProcessor<short>
{
    public ShortVoiceProcessor(int rate, int channels, int frameSize) : base(rate, channels, frameSize) { }
    public short[] Process(short[] buffer)
    {
        if (Stopped || Faulted) return null!;
        bool mic = MicrophoneAllowed;
        if (Bus.ActiveCount == 0) { if (!mic) Array.Clear(buffer, 0, buffer.Length); return buffer; }
        try
        {
            if (buffer.Length != Work.Length) throw new InvalidOperationException("Voice frame size changed without rebinding.");
            for (int i = 0; i < buffer.Length; i++) Work[i] = buffer[i] / 32768f;
            Bus.Render(Work, Channels, mic, MasterGain);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (int)Math.Round(Work[i] * 32768f)));
            CountMixedFrame(mic);
        }
        catch { Fault(); return null!; }
        return Stopped ? null! : buffer;
    }
    public override void Attach(LocalVoice voice) => ((LocalVoiceAudio<short>)voice).AddPostProcessor(this);
}
