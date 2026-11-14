using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Sable.Toolbox.Qa.Rules
{
    internal sealed class VisemeRule : AvatarRule
    {
        public override string Id => "qa.missing-visemes";
        public override string Title => "Missing visemes";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            VRCAvatarDescriptor d = scan.Descriptor;
            const string why = "Without visemes the mouth doesn't move when you talk.";
            switch (d.lipSync)
            {
                case VRC_AvatarDescriptor.LipSyncStyle.Default:
                    yield return Found(d, "Lip sync is not configured", why + " Set Lip Sync to Viseme Blend Shape and pick the face mesh.");
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape:
                    if (d.VisemeSkinnedMesh == null || d.VisemeSkinnedMesh.sharedMesh == null)
                    {
                        yield return Found(d, "Viseme lip sync has no face mesh", why);
                        break;
                    }
                    Mesh face = d.VisemeSkinnedMesh.sharedMesh;
                    int count = (int)VRC_AvatarDescriptor.Viseme.Count;
                    string[] shapes = d.VisemeBlendShapes ?? new string[0];
                    var missing = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        string shape = i < shapes.Length ? shapes[i] : null;
                        if (string.IsNullOrEmpty(shape) || face.GetBlendShapeIndex(shape) < 0)
                            missing.Add(((VRC_AvatarDescriptor.Viseme)i).ToString());
                    }
                    if (missing.Count > 0)
                        yield return Found(d, $"{missing.Count} viseme(s) unset or not on '{face.name}': {string.Join(", ", missing)}",
                            why + " Each of the 15 visemes needs a blendshape that exists on the face mesh.");
                    break;
            }
        }
    }

    internal sealed class EyeLookRule : AvatarRule
    {
        public override string Id => "qa.eye-look";
        public override string Title => "Eye look not configured";
        public override Severity DefaultSeverity => Severity.Info;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            if (!scan.Descriptor.enableEyeLook)
                yield return Found(scan.Descriptor, "Eye look is off",
                    "The eyes won't follow people or blink. Enable Eye Look on the descriptor and assign the eye bones and blink shape.");
        }
    }

    internal sealed class LightAudioRule : AvatarRule
    {
        public override string Id => "qa.light-audio-enabled";
        public override string Title => "Lights or audio sources left enabled";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (Component c in scan.Components)
            {
                if (!(c is Light) && !(c is AudioSource)) continue;
                var b = (Behaviour)c;
                if (!b.enabled || !b.gameObject.activeInHierarchy) continue;
                bool light = c is Light;
                yield return Found(c, $"{(light ? "Light" : "Audio source")} on '{c.name}' is enabled by default",
                    light
                        ? "Realtime lights on avatars are expensive for everyone nearby and drop the rank to Poor; Android removes them. Keep them off and toggle them on."
                        : "An audio source that is on by default plays the moment you load in, for everyone. Keep it off and toggle it on.",
                    Fix("Disable", $"Turns the {(light ? "Light" : "Audio Source")} component on '{c.name}' off.",
                        () => { UnityEditor.Undo.RecordObject(b, "Disable component"); b.enabled = false; }));
            }
        }
    }

    internal sealed class MissingScriptRule : AvatarRule
    {
        public override string Id => "qa.missing-scripts";
        public override string Title => "Missing scripts";
        public override Severity DefaultSeverity => Severity.Error;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (GameObject go in scan.MissingScriptObjects)
            {
                GameObject target = go;
                bool prefabInstance = PrefabUtility.IsPartOfPrefabInstance(go);
                yield return Found(go, $"'{go.name}' has {GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go)} missing script(s)",
                    "The SDK blocks upload while missing scripts remain. They usually come from a package that was removed or not installed yet.",
                    prefabInstance ? null : Fix("Remove", $"Removes the missing script components from '{go.name}'.",
                        () =>
                        {
                            UnityEditor.Undo.RegisterCompleteObjectUndo(target, "Remove missing scripts");
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
                        }));
            }
        }
    }
}
