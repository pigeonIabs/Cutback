// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using BeatLeader.Models.Replay;

namespace Cutback
{
    internal static class LocalReplayCodec
    {
        internal static Replay Decode(byte[] bytes)
        {
            var replay = ReplayDecoder.DecodeReplay(bytes);
            if (replay == null) return null;
            // The 0.9.33 writer embeds this flag, but its reader leaves it packed in
            // saberSpeed. Restore it on our own decoded copy, leaving BeatLeader untouched.
            foreach (var note in replay.notes)
            {
                var cut = note.noteCutInfo;
                if (cut == null) continue;
                int bits = BitConverter.ToInt32(BitConverter.GetBytes(cut.saberSpeed), 0);
                cut.cutDistanceToCenterPositive = (bits & 1) != 0;
                cut.saberSpeed = BitConverter.ToSingle(BitConverter.GetBytes(bits & ~1), 0);
            }
            return replay;
        }
    }
}
