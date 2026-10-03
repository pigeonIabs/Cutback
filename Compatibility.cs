// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Reflection;
using BeatLeader;
using HarmonyLib;

namespace Cutback
{
    internal static class Compatibility
    {
        internal static void Verify()
        {
            foreach (string field in new[] { "_replay", "_timeSyncController", "_stopRecording", "_noteEventCache", "_noteIdCache" })
                Need(AccessTools.Field(typeof(ReplayRecorder), field), field);
            foreach (string field in new[] { "_songTime", "_startSongTime", "_timeScale", "_audioStartTimeOffsetSinceStart", "_prevAudioSamplePos", "_audioSource", "_playbackLoopIndex", "_audioLatency", "_initData", "_inBetweenDSPBufferingTimeEstimate" })
                Need(AccessTools.Field(typeof(AudioTimeSyncController), field), field);
            Need(AccessTools.Method(typeof(ReplayRecorder), "FinalizeReplay"), "FinalizeReplay");
            Need(AccessTools.Method(typeof(ReplayRecorder), "OnNoteWasCut"), "OnNoteWasCut");
            Need(AccessTools.TypeByName("BeatLeader.Replayer.BeatmapTimeController"), "BeatLeader time controller");
            Need(AccessTools.Method("BeatLeader.Utils.RecorderUtils:OnRestartPauseButtonWasPressed"), "BeatLeader restart recorder gate");
        }

        private static void Need(MemberInfo member, string name)
        {
            if (member == null) throw new MissingMemberException("Incompatible game or BeatLeader assembly", name);
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            Patch(harmony, "PracticePlugin.Installers.PlayerInstaller", "InstallBindings", nameof(PracticeCompatibility.InstallPrefix), null);
            Patch(harmony, "PracticePlugin.Models.AudioSpeedController", "SetTimeScale", null, nameof(PracticeCompatibility.SpeedPostfix));
            Patch(harmony, "PracticePlugin.Models.AudioSpeedController", "ChangeMusicPitch", null, nameof(PracticeCompatibility.PitchPostfix));
            Patch(harmony, "PracticePlugin.Models.SongSeekBeatmapHandler", "OnSongTimeChanged", nameof(PracticeCompatibility.SeekPrefix), nameof(PracticeCompatibility.SeekPostfix));
            Patch(harmony, "PracticePlugin.Models.SongSeekBeatmapHandler", "ChangeSongStartTime", null, nameof(PracticeCompatibility.StartChangedPostfix));
            Patch(harmony, "PracticePlugin.Views.PracticeUI", "PostParse", null, nameof(PracticeCompatibility.PracticeUIPostfix));
        }

        private static void Patch(Harmony harmony, string typeName, string method, string prefix, string postfix)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) return;
            MethodInfo target = AccessTools.Method(type, method);
            Need(target, typeName + "." + method);
            harmony.Patch(target,
                prefix == null ? null : new HarmonyMethod(typeof(PracticeCompatibility), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(PracticeCompatibility), postfix));
        }
    }
}
