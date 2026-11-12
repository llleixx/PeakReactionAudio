using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using PeakReactionAudio.Core;
using UnityEngine;

namespace PeakReactionAudio;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "com.github.lllei.PeakReactionAudio";
    public const string Name = "PeakReactionAudio";
    public const string Version = "0.1.0";
    internal static Plugin? Instance { get; private set; }
    internal ModConfig Options { get; private set; } = null!;
    internal ReactionController Controller { get; private set; } = null!;
    internal VoiceRuntime? Voice { get; private set; }
    internal ManualLogSource Log => Logger;
    private AudioLibrary _library = null!;
    private PatchInstaller? _patches;
    private bool _running;

    private void Awake()
    {
        Instance = this;
        Options = new ModConfig(Config);
        _library = new AudioLibrary(Logger);
        Controller = new ReactionController(this);
        try { Voice = new VoiceRuntime(this, _library); }
        catch (Exception ex) { Logger.LogError($"Voice integration unavailable; reaction audio cannot start: {ex}"); }
        _patches = new PatchInstaller(this);
        _patches.Install();
        Logger.LogInfo($"{Name} {Version} loaded. {Limits.ReactionCount} reactions; all cooldowns default to 3 seconds.");
    }
    private void Start()
    {
        StartCoroutine(LoadAudio());
        StartCoroutine(_library.PrepareRequestedRates());
    }
    private IEnumerator LoadAudio()
    {
        IEnumerator loader = _library.Load(Path.GetDirectoryName(Info.Location)!);
        while (true)
        {
            object? current;
            try { if (!loader.MoveNext()) break; current = loader.Current; }
            catch (Exception ex) { Logger.LogError($"Audio loading stopped: {ex}"); break; }
            yield return current;
        }
        (loader as IDisposable)?.Dispose();
    }
    private void Update()
    {
        bool running = Options.Enabled.Value;
        Character? character = running ? Character.localCharacter : null;
        if (character != Controller.Character || running != _running)
        {
            Controller.Bind(character); _running = running;
        }
        Voice?.Update(character);
        if (!running || character == null) return;
        try { Controller.Tick(); }
        catch (Exception ex)
        {
            Controller.Disable(ReactionId.Falling);
            Logger.LogError($"Falling detection disabled: {ex}");
        }
    }
    private void OnDisable()
    { Voice?.Unbind(); Controller?.Bind(null); _running = false; }
    private void OnDestroy()
    {
        try { Voice?.Dispose(); }
        finally { _patches?.Dispose(); if (Instance == this) Instance = null; }
    }
}
