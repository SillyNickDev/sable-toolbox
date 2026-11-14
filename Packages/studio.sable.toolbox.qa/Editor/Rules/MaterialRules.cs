using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using Sable.Toolbox.Shaders;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Qa.Rules
{
    internal sealed class MissingShaderRule : AvatarRule
    {
        public override string Id => "qa.missing-shader";
        public override string Title => "Missing or pink shaders";
        public override Severity DefaultSeverity => Severity.Error;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (Material m in scan.Materials)
            {
                Shader s = m.shader;
                if (s != null && s.name != "Hidden/InternalErrorShader" && s.isSupported) continue;
                string original = m.GetTag("OriginalShader", false, "");
                yield return Found(m,
                    s == null || s.name == "Hidden/InternalErrorShader"
                        ? $"Material '{m.name}' has no working shader" + (original != "" ? $" (was {original})" : "")
                        : $"Material '{m.name}' uses shader '{s.name}', which is not supported here",
                    "The material renders pink in-game for everyone. Install the shader it was made with, " +
                    "or assign a replacement. " + (scan.AnimatedOnlyMaterials.Contains(m) ? "It is used by an animation (material swap)." : ""));
            }

            foreach (Renderer r in scan.Renderers)
            {
                int empty = r.sharedMaterials.Count(x => x == null);
                if (empty > 0)
                    yield return Found(r, $"'{r.name}' has {empty} empty material slot(s)",
                        "Empty slots render pink in-game and still count towards material slots.", severity: Severity.Warning);
            }
        }
    }

    internal sealed class PoiyomiUnlockedRule : AvatarRule
    {
        public override string Id => "qa.poiyomi-unlocked";
        public override string Title => "Unlocked Poiyomi materials";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            if (!PoiyomiBridge.IsAvailable) yield break;
            foreach (Material m in scan.Materials)
            {
                if (!PoiyomiBridge.IsOptimizerMaterial(m) || PoiyomiBridge.IsLocked(m)) continue;
                Material target = m;
                yield return Found(m, $"Poiyomi material '{m.name}' is not locked",
                    "Unlocked Poiyomi materials are slow to render and VRChat may refuse them at upload. " +
                    "Locking compiles a shader with only the features this material uses.",
                    Fix("Lock", $"Locks '{m.name}' with Poiyomi's optimizer. Properties not marked as animated become fixed.",
                        () => PoiyomiBridge.SetLocked(new[] { target }, true)));
            }
        }
    }

    internal sealed class TextureMipmapRule : AvatarRule
    {
        public override string Id => "qa.texture-mipmaps";
        public override string Title => "Textures without mipmaps or streaming";
        public override Severity DefaultSeverity => Severity.Warning;

        public override IEnumerable<Finding> Check(AvatarScan scan, RuleContext context)
        {
            foreach (Texture t in scan.Textures)
            {
                string path = AssetDatabase.GetAssetPath(t);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (importer.textureType == TextureImporterType.Sprite || importer.textureType == TextureImporterType.GUI) continue;
                bool noMips = !importer.mipmapEnabled;
                bool noStreaming = importer.mipmapEnabled && !importer.streamingMipmaps;
                if (!noMips && !noStreaming) continue;

                yield return Found(t,
                    noMips ? $"Texture '{t.name}' has no mipmaps" : $"Texture '{t.name}' has mipmap streaming off",
                    noMips
                        ? "Without mipmaps the texture shimmers at a distance and always uses full-resolution memory bandwidth."
                        : "VRChat requires mipmap streaming on avatar textures to manage texture memory; the SDK flags it at upload.",
                    Fix("Enable", $"Turns on mipmaps and mipmap streaming in the import settings of '{path}' and reimports it.",
                        () =>
                        {
                            UnityEditor.Undo.RecordObject(importer, "Enable mipmaps");
                            importer.mipmapEnabled = true;
                            importer.streamingMipmaps = true;
                            importer.SaveAndReimport();
                        }));
            }
        }
    }
}
