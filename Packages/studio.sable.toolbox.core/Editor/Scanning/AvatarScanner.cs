using System.Collections.Generic;
using System.Diagnostics;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sable.Toolbox.Scanning
{
    /// <summary>
    /// Builds an AvatarScan. Walks the hierarchy exactly once (GetComponentsInChildren), then
    /// follows asset references from what it found. Use ScanCache.Get instead of calling this
    /// directly, so the result is shared and invalidated correctly.
    /// </summary>
    public static class AvatarScanner
    {
        public static AvatarScan Scan(VRCAvatarDescriptor descriptor) => Scan(descriptor, AdapterRegistry.All);

        public static AvatarScan Scan(VRCAvatarDescriptor descriptor, IReadOnlyList<IBuildToolAdapter> adapters)
        {
            var timer = Stopwatch.StartNew();
            var scan = new AvatarScan { Descriptor = descriptor };
            AssetGraph g = scan.Graph;

            // 1. The one hierarchy walk.
            scan.components.AddRange(descriptor.GetComponentsInChildren<Component>(true));

            foreach (Component c in scan.components)
            {
                if (c == null) { scan.MissingScriptCount++; continue; }
                switch (c)
                {
                    case SkinnedMeshRenderer smr:
                        scan.renderers.Add(smr);
                        AddMesh(scan, smr, smr.sharedMesh);
                        foreach (Transform b in smr.bones) scan.bones.Add(b);
                        AddRendererMaterials(scan, smr);
                        break;
                    case Renderer r:
                        scan.renderers.Add(r);
                        AddRendererMaterials(scan, r);
                        break;
                    case MeshFilter mf:
                        AddMesh(scan, mf, mf.sharedMesh);
                        break;
                    case VRCPhysBoneBase pb:
                        scan.physBones.Add(pb);
                        if (pb.colliders != null)
                            foreach (VRCPhysBoneColliderBase col in pb.colliders) g.Add(pb, col);
                        break;
                    case VRCPhysBoneColliderBase col:
                        scan.colliders.Add(col);
                        break;
                    case ContactBase contact:
                        scan.contacts.Add(contact);
                        break;
                    case VRCConstraintBase vc:
                        scan.constraints.Add(vc);
                        break;
                    case IConstraint _:
                        scan.constraints.Add(c);
                        break;
                }
            }

            // 2. Playable layers -> controllers -> layers and clips -> animated references.
            AddPlayableLayers(scan, descriptor, descriptor.baseAnimationLayers);
            AddPlayableLayers(scan, descriptor, descriptor.specialAnimationLayers);

            // 3. Expression parameters and menus.
            if (descriptor.expressionParameters != null)
            {
                g.Add(descriptor, descriptor.expressionParameters);
                if (descriptor.expressionParameters.parameters != null)
                    foreach (var p in descriptor.expressionParameters.parameters)
                        if (p != null && !string.IsNullOrEmpty(p.name)) scan.parameters.Add(p);
            }
            AddMenu(scan, descriptor, descriptor.expressionsMenu);

            // 4. Build-time tools, from the same component list.
            foreach (IBuildToolAdapter adapter in adapters)
            {
                IReadOnlyList<Component> found = adapter.Detect(scan.components);
                if (found.Count > 0 || adapter.IsInstalled) scan.buildTools.Add(new BuildToolReport(adapter, found));
            }

            scan.ScanMilliseconds = timer.Elapsed.TotalMilliseconds;
            return scan;
        }

        private static void AddMesh(AvatarScan scan, Component owner, Mesh mesh)
        {
            if (mesh == null) return;
            scan.meshes.Add(mesh);
            scan.Graph.Add(owner, mesh);
        }

        private static void AddRendererMaterials(AvatarScan scan, Renderer r)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                scan.Graph.Add(r, m);
                AddMaterial(scan, m);
            }
        }

        private static void AddMaterial(AvatarScan scan, Material m)
        {
            if (!scan.materials.Add(m)) return;
            if (m.shader != null)
            {
                scan.shaders.Add(m.shader);
                scan.Graph.Add(m, m.shader);
            }
            foreach (int id in m.GetTexturePropertyNameIDs())
            {
                Texture t = m.GetTexture(id);
                if (t == null) continue;
                scan.textures.Add(t);
                scan.Graph.Add(m, t);
            }
        }

        private static void AddPlayableLayers(AvatarScan scan, VRCAvatarDescriptor d, VRCAvatarDescriptor.CustomAnimLayer[] layers)
        {
            if (layers == null) return;
            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in layers)
            {
                RuntimeAnimatorController rac = layer.animatorController;
                if (layer.isDefault || rac == null) continue;
                scan.Graph.Add(d, rac);

                if (EditorControllerOf(rac) is UnityEditor.Animations.AnimatorController ac)
                {
                    UnityEditor.Animations.AnimatorControllerLayer[] acLayers = ac.layers;
                    for (int i = 0; i < acLayers.Length; i++)
                        scan.animatorLayers.Add(new AnimatorLayerRef(layer.type, rac, i, acLayers[i].name));
                }

                foreach (AnimationClip clip in rac.animationClips)
                {
                    if (clip == null) continue;
                    scan.Graph.Add(rac, clip);
                    if (scan.clips.Add(clip)) AddClipReferences(scan, clip);
                }
            }
        }

        private static RuntimeAnimatorController EditorControllerOf(RuntimeAnimatorController rac)
        {
            // Override controllers wrap a base controller; layer structure lives on the base.
            while (rac is AnimatorOverrideController aoc) rac = aoc.runtimeAnimatorController;
            return rac;
        }

        /// <summary>Object-reference curves: material swaps and any other asset a clip keys in.</summary>
        private static void AddClipReferences(AvatarScan scan, AnimationClip clip)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null) continue;
                foreach (ObjectReferenceKeyframe key in keys)
                {
                    Object value = key.value;
                    if (value == null) continue;
                    scan.Graph.Add(clip, value, EdgeKind.Animated);
                    if (value is Material m)
                    {
                        bool onRenderer = scan.materials.Contains(m);
                        AddMaterial(scan, m);
                        if (!onRenderer) scan.animatedOnlyMaterials.Add(m);
                    }
                    else if (value is Texture t)
                    {
                        scan.textures.Add(t);
                    }
                }
            }
        }

        private static void AddMenu(AvatarScan scan, Object parent, VRCExpressionsMenu menu)
        {
            if (menu == null) return;
            scan.Graph.Add(parent, menu);
            if (!scan.menus.Add(menu)) return; // already visited (menus can loop)
            if (menu.controls == null) return;
            foreach (VRCExpressionsMenu.Control control in menu.controls)
            {
                if (control == null) continue;
                if (control.icon != null) scan.Graph.Add(menu, control.icon);
                AddMenu(scan, menu, control.subMenu);
            }
        }
    }
}
