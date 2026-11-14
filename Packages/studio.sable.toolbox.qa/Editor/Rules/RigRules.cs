using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Sable.Toolbox.Qa.Rules
{
    internal sealed class PhysBoneLimitsRule : AvatarRule
    {
        public override string Id => "qa.physbone-no-limits";
        public override string Title => "PhysBones without limits";
        public override Severity DefaultSeverity => Severity.Info;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (VRCPhysBoneBase pb in scan.PhysBones)
                if (pb.limitType == VRCPhysBoneBase.LimitType.None)
                    yield return Found(pb, $"PhysBone on '{pb.name}' has no rotation limits",
                        "Without limits the chain can be pulled or fall through the body (tails into legs, ears into the head). " +
                        "An Angle or Polar limit keeps it in a believable range.");
        }
    }

    internal sealed class PhysBoneCollisionRule : AvatarRule
    {
        public override string Id => "qa.physbone-collision-checks";
        public override string Title => "Long PhysBone chains with many collisions";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            float max = context.Threshold("physbone.maxCollisionChecksPerComponent", 64);
            foreach (VRCPhysBoneBase pb in scan.PhysBones)
            {
                int colliders = pb.colliders?.Count(c => c != null) ?? 0;
                if (colliders == 0) continue;
                int transforms = AffectedTransforms(pb);
                int checks = transforms * colliders;
                if (checks <= max) continue;
                yield return Found(pb,
                    $"PhysBone on '{pb.name}': {transforms} transforms × {colliders} colliders = {checks} collision checks",
                    $"Every transform in the chain is tested against every collider each frame (limit here: {max}). " +
                    "Long chains with several colliders are a common cause of Poor rank. Remove colliders the chain can't reach, " +
                    "or ignore transforms near the root.");
            }
        }

        internal static int AffectedTransforms(VRCPhysBoneBase pb)
        {
            Transform root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
            var ignored = new HashSet<Transform>(pb.ignoreTransforms?.Where(t => t != null) ?? Enumerable.Empty<Transform>());
            int Count(Transform t)
            {
                if (ignored.Contains(t)) return 0;
                int n = 1;
                for (int i = 0; i < t.childCount; i++) n += Count(t.GetChild(i));
                return n;
            }
            return Count(root);
        }
    }

    internal sealed class ZeroWeightBoneRule : AvatarRule
    {
        public override string Id => "qa.zero-weight-bones";
        public override string Title => "Bones with no skin weight";
        public override Severity DefaultSeverity => Severity.Info;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            var weighted = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer smr in scan.Renderers.OfType<SkinnedMeshRenderer>())
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;
                Transform[] bones = smr.bones;
                foreach (BoneWeight1 w in mesh.GetAllBoneWeights())
                    if (w.weight > 0 && w.boneIndex < bones.Length && bones[w.boneIndex] != null)
                        weighted.Add(bones[w.boneIndex]);
            }

            var keep = new HashSet<Transform>();
            Animator animator = scan.Descriptor.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    Transform t = animator.GetBoneTransform(b);
                    if (t != null) keep.Add(t);
                }

            // Bones that weigh nothing, aren't humanoid, and have no weighted bone below them.
            List<Transform> zero = scan.Bones
                .Where(b => !weighted.Contains(b) && !keep.Contains(b) && !weighted.Any(w => w != b && w.IsChildOf(b)))
                .ToList();
            if (zero.Count == 0) yield break;
            yield return Found(zero[0],
                $"{zero.Count} bone(s) carry no skin weight: {string.Join(", ", zero.Take(5).Select(b => b.name))}{(zero.Count > 5 ? ", …" : "")}",
                "Every bone listed on a skinned mesh counts towards the bone limit even if nothing is weighted to it. " +
                "Bones used only by PhysBones or constraints may be intentional.");
        }
    }

    internal sealed class MeshBoundsRule : AvatarRule
    {
        public override string Id => "qa.mesh-bounds";
        public override string Title => "Mesh bounds too small or not unified";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            float min = context.Threshold("mesh.minBoundsExtent", 0.05f);
            List<SkinnedMeshRenderer> skinned = scan.Renderers.OfType<SkinnedMeshRenderer>().Where(s => s.sharedMesh != null).ToList();
            foreach (SkinnedMeshRenderer smr in skinned)
            {
                if (smr.updateWhenOffscreen) continue;
                Vector3 e = smr.localBounds.extents;
                if (Mathf.Max(e.x, e.y, e.z) < min)
                    yield return Found(smr, $"'{smr.name}' has very small bounds ({e.x:0.###}, {e.y:0.###}, {e.z:0.###})",
                        "When the bounds leave the camera view the mesh is culled, so tiny bounds make it vanish while it is still on screen.");
            }

            int distinct = skinned.Select(s => (s.rootBone, s.localBounds.center, s.localBounds.extents)).Distinct().Count();
            if (skinned.Count > 1 && distinct > 1)
                yield return Found(scan.Descriptor, $"{skinned.Count} skinned meshes use {distinct} different bounds/root bones",
                    "Meshes with different bounds cull at different moments, so parts of the avatar can pop out at screen edges. " +
                    "Giving every skinned mesh the same root bone and bounds keeps them together.", severity: Severity.Info);
        }
    }

    internal sealed class BlendShapeNormalsRule : AvatarRule
    {
        public override string Id => "qa.blendshape-normals";
        public override string Title => "Blendshape normals set to calculate";
        public override Severity DefaultSeverity => Severity.Info;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            var seen = new HashSet<string>();
            foreach (Mesh mesh in scan.Meshes)
            {
                if (mesh.blendShapeCount == 0) continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (!seen.Add(path) || !(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                if (importer.importBlendShapeNormals != ModelImporterNormals.Calculate) continue;
                yield return Found(mesh, $"'{System.IO.Path.GetFileName(path)}' recalculates blendshape normals on import",
                    "Calculated blendshape normals add to the avatar's size and can cause seams. Setting them to None is the usual choice " +
                    "for VRChat; large shape changes may shade slightly less accurately.",
                    Fix("Set to None", $"Sets Blend Shape Normals to None in the import settings of '{path}' and reimports it.",
                        () =>
                        {
                            UnityEditor.Undo.RecordObject(importer, "Blend shape normals");
                            importer.importBlendShapeNormals = ModelImporterNormals.None;
                            importer.SaveAndReimport();
                        }));
            }
        }
    }
}
