// BlendShapeCopier.cs
//
// Copies blend shape weights from one source (a SkinnedMeshRenderer, or a saved
// JSON preset) onto any number of target SkinnedMeshRenderers, with name matching
// options, filtering, a dry-run preview, and undo/prefab-safe application.
//
// Moved in from the standalone BlendShapeCopierWindow. The state and IMGUI drawing
// live here so the same tool can be hosted by its own window (BlendShapeCopierWindow)
// and by the Sable Toolbox window (BlendShapeCopierModule).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Sable.Toolbox.Rigging
{
    [Serializable]
    public class BlendShapeCopier
    {
        // ---------------------------------------------------------------- types

        public enum MatchMode
        {
            ExactName,      // mesh.GetBlendShapeIndex(name)
            IgnoreCase,     // case-insensitive name match
            Normalized,     // case-insensitive, punctuation/space stripped, common prefixes removed
            Index           // positional, meshes must share ordering
        }

        [Serializable]
        public class ShapeWeight
        {
            public string name;
            public float weight;
        }

        [Serializable]
        public class WeightPreset
        {
            public string label;
            public string savedFrom;
            public List<ShapeWeight> shapes = new List<ShapeWeight>();
        }

        private enum EntryStatus
        {
            Copy,        // will change
            Unchanged,   // already equal
            NoMatch,     // no corresponding shape on target
            Filtered,    // excluded by the name filter
            ZeroSkipped, // source weight is 0 and "skip zero weights" is on
            Cleared      // target-only shape that will be zeroed
        }

        private class Entry
        {
            public string sourceName;
            public string targetName;
            public int targetIndex = -1;
            public float sourceWeight;
            public float currentWeight;
            public EntryStatus status;
        }

        private class TargetPlan
        {
            public SkinnedMeshRenderer renderer;
            public readonly List<Entry> entries = new List<Entry>();
            public int copy, unchanged, noMatch, cleared;
            public bool expanded = true;
        }

        // --------------------------------------------------------------- fields

        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> targets = new List<SkinnedMeshRenderer>();

        [SerializeField] private bool useSourcePreset;
        [SerializeField] private WeightPreset loadedPreset;

        [SerializeField] private MatchMode matchMode = MatchMode.ExactName;
        [SerializeField] private bool fallbackToIndex;          // if a name match fails, try the same index
        [SerializeField] private bool skipZeroWeights;          // don't push 0 values over existing values
        [SerializeField] private bool zeroUnmatchedOnTarget;    // reset target shapes the source doesn't define
        [SerializeField] private bool includeInactiveOnScan = true;

        [SerializeField] private string nameFilter = "";
        [SerializeField] private bool filterIsRegex;

        [SerializeField] private bool showPreview = true;
        [SerializeField] private bool hideNoOpRows = true;

        private ReorderableList targetList;
        private Vector2 scrollPos;
        private readonly List<TargetPlan> plans = new List<TargetPlan>();
        private bool planDirty = true;
        private string filterError;

        private const int MaxPreviewRowsPerTarget = 200;

        private static readonly string[] StripPrefixes =
        {
            "vrc.", "vrc_", "blendshape.", "key_", "shape_", "mmd_", "bs_"
        };

        // ------------------------------------------------------------ host API

        /// <summary>Raised when the host should repaint (undo/redo changed live weights).</summary>
        [NonSerialized] public Action RepaintRequested;

        public void SetSource(SkinnedMeshRenderer renderer)
        {
            sourceRenderer = renderer;
            useSourcePreset = false;
            planDirty = true;
        }

        public void AddTargets(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            foreach (SkinnedMeshRenderer smr in renderers) AddTarget(smr);
            BuildTargetList();
            planDirty = true;
        }

        /// <summary>Call when the live weights may have changed outside the tool.</summary>
        public void MarkDirty() => planDirty = true;

        // ------------------------------------------------------------- lifecycle

        public void Enable()
        {
            BuildTargetList();
            planDirty = true;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void Disable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            planDirty = true;
            RepaintRequested?.Invoke();
        }

        private void BuildTargetList()
        {
            targetList = new ReorderableList(targets, typeof(SkinnedMeshRenderer), true, true, true, true);

            targetList.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, $"Targets ({targets.Count(t => t != null)})");

            targetList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;

                EditorGUI.BeginChangeCheck();
                SkinnedMeshRenderer value = (SkinnedMeshRenderer)EditorGUI.ObjectField(
                    rect, targets[index], typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck())
                {
                    targets[index] = value;
                    planDirty = true;
                }
            };

            targetList.onAddCallback = list =>
            {
                targets.Add(null);
                planDirty = true;
            };

            targetList.onRemoveCallback = list =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                planDirty = true;
            };
        }

        // -------------------------------------------------------------------- UI

        public void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            DrawSourceSection();
            EditorGUILayout.Space();
            DrawTargetSection();
            EditorGUILayout.Space();
            DrawOptionsSection();
            EditorGUILayout.Space();
            DrawActionSection();
            EditorGUILayout.Space();
            DrawPreviewSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            useSourcePreset = EditorGUILayout.Toggle("Use saved preset", useSourcePreset);

            if (useSourcePreset)
            {
                string label = loadedPreset == null
                    ? "No preset loaded"
                    : $"{loadedPreset.label}  ({loadedPreset.shapes.Count} shapes)";
                EditorGUILayout.LabelField("Preset", label);
            }
            else
            {
                sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    "Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);

                if (sourceRenderer != null && sourceRenderer.sharedMesh != null)
                {
                    EditorGUILayout.LabelField(" ", $"{sourceRenderer.sharedMesh.blendShapeCount} blend shapes");
                }
            }
            if (EditorGUI.EndChangeCheck()) planDirty = true;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(useSourcePreset || sourceRenderer == null))
                {
                    if (GUILayout.Button("Save Preset...")) SavePreset();
                }
                if (GUILayout.Button("Load Preset...")) LoadPreset();
            }
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);

            if (targetList == null) BuildTargetList();
            targetList.DoLayoutList();

            DrawDropArea();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add From Selection")) AddFromSelection();

                using (new EditorGUI.DisabledScope(sourceRenderer == null))
                {
                    if (GUILayout.Button("Add Siblings Of Source")) AddSiblingsOfSource();
                }

                using (new EditorGUI.DisabledScope(targets.Count == 0))
                {
                    if (GUILayout.Button("Clear"))
                    {
                        targets.Clear();
                        BuildTargetList();
                        planDirty = true;
                    }
                }
            }
        }

        private void DrawDropArea()
        {
            Rect drop = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop GameObjects or renderers here  (children included)", EditorStyles.helpBox);

            Event e = Event.current;
            if (!drop.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences) AddTargetsFrom(obj);
                BuildTargetList();
                planDirty = true;
                e.Use();
            }
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField("Matching", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            matchMode = (MatchMode)EditorGUILayout.EnumPopup("Match mode", matchMode);

            using (new EditorGUI.DisabledScope(matchMode == MatchMode.Index))
            {
                fallbackToIndex = EditorGUILayout.ToggleLeft(
                    "Fall back to matching index when the name is missing", fallbackToIndex);
            }

            skipZeroWeights = EditorGUILayout.ToggleLeft(
                "Skip source shapes whose weight is 0", skipZeroWeights);
            zeroUnmatchedOnTarget = EditorGUILayout.ToggleLeft(
                "Zero target shapes the source doesn't define", zeroUnmatchedOnTarget);
            includeInactiveOnScan = EditorGUILayout.ToggleLeft(
                "Include inactive renderers when scanning hierarchies", includeInactiveOnScan);

            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                nameFilter = EditorGUILayout.TextField("Name filter", nameFilter);
                filterIsRegex = GUILayout.Toggle(filterIsRegex, ".*", EditorStyles.miniButton, GUILayout.Width(30f));
            }
            EditorGUILayout.LabelField(" ", filterIsRegex
                ? "Regex. Empty matches everything."
                : "Comma-separated substrings, any match passes. Empty matches everything.",
                EditorStyles.miniLabel);

            if (EditorGUI.EndChangeCheck()) planDirty = true;

            if (!string.IsNullOrEmpty(filterError))
            {
                EditorGUILayout.HelpBox(filterError, MessageType.Warning);
            }

            if (matchMode == MatchMode.Index)
            {
                EditorGUILayout.HelpBox(
                    "Index matching ignores names entirely. Only safe when source and target were exported together.",
                    MessageType.Info);
            }
        }

        private void DrawActionSection()
        {
            if (planDirty) RebuildPlans();

            int totalCopy = plans.Sum(p => p.copy);
            int totalCleared = plans.Sum(p => p.cleared);
            int totalNoMatch = plans.Sum(p => p.noMatch);

            bool canApply = plans.Count > 0 && (totalCopy > 0 || totalCleared > 0);

            using (new EditorGUI.DisabledScope(!canApply))
            {
                string label = totalCleared > 0
                    ? $"Apply  ({totalCopy} to copy, {totalCleared} to clear, across {plans.Count} target(s))"
                    : $"Apply  ({totalCopy} weight(s) across {plans.Count} target(s))";

                if (GUILayout.Button(label, GUILayout.Height(28f))) Apply();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(plans.Count == 0))
                {
                    if (GUILayout.Button("Zero All Targets")) ZeroAllTargets();
                    if (GUILayout.Button("Select Targets"))
                        Selection.objects = plans.Select(p => (UnityEngine.Object)p.renderer.gameObject).ToArray();
                }
                if (GUILayout.Button("Refresh Preview")) planDirty = true;
            }

            if (totalNoMatch > 0 && matchMode == MatchMode.ExactName)
            {
                EditorGUILayout.HelpBox(
                    $"{totalNoMatch} shape(s) had no exact name match. Try Normalized matching if the meshes came from different exports.",
                    MessageType.Info);
            }
        }

        private void DrawPreviewSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                showPreview = EditorGUILayout.Foldout(showPreview, "Preview", true);
                GUILayout.FlexibleSpace();
                hideNoOpRows = GUILayout.Toggle(hideNoOpRows, "Hide unchanged / filtered", EditorStyles.miniButton);
            }

            if (!showPreview) return;

            if (plans.Count == 0)
            {
                EditorGUILayout.HelpBox("Assign a source and at least one target with a valid mesh.", MessageType.None);
                return;
            }

            foreach (TargetPlan plan in plans)
            {
                string header = $"{plan.renderer.name} — {plan.copy} copy, {plan.unchanged} same, {plan.noMatch} missing" +
                                (plan.cleared > 0 ? $", {plan.cleared} clear" : "");

                plan.expanded = EditorGUILayout.Foldout(plan.expanded, header, true);
                if (!plan.expanded) continue;

                EditorGUI.indentLevel++;
                int shown = 0;

                foreach (Entry entry in plan.entries)
                {
                    if (hideNoOpRows &&
                        (entry.status == EntryStatus.Unchanged ||
                         entry.status == EntryStatus.Filtered ||
                         entry.status == EntryStatus.ZeroSkipped))
                    {
                        continue;
                    }

                    if (shown >= MaxPreviewRowsPerTarget)
                    {
                        EditorGUILayout.LabelField($"... and more (preview capped at {MaxPreviewRowsPerTarget} rows)",
                            EditorStyles.miniLabel);
                        break;
                    }

                    DrawEntryRow(entry);
                    shown++;
                }

                if (shown == 0) EditorGUILayout.LabelField("Nothing to do.", EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawEntryRow(Entry entry)
        {
            Color previous = GUI.color;
            string suffix;

            switch (entry.status)
            {
                case EntryStatus.Copy:
                    GUI.color = new Color(0.65f, 1f, 0.65f);
                    suffix = $"{entry.currentWeight:0.##} → {entry.sourceWeight:0.##}";
                    break;
                case EntryStatus.Cleared:
                    GUI.color = new Color(1f, 0.85f, 0.5f);
                    suffix = $"{entry.currentWeight:0.##} → 0  (not in source)";
                    break;
                case EntryStatus.NoMatch:
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    suffix = "no match on target";
                    break;
                case EntryStatus.ZeroSkipped:
                    suffix = "skipped (source is 0)";
                    break;
                case EntryStatus.Filtered:
                    suffix = "filtered out";
                    break;
                default:
                    suffix = $"already {entry.sourceWeight:0.##}";
                    break;
            }

            string name = entry.status == EntryStatus.Cleared ? entry.targetName : entry.sourceName;
            string mapped = entry.targetName != null && entry.targetName != entry.sourceName &&
                            entry.status != EntryStatus.Cleared
                ? $"  ⇢ {entry.targetName}"
                : "";

            EditorGUILayout.LabelField($"{name}{mapped}", suffix);
            GUI.color = previous;
        }

        // -------------------------------------------------------- target helpers

        private void AddFromSelection()
        {
            foreach (UnityEngine.Object obj in Selection.objects) AddTargetsFrom(obj);
            BuildTargetList();
            planDirty = true;
        }

        private void AddSiblingsOfSource()
        {
            if (sourceRenderer == null) return;

            Transform root = sourceRenderer.transform.root;
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactiveOnScan))
            {
                AddTarget(smr);
            }
            BuildTargetList();
            planDirty = true;
        }

        private void AddTargetsFrom(UnityEngine.Object obj)
        {
            GameObject go = obj as GameObject;
            if (go == null)
            {
                Component component = obj as Component;
                if (component != null) go = component.gameObject;
            }
            if (go == null) return;

            foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactiveOnScan))
            {
                AddTarget(smr);
            }
        }

        private void AddTarget(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return;
            if (!useSourcePreset && renderer == sourceRenderer) return;
            if (targets.Contains(renderer)) return;
            targets.Add(renderer);
        }

        // ------------------------------------------------------------- plan build

        private List<ShapeWeight> GetSourceWeights()
        {
            List<ShapeWeight> result = new List<ShapeWeight>();

            if (useSourcePreset)
            {
                if (loadedPreset != null) result.AddRange(loadedPreset.shapes);
                return result;
            }

            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) return result;

            Mesh mesh = sourceRenderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                result.Add(new ShapeWeight
                {
                    name = mesh.GetBlendShapeName(i),
                    weight = sourceRenderer.GetBlendShapeWeight(i)
                });
            }
            return result;
        }

        private void RebuildPlans()
        {
            planDirty = false;
            plans.Clear();
            filterError = null;

            List<ShapeWeight> source = GetSourceWeights();
            if (source.Count == 0) return;

            Regex regex = null;
            if (filterIsRegex && !string.IsNullOrEmpty(nameFilter))
            {
                try
                {
                    regex = new Regex(nameFilter, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    filterError = "Invalid regex: " + ex.Message;
                }
            }

            string[] filterTerms = filterIsRegex || string.IsNullOrEmpty(nameFilter)
                ? new string[0]
                : nameFilter.Split(',')
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0)
                            .ToArray();

            foreach (SkinnedMeshRenderer target in targets.Distinct())
            {
                if (target == null || target.sharedMesh == null) continue;
                if (!useSourcePreset && target == sourceRenderer) continue;

                Mesh mesh = target.sharedMesh;
                TargetPlan plan = new TargetPlan { renderer = target };
                Dictionary<string, int> lookup = BuildLookup(mesh);
                HashSet<int> touched = new HashSet<int>();

                for (int i = 0; i < source.Count; i++)
                {
                    ShapeWeight shape = source[i];
                    Entry entry = new Entry
                    {
                        sourceName = shape.name,
                        sourceWeight = shape.weight
                    };

                    if (!PassesFilter(shape.name, regex, filterTerms))
                    {
                        entry.status = EntryStatus.Filtered;
                        plan.entries.Add(entry);
                        continue;
                    }

                    int targetIndex = FindTargetIndex(mesh, lookup, shape.name, i);
                    if (targetIndex < 0)
                    {
                        entry.status = EntryStatus.NoMatch;
                        plan.noMatch++;
                        plan.entries.Add(entry);
                        continue;
                    }

                    entry.targetIndex = targetIndex;
                    entry.targetName = mesh.GetBlendShapeName(targetIndex);
                    entry.currentWeight = target.GetBlendShapeWeight(targetIndex);
                    touched.Add(targetIndex);

                    if (skipZeroWeights && Mathf.Approximately(shape.weight, 0f))
                    {
                        entry.status = EntryStatus.ZeroSkipped;
                    }
                    else if (Mathf.Approximately(entry.currentWeight, shape.weight))
                    {
                        entry.status = EntryStatus.Unchanged;
                        plan.unchanged++;
                    }
                    else
                    {
                        entry.status = EntryStatus.Copy;
                        plan.copy++;
                    }

                    plan.entries.Add(entry);
                }

                if (zeroUnmatchedOnTarget)
                {
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        if (touched.Contains(i)) continue;

                        float current = target.GetBlendShapeWeight(i);
                        if (Mathf.Approximately(current, 0f)) continue;

                        plan.entries.Add(new Entry
                        {
                            sourceName = mesh.GetBlendShapeName(i),
                            targetName = mesh.GetBlendShapeName(i),
                            targetIndex = i,
                            currentWeight = current,
                            sourceWeight = 0f,
                            status = EntryStatus.Cleared
                        });
                        plan.cleared++;
                    }
                }

                plans.Add(plan);
            }
        }

        private Dictionary<string, int> BuildLookup(Mesh mesh)
        {
            if (matchMode == MatchMode.ExactName || matchMode == MatchMode.Index) return null;

            Dictionary<string, int> lookup = new Dictionary<string, int>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string key = matchMode == MatchMode.Normalized
                    ? Normalize(mesh.GetBlendShapeName(i))
                    : mesh.GetBlendShapeName(i).ToLowerInvariant();

                if (!lookup.ContainsKey(key)) lookup[key] = i; // first occurrence wins
            }
            return lookup;
        }

        private int FindTargetIndex(Mesh mesh, Dictionary<string, int> lookup, string sourceName, int sourceIndex)
        {
            int index = -1;

            switch (matchMode)
            {
                case MatchMode.Index:
                    return sourceIndex < mesh.blendShapeCount ? sourceIndex : -1;

                case MatchMode.ExactName:
                    index = mesh.GetBlendShapeIndex(sourceName);
                    break;

                case MatchMode.IgnoreCase:
                    if (lookup != null && lookup.TryGetValue(sourceName.ToLowerInvariant(), out int ci)) index = ci;
                    break;

                case MatchMode.Normalized:
                    if (lookup != null && lookup.TryGetValue(Normalize(sourceName), out int ni)) index = ni;
                    break;
            }

            if (index < 0 && fallbackToIndex && sourceIndex < mesh.blendShapeCount) index = sourceIndex;

            return index;
        }

        private static bool PassesFilter(string name, Regex regex, string[] terms)
        {
            if (regex != null) return regex.IsMatch(name);
            if (terms == null || terms.Length == 0) return true;

            foreach (string term in terms)
            {
                if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            string lower = value.ToLowerInvariant();

            foreach (string prefix in StripPrefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                {
                    lower = lower.Substring(prefix.Length);
                    break;
                }
            }

            StringBuilder builder = new StringBuilder(lower.Length);
            foreach (char c in lower)
            {
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            }
            return builder.ToString();
        }

        // ------------------------------------------------------------------ apply

        private void Apply()
        {
            if (plans.Count == 0) return;

            UnityEngine.Object[] objects = plans.Select(p => (UnityEngine.Object)p.renderer).ToArray();
            Undo.RecordObjects(objects, "Copy Blend Shape Weights");

            int copied = 0;
            int cleared = 0;

            foreach (TargetPlan plan in plans)
            {
                foreach (Entry entry in plan.entries)
                {
                    if (entry.status == EntryStatus.Copy)
                    {
                        plan.renderer.SetBlendShapeWeight(entry.targetIndex, entry.sourceWeight);
                        copied++;
                    }
                    else if (entry.status == EntryStatus.Cleared)
                    {
                        plan.renderer.SetBlendShapeWeight(entry.targetIndex, 0f);
                        cleared++;
                    }
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(plan.renderer);
                EditorUtility.SetDirty(plan.renderer);
            }

            StringBuilder log = new StringBuilder();
            log.Append($"BlendShape Copier: set {copied} weight(s) across {plans.Count} renderer(s)");
            if (cleared > 0) log.Append($", cleared {cleared}");

            int missing = plans.Sum(p => p.noMatch);
            if (missing > 0) log.Append($", {missing} source shape(s) had no match");

            Debug.Log(log.ToString());
            planDirty = true;
        }

        private void ZeroAllTargets()
        {
            UnityEngine.Object[] objects = plans.Select(p => (UnityEngine.Object)p.renderer).ToArray();
            if (objects.Length == 0) return;

            Undo.RecordObjects(objects, "Zero Blend Shape Weights");

            foreach (TargetPlan plan in plans)
            {
                Mesh mesh = plan.renderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++) plan.renderer.SetBlendShapeWeight(i, 0f);

                PrefabUtility.RecordPrefabInstancePropertyModifications(plan.renderer);
                EditorUtility.SetDirty(plan.renderer);
            }

            planDirty = true;
        }

        // ----------------------------------------------------------------- presets

        private void SavePreset()
        {
            List<ShapeWeight> shapes = GetSourceWeights();
            if (shapes.Count == 0)
            {
                Debug.LogWarning("BlendShape Copier: nothing to save, the source has no blend shapes.");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Save blend shape preset",
                Application.dataPath,
                (sourceRenderer != null ? sourceRenderer.name : "BlendShapes") + "_Preset",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            WeightPreset preset = new WeightPreset
            {
                label = sourceRenderer != null ? sourceRenderer.name : "Preset",
                savedFrom = sourceRenderer != null && sourceRenderer.sharedMesh != null
                    ? sourceRenderer.sharedMesh.name
                    : "",
                shapes = shapes
            };

            File.WriteAllText(path, JsonUtility.ToJson(preset, true));

            if (path.StartsWith(Application.dataPath, StringComparison.Ordinal)) AssetDatabase.Refresh();
            Debug.Log($"BlendShape Copier: saved {shapes.Count} shape(s) to {path}");
        }

        private void LoadPreset()
        {
            string path = EditorUtility.OpenFilePanel("Load blend shape preset", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                WeightPreset preset = JsonUtility.FromJson<WeightPreset>(File.ReadAllText(path));
                if (preset == null || preset.shapes == null)
                {
                    Debug.LogWarning("BlendShape Copier: that file isn't a valid preset.");
                    return;
                }

                loadedPreset = preset;
                useSourcePreset = true;
                planDirty = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("BlendShape Copier: failed to read preset — " + ex.Message);
            }
        }
    }
}
