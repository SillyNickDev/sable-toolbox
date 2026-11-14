using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using Sable.Toolbox.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Materials
{
    /// <summary>Material manager: inventory table, bulk edit, presets, lock management, texture replace.</summary>
    public sealed class MaterialManagerModule : IToolboxModule
    {
        public const string AuditSourceId = "studio.sable.toolbox.materials.audit";
        public string Id => "studio.sable.toolbox.materials.manager";
        public string DisplayName => "Materials";
        public int Order => 105;

        /// <summary>Copied property values, shared across windows for the session.</summary>
        private static List<PropertyValue> clipboard = new List<PropertyValue>();
        private static string clipboardFamily = "";

        private sealed class Item
        {
            public MaterialRow Row;
            public string Group;
            public int Count;
        }

        public VisualElement CreateView(ToolboxContext context) => new View(context).Root;

        private sealed class View
        {
            public readonly VisualElement Root = new VisualElement();
            private readonly ToolboxContext context;
            private List<MaterialRow> rows = new List<MaterialRow>();
            private readonly HashSet<Material> selected = new HashSet<Material>();
            private string filter = "";
            private bool problemsOnly;
            private MaterialInventory.Grouping grouping = MaterialInventory.Grouping.Shader;
            private readonly MultiColumnTreeView tree;
            private readonly Label counts = new Label();
            private readonly VisualElement panel = new VisualElement { style = { marginTop = 6 } };
            private string tab = "Edit";
            private string propertySearch = "";
            private readonly HashSet<string> checkedProps = new HashSet<string>();
            private bool rebuildingTree;

            public View(ToolboxContext context)
            {
                this.context = context;

                var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
                var search = new ToolbarSearchField { style = { flexGrow = 1 }, tooltip = "Filter by material, shader, family or keyword" };
                search.RegisterValueChangedCallback(e => { filter = e.newValue; RebuildTree(); });
                var group = new DropdownField(Enum.GetNames(typeof(MaterialInventory.Grouping)).ToList(), (int)grouping) { tooltip = "Group by" };
                group.RegisterValueChangedCallback(_ => { grouping = (MaterialInventory.Grouping)group.index; RebuildTree(); });
                var problems = new Toggle("Problems only") { tooltip = "Missing shaders, stale locks, unlocked Poiyomi" };
                problems.RegisterValueChangedCallback(e => { problemsOnly = e.newValue; RebuildTree(); });
                bar.Add(search); bar.Add(new Label("Group")); bar.Add(group); bar.Add(problems);
                Root.Add(bar);
                Root.Add(counts);

                tree = new MultiColumnTreeView { selectionType = SelectionType.Multiple, sortingEnabled = true, style = { height = 280 } };
                AddColumn("Material", 200, i => i.Row == null ? $"{i.Group}  ({i.Count})" : i.Row.Material.name, null);
                AddColumn("Shader", 170, i => i.Row?.ShaderName, r => r.ShaderName);
                AddColumn("Version", 60, i => i.Row?.Version, r => r.Version);
                AddColumn("Lock", 70, i => i.Row == null ? "" : i.Row.Lock == LockState.NotLockable ? "—" : i.Row.Lock.ToString(), r => r.Lock.ToString());
                AddColumn("Queue", 50, i => i.Row?.RenderQueue.ToString(), r => r.RenderQueue.ToString("D5"));
                AddColumn("Slots", 150, i => i.Row == null ? "" : i.Row.Slots.Count == 0 ? "(animation only)" : string.Join(", ", i.Row.Slots), r => r.Slots.Count.ToString("D4"));
                AddColumn("Tex", 40, i => i.Row?.Textures.Count.ToString(), r => r.Textures.Count.ToString("D4"));
                AddColumn("VRAM", 70, i => i.Row == null ? "" : $"{i.Row.VramMB:0.0} MB", r => r.VramMB.ToString("000000.000"));
                AddColumn("Flags", 140, i => i.Row == null ? "" : Flags(i.Row), Flags);
                tree.columnSortingChanged += RebuildTree;
                tree.selectionChanged += _ =>
                {
                    if (rebuildingTree) return;
                    selected.Clear();
                    foreach (int index in tree.selectedIndices)
                        if (tree.GetItemDataForIndex<Item>(index)?.Row is MaterialRow r) selected.Add(r.Material);
                    if (selected.Count == 1) EditorGUIUtility.PingObject(selected.First());
                    BuildPanel();
                };
                tree.itemsChosen += items =>
                {
                    if (items.OfType<Item>().FirstOrDefault()?.Row is MaterialRow r)
                    {
                        Selection.activeObject = r.Material; // shows the shader's own inspector
                        EditorApplication.ExecuteMenuItem("Window/General/Inspector");
                    }
                };
                Root.Add(tree);

                var tabs = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
                foreach (string name in new[] { "Edit", "Presets", "Locking", "Textures" })
                {
                    string n = name;
                    tabs.Add(new Button(() => { tab = n; BuildPanel(); }) { text = n });
                }
                Root.Add(tabs);
                Root.Add(panel);

                Root.RegisterCallback<AttachToPanelEvent>(_ => { context.Changed += Refresh; Refresh(); });
                Root.RegisterCallback<DetachFromPanelEvent>(_ => context.Changed -= Refresh);
            }

            private void AddColumn(string title, float width, Func<Item, string> text, Func<MaterialRow, string> sortKey)
            {
                var column = new Column
                {
                    name = title, title = title, width = width, sortable = sortKey != null || title == "Material",
                    makeCell = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 2 } },
                    bindCell = (e, i) =>
                    {
                        Item item = tree.GetItemDataForIndex<Item>(i);
                        var label = (Label)e;
                        label.text = item == null ? "" : text(item) ?? "";
                        label.style.unityFontStyleAndWeight = item?.Row == null ? FontStyle.Bold : FontStyle.Normal;
                        label.style.color = item?.Row != null && (item.Row.MissingShader || item.Row.Lock == LockState.Stale)
                            ? new StyleColor(new Color(0.95f, 0.4f, 0.4f)) : new StyleColor(StyleKeyword.Null);
                    }
                };
                tree.columns.Add(column);
            }

            private static string Flags(MaterialRow r)
            {
                var f = new List<string>();
                if (r.MissingShader) f.Add("missing shader");
                if (r.AnimatedSwap) f.Add("animated swap");
                if (r.RenderQueue > 2500) f.Add("transparent");
                if (r.Lock == LockState.Unlocked) f.Add("unlocked");
                return string.Join(", ", f);
            }

            private void Refresh()
            {
                AvatarScan scan = context.Scan;
                rows = scan == null ? new List<MaterialRow>() : MaterialInventory.Build(scan);
                selected.RemoveWhere(m => m == null || rows.All(r => r.Material != m));
                RebuildTree();
                if (tab != "Edit") BuildPanel(); // the edit panel keeps focus while dragging values
                EmitAuditFindings(scan);
            }

            private IEnumerable<MaterialRow> Visible() => rows.Where(r => r.Matches(filter) &&
                (!problemsOnly || r.MissingShader || r.Lock == LockState.Stale || r.Lock == LockState.Unlocked));

            private void RebuildTree()
            {
                List<MaterialRow> visible = Visible().ToList();
                SortColumnDescription sort = tree.sortedColumns?.FirstOrDefault();
                Func<MaterialRow, string> key = r => r.Material.name;
                if (sort != null)
                {
                    key = sort.columnName switch
                    {
                        "Shader" => r => r.ShaderName, "Version" => r => r.Version, "Lock" => r => r.Lock.ToString(),
                        "Queue" => r => r.RenderQueue.ToString("D5"), "Slots" => r => r.Slots.Count.ToString("D4"),
                        "Tex" => r => r.Textures.Count.ToString("D4"), "VRAM" => r => r.VramMB.ToString("000000.000"),
                        "Flags" => Flags, _ => r => r.Material.name
                    };
                }
                bool desc = sort != null && sort.direction == SortDirection.Descending;
                IEnumerable<MaterialRow> Sorted(IEnumerable<MaterialRow> x) => desc ? x.OrderByDescending(key) : x.OrderBy(key);

                int id = 0;
                var reselect = new List<int>();
                TreeViewItemData<Item> Leaf(MaterialRow r)
                {
                    if (selected.Contains(r.Material)) reselect.Add(id);
                    return new TreeViewItemData<Item>(id++, new Item { Row = r });
                }
                var items = new List<TreeViewItemData<Item>>();
                if (grouping == MaterialInventory.Grouping.None)
                    items.AddRange(Sorted(visible).Select(Leaf).ToList());
                else
                    foreach (IGrouping<string, MaterialRow> g in visible.GroupBy(r => MaterialInventory.GroupKey(r, grouping)).OrderBy(g => g.Key))
                    {
                        var children = Sorted(g).Select(Leaf).ToList();
                        items.Add(new TreeViewItemData<Item>(id++, new Item { Group = g.Key, Count = children.Count }, children));
                    }

                rebuildingTree = true;
                tree.SetRootItems(items);
                tree.Rebuild();
                tree.ExpandAll();
                tree.SetSelectionByIdWithoutNotify(reselect);
                rebuildingTree = false;

                double vram = rows.Sum(r => r.VramMB);
                counts.text = context.Scan == null ? "Pick an avatar above."
                    : $"{rows.Count} materials ({rows.Count(r => r.AnimatedSwap)} animated swaps), {visible.Count} shown · " +
                      $"{rows.Count(r => r.Lock == LockState.Unlocked)} unlocked · {rows.Count(r => r.MissingShader)} missing shader · ≈{vram:0.0} MB textures (shared textures counted per material)";
            }

            // ------------------------------------------------------------------ panels

            private void BuildPanel()
            {
                panel.Clear();
                switch (tab)
                {
                    case "Edit": EditPanel(); break;
                    case "Presets": PresetPanel(); break;
                    case "Locking": LockPanel(); break;
                    case "Textures": TexturePanel(); break;
                }
            }

            private List<Material> Selected() => selected.Where(m => m != null).ToList();

            private void EditPanel()
            {
                List<Material> sel = Selected();
                if (sel.Count == 0) { panel.Add(new HelpBox("Select one or more materials in the table to edit them together.", HelpBoxMessageType.Info)); return; }
                List<Material> editable = MaterialEdits.Editable(sel).ToList();
                int locked = sel.Count - editable.Count;
                panel.Add(new Label($"{sel.Count} selected" + (locked > 0 ? $" · {locked} locked (unlock them to edit)" : "")) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                if (editable.Count == 0) return;

                // Semantic aliases: one control drives each family's own property.
                var aliases = PropertyAliases.Aliases.Where(a => editable.Any(m => Has(m, a.PropertyFor(PropertyAliases.FamilyOf(m))))).ToList();
                if (aliases.Count > 0)
                {
                    var fold = new Foldout { text = "Across shader families", value = true };
                    foreach (PropertyAlias alias in aliases)
                    {
                        Material first = editable.First(m => Has(m, alias.PropertyFor(PropertyAliases.FamilyOf(m))));
                        PropertyValue initial = PropertyValue.Read(first, alias.PropertyFor(PropertyAliases.FamilyOf(first)));
                        int targets = editable.Count(m => Has(m, alias.PropertyFor(PropertyAliases.FamilyOf(m))));
                        fold.Add(Control($"{alias.label}  ({targets})", initial, v => MaterialEdits.SetAlias(editable, alias, v)));
                    }
                    panel.Add(fold);
                }

                // Properties every selected material shares (same name and type).
                Material head = editable[0];
                var shared = new List<string>();
                for (int i = 0; i < head.shader.GetPropertyCount(); i++)
                {
                    string name = head.shader.GetPropertyName(i);
                    if ((head.shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
                    ShaderPropertyType type = head.shader.GetPropertyType(i);
                    if (editable.All(m => { int j = m.shader.FindPropertyIndex(name); return j >= 0 && m.shader.GetPropertyType(j) == type; }))
                        shared.Add(name);
                }

                var props = new Foldout { text = $"Shared properties ({shared.Count})", value = true };
                var search = new ToolbarSearchField { value = propertySearch, style = { width = StyleKeyword.Auto } };
                var list = new VisualElement();
                void Fill()
                {
                    list.Clear();
                    var match = shared.Where(n => string.IsNullOrEmpty(propertySearch) || n.IndexOf(propertySearch, StringComparison.OrdinalIgnoreCase) >= 0
                        || head.shader.GetPropertyDescription(head.shader.FindPropertyIndex(n)).IndexOf(propertySearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    foreach (string name in match.Take(80))
                    {
                        string n = name;
                        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                        var check = new Toggle { value = checkedProps.Contains(n), tooltip = "Include in Copy" };
                        check.RegisterValueChangedCallback(e => { if (e.newValue) checkedProps.Add(n); else checkedProps.Remove(n); });
                        row.Add(check);
                        string label = head.shader.GetPropertyDescription(head.shader.FindPropertyIndex(n));
                        bool mixed = editable.Select(m => PropertyValue.Read(m, n)?.ToString()).Distinct().Count() > 1;
                        VisualElement control = Control($"{Trim(label)} ({n}){(mixed ? " — mixed" : "")}", PropertyValue.Read(head, n), v => MaterialEdits.Set(editable, v));
                        control.style.flexGrow = 1;
                        row.Add(control);
                        list.Add(row);
                    }
                    if (match.Count > 80) list.Add(new Label($"… {match.Count - 80} more; narrow the search.") { style = { opacity = 0.7f } });
                }
                search.RegisterValueChangedCallback(e => { propertySearch = e.newValue; Fill(); });
                props.Add(search);
                props.Add(list);
                Fill();
                panel.Add(props);

                var copy = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
                copy.Add(new Button(() =>
                {
                    clipboard = checkedProps.Select(n => PropertyValue.Read(head, n)).Where(v => v != null).ToList();
                    clipboardFamily = PropertyAliases.FamilyOf(head);
                    BuildPanel();
                }) { text = $"Copy {checkedProps.Count} ticked from '{head.name}'" });
                copy.Add(new Button(() =>
                {
                    int n = MaterialEdits.Paste(editable, clipboard);
                    Debug.Log($"Sable Toolbox: pasted {n} property value(s) onto {editable.Count} material(s).");
                }) { text = $"Paste {clipboard.Count} to selected" });
                panel.Add(copy);
            }

            private void PresetPanel()
            {
                var folderRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                folderRow.Add(new Label("Library: " + PresetLibrary.Folder) { style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden } });
                folderRow.Add(new Button(() =>
                {
                    string f = EditorUtility.OpenFolderPanel("Preset library folder", PresetLibrary.Folder, "");
                    if (!string.IsNullOrEmpty(f)) { PresetLibrary.Folder = f; BuildPanel(); }
                }) { text = "Change…" });
                folderRow.Add(new Button(() => { Directory.CreateDirectory(PresetLibrary.Folder); EditorUtility.RevealInFinder(PresetLibrary.Folder); }) { text = "Open" });
                panel.Add(folderRow);

                List<MaterialPreset> presets = PresetLibrary.LoadAll();
                List<Material> sel = Selected();
                if (presets.Count > 0)
                {
                    var pick = new DropdownField("Preset", presets.Select(p => $"{p.name}  [{string.Join(", ", p.families.Select(f => f.family))}]").ToList(), 0);
                    panel.Add(pick);
                    var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    MaterialPreset Current() => presets[Math.Max(0, pick.index)];
                    var apply = new Button(() =>
                    {
                        int n = Current().ApplyTo(sel);
                        Debug.Log($"Sable Toolbox: preset '{Current().name}' wrote {n} value(s) on {sel.Count} material(s).");
                    }) { text = $"Apply to {sel.Count} selected" };
                    apply.SetEnabled(sel.Count > 0);
                    actions.Add(apply);
                    actions.Add(new Button(() =>
                    {
                        string path = EditorUtility.SaveFilePanel("Export preset", "", PresetLibrary.FileName(Current().name), "json");
                        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, JsonUtility.ToJson(Current(), true));
                    }) { text = "Export…" });
                    actions.Add(new Button(() =>
                    {
                        if (!EditorUtility.DisplayDialog("Delete preset", $"Delete '{Current().name}' from the library folder? This cannot be undone.", "Delete", "Cancel")) return;
                        PresetLibrary.Delete(Current());
                        BuildPanel();
                    }) { text = "Delete" });
                    panel.Add(actions);
                }
                else panel.Add(new HelpBox("No presets yet. Copy properties in the Edit tab, then save them here.", HelpBoxMessageType.Info));

                var save = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
                var name = new TextField("Save copied as") { style = { flexGrow = 1 } };
                var saveButton = new Button(() =>
                {
                    if (string.IsNullOrWhiteSpace(name.value) || clipboard.Count == 0) return;
                    MaterialPreset preset = presets.FirstOrDefault(p => p.name == name.value.Trim()) ?? new MaterialPreset { name = name.value.Trim() };
                    preset.Set(clipboardFamily, clipboard);
                    PresetLibrary.Save(preset);
                    BuildPanel();
                }) { text = $"Save ({clipboard.Count} {clipboardFamily} values)", tooltip = "Saving under an existing name adds or replaces that shader family's values." };
                saveButton.SetEnabled(clipboard.Count > 0);
                save.Add(name); save.Add(saveButton);
                panel.Add(save);
                panel.Add(new Button(() =>
                {
                    string file = EditorUtility.OpenFilePanel("Import preset", "", "json");
                    if (string.IsNullOrEmpty(file)) return;
                    if (PresetLibrary.Import(file) == null) EditorUtility.DisplayDialog("Import preset", "That file is not a Sable material preset.", "OK");
                    BuildPanel();
                }) { text = "Import…", style = { alignSelf = Align.FlexStart } });
            }

            private void LockPanel()
            {
                if (!PoiyomiBridge.IsAvailable)
                {
                    panel.Add(new HelpBox("Poiyomi is not installed in this project, so there is nothing to lock.", HelpBoxMessageType.Info));
                    return;
                }
                panel.Add(new Label($"{rows.Count(r => r.Lock == LockState.Unlocked)} unlocked · {rows.Count(r => r.Lock == LockState.Locked)} locked · {rows.Count(r => r.Lock == LockState.Stale)} stale"));
                List<Material> sel = Selected();
                var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                void Lock(IEnumerable<Material> mats, bool state)
                {
                    PoiyomiBridge.SetLocked(mats.ToList(), state);
                    ScanCache.Invalidate();
                }
                actions.Add(new Button(() => Lock(sel, true)) { text = $"Lock {sel.Count} selected" });
                actions.Add(new Button(() => Lock(sel, false)) { text = $"Unlock {sel.Count} selected" });
                List<Material> unlocked = rows.Where(r => r.Lock == LockState.Unlocked).Select(r => r.Material).ToList();
                var all = new Button(() => Lock(unlocked, true)) { text = $"Lock all {unlocked.Count} unlocked" };
                all.SetEnabled(unlocked.Count > 0);
                actions.Add(all);
                List<Material> stale = rows.Where(r => r.Lock == LockState.Stale).Select(r => r.Material).ToList();
                var relock = new Button(() => { PoiyomiBridge.SetLocked(stale, false); PoiyomiBridge.SetLocked(stale, true); ScanCache.Invalidate(); })
                    { text = $"Relock {stale.Count} stale", tooltip = "Stale: locked, but the generated locked shader is missing or broken." };
                relock.SetEnabled(stale.Count > 0);
                actions.Add(relock);
                panel.Add(actions);

                List<AnimatedPropertyIssue> issues = context.Scan == null ? new List<AnimatedPropertyIssue>() : AnimatedPropertyAudit.Run(context.Scan);
                panel.Add(new Label($"Animated-property audit: {issues.Count} issue(s)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
                panel.Add(new Label("Properties an animation changes must be marked animated before locking, or they stop animating (dissolve toggles).") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.75f } });
                foreach (AnimatedPropertyIssue issue in issues.Take(50))
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.Add(new Label($"{issue.Material.name}: {issue.Property} (in '{issue.Clip.name}'){(issue.AlreadyLocked ? " — already locked, not animating" : "")}") { style = { flexGrow = 1, flexShrink = 1 } });
                    row.Add(new Button(() => { AnimatedPropertyAudit.MarkAnimated(issue); ScanCache.Invalidate(); }) { text = "Mark animated" });
                    panel.Add(row);
                }
                if (issues.Count > 1)
                    panel.Add(new Button(() => { foreach (var i in issues) AnimatedPropertyAudit.MarkAnimated(i); ScanCache.Invalidate(); })
                        { text = $"Mark all {issues.Count}", style = { alignSelf = Align.FlexStart } });
            }

            private void TexturePanel()
            {
                var find = new ObjectField("Find") { objectType = typeof(Texture) };
                var replace = new ObjectField("Replace with") { objectType = typeof(Texture) };
                var onlySelected = new Toggle("Selected materials only");
                var result = new Label();
                panel.Add(find); panel.Add(replace); panel.Add(onlySelected);
                panel.Add(new Button(() =>
                {
                    IEnumerable<Material> scope = onlySelected.value ? Selected() : rows.Select(r => r.Material);
                    int n = MaterialEdits.ReplaceTexture(scope, find.value as Texture, replace.value as Texture);
                    result.text = $"Replaced {n} texture slot(s). Locked materials were skipped.";
                }) { text = "Replace", style = { alignSelf = Align.FlexStart } });
                panel.Add(result);
            }

            private void EmitAuditFindings(AvatarScan scan)
            {
                if (scan == null || !PoiyomiBridge.IsAvailable) { context.Findings.Clear(AuditSourceId); return; }
                context.Findings.Replace(AuditSourceId, AnimatedPropertyAudit.Run(scan).Select(i => new Finding(AuditSourceId,
                    i.AlreadyLocked ? Severity.Error : Severity.Warning, i.Material,
                    $"'{i.Material.name}': {i.Property} is animated by '{i.Clip.name}' but not marked animated",
                    i.AlreadyLocked ? "The material is locked, so this property no longer animates in-game." : "It will stop animating once the material is locked.",
                    new FixAction("Mark animated", $"Tags {i.Property} as animated on '{i.Material.name}'" + (i.AlreadyLocked ? " (unlocks and relocks it)." : "."),
                        () => AnimatedPropertyAudit.MarkAnimated(i)))));
            }

            // ------------------------------------------------------------------ helpers

            private static bool Has(Material m, string property) => property != null && m.shader.FindPropertyIndex(property) >= 0;

            private static string Trim(string label)
            {
                int cut = label.IndexOf("--", StringComparison.Ordinal); // Thry puts options after "--"
                label = cut >= 0 ? label.Substring(0, cut) : label;
                return System.Text.RegularExpressions.Regex.Replace(label, "<.*?>", "");
            }

            /// <summary>An editor control for a property value; onChange gets a new value of the same name and type.</summary>
            private static VisualElement Control(string label, PropertyValue initial, Action<PropertyValue> onChange)
            {
                if (initial == null) return new Label(label + ": (unsupported)");
                PropertyValue With(Action<PropertyValue> set) { var v = new PropertyValue { name = initial.name, type = initial.type }; set(v); return v; }
                switch (initial.type)
                {
                    case "Color":
                        var color = new ColorField(label) { value = initial.color, hdr = true };
                        color.RegisterValueChangedCallback(e => onChange(With(v => v.color = e.newValue)));
                        return color;
                    case "Vector":
                        var vector = new Vector4Field(label) { value = initial.vector };
                        vector.RegisterValueChangedCallback(e => onChange(With(v => v.vector = e.newValue)));
                        return vector;
                    case "Texture":
                        string path = string.IsNullOrEmpty(initial.textureGuid) ? null : AssetDatabase.GUIDToAssetPath(initial.textureGuid);
                        var tex = new ObjectField(label) { objectType = typeof(Texture), value = path == null ? null : AssetDatabase.LoadAssetAtPath<Texture>(path) };
                        tex.RegisterValueChangedCallback(e => onChange(With(v => v.textureGuid = e.newValue == null ? "" :
                            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.newValue)))));
                        return tex;
                    default:
                        var number = new FloatField(label) { value = initial.number, isDelayed = false };
                        number.RegisterValueChangedCallback(e => onChange(With(v => v.number = e.newValue)));
                        return number;
                }
            }
        }
    }
}
