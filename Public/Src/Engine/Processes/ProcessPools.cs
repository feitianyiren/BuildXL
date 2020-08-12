// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes
{
    /// <summary>
    /// Object pools for process management types.
    /// </summary>
    public static class ProcessPools
    {
        /// <summary>
        /// Global pool of dictionaries for grouping reported accesses by path.
        /// </summary>
        public static ObjectPool<Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>>> ReportedFileAccessesByPathPool { get; } = new ObjectPool<Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>>>(
            () => new Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>>(),
            s => s.Clear());

        /// <summary>
        /// Global pool of dictionaries for grouping reported writes accesses in dynamic directories
        /// </summary>
        public static ObjectPool<Dictionary<AbsolutePath, HashSet<AbsolutePath>>> DynamicWriteAccesses { get; } = new ObjectPool<Dictionary<AbsolutePath, HashSet<AbsolutePath>>>(
            () => new Dictionary<AbsolutePath, HashSet<AbsolutePath>>(),
            s => s.Clear());

        /// <summary>
        /// Global pool of lists for collecting unsorted observed accesses
        /// </summary>
        public static ObjectPool<List<ObservedFileAccess>> AccessUnsorted { get; } = new ObjectPool<List<ObservedFileAccess>>(
            () => new List<ObservedFileAccess>(),
            s => s.Clear());

        /// <summary>
        /// Global pool of lists for collecting reported file accesses
        /// </summary>
        public static ObjectPool<List<ReportedFileAccess>> ReportedFileAccessList { get; } = new ObjectPool<List<ReportedFileAccess>>(
            () => new List<ReportedFileAccess>(),
            s => s.Clear());
    }
}
