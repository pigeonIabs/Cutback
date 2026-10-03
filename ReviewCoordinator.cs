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
        private static float startAt, reviewEnd, loopAt;
        private static bool deathReview;
        private static int controlsFrame;
        private static DiContainer activeContainer;
        private static GameScenesManager scenes;
        private static IReplayTimeController transport;
        private static IReplayPauseController pause;
        private static IReplayFinishController finish;
        private static AudioTimeSyncController clock;
        private static RectTransform controls;
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
            startAt = snapshot.Header.Start;
            deathReview = snapshot.Header.DeathTime.HasValue;
            reviewEnd = Math.Min(snapshot.Header.End, snapshot.Header.DeathTime ?? snapshot.Header.End);
            if (focusMistake)
            {
                float focus = snapshot.Header.DeathTime ?? snapshot.Header.Mistakes.LastOrDefault();
                if (focus > snapshot.Header.Start) startAt = Math.Max(snapshot.Header.Start, focus - Mathf.Clamp(Plugin.Settings.ReviewLeadSeconds, 1f, 15f));
            }
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
            finish = container.Resolve<IReplayFinishController>();
            clock = container.Resolve<AudioTimeSyncController>();
            // The transport and note emulators are BeatLeader's installed 1.40-era engine.
            transport.SongWasRewoundEvent += Rewound;
        }

        internal static void Tick()
        {
            if (!IsLocalReview || transport == null || clock == null || !clock.isReady) return;
            if (!initialized)
            {
                initialized = true;
                transport.Rewind(Mathf.Clamp(startAt, transport.SongStartTime, Active.Header.End), !pause.IsPaused);
                transport.SetSpeedMultiplier(SpeedAt(transport.SongTime), !pause.IsPaused);
                controlsFrame = Time.frameCount + 3;
            }
            // Let BeatLeader apply its first recorded head pose before placing world UI.
            if (controls == null && Time.frameCount >= controlsFrame)
            {
                controls = CutbackUI.CreateReplayControls(clock, ToggleSlow, PreviousMistake, NextMistake, ReplayAgain);
            }
            if (atEnd && deathReview && Plugin.Settings.LoopDeathClip && Time.realtimeSinceStartup >= loopAt)
                ReplayAgain();
            if (transport.SongTime >= reviewEnd - 0.001f)
            {
                if (!atEnd) { atEnd = true; pause.Pause(); loopAt = Time.realtimeSinceStartup + 1f; }
                CutbackUI.UpdatePlayback(transport.SongSpeedMultiplier, true);
                return;
            }
            if (!pause.IsPaused && AutoSlow)
            {
                float desired = SpeedAt(transport.SongTime);
                if (Math.Abs(desired - transport.SongSpeedMultiplier) > 0.005f) transport.SetSpeedMultiplier(desired);
            }
            CutbackUI.UpdatePlayback(transport.SongSpeedMultiplier, pause.IsPaused);
        }

        internal static float SpeedAt(float time)
        {
            if (!AutoSlow || Active == null) return 1f;
            float slow = Mathf.Clamp(Plugin.Settings.MistakeSpeedPercent / 100f, 0.05f, 0.5f);
            float lead = Mathf.Clamp(Plugin.Settings.SlowdownSeconds, 0.5f, 10f);
            if (deathReview) return Ramp(time, Active.Header.DeathTime.Value, lead, slow);
            float speed = 1f;
            foreach (float mistake in Active.Header.Mistakes)
            {
                if (mistake > time + lead) break;
                if (time <= mistake) speed = Math.Min(speed, Ramp(time, mistake, lead, slow));
                else if (time <= mistake + Mathf.Clamp(Plugin.Settings.TailSeconds, 0.1f, 5f)) speed = Math.Min(speed, slow);
            }
            return speed;
        }

        private static float Ramp(float time, float target, float lead, float slow)
        {
            float progress = Mathf.Clamp01((time - (target - lead)) / lead);
            return Mathf.Pow(slow, progress);
        }

        private static void ToggleSlow()
        {
            AutoSlow = !AutoSlow;
            transport.SetSpeedMultiplier(AutoSlow ? SpeedAt(transport.SongTime) : 1);
            CutbackUI.UpdateSlowLabel(AutoSlow);
        }
        private static void Jump(float time)
        {
            bool resume = !pause.IsPaused;
            transport.Rewind(Mathf.Clamp(time, transport.SongStartTime, Active.Header.End), resume);
            transport.SetSpeedMultiplier(SpeedAt(transport.SongTime), resume);
            // Navigation preserves the viewer's existing pause state.
        }
        private static void PreviousMistake()
        {
            float lead = Math.Max(1, Plugin.Settings.ReviewLeadSeconds);
            float mistake = Active.Header.Mistakes.LastOrDefault(t => t - lead < transport.SongTime - 0.5f);
            Jump(Math.Max(Active.Header.Start, mistake - lead));
        }
        private static void NextMistake()
        {
            float lead = Math.Max(1, Plugin.Settings.ReviewLeadSeconds);
            float? mistake = Active.Header.Mistakes.Where(t => t - lead > transport.SongTime + 0.5f).Select(t => (float?)t).FirstOrDefault();
            if (mistake.HasValue) Jump(Math.Max(Active.Header.Start, mistake.Value - lead));
        }
        private static void ReplayAgain()
        {
            Jump(startAt);
            pause.Resume();
        }
        internal static void TogglePause()
        {
            if (pause.IsPaused) { if (atEnd) ReplayAgain(); else pause.Resume(); }
            else pause.Pause();
        }
        internal static void Exit() => finish?.Exit();
        internal static void SeekRelative(float seconds) => Jump(transport.SongTime + seconds);
        private static void Rewound(float _) { atEnd = false; }
        internal static void Detach(DiContainer container)
        {
            if (activeContainer == container) Clear();
        }
        private static void Clear()
        {
            if (transport != null) transport.SongWasRewoundEvent -= Rewound;
            if (controls != null) UnityEngine.Object.Destroy(controls.gameObject);
            transport = null;
            pause = null;
            finish = null;
            clock = null;
            activeContainer = null;
            controls = null;
        }
    }
}
