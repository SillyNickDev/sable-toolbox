using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Settings
{
    /// <summary>
    /// The one per-project settings file, stored in ProjectSettings/ so it never ends up in an avatar
    /// package. Edit it under Project Settings > Sable Toolbox (changes are undoable).
    /// </summary>
    [FilePath("ProjectSettings/SableToolboxSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SableToolboxSettings : ScriptableSingleton<SableToolboxSettings>
    {
        [Tooltip("Folder for every generated asset. Must be under Assets/ and should end in Generated.")]
        [SerializeField] private string generatedRoot = "Assets/Generated";

        [Tooltip("Module ids hidden from the toolbox window.")]
        [SerializeField] private List<string> disabledModules = new List<string>();

        [SerializeField, HideInInspector] private string lastModule = "";

        public string GeneratedRoot
        {
            get => string.IsNullOrWhiteSpace(generatedRoot) || !generatedRoot.StartsWith("Assets")
                ? "Assets/Generated"
                : generatedRoot;
            set { generatedRoot = value; Save(true); }
        }

        public bool IsModuleEnabled(string id) => !disabledModules.Contains(id);

        public void SetModuleEnabled(string id, bool enabled)
        {
            disabledModules.Remove(id);
            if (!enabled) disabledModules.Add(id);
            Save(true);
        }

        public string LastModule
        {
            get => lastModule;
            set { if (lastModule == value) return; lastModule = value; Save(true); }
        }

        public void SaveSettings() => Save(true);

        // ------------------------------------------------------------ module settings
        // Modules keep their options in this same file (one settings file per project), each as a
        // JSON blob under its module id, so core needs no knowledge of their types.

        [System.Serializable]
        private class ModuleEntry { public string id; public string json; }

        [SerializeField, HideInInspector] private List<ModuleEntry> moduleSettings = new List<ModuleEntry>();

        /// <summary>The module's options, or a new T if none are saved yet. T must be JsonUtility-serializable.</summary>
        public T GetModuleSettings<T>(string moduleId) where T : new()
        {
            ModuleEntry e = moduleSettings.Find(m => m.id == moduleId);
            if (e == null || string.IsNullOrEmpty(e.json)) return new T();
            var value = new T();
            JsonUtility.FromJsonOverwrite(e.json, value);
            return value;
        }

        public void SetModuleSettings<T>(string moduleId, T value)
        {
            UnityEditor.Undo.RecordObject(this, "Change Sable Toolbox settings");
            ModuleEntry e = moduleSettings.Find(m => m.id == moduleId);
            if (e == null) moduleSettings.Add(e = new ModuleEntry { id = moduleId });
            e.json = JsonUtility.ToJson(value);
            Save(true);
        }
    }

    internal static class SableToolboxSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create() => new SettingsProvider("Project/Sable Toolbox", SettingsScope.Project)
        {
            guiHandler = _ =>
            {
                SableToolboxSettings settings = SableToolboxSettings.instance;
                settings.hideFlags &= ~HideFlags.NotEditable;
                var so = new SerializedObject(settings);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(so.FindProperty("generatedRoot"));
                EditorGUILayout.PropertyField(so.FindProperty("disabledModules"), true);
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedProperties(); // registers Undo
                    settings.SaveSettings();
                }
            },
            keywords = new[] { "Sable", "Toolbox", "Generated", "Module" }
        };
    }
}
