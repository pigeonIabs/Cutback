// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Cutback
{
    [HarmonyPatch]
    internal static class TimelineMarkerNavigation
    {
        private const string TimelineTypeName = "BeatLeader.Components.Timeline";
        private const string MissMarkerName = "MissMark";
        private static readonly FieldInfo MarksField = AccessTools.Field(
            AccessTools.TypeByName(TimelineTypeName), "_marks");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName(TimelineTypeName),
                "GenerateMarkers",
                new[] { typeof(IEnumerable<float>), typeof(GameObject) });
        }

        private static void Prefix(ref IEnumerable<float> times, GameObject prefab, out float[] __state)
        {
            __state = null;
            if (!ReviewCoordinator.IsLocalReview || prefab == null || prefab.name != MissMarkerName)
                return;

            __state = times.ToArray();
            times = __state;
        }

        private static void Postfix(object __instance, GameObject prefab, float[] __state)
        {
            if (__state == null || !ReviewCoordinator.IsLocalReview || prefab == null || prefab.name != MissMarkerName)
                return;

            var marks = MarksField.GetValue(__instance) as Dictionary<string, List<GameObject>>;
            if (marks == null || !marks.TryGetValue(MissMarkerName, out var instances) || instances.Count != __state.Length)
                return;

            for (int i = 0; i < instances.Count; i++)
            {
                var marker = instances[i];
                if (marker == null) continue;

                var image = marker.GetComponent<Image>();
                var button = marker.GetComponent<Button>();
                if (image == null || button != null) continue;

                image.raycastTarget = true;
                button = marker.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };

                float songTime = __state[i];
                button.onClick.AddListener(() =>
                {
                    if (ReviewCoordinator.IsLocalReview)
                        ReviewCoordinator.FocusMistake(songTime);
                });
            }
        }
    }
}
