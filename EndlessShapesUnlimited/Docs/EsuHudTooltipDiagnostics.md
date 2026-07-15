# ESU HUD and Tooltip Diagnostics

This note tracks the safe HUD-hiding path used by ESU editors. Never mutate
FtD tooltip state. ESU uses the player-facing HUD flag first, then skips
vanilla HUD and tooltip render roots while ESU owns an editor.

## Architecture

- `EsuEditorScope` reports whether Decoration Edit, Smart Builder, a visible
  ESU GUI-frame lease, or a short mode-switch handoff is active.
- `EsuVanillaHudVisibilityScope` captures `GuiDisplayBase.displayGUIs` when the
  first ESU editor opens, forces it to `false` while ESU owns the editor, and
  restores the captured value after ESU closes.
- `EsuVanillaHudVisibilityScope_GuiDisplayer_LateUpdate_Patch` runs after
  vanilla `GuiDisplayer.LateUpdate`, so an F9 press cannot reveal vanilla HUD
  while an ESU editor owns the screen.
- `EsuVanillaHudRenderGate` is the render-only fallback for vanilla paths that
  still draw while `displayGUIs` is false. It gates vanilla HUD and tooltip
  render roots only while an ESU editor scope is active.
- `DecorationEditorInputScope` and `SmartBuildInputScope` call the visibility
  scope from their begin/end paths.
- `EsuVanillaInputBridge.Tick(...)` and the global ESU overlay call
  `EsuVanillaHudVisibilityScope.Tick(...)` as a watchdog, so unusual close or
  handoff frames still restore the vanilla flag.
- `EsuCursorTooltip` renders ESU-owned cursor tooltips without using the
  generic `GUI.tooltip` fallback.
- `EsuHudNotifications` captures `InfoStore.Add(...)` messages while ESU
  editors are active and routes them into ESU notifications and logs.

## Safety rules

- Do not call `TipDisplayer.SetTip(null)`.
- Do not assign `GUI.tooltip`.
- Do not clear or overwrite private FtD tooltip fields by reflection.
- Do not globally disable vanilla tooltip generation.
- Do not leave render gates active after ESU closes. Prefixes must return
  `true` when `EsuEditorScope.ShouldHideVanillaHud` is false.
- Always restore the captured `GuiDisplayBase.displayGUIs` value after ESU
  closes. If the player had HUD hidden before opening ESU, it should stay
  hidden afterward.

## Diagnostics

Enable **Developer diagnostics** in the Endless Shapes Unlimited options, then
open the ESU log and look for `HUD diagnostics`.

Useful fields:

- `vanilla_gui_hidden_by_esu`: whether ESU captured the vanilla HUD flag.
- `previous_display_guis`: the value ESU will restore on close.
- `current_display_guis`: the live vanilla draw flag.
- `editor`: the active ESU editor according to `EsuEditorScope`.
- `decoration_editor_active`, `smart_builder_active`, and
  `mode_switch_handoff_active`: the individual ESU owners behind `editor`.
- `decoration_input_scope_active` and `smart_input_scope_active`: the scoped
  input guards for each editor.
- `decoration_registration_active` and `smart_registration_active`: the
  registered editor sessions.
- `gui_lease_active` and `gui_lease_owner`: the current per-frame ESU GUI draw
  owner.
- `should_hide_vanilla_hud`: whether ESU intends to hide vanilla HUD.
- `last_hud_force_context` and `last_hud_restore_context`: the latest code path
  that forced or restored the vanilla HUD flag.
- `render_gate_targets_installed`, `render_gate_core_targets_installed`, and
  `render_gate_core_targets_missing`: installation and target-drift status.
- `render_gate_suppressions` and `render_gate_recent`: skipped vanilla render
  calls.
- `assembly_location`, `duplicate_esu_dll_count`, and
  `duplicate_esu_dll_warning`: duplicate runtime diagnostics.

Expected while an ESU editor is open:

- `vanilla_gui_hidden_by_esu=true`
- `current_display_guis=false`
- `editor=Decoration Edit`, `Smart Builder`, or `Mode Switch`

Expected after closing ESU:

- `vanilla_gui_hidden_by_esu=false`
- `current_display_guis` restored to the previous value
- Vanilla menus, Options tabs, hover tooltips, and `Q` block editors remain
  interactive.

## Debug workflow

1. Before opening ESU, confirm vanilla hover tooltips and `Q` block editors
   work.
2. Open an ESU editor and then open ESU Log.
3. Confirm the HUD diagnostics fields match the expected active-editor values.
4. Hover painted blocks, mainframes, and spin blocks behind the ESU UI.
5. If vanilla HUD or tooltip UI leaks, inspect `current_display_guis`,
   `render_gate_core_targets_missing`, and `render_gate_recent`.
6. Close ESU and immediately test Escape menu buttons, Options tabs, and `Q`.
7. If the HUD remains hidden, inspect the latest
   `Vanilla HUD display flag restored after ESU editor` log entry.

## Manual regression checklist

- Decoration Edit, Surface Builder, and Smart Builder keep vanilla HUD hidden
  behind ESU panels.
- ESU console, popovers, and explicit ESU cursor tooltips still render.
- Closing each ESU editor restores Escape-menu hover/click, Options tabs, and
  `Q` block interaction.
