# Automation HUD and Tooltip Diagnostics

This note tracks the safe HUD hiding path used by ESU editors. The important lesson from the tooltip work is simple: never mutate FtD tooltip state. ESU now hides vanilla HUD drawing the same way the player-facing HUD toggle does, then restores the previous state on exit.

## Architecture

- `EsuEditorScope` reports whether Decoration Edit, Smart Builder, Automation Builder, or a short mode-switch handoff is active.
- `EsuVanillaHudVisibilityScope` is the primary HUD hide path. It captures `GuiDisplayBase.displayGUIs` when the first ESU editor opens, forces it to `false` while ESU owns the editor, and restores the captured value after ESU closes.
- `DecorationEditorInputScope`, `SmartBuildInputScope`, and `AutomationBuilderInputScope` call the visibility scope from their begin/end paths.
- `EsuVanillaInputBridge.Tick(...)` and the global ESU overlay call `EsuVanillaHudVisibilityScope.Tick(...)` as a watchdog, so unusual close or handoff frames still restore the vanilla flag.
- Existing cHud/build-input Harmony patches remain as targeted input/HUD protection, but they are not the primary tooltip suppression mechanism.
- `EsuCursorTooltip` renders ESU-owned cursor tooltips. Automation does not use generic `GUI.tooltip` fallback.
- `EsuHudNotifications` captures `InfoStore.Add(...)` messages while ESU editors are active and routes them into ESU notifications/logs.

## Safety Rules

- Do not call `TipDisplayer.SetTip(null)`.
- Do not assign `GUI.tooltip`.
- Do not clear or overwrite private FtD tooltip fields by reflection.
- Do not globally disable vanilla tooltip generation.
- Always restore the captured `GuiDisplayBase.displayGUIs` value after ESU closes. If the player had HUD hidden before opening ESU, it should stay hidden afterward.

## Diagnostics

Open the ESU log and look for `HUD diagnostics`.

Useful fields:

- `vanilla_gui_hidden_by_esu`: whether ESU has captured the vanilla HUD flag.
- `previous_display_guis`: the value ESU will restore on close.
- `current_display_guis`: the live vanilla draw flag.
- `editor`: the active ESU editor according to `EsuEditorScope`.

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
5. If vanilla HUD/tooltip UI leaks, check whether `current_display_guis` is unexpectedly `true`.
6. Close ESU and immediately test Escape menu buttons, Options tabs, and `Q` again.
7. If the game remains HUD-hidden after closing ESU, check the latest `Vanilla HUD display flag restored after ESU editor` log entry.

## Manual Regression Checklist

- Automation Builder world view: vanilla key hints, right-side status HUD, paint toolbar, and block tooltip boxes should not draw over ESU.
- Automation graph view: hovering craft behind the grid should not show vanilla tooltips.
- Decoration Edit and Smart Builder: vanilla HUD should stay hidden behind ESU panels.
- ESU console, popovers, and explicit ESU cursor tooltips should still render.
- Close each ESU editor and test Escape menu hover/click, Options tabs, and `Q` on breadboard/mainframe/spinblock.
