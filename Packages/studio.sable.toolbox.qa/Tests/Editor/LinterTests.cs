using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sable.Toolbox.Adapters;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Tests;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Qa.Tests
{
    public class LinterTests
    {
        private TestAvatar avatar;

        [SetUp] public void SetUp() => avatar = TestAvatar.Build();
        [TearDown] public void TearDown() => avatar.Destroy();

        private AvatarScan Scan() => AvatarScanner.Scan(avatar.Descriptor, new IBuildToolAdapter[0]);
        private static IAvatarRule Rule(string id) => Linter.Rules.Single(r => r.Id == id);
        private List<Finding> Check(string id, RuleContext ctx = null) =>
            Rule(id).Check(Scan(), ctx ?? Linter.Context()).ToList();

        [Test]
        public void AllStarterRulesAreDiscovered()
        {
            string[] expected =
            {
                "qa.missing-shader", "qa.poiyomi-unlocked", "qa.texture-mipmaps", "qa.mixed-write-defaults",
                "qa.unused-parameter", "qa.parameter-budget", "qa.empty-state", "qa.physbone-no-limits",
                "qa.physbone-collision-checks", "qa.zero-weight-bones", "qa.mesh-bounds", "qa.blendshape-normals",
                "qa.missing-visemes", "qa.eye-look", "qa.light-audio-enabled", "qa.missing-scripts"
            };
            CollectionAssert.IsSubsetOf(expected, Linter.Rules.Select(r => r.Id).ToList());
        }

        [Test]
        public void ProfilesLoadFromJson()
        {
            CollectionAssert.AreEquivalent(new[] { "personal", "commission", "public", "android" }, Linter.Profiles.Select(p => p.id));
            Assert.AreEqual(64f, Linter.Context().Threshold("physbone.maxCollisionChecksPerComponent", 0));
        }

        [Test]
        public void ProfileAndOverridesSetSeverity()
        {
            IAvatarRule poi = Rule("qa.poiyomi-unlocked");
            Assert.AreEqual(Severity.Error, Linter.EffectiveSeverity(poi, Linter.Profile("commission"), null));
            Assert.AreEqual(Severity.Info, Linter.EffectiveSeverity(poi, Linter.Profile("personal"), null));
            var settings = new QaSettings();
            settings.overrides.Add(new RuleSetting { id = poi.Id, severity = "Off" });
            Assert.IsNull(Linter.EffectiveSeverity(poi, Linter.Profile("commission"), settings));
        }

        [Test]
        public void SuppressedFindingsAreLeftOut()
        {
            QaProfile profile = Linter.Profile("personal");
            var settings = new QaSettings();
            Finding eye = Linter.Run(Scan(), profile, settings).Single(f => f.RuleId == "qa.eye-look");
            settings.suppressions.Add(new Suppression { ruleId = eye.RuleId, objectId = QaSettings.ObjectId(eye.Location) });
            Assert.IsFalse(Linter.Run(Scan(), profile, settings).Any(f => f.RuleId == "qa.eye-look"));
        }

        [Test]
        public void FindingsAreFiledUnderLinterWithRuleId()
        {
            List<Finding> results = Linter.Run(Scan(), Linter.Profile("personal"), new QaSettings());
            Assert.IsTrue(results.All(f => f.SourceId == Linter.SourceId));
            Assert.IsTrue(results.Any(f => f.RuleId == "qa.missing-visemes"));
        }

        [Test]
        public void CrashingRuleBecomesAnErrorFinding()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            List<Finding> results = Linter.Run(Scan(), Linter.Profile("personal"), null, new IAvatarRule[] { new ThrowingRule(0) });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(Severity.Error, results[0].Severity);
        }

        [Test]
        public void MissingShaderAndEmptySlot()
        {
            var broken = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Broken" };
            try
            {
                avatar.Body.sharedMaterials = new[] { avatar.BodyMat, broken, null };
                List<Finding> f = Check("qa.missing-shader");
                Assert.IsTrue(f.Any(x => x.Location == broken));
                Assert.IsTrue(f.Any(x => x.Location == avatar.Body && x.Severity == Severity.Warning));
            }
            finally { Object.DestroyImmediate(broken); }
        }

        [Test]
        public void UnusedParametersAreFoundAndRemovable()
        {
            List<Finding> f = Check("qa.unused-parameter");
            Assert.AreEqual(2, f.Count, "neither parameter is in the FX controller");
            avatar.Fx.AddParameter("Toggle", AnimatorControllerParameterType.Bool);
            f = Check("qa.unused-parameter");
            Assert.AreEqual(1, f.Count);
            f[0].Fix.Apply();
            CollectionAssert.AreEqual(new[] { "Toggle" }, avatar.Descriptor.expressionParameters.parameters.Select(p => p.name));
        }

        [Test]
        public void ParameterBudget()
        {
            CollectionAssert.IsEmpty(Check("qa.parameter-budget"));
            avatar.Descriptor.expressionParameters.parameters = Enumerable.Range(0, 40)
                .Select(i => new VRCExpressionParameters.Parameter { name = "p" + i, valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true })
                .ToArray();
            Assert.AreEqual(1, Check("qa.parameter-budget").Count, "40 synced ints = 320 bits");
        }

        [Test]
        public void MixedWriteDefaults()
        {
            CollectionAssert.IsEmpty(Check("qa.mixed-write-defaults"));
            AnimatorState off = avatar.Fx.layers[1].stateMachine.AddState("Off");
            off.writeDefaultValues = false;
            off.motion = avatar.SwapClip;
            Assert.AreEqual(1, Check("qa.mixed-write-defaults").Count);
        }

        [Test]
        public void EmptyStates()
        {
            CollectionAssert.IsEmpty(Check("qa.empty-state"));
            avatar.Fx.layers[1].stateMachine.AddState("Nothing");
            Assert.AreEqual(1, Check("qa.empty-state").Count);
        }

        [Test]
        public void PhysBoneCollisionChecksUseThreshold()
        {
            var strict = new RuleContext(new Dictionary<string, float> { ["physbone.maxCollisionChecksPerComponent"] = 1 });
            CollectionAssert.IsEmpty(Check("qa.physbone-collision-checks"), "2 transforms × 1 collider is under 64");
            Assert.AreEqual(1, Check("qa.physbone-collision-checks", strict).Count);
        }

        [Test]
        public void VisemesAndEyeLook()
        {
            Assert.AreEqual(1, Check("qa.missing-visemes").Count, "lip sync not configured");
            Assert.AreEqual(1, Check("qa.eye-look").Count);
            avatar.Descriptor.enableEyeLook = true;
            CollectionAssert.IsEmpty(Check("qa.eye-look"));
        }

        [Test]
        public void EnabledLightIsFoundAndFixIsUndoable()
        {
            var light = avatar.Body.gameObject.AddComponent<Light>();
            Finding f = Check("qa.light-audio-enabled").Single();
            f.Fix.Apply();
            Assert.IsFalse(light.enabled);
            UnityEditor.Undo.PerformUndo();
            Assert.IsTrue(light.enabled);
        }

        [Test]
        public void ReportsContainFindingsAndEscapeHtml()
        {
            var findings = new List<Finding> { new Finding(Linter.SourceId, Severity.Error, null, "Bad <thing>", "Why", ruleId: "qa.x") };
            QaProfile p = Linter.Profile("commission");
            StringAssert.Contains("Bad <thing>", LintReport.Markdown("Av", p, findings, DateTime.Now));
            string html = LintReport.Html("Av", p, findings, DateTime.Now);
            StringAssert.Contains("Bad &lt;thing&gt;", html);
            StringAssert.Contains("1 errors", html);
        }

        /// <summary>No parameterless constructor, so Linter.Rules never picks it up outside this test.</summary>
        private sealed class ThrowingRule : IAvatarRule
        {
            public ThrowingRule(int _) { }
            public string Id => "test.throws";
            public string Title => "Throws";
            public Severity DefaultSeverity => Severity.Warning;
            public IEnumerable<Finding> Check(AvatarScan scan, RuleContext context) => throw new InvalidOperationException("boom");
        }
    }
}
