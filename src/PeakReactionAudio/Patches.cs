using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakReactionAudio.Core;
using Photon.Voice.Unity;

namespace PeakReactionAudio;

internal sealed class PatchInstaller : IDisposable
{
    private readonly Plugin _plugin;
    private readonly List<Harmony> _groups = new List<Harmony>();
    public PatchInstaller(Plugin plugin) => _plugin = plugin;
    public void Install()
    {
        Group("PrePassOut", new[] { ReactionId.PrePassOut }, h => Patch(h, typeof(Character), "HandleLife", post: nameof(Hooks.LifePostfix)));
        Group("Pull", new[] { ReactionId.PullPlayer, ReactionId.BeingPulled }, h =>
        {
            if (Hooks.GrabbingCharacter == null) throw new MissingFieldException("CharacterGrabbing.character");
            Patch(h, typeof(CharacterGrabbing), "Reach", pre: nameof(Hooks.ReachPrefix), post: nameof(Hooks.ReachPostfix), final: nameof(Hooks.ReachFinalizer));
            var target = AccessTools.Method(typeof(CharacterGrabbing), "TargetCanBeHelped", new[] { typeof(Character), typeof(Character) })
                ?? throw new MissingMethodException("TargetCanBeHelped(Character, Character)");
            h.Patch(target, postfix: Callback(nameof(Hooks.HelpedPostfix)));
        });
        Group("Landing", new[] { ReactionId.HardLanding }, h =>
        {
            if (Hooks.MovementCharacter == null) throw new MissingFieldException("CharacterMovement.character");
            Patch(h, typeof(CharacterMovement), "Land", pre: nameof(Hooks.LandPrefix), post: nameof(Hooks.LandPostfix));
        });
        Group("Recovery", new[] { ReactionId.RegainConsciousness, ReactionId.Revived }, h =>
        {
            Patch(h, typeof(Character), "UnPassOutDone", pre: nameof(Hooks.RecoverPrefix), post: nameof(Hooks.RecoverPostfix));
            Patch(h, typeof(Character), "ReviveCharacter", pre: nameof(Hooks.RecoverPrefix), post: nameof(Hooks.RecoverPostfix));
        });
        Group("Voice", Array.Empty<ReactionId>(), h =>
        {
            Patch(h, typeof(Recorder), "StopRecording", pre: nameof(Hooks.VoiceStopping));
            Patch(h, typeof(CharacterVoiceHandler), "PushToTalk", post: nameof(Hooks.PushToTalkPostfix));
        }, voice: true);
        // Optional compatibility must never roll back the basic PTT/retirement hooks.
        Group("WebRTC", Array.Empty<ReactionId>(), h => Patch(h, typeof(WebRtcAudioDsp), "StartProc", post: nameof(Hooks.DspStarted)), optional: true);
    }
    private void Group(string name, ReactionId[] affected, Action<Harmony> install, bool voice = false, bool optional = false)
    {
        var harmony = new Harmony(Plugin.Guid + "." + name);
        try { install(harmony); _groups.Add(harmony); }
        catch (Exception ex)
        {
            harmony.UnpatchSelf(); _plugin.Controller.Disable(affected);
            if (optional) _plugin.Log.LogWarning($"Optional {name} hook unavailable: {ex.Message}");
            else _plugin.Log.LogError($"{name} hooks unavailable: {ex}");
            if (voice) _plugin.Voice?.Fail(ex);
        }
    }
    private static HarmonyMethod Callback(string name) => new HarmonyMethod(AccessTools.Method(typeof(Hooks), name));
    private static void Patch(Harmony h, Type type, string name, string? pre = null, string? post = null, string? final = null)
    {
        var original = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        h.Patch(original, prefix: pre == null ? null : Callback(pre), postfix: post == null ? null : Callback(post),
            finalizer: final == null ? null : Callback(final));
    }
    public void Dispose() { foreach (Harmony h in _groups) h.UnpatchSelf(); _groups.Clear(); }
}

internal static class Hooks
{
    internal static readonly FieldInfo? GrabbingCharacter = AccessTools.Field(typeof(CharacterGrabbing), "character");
    internal static readonly FieldInfo? MovementCharacter = AccessTools.Field(typeof(CharacterMovement), "character");
    private static Character? _helper;
    private static bool _helping, _beingHelped;
    private static Plugin? Active => Plugin.Instance != null && Plugin.Instance.isActiveAndEnabled ? Plugin.Instance : null;
    private static void Event(ReactionId id, Action<ReactionController> action, ReactionId? also = null)
    {
        Plugin? plugin = Active;
        if (plugin == null || (!plugin.Controller.Available(id) && (!also.HasValue || !plugin.Controller.Available(also.Value)))) return;
        try { action(plugin.Controller); }
        catch (Exception ex)
        {
            plugin.Controller.Disable(id);
            if (also.HasValue) plugin.Controller.Disable(also.Value);
            plugin.Log.LogError($"{id} detection disabled: {ex}");
        }
    }
    private static void Voice(Action<VoiceRuntime> action)
    {
        VoiceRuntime? voice = Active?.Voice;
        if (voice == null) return;
        try { action(voice); }
        catch (Exception ex) { voice.Fail(ex); }
    }
    public static void LifePostfix(Character __instance) => Event(ReactionId.PrePassOut, c => c.LifeUpdated(__instance));

    // Reach runs on Unity's main physics thread. Only local participation matters;
    // no relationship collection, object pool or speculative reentrancy support.
    public static void ReachPrefix(CharacterGrabbing __instance)
        => Event(ReactionId.PullPlayer, _ =>
        {
            _helper = GrabbingCharacter!.GetValue(__instance) as Character;
            _helping = _beingHelped = false;
        }, ReactionId.BeingPulled);
    public static void HelpedPostfix(Character target, Character helper, bool __result)
    {
        if (!__result || _helper == null || helper != _helper || helper.data.isScoutmaster) return;
        _helping |= helper.IsLocal; _beingHelped |= target.IsLocal;
    }
    public static void ReachPostfix() => Event(ReactionId.PullPlayer, c => c.Helping(_helping, _beingHelped), ReactionId.BeingPulled);
    public static Exception? ReachFinalizer(Exception? __exception)
    { _helper = null; _helping = _beingHelped = false; return __exception; }

    internal readonly struct LandingSnapshot
    {
        public LandingSnapshot(Character? character)
        {
            Character = character; AirTime = character != null ? character.data.sinceGrounded : 0;
            Velocity = character != null ? character.data.avarageLastFrameVelocity.y : 0;
            Conscious = character != null && character.data.fullyConscious;
        }
        public Character? Character { get; }
        public float AirTime { get; }
        public float Velocity { get; }
        public bool Conscious { get; }
    }
    public static void LandPrefix(CharacterMovement __instance, out LandingSnapshot __state)
        => __state = new LandingSnapshot(MovementCharacter?.GetValue(__instance) as Character);
    public static void LandPostfix(LandingSnapshot __state)
    {
        if (__state.Character != null) Event(ReactionId.HardLanding,
            c => c.Landed(__state.Character, __state.AirTime, __state.Velocity, __state.Conscious));
    }
    public static void RecoverPrefix(Character __instance, out RecoverySnapshot __state)
        => __state = new RecoverySnapshot(__instance.data.dead, __instance.data.passedOut || __instance.data.fullyPassedOut);
    public static void RecoverPostfix(Character __instance, RecoverySnapshot __state)
        => Event(ReactionId.RegainConsciousness, c => c.Recovered(__instance, __state), ReactionId.Revived);
    public static void VoiceStopping(Recorder __instance) => Voice(v => v.RecorderStopping(__instance));
    public static void PushToTalkPostfix(CharacterVoiceHandler __instance) => Voice(v => v.OnPushToTalk(__instance, __instance.transmitting));
    public static void DspStarted(WebRtcAudioDsp __instance) => Voice(v => v.DspChanged(__instance));
}
