# Sable Toolbox architecture

All code is editor-only: every assembly definition includes only the Editor platform, so nothing
from the toolbox ends up in an uploaded avatar. Targets Unity 2022.3 and VRChat Avatars SDK 3.10.x.

```
studio.sable.toolbox.core      Sable.Toolbox.Core.Editor     scanner, asset graph, findings, adapters, window, settings, undo
studio.sable.toolbox.rigging   Sable.Toolbox.Rigging.Editor  BlendShape Copier module          -> depends on core
studio.sable.toolbox.qa        Sable.Toolbox.Qa.Editor       linter: rules, profiles, reports, upload gate -> depends on core
(later) materials, animation, delivery, ndmf, android        one asmdef each                   -> depend on core
```

## Scanner model

`ScanCache.Get(descriptor)` returns an `AvatarScan`. The first call runs `AvatarScanner.Scan`; later
calls return the same object until the cache is invalidated.

**One walk.** The scanner calls `GetComponentsInChildren<Component>(true)` once, keeps that list in
`AvatarScan.Components`, sorts each component into its category, then follows asset references from
what it found. Nothing else in the toolbox walks the hierarchy: modules and adapters read the scan.

| Category | Source |
| --- | --- |
| Renderers, meshes | `Renderer` components; `SkinnedMeshRenderer.sharedMesh`, `MeshFilter.sharedMesh` |
| Materials, shaders, textures | `Renderer.sharedMaterials`, `Material.shader`, every texture property; plus materials and textures keyed in animation clips |
| Bones | every transform in any `SkinnedMeshRenderer.bones` |
| PhysBones, colliders | `VRC.Dynamics.VRCPhysBoneBase`, `VRCPhysBoneColliderBase` |
| Contacts | `VRC.Dynamics.ContactBase` (senders and receivers) |
| Constraints | `VRC.Dynamics.VRCConstraintBase` and Unity `IConstraint` |
| Animator layers, clips | descriptor `baseAnimationLayers` + `specialAnimationLayers` (non-default), controller layers, `RuntimeAnimatorController.animationClips` |
| Parameters, menus | `expressionParameters.parameters`; `expressionsMenu` and submenus (loops visited once) |
| Build-time tools | each registered adapter's `Detect` over `Components` |

All lists are de-duplicated, keep discovery order and include inactive objects. `AvatarScan.Counts()`
gives the per-category counts the Overview page shows.

**Invalidation.** The cache is cleared by:

- any `ObjectChangeEvents` stream (Undo-recorded hierarchy, component or property changes, and asset object changes)
- `EditorApplication.hierarchyChanged`, which catches objects created, reparented or given components without Undo
- any asset import, delete or move
- any undo or redo

`ScanCache.Invalidated` then fires once on the next editor update, and the window passes it on to
modules through `ToolboxContext.Changed`. A property set by a script *without* Undo (for example
assigning a material in code) raises none of these, so **Rescan** in the toolbar exists for that case.
0.1 clears all cached avatars on any change (marked `ponytail:` in `ScanCache`).

**Speed.** A cache hit is a dictionary lookup. A rescan after an edit is a full scan; its time is
shown on the Overview page ("Scanned in N ms"). The roadmap target of under a second for a full
commission avatar has **not been measured yet**; check that line on a real avatar. If it misses,
the upgrade is per-avatar, per-category invalidation driven by the `ObjectChangeEvents` stream.

**Asset graph.** `AvatarScan.Graph` records `from -> to` edges between scene components and assets:
descriptor to controllers, parameters and menus; renderer to mesh and materials; material to shader
and textures; controller to clips; menu to submenus and icons; PhysBone to colliders. A reference
held by a clip keyframe (`AnimationUtility.GetObjectReferenceCurveBindings`) is an `Animated` edge.
`IsReachableOnlyThroughAnimation(node, descriptor)` answers "does this only show up through a
material swap?", which is what the material manager and the VRAM numbers need. The same answer is in
`AvatarScan.AnimatedOnlyMaterials`.

Known gaps in 0.1: controllers merged in by Modular Avatar or VRCFury, and Animator components
inside the hierarchy, are not followed yet. Adapters will add these (see below).

## Findings

`Finding` is the shared record: `SourceId`, `Severity` (Info, Warning, Error), `Location` (a Unity
object to ping) plus optional `PropertyPath`, `Message` (what is wrong), `Explanation` (why it matters
in-game) and an optional `FixAction`. A `FixAction` has a `Label`, a `Description` of what it will
change (shown in a confirm dialog) and runs through `ToolboxUndo.Group`, so one click is one undo step.

`ToolboxContext.Findings` is a single `FindingSet` per window. Each emitter calls
`Replace(sourceId, findings)` with all of its current findings, so re-running one tool never removes
another tool's results. The window's findings drawer lists them most severe first, pings the location on
click and offers the fix.

## Extension points

### Modules

```csharp
public sealed class MyModule : IToolboxModule
{
    public string Id => "com.example.my-module";
    public string DisplayName => "My Module";
    public int Order => 500;                       // core 0-99, Sable packages 100+, others anything
    public VisualElement CreateView(ToolboxContext ctx) { ... }
}
```

The window finds every non-abstract `IToolboxModule` with a public parameterless constructor through
`TypeCache`, so a package adds a module by including the class. Core never references it. Modules
read `ctx.Scan`, subscribe to `ctx.Changed`, and write `ctx.Findings`. Users can hide modules in
Project Settings > Sable Toolbox. IMGUI tools can be hosted with an `IMGUIContainer`, as the
BlendShape Copier does.

### Rules (linter, `studio.sable.toolbox.qa`)

The rule contract lives in the `qa` package, not core, because only the linter runs rules:

```csharp
public interface IAvatarRule
{
    string Id { get; }                 // "qa.missing-shader"; used by profiles, overrides, suppressions
    string Title { get; }
    Severity DefaultSeverity { get; }
    IEnumerable<Finding> Check(AvatarScan scan, RuleContext context);  // findings may carry a FixAction
}
```

`AvatarRule` is an optional base class that fills in the source id and severity. Third-party packages
reference `Sable.Toolbox.Qa.Editor`, implement the interface on a class with a public parameterless
constructor, and `Linter.Rules` finds it through `TypeCache`, the same way the window finds modules.
A rule only reads the scan; anything it changes happens inside a `FixAction`.

The linter added two things to core, both additive: `Finding.RuleId` (with `Finding.With`), so findings filed
under the linter's single source id keep the rule that produced them; and `PoiyomiBridge`, which locks
and inspects Poiyomi materials by reflection.

- **Profiles** are data in `qa/Editor/Data/profiles.json` (versioned): Personal, Commission delivery,
  Public release, Android. Each lists rule ids with a severity (`""` = rule default, `"Off"`). Rules
  a profile doesn't list run at their default severity when `includeOtherRules` is true, which is how
  third-party rules join every profile. Thresholds (collision checks per chain, minimum bounds)
  are in the same file.
- **Per-project settings** (`QaSettings`: active profile, run on save, build gate, severity
  overrides, suppressions) are stored in the toolbox settings file through
  `SableToolboxSettings.Get/SetModuleSettings`, a JSON entry per module id. Suppressions are rule id +
  `GlobalObjectId`.
- **Running.** You can run on demand from the Linter page or on save. The upload gate is an
  `IVRCSDKPreprocessAvatarCallback` at order −12000, so it runs before NDMF (−11000) and sees the avatar as
  authored; it blocks the build on errors, and outside batch mode it offers "Build anyway".
- **Reports.** `LintReport.Markdown` and `LintReport.Html` produce a QA record for a commission.
- A rule that throws becomes one error finding; the other rules still run.

### Adapters

`IBuildToolAdapter { Id, DisplayName, IsInstalled, Detect(components) }`. The four built-in adapters
are `TypeNameAdapter` instances loaded from `Editor/Data/adapters.json` (versioned). Each matches
components whose type, or any base type, has a listed full name, by reflection, with no assembly
reference:

| Adapter | Type name | Checked against |
| --- | --- | --- |
| Modular Avatar | `nadena.dev.modular_avatar.core.AvatarTagComponent` | MA 1.18.3 source |
| VRCFury | `VF.Component.VRCFuryComponent` | VRCFury 1.1421.0 source |
| Avatar Optimizer | `Anatawa12.AvatarOptimizer.AvatarTagComponent` | **unverified** (not installed locally) |
| d4rk Avatar Optimizer | `d4rkAvatarOptimizer` | **unverified** (not installed locally) |

If an upstream rename happens, the fix is a JSON data update, not a code change. Other packages can
`AdapterRegistry.Register(adapter)` from an `[InitializeOnLoad]` class.

What an adapter will add or merge at build time is a later step. It arrives as optional
interfaces that an adapter may also implement, for example
`IBuildEstimateAdapter { void Contribute(AvatarScan scan, BuildEstimate estimate); }` (extra
controllers and clips to follow, meshes an optimiser will merge, components that will be removed).
`IBuildToolAdapter` itself does not change. VRCFury stays read-only, as in the spec.

## Settings and undo

`SableToolboxSettings` is a `ScriptableSingleton` stored at `ProjectSettings/SableToolboxSettings.asset`.
It's one per project and never inside `Assets/`, so it can't leak into an avatar package. It holds the
Generated folder (default `Assets/Generated`), disabled modules and the last open module. You edit it under
Project Settings > Sable Toolbox, through `SerializedObject`, so edits are undoable.

- `ToolboxUndo.Group(name, action)` collapses everything inside into one Undo step. `FixAction.Apply`
  always uses it, and modules should too.
- `GeneratedAssets.UniquePath(subfolder, file)` returns a fresh path under the Generated folder and
  creates the folders. Tools write generated copies there and never over source assets. Unity Undo
  reverts references to a generated asset but not the file itself; the file stays under Generated/.

Not in 0.1 (spec items deferred, designed for): per-avatar action history, report export, opening
each module in its own window (the BlendShape Copier already has one).

## How the later modules plug in

| Module (release) | Package | Reads | Plugs in through |
| --- | --- | --- | --- |
| Material manager (0.2) | materials | `Materials`, `AnimatedOnlyMaterials`, `Shaders`, `Textures`, `Graph.ReferencedBy` for "renderers and slots using it" | `IToolboxModule`; edits via `ToolboxUndo`; converted copies via `GeneratedAssets`; cost findings into `FindingSet` |
| Linter (shipped in 0.0.2) | qa | whole `AvatarScan` | `IAvatarRule` (above); `LinterModule` runs a profile and calls `Findings.Replace(Linter.SourceId, ...)`; fixes are `FixAction`s |
| Performance budget (0.2) | qa | counts from `AvatarScan`; build-time estimates from adapters | `IToolboxModule`; thresholds in a versioned `Data/performance-limits.json` in qa (PC and mobile tables from the spec); over-limit items emitted as findings |
| Texture tools, BlendShape tools, bone utils, component copier (0.3) | materials / rigging | `Textures`, `Renderers`, `Bones`, `PhysBones`, ... | `IToolboxModule` each |
| Animator and parameter tools (0.3) | animation | `AnimatorLayers`, `Clips`, `Parameters`, `Menus` | `IToolboxModule`; Write Defaults across merged controllers needs the adapter estimate interface |
| NDMF passes (1.0) | ndmf | `AvatarScan` of the build clone | an NDMF plugin assembly referencing core and NDMF; core stays NDMF-free |
| Android tools (1.x) | android | everything; mobile limits from the same performance JSON | wizard is an `IToolboxModule`; variants and baked assets under `Generated/Android/` via `GeneratedAssets`; "Android" linter profile is a qa rule profile |
