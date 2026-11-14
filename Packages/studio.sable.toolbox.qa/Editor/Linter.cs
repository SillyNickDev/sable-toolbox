using System;
using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Settings;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sable.Toolbox.Qa
{
    [Serializable]
    public sealed class RuleSetting
    {
        public string id;
        /// <summary>"" = rule default, "Off", or a Severity name.</summary>
        public string severity = "";
    }

    [Serializable]
    public sealed class QaProfile
    {
        public string id;
        public string name;
        public bool includeOtherRules = true;
        public List<RuleSetting> rules = new List<RuleSetting>();
    }

    [Serializable]
    public sealed class Suppression
    {
        public string ruleId;
        /// <summary>GlobalObjectId of the suppressed object, stable across sessions.</summary>
        public string objectId;
    }

    /// <summary>Per-project linter options, stored in the toolbox settings file.</summary>
    [Serializable]
    public sealed class QaSettings
    {
        public string activeProfile = "personal";
        public bool runOnSave;
        public bool buildGate;
        /// <summary>Project overrides, applied on top of every profile.</summary>
        public List<RuleSetting> overrides = new List<RuleSetting>();
        public List<Suppression> suppressions = new List<Suppression>();

        public const string ModuleId = "studio.sable.toolbox.qa";
        public static QaSettings Load() => SableToolboxSettings.instance.GetModuleSettings<QaSettings>(ModuleId);
        public void Save() => SableToolboxSettings.instance.SetModuleSettings(ModuleId, this);

        public bool IsSuppressed(Finding f) =>
            f.Location != null && suppressions.Any(s => s.ruleId == f.RuleId && s.objectId == ObjectId(f.Location));

        public void Suppress(Finding f)
        {
            if (f.Location == null || IsSuppressed(f)) return;
            suppressions.Add(new Suppression { ruleId = f.RuleId, objectId = ObjectId(f.Location) });
            Save();
        }

        /// <summary>
        /// Suppression key. Scene objects use their path under the avatar root plus component type, so a
        /// suppression still matches on the copy the SDK builds (the upload gate lints that copy) and on
        /// variants of the same avatar. Assets use their GlobalObjectId.
        /// </summary>
        public static string ObjectId(Object o)
        {
            Transform t = o is Component c ? c.transform : o is GameObject go ? go.transform : null;
            if (t == null) return GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
            var parts = new List<string>();
            for (Transform x = t; x != null && x.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() == null; x = x.parent)
                parts.Insert(0, x.name);
            return "path:" + string.Join("/", parts) + (o is Component ? "|" + o.GetType().FullName : "");
        }
    }

    public static class Linter
    {
        public const string SourceId = "studio.sable.toolbox.qa.linter";
        public const string DataPath = "Packages/studio.sable.toolbox.qa/Editor/Data/profiles.json";

        [Serializable] private class Threshold { public string key; public float value; }
        [Serializable] private class DataFile { public int version; public Threshold[] thresholds; public QaProfile[] profiles; }

        private static List<IAvatarRule> rules;
        private static DataFile data;

        public static IReadOnlyList<IAvatarRule> Rules => rules ??= TypeCache.GetTypesDerivedFrom<IAvatarRule>()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (IAvatarRule)Activator.CreateInstance(t))
            .OrderBy(r => r.Id)
            .ToList();

        public static IReadOnlyList<QaProfile> Profiles => Data.profiles ?? Array.Empty<QaProfile>();

        public static QaProfile Profile(string id) =>
            Profiles.FirstOrDefault(p => p.id == id) ?? Profiles.FirstOrDefault() ?? new QaProfile { id = "all", name = "All rules" };

        public static RuleContext Context() =>
            new RuleContext((Data.thresholds ?? Array.Empty<Threshold>()).ToDictionary(t => t.key, t => t.value));

        /// <summary>Severity a rule runs at under a profile and project overrides; null means off.</summary>
        public static Severity? EffectiveSeverity(IAvatarRule rule, QaProfile profile, QaSettings settings)
        {
            RuleSetting setting = settings?.overrides.FirstOrDefault(o => o.id == rule.Id)
                                  ?? profile.rules.FirstOrDefault(r => r.id == rule.Id);
            if (setting == null) return profile.includeOtherRules ? rule.DefaultSeverity : (Severity?)null;
            if (string.IsNullOrEmpty(setting.severity)) return rule.DefaultSeverity;
            if (setting.severity.Equals("Off", StringComparison.OrdinalIgnoreCase)) return null;
            return Enum.TryParse(setting.severity, true, out Severity s) ? s : rule.DefaultSeverity;
        }

        /// <summary>
        /// Runs every rule enabled in the profile. Findings come back filed under SourceId with the
        /// profile's severity; suppressed ones are left out. A rule that throws becomes an error finding.
        /// </summary>
        public static List<Finding> Run(AvatarScan scan, QaProfile profile, QaSettings settings, IEnumerable<IAvatarRule> ruleSet = null)
        {
            RuleContext context = Context();
            var results = new List<Finding>();
            foreach (IAvatarRule rule in ruleSet ?? Rules)
            {
                Severity? severity = EffectiveSeverity(rule, profile, settings);
                if (severity == null) continue;
                try
                {
                    foreach (Finding f in rule.Check(scan, context) ?? Enumerable.Empty<Finding>())
                    {
                        // A rule may lower its own severity (e.g. "may be used by a build tool"); never raise it.
                        bool lowered = f.Severity < rule.DefaultSeverity;
                        Severity s = lowered && f.Severity < severity.Value ? f.Severity : severity.Value;
                        Finding filed = new Finding(SourceId, s, f.Location, f.Message, f.Explanation, f.Fix, f.PropertyPath, rule.Id);
                        if (settings == null || !settings.IsSuppressed(filed)) results.Add(filed);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    results.Add(new Finding(SourceId, Severity.Error, null, $"Rule {rule.Id} failed: {e.Message}",
                        "This is a bug in the rule, not in your avatar. The other rules still ran.", ruleId: rule.Id));
                }
            }
            return results;
        }

        private static DataFile Data
        {
            get
            {
                if (data != null) return data;
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
                data = json != null ? JsonUtility.FromJson<DataFile>(json.text) : new DataFile();
                if (json == null) Debug.LogWarning($"Sable Toolbox: {DataPath} not found; linter has no profiles.");
                return data;
            }
        }
    }
}
