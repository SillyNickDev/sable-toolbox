using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sable.Toolbox.Materials
{
    [Serializable]
    public sealed class ShaderFamily
    {
        public string id;
        public string name;
        public string[] shaderPrefixes;
    }

    [Serializable]
    public sealed class AliasTarget { public string family; public string property; }

    [Serializable]
    public sealed class PropertyAlias
    {
        public string id;
        public string label;
        /// <summary>"Color", "Float", "Texture" or "Vector".</summary>
        public string type;
        public AliasTarget[] properties;

        public string PropertyFor(string familyId) => properties?.FirstOrDefault(p => p.family == familyId)?.property;
    }

    /// <summary>Shader families and semantic aliases from Data/property-aliases.json.</summary>
    public static class PropertyAliases
    {
        public const string DataPath = "Packages/studio.sable.toolbox.materials/Editor/Data/property-aliases.json";
        [Serializable] private class DataFile { public int version; public ShaderFamily[] families; public PropertyAlias[] aliases; }
        private static DataFile data;

        public static IReadOnlyList<ShaderFamily> Families => Data.families ?? Array.Empty<ShaderFamily>();
        public static IReadOnlyList<PropertyAlias> Aliases => Data.aliases ?? Array.Empty<PropertyAlias>();

        /// <summary>Family id for a material ("poiyomi", "liltoon", "standard"), or "other".</summary>
        public static string FamilyOf(Material m)
        {
            string shader = MaterialInfo.SourceShaderName(m);
            if (shader == null) return "other";
            foreach (ShaderFamily f in Families)
                if (f.shaderPrefixes != null && f.shaderPrefixes.Any(p => shader.StartsWith(p, StringComparison.Ordinal)))
                    return f.id;
            return "other";
        }

        private static DataFile Data
        {
            get
            {
                if (data != null) return data;
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
                if (json == null) Debug.LogWarning($"Sable Toolbox: {DataPath} not found; shader families are unknown.");
                return data = json != null ? JsonUtility.FromJson<DataFile>(json.text) : new DataFile();
            }
        }
    }

    /// <summary>A material property value detached from any material: used for copy/paste and presets.</summary>
    [Serializable]
    public sealed class PropertyValue
    {
        public string name;
        public string type; // Color, Float, Vector, Texture, Int
        public Color color;
        public Vector4 vector;
        public float number;
        public string textureGuid;

        public static PropertyValue Read(Material m, string name)
        {
            int i = m.shader.FindPropertyIndex(name);
            if (i < 0) return null;
            var v = new PropertyValue { name = name };
            switch (m.shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color: v.type = "Color"; v.color = m.GetColor(name); break;
                case ShaderPropertyType.Vector: v.type = "Vector"; v.vector = m.GetVector(name); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: v.type = "Float"; v.number = m.GetFloat(name); break;
                case ShaderPropertyType.Int: v.type = "Int"; v.number = m.GetInteger(name); break;
                case ShaderPropertyType.Texture:
                    v.type = "Texture";
                    Texture t = m.GetTexture(name);
                    v.textureGuid = t == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t));
                    break;
                default: return null;
            }
            return v;
        }

        /// <summary>Writes onto a material that has a property of a compatible type. Caller records Undo.</summary>
        public bool WriteTo(Material m, string asName = null)
        {
            string target = asName ?? name;
            int i = m.shader.FindPropertyIndex(target);
            if (i < 0) return false;
            ShaderPropertyType t = m.shader.GetPropertyType(i);
            switch (type)
            {
                case "Color" when t == ShaderPropertyType.Color: m.SetColor(target, color); return true;
                case "Vector" when t == ShaderPropertyType.Vector: m.SetVector(target, vector); return true;
                case "Float" when t == ShaderPropertyType.Float || t == ShaderPropertyType.Range: m.SetFloat(target, number); return true;
                case "Int" when t == ShaderPropertyType.Int: m.SetInteger(target, (int)number); return true;
                case "Float" when t == ShaderPropertyType.Int: m.SetInteger(target, Mathf.RoundToInt(number)); return true;
                case "Texture" when t == ShaderPropertyType.Texture:
                    string path = string.IsNullOrEmpty(textureGuid) ? null : AssetDatabase.GUIDToAssetPath(textureGuid);
                    if (!string.IsNullOrEmpty(textureGuid) && string.IsNullOrEmpty(path)) return false; // texture not in this project
                    m.SetTexture(target, path == null ? null : AssetDatabase.LoadAssetAtPath<Texture>(path));
                    return true;
                default: return false;
            }
        }

        public override string ToString() => type switch
        {
            "Color" => $"#{ColorUtility.ToHtmlStringRGBA(color)}",
            "Vector" => vector.ToString(),
            "Texture" => string.IsNullOrEmpty(textureGuid) ? "None" : System.IO.Path.GetFileName(AssetDatabase.GUIDToAssetPath(textureGuid)),
            _ => number.ToString("0.###")
        };
    }

    /// <summary>Bulk operations on materials. Every method records Undo; locked materials are skipped.</summary>
    public static class MaterialEdits
    {
        /// <summary>Sets one property on every material that has it. Returns how many changed.</summary>
        public static int Set(IEnumerable<Material> materials, PropertyValue value, string undoName = "Edit materials")
        {
            List<Material> editable = Editable(materials).Where(m => m.shader.FindPropertyIndex(value.name) >= 0).ToList();
            UnityEditor.Undo.RecordObjects(editable.ToArray(), undoName);
            return editable.Count(m => value.WriteTo(m));
        }

        /// <summary>Sets an alias: each material gets its own family's property. Returns how many changed.</summary>
        public static int SetAlias(IEnumerable<Material> materials, PropertyAlias alias, PropertyValue value)
        {
            var targets = Editable(materials)
                .Select(m => (m, prop: alias.PropertyFor(PropertyAliases.FamilyOf(m))))
                .Where(x => x.prop != null && x.m.shader.FindPropertyIndex(x.prop) >= 0)
                .ToList();
            UnityEditor.Undo.RecordObjects(targets.Select(t => (UnityEngine.Object)t.m).ToArray(), "Edit " + alias.label);
            return targets.Count(t => value.WriteTo(t.m, t.prop));
        }

        /// <summary>Pastes values onto materials, by property name. Returns the number of properties written.</summary>
        public static int Paste(IEnumerable<Material> materials, IReadOnlyCollection<PropertyValue> values)
        {
            List<Material> editable = Editable(materials).ToList();
            UnityEditor.Undo.RecordObjects(editable.ToArray(), "Paste material properties");
            return editable.Sum(m => values.Count(v => v.WriteTo(m)));
        }

        /// <summary>Replaces a texture in every texture property of the materials. Returns slots changed.</summary>
        public static int ReplaceTexture(IEnumerable<Material> materials, Texture find, Texture replace)
        {
            if (find == null || find == replace) return 0;
            var hits = new List<(Material m, int id)>();
            foreach (Material m in Editable(materials))
                foreach (int id in m.GetTexturePropertyNameIDs())
                    if (m.GetTexture(id) == find) hits.Add((m, id));
            UnityEditor.Undo.RecordObjects(hits.Select(h => (UnityEngine.Object)h.m).Distinct().ToArray(), "Replace texture");
            foreach ((Material m, int id) in hits) m.SetTexture(id, replace);
            return hits.Count;
        }

        public static IEnumerable<Material> Editable(IEnumerable<Material> materials) =>
            materials.Where(m => m != null && m.shader != null && !Sable.Toolbox.Shaders.PoiyomiBridge.IsLocked(m)).Distinct();
    }
}
