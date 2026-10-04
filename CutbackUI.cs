// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using BeatSaberMarkupLanguage;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UTransform = UnityEngine.Transform;

namespace Cutback
{
    internal static class CutbackUI
    {
        private static readonly Color Accent = new Color(0.12f, 0.55f, 0.7f, 1);

        internal static RectTransform Root(UTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        internal static TextMeshProUGUI Text(UTransform parent, string value, float x, float y, float width, float height, float font)
        {
            var text = BeatSaberUI.CreateText(parent as RectTransform, value, new Vector2(x, y));
            text.rectTransform.sizeDelta = new Vector2(width, height);
            text.fontSize = font;
            text.alignment = TextAlignmentOptions.Midline;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        internal static Button Button(UTransform parent, string label, float x, float y, float width, float height, Action action, float font = 3.1f, bool filled = true)
        {
            var rect = Root(parent, "ForgeButton", new Vector2(width, height));
            rect.anchoredPosition = new Vector2(x, y);
            var image = rect.gameObject.AddComponent<ImageView>();
            image.sprite = Utilities.ImageResources.BlankSprite;
            image.gradient = true;
            image.color0 = new Color(0.03f, 0.12f, 0.19f, filled ? 0.85f : 0.35f);
            image.color1 = new Color(0.08f, 0.28f, 0.34f, filled ? 0.85f : 0.35f);
            image.color = Color.white;
            image.raycastTarget = false;
            image.material = Utilities.ImageResources.NoGlowMat;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.5f, 2.1f, 2.3f, 1);
            colors.pressedColor = Accent;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Plugin.Guard(action));
            Line(rect, -width / 2 + 0.3f, 0, 0.6f, height, Accent);
            Text(rect, label, 0, 0, width - 3, height - 1, font);
            Root(rect, "ForgeHitArea", new Vector2(width, height)).gameObject.AddComponent<Touchable>();
            return button;
        }

        private static void Line(UTransform parent, float x, float y, float width, float height, Color color)
        {
            var rect = Root(parent, "ForgeAccent", new Vector2(width, height));
            rect.anchoredPosition = new Vector2(x, y);
            var image = rect.gameObject.AddComponent<Image>();
            image.material = Utilities.ImageResources.NoGlowMat;
            image.color = color;
            image.raycastTarget = false;
        }

        internal static void SquareMenuEntry()
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (text.text != "Cutback" || !text.gameObject.activeInHierarchy) continue;
                var button = text.GetComponentInParent<Button>();
                if (button == null || button.GetComponent<CutbackSquareMarker>() != null) continue;
                button.gameObject.AddComponent<CutbackSquareMarker>();
                foreach (var image in button.GetComponentsInChildren<Image>(true)) image.enabled = false;
                var rect = Root(button.transform, "ForgeFlatBackground", (button.transform as RectTransform).rect.size);
                rect.SetAsFirstSibling();
                var flat = rect.gameObject.AddComponent<ImageView>();
                flat.sprite = Utilities.ImageResources.BlankSprite;
                flat.material = Utilities.ImageResources.NoGlowMat;
                flat.gradient = true;
                flat.color0 = new Color(0.03f, 0.12f, 0.19f, 0.9f);
                flat.color1 = new Color(0.08f, 0.28f, 0.34f, 0.9f);
                flat.color = Color.white;
                button.targetGraphic = flat;
            }
        }
    }
    public sealed class CutbackSquareMarker : MonoBehaviour { }
}
