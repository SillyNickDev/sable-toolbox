using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Qa.Budget;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using Sable.Toolbox.Tests;
using UnityEngine;

namespace Sable.Toolbox.Qa.Tests
{
    public class BudgetTests
    {
        [Test]
        public void LimitsLoadFromJsonWithSdkValues()
        {
            PlatformLimits pc = PerformanceLimits.Platform("pc");
            PlatformLimits android = PerformanceLimits.Platform("android");
            Assert.IsNotNull(pc);
            Assert.IsNotNull(android);
            MetricLimit pcTris = pc.metrics.Single(m => m.id == "polyCount");
            Assert.AreEqual(32000, pcTris.excellent);
            Assert.AreEqual(70000, pcTris.good);
            MetricLimit mobilePb = android.metrics.Single(m => m.id == "physBone.componentCount");
            Assert.AreEqual(new double[] { 0, 4, 6, 8 }, new[] { mobilePb.excellent, mobilePb.good, mobilePb.medium, mobilePb.poor });
            Assert.AreEqual(10, android.downloadLimitMB);
        }

        [Test]
        public void RatesAtBoundaries()
        {
            var m = new MetricLimit { excellent = 10, good = 20, medium = 30, poor = 40 };
            Assert.AreEqual(Rank.Excellent, m.Rate(10));
            Assert.AreEqual(Rank.Good, m.Rate(10.5));
            Assert.AreEqual(Rank.Poor, m.Rate(40));
            Assert.AreEqual(Rank.VeryPoor, m.Rate(41));
            Assert.AreEqual(30, m.Ceiling(Rank.Medium));
        }

        private class FakeStats { public int polyCount = 5; public float textureMegabytes = 1.5f; public Nested? physBone = new Nested { componentCount = 3 }; public bool flag = true; }
        private struct Nested { public int componentCount; }

        [Test]
        public void ReadsSdkFieldPathsIncludingNullableNested()
        {
            var s = new FakeStats();
            Assert.AreEqual(5, PerformanceBudget.Read(s, "polyCount"));
            Assert.AreEqual(1.5, PerformanceBudget.Read(s, "textureMegabytes"), 1e-6);
            Assert.AreEqual(3, PerformanceBudget.Read(s, "physBone.componentCount"));
            Assert.AreEqual(1, PerformanceBudget.Read(s, "flag"));
            Assert.AreEqual(0, PerformanceBudget.Read(s, "notAField"));
            s.physBone = null;
            Assert.AreEqual(0, PerformanceBudget.Read(s, "physBone.componentCount"));
        }

        [Test]
        public void EvaluatesTestAvatarWithSdkCalculator()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                AvatarScan scan = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);
                PlatformResult pc = PerformanceBudget.Evaluate(scan, PerformanceLimits.Platform("pc"));
                MetricResult tris = pc.Metrics.Single(m => m.Limit.id == "polyCount");
                Assert.Greater(tris.Value, 0, "SDK counted the test triangles");
                Assert.AreEqual(1, pc.Metrics.Single(m => m.Limit.id == "physBone.componentCount").Value);
                Assert.AreEqual(Rank.Excellent, tris.Rank);

                double contributorTris = PerformanceBudget.Contributors(scan, "polyCount").Sum(c => c.Amount);
                Assert.AreEqual(2, contributorTris, "body + inactive prop, one triangle each");
            }
            finally { avatar.Destroy(); }
        }

        [Test]
        public void TextureMemoryCountsMips()
        {
            var noMips = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var mips = new Texture2D(4, 4, TextureFormat.RGBA32, true);
            try
            {
                Assert.AreEqual(64, TextureMemory.Bytes(noMips));
                Assert.AreEqual(64 + 16 + 4, TextureMemory.Bytes(mips));
            }
            finally { Object.DestroyImmediate(noMips); Object.DestroyImmediate(mips); }
        }

        [Test]
        public void CountsTextureMemoryOnlyReachableThroughAnimation()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                AvatarScan scan = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);
                Assert.AreEqual(TextureMemory.Megabytes(avatar.SwapTex), PerformanceBudget.AnimatedOnlyTextureMegabytes(scan), 1e-9);
            }
            finally { avatar.Destroy(); }
        }
    }
}
