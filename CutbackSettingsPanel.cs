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
            content = CutbackUI.Root(host, "RecentReplaySettings", new Vector2(110, 74));
            var layout = content.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredWidth = 110;
            layout.preferredHeight = 74;
            Toggle("Replay on death", 31, () => Plugin.Settings.ReplayOnDeath, v => Plugin.Settings.ReplayOnDeath = v);
            Toggle("Automatic slow motion", 22, () => Plugin.Settings.AutomaticSlowMotion, v => Plugin.Settings.AutomaticSlowMotion = v);
            Number("Start before death", 13, () => Plugin.Settings.ReviewLeadSeconds, v => Plugin.Settings.ReviewLeadSeconds = v, 1, 1, 15, "s");
            Number("Slowdown duration", 4, () => Plugin.Settings.SlowdownSeconds, v => Plugin.Settings.SlowdownSeconds = v, 0.5f, 0.5f, 10, "s");
            Number("Minimum speed", -5, () => Plugin.Settings.MistakeSpeedPercent, v => Plugin.Settings.MistakeSpeedPercent = v, 5, 5, 50, "%");
            Number("Recent clip length", -14, () => Plugin.Settings.BufferSeconds, v => Plugin.Settings.BufferSeconds = v, 5, 5, 120, "s");
            Number("Replace replay after", -23, () => Plugin.Settings.ReplayReplacementSeconds, v => Plugin.Settings.ReplayReplacementSeconds = v, 1, 0, 120, "s");
            Toggle("Loop death clip", -32, () => Plugin.Settings.LoopDeathClip, v => Plugin.Settings.LoopDeathClip = v);
        }

        private void Toggle(string title, float y, Func<bool> get, Action<bool> set)
        {
            Label(title, y);
            TMPro.TextMeshProUGUI value = null;
            var button = CutbackUI.Button(content, get() ? "On" : "Off", 38, y, 28, 7,
                () => { set(!get()); value.text = get() ? "On" : "Off"; });
            value = button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        }

        private void Number(string title, float y, Func<float> get, Action<float> set, float step, float min, float max, string unit)
        {
            Label(title, y);
            var value = CutbackUI.Text(content, Format(get(), unit), 36, y, 19, 7, 2.8f);
            Action<float> change = delta => { set(Mathf.Clamp(get() + delta, min, max)); value.text = Format(get(), unit); };
            CutbackUI.Button(content, "−", 22, y, 8, 7, () => change(-step));
            CutbackUI.Button(content, "+", 50, y, 8, 7, () => change(step));
        }
        private void Label(string title, float y)
        {
            var label = CutbackUI.Text(content, title, -19, y, 65, 7, 2.8f);
            label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        }
        private static string Format(float value, string unit) => value.ToString("0.#") + unit;
    }
}
