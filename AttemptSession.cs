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
    public sealed class AttemptSession : IInitializable, ITickable, ILateTickable, IDisposable
    {
        internal static AttemptSession Current;
        internal static AttemptSnapshot FailedResultReplay { get; private set; }
        internal static BeatmapKey? FailedResultKey { get; private set; }
        internal static bool HasFailedResult(BeatmapKey key) => FailedResultReplay != null &&
            FailedResultKey.HasValue && FailedResultKey.Value.Equals(key);
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
        private bool recordingPromoted;
        private float replacementSeconds;
        private bool reviewExitRequested;
        private bool reviewLoading;
        private float reviewExitAt;
        private Action exitReview;
        private readonly List<NoteEvent> immediateMistakes = new List<NoteEvent>();
        private RectTransform pauseRoot;
        private float? failureTime;
        private float failureStartedAt, failureTailUntil;
        private readonly List<Frame> failureTailFrames = new List<Frame>();
        private bool automaticFailureReview;

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
            failureTime = null;
            failureTailFrames.Clear();
            FailedResultReplay = null;
            FailedResultKey = null;
            recordingPromoted = false;
            replacementSeconds = Plugin.Settings.ReplayReplacementSeconds;
            if (float.IsNaN(replacementSeconds) || float.IsInfinity(replacementSeconds)) replacementSeconds = 5;
            replacementSeconds = Mathf.Clamp(replacementSeconds, 0, 120);
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
            if (failureTime.HasValue) return;
            if (!enabled || finalized || seeking || clock.state != AudioTimeSyncController.State.Playing) return;
            UpdateReplayOwnership();
            if (Time.realtimeSinceStartup >= checkpointAt)
            {
                checkpointAt = Time.realtimeSinceStartup + Math.Max(10, Plugin.Settings.CheckpointSeconds);
                Plugin.Guard(() => Save("Checkpoint", false));
            }
        }

        public void LateTick()
        {
            // BeatLeader still measures real poses during the native failure animation,
            // even after the audio clock stops. Give only our owned tail frames a
            // continuous timestamp, leaving BeatLeader's ordinary recording untouched.
            if (!enabled || finalized || !failureTime.HasValue) return;
            // Sample the deadline and frame time together. Real time can advance
            // during this method if the game stalls or collects garbage.
            float now = Time.realtimeSinceStartup;
            CaptureFailureFrame(now);
            if (now >= failureTailUntil) Plugin.Guard(() => Save("Failed", true));
        }

        private void CaptureFailureFrame(float now)
        {
            float elapsed = Math.Min(0.5f, now - failureStartedAt);
            float time = failureTime.Value + elapsed * segmentSpeed;
            if (live.frames.Count > 0 && (failureTailFrames.Count == 0 || time > failureTailFrames[failureTailFrames.Count - 1].time))
            {
                var measured = Reflect.Clone(live.frames[live.frames.Count - 1]);
                measured.time = time;
                failureTailFrames.Add(measured);
            }
        }

        internal void Cut(NoteController note, in global::NoteCutInfo cut)
        {
            if (seeking || finalized || failureTime.HasValue || cut.allIsOK) return;
            immediateMistakes.Add(new NoteEvent {
                noteID = BeatLeader.Utils.ReplayDataUtils.ComputeNoteId(note.noteData),
                spawnTime = note.noteData.time, eventTime = clock.songTime,
                eventType = note.noteData.colorType == ColorType.None ? NoteEventType.bomb : NoteEventType.bad,
                noteCutInfo = (CutInfo)cut
            });
        }

        internal void Miss(NoteController note)
        {
            if (seeking || finalized || failureTime.HasValue || note.noteData.colorType == ColorType.None || note.noteData.scoringType == NoteData.ScoringType.NoScore) return;
            immediateMistakes.Add(new NoteEvent {
                noteID = BeatLeader.Utils.ReplayDataUtils.ComputeNoteId(note.noteData),
                spawnTime = note.noteData.time, eventTime = clock.songTime, eventType = NoteEventType.miss
            });
        }

        private void Paused() => Plugin.Guard(() =>
        {
            Save("Paused", false);
            if (pauseRoot == null) pauseRoot = PauseReplayButton.Create(pauseMenu, Review);
            pauseRoot.gameObject.SetActive(true);
        });
        private void Resumed() { if (pauseRoot != null) pauseRoot.gameObject.SetActive(false); }

        private async void Review()
        {
            if (reviewLoading || reviewExitRequested || ReviewCoordinator.Pending != null) return;
            reviewLoading = true;
            try
            {
                bool usePrevious = PreservesPreviousReplay();
                AttemptSnapshot snapshot = usePrevious ? await AttemptStore.LatestAfterRecovery() : null;
                // Recovery runs off-thread at startup. Preserve its replay while loading,
                // and abandon the request if the player has already left or resumed.
                if (Current != this || clock.state != AudioTimeSyncController.State.Paused || ReviewCoordinator.Pending != null) return;
                usePrevious = usePrevious && snapshot != null && PreservesPreviousReplay();
                if (!usePrevious) snapshot = Snapshot(finalized ? outcome : "Reviewed");
                if (snapshot == null || snapshot.Replay.frames.Count < 2)
                {
                    PauseReplayButton.SetMessage(pauseRoot, "Play a little longer to capture motion");
                    return;
                }
                if (!usePrevious) AttemptStore.Save(snapshot);
                // The provisional recording lives only in the recorder. Finalizing the
                // local session discards it without publishing or serializing it on exit.
                finalized = true;
                ReviewCoordinator.Queue(snapshot, true);
                // The normal exit unwinds gameplay before the replay owns the shared transition SO.
                // Defer scene teardown until the UI click has completed dispatching.
                reviewExitAt = Time.realtimeSinceStartup + 0.06f;
                reviewExitRequested = true;
                exitReview = () => returnToMenu.ReturnToMenu();
                PauseReplayButton.SetMessage(pauseRoot, "Opening replay");
                Plugin.Log.Info("Queued pause replay " + snapshot.Header.Id + " with " + snapshot.Replay.frames.Count + " frames");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex);
                PauseReplayButton.SetMessage(pauseRoot, "Replay needs attention. See the game log.");
            }
            finally { reviewLoading = false; }
        }

        internal void MarkFailed(bool autoRestart)
        {
            if (!enabled || finalized || failureTime.HasValue) return;
            UpdateReplayOwnership();
            failureTime = clock.songTime;
            failureStartedAt = Time.realtimeSinceStartup;
            failureTailUntil = failureStartedAt + 0.5f;
            automaticFailureReview = Plugin.Settings.ReplayOnDeath && !autoRestart;
            outcome = "Failed";
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
            // A native completion callback can run before our next late tick.
            // Include its final measured pose and use the same finalization path.
            if (finish && failureTime.HasValue) CaptureFailureFrame(Time.realtimeSinceStartup);
            AttemptSnapshot snapshot = null;
            if (!PreservesPreviousReplay())
            {
                snapshot = Snapshot(failureTime.HasValue ? "Failed" : reason);
                if (snapshot != null && snapshot.Replay.frames.Count >= 2) AttemptStore.Save(snapshot);
            }
            if (finish)
            {
                finalized = true;
                outcome = failureTime.HasValue ? "Failed" : reason;
                if (failureTime.HasValue)
                {
                    FailedResultReplay = snapshot?.Replay.frames.Count >= 2 ? snapshot : AttemptStore.GetLatest();
                    FailedResultKey = setup.beatmapKey;
                    if (FailedResultReplay?.Header.Id == id)
                    {
                        Plugin.Log.Info($"Captured failed attempt through {FailedResultReplay.Header.End:F3}s, failure at {failureTime.Value:F3}s");
                        if (automaticFailureReview) ReviewCoordinator.Queue(FailedResultReplay, true);
                    }
                }
            }
        }

        private void UpdateReplayOwnership()
        {
            if (recordingPromoted || failureTime.HasValue || clock.songTime - segmentStart < replacementSeconds) return;
            recordingPromoted = true;
            AttemptStore.PromoteRecording(id);
        }

        private bool PreservesPreviousReplay()
        {
            UpdateReplayOwnership();
            return !recordingPromoted && AttemptStore.HasPrevious(id);
        }

        private AttemptSnapshot Snapshot(string reason)
        {
            if (live == null) return null;
            // The recorder's completed frames are append-only. Copy list ownership and all
            // mutable note/wall/header objects before handing work to the disk writer.
            var frames = new List<Frame>();
            float captureEnd = failureTime.HasValue
                ? (failureTailFrames.Count > 0 ? failureTailFrames[failureTailFrames.Count - 1].time : failureTime.Value)
                : clock.songTime;
            float recorderEnd = failureTime ?? captureEnd;
            float last = float.NegativeInfinity;
            for (int i = frameOffset; i < live.frames.Count; i++)
            {
                var frame = live.frames[i];
                if (frame.time < segmentStart || frame.time <= last || frame.time > recorderEnd + 0.001f) continue;
                frames.Add(frame);
                last = frame.time;
            }
            foreach (var frame in failureTailFrames)
            {
                if (frame.time <= last) continue;
                frames.Add(frame);
                last = frame.time;
            }
            // The paused clock can be one frame ahead of LateTick. Hold the last measured
            // pose at that exact time so an immediate mistake remains inside the replay.
            if (frames.Count > 0 && captureEnd > last && captureEnd - last < 0.1f)
            {
                var finalFrame = Reflect.Clone(frames[frames.Count - 1]);
                finalFrame.time = captureEnd;
                frames.Add(finalFrame);
            }
            var replay = new Replay {
                info = Reflect.Clone(live.info), frames = frames,
                notes = live.notes.Skip(noteOffset).Where(n => n.eventType != NoteEventType.unknown && n.spawnTime >= filterStart && n.eventTime <= recorderEnd).Select(CloneNote).ToList(),
                walls = live.walls.Skip(wallOffset).Select(Reflect.Clone).ToList(),
                heights = live.heights.Skip(heightOffset).Select(Reflect.Clone).ToList(),
                pauses = live.pauses.Skip(pauseOffset).Select(Reflect.Clone).ToList(),
                saberOffsets = Reflect.Clone(live.saberOffsets),
                customData = new Dictionary<string, byte[]>()
            };
            var recordedNotes = new Dictionary<(int, float), NoteEvent>(replay.notes.Count);
            foreach (var note in replay.notes) recordedNotes[(note.noteID, note.spawnTime)] = note;
            foreach (var mistake in immediateMistakes)
            {
                if (!recordedNotes.TryGetValue((mistake.noteID, mistake.spawnTime), out var recorded))
                {
                    var copy = CloneNote(mistake);
                    replay.notes.Add(copy);
                    recordedNotes[(copy.noteID, copy.spawnTime)] = copy;
                }
                else if (recorded.eventType == NoteEventType.bad || recorded.eventType == NoteEventType.miss || recorded.eventType == NoteEventType.bomb)
                    recorded.eventTime = mistake.eventTime;
            }
            replay.notes.Sort((a, b) => a.eventTime.CompareTo(b.eventTime));
            ReplayMetadata.Fill(replay.info, setup, score.multipliedScore, movement.jumpDistance);
            replay.info.startTime = filterStart;
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
                DeathTime = failureTime,
                ModifierValues = ModifiersMapManager.CurrentModifiersMap,
                Mistakes = replay.notes.Where(n => IsMistake(n) && n.eventTime >= segmentStart && n.eventTime <= captureEnd).Select(n => n.eventTime).Distinct().OrderBy(t => t).ToArray()
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

    [HarmonyPatch(typeof(StandardLevelFailedController), "HandleLevelFailed")]
    internal static class CaptureFailure
    {
        private static void Prefix(StandardLevelFailedController __instance)
        {
            Plugin.Guard(() => AttemptSession.Current?.MarkFailed(
                Reflect.Get<StandardLevelFailedController.InitData>(__instance, "_initData").autoRestart));
        }
    }
}
