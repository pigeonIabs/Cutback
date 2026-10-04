// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Util;
using UnityEngine;
using Zenject;

namespace Cutback
{
    public sealed class CutbackSettingsPanel : IInitializable, IDisposable
    {
        [UIComponent("settings-root")] private RectTransform host;
        private RectTransform content;

        public void Initialize() => ZenjectSingleton<GameplaySetup>.Instance.AddTab("Cutback", "Cutback.Settings.bsml", this);
        public void Dispose() => ZenjectSingleton<GameplaySetup>.Instance.RemoveTab("Cutback");

        [UIAction("#post-parse")]
        private void Build()
        {
            content = CutbackUI.Root(host, "RecentReplaySettings", new Vector2(110, 60));
            var layout = content.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredWidth = 110;
            layout.preferredHeight = 60;
            Toggle("Replay on death", 26, () => Plugin.Settings.ReplayOnDeath, v => Plugin.Settings.ReplayOnDeath = v);
            Toggle("Focus missed notes", 19.5f, () => Plugin.Settings.ReviewMissedNotes, v => Plugin.Settings.ReviewMissedNotes = v);
            Toggle("Focus bad cuts", 13, () => Plugin.Settings.ReviewBadCuts, v => Plugin.Settings.ReviewBadCuts = v);
            Toggle("Automatic slow motion", 6.5f, () => Plugin.Settings.AutomaticSlowMotion, v => Plugin.Settings.AutomaticSlowMotion = v);
            Number("Start before mistake", 0, () => Plugin.Settings.ReviewLeadSeconds, v => Plugin.Settings.ReviewLeadSeconds = v, 1, 1, 15, "s");
            Number("Slowdown duration", -6.5f, () => Plugin.Settings.SlowdownSeconds, v => Plugin.Settings.SlowdownSeconds = v, 0.5f, 0.5f, 10, "s");
            Number("Minimum speed", -13, () => Plugin.Settings.MistakeSpeedPercent, v => Plugin.Settings.MistakeSpeedPercent = v, 5, 5, 50, "%");
            Number("Replace replay after", -19.5f, () => Plugin.Settings.ReplayReplacementSeconds, v => Plugin.Settings.ReplayReplacementSeconds = v, 1, 0, 120, "s");
            Toggle("Loop death clip", -26, () => Plugin.Settings.LoopDeathClip, v => Plugin.Settings.LoopDeathClip = v);
        }

        private void Toggle(string title, float y, Func<bool> get, Action<bool> set)
        {
            Label(title, y);
            TMPro.TextMeshProUGUI value = null;
            var button = CutbackUI.Button(content, get() ? "On" : "Off", 38, y, 28, 6,
                () => { set(!get()); value.text = get() ? "On" : "Off"; }, 2.6f);
            value = button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        }

        private void Number(string title, float y, Func<float> get, Action<float> set, float step, float min, float max, string unit)
        {
            Label(title, y);
            var value = CutbackUI.Text(content, Format(get(), unit), 36, y, 19, 6, 2.6f);
            Action<float> change = delta => { set(Mathf.Clamp(get() + delta, min, max)); value.text = Format(get(), unit); };
            CutbackUI.Button(content, "−", 22, y, 8, 6, () => change(-step), 2.6f);
            CutbackUI.Button(content, "+", 50, y, 8, 6, () => change(step), 2.6f);
        }
        private void Label(string title, float y)
        {
            var label = CutbackUI.Text(content, title, -19, y, 65, 6, 2.6f);
            label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        }
        private static string Format(float value, string unit) => value.ToString("0.#") + unit;
    }
}
