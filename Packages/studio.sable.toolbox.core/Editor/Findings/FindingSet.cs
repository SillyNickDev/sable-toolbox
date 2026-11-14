using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Sable.Toolbox.Findings
{
    /// <summary>
    /// The one shared findings list. Each emitter (a module, the linter, a rule) owns the findings
    /// tagged with its source id and replaces them wholesale on every run, so re-running one tool
    /// never wipes another tool's results.
    /// </summary>
    public sealed class FindingSet : IReadOnlyList<Finding>
    {
        private readonly List<Finding> items = new List<Finding>();

        public event Action Changed;

        public int Count => items.Count;
        public Finding this[int index] => items[index];

        public void Replace(string sourceId, IEnumerable<Finding> findings)
        {
            items.RemoveAll(f => f.SourceId == sourceId);
            foreach (Finding f in findings ?? Enumerable.Empty<Finding>())
            {
                if (f.SourceId != sourceId)
                    throw new ArgumentException($"Finding from '{f.SourceId}' passed to Replace for '{sourceId}'.");
                items.Add(f);
            }
            // Most severe first; stable within a severity.
            List<Finding> sorted = items.OrderByDescending(f => f.Severity).ToList();
            items.Clear();
            items.AddRange(sorted);
            Changed?.Invoke();
        }

        public void Clear(string sourceId) => Replace(sourceId, null);

        public void ClearAll()
        {
            items.Clear();
            Changed?.Invoke();
        }

        public int CountOf(Severity severity) => items.Count(f => f.Severity == severity);

        public IEnumerator<Finding> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
