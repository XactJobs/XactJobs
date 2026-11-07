// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections;

namespace XactJobs.CronUtil
{
    static class BitArrayExtensions
    {
        public static int GetNextSetBit(this BitArray bits, int startIndex)
        {
            for (var i = startIndex; i < bits.Count; i++)
            {
                if (bits.Get(i)) return i;
            }

            return -1;
        }
    }
}
