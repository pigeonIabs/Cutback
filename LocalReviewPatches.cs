// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using HarmonyLib;

namespace Cutback
{
    [HarmonyPatch]
    internal static class LocalReplayModifiers
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method("BeatLeader.Replayer.Tweaking.ModifiersTweak:Initialize");

        private static bool Prefix()
        {
            if (!ReviewCoordinator.IsLocalReview) return true;
            var values = ReviewCoordinator.Active.Header.ModifierValues;
            if (values.HasValue) BeatLeader.ModifiersMapManager.LoadCustomModifiersMap(values.Value);
            else BeatLeader.ModifiersMapManager.LoadGameplayModifiersMap();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class LocalReplayBounds
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.PropertyGetter(AccessTools.TypeByName("BeatLeader.Replayer.ReplayTimeController"), "ReplayEndTime");

        private static void Postfix(ref float __result)
        {
            // BeatLeader extends No Fail replays to the song's end. Local captures
            // have their own end, shared by the native timeline and transport.
            if (ReviewCoordinator.IsLocalReview) __result = ReviewCoordinator.Active.Header.End;
        }
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
