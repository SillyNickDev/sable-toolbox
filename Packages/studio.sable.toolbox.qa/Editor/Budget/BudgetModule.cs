using System;
using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sable.Toolbox.Qa.Budget
{
    /// <summary>Performance budget page: live rank per platform, real cost, and what would reach a target rank.</summary>
    public sealed class BudgetModule : IToolboxModule
    {
        public const string SourceId = "studio.sable.toolbox.qa.budget";
        public string Id => "studio.sable.toolbox.qa.budget";
        public string DisplayName => "Performance Budget";
        public int Order => 120;

        private static readonly Color[] RankColors =
        {
            new Color(0.30f, 0.75f, 0.40f), new Color(0.55f, 0.80f, 0.35f), new Color(0.95f, 0.75f, 0.25f),
            new Color(0.95f, 0.50f, 0.25f), new Color(0.90f, 0.30f, 0.30f)
        };

        [Serializable]
        private class Prefs { public string platform = "pc"; public int target = (int)Rank.Good; }

        public VisualElement CreateView(ToolboxContext context)
        {
            Prefs prefs = Sable.Toolbox.Settings.SableToolboxSettings.instance.GetModuleSettings<Prefs>(Id);
            var root = new VisualElement();
            AvatarScan lastScan = null;
            List<PlatformResult> results = null;

            void Refresh()
            {
                root.Clear();
                AvatarScan scan = context.Scan;
                if (scan == null)
                {
                    context.Findings.Clear(SourceId);
                    root.Add(new HelpBox("Pick an avatar above.", HelpBoxMessageType.Info));
                    return;
                }
                if (PerformanceLimits.Platforms.Count == 0)
                {
                    root.Add(new HelpBox("performance-limits.json is missing from the QA package.", HelpBoxMessageType.Error));
                    return;
                }
                if (scan != lastScan || results == null)
                {
                    lastScan = scan;
                    try { results = PerformanceLimits.Platforms.Select(p => PerformanceBudget.Evaluate(scan, p)).ToList(); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        root.Add(new HelpBox("The VRChat SDK performance calculator failed: " + e.Message, HelpBoxMessageType.Error));
                        return;
                    }
                }

                root.Add(RankHeader(results));
                if (RuleContext.HasBuildTools(scan))
                    root.Add(new HelpBox("Modular Avatar, VRCFury or an optimiser is on this avatar. Numbers are before their build-time changes, " +
                                         "so the uploaded avatar can differ.", HelpBoxMessageType.Info));
                root.Add(MetricTable(results));
                root.Add(RealCost(scan, results));
                root.Add(WhatIf(scan, results, prefs, Refresh));
                EmitFindings(context, scan, results, prefs);
            }

            root.RegisterCallback<AttachToPanelEvent>(_ => { context.Changed += Refresh; Refresh(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => context.Changed -= Refresh);
            return root;
        }

        private static VisualElement RankHeader(List<PlatformResult> results)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            foreach (PlatformResult r in results)
            {
                var box = new VisualElement { style = { flexGrow = 1, flexBasis = 0, marginRight = 6, paddingLeft = 8, paddingTop = 4, paddingBottom = 4,
                    borderLeftWidth = 4, borderLeftColor = RankColors[(int)r.Overall], backgroundColor = new Color(0, 0, 0, 0.12f) } };
                box.Add(new Label($"{r.Platform.name}: {Nice(r.Overall)}") { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold } });
                string limiting = string.Join(", ", r.Limiting.Select(m => m.Limit.label));
                box.Add(new Label(limiting.Length > 0 ? "Set by: " + limiting : "Every metric is Excellent") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.85f } });
                row.Add(box);
            }
            return row;
        }

        private static VisualElement MetricTable(List<PlatformResult> results)
        {
            var fold = new Foldout { text = "All metrics", value = false };
            var header = Row("Metric", results.Select(r => r.Platform.name).ToArray(), bold: true);
            fold.Add(header);
            PlatformResult first = results[0];
            for (int i = 0; i < first.Metrics.Count; i++)
            {
                int index = i;
                string id = first.Metrics[i].Limit.id;
                var cells = results.Select(r => r.Metrics.FirstOrDefault(m => m.Limit.id == id)).ToList();
                if (cells.All(c => c.Limit == null || c.Value == 0)) continue;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(new Label(first.Metrics[index].Limit.label) { style = { width = 190 } });
                for (int p = 0; p < results.Count; p++)
                {
                    MetricResult m = cells[p];
                    bool limiting = m.Limit != null && results[p].Limiting.Any(x => x.Limit.id == id);
                    var cell = new Label(m.Limit == null ? "—" : $"{Format(m.Value)}  {Nice(m.Rank)}")
                    {
                        style = { width = 170, color = m.Limit == null ? Color.gray : RankColors[(int)m.Rank],
                            unityFontStyleAndWeight = limiting ? FontStyle.Bold : FontStyle.Normal },
                        tooltip = m.Limit == null ? "" : $"Excellent ≤ {Format(m.Limit.excellent)}, Good ≤ {Format(m.Limit.good)}, Medium ≤ {Format(m.Limit.medium)}, Poor ≤ {Format(m.Limit.poor)}"
                    };
                    row.Add(cell);
                }
                fold.Add(row);
            }
            fold.Add(new Label($"Thresholds from VRChat SDK {PerformanceLimits.SdkVersion}. Measured with the SDK's own calculator.") { style = { opacity = 0.6f, marginTop = 4 } });
            return fold;
        }

        private static VisualElement RealCost(AvatarScan scan, List<PlatformResult> results)
        {
            var fold = new Foldout { text = "Real cost (not part of the rank)", value = true };
            double vram = results[0].Metrics.FirstOrDefault(m => m.Limit.id == "textureMegabytes").Value;
            double animatedOnly = PerformanceBudget.AnimatedOnlyTextureMegabytes(scan);
            fold.Add(Row("Texture memory", new[] { $"{vram:0.0} MB" + (animatedOnly > 0 ? $"  + {animatedOnly:0.0} MB only via animations (VRChat's stat misses these)" : "") }));
            int transparent = scan.Materials.Count(m => m.renderQueue > 2500);
            fold.Add(Row("Transparent materials", new[] { $"{transparent} of {scan.Materials.Count}" + (transparent > 0 ? "  (overdraw on large surfaces is expensive)" : "") }));
            fold.Add(Row("Animator layers", new[] { scan.AnimatorLayers.Count.ToString() }));
            VRCExpressionParameters p = scan.Descriptor.expressionParameters;
            if (p != null) fold.Add(Row("Synced parameter bits", new[] { $"{p.CalcTotalCost()} / {VRCExpressionParameters.MAX_PARAMETER_COST}" }));
            string sizes = string.Join(", ", results.Select(r => $"{r.Platform.name} {r.Platform.downloadLimitMB:0} MB download / {r.Platform.memoryLimitMB:0} MB memory"));
            fold.Add(new Label($"Download and memory size are only known after an SDK build. Limits: {sizes}.") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, marginTop = 4 } });
            return fold;
        }

        private VisualElement WhatIf(AvatarScan scan, List<PlatformResult> results, Prefs prefs, Action refresh)
        {
            var box = new VisualElement { style = { marginTop = 8 } };
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            bar.Add(new Label("What would get") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var platform = new DropdownField(results.Select(r => r.Platform.name).ToList(), Math.Max(0, results.FindIndex(r => r.Platform.id == prefs.platform)));
            var ranks = new[] { Rank.Excellent, Rank.Good, Rank.Medium, Rank.Poor };
            var target = new DropdownField(ranks.Select(Nice).ToList(), Mathf.Clamp(prefs.target, 0, 3));
            bar.Add(platform); bar.Add(new Label("to")); bar.Add(target);
            box.Add(bar);

            void Save()
            {
                prefs.platform = results[platform.index].Platform.id;
                prefs.target = target.index;
                Sable.Toolbox.Settings.SableToolboxSettings.instance.SetModuleSettings(Id, prefs);
                refresh();
            }
            platform.RegisterValueChangedCallback(_ => Save());
            target.RegisterValueChangedCallback(_ => Save());

            PlatformResult r = results[Math.Max(0, platform.index)];
            Rank goal = ranks[Mathf.Clamp(target.index, 0, 3)];
            List<MetricResult> blocking = r.Blocking(goal).ToList();
            if (blocking.Count == 0)
            {
                box.Add(new HelpBox($"Already {Nice(r.Overall)} on {r.Platform.name}; nothing blocks {Nice(goal)}.", HelpBoxMessageType.Info));
                return box;
            }

            foreach (MetricResult m in blocking)
            {
                double ceiling = m.Limit.Ceiling(goal);
                double needed = m.Value - ceiling;
                box.Add(new Label($"{m.Limit.label}: {Format(m.Value)} → at most {Format(ceiling)} (cut {Format(needed)})")
                    { style = { marginTop = 6, unityFontStyleAndWeight = FontStyle.Bold, color = RankColors[(int)m.Rank] } });
                List<Contribution> contributors = PerformanceBudget.Contributors(scan, m.Limit.id);
                if (contributors.Count == 0)
                {
                    box.Add(new Label("  No per-object breakdown for this metric yet.") { style = { opacity = 0.7f } });
                    continue;
                }
                foreach (Contribution c in contributors.Take(6))
                {
                    bool enough = c.Amount >= needed;
                    var line = new Label($"  {(enough ? "✔" : "•")} {c.Source.name}: {Format(c.Amount)}{(c.Note != null ? $" ({c.Note})" : "")}{(enough ? "  — removing or reducing this alone is enough" : "")}")
                        { tooltip = "Click to ping", style = { whiteSpace = WhiteSpace.Normal } };
                    UnityEngine.Object target2 = c.Source;
                    line.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(target2));
                    box.Add(line);
                }
                if (contributors.Count > 6) box.Add(new Label($"  … {contributors.Count - 6} more") { style = { opacity = 0.7f } });
            }
            box.Add(new Label("Per-object amounts are estimates from the scan; the totals above come from the SDK.") { style = { opacity = 0.6f, marginTop = 4 } });
            return box;
        }

        private static void EmitFindings(ToolboxContext context, AvatarScan scan, List<PlatformResult> results, Prefs prefs)
        {
            PlatformResult r = results.FirstOrDefault(x => x.Platform.id == prefs.platform) ?? results[0];
            var goal = (Rank)Mathf.Clamp(prefs.target, 0, 3);
            context.Findings.Replace(SourceId, r.Blocking(goal).Select(m => new Finding(SourceId,
                m.Rank == Rank.VeryPoor ? Severity.Error : Severity.Warning, scan.Descriptor,
                $"{r.Platform.name}: {m.Limit.label} {Format(m.Value)} is {Nice(m.Rank)} (target {Nice(goal)} ≤ {Format(m.Limit.Ceiling(goal))})",
                "Open the Performance Budget page for the objects that contribute most.", ruleId: SourceId + "." + m.Limit.id)));
        }

        private static VisualElement Row(string name, string[] values, bool bold = false)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            row.Add(new Label(name) { style = { width = 190, unityFontStyleAndWeight = style } });
            foreach (string v in values) row.Add(new Label(v) { style = { width = values.Length == 1 ? new StyleLength(StyleKeyword.Auto) : new StyleLength(170), flexShrink = 1, whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = style } });
            return row;
        }

        private static string Nice(Rank r) => r == Rank.VeryPoor ? "Very Poor" : r.ToString();
        private static string Format(double v) => Math.Abs(v - Math.Round(v)) < 0.001 ? $"{v:#,0}" : $"{v:#,0.0}";
    }
}
