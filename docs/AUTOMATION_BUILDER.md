# Automation Builder

Automation Builder is ESU's Scratch-style editor for vanilla From the Depths
breadboards. It presents native breadboard components as readable blocks, but
the applied result remains ordinary FtD board data. ESU does not install a
parallel automation runtime, add a craft-save schema, or require ESU to execute
the program after it has been applied.

Open Automation Builder with `Ctrl+Shift+A`, select or place a breadboard, and
open its block canvas with `E` or **Open blocks**.

## Work-in-progress warning

Automation Builder is still work in progress and may be very buggy. On first
use for each player profile, ESU opens a blocking warning before the editor can
accept world, panel, or graph input. Acknowledging **I understand - continue**
persists that acknowledgement in the ESU profile so the warning is shown once.
If the acknowledgement cannot be saved, ESU reports the failure and may show
the warning again on the next open.

Back up important craft before using this editor and verify every applied result
in FtD's vanilla breadboard editor. The warning does not weaken the ownership,
validation, rollback, or imported-component preservation rules documented
below.

## Supported controllers and targets

The explicit controller adapters are:

- the vanilla basic `BreadBoard` block and its `FtdBoard`;
- the vanilla `AiBreadboard` block and its `FtdBoard`; and
- another block type only when it exposes a compatible `FtdBoard` through a
  `Board` property.

The placement palette resolves the loaded basic and AI breadboard item
definitions. If the basic definition is unavailable, the UI reports its AI
fallback instead of pretending a separate basic item was found.

Other construct blocks can be linked as signal targets when FtD's native
`GenericBlockGetter` or `GenericBlockSetter` advertises a readable or writable
property for that block type. Property availability therefore follows the
installed FtD version and the selected target; ESU does not invent proxy
properties.

ACBs, ACB Controllers, Missile Breadboard Controllers, arbitrary reflected
controllers, and custom component families do not have dedicated editors in
this workflow. A block from one of those families is usable as a linked target
only when the normal native getter/setter surface supports it.

## End-to-end workflow

1. Select an existing breadboard, or arm **AI**/**Basic** placement and place it
   through FtD's normal block command path.
2. Create world links. Select a target and then the breadboard for an input
   link; select the breadboard and then the target for an output link. The left
   panel and colored world lines show the resulting direction.
3. Open the block canvas. Drag blocks from the categorized palette, or use
   **Starter Flow** when exactly one readable input and one writable output are
   linked.
4. Bind every Read/Input Getter and Set/Output Setter to a linked target and a
   property. Edit constants and native settings in the block or selected-block
   inspector.
5. Use **Check** to inspect the native plan without changing the breadboard.
6. Use **Apply** to validate, lower, connect, and verify the native graph.
7. Close and reopen the canvas, then save/reload the craft to confirm the native
   round trip.

The starter stages this supported shape: Read -> Below Threshold -> If True ->
Set, with a threshold of `10`, a then value of `45`, and an else value of `0`.
It is a starting point, not a hidden runtime template.

## Block-to-native mapping

| Palette | Scratch-style block | Vanilla native data |
| --- | --- | --- |
| Inputs | Input Getter / Read | `GenericBlockGetter` |
| Outputs | Output Setter / Set | `GenericBlockSetter` |
| Logic | Forever | ESU-marked native `Comment` used for layout identity |
| Logic | If True | `Switch` with threshold `0.5` |
| Logic | Switch > Threshold | `Switch` with an editable constant threshold |
| Logic | Not, And, Or, Xor, Nand, Nor, Xnor | `LogicGate` with the matching operation |
| Logic | Above Threshold, Below Threshold | `FuzzyThreshold` with the matching direction |
| Math | Add, Subtract, Multiply | `Evaluator` using the corresponding supported expression |
| Math | Max, Min | `MaxMin` with the matching operation |
| Math | Clamp | `Clamp` |
| Math | Delay | native `Delay` |
| Values | Constant | `ConstantInput` |
| Values | Random | `RandomInput` |
| Notes | Comment | native `Comment` |
| Imported | Advanced Native | an unsupported vanilla component shown as an opaque, read-only block |

Every block available from the six palette tabs has a native lowering path.
“Advanced Native” is not a palette promise: it is the safe visual
representation of an existing component that Automation Builder does not
understand.

## Stack, value, and body semantics

The canvas uses Scratch-like shapes and snapping, but their meaning is native
dataflow:

- **Stack** snapping connects one native component's output to the next
  component's primary input. For a Switch, the component above feeds its
  `Switcher` input. A block cannot use both the stack input and its primary
  value socket at the same time.
- **Value** snapping connects a compact value-producing block to a named native
  input. Slots backed by native scalar settings accept a Constant and copy its
  value into that setting: Switch threshold/fail, threshold level, Clamp
  minimum/maximum, and Delay seconds. Other sockets become native component
  connections, such as Switch pass, logic B, evaluator B, Max/Min B, or a
  Setter value.
- **Body** snapping groups a downstream chain inside Forever or a Switch.
  During lowering, direct Switch body children are flattened into the native
  dataflow chain so the Switch output feeds the first child and siblings remain
  chained. A Forever body keeps its child chain together visually but does not
  create a wire from the marker Comment.
- **Free** blocks remain separate roots. Comment blocks are notation and do not
  participate in value or stack flow.

Moving, copying, duplicating, or nudging an editable block carries its attached
editable value blocks, direct body descendants, and following stack. Imported
read-only nodes form a fixed boundary. An occupied value socket rejects a
second block rather than silently replacing the first.

### Forever is grouping, not a loop

Vanilla breadboards already evaluate continuously. Forever therefore does not
compile to an ESU loop or scheduler. Apply writes a specially marked native
Comment so the control container can be reconstructed after reopening, while
the blocks inside it remain a normal native component chain. Removing ESU does
not stop an applied child chain from evaluating.

### Switch is numeric dataflow, not imperative branching

**If True** and **Switch > Threshold** both lower to a vanilla `Switch`:

- the incoming stack signal drives `Switcher`;
- If True uses a fixed `0.5` threshold;
- Switch > Threshold uses its constant threshold setting;
- the required then/pass socket supplies the pass value;
- the else socket supplies the constant fail value, defaulting to the value in
  the block when no Constant is socketed; and
- the Switch's selected numeric result feeds the next stack block or the first
  block in its body.

The body caption “driven actions” is an authoring aid. It does not mean that FtD
conditionally executes statements. It means those downstream native components
receive the Switch's selected then/else value.

## Check, Apply, Revert, discard, and undo ownership

| Action | What it owns |
| --- | --- |
| **Check** | Builds readiness diagnostics and a native create/update/remove/connection plan. It does not mutate the board. |
| **Apply** | Creates staged native components, records ESU ownership marker Comments, applies pending edits to ESU-owned components, resolves getter/setter targets and properties, and rebuilds connections whose destination is ESU-owned. It verifies the result and rolls back a failed apply. |
| **Revert** | From a clean draft, atomically removes only components identified by exact ESU ownership markers, those markers, and native input wires sourced from the removed ESU components. It does not delete unrelated vanilla components. |
| **Close anyway** | Discards staged blocks, staged links, pending ESU-owned edits/removals, and the current dirty draft, then reloads the unchanged native board. |
| **Undo / Redo** | Restores up to 64 in-canvas edit snapshots for the current graph, including staged blocks, links, bindings, layout, and pending ESU-owned edits/removals. It does not undo a completed native Apply. |
| FtD block undo | Owns world block placement/deletion commands. It is separate from the canvas history. |

Closing the editor while dirty offers **Apply and close**, **Close anyway**, and
**Keep editing**. Enter chooses Apply and close; Escape returns to editing. A
completed Apply resets the graph history to the newly imported native state.
Use Revert—not canvas Ctrl+Z—to remove generated native components after Apply.

## Editing controls

World HUD shortcuts:

| Input | Action |
| --- | --- |
| `Ctrl+Shift+A` | Open Automation Builder. |
| `Tab` | Cycle to/from the other ESU editors when no draft is dirty. |
| `1` | Toggle input-link mode. |
| `2` | Toggle output-link mode. |
| `3` | Cycle the world view mode. |
| `E` | Open the selected breadboard's block canvas. |
| `Delete` | Remove or stage removal of the selected world link. Imported native links remain read-only. |

Block canvas controls:

| Input | Action |
| --- | --- |
| Drag a palette row | Create and snap a staged block. |
| Drag a block | Move its attached stack/value/body group and show the exact snap target. |
| Drag empty workspace with left or middle mouse | Pan. |
| Mouse wheel over the workspace | Zoom from 0.55x to 1.6x around the pointer. |
| Right-click a block | Open Select/Copy/Duplicate/Delete actions as applicable. |
| `Ctrl+C`, `Ctrl+V`, `Ctrl+D` | Copy, paste, or duplicate the selected editable attached stack. The clipboard is Automation Builder session state, not the OS clipboard. |
| `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z` | Undo or redo a graph edit. |
| Arrow keys | Nudge the selected editable group by 2 scaled canvas units. |
| Shift+Arrow keys | Nudge it by 10 scaled canvas units. |
| `Delete` or `Backspace` | Remove the selected editable block; imported vanilla blocks are read-only. |
| Escape | Dismiss the topmost property/slot/readiness/context popup or cancel the active canvas interaction. |

The header also provides **Fit**, **Center**, and **Arrange**. Fit and Center only
change the current view. Arrange lays out the visual native chains for
readability without changing their breadboard logic.

## Validation

Readiness is derived from the graph that Apply will lower. Apply is blocked when
any required condition is missing or ambiguous. Important checks include:

- a live selected controller with an editable native `FtdBoard`;
- a lowerable staged kind;
- a live linked target and a resolved native readable/writable property for each
  Getter or Setter;
- exactly one primary signal path where a block can accept either a stack input
  or a value socket;
- an incoming condition, a then/pass value, and a downstream action for each
  Switch;
- the required first/second inputs for logic and Max/Min blocks;
- an incoming signal for threshold, evaluator, Clamp, and Delay blocks; and
- at least one body action inside Forever.

Constants in scalar-only sockets must parse to a finite native value, and
labeled/range expressions must consume the complete field rather than silently
ignoring trailing text. Target and property pickers enumerate FtD's actual
options; an unresolved choice remains a blocking readiness issue. The Generated
Native Plan panel shows the current block program, live Getter/Setter values
when available, warnings, errors, and the native operations Apply would perform.

## Native round trip and preservation

The selected native board is the source of truth. On open, refresh, Apply, and
reopen, Automation Builder reconstructs its canvas from native components,
native input connections, component positions/sizes, native getter/setter
bindings, and ESU ownership marker Comments. World links are rebuilt from the
persisted `GenericBlockGetter` and `GenericBlockSetter` proxies. Applying the
same clean graph is idempotent; it does not append duplicate packages.

Every pre-existing vanilla component is preserved:

- recognized component families are imported with their semantic block shape;
- unrecognized families appear as opaque **Advanced Native** blocks;
- every imported latched input is projected with its stable native input index,
  including multiple ports on opaque components;
- imported vanilla blocks and imported wires are read-only in this editor; and
- expanded cards, normalization, selection, and Arrange never write display
  bounds back into imported components.

An imported component can feed an ESU-owned destination when that native output
is compatible, but Apply does not take ownership of or rewrite the imported
source. Edit or delete imported components and wires in FtD's native breadboard
editor.

## Current limitations

- This is a bounded block language, not arbitrary C#, Lua, expression, or event
  execution. Evaluator lowering is limited to the advertised Add, Subtract, and
  Multiply shapes.
- There is no Code page, recipe compiler, System Block template library, nested
  system workspace, or custom runtime component in the current editor.
- Control blocks express vanilla numeric dataflow. They do not provide
  imperative statements, event triggers, parallel tasks, or custom scheduling.
- Delay is FtD's native signal Delay; it is not a “wait, then execute” statement.
- Existing vanilla components and wires are intentionally read-only, even when
  their type is recognized. Automation Builder edits only staged or previously
  ESU-owned generated nodes.
- Controller and property support is constrained by the native `FtdBoard` and
  getter/setter adapters exposed by the installed FtD build.
- Canvas undo/redo and copy/paste are session-local. Native persistence begins
  at Apply and craft save.

Historical nested-workspace and Code/System concepts in design notes are
roadmap context, not hidden or partially supported release features.
