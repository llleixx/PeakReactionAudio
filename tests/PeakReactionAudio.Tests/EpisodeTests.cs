using NUnit.Framework;
using PeakReactionAudio.Core;

namespace PeakReactionAudio.Tests;

public sealed class EpisodeTests
{
    [Test]
    public void AllEventsUseThreeSecondIndependentCooldowns()
    {
        Assert.That(Enum.GetValues<ReactionId>(), Has.Length.EqualTo(7));
        Assert.That(Limits.DefaultCooldown, Is.EqualTo(3));
        var cooldowns = new Cooldowns();
        foreach (ReactionId id in Enum.GetValues<ReactionId>())
        {
            Assert.That(cooldowns.Ready(id, 10, 3), Is.True);
            cooldowns.Accepted(id, 10);
        }
        foreach (ReactionId id in Enum.GetValues<ReactionId>())
        {
            Assert.That(cooldowns.Ready(id, 12.999, 3), Is.False);
            Assert.That(cooldowns.Ready(id, 13, 3), Is.True);
        }
        cooldowns.Accepted(ReactionId.Falling, 14);
        Assert.That(cooldowns.Ready(ReactionId.PullPlayer, 14, 3), Is.True);
        cooldowns.Reset();
        Assert.That(cooldowns.Ready(ReactionId.Falling, 0, 3), Is.True);
    }

    [TestCase(true, true, true, ReactionId.Revived)]
    [TestCase(true, false, true, ReactionId.Revived)]
    [TestCase(false, true, true, ReactionId.RegainConsciousness)]
    [TestCase(false, false, true, null)]
    [TestCase(true, true, false, null)]
    [TestCase(false, true, false, null)]
    public void RecoverySeparatesDeathFromKnockoutAndRequiresSuccessfulRecovery(bool dead, bool knockedOut, bool conscious, object? expected)
        => Assert.That(new RecoverySnapshot(dead, knockedOut).Completed(conscious), Is.EqualTo(expected));

    [Test]
    public void WarningTriggersAtHalfProgressAndRearmsOnlyAtZero()
    {
        var episode = new PrePassOutEpisode();
        episode.Reset(0);
        bool Observe(float progress, bool risk = true, bool awake = true)
            => episode.Observe(progress, risk, awake);
        Assert.That(Observe(0), Is.False);
        Assert.That(Observe(0.49f), Is.False);
        Assert.That(Observe(0.5f), Is.True);
        Observe(0.49f);
        Assert.That(Observe(0.51f), Is.False, "threshold jitter is not a new episode");
        Observe(0.01f);
        Assert.That(Observe(0.6f), Is.False, "progress must reach zero to rearm");
        Observe(0);
        Assert.That(Observe(0.5f), Is.True, "zero rearms immediately without a hold timer");
        Observe(0);
        Assert.That(Observe(0.5f, risk: false), Is.False, "stone or other progress is excluded");
    }

    [Test]
    public void WarningRebindDoesNotReplayOldDangerAndRecoveryRearms()
    {
        var episode = new PrePassOutEpisode(); episode.Reset(0.8f);
        Assert.That(episode.Observe(0.9f, true, true), Is.False);
        episode.Recovered();
        Assert.That(episode.Observe(0.5f, true, true), Is.True);
        episode.Recovered();
        Assert.That(episode.Observe(1, true, false), Is.False, "no late warning for instant pass-out");
        episode.Reset(0.4f);
        Assert.That(episode.Observe(0.6f, true, true), Is.False, "binding establishes a baseline even below the trigger");
    }

    [Test]
    public void HelpingHasImmediateFirstSignalAndToleratesShortGaps()
    {
        var outgoing = new ActivityEpisode(); var incoming = new ActivityEpisode();
        outgoing.Reset(0, false); incoming.Reset(0, false);
        Assert.That(outgoing.Seen(0), Is.True);
        Assert.That(outgoing.Seen(0.02), Is.False);
        Assert.That(outgoing.Seen(0.3), Is.False, "changing targets within a session does not retrigger");
        Assert.That(incoming.Seen(0.3), Is.True, "being helped has its own episode");
        Assert.That(outgoing.Seen(0.7), Is.True);
        outgoing.Reset(1, true);
        Assert.That(outgoing.Seen(1.01), Is.False, "binding during help is a baseline");
    }

    [Test]
    public void FallingExcludesShortJumpsAndRearmsOnLandingOrClimbing()
    {
        var fall = new FallingEpisode(); fall.Reset(false);
        bool Observe(double now, bool eligible, bool stable, float air, float speed)
            => fall.Observe(now, eligible, stable, air, speed);
        Assert.That(Observe(0, true, false, 0.3f, -10), Is.False);
        Assert.That(Observe(1, true, false, 0.7f, -8), Is.False);
        Assert.That(Observe(1.25, true, false, 1.49f, -9), Is.False, "airtime below 1.5 seconds is excluded even after sustained descent");
        Assert.That(Observe(1.3, true, false, 1.5f, -9), Is.False);
        Assert.That(Observe(1.4, true, false, 1.6f, -9), Is.False);
        Assert.That(Observe(1.55, true, false, 1.75f, -9), Is.True);
        Assert.That(Observe(5, true, false, 5, -15), Is.False, "one long fall does not retrigger after the 3s cooldown");
        Observe(6, false, true, 0, 0);
        Observe(6.1, true, false, 1.6f, -9);
        Assert.That(Observe(6.4, true, false, 1.9f, -9), Is.True, "landing rearms without an additional hold timer");
        Observe(7, false, true, 0, 0); // also represents a climbing frame
        Observe(8, true, false, 1.6f, -9);
        Assert.That(Observe(8.3, true, false, 1.9f, -9), Is.True);
        fall.Reset(false);
        Assert.That(Observe(20, false, false, 8, -20), Is.False, "carried/dead/ghost frames are ineligible");
        fall.Reset(true);
        Observe(21, true, false, 8, -20);
        Assert.That(Observe(22, true, false, 9, -20), Is.False, "binding in mid-fall establishes a baseline");
    }

    [Test]
    public void ExhaustedWallSlideNeedsDescentAndDoesNotRepeatWhenItBecomesFreeFall()
    {
        var fall = new FallingEpisode(); fall.Reset(false);
        bool Slide(double now, float speed) => fall.Observe(now, true, false, 0, speed, exhaustedClimb: true);
        Assert.That(Slide(0, 0), Is.False, "exhaustion alone is not a fall");
        Assert.That(Slide(0.5, -0.5f), Is.False);
        Assert.That(Slide(1, -2), Is.False);
        Assert.That(Slide(1.1, -2), Is.False, "the downward motion must last 0.2 seconds");
        Assert.That(Slide(1.25, -2), Is.True, "wall sliding does not require free-fall speed or airtime");
        Assert.That(Slide(5, -2), Is.False, "passing the cooldown does not start a new episode");
        Assert.That(fall.Observe(6, true, false, 2, -12), Is.False, "leaving the wall continues the same fall");
        Assert.That(Slide(7, -3), Is.False, "grabbing the wall while still exhausted must not rearm");
        fall.Observe(8, false, true, 0, 0);
        fall.Observe(9, true, false, 1.6f, -9);
        Assert.That(fall.Observe(9.25, true, false, 1.85f, -9), Is.True, "landing allows another fall");
    }

    [Test]
    public void WallSlideRebindAndTemporarySupportDoNotReplayButNormalClimbingRearms()
    {
        var fall = new FallingEpisode(); fall.Reset(true);
        bool Slide(double now, float speed) => fall.Observe(now, true, false, 0, speed, exhaustedClimb: true);
        Slide(0, -2);
        Assert.That(Slide(0.25, -2), Is.False, "binding during a slide establishes a baseline");
        Slide(1, 0);
        Slide(2, -2);
        Assert.That(Slide(2.25, -2), Is.False, "being briefly supported while exhausted is not a reset");
        Assert.That(fall.Observe(3, false, true, 0, -2), Is.False, "normal climbing is excluded and rearms");
        Assert.That(Slide(4, -2), Is.False);
        Assert.That(Slide(4.25, -2), Is.True);
    }

    [TestCase(true, 0.9f, -11f, true)]
    [TestCase(true, 0.2f, -11f, false)]
    [TestCase(true, 0.9f, -5f, false)]
    [TestCase(false, 0.9f, -11f, false)]
    public void LandingUsesPreImpactSnapshot(bool awake, float air, float velocity, bool expected)
        => Assert.That(LandingRule.Matches(awake, air, velocity), Is.EqualTo(expected));

    [Test]
    public void RandomChoiceNeverImmediatelyRepeatsWhenAlternativesExist()
    {
        var random = new Random(123); int previous = -1;
        for (int i = 0; i < 1000; i++)
        {
            int next = ClipChoice.Next(6, previous, random);
            Assert.That(next, Is.InRange(0, 5)); Assert.That(next, Is.Not.EqualTo(previous)); previous = next;
        }
        Assert.That(ClipChoice.Next(0, -1, random), Is.EqualTo(-1));
        Assert.That(ClipChoice.Next(1, 0, random), Is.Zero);
    }
}
