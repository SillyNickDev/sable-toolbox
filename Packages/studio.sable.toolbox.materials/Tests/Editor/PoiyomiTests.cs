using System.Collections;
using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using Sable.Toolbox.Tests;
using Sable.Toolbox.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Materials.Tests
{
    /// <summary>Runs against the real Poiyomi package when it is installed; skipped otherwise.</summary>
    public class PoiyomiTests
    {
        private TestAvatar avatar;
        private Material poi;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Shader.Find(".poiyomi/Poiyomi Toon");
            if (shader == null || !PoiyomiBridge.IsAvailable) Assert.Ignore("Poiyomi is not installed in this project.");
            avatar = TestAvatar.Build();
            poi = new Material(shader) { name = "Poi" };
            avatar.Body.sharedMaterials = new[] { poi };
        }

        [TearDown]
        public void TearDown()
        {
            if (poi != null && !AssetDatabase.Contains(poi)) Object.DestroyImmediate(poi);
            avatar?.Destroy();
        }

        private AvatarScan Scan() => AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);

        [Test]
        public void RecognisesPoiyomiFamilyVersionAndLockState()
        {
            Assert.IsTrue(PoiyomiBridge.IsOptimizerMaterial(poi));
            Assert.IsFalse(PoiyomiBridge.IsLocked(poi));
            Assert.AreEqual(LockState.Unlocked, MaterialInfo.Lock(poi));
            Assert.AreEqual("poiyomi", PropertyAliases.FamilyOf(poi));
            StringAssert.IsMatch(@"^\d+\.\d+\.\d+$", MaterialInfo.ShaderVersion(poi));
        }

        [Test]
        public void AliasPropertiesExistOnInstalledPoiyomi()
        {
            foreach (PropertyAlias alias in PropertyAliases.Aliases)
            {
                string prop = alias.PropertyFor("poiyomi");
                if (prop != null) Assert.GreaterOrEqual(poi.shader.FindPropertyIndex(prop), 0, $"{alias.id} -> {prop}");
            }
        }

        [Test]
        public void AuditFlagsUntaggedAnimatedPropertyUntilMarked()
        {
            var binding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "material._EmissionStrength");
            AnimationUtility.SetEditorCurve(avatar.SwapClip, binding, AnimationCurve.Constant(0, 1, 2));

            var issue = AnimatedPropertyAudit.Run(Scan()).Single();
            Assert.AreEqual("_EmissionStrength", issue.Property);
            Assert.IsFalse(issue.AlreadyLocked);

            AnimatedPropertyAudit.MarkAnimated(issue);
            Assert.AreEqual("1", poi.GetTag("_EmissionStrengthAnimated", false, ""));
            CollectionAssert.IsEmpty(AnimatedPropertyAudit.Run(Scan()));
        }

        [Test]
        public void RenamedAnimatedPropertiesCountAsMarked()
        {
            poi.SetOverrideTag("_ColorAnimated", "2");
            Assert.IsTrue(PoiyomiBridge.IsMarkedAnimated(poi, "_Color"));
            Assert.IsTrue(PoiyomiBridge.IsMarkedAnimated(poi, "_Color_Body"), "locked shaders rename '2' properties");
            Assert.IsFalse(PoiyomiBridge.IsMarkedAnimated(poi, "_EmissionStrength"));
        }

        [Test]
        public void LocksAndUnlocksThroughPoiyomi()
        {
            const string folder = "Assets/SableToolboxPoiTest";
            try
            {
                AssetDatabase.CreateFolder("Assets", "SableToolboxPoiTest");
                AssetDatabase.CreateAsset(poi, folder + "/Poi.mat");

                Assert.IsTrue(PoiyomiBridge.SetLocked(new[] { poi }, true), "lock");
                Assert.IsTrue(PoiyomiBridge.IsLocked(poi));
                Assert.AreEqual(LockState.Locked, MaterialInfo.Lock(poi));
                Assert.AreEqual("poiyomi", PropertyAliases.FamilyOf(poi), "family survives locking");
                StringAssert.IsMatch(@"^\d+\.\d+\.\d+$", MaterialInfo.ShaderVersion(poi), "version from the original shader");
                CollectionAssert.IsEmpty(MaterialEdits.Editable(new[] { poi }), "locked materials are not edited");

                Assert.IsTrue(PoiyomiBridge.SetLocked(new[] { poi }, false), "unlock");
                Assert.AreEqual(LockState.Unlocked, MaterialInfo.Lock(poi));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                poi = null;
            }
        }

        [UnityTest]
        public IEnumerator MaterialsPageRendersInventory()
        {
            if (!TestHost.HasGraphics) Assert.Ignore("Needs a graphics device; run without -nographics.");
            var context = new ToolboxContext { Avatar = avatar.Descriptor };
            VisualElement view = new MaterialManagerModule().CreateView(context);
            TestHostWindow window = TestHost.Attach(view);
            try
            {
                yield return null;
                Assert.IsNotNull(TestHost.FindLabel(view, t => t.StartsWith("3 materials")), "inventory summary");
                TestHost.Click(TestHost.FindButton(view, "Locking"));
                yield return null;
                Assert.IsNotNull(TestHost.FindLabel(view, t => t.Contains("unlocked ·")), "locking tab shows counts");
            }
            finally { window.Close(); }
        }
    }
}
