# Automation HUD and Tooltip Diagnostics

This note tracks the safe HUD hiding path used by ESU editors. The important lesson from the tooltip work is simple: never mutate FtD tooltip state. ESU uses the player-facing HUD flag as a first pass, then skips vanilla HUD/tooltip render roots while ESU owns an editor.

## Architecture

- `EsuEditorScope` reports whether Decoration Edit, Smart Builder, Automation Builder, a visible ESU GUI-frame lease, or a short mode-switch handoff is active.
- `EsuVanillaHudVisibilityScope` captures `GuiDisplayBase.displayGUIs` when the first ESU editor opens, forces it to `false` while ESU owns the editor, and restores the captured value after ESU closes.
- `EsuVanillaHudVisibilityScope_GuiDisplayer_LateUpdate_Patch` runs after vanilla `GuiDisplayer.LateUpdate`, so a player F9 press cannot reveal vanilla HUD while an ESU editor owns the screen.
- `EsuVanillaHudRenderGate` is the render-only fallback for vanilla paths that still draw while `displayGUIs` is false. It gates `GuiDisplayBase.OnGUI`, `cHud.OnGUI`/`OnGuiInternal`, build HUD prompts, player/vehicle/status RHS rows, `TipDisplayer` render methods, and direct HUD helper draw methods only while an ESU editor scope is active.
- `DecorationEditorInputScope`, `SmartBuildInputScope`, and `AutomationBuilderInputScope` call the visibility scope from their begin/end paths.
- `EsuVanillaInputBridge.Tick(...)` and the global ESU overlay call `EsuVanillaHudVisibilityScope.Tick(...)` as a watchdog, so unusual close or handoff frames still restore the vanilla flag.
- Existing cHud/build-input Harmony patches remain as targeted input/HUD protection.
- `EsuCursorTooltip` renders ESU-owned cursor tooltips. Automation does not use generic `GUI.tooltip` fallback.
- `EsuHudNotifications` captures `InfoStore.Add(...)` messages while ESU editors are active and routes them into ESU notifications/logs.

## Safety Rules

- Do not call `TipDisplayer.SetTip(null)`.
- Do not assign `GUI.tooltip`.
- Do not clear or overwrite private FtD tooltip fields by reflection.
- Do not globally disable vanilla tooltip generation.
- Do not leave render gates active after ESU closes. Prefixes must return `true` when `EsuEditorScope.ShouldHideVanillaHud` is false.
- Always restore the captured `GuiDisplayBase.displayGUIs` value after ESU closes. If the player had HUD hidden before opening ESU, it should stay hidden afterward.

## Diagnostics

Enable `Developer diagnostics` in the Endless Shapes Unlimited options, then open the ESU log and look for `HUD diagnostics`. The diagnostics are hidden by default so normal play does not fill the ESU log with low-level HUD traces.

Useful fields:

- `vanilla_gui_hidden_by_esu`: whether ESU has captured the vanilla HUD flag.
- `previous_display_guis`: the value ESU will restore on close.
- `current_display_guis`: the live vanilla draw flag.
- `editor`: the active ESU editor according to `EsuEditorScope`.
- `decoration_editor_active`, `smart_builder_active`, `automation_builder_active`, `mode_switch_handoff_active`: the individual ESU owners behind `editor`.
- `decoration_input_scope_active`, `smart_input_scope_active`, `automation_input_scope_active`: the scoped input guards for each editor.
- `decoration_registration_active`, `smart_registration_active`, `automation_registration_active`: the actual registered editor sessions.
- `gui_lease_active` and `gui_lease_owner`: the current per-frame ESU GUI draw owner, used to keep vanilla HUD hidden whenever ESU panels are visible.
- `should_hide_vanilla_hud`: whether ESU currently intends to hide vanilla HUD.
- `last_hud_force_context` and `last_hud_restore_context`: the most recent code path that forced or restored the vanilla HUD flag.
- `render_gate_targets_installed`: how many optional vanilla render roots are gated.
- `render_gate_core_targets_installed`: how many required vanilla HUD render roots are gated. This is the full no-visible-vanilla-HUD contract, not only `cHud`.
- `render_gate_core_targets_missing` and `render_gate_targets_missing_list`: target drift after an FtD update.
- `render_gate_suppressions`: how many vanilla render calls have been skipped since startup.
- `render_gate_recent`: the hottest skipped render methods.
- `assembly_location`, `duplicate_esu_dll_count`, and `duplicate_esu_dll_warning`: whether the loaded mod folder contains nested stale ESU DLLs.

Expected while an ESU editor is open:

- `vanilla_gui_hidden_by_esu=true`
- `current_display_guis=false`
- `editor=Automation Builder`, `Decoration Edit`, `Smart Builder`, or `Mode Switch`

Expected after closing ESU:

- `vanilla_gui_hidden_by_esu=false`
- `current_display_guis` restored to the previous value
- vanilla menus, Options tabs, hover tooltips, and `Q` block editors remain interactive

## Debug Workflow

1. Before opening ESU, confirm vanilla hover tooltips and `Q` block editors work.
2. Open an ESU editor and then open ESU Log.
3. Confirm the HUD diagnostics fields match the expected active-editor values.
4. Hover painted blocks, breadboards, mainframes, and spinblocks behind the ESU UI.
5. If vanilla HUD/tooltip UI leaks, check whether `current_display_guis` is unexpectedly `true`, whether `render_gate_core_targets_missing` names a drifted target, and whether `render_gate_recent` names the leaking draw root.
6. Close ESU and immediately test Escape menu buttons, Options tabs, and `Q` again.
7. If the game remains HUD-hidden after closing ESU, check the latest `Vanilla HUD display flag restored after ESU editor` log entry.

## Manual Regression Checklist

- Automation Builder world view: vanilla key hints, right-side status HUD, paint toolbar, and block tooltip boxes should not draw over ESU.
- Automation graph view: hovering craft behind the grid should not show vanilla tooltips.
- Decoration Edit and Smart Builder: vanilla HUD should stay hidden behind ESU panels.
- ESU console, popovers, and explicit ESU cursor tooltips should still render.
- Close each ESU editor and test Escape menu hover/click, Options tabs, and `Q` on breadboard/mainframe/spinblock.
