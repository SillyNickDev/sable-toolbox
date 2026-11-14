using System;
using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Qa
{
    /// <summary>
    /// One lint check. Implement it (or derive from AvatarRule) in any editor assembly that references
    /// Sable.Toolbox.Qa.Editor; the linter finds it through TypeCache. A rule only reads the scan and
    /// returns findings; it never changes anything outside a FixAction.
    /// </summary>
    public interface IAvatarRule
    {
        /// <summary>Stable id used by profiles, overrides and suppressions, e.g. "qa.missing-shader".</summary>
        string Id { get; }
        string Title { get; }
        Severity DefaultSeverity { get; }
        IEnumerable<Finding> Check(AvatarScan scan, RuleContext context);
    }

    /// <summary>What a rule may read besides the scan: tunable thresholds from data/profiles.json.</summary>
    public sealed class RuleContext
    {
        private readonly IReadOnlyDictionary<string, float> thresholds;

        public RuleContext(IReadOnlyDictionary<string, float> thresholds)
        {
            this.thresholds = thresholds ?? new Dictionary<string, float>();
        }

        public float Threshold(string key, float fallback) =>
            thresholds.TryGetValue(key, out float v) ? v : fallback;

        /// <summary>True when Modular Avatar, VRCFury or an optimiser has components on the avatar.</summary>
        public static bool HasBuildTools(AvatarScan scan) => scan.BuildTools.Any(b => b.Components.Count > 0);
    }

    /// <summary>Convenience base: fills in SourceId and severity on findings.</summary>
    public abstract class AvatarRule : IAvatarRule
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract Severity DefaultSeverity { get; }
        public abstract IEnumerable<Finding> Check(AvatarScan scan, RuleContext context);

        protected Finding Found(Object location, string message, string explanation,
            FixAction fix = null, Severity? severity = null) =>
            new Finding(Id, severity ?? DefaultSeverity, location, message, explanation, fix);

        protected static FixAction Fix(string label, string description, Action apply) =>
            new FixAction(label, description, apply);
    }
}
