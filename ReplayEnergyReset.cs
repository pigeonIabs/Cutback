// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BeatLeader.Replayer;
using HarmonyLib;

namespace Cutback
{
    [HarmonyPatch]
    public static class ReplayEnergyReset
    {
        private const string ScoreProcessorTypeName = "BeatLeader.Replayer.Emulation.ReplayerScoreProcessor";
        private const string BatteryLivesFieldName = "_batteryLives";

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName(ScoreProcessorTypeName);
            var method = type == null ? null : AccessTools.Method(type, "HandleReprocessRequested", Type.EmptyTypes);
            if (method == null)
                throw new InvalidOperationException("Cutback compatibility error. BeatLeader ReplayerScoreProcessor.HandleReprocessRequested was not found.");
            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var helper = AccessTools.Method(typeof(ReplayEnergyReset), nameof(GetBatteryLives));
            if (helper == null)
                throw new InvalidOperationException("Cutback compatibility error. ReplayEnergyReset.GetBatteryLives was not found.");

            int matchCount = 0;
            int constantIndex = -1;

            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldstr ||
                    !string.Equals(code[i].operand as string, BatteryLivesFieldName, StringComparison.Ordinal))
                    continue;
                if (code[i + 1].opcode != OpCodes.Ldc_I4_4)
                    continue;

                matchCount++;
                constantIndex = i + 1;
            }

            if (matchCount != 1)
                throw new InvalidOperationException(
                    "Cutback compatibility error. Expected one _batteryLives reset to 4 in BeatLeader ReplayerScoreProcessor.HandleReprocessRequested, found " + matchCount + ".");

            var original = code[constantIndex];
            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(original.labels);
            replacement.blocks.AddRange(original.blocks);
            code[constantIndex] = replacement;

            return code;
        }

        public static int GetBatteryLives()
        {
            if (!ReviewCoordinator.IsLocalReview)
                return 4;

            var replay = ReplayerLauncher.LaunchData?.MainReplay;
            return replay != null && replay.ReplayData.GameplayModifiers.instaFail ? 1 : 4;
        }
    }
}
