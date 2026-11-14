using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Materials
{
    [Serializable]
    public sealed class FamilyValues
    {
        public string family;
        public List<PropertyValue> values = new List<PropertyValue>();
    }

    /// <summary>
    /// A named property subset ("Sable fur lighting", "Matte cloth"), stored per shader family so one
    /// preset can carry a Poiyomi and a lilToon version. One JSON file per preset.
    /// </summary>
    [Serializable]
    public sealed class MaterialPreset
    {
        public int version = 1;
        public string name;
        public List<FamilyValues> families = new List<FamilyValues>();

        public FamilyValues For(string family) => families.FirstOrDefault(f => f.family == family);

        /// <summary>Adds or replaces the values for one family.</summary>
        public void Set(string family, IEnumerable<PropertyValue> values)
        {
            families.RemoveAll(f => f.family == family);
            families.Add(new FamilyValues { family = family, values = values.ToList() });
        }

        /// <summary>
        /// Applies to materials: the material's own family entry if present, otherwise any entry
        /// translated through the alias map. Returns properties written.
        /// </summary>
        public int ApplyTo(IEnumerable<Material> materials)
        {
            List<Material> editable = MaterialEdits.Editable(materials).ToList();
            UnityEditor.Undo.RecordObjects(editable.ToArray(), "Apply preset " + name);
            int written = 0;
            foreach (Material m in editable)
            {
                string family = PropertyAliases.FamilyOf(m);
                FamilyValues own = For(family);
                if (own != null) { written += own.values.Count(v => v.WriteTo(m)); continue; }
                foreach (FamilyValues other in families)
                foreach (PropertyValue v in other.values)
                {
                    PropertyAlias alias = PropertyAliases.Aliases.FirstOrDefault(a => a.PropertyFor(other.family) == v.name);
                    string target = alias?.PropertyFor(family);
                    if (target != null && v.WriteTo(m, target)) written++;
                }
            }
            return written;
        }
    }

    /// <summary>The preset library: a folder outside any project (per machine), shareable as single files.</summary>
    public static class PresetLibrary
    {
        private const string FolderPref = "Sable.Toolbox.Materials.PresetFolder";

        public static string Folder
        {
            get => EditorPrefs.GetString(FolderPref, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sable Toolbox", "Material Presets"));
            set => EditorPrefs.SetString(FolderPref, value);
        }

        public static List<MaterialPreset> LoadAll(string folder = null)
        {
            folder ??= Folder;
            if (!Directory.Exists(folder)) return new List<MaterialPreset>();
            var list = new List<MaterialPreset>();
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    MaterialPreset p = JsonUtility.FromJson<MaterialPreset>(File.ReadAllText(file));
                    if (p != null && !string.IsNullOrEmpty(p.name)) list.Add(p);
                }
                catch (Exception e) { Debug.LogWarning($"Sable Toolbox: skipped preset {file}: {e.Message}"); }
            }
            return list.OrderBy(p => p.name).ToList();
        }

        public static string Save(MaterialPreset preset, string folder = null)
        {
            folder ??= Folder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName(preset.name));
            File.WriteAllText(path, JsonUtility.ToJson(preset, true));
            return path;
        }

        public static void Delete(MaterialPreset preset, string folder = null)
        {
            string path = Path.Combine(folder ?? Folder, FileName(preset.name));
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>Copies a preset file into the library. Returns the imported preset or null.</summary>
        public static MaterialPreset Import(string file, string folder = null)
        {
            MaterialPreset p = JsonUtility.FromJson<MaterialPreset>(File.ReadAllText(file));
            if (p == null || string.IsNullOrEmpty(p.name)) return null;
            Save(p, folder);
            return p;
        }

        public static string FileName(string presetName)
        {
            string safe = new string(presetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            return (safe.Length == 0 ? "preset" : safe) + ".json";
        }
    }
}
