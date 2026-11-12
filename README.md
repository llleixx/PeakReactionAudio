# PeakReactionAudio

[![GitHub Repo](https://img.shields.io/badge/GitHub-llleixx%2FPeakReactionAudio-black?logo=github)](https://github.com/llleixx/PeakReactionAudio)

[English](#english) | [中文](#中文)

## English

PeakReactionAudio plays custom reaction sounds for player events in PEAK and mixes them into the game's voice chat. Only the player using the mod needs to install it; teammates receive the sounds through normal voice chat.

### Features

- **Event reactions:** Play sounds before passing out, while helping or being helped, during falls, after hard landings, when waking up, and when revived after death.
- **Custom sound folders:** Add WAV, Ogg Vorbis, or MP3 files for each event. Multiple files are selected randomly without immediate repeats.
- **Voice mixing:** Keep talking while sounds play. In push-to-talk mode, sounds can play without holding the voice key, with microphone audio blocked while the key is released.

### Installation

Install with Thunderstore Mod Manager or r2modman, or copy the complete `plugins/PeakReactionAudio` folder into PEAK's `BepInEx/plugins` directory. PeakReactionAudio requires BepInEx 5.

Keep the event folders directly beside the DLL:

```text
BepInEx/plugins/PeakReactionAudio/
  PeakReactionAudio.dll
  PrePassOut/
  PullPlayer/
  BeingPulled/
  Falling/
  HardLanding/
  RegainConsciousness/
  Revived/
```

### Reaction sounds

| Folder | Trigger | Included sound |
|---|---|---|
| `PrePassOut` | Ordinary pass-out progress crosses 50% while still conscious. | Orga: “Don't stop…” / “Good night, Makka Pakka” (random selection) |
| `PullPlayer` | You begin helping another player with the normal reaching action. | Empty |
| `BeingPulled` | Another player begins helping you. | Empty |
| `Falling` | A sustained downward fall. | Tom's scream |
| `HardLanding` | Landing after sufficient airtime and downward speed. | Empty |
| `RegainConsciousness` | Waking up from unconsciousness. | Empty |
| `Revived` | Being revived after death. | Empty |

Place audio files directly inside the corresponding folder. Empty folders stay silent. Restart the game after changing audio files; each clip can be up to 60 seconds long.

### Configuration

The config file is generated at `BepInEx/config/com.github.lllei.PeakReactionAudio.cfg`. Restart the game after editing the file. An example is included as `config.example.cfg`.

| Key | Default | Description |
|---|---|---|
| `General.Enabled` | `true` | Enables reaction sounds. |
| `General.MasterVolume` | `0.5` | Effect volume for both local playback and voice chat (`0` to `2`). |
| `General.CooldownSeconds` | `3` | Cooldown duration for all events (`0` to `300` seconds); each event counts separately. |
| `General.DebugLogging` | `false` | Logs event triggers, skip reasons, and sender counters. |
| `Event.<Folder>.Enabled` | `true` | Enables this event. |
| `Event.<Folder>.Volume` | `1` | Additional volume multiplier for this event (`0` to `2`). |

Replace `<Folder>` with an event folder name, such as `Event.PrePassOut.Volume`.

### Voice behavior

Reaction sounds follow the game's voice volume, distance, mute, and communication rules. Sounds may fade or be cut off after you become fully unconscious. A working microphone and initialized game voice stream are required.

Loud sounds share the voice channel with your speech and can reduce its clarity. Lower `General.MasterVolume` or the individual event's volume if needed.

### Building

Set `PEAK_GAME_DIR` to a PEAK installation with BepInEx, or copy `PeakGameDir.props.example` to the gitignored `PeakGameDir.props` and set the game directory. Tests require .NET 10 SDK.

```powershell
dotnet build src/PeakReactionAudio/PeakReactionAudio.csproj -c Release
dotnet test tests/PeakReactionAudio.Tests/PeakReactionAudio.Tests.csproj -c Release
powershell -NoProfile -File build/Package.ps1
```

Normal builds do not write to the game directory. Packaging creates `artifacts/lllei-PeakReactionAudio-<version>.zip` with the DLL.

## 中文

PeakReactionAudio 会在 PEAK 的玩家事件发生时播放自定义音频，并混入游戏原版语音。只需使用者安装，队友通过正常的游戏语音接收。

### 功能

- **事件音频：** 支持昏迷预警、拉人、被拉、坠落、重落地、醒来和死亡后被复活。
- **自定义素材：** 每个事件有独立文件夹，支持 WAV、Ogg Vorbis 和 MP3。放入多个文件时随机选择，避免紧接着重复同一段。
- **人声混音：** 说话时可同时播放音频；按键说话模式下，不按语音键也能发送音频，此时屏蔽麦克风部分。

### 安装

通过 Thunderstore Mod Manager 或 r2modman 安装，也可以将发布包中完整的 `plugins/PeakReactionAudio` 文件夹复制到游戏的 `BepInEx/plugins` 下。需要 BepInEx 5。

七个事件文件夹直接放在 DLL 旁边，目录结构见英文安装部分。

### 事件与音频

| 文件夹 | 触发时机 | 包内音频 |
|---|---|---|
| `PrePassOut` | 清醒时，普通昏迷进度向上越过 50% | 奥尔加：“所以，不要停下来啊……”／“晚安，玛卡巴卡”（随机播放） |
| `PullPlayer` | 使用普通伸手动作开始帮助队友。 | 留空 |
| `BeingPulled` | 开始接受其他玩家的伸手帮助。 | 留空 |
| `Falling` | 持续向下坠落。 | 汤姆惨叫 |
| `HardLanding` | 滞空时间和下降速度满足条件后的重落地。 | 留空 |
| `RegainConsciousness` | 从普通昏迷恢复清醒。 | 留空 |
| `Revived` | 死亡后被复活。 | 留空 |

将音频直接放进对应文件夹，空文件夹不会播放。每段音频最长 60 秒，修改素材后需要重启游戏。

### 配置

配置文件生成于 `BepInEx/config/com.github.lllei.PeakReactionAudio.cfg`，修改文件后重启游戏生效。

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `General.Enabled` | `true` | 总开关。 |
| `General.MasterVolume` | `0.5` | 本地与发送端共用的音频总音量，范围 `0` 到 `2`。 |
| `General.CooldownSeconds` | `3` | 所有事件使用的冷却时长，范围 `0` 到 `300` 秒；各事件分别计时。 |
| `General.DebugLogging` | `false` | 记录事件触发、跳过原因和发送计数。 |
| `Event.<文件夹>.Enabled` | `true` | 该事件的开关。 |
| `Event.<文件夹>.Volume` | `1` | 该事件的额外音量倍率，范围 `0` 到 `2`。 |

例如，`Event.PrePassOut.Volume` 用于调整昏迷预警音量。

### 语音说明

音频遵循原版语音的音量、距离、静音和通信规则；完全昏迷后可能衰减或中断。需要可用的麦克风和已初始化的游戏语音流。

音频与说话共用语音通道，过响的素材可能影响人声清晰度，可以调低总音量或对应事件音量。

构建与打包命令见英文部分。
