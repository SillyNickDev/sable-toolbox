using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Sable.Toolbox.Shaders
{
    /// <summary>
    /// Talks to Poiyomi's shader optimizer (Thry's ShaderOptimizer) by reflection, so the toolbox
    /// works with or without Poiyomi installed and never references its assembly.
    /// Checked against com.poiyomi.toon 9.3.64 (Thry.ThryEditor.ShaderOptimizer).
    /// </summary>
    public static class PoiyomiBridge
    {
        // Newest first; older Thry builds used the shorter namespace.
        private static readonly string[] TypeNames = { "Thry.ThryEditor.ShaderOptimizer", "Thry.ShaderOptimizer" };
        public const string LockedShaderPrefix = "Hidden/Locked/";
        public const string AnimatedSuffix = "Animated";

        private static bool resolved;
        private static MethodInfo usesOptimizer, isLocked, setLocked;

        /// <summary>True when Poiyomi's optimizer is loaded.</summary>
        public static bool IsAvailable { get { Resolve(); return usesOptimizer != null; } }

        /// <summary>True for Poiyomi (and other Thry-optimizer) materials, locked or not.</summary>
        public static bool IsOptimizerMaterial(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (IsLockedByName(m)) return true;
            Resolve();
            return usesOptimizer != null && (bool)usesOptimizer.Invoke(null, new object[] { m.shader });
        }

        public static bool IsLocked(Material m)
        {
            if (m == null) return false;
            Resolve();
            return isLocked != null ? (bool)isLocked.Invoke(null, new object[] { m }) : IsLockedByName(m);
        }

        /// <summary>
        /// Locks or unlocks through Poiyomi's own code path (it generates the locked shaders).
        /// Returns false when Poiyomi is not installed or the user cancelled.
        /// </summary>
        public static bool SetLocked(IEnumerable<Material> materials, bool locked)
        {
            Resolve();
            if (setLocked == null) return false;
            var list = materials.Where(m => m != null).ToList();
            if (list.Count == 0) return true;
            object[] args = setLocked.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            args[0] = list;
            args[1] = locked ? 1 : 0;
            if (args.Length > 2) args[2] = true; // progress bar
            return (bool)setLocked.Invoke(null, args);
        }

        /// <summary>Properties marked to stay animatable after locking (the "…Animated" tags).</summary>
        public static bool IsMarkedAnimated(Material m, string propertyName) =>
            m != null && !string.IsNullOrEmpty(m.GetTag(propertyName + AnimatedSuffix, false, ""))
                      && m.GetTag(propertyName + AnimatedSuffix, false, "") != "0";

        private static bool IsLockedByName(Material m) =>
            m.shader != null && m.shader.name.StartsWith(LockedShaderPrefix, StringComparison.Ordinal);

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            Type t = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => TypeNames.Select(n => a.GetType(n, false)))
                .FirstOrDefault(x => x != null);
            if (t == null) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            usesOptimizer = t.GetMethod("IsShaderUsingThryOptimizer", flags, null, new[] { typeof(Shader) }, null);
            isLocked = t.GetMethod("IsMaterialLocked", flags, null, new[] { typeof(Material) }, null);
            setLocked = t.GetMethods(flags).FirstOrDefault(mi => mi.Name == "SetLockedForAllMaterials"
                && mi.GetParameters().Length >= 2 && mi.GetParameters()[1].ParameterType == typeof(int));
        }
    }
}
