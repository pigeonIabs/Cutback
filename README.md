# Cutback

An instant replay mod for Beat Saber that shows how your last attempt ended.

When you fail, Cutback opens your recent recording a few seconds before death and slows playback exponentially as it approaches the final moment. Your last attempt is available immediately from the local recording, regardless of score or leaderboard eligibility.

## Features

- Automatic death review starting three song seconds before failure
- Exponential slowdown from full speed toward 10 percent at death
- One replaceable recent replay holding 20 song seconds by default
- A Replay action in the pause menu
- In-game settings on the song selection side panel
- Optional looping, mistake navigation and replay transport controls
- Practice speeds from 5 to 200 percent

## Compatibility

This release targets Steam Beat Saber **1.40.8** and **BeatLeader 0.9.33**.

| Dependency | Version |
| --- | --- |
| BSIPA | 4.3.6 |
| BeatLeader | 0.9.33 |
| BeatSaberMarkupLanguage | 1.12.5 |
| SiraUtil | 3.2.1 |

PracticePlugin 9.1.0 is optional and adds its existing live practice controls. Cutback integrates its speed and seek behavior. Recording covers solo gameplay supported by BeatLeader's recorder. The original map and its required extensions must remain installed for playback.

## Installation

1. Close Beat Saber.
2. Download the ZIP from [Releases](https://github.com/pigeonIabs/Cutback/releases).
3. Extract its contents into the Beat Saber 1.40.8 instance folder. The plugin belongs at `Plugins/Cutback.dll`.
4. Launch that instance normally.

For an upgrade from PracticeForge, replace `Plugins/PracticeForge.dll` with Cutback. Its previous configuration can be copied from `UserData/PracticeForge.json` to `UserData/Cutback.json`. The previous `Recent` folder can be copied to `UserData/Cutback/Recent`. Cutback reads the previous replay metadata format.

## Usage

Choose **Replay** in the pause menu to review the current attempt. Opening review ends live gameplay and loads the captured scene through the game's scene transitions.

On death, review launches automatically and pauses at the last captured pose. The replay transport provides play and pause, watch again, previous and next mistake, five-second jumps and exit. Choose **Cutback** in the main menu to watch the latest recording.

Open the **Cutback** tab on the song selection side panel to adjust automatic death playback, slow motion, lead time, slowdown duration, minimum speed, clip length and looping. Settings persist through BSIPA configuration. A very short attempt can provide less footage than the selected lead time.

The recent recording replaces `UserData/Cutback/Recent/latest.bsor` and `latest.json`. BeatLeader manages its own ordinary recordings independently.

## Building

Install a .NET SDK and point the build at a compatible game instance containing the dependencies above.

```powershell
dotnet build Cutback.csproj -c Release -p:BeatSaberDir="D:\Games\Beat Saber\1.40.8"
```

Alternatively, place the game instance in the ignored `Game` directory. The build restores the .NET Framework 4.8 reference package and resolves runtime references from the game installation. The resulting plugin is `bin/Release/net48/Cutback.dll`.

The 1.0.0 release compiles with zero warnings and zero errors. VR interaction, replay playback and coexistence with other mods await in-game verification.

## License

Copyright (c) 2026 pigeonIabs. Cutback is licensed under [GNU AGPL-3.0](LICENSE), with the runtime linking permission in [NOTICE.md](NOTICE.md).

The adapted BeatLeader code retains its MIT notices. See [ThirdParty](ThirdParty) and [implementation notes](IMPLEMENTATION.md) for attribution and architecture.
