using System.Collections.Generic;
using UnityEngine;

namespace Sable.Toolbox.Graph
{
    public enum EdgeKind
    {
        /// <summary>A direct serialized reference (renderer to material, material to texture, ...).</summary>
        Direct,
        /// <summary>A reference held by an animation clip keyframe (material swap, object-reference curve).</summary>
        Animated
    }

    /// <summary>
    /// Which object references which, for one avatar. Nodes are scene components and assets alike.
    /// Edges are built by the scanner; Animated edges are what lets the toolbox see materials and
    /// textures that only appear through animation clips.
    /// </summary>
    public sealed class AssetGraph
    {
        private static readonly IReadOnlyCollection<Object> None = new Object[0];

        private readonly Dictionary<Object, HashSet<Object>> outgoing = new Dictionary<Object, HashSet<Object>>();
        private readonly Dictionary<Object, HashSet<Object>> incoming = new Dictionary<Object, HashSet<Object>>();
        private readonly HashSet<(Object, Object)> animated = new HashSet<(Object, Object)>();

        public int NodeCount => outgoing.Count + CountIncomingOnly();
        public int EdgeCount { get; private set; }

        public bool Add(Object from, Object to, EdgeKind kind = EdgeKind.Direct)
        {
            if (from == null || to == null || from == to) return false;
            if (!Get(outgoing, from, true).Add(to)) return false;
            Get(incoming, to, true).Add(from);
            if (kind == EdgeKind.Animated) animated.Add((from, to));
            EdgeCount++;
            return true;
        }

        /// <summary>Objects this one references directly.</summary>
        public IReadOnlyCollection<Object> ReferencesOf(Object node) => Get(outgoing, node, false) ?? None;

        /// <summary>Objects that reference this one directly.</summary>
        public IReadOnlyCollection<Object> ReferencedBy(Object node) => Get(incoming, node, false) ?? None;

        public bool IsAnimatedEdge(Object from, Object to) => animated.Contains((from, to));

        /// <summary>True when every path from a root to this node passes through an animated edge.</summary>
        public bool IsReachableOnlyThroughAnimation(Object node, Object root)
        {
            if (node == null || root == null) return false;
            return !Reaches(root, node, followAnimated: false) && Reaches(root, node, followAnimated: true);
        }

        /// <summary>Everything reachable from root, following both direct and animated edges.</summary>
        public HashSet<Object> Reachable(Object root)
        {
            var seen = new HashSet<Object>();
            Walk(root, seen, true, null);
            return seen;
        }

        private bool Reaches(Object from, Object target, bool followAnimated)
        {
            var seen = new HashSet<Object>();
            return Walk(from, seen, followAnimated, target);
        }

        // Iterative DFS; returns true as soon as target is found (if given).
        private bool Walk(Object start, HashSet<Object> seen, bool followAnimated, Object target)
        {
            var stack = new Stack<Object>();
            stack.Push(start);
            seen.Add(start);
            while (stack.Count > 0)
            {
                Object node = stack.Pop();
                if (target != null && node == target) return true;
                HashSet<Object> next = Get(outgoing, node, false);
                if (next == null) continue;
                foreach (Object n in next)
                {
                    if (!followAnimated && animated.Contains((node, n))) continue;
                    if (seen.Add(n)) stack.Push(n);
                }
            }
            return false;
        }

        private int CountIncomingOnly()
        {
            int n = 0;
            foreach (Object k in incoming.Keys) if (!outgoing.ContainsKey(k)) n++;
            return n;
        }

        private static HashSet<Object> Get(Dictionary<Object, HashSet<Object>> map, Object key, bool create)
        {
            if (key == null) return null;
            if (map.TryGetValue(key, out HashSet<Object> set)) return set;
            if (!create) return null;
            set = new HashSet<Object>();
            map[key] = set;
            return set;
        }
    }
}
