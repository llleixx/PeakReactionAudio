namespace PeakReactionAudio.Core;

internal sealed class PcmBudget
{
    public long UsedBytes { get; private set; }
    public bool CanFit(long bytes) => bytes > 0 && bytes <= Limits.CacheBytes - UsedBytes;
    public bool Reserve(long bytes)
    {
        if (!CanFit(bytes)) return false;
        UsedBytes += bytes;
        return true;
    }
}
