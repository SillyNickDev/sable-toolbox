using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Graph;
using Sable.Toolbox.Scanning;
using UnityEngine;

namespace Sable.Toolbox.Tests
{
    public class AssetGraphTests
    {
        [Test]
        public void IgnoresNullSelfAndDuplicateEdges()
        {
            var a = new Material(Shader.Find("Standard"));
            var b = new Texture2D(1, 1);
            try
            {
                var g = new AssetGraph();
                Assert.IsTrue(g.Add(a, b));
                Assert.IsFalse(g.Add(a, b), "duplicate");
                Assert.IsFalse(g.Add(a, a), "self");
                Assert.IsFalse(g.Add(a, null), "null");
                Assert.AreEqual(1, g.EdgeCount);
                Assert.AreEqual(2, g.NodeCount);
                CollectionAssert.Contains(g.ReferencedBy(b), a);
                CollectionAssert.IsEmpty(g.ReferencesOf(b));
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ScanRecordsDirectAndAnimatedReferences()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                AssetGraph g = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]).Graph;
                Object root = avatar.Descriptor;

                CollectionAssert.Contains(g.ReferencesOf(avatar.Body), avatar.BodyMat);
                CollectionAssert.Contains(g.ReferencedBy(avatar.BodyTex), avatar.BodyMat);
                CollectionAssert.Contains(g.ReferencesOf(avatar.Fx), avatar.SwapClip);
                Assert.IsTrue(g.IsAnimatedEdge(avatar.SwapClip, avatar.SwapMat));

                // Descriptor -> FX -> clip ~> SwapMat -> SwapTex: only via the animated edge.
                Assert.IsTrue(g.IsReachableOnlyThroughAnimation(avatar.SwapMat, root));
                Assert.IsTrue(g.IsReachableOnlyThroughAnimation(avatar.SwapTex, root));
                Assert.IsTrue(g.Reachable(root).Contains(avatar.SwapTex));
            }
            finally
            {
                avatar.Destroy();
            }
        }

        [Test]
        public void RendererMaterialIsNotAnimatedOnlyEvenIfAlsoAnimated()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                // Also put SwapMat on the body: it is now directly reachable.
                avatar.Body.sharedMaterials = new[] { avatar.BodyMat, avatar.SwapMat };
                AvatarScan s = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);
                CollectionAssert.IsEmpty(s.AnimatedOnlyMaterials);
                Assert.IsFalse(s.Graph.IsReachableOnlyThroughAnimation(avatar.SwapMat, avatar.Descriptor));
            }
            finally
            {
                avatar.Destroy();
            }
        }
    }
}
