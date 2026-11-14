using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Qa
{
    /// <summary>Linter page: pick a profile, run, fix or suppress findings, export a report.</summary>
    public sealed class LinterModule : IToolboxModule
    {
        public string Id => "studio.sable.toolbox.qa.linter";
        public string DisplayName => "Linter";
        public int Order => 110;

        public VisualElement CreateView(ToolboxContext context)
        {
            QaSettings settings = QaSettings.Load();
            List<Finding> results = new List<Finding>();
            bool hasRun = false;

            var root = new VisualElement();
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            var profiles = Linter.Profiles.ToList();
            var profile = new DropdownField("Profile", profiles.Select(p => p.name).ToList(),
                Math.Max(0, profiles.FindIndex(p => p.id == settings.activeProfile))) { style = { flexGrow = 1 } };
            var run = new Button { text = "Run" };
            var export = new Button { text = "Export report…" };
            bar.Add(profile); bar.Add(run); bar.Add(export);
            root.Add(bar);

            var options = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            var onSave = new Toggle("Run on save") { value = settings.runOnSave, style = { marginRight = 16 } };
            var gate = new Toggle("Block upload on errors") { value = settings.buildGate,
                tooltip = "Runs the active profile when you build in the VRChat SDK and blocks the build if there are errors." };
            options.Add(onSave); options.Add(gate);
            root.Add(options);

            var summary = new Label { style = { marginTop = 4, marginBottom = 4, unityFontStyleAndWeight = FontStyle.Bold } };
            root.Add(summary);
            var list = new VisualElement();
            root.Add(list);
            var suppressedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            var suppressedLabel = new Label { style = { opacity = 0.7f, flexGrow = 1 } };
            var clearSuppressed = new Button { text = "Clear suppressions" };
            suppressedRow.Add(suppressedLabel); suppressedRow.Add(clearSuppressed);
            root.Add(suppressedRow);

            QaProfile Active() => Linter.Profile(settings.activeProfile);

            void Run()
            {
                list.Clear();
                if (context.Scan == null)
                {
                    summary.text = "";
                    list.Add(new HelpBox("Pick an avatar above.", HelpBoxMessageType.Info));
                    context.Findings.Clear(Linter.SourceId);
                    return;
                }
                results = Linter.Run(context.Scan, Active(), settings);
                hasRun = true;
                context.Findings.Replace(Linter.SourceId, results);
                summary.text = $"{Active().name}: {LintReport.Summary(results)}";
                foreach (Finding f in results) list.Add(Row(f));
                if (results.Count == 0) list.Add(new HelpBox("No findings. This avatar passes the profile.", HelpBoxMessageType.Info));
                suppressedLabel.text = $"{settings.suppressions.Count} suppressed finding(s) in this project";
                clearSuppressed.SetEnabled(settings.suppressions.Count > 0);
            }

            VisualElement Row(Finding f)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.15f), paddingTop = 2, paddingBottom = 2 } };
                row.Add(new Image { image = SeverityIcon(f.Severity), style = { width = 16, height = 16, marginRight = 4 } });
                var text = new Label(f.Message) { tooltip = f.Explanation, style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } };
                text.RegisterCallback<ClickEvent>(_ => { if (f.Location != null) EditorGUIUtility.PingObject(f.Location); });
                row.Add(text);
                if (f.Fix != null)
                    row.Add(new Button(() =>
                    {
                        if (!EditorUtility.DisplayDialog(f.Fix.Label, f.Fix.Description, "Apply", "Cancel")) return;
                        f.Fix.Apply();
                        Sable.Toolbox.Scanning.ScanCache.Invalidate();
                        Run();
                    }) { text = f.Fix.Label, tooltip = f.Fix.Description });
                if (f.Location != null)
                    row.Add(new Button(() => { settings.Suppress(f); Run(); })
                        { text = "Suppress", tooltip = $"Hide '{f.RuleId}' for this object in this project." });
                return row;
            }

            profile.RegisterValueChangedCallback(e =>
            {
                settings.activeProfile = profiles[profile.index].id;
                settings.Save();
                Run();
            });
            onSave.RegisterValueChangedCallback(e => { settings.runOnSave = e.newValue; settings.Save(); });
            gate.RegisterValueChangedCallback(e => { settings.buildGate = e.newValue; settings.Save(); });
            run.clicked += Run;
            clearSuppressed.clicked += () => { settings.suppressions.Clear(); settings.Save(); Run(); };
            export.clicked += () =>
            {
                if (!hasRun) Run();
                if (context.Scan == null) return;
                string avatar = context.Avatar.name;
                string path = EditorUtility.SaveFilePanel("Export lint report", "", $"{avatar} lint report", "html");
                if (string.IsNullOrEmpty(path)) return;
                bool md = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(path, md
                    ? LintReport.Markdown(avatar, Active(), results, DateTime.Now)
                    : LintReport.Html(avatar, Active(), results, DateTime.Now));
                EditorUtility.RevealInFinder(path);
            };

            void OnChanged() { if (hasRun || context.Scan == null) Run(); }
            void OnSaved() { if (settings.runOnSave) Run(); }
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                context.Changed += OnChanged;
                SaveWatcher.Saved += OnSaved;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                context.Changed -= OnChanged;
                SaveWatcher.Saved -= OnSaved;
            });
            summary.text = "Press Run to lint the avatar. Save the report as .html, or type a .md name for Markdown.";
            return root;
        }

        private static Texture SeverityIcon(Severity s) => EditorGUIUtility.IconContent(s switch
        {
            Severity.Error => "console.erroricon.sml",
            Severity.Warning => "console.warnicon.sml",
            _ => "console.infoicon.sml"
        }).image;
    }
}
