// Copyright (c) 2026 pigeonIabs
// SPDX-License-Identifier: AGPL-3.0-only
// Runtime linking permission is described in NOTICE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cutback
{
    internal sealed class MistakeTimeline
    {
        private readonly List<(float Start, float End)> windows = new List<(float, float)>();
        internal MistakeTimeline(IEnumerable<float> mistakes, float lead, float tail)
        {
            foreach (float time in mistakes.Where(t => !float.IsNaN(t) && !float.IsInfinity(t)).OrderBy(t => t))
            {
                float start = time - lead, end = time + tail;
                if (windows.Count > 0 && start <= windows[windows.Count - 1].End)
                {
                    var previous = windows[windows.Count - 1];
                    windows[windows.Count - 1] = (previous.Start, Math.Max(previous.End, end));
                }
                else windows.Add((start, end));
            }
        }
        internal bool Contains(float time)
        {
            int low = 0, high = windows.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var window = windows[middle];
                if (time < window.Start) high = middle - 1;
                else if (time > window.End) low = middle + 1;
                else return true;
            }
            return false;
        }
    }
}
