using System;
using System.Collections.Generic;
using UnityEditor;
using VRC.SDK3.Avatars.Components;

namespace Sable.Toolbox.Scanning
{
    /// <summary>
    /// Shared scan cache. The first Get for an avatar scans it; later calls return the same object
    /// until something changes. Any scene change (hierarchy, component, property), asset import,
    /// asset change or undo/redo clears the cache and raises Invalidated.
    /// </summary>
    [InitializeOnLoad]
    public static class ScanCache
    {
        // ponytail: any change clears every cached avatar. Scans are cheap after asset loading;
        // switch to per-avatar invalidation from ObjectChangeEvents if multi-avatar scenes get slow.
        private static readonly Dictionary<VRCAvatarDescriptor, AvatarScan> cache = new Dictionary<VRCAvatarDescriptor, AvatarScan>();

        /// <summary>Raised (at most once per editor update) after the cache was cleared.</summary>
        public static event Action Invalidated;

        private static bool notifyQueued;

        static ScanCache()
        {
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => Invalidate();
            UnityEditor.Undo.undoRedoPerformed += Invalidate;
        }

        public static AvatarScan Get(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (!cache.TryGetValue(descriptor, out AvatarScan scan))
            {
                scan = AvatarScanner.Scan(descriptor);
                cache[descriptor] = scan;
            }
            return scan;
        }

        public static bool IsCached(VRCAvatarDescriptor descriptor) => descriptor != null && cache.ContainsKey(descriptor);

        public static void Invalidate()
        {
            if (cache.Count == 0 && notifyQueued) return;
            cache.Clear();
            if (notifyQueued) return;
            notifyQueued = true;
            EditorApplication.delayCall += () =>
            {
                notifyQueued = false;
                Invalidated?.Invoke();
            };
        }

        private sealed class AssetWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Length + deleted.Length + moved.Length > 0) Invalidate();
            }
        }
    }
}
