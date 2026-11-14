using System.Collections;
using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Qa.Budget;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using Sable.Toolbox.Tests;
using Sable.Toolbox.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Qa.Tests
{
    public class PageTests
    {
        private TestAvatar avatar;
        private TestHostWindow window;

        [SetUp] public void SetUp() => avatar = TestAvatar.Build();

        [TearDown]
        public void TearDown()
        {
            if (window != null) window.Close();
            avatar.Destroy();
        }

        [UnityTest]
        public IEnumerator LinterPageRunsAndFillsSharedFindings()
        {
            if (!TestHost.HasGraphics) Assert.Ignore("Needs a graphics device; run without -nographics.");
            var context = new ToolboxContext { Avatar = avatar.Descriptor };
            VisualElement view = new LinterModule().CreateView(context);
            window = TestHost.Attach(view);
            yield return null;

            TestHost.Click(TestHost.FindButton(view, "Run"));
            yield return null;
            Assert.IsTrue(context.Findings.Any(f => f.RuleId == "qa.eye-look"), "lint findings reached the shared drawer");
            Assert.IsNotNull(TestHost.FindLabel(view, t => t.Contains("errors,") && t.Contains("warnings,")), "summary shown");
        }

        [UnityTest]
        public IEnumerator BudgetPageShowsRanks()
        {
            if (!TestHost.HasGraphics) Assert.Ignore("Needs a graphics device; run without -nographics.");
            var context = new ToolboxContext { Avatar = avatar.Descriptor };
            VisualElement view = new BudgetModule().CreateView(context);
            window = TestHost.Attach(view);
            yield return null;
            Assert.IsNotNull(TestHost.FindLabel(view, t => t.StartsWith("PC: ")), "PC rank header");
            Assert.IsNotNull(TestHost.FindLabel(view, t => t.StartsWith("Android: ")), "Android rank header");
        }

        [Test]
        public void PoiyomiUnlockedRuleFiresOnRealPoiyomi()
        {
            Shader poi = Shader.Find(".poiyomi/Poiyomi Toon");
            if (poi == null || !PoiyomiBridge.IsAvailable) Assert.Ignore("Poiyomi is not installed in this project.");
            var mat = new Material(poi) { name = "Poi" };
            try
            {
                avatar.Body.sharedMaterials = new[] { mat };
                var scan = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);
                var found = Linter.Rules.Single(r => r.Id == "qa.poiyomi-unlocked").Check(scan, Linter.Context()).ToList();
                Assert.AreEqual(1, found.Count);
                Assert.IsNotNull(found[0].Fix);
            }
            finally { Object.DestroyImmediate(mat); }
        }
    }
}
