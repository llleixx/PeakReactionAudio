using System;

namespace PeakReactionAudio.Core;

internal readonly struct RecoverySnapshot
{
    private readonly bool _dead, _knockedOut;
    public RecoverySnapshot(bool dead, bool knockedOut) { _dead = dead; _knockedOut = knockedOut; }
    public ReactionId? Completed(bool conscious)
        => !conscious ? null : _dead ? ReactionId.Revived : _knockedOut ? ReactionId.RegainConsciousness : (ReactionId?)null;
}

internal sealed class PrePassOutEpisode
{
    private float _previous;
    private bool _consumed;
    public void Reset(float progress) { _previous = progress; _consumed = true; }
    public void Recovered() { _previous = 0; _consumed = false; }
    public bool Observe(float progress, bool atRisk, bool conscious)
    {
        if (progress <= 0 && conscious) _consumed = false;
        bool fire = !_consumed && conscious && atRisk && _previous < 0.5f && progress >= 0.5f;
        _previous = progress;
        if (fire) _consumed = true;
        return fire;
    }
}

internal sealed class ActivityEpisode
{
    public const float EndGap = 0.35f;
    private double _lastSeen = double.NegativeInfinity;
    private bool _active;
    public void Reset(double now, bool alreadyActive) { _active = alreadyActive; _lastSeen = alreadyActive ? now : double.NegativeInfinity; }
    public bool Seen(double now)
    {
        bool first = !_active || now - _lastSeen >= EndGap;
        _active = true;
        _lastSeen = now;
        return first;
    }
}

internal sealed class FallingEpisode
{
    public const float MinAirTime = 1.5f;
    public const float MinSlideSpeed = 1f;
    private bool _consumed;
    private double _descendingSince = double.NaN;
    public void Reset(bool alreadyFalling)
    { _consumed = alreadyFalling; _descendingSince = double.NaN; }
    public bool Observe(double now, bool eligible, bool stable, float airTime, float velocityY, bool exhaustedClimb = false)
    {
        if (stable) _consumed = false;
        bool descending = exhaustedClimb
            ? velocityY <= -MinSlideSpeed
            : airTime >= MinAirTime && velocityY <= -6f;
        if (!eligible || stable || !descending)
        { _descendingSince = double.NaN; return false; }
        if (double.IsNaN(_descendingSince)) _descendingSince = now;
        if (_consumed || now - _descendingSince < 0.2f) return false;
        _consumed = true;
        return true;
    }
}

internal static class LandingRule
{
    public static bool Matches(bool conscious, float airTime, float velocityY)
        => conscious && airTime >= 0.8f && velocityY <= -10f;
}
