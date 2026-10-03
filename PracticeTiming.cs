// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Runtime.CompilerServices;
using BeatLeader.Replayer;
using HarmonyLib;
using HMUI;
using UnityEngine;
using Zenject;

namespace Cutback
{
    [HarmonyPatch(typeof(PracticeViewController), "Init")]
    internal static class PracticeRange
    {
        // Init clamps the persisted speed before DidActivate runs on 1.40.8.
        private static void Prefix(PercentSlider ____speedSlider)
        {
            ____speedSlider.minValue = 0.05f;
            ____speedSlider.maxValue = 2f;
            ____speedSlider.numberOfSteps = 40;
        }
    }

    [HarmonyPatch(typeof(PracticeViewController), "DidActivate")]
    internal static class PracticeRangeRefresh
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PercentSlider ____speedSlider, PracticeSettings ____practiceSettings)
        {
            ____speedSlider.minValue = 0.05f;
            ____speedSlider.maxValue = 2f;
            ____speedSlider.numberOfSteps = 40;
            ____speedSlider.value = Mathf.Clamp(____practiceSettings.songSpeedMul, 0.05f, 2f);
            ____practiceSettings.songSpeedMul = ____speedSlider.value;
        }
    }

    internal static class PracticeClock
    {
        internal static readonly ConditionalWeakTable<AudioTimeSyncController.InitData, PracticeSettings> Starts = new ConditionalWeakTable<AudioTimeSyncController.InitData, PracticeSettings>();
        internal static bool Applies(AudioTimeSyncController audio) => audio.timeScale < 0.5f || ReviewCoordinator.IsLocalReview;
        internal static void Rebase(AudioTimeSyncController audio)
        {
            var source = Reflect.Get<AudioSource>(audio, "_audioSource");
            float time = audio.songTime + audio.songTimeOffset + Reflect.Get<float>(audio, "_audioLatency");
            Reflect.Set(audio, "_audioStartTimeOffsetSinceStart", Time.timeSinceLevelLoad * audio.timeScale - time);
            Reflect.Set(audio, "_prevAudioSamplePos", source.timeSamples);
            Reflect.Set(audio, "_playbackLoopIndex", 0);
            Reflect.Set(audio, "_fixingAudioSyncError", false);
            Reflect.Set(audio, "_inBetweenDSPBufferingTimeEstimate", 0f);
            Reflect.Set(audio, "_dspTimeOffset", AudioSettings.dspTime - time / Math.Max(0.05f, audio.timeScale));
        }
        internal static void Pitch(AudioManagerSO mixer, float scale)
        {
            if (mixer != null && scale < 0.5f) mixer.musicPitch = 2f;
        }
    }

    [HarmonyPatch(typeof(GameplayCoreInstaller), "InstallBindings")]
    internal static class PracticeInit
    {
        private static void Postfix(GameplayCoreInstaller __instance, GameplayCoreSceneSetupData ____sceneSetupData)
        {
            if (____sceneSetupData.practiceSettings == null) return;
            var container = Reflect.Get<DiContainer>(__instance, "<Container>k__BackingField");
            var data = container.Resolve<AudioTimeSyncController.InitData>();
            PracticeClock.Starts.Remove(data);
            PracticeClock.Starts.Add(data, ____sceneSetupData.practiceSettings);
        }
    }

    [HarmonyPatch(typeof(AudioTimeSyncController), "Start")]
    internal static class ClockStart
    {
        private static void Prefix(AudioTimeSyncController.InitData ____initData)
        {
            if (____initData.timeScale >= 0.5f || !PracticeClock.Starts.TryGetValue(____initData, out var settings)) return;
            // Stock advances one song second, which becomes twenty real seconds at 5 percent.
            if (settings.startInAdvanceAndClearNotes)
                Reflect.Set(____initData, "startSongTime", Math.Max(0f, settings.startSongTime - ____initData.timeScale));
        }

        private static void Postfix(AudioTimeSyncController __instance, AudioTimeSyncController.InitData ____initData, ref float ____songTime)
        {
            if (PracticeClock.Starts.TryGetValue(____initData, out _) || ReviewCoordinator.IsLocalReview)
            {
                // Objects are prewarmed one frame BEFORE StartSong. Give them the correct clock.
                ____songTime = __instance.startSongTime;
                foreach (var mixer in Resources.FindObjectsOfTypeAll<AudioManagerSO>()) PracticeClock.Pitch(mixer, __instance.timeScale);
            }
        }
    }

    [HarmonyPatch(typeof(AudioTimeSyncController), "Update")]
    internal static class ClockUpdate
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(AudioTimeSyncController __instance, AudioSource ____audioSource,
            ref int ____prevAudioSamplePos, ref int ____playbackLoopIndex, ref float ____lastFrameDeltaSongTime)
        {
            if (!PracticeClock.Applies(__instance)) return true;
            if (__instance.state == AudioTimeSyncController.State.Paused)
            {
                ____lastFrameDeltaSongTime = 0;
                return false;
            }
            if (____audioSource != null && !____audioSource.loop)
            {
                // A seek or audio-device resync is not an entire song loop.
                ____playbackLoopIndex = 0;
                if (____prevAudioSamplePos > ____audioSource.timeSamples)
                    ____prevAudioSamplePos = ____audioSource.timeSamples;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AudioTimeSyncController), "SeekTo")]
    internal static class ClockSeek
    {
        private static void Postfix(AudioTimeSyncController __instance)
        {
            if (PracticeClock.Applies(__instance)) PracticeClock.Rebase(__instance);
        }
    }

    [HarmonyPatch]
    internal static class ReplaySpeedRebase
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("BeatLeader.Replayer.BeatmapTimeController"), "SetSpeedMultiplier");
        private static bool Prefix(object __instance, float speedMultiplier, AudioTimeSyncController ____audioTimeSyncController, AudioManagerSO ____audioManagerSO)
        {
            if (!ReviewCoordinator.IsLocalReview) return true;
            float speed = Mathf.Clamp(speedMultiplier, 0.05f, 2f);
            if (Math.Abs(speed - ____audioTimeSyncController.timeScale) < 0.001f) return false;
            // Keep audio running while ramping. Rebase the clock at the current pose time.
            Reflect.Set(____audioTimeSyncController, "_timeScale", speed);
            Reflect.Get<AudioSource>(____audioTimeSyncController, "_audioSource").pitch = speed;
            if (____audioManagerSO != null) ____audioManagerSO.musicPitch = Math.Min(2f, 1f / speed);
            PracticeClock.Rebase(____audioTimeSyncController);
            var changed = Reflect.Get<Action<float>>(__instance, "SongSpeedWasChangedEvent");
            changed?.Invoke(speed);
            return false;
        }
        private static void Postfix(AudioTimeSyncController ____audioTimeSyncController, AudioManagerSO ____audioManagerSO)
        {
            if (!ReviewCoordinator.IsLocalReview) return;
            PracticeClock.Rebase(____audioTimeSyncController);
            PracticeClock.Pitch(____audioManagerSO, ____audioTimeSyncController.timeScale);
        }
    }

    internal static class PracticeCompatibility
    {
        internal static void PracticeUIPostfix(object __instance)
        {
            var setup = Reflect.Get<GameplayCoreSceneSetupData>(__instance, "_gameplayCoreSceneSetupData");
            int initialSpeed = Mathf.RoundToInt((setup.practiceSettings?.songSpeedMul ?? setup.gameplayModifiers.songSpeedMul) * 100);
            // PP parses the controls into PauseMenu's canvas, while its host view
            // controller lives on a separate object.
            var menu = GameObject.Find("PauseMenu");
            if (menu == null) return;
            foreach (var setting in menu.GetComponentsInChildren<BeatSaberMarkupLanguage.Components.Settings.IncrementSetting>(true))
            {
                if (setting.MinValue == 10 && setting.MaxValue == 500 && setting.Increments == 5)
                {
                    setting.MinValue = 5;
                    AccessTools.Property(__instance.GetType(), "Speed").SetValue(__instance, initialSpeed);
                    setting.Value = initialSpeed;
                }
            }
        }
        // BeatLeader provides its own replay transport. PP's live seek and loop controllers
        // would also initialize because BeatLeader uses PracticeSettings for partial replays.
        internal static bool InstallPrefix() => !ReviewCoordinator.IsLocalReview;
        internal static void SpeedPostfix(AudioTimeSyncController ____audioTimeSyncController)
        {
            if (____audioTimeSyncController != null) PracticeClock.Rebase(____audioTimeSyncController);
        }
        internal static void PitchPostfix(AudioManagerSO ____mixer, float pitch) => PracticeClock.Pitch(____mixer, pitch);

        internal static void StartChangedPostfix(float newSongTime, AudioTimeSyncController ____audioTimeSyncController,
            BeatmapCallbacksController ____beatmapCallbacksController)
        {
            var data = Reflect.Get<AudioTimeSyncController.InitData>(____audioTimeSyncController, "_initData");
            PracticeClock.Starts.Remove(data);
            PracticeClock.Starts.Add(data, new PracticeSettings(newSongTime, data.timeScale));
            Reflect.Set(____beatmapCallbacksController, "_startFilterTime", newSongTime);
            Plugin.Guard(() => AttemptSession.Current?.ScheduledStart(newSongTime));
        }

        internal static void SeekPrefix(bool ____failed)
        {
            if (!____failed) Plugin.Guard(() => AttemptSession.Current?.BeforeSeek());
        }

        internal static void SeekPostfix(object __instance, float newSongTime, bool ____failed,
            AudioTimeSyncController ____audioTimeSyncController, BeatmapCallbacksController ____beatmapCallbacksController,
            object ____noodleObjectsCallbacksManager)
        {
            if (____failed) return;
            // Reuse PP's despawning and extension cleanup, repair the mismatched clocks/filter.
            float lead = Math.Min(newSongTime, Math.Max(0.05f, ____audioTimeSyncController.timeScale));
            float start = Math.Max(0, newSongTime - lead);
            var source = Reflect.Get<AudioSource>(____audioTimeSyncController, "_audioSource");
            float offset = ____audioTimeSyncController.songTimeOffset + Reflect.Get<float>(____audioTimeSyncController, "_audioLatency");
            source.time = Mathf.Clamp(start + offset, 0, source.clip.length - 0.01f);
            Reflect.Set(____audioTimeSyncController, "_songTime", start);
            Reflect.Set(____beatmapCallbacksController, "_startFilterTime", newSongTime);
            Reflect.Set(____beatmapCallbacksController, "_prevSongTime", float.MinValue);
            if (____noodleObjectsCallbacksManager != null)
            {
                Reflect.Set(____noodleObjectsCallbacksManager, "_startFilterTime", newSongTime);
                Reflect.Set(____noodleObjectsCallbacksManager, "_prevSongtime", float.MinValue);
            }
            PracticeClock.Rebase(____audioTimeSyncController);
            Plugin.Guard(() => AttemptSession.Current?.AfterSeek(newSongTime));
        }
    }
}
