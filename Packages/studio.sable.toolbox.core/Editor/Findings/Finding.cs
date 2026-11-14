using System;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Findings
{
    public enum Severity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// One problem found on an avatar: what is wrong (Message), why it matters in-game (Explanation),
    /// where it is (Location, optionally a property path inside it) and an optional one-click fix.
    /// Emitted by any module or rule; shown in the shared findings drawer.
    /// </summary>
    public sealed class Finding
    {
        public string SourceId { get; }
        public Severity Severity { get; }
        public Object Location { get; }
        public string PropertyPath { get; }
        public string Message { get; }
        public string Explanation { get; }
        public FixAction Fix { get; }
        /// <summary>The rule or check that produced this finding. Defaults to SourceId.</summary>
        public string RuleId { get; }

        public Finding(string sourceId, Severity severity, Object location, string message,
            string explanation = null, FixAction fix = null, string propertyPath = null, string ruleId = null)
        {
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A finding needs a source id.", nameof(sourceId));
            if (string.IsNullOrEmpty(message)) throw new ArgumentException("A finding needs a message.", nameof(message));
            SourceId = sourceId;
            Severity = severity;
            Location = location;
            Message = message;
            Explanation = explanation ?? string.Empty;
            Fix = fix;
            PropertyPath = propertyPath;
            RuleId = string.IsNullOrEmpty(ruleId) ? sourceId : ruleId;
        }

        /// <summary>
        /// A copy under another emitter and/or severity, keeping RuleId. Used by the linter to file
        /// rule findings under its own source with the active profile's severity.
        /// </summary>
        public Finding With(string sourceId = null, Severity? severity = null) =>
            new Finding(sourceId ?? SourceId, severity ?? Severity, Location, Message, Explanation, Fix, PropertyPath, RuleId);
    }

    /// <summary>
    /// A fix the user can apply from the findings drawer. Description says what the fix will change,
    /// shown before applying. Apply always runs as one Undo step.
    /// </summary>
    public sealed class FixAction
    {
        public string Label { get; }
        public string Description { get; }
        private readonly Action apply;

        public FixAction(string label, string description, Action apply)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Description = description ?? string.Empty;
            this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public void Apply() => ToolboxUndo.Group(Label, apply);
    }
}
