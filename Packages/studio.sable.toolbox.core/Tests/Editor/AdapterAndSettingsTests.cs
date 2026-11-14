using System;
using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Settings;
using Sable.Toolbox.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Tests
{
    public class AdapterAndSettingsTests
    {
        // A real base type with a subclass on the test avatar (VRCPhysBone : VRCPhysBoneBase), standing in
        // for e.g. Modular Avatar's AvatarTagComponent. Editor-assembly MonoBehaviours cannot be attached.
        private const string BaseName = "VRC.Dynamics.VRCPhysBoneBase";

        [Test]
        public void TypeNameAdapterMatchesSubclassesOfNamedBase()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                var adapter = new TypeNameAdapter("test", "Test tool", new[] { BaseName });

                Assert.IsTrue(adapter.IsInstalled);
                AvatarScan s = AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[] { adapter });
                Assert.AreEqual(1, s.BuildTools.Count);
                Assert.AreEqual(1, s.BuildTools[0].Components.Count);
            }
            finally
            {
                avatar.Destroy();
            }
        }

        [Test]
        public void AbsentToolIsNotInstalledAndNotReported()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                var adapter = new TypeNameAdapter("none", "Not here", new[] { "No.Such.Type" });
                Assert.IsFalse(adapter.IsInstalled);
                Assert.AreEqual(0, AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[] { adapter }).BuildTools.Count);
            }
            finally
            {
                avatar.Destroy();
            }
        }

        [Test]
        public void BuiltInAdaptersLoadFromJson()
        {
            string[] ids = AdapterRegistry.All.Select(a => a.Id).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "modular-avatar", "vrcfury", "avatar-optimizer", "d4rk-avatar-optimizer" }, ids);
        }

        [Test]
        public void OverviewModuleIsDiscovered()
        {
            Assert.IsTrue(ModuleRegistry.CreateAll().Any(m => m.Id == "studio.sable.toolbox.core.overview"));
        }

        [Test]
        public void EveryModuleBuildsItsViewForAnAvatar()
        {
            TestAvatar avatar = TestAvatar.Build();
            try
            {
                var context = new ToolboxContext { Avatar = avatar.Descriptor };
                foreach (IToolboxModule module in ModuleRegistry.CreateAll())
                    Assert.IsNotNull(module.CreateView(context), module.Id);
            }
            finally
            {
                avatar.Destroy();
            }
        }

        [Test]
        public void WindowOpensAndCloses()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Needs a graphics device; run without -nographics.");
            var window = EditorWindow.GetWindow<SableToolboxWindow>();
            Assert.IsNotNull(window);
            window.Close();
        }

        [Test]
        public void GeneratedPathsStayUnderGeneratedRoot()
        {
            SableToolboxSettings settings = SableToolboxSettings.instance;
            string previous = settings.GeneratedRoot;
            const string root = "Assets/SableToolboxTestGenerated";
            try
            {
                settings.GeneratedRoot = root;
                string path = GeneratedAssets.UniquePath("Android", "Body.mat");
                StringAssert.StartsWith(root + "/Android/", path);
                Assert.IsTrue(AssetDatabase.IsValidFolder(root + "/Android"));
                Assert.IsTrue(GeneratedAssets.IsGenerated(path));
                Assert.IsFalse(GeneratedAssets.IsGenerated("Assets/Body.mat"));
                Assert.Throws<ArgumentException>(() => GeneratedAssets.UniquePath("x", "../Body.mat"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
                settings.GeneratedRoot = previous;
            }
        }
    }
}
