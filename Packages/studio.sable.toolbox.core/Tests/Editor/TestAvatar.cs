using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Sable.Toolbox.Tests
{
    /// <summary>
    /// A small avatar built entirely in memory, so tests need no binary assets.
    ///
    /// Avatar (descriptor)
    ///   Armature/Hips (PhysBone -> collider on Spine)  /Spine (collider, contact receiver)
    ///   Body  (SkinnedMeshRenderer, bones Hips+Spine, BodyMat -> BodyTex)
    ///   Prop  (inactive; MeshRenderer+MeshFilter, BodyMat; VRC parent constraint, Unity parent constraint, contact sender)
    /// FX controller: layers Base, Toggle; Toggle clip swaps Body's material to SwapMat -> SwapTex.
    /// Parameters: 2. Menus: Root -> Sub -> Root (a loop).
    /// </summary>
    public sealed class TestAvatar
    {
        public GameObject Root;
        public VRCAvatarDescriptor Descriptor;
        public SkinnedMeshRenderer Body;
        public Material BodyMat, SwapMat;
        public Texture2D BodyTex, SwapTex;
        public AnimationClip SwapClip;
        public AnimatorController Fx;

        private readonly List<Object> owned = new List<Object>();

        public static TestAvatar Build()
        {
            var a = new TestAvatar();
            a.Root = new GameObject("TestAvatar");
            a.Descriptor = a.Root.AddComponent<VRCAvatarDescriptor>();

            Transform hips = a.Child("Armature/Hips", a.Root.transform);
            Transform spine = a.Child("Spine", hips);

            Shader shader = Shader.Find("Standard");
            a.BodyTex = a.Own(new Texture2D(4, 4) { name = "BodyTex" });
            a.SwapTex = a.Own(new Texture2D(4, 4) { name = "SwapTex" });
            a.BodyMat = a.Own(new Material(shader) { name = "BodyMat", mainTexture = a.BodyTex });
            a.SwapMat = a.Own(new Material(shader) { name = "SwapMat", mainTexture = a.SwapTex });

            Transform body = a.Child("Body", a.Root.transform);
            a.Body = body.gameObject.AddComponent<SkinnedMeshRenderer>();
            a.Body.sharedMesh = a.Own(Triangle("BodyMesh"));
            a.Body.bones = new[] { hips, spine };
            a.Body.sharedMaterials = new[] { a.BodyMat };

            Transform prop = a.Child("Prop", a.Root.transform);
            prop.gameObject.AddComponent<MeshFilter>().sharedMesh = a.Own(Triangle("PropMesh"));
            prop.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { a.BodyMat };
            prop.gameObject.AddComponent<VRCParentConstraint>();
            prop.gameObject.AddComponent<ParentConstraint>();
            prop.gameObject.AddComponent<VRCContactSender>();
            prop.gameObject.SetActive(false);

            var collider = spine.gameObject.AddComponent<VRCPhysBoneCollider>();
            var physBone = hips.gameObject.AddComponent<VRCPhysBone>();
            physBone.colliders = new List<VRC.Dynamics.VRCPhysBoneColliderBase> { collider };
            spine.gameObject.AddComponent<VRCContactReceiver>();

            // FX: Base layer + Toggle layer whose clip swaps the body material.
            a.SwapClip = a.Own(new AnimationClip { name = "SwapClip" });
            var binding = EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(a.SwapClip, binding, new[] { new ObjectReferenceKeyframe { time = 0, value = a.SwapMat } });

            a.Fx = a.Own(new AnimatorController { name = "FX" });
            a.Fx.AddLayer("Base");
            a.Fx.AddLayer("Toggle");
            a.Fx.layers[1].stateMachine.AddState("On").motion = a.SwapClip;

            a.Descriptor.customizeAnimationLayers = true;
            a.Descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = a.Fx },
            };
            a.Descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];

            var parameters = a.Own(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            parameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Toggle", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "Hue", valueType = VRCExpressionParameters.ValueType.Float },
            };
            a.Descriptor.customExpressions = true;
            a.Descriptor.expressionParameters = parameters;

            var rootMenu = a.Own(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            var subMenu = a.Own(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            rootMenu.controls.Add(new VRCExpressionsMenu.Control { name = "Sub", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = subMenu });
            subMenu.controls.Add(new VRCExpressionsMenu.Control { name = "Back", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = rootMenu });
            a.Descriptor.expressionsMenu = rootMenu;

            return a;
        }

        public void Destroy()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            foreach (Object o in owned) if (o != null) Object.DestroyImmediate(o);
        }

        private T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }

        private Transform Child(string path, Transform parent)
        {
            foreach (string part in path.Split('/'))
            {
                var go = new GameObject(part);
                go.transform.SetParent(parent, false);
                parent = go.transform;
            }
            return parent;
        }

        private static Mesh Triangle(string name) => new Mesh
        {
            name = name,
            vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
            triangles = new[] { 0, 1, 2 }
        };
    }
}
