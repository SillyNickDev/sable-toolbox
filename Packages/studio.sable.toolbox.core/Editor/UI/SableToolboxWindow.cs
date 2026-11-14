using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Settings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Sable.Toolbox.UI
{
    /// <summary>Tools > Sable Toolbox: avatar picker, module sidebar, module page, findings drawer.</summary>
    public sealed class SableToolboxWindow : EditorWindow
    {
        [SerializeField] private VRCAvatarDescriptor avatar;

        private readonly ToolboxContext context = new ToolboxContext();
        private readonly Dictionary<string, VisualElement> views = new Dictionary<string, VisualElement>();
        private List<IToolboxModule> modules;
        private ListView sidebar;
        private VisualElement page;
        private Foldout drawer;
        private ListView findingList;

        [MenuItem("Tools/Sable Toolbox", priority = 0)]
        public static void Open() => GetWindow<SableToolboxWindow>("Sable Toolbox").minSize = new Vector2(560, 360);

        private void OnEnable()
        {
            ScanCache.Invalidated += OnInvalidated;
            context.Findings.Changed += RefreshFindings;
        }

        private void OnDisable()
        {
            ScanCache.Invalidated -= OnInvalidated;
            context.Findings.Changed -= RefreshFindings;
        }

        private void OnInvalidated()
        {
            if (avatar == null && context.Avatar != null) context.Avatar = null; // avatar was deleted
            context.RaiseChanged();
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // Toolbar: avatar picker.
            var toolbar = new Toolbar();
            var picker = new ObjectField("Avatar") { objectType = typeof(VRCAvatarDescriptor), allowSceneObjects = true };
            picker.style.flexGrow = 1;
            picker.RegisterValueChangedCallback(e => SetAvatar(e.newValue as VRCAvatarDescriptor));
            toolbar.Add(picker);
            toolbar.Add(new ToolbarButton(() => { ScanCache.Invalidate(); }) { text = "Rescan" });
            root.Add(toolbar);

            // Sidebar + page.
            var split = new TwoPaneSplitView(0, 170, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            modules = ModuleRegistry.CreateAll().Where(m => SableToolboxSettings.instance.IsModuleEnabled(m.Id)).ToList();
            sidebar = new ListView(modules, 22, () => new Label(), (e, i) =>
            {
                var label = (Label)e;
                label.text = modules[i].DisplayName;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 6;
            }) { selectionType = SelectionType.Single };
            sidebar.selectionChanged += _ => ShowModule(sidebar.selectedIndex);
            page = new ScrollView();
            page.style.flexGrow = 1;
            page.style.paddingLeft = page.style.paddingRight = 8;
            page.style.paddingTop = 6;
            split.Add(sidebar);
            split.Add(page);
            root.Add(split);

            // Findings drawer.
            drawer = new Foldout { value = false };
            drawer.style.maxHeight = 220;
            drawer.style.borderTopWidth = 1;
            drawer.style.borderTopColor = new Color(0, 0, 0, 0.3f);
            findingList = new ListView(new List<Finding>(), 22, MakeFindingRow, BindFindingRow);
            findingList.style.minHeight = 80;
            findingList.selectionChanged += sel =>
            {
                if (sel.FirstOrDefault() is Finding f && f.Location != null) EditorGUIUtility.PingObject(f.Location);
            };
            drawer.Add(findingList);
            root.Add(drawer);

            // Initial state: restored avatar, else selection, else first in scene.
            if (avatar == null) avatar = PickDefaultAvatar();
            picker.SetValueWithoutNotify(avatar);
            context.Avatar = avatar;
            RefreshFindings();

            int last = modules.FindIndex(m => m.Id == SableToolboxSettings.instance.LastModule);
            sidebar.SetSelection(last >= 0 ? last : 0);
        }

        private void SetAvatar(VRCAvatarDescriptor value)
        {
            avatar = value;
            context.Avatar = value;
        }

        private void ShowModule(int index)
        {
            if (index < 0 || index >= modules.Count) return;
            IToolboxModule module = modules[index];
            SableToolboxSettings.instance.LastModule = module.Id;
            if (!views.TryGetValue(module.Id, out VisualElement view))
            {
                try
                {
                    view = module.CreateView(context) ?? new Label("(module returned no view)");
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    view = new HelpBox($"{module.DisplayName} failed to load: {e.Message}", HelpBoxMessageType.Error);
                }
                views[module.Id] = view;
            }
            page.Clear();
            page.Add(view);
        }

        private static VRCAvatarDescriptor PickDefaultAvatar()
        {
            if (Selection.activeGameObject != null)
            {
                var d = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (d != null) return d;
            }
            return FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        }

        // ---------------------------------------------------------------- findings drawer

        private void RefreshFindings()
        {
            if (drawer == null) return;
            FindingSet set = context.Findings;
            drawer.text = $"Findings  —  {set.CountOf(Severity.Error)} errors, {set.CountOf(Severity.Warning)} warnings, {set.CountOf(Severity.Info)} info";
            findingList.itemsSource = set.ToList();
            findingList.Rebuild();
        }

        private static VisualElement MakeFindingRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.Add(new Image { name = "icon", style = { width = 16, height = 16, marginRight = 4 } });
            row.Add(new Label { name = "msg", style = { flexGrow = 1 } });
            row.Add(new Button { name = "fix" });
            return row;
        }

        private void BindFindingRow(VisualElement row, int i)
        {
            var f = (Finding)findingList.itemsSource[i];
            row.Q<Image>("icon").image = EditorGUIUtility.IconContent(f.Severity switch
            {
                Severity.Error => "console.erroricon.sml",
                Severity.Warning => "console.warnicon.sml",
                _ => "console.infoicon.sml"
            }).image;
            var msg = row.Q<Label>("msg");
            msg.text = f.Location != null ? $"{f.Message}  ({f.Location.name})" : f.Message;
            msg.tooltip = f.Explanation;
            var fix = row.Q<Button>("fix");
            fix.style.display = f.Fix != null ? DisplayStyle.Flex : DisplayStyle.None;
            fix.clickable = new Clickable(() =>
            {
                if (EditorUtility.DisplayDialog(f.Fix.Label, f.Fix.Description, "Apply", "Cancel")) f.Fix.Apply();
            });
            fix.text = f.Fix?.Label ?? "";
            fix.tooltip = f.Fix?.Description ?? "";
        }
    }
}
