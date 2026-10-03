// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Linq;
using BeatLeader.Replayer;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using HMUI;
using UnityEngine;
using Zenject;

namespace Cutback
{
    public sealed class CutbackMenu : IInitializable, ITickable, IDisposable
    {
        internal static CutbackMenu Instance;
        internal static string Status = "";
        [Inject] private readonly ReplayerLauncher launcher;
        [Inject] private readonly BeatmapLevelsModel levels;
        [Inject] private readonly MainFlowCoordinator mainFlow;
        [Inject] private readonly GameScenesManager scenes;
        private MenuButton button;
        private CutbackFlow flow;
        private bool loading;
        private float nextRefresh;

        public void Initialize()
        {
            if (!Plugin.Ready) return;
            Instance = this;
            button = new MenuButton("Cutback", Open);
            MenuButtons.Instance.RegisterButton(button);
        }
        internal void Open()
        {
            if (flow == null) flow = BeatSaberUI.CreateFlowCoordinator<CutbackFlow>();
            mainFlow.PresentFlowCoordinator(flow);
        }
        internal void Close() => mainFlow.DismissFlowCoordinator(flow);

        internal async void Watch(AttemptHeader header, bool mistake)
        {
            if (loading || header == null) return;
            loading = true;
            Status = "Loading attempt";
            try
            {
                var snapshot = await AttemptStore.Load(header);
                if (snapshot == null) throw new InvalidOperationException("The attempt file needs recovery.");
                ReviewCoordinator.Queue(snapshot, mistake);
            }
            catch (Exception ex) { Status = ex.Message; Plugin.Log.Error(ex); }
            finally { loading = false; }
        }

        public void Tick()
        {
            if (!Plugin.Ready) return;
            if (Time.realtimeSinceStartup > nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + 0.5f;
                CutbackUI.SquareMenuEntry();
                if (flow != null) flow.RefreshStatus();
            }
            if (ReviewCoordinator.Pending == null || AttemptSession.Current != null || ReplayerLauncher.IsStartedAsReplay || scenes.isInTransition) return;
            // A queued pause review reaches here only after the original scene is disposed.
            try { ReviewCoordinator.Launch(launcher, levels, scenes); }
            catch (Exception ex) { Status = ex.Message; Plugin.Log.Error(ex); }
        }
        public void Dispose()
        {
            if (button != null) MenuButtons.Instance.UnregisterButton(button);
            if (flow != null) UnityEngine.Object.Destroy(flow.gameObject);
            if (Instance == this) Instance = null;
        }
    }

    public sealed class CutbackFlow : FlowCoordinator
    {
        private CutbackView view;
        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Cutback");
                showBackButton = true;
                view = BeatSaberUI.CreateViewController<CutbackView>();
                ProvideInitialViewControllers(view);
            }
            view?.Refresh();
        }
        protected override void BackButtonWasPressed(ViewController topViewController) => CutbackMenu.Instance.Close();
        internal void RefreshStatus() => view?.RefreshStatus();
    }

    public sealed class CutbackView : ViewController
    {
        private RectTransform content;
        private TMPro.TextMeshProUGUI status, selection;
        private AttemptHeader recent;
        private UnityEngine.UI.Button watch, focus;
        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                content = CutbackUI.Root(transform, "CutbackRecent", new Vector2(150, 82));
                selection = CutbackUI.Text(content, "", 0, 14, 140, 14, 4f);
                selection.enableWordWrapping = true;
                watch = CutbackUI.Button(content, "Watch latest", -29, -6, 52, 9, () => CutbackMenu.Instance.Watch(recent, false));
                focus = CutbackUI.Button(content, "Review ending", 29, -6, 52, 9, () => CutbackMenu.Instance.Watch(recent, true));
                status = CutbackUI.Text(content, "", 0, -22, 140, 8, 2.8f);
            }
            Refresh();
        }
        internal void Refresh()
        {
            if (content == null) return;
            recent = AttemptStore.List().FirstOrDefault();
            selection.text = recent == null ? "Play a song to capture a replay" : $"{recent.Song}\n{recent.Difficulty}   {recent.Outcome}   {recent.End - recent.Start:F1}s";
            watch.interactable = focus.interactable = recent != null && recent.Frames >= 2;
            RefreshStatus();
        }
        internal void RefreshStatus()
        {
            if (status != null) status.text = AttemptStore.Error ?? CutbackMenu.Status;
            var latest = AttemptStore.List().FirstOrDefault();
            if (content != null && recent != latest) Refresh();
        }
    }
}
