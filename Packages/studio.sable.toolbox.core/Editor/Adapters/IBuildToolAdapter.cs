using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Adapters
{
    /// <summary>
    /// Knows about one build-time tool (Modular Avatar, VRCFury, ...). Adapters never reference the
    /// tool's assemblies; they match components by type name. The scanner hands every component it
    /// found in its single hierarchy walk to Detect, so adapters never re-walk the avatar.
    ///
    /// v0.1 adapters only detect. Later versions add estimates of what the tool will add or merge at
    /// build time as new interfaces an adapter may also implement (see docs/ARCHITECTURE.md), so this
    /// interface stays as it is.
    /// </summary>
    public interface IBuildToolAdapter
    {
        string Id { get; }
        string DisplayName { get; }
        /// <summary>True when the tool's types are loaded in this editor.</summary>
        bool IsInstalled { get; }
        /// <summary>The tool's components among the avatar's components.</summary>
        IReadOnlyList<Component> Detect(IReadOnlyList<Component> avatarComponents);
    }

    /// <summary>What the scanner found for one adapter.</summary>
    public sealed class BuildToolReport
    {
        public IBuildToolAdapter Adapter { get; }
        public IReadOnlyList<Component> Components { get; }
        public BuildToolReport(IBuildToolAdapter adapter, IReadOnlyList<Component> components)
        {
            Adapter = adapter;
            Components = components;
        }
    }

    /// <summary>Matches components whose type, or any base type, has one of the given full names.</summary>
    public sealed class TypeNameAdapter : IBuildToolAdapter
    {
        private readonly HashSet<string> typeNames;
        private bool? installed;

        public string Id { get; }
        public string DisplayName { get; }

        public TypeNameAdapter(string id, string displayName, IEnumerable<string> fullTypeNames)
        {
            Id = id;
            DisplayName = displayName;
            typeNames = new HashSet<string>(fullTypeNames);
        }

        public bool IsInstalled => installed ??= AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => typeNames.Any(n => a.GetType(n, false) != null));

        public IReadOnlyList<Component> Detect(IReadOnlyList<Component> avatarComponents)
        {
            var result = new List<Component>();
            foreach (Component c in avatarComponents)
            {
                if (c == null) continue; // missing script
                for (Type t = c.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    if (!typeNames.Contains(t.FullName)) continue;
                    result.Add(c);
                    break;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// The registered adapters: the built-in ones from Data/adapters.json, plus any added by other
    /// packages through Register (call it from an [InitializeOnLoad] static constructor).
    /// </summary>
    public static class AdapterRegistry
    {
        public const string DataPath = "Packages/studio.sable.toolbox.core/Editor/Data/adapters.json";

        private static List<IBuildToolAdapter> adapters;
        private static readonly List<IBuildToolAdapter> extra = new List<IBuildToolAdapter>();

        public static IReadOnlyList<IBuildToolAdapter> All => adapters ??= LoadBuiltIn().Concat(extra).ToList();

        public static void Register(IBuildToolAdapter adapter)
        {
            extra.Add(adapter);
            adapters = null;
        }

        [Serializable] private class AdapterFile { public int version; public AdapterEntry[] adapters; }
        [Serializable] private class AdapterEntry { public string id; public string displayName; public string[] typeNames; }

        private static IEnumerable<IBuildToolAdapter> LoadBuiltIn()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (json == null)
            {
                Debug.LogWarning($"Sable Toolbox: {DataPath} not found; build-tool detection is off.");
                return Enumerable.Empty<IBuildToolAdapter>();
            }
            AdapterFile file = JsonUtility.FromJson<AdapterFile>(json.text);
            return (file?.adapters ?? Array.Empty<AdapterEntry>())
                .Select(e => (IBuildToolAdapter)new TypeNameAdapter(e.id, e.displayName, e.typeNames ?? Array.Empty<string>()))
                .ToList();
        }
    }
}
