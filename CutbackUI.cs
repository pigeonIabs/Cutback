// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Linq;
using BeatLeader.Replayer;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UTransform = UnityEngine.Transform;

namespace Cutback
{
    internal static class CutbackUI
    {
        private static TextMeshProUGUI slowLabel;
        private static TextMeshProUGUI playbackLabel, pauseLabel;
        private static readonly Color Surface = new Color(0.07f, 0.09f, 0.13f, 0.96f);
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

        internal static RectTransform CreatePauseReview(PauseMenuManager menu, Action review)
        {
            var back = Reflect.Get<Button>(menu, "_backButton");
            var root = Root(back.transform.parent, "CutbackPause", new Vector2(40, 9));
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            // Fit four actions in the native row, clear of the live seek controls.
            var native = new[] { back, Reflect.Get<Button>(menu, "_restartButton"), Reflect.Get<Button>(menu, "_continueButton") };
            float y = (back.transform as RectTransform).anchoredPosition.y;
            for (int i = 0; i < native.Length; i++)
            {
                if (native[i] == null) continue;
                var rect = native[i].transform as RectTransform;
                rect.sizeDelta = new Vector2(36, rect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(-57 + i * 38, y);
            }
            root.anchoredPosition = new Vector2(57, y);
            var button = Button(root, "Replay", 0, 0, 36, 9, review);
            pauseLabel = button.GetComponentInChildren<TextMeshProUGUI>();
            return root;
        }

        internal static void PauseMessage(RectTransform root, string message)
        {
            if (root != null && pauseLabel != null) pauseLabel.text = message;
        }

        internal static RectTransform CreateReplayControls(AudioTimeSyncController clock, Action toggle, Action previous, Action next, Action again)
        {
            var screen = FloatingScreen.CreateFloatingScreen(new Vector2(100, 34), false, new Vector3(0, 1.4f, 1.7f), Quaternion.Euler(10, 0, 0));
            screen.gameObject.name = "CutbackReview";
            var camera = UnityEngine.Object.FindObjectOfType<ReplayerCameraController>()?.ViewableCamera?.Camera ?? Camera.main;
            if (camera == null) camera = Camera.allCameras.FirstOrDefault(c => c.name == "SmoothCamera");
            if (camera != null)
            {
                screen.transform.position = camera.transform.TransformPoint(new Vector3(0, -0.35f, 1.7f));
                screen.transform.rotation = camera.transform.rotation * Quaternion.Euler(10, 0, 0);
            }
            var root = screen.transform as RectTransform;
            Line(root, 0, 16, 98, 0.4f, Accent);
            // The original BeatLeader VR view also rebinds this raycaster in gameplay.
            var raycaster = screen.GetComponent<VRUIControls.VRGraphicRaycaster>();
            Reflect.Set(raycaster, "_physicsRaycaster", BeatSaberUI.PhysicsRaycasterWithCache);
            var play = Button(root, "Pause", -33, 10, 30, 8, ReviewCoordinator.TogglePause);
            playbackLabel = play.GetComponentInChildren<TextMeshProUGUI>();
            Button(root, "Watch again", 0, 10, 30, 8, again);
            Button(root, "Exit review", 33, 10, 30, 8, ReviewCoordinator.Exit);
            Button(root, "Previous mistake", -25, 0, 46, 8, previous);
            Button(root, "Next mistake", 25, 0, 46, 8, next);
            var slow = Button(root, "", 0, -10, 46, 8, toggle);
            slowLabel = slow.GetComponentInChildren<TextMeshProUGUI>();
            UpdateSlowLabel(ReviewCoordinator.AutoSlow);
            Button(root, "-5 seconds", -37, -10, 23, 8, () => ReviewCoordinator.SeekRelative(-5), 2.8f);
            Button(root, "+5 seconds", 37, -10, 23, 8, () => ReviewCoordinator.SeekRelative(5), 2.8f);
            return root;
        }

        internal static void UpdatePlayback(float speed, bool paused)
        {
            if (playbackLabel != null) playbackLabel.text = paused ? "Play" : $"Pause   {speed * 100:F0}%";
        }

        internal static void UpdateSlowLabel(bool on)
        {
            if (slowLabel != null) slowLabel.text = "Auto slow motion " + (on ? "on" : "off");
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
