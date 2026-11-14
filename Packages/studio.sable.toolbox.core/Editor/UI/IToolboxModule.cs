using System;
using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Sable.Toolbox.UI
{
    /// <summary>
    /// A page in the Sable Toolbox window. Any editor assembly can add one: implement this on a
    /// non-abstract class with a public parameterless constructor and the window finds it through
    /// TypeCache. Core never needs to know about the module.
    /// </summary>
    public interface IToolboxModule
    {
        /// <summary>Stable id, used for settings ("studio.sable.toolbox.rigging.blendshape-copier").</summary>
        string Id { get; }
        string DisplayName { get; }
        /// <summary>Sidebar order, ascending. Core uses 0-99; module packages use 100+.</summary>
        int Order { get; }
        /// <summary>
        /// Build the module's UI. Called once per window. Read avatar data from context.Scan and
        /// subscribe to context.Changed to refresh; never walk the avatar hierarchy yourself.
        /// </summary>
        VisualElement CreateView(ToolboxContext context);
    }

    /// <summary>What every module sees: the picked avatar, its cached scan and the shared findings.</summary>
    public sealed class ToolboxContext
    {
        private VRCAvatarDescriptor avatar;

        public FindingSet Findings { get; } = new FindingSet();

        /// <summary>Raised when the avatar changes or its scan was invalidated.</summary>
        public event Action Changed;

        public VRCAvatarDescriptor Avatar
        {
            get => avatar;
            set
            {
                if (avatar == value) return;
                avatar = value;
                Findings.ClearAll();
                Changed?.Invoke();
            }
        }

        /// <summary>The avatar's scan, from cache. Null when no avatar is picked.</summary>
        public AvatarScan Scan => avatar == null ? null : ScanCache.Get(avatar);

        internal void RaiseChanged() => Changed?.Invoke();
    }

    public static class ModuleRegistry
    {
        /// <summary>Instantiates every IToolboxModule in loaded assemblies, ordered by Order then name.</summary>
        public static List<IToolboxModule> CreateAll()
        {
            var modules = new List<IToolboxModule>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IToolboxModule>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    modules.Add((IToolboxModule)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Sable Toolbox: could not create module {type.FullName}: {e.Message}");
                }
            }
            return modules.OrderBy(m => m.Order).ThenBy(m => m.DisplayName).ToList();
        }
    }
}
