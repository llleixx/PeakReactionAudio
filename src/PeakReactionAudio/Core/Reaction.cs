using System;

namespace PeakReactionAudio.Core;

internal enum ReactionId { PrePassOut, PullPlayer, BeingPulled, Falling, HardLanding, RegainConsciousness, Revived }

internal static class Limits
{
    public const int ReactionCount = 7;
    public const float DefaultCooldown = 3f;
    public const long CacheBytes = 128L * 1024 * 1024;
    public const int MaxClipSeconds = 60;
    public static float Clamp(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
        return Math.Max(min, Math.Min(max, value));
    }
}

internal sealed class Cooldowns
{
    private readonly double[] _last = new double[Limits.ReactionCount];
    public Cooldowns() => Reset();
    public bool Ready(ReactionId id, double now, double seconds) => now - _last[(int)id] >= seconds;
    public void Accepted(ReactionId id, double now) => _last[(int)id] = now;
    public void Reset() { for (int i = 0; i < _last.Length; i++) _last[i] = double.NegativeInfinity; }
}

internal static class ClipChoice
{
    public static int Next(int count, int previous, Random random)
    {
        if (count <= 0) return -1;
        if (count == 1) return 0;
        if (previous < 0 || previous >= count) return random.Next(count);
        int index = random.Next(count - 1);
        return index >= previous ? index + 1 : index;
    }
}
