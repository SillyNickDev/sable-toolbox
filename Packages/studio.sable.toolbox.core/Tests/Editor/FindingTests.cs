using System;
using NUnit.Framework;
using Sable.Toolbox.Findings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Tests
{
    public class FindingTests
    {
        private static Finding F(string source, Severity s, string msg = "m") => new Finding(source, s, null, msg);

        [Test]
        public void RequiresSourceAndMessage()
        {
            Assert.Throws<ArgumentException>(() => new Finding("", Severity.Info, null, "m"));
            Assert.Throws<ArgumentException>(() => new Finding("src", Severity.Info, null, ""));
        }

        [Test]
        public void ReplaceOnlyTouchesItsOwnSource()
        {
            var set = new FindingSet();
            set.Replace("linter", new[] { F("linter", Severity.Warning), F("linter", Severity.Info) });
            set.Replace("budget", new[] { F("budget", Severity.Error) });
            set.Replace("linter", new[] { F("linter", Severity.Info) });

            Assert.AreEqual(2, set.Count);
            Assert.AreEqual(1, set.CountOf(Severity.Error));
            Assert.AreEqual(0, set.CountOf(Severity.Warning));
        }

        [Test]
        public void SortsMostSevereFirst()
        {
            var set = new FindingSet();
            set.Replace("a", new[] { F("a", Severity.Info), F("a", Severity.Error), F("a", Severity.Warning) });
            Assert.AreEqual(Severity.Error, set[0].Severity);
            Assert.AreEqual(Severity.Info, set[2].Severity);
        }

        [Test]
        public void RejectsFindingsFromAnotherSource()
        {
            var set = new FindingSet();
            Assert.Throws<ArgumentException>(() => set.Replace("a", new[] { F("b", Severity.Info) }));
        }

        [Test]
        public void ChangedFiresOnReplaceAndClear()
        {
            var set = new FindingSet();
            int fired = 0;
            set.Changed += () => fired++;
            set.Replace("a", new[] { F("a", Severity.Info) });
            set.Clear("a");
            Assert.AreEqual(2, fired);
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void FixIsOneUndoStep()
        {
            var go = new GameObject("before");
            try
            {
                var fix = new FixAction("Rename twice", "Renames the object.", () =>
                {
                    UnityEditor.Undo.RecordObject(go, "step 1");
                    go.name = "middle";
                    UnityEditor.Undo.RecordObject(go, "step 2");
                    go.name = "after";
                });
                new Finding("test", Severity.Warning, go, "Bad name", "Because.", fix).Fix.Apply();
                Assert.AreEqual("after", go.name);

                UnityEditor.Undo.PerformUndo();
                Assert.AreEqual("before", go.name, "a single undo reverts the whole fix");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
