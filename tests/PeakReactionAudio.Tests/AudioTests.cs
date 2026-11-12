using NUnit.Framework;
using PeakReactionAudio.Core;

namespace PeakReactionAudio.Tests;

public sealed class AudioTests
{
    [Test]
    public void CacheBudgetRejectsOverflowWithoutConsumingCapacity()
    {
        var budget = new PcmBudget();
        Assert.That(budget.Reserve(Limits.CacheBytes - 4), Is.True);
        Assert.That(budget.Reserve(8), Is.False);
        Assert.That(budget.UsedBytes, Is.EqualTo(Limits.CacheBytes - 4));
        Assert.That(budget.Reserve(4), Is.True);
        Assert.That(budget.Reserve(1), Is.False);
    }
    [Test]
    public void DownmixSanitizesAndResamplingPreservesDcAndDuration()
    {
        Assert.That(Pcm.Downmix(new[] { 1f, -1f, float.NaN, 1f }, 2), Is.EqualTo(new[] { 0f, 0.5f }));
        float[] converted = Pcm.Resample(Enumerable.Repeat(0.25f, 4410).ToArray(), 44100, 24000);
        Assert.That(converted, Has.Length.EqualTo(2400));
        Assert.That(converted.All(x => Math.Abs(x - 0.25) < 1e-5), Is.True);
    }

    [Test]
    public void DownsamplingRejectsHighFrequencyAliasing()
    {
        var source = Enumerable.Range(0, 4800).Select(i => (float)Math.Sin(2 * Math.PI * 18000 * i / 48000)).ToArray();
        float[] downsampled = Pcm.Resample(source, 48000, 24000);
        double rms = Math.Sqrt(downsampled.Skip(100).Take(2000).Average(x => (double)x * x));
        Assert.That(rms, Is.LessThan(0.02));
    }

    [Test]
    public void AllSlotsOverlapWithoutSameEventReentryAndCursorsAreIndependent()
    {
        var bus = new MixBus(1000);
        foreach (ReactionId id in Enum.GetValues<ReactionId>())
            Assert.That(bus.TryStart(id, Enumerable.Repeat(0.1f, 100 + (int)id * 100).ToArray(), 1), Is.True);
        Assert.That(bus.ActiveCount, Is.EqualTo(7));
        Assert.That(bus.TryStart(ReactionId.Falling, new float[10], 1), Is.False);
        var output = new float[150]; bus.Render(output, 1, false, 0.5f);
        Assert.That(output[50], Is.EqualTo(0.7f * 0.5f / Math.Sqrt(7)).Within(1e-6));
        Assert.That(bus.ActiveCount, Is.EqualTo(6));
        Assert.That(bus.IsActive(ReactionId.PrePassOut), Is.False);
        Assert.That(bus.IsActive(ReactionId.RegainConsciousness), Is.True);
        Assert.That(bus.IsActive(ReactionId.Revived), Is.True);
        bus.Clear(); Assert.That(bus.ActiveCount, Is.Zero);
    }

    [Test]
    public void OutputIsLimitedWithoutWrappingAndMonoEffectsReachEveryChannel()
    {
        var bus = new MixBus(1000);
        bus.TryStart(ReactionId.Falling, Enumerable.Repeat(1f, 500).ToArray(), 2);
        var output = Enumerable.Repeat(0.9f, 400).ToArray();
        bus.Render(output, 2, true, 2);
        Assert.That(output.Max(), Is.EqualTo(0.98f).Within(1e-6));
        for (int i = 0; i < output.Length; i += 2) Assert.That(output[i], Is.EqualTo(output[i + 1]));
    }

    [Test]
    public void FloatAndShortProcessorsMaskMicrophoneEvenDuringTheEffectTail()
    {
        using var floats = new FloatVoiceProcessor(24000, 1, 480) { MicrophoneAllowed = false };
        using var shorts = new ShortVoiceProcessor(24000, 1, 480) { MicrophoneAllowed = false };
        floats.Bus.TryStart(ReactionId.Falling, new float[100], 1);
        shorts.Bus.TryStart(ReactionId.Falling, new float[100], 1);
        Assert.That(floats.Process(Enumerable.Repeat(0.7f, 480).ToArray()), Is.All.Zero);
        Assert.That(shorts.Process(Enumerable.Repeat((short)25000, 480).ToArray()), Is.All.Zero);
        Assert.That(floats.Process(Enumerable.Repeat(0.7f, 480).ToArray()), Is.All.Zero);
        Assert.That(shorts.Process(Enumerable.Repeat((short)25000, 480).ToArray()), Is.All.Zero);
    }

    [Test]
    public void RenderingDoesNotAllocatePerFrame()
    {
        var bus = new MixBus(24000);
        bus.TryStart(ReactionId.Falling, new float[240000], 1);
        var frame = new float[480];
        for (int i = 0; i < 10; i++) bus.Render(frame, 1, false, 0.5f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) bus.Render(frame, 1, false, 0.5f);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void IdleProcessorsPassMicrophoneThroughExactly()
    {
        using var floats = new FloatVoiceProcessor(24000, 1, 480) { MicrophoneAllowed = true };
        float[] input = { 0.12345678f, -0.98765f, 1f };
        float[] copy = (float[])input.Clone();
        Assert.That(floats.Process(input), Is.SameAs(input));
        Assert.That(input, Is.EqualTo(copy));
        using var processor = new ShortVoiceProcessor(24000, 1, 480) { MicrophoneAllowed = true };
        short[] samples = { short.MinValue, -100, -1, 0, 1, 100, short.MaxValue };
        Assert.That(processor.Process(samples), Is.SameAs(samples));
        Assert.That(samples, Is.EqualTo(new short[] { short.MinValue, -100, -1, 0, 1, 100, short.MaxValue }));
    }
}
