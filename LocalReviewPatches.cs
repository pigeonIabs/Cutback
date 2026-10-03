// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Cutback
{
    [HarmonyPatch]
    internal static class LocalReviewUI
    {
        private static readonly ConditionalWeakTable<object, object> Replaced = new ConditionalWeakTable<object, object>();
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("BeatLeader.UI.ReplayerUIBinder");
            yield return AccessTools.Method(type, "Start");
            yield return AccessTools.Method(type, "OnDisable");
        }
        // Local review supplies a compact rectangular transport. The normal BeatLeader
        // viewer keeps its existing interface for every other replay launch.
        private static bool Prefix(object __instance, MethodBase __originalMethod)
        {
            if (__originalMethod.Name == "Start" && ReviewCoordinator.IsLocalReview)
                Replaced.GetOrCreateValue(__instance);
            return !Replaced.TryGetValue(__instance, out _);
        }
    }

    [HarmonyPatch]
    internal static class LocalReviewLayoutShortcut
    {
        private static MethodBase TargetMethod() => AccessTools.Method("BeatLeader.Replayer.Binding.PartialDisplayModeHotkey:OnKeyDown");
        private static bool Prefix() => !ReviewCoordinator.IsLocalReview;
    }

    [HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator), "HandleBasicLevelCompletionResults")]
    internal static class DeathReviewResults
    {
        private static void Postfix(LevelCompletionResults levelCompletionResults, ref bool __result)
        {
            if (levelCompletionResults.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed &&
                ReviewCoordinator.Pending?.Header.DeathTime != null) __result = true;
        }
    }
}
