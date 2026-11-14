using System.Collections.Generic;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Graph;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sable.Toolbox.Scanning
{
    /// <summary>One layer of one playable layer controller on the avatar descriptor.</summary>
    public readonly struct AnimatorLayerRef
    {
        public VRCAvatarDescriptor.AnimLayerType PlayableLayer { get; }
        public RuntimeAnimatorController Controller { get; }
        public int LayerIndex { get; }
        public string LayerName { get; }

        public AnimatorLayerRef(VRCAvatarDescriptor.AnimLayerType playableLayer, RuntimeAnimatorController controller,
            int layerIndex, string layerName)
        {
            PlayableLayer = playableLayer;
            Controller = controller;
            LayerIndex = layerIndex;
            LayerName = layerName;
        }
    }

    /// <summary>
    /// Immutable snapshot of one avatar, built by AvatarScanner in a single hierarchy walk.
    /// Modules read from this and never walk the hierarchy themselves. Lists are de-duplicated and
    /// keep discovery order. Inactive objects are included.
    /// </summary>
    public sealed class AvatarScan
    {
        public VRCAvatarDescriptor Descriptor { get; internal set; }
        public double ScanMilliseconds { get; internal set; }

        /// <summary>Every component under the avatar root, from the one hierarchy walk. Null entries are missing scripts.</summary>
        public IReadOnlyList<Component> Components => components;

        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<Mesh> Meshes => meshes;
        /// <summary>Materials on renderers plus materials reachable only through animation clips.</summary>
        public IReadOnlyList<Material> Materials => materials;
        /// <summary>Subset of Materials that appears only through animation clips (material swaps).</summary>
        public IReadOnlyList<Material> AnimatedOnlyMaterials => animatedOnlyMaterials;
        public IReadOnlyList<Shader> Shaders => shaders;
        public IReadOnlyList<Texture> Textures => textures;
        /// <summary>Transforms used as bones by any skinned mesh.</summary>
        public IReadOnlyList<Transform> Bones => bones;
        public IReadOnlyList<VRCPhysBoneBase> PhysBones => physBones;
        public IReadOnlyList<VRCPhysBoneColliderBase> Colliders => colliders;
        /// <summary>Contact senders and receivers.</summary>
        public IReadOnlyList<ContactBase> Contacts => contacts;
        /// <summary>VRChat constraints (VRCConstraintBase) and Unity constraints (IConstraint).</summary>
        public IReadOnlyList<Component> Constraints => constraints;
        public IReadOnlyList<AnimatorLayerRef> AnimatorLayers => animatorLayers;
        public IReadOnlyList<AnimationClip> Clips => clips;
        public IReadOnlyList<VRCExpressionParameters.Parameter> Parameters => parameters;
        public IReadOnlyList<VRCExpressionsMenu> Menus => menus;
        public int MissingScriptCount { get; internal set; }
        /// <summary>GameObjects carrying at least one missing script.</summary>
        public IReadOnlyList<GameObject> MissingScriptObjects => missingScriptObjects;
        public IReadOnlyList<BuildToolReport> BuildTools => buildTools;

        public AssetGraph Graph { get; } = new AssetGraph();

        /// <summary>Category counts in display order, for overviews and logs.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Counts() => new[]
        {
            Pair("Renderers", renderers.Count),
            Pair("Meshes", meshes.Count),
            Pair("Materials", materials.Count),
            Pair("  of which animated only", animatedOnlyMaterials.Count),
            Pair("Shaders", shaders.Count),
            Pair("Textures", textures.Count),
            Pair("Bones", bones.Count),
            Pair("PhysBones", physBones.Count),
            Pair("PhysBone colliders", colliders.Count),
            Pair("Contacts", contacts.Count),
            Pair("Constraints", constraints.Count),
            Pair("Animator layers", animatorLayers.Count),
            Pair("Animation clips", clips.Count),
            Pair("Expression parameters", parameters.Count),
            Pair("Expression menus", menus.Count),
            Pair("Missing scripts", MissingScriptCount),
        };

        private static KeyValuePair<string, int> Pair(string k, int v) => new KeyValuePair<string, int>(k, v);

        internal readonly List<Component> components = new List<Component>();
        internal readonly UniqueList<Renderer> renderers = new UniqueList<Renderer>();
        internal readonly UniqueList<Mesh> meshes = new UniqueList<Mesh>();
        internal readonly UniqueList<Material> materials = new UniqueList<Material>();
        internal readonly UniqueList<Material> animatedOnlyMaterials = new UniqueList<Material>();
        internal readonly UniqueList<Shader> shaders = new UniqueList<Shader>();
        internal readonly UniqueList<Texture> textures = new UniqueList<Texture>();
        internal readonly UniqueList<Transform> bones = new UniqueList<Transform>();
        internal readonly UniqueList<VRCPhysBoneBase> physBones = new UniqueList<VRCPhysBoneBase>();
        internal readonly UniqueList<VRCPhysBoneColliderBase> colliders = new UniqueList<VRCPhysBoneColliderBase>();
        internal readonly UniqueList<ContactBase> contacts = new UniqueList<ContactBase>();
        internal readonly UniqueList<Component> constraints = new UniqueList<Component>();
        internal readonly List<AnimatorLayerRef> animatorLayers = new List<AnimatorLayerRef>();
        internal readonly UniqueList<AnimationClip> clips = new UniqueList<AnimationClip>();
        internal readonly List<VRCExpressionParameters.Parameter> parameters = new List<VRCExpressionParameters.Parameter>();
        internal readonly UniqueList<VRCExpressionsMenu> menus = new UniqueList<VRCExpressionsMenu>();
        internal readonly List<BuildToolReport> buildTools = new List<BuildToolReport>();
        internal readonly List<GameObject> missingScriptObjects = new List<GameObject>();
    }

    /// <summary>Insertion-ordered list that ignores nulls and duplicates.</summary>
    internal sealed class UniqueList<T> : List<T> where T : Object
    {
        private readonly HashSet<T> seen = new HashSet<T>();
        public new bool Add(T item)
        {
            if (item == null || !seen.Add(item)) return false;
            base.Add(item);
            return true;
        }
        public new bool Contains(T item) => item != null && seen.Contains(item);
    }
}
