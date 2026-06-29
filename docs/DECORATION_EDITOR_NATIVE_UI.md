# Decoration Edit Mode native UI pass

EndlessShapes Unlimited uses a native-looking FTD overlay for Decoration Edit
Mode rather than a separate Unity-style window. The editor keeps the current
safe transaction model: edits preview live, **Apply** commits, and **Cancel** or
closing the mode restores the original decoration fields.

## Runtime layout

- Top toolbar: ESU mode indicator/switch, Select, Move, Rotate, Scale,
  Global/Local transform orientation, Anchor with a compact follow-anchor
  dropdown, Paint/Material, View mode, panel toggles, Undo, Redo, Apply,
  Cancel, and Close.
- Right panel: outliner for main construct, subconstructs, optional tether
  groups, decorations, and selected-anchor context below the outliner.
- Left mesh palette: item/object mesh search, category filters, recent meshes,
  draggable placement panel, searchable list mode, lazy 3D preview-grid mode,
  foreground hover preview clipped to mesh rows only, and a compact Inspector
  below the mesh list.
- Bottom status strip: active tool, selected decoration, dirty state,
  decoration counts, manager maximum, and serialization forecast from the
  existing ESU telemetry model.

Decoration Edit Mode and Smart Builder share the `EsuHudLayout` scaling helper.
The ESU options screen exposes automatic editor scaling, a manual scale
multiplier, and a reset-layout command. The mesh palette, right outliner/anchor
panel, and Smart Builder left panel have resize grips and are clamped back into
the current game window when the player changes resolution or scale.

The pass remains single-active-selection. Internally the selection is stored as
a set so drag-box and true multi-select can be added later without replacing the
UI or transaction model.

Mesh selection follows a KSP-style placement model. Clicking a mesh in the left
palette attaches that mesh to the pointer as a ghost preview. The ghost snaps to
the currently pointed craft block, shows valid/invalid feedback, and creates a
new decoration anchored to that block on left click. Right click or Esc cancels
placement. The row-level **Set** action still swaps the selected decoration's
mesh.

Undo/redo is scoped to the open editor session. `Ctrl+Z` and the toolbar Undo
button undo un-applied editor actions; `Ctrl+Y`, `Ctrl+Shift+Z`, and the toolbar
Redo button redo them. Drag moves are coalesced into one command per drag, and
Apply intentionally clears editor history after committing the current preview.
`Tab` cycles Decoration Edit Mode, Surface Builder, and Smart Block Builder in
that order only when the current ESU mode is clean; live edits require explicit
**Apply** or **Cancel** first.

Hovering ESU panels does not claim camera/WASD input. ESU-owned scroll events do
claim FTD's mouse-wheel-in-use flag so palette/outliner/detail scrolling does
not also zoom the build camera. While ESU owns the HUD, Caps Lock freeze is
forwarded to vanilla build mode if FTD does not consume it on the key frame.
Selection, transform, and anchor overlays use ESU's unlit editor drawing path so
their RGB colors remain stable when camera angle or scene lighting changes.

The toolbar **Anchor** button opens anchor settings while selecting the Anchor
tool. Anchor follow can be toggled there. When enabled, moving a selected
decoration can retether it to the nearest valid craft block once its visual
center is at least the configured distance from the current tether. The
retether preserves the visible decoration position by recalculating the local
decoration offset, and remains part of the current undoable transform edit.

## View modes

The toolbar **View** button offers these editor view modes:

- **Mixed**: default editing view. FTD decoration wireframe is enabled, blocks
  are lightly dimmed, and non-selected decorations get low-intensity outlines.
- **Wireframe**: FTD decoration wireframe remains enabled, block dimming is
  lighter, and decoration outlines are brighter for tracing dense builds.
- **Deco only**: strongest safe isolation. Blocks are heavily de-emphasized by a
  screen-space dim layer, selected/nearby decorations are highlighted harder,
  and FTD decoration wireframe remains enabled.
- **Mass**, **Drag**, **Cost**, **Surface**, and **Important**: bridge into FTD's
  native special build visualisations where available while keeping ESU handles
  and selection overlays active.
- **Normal**: ESU removes its dimming and returns FTD decoration wireframe to the
  player's pre-editor value while keeping selection/handle overlays available.

View mode preserves FTD's original decoration wireframe setting, enables a safe
modal cursor/key state while the editor is open, and restores both on every exit
path. It does not disable block renderers. Instead, it uses safe screen-space
dimming plus decoration outlines, center markers, anchor lines, and XYZ handles.

This deliberately avoids full block invisibility in the first native pass
because FTD renderers are not uniform across main constructs, subconstructs,
special blocks, and other mods.

## FTD UI element icon catalog

The icon list was sourced from:

`From_The_Depths_Data/StreamingAssets/Mods/UI/Ui Elements`

ESU references these by name/GUID at runtime and falls back to generated
in-memory ESU icons if the running FTD build does not expose a matching loaded
texture. ESU does not package copied FTD texture files.

| ESU use | FTD UI element | GUID |
|---|---|---|
| Open editor | `editButton` | `8d3109e4-19bc-454b-9691-1432a54c99f4` |
| Build/add | `buildIcon` | `dfbc8eb5-8b53-4fb6-a7da-7b93ec46ec2c` |
| Create | `create` | `e70fd358-3df2-4fea-9cf9-af1e310df610` |
| Select | `crosshair` | `f417ee2c-2aa4-4fb2-ab9f-de4c59b94e45` |
| Move | `move` | `68419445-57e1-41ac-89c9-7683976ddcff` |
| Axis | `axis` | `2ba07384-f67f-48a9-897c-901a51c690f3` |
| Three-axis transform | `threeAxis` | `2754a2f9-2523-4837-a67f-a3a247aede97` |
| Rotate placeholder | `spin` | `c74e78ba-0d66-4b7c-932f-47cf9bc48bc3` |
| Scale placeholder | `scales` | `a243302c-f2ea-4f82-b937-393ac3036009` |
| Anchor/tether | `pin` | `6d2363fd-760c-4b3f-b4b6-a1803d1ede2c` |
| Paint | `paint` | `d1d370f1-f5c8-47cc-9471-15274e4c474c` |
| Brush | `brush` | `2349b49b-74ba-4ba1-986d-ea0ce6db383e` |
| Material | `materialfull` | `bfed1af7-09ac-441e-9352-2c9346dcde2d` |
| Default material | `materialEmpty` | `e5bdbd61-0a61-4f8d-ac3b-bb30b0314600` |
| Visibility | `standardEye` | `c403c4cc-ed2a-4041-a100-1a32151d5d24` |
| Focus | `focus` | `2616f6ef-894b-4075-b49a-d6309fb57c7c` |
| Focus camera | `Focus camera` | `2c10cd31-99ec-44cb-b25a-3eaaf8d8d55f` |
| Duplicate | `duplicate` | `3aef4997-5045-48b2-909b-006a0b0c0713` |
| Delete | `delete` | `157d2a08-9ec3-4ca7-8fb0-21ca7cd780e9` |
| Cancel | `cancel` | `f71a9e09-53e0-4e2d-bdae-512705b4e72c` |
| Apply/save | `save` | `62b709ba-5c66-452e-a613-0d7d7292881b` |
| Settings | `cogs` | `ee0feae4-f36b-451e-b30d-b159aca123a2` |
| Gear | `gear` | `a13af71c-7120-4588-b908-abbd16b78e13` |
| Camera | `camera` | `ef38a3c1-69d3-427e-b3bc-cfebc0d5ed3b` |
| Mirror | `mirror` | `582c17b5-9372-4247-933b-9b4568242c38` |
| Chevron 1 | `Chevron1` | `45cc3ab4-2ae0-4103-bb57-101428306377` |
| Chevron 2 | `Chevron2` | `8a970688-b2ce-4d8c-ac7e-cdd3e64e385b` |
| Chevron 3 | `Chevron3` | `55f85f3a-333d-4992-be06-611912522128` |

## ESU-owned fallback icons

The following are generated in memory and are safe for ESU to package/use:

- outliner
- filter
- dirty state
- serializer risk
- lock/unlock
- decoration count
- generic mesh

They are simple pixel glyphs created by `DecorationEditorIconCatalog` and are
not copied from FTD.

## Deferred work

- true multi-select and drag-box selection;
- per-decoration visibility/lock toggles;
- full block invisibility.
