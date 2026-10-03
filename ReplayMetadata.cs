// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
// Adapted from BeatLeader MapEnhancer at 265424aa. See ThirdParty/BeatLeader-LICENSE.
using System.Collections.Generic;
using BeatLeader.Models.Replay;

namespace Cutback
{
    internal static class ReplayMetadata
    {
        internal static void Fill(ReplayInfo info, GameplayCoreSceneSetupData setup, int score, float jumpDistance)
        {
            info.hash = setup.beatmapKey.levelId.Replace("custom_level_", "");
            info.songName = setup.beatmapLevel.songName;
            info.mapper = string.Join(",", setup.beatmapLevel.allMappers);
            info.difficulty = setup.beatmapKey.difficulty.ToString();
            info.mode = setup.beatmapKey.beatmapCharacteristic.serializedName;
            info.environment = setup.targetEnvironmentInfo.serializedName;
            info.leftHanded = setup.playerSpecificSettings.leftHanded;
            info.height = setup.playerSpecificSettings.automaticPlayerHeight ? 0 : setup.playerSpecificSettings.playerHeight;
            info.score = score;
            info.jumpDistance = jumpDistance;
            var m = setup.gameplayModifiers;
            var modifiers = new List<string>();
            if (m.disappearingArrows) modifiers.Add("DA");
            if (m.songSpeed == GameplayModifiers.SongSpeed.Faster) modifiers.Add("FS");
            if (m.songSpeed == GameplayModifiers.SongSpeed.Slower) modifiers.Add("SS");
            if (m.songSpeed == GameplayModifiers.SongSpeed.SuperFast) modifiers.Add("SF");
            if (m.ghostNotes) modifiers.Add("GN");
            if (m.noArrows) modifiers.Add("NA");
            if (m.noBombs) modifiers.Add("NB");
            if (m.noFailOn0Energy) modifiers.Add("NF");
            if (m.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles) modifiers.Add("NO");
            if (m.strictAngles) modifiers.Add("SA");
            if (m.smallCubes) modifiers.Add("SC");
            if (m.proMode) modifiers.Add("PM");
            if (m.failOnSaberClash) modifiers.Add("CS");
            if (m.instaFail) modifiers.Add("IF");
            if (m.energyType == GameplayModifiers.EnergyType.Battery) modifiers.Add("BE");
            info.modifiers = string.Join(",", modifiers);
        }
    }
}
