# Changelog

This file tracks player-visible and release-process changes for EndlessShapes
Unlimited. Dates use `YYYY-MM-DD`.

The packaged Steam Workshop/player readme remains
`EndlessShapesUnlimited/README.md`. Keep this changelog technical enough for
GitHub history, but short enough that it can be copied into release notes.

## 1.0.10 - 2026-07-15

### Removed

- Removed Automation Builder, including its runtime registration, graph UI,
  breadboard adapter/lowering code, dedicated input and preview systems,
  options/keybinding entry, documentation, and feature-specific verification
  suites.
- Removed ESU's no-longer-needed `Breadboards.dll` assembly dependency. Saved
  craft data and native FtD breadboard components are not modified.

### Changed

- Restored the remaining clean-editor cycle to Decoration Edit Mode, Surface
  Builder, and Smart Block Builder, with Smart Builder returning to Decoration
  Edit Mode.
- Preserved the numeric IDs of existing copy/paste key bindings so profiles
  created by earlier versions continue to load those bindings correctly.
- Kept the remaining 628 executable verification checks green with warning-free
  Release builds and a hash-verified runtime package.

## 1.0.9 - 2026-07-15

### Added

- Added link-first x-ray picking in Automation Builder Select mode. World links
  can be hovered and selected through solid craft blocks, with deterministic
  cycling when several projected links overlap.
- Added a dwell-based linked-target preview shared by world links, world link
  rows, canvas linked-hardware rows, and Read/Set blocks. The preview uses an
  isolated orbiting render camera plus an always-visible wireframe around the
  exact live craft cell; it never moves the player camera or changes craft
  materials.
- Added a searchable **Vanilla** palette populated from the selected
  breadboard's live `AvailableComponentTypes`. Advertised component families can
  be staged as their exact native type even when ESU has no specialized visual
  shape. The live adapter matrix covers FtD 4.3.3's 57 base and 6 AI families.
- Added exact indexed native-port editing. Graph connections now retain both the
  source output index and destination input index through import, copy/paste,
  Apply, verification, and reopen.
- Added reflection-backed typed native setting editors, including bounded
  integers/floats, booleans, enums, ranges, vectors, colors, and nested PID
  `SubPack` fields, with stable setting keys independent of localized labels.
- Added Ctrl/Shift multi-selection, lasso, atomic group move/copy/paste/delete,
  a clickable minimap, per-board viewport persistence, visible-canvas culling,
  and virtualized target/property choice rows.
- Added exact typed logical wires for the loose Automation dataflow canvas.
  Inputs accept one source, outputs support fan-out, occupied inputs reconnect
  atomically, and geometry can no longer manufacture program connections.
- Added **Conditional Value** with A/B/Then/Else numeric sockets, explicit Else,
  live preview, and `<`, `<=`, `>`, `>=`, `=`, and `!=`. It lowers to one
  canonical native Evaluator with inputs 0/1/2/3 and result output 0.
- Added V4 Automation ownership metadata for visible node/group identity,
  descriptor schema, native member roles, exact logical-to-native port maps,
  inline values, and signed visual position. Atomic re-keying covers paste and
  cross-board rebinding; malformed owned metadata falls back losslessly.
- Added a compact blocking-issue bar whose deterministic **Fix** action focuses
  the affected block and opens its exact property picker when applicable.

### Changed

- Replaced the canvas category grid with one top-to-bottom list ordered
  **Inputs**, **Outputs**, Conditions, Math, Logic, Values, Notes, then Vanilla.
- The graph workspace is now an effectively unbounded signed coordinate plane.
  Placement, dragging, snapping, panning, Fit, Center, viewport persistence,
  culling, and the grid all accept large positive and negative positions.
- Rebuilt the Automation Builder canvas around zoom-independent palette and
  graph coordinate paths. Its compact category list, search header, virtualized
  rows, palette previews, scrolling, and hit regions remain at HUD scale while
  graph zoom affects only the workspace.
- Replaced nested Scratch hats, C-block mouths, stack/body snapping, and subtree
  peeling with independent rounded dataflow cards. Headers move cards; sockets,
  fields, and ports own their controls; visible typed wires are authoritative.
- Removed Starter Flow and the guided nested-rule prototype. A graph containing
  only linked endpoints now shows a non-mutating hint. The primary APS workflow
  is `Shells available -> IF A < 30 THEN 10 ELSE 0 -> Spin Target angle`.
- Setter now exposes explicit `Value` and optional Boolean `When` inputs mapped
  to native inputs 0 and 4. Conditional Value always emits Then or Else; use
  `When` when false must perform no write.
- Normal mode now shows logical wires, typed sockets, inline values, and compact
  live/status badges. Advanced reveals exact native wires, indexed ports, IDs,
  canonical Evaluator expressions, helpers, settings, and Native Plan.
- Imported non-marker native components can now be edited as connection
  destinations. Semantic adapters expose their normal fields; Advanced Native
  cards expose exact indexed ports and advertised settings. Supported imported
  destination inputs—including existing latched wires—can be edited; genuinely
  unrecognized future-version families remain lossless and read-only. Deleting
  an imported component requires confirmation.
- Imported vanilla sources may feed editable destinations without being adopted,
  moved, or rewritten by Automation Builder.
- Breadboard item-definition discovery now uses a registry fingerprint and
  bounded cache instead of enumerating every loaded definition every frame.
- Expanded the executable regression harness from 672 to 795 checks with live
  63-family manifest coverage, lifecycle/recovery routing, evaluator fallback,
  exact indexed-port identity/copying, cross-board unresolved bindings, PID and
  integer settings, stable property IDs, v1/v2 markers, output-state rollback,
  port anomalies, catalog-cache behavior, unresolved link retention, pointer
  capture recovery, signed coordinates, x-ray link picking, hover arbitration,
  preview-card bounds, and target-renderer contracts.

### Fixed

- Fixed a staged Output Setter disappearing after the 0.75-second native
  refresh when a generic `value` link did not identify one unique writable
  native property. Link identity is retained separately from the editable
  property, unresolved setters remain selected with a readiness warning, and
  dirty-draft refresh cannot prune graph nodes or rewrite history.
- Fixed duplicate localized Spin property labels selecting the wrong native
  variable. `Target angle` now assigns exact setter key `2:100`, while
  `Rotation rate` assigns `3:100`; the searchable picker commits only a stable
  key and no raw property text is editable.
- Fixed presentation-only control-body membership compiling as if it were an
  executable branch. Setters inside `if` now receive the recursively lowered
  predicate on native trigger input 4, and Check blocks legacy body-only layouts
  until the user converts or detaches them.
- Fixed canvas input becoming permanently captured after right-clicking during
  a pan, and fixed crossing an inline pill during a node drag cancelling only
  half of the gesture. Every capture now finishes or cancels through one
  idempotent path and movement is committed at most once.
- Restored plain-left empty-canvas panning and made Shift+left-drag the explicit
  replacement lasso; Ctrl+Shift toggles lasso hits, while middle-drag and
  Alt+left-drag remain pan aliases. Pointer movement below six screen pixels is
  treated as click jitter and cannot move a block or dirty graph history.
- Fixed graph zoom leaking into palette block size, row layout, scrolling, and
  hit regions, and extended the workspace grid through every clipped edge for
  positive or negative graph positions.
- UI toggle, hotkey, Escape, and build-mode-loss close requests now share the
  dirty-draft Apply/Discard/Cancel flow. Update or GUI failures pause the live
  in-memory session behind a recovery dialog instead of silently destroying it;
  **Close safely** exits directly from that recovery path while preserving the
  recoverable snapshot.
- Breadboard selection, link focus/creation, and board switching now preflight
  the same dirty-draft state machine before mutating selection or links. Cancel
  is atomic, stale targets leave the current draft untouched, and per-board
  viewport/history state follows the selected board.
- Unsupported Evaluator expressions remain opaque Advanced Native components
  instead of being misrepresented as comments, and pasted target bindings keep
  their staged world links across graph refreshes. Failed pastes restore graph,
  links, pending edits, history, recovery data, and allocation counters.
- Apply now blocks a stale draft when the live board fingerprint changed,
  rejects a deleted/replaced native board even when an identical board occupies
  the same construct cell,
  verifies every planned physical source-output/destination-input pair, restores
  output topology/colors/settings during rollback, and stores the latest dirty
  edit snapshot for recovery after editor teardown.
- Fixed normal-mode loose cards returning to their old position after release.
  Free signed positions now commit exactly once, while legacy magnetic semantic
  snapping is available only in Advanced mode.
- Occupied input pills now reconnect across their complete measured hit region;
  releases outside the workspace cancel, and a topmost card blocks hidden ports
  on cards beneath it.
- Reopen now treats the canonical native Evaluator expression as authoritative.
  V4 markers may restore only a remembered literal hidden behind a connected
  socket; unsupported schemas or incompatible exact-port maps fall back to a
  lossless Vanilla card instead of rewriting externally changed behavior.

### Packaging

- Release builds no longer overwrite the packaged mod DLL by default. The copy
  target requires `CopyReleaseAssemblyToModFolder=true`, which the release
  packaging script supplies explicitly.

## 1.0.8 - 2026-07-11

### Added

- Renamed Decoration Edit's Mesh Palette to **Block Palette** and added a
  default-off **Build** sub-mode. With Build off it retains decoration-mesh
  selection; while enabled it accepts native inventory block definitions by
  left or right click, rejects locked or construct-incompatible choices, uses
  FtD's native grid placement path, and right-click samples craft blocks with
  their rotation.

### Changed

- Block Palette placement retains every item's native footprint and FtD's own
  marker, rotation, mirroring, attachment, collision, resource, undo, and
  networking behavior. Its authoritative full-size marker follows the free
  mouse cursor and only hands an actual click to FtD while the exact target is
  valid for add. Simple-mode Shift replacement and distance-scaled attachment
  indicators are disabled because their hidden targets or offsets cannot safely
  match that cursor preview.
- Expanded the executable regression harness from 627 to 672 checks with
  Decoration X-ray occlusion, native cursor-following Block Palette targets,
  native footprint math, input isolation, cursor lifecycle, partial-arc
  generator alignment, real-mesh Extra Tools preview/render-budget lifecycle,
  and opt-in large-craft part-status memory-safety coverage.

### Fixed

- Fixed Circle and Frustum blocks scattering when a partial arc used an X- or
  Y-axis mesh, including after rotating the generator plane. The segment
  quaternion was correct, but its stored Euler angles used a different rotation
  order from FtD; generated blocks now replay the same Unity-order rotation and
  span their intended arc or rail endpoints.
- Fixed Surface Builder **Extra Tools** Preview showing only cyan guide lines.
  Preview now draws the selected decoration mesh at the exact planned anchor,
  position, rotation, scale, paint color, material override, and symmetry
  variants used by Place. Large plans deterministically sample up to 768 meshes,
  1,400 guide segments, and 650 Same-anchor hints across the complete shape so
  a 100,000-placement draft cannot flood the render overlay.
- Added a default-off **Memory-safe part status checks** option for constructs
  with millions of status-checkable blocks. When enabled, ESU bounds FtD's
  once-per-second temporary status dictionary and omits already-clear healthy
  entries while retaining warnings, errors, and entries needed to clear stale
  flags. This addresses repeated `StatusUpdate` dictionary out-of-memory
  failures without changing blueprint JSON or the default vanilla path.
- Fixed Decoration X-ray affecting Box selection only. With X-ray off,
  viewport hover, single/Shift-click, right-click context, paint, and box
  acquisition now reject decoration centers occluded by any block in the main
  craft/subobject tree; X-ray on restores through-block acquisition, while
  Outliner and Selected anchor rows remain directly selectable. Visibility rays
  use a nonalloc buffer plus bounded cell fallback, treat exact backside tether
  geometry as occluding while avoiding coarse partial-block false hits, and
  reject X-ray-off boxes above 512 projected centers before partial work.

### Packaging

- Advanced the manifest, assembly, player documentation, release checklist,
  and deterministic release package to version 1.0.8 while preserving the
  published 1.0.7 history.

## 1.0.7 - 2026-07-06

### Added

- Added two profile-backed ESU editor HUD options. **Fade HUD behind popups**
  defaults off so modal menus keep the editor visually solid while still
  owning input. **Responsive paint palettes** defaults on so color buttons fill
  the available width in at least two rows; turning it off restores the legacy
  fixed grid.
- Added viewport drag-selection to the Decoration paint tool so one brush
  gesture can paint every eligible decoration or block inside the selection.
- Added a one-time, profile-persisted Automation Builder warning that clearly
  identifies the editor as unfinished and potentially very buggy before first
  use.
- Established Automation Builder's initial Scratch-style editor for vanilla
  breadboards. Stack, value-socket, and control-body snapping now share exact
  drag previews; attached block groups support copy, paste, duplicate, delete,
  keyboard nudging, and a 64-state Undo/Redo history.
- Added an Automation Builder guide covering controller compatibility,
  every block-to-native mapping, Forever/Switch dataflow semantics, validation,
  native ownership, editing controls, safe discard, and craft-save round trips.
- Added Automation Builder as a fourth ESU editor mode, focused on FtD
  breadboard workflows with AI/basic breadboard placement, input/output block
  linking, a Decoration-style HUD, view controls, number-key shortcuts, panel
  splitters, close prompts, and a Tinkercad-style native block graph.
- Added Automation Builder graph palette/workspace support for native
  breadboard-backed Read, Set, control, math, logic, threshold, constant,
  random, clamp, smooth, forever/comment, and starter-flow blocks.
- Added Smart Builder camera-facing symmetry toggling from the build cursor and
  fixed-geometry face picking/highlighting for non-cuboid preview pieces.
- Added a Smart Builder `Mat only` preview mode that shows material-family
  preview faces without the wireframe overlay.
- Added Smart Builder cursor tooltips for buttons and interactive panel
  controls, matching Decoration Edit Mode and Surface Builder behavior.
- Added a dedicated Surface Builder coordinate workbench in the left panel.
  Points, edges, faces, and generator path points/centers expose construct-local
  X/Y/Z text fields and 0.001 m sliders. The shelf defaults collapsed; sliders
  update geometry live as one undoable drag, and Enter atomically commits exact
  text without a separate Apply button.
- Added live `-step` / `+step` controls on both sides of every coordinate
  slider. Each button displays its configured amount. Right-clicking either
  button opens a per-axis custom step editor with common presets including 1/8,
  1/4, and 1/2 metre values; X/Y/Z steps persist in the ESU profile.
- Added profile-persistent X/Y/Z Surface slider ranges. Each axis defaults to
  `-10..+10`, supports validated custom limits and reset, and temporarily
  expands for out-of-range selected or staged values without clamping geometry
  or changing the saved preference.
- Added Quad, Polygon, and Tube generators to Surface Builder Extra Tools.
  Quad exposes independent width/height, Polygon exposes radius and 3-12 sides,
  and Tube sweeps configurable rings and rails along a multi-point path. The
  three tools share the existing mesh, strut diameter, paint, material, anchor,
  symmetry, preview, placement, history, and rollback paths.
- Added shared profile-backed gizmo settings to Decoration Edit, Surface
  Builder, and Smart Builder. Move/Rotate/Scale size, thickness, and click area
  can be adjusted from the bottom-left gear without dirtying the craft or
  changing transform sensitivity.
- Added Decoration Edit clipboard parity with separate native-compatible
  settings Copy/Paste and repeatable whole-selection Copy selection/Paste in
  place workflows. Group paste creates and selects exact in-place clones as one
  undoable operation.
- Added mirrored Decoration Edit placement and anchor preview hints while
  symmetry planes are active, so the original and mirrored side can be checked
  before committing.
- Added AMUI-style Decoration Edit mass transforms for box-selected
  decorations, including group move, rotate, axis scale, uniform scale from a
  center handle, and typed XYZ group-scale factors through the normal Scale
  fields.
- Added a Decoration Edit group pivot selector for mass transforms, with
  Bounds, Average, Selected decoration, and Selected anchor origins.
- Added Decoration Edit Shift-click additive selection in Single selection
  mode, so decorations can be added to the current selection without box
  dragging.
- Added Ctrl-click toggle and Shift-click range selection to the Decoration
  Edit Selected anchor list.
- Added Decoration Edit multi-selection primary switching, so clicking an
  already selected decoration promotes it without clearing the selected group.
- Added the Decoration cursor action menu to decoration rows in both the
  Outliner and Selected anchor lists. Right-clicked selected rows retain and
  promote the current group, while unselected rows become a safe single target.
- Added a Developer diagnostics setting in the serialization/editor settings
  screen. It is off by default and gates low-level HUD/editor/render-gate
  diagnostic entries so normal players do not fill the ESU log with bug-report
  data.
- Added richer ESU HUD diagnostics for development mode, including active
  editor registrations, input scopes, GUI-frame ownership, last HUD
  force/restore contexts, render-gate coverage, assembly location, and duplicate
  deployed DLL detection.

### Changed

- Surface Builder now budgets the full Extra Tools panel and dynamically shares
  the left-panel height between Draft and Coordinates. Opening, resizing, or
  hiding Coordinates immediately gives all remaining space back to the draft
  list instead of leaving an unused region.
- Paint-color button groups now use the same responsive layout in every ESU
  editor surface that exposes them, while retaining the opt-out legacy grid.
- Clarified the Automation lifecycle throughout the HUD and documentation:
  Check is non-mutating, Apply writes and verifies ESU-owned vanilla components,
  Revert removes only ESU-owned components, markers, and sourced wires, and
  imported vanilla nodes/wires remain visible but read-only—including opaque
  unsupported component families.
- Replaced obsolete Code-page, System Block, nested-workspace, and arbitrary
  native-editor acceptance steps with tests for the implemented Scratch block
  workflow. Those earlier concepts are now explicitly identified as non-features
  rather than missing release controls.
- Expanded the executable regression harness to 627 checks, including Scratch
  block editing, exact native-port preservation, strict marker ownership,
  transaction rollback, imported-component isolation, responsive HUD geometry,
  modal input ownership, paint selection, and native Surface preview colors.
- Polished the complete ESU editor HUD across Decoration Edit, Surface Builder,
  Smart Builder, Automation Builder, notifications, tooltips, and the runtime
  console. Toolbar controls now compact inside their real rail budgets, retain
  the notification/log slot, and expose the same actions through descriptive
  tooltips at high HUD scales.
- Made every editor panel resolve its minimum size against the actual space
  between the toolbar and status strip. Constrained panel bodies, Surface draft
  coordinates/settings, Smart Scene/Selected shelves, and Automation graph
  popovers remain reachable without overlapping the footer or leaving the
  screen at 1366x768/1.44x and 1920x1080/2x.
- Unified panel accents, compact headers, section disclosure controls, hover,
  selected, disabled, and focused-field states. The ESU console now has clearer
  filtered/total counts, empty/disabled states, alternating log rows, and the
  same foreground chrome as the editors.
- Tightened foreground ordering so expanded notifications, context menus,
  graph popovers, close prompts, the console, and delayed tooltips render in a
  predictable stack and do not expose visually covered editor controls.
- Automation Builder now treats the selected native breadboard board as the
  source of truth: links and graph nodes are rebuilt from vanilla
  `GenericBlockGetter` / `GenericBlockSetter` and native board components
  instead of temporary session-only state.
- Automation Builder Apply now validates the visible native graph and connects
  recognized snapped/stacked components idempotently without appending duplicate
  packages.
- Automation Builder only imports and lowers native breadboard components it
  understands, leaving unsupported vanilla breadboard components intact instead
  of guessing a visual block type.
- ESU editor panels now share more of the same responsive top/bottom layout
  limits across Decoration Edit, Surface Builder, and Smart Builder.
- Surface Builder coordinate editing moved out of the cramped bottom transform
  row into an adaptive, internally scrolling shelf above the pinned material
  and action controls. Shelf allocation now follows the visible draft rows
  instead of donating all unused list space to the draft panel, keeping the
  coordinate workbench directly below its actual content. The bottom strip
  keeps its previous height and snap controls and now points players to the
  left-panel workbench.
- Simplified the Surface coordinate header to `Coordinates`, removed the
  redundant New triangle action, added Hide/Show coordinates controls, and
  added a persisted draggable divider for resizing the Draft and Coordinates
  shelves. The automatic split now accounts for the draft list's own viewport
  cap so unused list space is not reserved twice.
- Surface Coordinates now collapses to a persistent bottom header with its
  Show control instead of disappearing. The Draft list expands into the
  released space, while the open divider preserves the real minimum height of
  Draft controls and no longer continuously rescales the list viewport.
- Hovering an A/B/C coordinate header or slider row now highlights that row and
  draws a high-contrast marker around the exact bound Surface or generator
  point on the construct.
- Move, scale, point, decoration-anchor, and shared-anchor gizmos now use the
  same full-shaft picker and hover preview as their rendered geometry. Tool size
  changes affect display and picking together while drag projection remains
  based on the original sensitivity geometry.
- Renamed the Decoration Edit context action from `Duplicate` to `Duplicate in
  place` and routed it through the same transactional one-item creation path as
  whole-selection paste without overwriting either clipboard.
- Trimmed the Decoration cursor menu by replacing `Copy selection` with an
  active-state `Focus deco` toggle above native settings Copy/Paste. Whole-
  selection copying remains available from the Inspector and keyboard
  shortcuts. Group duplication uses deterministic manager ordering;
  group deletion preflights all explicit and mirrored targets before mutation.
- Decoration Edit's configured native Copy/Paste shortcuts (`Ctrl+C` / `Ctrl+V`
  by default) are now selection-aware: an explicit multi-selection copies and
  pastes whole decorations in place, while a single selection retains native
  settings clipboard behavior. `Ctrl+Shift+C/V` remain explicit whole-selection
  shortcuts.
- Smart Builder down-slope scale handles can stretch along the cardinal ramp
  run while preserving the anchored first slope cell.
- ESU mode handoff rendering now lets the newly opened target editor claim the
  handoff frame so passive old-mode GUI does not overlap the new mode.
- Smart Builder now switches the right panel between full `Shapes` and
  `Generators` pages, so procedural generators get the full browser area
  instead of being squeezed under the shape palette.
- Smart Builder now keeps the `Scene` list and scrollable `Selected` piece
  actions in the left panel, with Scene above Selected and independent
  persisted split dividers. The right Shapes/Generators browser now uses its
  full panel height.
- Surface Builder now places `Draw` first with the right-panel Extra Tools
  creation buttons instead of beside the left Draft list. Draw and generator
  buttons now have mutually exclusive active highlighting.
- Right-click context menus now own a modal foreground input layer across
  Decoration Edit, Surface Builder, Smart Builder, and Automation Builder.
  Controls behind a menu cannot consume its clicks, wheel, keyboard, or camera
  input; Automation graph fields likewise yield to graph popups. Their normal
  appearance is retained unless the optional modal HUD fading preference is
  enabled.
- Surface Builder previews now draw the actual planned decoration meshes using
  the selected material and paint color before Apply, while keeping draft
  wireframes and points visible for editing.
- Smart Builder blocked-placement and occupied-cell warnings now use the shared
  ESU HUD notification/log path instead of clipped bottom-right status text.
- The packaged Steam/player readme was synchronized with the current Workshop
  description text.
- Main-menu Alerts entries for ESU now use the same cyan accent color as the
  in-game ESU HUD.
- Decoration Edit Mode scale fields now accept `0` on any axis and allow
  nonzero scale values down to `0.00001`; the scale snap minimum now matches
  that precision.
- Decoration Edit mass-transform pivot selection now lives beside the
  Single/Box/X-ray controls, and the separate group-scale field was folded into
  the existing bottom Scale XYZ editor.
- Decoration Edit now keeps the mass-transform pivot selector visible in the
  selection strip and disables it until multiple decorations are selected.
- Decoration Edit, Surface Builder, and Smart Builder toolbar icons now use
  clearer tool-specific ESU artwork for rotate rings, paint, box select,
  Surface Builder generators, symmetry axes, Cancel, Close, and undo/redo key
  buttons.
- ESU world overlays now cull fully off-screen wireframe lines and preview
  quads before the expensive screen-space width conversion, reducing editor
  wireframe cost across Decoration Edit, Surface Builder, and Smart Builder.
- Updated the ESU change-test checklist with a focused v1.0.7 smoke course for
  Decoration Edit, Surface Builder, Smart Builder generators, and packaging.
- ESU editor vanilla-HUD ownership now follows actual editor registrations and
  per-frame GUI ownership, not only the input scope booleans. Visible ESU editor
  panels therefore keep owning the screen during mode handoffs and passive UI
  frames.
- The scoped vanilla HUD render gate now covers the visible FtD HUD families
  that can draw outside `GuiDisplayBase.displayGUIs`, including build command
  prompts/messages, vanilla toolbar buttons, player/vehicle/resource/status
  rows, camera controls, tooltips, hints, and cHud draw roots.
- ESU now reapplies the hidden vanilla-HUD state after
  `GuiDisplayer.LateUpdate`, matching vanilla F9 ordering while preserving the
  player's original HUD visibility when ESU closes.
- Smart Builder no longer talks about an unsupported selected item when the
  active material or shape family cannot be used.
- Legacy EndlessShapes2 UI source was cleaned up to replace decompiler-style
  names such as numbered console windows and screen segments with descriptive
  local names.

### Fixed

- Fixed Automation ownership on an empty vanilla breadboard where the first
  generated component receives ID `0`. Failed Apply rollback now restores
  targets before markers, rewrites markers after vanilla reassigns IDs, and
  remaps the pending graph journals before verifying exact ownership.
- Fixed untrackable imported-source to ESU-target wires by rejecting mixed
  ownership consistently in graph editing and Apply readiness. Clipboard
  target bindings now clear across constructs, safely rebind across boards on
  the same construct, and repeated Paste/Duplicate operations cascade without
  overlapping their connections.
- Fixed marquee paint leaving blocks outside Decoration undo or Cancel. A
  mixed decoration/block gesture is now one rollback-safe history command,
  sends block restoration through the multiplayer paint RPC path, and caps
  block, cell, visibility-ray, and result work before applying anything.
- Fixed Surface preview caches clearing and recloning every frame above 256
  variants. The cache now admits or evicts individual stale entries, while
  non-readable catalog meshes use a private tinted-material fallback.
- Fixed compact Surface layouts hiding Draft whenever Coordinates opened and
  fixed wrapped Extra Tools summaries clipping above unused space. Coordinates
  now shares an ordered split with Draft, and the complete Extra Tools body is
  one full-height scroll viewport.
- Fixed temporary small-screen Smart Builder constraints overwriting the
  user's preferred Overview/Workspace and Scene/Selected divider ratios.
- Fixed colored Surface triangle previews using a different color conversion
  from committed decorations; preview and placement now resolve the same active
  craft-palette color.
- Fixed Smart Builder panel clicks falling through to the world and placing the
  currently armed preview shape.
- Fixed purely visual cyan panel outlines competing with meaningful hover,
  focus, selection, and action color cues.
- Fixed Tube symmetry rebuilding reflected cross-sections with a different
  winding. Symmetry now mirrors one validated segment batch exactly and applies
  the 100,000-segment limit to aggregate unique output across all variants.
- Fixed near-180-degree Tube turns producing crossed rails. Consecutive path
  directions with a dot product of `-0.999` or less now reject the offending
  point, and required Quad, Polygon, and Tube edges at or below 0.000001 m fail
  atomically instead of leaving partial geometry.
- Fixed Quad uniform scaling drifting from its starting aspect ratio and allowed
  both dimensions to share one snapped, minimum-clamped scale factor.
- Fixed modal world and Automation graph popups leaking clicks, wheel, keyboard,
  divider drags, and console input into disabled background controls.
- Fixed high HUD scales preserving a Smart Builder minimum height larger than
  the toolbar-to-status budget, which could move Apply/Cancel off-screen.
- Fixed viewport right-click in Decoration Edit consuming the click to disable
  Box mode before the selected decoration group could open its action menu.
  Completed Box selections now get the full group-aware menu first; active Box
  drags still cancel safely and empty-space right-click retains the Box-off
  fallback. The projected group gizmo and selected bounds also act as safe
  context targets when no individual decoration center is close enough.
- Fixed right-clicking a selected Outliner or Selected anchor row running the
  row's normal primary-click handler first and collapsing the multi-selection.
  Secondary clicks are now consumed before normal row selection.
- Fixed right-clicking a Surface coordinate `-step` / `+step` button running
  its normal increment/decrement action instead of opening the step editor.
- Fixed extreme Coordinates divider positions allowing Draft list controls and
  hint rows to overflow into the coordinate shelf.
- Fixed Automation Builder links not persisting through mode exits by creating
  vanilla-compatible getter/setter components immediately on the selected
  breadboard.
- Fixed Automation Builder graph blocks being invisible or failing to drag/drop
  on an empty graph by adding native components directly at click/drop time and
  hardening drag cleanup.
- Fixed exact block targeting by auto-naming unnamed or ambiguous linked blocks
  with stable `ESU_AB_*` names and rejecting generated-name collisions.
- Fixed broad/type-only vanilla getter/setter targets being treated as missing
  targets when their native component still resolves a live block.
- Fixed unsupported vanilla evaluator/components being misclassified as ESU math
  blocks during graph sync and Apply.
- Fixed the global floating ESU notification overlay showing inside Automation
  Builder; messages now stay in the shared toolbar/log while the editor is
  active.
- Fixed the Automation Builder apply-and-close prompt so stale breadboards or
  readiness failures keep the prompt open instead of silently discarding dirty
  graph state.
- Fixed a fast blueprint load crash when optional timing patches encountered
  generic construct modules such as module external linkup handlers, and stopped
  installing the optional per-module linkup patch during boot.
- Fixed Decoration Edit's options/native UI toggle so closing through the UI
  uses the same unapplied-change prompt/apply/discard flow as the hotkey.
- Fixed Decoration Edit and Smart Builder snap settings so pressing Set
  explicitly saves the ESU profile instead of relying on later profile writes.
- Fixed Move/Scale/Anchor arrows being difficult to acquire except near their
  tips. The nearest visible axis can now be selected across the full shaft,
  collapsed or invalid projected axes no longer disable valid axes, and the
  oversized free-move region was reduced to a compact center target.
- Fixed Surface coordinate entry mutating or partially invalidating editor
  state on bad input. All affected points/faces are now validated before one
  atomic update; stale indexes, non-finite/overflowed math, repeated or
  zero-length edges, zero-area faces, duplicate triangles, and cross-construct
  targets leave draft, selection, preview, pending points, and history intact.
- Fixed coordinate slider history and Revert semantics for live editing. A
  complete mouse drag records one command, each step click records one command,
  rejected intermediate positions remain non-mutating, and Revert restores the
  target coordinates captured when it was selected without rolling back other
  Surface settings.
- Fixed decoration clipboard batch failures leaving partial settings or clones.
  Settings paste and whole-selection paste now preflight all targets/capacity,
  preserve tether/runtime identity where required, and reverse-clean the full
  batch on failure.
- Fixed Smart Builder fixed-geometry preview edge filtering so angled mesh
  handles are not mistaken for removable internal axis-aligned faces.
- Fixed Decoration/Smart Builder toolbar icons regressing to generic boxed
  glyphs or unsafe runtime texture matches; editor buttons now use
  ESU-owned generated icons, including proper cube, axis, save, cancel, and
  delete artwork.
- Fixed Smart Builder generator resizing so circles, polygons, and spheres
  scale from their stored generator dimensions instead of their current
  rasterized cell hull; round-locked generators now resize once from the
  dragged handle and keep the forced radii centered.
- Fixed large Smart Builder generated previews causing heavy frame drops by
  caching generated hull geometry, merging adjacent wireframe edge runs, and
  budget-sampling very large wire/material overlays.
- Fixed Decoration Edit symmetry deletion so deleting a decoration while
  symmetry is active also deletes its mirrored counterpart decorations in the
  same undoable operation.
- Fixed mirrored anchor movement visibility and history handling so symmetry
  anchor edits show the counterpart anchor and keep undo/redo grouped.
- Fixed ESU undo/redo input so remapping away from `Ctrl+Z` / `Ctrl+Y` does not
  leave the hardcoded defaults active.
- Fixed the forwarded vanilla Freeze input so a remapped Freeze key no longer
  keeps Caps Lock active after the profile key map is loaded.
- Fixed the Workshop update notifier so stalled Steam detail requests time out
  and transient failed or empty callbacks retry instead of suppressing update
  checks until restart.
- Fixed ESU paint color swatches and preview tints to read the active craft's
  paint palette instead of a hardcoded color table.
- Fixed Surface Builder material previews on spin-block and subobject
  constructs so normal-flipped generated surfaces use the subobject's local
  position and rotation without inheriting transform scale or shear.
- Fixed Surface Builder context actions and same-anchor guide overlays
  implicitly entering material preview mode after Create Face, shared-anchor
  edits, or helper redraws.
- Fixed Surface Builder normal-flipped split surfaces double-applying the
  normal reversal during ES2 polygon conversion, which could corrupt placed
  decorations on angled spin-block or subobject constructs.
- Fixed Decoration Edit paint color changes after box selection so swatch,
  typed color, and paint-brush clicks recolor the whole selected decoration
  group in one undoable batch instead of only the primary decoration.
- Fixed Decoration Edit box-selection overlays so selected decorations sharing
  the same anchor also draw their anchor-to-decoration guide lines.
- Fixed vanilla HUD leakage under Decoration Edit, Surface Builder, Smart
  Builder, and Automation Builder. Holding or pressing F9 while an ESU editor is
  visible should no longer reveal the vanilla left key prompt stack, build
  messages, paint tooltip, toolbar, right-side vehicle/resource/status rows, or
  other vanilla HUD roots behind ESU panels.
- Fixed the specific state where ESU panels remained visible while
  `should_hide_vanilla_hud=false` because the editor input scope had ended or
  moved through a mode-switch handoff.
- Fixed ESU HUD diagnostics and render-gate startup checks so future FtD HUD
  draw-root renames fail loudly in verification instead of silently leaking a
  vanilla layer in-game.

### Removed

- Removed obsolete development-only debug code: the old loader debug gate, the
  one-off header logger, and the temporary Harmony debug patch file.
- Removed the unused Smart Builder selected-item resolver and its stale catalog
  helpers now that Smart Builder uses the material/shape source pipeline.
- Removed unused EndlessShapes2 polygon helper/proxy members that were no longer
  called by ESU's importer or editor flows.

### Packaging

- Clean deploy now replaces the destination mod folder and copies only runtime
  files: top-level ESU/Harmony DLLs, manifests/readmes/licenses, assets,
  character items, items, and meshes.
- Runtime package verification now rejects nested ESU DLLs, unexpected DLLs,
  `Source`, `bin`, `obj`, PDBs, and other build/development artifacts in the
  deployed mod package.
- Removed the stale packaged `Version 1.1.0` release-list entry while keeping
  the active release line at `1.0.7`.
- Added a local `*.log` ignore rule so verifier logs do not show up as
  accidental repository changes.
- Expanded Release verification with strict ordering, cached Surface coordinate
  bindings, Tube mirror/cap/reversal boundaries, modal event ownership,
  responsive toolbar/Smart/Automation panel geometry, transactional clipboard
  paths, and list-opened multi-selection decoration actions. The authoritative
  v1.0.7 total is 627 checks as documented above.

## 1.0.6 - 2026-07-04

### Added

- Added a Decoration Edit Mode viewport right-click context menu for the
  decoration under the cursor. It can select, switch to Move, Rotate, Scale, or
  Anchor, duplicate, hide/show the anchor mesh, or delete the decoration.
- Decoration context-menu deletion now participates in undo/redo and Cancel
  restores deleted existing decorations before leaving the edit session.

### Changed

- Smart Builder fixed-shape scene wires now use the real FtD item mesh hard
  edges instead of an aggregate cuboid outline. Shared internal faces are
  filtered so repeated/tiled preview shapes do not draw inner seams.
- Decoration Builder Mesh Palette 3D grid now uses the same adaptive card
  sizing behavior as Smart Builder's Shapes 3D grid.
- Smart Builder Shapes panel sizing now uses the available panel space more
  aggressively, and the 3D grid palette adapts its columns, card size, preview
  image size, and scroll bounds to the current panel width.
- Surface Builder Extra Tools now sizes its scroll area and material picker from
  the live panel viewport instead of a fixed list height.
- Surface Builder Extra Tools now opens the material override list by default.
- Surface Builder paint color is now shared across Draw and Extra Tools, tints
  previews before placement, and can be assigned per surface face with the face
  color shown in the draft list.
- Surface Builder bridge selection now reuses an already selected edge as the
  first bridge edge, so only the second edge needs Shift-clicking.
- ESU mode hotkeys now treat the profiled vanilla settings binding as the
  authoritative input once loaded, so remapping actions such as Toggle
  Decoration Edit Mode away from `Ctrl+D` works across sessions.
- Surface Builder Extra Tools generated placements now orient from the clicked
  block face normal rather than the camera direction.
- The packaged Steam/player readme was rewritten into a shorter
  Workshop-friendly BBCode description.
- Release-channel documentation now treats
  `[b]Mod latest version X.Y.Z[/b]` as the preferred Workshop update line while
  keeping support for the older plain-text form.

### Fixed

- Fixed the Decoration Edit Hide mesh button so it hides only the selected
  decoration's anchor/tether block mesh, matching the advanced mimic UI, instead
  of switching the whole craft to decoration-only view.
- Fixed the Decoration/Surface View submenu draw order so it renders in the
  foreground over editor panels and the ESU console.
- Fixed ESU Console and Log foreground priority in Smart Builder so it matches
  Decoration Edit and Surface Builder behavior.
- Fixed Smart Builder fixed-shape previews, such as wedges, corners,
  transitions, facing slopes, and offset slopes, drawing as cuboid wireframes.
- Fixed Smart Builder 3D grid palette cards staying fixed to a narrow two-column
  layout when the Shapes panel had more available width.
- Fixed packaging so the developer-only Smart Builder HUD notes are no longer
  included in the shippable mod folder.
- Fixed Workshop update checks failing to parse BBCode-wrapped
  `Mod latest version` lines.

## 1.0.5 - 2026-07-03

### Added

- Added explicit shared-anchor picking for Surface Builder same-anchor mode.
  Surface Draw drafts and Extra Tools generator drafts can now pick one craft
  block as the shared anchor, select it independently, and move it before
  applying.
- Added Surface Builder Extra Tools generators for partial circles, 2D cone
  sectors, cones, frustums, spheres, and partial spheres.
- Added Smart Builder shape palette list/grid previews with categorized filters,
  searchable rows, size buttons, and visible-only 3D thumbnail rendering.
- Added Smart Builder draggable split dividers for Palette, Selected, and Scene
  sections.
- Added a Hide Original Mesh view toggle to the ESU bottom controls.
- Added finer transform snap settings and live snap-setting application so
  values such as `0.625` can be typed without pressing Set every time.
- Added extended decoration scale bounds so ESU scale editing and generated
  placements can exceed the old 10x limit when needed.

### Changed

- Surface Builder preview/apply validation now uses Unity-compatible Euler and
  LookRotation behavior so non-axis-aligned manually-created and bridged faces
  keep strict normal validation without false rejections.
- Surface Builder bridge creation now reuses the normal face winding checks,
  validates conflicts between both new bridge faces, and tries alternate quad
  bridge diagonals when the preferred pairing duplicates or conflicts.
- Surface Builder Shift-click point workflows remain explicit: two selected
  points can become an edge seed, and three selected points create a face from
  the context action.
- Smart Builder world previews now draw hard/boundary mesh outlines and
  aggregate piece hulls instead of internal triangle diagonals and repeated
  placement seams.
- Smart Builder `1` shortcut now cycles only shapes with a 1m candidate and
  forces the selected size back to 1m. Size buttons remain the way to choose
  larger variants.
- Smart Builder material changes now clear preview caches before rebuilding
  palette thumbnails and scene previews.
- Smart Builder right-click now cancels active edits, cancels Add cursor ghosts,
  or deselects the active preview piece before opening piece context menus.
- Smart Builder panel sizing now follows the same resizable split-panel pattern
  as the other ESU editor modes.

### Fixed

- Fixed Surface Builder bridge previews rejected with transform-normal mismatch
  errors on screenshot-like slanted roof triangles.
- Fixed Smart Builder list mode hover previews not appearing.
- Fixed Smart Builder 3D previews disappearing after switching material.
- Fixed Smart Builder grid mode rendering off-screen thumbnails.
- Fixed shared-anchor handle clicks moving the anchor by one block even when the
  mouse had not crossed a block movement threshold.
- Fixed generator same-anchor overlay trying to build a generator preview when
  only an explicit shared anchor had been selected.

### Notes

- Steam Workshop descriptions now use a `[b]Mod latest version 1.0.5[/b]`
  line so installed copies can check whether a newer Workshop version exists.

## 1.0.4 - 2026-07-01

### Added

- Added Vanilla Compatibility Mode, default ON, to block decoration creation and
  saves that would exceed vanilla-compatible decoration data.
- Added an ESU Options toggle for Vanilla Compatibility Mode.
- Added save-time checks that reject actual converted blueprint output when it
  contains ESU sentinel containers or requires ESU-only serializer buffers.
- Added load warnings for existing sentinel craft and over-5,000-decoration
  craft while still allowing those craft to load.
- Added Serialization HUD compatibility status text.
- Added separate HUD wording for serializer wire format versus vanilla load
  compatibility.
- Added a separate Vanilla edit limit HUD status so legacy-wire
  over-5,000-decoration craft are shown as over the vanilla decoration-tool cap
  without claiming vanilla cannot load them.
- Added verifier coverage for compatibility defaults, options UI, save/load
  guard behavior, creation guards, load/cleanup suppression, and unchanged
  legacy/sentinel serializer selection.
- Added an opt-in Blueprint loading options section with `Off`, `V1`, `V2`,
  and `V3` fast-load tiers.
- Added V1 streamed blueprint JSON loading for large `.blueprint` files to avoid
  loading the whole JSON document into one string.
- Added V2 parallel `BlockData` container predecode with serial main-thread
  block-data application in original block order.
- Added passive fast-load diagnostics and a small-blueprint testing override,
  both default OFF.
- Added diagnostics-only V3 collider timing rows that report per-method
  `call_count`, total elapsed time, maximum single-call time, and parent/child
  unaccounted collider time for huge-craft load analysis.
- Added a fast blueprint loading developer guide documenting V1/V2/V3 routing,
  diagnostics, unsafe probes, and vanilla-schema compatibility.
- Added a global ESU notification overlay so Vanilla Compatibility Mode
  blocked-save errors are visible in game instead of only appearing in logs.
- Added Smart Block Builder structural shape descriptors and a categorized
  shape palette for blocks, poles, slopes, wedges, corners, and transitions.
- Added fixed-geometry Smart Builder planning that uses FtD `SizeInfo`
  footprints for preview, collision, symmetry, and commit coverage.
- Added a coarse ESU change test checklist for future build, verifier,
  packaging, HUD, save/load, Surface Builder, and Smart Builder smoke tests.
- Added a Steam Workshop update notifier that can show a non-error main-menu
  mod status row when the Workshop description advertises a newer version.
- Added this changelog.

### Changed

- ESU decoration creation paths now preflight the vanilla 5,000-decoration
  manager cap when Vanilla Compatibility Mode is enabled.
- Compatibility warnings now explain that over-5,000-decoration craft can still
  be legacy wire and may load in vanilla, while vanilla decoration tools remain
  capped/limited above 5,000 per manager.
- Surface Builder, Path/Circle generation, mesh placement, OBJ Decoration
  Builder generation, symmetry placement batches, and redo recreation now reject
  incompatible creation before partial batches are committed.
- Blueprint loading remains fully vanilla by default. Fast-load routing only
  activates for large blueprints, or for small blueprints when the explicit
  testing override is enabled.
- V3 wording now presents it as the recommended opt-in mode for huge
  block-count craft while keeping the default load path vanilla.
- Fast-load route diagnostics now avoid the ambiguous conversion-local
  `file_bytes=-1` field; conversion-only traces log
  `conversion_file_bytes_known` and `conversion_path_file_bytes` instead.
- Surface Builder mirrored triangle placement now preserves intended committed
  normals for right, isosceles, and scalene faces across symmetry variants.
- Surface Builder right-click context menus now cover point, edge, and face
  targets for conservative preview, bridge, select, and delete actions.
- Smart Builder fixed structural shapes now repeat whole items on voxel
  strides instead of distorting block geometry, and handed shape mirrors swap
  to their left/right counterpart when odd-axis symmetry requires it.

### Notes

- Existing sentinel craft are not automatically converted back to vanilla data.
  They must be reduced/repaired and saved again while compatible.
- The serializer still chooses legacy versus sentinel from the actual serialized
  header/data byte sizes. The compatibility mode is a guard around creation and
  saving, not a new save format.
- The Steam/player readme now distinguishes `Wire bytes`, `Vanilla load`, and
  `Vanilla edit limit` compatibility.
- Steam Workshop descriptions now need a `Mod latest version 1.0.4` line so
  installed copies can check whether a newer Workshop version exists.
- Fast blueprint loading does not change the `.blueprint` schema, does not write
  sidecar files, and keeps V3 conservative by falling back unless bulk loading
  can be proven safe.
- Tester data from a 3.8 million block craft showed vanilla loading at about
  `2.5 hours` and V3 loading at about `21 minutes`; active fast-load
  optimization is paused in favor of documentation and polish.

## 1.0.3 - 2026-07-01

### Added

- Added Steam Workshop preview image packaging through `header.jpg`.
- Added GitHub/Steam release-channel documentation.
- Added explicit release workflow documentation for packaging, scanning,
  publishing, and Workshop upload preparation.
- Added Steam Workshop/player readme text synchronized with the live Workshop
  description.

### Changed

- Prepared the first Steam Workshop release.
- Refreshed the GitHub release package and release body to mention the first
  Steam Workshop release.
- Scrubbed public release artifacts and bundled binaries for local-path/privacy
  leaks.
- Clarified the difference between the GitHub technical readme and the packaged
  Steam/player readme.

### Packaging

- GitHub release tag: `v1.0.3`.
- Runtime package should contain only the files listed in
  `RELEASE_CHANNELS.md`.

## 1.0.2 - 2026-07-01

### Added

- Added the expanded ESU editor documentation pass.
- Added Steam Workshop player-readme compatibility notes.
- Added number shortcut cycles for ESU editors:
  - `1`: create/select family.
  - `2`: transform family.
  - `3`: display/preview family.
- Added Surface Builder unification work so Surface, Path, and Circle share the
  left draft/action panel.
- Added Smart Builder and Decoration Edit Mode UI polish checks in the verifier.

### Changed

- Surface Builder draft/settings layout was tightened.
- Extra Tools was changed toward settings-only behavior for Surface Builder.
- Smart Builder panel height and controls were polished.
- Outliner selection behavior was expanded toward normal Shift/Ctrl
  multi-select.
- Paint/color/material UI behavior was improved across ESU panels.

### Fixed

- Fixed several Surface Builder preview/apply and bridge-placement bugs.
- Fixed circle placement orientation to use clicked surface normals instead of
  camera perspective.
- Fixed down-slope preview/transform issues in Smart Builder.
- Fixed ESU number-key shortcuts so they do not leak into vanilla build input
  while an ESU editor is active.

### Packaging

- `v1.0.2` was an intermediate GitHub packaging/release-prep version. The final
  public release line moved to `v1.0.3`.

## 1.0.1 - 2026-06-27

### Added

- Added large Decoration Edit Mode, Surface Builder, and Smart Builder workflow
  polish.
- Added ESU runtime console/log support.
- Added cursor tooltips, toolbar notifications, and panel layout scaling.
- Added multi-piece Smart Builder scenes and material/wireframe preview work.
- Added Decoration Generator Path/Circle planning and shared symmetry support.
- Added broader verifier coverage for ESU editor workflows.

### Changed

- Refined Smart Builder preview controls and connected preview apply flow.
- Improved pointer probing, mesh placement, surface planning, and editor input
  ownership.
- Improved editor UI layout and native-style controls.

## 1.0.0 - 2026-06-27

### Added

- First combined EndlessShapes Unlimited public repository release candidate.
- Combined DecoLimitLifter-style extended decoration serialization with
  EndlessShapes2 OBJ import/export and decoration-building tools.
- Added the technical GitHub readme and simplified packaged player readme.
- Added build and verification tooling around the runtime package.

### Changed

- Slimmed the public repository contents to remove local investigation files,
  copied/decompiled game assets, and private development notes.
- Defined the runtime package layout used by GitHub releases and Steam Workshop
  upload preparation.

## Imported Legacy Notes

These entries are retained from the legacy runtime `releases` file for context.
They may predate the current GitHub tag history or refer to upstream/pre-combine
work.

- 2025-11-01: First public release.
- 2026-06-27: Serializer and Harmony reliability fixes were recorded as
  `1.1.0` in legacy notes.
