using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sable.Toolbox.Qa.Rules
{
    internal sealed class MixedWriteDefaultsRule : AvatarRule
    {
        public override string Id => "qa.mixed-write-defaults";
        public override string Title => "Mixed Write Defaults";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (AnimatorController ac in AnimatorUtil.Controllers(scan))
            {
                int on = 0, off = 0;
                foreach (AnimatorControllerLayer layer in ac.layers)
                foreach (AnimatorState s in AnimatorUtil.States(layer.stateMachine))
                {
                    // Direct blend trees must use WD on by convention; they don't count as mixing.
                    if (s.motion is BlendTree bt && bt.blendType == BlendTreeType.Direct) continue;
                    if (s.writeDefaultValues) on++; else off++;
                }
                if (on == 0 || off == 0) continue;
                yield return Found(ac, $"Controller '{ac.name}' mixes Write Defaults: {on} state(s) on, {off} off",
                    "Mixing Write Defaults in one controller makes toggles stick or reset unpredictably, " +
                    "especially after other tools merge layers in. Pick one and set every state to it.");
            }
        }
    }

    internal sealed class UnusedParameterRule : AvatarRule
    {
        public override string Id => "qa.unused-parameter";
        public override string Title => "Unused expression parameters";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            VRCExpressionParameters asset = scan.Descriptor.expressionParameters;
            if (asset == null) yield break;
            var used = new HashSet<string>(AnimatorUtil.Controllers(scan).SelectMany(c => c.parameters).Select(p => p.name));
            bool buildTools = RuleContext.HasBuildTools(scan);

            foreach (VRCExpressionParameters.Parameter p in scan.Parameters)
            {
                if (used.Contains(p.name)) continue;
                string name = p.name;
                yield return Found(asset, $"Parameter '{p.name}' is not used by any playable layer",
                    (p.networkSynced ? "It still costs synced parameter memory. " : "") +
                    "Nothing on the avatar reads it, unless an OSC app or a build-time tool does." +
                    (buildTools ? " This avatar has Modular Avatar/VRCFury components, which may add controllers that use it." : ""),
                    buildTools ? null : Fix("Remove", $"Removes '{name}' from the expression parameters asset '{asset.name}'.",
                        () =>
                        {
                            UnityEditor.Undo.RecordObject(asset, "Remove parameter");
                            asset.parameters = asset.parameters.Where(x => x == null || x.name != name).ToArray();
                            UnityEditor.EditorUtility.SetDirty(asset);
                        }),
                    buildTools ? Severity.Info : (Severity?)null);
            }
        }
    }

    internal sealed class ParameterBudgetRule : AvatarRule
    {
        public override string Id => "qa.parameter-budget";
        public override string Title => "Synced parameter memory over budget";
        public override Severity DefaultSeverity => Severity.Error;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            VRCExpressionParameters asset = scan.Descriptor.expressionParameters;
            if (asset == null) yield break;
            int cost = asset.CalcTotalCost();
            if (cost <= VRCExpressionParameters.MAX_PARAMETER_COST) yield break;
            yield return Found(asset, $"Synced parameters use {cost} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits",
                "The SDK refuses to upload an avatar over the synced parameter budget. " +
                "Make rarely changed parameters local-only, or combine toggles into an int.");
        }
    }

    internal sealed class EmptyStateRule : AvatarRule
    {
        public override string Id => "qa.empty-state";
        public override string Title => "Empty animator states";
        public override Severity DefaultSeverity => Severity.Info;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (AnimatorLayerRef l in scan.AnimatorLayers)
            {
                AnimatorControllerLayer layer = AnimatorUtil.Layer(l);
                if (layer == null) continue;
                List<AnimatorState> empty = AnimatorUtil.States(layer.stateMachine).Where(s => s.motion == null).ToList();
                if (empty.Count == 0) continue;
                yield return Found(empty[0],
                    $"{l.PlayableLayer} layer '{l.LayerName}' has {empty.Count} state(s) with no motion ({string.Join(", ", empty.Take(3).Select(s => s.name))}{(empty.Count > 3 ? ", …" : "")})",
                    "A state with no motion behaves differently under Write Defaults on and off, and the difference shows up " +
                    "when layers are merged. A short empty clip makes the state's effect explicit.");
            }
        }
    }
}
