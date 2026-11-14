# Sable Toolbox

An open-source Unity editor toolkit for VRChat avatar work, distributed as VPM packages. One
dockable window, one shared avatar scan, and modules that plug into it. See [docs/SPEC.md](docs/SPEC.md)
for the full plan and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how it fits together.

**0.1 Foundation** ships the core (avatar scanner, asset graph, findings, build-tool detection,
window shell, settings) and the BlendShape Copier module.

Requires Unity 2022.3 and VRChat Avatars SDK 3.10 or newer. Editor-only: nothing is added to
uploaded avatars.

## Install via VPM

1. In VRChat Creator Companion or ALCOM, open **Settings > Packages > Add Repository**.
2. Paste the listing URL: `https://<owner>.github.io/<repo>/index.json`
   (filled in once the GitHub repo is live).
3. Open your avatar project's **Manage Project** and add **Sable Toolbox Core**, plus
   **Sable Toolbox Rigging** for the BlendShape Copier.

With vrc-get: `vrc-get repo add <listing URL>`, then `vrc-get install studio.sable.toolbox.rigging`.

If you used the standalone BlendShape Copier before, delete `Assets/Editor/BlendShapeCopierWindow.cs`
so the context menu entries are not registered twice.

## Use

- **Tools > Sable Toolbox** opens the window. Pick an avatar (anything with a VRC Avatar
  Descriptor). The window defaults to the selected or first avatar in the scene. **Overview** shows scan
  counts per category and any Modular Avatar, VRCFury or optimiser components it found.
- **BlendShape Copier** is in the sidebar, and still available as its own window under
  **Window > Sable Toolbox > BlendShape Copier** and from the SkinnedMeshRenderer context menu.
- **Project Settings > Sable Toolbox** sets the Generated folder and hides modules.

## Add a module

Any editor assembly can add a page to the window. Reference `Sable.Toolbox.Core.Editor` from your
assembly definition (Editor platform only) and add a class:

```csharp
using Sable.Toolbox.Findings;
using Sable.Toolbox.UI;
using UnityEngine.UIElements;

public sealed class HelloModule : IToolboxModule
{
    public string Id => "com.example.hello";
    public string DisplayName => "Hello";
    public int Order => 500;

    public VisualElement CreateView(ToolboxContext ctx)
    {
        var label = new Label();
        void Refresh()
        {
            label.text = ctx.Scan == null ? "No avatar" : $"{ctx.Scan.PhysBones.Count} PhysBones";
            if (ctx.Scan != null && ctx.Scan.PhysBones.Count > 8)
                ctx.Findings.Replace(Id, new[] { new Finding(Id, Severity.Warning, ctx.Avatar,
                    "More than 8 PhysBones", "Over the Poor limit on Android, all PhysBones are removed.") });
            else
                ctx.Findings.Clear(Id);
        }
        ctx.Changed += Refresh;
        Refresh();
        return label;
    }
}
```

The window discovers it automatically. Rules for reading the scan:

- Read from `ctx.Scan`; never walk the avatar hierarchy yourself.
- Refresh on `ctx.Changed`.
- Wrap edits in `ToolboxUndo.Group`.
- Write generated assets to `GeneratedAssets.UniquePath(...)`.

## Development

```
Packages/studio.sable.toolbox.core      core package (Editor/, Tests/Editor/)
Packages/studio.sable.toolbox.rigging   rigging package
listing/source.json                     VPM listing metadata
.github/workflows/release.yml           tag vX.Y.Z -> zips, GitHub release, index.json on gh-pages
```

To work on it, add the packages to an avatar project's `Packages/manifest.json` as local packages
and enable their tests:

```json
"studio.sable.toolbox.core": "file:C:/path/to/repo/Packages/studio.sable.toolbox.core",
"studio.sable.toolbox.rigging": "file:C:/path/to/repo/Packages/studio.sable.toolbox.rigging"
...
"testables": ["studio.sable.toolbox.core"]
```

Then run **Window > General > Test Runner > EditMode**, or headless:

```
Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode -testResults results.xml
```

**Releasing:** set the same version in every `package.json`, then push a tag such as `v0.1.0`. Enable GitHub
Pages for the `gh-pages` branch once. The workflow refuses to release while `LICENSE` is a
placeholder.

## Licence

GNU General Public License v3.0 only (GPL-3.0-only). See [LICENSE](LICENSE). You may use the toolbox
in any project, including commercial commissions; avatars you build with it are not affected. If you
distribute a modified version of the toolbox itself, it must stay GPL-3.0 with its source available.
