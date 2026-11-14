using System.IO;
using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Tests;
using UnityEngine;

namespace Sable.Toolbox.Materials.Tests
{
    public class MaterialTests
    {
        private TestAvatar avatar;

        [SetUp] public void SetUp() => avatar = TestAvatar.Build();
        [TearDown] public void TearDown() => avatar.Destroy();

        private AvatarScan Scan() => AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);

        [Test]
        public void FamiliesAndAliasesLoad()
        {
            Assert.AreEqual("standard", PropertyAliases.FamilyOf(avatar.BodyMat));
            CollectionAssert.IsSubsetOf(new[] { "poiyomi", "liltoon", "standard" }, PropertyAliases.Families.Select(f => f.id).ToList());
            PropertyAlias color = PropertyAliases.Aliases.Single(a => a.id == "mainColor");
            Assert.AreEqual("_Color", color.PropertyFor("poiyomi"));
            Assert.AreEqual("_LineWidth", PropertyAliases.Aliases.Single(a => a.id == "outlineWidth").PropertyFor("poiyomi"));
        }

        [Test]
        public void PropertyValuesRoundTrip()
        {
            avatar.BodyMat.SetColor("_Color", Color.red);
            avatar.BodyMat.SetFloat("_Glossiness", 0.25f);
            PropertyValue color = PropertyValue.Read(avatar.BodyMat, "_Color");
            PropertyValue gloss = PropertyValue.Read(avatar.BodyMat, "_Glossiness");
            Assert.AreEqual("Color", color.type);
            Assert.IsTrue(color.WriteTo(avatar.SwapMat));
            Assert.IsTrue(gloss.WriteTo(avatar.SwapMat));
            Assert.AreEqual(Color.red, avatar.SwapMat.GetColor("_Color"));
            Assert.AreEqual(0.25f, avatar.SwapMat.GetFloat("_Glossiness"), 1e-6);
            Assert.IsNull(PropertyValue.Read(avatar.BodyMat, "_NoSuchProperty"));
            Assert.IsFalse(gloss.WriteTo(avatar.SwapMat, "_Color"), "type mismatch is refused");
        }

        [Test]
        public void BulkSetIsUndoable()
        {
            var mats = new[] { avatar.BodyMat, avatar.SwapMat };
            var v = new PropertyValue { name = "_Glossiness", type = "Float", number = 0.9f };
            Assert.AreEqual(2, MaterialEdits.Set(mats, v));
            Assert.AreEqual(0.9f, avatar.SwapMat.GetFloat("_Glossiness"), 1e-6);
            UnityEditor.Undo.PerformUndo();
            Assert.AreNotEqual(0.9f, avatar.SwapMat.GetFloat("_Glossiness"));
        }

        [Test]
        public void AliasDrivesEachFamilysProperty()
        {
            PropertyAlias color = PropertyAliases.Aliases.Single(a => a.id == "mainColor");
            int n = MaterialEdits.SetAlias(new[] { avatar.BodyMat }, color, new PropertyValue { name = "_Color", type = "Color", color = Color.green });
            Assert.AreEqual(1, n);
            Assert.AreEqual(Color.green, avatar.BodyMat.color);
        }

        [Test]
        public void PasteAndReplaceTexture()
        {
            avatar.BodyMat.SetFloat("_Metallic", 0.7f);
            var values = new[] { PropertyValue.Read(avatar.BodyMat, "_Metallic") };
            Assert.AreEqual(1, MaterialEdits.Paste(new[] { avatar.SwapMat }, values));
            Assert.AreEqual(0.7f, avatar.SwapMat.GetFloat("_Metallic"), 1e-6);

            Assert.AreEqual(1, MaterialEdits.ReplaceTexture(new[] { avatar.BodyMat, avatar.SwapMat }, avatar.BodyTex, avatar.SwapTex));
            Assert.AreEqual(avatar.SwapTex, avatar.BodyMat.mainTexture);
            UnityEditor.Undo.PerformUndo();
            Assert.AreEqual(avatar.BodyTex, avatar.BodyMat.mainTexture);
        }

        [Test]
        public void InventoryRows()
        {
            var rows = MaterialInventory.Build(Scan());
            Assert.AreEqual(2, rows.Count);
            MaterialRow body = rows.Single(r => r.Material == avatar.BodyMat);
            CollectionAssert.AreEquivalent(new[] { "Body[0]", "Prop[0]" }, body.Slots);
            Assert.AreEqual("standard", body.Family);
            Assert.AreEqual(LockState.NotLockable, body.Lock);
            Assert.Greater(body.VramMB, 0);
            MaterialRow swap = rows.Single(r => r.Material == avatar.SwapMat);
            Assert.IsTrue(swap.AnimatedSwap);
            CollectionAssert.IsEmpty(swap.Slots);
            Assert.AreEqual("(animation only)", MaterialInventory.GroupKey(swap, MaterialInventory.Grouping.Mesh));
            Assert.AreEqual("Standard", MaterialInventory.GroupKey(body, MaterialInventory.Grouping.Shader));
            Assert.IsTrue(body.Matches("stand"));
            Assert.IsFalse(body.Matches("poiyomi"));
        }

        [Test]
        public void AuditFindsNothingWithoutPoiyomiMaterials()
        {
            CollectionAssert.IsEmpty(AnimatedPropertyAudit.Run(Scan()));
        }

        [Test]
        public void PresetsSaveLoadAndApplyAcrossFamilies()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SableToolboxPresetTest_" + System.Guid.NewGuid().ToString("N")));
            try
            {
                var preset = new MaterialPreset { name = "Matte: cloth/test" };
                preset.Set("poiyomi", new[] { new PropertyValue { name = "_Color", type = "Color", color = Color.blue } });
                preset.Set("poiyomi", new[] { new PropertyValue { name = "_Color", type = "Color", color = Color.cyan } }); // replaces
                string path = PresetLibrary.Save(preset, folder);
                Assert.AreEqual("Matte_ cloth_test.json", Path.GetFileName(path));

                MaterialPreset loaded = PresetLibrary.LoadAll(folder).Single();
                Assert.AreEqual(1, loaded.families.Count);
                // A Poiyomi preset applied to a Standard material goes through the alias map (_Color -> _Color).
                Assert.AreEqual(1, loaded.ApplyTo(new[] { avatar.BodyMat }));
                Assert.AreEqual(Color.cyan, avatar.BodyMat.color);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
