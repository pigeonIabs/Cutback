// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Cutback
{
    [HarmonyPatch]
    internal static class ResultsReplayRouting
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method("BeatLeader.ViewControllers.ResultsScreenUI:HandleReplayButtonClicked");

        private static bool Prefix(object __instance)
        {
            if (!TryGetFailedLocalResult(__instance, out _)) return true;
            var snapshot = AttemptSession.FailedResultReplay;
            if (snapshot == null) return true;
            ReviewCoordinator.Queue(snapshot, true);
            return false;
        }

        internal static bool TryGetFailedLocalResult(object instance, out BeatmapKey key)
        {
            key = default(BeatmapKey);
            var component = instance as Component;
            var results = component == null ? null : component.GetComponentInParent<ResultsViewController>();
            if (results == null) return false;

            var completion = Reflect.Get<LevelCompletionResults>(results, "_levelCompletionResults");
            if (completion == null || completion.levelEndStateType != LevelCompletionResults.LevelEndStateType.Failed)
                return false;

            key = Reflect.Get<BeatmapKey>(results, "_beatmapKey");
            return AttemptSession.HasFailedResult(key);
        }
    }

    [HarmonyPatch]
    internal static class FailedLocalReplayButtonAvailability
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method("BeatLeader.ViewControllers.ResultsScreenUI:Refresh");

        private static void Postfix(object __instance)
        {
            if (!ResultsReplayRouting.TryGetFailedLocalResult(__instance, out _)) return;
            var component = (Component)__instance;
            var replayButton = Reflect.Get<object>(component, "_replayButton");
            Reflect.Get<Button>(replayButton, "_button").interactable = true;
        }
    }
}
