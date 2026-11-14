# Sable Toolbox — Unity Avatar Toolkit Spec

Nov 13, 2026 · @Nick

Sable Toolbox is one VPM-distributed Unity editor suite for VRChat avatar work: a shared avatar scanner feeding a dozen modules in a single dockable window, consolidating scattered single-purpose tools and adding new ones, led by a shader and material manager.

## Scope

The toolbox covers everything done to an avatar inside Unity between FBX import and upload. Garment authoring stays in Blender, where existing addons already cover it.

**Goals**

- One window, one install, one settings file for the whole Unity side of the avatar pipeline.
- Every module reads the same avatar scan, so a problem found by one tool is fixable by another in one click.
- Faster commission turnaround: fewer manual checks, repeatable presets, a clean delivery package.
- Free and open source, published through the Sable Studios VPM repo.

**Non-goals**

- Not a replacement for NDMF, Modular Avatar, VRCFury or Avatar Optimizer. The toolbox detects them and works with their components.
- Not a shader. It manages Poiyomi, lilToon and Standard-family materials; it does not render anything.
- No Blender-side work: no weight transfer, retopology or shrinkwrap blendshape generation.
- No world tooling.

**Design principles**

1. Non-destructive by default. Tools write components processed at build time, or work on generated copies in a `Generated/` folder. Source assets are never overwritten without a confirm.
2. Everything undoable. Every action registers with Unity Undo and logs to a per-avatar history.
3. Scan once, use everywhere. Modules never re-walk the hierarchy themselves.
4. Each module works alone. Any module can be disabled or left uninstalled without breaking the others.
5. Explain, then fix. Every finding says what is wrong, why it matters in-game and what the fix will change.

## Architecture

The toolbox ships as one core package plus optional module packages, all editor-only, so nothing is added to the uploaded avatar except what an NDMF pass deliberately generates.

| Package | Contents | Depends on |
| --- | --- | --- |
| `studio.sable.toolbox.core` | Avatar scanner, asset graph, shared UI kit, settings, undo/history, report export | VRChat Avatars SDK |
| `studio.sable.toolbox.materials` | Shader and material manager, texture tools | core |
| `studio.sable.toolbox.qa` | Linter, performance budget, prefab diff | core |
| `studio.sable.toolbox.rigging` | PhysBone presets, blendshape tools, bone utilities | core |
| `studio.sable.toolbox.animation` | Animator and parameter tools | core |
| `studio.sable.toolbox.delivery` | Commission packaging, multi-platform upload helper | core |
| `studio.sable.toolbox.ndmf` | Build-time passes (optional) | core, NDMF |

**Core services**

- **Avatar scanner.** Walks the selected avatar descriptor once and builds an in-memory model: renderers, meshes, materials, shaders, textures, bones, PhysBones, colliders, contacts, constraints, animator layers, parameters and menus. Cached and invalidated on hierarchy or asset change.
- **Asset graph.** Records which asset references which, including materials and textures reachable only through animation clips (material swaps), which static checks usually miss.
- **Finding model.** A shared record of severity, location, explanation and an optional fix action. The linter, budget and material manager all emit findings into one list.
- **Adapter layer.** Small adapters detect Modular Avatar, VRCFury, Avatar Optimizer and d4rk Avatar Optimizer components so the scanner can account for what they will add or merge at build time.
- **UI.** One dockable UI Toolkit window with a module sidebar, an avatar picker and a shared findings drawer. Each module is also openable as its own window.

**Build-time versus edit-time**

NDMF runs plugins in ordered build phases and lets tools that share its pipeline process the same avatar without manual conflict resolution ([VRChat wiki: NDMF](https://wiki.vrchat.com/wiki/Community:NDMF)). Anything that must change the uploaded avatar, such as material deduplication or texture swaps per platform, is therefore an NDMF pass in the optional `ndmf` package. Everything else is an edit-time tool that needs no framework.

VRCFury runs its own build pipeline, so the toolbox treats VRCFury components as read-only inputs: it reads them for analysis and never generates them in v1.

**Targets:** the Unity version current VRChat SDKs require, C# with assembly definitions per package, no runtime assemblies, no third-party binary dependencies.

## Module catalogue

Twelve modules: seven consolidate work currently spread over separate tools, five are new. Eight advanced tools are specified in their own section further down.

| Module | What it does | Overlaps today with | Type |
| --- | --- | --- | --- |
| Shader and material manager | Inventory, bulk edit, convert, lock, dedupe and preset every material on the avatar | Poiyomi and lilToon inspectors, Thry's tools, lilAvatarUtils | New |
| Avatar linter | Rule-based checks with one-click fixes | SDK build panel warnings, manual checklists | New |
| Prefab diff | Compare two avatars or prefab variants component by component | Nothing avatar-aware | New |
| PhysBone preset library | Save, tag, apply and mirror PhysBone and collider setups | Copy/paste component, Pumkin's Avatar Tools | New |
| Delivery packager | Build a clean `.unitypackage` with dependency checks and a readme | Manual export | New |
| Performance budget | Live rank per platform with per-asset cost breakdown | SDK panel, Thry's Avatar Performance Tools | Consolidation |
| Texture tools | Import-setting audit, bulk resize and compression, VRAM view, simple atlas | Thry's VRAM calculator, manual import settings | Consolidation |
| BlendShape tools | Copier (existing seed tool), delta transfer, renamer, viseme and face-tracking shape checker | Standalone BlendShape Copier, assorted scripts | Consolidation |
| Bone and hierarchy utilities | Bone visualiser, unused-bone finder, renamer, armature compare | Bone renamers, Pumkin's Avatar Tools | Consolidation |
| Animator and parameter tools | Parameter memory budget, unused parameter and layer finder, Write Defaults audit, clip path repair | Avatars 3.0 Manager, manual checks | Consolidation |
| Component copier | Copy PhysBones, contacts, constraints and descriptors between avatars with bone remapping | Pumkin's Avatar Tools | Consolidation |
| Platform helper | Multi-platform build status; expanded into the Android tools section | VRCQuestTools | Consolidation |

Names in the overlap column are the tools a creator typically reaches for now; the toolbox does not bundle or fork any of them.

## Shader and material manager

The material manager treats every material on the avatar as a row in one sortable table, so changes that now mean opening forty inspectors become one bulk action.

**Inventory view**

- One row per material: shader, shader version, locked state, render queue, renderers and slots using it, texture count and estimated VRAM.
- Includes materials reachable only through animation clips, flagged as "animated swap". VRChat's own texture memory stat has historically missed these ([bug report](https://feedback.vrchat.com/bug-reports/p/1281-texture-memory-stat-doesnt-include-textures-from-matswaps-and-animations)).
- Group by shader, by mesh or by texture set. Filter by keyword, feature or missing shader.
- Click a row to ping the material; double-click opens the native shader inspector.

**Bulk property editing**

- Multi-select materials and edit shared properties in a side panel. Properties are matched by shader property name, so mixed Poiyomi versions still line up.
- Semantic aliases map equivalent properties across shader families (main colour, emission strength, lighting mode, outline width), so one slider drives Poiyomi and lilToon materials together.
- Copy and paste a subset of properties, for example lighting only, from one material to many.
- Find and replace a texture across all materials.

**Presets**

- Save any property subset as a named preset asset: "Sable fur lighting", "Matte cloth", "Dissolve toggle base".
- Presets store values per shader family, so one preset can carry both a Poiyomi and a lilToon version.
- Presets live in a project-independent library folder and can be exported as a single file to share.

**Shader conversion and versions**

- Convert between shader families using the alias map; unmapped properties are listed before anything is applied.
- Upgrade materials between installed Poiyomi versions in bulk, with a before/after property diff.
- Missing-shader repair: detect pink materials, read the saved shader name from the asset and offer the closest installed match.

**Lock management (Poiyomi)**

- Table column showing locked, unlocked or stale (locked against an older property state).
- Bulk lock and unlock, plus "lock all before upload" as a linter fix.
- Animated-property audit: cross-checks every material property touched by an animation clip against the material's "animated" tags, and flags properties that will stop animating once locked. This directly protects dissolve-style toggles.

**Deduplication**

- Finds materials with identical shader and property values and offers to merge references.
- Finds near-duplicates differing only by one or two properties and shows the diff.
- Edit-time merge rewrites references on a generated copy; build-time merge is an NDMF pass that leaves the project untouched.

**Cost view**

- Per-material slot count, enabled feature modules, keyword count and texture memory.
- Flags expensive combinations: grab passes, many enabled modules on a small accessory, transparent queue on large surfaces.
- Totals feed the performance budget module.

**Out of scope for v1:** shader graph editing, custom shader authoring, and automatic full-avatar atlasing (the texture module offers a simple manual atlas only).

## Other module specs

Each module below is specified to the level needed to scope a first version; all read from the shared avatar scan and emit shared findings.

### Avatar linter

- Rules are small classes implementing one interface (`id`, severity, `Check(scan)`, optional `Fix`). Third parties can add rules from their own packages.
- Rule sets as profiles: "Personal", "Commission delivery", "Public release", "Android". Per-project overrides and per-object suppress comments.
- Starter rules: missing or pink shaders, unlocked Poiyomi materials, mixed Write Defaults, unused parameters, parameter memory over budget, PhysBones without limits or with self-collision on long chains, zero-weight bones, meshes with bounds too small or not unified, blendshape normals set to recalculate, textures without mipmaps or streaming, missing visemes, eye-look not configured, lights or audio sources left enabled, missing scripts, empty animator states.
- Runs on demand, on save, or as a build gate that blocks upload on errors.
- Exports a Markdown or HTML report, usable as a QA record for a commission.

### Performance budget

- Live rank per platform with the single metric currently setting the rank highlighted. On PC the official triangle limits are 32,000 for Excellent and 70,000 for Good ([VRChat Creator Docs](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)).
- Thresholds load from a versioned JSON file in the package, so a VRChat limits change is a data update, not a code release.
- Size limits tracked alongside rank: PC avatars are capped at 200 MB download and 500 MB estimated memory, Quest at 10 MB and 40 MB ([VRChat feedback: size limits](https://feedback.vrchat.com/avatar-30/p/add-memory-usage-limit-to-avatars)).
- "What would get me to Good" view: ranks candidate changes by how far each moves the limiting metric.
- Accounts for what build-time tools will do, where an adapter exists (for example meshes an optimiser will merge), and labels those numbers as estimates.
- Because rank is a static analysis that ignores shaders, texture resolution and animators ([VRChat Creator Docs](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)), the module shows a separate "real cost" panel: VRAM, material cost flags and animator layer count.

### Prefab diff

- Pick two avatars, prefab variants or a prefab and its scene instance. Output is a tree of added, removed and changed objects, components and properties.
- Avatar-aware matching: bones matched by humanoid mapping then by name, so renamed armatures still line up.
- Filters by component type (PhysBones only, materials only, transforms only).
- Apply selected differences from A to B. Main uses: porting a setup to an updated base version, and checking what a client changed.

### PhysBone preset library

- Save a PhysBone component, or a whole chain setup with its colliders, as a preset tagged by use: tail, ears, hair, cloth, chest.
- Apply by drag onto a bone. Values scale with avatar size and chain length; left/right mirroring is automatic.
- Side-by-side A/B: two presets on mirrored chains in play mode for direct comparison.
- Ships with a small starter set, including docked-tail and long-tail canine presets.
- Collider helper: generates capsule colliders from humanoid bones with adjustable radius, and reports collider count against the budget.

### BlendShape tools

- **Copier**: the existing BlendShape Copier window, moved in as the seed tool.
- **Delta transfer**: transfer blendshape deltas between meshes by nearest surface, for body-shape shapes on clothing.
- **Sync setup**: one click to generate blendshape sync components from name matches between body and clothing.
- **Checker**: verifies viseme and face-tracking shape naming against standard sets and lists what is missing.
- **Cleaner**: finds empty and duplicate shapes; bulk rename with find/replace.

### Texture tools

- Table of every texture: resolution, format, mipmaps, streaming, sRGB flag, VRAM, and which materials use it.
- Bulk import-setting changes with presets per platform; flags normal maps imported as colour and colour maps imported as linear.
- Per-platform override manager: set maximum size and compression for Android without touching PC settings.
- Simple manual atlas: pick materials sharing a shader, pack to one sheet, generate remapped mesh copies.

### Animator and parameter tools

- Synced parameter memory meter with per-parameter cost and source (which prefab or component adds it).
- Unused finder: parameters never read, layers with no effect, clips animating missing paths.
- Path repair: after a hierarchy rename or move, rewrites clip paths with a preview.
- Write Defaults audit across all controllers, including those merged in by other tools.
- Dissolve toggle generator: produces the matched "dissolve in" and "dissolve out" clip pair for a material plus optional body blendshape, as plain clips rather than a transition abstraction.

### Bone, hierarchy and component utilities

- Bone visualiser with weight heatmap per bone; unused and zero-weight bone finder.
- Armature compare between base and clothing to catch scale and rotation mismatches before merging.
- Component copier with bone remapping for PhysBones, contacts, constraints and the avatar descriptor.

### Delivery packager

- Builds a `.unitypackage` from a chosen prefab with exactly its dependencies, excluding shader and SDK folders.
- Checks before export: linter profile passes, no absolute paths, no references outside the package, required VPM packages listed.
- Generates a readme with dependencies and versions, install steps and a changelog, from a template.
- Named export profiles per client or product.

### Platform helper

- Maps each PC material to an Android fallback material and applies the mapping on a generated variant.
- Checklist of components unsupported on Android found on the avatar, with remove-on-variant fixes.
- One-screen status of each platform build: rank, size, last upload.

## Advanced tools

Eight further tools go beyond inspection and bulk editing: they simulate, generate or automate, and each builds on the core modules above.

| Tool | What it does | Builds on |
| --- | --- | --- |
| Clipping detector | Finds body-through-clothing intersections across poses, blendshapes and toggle states | Scanner, BlendShape tools |
| Hidden mesh remover | Deletes body polygons that are never visible under clothing, per toggle state | Clipping detector, NDMF pass |
| Toggle matrix tester | Steps through every menu state and gesture combination and records problems | Animator tools, linter |
| Lighting test bench | Shows the avatar under a grid of standard world lighting conditions at once | Material manager |
| Parameter compressor | Packs synced parameters into fewer bits at build time | Animator tools, NDMF pass |
| Channel packer | Packs separate mask textures into one RGBA texture and rewires materials | Texture tools, material manager |
| Recipes and batch runner | Chains toolbox actions into saved macros; runs them headless across avatars | Everything |
| Snapshots and live debugger | Per-avatar restore points, plus a play-mode monitor for parameters, contacts and OSC | Prefab diff, animator tools |

### Clipping detector

- Samples a set of poses (T-pose, sit, crouch, arms raised, extreme twist) crossed with body-shape blendshapes and outfit toggles.
- Detects intersection by rendering body and clothing to an ID buffer from several cameras and counting body pixels that show through clothing.
- Reports each hit with pose, shape values, a thumbnail and the affected triangles highlighted in the scene view.
- Suggested fixes per hit: add a hide blendshape, extend a body mask, or flag for a weight fix in Blender.
- This is the Unity-side half of the fit QA sweep; the pose set and thresholds are shared data a Blender plugin could reuse.

### Hidden mesh remover

- For each outfit state, finds body triangles fully covered in every sampled pose and marks them for removal.
- Only removes triangles hidden in all states where that body mesh is visible, unless the outfit is non-toggleable.
- Runs as an NDMF pass on the built avatar; the source mesh is untouched. Safety margin is adjustable to avoid holes at garment edges.
- Reports triangles saved, feeding the performance budget.

### Toggle matrix tester

- Enumerates menu toggles, radial values at 0, 0.5 and 1, and gesture pairs, then drives the animator through them in play mode.
- Flags: states that leave a material property or blendshape stuck after toggling back, objects enabled by two conflicting toggles, clips with no visible effect, and Write Defaults bleed.
- Pairwise coverage by default so the run stays short; full combinations optional for small menus.
- Outputs a contact sheet of every state for client sign-off.

### Lighting test bench

- One window, a grid of viewports: bright realtime sun, baked-only with light probes, near-dark, coloured club lighting, no lights at all, and a mirror view.
- Catches overbright fur, black-in-the-dark materials, and emission or outline that only works in some conditions.
- Material edits from the material manager update every viewport live.
- Custom lighting setups saved as presets; one-click screenshot grid for product listings.

### Parameter compressor

- Groups mutually exclusive toggles into a single synced integer and generates the decoding layers.
- Marks parameters that rarely change as candidates for local-only with a sync-on-change driver.
- Shows bits saved before applying; runs at build time, so authored controllers stay readable.
- Skips any parameter read by OSC or external apps unless explicitly allowed, since those depend on parameter names.

### Channel packer

- Detects single-channel masks used by the same material (metallic, smoothness, emission mask, AO) and packs them into one texture.
- Rewrites the material's texture slots and channel selectors for the shader family in use.
- Works on generated copies; originals remain for re-export from Substance Painter.
- Reports VRAM saved per material.

### Recipes and batch runner

- A recipe is an ordered list of toolbox actions with parameters, saved as an asset: for example "Commission prep" runs lint, lock materials, apply texture presets, snapshot, then package.
- Batch runner applies a recipe to several avatars or prefab variants in one go, with a per-avatar result log.
- Command-line entry point for Unity batch mode, so lint and packaging can run in CI on the GitHub repo.
- Every toolbox action exposes a public C# API, which is what recipes call; other tools can call it too.

### Snapshots and live debugger

- **Snapshots**: named restore points of an avatar prefab and its generated assets, stored outside the Assets folder. Compare any two with the prefab diff, restore whole or in part.
- **Live debugger**: play-mode panel listing every parameter with current value, sync type and last writer (menu, gesture, contact, OSC, driver).
- Contact and PhysBone visualiser showing which receivers are firing in real time.
- OSC inspector: logs incoming and outgoing messages and can replay a recorded session, useful for testing face-tracking or heart-rate driven parameters without the hardware running.

## Android tools

The Android toolset turns a finished PC avatar into an Android variant that lands on a chosen rank, without touching the PC prefab. It expands the platform helper above into eight tools built around one conversion helper.

**What the tools aim at**

Mobile limits are far tighter than PC, and the default visibility cut-off on mobile is Medium, so Medium is the default target. Figures below are from the [VRChat Creator Docs](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) and load from the same versioned JSON file as the PC thresholds.

| Mobile metric | Excellent | Good | Medium | Poor |
| --- | --- | --- | --- | --- |
| Triangles | 7,500 | 10,000 | 15,000 | 20,000 |
| Texture memory | 10 MB | 18 MB | 25 MB | 40 MB |
| Skinned meshes | 1 | 1 | 2 | 2 |
| Material slots | 1 | 1 | 2 | 4 |
| Bones | 75 | 90 | 150 | 150 |
| PhysBone components | 0 | 4 | 6 | 8 |

Two hard rules shape the design. Going over the Poor value for PhysBones, contacts or constraints strips all of them from the avatar, and only the VRChat Mobile shaders are allowed ([Android optimisation guide](https://creators.vrchat.com/platforms/android/quest-content-optimization)).

| Tool | What it does |
| --- | --- |
| Conversion helper | Guided wizard from PC prefab to Android variant, with live rank after each step |
| Material baker | Bakes the PC shader look into textures for a Mobile shader |
| Mesh and bone reducer | Merges meshes, decimates to a triangle target, removes bones to a bone target |
| PhysBone reducer | Chooses which PhysBones, colliders and contacts survive under the hard limits |
| Texture budgeter | Splits a texture memory budget across the avatar and sets sizes and formats |
| Feature pruner | Picks which toggles and outfits exist on Android while keeping parameters in sync |
| Android gate | Linter profile and upload gate for hard limits and file size |
| Cross-platform builder | Builds and uploads PC and Android to one blueprint ID, with a side-by-side preview |

### Conversion helper

- Input: a PC avatar prefab and a target rank. Output: a prefab variant plus generated assets under `Generated/Android/`; the PC prefab is never modified.
- Fixed step order: remove unsupported components, prune features, reduce PhysBones, merge and reduce meshes, bake materials, budget textures, run the Android gate.
- Each step shows the rank before and after, and which metric still blocks the target.
- Every step can be skipped, redone or hand-adjusted; choices are stored in a conversion profile asset so a re-run after a PC change takes one click.
- Removes components disabled on Android (cloth, cameras, lights, audio sources, rigidbodies, colliders, joints) and cleans up animations that reference them.
- Profiles are reusable across avatars on the same base, so one Winterpaw conversion profile seeds the next.

### Material baker

- Renders each PC material's result (base colour, tint, shading ramp, emission, decals, matcap contribution) into a flat texture in UV space.
- Picks the Mobile shader per material: Toon Lit by default, Standard Lite where metallic or smoothness matters, Matcap Lit where a matcap carries the look.
- Skips Bumped and Specular variants when there is no normal map, as the VRChat guide advises.
- Transparency audit: lists every transparent or cutout material and offers replacements (opaque bake, mesh removal, or keep and accept the cost).
- Side-by-side PC and baked preview under the lighting test bench presets.

### Mesh and bone reducer

- Merges skinned meshes down to the target count, respecting toggles the feature pruner kept.
- Decimation with a triangle target per rank, protecting blendshapes used by visemes, eye-look and face tracking, and weighting detail towards face and hands.
- Removes hidden geometry first (uses the hidden mesh remover), so decimation spends its budget on visible surface.
- Bone reduction: merges leaf bones and unused twist or helper bones into parents and re-weights, with a preview of deformation in the sampled poses.
- Decimation backend is pluggable; v1 ships one built-in simplifier and an import path for a mesh decimated in Blender.

### PhysBone reducer

- Lists every PhysBone with its transform count, collider count and collision checks, against the mobile limits.
- Ranks by visual value (tail, ears, hair, then cloth and accessories) and proposes a keep list that fits the target rank.
- Fits more under the component cap by merging sibling chains under one component, shortening chains with end-bone trimming, and ignoring selected transforms.
- Replaces dropped PhysBones with a static pose or a baked idle sway animation.
- Hard stop: refuses to finish with any count above the Poor limit, because that removes all dynamics on mobile.

### Texture budgeter

- Takes the texture memory budget for the target rank and allocates it across atlases by on-screen importance.
- Builds atlases per material slot, defaulting to 1024 px maximum in line with VRChat's guidance.
- Chooses the ASTC block size per texture and shows a quality preview with the memory cost.
- Reports in-memory size, not download size, and does not rely on Crunch compression, which only reduces download size.

### Feature pruner

- Table of every toggle, outfit, radial and gimmick with its Android cost in triangles, materials and PhysBones.
- Tick what survives on Android; removed features have their objects deleted on the variant.
- Keeps the expression parameter list identical to the PC version so sync between platforms holds; menu entries for removed features are hidden or replaced with a "PC only" label.
- Handles shader-driven effects with no mobile equivalent (dissolve, audio-reactive, grab-pass): swaps to a plain on/off toggle or drops them.

### Android gate

- Linter profile "Android": disallowed shaders, unsupported components, PhysBone and contact hard limits, texture sizes, mesh and material counts.
- Size estimate before upload against the 10 MB build limit, with the largest contributors listed.
- Optional upload block when the variant misses its target rank.

### Cross-platform builder

- Queues PC then Android builds to the same blueprint ID, with a pre-flight check that both variants share parameters.
- Side-by-side viewport of PC and Android variants in the same pose and lighting.
- Status panel per platform: rank, size, last upload, and whether the Android variant is stale relative to the PC prefab.
- Optional fallback-avatar check: confirms the Android variant meets the rank needed to be set as a fallback.

## Roadmap

Six releases, each usable on its own; the core and the two highest-value modules ship first so the toolbox pays for itself on real commissions early.

| Release | Ships | Done when |
| --- | --- | --- |
| 0.1 Foundation | Core (scanner, asset graph, findings, window shell), BlendShape Copier moved in, VPM repo live | Scanner handles a full commission avatar in under a second after first scan |
| 0.2 Materials and QA | Material manager (inventory, bulk edit, presets, lock management), linter with starter rules, performance budget | A delivery avatar passes the "Commission delivery" profile with no manual checks |
| 0.3 Rigging and diff | PhysBone preset library, prefab diff, component copier, texture tools, animator and parameter tools | A base-version update can be ported with diff and copier alone |
| 1.0 Delivery and build-time | Delivery packager, platform helper, NDMF passes (material dedupe, per-platform textures), shader conversion, documentation site | Public release with docs and a stable rule and preset format |
| 1.x Advanced | Lighting test bench, channel packer and live debugger first; then clipping detector, toggle matrix tester, parameter compressor, hidden mesh remover, recipes and batch runner | A commission can go from finished prefab to packaged delivery with one recipe |
| 1.x Android | Android gate and PhysBone reducer first; then material baker, texture budgeter, feature pruner, mesh and bone reducer; conversion helper and cross-platform builder last, tying them together | A commission avatar converts to a Medium Android variant from a saved profile in one run |

Shader conversion is held to 1.0 because the alias map needs real-world use from bulk editing before it can be trusted to convert whole avatars.

## Open questions

- [ ] Name and package ID prefix: "Sable Toolbox" and `studio.sable.toolbox` are working assumptions.
- [ ] Licence: MIT keeps it simplest for other creators to build rules and presets on; GPL would keep forks open.
- [ ] One package or several: separate module packages are cleaner, a single package is easier to install and support.
- [ ] Shader coverage for v1: Poiyomi and lilToon only, or Standard-family and other toon shaders too.
- [ ] Should the toolbox ever generate VRCFury or Modular Avatar components, or stay strictly read-only towards them?
- [ ] Android scope for the first Android release: the full toolset as specified, or the gate, PhysBone reducer and material baker only.
- [ ] Where the fit QA sweep lives: a Blender plugin with a Unity-side component, or a toolbox module.
- [ ] Preset sharing: local library only, or a community preset repo later.
