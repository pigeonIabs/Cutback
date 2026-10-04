// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using BeatSaberMarkupLanguage;
using HMUI;
using UnityEngine;
using UnityEngine.UI;

namespace Cutback
{
    internal static class PauseReplayButton
    {
        private const float Width = 12;
        private const float Height = 10;
        private const float RowGap = 6;

        internal static RectTransform Create(PauseMenuManager menu, Action review)
        {
            var back = Reflect.Get<Button>(menu, "_backButton");
            var parent = (RectTransform)back.transform.parent;
            var bounds = RowBounds(menu, parent);
            var rootObject = new GameObject("CutbackPauseReplay", typeof(RectTransform));
            rootObject.layer = 5;
            var root = (RectTransform)rootObject.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(Width, Height);
            root.anchoredPosition = new Vector2(
                (bounds.MinX + bounds.MaxX) * 0.5f - parent.rect.center.x,
                bounds.MaxY + RowGap + Height * 0.5f - parent.rect.center.y);
            rootObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var icon = rootObject.AddComponent<ImageView>();
            icon.sprite = BeatLeader.BundleLoader.ReplayIcon;
            icon.preserveAspect = true;
            icon.material = Utilities.ImageResources.NoGlowMat;
            icon.raycastTarget = true;

            var button = rootObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = icon;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Plugin.Guard(review));

            BeatSaberUI.DiContainer.InstantiateComponent<HoverHint>(rootObject).text = "Review recent attempt";
            return root;
        }

        internal static void SetMessage(RectTransform root, string message)
        {
            if (root != null) root.GetComponent<HoverHint>().text = message;
        }

        private static Bounds2D RowBounds(PauseMenuManager menu, RectTransform parent)
        {
            var buttons = new[]
            {
                Reflect.Get<Button>(menu, "_continueButton"),
                Reflect.Get<Button>(menu, "_restartButton"),
                Reflect.Get<Button>(menu, "_backButton")
            };
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            var corners = new Vector3[4];
            foreach (var button in buttons)
            {
                // The menu's animator activates its parent after the pause event.
                // Measure the enabled buttons even during that first hidden frame.
                if (button == null || !button.gameObject.activeSelf) continue;
                var rect = button.transform as RectTransform;
                if (rect == null) continue;
                rect.GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; i++)
                {
                    var point = parent.InverseTransformPoint(corners[i]);
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }
            return new Bounds2D(minX, maxX, maxY);
        }

        private readonly struct Bounds2D
        {
            internal readonly float MinX;
            internal readonly float MaxX;
            internal readonly float MaxY;

            internal Bounds2D(float minX, float maxX, float maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MaxY = maxY;
            }
        }
    }
}
