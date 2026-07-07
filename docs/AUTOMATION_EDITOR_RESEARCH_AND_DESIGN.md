# Automation Editor research and design

This document records the current Automation Editor architecture, the review
findings from the latest code pass, and the design target for the Tinkercad-like
ESU block builder. It complements
`docs/AUTOMATION_EDITOR_NESTED_WORKSPACE_GOAL.md`, which describes the desired
end state.

## Current verdict

The Automation Editor is directionally aligned with the requested vision. It is
already a native-compatible editor shell rather than a separate automation
runtime. It can discover craft automation controllers and targets, create
directional links, inspect native Breadboard data, create Generic Getter/Setter
proxies, add live native Breadboard component wrappers, compile a narrow
if/else recipe into native Evaluator/Switch nodes, and persist early System
Block metadata.

It is not full vanilla Breadboard compatibility yet. The main gap is that ESU
Blocks are still mostly an ordered recipe stack. They are not yet a general
connected-nub graph compiler that can lower arbitrary ESU block links into the
same native component graph a player would build by hand in FtD.

## Current Safe Improvements

- The workspace guide uses shared Automation vocabulary and compact
  compatibility labels so users can distinguish direct native data from
  `Native + ESU Layout` metadata.
- The focused editor pages repeat the current Next action and native
  compatibility label near the active Graph, Code, ESU Blocks, and System Block
  controls, not only in the bottom strip.
- The ESU Blocks linked-signal panel now explains the Input/Read/GBG and
  Output/Set/GBS mapping, with empty states that name the correct click order
  before adding Read or Set blocks.
- The ESU Blocks linked-signal panel now has a local search for linked
  Inputs/Outputs before offering Add read/set actions. It filters by direction,
  target label, category, role, runtime type, cell, or target key, reports
  matching/total counts per direction, and warns if the selected signal is
  hidden by the filter.
- The semantic ESU block palette now has its own search field before beginner
  blocks render. It filters catalog definitions by label, category, kind, port,
  setting, or description, reports matching/category counts, and shows an empty
  state without changing the native lowering behavior.
- The ESU Blocks palette now uses beginner-facing categories for Inputs, Logic,
  Math, Outputs, Timing, Organization, and Advanced Native. Palette cards show
  a one-line explanation, named input/output port summary, Beginner/Advanced/
  Native Wrapper/Metadata Only role, compatibility label, and whether the block
  can currently lower to native behavior.
- ESU Blocks workspace ports now come from the block catalog instead of generic
  input/output placeholders. The canvas shows named input/output nubs, draws the
  supported starter links nub-to-nub, and labels link direction in the preview.
- ESU Blocks Check now validates missing starter links with block-friendly
  messages such as Compare needing a left input and Set Target needing a target
  and value. Unsupported Delay and nested System Block blocks are clearly marked
  as not lowerable through the safe starter compiler instead of being silently
  applied.
- The left linked-target list and ESU Blocks linked-signal panel now state that
  link identity is saved per controller with portable target keys. Users only
  need to re-check linked Inputs/Outputs when duplicated unnamed blocks at the
  same cell/type make a restored target ambiguous.
- The left linked-target list now has a local selected-controller link search.
  It filters Inputs/Outputs by direction, target label, category, role, runtime
  type, cell, controller, or target key, reports matching/total counts, shows
  per-direction empty states, and warns when the selected linked target is
  hidden by the filter.
- The ESU Blocks selected-block inspector now explains Target, Property,
  Threshold/Value, and order edits; Read/Set blocks spell out Auto versus
  explicit native GBG/GBS property binding; native wrapper blocks state that
  Apply creates advertised native Breadboard components and Revert removes only
  ESU-generated component ids.
- The Read/Set native property picker now explains that it discovers FtD
  options through a temporary native Generic Getter/Setter proxy, cleans that
  probe up, stores only ESU metadata when a property is selected, and performs
  the real GBG/GBS binding on Apply. It also shows the 80-option safety cap and
  asks users to filter large native property lists.
- The Breadboard `Linked target proxies` action list now searches linked targets
  before the eight-row HUD cap. It filters by Input/Output, GBG/GBS, label,
  category, role, runtime type, controller, cell, or target key, reports
  shown/matching/total counts, and warns when the selected link is hidden by
  the filter.
- Individual linked-target proxy shortcut rows now label those buttons as
  immediate native Proxy Node creation for GBG/GBS components. The same copy
  separates manual proxy shortcuts from Blocks/Code/System Apply-owned nodes
  tracked by `Revert compile`, and reminds users that auto-picks can be
  corrected with the native property picker.
- The ESU Blocks native-lowering preview now reports how many native lowering
  steps are visible out of the full plan and warns when additional steps are
  hidden in the compact HUD. The message keeps Check clearly preview-only and
  names Apply as the action that performs native writes.
- The ESU Blocks native-lowering preview now also reports whether the supported
  graph slice is complete, which target/property will be read, which
  target/property will be written, how many Advanced native wrappers are queued,
  and which metadata-only blocks stay in ESU layout.
- The Advanced Graph page now labels its Breadboard canvas as native edit mode:
  quick-adds, moves, wires, setting changes, and deletes apply through FtD board
  commands immediately. The same helper text distinguishes those manual native
  edits from ESU Revert ownership, which only removes generated component ids
  from the last Blocks/Code/System apply.
- The native Breadboard quick-add, shared-variable bridge, and missile-output
  shortcut rows now repeat that boundary beside the buttons. They state that
  shortcuts create advertised/native vanilla nodes immediately, while
  Blocks/Code/System Apply-owned nodes are the ones tracked by `Revert compile`.
  The individual quick-add buttons also carry outcome-focused tooltips that
  name the native component/search terms, the immediate native write, and the
  disabled state when the selected board does not advertise a matching vanilla
  component.
- ACB and ACB Controller panels now use matching native edit-mode guidance. ACB
  fields are described as direct writes to native `ControlBlockData` through the
  FtD `Var.Us` path, and ACB Controller button edits are described as immediate
  native button data writes. The guidance also separates native keyword-output
  bridges from ESU-generated proxy/component ids owned by Revert.
- The ACB Controller button editor now searches native button data before the
  visible-row cap. It filters by button number, name, keyword, Breadboard
  output, shape, color, or reflected type, reports shown/matching/total counts,
  and shows an empty state without changing the immediate native edit path.
- The System Block signature page now has a scope/rebinding guide. It states
  that Apply template saves ESU-only metadata, Check validates without native
  controller mutation, Suggest ports reads the current selected controller's
  linked Inputs/Outputs, and users should re-check ports only when duplicated
  unnamed blocks make portable rebinding ambiguous.
- The System Block signature port preview now shows normalized input/output
  ports as explicit ports/nubs before Check or Apply. Duplicate names are
  surfaced in the HUD and validation preserves duplicates long enough to reject
  them instead of silently dropping repeated entries.
- The reusable System Block template list now shows its current profile-library
  count against the existing 64-template persistence cap, notes that duplicate
  normalized name/ports keep the latest template, and warns when library
  paging/virtualization is still needed.
- The reusable System Block template list now has a search field for template
  name, controller, input/output ports, comments, and internal graph notes, with
  matching/total counts and an empty state when the active filter hides every
  template.
- The Graph page `System Block nodes` list now has a local graph-node search for
  saved nested nodes on the selected controller. It filters by node name,
  controller, input/output ports, comment, or internal graph notes, reports
  shown/total counts, and warns when the currently open nested node is hidden by
  the filter.
- The Code page now shows a compact recipe-lowering guide that names the two
  supported deterministic recipe shapes, the native Math Evaluator / Logic Gate
  / Switch / Generic Setter lowering path, and the no-arbitrary-runtime rule.
- The Code page built-in recipe picker now includes a searchable deterministic
  recipe catalog. It filters by recipe name, target category, or expression text,
  reports matching/total recipe counts, and shows an empty state without expanding the supported parser syntax.
- The Code page output-target picker now searches linked writable outputs before
  the six-button HUD display cap. It filters by label, category, role, runtime
  type, cell, or target key, reports shown/matching/total counts, and warns when
  the selected output is hidden by the active filter.
- The Code page linked-identifier strip now searches evaluator-safe input names
  before the five-button display cap. It reports shown/matching/total counts and
  keeps insertion limited to deterministic identifiers derived from linked input
  targets.
- The Advanced native component palette is still generated from the selected
  board's advertised native types, but it now has a search field before the
  visible cap. Large boards can be narrowed by label, type, namespace, or
  description without changing native data. Native wrapper cards also expose
  the advertised FtD component type so users can see which vanilla component
  Apply will create.
- The Advanced Graph stored-component list now has a search field before its
  safe 18-row display cap. It filters reflected native components by label,
  type, id, proxy target/filter, or description, reports matching/total counts,
  and says large boards still need virtualization/pagination for full
  compatibility.
- The Breadboard Inspector now warns when the reflected native component list
  reaches the current 64-component scan cap. The warning says large boards may
  contain more native components than the HUD can inspect yet and that full
  compatibility still needs scalable native enumeration/virtualization.
- Selected native Breadboard nodes now warn when the current node has more
  input/output ports than the safe HUD display caps. The message names the
  eight-nub canvas cap, the four-port wire-control cap, and the remaining port
  virtualization/pagination gap for full vanilla compatibility.
- The selected native node wire controls now have a local port/nub search for
  the currently visible capped ports. It filters by input/output, index, label,
  connection state, or connected source, reports visible/capped counts, and
  warns when the selected wire-source output is hidden by the filter.
- The world target browser keeps its safe visible-row cap, shows how many
  filtered targets are hidden, and points users back to search/category filters
  when a large construct has more matching targets than the HUD should render at
  once. Target search also shows example terms and an active-search reminder so
  users can narrow large constructs by label, role, construct, controller id, or
  cell.
- The right-panel `Controllers on craft` index now has its own search field
  before the grouped controller display caps. It filters placed Breadboard/ACB
  controllers by label, controller type, construct, cell, or target key, reports
  shown/matching/total counts, and warns when a selected controller is hidden by
  the active search.
- Runtime diagnostics now label findings as missing native capability, ESU UI
  coverage, Apply-required setup, save/reload evidence, or scale/cap limits.
  The Runtime Checks HUD includes a compact legend for these labels plus a
  local filter for severity, labels, native capability text, proxies, Switch,
  save/reload, or target text. The filter is display-only: it filters cached
  diagnostic lines and does not rerun probes or write native data. These checks
  remain observational except for existing same-value native access probes. The
  `Run checks` tooltip and empty state now state that those probes do not create
  native nodes or change craft behavior.
- The `Live evidence gates` rows now explain that they are read-only results
  from the last `Run checks` pass. `OK` means evidence is present, while `WAIT`
  means link/apply/prepare/run checks work is still needed; the rows themselves
  do not write native data.
- The save/reload validation panel now spells out the one intentional native
  write in that workflow: `Prepare validation graph` compiles the deterministic
  validation Recipe into native Evaluator/Switch proof nodes plus a
  target-specific Generic Setter Proxy Node, and those generated validation
  nodes are tracked by `Revert compile`.
- The same panel now labels `Capture baseline` and `Compare baseline` as
  diagnostic-only. They read live evidence fingerprints, update ESU
  status/runtime-log text, and do not write native Breadboard data.
- Verification now has a dedicated Check-safety gate for Automation. It keeps
  ESU Blocks and System Block Check paths source-separated from Apply/Revert
  paths that create, compile, or remove native Breadboard nodes.
- Check-safety verifier coverage is now split into focused gates for ESU Blocks
  preview-only Check, System Block non-mutating Check/validation, Apply-only
  native mutation paths, and documentation/checklist coverage of the Check
  contract.
- ESU Blocks/System Block workspace verifier coverage is now split into focused
  gates for shared HUD shell, workspace model, Tinkercad page/canvas, linked
  GBG/GBS signals, Blocks Apply/Revert lowering, System Block host/proxy
  lowering, workspace guide vocabulary, System Block metadata persistence, and
  docs/checklist alignment.
- Runtime diagnostics verifier coverage is now split into focused gates for
  native capability probes, persistence fingerprints, diagnostic labels,
  validation controls, same-value native access probes, and smoke-checklist
  coverage. Failures should point at the missing diagnostic surface instead of
  one oversized Automation assertion.
- Runtime diagnostics probe verifier coverage is now split further into focused
  gates for the Run/Breadboard probe path, advertised native component
  families, Generic Getter/Setter proxy prerequisites and property pickers,
  Switch fail-expression readiness, and linked ACB/ACB Controller surfaces.
- Runtime diagnostics evidence verifier coverage is now split into focused
  gates for native persistence fingerprints, Generic Getter/Setter proxy
  fingerprints, Switch readiness evidence, sanitized stable hashing, and native
  component/wire/linked-target fingerprints.
- Runtime Checks HUD verifier coverage is now split into focused gates for
  panel/run-action wiring and the native capability, ESU UI coverage,
  Apply-required, save/reload evidence, and scale/cap label legend.
- Target/link guard verifier coverage is now split into focused gates for
  workspace metadata, target search guidance, Important/Generic browsing,
  directional Input/Output links, canvas pan/zoom, native property picking,
  target preview rendering, and mode-handoff overlap prevention.
- Breadboard command/stale-controller verifier coverage is now split into
  focused gates for reflected native command failure, stale selected-controller
  close/clear behavior, and native graph selection/wire/Revert state cleanup.
- Iconized row verifier coverage is now split into focused gates for controller
  palette placement rows, controller index selection rows, and target list link
  rows so old per-row button regressions report the exact affected list.
- HUD shell and mode-state verifier coverage is now split into focused gates
  for shared shell controls, world highlights, focused editor layout, placement
  previews, mouse/right-click routing, x-ray world picking, and context menus.
- APS starter and semantic-lowering verifier coverage is now split into focused
  gates for template seeding, ammo/angle defaults, semantic native lowering,
  readable Getter identifiers, metadata-only auto property hints, and preferred
  ammo property matching.
- Semantic ESU block palette verifier coverage is now split into focused gates
  for palette/catalog wiring, search/count/empty states, definition matcher
  fields, and docs/checklist coverage of beginner block search.
- Advanced native wrapper verifier coverage is now split into focused gates for
  live `AvailableComponents` generation, native palette search/cap behavior,
  and advertised FtD component type identity on wrapper cards.
- Cap warnings remain visible when the filtered native component list is still
  larger than the current safe display limit. This is a usability improvement,
  not the final pagination/virtualization work.

## Current architecture

- `AutomationEditModeBehaviour` owns mode activation, handoff, and the GUI
  session lifecycle.
- `AutomationEditSession` owns the HUD shell, controller placement, target
  linking, editor pages, Tinkercad-style block UI, native Breadboard graph UI,
  code recipes, System Block UI, diagnostics, and generated-node revert state.
- `AutomationTargetCatalog` discovers candidate craft blocks and classifies
  them as controllers, movement targets, weapons, spinblocks, propulsion,
  resources, ACB targets, Breadboard-readable targets, and Breadboard-writable
  targets.
- `AutomationControllerCatalog` identifies native FtD automation controller
  block types such as Breadboard, AI Breadboard, ACB, ACB Controller, and
  Missile Breadboard Controller.
- `AutomationBreadboardInspector` is the native Breadboard bridge. It reflects
  the board, enumerates components, reads ports, creates advertised native
  component types, wires ports through FtD undo/redo commands, edits a focused
  set of component settings, and configures Generic Getter/Setter proxies.
- `AutomationBlockWorkspace` stores ESU block metadata: node kind, ports, links,
  target binding, auto property hints, explicit property selections, native
  component wrapper requests, canvas position, and lowering plans.
- `AutomationRuntimeDiagnostics` probes the selected controller for available
  native Breadboard capabilities, generic proxy health, Switch readiness, and
  persistence fingerprints.

## Tinkercad Builder Model

The intended builder model is a friendly layer on top of native FtD Breadboard
concepts.

- **Semantic ESU blocks** are beginner-facing blocks such as Read Target,
  Compare, If/Else, Constant, Math Evaluator, Switch, Set Target, Delay, Comment,
  and System Block. These should read like a visual program.
- **Native wrapper blocks** are generated from the selected board's advertised
  `AvailableComponentTypes`. They are the compatibility escape hatch for every
  vanilla component ESU does not yet have a polished semantic block for.
- **Ports and nubs** are the same idea. ESU should draw explicit input and
  output nubs and lower nub-to-nub links into native Breadboard connections.
- **Settings** are block-specific editable values. For semantic blocks these are
  ESU fields such as threshold, operator, target, and property. For native blocks
  they should reflect the vanilla component settings wherever FtD exposes them.
- **Linked signals** are target bindings from the craft world. Input links feed
  Generic Block Getter (GBG) nodes. Output links feed Generic Block Setter (GBS)
  nodes.
- **Lowering** converts ESU metadata into native Breadboard components and
  native connections. Check must be non-mutating. Apply may create native data.
  Revert must remove only the generated native nodes it owns.
- **System Blocks** are ESU-owned templates for grouping, naming, ports, and
  reusable metadata. They must lower to native behavior instead of becoming a
  parallel runtime.
- **Compatibility labels** should stay compact and honest: `Native` for direct
  vanilla data surfaces, `Native + ESU Layout` where ESU stores UI/template
  metadata that lowers to native nodes, and `ESU Runtime Required` should remain
  `no` for committed automation behavior.

## Vanilla Breadboard Mapping

The Breadboard Basics reference uses the same primitives ESU should expose:
components, explicit input/output nubs, Generic Block Getter, Generic Block
Setter, Math Evaluator, Switch, variables, and component settings.

Important compatibility rules for ESU:

- GBG nodes read values from real craft blocks. GBS nodes write values to real
  craft blocks.
- Math Evaluators are the flexible native expression layer. ESU code recipes and
  high-level math blocks should lower into evaluator components when possible.
- Switch components are the native if/else gate used by the current APS
  threshold slice.
- Variable Reader and Variable Writer components are native cross-board and
  cross-construct signal tools. ESU should expose them through the Advanced
  palette immediately and later with polished semantic blocks.
- Vanilla Breadboard graphs do not allow ordinary graph recursion. Recursive
  behavior belongs in native-supported mechanisms such as evaluator `Output(n)`
  patterns, not in an ESU runtime loop.

## Implemented Vertical Slice

The first official ESU Blocks workflow is:

1. Place or select a Breadboard.
2. Link an APS or weapon target as an input.
3. Link a spinblock as an output.
4. Open ESU Blocks.
5. Use the contextual starter stack:
   `Read APS ammo -> Compare ammo < 10 -> Constant 45 -> Set spinblock angle`,
   with an else value of `0`.
6. Check reports the native lowering plan without mutation.
7. Apply creates native Evaluator/Switch logic, creates or reuses GBG/GBS proxy
   nodes, binds preferred ammo/angle properties where possible, and records
   generated component ids for Revert.

This slice proves the shape of the vision, but it does not prove full block
graph compilation.

## Code Review Findings

- **P1 - Portable identity is still heuristic for duplicate unnamed blocks.**
  ESU now stores controller workspaces with portable block type/cell/name keys
  in the profile, but identical unnamed blocks at the same local cell/type can
  still make restored links ambiguous.
- **P1 - ESU Blocks are still recipe-lowered.**
  `CheckBlocksToNative` recognizes the current Read/Compare/Constant/Set flow
  and native component requests. It does not yet traverse arbitrary connected
  block links or compile a general graph.
- **P1 - Native graph scale is capped.**
  The native inspector reads only a fixed number of components and the Advanced
  palette intentionally limits visible advertised components. That keeps the HUD
  safe today but is not the final compatibility bar.
- **P2 - Native settings coverage is partial.**
  Settings editing is implemented for focused component families such as
  Evaluator, Switch, Logic Gate, Constant, proxies, ACB, and ACB Controller.
  Other advertised native components can be created, moved, wired, and deleted,
  but not fully inspected or configured through component-specific settings yet.
- **P2 - Auto property hints are not full compiler bindings.**
  The APS starter applies `ammo` and `angle` hints. Apply uses preferred search
  terms for GBG/GBS property binding, but expression identifiers still fall back
  unless a native property is explicitly selected.
- **P2 - Code recipes are intentionally narrow.**
  The parser supports one evaluator expression or a four-line if/else recipe.
  It is deterministic and safe, but not a general language.
- **P3 - Verification assertions are too broad.**
  Some Automation checks still combine unrelated HUD, graph, native, and
  template requirements. Check safety, runtime diagnostics, target/link guard,
  Breadboard command/stale-controller safety, HUD shell, ESU Blocks/System
  Block workspace, APS starter/semantic lowering, semantic palette, and
  Advanced native wrapper coverage are now split into smaller gates. Iconized
  controller/target row coverage is also split by row family. The remaining
  broad assertions should keep moving in that direction so failures point to
  the exact missing behavior.

## Full Vanilla Breadboard Compatibility Target

ESU reaches full vanilla Breadboard compatibility when all of the following are
true:

- Every native component advertised by the selected board is visible in the
  Advanced palette, searchable, and addable without hard-coded type lists.
- Every native component input and output nub is visible, selectable, and
  wireable from the ESU canvas.
- All component settings that vanilla exposes are discoverable and editable, or
  clearly marked read-only with the reason.
- Generic Getter and Generic Setter property pickers enumerate the same target
  properties vanilla exposes and can be corrected manually after auto-picking.
- ESU Blocks can compile connected nub graphs, not only an ordered starter
  recipe.
- Check never mutates native data. Apply mutates only through native FtD data and
  commands. Revert removes only ESU-generated native components and connections.
- No committed behavior depends on an ESU automation runtime.
- Save/reload preserves native Breadboard components, wires, proxy selections,
  ACB edits, ACB Controller button edits, and ESU-only layout/template metadata.
- Large native boards remain usable through search, filtering, virtualization,
  and pagination instead of fixed small caps.

## Improvement Roadmap

1. **Correctness first.** Replace session-only target identity with a stable
   construct/block identity, expand native component enumeration safely, split
   verifier gates, and make auto hints participate in compiler identifiers.
2. **Generalize the block graph.** Promote `AutomationBlockLink` from display
   metadata into the compiler input, model ports as typed nubs, topologically
   lower connected semantic blocks, and preserve native wrapper requests in the
   same graph.
3. **Expose native settings.** Build a reflection-backed native settings catalog
   with safe editors for booleans, numbers, strings, enums, vectors, and
   component-specific picker actions.
4. **Make templates portable.** Store System Block ports and internal graphs in
   controller-independent metadata, then bind them to live linked targets at
   Check/Apply time.
5. **Polish debugging.** Add live value overlays, generated-node grouping,
   validation proofs, baseline comparison, and clearer failure recovery for
   partially applied native graphs.

## Verification Targets

- The documentation must keep saying that ESU is native-first and does not add a
  parallel automation runtime.
- The APS ammo `< 10` to spinblock angle `45 else 0` slice must remain covered.
- The Advanced palette must remain generated from live native board capability.
- Verifier assertions should distinguish HUD polish, target/link behavior,
  semantic block lowering, native wrapper coverage, System Block metadata, and
  docs so one drift does not hide the exact failure.
