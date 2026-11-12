using PeakReactionAudio.Core;
using UnityEngine;

namespace PeakReactionAudio;

internal sealed class ReactionController
{
    private readonly Plugin _plugin;
    private readonly Cooldowns _cooldowns = new Cooldowns();
    private readonly PrePassOutEpisode _warning = new PrePassOutEpisode();
    private readonly ActivityEpisode _pulling = new ActivityEpisode();
    private readonly ActivityEpisode _pulled = new ActivityEpisode();
    private readonly FallingEpisode _fall = new FallingEpisode();
    private readonly bool[] _available = { true, true, true, true, true, true, true };
    public ReactionController(Plugin plugin) => _plugin = plugin;
    public Character? Character { get; private set; }
    private ModConfig Options => _plugin.Options;
    public void Bind(Character? character)
    {
        Character = character;
        double now = Time.unscaledTimeAsDouble;
        _cooldowns.Reset();
        _warning.Reset(character != null ? character.data.passOutValue : 0);
        bool recentPull = character != null && character.data.sinceGrabFriend < ActivityEpisode.EndGap;
        _pulling.Reset(now, recentPull); _pulled.Reset(now, recentPull);
        bool alreadySliding = character != null && character.data.isClimbing && character.data.outOfStaminaFor > 0 &&
            character.data.avarageVelocity.y <= -FallingEpisode.MinSlideSpeed;
        _fall.Reset(character != null && !character.data.isGrounded &&
            (alreadySliding || character.data.sinceGrounded >= FallingEpisode.MinAirTime));
    }
    public void Disable(params ReactionId[] ids) { foreach (ReactionId id in ids) _available[(int)id] = false; }
    public bool Available(ReactionId id) => _available[(int)id];
    private bool Local(Character character) => character != null && character == Character && character.IsLocal;
    private bool Conscious(Character character) => Local(character) && character.data.fullyConscious && !character.IsGhost;

    public void Tick()
    {
        if (!_available[(int)ReactionId.Falling]) return;
        Character? character = Character;
        if (character == null) return;
        // The game updates this timer using its own normal/bonus/infinite stamina rules.
        bool exhaustedClimb = character.data.isClimbing && character.data.outOfStaminaFor > 0;
        float velocityY = character.data.avarageVelocity.y;
        bool stable = character.data.isGrounded || (character.data.isClimbingAnything && !exhaustedClimb);
        bool eligible = Conscious(character) && !stable && !character.data.isCarried;
        if (_fall.Observe(Time.unscaledTimeAsDouble, eligible, stable, character.data.sinceGrounded,
            velocityY, exhaustedClimb))
        {
            Debug(ReactionId.Falling, $"detected {(exhaustedClimb ? "wall slide" : "free fall")}: " +
                $"air={character.data.sinceGrounded:F2}s, vy={velocityY:F2}, " +
                $"stamina={character.data.currentStamina:F3}+{character.data.extraStamina:F3}");
            Emit(ReactionId.Falling);
        }
    }
    public void LifeUpdated(Character character)
    {
        if (!Local(character)) return;
        bool risk = character.refs.afflictions.shouldPassOut && !character.data.shouldPetrify;
        if (_warning.Observe(character.data.passOutValue, risk, Conscious(character))) Emit(ReactionId.PrePassOut);
    }
    public void Helping(bool helping, bool beingHelped)
    {
        double now = Time.unscaledTimeAsDouble;
        if (helping && _pulling.Seen(now)) Emit(ReactionId.PullPlayer);
        if (beingHelped && _pulled.Seen(now)) Emit(ReactionId.BeingPulled);
    }
    public void Landed(Character character, float airTime, float velocityY, bool wasConscious)
    {
        if (!Local(character) || character.data.isCarried || character.IsGhost) return;
        if (LandingRule.Matches(wasConscious, airTime, velocityY)) Emit(ReactionId.HardLanding);
    }
    public void Recovered(Character character, RecoverySnapshot before)
    {
        ReactionId? id = before.Completed(Local(character) && character.data.fullyConscious);
        if (!id.HasValue) return;
        _warning.Recovered();
        Emit(id.Value);
    }
    private void Emit(ReactionId id)
    {
        Character? character = Character;
        if (character == null || !Local(character) || !character.data.fullyConscious ||
            (character.IsGhost && id != ReactionId.Revived) || !_available[(int)id] || !Options.Enabled.Value) return;
        EventOptions options = Options.Events[(int)id];
        if (!options.Enabled.Value) return;
        double now = Time.unscaledTimeAsDouble;
        if (!_cooldowns.Ready(id, now, ModConfig.Safe(Options.Cooldown, Limits.DefaultCooldown))) { Debug(id, "cooldown"); return; }
        if (_plugin.Voice == null) { Debug(id, "voice integration unavailable"); return; }
        if (!_plugin.Voice.TryPlay(id, ModConfig.Safe(options.Volume, 1), out AudioAsset? asset, out string reason))
        { Debug(id, reason); return; }
        _cooldowns.Accepted(id, now);
        Debug(id, "queued " + asset!.Path + (string.IsNullOrEmpty(reason) ? string.Empty : " (" + reason + ")"));
    }
    private void Debug(ReactionId id, string message)
    { if (Options.Debug.Value) _plugin.Log.LogInfo($"{id}: {message}"); }
}
