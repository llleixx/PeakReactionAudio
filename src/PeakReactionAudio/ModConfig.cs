using System;
using BepInEx.Configuration;
using PeakReactionAudio.Core;

namespace PeakReactionAudio;

internal sealed class EventOptions
{
    public EventOptions(ConfigFile config, ReactionId id)
    {
        string section = "Event." + id;
        Enabled = config.Bind(section, "Enabled", true, "Enable this event. Restart after editing the config file.");
        Volume = config.Bind(section, "Volume", 1f, new ConfigDescription("Event gain.", new AcceptableValueRange<float>(0, 2)));
    }
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<float> Volume { get; }
}

internal sealed class ModConfig
{
    public ModConfig(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Enable reactions. Restart after editing config files.");
        MasterVolume = config.Bind("General", "MasterVolume", 0.5f,
            new ConfigDescription("Gain of the effects bus; microphone gain is unchanged.", new AcceptableValueRange<float>(0, 2)));
        Cooldown = config.Bind("General", "CooldownSeconds", Limits.DefaultCooldown,
            new ConfigDescription("Cooldown duration for all events. Each event counts separately.", new AcceptableValueRange<float>(0, 300)));
        Debug = config.Bind("General", "DebugLogging", false, "Log event IDs and skip reasons; never microphone samples.");
        Events = new EventOptions[Limits.ReactionCount];
        foreach (ReactionId id in Enum.GetValues(typeof(ReactionId))) Events[(int)id] = new EventOptions(config, id);
    }
    public static float Safe(ConfigEntry<float> entry, float fallback) => Limits.Clamp(entry.Value, 0, 300, fallback);
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<float> MasterVolume { get; }
    public ConfigEntry<float> Cooldown { get; }
    public ConfigEntry<bool> Debug { get; }
    public EventOptions[] Events { get; }
}
