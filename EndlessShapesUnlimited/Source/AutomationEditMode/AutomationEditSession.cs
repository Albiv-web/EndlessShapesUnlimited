using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationEditSession
    {
        private const float ToolbarHeight = 54f;
        private const float LeftPanelWidth = 384f;
        private const float RightPanelWidth = 336f;
        private const float EditorWidth = 900f;
        private const float BottomStripHeight = 122f;
        private const int WorldHighlightLimit = 180;
        private const uint NoWireSourceComponentId = uint.MaxValue;

        private enum AutomationTool
        {
            Link,
            Place
        }

        private enum AutomationEditorPage
        {
            Graph,
            Code
        }

        private enum AutomationContextMenuKind
        {
            None,
            Placement,
            Controller,
            Target,
            Link,
            BreadboardNode,
            Selection
        }

        private enum AutomationContextAction
        {
            None,
            ArmPlacement,
            CancelPlacement,
            SelectController,
            OpenEditor,
            CloseEditor,
            LinkTarget,
            UnlinkTarget,
            InspectLink,
            RemoveLink,
            ClearLinks,
            ClearSelection,
            FilterCategory,
            RunChecks,
            DeleteController,
            SelectNode,
            DeleteNode,
            ClearWireSource
        }

        private delegate bool AcbControllerTextApply(
            AutomationAcbControllerButtonSummary button,
            string value,
            out string message);

        private static Rect s_leftPanelRect = Rect.zero;
        private static Rect s_rightPanelRect = Rect.zero;
        private static Rect s_editorRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static AutomationValidationBaseline s_validationBaseline;
        private static readonly AutomationCodeRecipe[] s_codeRecipes =
        {
            new AutomationCodeRecipe(
                "Validation proof",
                AutomationTargetCategory.BreadboardWritable,
                "if proof_signal > 0.5:\n" +
                "    out = proof_signal\n" +
                "else:\n" +
                "    out = proof_signal * 0\n"),
            new AutomationCodeRecipe(
                "If/else gate",
                AutomationTargetCategory.Other,
                "if a > 0.5:\n" +
                "    out = a\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Spinblock park",
                AutomationTargetCategory.Spinblocks,
                "if angle_error * angle_error > 0.0025:\n" +
                "    out = angle_error * 0.8\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Door sequence",
                AutomationTargetCategory.DoorsDocking,
                "if gear_down > 0.5 and door_clear > 0.5:\n" +
                "    out = 1\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Propulsion trim",
                AutomationTargetCategory.Propulsion,
                "if pitch_error * pitch_error > 0.01:\n" +
                "    out = pitch_error * -0.7\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "ACB bridge",
                AutomationTargetCategory.Controllers,
                "if button_signal > 0.5:\n" +
                "    out = 1\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Missile fuse",
                AutomationTargetCategory.Missiles,
                "if range_error < 0 and target_lock > 0.5:\n" +
                "    out = 1\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Missile guidance",
                AutomationTargetCategory.Missiles,
                "if target_lock > 0.5:\n" +
                "    out = guidance_error * 0.75\n" +
                "else:\n" +
                "    out = 0\n"),
            new AutomationCodeRecipe(
                "Missile thrust gate",
                AutomationTargetCategory.Missiles,
                "if time_since_launch > 0.15 and target_lock > 0.5:\n" +
                "    out = 1\n" +
                "else:\n" +
                "    out = 0\n")
        };

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.Automation.Toolbar".GetHashCode();
        private readonly int _leftPanelWindowId = "EndlessShapesUnlimited.Automation.LeftPanel".GetHashCode();
        private readonly int _rightPanelWindowId = "EndlessShapesUnlimited.Automation.RightPanel".GetHashCode();
        private readonly int _editorWindowId = "EndlessShapesUnlimited.Automation.Editor".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.Automation.Status".GetHashCode();
        private readonly List<AutomationLink> _links = new List<AutomationLink>();

        private Rect _toolbarRect;
        private Rect _leftPanelRect = s_leftPanelRect;
        private Rect _rightPanelRect = s_rightPanelRect;
        private Rect _editorRect = s_editorRect;
        private Rect _statusRect;
        private Rect _leftPanelResizeStart;
        private Rect _rightPanelResizeStart;
        private Vector2 _leftPanelResizeMouseStart;
        private Vector2 _rightPanelResizeMouseStart;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private Vector2 _editorScroll;
        private IReadOnlyList<AutomationTarget> _targets = Array.Empty<AutomationTarget>();
        private AutomationTarget _selectedController;
        private AutomationTarget _hoverTarget;
        private AutomationControllerDescriptor _selectedPlacement =
            AutomationControllerCatalog.All.FirstOrDefault();
        private AutomationTargetCategory _filter = AutomationTargetCategory.All;
        private AutomationTool _tool = AutomationTool.Link;
        private AutomationEditorPage _editorPage = AutomationEditorPage.Graph;
        private string _automationCodeText = string.Empty;
        private string _automationCodeControllerKey = string.Empty;
        private int _automationCodeRecipeIndex;
        private AutomationCompileRevertSet _lastCompileRevert;
        private bool _showLeftPanel = true;
        private bool _showRightPanel = true;
        private bool _showBlocksSection = true;
        private bool _showFilterSection = true;
        private bool _editorOpen;
        private bool _closeRequested;
        private bool _layoutInitialized;
        private bool _resizingLeftPanel;
        private bool _resizingRightPanel;
        private bool _resizingEditor;
        private float _nextTargetRefresh;
        private string _proxyPropertyFilter = string.Empty;
        private string _targetSearchText = string.Empty;
        private string _automationCodeOutputTargetKey = string.Empty;
        private uint _wireSourceComponentId = NoWireSourceComponentId;
        private int _wireSourceOutputIndex = -1;
        private uint _selectedCanvasComponentId = NoWireSourceComponentId;
        private uint _canvasDragComponentId = NoWireSourceComponentId;
        private AutomationContextMenuKind _contextMenuKind = AutomationContextMenuKind.None;
        private Rect _contextMenuRect;
        private Vector2 _contextMenuAnchor;
        private AutomationControllerDescriptor _contextMenuPlacement;
        private string _contextMenuControllerKey = string.Empty;
        private string _contextMenuTargetKey = string.Empty;
        private uint _contextMenuComponentId = NoWireSourceComponentId;
        private string _selectedLinkTargetKey = string.Empty;
        private Vector2 _canvasDragStartMouse;
        private Vector2 _canvasDragPreviewDelta;
        private float _canvasDragScale = 1f;
        private string _status = "Select or place a Breadboard/ACB controller.";
        private bool _lastCompileBoundOutput;
        private AutomationRuntimeDiagnosticResult _lastRuntimeDiagnostics =
            AutomationRuntimeDiagnosticResult.Empty;
        private string _lastRuntimeDiagnosticsControllerKey = string.Empty;
        private string _validationCompareStatus = string.Empty;
        private int _layoutResetGeneration = s_layoutGeneration;

        internal AutomationEditSession(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _pointerProbe = new DecorationPointerProbe(build);
        }

        internal bool Active { get; private set; }

        internal bool CloseRequested => _closeRequested;

        internal bool SwitchToDecorationEditRequested { get; private set; }

        internal void ClearSwitchToDecorationEditRequest() =>
            SwitchToDecorationEditRequested = false;

        internal bool CanSwitchToDecorationEdit(out string reason)
        {
            reason = null;
            return true;
        }

        internal void Begin()
        {
            Active = true;
            AutomationInputScope.Begin();
            RefreshTargets(force: true);
        }

        internal void End(bool preserveSharedHud = false)
        {
            AutomationInputScope.End();
            if (!preserveSharedHud)
                DecorationEditorOverlay.Clear();
            Active = false;
            _targets = Array.Empty<AutomationTarget>();
            _selectedController = null;
            _hoverTarget = null;
            _selectedLinkTargetKey = string.Empty;
            CloseAutomationContextMenu();
            _links.Clear();
        }

        internal void SuspendForModeSwitchHandoff()
        {
            AutomationInputScope.End();
            Active = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _resizingEditor = false;
            _closeRequested = false;
            SwitchToDecorationEditRequested = false;
            CloseAutomationContextMenu();
        }

        internal void Update()
        {
            if (!Active)
                return;

            EsuHudNotifications.SetActiveSource("Automation");
            RefreshTargets(force: false);
            RefreshHoverTarget();
            HandleKeyboard();
            HandleMouse();
            DrawWorldOverlay();
        }

        internal void OnGUI()
        {
            if (!Active)
                return;

            DrawGui(interactive: true);
        }

        internal void DrawModeSwitchHandoffGui()
        {
            DrawGui(interactive: false);
        }

        private void DrawGui(bool interactive)
        {
            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -10000);
            try
            {
                DecorationEditorTheme.Ensure();
                if (Event.current.type == EventType.Repaint)
                    DecorationEditorOverlay.Render();

                if (interactive)
                {
                    EsuCursorTooltip.BeginFrame(
                        Event.current.mousePosition,
                        _resizingLeftPanel || _resizingRightPanel || _resizingEditor);
                }

                ApplyLayoutResetIfNeeded();
                PrepareAutomationLayout();
                if (interactive)
                    HandleAutomationPanelResizes();

                if (!_editorOpen)
                {
                    GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
                    if (_showLeftPanel)
                        _leftPanelRect = GUI.Window(
                            _leftPanelWindowId,
                            _leftPanelRect,
                            DrawLeftPanel,
                            GUIContent.none,
                            GUIStyle.none);
                    if (_showRightPanel)
                        _rightPanelRect = GUI.Window(
                            _rightPanelWindowId,
                            _rightPanelRect,
                            DrawRightPanel,
                            GUIContent.none,
                            GUIStyle.none);
                    GUI.Window(_statusWindowId, _statusRect, DrawStatusStrip, GUIContent.none, GUIStyle.none);
                }

                if (_editorOpen)
                    _editorRect = GUI.Window(
                        _editorWindowId,
                        _editorRect,
                        DrawEditor,
                        GUIContent.none,
                        GUIStyle.none);

                if (interactive)
                {
                    DrawAutomationResizeGrips();
                    DrawAutomationContextMenu();
                    EsuHudNotifications.DrawExpandedPopup();
                    EsuConsoleWindow.DrawForegroundWindow();
                    EsuCursorTooltip.Draw();
                }
                else
                {
                    EsuHudNotifications.DrawExpandedPopup();
                }

                PersistLayoutState();
                if (!interactive)
                    return;

                bool mouseOverUi = IsMouseOverAnyUi(Event.current.mousePosition);
                AutomationInputScope.SetMouseOverUi(mouseOverUi);
                if (mouseOverUi && ShouldConsumeGuiEvent(Event.current))
                {
                    if (Event.current.type == EventType.ScrollWheel)
                        AutomationInputScope.ClaimMouseWheelInputForFrames();
                    else
                        AutomationInputScope.ClaimBuildInputForFrames();
                    Event.current.Use();
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private void RefreshTargets(bool force)
        {
            float now = Time.unscaledTime;
            if (!force && now < _nextTargetRefresh)
                return;

            _nextTargetRefresh = now + 0.75f;
            _targets = AutomationTargetCatalog.Capture(_build);
            if (_selectedController != null)
            {
                string selectedKey = _selectedController.StableKey;
                AutomationTarget refreshed = _targets.FirstOrDefault(
                    target => target.StableKey == selectedKey);
                if (refreshed != null)
                {
                    _selectedController = refreshed;
                }
                else
                {
                    ClearStaleSelectedController(selectedKey);
                }
            }

            RebindAutomationLinks();
            RehydrateSelectedControllerLinksFromNativeProxies();
        }

        private void ClearStaleSelectedController(string selectedKey)
        {
            string label = _selectedController?.Label ?? "Selected controller";
            if (!string.IsNullOrWhiteSpace(selectedKey))
                _links.RemoveAll(link => string.Equals(link.ControllerKey, selectedKey, StringComparison.Ordinal));
            _selectedController = null;
            _selectedLinkTargetKey = string.Empty;
            _automationCodeOutputTargetKey = string.Empty;
            _automationCodeControllerKey = string.Empty;
            _lastCompileRevert = null;
            _lastCompileBoundOutput = false;
            _lastRuntimeDiagnostics = AutomationRuntimeDiagnosticResult.Empty;
            _lastRuntimeDiagnosticsControllerKey = string.Empty;
            _wireSourceComponentId = NoWireSourceComponentId;
            _wireSourceOutputIndex = -1;
            _selectedCanvasComponentId = NoWireSourceComponentId;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
            CloseAutomationContextMenu();
            CloseEditor();
            _status = label + " is no longer available. Select a live Automation controller.";
        }

        private void RebindAutomationLinks()
        {
            if (_links.Count == 0)
                return;

            Dictionary<string, AutomationTarget> targetsByKey = _targets
                .GroupBy(target => target.StableKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            for (int index = 0; index < _links.Count; index++)
            {
                AutomationLink link = _links[index];
                if (link == null)
                    continue;

                AutomationTarget refreshed;
                link.RebindTarget(
                    targetsByKey.TryGetValue(link.TargetKey, out refreshed)
                        ? refreshed
                        : null);
            }
        }

        private void RehydrateSelectedControllerLinksFromNativeProxies()
        {
            if (_selectedController == null ||
                !IsBreadboardController(_selectedController.Controller) ||
                _targets.Count == 0)
            {
                return;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out _))
            {
                return;
            }

            string controllerKey = _selectedController.StableKey;
            var existing = new HashSet<string>(
                _links
                    .Where(link => string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal))
                    .Select(link => link.TargetKey),
                StringComparer.Ordinal);
            int added = 0;
            foreach (AutomationBreadboardComponentSummary proxy in inspector.Components)
            {
                if (proxy == null ||
                    !proxy.IsGenericProxy ||
                    string.IsNullOrWhiteSpace(proxy.BlockTypeName))
                {
                    continue;
                }

                AutomationTarget target = UniqueTargetForPersistedProxy(proxy);
                if (target == null || existing.Contains(target.StableKey))
                    continue;

                _links.Add(new AutomationLink(_selectedController, target));
                existing.Add(target.StableKey);
                added++;
            }

            if (added > 0)
            {
                _status =
                    "Rehydrated " +
                    added.ToString(CultureInfo.InvariantCulture) +
                    " linked target(s) from native Generic Getter/Setter proxies.";
            }
        }

        private void RefreshHoverTarget()
        {
            _hoverTarget = null;
            if (AutomationInputScope.MouseOverUi || IsMouseCurrentlyOverUi())
                return;

            if (_tool != AutomationTool.Place &&
                TryPickProjectedAutomationTarget(out AutomationTarget projectedTarget))
            {
                _hoverTarget = projectedTarget;
                return;
            }

            if (_pointerProbe.TryProbe(out DecorationPointerHit hit) &&
                AutomationTargetCatalog.TryTargetFromHit(hit, out AutomationTarget target))
            {
                _hoverTarget = target;
            }
        }

        private void HandleKeyboard()
        {
            if (GUIUtility.keyboardControl != 0 ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
            {
                return;
            }

            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(1))
            {
                _tool = _tool == AutomationTool.Link
                    ? AutomationTool.Place
                    : AutomationTool.Link;
                _status = "Automation tool: " + ToolLabel(_tool) + ".";
            }
            else if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(2))
            {
                CycleFilter();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_editorOpen)
                    CloseEditor();
                else
                    TryOpenEditor();
            }
        }

        private bool TryOpenEditor(AutomationEditorPage? page = null)
        {
            if (_selectedController == null)
            {
                _status = "Select a live Breadboard/ACB controller before opening the Automation editor.";
                return false;
            }

            if (!_targets.Any(target => string.Equals(target.StableKey, _selectedController.StableKey, StringComparison.Ordinal)))
            {
                ClearStaleSelectedController(_selectedController.StableKey);
                return false;
            }

            if (page.HasValue)
                _editorPage = page.Value;
            _editorOpen = true;
            FitEditorToViewport();
            return true;
        }

        private void CloseEditor()
        {
            _editorOpen = false;
            _resizingEditor = false;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
        }

        private void HandleMouse()
        {
            if (AutomationInputScope.MouseOverUi || IsMouseCurrentlyOverUi())
            {
                if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
                    AutomationInputScope.ClaimMouseWheelInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                AutomationInputScope.ClaimBuildInputForFrames();
                if (TryCancelAutomationPlacementRightClick())
                    return;
                if (TryOpenAutomationWorldContextMenu())
                    return;
                if (TryOpenAutomationSelectionContextMenu(MouseGuiPosition()))
                    return;
                if (!TryCancelAutomationRightClick())
                    _status = "No Automation target or selection under the cursor.";
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            AutomationInputScope.ClaimBuildInputForFrames(3);
            if (_tool != AutomationTool.Place &&
                TryPickProjectedAutomationTarget(out AutomationTarget projectedTarget))
            {
                TryHandleAutomationTargetClick(
                    projectedTarget,
                    "Selected through x-ray Automation pick: ");
                return;
            }

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit == null)
            {
                return;
            }

            if (_tool == AutomationTool.Place)
            {
                TryPlaceSelectedController(hit);
                return;
            }

            if (!AutomationTargetCatalog.TryTargetFromHit(hit, out AutomationTarget target))
                return;

            TryHandleAutomationTargetClick(target, "Selected ");
        }

        private bool TryCancelAutomationRightClick()
        {
            if (TryCancelAutomationPlacementRightClick())
                return true;

            if (_selectedController == null &&
                string.IsNullOrEmpty(_selectedLinkTargetKey) &&
                _selectedCanvasComponentId == NoWireSourceComponentId &&
                _wireSourceComponentId == NoWireSourceComponentId)
            {
                return false;
            }

            string selected = SelectedControllerSummary();
            _selectedController = null;
            _selectedLinkTargetKey = string.Empty;
            _automationCodeOutputTargetKey = string.Empty;
            _automationCodeControllerKey = string.Empty;
            _lastCompileRevert = null;
            _lastCompileBoundOutput = false;
            _lastRuntimeDiagnostics = AutomationRuntimeDiagnosticResult.Empty;
            _lastRuntimeDiagnosticsControllerKey = string.Empty;
            _wireSourceComponentId = NoWireSourceComponentId;
            _wireSourceOutputIndex = -1;
            _selectedCanvasComponentId = NoWireSourceComponentId;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
            CloseEditor();
            _status = string.Equals(selected, "none", StringComparison.Ordinal)
                ? "Automation selection cleared."
                : "Automation selection cleared: " + selected + ".";
            return true;
        }

        private bool TryCancelAutomationPlacementRightClick()
        {
            if (_tool != AutomationTool.Place)
                return false;

            string block = SelectedPlacementSummary();
            _selectedPlacement = null;
            _tool = AutomationTool.Link;
            CloseAutomationContextMenu();
            _status = string.Equals(block, "none", StringComparison.Ordinal)
                ? "Automation placement cancelled."
                : "Automation placement cancelled: " + block + ".";
            return true;
        }

        private bool TryOpenAutomationWorldContextMenu()
        {
            Vector2 mouse = MouseGuiPosition();
            if (TryPickProjectedAutomationTarget(out AutomationTarget projectedTarget))
            {
                OpenAutomationTargetContextMenu(projectedTarget, mouse);
                return true;
            }

            if (_pointerProbe.TryProbe(out DecorationPointerHit hit) &&
                AutomationTargetCatalog.TryTargetFromHit(hit, out AutomationTarget target))
            {
                OpenAutomationTargetContextMenu(target, mouse);
                return true;
            }

            return false;
        }

        private bool TryHandleAutomationTargetClick(
            AutomationTarget target,
            string controllerStatusPrefix)
        {
            if (target == null)
                return false;

            if (target.IsController)
            {
                if (TryToggleControllerTargetLink(target))
                    return true;

                SelectAutomationController(target, controllerStatusPrefix);
                return true;
            }

            if (_selectedController == null)
            {
                _status = "Select a Breadboard/ACB before linking targets.";
                return true;
            }

            if (!AutomationTargetCatalog.PassesFilter(target, _filter) &&
                !IsLinked(_selectedController, target))
            {
                _status = target.Label + " does not match the active filter.";
                return true;
            }

            ToggleLink(_selectedController, target);
            return true;
        }

        private bool TryOpenAutomationSelectionContextMenu(Vector2 mouse)
        {
            AutomationLink selectedLink = SelectedLink();
            if (selectedLink != null)
            {
                OpenAutomationLinkContextMenu(selectedLink, mouse);
                return true;
            }

            if (_selectedCanvasComponentId != NoWireSourceComponentId &&
                ContextBreadboardComponent(_selectedCanvasComponentId, out _) != null)
            {
                OpenAutomationBreadboardNodeContextMenu(_selectedCanvasComponentId, mouse);
                return true;
            }

            if (_wireSourceComponentId != NoWireSourceComponentId &&
                ContextBreadboardComponent(_wireSourceComponentId, out _) != null)
            {
                OpenAutomationBreadboardNodeContextMenu(_wireSourceComponentId, mouse);
                return true;
            }

            if (_selectedController != null)
            {
                OpenAutomationControllerContextMenu(_selectedController, mouse);
                return true;
            }

            return false;
        }

        private void OpenAutomationPlacementContextMenu(
            AutomationControllerDescriptor descriptor,
            Vector2 mouse)
        {
            _contextMenuKind = AutomationContextMenuKind.Placement;
            _contextMenuPlacement = descriptor;
            _contextMenuControllerKey = string.Empty;
            _contextMenuTargetKey = string.Empty;
            _contextMenuComponentId = NoWireSourceComponentId;
            OpenAutomationContextMenuAt(mouse, buttonCount: descriptor == null ? 1 : 2);
        }

        private void OpenAutomationTargetContextMenu(AutomationTarget target, Vector2 mouse)
        {
            if (target == null)
                return;

            if (target.IsController)
            {
                OpenAutomationControllerContextMenu(target, mouse);
                return;
            }

            _contextMenuKind = AutomationContextMenuKind.Target;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = _selectedController?.StableKey ?? string.Empty;
            _contextMenuTargetKey = target.StableKey;
            _contextMenuComponentId = NoWireSourceComponentId;
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationControllerContextMenu(AutomationTarget controller, Vector2 mouse)
        {
            if (controller == null)
                return;

            _contextMenuKind = AutomationContextMenuKind.Controller;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = controller.StableKey;
            _contextMenuTargetKey = controller.StableKey;
            _contextMenuComponentId = NoWireSourceComponentId;
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationLinkContextMenu(AutomationLink link, Vector2 mouse)
        {
            if (link == null)
                return;

            _contextMenuKind = AutomationContextMenuKind.Link;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = link.ControllerKey ?? string.Empty;
            _contextMenuTargetKey = link.TargetKey ?? string.Empty;
            _contextMenuComponentId = NoWireSourceComponentId;
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationBreadboardNodeContextMenu(uint componentId, Vector2 mouse)
        {
            if (componentId == NoWireSourceComponentId)
                return;

            _contextMenuKind = AutomationContextMenuKind.BreadboardNode;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = _selectedController?.StableKey ?? string.Empty;
            _contextMenuTargetKey = string.Empty;
            _contextMenuComponentId = componentId;
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationContextMenuAt(Vector2 mouse, int buttonCount)
        {
            _contextMenuAnchor = mouse;
            _contextMenuRect = AutomationContextRect(mouse, buttonCount);
        }

        private Rect AutomationContextRect(Vector2 mouse, int buttonCount)
        {
            float width = EsuHudLayout.Scale(172f);
            float height = EsuHudLayout.Scale(34f + Mathf.Max(1, buttonCount) * 26f);
            var rect = new Rect(mouse.x, mouse.y, width, height);
            rect.x = Mathf.Clamp(rect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
            rect.y = Mathf.Clamp(rect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
            return rect;
        }

        private int ContextMenuButtonCount() =>
            Math.Max(1, AutomationContextMenuItems().Count());

        private void DrawAutomationContextMenu()
        {
            if (_contextMenuKind == AutomationContextMenuKind.None)
                return;

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_contextMenuRect.Contains(current.mousePosition))
            {
                CloseAutomationContextMenu();
                return;
            }

            AutomationContextMenuItem[] items = AutomationContextMenuItems().ToArray();
            if (items.Length == 0)
            {
                CloseAutomationContextMenu();
                return;
            }

            _contextMenuRect = AutomationContextRect(_contextMenuAnchor, items.Length);
            AutomationContextAction action = AutomationContextAction.None;
            GUI.Box(_contextMenuRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(_contextMenuRect, 5f));
            GUILayout.Label(AutomationContextTitle(), DecorationEditorTheme.SubHeader);
            foreach (AutomationContextMenuItem item in items)
            {
                bool previous = GUI.enabled;
                GUI.enabled = previous && item.Enabled;
                if (AutomationGUILayoutButton(
                        new GUIContent(item.Label, item.Tooltip),
                        item.Enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    action = item.Action;
                }
                GUI.enabled = previous;
            }
            GUILayout.EndArea();

            if (ShouldConsumeAutomationContextEvent(current))
                current.Use();

            ExecuteAutomationContextAction(action);
        }

        private string AutomationContextTitle()
        {
            switch (_contextMenuKind)
            {
                case AutomationContextMenuKind.Placement:
                    return "Automation block";
                case AutomationContextMenuKind.Controller:
                    return "Controller";
                case AutomationContextMenuKind.Target:
                    return "Target";
                case AutomationContextMenuKind.Link:
                    return "Automation link";
                case AutomationContextMenuKind.BreadboardNode:
                    return "Breadboard node";
                default:
                    return "Automation";
            }
        }

        private IEnumerable<AutomationContextMenuItem> AutomationContextMenuItems()
        {
            switch (_contextMenuKind)
            {
                case AutomationContextMenuKind.Placement:
                    yield return new AutomationContextMenuItem(
                        "Arm placement",
                        AutomationContextAction.ArmPlacement,
                        "Arm this Automation controller for placement.",
                        _contextMenuPlacement != null);
                    yield return new AutomationContextMenuItem(
                        "Cancel placement",
                        AutomationContextAction.CancelPlacement,
                        "Cancel the current Automation placement.",
                        _tool == AutomationTool.Place);
                    break;

                case AutomationContextMenuKind.Controller:
                    foreach (AutomationContextMenuItem item in ControllerContextMenuItems())
                        yield return item;
                    break;

                case AutomationContextMenuKind.Target:
                    foreach (AutomationContextMenuItem item in TargetContextMenuItems())
                        yield return item;
                    break;

                case AutomationContextMenuKind.Link:
                    foreach (AutomationContextMenuItem item in LinkContextMenuItems())
                        yield return item;
                    break;

                case AutomationContextMenuKind.BreadboardNode:
                    foreach (AutomationContextMenuItem item in BreadboardNodeContextMenuItems())
                        yield return item;
                    break;

                case AutomationContextMenuKind.Selection:
                    yield return new AutomationContextMenuItem(
                        "Clear selection",
                        AutomationContextAction.ClearSelection,
                        "Clear the current Automation selection.",
                        _selectedController != null);
                    break;
            }
        }

        private IEnumerable<AutomationContextMenuItem> ControllerContextMenuItems()
        {
            AutomationTarget controller = ContextController();
            bool hasController = controller != null;
            bool canOpenEditor = hasController;
            bool canRunChecks = hasController;
            bool selectedCanLink = _selectedController != null &&
                                   controller != null &&
                                   !string.Equals(_selectedController.StableKey, controller.StableKey, StringComparison.Ordinal) &&
                                   CanLinkControllerTarget(_selectedController, controller);
            bool linked = selectedCanLink && IsLinked(_selectedController, controller);
            yield return new AutomationContextMenuItem(
                "Select",
                AutomationContextAction.SelectController,
                "Select this Automation controller.",
                hasController);
            if (_editorOpen && _selectedController != null &&
                controller != null &&
                string.Equals(_selectedController.StableKey, controller.StableKey, StringComparison.Ordinal))
            {
                yield return new AutomationContextMenuItem(
                    "Close editor",
                    AutomationContextAction.CloseEditor,
                    "Close the fullscreen Automation editor.",
                    true);
            }
            else
            {
                yield return new AutomationContextMenuItem(
                    "Open editor",
                    AutomationContextAction.OpenEditor,
                    "Open the graph/code editor for this controller.",
                    canOpenEditor);
            }

            if (_selectedController != null &&
                controller != null &&
                !string.Equals(_selectedController.StableKey, controller.StableKey, StringComparison.Ordinal))
            {
                yield return new AutomationContextMenuItem(
                    linked ? "Unlink controller" : "Link controller",
                    linked ? AutomationContextAction.UnlinkTarget : AutomationContextAction.LinkTarget,
                    linked
                        ? "Remove the link from the selected controller to this controller."
                        : "Link the selected Breadboard controller to this Automation controller.",
                    selectedCanLink || linked);
            }

            yield return new AutomationContextMenuItem(
                "Clear links",
                AutomationContextAction.ClearLinks,
                "Remove every linked target from this controller.",
                hasController && LinksForController(controller).Count > 0);
            yield return new AutomationContextMenuItem(
                "Run checks",
                AutomationContextAction.RunChecks,
                "Run live runtime checks for this controller.",
                canRunChecks);
            yield return new AutomationContextMenuItem(
                "Delete controller",
                AutomationContextAction.DeleteController,
                "Delete this Automation controller block. Undo can restore it.",
                hasController);
            yield return new AutomationContextMenuItem(
                "Clear selection",
                AutomationContextAction.ClearSelection,
                "Clear the current Automation selection.",
                _selectedController != null);
        }

        private IEnumerable<AutomationContextMenuItem> TargetContextMenuItems()
        {
            AutomationTarget target = ContextTarget();
            bool canLink = CanContextLinkTarget(target);
            bool linked = _selectedController != null && target != null && IsLinked(_selectedController, target);
            AutomationLink link = ContextLinkForTarget(target);
            yield return new AutomationContextMenuItem(
                linked ? "Unlink target" : "Link target",
                linked ? AutomationContextAction.UnlinkTarget : AutomationContextAction.LinkTarget,
                linked
                    ? "Remove this target from the selected controller."
                    : "Link this target to the selected controller.",
                linked || canLink);
            yield return new AutomationContextMenuItem(
                "Inspect link",
                AutomationContextAction.InspectLink,
                "Show this link in the Automation details panel.",
                link != null);
            yield return new AutomationContextMenuItem(
                "Filter category",
                AutomationContextAction.FilterCategory,
                "Filter the target list to this target category.",
                target != null);
            yield return new AutomationContextMenuItem(
                "Clear selection",
                AutomationContextAction.ClearSelection,
                "Clear the current Automation selection.",
                _selectedController != null);
        }

        private IEnumerable<AutomationContextMenuItem> LinkContextMenuItems()
        {
            AutomationLink link = ContextLink();
            yield return new AutomationContextMenuItem(
                "Inspect link",
                AutomationContextAction.InspectLink,
                "Show this link in the Automation details panel.",
                link != null);
            yield return new AutomationContextMenuItem(
                "Open graph",
                AutomationContextAction.OpenEditor,
                "Open the graph editor for this link's controller.",
                ContextController() != null);
            yield return new AutomationContextMenuItem(
                "Remove link",
                AutomationContextAction.RemoveLink,
                "Remove this Automation link.",
                link != null);
            yield return new AutomationContextMenuItem(
                "Filter category",
                AutomationContextAction.FilterCategory,
                "Filter the target list to this target category.",
                link?.Target != null);
        }

        private IEnumerable<AutomationContextMenuItem> BreadboardNodeContextMenuItems()
        {
            AutomationBreadboardComponentSummary component =
                ContextBreadboardComponent(_contextMenuComponentId, out _);
            yield return new AutomationContextMenuItem(
                "Select node",
                AutomationContextAction.SelectNode,
                "Select this native breadboard node.",
                component != null);
            yield return new AutomationContextMenuItem(
                "Delete node",
                AutomationContextAction.DeleteNode,
                "Delete this native breadboard node from the graph.",
                component != null);
            yield return new AutomationContextMenuItem(
                "Clear wire source",
                AutomationContextAction.ClearWireSource,
                "Clear the active breadboard wire source.",
                _wireSourceComponentId != NoWireSourceComponentId);
            yield return new AutomationContextMenuItem(
                "Close editor",
                AutomationContextAction.CloseEditor,
                "Close the fullscreen Automation editor.",
                _editorOpen);
        }

        private void ExecuteAutomationContextAction(AutomationContextAction action)
        {
            if (action == AutomationContextAction.None)
                return;

            switch (action)
            {
                case AutomationContextAction.ArmPlacement:
                    if (_contextMenuPlacement != null)
                    {
                        _selectedPlacement = _contextMenuPlacement;
                        _tool = AutomationTool.Place;
                        _status = PlacementArmedStatus();
                    }
                    break;
                case AutomationContextAction.CancelPlacement:
                    TryCancelAutomationPlacementRightClick();
                    break;
                case AutomationContextAction.SelectController:
                    SelectAutomationController(ContextController());
                    break;
                case AutomationContextAction.OpenEditor:
                    OpenContextEditor();
                    break;
                case AutomationContextAction.CloseEditor:
                    CloseEditor();
                    _status = "Automation editor closed.";
                    break;
                case AutomationContextAction.LinkTarget:
                case AutomationContextAction.UnlinkTarget:
                    ToggleContextTargetLink();
                    break;
                case AutomationContextAction.InspectLink:
                    InspectContextLink();
                    break;
                case AutomationContextAction.RemoveLink:
                    RemoveAutomationLink(ContextLink());
                    break;
                case AutomationContextAction.ClearLinks:
                    ClearContextControllerLinks();
                    break;
                case AutomationContextAction.ClearSelection:
                    TryCancelAutomationRightClick();
                    break;
                case AutomationContextAction.FilterCategory:
                    FilterToContextTargetCategory();
                    break;
                case AutomationContextAction.RunChecks:
                    RunContextControllerChecks();
                    break;
                case AutomationContextAction.DeleteController:
                    DeleteContextController();
                    break;
                case AutomationContextAction.SelectNode:
                    SelectContextBreadboardNode();
                    break;
                case AutomationContextAction.DeleteNode:
                    DeleteContextBreadboardNode();
                    break;
                case AutomationContextAction.ClearWireSource:
                    ClearContextWireSource();
                    break;
            }

            CloseAutomationContextMenu();
        }

        private void OpenContextEditor()
        {
            AutomationTarget controller = ContextController();
            if (controller != null)
                SelectAutomationController(controller);
            TryOpenEditor(AutomationEditorPage.Graph);
        }

        private void ToggleContextTargetLink()
        {
            AutomationTarget target = ContextTarget() ?? ContextLink()?.Target;
            if (!CanContextLinkTarget(target) && !IsLinked(_selectedController, target))
            {
                _status = _selectedController == null
                    ? "Select a controller before linking Automation targets."
                    : "This Automation target cannot be linked from the selected controller.";
                return;
            }

            ToggleLink(_selectedController, target);
        }

        private void InspectContextLink()
        {
            AutomationLink link = ContextLink();
            if (link == null)
                link = ContextLinkForTarget(ContextTarget());
            if (link == null)
                return;

            _selectedLinkTargetKey = link.TargetKey;
            _showLeftPanel = true;
            _status = "Inspecting Automation link: " + link.TargetLabel + ".";
        }

        private void RemoveAutomationLink(AutomationLink link)
        {
            if (link == null)
                return;

            string label = link.TargetLabel;
            _links.Remove(link);
            if (string.Equals(_selectedLinkTargetKey, link.TargetKey, StringComparison.Ordinal))
                _selectedLinkTargetKey = string.Empty;
            if (string.Equals(_automationCodeOutputTargetKey, link.TargetKey, StringComparison.Ordinal))
                _automationCodeOutputTargetKey = string.Empty;
            _status = "Removed automation link to " + label + ".";
        }

        private void ClearContextControllerLinks()
        {
            AutomationTarget controller = ContextController();
            if (controller != null)
                SelectAutomationController(controller);
            ClearSelectedLinks();
        }

        private void FilterToContextTargetCategory()
        {
            AutomationTarget target = ContextTarget() ?? ContextLink()?.Target;
            if (target == null)
                return;

            _filter = target.Category;
            _showRightPanel = true;
            _showFilterSection = true;
            _status = "Automation target filter: " + AutomationTargetCatalog.CategoryLabel(_filter) + ".";
        }

        private void RunContextControllerChecks()
        {
            AutomationTarget controller = ContextController();
            if (controller != null)
                SelectAutomationController(controller);
            RunAutomationRuntimeDiagnostics();
        }

        private void DeleteContextController()
        {
            AutomationTarget controller = ContextController();
            if (controller == null)
                return;

            string key = controller.StableKey;
            if (!AutomationControllerCommitter.TryRemoveController(
                    _build,
                    controller,
                    out string message))
            {
                _status = message ?? "Could not delete Automation controller.";
                EsuHudNotifications.ShowSystem(
                    "Automation Editor",
                    _status,
                    EsuHudNotificationKind.Warning,
                    "Automation controller delete rejected.\nController=" + controller.Label);
                return;
            }

            _links.RemoveAll(link =>
                string.Equals(link.ControllerKey, key, StringComparison.Ordinal) ||
                string.Equals(link.TargetKey, key, StringComparison.Ordinal));
            if (_selectedController != null &&
                string.Equals(_selectedController.StableKey, key, StringComparison.Ordinal))
            {
                _selectedController = null;
                _selectedLinkTargetKey = string.Empty;
                _automationCodeOutputTargetKey = string.Empty;
                _automationCodeControllerKey = string.Empty;
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
                _selectedCanvasComponentId = NoWireSourceComponentId;
                _canvasDragComponentId = NoWireSourceComponentId;
                _canvasDragPreviewDelta = Vector2.zero;
                CloseEditor();
            }

            _status = message ?? "Deleted Automation controller.";
            EsuHudNotifications.ShowSystem(
                "Automation Editor",
                _status,
                EsuHudNotificationKind.Info,
                "Deleted Automation controller block.\nController=" + controller.Label);
            RefreshTargets(force: true);
        }

        private void SelectContextBreadboardNode()
        {
            AutomationBreadboardComponentSummary component =
                ContextBreadboardComponent(_contextMenuComponentId, out _);
            if (component == null)
                return;

            _selectedCanvasComponentId = component.UniqueId;
            _status = "Selected " + component.Label + " in the native graph.";
        }

        private void DeleteContextBreadboardNode()
        {
            AutomationBreadboardComponentSummary component =
                ContextBreadboardComponent(_contextMenuComponentId, out AutomationBreadboardInspector inspector);
            if (component == null || inspector == null)
                return;

            DeleteBreadboardComponent(inspector, component);
        }

        private void ClearContextWireSource()
        {
            _wireSourceComponentId = NoWireSourceComponentId;
            _wireSourceOutputIndex = -1;
            _status = "Cleared breadboard wire source.";
        }

        private bool CanContextLinkTarget(AutomationTarget target)
        {
            if (_selectedController == null || target == null)
                return false;

            if (target.IsController)
                return CanLinkControllerTarget(_selectedController, target);

            return !string.Equals(_selectedController.StableKey, target.StableKey, StringComparison.Ordinal);
        }

        private AutomationTarget ContextController()
        {
            AutomationTarget controller = TargetByKey(_contextMenuControllerKey);
            if (controller?.IsController == true)
                return controller;

            AutomationTarget target = TargetByKey(_contextMenuTargetKey);
            return target?.IsController == true ? target : _selectedController;
        }

        private AutomationTarget ContextTarget()
        {
            AutomationTarget target = TargetByKey(_contextMenuTargetKey);
            return target ?? ContextLink()?.Target;
        }

        private AutomationTarget TargetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _targets == null)
                return null;

            return _targets.FirstOrDefault(
                target => string.Equals(target.StableKey, key, StringComparison.Ordinal));
        }

        private AutomationLink ContextLink()
        {
            if (string.IsNullOrWhiteSpace(_contextMenuTargetKey))
                return null;

            return _links.FirstOrDefault(link =>
                string.Equals(link.TargetKey, _contextMenuTargetKey, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(_contextMenuControllerKey) ||
                 string.Equals(link.ControllerKey, _contextMenuControllerKey, StringComparison.Ordinal)));
        }

        private AutomationLink ContextLinkForTarget(AutomationTarget target)
        {
            if (_selectedController == null || target == null)
                return null;

            string controllerKey = _selectedController.StableKey;
            string targetKey = target.StableKey;
            return _links.FirstOrDefault(link =>
                string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                string.Equals(link.TargetKey, targetKey, StringComparison.Ordinal));
        }

        private IReadOnlyList<AutomationLink> LinksForController(AutomationTarget controller)
        {
            if (controller == null)
                return Array.Empty<AutomationLink>();

            string key = controller.StableKey;
            return _links
                .Where(link => string.Equals(link.ControllerKey, key, StringComparison.Ordinal))
                .ToArray();
        }

        private AutomationBreadboardComponentSummary ContextBreadboardComponent(
            uint componentId,
            out AutomationBreadboardInspector inspector)
        {
            inspector = null;
            if (componentId == NoWireSourceComponentId ||
                !TryCreateSelectedBreadboardInspector(out inspector, out _))
            {
                return null;
            }

            return inspector.Components.FirstOrDefault(component => component.UniqueId == componentId);
        }

        private bool TryOpenAutomationRowContextMenu(Rect row, Action<Vector2> open)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.MouseDown ||
                current.button != 1 ||
                !row.Contains(current.mousePosition))
            {
                return false;
            }

            open?.Invoke(GUIUtility.GUIToScreenPoint(current.mousePosition));
            AutomationInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private bool ShouldConsumeAutomationContextEvent(Event current)
        {
            return current != null &&
                   _contextMenuRect.Contains(current.mousePosition) &&
                   (current.type == EventType.MouseDown ||
                    current.type == EventType.MouseUp ||
                    current.type == EventType.MouseDrag ||
                    current.type == EventType.ScrollWheel ||
                    current.type == EventType.ContextClick);
        }

        private bool IsAutomationContextMenuAt(Vector2 mouse) =>
            _contextMenuKind != AutomationContextMenuKind.None &&
            _contextMenuRect.Contains(mouse);

        private void CloseAutomationContextMenu()
        {
            _contextMenuKind = AutomationContextMenuKind.None;
            _contextMenuRect = Rect.zero;
            _contextMenuAnchor = Vector2.zero;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = string.Empty;
            _contextMenuTargetKey = string.Empty;
            _contextMenuComponentId = NoWireSourceComponentId;
        }

        private void TryPlaceSelectedController(DecorationPointerHit hit)
        {
            if (_selectedPlacement == null)
            {
                _status = "Select a controller from the right panel first.";
                return;
            }

            if (!TryPlacementFromHit(hit, out Vector3i cell, out Quaternion rotation))
            {
                _status = "Point at a block face to place the controller.";
                EsuHudNotifications.ShowSystem(
                    "Automation Editor",
                    _status,
                    EsuHudNotificationKind.Warning,
                    "Selected block=" + SelectedPlacementSummary());
                return;
            }

            if (!AutomationControllerCommitter.TryPlaceController(
                    _build,
                    hit.Construct,
                    cell,
                    rotation,
                    _selectedPlacement,
                    out string message))
            {
                _status = message;
                EsuHudNotifications.ShowSystem(
                    "Automation Editor",
                    message,
                    PlacementFailureKind(message),
                    "Automation controller placement rejected." +
                    "\nBlock=" + SelectedPlacementSummary() +
                    " cell=" + FormatCell(cell) +
                    " reason=" + (message ?? string.Empty));
                return;
            }

            _status = message;
            EsuHudNotifications.ShowSystem(
                "Automation Editor",
                message,
                EsuHudNotificationKind.Info,
                "Automation controller placed." +
                "\nBlock=" + SelectedPlacementSummary() +
                " cell=" + FormatCell(cell));
            RefreshTargets(force: true);
            AutomationTarget placed = _targets.FirstOrDefault(
                target => target.Construct == hit.Construct &&
                          target.LocalPosition.Equals(cell));
            if (placed != null && placed.IsController)
                SelectAutomationController(placed, "Placed and selected ");
        }

        private static EsuHudNotificationKind PlacementFailureKind(string message)
        {
            return (message ?? string.Empty).IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0
                ? EsuHudNotificationKind.Warning
                : EsuHudNotificationKind.Error;
        }

        private static bool TryPlacementFromHit(
            DecorationPointerHit hit,
            out Vector3i cell,
            out Quaternion rotation)
        {
            cell = default;
            rotation = Quaternion.identity;
            if (hit?.Construct == null)
                return false;

            Vector3 localNormal = WorldNormalToLocal(hit);
            Vector3 center = new Vector3(hit.Anchor.x, hit.Anchor.y, hit.Anchor.z);
            Vector3 offset = hit.LocalHit - center;
            SmartBuildAxis axis = SmartBuildAxisHelper.FromLargestComponent(offset, out int sign);
            if (Mathf.Abs(SmartBuildAxisHelper.Get(offset, axis)) < 0.18f)
                axis = SmartBuildAxisHelper.FromLargestComponent(localNormal, out sign);

            sign = sign >= 0 ? 1 : -1;
            cell = hit.Anchor + SmartBuildAxisHelper.ToVector3i(axis, sign);
            rotation = SmartBuildPlacement.RotationForAxis(axis, sign);
            return true;
        }

        private static Vector3 WorldNormalToLocal(DecorationPointerHit hit)
        {
            if (hit?.Construct == null)
                return Vector3.forward;

            try
            {
                if (hit.Construct.myTransform != null)
                    return hit.Construct.myTransform.InverseTransformDirection(hit.WorldNormal).normalized;
            }
            catch
            {
                // Fall through to local face normal.
            }

            return DecorationPointerProbe.TryGetLocalFaceNormal(
                    hit.Anchor,
                    hit.LocalHit,
                    out Vector3 normal)
                ? normal
                : Vector3.forward;
        }

        private void ToggleLink(AutomationTarget controller, AutomationTarget target)
        {
            string controllerKey = controller.StableKey;
            string targetKey = target.StableKey;
            int existing = _links.FindIndex(
                link => link.ControllerKey == controllerKey &&
                        link.TargetKey == targetKey);
            if (existing >= 0)
            {
                _links.RemoveAt(existing);
                if (string.Equals(_selectedLinkTargetKey, targetKey, StringComparison.Ordinal))
                    _selectedLinkTargetKey = string.Empty;
                if (string.Equals(_automationCodeOutputTargetKey, targetKey, StringComparison.Ordinal))
                    _automationCodeOutputTargetKey = string.Empty;
                _status = "Removed automation link to " + target.Label + ".";
                return;
            }

            _links.Add(new AutomationLink(controller, target));
            _selectedLinkTargetKey = targetKey;
            if (AutomationTargetCatalog.IsBreadboardWritableTarget(target))
                _automationCodeOutputTargetKey = targetKey;
            _status = IsAcbControllerBridgeTarget(target)
                ? "Linked " + controller.Label + " to ACB Controller button keyword output."
                : IsAcbProxyTarget(target)
                    ? "Linked " + controller.Label + " to ACB rule proxy target."
                    : "Linked " + controller.Label + " to " + target.Label + ".";
        }

        private void SelectAutomationController(AutomationTarget target, string statusPrefix = "Selected controller: ")
        {
            if (target == null)
                return;

            bool changed = _selectedController == null ||
                           !string.Equals(_selectedController.StableKey, target.StableKey, StringComparison.Ordinal);
            _selectedController = target;
            if (changed)
            {
                _selectedLinkTargetKey = string.Empty;
                _automationCodeOutputTargetKey = string.Empty;
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
                _selectedCanvasComponentId = NoWireSourceComponentId;
                _canvasDragComponentId = NoWireSourceComponentId;
                _canvasDragPreviewDelta = Vector2.zero;
            }

            _status = statusPrefix + target.Label + ".";
        }

        private bool TryToggleControllerTargetLink(AutomationTarget target)
        {
            if (!CanLinkControllerTarget(_selectedController, target))
                return false;

            ToggleLink(_selectedController, target);
            return true;
        }

        private bool CanLinkControllerTarget(
            AutomationTarget controller,
            AutomationTarget target)
        {
            if (controller == null ||
                target == null ||
                !target.IsController ||
                string.Equals(controller.StableKey, target.StableKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsBreadboardController(controller.Controller))
                return false;

            return IsAcbControllerBridgeTarget(target) ||
                   IsAcbProxyTarget(target);
        }

        private bool IsLinked(
            AutomationTarget controller,
            AutomationTarget target)
        {
            if (controller == null || target == null)
                return false;

            string controllerKey = controller.StableKey;
            string targetKey = target.StableKey;
            return _links.Any(link =>
                link.ControllerKey == controllerKey &&
                link.TargetKey == targetKey);
        }

        private static bool IsAcbControllerBridgeTarget(AutomationTarget target) =>
            target?.Controller?.Kind == AutomationControllerKind.AcbController;

        private static bool IsAcbProxyTarget(AutomationTarget target) =>
            target?.Controller?.Kind == AutomationControllerKind.Acb;

        private bool TryPickProjectedController(out AutomationTarget controller)
        {
            return TryPickProjectedAutomationTarget(
                out controller,
                candidate => candidate?.IsController == true,
                EsuHudLayout.Scale(26f));
        }

        private bool TryPickProjectedAutomationTarget(out AutomationTarget target)
        {
            return TryPickProjectedAutomationTarget(
                out target,
                IsProjectedAutomationTargetPickable,
                EsuHudLayout.Scale(24f));
        }

        private bool TryPickProjectedAutomationTarget(
            out AutomationTarget target,
            Func<AutomationTarget, bool> predicate,
            float baseRadius)
        {
            target = null;
            if (_targets == null || _targets.Count == 0)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Vector2 mouse = MouseGuiPosition();
            float radius = Mathf.Max(EsuHudLayout.Scale(10f), baseRadius);
            float radiusSquared = radius * radius;
            AllConstruct preferredConstruct = null;
            try
            {
                preferredConstruct = _selectedController?.Construct ?? _build.GetC();
            }
            catch
            {
                preferredConstruct = _selectedController?.Construct;
            }

            float bestScore = float.PositiveInfinity;
            foreach (AutomationTarget candidate in _targets)
            {
                if (candidate == null ||
                    predicate == null ||
                    !predicate(candidate) ||
                    !TryProjectedAutomationTargetScreenRect(
                        camera,
                        candidate,
                        out Rect screenRect,
                        out Vector2 screenCenter,
                        out float depth))
                {
                    continue;
                }

                Rect pickRect = ExpandRect(screenRect, candidate.IsController ? radius * 0.45f : radius * 0.35f);
                float distanceSquared = (screenCenter - mouse).sqrMagnitude;
                if (!pickRect.Contains(mouse) && distanceSquared > radiusSquared)
                    continue;

                float constructPenalty = ReferenceEquals(candidate.Construct, preferredConstruct) ? 0f : radiusSquared * 0.35f;
                float depthPenalty = Mathf.Clamp(depth, 0f, 10000f) * 0.001f;
                float linkedBonus = IsLinked(_selectedController, candidate) ? -radiusSquared * 0.25f : 0f;
                float filterPenalty =
                    candidate.IsController || AutomationTargetCatalog.PassesFilter(candidate, _filter)
                        ? 0f
                        : radiusSquared * 0.5f;
                float rectPenalty = pickRect.Contains(mouse) ? 0f : radiusSquared * 0.1f;
                float score = distanceSquared + rectPenalty + constructPenalty + depthPenalty + filterPenalty + linkedBonus;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                target = candidate;
            }

            return target != null;
        }

        private bool IsProjectedAutomationTargetPickable(AutomationTarget candidate)
        {
            if (candidate == null)
                return false;

            if (_selectedController != null &&
                string.Equals(candidate.StableKey, _selectedController.StableKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (candidate.IsController)
                return true;

            if (_selectedController == null)
                return false;

            return AutomationTargetCatalog.PassesFilter(candidate, _filter) ||
                   IsLinked(_selectedController, candidate);
        }

        private static bool TryProjectedAutomationTargetScreenRect(
            Camera camera,
            AutomationTarget target,
            out Rect rect,
            out Vector2 center,
            out float depth)
        {
            rect = Rect.zero;
            center = Vector2.zero;
            depth = float.PositiveInfinity;
            if (camera == null || target?.Construct == null)
                return false;

            Vector3[] corners = CellCorners(target.Construct, target.LocalPosition);
            if (corners == null || corners.Length == 0)
                return false;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float nearestDepth = float.PositiveInfinity;
            int projected = 0;
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 screen = camera.WorldToScreenPoint(corners[index]);
                if (screen.z <= 0.01f)
                    continue;

                projected++;
                nearestDepth = Mathf.Min(nearestDepth, screen.z);
                float x = screen.x;
                float y = Screen.height - screen.y;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }

            if (projected == 0)
                return false;

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            Vector3 centerScreen = camera.WorldToScreenPoint(target.WorldCenter);
            if (centerScreen.z > 0.01f)
            {
                center = new Vector2(centerScreen.x, Screen.height - centerScreen.y);
                depth = centerScreen.z;
            }
            else
            {
                center = rect.center;
                depth = nearestDepth;
            }

            return true;
        }

        private static Rect ExpandRect(Rect rect, float amount)
        {
            float pad = Mathf.Max(0f, amount);
            return new Rect(
                rect.x - pad,
                rect.y - pad,
                rect.width + pad * 2f,
                rect.height + pad * 2f);
        }

        private void DrawWorldOverlay()
        {
            DecorationEditorOverlay.BeginFrame();
            if (!Active)
                return;

            try
            {
                DrawTargetHighlights();
                DrawPlacementPreview();
                DrawLinks();
                if (_hoverTarget != null)
                    DrawTargetBox(_hoverTarget, new Color(1f, 0.92f, 0.25f, 0.92f), 4.4f, 0.08f);
                if (_selectedController != null)
                    DrawTargetBox(_selectedController, new Color(0.1f, 0.95f, 1f, 1f), 5.4f, 0.13f);
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception("Automation Editor", exception, "Automation overlay draw failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Automation overlay draw failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private void DrawPlacementPreview()
        {
            if (_tool != AutomationTool.Place ||
                _selectedPlacement == null ||
                AutomationInputScope.MouseOverUi ||
                IsMouseCurrentlyOverUi())
            {
                return;
            }

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit == null ||
                !TryPlacementFromHit(hit, out Vector3i cell, out _))
            {
                return;
            }

            bool occupied = IsCellOccupied(hit.Construct, cell);
            Color edge = occupied
                ? new Color(1f, 0.64f, 0.16f, 0.94f)
                : new Color(0.18f, 1f, 0.55f, 0.94f);
            DrawCellBox(hit.Construct, cell, edge, occupied ? 4.2f : 4.8f, occupied ? 0.09f : 0.12f);
        }

        private void DrawTargetHighlights()
        {
            IEnumerable<AutomationTarget> visible = _targets
                .Where(target =>
                    target != null &&
                    (target.IsController ||
                     (_selectedController != null &&
                      AutomationTargetCatalog.PassesFilter(target, _filter))))
                .Take(WorldHighlightLimit);

            foreach (AutomationTarget target in visible)
            {
                if (_selectedController != null &&
                    target.StableKey == _selectedController.StableKey)
                {
                    continue;
                }

                Color color = target.IsController
                    ? new Color(0.1f, 0.95f, 1f, 0.78f)
                    : CategoryColor(target.Category);
                DrawTargetBox(target, color, target.IsController ? 3.2f : 2.2f, target.IsController ? 0.07f : 0.035f);
            }
        }

        private void DrawLinks()
        {
            if (_selectedController == null)
                return;

            string key = _selectedController.StableKey;
            Vector3 start = _selectedController.WorldCenter;
            foreach (AutomationLink link in _links)
            {
                if (link.ControllerKey != key || link.Target == null)
                    continue;

                Vector3 end = link.Target.WorldCenter;
                DecorationEditorOverlay.Arrow(
                    start,
                    end,
                    new Color(0.2f, 1f, 0.65f, 0.96f),
                    3f,
                    0.26f);
                DecorationEditorOverlay.Circle(
                    end,
                    0.22f,
                    new Color(0.2f, 1f, 0.65f, 0.96f),
                    Vector3.up,
                    2.3f,
                    16);
            }
        }

        private static void DrawTargetBox(
            AutomationTarget target,
            Color edge,
            float width,
            float fillAlpha)
        {
            if (target?.Construct == null)
                return;

            DrawCellBox(target.Construct, target.LocalPosition, edge, width, fillAlpha);
        }

        private static void DrawCellBox(
            AllConstruct construct,
            Vector3i cell,
            Color edge,
            float width,
            float fillAlpha)
        {
            Vector3[] corners = CellCorners(construct, cell);
            if (corners == null)
                return;

            Color fill = new Color(edge.r, edge.g, edge.b, Mathf.Clamp01(fillAlpha));
            DecorationEditorOverlay.Quad(corners[0], corners[1], corners[2], corners[3], fill);
            DecorationEditorOverlay.Quad(corners[4], corners[7], corners[6], corners[5], fill);
            DecorationEditorOverlay.Quad(corners[0], corners[4], corners[5], corners[1], fill);
            DecorationEditorOverlay.Quad(corners[1], corners[5], corners[6], corners[2], fill);
            DecorationEditorOverlay.Quad(corners[2], corners[6], corners[7], corners[3], fill);
            DecorationEditorOverlay.Quad(corners[3], corners[7], corners[4], corners[0], fill);

            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
            };
            for (int index = 0; index < edges.GetLength(0); index++)
                DecorationEditorOverlay.Line(corners[edges[index, 0]], corners[edges[index, 1]], edge, width);
        }

        private static bool IsCellOccupied(AllConstruct construct, Vector3i cell)
        {
            try
            {
                return construct?.AllBasics?.GetBlockViaLocalPosition(cell) != null;
            }
            catch
            {
                return false;
            }
        }

        private static Vector3[] CellCorners(AllConstruct construct, Vector3i cell)
        {
            if (construct == null)
                return null;

            Vector3 center = new Vector3(cell.x, cell.y, cell.z);
            Vector3[] local =
            {
                center + new Vector3(-0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                center + new Vector3(-0.5f, 0.5f, -0.5f),
                center + new Vector3(-0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                center + new Vector3(-0.5f, 0.5f, 0.5f)
            };
            var world = new Vector3[local.Length];
            for (int index = 0; index < local.Length; index++)
            {
                try
                {
                    world[index] = construct.SafeLocalToGlobal(local[index]);
                }
                catch
                {
                    if (construct.myTransform == null)
                        return null;
                    world[index] = construct.myTransform.TransformPoint(local[index]);
                }
            }

            return world;
        }

        private static Color CategoryColor(AutomationTargetCategory category)
        {
            switch (category)
            {
                case AutomationTargetCategory.Spinblocks:
                    return new Color(1f, 0.56f, 0.18f, 0.72f);
                case AutomationTargetCategory.TurretsWeapons:
                    return new Color(1f, 0.25f, 0.25f, 0.72f);
                case AutomationTargetCategory.Propulsion:
                    return new Color(0.28f, 0.66f, 1f, 0.72f);
                case AutomationTargetCategory.Pistons:
                    return new Color(0.72f, 0.48f, 1f, 0.72f);
                case AutomationTargetCategory.Pumps:
                    return new Color(0.24f, 0.9f, 1f, 0.72f);
                case AutomationTargetCategory.ControlSurfaces:
                    return new Color(0.15f, 1f, 0.56f, 0.72f);
                case AutomationTargetCategory.Ai:
                    return new Color(1f, 0.82f, 0.25f, 0.72f);
                case AutomationTargetCategory.Missiles:
                    return new Color(1f, 0.4f, 0.55f, 0.72f);
                case AutomationTargetCategory.Lights:
                    return new Color(1f, 0.95f, 0.35f, 0.72f);
                case AutomationTargetCategory.ShieldsDefence:
                    return new Color(0.36f, 0.82f, 1f, 0.72f);
                case AutomationTargetCategory.Detection:
                    return new Color(0.58f, 1f, 0.72f, 0.72f);
                case AutomationTargetCategory.DoorsDocking:
                    return new Color(0.78f, 0.78f, 0.78f, 0.72f);
                case AutomationTargetCategory.SoundDisplay:
                    return new Color(0.92f, 0.44f, 1f, 0.72f);
                case AutomationTargetCategory.ResourcePower:
                    return new Color(0.3f, 1f, 0.3f, 0.72f);
                default:
                    return new Color(0.85f, 0.95f, 1f, 0.56f);
            }
        }

        private void DrawToolbar(int id)
        {
            EsuHudNotifications.SetActiveSource("Automation");
            GUI.Box(new Rect(0f, 0f, _toolbarRect.width, _toolbarRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_toolbarRect.width, _toolbarRect.height);
            EsuHudLayout.CenteredToolbarFrame frame =
                EsuHudLayout.CalculateCenteredToolbarFrame(inner);
            EsuHudLayout.ToolbarBudget budget = frame.Budget;

            GUILayout.BeginArea(frame.Rect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth));
            if (ToolbarButton("build", "Auto", "Tab: switch to Decoration Edit Mode.", true))
                SwitchToDecorationEditRequested = true;
            if (ToolbarButton("anchor", "Link", "Select controllers and link target blocks.", _tool == AutomationTool.Link))
                _tool = AutomationTool.Link;
            if (ToolbarButton("create", "Place", "Place the selected Breadboard/ACB controller.", _tool == AutomationTool.Place))
            {
                _tool = AutomationTool.Place;
                _status = PlacementArmedStatus();
            }
            if (ToolbarButton("open", "Edit", "Open the ESU automation graph/code editor.", _editorOpen))
            {
                TryOpenEditor();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(budget.Gap);
            EsuHudNotifications.DrawToolbarSlot(
                frame.Rect,
                budget.NotificationWidth,
                "ESU mode: Automation.",
                new Vector2(_toolbarRect.x + frame.Rect.x, _toolbarRect.y + frame.Rect.y));
            GUILayout.Space(budget.Gap);
            GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth));
            if (ToolbarButton("settings", "Info", "Show or hide Automation details.", _showLeftPanel))
                _showLeftPanel = !_showLeftPanel;
            if (ToolbarButton("cube", "Blocks", "Show or hide Automation controller blocks.", _showRightPanel && _showBlocksSection))
                ToggleRightPanelSection(ref _showBlocksSection);
            if (ToolbarButton("filter", "Filter", "Show or hide Automation target filters.", _showRightPanel && _showFilterSection))
                ToggleRightPanelSection(ref _showFilterSection);
            if (ToolbarButton("close", "Close", "Close Automation Editor.", false))
                _closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static bool ToolbarButton(string icon, string label, string tooltip, bool active)
        {
            return AutomationGUILayoutButton(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                DecorationEditorTheme.ToolButton(active),
                GUILayout.Width(EsuHudLayout.Scale(58f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));
        }

        private static bool AutomationGUILayoutButton(
            GUIContent content,
            GUIStyle style,
            params GUILayoutOption[] options)
        {
            if (content?.image == null)
            {
                bool clicked = GUILayout.Button(content ?? GUIContent.none, style, options);
                EsuCursorTooltip.RegisterLast(content?.tooltip);
                return clicked;
            }

            var layoutContent = new GUIContent(content.text ?? string.Empty, content.tooltip);
            Rect rect = GUILayoutUtility.GetRect(layoutContent, style, options);
            return AutomationGUIButton(rect, content, style);
        }

        private static bool AutomationGUIButton(
            Rect rect,
            GUIContent content,
            GUIStyle style)
        {
            if (content?.image == null)
            {
                bool clicked = GUI.Button(rect, content ?? GUIContent.none, style);
                EsuCursorTooltip.Register(rect, content?.tooltip);
                return clicked;
            }

            bool result = GUI.Button(rect, GUIContent.none, style);
            DrawAutomationButtonContent(rect, content, style);
            EsuCursorTooltip.Register(rect, content.tooltip);
            return result;
        }

        private static void DrawAutomationButtonContent(
            Rect rect,
            GUIContent content,
            GUIStyle baseStyle)
        {
            if (content == null || rect.width <= 1f || rect.height <= 1f)
                return;

            Texture icon = content.image;
            string text = content.text ?? string.Empty;
            bool hasText = !string.IsNullOrWhiteSpace(text);
            bool stacked = hasText &&
                           rect.height >= EsuHudLayout.Scale(34f) &&
                           rect.width <= EsuHudLayout.Scale(92f);
            Color previousColor = GUI.color;
            if (!GUI.enabled)
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.55f);

            if (stacked)
            {
                float iconSize = Mathf.Min(EsuHudLayout.Scale(16f), Mathf.Max(1f, rect.height * 0.42f));
                Rect iconRect = new Rect(
                    rect.center.x - iconSize * 0.5f,
                    rect.y + EsuHudLayout.Scale(4f),
                    iconSize,
                    iconSize);
                if (icon != null)
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

                Rect textRect = new Rect(
                    rect.x + EsuHudLayout.Scale(2f),
                    iconRect.yMax - EsuHudLayout.Scale(1f),
                    Mathf.Max(0f, rect.width - EsuHudLayout.Scale(4f)),
                    Mathf.Max(0f, rect.yMax - iconRect.yMax + EsuHudLayout.Scale(1f)));
                GUI.Label(textRect, text, AutomationButtonTextStyle(baseStyle, TextAnchor.MiddleCenter));
            }
            else if (hasText)
            {
                float iconSize = Mathf.Min(EsuHudLayout.Scale(14f), Mathf.Max(1f, rect.height - EsuHudLayout.Scale(8f)));
                Rect iconRect = new Rect(
                    rect.x + EsuHudLayout.Scale(6f),
                    rect.y + (rect.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize);
                if (icon != null)
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

                Rect textRect = new Rect(
                    iconRect.xMax + EsuHudLayout.Scale(4f),
                    rect.y,
                    Mathf.Max(0f, rect.xMax - iconRect.xMax - EsuHudLayout.Scale(8f)),
                    rect.height);
                GUI.Label(textRect, text, AutomationButtonTextStyle(baseStyle, TextAnchor.MiddleCenter));
            }
            else if (icon != null)
            {
                float iconSize = Mathf.Min(EsuHudLayout.Scale(16f), Mathf.Max(1f, rect.height - EsuHudLayout.Scale(8f)));
                Rect iconRect = new Rect(
                    rect.center.x - iconSize * 0.5f,
                    rect.center.y - iconSize * 0.5f,
                    iconSize,
                    iconSize);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
            }

            GUI.color = previousColor;
        }

        private static GUIStyle AutomationButtonTextStyle(GUIStyle baseStyle, TextAnchor alignment)
        {
            var style = new GUIStyle(baseStyle)
            {
                alignment = alignment,
                clipping = TextClipping.Clip,
                imagePosition = ImagePosition.TextOnly,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            return style;
        }

        private void ToggleRightPanelSection(ref bool sectionVisible)
        {
            if (!_showRightPanel)
            {
                _showRightPanel = true;
                sectionVisible = true;
                return;
            }

            sectionVisible = !sectionVisible;
            if (!_showBlocksSection && !_showFilterSection)
                _showRightPanel = false;
        }

        private void DrawStatusStrip(int id)
        {
            GUI.Box(
                new Rect(0f, 0f, _statusRect.width, _statusRect.height),
                GUIContent.none,
                DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_statusRect.width, _statusRect.height, 4f);
            float headerHeight = EsuHudLayout.Scale(24f);
            float statusHeight = EsuHudLayout.Scale(25f);
            float controlsHeight = EsuHudLayout.Scale(28f);
            float footerHeight = EsuHudLayout.Scale(20f);
            float gap = EsuHudLayout.Scale(4f);
            float y = inner.y;

            GUILayout.BeginArea(new Rect(inner.x, y, inner.width, headerHeight));
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent("Automation Editor", DecorationEditorIconCatalog.Get("build")),
                DecorationEditorTheme.SubHeader,
                GUILayout.Width(EsuHudLayout.Scale(160f)));
            GUILayout.Label("Mode: " + ToolLabel(_tool), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(180f)));
            GUILayout.Label("Filter: " + AutomationTargetCatalog.CategoryLabel(_filter), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(170f)));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Stage: " + WorkspaceStageLabel(),
                AutomationStateStyle(),
                GUILayout.Width(EsuHudLayout.Scale(190f)));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            y += headerHeight;
            DrawCyanLine(new Rect(inner.x, y, inner.width, Mathf.Max(1f, EsuHudLayout.Scale(1f))));
            y += EsuHudLayout.Scale(4f);

            GUI.Label(
                new Rect(inner.x, y, inner.width, statusHeight),
                StatusLine(),
                DecorationEditorTheme.Status);
            y += statusHeight + gap;

            GUILayout.BeginArea(new Rect(inner.x, y, inner.width, controlsHeight));
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Link", DecorationEditorIconCatalog.Get("anchor"), "Select controllers and link target blocks."),
                    DecorationEditorTheme.ToolButton(_tool == AutomationTool.Link),
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(controlsHeight)))
            {
                _tool = AutomationTool.Link;
                _status = "Automation tool: " + ToolLabel(_tool) + ".";
            }

            if (AutomationGUILayoutButton(
                    new GUIContent("Place", DecorationEditorIconCatalog.Get("create"), "Place the selected Breadboard/ACB controller."),
                    DecorationEditorTheme.ToolButton(_tool == AutomationTool.Place),
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(controlsHeight)))
            {
                _tool = AutomationTool.Place;
                _status = PlacementArmedStatus();
            }

            if (AutomationGUILayoutButton(
                    new GUIContent("Editor", DecorationEditorIconCatalog.Get("open"), "Open the ESU automation graph/code editor."),
                    DecorationEditorTheme.ToolButton(_editorOpen),
                    GUILayout.Width(EsuHudLayout.Scale(80f)),
                    GUILayout.Height(controlsHeight)))
            {
                if (_editorOpen)
                    CloseEditor();
                else
                    TryOpenEditor();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _editorOpen;
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit", DecorationEditorIconCatalog.Get("focus"), "Fit the graph/code editor to the viewport."),
                    _editorOpen ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(60f)),
                    GUILayout.Height(controlsHeight)))
            {
                FitEditorToViewport();
                _status = "Automation editor fitted to the available viewport.";
            }

            GUI.enabled = previous;
            GUILayout.Space(EsuHudLayout.Scale(12f));
            GUILayout.Label(
                "Selected: " + SelectedControllerSummary(),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(220f)));
            GUILayout.Label(
                "Links: " + SelectedLinks.Count.ToString("N0", CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(86f)));
            GUILayout.Label(
                "Targets: " + _targets.Count.ToString("N0", CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(98f)));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Block: " + SelectedPlacementSummary(),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(230f)));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            y += controlsHeight + gap;

            if (y < inner.yMax)
            {
                string footer = "Next: " + NextSafeActionLine();
                if (_tool == AutomationTool.Place)
                {
                    footer += " Placement preview shows green for empty target cells and amber for blocked cells.";
                }

                GUI.Label(
                    new Rect(inner.x, y, inner.width, Mathf.Min(footerHeight, inner.yMax - y)),
                    footer,
                    DecorationEditorTheme.Mini);
            }
        }

        private string StatusLine()
        {
            if (_tool == AutomationTool.Place)
            {
                string detail = _status ?? string.Empty;
                string armed = PlacementArmedStatus();
                if (string.IsNullOrWhiteSpace(detail) ||
                    string.Equals(detail, armed, StringComparison.Ordinal))
                {
                    return armed;
                }

                return detail + " | " + armed;
            }

            if (_selectedController == null)
                return _status ?? "Select a controller or place one from the Automation Blocks panel.";

            return (_status ?? string.Empty) +
                   " | Controller: " +
                   _selectedController.Label +
                   " | Linked targets: " +
                   SelectedLinks.Count.ToString("N0", CultureInfo.InvariantCulture);
        }

        private string PlacementArmedStatus()
        {
            string block = SelectedPlacementSummary();
            if (_selectedPlacement == null)
                return "Select an automation block in the right palette before placing.";

            return "Placement armed: " + block + ". Point at an exposed craft face and left-click.";
        }

        private string SelectedControllerSummary()
        {
            if (_selectedController == null)
                return "none";

            return _selectedController.Controller?.ShortLabel ??
                   _selectedController.Label ??
                   "controller";
        }

        private string SelectedPlacementSummary() =>
            _selectedPlacement == null ? "none" : _selectedPlacement.Label;

        private GUIStyle AutomationStateStyle()
        {
            if (_tool == AutomationTool.Place && _selectedPlacement == null)
                return DecorationEditorTheme.Warning;

            return _selectedController == null && _tool == AutomationTool.Link
                ? DecorationEditorTheme.Mini
                : DecorationEditorTheme.Body;
        }

        private string WorkspaceStageLabel()
        {
            if (_editorOpen)
                return _editorPage == AutomationEditorPage.Graph ? "Native graph" : "Recipe compile";
            if (_tool == AutomationTool.Place)
                return _selectedPlacement == null ? "Choose controller" : "Place controller";
            if (_selectedController == null)
                return "Select controller";
            return SelectedLinks.Count == 0 ? "Link targets" : "Build graph";
        }

        private string NextSafeActionLine()
        {
            if (_editorOpen)
            {
                if (_selectedController == null)
                    return "Close the editor and select a live native controller.";

                if (_editorPage == AutomationEditorPage.Code)
                {
                    if (!IsBreadboardController(_selectedController.Controller))
                        return "Use Graph view for this controller; deterministic recipes compile into Breadboard nodes.";
                    if (SelectedLinks.Count == 0)
                        return "Link a writable world target before compiling recipe output.";
                    if (CanRevertLastAutomationCompile())
                        return "Review generated native nodes, then keep editing or use Revert compile.";
                    return "Choose a recipe/output target, then Compile expression into native graph nodes.";
                }

                if (IsBreadboardController(_selectedController.Controller))
                    return "Inspect native nodes, add Generic proxy nodes for linked targets, or switch to Code.";

                return "Inspect native ACB data; changes are written to FtD controller data.";
            }

            if (_tool == AutomationTool.Place)
            {
                if (_selectedPlacement == null)
                    return "Choose a native controller block from Automation Blocks.";
                return "Point at an exposed craft face and left-click to place " + SelectedPlacementSummary() + ".";
            }

            if (_selectedController == null)
                return "Click a controller on the craft, or choose a controller block to place one.";

            if (SelectedLinks.Count == 0)
                return "Click highlighted world targets to link them, or open Editor to inspect native data.";

            return "Open Editor for graph/code work, or click linked targets to inspect or unlink them.";
        }

        private string WorkspaceSafetyLine()
        {
            if (CanRevertLastAutomationCompile())
                return "Generated recipe nodes have a Revert compile path.";
            if (_editorOpen && _editorPage == AutomationEditorPage.Code)
                return "Recipes are deterministic and lower into native Breadboard nodes.";
            if (_editorOpen)
                return "Viewing existing native data does not mutate it; inspector edits apply directly.";
            if (_selectedController == null)
                return "Selection and target discovery are HUD-only until you place or edit a native controller.";
            return "World links are ESU workspace state until proxy nodes are created in the native graph.";
        }

        private string WorkspaceNativeSurfaceLine()
        {
            if (_selectedController == null)
                return "No native controller selected";
            if (IsBreadboardController(_selectedController.Controller))
                return "Native Breadboard graph";
            if (IsAcbControllerBridgeTarget(_selectedController))
                return "Native ACB Controller buttons";
            if (IsAcbProxyTarget(_selectedController))
                return "Native ACB rule data";
            return _selectedController.Controller?.ClassName ??
                   _selectedController.RuntimeType ??
                   "Native controller data";
        }

        private static void DrawCyanLine(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = DecorationEditorTheme.Cyan;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawCompactIconHeader(string text, string iconKey, GUIStyle baseStyle)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(22f), GUILayout.ExpandWidth(true));
            GUI.Label(rect, GUIContent.none, baseStyle);

            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            Rect iconRect = new Rect(
                rect.x + EsuHudLayout.Scale(5f),
                rect.y + EsuHudLayout.Scale(3f),
                EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(16f));
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            Rect textRect = new Rect(
                iconRect.xMax + EsuHudLayout.Scale(6f),
                rect.y,
                Mathf.Max(0f, rect.xMax - iconRect.xMax - EsuHudLayout.Scale(8f)),
                rect.height);
            if (textRect.width > 1f)
                GUI.Label(textRect, text, IconHeaderTextStyle(baseStyle));
        }

        private static GUIStyle IconHeaderTextStyle(GUIStyle baseStyle)
        {
            var style = new GUIStyle(baseStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            style.normal.background = null;
            return style;
        }

        private static void DrawAutomationIconRow(
            Rect row,
            string iconKey,
            string title,
            string detail,
            GUIStyle titleStyle,
            GUIStyle detailStyle)
        {
            float inset = EsuHudLayout.Scale(5f);
            float iconSize = Mathf.Min(EsuHudLayout.Scale(22f), Mathf.Max(1f, row.height - inset * 2f));
            Rect iconRect = new Rect(
                row.x + inset,
                row.y + (row.height - iconSize) * 0.5f,
                iconSize,
                iconSize);
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            float textX = iconRect.xMax + EsuHudLayout.Scale(7f);
            float textWidth = Mathf.Max(0f, row.xMax - textX - EsuHudLayout.Scale(6f));
            float titleHeight = Mathf.Min(EsuHudLayout.Scale(18f), row.height);
            GUI.Label(
                new Rect(textX, row.y + EsuHudLayout.Scale(2f), textWidth, titleHeight),
                title ?? string.Empty,
                titleStyle);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                GUI.Label(
                    new Rect(
                        textX,
                        row.y + titleHeight,
                        textWidth,
                        Mathf.Max(0f, row.height - titleHeight - EsuHudLayout.Scale(2f))),
                    detail,
                    detailStyle);
            }
        }

        private static void DrawAutomationSingleLineIconRow(
            Rect row,
            string iconKey,
            string label,
            GUIStyle textStyle)
        {
            float inset = EsuHudLayout.Scale(5f);
            float iconSize = Mathf.Min(EsuHudLayout.Scale(18f), Mathf.Max(1f, row.height - inset * 2f));
            Rect iconRect = new Rect(
                row.x + inset,
                row.y + (row.height - iconSize) * 0.5f,
                iconSize,
                iconSize);
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            Rect textRect = new Rect(
                iconRect.xMax + EsuHudLayout.Scale(7f),
                row.y,
                Mathf.Max(0f, row.xMax - iconRect.xMax - EsuHudLayout.Scale(12f)),
                row.height);
            GUI.Label(textRect, label ?? string.Empty, SingleLineRowStyle(textStyle));
        }

        private static GUIStyle SingleLineRowStyle(GUIStyle baseStyle)
        {
            var style = new GUIStyle(baseStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            style.normal.background = null;
            return style;
        }

        private static string AutomationTargetIconKey(AutomationTarget target) =>
            target?.IsController == true ? ControllerIconKey(target.Controller) : CategoryIconKey(target?.Category ?? AutomationTargetCategory.Other);

        private static string ControllerIconKey(AutomationControllerDescriptor descriptor)
        {
            if (descriptor == null)
                return "cube";

            switch (descriptor.Kind)
            {
                case AutomationControllerKind.Breadboard:
                    return "build";
                case AutomationControllerKind.AiBreadboard:
                    return "settings";
                case AutomationControllerKind.Acb:
                    return "gear";
                case AutomationControllerKind.AcbController:
                    return "anchor";
                case AutomationControllerKind.MissileBreadboard:
                    return "cone";
                default:
                    return "cube";
            }
        }

        private static string CategoryIconKey(AutomationTargetCategory category)
        {
            switch (category)
            {
                case AutomationTargetCategory.All:
                    return "outliner";
                case AutomationTargetCategory.Controllers:
                    return "build";
                case AutomationTargetCategory.AcbActions:
                    return "gear";
                case AutomationTargetCategory.BreadboardReadable:
                    return "visibility";
                case AutomationTargetCategory.BreadboardWritable:
                    return "save";
                case AutomationTargetCategory.Movement:
                case AutomationTargetCategory.Pistons:
                    return "move";
                case AutomationTargetCategory.Subobjects:
                case AutomationTargetCategory.DoorsDocking:
                    return "anchor";
                case AutomationTargetCategory.Utility:
                    return "settings";
                case AutomationTargetCategory.Media:
                case AutomationTargetCategory.SoundDisplay:
                    return "camera";
                case AutomationTargetCategory.Spinblocks:
                    return "rotate";
                case AutomationTargetCategory.TurretsWeapons:
                    return "focus";
                case AutomationTargetCategory.Propulsion:
                    return "path";
                case AutomationTargetCategory.Pumps:
                    return "material";
                case AutomationTargetCategory.ControlSurfaces:
                    return "axis";
                case AutomationTargetCategory.Ai:
                    return "settings";
                case AutomationTargetCategory.Missiles:
                    return "cone";
                case AutomationTargetCategory.Lights:
                    return "paint";
                case AutomationTargetCategory.ShieldsDefence:
                    return "lock";
                case AutomationTargetCategory.Detection:
                    return "visibility";
                case AutomationTargetCategory.ResourcePower:
                    return "build";
                default:
                    return "cube";
            }
        }

        private void DrawLeftPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, _leftPanelRect.width - inset * 2f, _leftPanelRect.height - inset * 2f));
            DrawAutomationPanelHeader("Automation Editor", "build", ref _showLeftPanel, "Hide the Automation Editor panel.");
            DecorationEditorTheme.Separator();
            float scrollHeight = Mathf.Max(
                EsuHudLayout.Scale(160f),
                _leftPanelRect.height - inset * 2f - EsuHudLayout.Scale(82f));
            _leftScroll = GUILayout.BeginScrollView(
                _leftScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(scrollHeight));
            LabelRow("Tool", ToolLabel(_tool));
            LabelRow("Filter", AutomationTargetCatalog.CategoryLabel(_filter));
            LabelRow("Targets", _targets.Count.ToString("N0", CultureInfo.InvariantCulture));
            LabelRow("Visible links", SelectedLinks.Count.ToString("N0", CultureInfo.InvariantCulture));
            DrawWorkspaceGuide();
            if (_selectedController == null)
            {
                DrawCompactIconHeader("No controller selected", "build", DecorationEditorTheme.SubHeader);
                GUILayout.Label("Select a Breadboard/ACB in the world or place one from the right panel.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                DrawCompactIconHeader("Selected controller", AutomationTargetIconKey(_selectedController), DecorationEditorTheme.SubHeader);
                LabelRow("Block", _selectedController.Label);
                LabelRow("Type", _selectedController.Controller?.ClassName ?? _selectedController.RuntimeType);
                LabelRow("Cell", FormatCell(_selectedController.LocalPosition));
                if (AutomationGUILayoutButton(
                        new GUIContent("Open editor", DecorationEditorIconCatalog.Get("open"), "Open the graph/code editor for the selected controller."),
                        DecorationEditorTheme.ToolButton(_editorOpen),
                        GUILayout.Height(EsuHudLayout.Scale(28f))))
                {
                    TryOpenEditor();
                }
                if (AutomationGUILayoutButton(
                        new GUIContent("Clear links", DecorationEditorIconCatalog.Get("delete"), "Remove every linked target from the selected controller."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(28f))))
                    ClearSelectedLinks();
                DrawRuntimeDiagnosticsPanel();
            }

            DecorationEditorTheme.Separator();
            DrawCompactIconHeader("Linked targets", "anchor", DecorationEditorTheme.SubHeader);
            IReadOnlyList<AutomationLink> selectedLinks = SelectedLinks;
            if (selectedLinks.Count == 0)
                GUILayout.Label("No targets linked to the selected controller.", DecorationEditorTheme.MiniWrap);
            foreach (AutomationLink link in selectedLinks)
            {
                DrawLinkedTargetListRow(link);
            }

            DrawSelectedLinkedTargetInspector();

            GUILayout.EndScrollView();
            DecorationEditorTheme.Separator();
            GUILayout.Label(_status, DecorationEditorTheme.Status);
            GUI.DragWindow(new Rect(0f, 0f, _leftPanelRect.width, EsuHudLayout.Scale(34f)));
            GUILayout.EndArea();
        }

        private void DrawWorkspaceGuide()
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            DrawCompactIconHeader("Workspace guide", "focus", DecorationEditorTheme.SubHeader);
            LabelRow("Stage", WorkspaceStageLabel());
            LabelRow("Native data", WorkspaceNativeSurfaceLine());
            GUILayout.Label("Next: " + NextSafeActionLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label("Safety: " + WorkspaceSafetyLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label("System Blocks: planned nested graphs with exposed ports and native lowering.", DecorationEditorTheme.MiniWrap);
        }

        private void DrawLinkedTargetListRow(AutomationLink link)
        {
            if (link == null)
                return;

            bool selected = string.Equals(_selectedLinkTargetKey, link.TargetKey, StringComparison.Ordinal);
            float rowHeight = EsuHudLayout.Scale(28f);
            float gap = EsuHudLayout.Scale(4f);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                rowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));
            GUI.Label(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);

            float buttonHeight = Mathf.Max(EsuHudLayout.Scale(22f), row.height - EsuHudLayout.Scale(4f));
            float buttonY = row.y + (row.height - buttonHeight) * 0.5f;
            float removeWidth = EsuHudLayout.Scale(78f);
            float inspectWidth = EsuHudLayout.Scale(76f);
            Rect removeRect = new Rect(
                row.xMax - removeWidth - gap,
                buttonY,
                removeWidth,
                buttonHeight);
            Rect inspectRect = new Rect(
                removeRect.x - inspectWidth - gap,
                buttonY,
                inspectWidth,
                buttonHeight);
            Rect labelRect = new Rect(
                row.x,
                row.y,
                Mathf.Max(0f, inspectRect.x - row.x - gap),
                row.height);

            DrawAutomationSingleLineIconRow(
                labelRect,
                AutomationTargetIconKey(link.Target),
                link.IsStale ? link.TargetLabel + " (missing)" : link.TargetLabel,
                link.IsStale ? DecorationEditorTheme.Warning : DecorationEditorTheme.Mini);
            if (Event.current != null &&
                Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                labelRect.Contains(Event.current.mousePosition))
            {
                _selectedLinkTargetKey = link.TargetKey;
                Event.current.Use();
            }

            EsuCursorTooltip.Register(labelRect, "Inspect this linked target.");
            if (AutomationGUIButton(
                    inspectRect,
                    new GUIContent("Inspect", DecorationEditorIconCatalog.Get("focus"), "Inspect this linked target."),
                    DecorationEditorTheme.Button))
            {
                _selectedLinkTargetKey = link.TargetKey;
            }

            if (AutomationGUIButton(
                    removeRect,
                    new GUIContent("Remove", DecorationEditorIconCatalog.Get("delete"), "Remove this linked target."),
                    DecorationEditorTheme.Button))
            {
                _links.Remove(link);
                if (selected)
                    _selectedLinkTargetKey = string.Empty;
                if (string.Equals(_automationCodeOutputTargetKey, link.TargetKey, StringComparison.Ordinal))
                    _automationCodeOutputTargetKey = string.Empty;
                return;
            }

            TryOpenAutomationRowContextMenu(
                row,
                mouse => OpenAutomationLinkContextMenu(link, mouse));
        }

        private void DrawSelectedLinkedTargetInspector()
        {
            AutomationLink link = SelectedLink();
            if (link == null)
            {
                if (SelectedLinks.Count > 0)
                    GUILayout.Label("Inspect a linked target to see proxy warnings and target details.", DecorationEditorTheme.MiniWrap);
                return;
            }

            GUILayout.Space(EsuHudLayout.Scale(6f));
            DrawCompactIconHeader("Selected linked target", AutomationTargetIconKey(link.Target), DecorationEditorTheme.SubHeader);
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label(link.TargetLabel + (link.IsStale ? " (missing)" : string.Empty), link.IsStale ? DecorationEditorTheme.Warning : DecorationEditorTheme.Body);
            if (link.Target == null)
            {
                GUILayout.Label("This target is no longer in the live target catalog. Remove and relink it if the block was replaced.", DecorationEditorTheme.Warning);
            }
            else
            {
                LabelRow("Category", AutomationTargetCatalog.CategoryLabel(link.Target.Category));
                LabelRow("Roles", AutomationTargetCatalog.RoleLabel(link.Target));
                LabelRow("Runtime", link.Target.RuntimeType);
                LabelRow("Cell", FormatCell(link.Target.LocalPosition));
                GUILayout.Label(ProxyHint(link.Target), DecorationEditorTheme.MiniWrap);
                GUILayout.Label(ProxyPropertyHint(link.Target), DecorationEditorTheme.MiniWrap);
                GUILayout.Label(LinkedTargetWarning(link.Target), DecorationEditorTheme.MiniWrap);
            }

            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Open graph", DecorationEditorIconCatalog.Get("open"), "Open the graph editor for this link."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                TryOpenEditor(AutomationEditorPage.Graph);
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Remove link", DecorationEditorIconCatalog.Get("delete"), "Remove this linked target."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _links.Remove(link);
                _selectedLinkTargetKey = string.Empty;
                if (string.Equals(_automationCodeOutputTargetKey, link.TargetKey, StringComparison.Ordinal))
                    _automationCodeOutputTargetKey = string.Empty;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawRuntimeDiagnosticsPanel()
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            DrawCompactIconHeader("Runtime checks", "settings", DecorationEditorTheme.SubHeader);
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            if (AutomationGUILayoutButton(
                    new GUIContent("Run checks", DecorationEditorIconCatalog.Get("settings"), "Run live runtime checks for the selected controller."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                RunAutomationRuntimeDiagnostics();
            }

            bool current =
                _selectedController != null &&
                string.Equals(
                    _lastRuntimeDiagnosticsControllerKey,
                    _selectedController.StableKey,
                    StringComparison.Ordinal);
            if (!current || _lastRuntimeDiagnostics.IsEmpty)
            {
                GUILayout.Label(
                    "Checks use the live FtD controller instance and same-value writes to verify reflective access without changing craft behavior.",
                    DecorationEditorTheme.MiniWrap);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(
                _lastRuntimeDiagnostics.Summary,
                _lastRuntimeDiagnostics.HasFailures || _lastRuntimeDiagnostics.HasWarnings
                    ? DecorationEditorTheme.Warning
                    : DecorationEditorTheme.Status);
            int shown = Math.Min(_lastRuntimeDiagnostics.Lines.Count, 8);
            for (int index = 0; index < shown; index++)
            {
                string line = _lastRuntimeDiagnostics.Lines[index];
                bool issue = line.StartsWith("WARN:", StringComparison.Ordinal) ||
                             line.StartsWith("FAIL:", StringComparison.Ordinal);
                GUILayout.Label(
                    line,
                    issue ? DecorationEditorTheme.Warning : DecorationEditorTheme.MiniWrap);
            }

            if (_lastRuntimeDiagnostics.Lines.Count > shown)
            {
                GUILayout.Label(
                    "+" +
                    (_lastRuntimeDiagnostics.Lines.Count - shown).ToString(CultureInfo.InvariantCulture) +
                    " more line(s) in the ESU runtime log.",
                    DecorationEditorTheme.MiniWrap);
            }

            DrawRuntimeValidationEvidenceRows();
            DrawRuntimeValidationControls();
            GUILayout.EndVertical();
        }

        private void DrawRuntimeValidationEvidenceRows()
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Live evidence gates", DecorationEditorTheme.Mini);
            DrawRuntimeEvidenceRow(
                "Native graph fingerprint",
                _lastRuntimeDiagnostics.HasNativePersistenceFingerprint,
                _lastRuntimeDiagnostics.HasNativePersistenceFingerprint
                    ? _lastRuntimeDiagnostics.NativePersistenceFingerprint
                    : "Missing; run checks on a Breadboard controller.");
            DrawRuntimeEvidenceRow(
                "Generic proxy fingerprint",
                _lastRuntimeDiagnostics.HasGenericProxyFingerprint,
                _lastRuntimeDiagnostics.HasGenericProxyFingerprint
                    ? _lastRuntimeDiagnostics.GenericProxyFingerprint
                    : "Missing; create a Generic Getter/Setter proxy or compile a linked output setter.");
            bool switchReady =
                _lastRuntimeDiagnostics.SwitchProbeRan &&
                _lastRuntimeDiagnostics.SwitchFailExpressionReady;
            string switchDetail = _lastRuntimeDiagnostics.SwitchProbeRan
                ? (_lastRuntimeDiagnostics.SwitchFailExpressionReady ? "Ready" : "Not ready") +
                  ", max visible inputs " +
                  _lastRuntimeDiagnostics.SwitchMaxVisibleInputs.ToString(CultureInfo.InvariantCulture) +
                  "."
                : "Not probed.";
            DrawRuntimeEvidenceRow(
                "Switch fail-expression readiness",
                switchReady,
                switchDetail);
        }

        private static void DrawRuntimeEvidenceRow(
            string label,
            bool passed,
            string detail)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                passed ? "OK" : "WAIT",
                passed ? DecorationEditorTheme.Status : DecorationEditorTheme.Warning,
                GUILayout.Width(EsuHudLayout.Scale(42f)));
            GUILayout.BeginVertical();
            GUILayout.Label(label ?? string.Empty, DecorationEditorTheme.Mini);
            GUILayout.Label(
                detail ?? string.Empty,
                passed ? DecorationEditorTheme.MiniWrap : DecorationEditorTheme.Warning);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawRuntimeValidationControls()
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Save/reload validation", DecorationEditorTheme.Mini);
            bool selectedBreadboard =
                _selectedController != null &&
                IsBreadboardController(_selectedController.Controller);
            AutomationLink validationOutput = ValidationOutputLink();
            bool canPrepareValidationGraph = selectedBreadboard && validationOutput?.Target != null;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canPrepareValidationGraph;
            if (AutomationGUILayoutButton(
                    new GUIContent("Prepare validation graph", DecorationEditorIconCatalog.Get("create"), "Create validation graph nodes for save/reload checks."),
                    canPrepareValidationGraph ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                PrepareAutomationValidationGraph();
            }
            GUI.enabled = previous;
            GUILayout.Label(
                "Requires a writable linked target, then creates expression-else Switch proof nodes and a target-specific Generic Setter proxy for live save/reload checks.",
                DecorationEditorTheme.MiniWrap);
            if (selectedBreadboard && validationOutput?.Target == null)
            {
                GUILayout.Label(
                    "Link at least one Breadboard-writable world target before preparing the validation graph.",
                    DecorationEditorTheme.Warning);
            }

            if (!_lastRuntimeDiagnostics.HasNativePersistenceFingerprint)
            {
                GUILayout.Label(
                    "Breadboard persistence fingerprints appear after running checks on a Breadboard controller.",
                    DecorationEditorTheme.MiniWrap);
                return;
            }

            bool completeEvidence = _lastRuntimeDiagnostics.HasCompleteValidationEvidence;
            if (!completeEvidence)
            {
                GUILayout.Label(
                    "Complete all live evidence gates before capturing or comparing a save/reload baseline.",
                    DecorationEditorTheme.Warning);
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = previous && completeEvidence;
            if (AutomationGUILayoutButton(
                    new GUIContent("Capture baseline", DecorationEditorIconCatalog.Get("save"), "Capture the current Automation validation baseline."),
                    completeEvidence ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CaptureRuntimeValidationBaseline();
            }

            GUI.enabled = previous && s_validationBaseline != null && completeEvidence;
            if (AutomationGUILayoutButton(
                    new GUIContent("Compare baseline", DecorationEditorIconCatalog.Get("focus"), "Compare this controller against the captured baseline."),
                    s_validationBaseline == null || !completeEvidence
                        ? DecorationEditorTheme.DisabledButton
                        : DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CompareRuntimeValidationBaseline();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            if (s_validationBaseline != null)
            {
                GUILayout.Label(
                    "Baseline: " +
                    s_validationBaseline.ControllerLabel +
                    " " +
                    s_validationBaseline.NativePersistenceFingerprint,
                    DecorationEditorTheme.MiniWrap);
            }

            if (!string.IsNullOrWhiteSpace(_validationCompareStatus))
                GUILayout.Label(_validationCompareStatus, DecorationEditorTheme.MiniWrap);
        }

        private void PrepareAutomationValidationGraph()
        {
            if (_selectedController == null ||
                !IsBreadboardController(_selectedController.Controller))
            {
                _status = "Select a Breadboard controller before preparing validation graph nodes.";
                return;
            }

            AutomationLink writable = ValidationOutputLink();
            if (writable?.Target == null)
            {
                _status = "Link a Breadboard-writable world target before preparing validation graph nodes.";
                _validationCompareStatus = _status;
                return;
            }

            int recipeIndex = RecipeIndexForLabel("Validation proof");
            if (recipeIndex >= 0)
            {
                _automationCodeRecipeIndex = recipeIndex;
                _automationCodeText = s_codeRecipes[recipeIndex].Code;
            }

            _automationCodeOutputTargetKey = writable.TargetKey;
            _selectedLinkTargetKey = writable.TargetKey;

            bool compiled = CompileAutomationCodeExpression();
            string compileStatus = _status;
            if (!compiled || !_lastCompileBoundOutput)
            {
                _validationCompareStatus =
                    "Validation graph preparation failed: " +
                    compileStatus;
                return;
            }

            RunAutomationRuntimeDiagnostics();
            _validationCompareStatus =
                "Validation graph prepared. Capture baseline, save/reload, run checks, then compare baseline.";
            _status = compileStatus + " " + _lastRuntimeDiagnostics.Summary;
        }

        private void CaptureRuntimeValidationBaseline()
        {
            if (_selectedController == null ||
                !_lastRuntimeDiagnostics.HasNativePersistenceFingerprint)
            {
                _validationCompareStatus = "Run checks on a Breadboard controller before capturing a baseline.";
                return;
            }

            if (!_lastRuntimeDiagnostics.HasCompleteValidationEvidence)
            {
                _validationCompareStatus = "Complete all live evidence gates before capturing a baseline.";
                return;
            }

            s_validationBaseline = AutomationValidationBaseline.From(
                _selectedController,
                _lastRuntimeDiagnostics);
            _validationCompareStatus =
                "Captured validation baseline " +
                s_validationBaseline.NativePersistenceFingerprint +
                ".";
            _status = _validationCompareStatus;
            EsuRuntimeLog.Info(
                "Automation Editor",
                "Captured Automation validation baseline",
                s_validationBaseline.Describe());
        }

        private void CompareRuntimeValidationBaseline()
        {
            if (s_validationBaseline == null)
            {
                _validationCompareStatus = "No Automation validation baseline has been captured.";
                return;
            }

            if (_selectedController == null ||
                !_lastRuntimeDiagnostics.HasNativePersistenceFingerprint)
            {
                _validationCompareStatus = "Run checks on the reloaded Breadboard before comparing the baseline.";
                return;
            }

            if (!_lastRuntimeDiagnostics.HasCompleteValidationEvidence)
            {
                _validationCompareStatus = "Run checks on the reloaded Breadboard until all live evidence gates are OK before comparing the baseline.";
                return;
            }

            AutomationValidationBaseline current = AutomationValidationBaseline.From(
                _selectedController,
                _lastRuntimeDiagnostics);
            string message = s_validationBaseline.Compare(current);
            _validationCompareStatus = message;
            _status = message;
            string detail =
                "Baseline: " +
                s_validationBaseline.Describe() +
                "\nCurrent: " +
                current.Describe();
            if (message.StartsWith("Automation validation baseline match:", StringComparison.Ordinal))
                EsuRuntimeLog.Info("Automation Editor", message, detail);
            else
                EsuRuntimeLog.Warning("Automation Editor", message, detail);
        }

        private void RunAutomationRuntimeDiagnostics()
        {
            if (_selectedController == null)
            {
                _status = "Select an Automation controller before running runtime checks.";
                return;
            }

            AutomationTarget[] linkedTargets = SelectedLinks
                .Select(link => link.Target)
                .ToArray();
            _lastRuntimeDiagnostics = AutomationRuntimeDiagnostics.Run(_selectedController, linkedTargets);
            _lastRuntimeDiagnosticsControllerKey = _selectedController.StableKey;
            _status = _lastRuntimeDiagnostics.Summary;
            string detail = string.Join("\n", _lastRuntimeDiagnostics.Lines.ToArray());
            if (_lastRuntimeDiagnostics.HasFailures)
            {
                EsuRuntimeLog.Warning("Automation Editor", _lastRuntimeDiagnostics.Summary, detail);
            }
            else if (_lastRuntimeDiagnostics.HasWarnings)
            {
                EsuRuntimeLog.Warning("Automation Editor", _lastRuntimeDiagnostics.Summary, detail);
            }
            else
            {
                EsuRuntimeLog.Info("Automation Editor", _lastRuntimeDiagnostics.Summary, detail);
            }
        }

        private void DrawRightPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _rightPanelRect.width, _rightPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, _rightPanelRect.width - inset * 2f, _rightPanelRect.height - inset * 2f));
            DrawAutomationPanelHeader("Automation Blocks", "cube", ref _showRightPanel, "Hide the Automation Blocks panel.");
            DecorationEditorTheme.Separator();
            float scrollHeight = Mathf.Max(
                EsuHudLayout.Scale(180f),
                _rightPanelRect.height - inset * 2f - EsuHudLayout.Scale(48f));
            _rightScroll = GUILayout.BeginScrollView(
                _rightScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(scrollHeight));

            if (DrawAutomationSectionHeader("Controllers", "build", ref _showBlocksSection, "Show or hide the Automation controller palette and craft controller list."))
            {
                foreach (AutomationControllerDescriptor descriptor in AutomationControllerCatalog.All)
                    DrawControllerPaletteRow(descriptor);

                DecorationEditorTheme.Separator();
                DrawControllerIndex();
            }

            DecorationEditorTheme.Separator();
            if (DrawAutomationSectionHeader("Target filters", "filter", ref _showFilterSection, "Show or hide Automation target search, filters, and world target list."))
            {
                DrawTargetSearchControls(drawHeader: false);
                DecorationEditorTheme.Separator();
                foreach (AutomationTargetCategory category in AutomationTargetCatalog.FilterOrder)
                {
                    int count = CountTargets(category);
                    string label = AutomationTargetCatalog.CategoryLabel(category) +
                                   " (" +
                                   count.ToString("N0", CultureInfo.InvariantCulture) +
                                   ")";
                    if (AutomationGUILayoutButton(
                            new GUIContent(
                                label,
                                DecorationEditorIconCatalog.Get(CategoryIconKey(category)),
                                "Filter Automation targets by " + AutomationTargetCatalog.CategoryLabel(category) + "."),
                            DecorationEditorTheme.ToolButton(_filter == category),
                            GUILayout.Height(EsuHudLayout.Scale(25f))))
                    {
                        _filter = category;
                    }
                }

                DecorationEditorTheme.Separator();
                IReadOnlyList<AutomationTarget> visibleTargets = FilteredWorldTargets();
                DrawCompactIconHeader(
                    "World targets (" +
                    Math.Min(visibleTargets.Count, 80).ToString("N0", CultureInfo.InvariantCulture) +
                    "/" +
                    visibleTargets.Count.ToString("N0", CultureInfo.InvariantCulture) +
                    ")",
                    "outliner",
                    DecorationEditorTheme.SubHeader);
                if (visibleTargets.Count == 0)
                {
                    GUILayout.Label(
                        string.IsNullOrWhiteSpace(_targetSearchText)
                            ? "No targets match the selected filter."
                            : "No targets match the current search and filter.",
                        DecorationEditorTheme.MiniWrap);
                }

                foreach (AutomationTarget target in visibleTargets.Take(80))
                {
                    DrawTargetListRow(target);
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _rightPanelRect.width, EsuHudLayout.Scale(34f)));
            GUILayout.EndArea();
        }

        private static void DrawAutomationPanelHeader(
            string text,
            string iconKey,
            ref bool panelVisible,
            string hideTooltip)
        {
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(text, iconKey, DecorationEditorTheme.Header);
            if (AutomationGUILayoutButton(
                    new GUIContent("Hide", DecorationEditorIconCatalog.Get("close"), hideTooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                panelVisible = false;
            }

            GUILayout.EndHorizontal();
        }

        private static bool DrawAutomationSectionHeader(string text, string iconKey, ref bool sectionVisible, string tooltip)
        {
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(text, iconKey, DecorationEditorTheme.SubHeader);
            if (AutomationGUILayoutButton(
                    new GUIContent(
                        sectionVisible ? "Hide list" : "Show list",
                        DecorationEditorIconCatalog.Get(sectionVisible ? "close" : iconKey),
                        tooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                sectionVisible = !sectionVisible;
            }
            GUILayout.EndHorizontal();
            return sectionVisible;
        }

        private void DrawTargetSearchControls(bool drawHeader = true)
        {
            if (drawHeader)
                DrawCompactIconHeader("Target search", "filter", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string next = GUILayout.TextField(
                _targetSearchText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(next, _targetSearchText, StringComparison.Ordinal))
                _targetSearchText = next ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_targetSearchText);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", DecorationEditorIconCatalog.Get("close"), "Clear Automation target search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _targetSearchText = string.Empty;
                _status = "Automation target search cleared.";
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Filter targets by label, class, category, cell, controller GUID, or ACB/Breadboard role.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawControllerPaletteRow(AutomationControllerDescriptor descriptor)
        {
            bool available = descriptor.ResolveItemDefinition() != null;
            bool armed = _selectedPlacement == descriptor && _tool == AutomationTool.Place;
            string description = available ? descriptor.Description : "Unavailable in this FtD install.";
            string label = descriptor.Label + (armed ? "  [armed]" : string.Empty);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(48f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(48f)));
            bool previous = GUI.enabled;
            GUI.enabled = previous && available;
            bool clicked = GUI.Button(
                row,
                GUIContent.none,
                _selectedPlacement == descriptor
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row);
            GUI.enabled = previous;
            DrawAutomationIconRow(
                row,
                ControllerIconKey(descriptor),
                label,
                description,
                armed ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini,
                available ? DecorationEditorTheme.MiniWrap : DecorationEditorTheme.Warning);
            EsuCursorTooltip.Register(row, "Click to arm this Automation controller for placement.");
            if (TryOpenAutomationRowContextMenu(
                    row,
                    mouse => OpenAutomationPlacementContextMenu(descriptor, mouse)))
            {
                return;
            }

            if (clicked)
            {
                _selectedPlacement = descriptor;
                _tool = AutomationTool.Place;
                _status = PlacementArmedStatus();
            }
        }

        private void DrawControllerIndex()
        {
            AutomationTarget[] controllers = _targets
                .Where(target => target?.IsController == true)
                .ToArray();
            GUILayout.Label(
                "Controllers on craft (" +
                controllers.Length.ToString("N0", CultureInfo.InvariantCulture) +
                ")",
                DecorationEditorTheme.SubHeader);
            if (controllers.Length == 0)
            {
                GUILayout.Label("No Breadboard/ACB controller blocks were found on the live craft.", DecorationEditorTheme.MiniWrap);
                return;
            }

            int shown = 0;
            int groupIndex = 0;
            foreach (IGrouping<AllConstruct, AutomationTarget> group in controllers
                         .GroupBy(target => target.Construct)
                         .OrderBy(group => ConstructGroupSortKey(group.Key), StringComparer.OrdinalIgnoreCase))
            {
                groupIndex++;
                AutomationTarget[] groupTargets = group
                    .OrderBy(target => target.Controller?.ShortLabel ?? target.Label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(target => FormatCell(target.LocalPosition), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                GUILayout.Label(
                    ConstructGroupLabel(group.Key, groupIndex) +
                    " (" +
                    groupTargets.Length.ToString("N0", CultureInfo.InvariantCulture) +
                    ")",
                    DecorationEditorTheme.Mini);
                foreach (AutomationTarget target in groupTargets.Take(6))
                {
                    DrawControllerIndexRow(target);
                    shown++;
                }

                if (shown >= 18)
                    break;
            }

            if (controllers.Length > shown)
            {
                GUILayout.Label(
                    "+" + (controllers.Length - shown).ToString(CultureInfo.InvariantCulture) + " more controller(s) in the target list.",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawControllerIndexRow(AutomationTarget target)
        {
            bool selected = _selectedController != null &&
                            target.StableKey == _selectedController.StableKey;
            string rowText =
                (target.Controller?.ShortLabel ?? target.Label) +
                "    " +
                FormatCell(target.LocalPosition) +
                (selected ? "    [selected]" : string.Empty);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(28f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(28f)));
            if (GUI.Button(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row))
            {
                SelectAutomationController(target);
            }

            DrawAutomationSingleLineIconRow(
                row,
                AutomationTargetIconKey(target),
                rowText,
                selected ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini);
            EsuCursorTooltip.Register(row, "Click to select this Automation controller.");
            TryOpenAutomationRowContextMenu(
                row,
                mouse => OpenAutomationControllerContextMenu(target, mouse));
        }

        private void DrawTargetListRow(AutomationTarget target)
        {
            if (target == null)
                return;

            bool selected = _selectedController != null &&
                            target.StableKey == _selectedController.StableKey;
            string roleSummary = AutomationTargetCatalog.RoleSummary(target);
            bool linked = IsLinked(_selectedController, target);
            string detail = AutomationTargetCatalog.CategoryLabel(target.Category);
            if (!string.IsNullOrWhiteSpace(roleSummary))
                detail = roleSummary + " | " + detail;
            if (linked)
                detail += " | linked";
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(42f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(42f)));
            if (GUI.Button(
                    row,
                    GUIContent.none,
                    selected || linked ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row))
            {
                HandleTargetRowClick(target);
            }

            DrawAutomationIconRow(
                row,
                AutomationTargetIconKey(target),
                target.Label,
                detail,
                selected || linked ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini,
                DecorationEditorTheme.MiniWrap);
            EsuCursorTooltip.Register(row, TargetRowTooltip(target));
            TryOpenAutomationRowContextMenu(
                row,
                mouse => OpenAutomationTargetContextMenu(target, mouse));
        }

        private static string TargetRowTooltip(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;
            return target.IsController
                ? "Click to select this controller, or link it when the selected controller can drive controller targets."
                : "Click to link or unlink this target with the selected controller.";
        }

        private void HandleTargetRowClick(AutomationTarget target)
        {
            if (target == null)
                return;

            if (target.IsController && !CanLinkControllerTarget(_selectedController, target))
            {
                SelectAutomationController(target);
                return;
            }

            if (_selectedController == null)
            {
                _status = "Select a controller before linking targets.";
                return;
            }

            ToggleLink(_selectedController, target);
        }

        private void DrawEditor(int id)
        {
            GUI.Box(new Rect(0f, 0f, _editorRect.width, _editorRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(10f);
            GUILayout.BeginArea(new Rect(inset, inset, _editorRect.width - inset * 2f, _editorRect.height - inset * 2f));
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(EditorTitle(), "open", DecorationEditorTheme.Header);
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit", DecorationEditorIconCatalog.Get("focus"), "Fit the graph/code editor to the viewport."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
            {
                FitEditorToViewport();
                _status = "Automation editor fitted to the available viewport.";
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Close", DecorationEditorIconCatalog.Get("close"), "Close the fullscreen Automation editor."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(68f))))
                CloseEditor();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Graph", DecorationEditorIconCatalog.Get("outliner"), "Edit Automation graph nodes."),
                    DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.Graph)))
                _editorPage = AutomationEditorPage.Graph;
            if (AutomationGUILayoutButton(
                    new GUIContent("Code", DecorationEditorIconCatalog.Get("settings"), "Generate or edit Automation code recipes."),
                    DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.Code)))
                _editorPage = AutomationEditorPage.Code;
            GUILayout.EndHorizontal();
            DecorationEditorTheme.Separator();
            float scrollHeight = Mathf.Max(
                EsuHudLayout.Scale(180f),
                _editorRect.height - inset * 2f - EsuHudLayout.Scale(152f));
            _editorScroll = GUILayout.BeginScrollView(
                _editorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(scrollHeight));
            if (_editorPage == AutomationEditorPage.Graph)
                DrawGraphEditor();
            else
                DrawCodeEditor();
            GUILayout.EndScrollView();
            DrawEditorBottomStrip();
            GUI.DragWindow(new Rect(0f, 0f, _editorRect.width, EsuHudLayout.Scale(34f)));
            GUILayout.EndArea();
        }

        private void DrawEditorBottomStrip()
        {
            DecorationEditorTheme.Separator();
            GUILayout.BeginHorizontal(DecorationEditorTheme.Row);
            GUILayout.Label(
                "Page: " + (_editorPage == AutomationEditorPage.Graph ? "Graph" : "Code"),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(86f)));
            GUILayout.Label(
                "Links: " + SelectedLinks.Count.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(72f)));
            GUILayout.Label(
                EditorGridStatus(),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(118f)));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                CanRevertLastAutomationCompile()
                    ? "Generated nodes: revert available"
                    : "Native edits apply immediately",
                CanRevertLastAutomationCompile()
                    ? DecorationEditorTheme.Warning
                    : DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(190f)));
            GUILayout.EndHorizontal();
            GUILayout.Label("Next: " + NextSafeActionLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label("Status: " + (_status ?? string.Empty), DecorationEditorTheme.MiniWrap);
        }

        private string EditorGridStatus()
        {
            if (!IsBreadboardController(_selectedController?.Controller) ||
                !AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out _))
            {
                return "Grid: n/a";
            }

            return "Grid: " +
                   (inspector.GridEnabled ? "on " : "off ") +
                   inspector.GridSize.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string EditorTitle()
        {
            return _selectedController == null
                ? "Automation Graph"
                : "Automation Graph - " + _selectedController.Label;
        }

        private void DrawGraphEditor()
        {
            GUILayout.Label("Controller", DecorationEditorTheme.SubHeader);
            if (_selectedController == null)
            {
                GUILayout.Label("No Breadboard/ACB controller selected.", DecorationEditorTheme.MiniWrap);
                return;
            }

            DrawNodeBox(
                _selectedController.Label,
                _selectedController.Controller?.ClassName ?? _selectedController.RuntimeType,
                "world controller");
            DrawBreadboardInspectorSection();
            DrawAcbInspectorSection();
            DrawAcbControllerInspectorSection();
            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("World proxy nodes", DecorationEditorTheme.SubHeader);
            IReadOnlyList<AutomationLink> links = SelectedLinks;
            if (links.Count == 0)
            {
                GUILayout.Label("Link target blocks in the world to create proxy nodes here.", DecorationEditorTheme.MiniWrap);
                return;
            }

            foreach (AutomationLink link in links)
                DrawLinkedAutomationNode(link);
        }

        private void DrawLinkedAutomationNode(AutomationLink link)
        {
            if (link == null)
                return;

            AutomationTarget target = link.Target;
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            GUILayout.Label(link.TargetLabel + (link.IsStale ? " (missing)" : string.Empty), DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                target == null
                    ? "Missing linked target"
                    : AutomationTargetCatalog.CategoryLabel(target.Category),
                target == null ? DecorationEditorTheme.Warning : DecorationEditorTheme.Body);
            GUILayout.Label(ProxyHint(target), DecorationEditorTheme.MiniWrap);
            string propertyHint = ProxyPropertyHint(target);
            if (!string.IsNullOrWhiteSpace(propertyHint))
                GUILayout.Label(propertyHint, DecorationEditorTheme.MiniWrap);
            if (target == null)
            {
                GUILayout.Label(
                    "The target is not in the live target catalog after refresh. Remove and relink it if the block was replaced.",
                    DecorationEditorTheme.Warning);
                GUILayout.EndVertical();
                return;
            }

            if (IsAcbProxyTarget(target))
            {
                DrawLinkedTargetProxyShortcuts(target);
                DrawLinkedAcbTargetNode(target);
            }
            else if (IsAcbControllerBridgeTarget(target))
            {
                DrawLinkedTargetProxyShortcuts(target);
                DrawLinkedAcbControllerTargetNode(target);
            }
            else
            {
                DrawLinkedGenericTargetNode(target);
            }

            GUILayout.EndVertical();
        }

        private void DrawLinkedGenericTargetNode(AutomationTarget target)
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Linked Generic target node", DecorationEditorTheme.Mini);
            DrawLinkedTargetProxyShortcuts(target);
        }

        private void DrawLinkedTargetProxyShortcuts(AutomationTarget target)
        {
            if (target == null)
                return;

            if (!TryCreateSelectedBreadboardInspector(
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason ?? "Select a Breadboard controller before creating proxy nodes.", DecorationEditorTheme.MiniWrap);
                return;
            }

            bool canGetter = inspector.CanAddComponent("GenericBlockGetter");
            bool canSetter = inspector.CanAddComponent("GenericBlockSetter");
            if (!canGetter && !canSetter)
            {
                GUILayout.Label("This board does not advertise Generic Getter or Generic Setter components.", DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginHorizontal();
            if (IsAcbControllerBridgeTarget(target))
            {
                DrawProxyButton(inspector, target, canGetter, canSetter, "Button getter", getter: true, setter: false);
                GUILayout.Label("Keyword output", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(92f)));
            }
            else if (IsAcbProxyTarget(target))
            {
                DrawProxyButton(inspector, target, canGetter, canSetter, "Rule getter", getter: true, setter: false);
                DrawProxyButton(inspector, target, canGetter, canSetter, "Rule setter", getter: false, setter: true);
                DrawProxyButton(inspector, target, canGetter, canSetter, "Both", getter: true, setter: true);
            }
            else
            {
                DrawProxyButton(inspector, target, canGetter, canSetter, "Getter", getter: true, setter: false);
                DrawProxyButton(inspector, target, canGetter, canSetter, "Setter", getter: false, setter: true);
                DrawProxyButton(inspector, target, canGetter, canSetter, "Both", getter: true, setter: true);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private bool TryCreateSelectedBreadboardInspector(
            out AutomationBreadboardInspector inspector,
            out string reason)
        {
            inspector = null;
            reason = null;
            if (!IsBreadboardController(_selectedController?.Controller))
            {
                reason = "Select a Breadboard controller before creating proxy nodes.";
                return false;
            }

            return AutomationBreadboardInspector.TryCreate(
                _selectedController.Block,
                _selectedController.Controller,
                out inspector,
                out reason);
        }

        private void DrawLinkedAcbTargetNode(AutomationTarget target)
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Linked ACB target node", DecorationEditorTheme.Mini);
            if (!AutomationAcbInspector.TryCreate(
                    target.Block,
                    out AutomationAcbInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason ?? "Linked ACB data was not found.", DecorationEditorTheme.Warning);
                return;
            }

            DrawAcbInspectorBody(inspector, "Linked ACB", allowTrigger: false);
        }

        private void DrawLinkedAcbControllerTargetNode(AutomationTarget target)
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Linked ACB Controller node", DecorationEditorTheme.Mini);
            if (!AutomationAcbControllerInspector.TryCreate(
                    target.Block,
                    out AutomationAcbControllerInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason ?? "Linked ACB Controller button data was not found.", DecorationEditorTheme.Warning);
                return;
            }

            DrawAcbControllerInspectorBody(inspector, maxButtons: 3, statusPrefix: "Linked ACB Controller");
        }

        private void DrawCodeEditor()
        {
            GUILayout.Label("Code node compiler", DecorationEditorTheme.SubHeader);
            if (_selectedController == null)
            {
                GUILayout.Label("Select a controller before authoring automation code.", DecorationEditorTheme.MiniWrap);
                return;
            }

            if (!IsBreadboardController(_selectedController.Controller))
            {
                GUILayout.Label("Select a Breadboard controller to compile code into native graph nodes.", DecorationEditorTheme.Warning);
                return;
            }

            EnsureAutomationCodeText();
            DrawAutomationRecipePicker();
            DrawAutomationLinkedIdentifierHints();
            DrawAutomationCodeOutputTargetPicker();
            _automationCodeText = GUILayout.TextArea(
                _automationCodeText,
                DecorationEditorTheme.TextField,
                GUILayout.MinHeight(EsuHudLayout.Scale(220f)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "Compile expression",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(136f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                CompileAutomationCodeExpression();
            }
            if (GUILayout.Button(
                    "reset recipe",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(92f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                _automationCodeText = GeneratedCodeRecipe();
                _status = "Automation code recipe reset.";
            }
            bool guiWasEnabled = GUI.enabled;
            GUI.enabled = guiWasEnabled && CanRevertLastAutomationCompile();
            if (GUILayout.Button(
                    "revert compile",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(112f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                RevertLastAutomationCompile();
            }
            GUI.enabled = guiWasEnabled;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Compiles one expression into a native Maths Evaluator, or if condition / out = expression / else / out = number-or-expression into native Evaluator + Switch nodes. Conditions with one and/or insert a native Logic Gate. ESU binds compiled output to the selected linked target's Generic Setter, creating one when needed.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawAutomationCodeOutputTargetPicker()
        {
            AutomationLink[] writableLinks = SelectedLinks
                .Where(link => link?.Target != null &&
                               AutomationTargetCatalog.IsBreadboardWritableTarget(link.Target))
                .Take(6)
                .ToArray();
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label("Code output target", DecorationEditorTheme.Mini);
            if (writableLinks.Length == 0)
            {
                GUILayout.Label("Link a writable target before compiling if code should drive a world block.", DecorationEditorTheme.MiniWrap);
                _automationCodeOutputTargetKey = string.Empty;
                GUILayout.EndVertical();
                return;
            }

            AutomationLink selected = AutomationCodeOutputLink();
            if (selected == null)
            {
                selected = writableLinks[0];
                _automationCodeOutputTargetKey = selected.TargetKey;
            }

            GUILayout.BeginHorizontal();
            foreach (AutomationLink link in writableLinks)
            {
                bool active = selected != null &&
                              string.Equals(selected.TargetKey, link.TargetKey, StringComparison.Ordinal);
                if (GUILayout.Button(
                        link.TargetLabel,
                        DecorationEditorTheme.ToolButton(active),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    _automationCodeOutputTargetKey = link.TargetKey;
                    _selectedLinkTargetKey = link.TargetKey;
                    _status = "Code output target: " + link.TargetLabel + ".";
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Selected output binds to a target-specific Generic Setter proxy.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawAutomationRecipePicker()
        {
            AutomationCodeRecipe recipe = SelectedCodeRecipe();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Recipe", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            if (GUILayout.Button("<", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                SelectAutomationCodeRecipe(-1, load: false);
            GUILayout.Label(recipe.Label, DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(150f)));
            if (GUILayout.Button(">", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                SelectAutomationCodeRecipe(1, load: false);
            if (GUILayout.Button(
                    "load",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeText = recipe.Code;
                _status = "Loaded " + recipe.Label + " recipe.";
            }
            if (GUILayout.Button(
                    "suggest",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeRecipeIndex = SuggestedAutomationCodeRecipeIndex();
                AutomationCodeRecipe suggested = SelectedCodeRecipe();
                _automationCodeText = suggested.Code;
                _status = "Loaded " + suggested.Label + " recipe.";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawAutomationLinkedIdentifierHints()
        {
            IReadOnlyList<string> identifiers = AutomationCodeLinkedIdentifiers();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Linked identifiers", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(112f)));
            if (identifiers.Count == 0)
            {
                GUILayout.Label("none", DecorationEditorTheme.Mini);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            int shown = Math.Min(identifiers.Count, 5);
            for (int index = 0; index < shown; index++)
            {
                string identifier = identifiers[index];
                if (GUILayout.Button(
                        identifier,
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(98f)),
                        GUILayout.Height(EsuHudLayout.Scale(22f))))
                {
                    InsertAutomationCodeIdentifier(identifier);
                }
            }

            if (identifiers.Count > shown)
            {
                GUILayout.Label(
                    "+" + (identifiers.Count - shown).ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(28f)));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardInspectorSection()
        {
            if (!IsBreadboardController(_selectedController?.Controller))
                return;

            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("Breadboard Inspector", DecorationEditorTheme.SubHeader);
            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason, DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawBreadboardName(inspector);
            DrawBreadboardToggle("Grid", inspector.GridEnabled, value =>
            {
                inspector.GridEnabled = value;
                _status = "Breadboard grid " + (value ? "enabled" : "disabled") + ".";
            });
            DrawFloatBreadboardStepper("Grid size", inspector.GridSize, 5f, value =>
            {
                inspector.GridSize = Mathf.Max(1f, value);
                _status = "Breadboard grid size updated.";
            });
            DrawBreadboardToggle("Help labels", inspector.HelpLabels, value =>
            {
                inspector.HelpLabels = value;
                _status = "Breadboard help labels " + (value ? "enabled" : "disabled") + ".";
            });
            DrawBreadboardToggle("Blur", inspector.Blur, value =>
            {
                inspector.Blur = value;
                _status = "Breadboard blur " + (value ? "enabled" : "disabled") + ".";
            });

            GUILayout.Label(
                "Native graph: " +
                inspector.PackageCount.ToString(CultureInfo.InvariantCulture) +
                " package(s), " +
                inspector.AvailableComponents.Count.ToString(CultureInfo.InvariantCulture) +
                " available component type(s).",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label("Board type: " + inspector.BoardTypeName, DecorationEditorTheme.MiniWrap);
            DrawBreadboardQuickAdds(inspector);
            DrawMissileBreadboardQuickAdds(inspector);
            DrawBreadboardProxyActions(inspector, SelectedLinks);
            DrawBreadboardGraphCanvas(inspector);
            DrawBreadboardComponentList(inspector);
            GUILayout.Label("These edits use FtD's stored Board/IBoard instance, Var.Us settings, and AddComponentCommand/NewPackage path.", DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawAcbInspectorSection()
        {
            if (_selectedController?.Controller?.Kind != AutomationControllerKind.Acb)
                return;

            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("ACB Inspector", DecorationEditorTheme.SubHeader);
            if (!AutomationAcbInspector.TryCreate(
                    _selectedController.Block,
                    out AutomationAcbInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason, DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawAcbInspectorBody(inspector, "ACB", allowTrigger: true);
            GUILayout.EndVertical();
        }

        private void DrawAcbInspectorBody(
            AutomationAcbInspector inspector,
            string statusPrefix,
            bool allowTrigger)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB" : statusPrefix;
            bool enabled = inspector.IsEnabled;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enabled", DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button(
                    enabled ? "On" : "Off",
                    DecorationEditorTheme.ToolButton(enabled),
                    GUILayout.Width(EsuHudLayout.Scale(72f))))
            {
                inspector.IsEnabled = !enabled;
                _status = prefix + " enabled set to " + (!enabled ? "On" : "Off") + ".";
            }
            GUILayout.EndHorizontal();

            DrawIntAcbStepper("Priority", inspector.Priority, 1, value =>
            {
                inspector.Priority = value;
                _status = prefix + " priority set to " + value.ToString(CultureInfo.InvariantCulture) + ".";
            });
            DrawFloatAcbStepper("Affect delay", inspector.AffectDelay, 0.1f, value =>
            {
                inspector.AffectDelay = Mathf.Max(0f, value);
                _status = prefix + " affect delay updated.";
            });
            DrawIntAcbStepper("Affect range", inspector.AffectRange, 10, value =>
            {
                inspector.AffectRange = Math.Max(0, value);
                _status = prefix + " affect range updated.";
            });
            DrawFloatAcbStepper("Min interval", inspector.MinActivationInterval, 0.1f, value =>
            {
                inspector.MinActivationInterval = Mathf.Max(0f, value);
                _status = prefix + " minimum activation interval updated.";
            });
            DrawAcbSearchPattern(inspector, prefix);
            DrawAcbRuleSection(inspector, prefix);
            if (allowTrigger &&
                GUILayout.Button(
                        "Trigger test now",
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                _status = inspector.TriggerTestNow()
                    ? prefix + " test trigger sent."
                    : prefix + " test trigger is unavailable.";
            }
            GUILayout.Label("Edits write to this ACB's ControlBlockData package through FtD's Var.Us path.", DecorationEditorTheme.MiniWrap);
        }

        private void DrawAcbRuleSection(
            AutomationAcbInspector inspector,
            string statusPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB" : statusPrefix;
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("ACB rule", DecorationEditorTheme.Mini);
            if (!inspector.HasRuleData)
            {
                GUILayout.Label("ACB action/condition packages were not found on this instance.", DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawAcbEnumCycler(
                "Action",
                inspector.ActionTypeLabel,
                () =>
                {
                    _status = inspector.CycleActionType(-1)
                        ? prefix + " action type changed."
                        : prefix + " action type is unavailable.";
                },
                () =>
                {
                    _status = inspector.CycleActionType(1)
                        ? prefix + " action type changed."
                        : prefix + " action type is unavailable.";
                });
            if (!string.IsNullOrWhiteSpace(inspector.ActionDescription))
                GUILayout.Label(inspector.ActionDescription, DecorationEditorTheme.MiniWrap);
            DrawDoubleAcbStepper("Action value", inspector.ActionVariable, 1d, value =>
            {
                inspector.ActionVariable = value;
                _status = prefix + " action variable updated.";
            });
            GUILayout.EndVertical();

            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawAcbEnumCycler(
                "Condition",
                inspector.ConditionTypeLabel,
                () =>
                {
                    _status = inspector.CycleConditionType(-1)
                        ? prefix + " condition type changed."
                        : prefix + " condition type is unavailable.";
                },
                () =>
                {
                    _status = inspector.CycleConditionType(1)
                        ? prefix + " condition type changed."
                        : prefix + " condition type is unavailable.";
                });
            if (!string.IsNullOrWhiteSpace(inspector.ConditionDescription))
                GUILayout.Label(inspector.ConditionDescription, DecorationEditorTheme.MiniWrap);
            DrawBreadboardToggle("Invert", inspector.ConditionInverted, value =>
            {
                inspector.ConditionInverted = value;
                _status = prefix + " condition inversion " + (value ? "enabled" : "disabled") + ".";
            });
            DrawDoubleAcbStepper("Min value", inspector.ConditionMinVariable, 1d, value =>
            {
                inspector.ConditionMinVariable = value;
                _status = prefix + " condition minimum updated.";
            });
            DrawDoubleAcbStepper("Max value", inspector.ConditionMaxVariable, 1d, value =>
            {
                inspector.ConditionMaxVariable = value;
                _status = prefix + " condition maximum updated.";
            });
            GUILayout.EndVertical();
        }

        private void DrawAcbControllerInspectorSection()
        {
            if (_selectedController?.Controller?.Kind != AutomationControllerKind.AcbController)
                return;

            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("ACB Controller Inspector", DecorationEditorTheme.SubHeader);
            if (!AutomationAcbControllerInspector.TryCreate(
                    _selectedController.Block,
                    out AutomationAcbControllerInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason, DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawAcbControllerInspectorBody(inspector, maxButtons: 6, statusPrefix: "ACB Controller");
            GUILayout.EndVertical();
        }

        private void DrawAcbControllerInspectorBody(
            AutomationAcbControllerInspector inspector,
            int maxButtons,
            string statusPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB Controller" : statusPrefix;
            GUILayout.Label(
                inspector.Buttons.Count.ToString(CultureInfo.InvariantCulture) +
                " button data item(s). Type: " +
                inspector.ControllerTypeName,
                DecorationEditorTheme.MiniWrap);

            int shown = Math.Min(inspector.Buttons.Count, Math.Max(1, maxButtons));
            for (int index = 0; index < shown; index++)
                DrawAcbControllerButtonEditor(inspector, inspector.Buttons[index], prefix);

            if (inspector.Buttons.Count > shown)
            {
                GUILayout.Label(
                    "Showing first " +
                    shown.ToString(CultureInfo.InvariantCulture) +
                    " ACB Controller buttons.",
                    DecorationEditorTheme.MiniWrap);
            }

            GUILayout.Label(
                "Breadboard output uses FtD's ACB Controller button keyword path, so Generic Getter nodes can read the button signal by keyword.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawAcbControllerButtonEditor(
            AutomationAcbControllerInspector inspector,
            AutomationAcbControllerButtonSummary button,
            string statusPrefix)
        {
            if (button == null)
                return;

            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB Controller" : statusPrefix;
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label(
                "Button " +
                (button.Index + 1).ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Body);
            DrawAcbControllerTextField(inspector, button, "Name", button.ButtonName, inspector.TrySetButtonName, prefix);
            DrawAcbControllerTextField(inspector, button, "Keyword", button.Keyword, inspector.TrySetKeyword, prefix);
            DrawBreadboardToggle("Breadboard out", button.IsUsedForBreadboard, value =>
            {
                _status = inspector.TrySetBreadboardOutput(button, value, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " Breadboard output.";
            });
            DrawIntAcbStepper("Shape", button.ShapeId, 1, value =>
            {
                _status = inspector.TrySetShapeId(button, value, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " shape.";
            });
            DrawAcbControllerColorEditor(inspector, button, prefix);
            if (!string.IsNullOrWhiteSpace(button.DataTypeName))
                GUILayout.Label(button.DataTypeName, DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawAcbControllerTextField(
            AutomationAcbControllerInspector inspector,
            AutomationAcbControllerButtonSummary button,
            string label,
            string value,
            AcbControllerTextApply apply,
            string statusPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB Controller" : statusPrefix;
            GUILayout.Label(label, DecorationEditorTheme.Mini);
            string previous = value ?? string.Empty;
            string next = GUILayout.TextField(previous, DecorationEditorTheme.TextField);
            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                _status = apply(button, next, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " text.";
            }
        }

        private void DrawAcbControllerColorEditor(
            AutomationAcbControllerInspector inspector,
            AutomationAcbControllerButtonSummary button,
            string statusPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB Controller" : statusPrefix;
            Color color = button.ButtonColor;
            DrawFloatAcbStepper("Red", color.r, 0.05f, value =>
            {
                Color next = new Color(Mathf.Clamp01(value), color.g, color.b, color.a);
                _status = inspector.TrySetButtonColor(button, next, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " color.";
            });
            DrawFloatAcbStepper("Green", color.g, 0.05f, value =>
            {
                Color next = new Color(color.r, Mathf.Clamp01(value), color.b, color.a);
                _status = inspector.TrySetButtonColor(button, next, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " color.";
            });
            DrawFloatAcbStepper("Blue", color.b, 0.05f, value =>
            {
                Color next = new Color(color.r, color.g, Mathf.Clamp01(value), color.a);
                _status = inspector.TrySetButtonColor(button, next, out string message)
                    ? PrefixStatus(prefix, message)
                    : message ?? "Could not update " + prefix + " color.";
            });
        }

        private static string PrefixStatus(string prefix, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return prefix ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prefix) ||
                message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            return prefix + ": " + message;
        }

        private static void DrawAcbEnumCycler(
            string label,
            string value,
            Action previous,
            Action next)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
            if (GUILayout.Button("<", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                previous?.Invoke();
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.Body);
            if (GUILayout.Button(">", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                next?.Invoke();
            GUILayout.EndHorizontal();
        }

        private static void DrawDoubleAcbStepper(
            string label,
            double value,
            double step,
            Action<double> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value - Math.Max(0.001d, step));
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(76f)));
            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value + Math.Max(0.001d, step));
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardName(AutomationBreadboardInspector inspector)
        {
            GUILayout.Label("Controller name", DecorationEditorTheme.Mini);
            string previous = inspector.ControllerName ?? string.Empty;
            string next = GUILayout.TextField(previous, DecorationEditorTheme.TextField);
            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                inspector.ControllerName = next;
                _status = "Breadboard controller name updated.";
            }
        }

        private static void DrawBreadboardToggle(
            string label,
            bool value,
            Action<bool> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button(
                    value ? "On" : "Off",
                    DecorationEditorTheme.ToolButton(value),
                    GUILayout.Width(EsuHudLayout.Scale(72f))))
            {
                apply?.Invoke(!value);
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawFloatBreadboardStepper(
            string label,
            float value,
            float step,
            Action<float> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value - Mathf.Max(0.01f, step));
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(76f)));
            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value + Mathf.Max(0.01f, step));
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardQuickAdds(AutomationBreadboardInspector inspector)
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Quick add native component", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            DrawBreadboardAddButton(inspector, "Comment", "Comment");
            DrawBreadboardAddButton(inspector, "ConstantInput", "Constant");
            DrawBreadboardAddButton(inspector, "Evaluator", "Math");
            DrawBreadboardAddButton(inspector, "Switch", "Switch");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawBreadboardAddButton(inspector, "LogicGate", "Logic");
            DrawBreadboardAddButton(inspector, "GenericBlockGetter", "Getter");
            DrawBreadboardAddButton(inspector, "GenericBlockSetter", "Setter");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawBreadboardSearchAddButton(inspector, "PID", "pid");
            DrawBreadboardSearchAddButton(inspector, "Thresh", "threshold");
            DrawBreadboardSearchAddButton(inspector, "Clamp", "clamp");
            DrawBreadboardSearchAddButton(inspector, "Delay", "delay");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawBreadboardSearchAddButton(inspector, "Sum", "sum");
            DrawBreadboardSearchAddButton(inspector, "Multiply", "multiply");
            DrawBreadboardSearchAddButton(inspector, "Var read", "variable", "reader");
            DrawBreadboardSearchAddButton(inspector, "Var write", "variable", "writer");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            DrawBreadboardSharedVariableBridge(inspector);
        }

        private void DrawBreadboardSharedVariableBridge(AutomationBreadboardInspector inspector)
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Shared variable bridge", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            DrawBreadboardSearchAddButton(inspector, "Reader", "variable", "reader");
            DrawBreadboardSearchAddButton(inspector, "Writer", "variable", "writer");
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled &&
                          inspector.CanAddComponentMatching("variable", "reader") &&
                          inspector.CanAddComponentMatching("variable", "writer");
            if (GUILayout.Button(
                    "Pair",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                string[][] searches =
                {
                    new[] { "variable", "reader" },
                    new[] { "variable", "writer" }
                };
                _status = inspector.TryAddFirstAvailableComponents(
                        "Shared variable bridge",
                        searches,
                        out string message)
                    ? message
                    : message ?? "Could not create shared variable bridge.";
            }
            GUI.enabled = oldEnabled;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Use matching native variable names/settings after creating the bridge nodes.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawMissileBreadboardQuickAdds(AutomationBreadboardInspector inspector)
        {
            if (_selectedController?.Controller?.Kind != AutomationControllerKind.MissileBreadboard)
                return;

            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Missile output components", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            DrawBreadboardSearchAddButton(inspector, "Thrust", "thrust");
            DrawBreadboardSearchAddButton(inspector, "Detonate", "detonate");
            DrawBreadboardSearchAddButton(inspector, "Guidance", "guidance");
            DrawBreadboardSearchAddButton(inspector, "Target", "target");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawBreadboardSearchAddButton(inspector, "APN", "apn");
            DrawBreadboardSearchAddButton(inspector, "Prox fuse", "proximity", "fuse");
            DrawBreadboardSearchAddButton(inspector, "Alt fuse", "altitude", "fuse");
            DrawBreadboardSearchAddButton(inspector, "Seeker", "seeker");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardProxyActions(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationLink> links)
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Linked target proxies", DecorationEditorTheme.Mini);
            if (links == null || links.Count == 0)
            {
                GUILayout.Label("Link world targets first, then create native Generic Getter/Setter proxy nodes here.", DecorationEditorTheme.MiniWrap);
                return;
            }

            bool canGetter = inspector.CanAddComponent("GenericBlockGetter");
            bool canSetter = inspector.CanAddComponent("GenericBlockSetter");
            if (!canGetter && !canSetter)
            {
                GUILayout.Label("This board does not advertise Generic Getter or Generic Setter components.", DecorationEditorTheme.Warning);
                return;
            }

            int shown = Math.Min(links.Count, 8);
            for (int index = 0; index < shown; index++)
            {
                AutomationLink link = links[index];
                if (link == null)
                    continue;

                GUILayout.BeginVertical(DecorationEditorTheme.Row);
                if (link.Target == null)
                {
                    GUILayout.Label(link.TargetLabel + " (missing)", DecorationEditorTheme.Warning);
                    GUILayout.Label(
                        "This linked target is not in the live target catalog after refresh.",
                        DecorationEditorTheme.MiniWrap);
                    GUILayout.EndVertical();
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(link.TargetLabel, DecorationEditorTheme.Body);
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    AutomationTargetCatalog.CategoryLabel(link.Target.Category),
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(104f)));
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    AutomationTargetCatalog.RoleLabel(link.Target) +
                    " | " +
                    link.Target.RuntimeType +
                    "  cell " +
                    link.Target.LocalPosition.x.ToString(CultureInfo.InvariantCulture) +
                    "," +
                    link.Target.LocalPosition.y.ToString(CultureInfo.InvariantCulture) +
                    "," +
                    link.Target.LocalPosition.z.ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.MiniWrap);
                GUILayout.BeginHorizontal();
                if (IsAcbControllerBridgeTarget(link.Target))
                {
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Button getter", getter: true, setter: false);
                    GUILayout.Label("Matches a Breadboard-output keyword when FtD exposes one.", DecorationEditorTheme.Mini);
                }
                else if (IsAcbProxyTarget(link.Target))
                {
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Rule getter", getter: true, setter: false);
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Rule setter", getter: false, setter: true);
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Both", getter: true, setter: true);
                }
                else
                {
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Getter", getter: true, setter: false);
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Setter", getter: false, setter: true);
                    DrawProxyButton(inspector, link.Target, canGetter, canSetter, "Both", getter: true, setter: true);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            if (links.Count > shown)
            {
                GUILayout.Label(
                    "+" + (links.Count - shown).ToString(CultureInfo.InvariantCulture) + " more linked target(s).",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawProxyButton(
            AutomationBreadboardInspector inspector,
            AutomationTarget target,
            bool canGetter,
            bool canSetter,
            string label,
            bool getter,
            bool setter)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && (!getter || canGetter) && (!setter || canSetter);
            if (GUILayout.Button(
                    label,
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(66f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _status = inspector.TryCreateTargetProxy(target, getter, setter, out string message)
                    ? message
                    : message ?? "Could not create proxy node.";
            }
            GUI.enabled = oldEnabled;
        }

        private void DrawBreadboardAddButton(
            AutomationBreadboardInspector inspector,
            string typeName,
            string label)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && inspector.CanAddComponent(typeName);
            if (GUILayout.Button(
                    label,
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(74f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _status = inspector.TryAddComponent(typeName, out string message)
                    ? message
                    : message ?? "Could not add component.";
            }
            GUI.enabled = oldEnabled;
        }

        private void DrawBreadboardSearchAddButton(
            AutomationBreadboardInspector inspector,
            string label,
            params string[] searchTerms)
        {
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && inspector.CanAddComponentMatching(searchTerms);
            if (GUILayout.Button(
                    label,
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _status = inspector.TryAddFirstAvailableComponent(label, searchTerms, out string message)
                    ? message
                    : message ?? "Could not add native missile component.";
            }
            GUI.enabled = oldEnabled;
        }

        private void DrawBreadboardGraphCanvas(AutomationBreadboardInspector inspector)
        {
            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            ClearBreadboardComponentSelectionIfMissing(components);
            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("Native graph canvas", DecorationEditorTheme.Mini);
            Rect canvasRect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(310f),
                GUILayout.ExpandWidth(true));
            GUI.Box(canvasRect, GUIContent.none, DecorationEditorTheme.Panel);

            GUI.BeginGroup(canvasRect);
            try
            {
                Rect localRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
                DrawBreadboardCanvasGrid(localRect);
                if (components.Count == 0)
                {
                    GUI.Label(
                        new Rect(
                            EsuHudLayout.Scale(12f),
                            EsuHudLayout.Scale(12f),
                            localRect.width - EsuHudLayout.Scale(24f),
                            EsuHudLayout.Scale(48f)),
                        "Add native components or create linked target proxies to populate this graph.",
                        DecorationEditorTheme.MiniWrap);
                    return;
                }

                IReadOnlyList<AutomationBreadboardCanvasNode> nodes =
                    BuildBreadboardCanvasNodes(inspector, components, localRect);
                HandleBreadboardCanvasInput(inspector, nodes);
                DrawBreadboardCanvasWires(nodes);
                foreach (AutomationBreadboardCanvasNode node in nodes)
                    DrawBreadboardCanvasNode(node);
            }
            finally
            {
                GUI.EndGroup();
            }

            DrawSelectedBreadboardCanvasInspector(inspector, components);
        }

        private IReadOnlyList<AutomationBreadboardCanvasNode> BuildBreadboardCanvasNodes(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationBreadboardComponentSummary> components,
            Rect localRect)
        {
            const int PortDisplayLimit = 8;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (AutomationBreadboardComponentSummary component in components)
            {
                float nativeWidth = NativeCanvasNodeWidth(component);
                float nativeHeight = NativeCanvasNodeHeight(component);
                minX = Mathf.Min(minX, component.X);
                minY = Mathf.Min(minY, component.Y);
                maxX = Mathf.Max(maxX, component.X + nativeWidth);
                maxY = Mathf.Max(maxY, component.Y + nativeHeight);
            }

            if (minX == float.MaxValue)
                return Array.Empty<AutomationBreadboardCanvasNode>();

            float padding = EsuHudLayout.Scale(18f);
            float extentX = Mathf.Max(180f, maxX - minX);
            float extentY = Mathf.Max(120f, maxY - minY);
            float scaleX = (localRect.width - padding * 2f) / extentX;
            float scaleY = (localRect.height - padding * 2f) / extentY;
            float scale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.55f, 1.25f);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.01f)
                scale = 1f;

            float contentWidth = extentX * scale;
            float contentHeight = extentY * scale;
            float originX = padding + Mathf.Max(0f, localRect.width - padding * 2f - contentWidth) * 0.5f - minX * scale;
            float originY = padding + Mathf.Max(0f, localRect.height - padding * 2f - contentHeight) * 0.5f - minY * scale;
            var nodes = new List<AutomationBreadboardCanvasNode>(components.Count);
            foreach (AutomationBreadboardComponentSummary component in components)
            {
                Rect rect = new Rect(
                    originX + component.X * scale,
                    originY + component.Y * scale,
                    Mathf.Clamp(NativeCanvasNodeWidth(component) * scale, EsuHudLayout.Scale(118f), EsuHudLayout.Scale(220f)),
                    Mathf.Clamp(NativeCanvasNodeHeight(component) * scale, EsuHudLayout.Scale(58f), EsuHudLayout.Scale(150f)));
                if (_canvasDragComponentId == component.UniqueId)
                {
                    rect.x += _canvasDragPreviewDelta.x;
                    rect.y += _canvasDragPreviewDelta.y;
                }

                IReadOnlyList<AutomationBreadboardPortSummary> inputs =
                    inspector.InputPorts(component, PortDisplayLimit);
                IReadOnlyList<AutomationBreadboardPortSummary> outputs =
                    inspector.OutputPorts(component, PortDisplayLimit);
                nodes.Add(new AutomationBreadboardCanvasNode(
                    component,
                    rect,
                    inputs,
                    outputs,
                    scale));
            }

            return nodes;
        }

        private static float NativeCanvasNodeWidth(AutomationBreadboardComponentSummary component)
        {
            if (component == null)
                return 150f;

            return Mathf.Clamp(component.Width > 20f ? component.Width : 150f, 130f, 260f);
        }

        private static float NativeCanvasNodeHeight(AutomationBreadboardComponentSummary component)
        {
            if (component == null)
                return 78f;

            float portRows = Mathf.Max(component.InputCount, component.OutputCount);
            float fallback = Mathf.Max(78f, 48f + portRows * 13f);
            return Mathf.Clamp(component.Height > 20f ? component.Height : fallback, 62f, 180f);
        }

        private void HandleBreadboardCanvasInput(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationBreadboardCanvasNode> nodes)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (_canvasDragComponentId != NoWireSourceComponentId)
            {
                if (current.type == EventType.MouseDrag && current.button == 0)
                {
                    _canvasDragPreviewDelta = current.mousePosition - _canvasDragStartMouse;
                    current.Use();
                    return;
                }

                if (current.type == EventType.MouseUp && current.button == 0)
                {
                    AutomationBreadboardCanvasNode node = FindCanvasNodeById(nodes, _canvasDragComponentId);
                    Vector2 delta = current.mousePosition - _canvasDragStartMouse;
                    if (node != null && delta.sqrMagnitude > 4f)
                    {
                        float scale = Mathf.Max(0.01f, _canvasDragScale);
                        MoveBreadboardComponent(
                            inspector,
                            node.Component,
                            delta.x / scale,
                            delta.y / scale);
                    }

                    _canvasDragComponentId = NoWireSourceComponentId;
                    _canvasDragPreviewDelta = Vector2.zero;
                    current.Use();
                    return;
                }
            }

            if (current.type == EventType.MouseDown && current.button == 1)
            {
                for (int index = nodes.Count - 1; index >= 0; index--)
                {
                    AutomationBreadboardCanvasNode node = nodes[index];
                    if (!node.Rect.Contains(current.mousePosition))
                        continue;

                    OpenAutomationBreadboardNodeContextMenu(
                        node.Component.UniqueId,
                        GUIUtility.GUIToScreenPoint(current.mousePosition));
                    AutomationInputScope.ClaimBuildInputForFrames();
                    current.Use();
                    return;
                }
            }

            if (current.type != EventType.MouseDown || current.button != 0)
                return;

            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                AutomationBreadboardCanvasNode node = nodes[index];
                if (TryHandleCanvasOutputClick(node, current))
                    return;
                if (TryHandleCanvasInputClick(inspector, nodes, node, current))
                    return;
            }

            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                AutomationBreadboardCanvasNode node = nodes[index];
                if (!node.Rect.Contains(current.mousePosition))
                    continue;

                _canvasDragComponentId = node.Component.UniqueId;
                _selectedCanvasComponentId = node.Component.UniqueId;
                _canvasDragStartMouse = current.mousePosition;
                _canvasDragPreviewDelta = Vector2.zero;
                _canvasDragScale = Mathf.Max(0.01f, node.Scale);
                _status = "Selected " + node.Component.Label + " on the native breadboard canvas.";
                current.Use();
                return;
            }
        }

        private bool TryHandleCanvasOutputClick(
            AutomationBreadboardCanvasNode node,
            Event current)
        {
            foreach (AutomationBreadboardPortSummary port in node.Outputs)
            {
                if (!CanvasPortRect(node, true, port.Index).Contains(current.mousePosition))
                    continue;

                _wireSourceComponentId = node.Component.UniqueId;
                _wireSourceOutputIndex = port.Index;
                _status =
                    "Selected " +
                    node.Component.Label +
                    " output " +
                    port.Index.ToString(CultureInfo.InvariantCulture) +
                    " as wire source.";
                current.Use();
                return true;
            }

            return false;
        }

        private bool TryHandleCanvasInputClick(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationBreadboardCanvasNode> nodes,
            AutomationBreadboardCanvasNode node,
            Event current)
        {
            foreach (AutomationBreadboardPortSummary port in node.Inputs)
            {
                if (!CanvasPortRect(node, false, port.Index).Contains(current.mousePosition))
                    continue;

                AutomationBreadboardCanvasNode source =
                    FindCanvasNodeById(nodes, _wireSourceComponentId);
                if (current.shift && port.IsConnected)
                {
                    _status = inspector.TryClearInput(node.Component, port.Index, out string clearMessage)
                        ? clearMessage
                        : clearMessage ?? "Could not clear native breadboard input.";
                }
                else if (source != null && _wireSourceOutputIndex >= 0)
                {
                    _status = inspector.TryConnectPorts(
                            source.Component,
                            _wireSourceOutputIndex,
                            node.Component,
                            port.Index,
                            out string connectMessage)
                        ? connectMessage
                        : connectMessage ?? "Could not connect native breadboard ports.";
                }
                else if (port.IsConnected)
                {
                    _status = inspector.TryClearInput(node.Component, port.Index, out string clearMessage)
                        ? clearMessage
                        : clearMessage ?? "Could not clear native breadboard input.";
                }
                else
                {
                    _status = "Select an output port before connecting this input.";
                }

                current.Use();
                return true;
            }

            return false;
        }

        private void DrawBreadboardCanvasGrid(Rect rect)
        {
            DrawGuiRect(rect, new Color(0f, 0.06f, 0.08f, 0.42f));
            float step = EsuHudLayout.Scale(28f);
            Color minor = new Color(0.05f, 0.9f, 1f, 0.08f);
            for (float x = step; x < rect.width; x += step)
                DrawGuiRect(new Rect(x, 0f, 1f, rect.height), minor);
            for (float y = step; y < rect.height; y += step)
                DrawGuiRect(new Rect(0f, y, rect.width, 1f), minor);
            DrawGuiBorder(rect, new Color(0.05f, 0.9f, 1f, 0.24f), EsuHudLayout.Scale(1f));
        }

        private void DrawBreadboardCanvasWires(IReadOnlyList<AutomationBreadboardCanvasNode> nodes)
        {
            foreach (AutomationBreadboardCanvasNode target in nodes)
            {
                foreach (AutomationBreadboardPortSummary input in target.Inputs)
                {
                    if (!input.IsConnected || input.ConnectedFromComponentId == 0U)
                        continue;

                    AutomationBreadboardCanvasNode source =
                        FindCanvasNodeById(nodes, input.ConnectedFromComponentId);
                    if (source == null)
                        continue;

                    Vector2 start = CanvasPortCenter(source, true, input.ConnectedFromOutputIndex);
                    Vector2 end = CanvasPortCenter(target, false, input.Index);
                    DrawGuiLine(start, end, new Color(0.05f, 0.9f, 1f, 0.72f), EsuHudLayout.Scale(2f));
                }
            }
        }

        private void DrawBreadboardCanvasNode(AutomationBreadboardCanvasNode node)
        {
            bool activeSource = node.Component.UniqueId == _wireSourceComponentId;
            bool selected = node.Component.UniqueId == _selectedCanvasComponentId;
            Color fill = node.Component.IsGenericGetter
                ? new Color(0f, 0.26f, 0.34f, 0.94f)
                : node.Component.IsGenericSetter
                    ? new Color(0.34f, 0.1f, 0.12f, 0.94f)
                    : new Color(0f, 0.16f, 0.2f, 0.94f);
            DrawGuiRect(node.Rect, fill);
            DrawGuiBorder(
                node.Rect,
                activeSource
                    ? DecorationEditorTheme.Cyan
                    : selected
                        ? new Color(1f, 0.86f, 0.24f, 1f)
                        : new Color(0.28f, 0.65f, 0.72f, 0.9f),
                activeSource || selected ? EsuHudLayout.Scale(2f) : EsuHudLayout.Scale(1f));

            Rect titleRect = new Rect(
                node.Rect.x + EsuHudLayout.Scale(8f),
                node.Rect.y + EsuHudLayout.Scale(5f),
                node.Rect.width - EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(20f));
            GUI.Label(titleRect, TrimButtonLabel(node.Component.Label, 24), DecorationEditorTheme.Body);
            Rect typeRect = new Rect(
                node.Rect.x + EsuHudLayout.Scale(8f),
                titleRect.yMax,
                node.Rect.width - EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(18f));
            GUI.Label(typeRect, TrimButtonLabel(node.Component.TypeName, 28), DecorationEditorTheme.Mini);

            foreach (AutomationBreadboardPortSummary input in node.Inputs)
            {
                Rect portRect = CanvasPortRect(node, false, input.Index);
                DrawGuiRect(
                    portRect,
                    input.IsConnected
                        ? new Color(0.05f, 0.9f, 1f, 0.95f)
                        : new Color(0.55f, 0.72f, 0.76f, 0.95f));
            }

            foreach (AutomationBreadboardPortSummary output in node.Outputs)
            {
                Rect portRect = CanvasPortRect(node, true, output.Index);
                bool selectedOutput = node.Component.UniqueId == _wireSourceComponentId &&
                                      output.Index == _wireSourceOutputIndex;
                DrawGuiRect(
                    portRect,
                    selectedOutput
                        ? new Color(1f, 0.86f, 0.24f, 1f)
                        : new Color(0.9f, 0.55f, 0.18f, 0.95f));
            }
        }

        private void DrawSelectedBreadboardCanvasInspector(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationBreadboardComponentSummary> components)
        {
            AutomationBreadboardComponentSummary selected =
                FindComponentById(components, _selectedCanvasComponentId);
            if (selected == null)
                return;

            GUILayout.BeginVertical(DecorationEditorTheme.RowSelected);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Selected node", DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(92f)));
            GUILayout.Label(selected.Label, DecorationEditorTheme.Body);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    "clear",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _selectedCanvasComponentId = NoWireSourceComponentId;
                _status = "Cleared breadboard node selection.";
            }
            if (GUILayout.Button(
                    "delete",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DeleteBreadboardComponent(inspector, selected);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(
                selected.TypeName +
                "  id " +
                selected.UniqueId.ToString(CultureInfo.InvariantCulture) +
                "  " +
                selected.InputCount.ToString(CultureInfo.InvariantCulture) +
                " in / " +
                selected.OutputCount.ToString(CultureInfo.InvariantCulture) +
                " out",
                DecorationEditorTheme.MiniWrap);
            DrawBreadboardComponentSettings(inspector, selected);
            if (selected.IsGenericProxy)
                DrawBreadboardProxyPropertyPicker(inspector, selected);
            DrawBreadboardWireControls(inspector, components, selected);
            GUILayout.EndVertical();
        }

        private void DrawBreadboardComponentSettings(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component)
        {
            AutomationBreadboardComponentSettings settings = inspector.SettingsFor(component);
            if (settings == null)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Native settings", DecorationEditorTheme.Mini);
            if (string.Equals(settings.Kind, "Evaluator", StringComparison.Ordinal))
            {
                GUILayout.Label("Expression", DecorationEditorTheme.Mini);
                string previous = settings.Expression ?? string.Empty;
                string next = GUILayout.TextArea(
                    previous,
                    DecorationEditorTheme.TextField,
                    GUILayout.MinHeight(EsuHudLayout.Scale(54f)));
                if (!string.Equals(previous, next, StringComparison.Ordinal))
                    ApplyNativeSetting(inspector, component, "Expression", next);
            }
            else if (string.Equals(settings.Kind, "Switch", StringComparison.Ordinal))
            {
                DrawNativeFloatSetting(inspector, component, "Threshold", "Threshold", settings.Threshold, 0.1f);
                DrawNativeFloatSetting(inspector, component, "Fail value", "FailValue", settings.FailValue, 0.1f);
            }
            else if (string.Equals(settings.Kind, "LogicGate", StringComparison.Ordinal))
            {
                DrawNativeEnumCycler(inspector, component, "Gate", settings.LogicGateLabel, "SelectedGate");
                DrawNativeEnumCycler(inspector, component, "True when", settings.TrueLogicLabel, "TrueLogic");
            }
            else if (string.Equals(settings.Kind, "ConstantInput", StringComparison.Ordinal))
            {
                DrawNativeEnumCycler(inspector, component, "Type", settings.ConstantTypeLabel, "ConstantType");
                if (settings.ConstantType == 0)
                {
                    DrawNativeFloatSetting(inspector, component, "Float", "InputValue", settings.ConstantFloat, 0.1f);
                }
                else if (settings.ConstantType == 1)
                {
                    GUILayout.Label("Text", DecorationEditorTheme.Mini);
                    string previous = settings.ConstantString ?? string.Empty;
                    string next = GUILayout.TextField(previous, DecorationEditorTheme.TextField);
                    if (!string.Equals(previous, next, StringComparison.Ordinal))
                        ApplyNativeSetting(inspector, component, "InputValueString", next);
                }
                else if (settings.ConstantType == 4)
                {
                    DrawNativeLongSetting(inspector, component, "Integer", "InputValueLong", settings.ConstantLong, 1L);
                }
                else
                {
                    GUILayout.Label(
                        "Vector and quaternion constants still use the native Breadboard editor.",
                        DecorationEditorTheme.MiniWrap);
                }
            }
        }

        private void DrawNativeEnumCycler(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            string label,
            string value,
            string settingKey)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
            if (GUILayout.Button("<", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                CycleNativeSetting(inspector, component, settingKey, -1);
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(118f)));
            if (GUILayout.Button(">", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                CycleNativeSetting(inspector, component, settingKey, 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawNativeFloatSetting(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            string label,
            string settingKey,
            float value,
            float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
            float delta = Mathf.Max(0.01f, step);
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
            {
                ApplyNativeSetting(
                    inspector,
                    component,
                    settingKey,
                    (value - delta).ToString("0.###", CultureInfo.InvariantCulture));
            }

            string previous = value.ToString("0.###", CultureInfo.InvariantCulture);
            string next = GUILayout.TextField(
                previous,
                DecorationEditorTheme.TextField,
                GUILayout.Width(EsuHudLayout.Scale(82f)));
            if (!string.Equals(previous, next, StringComparison.Ordinal))
                ApplyNativeSetting(inspector, component, settingKey, next);

            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
            {
                ApplyNativeSetting(
                    inspector,
                    component,
                    settingKey,
                    (value + delta).ToString("0.###", CultureInfo.InvariantCulture));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawNativeLongSetting(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            string label,
            string settingKey,
            long value,
            long step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
            long delta = Math.Max(1L, step);
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
            {
                ApplyNativeSetting(
                    inspector,
                    component,
                    settingKey,
                    (value - delta).ToString(CultureInfo.InvariantCulture));
            }

            string previous = value.ToString(CultureInfo.InvariantCulture);
            string next = GUILayout.TextField(
                previous,
                DecorationEditorTheme.TextField,
                GUILayout.Width(EsuHudLayout.Scale(82f)));
            if (!string.Equals(previous, next, StringComparison.Ordinal))
                ApplyNativeSetting(inspector, component, settingKey, next);

            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
            {
                ApplyNativeSetting(
                    inspector,
                    component,
                    settingKey,
                    (value + delta).ToString(CultureInfo.InvariantCulture));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ApplyNativeSetting(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            string settingKey,
            string value)
        {
            _status = inspector.TryUpdateComponentSetting(component, settingKey, value, out string message)
                ? message
                : message ?? "Could not update native component setting.";
        }

        private void CycleNativeSetting(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            string settingKey,
            int delta)
        {
            _status = inspector.TryCycleComponentSetting(component, settingKey, delta, out string message)
                ? message
                : message ?? "Could not cycle native component setting.";
        }

        private void DeleteBreadboardComponent(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component)
        {
            if (component == null)
                return;

            uint deletedId = component.UniqueId;
            _status = inspector.TryDeleteComponent(component, out string message)
                ? message
                : message ?? "Could not delete native breadboard component.";
            ClearBreadboardComponentTransientState(deletedId);
        }

        private void ClearBreadboardComponentTransientState(uint componentId)
        {
            if (_selectedCanvasComponentId == componentId)
                _selectedCanvasComponentId = NoWireSourceComponentId;
            if (_wireSourceComponentId == componentId)
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
            }
            if (_canvasDragComponentId == componentId)
            {
                _canvasDragComponentId = NoWireSourceComponentId;
                _canvasDragPreviewDelta = Vector2.zero;
            }
        }

        private void ClearBreadboardComponentSelectionIfMissing(
            IReadOnlyList<AutomationBreadboardComponentSummary> components)
        {
            if (_selectedCanvasComponentId != NoWireSourceComponentId &&
                FindComponentById(components, _selectedCanvasComponentId) == null)
            {
                _selectedCanvasComponentId = NoWireSourceComponentId;
            }

            if (_wireSourceComponentId != NoWireSourceComponentId &&
                FindComponentById(components, _wireSourceComponentId) == null)
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
            }
        }

        private static Rect CanvasPortRect(
            AutomationBreadboardCanvasNode node,
            bool output,
            int portIndex)
        {
            float size = EsuHudLayout.Scale(10f);
            int slotCount = output ? node.OutputSlotCount : node.InputSlotCount;
            float y = CanvasPortY(node.Rect, portIndex, slotCount);
            float x = output ? node.Rect.xMax - size * 0.5f : node.Rect.x - size * 0.5f;
            return new Rect(x, y - size * 0.5f, size, size);
        }

        private static Vector2 CanvasPortCenter(
            AutomationBreadboardCanvasNode node,
            bool output,
            int portIndex)
        {
            return CanvasPortRect(node, output, Math.Max(0, portIndex)).center;
        }

        private static float CanvasPortY(Rect nodeRect, int portIndex, int slotCount)
        {
            float top = nodeRect.y + EsuHudLayout.Scale(34f);
            float bottom = nodeRect.yMax - EsuHudLayout.Scale(13f);
            int count = Math.Max(1, slotCount);
            float t = count == 1
                ? 0.5f
                : Mathf.Clamp01((portIndex + 0.5f) / count);
            return Mathf.Lerp(top, bottom, t);
        }

        private static AutomationBreadboardCanvasNode FindCanvasNodeById(
            IReadOnlyList<AutomationBreadboardCanvasNode> nodes,
            uint uniqueId)
        {
            if (nodes == null || uniqueId == NoWireSourceComponentId)
                return null;

            return nodes.FirstOrDefault(node => node.Component.UniqueId == uniqueId);
        }

        private static void DrawGuiRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void DrawGuiBorder(Rect rect, Color color, float width)
        {
            float line = Mathf.Max(1f, width);
            DrawGuiRect(new Rect(rect.x, rect.y, rect.width, line), color);
            DrawGuiRect(new Rect(rect.x, rect.yMax - line, rect.width, line), color);
            DrawGuiRect(new Rect(rect.x, rect.y, line, rect.height), color);
            DrawGuiRect(new Rect(rect.xMax - line, rect.y, line, rect.height), color);
        }

        private static void DrawGuiLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
                return;

            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            try
            {
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(angle, start);
                GUI.color = color;
                GUI.DrawTexture(
                    new Rect(start.x, start.y - Mathf.Max(1f, width) * 0.5f, delta.magnitude, Mathf.Max(1f, width)),
                    Texture2D.whiteTexture);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }

        private void DrawBreadboardComponentList(AutomationBreadboardInspector inspector)
        {
            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Stored components", DecorationEditorTheme.Mini);
            if (components.Count == 0)
            {
                GUILayout.Label("No native components found on this board yet.", DecorationEditorTheme.MiniWrap);
                return;
            }

            bool hasProxy = components.Any(component => component.IsGenericProxy);
            if (hasProxy)
            {
                GUILayout.Label("Proxy property filter", DecorationEditorTheme.Mini);
                string nextFilter = GUILayout.TextField(_proxyPropertyFilter ?? string.Empty, DecorationEditorTheme.TextField);
                if (!string.Equals(_proxyPropertyFilter, nextFilter, StringComparison.Ordinal))
                    _proxyPropertyFilter = nextFilter;
            }

            DrawBreadboardWireSourceStatus(components);

            int shown = Math.Min(components.Count, 18);
            for (int index = 0; index < shown; index++)
            {
                AutomationBreadboardComponentSummary component = components[index];
                bool selected = component.UniqueId == _selectedCanvasComponentId;
                GUILayout.BeginVertical(selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
                GUILayout.BeginHorizontal();
                GUILayout.Label(component.Label, DecorationEditorTheme.Body);
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    component.InputCount.ToString(CultureInfo.InvariantCulture) +
                    " in / " +
                    component.OutputCount.ToString(CultureInfo.InvariantCulture) +
                    " out",
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(88f)));
                if (GUILayout.Button(
                        "select",
                        DecorationEditorTheme.ToolButton(selected),
                        GUILayout.Width(EsuHudLayout.Scale(58f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    _selectedCanvasComponentId = component.UniqueId;
                    _status = "Selected " + component.Label + " in the native graph.";
                }
                if (GUILayout.Button(
                        "del",
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(38f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    DeleteBreadboardComponent(inspector, component);
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    component.TypeName + "  id " +
                    component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                    "  xy " +
                    component.X.ToString("0.#", CultureInfo.InvariantCulture) +
                    ", " +
                    component.Y.ToString("0.#", CultureInfo.InvariantCulture),
                    DecorationEditorTheme.MiniWrap);
                DrawBreadboardMoveControls(inspector, component);
                if (component.IsGenericProxy)
                    DrawBreadboardProxyPropertyPicker(inspector, component);
                DrawBreadboardWireControls(inspector, components, component);
                GUILayout.EndVertical();
                Rect componentRow = GUILayoutUtility.GetLastRect();
                TryOpenAutomationRowContextMenu(
                    componentRow,
                    mouse => OpenAutomationBreadboardNodeContextMenu(component.UniqueId, mouse));
            }

            if (components.Count > shown)
            {
                GUILayout.Label(
                    "+" + (components.Count - shown).ToString(CultureInfo.InvariantCulture) + " more component(s).",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawBreadboardProxyPropertyPicker(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component)
        {
            string target = string.IsNullOrWhiteSpace(component.BlockTypeName)
                ? "No block type"
                : component.BlockTypeName;
            GUILayout.Label(
                (component.IsGenericGetter ? "Getter" : "Setter") +
                " target: " +
                target +
                (string.IsNullOrWhiteSpace(component.BlockFilter) ? string.Empty : " / filter " + component.BlockFilter),
                DecorationEditorTheme.MiniWrap);

            IReadOnlyList<AutomationBreadboardProxyOption> options =
                inspector.ProxyPropertyOptions(component, _proxyPropertyFilter, 10);
            if (options.Count <= 1 && !string.IsNullOrWhiteSpace(_proxyPropertyFilter))
            {
                GUILayout.Label("No matching native properties.", DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.BeginHorizontal();
            int column = 0;
            foreach (AutomationBreadboardProxyOption option in options)
            {
                if (column == 3)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    column = 0;
                }

                bool selected = option.IsSelectedBy(component);
                string label = TrimButtonLabel(option.Label, option.IsClear ? 12 : 18);
                if (GUILayout.Button(
                        label,
                        DecorationEditorTheme.ToolButton(selected),
                        GUILayout.Width(EsuHudLayout.Scale(option.IsClear ? 88f : 136f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    _status = inspector.TrySelectProxyProperty(component, option, out string message)
                        ? message
                        : message ?? "Could not select proxy property.";
                }

                column++;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardMoveControls(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component)
        {
            const float step = 24f;
            if (inspector == null || component == null)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Move",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(58f)));
            if (AutomationGUILayoutButton(
                    new GUIContent("<", "Move component left"),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(26f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
                MoveBreadboardComponent(inspector, component, -step, 0f);
            if (AutomationGUILayoutButton(
                    new GUIContent(">", "Move component right"),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(26f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
                MoveBreadboardComponent(inspector, component, step, 0f);
            if (AutomationGUILayoutButton(
                    new GUIContent("^", "Move component up"),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(26f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
                MoveBreadboardComponent(inspector, component, 0f, -step);
            if (AutomationGUILayoutButton(
                    new GUIContent("v", "Move component down"),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(26f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
                MoveBreadboardComponent(inspector, component, 0f, step);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void MoveBreadboardComponent(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            float deltaX,
            float deltaY)
        {
            _status = inspector.TryMoveComponent(component, deltaX, deltaY, out string message)
                ? message
                : message ?? "Could not move native breadboard component.";
        }

        private void DrawBreadboardWireSourceStatus(
            IReadOnlyList<AutomationBreadboardComponentSummary> components)
        {
            AutomationBreadboardComponentSummary source = FindComponentById(components, _wireSourceComponentId);
            if (_wireSourceComponentId == NoWireSourceComponentId || source == null)
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Wire source: " +
                source.Label +
                " out " +
                _wireSourceOutputIndex.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
            if (GUILayout.Button(
                    "clear source",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(92f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
                _status = "Cleared breadboard wire source.";
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBreadboardWireControls(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationBreadboardComponentSummary> components,
            AutomationBreadboardComponentSummary component)
        {
            if (inspector == null || component == null)
                return;

            IReadOnlyList<AutomationBreadboardPortSummary> outputs =
                inspector.OutputPorts(component, 4);
            IReadOnlyList<AutomationBreadboardPortSummary> inputs =
                inspector.InputPorts(component, 4);
            if (outputs.Count == 0 && inputs.Count == 0)
                return;

            AutomationBreadboardComponentSummary source =
                FindComponentById(components, _wireSourceComponentId);
            if (_wireSourceComponentId != NoWireSourceComponentId && source == null)
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
            }

            if (outputs.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "Outputs",
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(58f)));
                foreach (AutomationBreadboardPortSummary port in outputs)
                {
                    bool active = component.UniqueId == _wireSourceComponentId &&
                                  port.Index == _wireSourceOutputIndex;
                    if (AutomationGUILayoutButton(
                            new GUIContent("out " + port.Index.ToString(CultureInfo.InvariantCulture), port.Label),
                            DecorationEditorTheme.ToolButton(active),
                            GUILayout.Width(EsuHudLayout.Scale(54f)),
                            GUILayout.Height(EsuHudLayout.Scale(23f))))
                    {
                        _wireSourceComponentId = component.UniqueId;
                        _wireSourceOutputIndex = port.Index;
                        _status =
                            "Selected " +
                            component.Label +
                            " output " +
                            port.Index.ToString(CultureInfo.InvariantCulture) +
                            " as wire source.";
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (inputs.Count == 0)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Inputs",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(58f)));
            foreach (AutomationBreadboardPortSummary port in inputs)
            {
                string label = "in " + port.Index.ToString(CultureInfo.InvariantCulture);
                if (port.IsConnected)
                    label += "*";

                if (AutomationGUILayoutButton(
                        new GUIContent(label, port.Label),
                        DecorationEditorTheme.ToolButton(port.IsConnected),
                        GUILayout.Width(EsuHudLayout.Scale(48f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    if (source == null || _wireSourceOutputIndex < 0)
                    {
                        _status = "Select a breadboard output before connecting an input.";
                    }
                    else
                    {
                        _status = inspector.TryConnectPorts(
                                source,
                                _wireSourceOutputIndex,
                                component,
                                port.Index,
                                out string message)
                            ? message
                            : message ?? "Could not connect native breadboard ports.";
                    }
                }

                if (port.IsConnected &&
                    AutomationGUILayoutButton(
                        new GUIContent("x", "Clear " + port.Label),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(26f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    _status = inspector.TryClearInput(component, port.Index, out string message)
                        ? message
                        : message ?? "Could not clear native breadboard input.";
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            foreach (AutomationBreadboardPortSummary port in inputs.Where(item => item.IsConnected))
            {
                string connectedFrom = string.IsNullOrWhiteSpace(port.ConnectedFrom)
                    ? "connected"
                    : port.ConnectedFrom;
                GUILayout.Label(
                    "in " +
                    port.Index.ToString(CultureInfo.InvariantCulture) +
                    " <- " +
                    connectedFrom,
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private static AutomationBreadboardComponentSummary FindComponentById(
            IReadOnlyList<AutomationBreadboardComponentSummary> components,
            uint uniqueId)
        {
            if (components == null || uniqueId == NoWireSourceComponentId)
                return null;

            return components.FirstOrDefault(component => component.UniqueId == uniqueId);
        }

        private static string TrimButtonLabel(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Property";
            if (value.Length <= maxChars)
                return value;

            return value.Substring(0, Math.Max(1, maxChars - 1)) + ".";
        }

        private static int PositiveModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;

            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static void DrawIntAcbStepper(
            string label,
            int value,
            int step,
            Action<int> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value - Math.Max(1, step));
            GUILayout.Label(value.ToString("N0", CultureInfo.InvariantCulture), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(76f)));
            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value + Math.Max(1, step));
            GUILayout.EndHorizontal();
        }

        private static void DrawFloatAcbStepper(
            string label,
            float value,
            float step,
            Action<float> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(120f)));
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value - Mathf.Max(0.01f, step));
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(76f)));
            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                apply?.Invoke(value + Mathf.Max(0.01f, step));
            GUILayout.EndHorizontal();
        }

        private void DrawAcbSearchPattern(
            AutomationAcbInspector inspector,
            string statusPrefix)
        {
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ACB" : statusPrefix;
            GUILayout.Label("Search pattern", DecorationEditorTheme.Mini);
            string previous = inspector.SearchPattern ?? string.Empty;
            string next = GUILayout.TextField(previous, DecorationEditorTheme.TextField);
            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                inspector.SearchPattern = next;
                _status = prefix + " search pattern updated.";
            }
        }

        private static bool IsBreadboardController(AutomationControllerDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            return descriptor.Kind == AutomationControllerKind.Breadboard ||
                   descriptor.Kind == AutomationControllerKind.AiBreadboard ||
                   descriptor.Kind == AutomationControllerKind.MissileBreadboard;
        }

        private sealed class AutomationBreadboardCanvasNode
        {
            internal AutomationBreadboardCanvasNode(
                AutomationBreadboardComponentSummary component,
                Rect rect,
                IReadOnlyList<AutomationBreadboardPortSummary> inputs,
                IReadOnlyList<AutomationBreadboardPortSummary> outputs,
                float scale)
            {
                Component = component;
                Rect = rect;
                Inputs = inputs ?? Array.Empty<AutomationBreadboardPortSummary>();
                Outputs = outputs ?? Array.Empty<AutomationBreadboardPortSummary>();
                Scale = scale;
                InputSlotCount = Math.Max(
                    component?.InputCount ?? 0,
                    Inputs.Count == 0 ? 0 : Inputs.Max(port => port.Index + 1));
                OutputSlotCount = Math.Max(
                    component?.OutputCount ?? 0,
                    Outputs.Count == 0 ? 0 : Outputs.Max(port => port.Index + 1));
            }

            internal AutomationBreadboardComponentSummary Component { get; }

            internal Rect Rect { get; }

            internal IReadOnlyList<AutomationBreadboardPortSummary> Inputs { get; }

            internal IReadOnlyList<AutomationBreadboardPortSummary> Outputs { get; }

            internal float Scale { get; }

            internal int InputSlotCount { get; }

            internal int OutputSlotCount { get; }
        }

        private sealed class AutomationCodeRecipe
        {
            internal static readonly AutomationCodeRecipe Empty =
                new AutomationCodeRecipe("Recipe", AutomationTargetCategory.Other, string.Empty);

            internal AutomationCodeRecipe(
                string label,
                AutomationTargetCategory category,
                string code)
            {
                Label = string.IsNullOrWhiteSpace(label) ? "Recipe" : label;
                Category = category;
                Code = code ?? string.Empty;
            }

            internal string Label { get; }

            internal AutomationTargetCategory Category { get; }

            internal string Code { get; }
        }

        private sealed class AutomationCompileRevertSet
        {
            internal AutomationCompileRevertSet(
                string controllerKey,
                AutomationBreadboardCompileResult result)
            {
                ControllerKey = controllerKey ?? string.Empty;
                Label = result?.Label ?? "compile";
                ComponentIds = result?.ComponentIds?.ToArray() ?? Array.Empty<uint>();
            }

            internal string ControllerKey { get; }

            internal string Label { get; }

            internal IReadOnlyList<uint> ComponentIds { get; }
        }

        private sealed class AutomationIfElseCompilePlan
        {
            internal AutomationIfElseCompilePlan(
                string conditionExpression,
                string secondaryConditionExpression,
                string logicGate,
                string passExpression,
                float failValue,
                string failExpression)
            {
                ConditionExpression = conditionExpression ?? string.Empty;
                SecondaryConditionExpression = secondaryConditionExpression ?? string.Empty;
                LogicGate = logicGate ?? string.Empty;
                PassExpression = passExpression ?? string.Empty;
                FailValue = failValue;
                FailExpression = failExpression ?? string.Empty;
            }

            internal string ConditionExpression { get; }

            internal string SecondaryConditionExpression { get; }

            internal string LogicGate { get; }

            internal string PassExpression { get; }

            internal float FailValue { get; }

            internal string FailExpression { get; }

            internal bool HasFailExpression =>
                !string.IsNullOrWhiteSpace(FailExpression);
        }

        private static void DrawNodeBox(string title, string subtitle, string footer)
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            GUILayout.Label(title, DecorationEditorTheme.SubHeader);
            GUILayout.Label(subtitle, DecorationEditorTheme.Body);
            GUILayout.Label(footer, DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private static string ProxyHint(AutomationTarget target)
        {
            if (target == null)
                return "target unavailable";

            if (IsAcbControllerBridgeTarget(target))
                return "ACB Controller keyword/button output proxy";

            if (IsAcbProxyTarget(target))
                return "ACB condition/action rule proxy";

            if (target.IsController)
                return "controller proxy";

            return "Generic Getter/Setter candidate: " + target.RuntimeType;
        }

        private static string ProxyPropertyHint(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            string[] terms = AutomationBreadboardInspector
                .TargetProxySearchTerms(target, getter: true)
                .Concat(AutomationBreadboardInspector.TargetProxySearchTerms(target, getter: false))
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
            if (terms.Length == 0)
                return "Auto-pick terms: none; use the property picker after creating the proxy.";

            return "Auto-pick terms: " + string.Join(", ", terms) + ".";
        }

        private static string LinkedTargetWarning(AutomationTarget target)
        {
            if (target == null)
                return "Target unavailable.";

            if (IsAcbControllerBridgeTarget(target))
                return "ACB Controller output requires a button marked for Breadboard output and a matching Generic Getter property.";

            if (IsAcbProxyTarget(target))
                return "ACB rule nodes edit native condition/action data; create Rule Getter/Setter proxies for Breadboard wiring.";

            if (target.IsController)
                return "Controller targets are reflectively available; verify native property options on the created proxy node.";

            return "Generic Getter/Setter properties are discovered from FtD at runtime and may require a name/filter match.";
        }

        private void EnsureAutomationCodeText()
        {
            string key = _selectedController?.StableKey ?? string.Empty;
            if (string.Equals(_automationCodeControllerKey, key, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(_automationCodeText))
            {
                return;
            }

            _automationCodeControllerKey = key;
            _automationCodeRecipeIndex = SuggestedAutomationCodeRecipeIndex();
            _automationCodeText = GeneratedCodeRecipe();
        }

        private AutomationCodeRecipe SelectedCodeRecipe()
        {
            if (s_codeRecipes.Length == 0)
                return AutomationCodeRecipe.Empty;

            _automationCodeRecipeIndex = PositiveModulo(_automationCodeRecipeIndex, s_codeRecipes.Length);
            return s_codeRecipes[_automationCodeRecipeIndex];
        }

        private void SelectAutomationCodeRecipe(int delta, bool load)
        {
            if (s_codeRecipes.Length == 0)
                return;

            _automationCodeRecipeIndex = PositiveModulo(_automationCodeRecipeIndex + delta, s_codeRecipes.Length);
            AutomationCodeRecipe recipe = SelectedCodeRecipe();
            if (load)
                _automationCodeText = recipe.Code;

            _status = "Selected " + recipe.Label + " recipe.";
        }

        private int SuggestedAutomationCodeRecipeIndex()
        {
            if (s_codeRecipes.Length == 0)
                return 0;

            AutomationControllerKind? controllerKind = _selectedController?.Controller?.Kind;
            if (controllerKind == AutomationControllerKind.MissileBreadboard)
                return RecipeIndexForCategory(AutomationTargetCategory.Missiles);
            if (controllerKind == AutomationControllerKind.AcbController)
                return RecipeIndexForCategory(AutomationTargetCategory.Controllers);

            IReadOnlyList<AutomationLink> links = SelectedLinks;
            if (links.Any(link => link.Target?.Category == AutomationTargetCategory.Spinblocks ||
                                  link.Target?.Category == AutomationTargetCategory.TurretsWeapons))
            {
                return RecipeIndexForCategory(AutomationTargetCategory.Spinblocks);
            }

            if (links.Any(link => link.Target?.Category == AutomationTargetCategory.DoorsDocking ||
                                  link.Target?.Category == AutomationTargetCategory.Pistons))
            {
                return RecipeIndexForCategory(AutomationTargetCategory.DoorsDocking);
            }

            if (links.Any(link => link.Target?.Category == AutomationTargetCategory.Propulsion ||
                                  link.Target?.Category == AutomationTargetCategory.ControlSurfaces))
            {
                return RecipeIndexForCategory(AutomationTargetCategory.Propulsion);
            }

            if (links.Any(link => link.Target?.Category == AutomationTargetCategory.Missiles))
                return RecipeIndexForCategory(AutomationTargetCategory.Missiles);

            return 0;
        }

        private static int RecipeIndexForCategory(AutomationTargetCategory category)
        {
            for (int index = 0; index < s_codeRecipes.Length; index++)
            {
                if (s_codeRecipes[index].Category == category)
                    return index;
            }

            return 0;
        }

        private static int RecipeIndexForLabel(string label)
        {
            for (int index = 0; index < s_codeRecipes.Length; index++)
            {
                if (string.Equals(s_codeRecipes[index].Label, label, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return -1;
        }

        private bool CompileAutomationCodeExpression()
        {
            _lastCompileBoundOutput = false;
            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                _status = reason ?? "Selected controller does not expose a native breadboard.";
                return false;
            }

            string normalized = StripAutomationCodeComments(_automationCodeText);
            if (ContainsControlFlow(normalized))
            {
                if (!TryExtractIfElseSwitch(_automationCodeText, out AutomationIfElseCompilePlan plan, out string ifElseError))
                {
                    _status = ifElseError;
                    return false;
                }

                bool compiled = inspector.TryCreateIfElseSwitch(
                        plan.ConditionExpression,
                        plan.SecondaryConditionExpression,
                        plan.LogicGate,
                        plan.PassExpression,
                        plan.FailValue,
                        plan.FailExpression,
                        out AutomationBreadboardCompileResult compileResult,
                        out string ifElseMessage);
                _status = compiled
                    ? RecordAutomationCompileResult(compileResult, ifElseMessage)
                    : ifElseMessage ?? "Could not compile if/else to native nodes.";
                _editorPage = AutomationEditorPage.Graph;
                return compiled;
            }

            if (!TryExtractEvaluatorExpression(_automationCodeText, out string expression, out string error))
            {
                _status = error;
                return false;
            }

            bool expressionCompiled = inspector.TryCreateEvaluatorExpression(
                expression,
                out AutomationBreadboardCompileResult expressionResult,
                out string message);
            _status = expressionCompiled
                ? RecordAutomationCompileResult(expressionResult, message + " Expression: " + expression)
                : message ?? "Could not compile expression to a native Evaluator.";
            _editorPage = AutomationEditorPage.Graph;
            return expressionCompiled;
        }

        private string RecordAutomationCompileResult(
            AutomationBreadboardCompileResult result,
            string message)
        {
            if (result?.ComponentIds == null || result.ComponentIds.Count == 0)
                return message ?? "Compiled native breadboard nodes.";

            string bindingMessage = TryBindCompiledOutputToLinkedSetter(
                result,
                out IReadOnlyList<uint> bindingComponentIds,
                out bool boundOutput);
            _lastCompileBoundOutput = boundOutput;
            uint[] revertComponentIds = result.ComponentIds
                .Concat(bindingComponentIds ?? Array.Empty<uint>())
                .Where(id => id != 0U)
                .Distinct()
                .ToArray();
            _lastCompileRevert = new AutomationCompileRevertSet(
                _selectedController?.StableKey,
                new AutomationBreadboardCompileResult(result.Label, revertComponentIds));
            return (message ?? "Compiled native breadboard nodes.") +
                   " Revert is available for " +
                   revertComponentIds.Length.ToString(CultureInfo.InvariantCulture) +
                   " generated node(s)." +
                   (string.IsNullOrWhiteSpace(bindingMessage) ? string.Empty : " " + bindingMessage);
        }

        private string TryBindCompiledOutputToLinkedSetter(
            AutomationBreadboardCompileResult result,
            out IReadOnlyList<uint> bindingComponentIds,
            out bool boundOutput)
        {
            bindingComponentIds = Array.Empty<uint>();
            boundOutput = false;
            if (result?.ComponentIds == null || result.ComponentIds.Count == 0)
                return string.Empty;
            AutomationLink outputLink = AutomationCodeOutputLink();
            AutomationTarget outputTarget = outputLink?.Target;
            if (_selectedController == null || outputTarget == null)
                return "No linked target setter was available for code output binding.";
            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out _))
            {
                return "Code output binding skipped because the native board could not be reopened.";
            }

            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            AutomationBreadboardComponentSummary output = null;
            foreach (uint id in result.ComponentIds.Reverse())
            {
                output = FindComponentById(components, id);
                if (output != null &&
                    inspector.OutputPorts(output, 1).Count > 0)
                {
                    break;
                }

                output = null;
            }

            if (output == null)
                return "Code output binding skipped because the generated node exposes no visible output.";

            AutomationBreadboardComponentSummary[] setters =
                TargetSettersFor(components, outputTarget);
            string proxyMessage = string.Empty;
            if (setters.Length == 0)
            {
                if (!inspector.TryCreateTargetProxy(
                        outputTarget,
                        getter: false,
                        setter: true,
                        out AutomationBreadboardCompileResult proxyResult,
                        out proxyMessage))
                {
                    return "Code output binding could not create a target setter: " +
                           (proxyMessage ?? "FtD rejected the Generic Setter proxy.") +
                           ".";
                }

                bindingComponentIds = proxyResult?.ComponentIds?.ToArray() ?? Array.Empty<uint>();
                components = inspector.Components;
                setters = TargetSettersFor(components, outputTarget);
            }

            if (setters.Length == 0)
                return "Code output binding skipped because no target-specific Generic Setter input was visible.";

            foreach (AutomationBreadboardComponentSummary setter in setters)
            {
                IReadOnlyList<AutomationBreadboardPortSummary> inputs =
                    inspector.InputPorts(setter, 4);
                AutomationBreadboardPortSummary input = inputs.FirstOrDefault();
                if (input == null)
                    continue;

                if (inspector.TryConnectPorts(output, 0, setter, input.Index, out string message))
                {
                    boundOutput = true;
                    return "Bound compiled output to " +
                           outputLink.TargetLabel +
                           " via " +
                           setter.Label +
                           " input " +
                           input.Index.ToString(CultureInfo.InvariantCulture) +
                           (bindingComponentIds.Count > 0 ? "; auto-created target setter proxy." : ".") +
                           (string.IsNullOrWhiteSpace(proxyMessage) ? string.Empty : " " + proxyMessage);
                }

                if (!string.IsNullOrWhiteSpace(message))
                    return "Code output binding failed: " + message;
            }

            return "Code output binding skipped because no Generic Setter input was visible.";
        }

        private static AutomationBreadboardComponentSummary[] TargetSettersFor(
            IEnumerable<AutomationBreadboardComponentSummary> components,
            AutomationTarget target)
        {
            if (components == null || target == null)
                return Array.Empty<AutomationBreadboardComponentSummary>();

            return components
                .Where(component => SetterMatchesTarget(component, target))
                .OrderBy(component => component.X)
                .ThenBy(component => component.Y)
                .ToArray();
        }

        private static bool SetterMatchesTarget(
            AutomationBreadboardComponentSummary component,
            AutomationTarget target)
        {
            return component != null &&
                   component.IsGenericSetter &&
                   ProxyComponentMatchesTarget(component, target);
        }

        private AutomationTarget UniqueTargetForPersistedProxy(
            AutomationBreadboardComponentSummary proxy)
        {
            AutomationTarget[] matches = _targets
                .Where(target => ProxyComponentMatchesTarget(proxy, target))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool ProxyComponentMatchesTarget(
            AutomationBreadboardComponentSummary component,
            AutomationTarget target)
        {
            if (component == null || target?.Block == null || !component.IsGenericProxy)
                return false;

            string expectedType = ExpectedProxyBlockTypeName(target);
            if (!string.Equals(component.BlockTypeName, expectedType, StringComparison.OrdinalIgnoreCase))
                return false;

            string proxyFilter = component.BlockFilter ?? string.Empty;
            string expectedFilter = ExpectedProxyBlockFilter(target);
            if (string.IsNullOrWhiteSpace(proxyFilter))
                return string.IsNullOrWhiteSpace(expectedFilter);

            if (string.Equals(proxyFilter, expectedFilter, StringComparison.OrdinalIgnoreCase))
                return true;

            return (target.Label ?? string.Empty)
                .IndexOf(proxyFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExpectedProxyBlockTypeName(AutomationTarget target)
        {
            Type blockType = target?.Block?.GetType();
            if (blockType == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(blockType.Name)
                ? blockType.FullName ?? string.Empty
                : blockType.Name;
        }

        private static string ExpectedProxyBlockFilter(AutomationTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Label))
                return string.Empty;

            if (target.Controller != null)
                return string.Empty;

            string runtimeType = target.RuntimeType ?? string.Empty;
            return string.Equals(target.Label, runtimeType, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : target.Label;
        }

        private bool CanRevertLastAutomationCompile()
        {
            return _selectedController != null &&
                   _lastCompileRevert != null &&
                   _lastCompileRevert.ComponentIds.Count > 0 &&
                   string.Equals(
                       _lastCompileRevert.ControllerKey,
                       _selectedController.StableKey,
                       StringComparison.Ordinal);
        }

        private void RevertLastAutomationCompile()
        {
            if (!CanRevertLastAutomationCompile())
            {
                _status = "No generated code compile is available to revert for this controller.";
                return;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                _status = reason ?? "Selected controller does not expose a native breadboard.";
                return;
            }

            Dictionary<uint, AutomationBreadboardComponentSummary> components = inspector.Components
                .GroupBy(component => component.UniqueId)
                .ToDictionary(group => group.Key, group => group.First());
            int deleted = 0;
            int missing = 0;
            int failed = 0;
            foreach (uint componentId in _lastCompileRevert.ComponentIds.Reverse())
            {
                if (!components.TryGetValue(componentId, out AutomationBreadboardComponentSummary component))
                {
                    missing++;
                    continue;
                }

                if (inspector.TryDeleteComponent(component, out _))
                {
                    deleted++;
                    ClearBreadboardComponentTransientState(componentId);
                }
                else
                {
                    failed++;
                }
            }

            if (failed == 0)
                _lastCompileRevert = null;

            _status =
                "Reverted " +
                deleted.ToString(CultureInfo.InvariantCulture) +
                " generated node(s)" +
                (missing > 0 ? ", " + missing.ToString(CultureInfo.InvariantCulture) + " already missing" : string.Empty) +
                (failed > 0 ? ", " + failed.ToString(CultureInfo.InvariantCulture) + " failed" : string.Empty) +
                ".";
        }

        private static bool TryExtractIfElseSwitch(
            string code,
            out AutomationIfElseCompilePlan plan,
            out string error)
        {
            plan = null;
            error = null;
            string normalized = StripAutomationCodeComments(code);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "Enter if/else code before compiling.";
                return false;
            }

            string[] lines = normalized
                .Replace("{", "\n{\n")
                .Replace("}", "\n}\n")
                .Replace(";", "\n")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                               line != "{" &&
                               line != "}")
                .ToArray();
            if (lines.Length != 4)
            {
                error = "Supported if/else form is: if condition, out = expression, else, out = number-or-expression.";
                return false;
            }

            string elseLine = lines[2].TrimEnd(':').Trim();
            if (!lines[0].StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(elseLine, "else", StringComparison.OrdinalIgnoreCase))
            {
                error = "Supported if/else form must use if ... then else.";
                return false;
            }

            string condition = lines[0].Substring(3).Trim().TrimEnd(':').Trim();
            string secondaryCondition = string.Empty;
            string logicGate = string.Empty;
            if (TrySplitLogicCondition(condition, out string leftCondition, out string gate, out string rightCondition))
            {
                condition = leftCondition;
                secondaryCondition = rightCondition;
                logicGate = gate;
            }

            string passExpression = ExtractAssignmentRightHandSide(lines[1]).Trim();
            string failText = ExtractAssignmentRightHandSide(lines[3]).Trim();
            if (string.IsNullOrWhiteSpace(condition) ||
                string.IsNullOrWhiteSpace(passExpression) ||
                string.IsNullOrWhiteSpace(failText))
            {
                error = "If/else lowering requires a condition, a pass expression, and an else expression.";
                return false;
            }

            if (!IsSafeEvaluatorExpressionText(condition) ||
                !IsSafeEvaluatorExpressionText(secondaryCondition) ||
                !IsSafeEvaluatorExpressionText(passExpression) ||
                !IsSafeEvaluatorExpressionText(failText))
            {
                error = "If/else expressions contain unsupported characters.";
                return false;
            }

            bool numericFail = float.TryParse(
                failText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float failValue);

            plan = new AutomationIfElseCompilePlan(
                condition,
                secondaryCondition,
                logicGate,
                passExpression,
                numericFail ? failValue : 0f,
                numericFail ? string.Empty : failText);
            return true;
        }

        private static bool TrySplitLogicCondition(
            string condition,
            out string leftCondition,
            out string logicGate,
            out string rightCondition)
        {
            leftCondition = condition ?? string.Empty;
            logicGate = string.Empty;
            rightCondition = string.Empty;
            if (string.IsNullOrWhiteSpace(condition))
                return false;

            string[] gates = { " and ", " && ", " or ", " || " };
            foreach (string gate in gates)
            {
                int index = condition.IndexOf(gate, StringComparison.OrdinalIgnoreCase);
                if (index <= 0)
                    continue;

                string right = condition.Substring(index + gate.Length).Trim();
                string left = condition.Substring(0, index).Trim();
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                    continue;

                if (condition.IndexOf(gate, index + gate.Length, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                leftCondition = left;
                rightCondition = right;
                logicGate = gate.IndexOf("or", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            gate.IndexOf("||", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "or"
                    : "and";
                return true;
            }

            return false;
        }

        private static bool TryExtractEvaluatorExpression(
            string code,
            out string expression,
            out string error)
        {
            expression = null;
            error = null;
            string normalized = StripAutomationCodeComments(code);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "Enter an expression before compiling.";
                return false;
            }

            if (ContainsControlFlow(normalized))
            {
                error = "This expression compiler path supports one expression only; use the supported if/else form for control flow.";
                return false;
            }

            string[] lines = normalized
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            if (lines.Length != 1)
            {
                error = "Compile one expression at a time.";
                return false;
            }

            expression = ExtractAssignmentRightHandSide(lines[0]).Trim();
            if (string.IsNullOrWhiteSpace(expression))
            {
                error = "The expression is empty.";
                return false;
            }

            if (expression.Length > 1000)
            {
                error = "Maths Evaluator expressions are limited to 1000 characters.";
                return false;
            }

            if (!IsSafeEvaluatorExpressionText(expression))
            {
                error = "Expression contains unsupported characters for this deterministic compiler slice.";
                return false;
            }

            return true;
        }

        private static string StripAutomationCodeComments(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            string[] lines = code.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int hash = line.IndexOf('#');
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                int cut = -1;
                if (hash >= 0)
                    cut = hash;
                if (slash >= 0)
                    cut = cut < 0 ? slash : Math.Min(cut, slash);
                lines[index] = cut >= 0 ? line.Substring(0, cut) : line;
            }

            return string.Join("\n", lines);
        }

        private static bool ContainsControlFlow(string text)
        {
            string padded = " " + text.Replace('\n', ' ') + " ";
            return padded.IndexOf(" if ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   padded.IndexOf(" else ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf('{') >= 0 ||
                   text.IndexOf('}') >= 0 ||
                   text.IndexOf(':') >= 0;
        }

        private static string ExtractAssignmentRightHandSide(string line)
        {
            int equals = line.IndexOf('=');
            if (equals < 0)
                return line;

            string left = line.Substring(0, equals).Trim();
            if (string.Equals(left, "out", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(left, "output", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(left, "result", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(equals + 1);
            }

            return line;
        }

        private static bool IsSafeEvaluatorExpressionText(string expression)
        {
            foreach (char character in expression)
            {
                if (char.IsLetterOrDigit(character) ||
                    char.IsWhiteSpace(character) ||
                    character == '_' ||
                    character == '.' ||
                    character == ',' ||
                    character == '(' ||
                    character == ')' ||
                    character == '+' ||
                    character == '-' ||
                    character == '*' ||
                    character == '/' ||
                    character == '%' ||
                    character == '<' ||
                    character == '>' ||
                    character == '=' ||
                    character == '!' ||
                    character == '&' ||
                    character == '|')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private IReadOnlyList<string> AutomationCodeLinkedIdentifiers()
        {
            IReadOnlyList<AutomationLink> links = SelectedLinks;
            if (links.Count == 0)
                return Array.Empty<string>();

            var results = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AutomationLink link in links)
            {
                AutomationTarget target = link?.Target;
                if (target == null)
                    continue;

                string baseName = AutomationCodeIdentifierForTarget(target);
                if (!counts.TryGetValue(baseName, out int count))
                    count = 0;

                counts[baseName] = count + 1;
                results.Add(count == 0
                    ? baseName
                    : baseName + "_" + (count + 1).ToString(CultureInfo.InvariantCulture));
            }

            return results;
        }

        private static string AutomationCodeIdentifierForTarget(AutomationTarget target)
        {
            if (target == null)
                return "target";

            if (IsAcbControllerBridgeTarget(target))
                return "button_signal";
            if (IsAcbProxyTarget(target))
                return "acb_rule";

            switch (target.Category)
            {
                case AutomationTargetCategory.Spinblocks:
                    return "spinblock";
                case AutomationTargetCategory.TurretsWeapons:
                    return "weapon";
                case AutomationTargetCategory.Propulsion:
                    return "propulsion";
                case AutomationTargetCategory.Pistons:
                    return "piston";
                case AutomationTargetCategory.Pumps:
                    return "pump";
                case AutomationTargetCategory.ControlSurfaces:
                    return "control_surface";
                case AutomationTargetCategory.Ai:
                    return "ai_target";
                case AutomationTargetCategory.Missiles:
                    return "missile";
                case AutomationTargetCategory.Lights:
                    return "light";
                case AutomationTargetCategory.ShieldsDefence:
                    return "shield";
                case AutomationTargetCategory.Detection:
                    return "detector";
                case AutomationTargetCategory.DoorsDocking:
                    return "door";
                case AutomationTargetCategory.SoundDisplay:
                    return "media";
                case AutomationTargetCategory.ResourcePower:
                    return "resource";
                default:
                    return SanitizeIdentifier(target.Label);
            }
        }

        private void InsertAutomationCodeIdentifier(string identifier)
        {
            string sanitized = SanitizeIdentifier(identifier);
            string current = _automationCodeText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(current))
            {
                _automationCodeText = sanitized;
                return;
            }

            string spacer = char.IsWhiteSpace(current[current.Length - 1]) ? string.Empty : " ";
            _automationCodeText = current + spacer + sanitized;
        }

        private string GeneratedCodeRecipe()
        {
            return SelectedCodeRecipe().Code;
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "target";

            var chars = new List<char>();
            bool lastWasUnderscore = false;
            foreach (char raw in value.Trim())
            {
                char character = char.ToLowerInvariant(raw);
                bool valid = char.IsLetterOrDigit(character) || character == '_';
                char next = valid ? character : '_';
                if (next == '_' && lastWasUnderscore)
                    continue;

                chars.Add(next);
                lastWasUnderscore = next == '_';
            }

            while (chars.Count > 0 && chars[chars.Count - 1] == '_')
                chars.RemoveAt(chars.Count - 1);

            if (chars.Count == 0)
                return "target";
            if (char.IsDigit(chars[0]))
                chars.Insert(0, '_');

            return new string(chars.ToArray());
        }

        private IReadOnlyList<AutomationLink> SelectedLinks
        {
            get
            {
                if (_selectedController == null)
                    return Array.Empty<AutomationLink>();

                string key = _selectedController.StableKey;
                return _links
                    .Where(link => link.ControllerKey == key)
                    .ToArray();
            }
        }

        private AutomationLink SelectedLink()
        {
            if (string.IsNullOrWhiteSpace(_selectedLinkTargetKey))
                return null;

            return SelectedLinks.FirstOrDefault(link =>
                string.Equals(link.TargetKey, _selectedLinkTargetKey, StringComparison.Ordinal));
        }

        private AutomationLink AutomationCodeOutputLink()
        {
            IReadOnlyList<AutomationLink> links = SelectedLinks;
            if (links.Count == 0)
                return null;

            AutomationLink selected = null;
            if (!string.IsNullOrWhiteSpace(_automationCodeOutputTargetKey))
            {
                selected = links.FirstOrDefault(link =>
                    string.Equals(link.TargetKey, _automationCodeOutputTargetKey, StringComparison.Ordinal));
            }

            if (selected?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(selected.Target))
            {
                return selected;
            }

            selected = SelectedLink();
            if (selected?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(selected.Target))
            {
                return selected;
            }

            return links.FirstOrDefault(link =>
                link?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(link.Target));
        }

        private AutomationLink ValidationOutputLink() =>
            SelectedLinks.FirstOrDefault(link =>
                link?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(link.Target));

        private void ClearSelectedLinks()
        {
            if (_selectedController == null)
                return;

            string key = _selectedController.StableKey;
            _links.RemoveAll(link => link.ControllerKey == key);
            _selectedLinkTargetKey = string.Empty;
            _automationCodeOutputTargetKey = string.Empty;
            _status = "Cleared links for " + _selectedController.Label + ".";
        }

        private int CountTargets(AutomationTargetCategory category)
        {
            if (category == AutomationTargetCategory.All)
            {
                return _targets.Count(target =>
                    AutomationTargetCatalog.MatchesSearch(target, _targetSearchText));
            }

            return _targets.Count(target =>
                AutomationTargetCatalog.PassesFilter(target, category) &&
                AutomationTargetCatalog.MatchesSearch(target, _targetSearchText));
        }

        private IReadOnlyList<AutomationTarget> FilteredWorldTargets()
        {
            return _targets
                .Where(target =>
                    AutomationTargetCatalog.PassesFilter(target, _filter) &&
                    AutomationTargetCatalog.MatchesSearch(target, _targetSearchText))
                .ToArray();
        }

        private void CycleFilter()
        {
            IReadOnlyList<AutomationTargetCategory> filters = AutomationTargetCatalog.FilterOrder;
            int index = -1;
            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i] == _filter)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                index = 0;
            _filter = filters[(index + 1) % filters.Count];
            _status = "Target filter: " + AutomationTargetCatalog.CategoryLabel(_filter) + ".";
        }

        private static string ToolLabel(AutomationTool tool) =>
            tool == AutomationTool.Place ? "Place controllers" : "Link targets";

        private static void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(96f)));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.Body);
            GUILayout.EndHorizontal();
        }

        private static string FormatCell(Vector3i cell) =>
            cell.x.ToString(CultureInfo.InvariantCulture) + ", " +
            cell.y.ToString(CultureInfo.InvariantCulture) + ", " +
            cell.z.ToString(CultureInfo.InvariantCulture);

        private string ConstructGroupLabel(AllConstruct construct, int groupIndex)
        {
            if (construct == null)
                return "Unknown construct";

            AllConstruct current = null;
            try
            {
                current = _build.GetC();
            }
            catch
            {
                current = null;
            }

            return ReferenceEquals(construct, current)
                ? "Main construct"
                : "Subconstruct " + Math.Max(1, groupIndex).ToString(CultureInfo.InvariantCulture);
        }

        private static string ConstructGroupSortKey(AllConstruct construct)
        {
            if (construct == null)
                return "z-null";

            long hash = construct.GetHashCode();
            if (hash < 0L)
                hash = -hash;

            return hash.ToString("D20", CultureInfo.InvariantCulture);
        }

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _layoutResetGeneration = EsuHudLayout.ResetGeneration;
            s_layoutGeneration = _layoutResetGeneration;
            _layoutInitialized = false;
            _leftPanelRect = Rect.zero;
            _rightPanelRect = Rect.zero;
            _editorRect = Rect.zero;
            s_leftPanelRect = Rect.zero;
            s_rightPanelRect = Rect.zero;
            s_editorRect = Rect.zero;
        }

        private void PrepareAutomationLayout()
        {
            float margin = EsuHudLayout.Scale(8f);
            _toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight);
            _statusRect = new Rect(
                margin,
                Screen.height - BottomStripHeightScaled() - margin,
                Screen.width - margin * 2f,
                BottomStripHeightScaled());

            if (!_layoutInitialized)
            {
                _leftPanelRect = ValidRect(s_leftPanelRect) ? s_leftPanelRect : DefaultLeftPanelRect();
                _rightPanelRect = ValidRect(s_rightPanelRect) ? s_rightPanelRect : DefaultRightPanelRect();
                _leftPanelRect = ClampLeftPanel(_leftPanelRect);
                _rightPanelRect = ClampRightPanel(_rightPanelRect);
                _editorRect = ValidRect(s_editorRect) ? s_editorRect : DefaultEditorRect();
                _layoutInitialized = true;
            }

            if (_editorOpen)
            {
                _editorRect = FullScreenEditorRect();
                return;
            }

            if (!ValidRect(_leftPanelRect))
                _leftPanelRect = DefaultLeftPanelRect();
            if (!ValidRect(_rightPanelRect))
                _rightPanelRect = DefaultRightPanelRect();
            _leftPanelRect = ClampLeftPanel(_leftPanelRect);
            _rightPanelRect = ClampRightPanel(_rightPanelRect);

            if (!ValidRect(_editorRect))
                _editorRect = DefaultEditorRect();
            _editorRect = ClampEditorPanel(_editorRect);
        }

        private void HandleAutomationPanelResizes()
        {
            if (_editorOpen)
            {
                _resizingLeftPanel = false;
                _resizingRightPanel = false;
                _resizingEditor = false;
                return;
            }

            if (_showLeftPanel)
            {
                HandleAutomationPanelResize(
                    ref _leftPanelRect,
                    ref _resizingLeftPanel,
                    ref _leftPanelResizeStart,
                    ref _leftPanelResizeMouseStart,
                    resizeFromLeft: false,
                    MinLeftPanelWidth(),
                    MinSidePanelHeight(),
                    MaxLeftPanelWidth(),
                    MaxSidePanelHeight(),
                    ToolbarBottomLimit(),
                    BottomPanelLimit());
            }

            if (_showRightPanel)
            {
                HandleAutomationPanelResize(
                    ref _rightPanelRect,
                    ref _resizingRightPanel,
                    ref _rightPanelResizeStart,
                    ref _rightPanelResizeMouseStart,
                    resizeFromLeft: true,
                    MinRightPanelWidth(),
                    MinSidePanelHeight(),
                    MaxRightPanelWidth(),
                    MaxSidePanelHeight(),
                    ToolbarBottomLimit(),
                    BottomPanelLimit());
            }

        }

        private void HandleAutomationPanelResize(
            ref Rect rect,
            ref bool resizing,
            ref Rect resizeStart,
            ref Vector2 resizeMouseStart,
            bool resizeFromLeft,
            float minWidth,
            float minHeight,
            float maxWidth,
            float maxHeight,
            float topLimit,
            float bottomLimit)
        {
            Event current = Event.current;
            if (current == null)
                return;

            Rect grip = EsuHudLayout.ResizeGripRect(rect, resizeFromLeft);
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                resizing &&
                !grip.Contains(current.mousePosition))
            {
                resizing = false;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                grip.Contains(current.mousePosition))
            {
                resizing = true;
                resizeStart = rect;
                resizeMouseStart = current.mousePosition;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && resizing)
            {
                Vector2 delta = current.mousePosition - resizeMouseStart;
                Rect next = resizeStart;
                if (resizeFromLeft)
                {
                    float right = resizeStart.xMax;
                    next.width = Mathf.Clamp(resizeStart.width - delta.x, minWidth, maxWidth);
                    next.x = right - next.width;
                }
                else
                {
                    next.width = Mathf.Clamp(resizeStart.width + delta.x, minWidth, maxWidth);
                }

                next.height = Mathf.Clamp(resizeStart.height + delta.y, minHeight, maxHeight);
                rect = EsuHudLayout.ClampPanel(
                    next,
                    minWidth,
                    minHeight,
                    maxWidth,
                    maxHeight,
                    topLimit,
                    bottomLimit);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                resizing = false;
        }

        private void DrawAutomationResizeGrips()
        {
            if (_editorOpen)
                return;

            if (_showLeftPanel)
            {
                EsuHudLayout.DrawResizeGrip(_leftPanelRect, leftEdge: false);
                EsuCursorTooltip.Register(
                    EsuHudLayout.ResizeGripRect(_leftPanelRect, leftEdge: false),
                    "Drag to resize the Automation status and link panel.");
            }

            if (_showRightPanel)
            {
                EsuHudLayout.DrawResizeGrip(_rightPanelRect, leftEdge: true);
                EsuCursorTooltip.Register(
                    EsuHudLayout.ResizeGripRect(_rightPanelRect, leftEdge: true),
                    "Drag to resize the Automation block palette.");
            }

        }

        private void FitEditorToViewport()
        {
            _editorRect = FullScreenEditorRect();
        }

        private void PersistLayoutState()
        {
            s_leftPanelRect = _leftPanelRect;
            s_rightPanelRect = _rightPanelRect;
            if (!_editorOpen)
                s_editorRect = _editorRect;
            s_layoutGeneration = _layoutResetGeneration;
        }

        private static bool ValidRect(Rect rect) =>
            rect.width > 1f &&
            rect.height > 1f &&
            !float.IsNaN(rect.x) &&
            !float.IsNaN(rect.y) &&
            !float.IsNaN(rect.width) &&
            !float.IsNaN(rect.height);

        private static float BottomStripHeightScaled() =>
            Mathf.Clamp(
                Screen.height * 0.13f,
                EsuHudLayout.Scale(BottomStripHeight - 18f),
                EsuHudLayout.Scale(BottomStripHeight + 22f));

        private static float ToolbarBottomLimit() =>
            EsuHudLayout.ToolbarRect(ToolbarHeight).yMax + EsuHudLayout.Scale(8f);

        private static float BottomPanelLimit() =>
            BottomStripHeightScaled() + EsuHudLayout.Scale(12f);

        private static float MaxSidePanelHeight() =>
            Mathf.Max(
                MinSidePanelHeight(),
                Screen.height - ToolbarBottomLimit() - BottomPanelLimit());

        private static float MinSidePanelHeight() => EsuHudLayout.Scale(360f);

        private static float MinLeftPanelWidth() => EsuHudLayout.Scale(300f);

        private static float MaxLeftPanelWidth() =>
            Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.48f);

        private static float MinRightPanelWidth() => EsuHudLayout.Scale(260f);

        private static float MaxRightPanelWidth() =>
            Mathf.Max(MinRightPanelWidth(), Screen.width * 0.42f);

        private static float MinEditorWidth() => EsuHudLayout.Scale(520f);

        private static float MinEditorHeight() => EsuHudLayout.Scale(360f);

        private static float MaxEditorWidth() =>
            Mathf.Max(MinEditorWidth(), Screen.width - EsuHudLayout.Scale(16f));

        private static float MaxEditorHeight() =>
            Mathf.Max(MinEditorHeight(), Screen.height - ToolbarBottomLimit() - BottomPanelLimit());

        private Rect DefaultLeftPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(LeftPanelWidth),
                Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.28f));
            return new Rect(
                EsuHudLayout.Scale(12f),
                ToolbarBottomLimit(),
                width,
                MaxSidePanelHeight());
        }

        private Rect DefaultRightPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(RightPanelWidth),
                Mathf.Max(MinRightPanelWidth(), Screen.width * 0.24f));
            return new Rect(
                Mathf.Max(EsuHudLayout.Scale(12f), Screen.width - width - EsuHudLayout.Scale(12f)),
                ToolbarBottomLimit(),
                width,
                MaxSidePanelHeight());
        }

        private Rect DefaultEditorRect()
        {
            float gap = EsuHudLayout.Scale(10f);
            float x = _showLeftPanel ? _leftPanelRect.xMax + gap : gap;
            float right = _showRightPanel ? _rightPanelRect.x - gap : Screen.width - gap;
            float width = right - x;
            if (width < MinEditorWidth())
            {
                width = Mathf.Min(MaxEditorWidth(), Mathf.Max(MinEditorWidth(), EsuHudLayout.Scale(EditorWidth)));
                x = Mathf.Clamp(
                    (Screen.width - width) * 0.5f,
                    gap,
                    Mathf.Max(gap, Screen.width - width - gap));
            }

            return new Rect(
                x,
                ToolbarBottomLimit(),
                width,
                MaxEditorHeight());
        }

        private static Rect FullScreenEditorRect()
        {
            float margin = EsuHudLayout.Scale(8f);
            return new Rect(
                margin,
                margin,
                Mathf.Max(1f, Screen.width - margin * 2f),
                Mathf.Max(1f, Screen.height - margin * 2f));
        }

        private static Rect ClampLeftPanel(Rect rect) =>
            EsuHudLayout.ClampPanel(
                rect,
                MinLeftPanelWidth(),
                MinSidePanelHeight(),
                MaxLeftPanelWidth(),
                MaxSidePanelHeight(),
                ToolbarBottomLimit(),
                BottomPanelLimit());

        private static Rect ClampRightPanel(Rect rect) =>
            EsuHudLayout.ClampPanel(
                rect,
                MinRightPanelWidth(),
                MinSidePanelHeight(),
                MaxRightPanelWidth(),
                MaxSidePanelHeight(),
                ToolbarBottomLimit(),
                BottomPanelLimit());

        private static Rect ClampEditorPanel(Rect rect) =>
            EsuHudLayout.ClampPanel(
                rect,
                MinEditorWidth(),
                MinEditorHeight(),
                MaxEditorWidth(),
                MaxEditorHeight(),
                ToolbarBottomLimit(),
                BottomPanelLimit());
        private bool IsMouseCurrentlyOverUi()
        {
            if (!_layoutInitialized || !ValidRect(_toolbarRect))
                PrepareAutomationLayout();

            return IsMouseOverAnyUi(MouseGuiPosition());
        }

        private static Vector2 MouseGuiPosition() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        private bool IsMouseOverAnyUi(Vector2 mouse)
        {
            if (IsAutomationContextMenuAt(mouse))
                return true;
            if (_editorOpen)
                return true;

            if (_toolbarRect.Contains(mouse))
                return true;
            if (_showLeftPanel && _leftPanelRect.Contains(mouse))
                return true;
            if (_showRightPanel && _rightPanelRect.Contains(mouse))
                return true;
            if (_editorOpen && _editorRect.Contains(mouse))
                return true;
            return _statusRect.Contains(mouse);
        }

        private static bool ShouldConsumeGuiEvent(Event current)
        {
            if (current == null)
                return false;

            return current.type == EventType.MouseDown ||
                   current.type == EventType.MouseUp ||
                   current.type == EventType.MouseDrag ||
                   current.type == EventType.ScrollWheel;
        }

        private sealed class AutomationContextMenuItem
        {
            internal AutomationContextMenuItem(
                string label,
                AutomationContextAction action,
                string tooltip,
                bool enabled = true)
            {
                Label = label ?? string.Empty;
                Action = action;
                Tooltip = tooltip ?? string.Empty;
                Enabled = enabled;
            }

            internal string Label { get; }

            internal AutomationContextAction Action { get; }

            internal string Tooltip { get; }

            internal bool Enabled { get; }
        }

        private sealed class AutomationValidationBaseline
        {
            private AutomationValidationBaseline(
                string controllerLabel,
                string controllerKind,
                string controllerCell,
                string nativePersistenceFingerprint,
                string genericProxyFingerprint,
                bool switchProbeRan,
                bool switchFailExpressionReady,
                int switchMaxVisibleInputs)
            {
                ControllerLabel = string.IsNullOrWhiteSpace(controllerLabel)
                    ? "Controller"
                    : controllerLabel;
                ControllerKind = controllerKind ?? string.Empty;
                ControllerCell = controllerCell ?? string.Empty;
                NativePersistenceFingerprint = nativePersistenceFingerprint ?? string.Empty;
                GenericProxyFingerprint = genericProxyFingerprint ?? string.Empty;
                SwitchProbeRan = switchProbeRan;
                SwitchFailExpressionReady = switchFailExpressionReady;
                SwitchMaxVisibleInputs = Math.Max(0, switchMaxVisibleInputs);
            }

            internal string ControllerLabel { get; }

            private string ControllerKind { get; }

            private string ControllerCell { get; }

            internal string NativePersistenceFingerprint { get; }

            private string GenericProxyFingerprint { get; }

            private bool SwitchProbeRan { get; }

            private bool SwitchFailExpressionReady { get; }

            private int SwitchMaxVisibleInputs { get; }

            internal static AutomationValidationBaseline From(
                AutomationTarget controller,
                AutomationRuntimeDiagnosticResult result)
            {
                return new AutomationValidationBaseline(
                    controller?.Label,
                    controller?.Controller?.Kind.ToString() ?? string.Empty,
                    controller == null ? string.Empty : FormatCell(controller.LocalPosition),
                    result?.NativePersistenceFingerprint,
                    result?.GenericProxyFingerprint,
                    result?.SwitchProbeRan == true,
                    result?.SwitchFailExpressionReady == true,
                    result?.SwitchMaxVisibleInputs ?? 0);
            }

            internal string Compare(AutomationValidationBaseline current)
            {
                if (current == null ||
                    string.IsNullOrWhiteSpace(current.NativePersistenceFingerprint))
                {
                    return "Automation validation baseline compare failed: current run has no native fingerprint.";
                }

                var mismatches = new List<string>();
                if (!string.Equals(
                        NativePersistenceFingerprint,
                        current.NativePersistenceFingerprint,
                        StringComparison.Ordinal))
                {
                    mismatches.Add("native graph");
                }

                if (!string.IsNullOrWhiteSpace(GenericProxyFingerprint) ||
                    !string.IsNullOrWhiteSpace(current.GenericProxyFingerprint))
                {
                    if (!string.Equals(
                            GenericProxyFingerprint,
                            current.GenericProxyFingerprint,
                            StringComparison.Ordinal))
                    {
                        mismatches.Add("Generic proxy");
                    }
                }

                if (SwitchProbeRan || current.SwitchProbeRan)
                {
                    if (SwitchFailExpressionReady != current.SwitchFailExpressionReady ||
                        SwitchMaxVisibleInputs != current.SwitchMaxVisibleInputs)
                    {
                        mismatches.Add("Switch fail-expression readiness");
                    }
                }

                if (mismatches.Count == 0)
                    return "Automation validation baseline match: native graph, proxy, and Switch readiness agree.";

                return "Automation validation baseline mismatch: " +
                       string.Join(", ", mismatches.ToArray()) +
                       " changed.";
            }

            internal string Describe()
            {
                return
                    ControllerLabel +
                    " [" +
                    ControllerKind +
                    "] cell " +
                    ControllerCell +
                    " native=" +
                    NativePersistenceFingerprint +
                    " proxy=" +
                    (string.IsNullOrWhiteSpace(GenericProxyFingerprint) ? "none" : GenericProxyFingerprint) +
                    " switch=" +
                    (SwitchProbeRan
                        ? (SwitchFailExpressionReady ? "ready" : "not-ready") +
                          "/" +
                          SwitchMaxVisibleInputs.ToString(CultureInfo.InvariantCulture)
                        : "not-probed");
            }
        }

        private sealed class AutomationLink
        {
            internal AutomationLink(
                AutomationTarget controller,
                AutomationTarget target)
            {
                ControllerKey = controller?.StableKey ?? string.Empty;
                ControllerLabel = controller?.Label ?? "Controller";
                TargetKey = target?.StableKey ?? string.Empty;
                TargetLabel = target?.Label ?? "Target";
                Target = target;
            }

            internal string ControllerKey { get; }

            internal string ControllerLabel { get; }

            internal string TargetKey { get; }

            internal string TargetLabel { get; private set; }

            internal AutomationTarget Target { get; private set; }

            internal bool IsStale => Target == null;

            internal void RebindTarget(AutomationTarget target)
            {
                Target = target;
                if (target != null)
                    TargetLabel = target.Label;
            }
        }
    }
}
