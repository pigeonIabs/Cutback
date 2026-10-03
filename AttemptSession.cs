// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using BeatLeader;
using BeatLeader.Models.Replay;
using BeatLeader.Replayer;
using HarmonyLib;
using UnityEngine;
using Zenject;
using Replay = BeatLeader.Models.Replay.Replay;
using CutInfo = BeatLeader.Models.Replay.NoteCutInfo;

namespace Cutback
{
    public sealed class AttemptSession : IInitializable, ITickable, IDisposable
    {
        internal static AttemptSession Current;
        [InjectOptional] private readonly ReplayRecorder recorder;
        [Inject] private readonly GameplayCoreSceneSetupData setup;
        [Inject] private readonly AudioTimeSyncController clock;
        [Inject] private readonly ScoreController score;
        [Inject] private readonly PauseMenuManager pauseMenu;
        [Inject] private readonly PauseController pause;
        [Inject] private readonly VariableMovementDataProvider movement;
        [Inject] private readonly DiContainer container;
        [Inject] private readonly IReturnToMenuController returnToMenu;
        private Replay live;
        private int frameOffset, noteOffset, wallOffset, heightOffset, pauseOffset;
        private float checkpointAt, segmentStart, filterStart, segmentSpeed;
        private string id, outcome;
        private DateTime created;
        private bool finalized, seeking, enabled;
        private bool reviewExitRequested;
        private float reviewExitAt;
        private Action exitReview;
        private readonly List<NoteEvent> immediateMistakes = new List<NoteEvent>();
        private RectTransform pauseRoot;

        public void Initialize()
        {
            if (ReplayerLauncher.IsStartedAsReplay)
            {
                if (ReviewCoordinator.IsLocalReview) ReviewCoordinator.Attach(container);
                return;
            }
            if (recorder == null)
            {
                Plugin.Log.Warn("Cutback could not resolve the BeatLeader recorder for this gameplay flow.");
                return;
            }
            Current = this;
            enabled = true;
            live = Reflect.Get<Replay>(recorder, "_replay");
            Begin(setup.practiceSettings?.startSongTime ?? 0);
            pause.didPauseEvent += Paused;
            pause.didResumeEvent += Resumed;
        }

        private void Begin(float start)
        {
            id = Guid.NewGuid().ToString("N");
            created = DateTime.UtcNow;
            outcome = "In progress";
            filterStart = Math.Max(0, start);
            segmentStart = filterStart;
            segmentSpeed = clock.isReady && clock.timeScale > 0 ? clock.timeScale
                : setup.practiceSettings?.songSpeedMul ?? setup.gameplayModifiers.songSpeedMul;
            frameOffset = live.frames.Count;
            noteOffset = live.notes.Count;
            wallOffset = live.walls.Count;
            heightOffset = live.heights.Count;
            pauseOffset = live.pauses.Count;
            immediateMistakes.Clear();
            finalized = false;
            checkpointAt = Time.realtimeSinceStartup + Math.Max(10, Plugin.Settings.CheckpointSeconds);
        }

        public void Tick()
        {
            if (reviewExitRequested && Time.realtimeSinceStartup >= reviewExitAt)
            {
                reviewExitRequested = false;
                Plugin.Guard(exitReview);
            }
            if (ReviewCoordinator.IsLocalReview) { ReviewCoordinator.Tick(); return; }
            if (!enabled || finalized || seeking || clock.state != AudioTimeSyncController.State.Playing) return;
            if (Time.realtimeSinceStartup >= checkpointAt)
            {
                checkpointAt = Time.realtimeSinceStartup + Math.Max(10, Plugin.Settings.CheckpointSeconds);
                Plugin.Guard(() => Save("Checkpoint", false));
            }
        }

        internal void Cut(NoteController note, in global::NoteCutInfo cut)
        {
            if (seeking || finalized || cut.allIsOK) return;
            immediateMistakes.Add(new NoteEvent {
                noteID = BeatLeader.Utils.ReplayDataUtils.ComputeNoteId(note.noteData),
                spawnTime = note.noteData.time, eventTime = clock.songTime,
                eventType = note.noteData.colorType == ColorType.None ? NoteEventType.bomb : NoteEventType.bad,
                noteCutInfo = (CutInfo)cut
            });
        }

        internal void Miss(NoteController note)
        {
            if (seeking || finalized || note.noteData.colorType == ColorType.None || note.noteData.scoringType == NoteData.ScoringType.NoScore) return;
            immediateMistakes.Add(new NoteEvent {
                noteID = BeatLeader.Utils.ReplayDataUtils.ComputeNoteId(note.noteData),
                spawnTime = note.noteData.time, eventTime = clock.songTime, eventType = NoteEventType.miss
            });
        }

        private void Paused() => Plugin.Guard(() =>
        {
            Save("Paused", false);
            if (pauseRoot == null) pauseRoot = CutbackUI.CreatePauseReview(pauseMenu, Review);
            pauseRoot.gameObject.SetActive(true);
        });
        private void Resumed() { if (pauseRoot != null) pauseRoot.gameObject.SetActive(false); }

        private void Review() => Plugin.Guard(() =>
        {
            if (reviewExitRequested || ReviewCoordinator.Pending != null) return;
            AttemptSnapshot snapshot = Snapshot(finalized ? outcome : "Reviewed");
            if (snapshot == null || snapshot.Replay.frames.Count < 2)
            {
                CutbackUI.PauseMessage(pauseRoot, "Play a little longer to capture motion");
                return;
            }
            AttemptStore.Save(snapshot);
            finalized = true;
            ReviewCoordinator.Queue(snapshot, true);
            // The normal exit unwinds gameplay before the replay owns the shared transition SO.
            // Defer scene teardown until the UI click has completed dispatching.
            reviewExitAt = Time.realtimeSinceStartup + 0.06f;
            reviewExitRequested = true;
            exitReview = () => returnToMenu.ReturnToMenu();
            CutbackUI.PauseMessage(pauseRoot, "Opening replay");
            Plugin.Log.Info("Queued pause replay " + snapshot.Header.Id + " with " + snapshot.Replay.frames.Count + " frames");
        });

        internal bool ReviewDeath(StandardLevelFailedController controller)
        {
            if (!enabled || finalized || !Plugin.Settings.ReplayOnDeath || reviewExitRequested) return false;
            var snapshot = Snapshot("Failed");
            if (snapshot == null || snapshot.Replay.frames.Count < 2) return false;
            snapshot.Header.DeathTime = clock.songTime;
            AttemptStore.Save(snapshot);
            finalized = true;
            outcome = "Failed";
            ReviewCoordinator.Queue(snapshot, true);
            // Finish on the following tick, after the energy event completes dispatching.
            exitReview = () =>
            {
                var song = Reflect.Get<GameSongController>(controller, "_gameSongController");
                song.StopSong();
                var results = Reflect.Get<PrepareLevelCompletionResults>(controller, "_prepareLevelCompletionResults")
                    .FillLevelCompletionResults(LevelCompletionResults.LevelEndStateType.Failed, LevelCompletionResults.LevelEndAction.Quit);
                Reflect.Get<StandardLevelScenesTransitionSetupDataSO>(controller, "_standardLevelSceneSetupData").Finish(results);
            };
            reviewExitAt = Time.realtimeSinceStartup;
            reviewExitRequested = true;
            Plugin.Log.Info("Queued death replay at " + snapshot.Header.DeathTime.Value);
            return true;
        }

        internal void BeforeSeek()
        {
            if (!enabled || seeking) return;
            Save("Seek", true);
            seeking = true;
        }
        internal void AfterSeek(float time)
        {
            if (!enabled) return;
            Begin(time);
            seeking = false;
        }
        internal void ScheduledStart(float time)
        {
            if (enabled && clock.state == AudioTimeSyncController.State.Stopped) Begin(time);
        }

        internal void Save(string reason, bool finish)
        {
            if (!enabled || finalized) return;
            var snapshot = Snapshot(reason);
            if (snapshot != null) AttemptStore.Save(snapshot);
            if (finish) { finalized = true; outcome = reason; }
        }

        private AttemptSnapshot Snapshot(string reason)
        {
            if (live == null) return null;
            // The recorder's completed frames are append-only. Copy list ownership and all
            // mutable note/wall/header objects before handing work to the disk writer.
            var frames = new List<Frame>();
            float bufferStart = Math.Max(segmentStart, clock.songTime - Mathf.Clamp(Plugin.Settings.BufferSeconds, 5f, 120f));
            float last = float.NegativeInfinity;
            for (int i = frameOffset; i < live.frames.Count; i++)
            {
                var frame = live.frames[i];
                if (frame.time < bufferStart || frame.time <= last || frame.time > clock.songTime + 0.01f) continue;
                frames.Add(frame);
                last = frame.time;
            }
            // The paused clock can be one frame ahead of LateTick. Hold the last measured
            // pose at that exact time so an immediate mistake remains inside the replay.
            if (frames.Count > 0 && clock.songTime > last && clock.songTime - last < 0.1f)
            {
                var finalFrame = Reflect.Clone(frames[frames.Count - 1]);
                finalFrame.time = clock.songTime;
                frames.Add(finalFrame);
            }
            var replay = new Replay {
                info = Reflect.Clone(live.info), frames = frames,
                notes = live.notes.Skip(noteOffset).Where(n => n.eventType != NoteEventType.unknown && n.spawnTime >= filterStart).Select(CloneNote).ToList(),
                walls = live.walls.Skip(wallOffset).Select(Reflect.Clone).ToList(),
                heights = live.heights.Skip(heightOffset).Select(Reflect.Clone).ToList(),
                pauses = live.pauses.Skip(pauseOffset).Select(Reflect.Clone).ToList(),
                saberOffsets = Reflect.Clone(live.saberOffsets),
                customData = new Dictionary<string, byte[]>()
            };
            foreach (var mistake in immediateMistakes)
            {
                var recorded = replay.notes.FirstOrDefault(n => n.noteID == mistake.noteID && Math.Abs(n.spawnTime - mistake.spawnTime) < 0.001f);
                if (recorded == null) replay.notes.Add(CloneNote(mistake));
                else if (recorded.eventType == NoteEventType.bad || recorded.eventType == NoteEventType.miss || recorded.eventType == NoteEventType.bomb)
                    recorded.eventTime = mistake.eventTime;
            }
            replay.notes.Sort((a, b) => a.eventTime.CompareTo(b.eventTime));
            ReplayMetadata.Fill(replay.info, setup, score.multipliedScore, movement.jumpDistance);
            replay.info.startTime = Math.Max(filterStart, bufferStart);
            // A pause speed edit belongs to the next seek segment. Preserve the speed
            // at which this segment was recorded rather than the newly selected value.
            replay.info.speed = segmentSpeed;
            replay.info.failTime = 0; // End-of-capture belongs to the local transport, not synthetic failure.
            if (replay.info.height <= 0 && replay.heights.Count == 0) replay.info.height = setup.playerSpecificSettings.playerHeight;
            var header = new AttemptHeader {
                Id = id, CreatedUtc = created, LevelId = setup.beatmapKey.levelId,
                Song = setup.beatmapLevel.songName, Characteristic = setup.beatmapKey.beatmapCharacteristic.serializedName,
                Difficulty = setup.beatmapKey.difficulty.ToString(), Outcome = reason,
                Start = frames.Count > 0 ? frames[0].time : filterStart,
                End = frames.Count > 0 ? frames[frames.Count - 1].time : filterStart,
                Speed = replay.info.speed, Frames = frames.Count,
                DeathTime = reason == "Failed" ? (float?)clock.songTime : null,
                Mistakes = replay.notes.Where(n => IsMistake(n) && n.eventTime >= bufferStart).Select(n => n.eventTime).Distinct().OrderBy(t => t).ToArray()
            };
            return new AttemptSnapshot { Header = header, Replay = replay };
        }

        private static NoteEvent CloneNote(NoteEvent note)
        {
            var clone = Reflect.Clone(note);
            clone.noteCutInfo = Reflect.Clone(note.noteCutInfo);
            return clone;
        }
        internal static bool IsMistake(NoteEvent n) => n.eventType == NoteEventType.bad || n.eventType == NoteEventType.miss || n.eventType == NoteEventType.bomb;

        public void Dispose()
        {
            if (enabled)
            {
                Plugin.Guard(() => Save(outcome == "In progress" ? "Abandoned" : outcome, true));
                pause.didPauseEvent -= Paused;
                pause.didResumeEvent -= Resumed;
                if (pauseRoot != null) UnityEngine.Object.Destroy(pauseRoot.gameObject);
                if (Current == this) Current = null;
            }
            ReviewCoordinator.Detach(container);
        }
    }

    // Capture before GameEnergyCounter receives the event. An insta-fail can finalize
    // the attempt from inside that same event dispatch.
    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasMissed")]
    internal static class CaptureImmediateMiss
    {
        private static void Prefix(NoteController noteController) => Plugin.Guard(() => AttemptSession.Current?.Miss(noteController));
    }

    [HarmonyPatch(typeof(BeatmapObjectManager), "HandleNoteControllerNoteWasCut")]
    internal static class CaptureImmediateCut
    {
        private static void Prefix(NoteController noteController, in global::NoteCutInfo noteCutInfo)
        {
            try { AttemptSession.Current?.Cut(noteController, in noteCutInfo); }
            catch (Exception ex) { Plugin.Log.Error(ex); }
        }
    }

    [HarmonyPatch(typeof(ReplayRecorder), "Dispose")]
    internal static class RecorderDisposal
    {
        private static void Prefix() => Plugin.Guard(() => AttemptSession.Current?.Save("Abandoned", true));
    }

    [HarmonyPatch(typeof(ReplayRecorder), "FinalizeReplay")]
    internal static class RecorderFinalization
    {
        private static void Prefix(LevelCompletionResults results) => Plugin.Guard(() =>
            AttemptSession.Current?.Save(results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Incomplete
                ? "Abandoned" : results.levelEndStateType.ToString(), true));
    }

    [HarmonyPatch(typeof(StandardLevelRestartController), "RestartLevel")]
    internal static class CaptureRestart
    {
        private static void Prefix() => Plugin.Guard(() =>
        {
            if (AttemptSession.Current == null) return;
            AttemptSession.Current.Save("Restarted", true);
            // Direct restart callers also need the recorder gate restored. BeatLeader
            // normally does this from its pause button hook rather than this controller.
            AccessTools.Method("BeatLeader.Utils.RecorderUtils:OnRestartPauseButtonWasPressed").Invoke(null, null);
        });
    }

    [HarmonyPatch(typeof(StandardLevelGameplayManager), "HandleGameEnergyDidReach0")]
    internal static class CaptureFailure
    {
        private static void Postfix(StandardLevelGameplayManager __instance)
        {
            if (Reflect.Get<object>(__instance, "_gameState").ToString() == "Failed")
                Plugin.Guard(() => AttemptSession.Current?.Save("Failed", true));
        }
    }

    [HarmonyPatch(typeof(StandardLevelFailedController), "HandleLevelFailed")]
    internal static class AutomaticDeathReview
    {
        private static bool Prefix(StandardLevelFailedController __instance)
        {
            try { return !(AttemptSession.Current?.ReviewDeath(__instance) ?? false); }
            catch (Exception ex) { Plugin.Log.Error(ex); return true; }
        }
    }
}
