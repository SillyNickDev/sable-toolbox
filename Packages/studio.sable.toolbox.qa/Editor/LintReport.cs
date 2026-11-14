using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Sable.Toolbox.Findings;

namespace Sable.Toolbox.Qa
{
    /// <summary>Markdown and HTML lint reports, usable as a QA record for a commission.</summary>
    public static class LintReport
    {
        public static string Markdown(string avatar, QaProfile profile, IReadOnlyList<Finding> findings, DateTime when)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Lint report: {avatar}").AppendLine();
            sb.AppendLine($"- Profile: {profile.name}");
            sb.AppendLine($"- Date: {when:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Result: {Summary(findings)}").AppendLine();
            foreach (Severity s in new[] { Severity.Error, Severity.Warning, Severity.Info })
            {
                List<Finding> group = findings.Where(f => f.Severity == s).ToList();
                if (group.Count == 0) continue;
                sb.AppendLine($"## {s} ({group.Count})").AppendLine();
                foreach (Finding f in group)
                {
                    sb.AppendLine($"- **{f.Message}** `{f.RuleId}`{(f.Location != null ? $" — {f.Location.name}" : "")}");
                    if (!string.IsNullOrEmpty(f.Explanation)) sb.AppendLine($"  {f.Explanation}");
                }
                sb.AppendLine();
            }
            if (findings.Count == 0) sb.AppendLine("No findings.");
            return sb.ToString();
        }

        public static string Html(string avatar, QaProfile profile, IReadOnlyList<Finding> findings, DateTime when)
        {
            string E(string s) => WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=\"utf-8\"><title>Lint report: ").Append(E(avatar)).Append("</title>");
            sb.Append("<style>body{font:14px/1.5 system-ui,sans-serif;max-width:860px;margin:2em auto;padding:0 1em;color:#222}")
              .Append("h2{margin-top:1.6em}.Error{border-left:4px solid #c62828}.Warning{border-left:4px solid #e0a000}")
              .Append(".Info{border-left:4px solid #1e88e5}li{list-style:none;padding:.4em .8em;margin:.4em 0;background:#f6f6f6}")
              .Append("code{color:#666;font-size:12px}p{margin:.2em 0;color:#444}</style>");
            sb.Append("<h1>Lint report: ").Append(E(avatar)).Append("</h1>");
            sb.Append("<p>Profile: ").Append(E(profile.name)).Append(" · ").Append(when.ToString("yyyy-MM-dd HH:mm"))
              .Append(" · ").Append(E(Summary(findings))).Append("</p>");
            foreach (Severity s in new[] { Severity.Error, Severity.Warning, Severity.Info })
            {
                List<Finding> group = findings.Where(f => f.Severity == s).ToList();
                if (group.Count == 0) continue;
                sb.Append("<h2>").Append(s).Append(" (").Append(group.Count).Append(")</h2><ul>");
                foreach (Finding f in group)
                    sb.Append("<li class=\"").Append(s).Append("\"><b>").Append(E(f.Message)).Append("</b> <code>")
                      .Append(E(f.RuleId)).Append("</code>").Append(f.Location != null ? " — " + E(f.Location.name) : "")
                      .Append("<p>").Append(E(f.Explanation)).Append("</p></li>");
                sb.Append("</ul>");
            }
            if (findings.Count == 0) sb.Append("<p>No findings.</p>");
            return sb.ToString();
        }

        public static string Summary(IReadOnlyList<Finding> findings) =>
            $"{findings.Count(f => f.Severity == Severity.Error)} errors, " +
            $"{findings.Count(f => f.Severity == Severity.Warning)} warnings, " +
            $"{findings.Count(f => f.Severity == Severity.Info)} info";
    }
}
