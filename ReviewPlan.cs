// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using BeatLeader.Models.Replay;

namespace Cutback
{
    // Selection changes the viewing position, never the captured attempt's bounds.
    internal sealed class ReviewPlan
    {
        private readonly float[] moments;
        private readonly float lead, slowdown, tail, minimum;
        private float? explicitMoment;
        internal float Start { get; }
        internal float End { get; }
        internal float InitialPosition { get; }
        internal float RecordedSpeed { get; }

        internal ReviewPlan(AttemptSnapshot attempt, bool focus, Settings settings)
        {
            Start = attempt.Header.Start;
            End = attempt.Header.End;
            RecordedSpeed = Clamp(attempt.Header.Speed, 0.05f, 2f, 1f);
            lead = Clamp(settings.ReviewLeadSeconds, 1, 15, 3);
            slowdown = Clamp(settings.SlowdownSeconds, 0.5f, 10, 3);
            tail = Clamp(settings.TailSeconds, 0.1f, 5, 0.5f);
            minimum = Clamp(settings.MistakeSpeedPercent / 100f, 0.05f, 0.5f, 0.1f);
            moments = attempt.Replay.notes
                .Where(n => (settings.ReviewMissedNotes && n.eventType == NoteEventType.miss) ||
                    (settings.ReviewBadCuts && n.eventType == NoteEventType.bad))
                .Select(n => n.eventTime)
                .Concat(attempt.Header.DeathTime.HasValue ? new[] { attempt.Header.DeathTime.Value } : Array.Empty<float>())
                .Where(t => !float.IsNaN(t) && !float.IsInfinity(t) && t >= Start && t <= End)
                .Distinct().OrderBy(t => t).ToArray();
            float? latest = attempt.Header.DeathTime ?? (moments.Length > 0 ? (float?)moments[moments.Length - 1] : null);
            InitialPosition = focus && latest.HasValue ? LeadIn(latest.Value) : Start;
        }

        internal float LeadIn(float moment) => Math.Max(Start, Math.Min(End, moment - lead));

        internal void Focus(float moment)
        {
            if (!float.IsNaN(moment) && !float.IsInfinity(moment)) explicitMoment = Math.Max(Start, Math.Min(End, moment));
        }

        internal float SpeedAt(float time, bool automatic)
        {
            if (!automatic) return RecordedSpeed;
            float speed = 1;
            int index = Array.BinarySearch(moments, time - tail);
            if (index < 0) index = ~index;
            for (; index < moments.Length && moments[index] <= time + slowdown; index++)
                speed = Math.Min(speed, Ramp(time, moments[index]));
            if (explicitMoment.HasValue) speed = Math.Min(speed, Ramp(time, explicitMoment.Value));
            return RecordedSpeed * speed;
        }

        private float Ramp(float time, float moment)
        {
            if (time < moment - slowdown || time > moment + tail) return 1;
            if (time >= moment) return minimum;
            return (float)Math.Pow(minimum, (time - moment + slowdown) / slowdown);
        }

        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    }
}
