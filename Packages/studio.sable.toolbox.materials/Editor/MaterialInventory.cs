using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Materials
{
    public enum LockState
    {
        /// <summary>Not a Poiyomi/Thry-optimizer material.</summary>
        NotLockable,
        Unlocked,
        Locked,
        /// <summary>Locked, but the generated locked shader is missing or broken: needs unlocking and relocking.</summary>
        Stale
    }

    public static class MaterialInfo
    {
        /// <summary>The shader a material was authored with: the original shader for locked Poiyomi materials.</summary>
        public static string SourceShaderName(Material m)
        {
            if (m == null) return null;
            string original = m.GetTag("OriginalShader", false, "");
            if (m.shader != null && m.shader.name.StartsWith(PoiyomiBridge.LockedShaderPrefix, StringComparison.Ordinal) && original != "")
                return original;
            if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader") return original != "" ? original : null;
            return m.shader.name;
        }

        public static bool IsMissingShader(Material m) =>
            m.shader == null || m.shader.name == "Hidden/InternalErrorShader" || !m.shader.isSupported;

        public static LockState Lock(Material m)
        {
            if (!PoiyomiBridge.IsOptimizerMaterial(m) && m.GetTag("OriginalShader", false, "") == "") return LockState.NotLockable;
            bool locked = PoiyomiBridge.IsLocked(m);
            if (!locked) return PoiyomiBridge.IsOptimizerMaterial(m) ? LockState.Unlocked : LockState.NotLockable;
            return IsMissingShader(m) ? LockState.Stale : LockState.Locked;
        }

        private static readonly Regex VersionPattern = new Regex(@"\d+\.\d+(\.\d+)?");

        /// <summary>Shader version from Poiyomi's "shader_master_label" property, or "" if the shader has none.</summary>
        public static string ShaderVersion(Material m)
        {
            string name = SourceShaderName(m);
            Shader s = name == null ? null : Shader.Find(name);
            if (s == null) return "";
            int i = s.FindPropertyIndex("shader_master_label");
            if (i < 0) return "";
            Match match = VersionPattern.Match(s.GetPropertyDescription(i));
            return match.Success ? match.Value : "";
        }
    }

    /// <summary>One row of the material inventory.</summary>
    public sealed class MaterialRow
    {
        public Material Material;
        public string Family;
        public string ShaderName;
        public string Version;
        public LockState Lock;
        public int RenderQueue;
        /// <summary>"Body[0]" style slot names of renderers using the material.</summary>
        public List<string> Slots = new List<string>();
        public Mesh FirstMesh;
        public List<Texture> Textures = new List<Texture>();
        public double VramMB;
        public bool AnimatedSwap;
        public bool MissingShader;
        public int KeywordCount;

        public bool Matches(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            string f = filter.Trim();
            bool Has(string s) => s != null && s.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(Material.name) || Has(ShaderName) || Has(Family) || Material.shaderKeywords.Any(Has);
        }
    }

    public static class MaterialInventory
    {
        public static List<MaterialRow> Build(AvatarScan scan)
        {
            var rows = new List<MaterialRow>();
            foreach (Material m in scan.Materials)
            {
                var row = new MaterialRow
                {
                    Material = m,
                    Family = PropertyAliases.FamilyOf(m),
                    ShaderName = MaterialInfo.SourceShaderName(m) ?? "(missing)",
                    Version = MaterialInfo.ShaderVersion(m),
                    Lock = MaterialInfo.Lock(m),
                    RenderQueue = m.renderQueue,
                    AnimatedSwap = scan.AnimatedOnlyMaterials.Contains(m),
                    MissingShader = MaterialInfo.IsMissingShader(m),
                    KeywordCount = m.shaderKeywords.Length,
                };
                foreach (Renderer r in scan.Renderers)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == m)
                        {
                            row.Slots.Add($"{r.name}[{i}]");
                            if (row.FirstMesh == null) row.FirstMesh = MeshOf(r);
                        }
                }
                foreach (int id in m.GetTexturePropertyNameIDs())
                {
                    Texture t = m.GetTexture(id);
                    if (t != null && !row.Textures.Contains(t)) row.Textures.Add(t);
                }
                row.VramMB = row.Textures.Sum(TextureMemory.Megabytes);
                rows.Add(row);
            }
            return rows;
        }

        public enum Grouping { None, Shader, Mesh, TextureSet }

        public static string GroupKey(MaterialRow row, Grouping g) => g switch
        {
            Grouping.Shader => row.ShaderName,
            Grouping.Mesh => row.FirstMesh != null ? row.FirstMesh.name : row.AnimatedSwap ? "(animation only)" : "(no mesh)",
            Grouping.TextureSet => row.Material.mainTexture != null ? row.Material.mainTexture.name : "(no main texture)",
            _ => ""
        };

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
    }

    /// <summary>A material property animated by a clip but not marked to stay animatable when locked.</summary>
    public sealed class AnimatedPropertyIssue
    {
        public Material Material;
        public string Property;
        public AnimationClip Clip;
        public bool AlreadyLocked;
    }

    /// <summary>
    /// Cross-checks every material property touched by an animation clip against the material's
    /// Poiyomi "…Animated" tags. Untagged properties stop animating once the material is locked
    /// (dissolve toggles are the classic casualty).
    /// </summary>
    public static class AnimatedPropertyAudit
    {
        private static readonly Regex ComponentSuffix = new Regex(@"\.(r|g|b|a|x|y|z|w)$");

        public static List<AnimatedPropertyIssue> Run(AvatarScan scan)
        {
            var issues = new List<AnimatedPropertyIssue>();
            var seen = new HashSet<(Material, string)>();
            Transform root = scan.Descriptor.transform;
            foreach (AnimationClip clip in scan.Clips)
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.StartsWith("material.", StringComparison.Ordinal) || !typeof(Renderer).IsAssignableFrom(b.type)) continue;
                string property = ComponentSuffix.Replace(b.propertyName.Substring("material.".Length), "");
                Transform t = string.IsNullOrEmpty(b.path) ? root : root.Find(b.path);
                if (t == null || !(t.GetComponent(b.type) is Renderer r)) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || !PoiyomiBridge.IsOptimizerMaterial(m) || !seen.Add((m, property))) continue;
                    if (m.shader.FindPropertyIndex(property) < 0 && !PoiyomiBridge.IsLocked(m)) continue;
                    if (PoiyomiBridge.IsMarkedAnimated(m, property)) continue;
                    issues.Add(new AnimatedPropertyIssue { Material = m, Property = property, Clip = clip, AlreadyLocked = PoiyomiBridge.IsLocked(m) });
                }
            }
            return issues;
        }

        /// <summary>Tags the property as animated (unlocking and relocking a locked material around it).</summary>
        public static void MarkAnimated(AnimatedPropertyIssue issue)
        {
            Material m = issue.Material;
            bool relock = PoiyomiBridge.IsLocked(m);
            if (relock && !PoiyomiBridge.SetLocked(new[] { m }, false)) return;
            UnityEditor.Undo.RecordObject(m, "Mark property animated");
            m.SetOverrideTag(issue.Property + PoiyomiBridge.AnimatedSuffix, "1");
            EditorUtility.SetDirty(m);
            if (relock) PoiyomiBridge.SetLocked(new[] { m }, true);
        }
    }
}
