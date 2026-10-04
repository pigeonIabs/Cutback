// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Linq;
using BeatLeader.Models;
using BeatLeader.Replayer;
using BeatLeader.Utils;
using UnityEngine;
using Zenject;

namespace Cutback
{
    internal static class ReviewCoordinator
    {
        internal static bool IsLocalReview => Active != null && ReplayerLauncher.IsStartedAsReplay;
        internal static AttemptSnapshot Active { get; private set; }
        internal static AttemptSnapshot Pending { get; private set; }
        private static bool focusMistake, initialized, atEnd;
        private static float loopAt;
        private static bool deathReview;
        private static ReviewPlan plan;
        private static DiContainer activeContainer;
        private static GameScenesManager scenes;
        private static IReplayTimeController transport;
        private static IReplayPauseController pause;
        private static AudioTimeSyncController clock;
        internal static bool AutoSlow { get; private set; }

        internal static void Queue(AttemptSnapshot snapshot, bool focus)
        {
            Pending = snapshot;
            focusMistake = focus;
            CutbackMenu.Status = "Opening attempt";
        }

        internal static bool Launch(ReplayerLauncher launcher, BeatmapLevelsModel levels, GameScenesManager scenesManager)
        {
            if (Pending == null) return false;
            var snapshot = Pending;
            Pending = null;
            var level = levels.GetBeatmapLevel(snapshot.Header.LevelId);
            var characteristic = level?.GetCharacteristics().FirstOrDefault(c => c.serializedName == snapshot.Header.Characteristic);
            if (level == null || characteristic == null) throw new InvalidOperationException("Install the recorded map and characteristic to review this attempt.");
            if (snapshot.Replay.frames.Count < 2) throw new InvalidOperationException("This attempt ended before movement was captured.");
            var key = new BeatmapKey(level.levelID, characteristic, (BeatmapDifficulty)Enum.Parse(typeof(BeatmapDifficulty), snapshot.Header.Difficulty));
            var settings = Reflect.Clone(ReplayerSettings.UserSettings);
            settings.ExitReplayAutomatically = false;
            settings.AutoHideUI = false;
            settings.IgnoreModifiers = false;
            settings.ShowLeftSaber = true;
            settings.ShowRightSaber = true;
            settings.ShowTimelineMisses = true;
            settings.ShowTimelineBombs = true;
            settings.LoadPlayerJumpDistance = true;
            settings.LoadPlayerEnvironment = false;
            settings.ShowWatermark = false;
            var data = new ReplayLaunchData();
            data.Init(ReplayDataUtils.ConvertToAbstractReplay(snapshot.Replay, null), ReplayDataUtils.BasicReplayComparator, settings, level, key);
            plan = new ReviewPlan(snapshot, focusMistake, Plugin.Settings);
            deathReview = snapshot.Header.DeathTime.HasValue;
            Active = snapshot;
            scenes = scenesManager;
            AutoSlow = Plugin.Settings.AutomaticSlowMotion;
            initialized = false;
            atEnd = false;
            loopAt = 0;
            data.ReplayWasFinishedEvent += Finished;
            try
            {
                if (!launcher.StartReplay(data)) throw new InvalidOperationException("BeatLeader could not start this replay.");
            }
            catch { data.ReplayWasFinishedEvent -= Finished; Active = null; scenes = null; throw; }
            CutbackMenu.Status = "";
            Plugin.Log.Info($"Cutback review starts at {plan.InitialPosition:F3}s within {plan.Start:F3}s to {plan.End:F3}s");
            return true;
        }

        private static void Finished(StandardLevelScenesTransitionSetupDataSO setup, ReplayLaunchData data)
        {
            data.ReplayWasFinishedEvent -= Finished;
            Active = null;
            Clear();
            // BeatLeader's menu loader performs this pop after the launcher finishes.
            // Use the same scene lifecycle for a local launch through our menu.
            var manager = scenes;
            scenes = null;
            manager?.PopScenes(0.3f);
        }

        internal static void Attach(DiContainer container)
        {
            activeContainer = container;
            transport = container.Resolve<IReplayTimeController>();
            pause = container.Resolve<IReplayPauseController>();
            clock = container.Resolve<AudioTimeSyncController>();
            // The transport and note emulators are BeatLeader's installed 1.40-era engine.
            transport.SongWasRewoundEvent += Rewound;
            pause.PauseStateChangedEvent += PauseStateChanged;
        }

        internal static void Tick()
        {
            if (!IsLocalReview || transport == null || clock == null || !clock.isReady) return;
            if (!initialized)
            {
                initialized = true;
                transport.Rewind(Mathf.Clamp(plan.InitialPosition, transport.SongStartTime, plan.End), !pause.IsPaused);
                transport.SetSpeedMultiplier(SpeedAt(transport.SongTime), !pause.IsPaused);
            }
            if (atEnd && deathReview && Plugin.Settings.LoopDeathClip && Time.realtimeSinceStartup >= loopAt)
                ReplayAgain();
            if (transport.SongTime >= plan.End - 0.001f)
            {
                if (!atEnd) { atEnd = true; pause.Pause(); loopAt = Time.realtimeSinceStartup + 1f; }
                return;
            }
            if (!pause.IsPaused && AutoSlow)
            {
                float desired = SpeedAt(transport.SongTime);
                if (Math.Abs(desired - transport.SongSpeedMultiplier) > Math.Max(0.00001f, desired * 0.005f))
                    transport.SetSpeedMultiplier(desired);
            }
        }

        internal static float SpeedAt(float time) => plan?.SpeedAt(time, AutoSlow) ?? 1;
        private static void Jump(float time)
        {
            bool resume = !pause.IsPaused;
            transport.Rewind(Mathf.Clamp(time, transport.SongStartTime, plan.End), resume);
            transport.SetSpeedMultiplier(SpeedAt(transport.SongTime), resume);
            // Navigation preserves the viewer's existing pause state.
        }
        internal static void FocusMistake(float songTime)
        {
            if (!IsLocalReview || plan == null || transport == null || float.IsNaN(songTime) || float.IsInfinity(songTime)) return;
            plan.Focus(songTime);
            Plugin.Log.Info($"Cutback timeline mistake at {songTime:F3}s, lead-in at {plan.LeadIn(songTime):F3}s");
            Plugin.Guard(() => Jump(plan.LeadIn(songTime)));
        }
        private static void ReplayAgain()
        {
            Jump(plan.InitialPosition);
            pause.Resume();
        }
        private static void PauseStateChanged(bool paused)
        {
            // Native Play starts the selected review again when it reaches its captured
            // end. Scrubbing first clears atEnd and preserves the chosen position.
            if (!paused && atEnd) Plugin.Guard(() => Jump(plan.InitialPosition));
        }
        private static void Rewound(float _) { atEnd = false; }
        internal static void Detach(DiContainer container)
        {
            if (activeContainer == container) Clear();
        }
        private static void Clear()
        {
            if (transport != null) transport.SongWasRewoundEvent -= Rewound;
            if (pause != null) pause.PauseStateChangedEvent -= PauseStateChanged;
            transport = null;
            pause = null;
            clock = null;
            activeContainer = null;
            plan = null;
        }
    }
}
