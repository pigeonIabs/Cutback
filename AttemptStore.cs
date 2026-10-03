// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BeatLeader.Models.Replay;
using IPA.Utilities;
using Newtonsoft.Json;

namespace Cutback
{
    public sealed class AttemptHeader
    {
        public int Format = 1;
        public string Id;
        public DateTime CreatedUtc;
        public string LevelId, Song, Characteristic, Difficulty, Outcome;
        public float Start, End, Speed;
        public int Frames;
        public float? DeathTime;
        public float[] Mistakes = Array.Empty<float>();
    }

    internal sealed class AttemptSnapshot
    {
        public AttemptHeader Header;
        public Replay Replay;
    }

    internal static class AttemptStore
    {
        private const string MetadataKey = "Cutback.Attempt.v1";
        private const string LegacyMetadataKey = "PracticeForge.Attempt.v1";
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, AttemptHeader> Headers = new Dictionary<string, AttemptHeader>();
        private static Task pending = Task.CompletedTask;
        internal static string DirectoryPath { get; private set; }
        internal static AttemptSnapshot Latest { get; private set; }
        internal static volatile string Error;

        internal static void Initialize()
        {
            DirectoryPath = Path.Combine(UnityGame.UserDataPath, "Cutback", "Recent");
            Directory.CreateDirectory(DirectoryPath);
            pending = Task.Run(() =>
            {
                string latestPath = ReplayPath(null);
                if (File.Exists(latestPath))
                {
                    try
                    {
                        var snapshot = Read(latestPath);
                        if (snapshot != null) lock (Gate)
                        {
                            if (Latest == null) { Latest = snapshot; Headers[snapshot.Header.Id] = snapshot.Header; }
                        }
                    }
                    catch (Exception ex) { Plugin.Log.Warn("Recent replay recovery " + ex.Message); }
                }
            });
        }

        private static bool Valid(AttemptHeader header) => header != null && header.Format == 1 &&
            Guid.TryParseExact(header.Id, "N", out _) && !string.IsNullOrEmpty(header.LevelId) &&
            !string.IsNullOrEmpty(header.Characteristic) && Enum.TryParse<BeatmapDifficulty>(header.Difficulty, out _) &&
            header.Mistakes != null && header.Mistakes.All(Finite) && Finite(header.Start) && Finite(header.End) &&
            Finite(header.Speed) && header.Speed > 0 && header.End >= header.Start && header.Frames >= 0 &&
            (!header.DeathTime.HasValue || Finite(header.DeathTime.Value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string ReplayPath(string id) => Path.Combine(DirectoryPath, "latest.bsor");
        private static string IndexPath(string id) => Path.Combine(DirectoryPath, "latest.json");

        internal static void Save(AttemptSnapshot snapshot)
        {
            if (!Valid(snapshot.Header)) throw new InvalidDataException("Invalid local attempt identity.");
            snapshot.Replay.customData[MetadataKey] = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot.Header));
            lock (Gate)
            {
                Latest = snapshot;
                Headers.Clear();
                Headers[snapshot.Header.Id] = snapshot.Header;
                pending = pending.ContinueWith(_ =>
                {
                    try
                    {
                        using (var buffer = new MemoryStream())
                        {
                            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) LocalReplayEncoder.Encode(snapshot.Replay, writer);
                            AtomicWrite(ReplayPath(snapshot.Header.Id), buffer.ToArray());
                        }
                        AtomicWrite(IndexPath(snapshot.Header.Id), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot.Header)));
                        Error = null;
                    }
                    catch (Exception ex)
                    {
                        Error = "Attempt save needs attention. Check free disk space and the game log.";
                        Plugin.Log.Error(ex);
                    }
                }, TaskScheduler.Default);
            }
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string temp = path + ".writing";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        internal static AttemptHeader[] List()
        {
            lock (Gate) return Headers.Values.OrderByDescending(h => h.CreatedUtc).ToArray();
        }

        internal static async Task<AttemptSnapshot> Load(AttemptHeader header)
        {
            if (!Valid(header)) throw new InvalidDataException("Invalid attempt index.");
            Task wait;
            lock (Gate)
            {
                if (Latest?.Header.Id == header.Id) return Latest;
                wait = pending;
            }
            await wait;
            var snapshot = await Task.Run(() => Read(ReplayPath(header.Id)));
            if (snapshot?.Header.Id != header.Id) throw new InvalidOperationException("A newer attempt is ready. Select Watch latest.");
            return snapshot;
        }

        private static AttemptSnapshot Read(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 512L * 1024 * 1024) throw new InvalidDataException("This replay exceeds the 512 MB loading limit.");
            Replay replay = LocalReplayCodec.Decode(File.ReadAllBytes(path));
            if (replay == null) return null;
            if (!replay.customData.TryGetValue(MetadataKey, out var bytes) &&
                !replay.customData.TryGetValue(LegacyMetadataKey, out bytes)) return null;
            AttemptHeader header = JsonConvert.DeserializeObject<AttemptHeader>(Encoding.UTF8.GetString(bytes));
            if (!Valid(header)) return null;
            return new AttemptSnapshot { Header = header, Replay = replay };
        }

        internal static void Flush()
        {
            Task task;
            lock (Gate) task = pending;
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex) { Plugin.Log.Error(ex); }
        }
    }
}
