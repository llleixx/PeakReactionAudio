using System.Collections.Concurrent;
using NUnit.Framework;
using PeakReactionAudio.Core;
using Photon.Voice;

namespace PeakReactionAudio.Tests;

public sealed class VoiceSessionTests
{
    [Test]
    public void SoloPlaybackStaysLocalAcrossNetworkJoinAndClearsOnClose()
    {
        using var fixture = new VoiceFixture<float>();
        fixture.Control.VoiceDetection = true;
        var session = fixture.Session();
        session.RequestPlay(ReactionId.PrePassOut, Enumerable.Repeat(0.1f, 1000).ToArray(), 1);
        session.ApplyGate(false, true, networkReady: false);
        Assert.That(session.Processor.Bus.ActiveCount, Is.Zero);
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(1));
        Assert.That(fixture.Voice.TransmitEnabled, Is.False);
        Assert.That(fixture.Control.VoiceDetection, Is.True, "local playback must not override network VAD");
        var local = new float[480]; session.LocalBus.Render(local, 1, false, 0.5f);
        Assert.That(local[250], Is.EqualTo(0.05f).Within(1e-6));
        fixture.Voice.PushData(Enumerable.Repeat(0.8f, 480).ToArray());
        Assert.That(fixture.Encoder.Frames, Is.Empty);
        session.ApplyGate(false, true, true);
        Assert.That(session.Processor.Bus.ActiveCount, Is.Zero);
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(1));
        Assert.That(fixture.Voice.TransmitEnabled, Is.False);
        session.Close(true);
        Assert.That(session.LocalBus.ActiveCount, Is.Zero);
    }

    [Test]
    public void NetworkLossStopsSendingButKeepsLocalPlaybackAndVanillaPtt()
    {
        using var fixture = new VoiceFixture<float>();
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, new float[1000], 1);
        session.ApplyGate(false, true, true);
        Assert.That(fixture.Voice.TransmitEnabled, Is.True);
        session.ApplyGate(false, true, false);
        Assert.That(fixture.Voice.TransmitEnabled, Is.False);
        Assert.That(session.Processor.Bus.ActiveCount, Is.Zero);
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(1));
        session.ApplyGate(true, true, false);
        session.Close(true);
        Assert.That(fixture.Control.RestoredTransmit, Is.True, "keep the original microphone request across reconnect");
    }

    [Test]
    public void PlaybackWaitsForFreshPttStateAndUsesOnePcmForBothCursors()
    {
        using var fixture = new VoiceFixture<float>();
        var session = fixture.Session();
        float[] pcm = Enumerable.Repeat(0.1f, 1000).ToArray();
        Assert.That(session.RequestPlay(ReactionId.Falling, pcm, 1), Is.True);
        Assert.That(session.Busy(ReactionId.Falling), Is.True);
        Assert.That(session.RequestPlay(ReactionId.Falling, pcm, 1), Is.False);
        Assert.That(fixture.Voice.TransmitEnabled, Is.False, "an event must not force open stale PTT state");
        session.ApplyGate(false, true);
        fixture.Voice.PushData(Enumerable.Repeat(0.7f, 480).ToArray());
        Assert.That(fixture.Encoder.Frames.Single()[250], Is.EqualTo(0.05f).Within(1e-6));
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(1));
        session.Processor.Bus.Render(new float[520], 1, false, 0.5f);
        Assert.That(session.Processor.Bus.ActiveCount, Is.Zero);
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(1), "monitoring owns a separate cursor, not another PCM format");
        session.Close(true);
    }

    [Test]
    public void PttSwitchesDuringPlaybackDoNotRestartTheStream()
    {
        using var fixture = new VoiceFixture<float>();
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, Enumerable.Repeat(0.1f, 4000).ToArray(), 1);
        session.ApplyGate(true, true);
        fixture.Voice.PushData(Enumerable.Repeat(0.2f, 480).ToArray());
        session.ApplyGate(false, true);
        fixture.Voice.PushData(Enumerable.Repeat(0.2f, 480).ToArray());
        session.ApplyGate(true, true);
        fixture.Voice.PushData(Enumerable.Repeat(0.2f, 480).ToArray());
        float[][] frames = fixture.Encoder.Frames.ToArray();
        Assert.That(frames[0][250], Is.EqualTo(0.25f).Within(1e-6));
        Assert.That(frames[1][250], Is.EqualTo(0.05f).Within(1e-6));
        Assert.That(frames[2][250], Is.EqualTo(0.25f).Within(1e-6));
        Assert.That(fixture.Control.Recreates, Is.Zero);
        session.Close(true);
    }

    [Test]
    public void EndOfSoundRestoresDetectionAndOriginalTransmitState()
    {
        using var fixture = new VoiceFixture<float>();
        fixture.Control.VoiceDetection = true; fixture.Control.DspVoiceDetection = true;
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, Enumerable.Repeat(0.1f, 480).ToArray(), 1);
        session.ApplyGate(false, true);
        session.ApplyGate(false, true); // repeated updates must preserve the original VAD settings
        Assert.That(fixture.Control.VoiceDetection || fixture.Control.DspVoiceDetection, Is.False);
        fixture.Voice.PushData(new float[480]); // silent mic would be discarded if VAD stayed enabled
        session.ApplyGate(false, true);
        Assert.That(fixture.Control.VoiceDetection && fixture.Control.DspVoiceDetection, Is.True);
        Assert.That(fixture.Voice.TransmitEnabled, Is.False);
        Assert.That(fixture.Encoder.Frames.Single().Any(x => x > 0), Is.True);
        session.Close(true);
    }

    [Test]
    public void ShortVoicePipelineLimitsMixedAudioWithoutWrapping()
    {
        using var fixture = new VoiceFixture<short>();
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, Enumerable.Repeat(1f, 1000).ToArray(), 2);
        session.ApplyGate(true, true);
        fixture.Voice.PushData(Enumerable.Repeat((short)30000, 480).ToArray());
        Assert.That(fixture.Encoder.Frames.Single().All(x => x > 0 && x <= 32113), Is.True);
        session.Close(true);
    }

    [Test]
    public void PermissionLossClearsPendingAndActiveSounds()
    {
        using var fixture = new VoiceFixture<float>();
        fixture.Control.VoiceDetection = true;
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, new float[4000], 1);
        session.ApplyGate(false, true);
        session.RequestPlay(ReactionId.PullPlayer, new float[4000], 1);
        session.ApplyGate(true, false);
        Assert.That(session.Busy(ReactionId.Falling) || session.Busy(ReactionId.PullPlayer), Is.False);
        Assert.That(fixture.Voice.TransmitEnabled, Is.False);
        Assert.That(fixture.Control.VoiceDetection, Is.True);
        session.Close(true);
        Assert.That(fixture.Control.RestoredTransmit, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosingWaitsForInflightAndQueuedFramesWithoutOpeningMicrophone(bool useShort)
    {
        if (useShort) CheckConcurrentClose<short>((short)25000);
        else CheckConcurrentClose<float>(0.8f);
    }

    private static void CheckConcurrentClose<T>(T microphone)
    {
        // Real Photon worker thread. Hold its first frame after it has passed
        // the outer TransmitEnabled check, but before the production processor.
        using var fixture = new VoiceFixture<T>(threaded: true);
        using var blocker = new BlockingProcessor<T>();
        fixture.Voice.AddPreProcessor(blocker);
        var session = fixture.Session();
        fixture.Control.VoiceDetection = true;
        session.RequestPlay(ReactionId.Falling, Enumerable.Repeat(0.1f, 4000).ToArray(), 1);
        session.ApplyGate(false, true);
        Task? close = null;
        try
        {
            fixture.Enqueue(microphone);
            Assert.That(blocker.Entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            for (int i = 0; i < 5; i++) fixture.Enqueue(microphone);
            close = Task.Run(() => session.Close(true));
            Assert.That(fixture.Control.ResetEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(session.Closed, Is.True);
            Assert.That(close.IsCompleted, Is.False, "native retirement must wait for the encoder lock");
        }
        finally { blocker.Release.Set(); }
        Assert.That(close!.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(fixture.Encoder.Frames, Is.Empty, "neither inflight nor queued frames may lose their microphone gate");
        Assert.That(fixture.Control.Recreates, Is.EqualTo(1));
        Assert.That(fixture.Control.RestoredTransmit, Is.False);
        Assert.That(fixture.Control.VoiceDetection, Is.True);
        Assert.That(session.Processor.MicrophoneAllowed, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeRecorderStopLeavesTerminalGateUntilPhotonDisposesTheVoice(bool microphoneRequested)
    {
        using var fixture = new VoiceFixture<float>();
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, new float[1000], 1);
        session.ApplyGate(microphoneRequested, true);
        session.Close(false); // called by Recorder.StopRecording prefix
        fixture.Voice.TransmitEnabled = true; // queued/stale enable must not bypass the retained processor
        fixture.Voice.PushData(Enumerable.Repeat(0.8f, 480).ToArray());
        Assert.That(fixture.Encoder.Frames, Is.Empty);
        Assert.That(fixture.Control.Recreates, Is.Zero);
        fixture.Retire(); // original Recorder.StopRecording then retires its own stream
        session.Close(true);
        Assert.That(fixture.Control.Recreates, Is.Zero, "close is idempotent");
    }

    [Test]
    public void ClosedSessionDropsPendingEventsAndCannotChangeReplacementSession()
    {
        using var first = new VoiceFixture<float>();
        var old = first.Session(); old.RequestPlay(ReactionId.Falling, new float[1000], 1);
        old.Close(true);
        Assert.That(old.Busy(ReactionId.Falling), Is.False);
        using var replacement = new VoiceFixture<float>();
        var fresh = replacement.Session(); fresh.ApplyGate(false, true);
        Assert.That(fresh.Busy(ReactionId.Falling), Is.False);
        Assert.That(replacement.Voice.TransmitEnabled, Is.False);
        fresh.RequestPlay(ReactionId.PrePassOut, new float[2000], 1);
        fresh.ApplyGate(true, true);
        old.ApplyGate(false, false); old.Close(true);
        Assert.That(replacement.Voice.TransmitEnabled, Is.True);
        Assert.That(fresh.Closed, Is.False);
        Assert.That(old.RequestPlay(ReactionId.Falling, new float[100], 1), Is.False);
        Assert.That(first.Control.Recreates, Is.EqualTo(1));
        fresh.Close(true);
        Assert.That(replacement.Control.RestoredTransmit, Is.True);
    }

    [Test]
    public void AllPendingEventsStartTogetherAndDoNotRepeatOnNextPttUpdate()
    {
        using var fixture = new VoiceFixture<float>();
        var session = fixture.Session();
        float[] pcm = new float[1000];
        foreach (ReactionId id in Enum.GetValues<ReactionId>()) Assert.That(session.RequestPlay(id, pcm, 1), Is.True);
        Assert.That(session.Processor.Bus.ActiveCount, Is.Zero);
        session.ApplyGate(false, true); session.ApplyGate(false, true);
        Assert.That(session.Processor.Bus.ActiveCount, Is.EqualTo(7));
        Assert.That(session.LocalBus.ActiveCount, Is.EqualTo(7));
        session.Close(true);
        Assert.That(session.LocalBus.ActiveCount, Is.Zero);
    }

    [Test]
    public void DetectionRestoreErrorStillRetiresTheOldRecording()
    {
        using var fixture = new VoiceFixture<float>();
        fixture.Control.VoiceDetection = true;
        var session = fixture.Session();
        session.RequestPlay(ReactionId.Falling, new float[1000], 1);
        session.ApplyGate(false, true);
        fixture.Control.ThrowOnDetectionRestore = true;
        Assert.Throws<InvalidOperationException>(() => session.Close(true));
        Assert.That(fixture.Control.Recreates, Is.EqualTo(1));
        fixture.Voice.TransmitEnabled = true;
        fixture.Voice.PushData(Enumerable.Repeat(0.8f, 480).ToArray());
        Assert.That(fixture.Encoder.Frames, Is.Empty);
    }
}

internal sealed class VoiceFixture<T> : IDisposable
{
    private readonly VoiceClient _client;
    private int _retired;
    public VoiceFixture(bool threaded = false)
    {
        _client = new VoiceClient(new TestTransport(), new TestLogger()) { ThreadingEnabled = threaded };
        Encoder = new TestEncoder<T>();
        Voice = _client.CreateLocalVoiceAudio<T>(VoiceInfo.CreateAudio(Codec.AudioOpus, 24000, 1, 20000),
            new TestDescription(), 1, new VoiceCreateOptions { Encoder = Encoder });
        Voice.VoiceDetector.On = false; Voice.TransmitEnabled = false;
        Control = new TestVoiceControl(Voice, Retire);
    }
    public LocalVoiceAudio<T> Voice { get; }
    public TestEncoder<T> Encoder { get; }
    public TestVoiceControl Control { get; }
    public VoiceSession Session() => new VoiceSession(Voice, Control, false, true);
    public void Enqueue(T microphone)
    {
        T[] buffer = Voice.BufferFactory.New(480); Array.Fill(buffer, microphone); Voice.PushDataAsync(buffer);
    }
    public void Retire() { if (Interlocked.Exchange(ref _retired, 1) == 0) Voice.RemoveSelf(); }
    public void Dispose() { Retire(); _client.Dispose(); }
    private sealed class TestDescription : IAudioDesc
    { public int SamplingRate => 24000; public int Channels => 1; public string Error => null!; public void Dispose() { } }
    private sealed class TestLogger : ILogger
    { public LogLevel Level => LogLevel.Error; public void Log(LogLevel level, string fmt, params object[] args) { } }
    private sealed class TestTransport : IVoiceTransport
    {
        public bool IsChannelJoined(int channelId) => false;
        public void SendVoiceInfo(LocalVoice v, int c, bool me, int[] targets) { }
        public void SendVoiceRemove(LocalVoice v, int c, bool me, int[] targets) { }
        public void SendFrame(ArraySegment<byte> data, FrameFlags flags, byte ev, byte frame, byte v, int c, SendFrameParams p)
            => throw new InvalidOperationException("Tests must not use networking.");
        public string ChannelIdStr(int id) => id.ToString();
        public string PlayerIdStr(int id) => id.ToString();
        public int GetPayloadFragmentSize(SendFrameParams p) => 1200;
    }
}

internal sealed class TestVoiceControl(LocalVoice voice, Action retire) : IVoiceControl
{
    public readonly ManualResetEventSlim ResetEntered = new();
    public int Recreates { get; private set; }
    public bool RestoredTransmit { get; private set; }
    public bool ThrowOnDetectionRestore { get; set; }
    public bool TransmitEnabled { set => voice.TransmitEnabled = value; }
    public bool VoiceDetection
    {
        get => ((ILocalVoiceAudio)voice).VoiceDetector.On;
        set
        {
            if (value && ThrowOnDetectionRestore) throw new InvalidOperationException("simulated native settings error");
            ((ILocalVoiceAudio)voice).VoiceDetector.On = value;
        }
    }
    public bool DspVoiceDetection { get; set; }
    public void RecreateRecording(bool vanillaTransmit)
    {
        ResetEntered.Set(); retire(); RestoredTransmit = vanillaTransmit; Recreates++;
        TransmitEnabled = vanillaTransmit;
    }
}

internal sealed class TestEncoder<T> : IEncoderDirect<T[]>
{
    public ConcurrentQueue<T[]> Frames { get; } = new();
    public string Error => null!;
    public Action<ArraySegment<byte>, FrameFlags> Output { private get; set; } = null!;
    public void Input(T[] buffer) => Frames.Enqueue((T[])buffer.Clone());
    public ArraySegment<byte> DequeueOutput(out FrameFlags flags) { flags = default; return default; }
    public void EndOfStream() { }
    public I GetPlatformAPI<I>() where I : class => null!;
    public void Dispose() { }
}

internal sealed class BlockingProcessor<T> : IProcessor<T>
{
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public T[] Process(T[] buffer)
    {
        Entered.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("controlled audio interleaving was not released");
        return buffer;
    }
    public void Dispose() { } // handles stay valid until all controlled threads have exited
}
