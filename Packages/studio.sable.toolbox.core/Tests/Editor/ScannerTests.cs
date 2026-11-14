using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Scanning;
using UnityEngine;

namespace Sable.Toolbox.Tests
{
    public class ScannerTests
    {
        private TestAvatar avatar;

        [SetUp] public void SetUp() => avatar = TestAvatar.Build();
        [TearDown] public void TearDown() => avatar.Destroy();

        private AvatarScan Scan() => AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);

        [Test]
        public void CountsEveryCategory()
        {
            AvatarScan s = Scan();
            Assert.AreEqual(2, s.Renderers.Count, "renderers (incl. inactive prop)");
            Assert.AreEqual(2, s.Meshes.Count, "meshes");
            Assert.AreEqual(2, s.Materials.Count, "materials (body + animated swap; prop shares body)");
            Assert.AreEqual(1, s.Shaders.Count, "shaders");
            Assert.AreEqual(2, s.Textures.Count, "textures (incl. one only reachable via clip)");
            Assert.AreEqual(2, s.Bones.Count, "bones");
            Assert.AreEqual(1, s.PhysBones.Count, "physbones");
            Assert.AreEqual(1, s.Colliders.Count, "colliders");
            Assert.AreEqual(2, s.Contacts.Count, "contacts");
            Assert.AreEqual(2, s.Constraints.Count, "constraints (VRC + Unity)");
            Assert.AreEqual(2, s.AnimatorLayers.Count, "animator layers of FX");
            Assert.AreEqual(1, s.Clips.Count, "clips");
            Assert.AreEqual(2, s.Parameters.Count, "parameters");
            Assert.AreEqual(2, s.Menus.Count, "menus (loop visited once)");
            Assert.AreEqual(0, s.MissingScriptCount, "missing scripts");
        }

        [Test]
        public void SkipsDefaultPlayableLayers()
        {
            Assert.IsTrue(Scan().AnimatorLayers.All(l => l.PlayableLayer == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX));
        }

        [Test]
        public void FindsMaterialsOnlyReachableThroughAnimation()
        {
            AvatarScan s = Scan();
            CollectionAssert.AreEqual(new[] { avatar.SwapMat }, s.AnimatedOnlyMaterials);
            CollectionAssert.Contains(s.Textures, avatar.SwapTex);
        }

        [Test]
        public void CountsMatchCountsTable()
        {
            AvatarScan s = Scan();
            var counts = s.Counts().ToDictionary(p => p.Key, p => p.Value);
            Assert.AreEqual(s.PhysBones.Count, counts["PhysBones"]);
            Assert.AreEqual(s.Materials.Count, counts["Materials"]);
        }

        [Test]
        public void CacheReturnsSameScanUntilInvalidated()
        {
            AvatarScan first = ScanCache.Get(avatar.Descriptor);
            Assert.AreSame(first, ScanCache.Get(avatar.Descriptor));
            ScanCache.Invalidate();
            Assert.IsFalse(ScanCache.IsCached(avatar.Descriptor));
            Assert.AreNotSame(first, ScanCache.Get(avatar.Descriptor));
        }

        [UnityTest]
        public IEnumerator CacheInvalidatesOnHierarchyChangeWithoutUndo()
        {
            int events = 0;
            void Count() => events++;
            UnityEditor.EditorApplication.hierarchyChanged += Count;
            try
            {
                ScanCache.Get(avatar.Descriptor);
                new GameObject("Tail").transform.SetParent(avatar.Root.transform); // no Undo, like most scripts
                for (int i = 0; i < 5; i++) yield return null;

                // Unity does not dispatch hierarchyChanged in batch mode (no hierarchy window), so this
                // can only be checked from the interactive Test Runner.
                if (events == 0) Assert.Ignore("hierarchyChanged is not dispatched in this editor session (batch mode).");
                Assert.IsFalse(ScanCache.IsCached(avatar.Descriptor), "reparent");

                ScanCache.Get(avatar.Descriptor);
                avatar.Root.transform.Find("Tail").gameObject.AddComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>();
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsFalse(ScanCache.IsCached(avatar.Descriptor), "add component");
            }
            finally
            {
                UnityEditor.EditorApplication.hierarchyChanged -= Count;
            }
        }

        [UnityTest]
        public IEnumerator CacheInvalidatesOnUndoRecordedPropertyChange()
        {
            ScanCache.Get(avatar.Descriptor);
            UnityEditor.Undo.RecordObject(avatar.Body, "Swap material");
            avatar.Body.sharedMaterials = new[] { avatar.SwapMat };
            UnityEditor.Undo.FlushUndoRecordObjects();
            yield return null;
            Assert.IsFalse(ScanCache.IsCached(avatar.Descriptor));
        }

        [Test]
        public void RescanSeesHierarchyChanges()
        {
            Assert.AreEqual(1, Scan().PhysBones.Count);
            new GameObject("Tail").transform.SetParent(avatar.Root.transform);
            avatar.Root.transform.Find("Tail").gameObject.AddComponent<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>();
            Assert.AreEqual(2, Scan().PhysBones.Count);
        }
    }
}
