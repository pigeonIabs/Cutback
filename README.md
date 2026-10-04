# Cutback

Cutback records Beat Saber solo attempts as local replays and lets you review them through BeatLeader's replay viewer.

**Source only for development.** The current code is going through validation in the game. Treat it as development code while replay flow, VR interaction, and mod compatibility are still being checked.

## Current behavior

- Cutback records the active attempt from the first available recorder frames through pause, failure, completion, seek, restart, or exit. Each snapshot covers the captured segment through its save boundary.
- When a song fails, Cutback stays with Beat Saber's native failure flow and captures the scene's later movement for half a second of real time. The capture end follows the active song speed.
- The failed results Replay action opens the failed attempt's own local snapshot when it matches the failed map.
- A separate Replay icon appears above the original pause menu button row. Continue, restart, and back keep their native positions and actions.
- Local reviews use BeatLeader's normal toolbar, time display, timeline, and markers throughout playback. Miss and bad cut X markers seek to a configurable start time before the event.
- For local No Fail replays, the native timeline and transport end at the captured attempt boundary.
- Misses participate in automatic focus and slow motion by default. Bad cuts start disabled. Their settings work independently. A recorded failure takes priority as the initial focus event.
- Automatic speed follows an exponential ramp around the selected events, reaches the chosen minimum at each event, and holds through the configured tail. The default minimum is 10 percent of the recorded playback speed.
- Beat Saber's practice speed slider ranges from 5 to 200 percent. With PracticePlugin installed, Cutback lowers its practice speed minimum to 5 percent.

## Recent replay and settings

Cutback keeps one recent replay at `UserData/Cutback/Recent/latest.bsor` with its index in `latest.json`. The default replacement grace is five song seconds. The current attempt starts recording immediately while the previous replay remains available during that grace. At the threshold, Cutback releases the previous replay from the active session. A later save publishes the current attempt in its place. Set the grace from zero to 120 seconds in the Cutback tab in Gameplay Setup.

The same tab controls replay on death, missed note focus, bad cut focus, automatic slow motion, the start time before a mistake, slowdown duration, minimum speed, replacement grace, and death clip looping. Defaults are replay on death enabled, missed note focus enabled, bad cut focus disabled, automatic slow motion enabled, three seconds before a mistake, three seconds of slowdown, 10 percent minimum speed, five seconds of replacement grace, and looping disabled.

The main menu Cutback page offers **Watch latest** and **Review mistake**. The pause Replay action uses the protected previous replay during the grace period. After promotion, it captures and reviews the active attempt.

## Compatibility and limits

Development targets Steam Beat Saber **1.40.8** and **BeatLeader 0.9.33**.

| Dependency | Version |
| --- | --- |
| BSIPA | 4.3.6 |
| BeatLeader | 0.9.33 |
| BeatSaberMarkupLanguage | 1.12.5 |
| SiraUtil | 3.2.1 |

PracticePlugin 9.1.0 is optional. Cutback adjusts its supported practice speed and clock behavior. Reviewing requires the recorded map and characteristic to remain installed. The replay viewer requires at least two captured movement frames. The local replay reader rejects files larger than 512 MB.

## Build

Build against an existing compatible game instance with the dependencies above.

```powershell
dotnet build Cutback.csproj -c Release -p:BeatSaberDir="D:\Games\Beat Saber\1.40.8"
```

You can also place the game instance in the ignored `Game` directory. The build restores the .NET Framework 4.8 reference package and reads runtime assemblies from that game instance. The output is `bin/Release/net48/Cutback.dll`.

## License

Copyright (c) 2026 pigeonIabs. Cutback is licensed under [GNU AGPL-3.0](LICENSE), with runtime linking permission in [NOTICE.md](NOTICE.md).

Third party notices are retained in [ThirdParty](ThirdParty). See [IMPLEMENTATION.md](IMPLEMENTATION.md) for architecture and upstream attribution.
