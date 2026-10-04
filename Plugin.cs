// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using Logger = IPA.Logging.Logger;
using SiraUtil.Zenject;
using UnityEngine;
using Zenject;

namespace Cutback
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public sealed class Plugin
    {
        internal static Logger Log;
        internal static Settings Settings;
        internal static bool Ready;
        private Harmony harmony;

        [Init]
        public Plugin(Logger logger, Config config, Zenjector zenjector)
        {
            Log = logger;
            Settings = config.Generated<Settings>();
            zenjector.Install<CutbackMenuInstaller>(Location.Menu);
            zenjector.Install<CutbackGameInstaller>(Location.StandardPlayer);
        }

        [OnStart]
        public void Start()
        {
            try
            {
                string version = Application.version.Split('_')[0];
                if (version != "1.40.8") throw new InvalidOperationException("This build targets Steam Beat Saber 1.40.8 only.");
                Compatibility.Verify();
                AttemptStore.Initialize();
                harmony = new Harmony("Cutback.1.40.8");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Compatibility.InstallOptionalPatches(harmony);
                Ready = true;
                Log.Info("Cutback ready for Steam Beat Saber 1.40.8 and BeatLeader 0.9.33");
            }
            catch (Exception ex)
            {
                harmony?.UnpatchSelf();
                Log.Error(ex);
            }
        }

        [OnExit]
        public void Exit()
        {
            AttemptSession.Current?.Save("Quit", true);
            AttemptStore.Flush();
        }

        internal static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error(ex); CutbackMenu.Status = "Cutback needs attention. See the game log."; }
        }
    }

    public class Settings
    {
        public virtual float MistakeSpeedPercent { get; set; } = 10;
        public virtual float TailSeconds { get; set; } = 0.5f;
        public virtual float ReviewLeadSeconds { get; set; } = 3;
        public virtual bool AutomaticSlowMotion { get; set; } = true;
        public virtual int CheckpointSeconds { get; set; } = 30;
        public virtual bool ReplayOnDeath { get; set; } = true;
        public virtual float ReplayReplacementSeconds { get; set; } = 5;
        public virtual bool ReviewMissedNotes { get; set; } = true;
        public virtual bool ReviewBadCuts { get; set; } = false;
        public virtual float SlowdownSeconds { get; set; } = 3;
        public virtual bool LoopDeathClip { get; set; } = false;
    }

    public class CutbackMenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<CutbackMenu>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<CutbackSettingsPanel>().AsSingle().NonLazy();
        }
    }

    public class CutbackGameInstaller : Installer
    {
        public override void InstallBindings()
        {
            if (Plugin.Ready)
            {
                Container.BindInterfacesAndSelfTo<AttemptSession>().AsSingle().NonLazy();
                Container.BindLateTickableExecutionOrder<AttemptSession>(100);
            }
        }
    }

    internal static class Reflect
    {
        internal static T Get<T>(object instance, string name) => (T)AccessTools.Field(instance.GetType(), name).GetValue(instance);
        internal static void Set(object instance, string name, object value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);
        internal static object Call(object instance, string name, params object[] args) => AccessTools.Method(instance.GetType(), name).Invoke(instance, args);
        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        internal static T Clone<T>(T value) where T : class => value == null ? null : (T)CloneMethod.Invoke(value, null);
    }
}
