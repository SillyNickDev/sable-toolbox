using System.Collections.Generic;
using Sable.Toolbox.Scanning;
using UnityEngine.UIElements;

namespace Sable.Toolbox.UI
{
    /// <summary>Built-in first page: scan counts per category and detected build-time tools.</summary>
    internal sealed class OverviewModule : IToolboxModule
    {
        public string Id => "studio.sable.toolbox.core.overview";
        public string DisplayName => "Overview";
        public int Order => 0;

        public VisualElement CreateView(ToolboxContext context)
        {
            var root = new VisualElement();
            void Refresh()
            {
                root.Clear();
                AvatarScan scan = context.Scan;
                if (scan == null)
                {
                    root.Add(new HelpBox("Pick an avatar (a GameObject with a VRC Avatar Descriptor) above.", HelpBoxMessageType.Info));
                    return;
                }

                root.Add(Header($"{scan.Descriptor.name}"));
                root.Add(new Label($"Scanned in {scan.ScanMilliseconds:0} ms (cached until the avatar or its assets change)") { style = { opacity = 0.7f, marginBottom = 6 } });
                foreach (KeyValuePair<string, int> pair in scan.Counts()) root.Add(Row(pair.Key, pair.Value.ToString()));

                root.Add(Header("Build-time tools"));
                if (scan.BuildTools.Count == 0) root.Add(new Label("None installed or detected."));
                foreach (var report in scan.BuildTools)
                {
                    string state = report.Components.Count > 0
                        ? $"{report.Components.Count} component(s)"
                        : "installed, none on this avatar";
                    root.Add(Row(report.Adapter.DisplayName, state));
                }
            }

            context.Changed += Refresh;
            root.RegisterCallback<DetachFromPanelEvent>(_ => context.Changed -= Refresh);
            root.RegisterCallback<AttachToPanelEvent>(_ => { context.Changed -= Refresh; context.Changed += Refresh; Refresh(); });
            Refresh();
            return root;
        }

        private static Label Header(string text) =>
            new Label(text) { style = { unityFontStyleAndWeight = UnityEngine.FontStyle.Bold, fontSize = 13, marginTop = 8, marginBottom = 2 } };

        private static VisualElement Row(string name, string value)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label(name) { style = { width = 200 } });
            row.Add(new Label(value));
            return row;
        }
    }
}
