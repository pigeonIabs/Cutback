// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
// Forked from BeatLeader 265424aa, MIT. See ThirdParty/BeatLeader-LICENSE.
using System.Collections.Generic; using System.IO; using System.Text; using BeatLeader.Models.Replay;
namespace Cutback {
    static class LocalReplayEncoder
    {
        public static void Encode(BeatLeader.Models.Replay.Replay replay, BinaryWriter stream)
        {
            stream.Write(0x442d3d69);
            stream.Write((byte)1);

            for (int a = 0; a <= (int)StructType.customData; a++)
            {
                StructType type = (StructType)a;
                stream.Write((byte)a);

                switch (type)
                {
                    case StructType.info:
                        EncodeInfo(replay.info, stream);
                        break;
                    case StructType.frames:
                        EncodeFrames(replay.frames, stream);
                        break;
                    case StructType.notes:
                        EncodeNotes(replay.notes, stream);
                        break;
                    case StructType.walls:
                        EncodeWalls(replay.walls, stream);
                        break;
                    case StructType.heights:
                        EncodeHeights(replay.heights, stream);
                        break;
                    case StructType.pauses:
                        EncodePauses(replay.pauses, stream);
                        break;
                    case StructType.saberOffsets:
                        EncodeSaberOffsets(replay.saberOffsets, stream);
                        break;
                    case StructType.customData:
                        EncodeCustomData(replay.customData, stream);
                        break;
                }
            }
        }

        static void EncodeInfo(ReplayInfo info, BinaryWriter stream)
        {
            EncodeString(info.version, stream);
            EncodeString(info.gameVersion, stream);
            EncodeString(info.timestamp, stream);

            EncodeString(info.playerID, stream);
            EncodeString(info.playerName, stream);
            EncodeString(info.platform, stream);

            EncodeString(info.trackingSytem, stream);
            EncodeString(info.hmd, stream);
            EncodeString(info.controller, stream);

            EncodeString(info.hash, stream);
            EncodeString(info.songName, stream);
            EncodeString(info.mapper, stream);
            EncodeString(info.difficulty, stream);

            stream.Write(info.score);
            EncodeString(info.mode, stream);
            EncodeString(info.environment, stream);
            EncodeString(info.modifiers, stream);
            stream.Write(info.jumpDistance);
            stream.Write(info.leftHanded);
            stream.Write(info.height);

            stream.Write(info.startTime);
            stream.Write(info.failTime);
            stream.Write(info.speed);
        }

        static void EncodeFrames(List<Frame> frames, BinaryWriter stream)
        {
            stream.Write((uint)frames.Count);
            foreach (var frame in frames)
            {
                stream.Write(frame.time);
                stream.Write(frame.fps);
                EncodeVector(frame.head.position, stream);
                EncodeQuaternion(frame.head.rotation, stream);
                EncodeVector(frame.leftHand.position, stream);
                EncodeQuaternion(frame.leftHand.rotation, stream);
                EncodeVector(frame.rightHand.position, stream);
                EncodeQuaternion(frame.rightHand.rotation, stream);
            }
        }

        static void EncodeNotes(List<NoteEvent> notes, BinaryWriter stream)
        {
            stream.Write((uint)notes.Count);
            foreach (var note in notes)
            {
                stream.Write(note.noteID);
                stream.Write(note.eventTime);
                stream.Write(note.spawnTime);
                stream.Write((int)note.eventType);
                if (note.eventType == NoteEventType.good || note.eventType == NoteEventType.bad)
                {
                    EncodeNoteInfo(note.noteCutInfo, stream);
                }
            }
        }

        static void EncodeWalls(List<WallEvent> walls, BinaryWriter stream)
        {
            stream.Write((uint)walls.Count);
            foreach (var wall in walls)
            {
                stream.Write(wall.wallID);
                stream.Write(wall.energy);
                stream.Write(wall.time);
                stream.Write(wall.spawnTime);
            }
        }

        static void EncodeHeights(List<AutomaticHeight> heights, BinaryWriter stream)
        {
            stream.Write((uint)heights.Count);
            foreach (var height in heights)
            {
                stream.Write(height.height);
                stream.Write(height.time);
            }
        }

        static void EncodePauses(List<Pause> pauses, BinaryWriter stream)
        {
            stream.Write((uint)pauses.Count);
            foreach (var pause in pauses)
            {
                stream.Write(pause.duration);
                stream.Write(pause.time);
            }
        }

        static void EncodeSaberOffsets(SaberOffsets saberOffsets, BinaryWriter stream)
        {
            EncodeVector(saberOffsets.LeftSaberLocalPosition, stream);
            EncodeQuaternion(saberOffsets.LeftSaberLocalRotation, stream);
            EncodeVector(saberOffsets.RightSaberLocalPosition, stream);
            EncodeQuaternion(saberOffsets.RightSaberLocalRotation, stream);
        }

        static void EncodeCustomData(Dictionary<string, byte[]> customData, BinaryWriter stream)
        {
            stream.Write(customData.Count);
            foreach (var pair in customData) {
                EncodeString(pair.Key, stream);
                EncodeByteArray(pair.Value, stream);
            }
        }

        static void EncodeNoteInfo(BeatLeader.Models.Replay.NoteCutInfo info, BinaryWriter stream)
        {
            stream.Write(info.speedOK);
            stream.Write(info.directionOK);
            stream.Write(info.saberTypeOK);
            stream.Write(info.wasCutTooSoon);
            stream.Write(PackBool(info.saberSpeed, info.cutDistanceToCenterPositive));
            EncodeVector(info.saberDir, stream);
            stream.Write(info.saberType);
            stream.Write(info.timeDeviation);
            stream.Write(info.cutDirDeviation);
            EncodeVector(info.cutPoint, stream);
            EncodeVector(info.cutNormal, stream);
            stream.Write(info.cutDistanceToCenter);
            stream.Write(info.cutAngle);
            stream.Write(info.beforeCutRating);
            stream.Write(info.afterCutRating);
        }

        static void EncodeString(string value, BinaryWriter stream)
        {
            string toEncode = value != null ? value : "";
            var bytes = Encoding.UTF8.GetBytes(toEncode);
            stream.Write(bytes.Length);
            stream.Write(bytes);
        }

        static void EncodeByteArray(byte[] value, BinaryWriter stream)
        {
            stream.Write(value.Length);
            stream.Write(value);
        }

        static void EncodeVector(Vector3 vector, BinaryWriter stream)
        {
            stream.Write(vector.x);
            stream.Write(vector.y);
            stream.Write(vector.z);
        }

        static void EncodeQuaternion(Quaternion quaternion, BinaryWriter stream)
        {
            stream.Write(quaternion.x);
            stream.Write(quaternion.y);
            stream.Write(quaternion.z);
            stream.Write(quaternion.w);
        }
            private static float PackBool(float value, bool flag) {
            int bits = System.BitConverter.ToInt32(System.BitConverter.GetBytes(value), 0);
            return System.BitConverter.ToSingle(System.BitConverter.GetBytes(flag ? bits | 1 : bits & ~1), 0);
        }}

}
