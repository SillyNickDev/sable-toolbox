using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Qa.Budget
{
    public enum Rank { Excellent, Good, Medium, Poor, VeryPoor }

    [Serializable]
    public sealed class MetricLimit
    {
        public string id;
        public string label;
        public double excellent, good, medium, poor;

        public Rank Rate(double value) =>
            value <= excellent ? Rank.Excellent : value <= good ? Rank.Good : value <= medium ? Rank.Medium : value <= poor ? Rank.Poor : Rank.VeryPoor;

        /// <summary>Highest value that still earns the rank (VeryPoor has no ceiling).</summary>
        public double Ceiling(Rank rank) => rank switch
        {
            Rank.Excellent => excellent, Rank.Good => good, Rank.Medium => medium, Rank.Poor => poor, _ => double.PositiveInfinity
        };
    }

    [Serializable]
    public sealed class PlatformLimits
    {
        public string id;
        public string name;
        public double downloadLimitMB;
        public double memoryLimitMB;
        public MetricLimit[] metrics;
    }

    /// <summary>Thresholds from Data/performance-limits.json (generated from the SDK; see tools/).</summary>
    public static class PerformanceLimits
    {
        public const string DataPath = "Packages/studio.sable.toolbox.qa/Editor/Data/performance-limits.json";

        [Serializable] private class DataFile { public int version; public string sdkVersion; public PlatformLimits[] platforms; }
        private static DataFile data;

        public static IReadOnlyList<PlatformLimits> Platforms => Data.platforms ?? Array.Empty<PlatformLimits>();
        public static string SdkVersion => Data.sdkVersion;
        public static PlatformLimits Platform(string id) => Platforms.FirstOrDefault(p => p.id == id);

        private static DataFile Data
        {
            get
            {
                if (data != null) return data;
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
                if (json == null) Debug.LogWarning($"Sable Toolbox: {DataPath} not found; performance budget is off.");
                return data = json != null ? JsonUtility.FromJson<DataFile>(json.text) : new DataFile();
            }
        }
    }

    public readonly struct MetricResult
    {
        public MetricLimit Limit { get; }
        public double Value { get; }
        public Rank Rank { get; }
        public MetricResult(MetricLimit limit, double value) { Limit = limit; Value = value; Rank = limit.Rate(value); }
    }

    /// <summary>One object's share of a metric, for the "what would get me to X" view.</summary>
    public readonly struct Contribution
    {
        public Object Source { get; }
        public double Amount { get; }
        public string Note { get; }
        public Contribution(Object source, double amount, string note = null) { Source = source; Amount = amount; Note = note; }
    }

    public sealed class PlatformResult
    {
        public PlatformLimits Platform { get; internal set; }
        public IReadOnlyList<MetricResult> Metrics { get; internal set; }
        public Rank Overall => Metrics.Count == 0 ? Rank.Excellent : Metrics.Max(m => m.Rank);
        /// <summary>The metrics currently setting the overall rank.</summary>
        public IEnumerable<MetricResult> Limiting => Metrics.Where(m => m.Rank == Overall && Overall != Rank.Excellent);
        public IEnumerable<MetricResult> Blocking(Rank target) => Metrics.Where(m => m.Rank > target).OrderByDescending(m => m.Value / Math.Max(1, m.Limit.Ceiling(target)));
    }

    /// <summary>
    /// Measures with the VRChat SDK's own calculator (so numbers match the SDK build panel) and rates
    /// against the JSON thresholds. Metric ids are SDK AvatarPerformanceStats field paths.
    /// </summary>
    public static class PerformanceBudget
    {
        public static PlatformResult Evaluate(AvatarScan scan, PlatformLimits platform)
        {
            bool mobile = platform.id == "android";
            var stats = new AvatarPerformanceStats(mobile);
            AvatarPerformance.CalculatePerformanceStats(scan.Descriptor.name, scan.Descriptor.gameObject, stats, mobile);
            return new PlatformResult
            {
                Platform = platform,
                Metrics = platform.metrics.Select(m => new MetricResult(m, Read(stats, m.id))).ToList()
            };
        }

        /// <summary>Reads "field" or "parent.field" from the stats object; missing or null reads as 0.</summary>
        public static double Read(object stats, string path)
        {
            object value = stats;
            foreach (string part in path.Split('.'))
            {
                if (value == null) return 0;
                FieldInfo f = value.GetType().GetField(part, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) return 0;
                value = f.GetValue(value);
            }
            return value switch
            {
                null => 0,
                bool b => b ? 1 : 0,
                IConvertible c => c.ToDouble(null),
                _ => 0
            };
        }

        /// <summary>
        /// Estimated per-object shares of a metric, largest first, from the scan. These explain the
        /// SDK total; they are estimates and can differ from it slightly.
        /// </summary>
        public static List<Contribution> Contributors(AvatarScan scan, string metricId)
        {
            IEnumerable<Contribution> list = metricId switch
            {
                "polyCount" => scan.Renderers.Select(r => new Contribution(r, Triangles(r))),
                "skinnedMeshCount" => scan.Renderers.OfType<SkinnedMeshRenderer>().Select(r => new Contribution(r, 1)),
                "meshCount" => scan.Renderers.OfType<MeshRenderer>().Select(r => new Contribution(r, 1)),
                "materialCount" => scan.Renderers.Select(r => new Contribution(r, r.sharedMaterials.Length)),
                "boneCount" => scan.Renderers.OfType<SkinnedMeshRenderer>().Select(r => new Contribution(r, r.bones.Count(b => b != null), "bones on this mesh (shared bones count once)")),
                "textureMegabytes" => scan.Textures.Select(t => new Contribution(t, TextureMemory.Megabytes(t),
                    TextureOnlyAnimated(scan, t) ? "only via animation" : null)),
                "physBone.componentCount" => scan.PhysBones.Select(p => new Contribution(p, 1)),
                "physBone.transformCount" => scan.PhysBones.Select(p => new Contribution(p, Rules.PhysBoneCollisionRule.AffectedTransforms(p))),
                "physBone.colliderCount" => scan.Colliders.Select(c => new Contribution(c, 1)),
                "physBone.collisionCheckCount" => scan.PhysBones.Select(p => new Contribution(p,
                    Rules.PhysBoneCollisionRule.AffectedTransforms(p) * (p.colliders?.Count(c => c != null) ?? 0))),
                "contactCount" => scan.Contacts.Select(c => new Contribution(c, 1)),
                "constraintsCount" => scan.Constraints.Select(c => new Contribution(c, 1)),
                "lightCount" => scan.Components.OfType<Light>().Select(l => new Contribution(l, 1)),
                "audioSourceCount" => scan.Components.OfType<AudioSource>().Select(a => new Contribution(a, 1)),
                "particleSystemCount" => scan.Components.OfType<ParticleSystem>().Select(p => new Contribution(p, 1)),
                "trailRendererCount" => scan.Components.OfType<TrailRenderer>().Select(t => new Contribution(t, 1)),
                "lineRendererCount" => scan.Components.OfType<LineRenderer>().Select(t => new Contribution(t, 1)),
                "clothCount" => scan.Components.OfType<Cloth>().Select(c => new Contribution(c, 1)),
                "physicsColliderCount" => scan.Components.OfType<Collider>().Select(c => new Contribution(c, 1)),
                "physicsRigidbodyCount" => scan.Components.OfType<Rigidbody>().Select(c => new Contribution(c, 1)),
                _ => Enumerable.Empty<Contribution>()
            };
            return list.Where(c => c.Amount > 0).OrderByDescending(c => c.Amount).ToList();
        }

        /// <summary>Texture memory used only by materials that appear through animation (missed by VRChat's stat).</summary>
        public static double AnimatedOnlyTextureMegabytes(AvatarScan scan) =>
            scan.Textures.Where(t => TextureOnlyAnimated(scan, t)).Sum(TextureMemory.Megabytes);

        private static bool TextureOnlyAnimated(AvatarScan scan, Texture t) =>
            scan.Graph.IsReachableOnlyThroughAnimation(t, scan.Descriptor);

        private static double Triangles(Renderer r)
        {
            // Unity's fake-null from GetComponent breaks ?., so compare explicitly.
            MeshFilter mf = r is SkinnedMeshRenderer ? null : r.GetComponent<MeshFilter>();
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : mf != null ? mf.sharedMesh : null;
            if (m == null) return 0;
            long tris = 0;
            for (int i = 0; i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3;
            return tris;
        }
    }
}
