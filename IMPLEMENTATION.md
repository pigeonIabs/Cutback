# Implementation

## Recording and retention

AttemptSession resolves BeatLeader's recorder from the solo gameplay container. It copies the latest movement frames and takes independent copies of mutable note, wall and metadata objects. The clip length is configurable. Note history remains available to reconstruct scoring state when seeking into the clip.

Misses and bad cuts are captured before the energy counter can trigger failure from the same event. A snapshot merges those immediate events with completed recorder events. Pauses, failures, restarts, seeks, periodic checkpoints and clean exit update the recent recording.

AttemptStore keeps the latest snapshot in memory and serializes writes on a background queue. It flushes temporary files before atomically replacing latest.bsor and latest.json. Embedded metadata supports recovery independently of the index. Previous replay metadata is accepted for migration.

## Death review

The failure handler queues a local snapshot and defers scene teardown until the energy event finishes dispatching. It completes the live attempt with its failed state and a quit action. The menu opens the queued replay after the live gameplay scene is disposed and the scene transition ends. The usual results presentation yields to the pending death review.

Playback uses BeatLeader's ReplayerLauncher and replay transport interfaces. The replay finish callback pops its gameplay scene using the same lifecycle as BeatLeader's menu loader.

## Slow motion

During death review, speed follows minimumSpeed raised to progress, where progress increases from zero to one across the configured slowdown interval. This reaches the selected minimum speed at the death moment. Other reviews ramp toward recorded mistakes and hold the minimum briefly afterward.

Local replay speed changes adjust audio pitch and rebase the audio clock at the current song time while audio remains running. BeatLeader's speed event updates replay visuals. Explicit seeks preserve playback pause state. The transport pauses at the captured end, with optional death-clip looping after a short hold.

## Practice integration

Practice speed controls receive a 5 to 200 percent range. The clock is initialized before note prewarming. Low-speed audio sample regressions and seeks receive clock corrections. Pitch compensation is capped at 2.

When PracticePlugin is installed, its existing seek cleanup remains responsible for despawning and map extension integration. Cutback aligns the audio clock and note filter afterward. PracticePlugin's live gameplay installer yields during a Cutback replay so the replay transport owns seeking and playback.

## Interface and scoring

The additional pause action shares the native action row, clear of the live seek controls. Cutback's controls use rectangular gradient surfaces and explicit VR hit areas. The song selection side panel uses BSML tab registration with persistent settings.

BeatLeader's normal viewer yields only during a local Cutback replay. Its replay flag and score-submission isolation remain active throughout playback. The local snapshot is independent of leaderboard replay selection.

## Upstream source

- [BeatLeader](https://github.com/BeatLeader/beatleader-mod/tree/265424aa1dcd2ba205ccc78238354de6c52f4d59) supplied the adapted encoder and metadata mapping, plus the installed runtime engine.
- [BS Open Replay](https://github.com/BeatLeader/BS-Open-Replay/tree/5f7d302683beee332a14542de57f6bd72a10054a) documents the replay format.
- [PracticePlugin](https://github.com/denpadokei/PracticePlugin/tree/5c6e0c6cb2f9cb0b2f7aa09be11da91af3f6098d) supplies the optional practice implementation.

Third-party copyright notices are retained in ThirdParty. Game assemblies and runtime dependencies are resolved from a local installation when building.
