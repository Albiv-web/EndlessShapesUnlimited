using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using BrilliantSkies.PlayerProfiles;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
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
        private const int WorldHighlightLimit = 180;
        private const int TargetBrowserVisibleLimit = 80;
        private const int NativePaletteVisibleLimit = 90;
        private const int NativeComponentListVisibleLimit = 18;
        private const int NativeCanvasPortVisibleLimit = 8;
        private const int NativeWireControlPortVisibleLimit = 4;
        private const int PropertyPickerVisibleLimit = 80;
        private const int SystemBlockTemplateLibraryLimit = 64;
        private const int ControllerIndexGroupVisibleLimit = 6;
        private const int ControllerIndexVisibleLimit = 18;
        private const int CodeOutputTargetVisibleLimit = 6;
        private const int CodeIdentifierVisibleLimit = 5;
        private const int BlockLoweringVisibleStepLimit = 4;
        private const int SignalFlyoutAvailableTargetLimit = 16;
        private const int AutomationWorkspaceProfileLimit = 80;
        private const uint NoWireSourceComponentId = uint.MaxValue;
        private const string AutomationVocabularyLine =
            "Terms: Controller, Target, Link, Proxy Node, System Block, Port, Recipe, Native Lowering, Check, Apply, Revert.";
        private const string AutomationLinkIdentityGuideLine =
            "Link identity: saved per controller with portable target keys. Re-check only if duplicated unnamed blocks at the same cell/type make a target ambiguous.";

        private enum AutomationTool
        {
            Link,
            Place
        }

        private enum AutomationEditorPage
        {
            Blocks,
            Graph,
            Code,
            System
        }

        private enum AutomationTargetBrowserMode
        {
            Important,
            Generic
        }

        private enum AutomationContextMenuKind
        {
            None,
            Placement,
            Controller,
            Target,
            Link,
            BreadboardNode,
            Block,
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
            ClearWireSource,
            MoveBlockUp,
            MoveBlockDown,
            DeleteBlock
        }

        private enum AutomationClosePromptAction
        {
            None,
            CloseEditor,
            CloseMode,
            SwitchToDecorationEdit
        }

        private enum AutomationBlocksSplitter
        {
            None,
            MainRight,
            CanvasLowering,
            RightLinked,
            RightPalette,
            RightInspector
        }

        private enum NativeExactDrawer
        {
            None,
            Inspector,
            AddNative,
            Refresh,
            Status
        }

        private delegate bool AcbControllerTextApply(
            AutomationAcbControllerButtonSummary button,
            string value,
            out string message);

        private static Rect s_leftPanelRect = Rect.zero;
        private static Rect s_rightPanelRect = Rect.zero;
        private static Rect s_editorRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static bool s_showLeftPanel = true;
        private static bool s_showRightPanel = true;
        private static bool s_showBlocksSection = true;
        private static bool s_showFilterSection = true;
        private static bool s_showAdvancedFilters;
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
        private static readonly AutomationBlockCategory[] s_blockPaletteCategories =
        {
            AutomationBlockCategory.Input,
            AutomationBlockCategory.Control,
            AutomationBlockCategory.Math,
            AutomationBlockCategory.Output,
            AutomationBlockCategory.Timing,
            AutomationBlockCategory.Organization,
            AutomationBlockCategory.Advanced
        };

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.Automation.Toolbar".GetHashCode();
        private readonly int _leftPanelWindowId = "EndlessShapesUnlimited.Automation.LeftPanel".GetHashCode();
        private readonly int _rightPanelWindowId = "EndlessShapesUnlimited.Automation.RightPanel".GetHashCode();
        private readonly int _editorWindowId = "EndlessShapesUnlimited.Automation.Editor".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.Automation.Status".GetHashCode();
        private readonly int _contextMenuWindowId = "EndlessShapesUnlimited.Automation.ContextMenu".GetHashCode();
        private readonly int _propertyPickerWindowId = "EndlessShapesUnlimited.Automation.PropertyPicker".GetHashCode();
        private readonly int _targetPickerWindowId = "EndlessShapesUnlimited.Automation.TargetPicker".GetHashCode();
        private readonly List<AutomationLink> _links = new List<AutomationLink>();
        private readonly AutomationTargetPreviewRenderer _targetPreviewRenderer =
            new AutomationTargetPreviewRenderer();

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
        private Vector2 _blockPaletteScroll;
        private Vector2 _blocksInspectorScroll;
        private Vector2 _blocksCanvasSignalsScroll;
        private Vector2 _blocksCanvasSystemsScroll;
        private IReadOnlyList<AutomationTarget> _targets = Array.Empty<AutomationTarget>();
        private AutomationTarget _selectedController;
        private AutomationTarget _hoverTarget;
        private AutomationControllerDescriptor _selectedPlacement =
            AutomationControllerCatalog.All.FirstOrDefault();
        private AutomationTargetCategory _filter = AutomationTargetCategory.All;
        private AutomationTargetBrowserMode _targetBrowserMode = AutomationTargetBrowserMode.Important;
        private AutomationTool _tool = AutomationTool.Link;
        private AutomationEditorPage _editorPage = AutomationEditorPage.Blocks;
        private AutomationBlockCategory _blockPaletteCategory = AutomationBlockCategory.Input;
        private AutomationBlockWorkspace _blockWorkspace;
        private AutomationLoweringPlan _blockLoweringPlan;
        private string _blockWorkspaceControllerKey = string.Empty;
        private string _blockLoweringStatus = string.Empty;
        private string _automationCodeText = string.Empty;
        private string _automationCodeControllerKey = string.Empty;
        private string _automationCodeOutputTargetSearch = string.Empty;
        private string _automationCodeIdentifierSearch = string.Empty;
        private string _automationCodeRecipeSearch = string.Empty;
        private int _automationCodeRecipeIndex;
        private AutomationCompileRevertSet _lastCompileRevert;
        private AutomationCompileRevertSet _lastSystemBlockLoweringRevert;
        private readonly List<AutomationSystemBlockTemplate> _systemBlockTemplates =
            new List<AutomationSystemBlockTemplate>();
        private string _systemBlockDraftControllerKey = string.Empty;
        private string _systemBlockDraftName = string.Empty;
        private string _systemBlockDraftInputs = string.Empty;
        private string _systemBlockDraftOutputs = string.Empty;
        private string _systemBlockDraftComment = string.Empty;
        private string _systemBlockTemplateSearch = string.Empty;
        private string _systemBlockHostNodeSearch = string.Empty;
        private string _systemBlockValidationStatus = string.Empty;
        private int _activeSystemBlockTemplateIndex = -1;
        private int _openSystemBlockTemplateIndex = -1;
        private string _systemBlockInternalDraft = string.Empty;
        private string _systemBlockInternalApplied = string.Empty;
        private string _systemBlockInternalStatus = string.Empty;
        private bool _systemBlockDraftDirty;
        private bool _systemBlockInternalDirty;
        private bool _showLeftPanel = s_showLeftPanel;
        private bool _showRightPanel = s_showRightPanel;
        private bool _showBlocksSection = s_showBlocksSection;
        private bool _showFilterSection = s_showFilterSection;
        private bool _showAdvancedFilters = s_showAdvancedFilters;
        private bool _editorOpen;
        private bool _closeRequested;
        private bool _automationClosePromptOpen;
        private AutomationClosePromptAction _automationClosePromptAction = AutomationClosePromptAction.None;
        private bool _automationNativeDirty;
        private readonly Dictionary<string, SerializationHudProfile.AutomationWorkspaceData> _automationWorkspaceSnapshots =
            new Dictionary<string, SerializationHudProfile.AutomationWorkspaceData>(StringComparer.Ordinal);
        private bool _layoutInitialized;
        private bool _resizingLeftPanel;
        private bool _resizingRightPanel;
        private bool _resizingEditor;
        private float _nextTargetRefresh;
        private string _proxyPropertyFilter = string.Empty;
        private string _targetSearchText = string.Empty;
        private string _controllerIndexSearch = string.Empty;
        private string _linkedTargetSearch = string.Empty;
        private string _linkedSignalSearch = string.Empty;
        private string _semanticBlockPaletteSearch = string.Empty;
        private string _nativeComponentPaletteSearch = string.Empty;
        private string _storedComponentSearch = string.Empty;
        private string _acbControllerButtonSearch = string.Empty;
        private string _breadboardProxySearch = string.Empty;
        private string _nativeWirePortSearch = string.Empty;
        private string _runtimeDiagnosticsFilter = string.Empty;
        private string _automationCodeOutputTargetKey = string.Empty;
        private uint _wireSourceComponentId = NoWireSourceComponentId;
        private int _wireSourceOutputIndex = -1;
        private uint _selectedCanvasComponentId = NoWireSourceComponentId;
        private uint _canvasDragComponentId = NoWireSourceComponentId;
        private bool _draggingPaletteBlock;
        private bool _draggingNativePaletteBlock;
        private bool _draggingWorkspaceBlock;
        private bool _panningBlockCanvas;
        private bool _blocksCanvasSignalsOpen;
        private bool _blocksCanvasSystemsOpen;
        private AutomationLinkDirection _blocksCanvasSignalsTab = AutomationLinkDirection.Input;
        private bool _outputLinkModeArmed;
        private AutomationBlockKind _dragPaletteBlockKind;
        private AutomationBreadboardAvailableComponent _dragNativePaletteComponent;
        private string _dragWorkspaceBlockId = string.Empty;
        private int _blockDropIndex = -1;
        private Rect _lastBlockCanvasRect = Rect.zero;
        private Rect _lastBlockStackRect = Rect.zero;
        private readonly List<Rect> _lastBlockStackNodeRects = new List<Rect>();
        private Vector2 _blockDragMouseStart;
        private Vector2 _blockDragNodeOffset;
        private Vector2 _blockCanvasPanMouseStart;
        private AutomationBlockCanvasPosition _blockCanvasPanStart;
        private AutomationBlocksSplitter _draggingBlocksSplitter = AutomationBlocksSplitter.None;
        private float _blocksRightColumnRatio = 0.26f;
        private float _blocksLowerPanelRatio = 0.18f;
        private float _blocksRightPaletteRatio = 0.50f;
        private NativeExactDrawer _nativeExactDrawer = NativeExactDrawer.None;
        private bool _nativeExactInspectorClosedForSelection;
        private string _nativeExactLastSelectedNodeId = string.Empty;
        private AutomationContextMenuKind _contextMenuKind = AutomationContextMenuKind.None;
        private Rect _contextMenuRect;
        private Vector2 _contextMenuAnchor;
        private AutomationControllerDescriptor _contextMenuPlacement;
        private string _contextMenuControllerKey = string.Empty;
        private string _contextMenuTargetKey = string.Empty;
        private AutomationLinkDirection _contextMenuLinkDirection = AutomationLinkDirection.Output;
        private uint _contextMenuComponentId = NoWireSourceComponentId;
        private string _contextMenuBlockNodeId = string.Empty;
        private string _selectedLinkTargetKey = string.Empty;
        private AutomationLinkDirection _selectedLinkDirection = AutomationLinkDirection.Output;
        private AutomationTarget _pendingLinkTarget;
        private Vector2 _canvasDragStartMouse;
        private Vector2 _canvasDragPreviewDelta;
        private float _canvasDragScale = 1f;
        private string _status = "Select or place a Breadboard/ACB controller.";
        private string _propertyPickerNodeId = string.Empty;
        private Rect _propertyPickerRect = Rect.zero;
        private string _propertyPickerFilter = string.Empty;
        private Vector2 _propertyPickerScroll;
        private IReadOnlyList<AutomationTargetPropertyOption> _propertyPickerOptions =
            Array.Empty<AutomationTargetPropertyOption>();
        private readonly Dictionary<string, AutomationBlockLiveValueSnapshot> _blockLiveValueCache =
            new Dictionary<string, AutomationBlockLiveValueSnapshot>(StringComparer.Ordinal);
        private string _targetPickerNodeId = string.Empty;
        private Rect _targetPickerRect = Rect.zero;
        private string _targetPickerFilter = string.Empty;
        private Vector2 _targetPickerScroll;
        private AutomationTarget _previewTarget;
        private string _previewReason = string.Empty;
        private Rect _previewSourceRect = Rect.zero;
        private float _targetPreviewSpin;
        private bool _lastCompileBoundOutput;
        private AutomationRuntimeDiagnosticResult _lastRuntimeDiagnostics =
            AutomationRuntimeDiagnosticResult.Empty;
        private string _lastRuntimeDiagnosticsControllerKey = string.Empty;
        private string _validationCompareStatus = string.Empty;
        private int _layoutResetGeneration = s_layoutGeneration;
        private int _lastEscapeHandledFrame = -1;
        private bool _suppressAutomationBackgroundDirectInput;
        private bool _confirmNativeRefreshFromNative;

        internal AutomationEditSession(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _pointerProbe = new DecorationPointerProbe(build);
        }

        internal bool Active { get; private set; }

        internal bool CloseRequested => _closeRequested;

        internal bool SwitchToDecorationEditRequested { get; private set; }

        internal bool HasOpenPrompt => _automationClosePromptOpen;

        internal void ClearSwitchToDecorationEditRequest() =>
            SwitchToDecorationEditRequested = false;

        internal void RequestHotkeyClose()
        {
            RequestAutomationClose(AutomationClosePromptAction.CloseMode);
        }

        internal bool HandleEscapeKeyDown()
        {
            ClaimAutomationModalInput(frames: 4);
            DecoLimitLifter.EsuEscapeCloseGuard.Arm(frames: 4);
            if (_lastEscapeHandledFrame == Time.frameCount)
                return true;

            _lastEscapeHandledFrame = Time.frameCount;
            if (_automationClosePromptOpen)
            {
                CloseAutomationClosePrompt();
                InfoStore.Add("Automation Editor close cancelled.");
                return true;
            }

            if (_editorOpen)
            {
                RequestCloseEditor();
                return true;
            }

            RequestHotkeyClose();
            return true;
        }

        internal void RequestSwitchToDecorationEdit()
        {
            RequestAutomationClose(AutomationClosePromptAction.SwitchToDecorationEdit);
        }

        internal bool DismissOpenPrompt()
        {
            if (!_automationClosePromptOpen)
                return false;

            CloseAutomationClosePrompt();
            InfoStore.Add("Automation Editor close cancelled.");
            return true;
        }

        internal bool CanSwitchToDecorationEdit(out string reason)
        {
            reason = null;
            return true;
        }

        internal void Begin()
        {
            Active = true;
            AutomationInputScope.Begin();
            LoadSystemBlockTemplateLibrary();
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
            _pendingLinkTarget = null;
            _outputLinkModeArmed = false;
            _selectedLinkTargetKey = string.Empty;
            _selectedLinkDirection = AutomationLinkDirection.Output;
            CloseAutomationContextMenu();
            _links.Clear();
            CloseAutomationClosePrompt();
            _automationNativeDirty = false;
            _automationWorkspaceSnapshots.Clear();
            _confirmNativeRefreshFromNative = false;
            ClearAutomationBlockWorkspace(persistCurrent: false);
            ClearSystemBlockWorkspace();
            _targetPreviewRenderer.Dispose();
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
            _pendingLinkTarget = null;
            _outputLinkModeArmed = false;
            CloseAutomationContextMenu();
        }

        internal void Update()
        {
            if (!Active)
                return;

            EsuHudNotifications.SetActiveSource("Automation");
            if (_editorOpen ||
                _automationClosePromptOpen ||
                IsAutomationForegroundPopupOpen())
            {
                ClaimAutomationModalInput();
            }
            _targetPreviewSpin += Time.unscaledDeltaTime * 70f;
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

        private static void ClaimAutomationModalInput(int frames = 2)
        {
            AutomationInputScope.ClaimBuildInputForFrames(frames);
            AutomationInputScope.ClaimCameraInputForFrames(frames);
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

                ConsumeAutomationGuiEscapeEvent(interactive);

                if (interactive)
                {
                    EsuCursorTooltip.BeginFrame(
                        Event.current.mousePosition,
                        _resizingLeftPanel || _resizingRightPanel || _resizingEditor);
                    _previewTarget = null;
                    _previewReason = string.Empty;
                    _previewSourceRect = Rect.zero;
                }

                ApplyLayoutResetIfNeeded();
                PrepareAutomationLayout();
                if (interactive &&
                    !_automationClosePromptOpen &&
                    !IsAutomationForegroundPopupOpen())
                {
                    HandleAutomationPanelResizes();
                }

                bool foregroundOpen =
                    interactive &&
                    !_automationClosePromptOpen &&
                    IsAutomationForegroundPopupOpen();

                bool suppressBackgroundInput = ShouldSuppressAutomationBackgroundInput(
                    Event.current,
                    foregroundOpen);
                bool previousEnabled = GUI.enabled;
                bool previousSuppressBackgroundDirectInput = _suppressAutomationBackgroundDirectInput;
                if (suppressBackgroundInput)
                    GUI.enabled = false;
                _suppressAutomationBackgroundDirectInput = suppressBackgroundInput;
                try
                {
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
                }
                finally
                {
                    _suppressAutomationBackgroundDirectInput = previousSuppressBackgroundDirectInput;
                    GUI.enabled = previousEnabled;
                }

                if (interactive)
                {
                    RegisterAutomationWorldHoverTooltip();
                    if (!_automationClosePromptOpen)
                        DrawAutomationResizeGrips();
                    if (!_automationClosePromptOpen)
                        DrawAutomationForegroundPopups();
                    if (!_automationClosePromptOpen)
                        DrawAutomationTargetPreviewCard();
                    EsuHudNotifications.DrawExpandedPopup();
                    EsuConsoleWindow.DrawForegroundWindow();
                    DrawAutomationClosePrompt();
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

        private void DrawAutomationForegroundPopups()
        {
            DrawAutomationContextMenu();
            DrawAutomationTargetPicker();
            DrawAutomationPropertyPicker();
        }

        private void ConsumeAutomationGuiEscapeEvent(bool interactive)
        {
            Event current = Event.current;
            if (!interactive ||
                current == null ||
                current.type != EventType.KeyDown ||
                current.keyCode != KeyCode.Escape)
            {
                return;
            }

            if (_lastEscapeHandledFrame != Time.frameCount)
                HandleEscapeKeyDown();
            else
                ClaimAutomationModalInput(frames: 4);

            current.Use();
        }

        private Rect AutomationClosePromptRect()
        {
            float width = Mathf.Min(EsuHudLayout.Scale(560f), Screen.width - EsuHudLayout.Scale(32f));
            float height = EsuHudLayout.Scale(230f);
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        private void DrawAutomationClosePrompt()
        {
            if (!_automationClosePromptOpen)
                return;

            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -30000);
            try
            {
                ClaimAutomationModalInput(frames: 4);
                GUI.DrawTexture(
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    DecorationEditorTheme.DimTexture);

                Rect rect = AutomationClosePromptRect();
                GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
                Rect inner = EsuHudLayout.PanelInnerRect(rect, 10f);
                float gap = EsuHudLayout.Scale(8f);
                float headerHeight = EsuHudLayout.Scale(34f);
                float warningHeight = EsuHudLayout.Scale(28f);
                float actionHeight = EsuHudLayout.Scale(40f);
                Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
                GUI.Box(headerRect, GUIContent.none, DecorationEditorTheme.DialogHeader);
                GUI.Label(
                    headerRect,
                    new GUIContent("Unsaved automation", DecorationEditorIconCatalog.Get("save")),
                    DecorationEditorTheme.DialogTitle);

                float y = headerRect.yMax + gap;
                Rect warningRect = new Rect(inner.x, y, inner.width, warningHeight);
                GUI.Label(
                    warningRect,
                    AutomationClosePromptWarning(),
                    DecorationEditorTheme.DialogWarning);
                y = warningRect.yMax + EsuHudLayout.Scale(4f);

                Rect actionsRect = new Rect(
                    inner.x,
                    inner.yMax - actionHeight,
                    inner.width,
                    actionHeight);
                Rect bodyRect = new Rect(
                    inner.x,
                    y,
                    inner.width,
                    Mathf.Max(EsuHudLayout.Scale(48f), actionsRect.y - gap - y));
                GUI.Label(
                    bodyRect,
                    AutomationClosePromptBody(),
                    DecorationEditorTheme.DialogBody);

                DrawAutomationClosePromptActions(actionsRect);
                HandleAutomationClosePromptKeyboard();
                ConsumeAutomationClosePromptInput();
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private string AutomationClosePromptWarning()
        {
            if (HasUnsavedAutomationWorkspaceChanges() && HasUnappliedAutomationChanges())
                return "Automation Editor has unsaved workspace changes and unapplied native changes.";
            if (HasUnsavedAutomationWorkspaceChanges())
                return "Automation Editor has unsaved workspace changes.";
            return "Automation Editor has unapplied native changes.";
        }

        private string AutomationClosePromptBody()
        {
            return "Save stores linked targets and ESU Blocks in your profile. Apply blocks/template changes first when native Breadboard behavior should update now. Close without saving restores the last saved Automation workspace.";
        }

        private void DrawAutomationClosePromptActions(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float buttonWidth = Mathf.Max(1f, (rect.width - gap * 2f) / 3f);
            Rect saveRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect discardRect = new Rect(saveRect.xMax + gap, rect.y, buttonWidth, rect.height);
            Rect keepEditingRect = new Rect(discardRect.xMax + gap, rect.y, buttonWidth, rect.height);

            if (PromptActionButton(
                    saveRect,
                    "save",
                    "Save and close",
                    "Save Automation workspace state, then finish the requested close action.",
                    DecorationEditorTheme.DialogActiveButton))
                SaveAndCloseFromAutomationPrompt();

            if (PromptActionButton(
                    discardRect,
                    "cancel",
                    "Close without saving",
                    "Restore the last saved Automation workspace state and close.",
                    DecorationEditorTheme.DialogButton))
                DiscardAndCloseFromAutomationPrompt();

            if (PromptActionButton(
                    keepEditingRect,
                    "close",
                    "Keep editing",
                    "Close this warning and return to Automation Editor.",
                    DecorationEditorTheme.DialogButton))
                CloseAutomationClosePrompt();
        }

        private static bool PromptActionButton(
            Rect rect,
            string icon,
            string label,
            string tooltip,
            GUIStyle style) =>
            GUI.Button(
                rect,
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                style);

        private void HandleAutomationClosePromptKeyboard()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
                return;

            ClaimAutomationModalInput(frames: 4);
            if (current.keyCode == KeyCode.Return ||
                current.keyCode == KeyCode.KeypadEnter)
            {
                SaveAndCloseFromAutomationPrompt();
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                CloseAutomationClosePrompt();
                InfoStore.Add("Automation Editor close cancelled.");
                current.Use();
                return;
            }

            current.Use();
        }

        private void ConsumeAutomationClosePromptInput()
        {
            Event current = Event.current;
            if (current != null && ShouldConsumeGuiEvent(current))
            {
                ClaimAutomationModalInput(frames: 4);
                current.Use();
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
            _outputLinkModeArmed = false;
            _selectedLinkTargetKey = string.Empty;
            _automationCodeOutputTargetKey = string.Empty;
            _automationCodeControllerKey = string.Empty;
            _lastCompileRevert = null;
            _lastSystemBlockLoweringRevert = null;
            _lastCompileBoundOutput = false;
            _lastRuntimeDiagnostics = AutomationRuntimeDiagnosticResult.Empty;
            _lastRuntimeDiagnosticsControllerKey = string.Empty;
            _wireSourceComponentId = NoWireSourceComponentId;
            _wireSourceOutputIndex = -1;
            _selectedCanvasComponentId = NoWireSourceComponentId;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
            ClearAutomationBlockWorkspace(persistCurrent: false);
            ClearSystemBlockWorkspace();
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
                if (!targetsByKey.TryGetValue(link.TargetKey, out refreshed))
                    refreshed = ResolvePersistedAutomationTarget(
                        link.TargetKey,
                        link.TargetPersistenceKey);

                link.RebindTarget(refreshed);
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
                    .Select(link => LinkIdentity(link.TargetKey, link.Direction)),
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
                AutomationLinkDirection direction = proxy.IsGenericGetter
                    ? AutomationLinkDirection.Input
                    : AutomationLinkDirection.Output;
                string identity = LinkIdentity(target?.StableKey, direction);
                if (target == null || existing.Contains(identity))
                    continue;

                _links.Add(new AutomationLink(_selectedController, target, direction));
                existing.Add(identity);
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
                AutomationTargetCatalog.TryTargetFromHit(hit, out AutomationTarget target) &&
                IsAutomationWorldPickableTarget(target))
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
                    RequestCloseEditor();
                else
                    TryOpenEditor();
            }
            else if (_editorOpen &&
                     (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
            {
                RemoveSelectedEsuBlock();
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

            _editorPage = page ?? AutomationEditorPage.Blocks;
            _editorOpen = true;
            FitEditorToViewport();
            return true;
        }

        private void CloseEditor()
        {
            _editorOpen = false;
            _resizingEditor = false;
            _panningBlockCanvas = false;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
        }

        private void RequestCloseEditor()
        {
            RequestAutomationClose(AutomationClosePromptAction.CloseEditor);
        }

        private void RequestAutomationClose(AutomationClosePromptAction action)
        {
            if (_automationClosePromptOpen)
                return;

            if (!HasAutomationExitWarningChanges())
            {
                CompleteAutomationCloseAction(action);
                return;
            }

            OpenAutomationClosePrompt(action);
        }

        private bool HasAutomationExitWarningChanges()
        {
            return HasUnsavedAutomationWorkspaceChanges() ||
                   HasUnappliedAutomationChanges();
        }

        private bool HasUnsavedAutomationWorkspaceChanges() =>
            _automationWorkspaceSnapshots.Count > 0;

        private bool HasUnappliedAutomationChanges() =>
            _automationNativeDirty ||
            _systemBlockDraftDirty ||
            _systemBlockInternalDirty;

        private void OpenAutomationClosePrompt(AutomationClosePromptAction action)
        {
            _automationClosePromptOpen = true;
            _automationClosePromptAction = action;
            CloseAutomationContextMenu();
            CloseAutomationPropertyPicker();
            CloseAutomationTargetPicker();
            InfoStore.Add("Unsaved Automation Editor changes. Save, close without saving, or keep editing.");
        }

        private void CloseAutomationClosePrompt()
        {
            _automationClosePromptOpen = false;
            _automationClosePromptAction = AutomationClosePromptAction.None;
        }

        private void SaveAndCloseFromAutomationPrompt()
        {
            SaveSelectedAutomationWorkspace();
            AutomationClosePromptAction action = _automationClosePromptAction;
            CloseAutomationClosePrompt();
            CompleteAutomationCloseAction(action);
        }

        private void DiscardAndCloseFromAutomationPrompt()
        {
            DiscardUnsavedAutomationWorkspaceStaging();
            ReloadSelectedAutomationWorkspaceFromProfile();
            AutomationClosePromptAction action = _automationClosePromptAction;
            CloseAutomationClosePrompt();
            CompleteAutomationCloseAction(action);
        }

        private void CompleteAutomationCloseAction(AutomationClosePromptAction action)
        {
            switch (action)
            {
                case AutomationClosePromptAction.CloseEditor:
                    CloseEditor();
                    _status = "Automation editor closed.";
                    break;
                case AutomationClosePromptAction.SwitchToDecorationEdit:
                    SwitchToDecorationEditRequested = true;
                    break;
                case AutomationClosePromptAction.CloseMode:
                    _closeRequested = true;
                    break;
            }
        }

        private void SaveSelectedAutomationWorkspace()
        {
            if (_selectedController == null)
            {
                _status = "Select a Breadboard/ACB controller before saving Automation workspace state.";
                return;
            }

            PersistSelectedAutomationWorkspaceState(saveProfile: true);
            _status = HasUnappliedAutomationChanges()
                ? "Saved Automation workspace. Apply blocks/template changes before closing if native behavior should update now."
                : "Saved Automation workspace.";
        }

        private void MarkAutomationWorkspaceDirty()
        {
            if (_selectedController == null)
                return;

            string key = AutomationWorkspaceStorageKey(_selectedController);
            if (_automationWorkspaceSnapshots.ContainsKey(key))
                return;

            _automationWorkspaceSnapshots[key] =
                CloneAutomationWorkspaceData(StoredAutomationWorkspaceFor(_selectedController));
        }

        private void DiscardUnsavedAutomationWorkspaceStaging()
        {
            try
            {
                SerializationHudProfile.ProfileData profile = SerializationHudProfile.Data;
                if (profile?.AutomationWorkspaces == null)
                    return;

                foreach (KeyValuePair<string, SerializationHudProfile.AutomationWorkspaceData> snapshot in _automationWorkspaceSnapshots)
                    RestoreAutomationWorkspaceProfileSnapshot(profile, snapshot.Key, snapshot.Value);
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception(
                    "Automation Editor",
                    exception,
                    "Automation workspace discard failed");
            }
            finally
            {
                _automationWorkspaceSnapshots.Clear();
                _automationNativeDirty = false;
            }
        }

        private void ReloadSelectedAutomationWorkspaceFromProfile()
        {
            if (_selectedController == null)
                return;

            string controllerKey = _selectedController.StableKey;
            _links.RemoveAll(link =>
                string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal));
            ClearAutomationBlockWorkspace(persistCurrent: false);
            RestoreSelectedAutomationWorkspaceState();
            _blockLoweringPlan = null;
            _blockLoweringStatus = string.Empty;
            _automationNativeDirty = false;
            _status = "Closed Automation editor without saving; restored last saved workspace state.";
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
                if (TryCancelAutomationLinkModeRightClick())
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

        private bool TryCancelAutomationLinkModeRightClick()
        {
            if (_tool != AutomationTool.Link)
                return false;

            if (_pendingLinkTarget != null)
            {
                string target = _pendingLinkTarget.Label;
                _pendingLinkTarget = null;
                _status = "Input link selection cancelled: " + target + ".";
                return true;
            }

            if (!_outputLinkModeArmed ||
                _selectedController == null)
                return false;

            string selected = SelectedControllerSummary();
            _outputLinkModeArmed = false;
            _status = string.Equals(selected, "none", StringComparison.Ordinal)
                ? "Output link selection cancelled."
                : "Output link selection cancelled: " + selected + ".";
            return true;
        }

        private bool TryCancelAutomationRightClick()
        {
            if (TryCancelAutomationPlacementRightClick())
                return true;

            if (_selectedController == null &&
                _pendingLinkTarget == null &&
                string.IsNullOrEmpty(_selectedLinkTargetKey) &&
                _selectedCanvasComponentId == NoWireSourceComponentId &&
                _wireSourceComponentId == NoWireSourceComponentId)
            {
                return false;
            }

            string selected = SelectedControllerSummary();
            _selectedController = null;
            _pendingLinkTarget = null;
            _outputLinkModeArmed = false;
            _selectedLinkTargetKey = string.Empty;
            _automationCodeOutputTargetKey = string.Empty;
            _automationCodeControllerKey = string.Empty;
            _lastCompileRevert = null;
            _lastSystemBlockLoweringRevert = null;
            _lastCompileBoundOutput = false;
            _lastRuntimeDiagnostics = AutomationRuntimeDiagnosticResult.Empty;
            _lastRuntimeDiagnosticsControllerKey = string.Empty;
            _wireSourceComponentId = NoWireSourceComponentId;
            _wireSourceOutputIndex = -1;
            _selectedCanvasComponentId = NoWireSourceComponentId;
            _canvasDragComponentId = NoWireSourceComponentId;
            _canvasDragPreviewDelta = Vector2.zero;
            ClearSystemBlockWorkspace();
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
                AutomationTargetCatalog.TryTargetFromHit(hit, out AutomationTarget target) &&
                IsAutomationWorldPickableTarget(target))
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
                if (_pendingLinkTarget != null &&
                    !string.Equals(_pendingLinkTarget.StableKey, target.StableKey, StringComparison.Ordinal))
                {
                    SelectAutomationController(target, controllerStatusPrefix, armOutputLinkMode: true);
                    ToggleLink(target, _pendingLinkTarget, AutomationLinkDirection.Input);
                    _pendingLinkTarget = null;
                    ArmOutputLinkModeForSelectedController();
                    return true;
                }

                if (_outputLinkModeArmed &&
                    TryToggleControllerTargetLink(target))
                    return true;

                SelectAutomationController(target, controllerStatusPrefix, armOutputLinkMode: true);
                return true;
            }

            if (_selectedController == null ||
                !_outputLinkModeArmed)
            {
                _pendingLinkTarget = target;
                _outputLinkModeArmed = false;
                _status = "Input source selected: " + target.Label + ". Click a Breadboard/controller to read from it.";
                return true;
            }

            if (!IsAutomationWorldPickableTarget(target))
            {
                _status = target.Label + " is hidden by the Automation world filter. Use search, a specific filter, or the target list to link it.";
                return true;
            }

            if (!TargetVisibleInBrowser(target) &&
                !IsLinked(_selectedController, target))
            {
                _status = target.Label + " is in the other Automation target browser. Search or switch Important/Generic to link it.";
                return true;
            }

            _pendingLinkTarget = null;
            ToggleLink(_selectedController, target, AutomationLinkDirection.Output);
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
            _contextMenuBlockNodeId = string.Empty;
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
            _contextMenuLinkDirection = _pendingLinkTarget != null
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output;
            _contextMenuComponentId = NoWireSourceComponentId;
            _contextMenuBlockNodeId = string.Empty;
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
            _contextMenuLinkDirection = AutomationLinkDirection.Output;
            _contextMenuComponentId = NoWireSourceComponentId;
            _contextMenuBlockNodeId = string.Empty;
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
            _contextMenuLinkDirection = link.Direction;
            _contextMenuComponentId = NoWireSourceComponentId;
            _contextMenuBlockNodeId = string.Empty;
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
            _contextMenuBlockNodeId = string.Empty;
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationBlockContextMenu(AutomationBlockNode node, Vector2 mouse)
        {
            if (node == null)
                return;

            _contextMenuKind = AutomationContextMenuKind.Block;
            _contextMenuPlacement = null;
            _contextMenuControllerKey = _selectedController?.StableKey ?? string.Empty;
            _contextMenuTargetKey = string.Empty;
            _contextMenuLinkDirection = node.LinkDirection;
            _contextMenuComponentId = NoWireSourceComponentId;
            _contextMenuBlockNodeId = node.Id;
            _blockWorkspace?.Select(node.Id);
            OpenAutomationContextMenuAt(mouse, buttonCount: ContextMenuButtonCount());
        }

        private void OpenAutomationContextMenuAt(Vector2 mouse, int buttonCount)
        {
            CloseAutomationPropertyPicker();
            CloseAutomationTargetPicker();
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

            AutomationContextMenuItem[] items = AutomationContextMenuItems().ToArray();
            if (items.Length == 0)
            {
                CloseAutomationContextMenu();
                return;
            }

            _contextMenuRect = AutomationContextRect(_contextMenuAnchor, items.Length);
            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_contextMenuRect.Contains(current.mousePosition))
            {
                CloseAutomationContextMenu();
                AutomationInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            bool shouldConsume = ShouldConsumeAutomationContextEvent(current);
            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -25000);
            AutomationContextAction action;
            try
            {
                action = DrawAutomationContextMenuWindow(items);
            }
            finally
            {
                GUI.depth = previousDepth;
            }

            if (action != AutomationContextAction.None)
            {
                AutomationInputScope.ClaimBuildInputForFrames(3);
                current?.Use();
                ExecuteAutomationContextAction(action);
                return;
            }

            if (shouldConsume && current != null)
            {
                AutomationInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private AutomationContextAction DrawAutomationContextMenuWindow(AutomationContextMenuItem[] items)
        {
            AutomationContextAction action = AutomationContextAction.None;
            GUI.Box(_contextMenuRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(_contextMenuRect, 5f);
            Rect titleRect = new Rect(inner.x, inner.y, inner.width, EsuHudLayout.Scale(24f));
            GUI.Label(titleRect, AutomationContextTitle(), DecorationEditorTheme.SubHeader);
            float y = titleRect.yMax + EsuHudLayout.Scale(3f);
            Event current = Event.current;
            for (int index = 0; index < items.Length; index++)
            {
                AutomationContextMenuItem item = items[index];
                Rect itemRect = new Rect(inner.x, y, inner.width, EsuHudLayout.Scale(24f));
                GUIStyle style = item.Enabled
                    ? DecorationEditorTheme.Button
                    : DecorationEditorTheme.DisabledButton;
                bool pressed = current != null &&
                               current.type == EventType.MouseDown &&
                               current.button == 0 &&
                               itemRect.Contains(current.mousePosition);
                bool clicked = GUI.Button(itemRect, new GUIContent(item.Label, item.Tooltip), style);
                EsuCursorTooltip.Register(itemRect, item.Tooltip);
                if (item.Enabled && (pressed || clicked))
                {
                    action = item.Action;
                    break;
                }
                y = itemRect.yMax + EsuHudLayout.Scale(2f);
            }
            return action;
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
                case AutomationContextMenuKind.Block:
                    return "ESU Block";
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

                case AutomationContextMenuKind.Block:
                    foreach (AutomationContextMenuItem item in BlockContextMenuItems())
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
                    "Open the ESU Blocks builder for this controller.",
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
            AutomationLinkDirection direction = _contextMenuLinkDirection;
            bool linked = _selectedController != null && target != null && IsLinked(_selectedController, target, direction);
            AutomationLink link = ContextLinkForTarget(target, direction);
            string directionText = LinkDirectionLabel(direction).ToLowerInvariant();
            yield return new AutomationContextMenuItem(
                linked ? "Unlink " + directionText : "Link " + directionText,
                linked ? AutomationContextAction.UnlinkTarget : AutomationContextAction.LinkTarget,
                linked
                    ? "Remove this target from the selected controller."
                    : "Link this target as an Automation " + directionText + ".",
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
                "Open builder",
                AutomationContextAction.OpenEditor,
                "Open the ESU Blocks builder for this link's controller.",
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

        private IEnumerable<AutomationContextMenuItem> BlockContextMenuItems()
        {
            AutomationBlockNode node = ContextBlockNode();
            bool hasNode = node != null;
            int index = node == null ? -1 : BlockNodeIndex(node.Id);
            int count = _blockWorkspace?.ExecutableNodes.Count ?? 0;
            bool inExecutableStack = node?.SnappedToStack == true;
            yield return new AutomationContextMenuItem(
                "Move up",
                AutomationContextAction.MoveBlockUp,
                "Move this ESU Block one slot up.",
                hasNode && inExecutableStack && index > 0);
            yield return new AutomationContextMenuItem(
                "Move down",
                AutomationContextAction.MoveBlockDown,
                "Move this ESU Block one slot down.",
                hasNode && inExecutableStack && index >= 0 && index < count - 1);
            yield return new AutomationContextMenuItem(
                "Delete block",
                AutomationContextAction.DeleteBlock,
                "Remove this ESU Block from the canvas.",
                hasNode);
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
                    RequestCloseEditor();
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
                case AutomationContextAction.MoveBlockUp:
                    MoveContextEsuBlock(-1);
                    break;
                case AutomationContextAction.MoveBlockDown:
                    MoveContextEsuBlock(1);
                    break;
                case AutomationContextAction.DeleteBlock:
                    RemoveContextEsuBlock();
                    break;
            }

            CloseAutomationContextMenu();
        }

        private void OpenContextEditor()
        {
            AutomationTarget controller = ContextController();
            if (controller != null)
                SelectAutomationController(controller);
            TryOpenEditor(AutomationEditorPage.Blocks);
        }

        private void ToggleContextTargetLink()
        {
            AutomationTarget target = ContextTarget() ?? ContextLink()?.Target;
            AutomationLinkDirection direction = _contextMenuLinkDirection;
            if (!CanContextLinkTarget(target) && !IsLinked(_selectedController, target, direction))
            {
                _status = _selectedController == null
                    ? "Select a controller before linking Automation targets."
                    : "This Automation target cannot be linked from the selected controller.";
                return;
            }

            ToggleLink(_selectedController, target, direction);
        }

        private void InspectContextLink()
        {
            AutomationLink link = ContextLink();
            if (link == null)
                link = ContextLinkForTarget(ContextTarget(), _contextMenuLinkDirection);
            if (link == null)
                return;

            SelectAutomationLink(link);
            _showLeftPanel = true;
            _status = "Inspecting Automation " + link.DirectionLabel.ToLowerInvariant() + " link: " + link.TargetLabel + ".";
        }

        private void RemoveAutomationLink(AutomationLink link)
        {
            if (link == null)
                return;

            string label = link.TargetLabel;
            string status = "Removed " + link.DirectionLabel.ToLowerInvariant() + " automation link to " + label + ".";
            _links.Remove(link);
            if (IsSelectedAutomationLink(link))
                SelectAutomationLink(null);
            if (link.Direction == AutomationLinkDirection.Output &&
                string.Equals(_automationCodeOutputTargetKey, link.TargetKey, StringComparison.Ordinal))
            {
                _automationCodeOutputTargetKey = string.Empty;
            }
            InvalidateAutomationLinksChanged(status);
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
            _targetBrowserMode = IsImportantAutomationTarget(target)
                ? AutomationTargetBrowserMode.Important
                : AutomationTargetBrowserMode.Generic;
            _showAdvancedFilters = true;
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
                _lastSystemBlockLoweringRevert = null;
                _selectedCanvasComponentId = NoWireSourceComponentId;
                _canvasDragComponentId = NoWireSourceComponentId;
                _canvasDragPreviewDelta = Vector2.zero;
                ClearSystemBlockWorkspace();
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

        private void SelectContextBlockNode()
        {
            AutomationBlockNode node = ContextBlockNode();
            if (node == null)
                return;

            _blockWorkspace?.Select(node.Id);
        }

        private bool TrySelectContextBlockNode(out AutomationBlockNode node)
        {
            node = ContextBlockNode();
            if (node == null)
            {
                _status = "ESU Block context target is no longer available.";
                return false;
            }

            _blockWorkspace?.Select(node.Id);
            return true;
        }

        private void MoveContextEsuBlock(int delta)
        {
            if (!TrySelectContextBlockNode(out AutomationBlockNode node))
                return;

            int index = BlockNodeIndex(node.Id);
            int count = _blockWorkspace?.ExecutableNodes.Count ?? 0;
            bool canMove = node.SnappedToStack &&
                           index >= 0 &&
                           (delta < 0 ? index > 0 : index < count - 1);
            if (!canMove)
            {
                _status = "Only snapped ESU Blocks inside forever can move up or down.";
                return;
            }

            MoveSelectedEsuBlock(delta);
        }

        private void RemoveContextEsuBlock()
        {
            if (!TrySelectContextBlockNode(out _))
                return;

            RemoveSelectedEsuBlock();
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
                link.Direction == _contextMenuLinkDirection &&
                (string.IsNullOrWhiteSpace(_contextMenuControllerKey) ||
                 string.Equals(link.ControllerKey, _contextMenuControllerKey, StringComparison.Ordinal)));
        }

        private AutomationLink ContextLinkForTarget(
            AutomationTarget target,
            AutomationLinkDirection direction = AutomationLinkDirection.Output)
        {
            if (_selectedController == null || target == null)
                return null;

            string controllerKey = _selectedController.StableKey;
            string targetKey = target.StableKey;
            return _links.FirstOrDefault(link =>
                string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                string.Equals(link.TargetKey, targetKey, StringComparison.Ordinal) &&
                link.Direction == direction);
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

        private AutomationBlockNode ContextBlockNode()
        {
            if (_blockWorkspace == null ||
                string.IsNullOrWhiteSpace(_contextMenuBlockNodeId))
            {
                return null;
            }

            return _blockWorkspace.Nodes.FirstOrDefault(node =>
                string.Equals(node.Id, _contextMenuBlockNodeId, StringComparison.Ordinal));
        }

        private bool TryOpenAutomationRowContextMenu(Rect row, Action<Vector2> open)
        {
            Event current = Event.current;
            if (ShouldSuppressAutomationBackgroundDirectInput(current))
                return false;

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
            _contextMenuBlockNodeId = string.Empty;
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

        private void ToggleLink(
            AutomationTarget controller,
            AutomationTarget target,
            AutomationLinkDirection direction = AutomationLinkDirection.Output)
        {
            string controllerKey = controller.StableKey;
            string targetKey = target.StableKey;
            int existing = _links.FindIndex(
                link => link.ControllerKey == controllerKey &&
                        link.TargetKey == targetKey &&
                        link.Direction == direction);
            if (existing >= 0)
            {
                AutomationLink link = _links[existing];
                _links.RemoveAt(existing);
                if (IsSelectedAutomationLink(link))
                    SelectAutomationLink(null);
                if (direction == AutomationLinkDirection.Output &&
                    string.Equals(_automationCodeOutputTargetKey, targetKey, StringComparison.Ordinal))
                {
                    _automationCodeOutputTargetKey = string.Empty;
                }
                InvalidateAutomationLinksChanged(
                    "Removed " + LinkDirectionLabel(direction).ToLowerInvariant() + " link to " + target.Label + ".");
                return;
            }

            var added = new AutomationLink(controller, target, direction);
            _links.Add(added);
            SelectAutomationLink(added);
            if (direction == AutomationLinkDirection.Output &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(target))
            {
                _automationCodeOutputTargetKey = targetKey;
            }
            string status = IsAcbControllerBridgeTarget(target)
                ? "Linked " + controller.Label + " to ACB Controller button keyword output."
                : IsAcbProxyTarget(target)
                    ? "Linked " + controller.Label + " to ACB rule proxy target."
                    : "Linked " + LinkDirectionLabel(direction).ToLowerInvariant() + ": " +
                      controller.Label + " -> " + target.Label + ".";
            InvalidateAutomationLinksChanged(status);
        }

        private void SelectAutomationController(
            AutomationTarget target,
            string statusPrefix = "Selected controller: ",
            bool armOutputLinkMode = false)
        {
            if (target == null)
                return;

            bool changed = _selectedController == null ||
                           !string.Equals(_selectedController.StableKey, target.StableKey, StringComparison.Ordinal);
            if (changed)
                PersistSelectedAutomationWorkspaceState(saveProfile: false);

            _selectedController = target;
            if (changed)
            {
                _selectedLinkTargetKey = string.Empty;
                _automationCodeOutputTargetKey = string.Empty;
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
                _lastSystemBlockLoweringRevert = null;
                _selectedCanvasComponentId = NoWireSourceComponentId;
                _canvasDragComponentId = NoWireSourceComponentId;
                _canvasDragPreviewDelta = Vector2.zero;
                ClearAutomationBlockWorkspace(persistCurrent: false);
                ClearSystemBlockWorkspace();
                RestoreSelectedAutomationWorkspaceState();
            }

            _outputLinkModeArmed = armOutputLinkMode && _tool == AutomationTool.Link;
            _status = statusPrefix + target.Label + ".";
            if (_outputLinkModeArmed)
            {
                _status += " Output mode: click a target it should affect, or right-click to exit.";
            }
        }

        private void ArmOutputLinkModeForSelectedController()
        {
            if (_selectedController == null || _tool != AutomationTool.Link)
                return;

            _outputLinkModeArmed = true;
            _status += " Output mode: click a target it should affect, or right-click to exit.";
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
            AutomationTarget target,
            AutomationLinkDirection? direction = null)
        {
            if (controller == null || target == null)
                return false;

            string controllerKey = controller.StableKey;
            string targetKey = target.StableKey;
            return _links.Any(link =>
                link.ControllerKey == controllerKey &&
                link.TargetKey == targetKey &&
                (!direction.HasValue || link.Direction == direction.Value));
        }

        private static string LinkDirectionLabel(AutomationLinkDirection direction) =>
            direction == AutomationLinkDirection.Input ? "Input" : "Output";

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
                    candidate.IsController || TargetVisibleInBrowser(candidate)
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
                return TargetVisibleInBrowser(candidate);

            return IsAutomationWorldPickableTarget(candidate);
        }

        private bool IsAutomationWorldPickableTarget(AutomationTarget candidate)
        {
            if (candidate == null)
                return false;

            if (candidate.IsController)
                return true;

            if (_selectedController == null)
                return TargetVisibleInBrowser(candidate);

            if (IsLinked(_selectedController, candidate))
                return true;

            if (!TargetVisibleInBrowser(candidate))
                return false;

            if (!AutomationTargetCatalog.MatchesSearch(candidate, _targetSearchText))
                return false;

            if (!string.IsNullOrWhiteSpace(_targetSearchText))
                return true;

            if (_showAdvancedFilters &&
                _filter != AutomationTargetCategory.All &&
                _filter != AutomationTargetCategory.BreadboardReadable)
                return true;

            if (_targetBrowserMode == AutomationTargetBrowserMode.Generic)
                return false;

            return IsImportantAutomationTarget(candidate);
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
            if (occupied)
                return;

            Color edge = new Color(0.18f, 1f, 0.55f, 0.94f);
            DrawCellBox(hit.Construct, cell, edge, 4.8f, 0.12f);
        }

        private void DrawTargetHighlights()
        {
            IEnumerable<AutomationTarget> visible = _targets
                .Where(target =>
                    target != null &&
                    ShouldDrawAutomationWorldHighlight(target))
                .Take(WorldHighlightLimit);

            foreach (AutomationTarget target in visible)
            {
                if (_selectedController != null &&
                    target.StableKey == _selectedController.StableKey)
                {
                    continue;
                }

                bool linked = TryGetLinkedTargetHighlightColor(target, out Color linkedColor);
                bool hovered = _hoverTarget != null &&
                               string.Equals(_hoverTarget.StableKey, target.StableKey, StringComparison.Ordinal);
                Color color = target.IsController
                    ? new Color(0.1f, 0.95f, 1f, 0.78f)
                    : linked
                        ? linkedColor
                        : CategoryColor(target.Category);
                float width = hovered ? 2.8f : target.IsController ? 3.2f : 2.2f;
                float pulse = hovered ? 0.055f : target.IsController ? 0.07f : 0.035f;
                DrawTargetBox(target, color, width, pulse);
            }
        }

        private bool TryGetLinkedTargetHighlightColor(AutomationTarget target, out Color color)
        {
            color = default(Color);
            if (_selectedController == null || target == null)
                return false;

            string controllerKey = _selectedController.StableKey;
            string targetKey = target.StableKey;
            AutomationLink selected = SelectedLink();
            AutomationLink link = selected != null &&
                                  string.Equals(selected.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                                  string.Equals(selected.TargetKey, targetKey, StringComparison.Ordinal)
                ? selected
                : _links.FirstOrDefault(candidate =>
                      string.Equals(candidate.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                      string.Equals(candidate.TargetKey, targetKey, StringComparison.Ordinal) &&
                      candidate.Direction == AutomationLinkDirection.Output) ??
                  _links.FirstOrDefault(candidate =>
                      string.Equals(candidate.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                      string.Equals(candidate.TargetKey, targetKey, StringComparison.Ordinal) &&
                      candidate.Direction == AutomationLinkDirection.Input);
            if (link == null)
                return false;

            color = AutomationLinkColor(link.Direction, 0.86f);
            return true;
        }

        private bool ShouldDrawAutomationWorldHighlight(AutomationTarget target)
        {
            if (target == null)
                return false;

            if (_pendingLinkTarget != null &&
                string.Equals(_pendingLinkTarget.StableKey, target.StableKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (_hoverTarget != null &&
                string.Equals(_hoverTarget.StableKey, target.StableKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsLinked(_selectedController, target))
                return true;

            return ShouldDrawFilteredAutomationWorldHighlight(target);
        }

        private bool ShouldDrawFilteredAutomationWorldHighlight(AutomationTarget target)
        {
            if (target == null ||
                !TargetVisibleInBrowser(target))
            {
                return false;
            }

            bool matchesSearch = AutomationTargetCatalog.MatchesSearch(target, _targetSearchText);
            if (!matchesSearch)
                return false;

            if (!string.IsNullOrWhiteSpace(_targetSearchText))
                return true;

            if (_showAdvancedFilters &&
                _filter != AutomationTargetCategory.All &&
                _filter != AutomationTargetCategory.BreadboardReadable)
            {
                return true;
            }

            if (_targetBrowserMode == AutomationTargetBrowserMode.Generic)
                return false;

            return IsImportantAutomationTarget(target);
        }

        private static bool IsImportantAutomationTarget(AutomationTarget target)
        {
            if (target == null)
                return false;

            if (AutomationTargetCatalog.IsAcbActionTarget(target))
                return true;

            switch (target.Category)
            {
                case AutomationTargetCategory.Controllers:
                case AutomationTargetCategory.Spinblocks:
                case AutomationTargetCategory.TurretsWeapons:
                case AutomationTargetCategory.Propulsion:
                case AutomationTargetCategory.Pistons:
                case AutomationTargetCategory.Pumps:
                case AutomationTargetCategory.ControlSurfaces:
                case AutomationTargetCategory.Ai:
                case AutomationTargetCategory.Missiles:
                case AutomationTargetCategory.Lights:
                case AutomationTargetCategory.ShieldsDefence:
                case AutomationTargetCategory.Detection:
                case AutomationTargetCategory.DoorsDocking:
                case AutomationTargetCategory.ResourcePower:
                    return true;
                default:
                    return false;
            }
        }

        private void RegisterAutomationWorldHoverTooltip()
        {
            if (_hoverTarget == null ||
                _tool == AutomationTool.Place ||
                AutomationInputScope.MouseOverUi ||
                IsMouseCurrentlyOverUi())
            {
                return;
            }

            string tooltip = AutomationWorldHoverTooltip(_hoverTarget);
            if (string.IsNullOrWhiteSpace(tooltip))
                return;

            Vector2 mouse = Event.current.mousePosition;
            Rect cursorRect = new Rect(
                mouse.x - EsuHudLayout.Scale(12f),
                mouse.y - EsuHudLayout.Scale(12f),
                EsuHudLayout.Scale(24f),
                EsuHudLayout.Scale(24f));
            EsuCursorTooltip.Register(cursorRect, tooltip);
        }

        private string AutomationWorldHoverTooltip(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            if (target.IsController)
            {
                if (_selectedController != null && CanLinkControllerTarget(_selectedController, target))
                    return IsLinked(_selectedController, target)
                        ? "Already linked: click to unlink " + target.Label + "."
                        : "Link controller target " + target.Label + ".";

                return "Select " + target.Label + ".";
            }

            if (_selectedController == null)
                return "Select a Breadboard first.";

            if (IsLinked(_selectedController, target))
                return "Already linked: click to unlink " + target.Label + ".";

            if (!TargetVisibleInBrowser(target) &&
                !IsImportantAutomationTarget(target))
            {
                return string.Empty;
            }

            return "Link to " + target.Label + ".";
        }

        private void DrawLinks()
        {
            if (_selectedController == null)
                return;

            string key = _selectedController.StableKey;
            Vector3 controllerCenter = _selectedController.WorldCenter;
            foreach (AutomationLink link in _links)
            {
                if (link.ControllerKey != key || link.Target == null)
                    continue;

                Vector3 targetCenter = link.Target.WorldCenter;
                Vector3 start = link.Direction == AutomationLinkDirection.Input
                    ? targetCenter
                    : controllerCenter;
                Vector3 end = link.Direction == AutomationLinkDirection.Input
                    ? controllerCenter
                    : targetCenter;
                Color color = AutomationLinkColor(link.Direction, 0.96f);
                DecorationEditorOverlay.Arrow(
                    start,
                    end,
                    color,
                    3f,
                    0.26f);
                float t = Mathf.Repeat(Time.unscaledTime * 0.82f + Mathf.Abs(link.TargetKey.GetHashCode() % 17) * 0.037f, 1f);
                DecorationEditorOverlay.Circle(
                    Vector3.Lerp(start, end, t),
                    0.12f,
                    color,
                    Vector3.up,
                    2.2f,
                    12);
                DecorationEditorOverlay.Circle(
                    end,
                    0.22f,
                    color,
                    Vector3.up,
                    2.3f,
                    16);
            }
        }

        private static Color AutomationLinkColor(
            AutomationLinkDirection direction,
            float alpha)
        {
            return direction == AutomationLinkDirection.Output
                ? new Color(0.62f, 0.36f, 1f, alpha)
                : new Color(0.2f, 1f, 0.65f, alpha);
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
                RequestSwitchToDecorationEdit();
            if (ToolbarButton("anchor", "Link", "Select controllers and link target blocks.", _tool == AutomationTool.Link))
                _tool = AutomationTool.Link;
            if (ToolbarButton("create", "Place", "Place the selected Breadboard/ACB controller.", _tool == AutomationTool.Place))
            {
                _tool = AutomationTool.Place;
                _status = PlacementArmedStatus();
            }
            if (ToolbarButton("open", "Edit", "Open the ESU Blocks automation editor.", _editorOpen))
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
            if (ToolbarButton(null, "Close", "Close Automation Editor.", false))
                RequestHotkeyClose();
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static bool ToolbarButton(string icon, string label, string tooltip, bool active)
        {
            Texture image = string.IsNullOrWhiteSpace(icon)
                ? null
                : DecorationEditorIconCatalog.Get(icon);
            return AutomationGUILayoutButton(
                new GUIContent(label, image, tooltip),
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
                float iconSize = Mathf.Min(
                    EsuHudLayout.Scale(14f),
                    Mathf.Max(1f, Mathf.Min(rect.height * 0.34f, rect.width * 0.34f)));
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
                DrawFittedSingleLineLabel(
                    textRect,
                    text,
                    AutomationButtonTextStyle(baseStyle, TextAnchor.MiddleCenter),
                    TextAnchor.MiddleCenter,
                    EsuHudLayout.FontSize(8));
            }
            else if (hasText)
            {
                float iconSize = Mathf.Min(
                    EsuHudLayout.Scale(13f),
                    Mathf.Max(1f, Mathf.Min(rect.height - EsuHudLayout.Scale(10f), rect.width * 0.24f)));
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
                DrawFittedSingleLineLabel(
                    textRect,
                    text,
                    AutomationButtonTextStyle(baseStyle, TextAnchor.MiddleCenter),
                    TextAnchor.MiddleCenter,
                    EsuHudLayout.FontSize(8));
            }
            else if (icon != null)
            {
                float iconSize = Mathf.Min(
                    EsuHudLayout.Scale(14f),
                    Mathf.Max(1f, Mathf.Min(rect.height - EsuHudLayout.Scale(10f), rect.width - EsuHudLayout.Scale(10f))));
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

        private static void DrawFittedSingleLineLabel(
            Rect rect,
            string text,
            GUIStyle baseStyle,
            TextAnchor alignment,
            int minimumFontSize)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            GUIStyle style = FittedSingleLineStyle(rect, text, baseStyle, alignment, minimumFontSize);
            GUI.Label(rect, EllipsizeText(text ?? string.Empty, style, rect.width), style);
        }

        private static GUIStyle FittedSingleLineStyle(
            Rect rect,
            string text,
            GUIStyle baseStyle,
            TextAnchor alignment,
            int minimumFontSize)
        {
            var style = new GUIStyle(baseStyle ?? GUI.skin.label)
            {
                alignment = alignment,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;

            int baseFontSize = style.fontSize > 0 ? style.fontSize : EsuHudLayout.FontSize(11);
            int heightLimited = Mathf.FloorToInt(Mathf.Max(minimumFontSize, rect.height * 0.68f));
            style.fontSize = Mathf.Clamp(Math.Min(baseFontSize, heightLimited), minimumFontSize, baseFontSize);
            while (style.fontSize > minimumFontSize &&
                   style.CalcSize(new GUIContent(text ?? string.Empty)).x > rect.width)
            {
                style.fontSize--;
            }

            return style;
        }

        private static string EllipsizeText(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || style == null || maxWidth <= 1f)
                return string.Empty;

            if (style.CalcSize(new GUIContent(text)).x <= maxWidth)
                return text;

            const string suffix = "...";
            if (style.CalcSize(new GUIContent(suffix)).x > maxWidth)
                return string.Empty;

            int length = text.Length;
            while (length > 0)
            {
                string candidate = text.Substring(0, length).TrimEnd() + suffix;
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                    return candidate;
                length--;
            }

            return suffix;
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
            Rect titleRect = GUILayoutUtility.GetRect(
                EsuHudLayout.Scale(160f),
                headerHeight,
                GUILayout.Width(EsuHudLayout.Scale(160f)),
                GUILayout.Height(headerHeight));
            DrawAutomationSingleLineIconRow(
                titleRect,
                "build",
                "Automation Editor",
                DecorationEditorTheme.SubHeader);
            GUILayout.Label("Mode: " + ToolLabel(_tool), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(180f)));
            GUILayout.Label("Targets: " + TargetBrowserSummary(), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(190f)));
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
                    new GUIContent("Editor", DecorationEditorIconCatalog.Get("open"), "Open the ESU Blocks automation editor."),
                    DecorationEditorTheme.ToolButton(_editorOpen),
                    GUILayout.Width(EsuHudLayout.Scale(80f)),
                    GUILayout.Height(controlsHeight)))
            {
                if (_editorOpen)
                    RequestCloseEditor();
                else
                    TryOpenEditor();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _editorOpen;
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit", DecorationEditorIconCatalog.Get("focus"), "Fit the automation editor to the viewport."),
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
            if (HasAutomationExitWarningChanges())
                return DecorationEditorTheme.Warning;

            if (_tool == AutomationTool.Place && _selectedPlacement == null)
                return DecorationEditorTheme.Warning;

            return _selectedController == null && _tool == AutomationTool.Link
                ? DecorationEditorTheme.Mini
                : DecorationEditorTheme.Body;
        }

        private string WorkspaceStageLabel()
        {
            if (HasUnsavedAutomationWorkspaceChanges())
                return "Unsaved";
            if (HasUnappliedAutomationChanges())
                return "Needs Apply";

            if (_editorOpen)
            {
                if (IsSystemBlockWorkspaceOpen())
                    return _editorPage == AutomationEditorPage.System
                        ? "System ports"
                        : _editorPage == AutomationEditorPage.Code
                            ? "System lowering"
                            : "System graph";
                if (_editorPage == AutomationEditorPage.Blocks)
                    return "ESU Blocks";
                if (_editorPage == AutomationEditorPage.Graph)
                    return "Native graph";
                if (_editorPage == AutomationEditorPage.System)
                    return "System Block";
                return "Recipe compile";
            }

            if (_tool == AutomationTool.Place)
                return _selectedPlacement == null ? "Choose controller" : "Place controller";
            if (_selectedController == null)
                return "Select controller";
            return SelectedLinks.Count == 0 ? "Link targets" : "Build blocks";
        }

        private string NextSafeActionLine()
        {
            if (_editorOpen)
            {
                if (_selectedController == null)
                    return "Close the editor and select a live native controller.";

                if (IsSystemBlockWorkspaceOpen())
                {
                    if (_systemBlockInternalDirty)
                        return "Check the System Block internals, then Apply internal graph or Revert internal draft.";
                    if (_editorPage == AutomationEditorPage.System)
                        return "Adjust named ports, Apply template, or use Graph/Code to edit the block internals.";
                    if (_editorPage == AutomationEditorPage.Code)
                        return "Compile deterministic code into native nodes, then return to System Graph to record the lowering plan.";
                    return "Edit the internal graph plan, use Code for native lowering, or go Up to the host graph.";
                }

                if (_editorPage == AutomationEditorPage.Blocks)
                {
                    if (SelectedLinks.Count == 0)
                        return "Close the editor or use Link mode, click a target this Breadboard affects, then return to ESU Blocks.";
                    if (_blockLoweringPlan == null)
                        return "Use linked targets in Read/Set blocks, then Check blocks before applying.";
                    if (CanRevertLastAutomationCompile())
                        return "Review generated native nodes through Advanced, or use Revert blocks.";
                    return "Apply blocks to native Breadboard nodes, collapse selected blocks to a System Block, or open Advanced.";
                }

                if (_editorPage == AutomationEditorPage.System)
                {
                    if (_systemBlockDraftDirty)
                        return "Check the System Block ports, then Apply template or Revert draft.";
                    return "Define named ports from linked targets, save as a template, or return to Graph/Code for native lowering.";
                }

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
                {
                    if (CanRevertLastSystemBlockLowering())
                        return "Review generated System Block proxy nodes, or use Revert on the System Block graph node.";

                    return "Inspect native nodes, add Generic proxy nodes, switch to Code, or create/enter System Blocks.";
                }

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
                return "Click highlighted world targets to link them to the Breadboard.";

            return "Open Editor to build ESU Blocks, or click linked targets to inspect or unlink them.";
        }

        private string WorkspaceSafetyLine()
        {
            if (CanRevertLastAutomationCompile())
                return "Generated recipe nodes have a Revert compile path.";
            if (CanRevertLastSystemBlockLowering())
                return "System Block native proxy lowering has a Revert path.";
            if (_editorOpen && IsSystemBlockWorkspaceOpen())
                return "Nested System Blocks store ESU layout/group metadata only; Graph/Code lowering still writes native nodes.";
            if (_editorOpen && _editorPage == AutomationEditorPage.Blocks)
                return "ESU Blocks are metadata until Check/Apply lowers them into native Breadboard nodes.";
            if (_editorOpen && _editorPage == AutomationEditorPage.System)
                return "System Blocks store ESU-only names, ports, comments, breadcrumbs, and template metadata.";
            if (_editorOpen && _editorPage == AutomationEditorPage.Code)
                return "Recipes are deterministic and lower into native Breadboard nodes.";
            if (_editorOpen)
                return "Viewing existing native data does not mutate it; inspector edits apply directly.";
            if (_selectedController == null)
                return "Selection and target discovery are HUD-only until you place or edit a native controller.";
            return "World links are ESU workspace state until Blocks Apply or Advanced proxy tools create native nodes.";
        }

        private string WorkspaceCompatibilityBadge()
        {
            if (_selectedController == null)
                return "Native after controller selection";
            if (_editorOpen &&
                (_editorPage == AutomationEditorPage.Blocks ||
                 _editorPage == AutomationEditorPage.System ||
                 IsSystemBlockWorkspaceOpen()))
            {
                return "Native + ESU Layout";
            }

            return "Native";
        }

        private string WorkspaceCompatibilityLine()
        {
            return "Compatibility: " + WorkspaceCompatibilityBadge() + " | ESU Runtime Required: no";
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

        private void DrawPanelNextStepPrompt()
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label("Next: " + NextSafeActionLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label(WorkspaceCompatibilityLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
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
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(EsuHudLayout.CompactHeaderHeightBase),
                GUILayout.ExpandWidth(true));
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
                DrawFittedSingleLineLabel(
                    textRect,
                    text,
                    IconHeaderTextStyle(baseStyle),
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(9));
        }

        private static void DrawFixedCompactIconHeader(
            Rect rect,
            string text,
            string iconKey,
            GUIStyle baseStyle)
        {
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
                DrawFittedSingleLineLabel(
                    textRect,
                    text,
                    IconHeaderTextStyle(baseStyle),
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(9));
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
            DrawFittedSingleLineLabel(
                new Rect(textX, row.y + EsuHudLayout.Scale(2f), textWidth, titleHeight),
                title ?? string.Empty,
                titleStyle,
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));

            if (!string.IsNullOrWhiteSpace(detail))
            {
                DrawFittedSingleLineLabel(
                    new Rect(
                        textX,
                        row.y + titleHeight,
                        textWidth,
                        Mathf.Max(0f, row.height - titleHeight - EsuHudLayout.Scale(2f))),
                    detail,
                    detailStyle,
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(8));
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
            DrawFittedSingleLineLabel(
                textRect,
                label ?? string.Empty,
                SingleLineRowStyle(textStyle),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
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
            LabelRow("Targets", TargetBrowserSummary());
            LabelRow("Targets", _targets.Count.ToString("N0", CultureInfo.InvariantCulture));
            LabelRow("Visible links", SelectedLinks.Count.ToString("N0", CultureInfo.InvariantCulture));
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
                        new GUIContent("Open editor", DecorationEditorIconCatalog.Get("open"), "Open the ESU Blocks builder for the selected controller."),
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
                DrawLinkedTargetIdentityGuide();
                if (_editorOpen && _editorPage != AutomationEditorPage.Blocks)
                    DrawRuntimeDiagnosticsPanel();
            }

            DecorationEditorTheme.Separator();
            IReadOnlyList<AutomationLink> selectedLinks = SelectedLinks;
            AutomationLink[] matchingLinks = selectedLinks
                .Where(link => LinkedTargetListMatchesSearch(link, _linkedTargetSearch))
                .ToArray();
            if (selectedLinks.Count > 0)
            {
                DrawLinkedTargetListSearch();
                GUILayout.Label(
                    "Linked targets shown: " +
                    matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                    "/" +
                    selectedLinks.Count.ToString(CultureInfo.InvariantCulture) +
                    " matching for selected controller.",
                    DecorationEditorTheme.MiniWrap);
                if (matchingLinks.Length == 0)
                {
                    GUILayout.Label(
                        "No linked targets match the current linked-target search. Clear it to show Input/Output links.",
                        DecorationEditorTheme.Warning);
                }
                if (!string.IsNullOrWhiteSpace(_linkedTargetSearch) &&
                    selectedLinks.Any(IsSelectedAutomationLink) &&
                    !matchingLinks.Any(IsSelectedAutomationLink))
                {
                    GUILayout.Label("Selected linked target is hidden by the current linked-target search.", DecorationEditorTheme.Warning);
                }
            }

            DrawCompactIconHeader("Inputs", "visibility", DecorationEditorTheme.SubHeader);
            int inputTotal = selectedLinks.Count(link => link.Direction == AutomationLinkDirection.Input);
            AutomationLink[] inputLinks = matchingLinks
                .Where(link => link.Direction == AutomationLinkDirection.Input)
                .ToArray();
            if (inputTotal == 0)
                GUILayout.Label("No input targets linked. Click a target first, then click this Breadboard/controller.", DecorationEditorTheme.MiniWrap);
            else if (inputLinks.Length == 0)
                GUILayout.Label("No input links match the current linked-target search. Clear it to show linked Inputs.", DecorationEditorTheme.Warning);
            foreach (AutomationLink link in inputLinks)
                DrawLinkedTargetListRow(link);

            DecorationEditorTheme.Separator();
            DrawCompactIconHeader("Outputs", "anchor", DecorationEditorTheme.SubHeader);
            int outputTotal = selectedLinks.Count(link => link.Direction == AutomationLinkDirection.Output);
            AutomationLink[] outputLinks = matchingLinks
                .Where(link => link.Direction == AutomationLinkDirection.Output)
                .ToArray();
            if (outputTotal == 0)
                GUILayout.Label("No output targets linked. Click this Breadboard/controller first, then click a target it should affect.", DecorationEditorTheme.MiniWrap);
            else if (outputLinks.Length == 0)
                GUILayout.Label("No output links match the current linked-target search. Clear it to show linked Outputs.", DecorationEditorTheme.Warning);
            foreach (AutomationLink link in outputLinks)
                DrawLinkedTargetListRow(link);

            DrawSelectedLinkedTargetInspector();

            GUILayout.EndScrollView();
            DecorationEditorTheme.Separator();
            GUILayout.Label(_status, DecorationEditorTheme.Status);
            GUI.DragWindow(new Rect(0f, 0f, _leftPanelRect.width, EsuHudLayout.Scale(34f)));
            GUILayout.EndArea();
        }

        private void DrawLinkedTargetListSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _linkedTargetSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _linkedTargetSearch, StringComparison.Ordinal))
                _linkedTargetSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_linkedTargetSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear linked-target list search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _linkedTargetSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search selected-controller links by Input/Output, target label, category, role, runtime type, cell, controller, or target key.", DecorationEditorTheme.MiniWrap);
        }

        private static bool LinkedTargetListMatchesSearch(
            AutomationLink link,
            string search)
        {
            if (link == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            AutomationTarget target = link.Target;
            string haystack =
                (link.DirectionLabel ?? string.Empty) + " " +
                (link.ControllerLabel ?? string.Empty) + " " +
                (link.ControllerKey ?? string.Empty) + " " +
                (link.TargetLabel ?? string.Empty) + " " +
                (link.TargetKey ?? string.Empty) + " " +
                (target == null
                    ? "missing stale"
                    : AutomationTargetCatalog.CategoryLabel(target.Category) + " " +
                      AutomationTargetCatalog.RoleLabel(target) + " " +
                      target.RuntimeType + " " +
                      FormatCell(target.LocalPosition) + " " +
                      target.StableKey);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawLinkedTargetListRow(AutomationLink link)
        {
            if (link == null)
                return;

            bool selected = IsSelectedAutomationLink(link);
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
            float actionSpace = Mathf.Clamp(row.width * 0.42f, EsuHudLayout.Scale(88f), EsuHudLayout.Scale(154f));
            float removeWidth = Mathf.Max(EsuHudLayout.Scale(42f), actionSpace * 0.5f - gap * 0.5f);
            float inspectWidth = Mathf.Max(EsuHudLayout.Scale(42f), actionSpace * 0.5f - gap * 0.5f);
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
                SelectAutomationLink(link);
                Event.current.Use();
            }

            RegisterAutomationTargetPreview(row, link.Target, link.DirectionLabel + " link");
            EsuCursorTooltip.Register(labelRect, "Inspect this linked target.");
            if (AutomationGUIButton(
                    inspectRect,
                    new GUIContent("Inspect", DecorationEditorIconCatalog.Get("focus"), "Inspect this linked target."),
                    DecorationEditorTheme.Button))
            {
                SelectAutomationLink(link);
            }

            if (AutomationGUIButton(
                    removeRect,
                    new GUIContent("Remove", DecorationEditorIconCatalog.Get("delete"), "Remove this linked target."),
                    DecorationEditorTheme.Button))
            {
                RemoveAutomationLink(link);
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
                LabelRow("Direction", link.DirectionLabel);
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
                    new GUIContent("Open builder", DecorationEditorIconCatalog.Get("open"), "Open the ESU Blocks builder for this link."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                TryOpenEditor(AutomationEditorPage.Blocks);
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Remove link", DecorationEditorIconCatalog.Get("delete"), "Remove this linked target."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                RemoveAutomationLink(link);
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
                    new GUIContent(
                        "Run checks",
                        DecorationEditorIconCatalog.Get("settings"),
                        "Run observational native capability checks for the selected Controller. This may use same-value native access probes, but it does not create nodes or change craft behavior."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                RunAutomationRuntimeDiagnostics();
            }

            GUILayout.Label(
                "Labels: Missing native capability = FtD did not expose it; ESU UI coverage = native support needs more ESU editor surface; Apply-required setup = link/apply/create proof nodes first.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Evidence labels: Save/reload evidence and Scale/cap limit are observational; they do not write native data.",
                DecorationEditorTheme.MiniWrap);

            bool current =
                _selectedController != null &&
                string.Equals(
                    _lastRuntimeDiagnosticsControllerKey,
                    _selectedController.StableKey,
                    StringComparison.Ordinal);
            if (!current || _lastRuntimeDiagnostics.IsEmpty)
            {
                GUILayout.Label(
                    "Checks use the live FtD Controller instance and same-value native access probes to verify reflective access. They do not create native nodes or change craft behavior.",
                    DecorationEditorTheme.MiniWrap);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(
                _lastRuntimeDiagnostics.Summary,
                _lastRuntimeDiagnostics.HasFailures || _lastRuntimeDiagnostics.HasWarnings
                    ? DecorationEditorTheme.Warning
                    : DecorationEditorTheme.Status);
            DrawRuntimeDiagnosticsFilter();
            string[] matchingLines = _lastRuntimeDiagnostics.Lines
                .Where(line => RuntimeDiagnosticLineMatchesFilter(line, _runtimeDiagnosticsFilter))
                .ToArray();
            GUILayout.Label(
                "Diagnostic lines: showing " +
                Math.Min(matchingLines.Length, 8).ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingLines.Length.ToString(CultureInfo.InvariantCulture) +
                " matching from " +
                _lastRuntimeDiagnostics.Lines.Count.ToString(CultureInfo.InvariantCulture) +
                ".",
                DecorationEditorTheme.MiniWrap);
            if (matchingLines.Length == 0)
            {
                GUILayout.Label("No runtime diagnostic lines match the current filter. Clear the filter to show all checks.", DecorationEditorTheme.Warning);
            }

            int shown = Math.Min(matchingLines.Length, 8);
            for (int index = 0; index < shown; index++)
            {
                string line = matchingLines[index];
                bool issue = line.StartsWith("WARN:", StringComparison.Ordinal) ||
                             line.StartsWith("FAIL:", StringComparison.Ordinal);
                GUILayout.Label(
                    line,
                    issue ? DecorationEditorTheme.Warning : DecorationEditorTheme.MiniWrap);
            }

            if (matchingLines.Length > shown)
            {
                GUILayout.Label(
                    "+" +
                    (matchingLines.Length - shown).ToString(CultureInfo.InvariantCulture) +
                    " more matching line(s) in the ESU runtime log.",
                    DecorationEditorTheme.MiniWrap);
            }

            DrawRuntimeValidationEvidenceRows();
            DrawRuntimeValidationControls();
            GUILayout.EndVertical();
        }

        private void DrawRuntimeDiagnosticsFilter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextFilter = GUILayout.TextField(
                _runtimeDiagnosticsFilter ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextFilter, _runtimeDiagnosticsFilter, StringComparison.Ordinal))
                _runtimeDiagnosticsFilter = nextFilter ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_runtimeDiagnosticsFilter);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear runtime diagnostics filter."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _runtimeDiagnosticsFilter = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Filter runtime checks by severity, label, native capability, proxy, Switch, save/reload, or target text. This is display-only: it filters cached diagnostic lines and does not rerun probes or write native data.",
                DecorationEditorTheme.MiniWrap);
        }

        private static bool RuntimeDiagnosticLineMatchesFilter(
            string line,
            string filter)
        {
            string[] terms = (filter ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack = line ?? string.Empty;
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawRuntimeValidationEvidenceRows()
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Live evidence gates", DecorationEditorTheme.Mini);
            GUILayout.Label(
                "Read-only results from the last Run checks pass. OK means evidence is present; WAIT means link/apply/prepare/run checks is still needed. These rows do not write native data.",
                DecorationEditorTheme.MiniWrap);
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
                    new GUIContent(
                        "Prepare validation graph",
                        DecorationEditorIconCatalog.Get("create"),
                        "Compile the deterministic validation Recipe into native Evaluator/Switch/Generic Setter nodes now. Disabled until a Breadboard Controller has a writable linked Target; generated nodes are tracked by Revert compile."),
                    canPrepareValidationGraph ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                PrepareAutomationValidationGraph();
            }
            GUI.enabled = previous;
            GUILayout.Label(
                "Requires a writable linked Target, then writes native Evaluator/Switch proof nodes plus a target-specific Generic Setter Proxy Node. Revert compile removes those generated validation nodes.",
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
                    new GUIContent(
                        "Capture baseline",
                        DecorationEditorIconCatalog.Get("save"),
                        "Capture the current live evidence fingerprints in memory only. This diagnostic step does not write native Breadboard data."),
                    completeEvidence ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CaptureRuntimeValidationBaseline();
            }

            GUI.enabled = previous && s_validationBaseline != null && completeEvidence;
            if (AutomationGUILayoutButton(
                    new GUIContent(
                        "Compare baseline",
                        DecorationEditorIconCatalog.Get("focus"),
                        "Compare the current live evidence fingerprints against the captured baseline. This diagnostic step reads only and does not write native Breadboard data."),
                    s_validationBaseline == null || !completeEvidence
                        ? DecorationEditorTheme.DisabledButton
                        : DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CompareRuntimeValidationBaseline();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Capture and Compare baseline are diagnostic-only: they read live evidence fingerprints and update ESU status/log text without native graph writes.",
                DecorationEditorTheme.MiniWrap);

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
                GUILayout.BeginHorizontal();
                if (AutomationGUILayoutButton(
                        new GUIContent(
                            "Important (" +
                            CountBrowserTargets(AutomationTargetBrowserMode.Important).ToString("N0", CultureInfo.InvariantCulture) +
                            ")",
                            DecorationEditorIconCatalog.Get("risk"),
                            "Show important Automation targets and systems."),
                        DecorationEditorTheme.ToolButton(_targetBrowserMode == AutomationTargetBrowserMode.Important),
                        GUILayout.Height(EsuHudLayout.Scale(28f))))
                {
                    _targetBrowserMode = AutomationTargetBrowserMode.Important;
                    _status = "Target browser: Important.";
                }

                if (AutomationGUILayoutButton(
                        new GUIContent(
                            "Generic (" +
                            CountBrowserTargets(AutomationTargetBrowserMode.Generic).ToString("N0", CultureInfo.InvariantCulture) +
                            ")",
                            DecorationEditorIconCatalog.Get("cube"),
                            "Show generic/structural targets that are hidden from default world overlays."),
                        DecorationEditorTheme.ToolButton(_targetBrowserMode == AutomationTargetBrowserMode.Generic),
                        GUILayout.Height(EsuHudLayout.Scale(28f))))
                {
                    _targetBrowserMode = AutomationTargetBrowserMode.Generic;
                    _status = "Target browser: Generic.";
                }
                GUILayout.EndHorizontal();

                if (AutomationGUILayoutButton(
                        new GUIContent(
                            _showAdvancedFilters ? "Hide advanced filters" : "Show advanced filters",
                            "Show detailed target category filters."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    _showAdvancedFilters = !_showAdvancedFilters;
                    if (!_showAdvancedFilters)
                        _filter = AutomationTargetCategory.All;
                }

                if (_showAdvancedFilters)
                {
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
                }

                DecorationEditorTheme.Separator();
                IReadOnlyList<AutomationTarget> visibleTargets = FilteredWorldTargets();
                DrawCompactIconHeader(
                    "World targets (" +
                    Math.Min(visibleTargets.Count, TargetBrowserVisibleLimit).ToString("N0", CultureInfo.InvariantCulture) +
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

                if (visibleTargets.Count > TargetBrowserVisibleLimit)
                {
                    GUILayout.Label(
                        "Showing first " +
                        TargetBrowserVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                        " of " +
                        visibleTargets.Count.ToString(CultureInfo.InvariantCulture) +
                        " matching world target(s). Use search/filter or category filters to narrow results.",
                        DecorationEditorTheme.Warning);
                }

                foreach (AutomationTarget target in visibleTargets.Take(TargetBrowserVisibleLimit))
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
            DrawCompactIconHeader(text, iconKey, DecorationEditorTheme.SubHeader);
            if (AutomationGUILayoutButton(
                    new GUIContent("Hide", hideTooltip),
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
                    new GUIContent("clear", "Clear Automation target search."),
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
            GUILayout.Label(
                "Examples: spinblock, APS, Breadboard Write, main, subconstruct, c0, 12,4,-2.",
                DecorationEditorTheme.MiniWrap);
            if (!string.IsNullOrWhiteSpace(_targetSearchText))
            {
                GUILayout.Label(
                    "Search active: " + _targetSearchText.Trim() + ". Clear it to restore the full filtered target list.",
                    DecorationEditorTheme.MiniWrap);
            }
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

            DrawControllerIndexSearch();
            AutomationTarget[] matchingControllers = controllers
                .Where(target => ControllerIndexMatchesSearch(target, _controllerIndexSearch))
                .ToArray();
            GUILayout.Label(
                "Controller list: " +
                Math.Min(matchingControllers.Length, ControllerIndexVisibleLimit).ToString("N0", CultureInfo.InvariantCulture) +
                "/" +
                matchingControllers.Length.ToString("N0", CultureInfo.InvariantCulture) +
                " matching from " +
                controllers.Length.ToString("N0", CultureInfo.InvariantCulture) +
                ".",
                DecorationEditorTheme.MiniWrap);
            if (matchingControllers.Length == 0)
            {
                GUILayout.Label("No Automation controllers match the current controller search. Clear it to show Breadboard/ACB controllers on craft.", DecorationEditorTheme.Warning);
                return;
            }

            int shown = 0;
            int groupIndex = 0;
            foreach (IGrouping<AllConstruct, AutomationTarget> group in matchingControllers
                         .GroupBy(target => target.Construct)
                         .OrderBy(group => ConstructGroupSortKey(group.Key), StringComparer.OrdinalIgnoreCase))
            {
                if (shown >= ControllerIndexVisibleLimit)
                    break;

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
                int remainingRows = Math.Max(0, ControllerIndexVisibleLimit - shown);
                AutomationTarget[] visibleGroupTargets = groupTargets
                    .Take(Math.Min(ControllerIndexGroupVisibleLimit, remainingRows))
                    .ToArray();
                foreach (AutomationTarget target in visibleGroupTargets)
                {
                    DrawControllerIndexRow(target);
                    shown++;
                }

                if (groupTargets.Length > visibleGroupTargets.Length)
                {
                    GUILayout.Label(
                        "Showing first " +
                        visibleGroupTargets.Length.ToString(CultureInfo.InvariantCulture) +
                        " of " +
                        groupTargets.Length.ToString(CultureInfo.InvariantCulture) +
                        " matching controller(s) in this construct. Use controller search to narrow results.",
                        DecorationEditorTheme.MiniWrap);
                }
            }

            if (matchingControllers.Length > shown)
            {
                GUILayout.Label(
                    "+" + (matchingControllers.Length - shown).ToString(CultureInfo.InvariantCulture) + " more matching controller(s). Use controller search to narrow results.",
                    DecorationEditorTheme.MiniWrap);
            }

            if (_selectedController != null &&
                !matchingControllers.Any(target => string.Equals(target.StableKey, _selectedController.StableKey, StringComparison.Ordinal)))
            {
                GUILayout.Label("Selected controller is hidden by the current controller search.", DecorationEditorTheme.Warning);
            }
        }

        private void DrawControllerIndexSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _controllerIndexSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _controllerIndexSearch, StringComparison.Ordinal))
                _controllerIndexSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_controllerIndexSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear Automation controller search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _controllerIndexSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search placed Automation controllers by label, controller type, construct, cell, or target key.", DecorationEditorTheme.MiniWrap);
        }

        private bool ControllerIndexMatchesSearch(
            AutomationTarget target,
            string search)
        {
            if (target == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string constructLabel = ReferenceEquals(target.Construct, SafeCurrentConstruct())
                ? "main construct"
                : "subconstruct";
            string haystack =
                (target.Label ?? string.Empty) + " " +
                (target.Controller?.Label ?? string.Empty) + " " +
                (target.Controller?.ShortLabel ?? string.Empty) + " " +
                (target.Controller?.ClassName ?? string.Empty) + " " +
                (target.RuntimeType ?? string.Empty) + " " +
                constructLabel + " " +
                FormatCell(target.LocalPosition) + " " +
                (target.StableKey ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private AllConstruct SafeCurrentConstruct()
        {
            try
            {
                return _build.GetC();
            }
            catch
            {
                return null;
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
                if (_pendingLinkTarget != null &&
                    !string.Equals(_pendingLinkTarget.StableKey, target.StableKey, StringComparison.Ordinal))
                {
                    SelectAutomationController(target, armOutputLinkMode: true);
                    ToggleLink(target, _pendingLinkTarget, AutomationLinkDirection.Input);
                    _pendingLinkTarget = null;
                    ArmOutputLinkModeForSelectedController();
                }
                else
                {
                    SelectAutomationController(target, armOutputLinkMode: true);
                }
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
            bool inputLinked = IsLinked(_selectedController, target, AutomationLinkDirection.Input);
            bool outputLinked = IsLinked(_selectedController, target, AutomationLinkDirection.Output);
            bool linked = inputLinked || outputLinked;
            string detail = AutomationTargetCatalog.CategoryLabel(target.Category);
            if (!string.IsNullOrWhiteSpace(roleSummary))
                detail = roleSummary + " | " + detail;
            if (inputLinked && outputLinked)
                detail += " | input + output";
            else if (inputLinked)
                detail += " | input";
            else if (outputLinked)
                detail += " | output";
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

        private string TargetRowTooltip(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;
            return target.IsController
                ? "Click to select this controller, or link it when the selected controller can drive controller targets."
                : _selectedController == null || !_outputLinkModeArmed
                    ? "Click to choose this as an input source, then click a Breadboard/controller."
                    : "Click to link or unlink this target as an output from the selected controller.";
        }

        private void HandleTargetRowClick(AutomationTarget target)
        {
            if (target == null)
                return;

            if (target.IsController &&
                _pendingLinkTarget != null &&
                !string.Equals(_pendingLinkTarget.StableKey, target.StableKey, StringComparison.Ordinal))
            {
                SelectAutomationController(target, armOutputLinkMode: true);
                ToggleLink(target, _pendingLinkTarget, AutomationLinkDirection.Input);
                _pendingLinkTarget = null;
                ArmOutputLinkModeForSelectedController();
                return;
            }

            if (target.IsController)
            {
                if (_outputLinkModeArmed &&
                    TryToggleControllerTargetLink(target))
                {
                    return;
                }

                SelectAutomationController(target, armOutputLinkMode: true);
                return;
            }

            if (_selectedController == null ||
                !_outputLinkModeArmed)
            {
                _pendingLinkTarget = target;
                _outputLinkModeArmed = false;
                _status = "Input source selected: " + target.Label + ". Click a Breadboard/controller to read from it.";
                return;
            }

            _pendingLinkTarget = null;
            ToggleLink(_selectedController, target, AutomationLinkDirection.Output);
        }

        private void DrawEditor(int id)
        {
            GUI.Box(new Rect(0f, 0f, _editorRect.width, _editorRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(10f);
            Rect inner = new Rect(
                inset,
                inset,
                Mathf.Max(1f, _editorRect.width - inset * 2f),
                Mathf.Max(1f, _editorRect.height - inset * 2f));
            float gap = EsuHudLayout.Scale(4f);
            float headerHeight = EsuHudLayout.Scale(30f);
            float tabHeight = EsuHudLayout.Scale(28f);
            float breadcrumbHeight = EsuHudLayout.Scale(22f);
            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect tabsRect = new Rect(inner.x, headerRect.yMax + gap, inner.width, tabHeight);
            Rect breadcrumbRect = new Rect(inner.x, tabsRect.yMax + gap, inner.width, breadcrumbHeight);
            Rect separatorRect = new Rect(inner.x, breadcrumbRect.yMax + gap, inner.width, separatorHeight);
            Rect contentRect = new Rect(
                inner.x,
                separatorRect.yMax + gap,
                inner.width,
                Mathf.Max(1f, inner.yMax - separatorRect.yMax - gap));

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(EditorTitle(), "open", DecorationEditorTheme.Header);
            if (IsSystemBlockWorkspaceOpen() &&
                AutomationGUILayoutButton(
                    new GUIContent("Up", DecorationEditorIconCatalog.Get("back"), "Return to the host controller workspace."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(56f))))
            {
                TryLeaveSystemBlockWorkspace();
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit", DecorationEditorIconCatalog.Get("focus"), "Fit the graph/code editor to the viewport."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
            {
                FitEditorToViewport();
                _status = "Automation editor fitted to the available viewport.";
            }
            bool saveEnabled = _selectedController != null;
            bool previousSaveEnabled = GUI.enabled;
            GUI.enabled = previousSaveEnabled && saveEnabled;
            if (AutomationGUILayoutButton(
                    new GUIContent("Save", DecorationEditorIconCatalog.Get("save"), "Save linked targets and ESU Blocks for the selected Automation controller."),
                    saveEnabled
                        ? DecorationEditorTheme.ToolButton(HasUnsavedAutomationWorkspaceChanges())
                        : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(68f))))
                SaveSelectedAutomationWorkspace();
            GUI.enabled = previousSaveEnabled;
            if (AutomationGUILayoutButton(
                    new GUIContent("Close", "Close the fullscreen Automation editor."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(68f))))
                RequestCloseEditor();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUILayout.BeginArea(tabsRect);
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Blocks", DecorationEditorIconCatalog.Get("build"), "Build beginner-friendly ESU Automation Blocks."),
                    DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.Blocks)))
            {
                ClearSystemBlockWorkspace();
                _editorPage = AutomationEditorPage.Blocks;
            }
            bool advanced = _editorPage != AutomationEditorPage.Blocks;
            if (AutomationGUILayoutButton(
                    new GUIContent("Advanced", DecorationEditorIconCatalog.Get("settings"), "Open native Breadboard graph/code/System Block tools."),
                    DecorationEditorTheme.ToolButton(advanced)))
            {
                if (_editorPage == AutomationEditorPage.Blocks)
                    _editorPage = AutomationEditorPage.Graph;
            }
            if (advanced)
            {
                GUILayout.Space(EsuHudLayout.Scale(10f));
                if (AutomationGUILayoutButton(
                        new GUIContent("Native", DecorationEditorIconCatalog.Get("outliner"), "Edit native Automation graph nodes."),
                        DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.Graph)))
                    _editorPage = AutomationEditorPage.Graph;
                if (AutomationGUILayoutButton(
                        new GUIContent("Code", DecorationEditorIconCatalog.Get("settings"), "Generate or edit native Automation code recipes."),
                        DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.Code)))
                    _editorPage = AutomationEditorPage.Code;
                if (AutomationGUILayoutButton(
                        new GUIContent("Systems", DecorationEditorIconCatalog.Get("duplicate"), "Define reusable ESU System Block metadata and native ports."),
                        DecorationEditorTheme.ToolButton(_editorPage == AutomationEditorPage.System)))
                    _editorPage = AutomationEditorPage.System;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.Label(breadcrumbRect, "Breadcrumb: " + SystemBlockBreadcrumb(), DecorationEditorTheme.MiniWrap);
            DrawCyanLine(separatorRect);

            if (_editorPage == AutomationEditorPage.Blocks)
            {
                DrawBlocksEditor(contentRect);
            }
            else
            {
                GUILayout.BeginArea(contentRect);
                _editorScroll = GUILayout.BeginScrollView(
                    _editorScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(contentRect.width),
                    GUILayout.Height(contentRect.height));
                if (_editorPage == AutomationEditorPage.Graph)
                    DrawGraphEditor();
                else if (_editorPage == AutomationEditorPage.Code)
                    DrawCodeEditor();
                else
                    DrawSystemBlockEditor();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            GUI.DragWindow(new Rect(0f, 0f, _editorRect.width, EsuHudLayout.Scale(34f)));
        }

        private string EditorPageLabel()
        {
            string prefix = IsSystemBlockWorkspaceOpen() ? "System " : string.Empty;
            switch (_editorPage)
            {
                case AutomationEditorPage.Blocks:
                    return prefix + "ESU Blocks";
                case AutomationEditorPage.Code:
                    return prefix + "Code";
                case AutomationEditorPage.System:
                    return prefix + "Ports";
                default:
                    return prefix + "Graph";
            }
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
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template != null)
                return "System Block - " + template.Name;

            return _selectedController == null
                ? "Automation Blocks"
                : _editorPage == AutomationEditorPage.Blocks
                    ? "Automation Blocks - " + _selectedController.Label
                    : "Native Breadboard - " + _selectedController.Label;
        }

        private void DrawBlocksEditor(Rect pageRect)
        {
            if (pageRect.width <= 1f || pageRect.height <= 1f)
                return;

            if (_selectedController == null)
            {
                DrawBlocksEditorUnavailable(
                    pageRect,
                    "Select or place a Breadboard, link a target it affects, then open the editor.",
                    showAdvancedButton: false);
                DrawAutomationBlockDragGhost();
                return;
            }

            if (!IsBreadboardController(_selectedController.Controller))
            {
                DrawBlocksEditorUnavailable(
                    pageRect,
                    "ESU Blocks currently lower into native Breadboard controllers. Use Advanced for ACB inspection.",
                    showAdvancedButton: true);
                DrawAutomationBlockDragGhost();
                return;
            }

            EnsureBlockWorkspace();
            if (pageRect.width < EsuHudLayout.Scale(420f) ||
                pageRect.height < EsuHudLayout.Scale(220f))
            {
                DrawBlocksEditorTooSmall(pageRect);
            }
            else
            {
                DrawBlocksEditorViewport(pageRect);
            }
            DrawAutomationBlockDragGhost();
        }

        private void DrawBlocksEditorUnavailable(
            Rect pageRect,
            string message,
            bool showAdvancedButton)
        {
            _lastBlockCanvasRect = Rect.zero;
            float pad = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(
                pageRect.x + pad,
                pageRect.y + pad,
                Mathf.Max(1f, pageRect.width - pad * 2f),
                Mathf.Max(1f, pageRect.height - pad * 2f)));
            GUILayout.Label(message ?? "Select a Breadboard controller to edit ESU Blocks.", DecorationEditorTheme.Warning);
            GUILayout.Label("Next: " + NextSafeActionLine(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label(WorkspaceCompatibilityLine(), DecorationEditorTheme.MiniWrap);

            if (showAdvancedButton &&
                AutomationGUILayoutButton(
                    new GUIContent("Open Advanced", DecorationEditorIconCatalog.Get("settings"), "Open native Automation tools for this controller."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(132f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                _editorPage = AutomationEditorPage.Graph;
            }
            GUILayout.EndArea();
        }

        private void DrawBlocksEditorTooSmall(Rect viewport)
        {
            _lastBlockCanvasRect = Rect.zero;
            if (viewport.width <= 1f || viewport.height <= 1f)
                return;

            GUI.Box(viewport, GUIContent.none, DecorationEditorTheme.Panel);
            float pad = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(
                viewport.x + pad,
                viewport.y + pad,
                Mathf.Max(1f, viewport.width - pad * 2f),
                Mathf.Max(1f, viewport.height - pad * 2f)));
            DrawCompactIconHeader("Block canvas", "outliner", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Automation Blocks needs more editor space. Resize or Fit the editor to show the grid, linked signals, palette, and Check/Apply panel.",
                DecorationEditorTheme.Warning);
            GUILayout.EndArea();
        }

        private void DrawBlocksEditorViewport(Rect viewport)
        {
            if (viewport.width <= 1f || viewport.height <= 1f)
                return;

            if (_blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                DrawNativeExactBlocksEditorViewport(viewport);
                return;
            }

            float divider = EsuHudLayout.Scale(8f);
            float minLeft = Mathf.Min(EsuHudLayout.Scale(360f), Mathf.Max(1f, viewport.width * 0.48f));
            float minRight = Mathf.Min(EsuHudLayout.Scale(260f), Mathf.Max(1f, viewport.width * 0.38f));
            float maxRight = Mathf.Max(minRight, viewport.width - minLeft - divider);
            float rightWidth = Mathf.Clamp(viewport.width * _blocksRightColumnRatio, minRight, maxRight);
            Rect leftRect = new Rect(viewport.x, viewport.y, Mathf.Max(1f, viewport.width - rightWidth - divider), viewport.height);
            Rect splitRect = new Rect(leftRect.xMax, viewport.y, divider, viewport.height);
            Rect rightRect = new Rect(splitRect.xMax, viewport.y, rightWidth, viewport.height);

            DrawBlocksLeftColumn(leftRect, divider);
            DrawBlocksSplitterGrip(splitRect, AutomationBlocksSplitter.MainRight, vertical: true, "Drag to resize the ESU Blocks canvas and side panels.");
            DrawBlocksRightColumn(rightRect, divider);
            HandleBlocksMainRightSplitter(viewport, splitRect);
        }

        private void DrawNativeExactBlocksEditorViewport(Rect viewport)
        {
            EnsureNativeExactDefaultDrawer();
            float toolbarHeight = EsuHudLayout.Scale(30f);
            float gap = EsuHudLayout.Scale(6f);
            Rect toolbarRect = new Rect(viewport.x, viewport.y, viewport.width, toolbarHeight);
            Rect canvasRect = new Rect(
                viewport.x,
                toolbarRect.yMax + gap,
                viewport.width,
                Mathf.Max(1f, viewport.yMax - toolbarRect.yMax - gap));

            DrawNativeExactToolbar(toolbarRect, canvasRect);
            DrawNativeExactBlockCanvas(canvasRect);
            DrawNativeExactDrawerOverlay(canvasRect);
        }

        private void DrawNativeExactToolbar(Rect rect, Rect canvasRect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader("Native graph", "outliner", DecorationEditorTheme.SubHeader);
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit graph", DecorationEditorIconCatalog.Get("focus"), "Fit the imported native graph to the visible canvas."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(86f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                FitNativeExactGraph(canvasRect);
            if (AutomationGUILayoutButton(
                    new GUIContent("Fit selected", DecorationEditorIconCatalog.Get("visibility"), "Fit the selected native component to the visible canvas."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(104f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                FitNativeExactSelection(canvasRect);
            if (AutomationGUILayoutButton(
                    new GUIContent("Reset view", DecorationEditorIconCatalog.Get("reload"), "Reset NativeExact pan and zoom."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(92f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                ResetNativeExactView();
            GUILayout.Space(EsuHudLayout.Scale(10f));
            DrawNativeExactDrawerButton(NativeExactDrawer.Inspector, "Inspector", "focus");
            DrawNativeExactDrawerButton(NativeExactDrawer.AddNative, "Add Native", "duplicate");
            DrawNativeExactDrawerButton(NativeExactDrawer.Refresh, "Refresh", "reload");
            DrawNativeExactDrawerButton(NativeExactDrawer.Status, "Status", "info");
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Zoom: " + (_blockWorkspace?.CanvasZoom ?? 1f).ToString("0.00", CultureInfo.InvariantCulture) + "x",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(86f)));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawNativeExactDrawerButton(
            NativeExactDrawer drawer,
            string label,
            string iconKey)
        {
            bool active = _nativeExactDrawer == drawer;
            if (AutomationGUILayoutButton(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(iconKey), "Open " + label + " drawer for NativeExact graph editing."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(drawer == NativeExactDrawer.AddNative ? 96f : 84f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                if (active)
                {
                    _nativeExactDrawer = NativeExactDrawer.None;
                    if (drawer == NativeExactDrawer.Inspector)
                        _nativeExactInspectorClosedForSelection = true;
                }
                else
                {
                    _nativeExactDrawer = drawer;
                    if (drawer == NativeExactDrawer.Inspector)
                        _nativeExactInspectorClosedForSelection = false;
                }
            }
        }

        private void DrawNativeExactBlockCanvas(Rect viewport)
        {
            if (viewport.width <= 1f || viewport.height <= 1f)
                return;

            GUI.Box(viewport, GUIContent.none, DecorationEditorTheme.Panel);
            _lastBlockCanvasRect = GuiToScreenRect(viewport);
            ClampBlockCanvasPan(viewport);
            List<Rect> nodeRects = DrawTinkercadProgram(viewport);
            Rect drawerBlocker = NativeExactDrawerRect(viewport);
            Event current = Event.current;
            bool drawerHasMouse = _nativeExactDrawer != NativeExactDrawer.None &&
                                  current != null &&
                                  drawerBlocker.Contains(current.mousePosition);
            if (!drawerHasMouse)
            {
                HandleBlockCanvasNavigation(viewport, nodeRects);
                HandleBlockCanvasDrop(viewport, nodeRects);
            }
        }

        private void DrawNativeExactDrawerOverlay(Rect canvasRect)
        {
            if (_nativeExactDrawer == NativeExactDrawer.None ||
                canvasRect.width <= 1f ||
                canvasRect.height <= 1f)
            {
                return;
            }

            Rect drawer = NativeExactDrawerRect(canvasRect);
            GUI.Box(drawer, GUIContent.none, DecorationEditorTheme.Panel);
            DrawFilledRect(drawer, new Color(0f, 0.08f, 0.1f, 0.72f));
            Rect header = new Rect(
                drawer.x + EsuHudLayout.Scale(8f),
                drawer.y + EsuHudLayout.Scale(8f),
                drawer.width - EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(28f));
            if (DrawNativeExactDrawerHeader(header, NativeExactDrawerTitle(_nativeExactDrawer), NativeExactDrawerIcon(_nativeExactDrawer)))
            {
                if (_nativeExactDrawer == NativeExactDrawer.Inspector)
                    _nativeExactInspectorClosedForSelection = true;
                _nativeExactDrawer = NativeExactDrawer.None;
                return;
            }

            Rect body = new Rect(
                drawer.x + EsuHudLayout.Scale(10f),
                header.yMax + EsuHudLayout.Scale(8f),
                drawer.width - EsuHudLayout.Scale(20f),
                Mathf.Max(1f, drawer.yMax - header.yMax - EsuHudLayout.Scale(18f)));
            GUILayout.BeginArea(body);
            switch (_nativeExactDrawer)
            {
                case NativeExactDrawer.AddNative:
                    DrawNativeExactAddNativeDrawer(body.height);
                    break;
                case NativeExactDrawer.Refresh:
                    DrawNativeExactRefreshDrawer();
                    break;
                case NativeExactDrawer.Status:
                    DrawNativeExactStatusDrawer();
                    break;
                default:
                    _blocksInspectorScroll = GUILayout.BeginScrollView(
                        _blocksInspectorScroll,
                        alwaysShowHorizontal: false,
                        alwaysShowVertical: true,
                        GUILayout.Width(body.width),
                        GUILayout.Height(body.height));
                    DrawNativeExactInspectorDrawer();
                    GUILayout.EndScrollView();
                    break;
            }
            GUILayout.EndArea();
        }

        private Rect NativeExactDrawerRect(Rect canvasRect)
        {
            float width = Mathf.Min(EsuHudLayout.Scale(390f), Mathf.Max(EsuHudLayout.Scale(280f), canvasRect.width * 0.34f));
            return new Rect(
                canvasRect.xMax - width - EsuHudLayout.Scale(8f),
                canvasRect.y + EsuHudLayout.Scale(8f),
                width,
                Mathf.Max(1f, canvasRect.height - EsuHudLayout.Scale(16f)));
        }

        private bool DrawNativeExactDrawerHeader(
            Rect header,
            string title,
            string iconKey)
        {
            GUI.Label(header, GUIContent.none, DecorationEditorTheme.SubHeader);
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            Rect iconRect = new Rect(
                header.x + EsuHudLayout.Scale(7f),
                header.y + EsuHudLayout.Scale(6f),
                EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(16f));
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            Rect closeRect = new Rect(
                header.xMax - EsuHudLayout.Scale(64f),
                header.y + EsuHudLayout.Scale(3f),
                EsuHudLayout.Scale(60f),
                header.height - EsuHudLayout.Scale(6f));
            DrawFittedSingleLineLabel(
                new Rect(
                    iconRect.xMax + EsuHudLayout.Scale(8f),
                    header.y,
                    Mathf.Max(1f, closeRect.x - iconRect.xMax - EsuHudLayout.Scale(12f)),
                    header.height),
                title,
                IconHeaderTextStyle(DecorationEditorTheme.SubHeader),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(9));

            return AutomationGUIButton(
                closeRect,
                new GUIContent("Close", "Close this NativeExact drawer."),
                DecorationEditorTheme.Button);
        }

        private static string NativeExactDrawerTitle(NativeExactDrawer drawer)
        {
            switch (drawer)
            {
                case NativeExactDrawer.AddNative:
                    return "Add Native";
                case NativeExactDrawer.Refresh:
                    return "Refresh";
                case NativeExactDrawer.Status:
                    return "Status";
                default:
                    return "Inspector";
            }
        }

        private static string NativeExactDrawerIcon(NativeExactDrawer drawer)
        {
            switch (drawer)
            {
                case NativeExactDrawer.AddNative:
                    return "duplicate";
                case NativeExactDrawer.Refresh:
                    return "reload";
                case NativeExactDrawer.Status:
                    return "info";
                default:
                    return "focus";
            }
        }

        private void EnsureNativeExactDefaultDrawer()
        {
            string selectedId = _blockWorkspace?.SelectedNodeId ?? string.Empty;
            if (!string.Equals(selectedId, _nativeExactLastSelectedNodeId, StringComparison.Ordinal))
            {
                _nativeExactLastSelectedNodeId = selectedId;
                _nativeExactInspectorClosedForSelection = false;
                if (!string.IsNullOrWhiteSpace(selectedId))
                    _nativeExactDrawer = NativeExactDrawer.Inspector;
            }

            if (string.IsNullOrWhiteSpace(selectedId) && _nativeExactDrawer == NativeExactDrawer.Inspector)
                _nativeExactDrawer = NativeExactDrawer.None;
            if (!string.IsNullOrWhiteSpace(selectedId) &&
                _nativeExactDrawer == NativeExactDrawer.None &&
                !_nativeExactInspectorClosedForSelection)
            {
                _nativeExactDrawer = NativeExactDrawer.Inspector;
            }
        }

        private void DrawNativeExactInspectorDrawer()
        {
            AutomationBlockNode node = _blockWorkspace?.SelectedNode();
            if (node == null)
            {
                GUILayout.Label("Select a native Breadboard component on the graph.", DecorationEditorTheme.MiniWrap);
                return;
            }

            AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionForNode(node);
            LabelRow("Component", node.NativeComponentId.ToString(CultureInfo.InvariantCulture));
            LabelRow("Label", string.IsNullOrWhiteSpace(node.NativeComponentLabel) ? node.Label : node.NativeComponentLabel);
            if (!string.IsNullOrWhiteSpace(node.NativeBlockTypeName))
                LabelRow("Block type", node.NativeBlockTypeName);
            if (!string.IsNullOrWhiteSpace(node.NativeBlockFilter))
                LabelRow("Block filter", node.NativeBlockFilter);
            LabelRow("Ports", AutomationBlockPortSummary(definition));
            if (!string.IsNullOrWhiteSpace(node.NativeSettingsSummary))
                LabelRow("Settings", node.NativeSettingsSummary);
            LabelRow("Native type", string.IsNullOrWhiteSpace(node.NativeComponentTypeName)
                ? "unknown"
                : node.NativeComponentTypeName);
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Inputs", DecorationEditorTheme.Mini);
            DrawNativeExactPortList(node, AutomationBlockPortDirection.Input);
            GUILayout.Label("Outputs", DecorationEditorTheme.Mini);
            DrawNativeExactPortList(node, AutomationBlockPortDirection.Output);
            DecorationEditorTheme.Separator();
            GUILayout.Label("Move selected native component", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Left", DecorationEditorIconCatalog.Get("back"), "Move selected native component left."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                NudgeSelectedNativeExact(-10f, 0f);
            if (AutomationGUILayoutButton(
                    new GUIContent("Right", DecorationEditorIconCatalog.Get("open"), "Move selected native component right."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                NudgeSelectedNativeExact(10f, 0f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Up", DecorationEditorIconCatalog.Get("up"), "Move selected native component up."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                NudgeSelectedNativeExact(0f, -10f);
            if (AutomationGUILayoutButton(
                    new GUIContent("Down", DecorationEditorIconCatalog.Get("down"), "Move selected native component down."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                NudgeSelectedNativeExact(0f, 10f);
            GUILayout.EndHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Delete native component", DecorationEditorIconCatalog.Get("delete"), "Delete this exact native Breadboard component."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
                RemoveSelectedEsuBlock();
        }

        private void DrawNativeExactPortList(
            AutomationBlockNode node,
            AutomationBlockPortDirection direction)
        {
            AutomationBlockPort[] ports = _blockWorkspace.Ports
                .Where(port =>
                    string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                    port.Direction == direction)
                .ToArray();
            if (ports.Length == 0)
            {
                GUILayout.Label(direction == AutomationBlockPortDirection.Input ? "No inputs." : "No outputs.", DecorationEditorTheme.MiniWrap);
                return;
            }

            foreach (AutomationBlockPort port in ports)
                GUILayout.Label(port.Name, DecorationEditorTheme.MiniWrap);
        }

        private void DrawNativeExactAddNativeDrawer(float availableHeight)
        {
            if (!TryCreateSelectedBreadboardInspector(
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason ?? "Select a Breadboard controller before adding native components.", DecorationEditorTheme.Warning);
                return;
            }

            DrawNativeBreadboardPaletteSearch();
            AutomationBreadboardAvailableComponent[] matchingComponents = inspector.AvailableComponents
                .Where(component => component != null &&
                                    !string.IsNullOrWhiteSpace(component.TypeName) &&
                                    NativeComponentMatchesSearch(component, _nativeComponentPaletteSearch))
                .OrderBy(component => component.Label, StringComparer.OrdinalIgnoreCase)
                .Take(NativePaletteVisibleLimit)
                .ToArray();
            GUILayout.Label(
                "Advertised native components: " + matchingComponents.Length.ToString(CultureInfo.InvariantCulture) + " shown.",
                DecorationEditorTheme.MiniWrap);
            float scrollHeight = Mathf.Max(EsuHudLayout.Scale(90f), availableHeight - EsuHudLayout.Scale(58f));
            _blockPaletteScroll = GUILayout.BeginScrollView(
                _blockPaletteScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(scrollHeight));
            for (int index = 0; index < matchingComponents.Length; index += 2)
            {
                GUILayout.BeginHorizontal();
                DrawNativeExactAddComponentButton(inspector, matchingComponents[index]);
                if (index + 1 < matchingComponents.Length)
                    DrawNativeExactAddComponentButton(inspector, matchingComponents[index + 1]);
                else
                    GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawNativeExactAddComponentButton(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardAvailableComponent component)
        {
            if (component == null)
                return;

            if (AutomationGUILayoutButton(
                    new GUIContent(
                        component.Label,
                        string.IsNullOrWhiteSpace(component.Description)
                            ? "Add native Breadboard component: " + component.TypeName + "."
                            : component.Description),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                if (inspector.TryAddComponent(component.TypeName, out string message))
                {
                    RefreshNativeExactWorkspaceAfterNativeCommand(message);
                    _nativeExactDrawer = NativeExactDrawer.Inspector;
                }
                else
                {
                    _status = message ?? "Could not add native Breadboard component.";
                }
            }
        }

        private void DrawNativeExactRefreshDrawer()
        {
            GUILayout.Label("Reimport the selected controller's live vanilla Breadboard graph.", DecorationEditorTheme.MiniWrap);
            if (HasUnsavedAutomationWorkspaceChanges())
                GUILayout.Label("Unsaved ESU layout changes will be discarded after confirmation.", DecorationEditorTheme.Warning);
            if (!string.IsNullOrWhiteSpace(_blockWorkspace?.NativeImportStatus))
                GUILayout.Label(_blockWorkspace.NativeImportStatus, DecorationEditorTheme.Body);
            if (AutomationGUILayoutButton(
                    new GUIContent("Refresh from native", DecorationEditorIconCatalog.Get("reload"), "Rebuild the NativeExact graph from the live Breadboard."),
                    _confirmNativeRefreshFromNative ? DecorationEditorTheme.Warning : DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                RefreshBlocksFromNative();
            if (AutomationGUILayoutButton(
                    new GUIContent("Save ESU layout", DecorationEditorIconCatalog.Get("save"), "Save current ESU NativeExact layout metadata."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                SaveSelectedAutomationWorkspace();
        }

        private void DrawNativeExactStatusDrawer()
        {
            LabelRow("Mode", "Native exact import");
            if (!string.IsNullOrWhiteSpace(_blockWorkspace?.NativeImportStatus))
                GUILayout.Label(_blockWorkspace.NativeImportStatus, DecorationEditorTheme.Body);
            if (!string.IsNullOrWhiteSpace(_status))
                GUILayout.Label(_status, DecorationEditorTheme.MiniWrap);
            if (!string.IsNullOrWhiteSpace(_blockLoweringStatus))
                GUILayout.Label(_blockLoweringStatus, DecorationEditorTheme.MiniWrap);
            GUILayout.Label("Native graph behavior lives in the vanilla Breadboard. ESU stores view/layout metadata only.", DecorationEditorTheme.MiniWrap);
        }

        private void NudgeSelectedNativeExact(float nativeDeltaX, float nativeDeltaY)
        {
            AutomationBlockNode node = _blockWorkspace?.SelectedNode();
            if (node == null)
                return;

            float displayScale = _blockWorkspace?.NativeDisplayScale ?? 1f;
            if (float.IsNaN(displayScale) || float.IsInfinity(displayScale) || displayScale <= 0.05f)
                displayScale = 1f;
            var drop = new AutomationBlockCanvasPosition(
                node.CanvasPosition.X + nativeDeltaX * displayScale,
                node.CanvasPosition.Y + nativeDeltaY * displayScale);
            if (TryMoveNativeExactWorkspaceBlock(
                    node,
                    drop,
                    out bool handled,
                    out string message))
            {
                RefreshNativeExactWorkspaceAfterNativeCommand(message);
            }
            else if (handled)
            {
                _status = message ?? "Could not move native Breadboard component.";
            }
        }

        private void FitNativeExactGraph(Rect canvasRect)
        {
            if (!TryGetNativeExactBounds(
                    _blockWorkspace?.Nodes.Where(node => node != null).ToArray(),
                    out Rect bounds))
            {
                _status = "Native graph has no nodes to fit.";
                return;
            }

            FitNativeExactBounds(canvasRect, bounds);
            _status = "Fitted native graph to view.";
        }

        private void FitNativeExactSelection(Rect canvasRect)
        {
            AutomationBlockNode node = _blockWorkspace?.SelectedNode();
            if (node == null ||
                !TryGetNativeExactBounds(new[] { node }, out Rect bounds))
            {
                _status = "Select a native component before fitting selection.";
                return;
            }

            FitNativeExactBounds(canvasRect, bounds);
            _status = "Fitted selected native component to view.";
        }

        private void ResetNativeExactView()
        {
            if (_blockWorkspace == null)
                return;

            _blockWorkspace.SetCanvasZoom(1f);
            _blockWorkspace.SetCanvasPan(0f, 0f);
            _status = "Reset NativeExact graph view.";
        }

        private void FitNativeExactBounds(Rect canvasRect, Rect bounds)
        {
            if (_blockWorkspace == null || bounds.width <= 0f || bounds.height <= 0f)
                return;

            float padding = EsuHudLayout.Scale(48f);
            float zoomX = (canvasRect.width - padding * 2f) / Mathf.Max(1f, bounds.width);
            float zoomY = (canvasRect.height - padding * 2f) / Mathf.Max(1f, bounds.height);
            float zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), 0.45f, 1.85f);
            _blockWorkspace.SetCanvasZoom(zoom);
            _blockWorkspace.SetCanvasPan(
                canvasRect.width * 0.5f - bounds.center.x * zoom,
                canvasRect.height * 0.5f - bounds.center.y * zoom);
        }

        private bool TryGetNativeExactBounds(
            IReadOnlyList<AutomationBlockNode> nodes,
            out Rect bounds)
        {
            bounds = Rect.zero;
            if (nodes == null || nodes.Count == 0)
                return false;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (AutomationBlockNode node in nodes)
            {
                if (node == null)
                    continue;

                float width = NativeExactNodeWidth(node);
                float height = NativeExactNodeHeight(node);
                minX = Mathf.Min(minX, node.CanvasPosition.X);
                minY = Mathf.Min(minY, node.CanvasPosition.Y);
                maxX = Mathf.Max(maxX, node.CanvasPosition.X + width);
                maxY = Mathf.Max(maxY, node.CanvasPosition.Y + height);
            }

            if (minX == float.MaxValue)
                return false;

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private void DrawBlocksLeftColumn(Rect rect, float divider)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            float minLower = Mathf.Min(EsuHudLayout.Scale(108f), rect.height * 0.42f);
            float minCanvas = Mathf.Min(EsuHudLayout.Scale(260f), rect.height * 0.58f);
            float maxLower = Mathf.Max(minLower, rect.height - minCanvas - divider);
            float lowerHeight = Mathf.Clamp(rect.height * _blocksLowerPanelRatio, minLower, maxLower);
            Rect canvasRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(1f, rect.height - lowerHeight - divider));
            Rect splitRect = new Rect(rect.x, canvasRect.yMax, rect.width, divider);
            Rect loweringRect = new Rect(rect.x, splitRect.yMax, rect.width, lowerHeight);

            DrawTinkercadBlockCanvas(canvasRect);
            DrawBlocksSplitterGrip(splitRect, AutomationBlocksSplitter.CanvasLowering, vertical: false, "Drag to resize the ESU Blocks canvas and native lowering panel.");
            DrawBlocksArea(loweringRect, DrawBlocksLoweringPanel);
            HandleBlocksCanvasLoweringSplitter(rect, splitRect);
        }

        private void DrawBlocksRightColumn(Rect rect, float divider)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            float minPalette = EsuHudLayout.Scale(176f);
            float minInspector = EsuHudLayout.Scale(112f);
            float paletteHeight = ClampSplitHeight(
                rect.height * _blocksRightPaletteRatio,
                rect.height,
                minPalette,
                minInspector + divider);
            Rect paletteRect = new Rect(rect.x, rect.y, rect.width, paletteHeight);
            Rect paletteSplit = new Rect(rect.x, paletteRect.yMax, rect.width, divider);
            Rect inspectorRect = new Rect(rect.x, paletteSplit.yMax, rect.width, Mathf.Max(1f, rect.yMax - paletteSplit.yMax));

            DrawBlocksArea(paletteRect, () => DrawTinkercadBlockPalette(paletteRect.height));
            DrawBlocksSplitterGrip(paletteSplit, AutomationBlocksSplitter.RightPalette, vertical: false, "Drag to resize the block palette and selected-block details.");
            DrawBlocksScrollableArea(inspectorRect, ref _blocksInspectorScroll, DrawTinkercadBlockInspector);

            HandleBlocksTopRatioSplitter(AutomationBlocksSplitter.RightPalette, rect, paletteSplit, ref _blocksRightPaletteRatio, minPalette, minInspector + divider);
        }

        private static float ClampSplitHeight(float desired, float available, float minimum, float minimumAfter)
        {
            if (available <= 1f)
                return 1f;

            if (available <= minimum + minimumAfter)
            {
                float totalMinimum = Mathf.Max(1f, minimum + minimumAfter);
                return Mathf.Clamp(available * minimum / totalMinimum, 1f, available);
            }

            return Mathf.Clamp(desired, minimum, available - minimumAfter);
        }

        private void DrawBlocksArea(Rect rect, Action draw)
        {
            if (draw == null || rect.width <= 1f || rect.height <= 1f)
                return;

            GUILayout.BeginArea(rect);
            draw();
            GUILayout.EndArea();
        }

        private void DrawBlocksScrollableArea(Rect rect, ref Vector2 scroll, Action draw)
        {
            if (draw == null || rect.width <= 1f || rect.height <= 1f)
                return;

            GUILayout.BeginArea(rect);
            scroll = GUILayout.BeginScrollView(
                scroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Width(rect.width),
                GUILayout.Height(rect.height));
            draw();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBlocksSplitterGrip(
            Rect rect,
            AutomationBlocksSplitter splitter,
            bool vertical,
            string tooltip)
        {
            bool active = _draggingBlocksSplitter == splitter;
            bool hovered = Event.current != null && rect.Contains(Event.current.mousePosition);
            if (vertical)
            {
                Color color = new Color(
                    DecorationEditorTheme.Cyan.r,
                    DecorationEditorTheme.Cyan.g,
                    DecorationEditorTheme.Cyan.b,
                    active || hovered ? 0.82f : 0.42f);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(1.25f));
                DrawFilledRect(
                    new Rect(rect.center.x - line * 0.5f, rect.y + EsuHudLayout.Scale(18f), line, Mathf.Max(1f, rect.height - EsuHudLayout.Scale(36f))),
                    color);
            }
            else
            {
                EsuHudLayout.DrawStackDividerGrip(rect, active || hovered);
            }

            EsuCursorTooltip.Register(rect, tooltip);
        }

        private void BeginBlocksSplitterDrag(AutomationBlocksSplitter splitter, Rect rect)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                _draggingBlocksSplitter = splitter;
                current.Use();
            }
        }

        private void HandleBlocksMainRightSplitter(Rect viewport, Rect splitter)
        {
            BeginBlocksSplitterDrag(AutomationBlocksSplitter.MainRight, splitter);
            Event current = Event.current;
            if (current == null)
                return;

            if (_draggingBlocksSplitter == AutomationBlocksSplitter.MainRight &&
                current.type == EventType.MouseDrag)
            {
                float divider = splitter.width;
                float minLeft = Mathf.Min(EsuHudLayout.Scale(360f), Mathf.Max(1f, viewport.width * 0.48f));
                float minRight = Mathf.Min(EsuHudLayout.Scale(260f), Mathf.Max(1f, viewport.width * 0.38f));
                float maxRight = Mathf.Max(minRight, viewport.width - minLeft - divider);
                float rightWidth = Mathf.Clamp(viewport.xMax - current.mousePosition.x, minRight, maxRight);
                _blocksRightColumnRatio = Mathf.Clamp(rightWidth / Mathf.Max(1f, viewport.width), 0.16f, 0.62f);
                current.Use();
                return;
            }

            ClearBlocksSplitterDragOnMouseUp();
        }

        private void HandleBlocksCanvasLoweringSplitter(Rect rect, Rect splitter)
        {
            BeginBlocksSplitterDrag(AutomationBlocksSplitter.CanvasLowering, splitter);
            Event current = Event.current;
            if (current == null)
                return;

            if (_draggingBlocksSplitter == AutomationBlocksSplitter.CanvasLowering &&
                current.type == EventType.MouseDrag)
            {
                float bottomHeight = Mathf.Clamp(rect.yMax - current.mousePosition.y, EsuHudLayout.Scale(96f), Mathf.Max(EsuHudLayout.Scale(96f), rect.height - EsuHudLayout.Scale(240f)));
                _blocksLowerPanelRatio = Mathf.Clamp(bottomHeight / Mathf.Max(1f, rect.height), 0.12f, 0.46f);
                current.Use();
                return;
            }

            ClearBlocksSplitterDragOnMouseUp();
        }

        private void HandleBlocksTopRatioSplitter(
            AutomationBlocksSplitter splitterKind,
            Rect rect,
            Rect splitter,
            ref float ratio,
            float minimum,
            float minimumAfter)
        {
            HandleBlocksNestedTopRatioSplitter(splitterKind, rect, splitter, ref ratio, minimum, minimumAfter);
        }

        private void HandleBlocksNestedTopRatioSplitter(
            AutomationBlocksSplitter splitterKind,
            Rect rect,
            Rect splitter,
            ref float ratio,
            float minimum,
            float minimumAfter)
        {
            BeginBlocksSplitterDrag(splitterKind, splitter);
            Event current = Event.current;
            if (current == null)
                return;

            if (_draggingBlocksSplitter == splitterKind &&
                current.type == EventType.MouseDrag)
            {
                float height = ClampSplitHeight(current.mousePosition.y - rect.y, rect.height, minimum, minimumAfter);
                ratio = Mathf.Clamp(height / Mathf.Max(1f, rect.height), 0.08f, 0.92f);
                current.Use();
                return;
            }

            ClearBlocksSplitterDragOnMouseUp();
        }

        private void ClearBlocksSplitterDragOnMouseUp()
        {
            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseUp &&
                _draggingBlocksSplitter != AutomationBlocksSplitter.None)
            {
                _draggingBlocksSplitter = AutomationBlocksSplitter.None;
                current.Use();
            }
        }

        private void DrawTinkercadBlockPalette(float availableHeight = -1f)
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Blocks", "outliner", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            for (int index = 0; index < s_blockPaletteCategories.Length; index++)
            {
                AutomationBlockCategory category = s_blockPaletteCategories[index];
                if (index == 3)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }

                string label = AutomationBlockCategoryLabel(category);
                if (AutomationGUILayoutButton(
                        new GUIContent(label, "Show " + label + " Automation blocks."),
                        DecorationEditorTheme.ToolButton(_blockPaletteCategory == category),
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                {
                    if (_blockPaletteCategory != category)
                    {
                        _blockPaletteCategory = category;
                        ResetBlockPaletteScroll();
                    }
                }
            }
            GUILayout.EndHorizontal();
            DecorationEditorTheme.Separator();

            float scrollHeight = availableHeight > 0f
                ? Mathf.Max(EsuHudLayout.Scale(86f), availableHeight - EsuHudLayout.Scale(94f))
                : EsuHudLayout.Scale(392f);
            _blockPaletteScroll = GUILayout.BeginScrollView(
                _blockPaletteScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(scrollHeight));
            if (_blockPaletteCategory == AutomationBlockCategory.Advanced)
            {
                DrawNativeBreadboardBlockPalette(scrollHeight);
            }
            else
            {
                DrawSemanticBlockPalette(scrollHeight);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(
                _blockPaletteCategory == AutomationBlockCategory.Advanced
                    ? "Drag native Breadboard blocks onto the canvas, or click one to append it. Apply creates native components and Revert removes them."
                    : "Drag blocks onto the canvas, or click a block to append it.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawSemanticBlockPalette(float scrollHeight)
        {
            DrawSemanticBlockPaletteSearch();
            AutomationBlockDefinition[] categoryDefinitions = AutomationBlockCatalog
                .DefinitionsForCategory(_blockPaletteCategory)
                .ToArray();
            AutomationBlockDefinition[] matchingDefinitions = categoryDefinitions
                .Where(definition => SemanticBlockDefinitionMatchesSearch(definition, _semanticBlockPaletteSearch))
                .ToArray();
            ClampBlockPaletteScroll(scrollHeight, matchingDefinitions.Length, EsuHudLayout.Scale(76f), EsuHudLayout.Scale(74f));
            GUILayout.Label(
                "Semantic blocks: " +
                matchingDefinitions.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                categoryDefinitions.Length.ToString(CultureInfo.InvariantCulture) +
                " shown in " +
                AutomationBlockCategoryLabel(_blockPaletteCategory) +
                ".",
                DecorationEditorTheme.MiniWrap);
            if (matchingDefinitions.Length == 0)
            {
                GUILayout.Label(
                    "No semantic ESU blocks match the current block search. Clear it or switch categories to show beginner blocks.",
                    DecorationEditorTheme.Warning);
                return;
            }

            foreach (AutomationBlockDefinition definition in matchingDefinitions)
                DrawPaletteBlockTemplate(definition);
        }

        private void DrawSemanticBlockPaletteSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _semanticBlockPaletteSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _semanticBlockPaletteSearch, StringComparison.Ordinal))
            {
                _semanticBlockPaletteSearch = nextSearch ?? string.Empty;
                ResetBlockPaletteScroll();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_semanticBlockPaletteSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear semantic ESU block search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _semanticBlockPaletteSearch = string.Empty;
                ResetBlockPaletteScroll();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search semantic ESU blocks by label, category, kind, port, setting, or description.", DecorationEditorTheme.MiniWrap);
        }

        private static bool SemanticBlockDefinitionMatchesSearch(
            AutomationBlockDefinition definition,
            string search)
        {
            if (definition == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (definition.Label ?? string.Empty) + " " +
                (definition.TemplateId ?? string.Empty) + " " +
                definition.Kind + " " +
                AutomationBlockCategoryLabel(definition.Category) + " " +
                (definition.Description ?? string.Empty) + " " +
                string.Join(" ", (definition.InputPorts ?? Array.Empty<AutomationBlockPortDefinition>())
                    .Select(port => port?.Name ?? string.Empty).ToArray()) + " " +
                string.Join(" ", (definition.OutputPorts ?? Array.Empty<AutomationBlockPortDefinition>())
                    .Select(port => port?.Name ?? string.Empty).ToArray()) + " " +
                string.Join(" ", (definition.Settings ?? Array.Empty<AutomationBlockSettingDefinition>())
                    .Select(setting => (setting?.Name ?? string.Empty) + " " + setting?.Kind).ToArray());
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawNativeBreadboardBlockPalette(float scrollHeight)
        {
            if (!TryCreateSelectedBreadboardInspector(
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                GUILayout.Label(reason ?? "Select a Breadboard controller before showing native components.", DecorationEditorTheme.MiniWrap);
                return;
            }

            DrawNativeBreadboardPaletteSearch();
            AutomationBreadboardAvailableComponent[] advertisedComponents = inspector.AvailableComponents
                .Where(component => component != null && !string.IsNullOrWhiteSpace(component.TypeName))
                .OrderBy(component => component.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AutomationBreadboardAvailableComponent[] matchingComponents = advertisedComponents
                .Where(component => NativeComponentMatchesSearch(component, _nativeComponentPaletteSearch))
                .ToArray();
            AutomationBreadboardAvailableComponent[] components = matchingComponents
                .Take(NativePaletteVisibleLimit)
                .ToArray();
            ClampBlockPaletteScroll(scrollHeight, components.Length, EsuHudLayout.Scale(66f), EsuHudLayout.Scale(96f));
            if (advertisedComponents.Length == 0)
            {
                GUILayout.Label("This board did not advertise native Breadboard component types.", DecorationEditorTheme.Warning);
                return;
            }

            GUILayout.Label(
                "Native components: " +
                components.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingComponents.Length.ToString(CultureInfo.InvariantCulture) +
                " shown" +
                (matchingComponents.Length == advertisedComponents.Length
                    ? string.Empty
                    : " from " + advertisedComponents.Length.ToString(CultureInfo.InvariantCulture) + " advertised") +
                ".",
                DecorationEditorTheme.MiniWrap);
            if (components.Length == 0)
            {
                GUILayout.Label("No native Breadboard component types match the current search.", DecorationEditorTheme.Warning);
                return;
            }

            if (matchingComponents.Length > components.Length)
            {
                GUILayout.Label(
                    "Showing first " +
                    components.Length.ToString(CultureInfo.InvariantCulture) +
                    " of " +
                    matchingComponents.Length.ToString(CultureInfo.InvariantCulture) +
                    " matching native Breadboard component(s). Use search/filter to narrow results; full compatibility also needs virtualized native palettes.",
                    DecorationEditorTheme.Warning);
            }

            foreach (AutomationBreadboardAvailableComponent component in components)
                DrawNativeBreadboardBlockTemplate(component);
        }

        private void DrawNativeBreadboardPaletteSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _nativeComponentPaletteSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _nativeComponentPaletteSearch, StringComparison.Ordinal))
            {
                _nativeComponentPaletteSearch = nextSearch ?? string.Empty;
                ResetBlockPaletteScroll();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_nativeComponentPaletteSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear native Breadboard component search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _nativeComponentPaletteSearch = string.Empty;
                ResetBlockPaletteScroll();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search native Breadboard components by label, type, namespace, or description.", DecorationEditorTheme.MiniWrap);
        }

        private static bool NativeComponentMatchesSearch(
            AutomationBreadboardAvailableComponent component,
            string search)
        {
            if (component == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (component.Label ?? string.Empty) + " " +
                (component.TypeName ?? string.Empty) + " " +
                (component.FullTypeName ?? string.Empty) + " " +
                (component.Description ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ResetBlockPaletteScroll()
        {
            _blockPaletteScroll = Vector2.zero;
        }

        private void ClampBlockPaletteScroll(
            float scrollHeight,
            int rowCount,
            float estimatedRowHeight,
            float estimatedHeaderHeight)
        {
            float safeScrollHeight = Mathf.Max(1f, scrollHeight);
            float contentHeight = Mathf.Max(0f, estimatedHeaderHeight) +
                                  Mathf.Max(0, rowCount) * Mathf.Max(1f, estimatedRowHeight);
            float maxY = Mathf.Max(0f, contentHeight - safeScrollHeight);
            if (_blockPaletteScroll.y < 0f || _blockPaletteScroll.y > maxY)
                _blockPaletteScroll.y = Mathf.Clamp(_blockPaletteScroll.y, 0f, maxY);
        }

        private void DrawNativeBreadboardBlockTemplate(AutomationBreadboardAvailableComponent component)
        {
            if (component == null)
                return;

            AutomationBlockDefinition definition = AutomationBlockCatalog.NativeDefinition(component);
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(66f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(66f)));
            DrawTinkercadBlockShape(
                rect,
                BlockCategoryColor(AutomationBlockCategory.Advanced),
                definition.Label,
                definition.IconKey,
                selected: false,
                hat: false,
                badge: AutomationBlockCompatibilityLabel(definition.Compatibility));
            string nativeType = string.IsNullOrWhiteSpace(component.TypeName)
                ? "unknown"
                : component.TypeName;
            Rect typeRect = new Rect(
                rect.x + EsuHudLayout.Scale(31f),
                rect.y + EsuHudLayout.Scale(28f),
                rect.width - EsuHudLayout.Scale(42f),
                EsuHudLayout.Scale(18f));
            DrawFittedSingleLineLabel(
                typeRect,
                "Native type: " + nativeType,
                SingleLineRowStyle(DecorationEditorTheme.Mini),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
            Rect portRect = new Rect(
                typeRect.x,
                typeRect.yMax + EsuHudLayout.Scale(2f),
                typeRect.width,
                EsuHudLayout.Scale(16f));
            DrawFittedSingleLineLabel(
                portRect,
                "Ports: " + AutomationBlockPortSummary(definition) + " | Native Wrapper | lowerable now",
                SingleLineRowStyle(DecorationEditorTheme.Mini),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
            EsuCursorTooltip.Register(
                rect,
                string.IsNullOrWhiteSpace(component.Description)
                    ? "Add native Breadboard component: " + component.TypeName + "."
                    : component.Description);
            EsuCursorTooltip.Register(
                typeRect,
                "Native FtD component type: " +
                (string.IsNullOrWhiteSpace(component.FullTypeName) ? nativeType : component.FullTypeName) +
                ". Apply creates this advertised native component.");

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                _draggingNativePaletteBlock = true;
                _draggingPaletteBlock = false;
                _draggingWorkspaceBlock = false;
                _dragNativePaletteComponent = component;
                _dragWorkspaceBlockId = string.Empty;
                _blockDropIndex = -1;
                _blockDragMouseStart = current.mousePosition;
                _blockDragNodeOffset = Vector2.zero;
                _status = "Drag native Breadboard block " + definition.Label + " onto the ESU Blocks canvas.";
                current.Use();
                return;
            }

            if (current != null &&
                current.type == EventType.MouseUp &&
                current.button == 0 &&
                _draggingNativePaletteBlock &&
                ReferenceEquals(_dragNativePaletteComponent, component) &&
                rect.Contains(current.mousePosition) &&
                !_lastBlockCanvasRect.Contains(GUIUtility.GUIToScreenPoint(current.mousePosition)) &&
                (current.mousePosition - _blockDragMouseStart).sqrMagnitude <= EsuHudLayout.Scale(8f) * EsuHudLayout.Scale(8f))
            {
                AddNativeAutomationBlock(component, -1);
                ClearAutomationBlockDrag();
                current.Use();
            }
        }

        private void DrawTinkercadLinkedSignalMenus()
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Linked signals", "link", DecorationEditorTheme.SubHeader);
            DrawTinkercadLinkedSignalTabs();
            DrawLinkedTargetIdentityGuide();
            DrawTinkercadLinkedSignalSearch();
            DrawTinkercadLinkedSignalSection(_blocksCanvasSignalsTab);
            GUILayout.EndVertical();
            GUILayout.Space(EsuHudLayout.Scale(8f));
        }

        private void DrawTinkercadLinkedSignalFlyout()
        {
            DrawTinkercadLinkedSignalTabs();
            DrawLinkedTargetIdentityGuide();
            DrawTinkercadLinkedSignalSearch();
            GUILayout.Space(EsuHudLayout.Scale(6f));
            DrawTinkercadLinkedSignalSection(_blocksCanvasSignalsTab);
            GUILayout.Space(EsuHudLayout.Scale(4f));
        }

        private void DrawFixedTinkercadLinkedSignalFlyout(Rect body)
        {
            if (body.width <= 1f || body.height <= 1f)
                return;

            float gap = EsuHudLayout.Scale(6f);
            float tabHeight = EsuHudLayout.Scale(28f);
            float y = body.y;
            Rect inputTab = new Rect(body.x, y, Mathf.Min(EsuHudLayout.Scale(96f), body.width * 0.5f - gap * 0.5f), tabHeight);
            Rect outputTab = new Rect(inputTab.xMax + gap, y, inputTab.width, tabHeight);
            DrawFixedTinkercadLinkedSignalTab(inputTab, AutomationLinkDirection.Input, "Inputs", "visibility");
            DrawFixedTinkercadLinkedSignalTab(outputTab, AutomationLinkDirection.Output, "Outputs", "anchor");
            y += tabHeight + gap;

            Rect guideRect = new Rect(body.x, y, body.width, EsuHudLayout.Scale(30f));
            GUI.Label(guideRect, AutomationLinkIdentityGuideLine, DecorationEditorTheme.MiniWrap);
            y += guideRect.height + gap;

            float searchHeight = EsuHudLayout.Scale(24f);
            Rect iconRect = new Rect(body.x, y + EsuHudLayout.Scale(2f), EsuHudLayout.Scale(20f), EsuHudLayout.Scale(20f));
            Texture filterIcon = DecorationEditorIconCatalog.Get("filter");
            if (filterIcon != null)
                GUI.DrawTexture(iconRect, filterIcon, ScaleMode.ScaleToFit, alphaBlend: true);
            Rect clearRect = new Rect(body.xMax - EsuHudLayout.Scale(56f), y, EsuHudLayout.Scale(56f), searchHeight);
            Rect fieldRect = new Rect(
                iconRect.xMax + EsuHudLayout.Scale(6f),
                y,
                Mathf.Max(1f, clearRect.x - iconRect.xMax - EsuHudLayout.Scale(12f)),
                searchHeight);
            string nextSearch = GUI.TextField(fieldRect, _linkedSignalSearch ?? string.Empty, DecorationEditorTheme.TextField);
            if (!string.Equals(nextSearch, _linkedSignalSearch, StringComparison.Ordinal))
            {
                _linkedSignalSearch = nextSearch ?? string.Empty;
                _blocksCanvasSignalsScroll = Vector2.zero;
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_linkedSignalSearch);
            if (AutomationGUIButton(
                    clearRect,
                    new GUIContent("clear", "Clear linked signal search."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
            {
                _linkedSignalSearch = string.Empty;
                _blocksCanvasSignalsScroll = Vector2.zero;
            }
            GUI.enabled = previous;
            y += searchHeight + EsuHudLayout.Scale(4f);

            Rect searchHelp = new Rect(body.x, y, body.width, EsuHudLayout.Scale(28f));
            GUI.Label(searchHelp, "Search linked Inputs/Outputs by direction, target label, category, role, runtime type, cell, or target key.", DecorationEditorTheme.MiniWrap);
            y += searchHelp.height + gap;

            Rect viewport = new Rect(body.x, y, body.width, Mathf.Max(1f, body.yMax - y));
            float contentWidth = Mathf.Max(1f, body.width - EsuHudLayout.Scale(18f));
            float contentHeight = MeasureFixedTinkercadLinkedSignalContentHeight(_blocksCanvasSignalsTab);
            Rect content = new Rect(0f, 0f, contentWidth, contentHeight);
            _blocksCanvasSignalsScroll = GUI.BeginScrollView(
                viewport,
                _blocksCanvasSignalsScroll,
                content,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true);
            try
            {
                float contentY = 0f;
                DrawFixedTinkercadLinkedSignalSection(contentWidth, ref contentY, _blocksCanvasSignalsTab);
            }
            finally
            {
                GUI.EndScrollView();
            }
        }

        private void DrawFixedTinkercadLinkedSignalTab(
            Rect rect,
            AutomationLinkDirection direction,
            string label,
            string icon)
        {
            bool active = _blocksCanvasSignalsTab == direction;
            if (AutomationGUIButton(
                    rect,
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), "Show linked and available " + label.ToLowerInvariant() + "."),
                    DecorationEditorTheme.ToolButton(active)) &&
                !active)
            {
                _blocksCanvasSignalsTab = direction;
                _blocksCanvasSignalsScroll = Vector2.zero;
            }
        }

        private float MeasureFixedTinkercadLinkedSignalContentHeight(AutomationLinkDirection direction)
        {
            AutomationLink[] links = SelectedLinks
                .Where(link => link.Direction == direction && link.Target != null)
                .ToArray();
            AutomationLink[] matchingLinks = links
                .Where(link => TinkercadLinkedSignalMatchesSearch(link, _linkedSignalSearch))
                .ToArray();
            AutomationTarget[] available = AvailableSignalTargets(direction);
            AutomationTarget[] matching = available
                .Where(target => TinkercadAvailableSignalMatchesSearch(target, direction, _linkedSignalSearch))
                .Take(SignalFlyoutAvailableTargetLimit)
                .ToArray();

            float height =
                EsuHudLayout.Scale(24f) +
                EsuHudLayout.Scale(42f) +
                EsuHudLayout.Scale(22f) +
                EsuHudLayout.Scale(6f);
            height += links.Length == 0 || matchingLinks.Length == 0
                ? EsuHudLayout.Scale(42f)
                : matchingLinks.Length * EsuHudLayout.Scale(42f);
            height +=
                EsuHudLayout.Scale(8f) +
                EsuHudLayout.Scale(24f) +
                EsuHudLayout.Scale(46f);
            if (available.Length > SignalFlyoutAvailableTargetLimit)
                height += EsuHudLayout.Scale(34f);
            height += available.Length == 0 || matching.Length == 0
                ? EsuHudLayout.Scale(42f)
                : matching.Length * EsuHudLayout.Scale(42f);
            return Mathf.Max(height + EsuHudLayout.Scale(10f), EsuHudLayout.Scale(160f));
        }

        private void DrawFixedTinkercadLinkedSignalSection(
            float width,
            ref float y,
            AutomationLinkDirection direction)
        {
            bool input = direction == AutomationLinkDirection.Input;
            string title = input ? "Inputs" : "Outputs";
            DrawFixedCompactIconHeader(
                new Rect(0f, y, width, EsuHudLayout.Scale(24f)),
                title,
                input ? "visibility" : "anchor",
                DecorationEditorTheme.SubHeader);
            y += EsuHudLayout.Scale(28f);

            GUI.Label(
                new Rect(0f, y, width, EsuHudLayout.Scale(40f)),
                input
                    ? "Input links become Read Target blocks and native Generic Block Getter proxy nodes on Apply."
                    : "Output links become Set Target blocks and native Generic Block Setter proxy nodes on Apply.",
                DecorationEditorTheme.MiniWrap);
            y += EsuHudLayout.Scale(42f);

            AutomationLink[] links = SelectedLinks
                .Where(link => link.Direction == direction && link.Target != null)
                .ToArray();
            AutomationLink[] matchingLinks = links
                .Where(link => TinkercadLinkedSignalMatchesSearch(link, _linkedSignalSearch))
                .ToArray();
            GUI.Label(
                new Rect(0f, y, width, EsuHudLayout.Scale(18f)),
                title + ": " +
                matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                links.Length.ToString(CultureInfo.InvariantCulture) +
                " linked signal(s) shown.",
                DecorationEditorTheme.MiniWrap);
            y += EsuHudLayout.Scale(22f);

            if (links.Length == 0)
            {
                GUI.Label(
                    new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                    input
                        ? "No input links yet. Close the editor or use Link mode: click a target first, then this Breadboard/controller. Then use Add read."
                        : "No output links yet. Close the editor or use Link mode: click this Breadboard/controller first, then the target it should affect. Then use Add set.",
                    DecorationEditorTheme.MiniWrap);
                y += EsuHudLayout.Scale(42f);
            }
            else if (matchingLinks.Length == 0)
            {
                GUI.Label(
                    new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                    input
                        ? "No input links match the current linked signal search. Clear it to show Read/GBG options."
                        : "No output links match the current linked signal search. Clear it to show Set/GBS options.",
                    DecorationEditorTheme.Warning);
                y += EsuHudLayout.Scale(42f);
            }
            else
            {
                AutomationBlockKind addKind = input
                    ? AutomationBlockKind.ReadTarget
                    : AutomationBlockKind.SetTarget;
                foreach (AutomationLink link in matchingLinks)
                {
                    DrawFixedTinkercadLinkedSignalRow(
                        new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                        link,
                        addKind);
                    y += EsuHudLayout.Scale(42f);
                }
            }

            DrawFixedTinkercadAvailableSignalTargets(width, ref y, direction);
        }

        private void DrawFixedTinkercadAvailableSignalTargets(
            float width,
            ref float y,
            AutomationLinkDirection direction)
        {
            bool input = direction == AutomationLinkDirection.Input;
            y += EsuHudLayout.Scale(4f);
            DrawFixedCompactIconHeader(
                new Rect(0f, y, width, EsuHudLayout.Scale(24f)),
                input ? "Available inputs" : "Available outputs",
                input ? "visibility" : "anchor",
                DecorationEditorTheme.SubHeader);
            y += EsuHudLayout.Scale(28f);

            AutomationTarget[] available = AvailableSignalTargets(direction);
            AutomationTarget[] matching = available
                .Where(target => TinkercadAvailableSignalMatchesSearch(target, direction, _linkedSignalSearch))
                .Take(SignalFlyoutAvailableTargetLimit)
                .ToArray();
            GUI.Label(
                new Rect(0f, y, width, EsuHudLayout.Scale(42f)),
                matching.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                available.Length.ToString(CultureInfo.InvariantCulture) +
                " available target(s) shown. " +
                (input
                    ? "Add read links this target as an input and drops a Read block."
                    : "Add set links this target as an output and drops a Set block."),
                DecorationEditorTheme.MiniWrap);
            y += EsuHudLayout.Scale(46f);

            if (available.Length > SignalFlyoutAvailableTargetLimit)
            {
                GUI.Label(
                    new Rect(0f, y, width, EsuHudLayout.Scale(30f)),
                    "Showing first " +
                    SignalFlyoutAvailableTargetLimit.ToString(CultureInfo.InvariantCulture) +
                    " available target(s). Use search to narrow the list.",
                    DecorationEditorTheme.Warning);
                y += EsuHudLayout.Scale(34f);
            }

            if (available.Length == 0)
            {
                GUI.Label(
                    new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                    input
                        ? "No readable input targets are available for the selected controller."
                        : "No writable output targets are available for the selected controller.",
                    DecorationEditorTheme.MiniWrap);
                y += EsuHudLayout.Scale(42f);
                return;
            }

            if (matching.Length == 0)
            {
                GUI.Label(
                    new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                    input
                        ? "No available input targets match the current linked signal search."
                        : "No available output targets match the current linked signal search.",
                    DecorationEditorTheme.Warning);
                y += EsuHudLayout.Scale(42f);
                return;
            }

            foreach (AutomationTarget target in matching)
            {
                DrawFixedTinkercadAvailableSignalRow(
                    new Rect(0f, y, width, EsuHudLayout.Scale(38f)),
                    target,
                    direction);
                y += EsuHudLayout.Scale(42f);
            }
        }

        private void DrawFixedTinkercadLinkedSignalRow(
            Rect row,
            AutomationLink link,
            AutomationBlockKind addKind)
        {
            if (link?.Target == null)
                return;

            bool selected = IsSelectedAutomationLink(link);
            GUI.Box(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
            Rect actionRect = FixedSignalActionRect(row);
            Rect textRect = new Rect(
                row.x,
                row.y,
                Mathf.Max(1f, actionRect.x - row.x - EsuHudLayout.Scale(4f)),
                row.height);
            DrawAutomationIconRow(
                textRect,
                AutomationTargetIconKey(link.Target),
                link.TargetLabel,
                link.DirectionLabel,
                DecorationEditorTheme.Body,
                DecorationEditorTheme.Mini);

            if (AutomationGUIButton(
                    actionRect,
                    new GUIContent(
                        addKind == AutomationBlockKind.ReadTarget ? "Add read" : "Add set",
                        DecorationEditorIconCatalog.Get(addKind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor"),
                        "Add a " + (addKind == AutomationBlockKind.ReadTarget ? "Read" : "Set") + " block for " + link.TargetLabel + "."),
                    DecorationEditorTheme.Button))
            {
                AddAutomationBlockForLink(addKind, link);
                Event.current?.Use();
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                row.Contains(current.mousePosition) &&
                !actionRect.Contains(current.mousePosition))
            {
                SelectAutomationLink(link);
                RegisterAutomationTargetPreview(row, link.Target, link.DirectionLabel + " link");
                _status = "Selected " + link.DirectionLabel.ToLowerInvariant() + " link: " + link.TargetLabel + ".";
                current.Use();
            }

            RegisterAutomationTargetPreview(row, link.Target, link.DirectionLabel + " link");
            TryOpenAutomationRowContextMenu(row, mouse => OpenAutomationLinkContextMenu(link, mouse));
        }

        private void DrawFixedTinkercadAvailableSignalRow(
            Rect row,
            AutomationTarget target,
            AutomationLinkDirection direction)
        {
            if (target == null)
                return;

            GUI.Box(row, GUIContent.none, DecorationEditorTheme.Row);
            Rect actionRect = FixedSignalActionRect(row);
            Rect textRect = new Rect(
                row.x,
                row.y,
                Mathf.Max(1f, actionRect.x - row.x - EsuHudLayout.Scale(4f)),
                row.height);
            DrawAutomationIconRow(
                textRect,
                AutomationTargetIconKey(target),
                target.Label,
                AutomationTargetCatalog.CategoryLabel(target.Category) + " | " + AutomationTargetCatalog.RoleLabel(target),
                DecorationEditorTheme.Mini,
                DecorationEditorTheme.MiniWrap);

            AutomationBlockKind addKind = direction == AutomationLinkDirection.Input
                ? AutomationBlockKind.ReadTarget
                : AutomationBlockKind.SetTarget;
            if (AutomationGUIButton(
                    actionRect,
                    new GUIContent(
                        addKind == AutomationBlockKind.ReadTarget ? "Add read" : "Add set",
                        DecorationEditorIconCatalog.Get(addKind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor"),
                        "Link " + target.Label + " as an " + LinkDirectionLabel(direction).ToLowerInvariant() + " and add its ESU block."),
                    DecorationEditorTheme.Button))
            {
                AutomationLink link = EnsureAutomationSignalLink(target, direction);
                AddAutomationBlockForLink(addKind, link);
                Event.current?.Use();
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                row.Contains(current.mousePosition) &&
                !actionRect.Contains(current.mousePosition))
            {
                EnsureAutomationSignalLink(target, direction);
                RegisterAutomationTargetPreview(row, target, LinkDirectionLabel(direction) + " target");
                current.Use();
            }

            RegisterAutomationTargetPreview(row, target, LinkDirectionLabel(direction) + " target");
            EsuCursorTooltip.Register(row, "Click to link this target, or use Add to link it and create a block.");
        }

        private static Rect FixedSignalActionRect(Rect row)
        {
            float actionWidth = Mathf.Clamp(row.width * 0.33f, EsuHudLayout.Scale(58f), EsuHudLayout.Scale(92f));
            return new Rect(
                row.xMax - actionWidth - EsuHudLayout.Scale(4f),
                row.y + EsuHudLayout.Scale(5f),
                actionWidth,
                row.height - EsuHudLayout.Scale(10f));
        }

        private void DrawTinkercadLinkedSignalTabs()
        {
            GUILayout.BeginHorizontal();
            DrawTinkercadLinkedSignalTab(AutomationLinkDirection.Input, "Inputs", "visibility");
            DrawTinkercadLinkedSignalTab(AutomationLinkDirection.Output, "Outputs", "anchor");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(EsuHudLayout.Scale(5f));
        }

        private void DrawTinkercadLinkedSignalTab(
            AutomationLinkDirection direction,
            string label,
            string icon)
        {
            bool active = _blocksCanvasSignalsTab == direction;
            if (AutomationGUILayoutButton(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), "Show linked and available " + label.ToLowerInvariant() + "."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(88f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))) &&
                !active)
            {
                _blocksCanvasSignalsTab = direction;
                _blocksCanvasSignalsScroll = Vector2.zero;
            }
        }

        private void DrawLinkedTargetIdentityGuide()
        {
            GUILayout.Label(AutomationLinkIdentityGuideLine, DecorationEditorTheme.MiniWrap);
        }

        private void DrawTinkercadLinkedSignalSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _linkedSignalSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _linkedSignalSearch, StringComparison.Ordinal))
                _linkedSignalSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_linkedSignalSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear linked signal search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _linkedSignalSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search linked Inputs/Outputs by direction, target label, category, role, runtime type, cell, or target key.", DecorationEditorTheme.MiniWrap);
        }

        private void DrawTinkercadLinkedSignalSection(AutomationLinkDirection direction)
        {
            bool input = direction == AutomationLinkDirection.Input;
            bool output = direction == AutomationLinkDirection.Output;
            string title = input ? "Inputs" : output ? "Outputs" : "Signals";
            DrawCompactIconHeader(
                title,
                input ? "visibility" : "anchor",
                DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                input
                    ? "Input links become Read Target blocks and native Generic Block Getter proxy nodes on Apply."
                    : "Output links become Set Target blocks and native Generic Block Setter proxy nodes on Apply.",
                DecorationEditorTheme.MiniWrap);
            AutomationLink[] links = SelectedLinks
                .Where(link => link.Direction == direction && link.Target != null)
                .ToArray();
            AutomationLink[] matchingLinks = links
                .Where(link => TinkercadLinkedSignalMatchesSearch(link, _linkedSignalSearch))
                .ToArray();
            GUILayout.Label(
                title + ": " +
                matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                links.Length.ToString(CultureInfo.InvariantCulture) +
                " linked signal(s) shown.",
                DecorationEditorTheme.MiniWrap);
            if (links.Length == 0)
            {
                GUILayout.Label(
                    input
                        ? "No input links yet. Close the editor or use Link mode: click a target first, then this Breadboard/controller. Then use Add read."
                        : "No output links yet. Close the editor or use Link mode: click this Breadboard/controller first, then the target it should affect. Then use Add set.",
                    DecorationEditorTheme.MiniWrap);
            }
            else if (matchingLinks.Length == 0)
            {
                GUILayout.Label(
                    input
                        ? "No input links match the current linked signal search. Clear it to show Read/GBG options."
                        : "No output links match the current linked signal search. Clear it to show Set/GBS options.",
                    DecorationEditorTheme.Warning);
            }
            else
            {
                AutomationBlockKind addKind = input
                    ? AutomationBlockKind.ReadTarget
                    : AutomationBlockKind.SetTarget;
                foreach (AutomationLink link in matchingLinks)
                    DrawTinkercadLinkedSignalRow(link, addKind);
                if (!string.IsNullOrWhiteSpace(_selectedLinkTargetKey) &&
                    _selectedLinkDirection == direction &&
                    links.Any(IsSelectedAutomationLink) &&
                    !matchingLinks.Any(IsSelectedAutomationLink))
                {
                    GUILayout.Label("Selected linked signal is hidden by the current linked signal search.", DecorationEditorTheme.Warning);
                }
            }

            DrawTinkercadAvailableSignalTargets(direction);
        }

        private void DrawTinkercadAvailableSignalTargets(AutomationLinkDirection direction)
        {
            AutomationTarget[] available = AvailableSignalTargets(direction);
            AutomationTarget[] matching = available
                .Where(target => TinkercadAvailableSignalMatchesSearch(target, direction, _linkedSignalSearch))
                .Take(SignalFlyoutAvailableTargetLimit)
                .ToArray();
            GUILayout.Space(EsuHudLayout.Scale(5f));
            DrawCompactIconHeader(
                direction == AutomationLinkDirection.Input ? "Available inputs" : "Available outputs",
                direction == AutomationLinkDirection.Input ? "visibility" : "anchor",
                DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                matching.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                available.Length.ToString(CultureInfo.InvariantCulture) +
                " available target(s) shown. " +
                (direction == AutomationLinkDirection.Input
                    ? "Add read links this target as an input and drops a Read block."
                    : "Add set links this target as an output and drops a Set block."),
                DecorationEditorTheme.MiniWrap);
            if (available.Length > SignalFlyoutAvailableTargetLimit)
            {
                GUILayout.Label(
                    "Showing first " +
                    SignalFlyoutAvailableTargetLimit.ToString(CultureInfo.InvariantCulture) +
                    " available target(s). Use search to narrow the list.",
                    DecorationEditorTheme.Warning);
            }

            if (available.Length == 0)
            {
                GUILayout.Label(
                    direction == AutomationLinkDirection.Input
                        ? "No readable input targets are available for the selected controller."
                        : "No writable output targets are available for the selected controller.",
                    DecorationEditorTheme.MiniWrap);
                return;
            }

            if (matching.Length == 0)
            {
                GUILayout.Label(
                    direction == AutomationLinkDirection.Input
                        ? "No available input targets match the current linked signal search."
                        : "No available output targets match the current linked signal search.",
                    DecorationEditorTheme.Warning);
                return;
            }

            foreach (AutomationTarget target in matching)
                DrawTinkercadAvailableSignalRow(target, direction);
        }

        private AutomationTarget[] AvailableSignalTargets(AutomationLinkDirection direction)
        {
            if (_selectedController == null || _targets == null)
                return Array.Empty<AutomationTarget>();

            string controllerKey = _selectedController.StableKey;
            HashSet<string> linkedKeys = new HashSet<string>(
                SelectedLinks
                    .Where(link => link.Direction == direction)
                    .Select(link => link.TargetKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.Ordinal);
            return _targets
                .Where(target => target != null &&
                                 !string.Equals(target.StableKey, controllerKey, StringComparison.Ordinal) &&
                                 !linkedKeys.Contains(target.StableKey))
                .Where(target => direction == AutomationLinkDirection.Input
                    ? AutomationTargetCatalog.IsBreadboardReadableTarget(target)
                    : AutomationTargetCatalog.IsBreadboardWritableTarget(target))
                .GroupBy(target => target.StableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => AutomationTargetCatalog.CategoryLabel(target.Category), StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool TinkercadAvailableSignalMatchesSearch(
            AutomationTarget target,
            AutomationLinkDirection direction,
            string search)
        {
            if (target == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (direction == AutomationLinkDirection.Input ? "input read getter readable" : "output write setter writable") + " " +
                target.Label + " " +
                target.StableKey + " " +
                AutomationTargetCatalog.CategoryLabel(target.Category) + " " +
                AutomationTargetCatalog.RoleLabel(target) + " " +
                target.RuntimeType + " " +
                FormatCell(target.LocalPosition);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawTinkercadAvailableSignalRow(
            AutomationTarget target,
            AutomationLinkDirection direction)
        {
            if (target == null)
                return;

            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(38f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(38f)));
            GUI.Box(row, GUIContent.none, DecorationEditorTheme.Row);

            float actionWidth = Mathf.Clamp(row.width * 0.33f, EsuHudLayout.Scale(58f), EsuHudLayout.Scale(92f));
            Rect actionRect = new Rect(
                row.xMax - actionWidth - EsuHudLayout.Scale(4f),
                row.y + EsuHudLayout.Scale(5f),
                actionWidth,
                row.height - EsuHudLayout.Scale(10f));
            Rect textRect = new Rect(
                row.x,
                row.y,
                Mathf.Max(1f, actionRect.x - row.x - EsuHudLayout.Scale(4f)),
                row.height);
            DrawAutomationIconRow(
                textRect,
                AutomationTargetIconKey(target),
                target.Label,
                AutomationTargetCatalog.CategoryLabel(target.Category) + " | " + AutomationTargetCatalog.RoleLabel(target),
                DecorationEditorTheme.Mini,
                DecorationEditorTheme.MiniWrap);

            AutomationBlockKind addKind = direction == AutomationLinkDirection.Input
                ? AutomationBlockKind.ReadTarget
                : AutomationBlockKind.SetTarget;
            if (AutomationGUIButton(
                    actionRect,
                    new GUIContent(
                        addKind == AutomationBlockKind.ReadTarget ? "Add read" : "Add set",
                        DecorationEditorIconCatalog.Get(addKind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor"),
                        "Link " + target.Label + " as an " + LinkDirectionLabel(direction).ToLowerInvariant() + " and add its ESU block."),
                    DecorationEditorTheme.Button))
            {
                AutomationLink link = EnsureAutomationSignalLink(target, direction);
                AddAutomationBlockForLink(addKind, link);
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                row.Contains(current.mousePosition) &&
                !actionRect.Contains(current.mousePosition))
            {
                EnsureAutomationSignalLink(target, direction);
                RegisterAutomationTargetPreview(row, target, LinkDirectionLabel(direction) + " target");
                current.Use();
            }

            RegisterAutomationTargetPreview(row, target, LinkDirectionLabel(direction) + " target");
            EsuCursorTooltip.Register(row, "Click to link this target, or use Add to link it and create a block.");
        }

        private AutomationLink EnsureAutomationSignalLink(
            AutomationTarget target,
            AutomationLinkDirection direction)
        {
            if (_selectedController == null || target == null)
                return null;

            string controllerKey = _selectedController.StableKey;
            string targetKey = target.StableKey;
            AutomationLink existing = _links.FirstOrDefault(link =>
                string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                string.Equals(link.TargetKey, targetKey, StringComparison.Ordinal) &&
                link.Direction == direction);
            if (existing != null)
            {
                SelectAutomationLink(existing);
                return existing;
            }

            var added = new AutomationLink(_selectedController, target, direction);
            _links.Add(added);
            SelectAutomationLink(added);
            if (direction == AutomationLinkDirection.Output &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(target))
            {
                _automationCodeOutputTargetKey = targetKey;
            }

            InvalidateAutomationLinksChanged(
                "Linked " +
                LinkDirectionLabel(direction).ToLowerInvariant() +
                ": " +
                _selectedController.Label +
                " -> " +
                target.Label +
                ".");
            return added;
        }

        private static bool TinkercadLinkedSignalMatchesSearch(
            AutomationLink link,
            string search)
        {
            if (link == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            AutomationTarget target = link.Target;
            string haystack =
                (link.DirectionLabel ?? string.Empty) + " " +
                (link.TargetLabel ?? string.Empty) + " " +
                (link.TargetKey ?? string.Empty) + " " +
                (link.SelectionKey ?? string.Empty) + " " +
                (target?.Label ?? string.Empty) + " " +
                (target == null ? string.Empty : AutomationTargetCatalog.CategoryLabel(target.Category)) + " " +
                (target == null ? string.Empty : AutomationTargetCatalog.RoleLabel(target)) + " " +
                (target?.RuntimeType ?? string.Empty) + " " +
                (target == null ? string.Empty : FormatCell(target.LocalPosition)) + " " +
                (target?.StableKey ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawTinkercadLinkedSignalRow(
            AutomationLink link,
            AutomationBlockKind addKind)
        {
            if (link?.Target == null)
                return;

            bool selected = IsSelectedAutomationLink(link);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(38f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(38f)));
            GUI.Box(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);

            float actionWidth = Mathf.Clamp(row.width * 0.33f, EsuHudLayout.Scale(58f), EsuHudLayout.Scale(92f));
            Rect actionRect = new Rect(
                row.xMax - actionWidth - EsuHudLayout.Scale(4f),
                row.y + EsuHudLayout.Scale(5f),
                actionWidth,
                row.height - EsuHudLayout.Scale(10f));
            Rect textRect = new Rect(
                row.x,
                row.y,
                Mathf.Max(1f, actionRect.x - row.x - EsuHudLayout.Scale(4f)),
                row.height);
            DrawAutomationIconRow(
                textRect,
                AutomationTargetIconKey(link.Target),
                link.TargetLabel,
                link.DirectionLabel,
                DecorationEditorTheme.Body,
                DecorationEditorTheme.Mini);

            if (AutomationGUIButton(
                    actionRect,
                    new GUIContent(
                        addKind == AutomationBlockKind.ReadTarget ? "Add read" : "Add set",
                        DecorationEditorIconCatalog.Get(addKind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor"),
                        "Add a " + (addKind == AutomationBlockKind.ReadTarget ? "Read" : "Set") + " block for " + link.TargetLabel + "."),
                    DecorationEditorTheme.Button))
            {
                AddAutomationBlockForLink(addKind, link);
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                row.Contains(current.mousePosition) &&
                !actionRect.Contains(current.mousePosition))
            {
                SelectAutomationLink(link);
                RegisterAutomationTargetPreview(row, link.Target, link.DirectionLabel + " link");
                _status = "Selected " + link.DirectionLabel.ToLowerInvariant() + " link: " + link.TargetLabel + ".";
                current.Use();
            }

            RegisterAutomationTargetPreview(row, link.Target, link.DirectionLabel + " link");
            TryOpenAutomationRowContextMenu(row, mouse => OpenAutomationLinkContextMenu(link, mouse));
        }

        private void DrawPaletteBlockTemplate(AutomationBlockDefinition definition)
        {
            if (definition == null)
                return;

            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(76f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(76f)));
            DrawTinkercadBlockShape(
                rect,
                BlockCategoryColor(definition.Category),
                definition.Label,
                definition.IconKey,
                selected: false,
                hat: definition.Kind == AutomationBlockKind.WhenIf,
                badge: AutomationBlockCompatibilityLabel(definition.Compatibility));
            Rect descriptionRect = new Rect(
                rect.x + EsuHudLayout.Scale(31f),
                rect.y + EsuHudLayout.Scale(27f),
                rect.width - EsuHudLayout.Scale(42f),
                EsuHudLayout.Scale(18f));
            DrawFittedSingleLineLabel(
                descriptionRect,
                definition.Description,
                SingleLineRowStyle(DecorationEditorTheme.Mini),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
            Rect summaryRect = new Rect(
                descriptionRect.x,
                descriptionRect.yMax + EsuHudLayout.Scale(4f),
                descriptionRect.width,
                EsuHudLayout.Scale(18f));
            DrawFittedSingleLineLabel(
                summaryRect,
                AutomationBlockAudienceLabel(definition.Audience) +
                " | ports: " +
                AutomationBlockPortSummary(definition) +
                " | " +
                (definition.CanLowerToNative ? "lowerable now" : "not lowerable yet"),
                SingleLineRowStyle(DecorationEditorTheme.Mini),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
            EsuCursorTooltip.Register(
                rect,
                definition.Description +
                " Ports: " +
                AutomationBlockPortSummary(definition) +
                ". " +
                AutomationBlockAudienceLabel(definition.Audience) +
                "; " +
                AutomationBlockCompatibilityLabel(definition.Compatibility) +
                ".");
            HandlePaletteBlockTemplateInput(rect, definition.Kind);
        }

        private void HandlePaletteBlockTemplateInput(Rect rect, AutomationBlockKind kind)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                _draggingPaletteBlock = true;
                _draggingNativePaletteBlock = false;
                _draggingWorkspaceBlock = false;
                _dragPaletteBlockKind = kind;
                _dragNativePaletteComponent = null;
                _dragWorkspaceBlockId = string.Empty;
                _blockDropIndex = -1;
                _blockDragMouseStart = current.mousePosition;
                _blockDragNodeOffset = Vector2.zero;
                _status = "Drag " + PaletteBlockPreviewText(kind) + " onto the ESU Blocks canvas.";
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp &&
                current.button == 0 &&
                _draggingPaletteBlock &&
                _dragPaletteBlockKind == kind &&
                rect.Contains(current.mousePosition) &&
                !_lastBlockCanvasRect.Contains(GUIUtility.GUIToScreenPoint(current.mousePosition)) &&
                (current.mousePosition - _blockDragMouseStart).sqrMagnitude <= EsuHudLayout.Scale(8f) * EsuHudLayout.Scale(8f))
            {
                AddAutomationBlock(kind, -1);
                ClearAutomationBlockDrag();
                current.Use();
            }
        }

        private void DrawTinkercadBlockCanvas(Rect viewport)
        {
            if (viewport.width <= 1f || viewport.height <= 1f)
                return;

            GUILayout.BeginArea(viewport);
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader("Block canvas", "outliner", DecorationEditorTheme.SubHeader);
            bool signalsWasOpen = _blocksCanvasSignalsOpen;
            DrawCanvasFlyoutToggle("Signals", "link", ref _blocksCanvasSignalsOpen, ref _blocksCanvasSystemsOpen, ref _blocksCanvasSignalsScroll);
            if (!signalsWasOpen && _blocksCanvasSignalsOpen)
                _blocksCanvasSignalsTab = AutomationLinkDirection.Input;
            DrawCanvasFlyoutToggle("Systems", "duplicate", ref _blocksCanvasSystemsOpen, ref _blocksCanvasSignalsOpen, ref _blocksCanvasSystemsScroll);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Zoom: " + (_blockWorkspace?.CanvasZoom ?? 1f).ToString("0.00", CultureInfo.InvariantCulture) + "x",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(86f)));
            GUILayout.EndHorizontal();
            float canvasHeight = Mathf.Max(1f, viewport.height - EsuHudLayout.Scale(26f));
            Rect canvas = GUILayoutUtility.GetRect(
                1f,
                canvasHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(canvasHeight));
            _lastBlockCanvasRect = GuiToScreenRect(canvas);
            GUI.Box(canvas, GUIContent.none, DecorationEditorTheme.Panel);
            DrawFilledRect(
                new Rect(canvas.x + EsuHudLayout.Scale(2f), canvas.y + EsuHudLayout.Scale(2f), canvas.width - EsuHudLayout.Scale(4f), canvas.height - EsuHudLayout.Scale(4f)),
                new Color(0.92f, 0.95f, 0.96f, 0.11f));

            ClampBlockCanvasPan(canvas);
            List<Rect> nodeRects = DrawTinkercadProgram(canvas);
            HandleBlockCanvasNavigation(canvas, nodeRects);
            HandleBlockCanvasDrop(canvas, nodeRects);
            DrawBlocksCanvasFlyout(canvas);
            GUILayout.EndArea();
        }

        private void DrawCanvasFlyoutToggle(
            string label,
            string icon,
            ref bool open,
            ref bool otherOpen,
            ref Vector2 scroll)
        {
            if (AutomationGUILayoutButton(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), "Open " + label + " tools on the canvas."),
                    DecorationEditorTheme.ToolButton(open),
                    GUILayout.Width(EsuHudLayout.Scale(82f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                open = !open;
                if (open)
                {
                    otherOpen = false;
                    scroll = Vector2.zero;
                }
            }
        }

        private void DrawBlocksCanvasFlyout(Rect canvas)
        {
            if (!_blocksCanvasSignalsOpen && !_blocksCanvasSystemsOpen)
                return;

            Rect flyout = BlocksCanvasFlyoutRect(canvas);
            GUI.Box(flyout, GUIContent.none, DecorationEditorTheme.Panel);

            Rect header = new Rect(
                flyout.x + EsuHudLayout.Scale(8f),
                flyout.y + EsuHudLayout.Scale(8f),
                flyout.width - EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(28f));
            if (DrawBlocksCanvasFlyoutHeader(
                    header,
                    _blocksCanvasSignalsOpen ? "Signals" : "Systems",
                    _blocksCanvasSignalsOpen ? "link" : "duplicate"))
            {
                _blocksCanvasSignalsOpen = false;
                _blocksCanvasSystemsOpen = false;
                _blocksCanvasSignalsScroll = Vector2.zero;
                _blocksCanvasSystemsScroll = Vector2.zero;
            }

            Rect body = new Rect(
                flyout.x + EsuHudLayout.Scale(12f),
                header.yMax + EsuHudLayout.Scale(8f),
                flyout.width - EsuHudLayout.Scale(24f),
                flyout.yMax - header.yMax - EsuHudLayout.Scale(18f));
            if (_blocksCanvasSignalsOpen)
            {
                DrawFixedTinkercadLinkedSignalFlyout(body);
                return;
            }

            GUILayout.BeginArea(body);
            try
            {
                _blocksCanvasSystemsScroll = GUILayout.BeginScrollView(
                    _blocksCanvasSystemsScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(Mathf.Max(1f, body.width)),
                    GUILayout.Height(Mathf.Max(1f, body.height)));
                GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(1f, body.width - EsuHudLayout.Scale(18f))));
                DrawBlocksSystemBlockFlyout();
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private bool DrawBlocksCanvasFlyoutHeader(
            Rect header,
            string title,
            string iconKey)
        {
            GUI.Label(header, GUIContent.none, DecorationEditorTheme.SubHeader);
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            Rect iconRect = new Rect(
                header.x + EsuHudLayout.Scale(7f),
                header.y + EsuHudLayout.Scale(6f),
                EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(16f));
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            Rect closeRect = new Rect(
                header.xMax - EsuHudLayout.Scale(74f),
                header.y + EsuHudLayout.Scale(3f),
                EsuHudLayout.Scale(70f),
                header.height - EsuHudLayout.Scale(6f));
            Rect titleRect = new Rect(
                iconRect.xMax + EsuHudLayout.Scale(8f),
                header.y,
                Mathf.Max(1f, closeRect.x - iconRect.xMax - EsuHudLayout.Scale(14f)),
                header.height);
            DrawFittedSingleLineLabel(
                titleRect,
                title,
                IconHeaderTextStyle(DecorationEditorTheme.SubHeader),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(9));

            return AutomationGUIButton(
                closeRect,
                new GUIContent("Close", "Close this canvas panel."),
                DecorationEditorTheme.Button);
        }

        private Rect BlocksCanvasFlyoutRect(Rect canvas)
        {
            if (!_blocksCanvasSignalsOpen && !_blocksCanvasSystemsOpen)
                return Rect.zero;

            float width = Mathf.Min(
                canvas.width - EsuHudLayout.Scale(24f),
                Mathf.Min(EsuHudLayout.Scale(520f), Mathf.Max(EsuHudLayout.Scale(360f), canvas.width * 0.48f)));
            float height = Mathf.Min(
                canvas.height - EsuHudLayout.Scale(24f),
                Mathf.Min(EsuHudLayout.Scale(520f), Mathf.Max(EsuHudLayout.Scale(220f), canvas.height * 0.72f)));
            return new Rect(
                canvas.x + EsuHudLayout.Scale(12f),
                canvas.y + EsuHudLayout.Scale(12f),
                width,
                height);
        }

        private static Rect GuiToScreenRect(Rect rect)
        {
            Vector2 screen = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.y));
            return new Rect(screen.x, screen.y, rect.width, rect.height);
        }

        private void ClampBlockCanvasPan(Rect canvas)
        {
            if (_blockWorkspace == null || canvas.width <= 1f || canvas.height <= 1f)
                return;

            if (_blockWorkspace.Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                ClampNativeExactCanvasPan(canvas);
                return;
            }

            float zoom = Mathf.Clamp(_blockWorkspace.CanvasZoom, 0.45f, 1.85f);
            float blockWidth = Mathf.Clamp(
                canvas.width - EsuHudLayout.Scale(140f),
                EsuHudLayout.Scale(300f),
                EsuHudLayout.Scale(620f)) * zoom;
            float baseX = Mathf.Clamp(
                canvas.width * 0.44f,
                EsuHudLayout.Scale(140f),
                Mathf.Max(EsuHudLayout.Scale(18f), canvas.width - blockWidth - EsuHudLayout.Scale(24f)));
            float minX = canvas.width - blockWidth - EsuHudLayout.Scale(18f) - baseX;
            float maxX = EsuHudLayout.Scale(18f) - baseX;

            float startHeight = EsuHudLayout.Scale(58f) * zoom;
            float gapHeight = EsuHudLayout.Scale(42f) * zoom;
            float stackHeight = Mathf.Max(
                EsuHudLayout.Scale(132f) * zoom,
                _blockWorkspace.ExecutableNodes.Sum(node => TinkercadBlockHeight(node) * zoom) + EsuHudLayout.Scale(74f) * zoom);
            float contentHeight = startHeight + gapHeight + stackHeight;
            float baseY = EsuHudLayout.Scale(42f);
            float minY = canvas.height - contentHeight - EsuHudLayout.Scale(18f) - baseY;
            float maxY = EsuHudLayout.Scale(18f) - baseY;

            float clampedX = ClampBetween(_blockWorkspace.CanvasPan.X, minX, maxX);
            float clampedY = ClampBetween(_blockWorkspace.CanvasPan.Y, minY, maxY);
            if (Math.Abs(clampedX - _blockWorkspace.CanvasPan.X) > 0.01f ||
                Math.Abs(clampedY - _blockWorkspace.CanvasPan.Y) > 0.01f)
            {
                _blockWorkspace.SetCanvasPan(clampedX, clampedY);
            }
        }

        private static float ClampBetween(float value, float a, float b)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            return Mathf.Clamp(value, min, max);
        }

        private void ClampNativeExactCanvasPan(Rect canvas)
        {
            AutomationBlockNode[] nodes = _blockWorkspace.Nodes
                .Where(node => node != null)
                .ToArray();
            if (nodes.Length == 0)
                return;

            float zoom = Mathf.Clamp(_blockWorkspace.CanvasZoom, 0.45f, 1.85f);
            float layoutScale = NativeExactDrawingScale();
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (AutomationBlockNode node in nodes)
            {
                float x = node.CanvasPosition.X * zoom;
                float y = node.CanvasPosition.Y * zoom;
                float width = NativeExactNodeWidth(node) * layoutScale * zoom;
                float height = NativeExactNodeHeight(node) * layoutScale * zoom;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x + width);
                maxY = Mathf.Max(maxY, y + height);
            }

            if (minX == float.MaxValue)
                return;

            float padding = EsuHudLayout.Scale(18f);
            float minPanX = canvas.width - maxX - padding;
            float maxPanX = padding - minX;
            float minPanY = canvas.height - maxY - padding;
            float maxPanY = padding - minY;
            float clampedX = ClampBetween(_blockWorkspace.CanvasPan.X, minPanX, maxPanX);
            float clampedY = ClampBetween(_blockWorkspace.CanvasPan.Y, minPanY, maxPanY);
            if (Math.Abs(clampedX - _blockWorkspace.CanvasPan.X) > 0.01f ||
                Math.Abs(clampedY - _blockWorkspace.CanvasPan.Y) > 0.01f)
            {
                _blockWorkspace.SetCanvasPan(clampedX, clampedY);
            }
        }

        private List<Rect> DrawTinkercadProgram(Rect canvas)
        {
            if (_blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact)
                return DrawNativeExactProgram(canvas);

            var screenNodeRects = new List<Rect>(_blockWorkspace.Nodes.Count);
            var localStackNodeRects = new List<Rect>(_blockWorkspace.ExecutableNodes.Count);
            _lastBlockStackNodeRects.Clear();
            float zoom = Mathf.Clamp(_blockWorkspace?.CanvasZoom ?? 1f, 0.45f, 1.85f);
            AutomationBlockCanvasPosition pan = _blockWorkspace?.CanvasPan ?? new AutomationBlockCanvasPosition(0f, 0f);
            float blockWidth = Mathf.Clamp(
                canvas.width - EsuHudLayout.Scale(140f),
                EsuHudLayout.Scale(360f),
                EsuHudLayout.Scale(760f)) * zoom;
            float x = Mathf.Clamp(
                canvas.width * 0.38f,
                EsuHudLayout.Scale(140f),
                Mathf.Max(EsuHudLayout.Scale(18f), canvas.width - blockWidth - EsuHudLayout.Scale(24f))) + pan.X;
            float y = EsuHudLayout.Scale(42f) + pan.Y;
            Rect start = new Rect(x, y, Mathf.Min(blockWidth, EsuHudLayout.Scale(190f) * zoom), EsuHudLayout.Scale(58f) * zoom);

            GUI.BeginGroup(canvas);
            try
            {
                DrawTinkercadBlockShape(
                    start,
                    BlockCategoryColor(AutomationBlockCategory.Control),
                    "on start",
                    "build",
                    selected: false,
                    hat: true,
                    uiScale: zoom);

                y = start.yMax + EsuHudLayout.Scale(42f) * zoom;
                AutomationBlockNode[] executableNodes = _blockWorkspace.ExecutableNodes.ToArray();
                float stackHeight = Mathf.Max(
                    EsuHudLayout.Scale(132f) * zoom,
                    executableNodes.Sum(node => TinkercadBlockHeight(node) * zoom) + EsuHudLayout.Scale(86f) * zoom);
                Rect forever = new Rect(x, y, blockWidth, stackHeight);
                _lastBlockStackRect = OffsetRect(forever, canvas.x, canvas.y);
                DrawTinkercadBlockShape(
                    forever,
                    BlockCategoryColor(AutomationBlockCategory.Control),
                    "forever",
                    "time",
                    selected: false,
                    hat: true,
                    uiScale: zoom);

                float blockX = forever.x + EsuHudLayout.Scale(22f) * zoom;
                float blockY = forever.y + EsuHudLayout.Scale(54f) * zoom;
                float childWidth = Mathf.Max(EsuHudLayout.Scale(220f) * zoom, forever.width - EsuHudLayout.Scale(44f) * zoom);
                var blockRectsByNodeId = new Dictionary<string, Rect>();
                for (int index = 0; index < executableNodes.Length; index++)
                {
                    AutomationBlockNode node = executableNodes[index];
                    float height = TinkercadBlockHeight(node) * zoom;
                    Rect rect = new Rect(blockX, blockY, childWidth, height);
                    localStackNodeRects.Add(rect);
                    Rect screenRect = OffsetRect(rect, canvas.x, canvas.y);
                    screenNodeRects.Add(screenRect);
                    _lastBlockStackNodeRects.Add(screenRect);
                    blockRectsByNodeId[node.Id] = rect;
                    blockY += height + EsuHudLayout.Scale(8f) * zoom;
                }

                AutomationBlockNode[] looseNodes = _blockWorkspace.Nodes
                    .Where(node => node != null && !node.SnappedToStack)
                    .OrderBy(node => node.CanvasOrder)
                    .ToArray();
                foreach (AutomationBlockNode node in looseNodes)
                {
                    float height = TinkercadBlockHeight(node) * zoom;
                    Rect rect = new Rect(
                        pan.X + node.CanvasPosition.X * zoom,
                        pan.Y + node.CanvasPosition.Y * zoom,
                        childWidth,
                        height);
                    screenNodeRects.Add(OffsetRect(rect, canvas.x, canvas.y));
                    blockRectsByNodeId[node.Id] = rect;
                }

                DrawTinkercadWorkspaceLinks(blockRectsByNodeId);
                foreach (AutomationBlockNode node in executableNodes)
                {
                    if (blockRectsByNodeId.TryGetValue(node.Id, out Rect rect))
                        DrawTinkercadWorkspaceBlock(rect, node, LocalBlocksCanvasFlyoutRect(canvas));
                }

                foreach (AutomationBlockNode node in looseNodes)
                {
                    if (blockRectsByNodeId.TryGetValue(node.Id, out Rect rect))
                        DrawTinkercadWorkspaceBlock(rect, node, LocalBlocksCanvasFlyoutRect(canvas));
                }

                if (executableNodes.Length == 0 && looseNodes.Length == 0)
                {
                    GUI.Label(
                        new Rect(canvas.width * 0.5f - EsuHudLayout.Scale(160f), EsuHudLayout.Scale(48f), EsuHudLayout.Scale(320f), EsuHudLayout.Scale(42f)),
                        "Drag blocks anywhere on the grid. Drop near forever to apply them.",
                        DecorationEditorTheme.MiniWrap);
                }

                DrawBlockDropIndicator(localStackNodeRects, new Rect(0f, 0f, canvas.width, canvas.height));
            }
            finally
            {
                GUI.EndGroup();
            }

            return screenNodeRects;
        }

        private List<Rect> DrawNativeExactProgram(Rect canvas)
        {
            var screenNodeRects = new List<Rect>(_blockWorkspace?.Nodes.Count ?? 0);
            _lastBlockStackNodeRects.Clear();
            _lastBlockStackRect = Rect.zero;
            if (_blockWorkspace == null)
                return screenNodeRects;

            float zoom = Mathf.Clamp(_blockWorkspace.CanvasZoom, 0.45f, 1.85f);
            float layoutScale = NativeExactDrawingScale();
            AutomationBlockCanvasPosition pan = _blockWorkspace.CanvasPan;
            AutomationBlockNode[] nodes = _blockWorkspace.Nodes
                .Where(node => node != null)
                .OrderBy(node => node.CanvasOrder)
                .ToArray();
            var blockRectsByNodeId = new Dictionary<string, Rect>();

            GUI.BeginGroup(canvas);
            try
            {
                Rect localCanvas = new Rect(0f, 0f, canvas.width, canvas.height);
                DrawBreadboardCanvasGrid(localCanvas);
                foreach (AutomationBlockNode node in nodes)
                {
                    Rect rect = new Rect(
                        pan.X + node.CanvasPosition.X * zoom,
                        pan.Y + node.CanvasPosition.Y * zoom,
                        NativeExactNodeWidth(node) * layoutScale * zoom,
                        NativeExactNodeHeight(node) * layoutScale * zoom);
                    blockRectsByNodeId[node.Id] = rect;
                    screenNodeRects.Add(OffsetRect(rect, canvas.x, canvas.y));
                }

                DrawTinkercadWorkspaceLinks(blockRectsByNodeId);
                foreach (AutomationBlockNode node in nodes)
                {
                    if (blockRectsByNodeId.TryGetValue(node.Id, out Rect rect))
                        DrawNativeExactWorkspaceBlock(rect, node, LocalBlocksCanvasFlyoutRect(canvas));
                }

                if (nodes.Length == 0)
                {
                    GUI.Label(
                        new Rect(
                            EsuHudLayout.Scale(18f),
                            EsuHudLayout.Scale(18f),
                            Mathf.Max(1f, canvas.width - EsuHudLayout.Scale(36f)),
                            EsuHudLayout.Scale(48f)),
                        "Native exact import found an empty Breadboard graph. Use Advanced Graph or Refresh from native after adding vanilla components.",
                        DecorationEditorTheme.MiniWrap);
                }
            }
            finally
            {
                GUI.EndGroup();
            }

            return screenNodeRects;
        }

        private void DrawNativeExactWorkspaceBlock(
            Rect rect,
            AutomationBlockNode node,
            Rect inputBlocker)
        {
            if (node == null)
                return;

            Event current = Event.current;
            bool inputBlocked = inputBlocker.width > 1f &&
                                current != null &&
                                inputBlocker.Contains(current.mousePosition);
            bool selected = string.Equals(_blockWorkspace.SelectedNodeId, node.Id, StringComparison.Ordinal);
            float uiScale = NativeExactBlockUiScale(rect, node);
            DrawTinkercadBlockShape(
                rect,
                BlockCategoryColor(node.Category),
                NativeExactBlockTitle(node),
                node.IconKey,
                selected,
                hat: false,
                badge: node.NativeImported ? "native exact" : "native wrapper",
                uiScale: uiScale);
            if (!inputBlocked)
                DrawNativeExactBlockDetails(rect, node, uiScale);
            DrawTinkercadBlockPorts(rect, node, uiScale);
            if (!inputBlocked)
                HandleWorkspaceBlockInput(rect, node);
        }

        private void DrawNativeExactBlockDetails(
            Rect rect,
            AutomationBlockNode node,
            float uiScale)
        {
            float pad = EsuHudLayout.Scale(8f) * uiScale;
            float rowHeight = Mathf.Max(EsuHudLayout.Scale(12f), EsuHudLayout.Scale(18f) * uiScale);
            Rect row = new Rect(
                rect.x + pad,
                rect.y + EsuHudLayout.Scale(28f) * uiScale,
                Mathf.Max(1f, rect.width - pad * 2f),
                rowHeight);
            string target = NativeExactTargetLine(node);
            if (!string.IsNullOrWhiteSpace(target))
            {
                DrawFilledRect(row, new Color(0f, 0f, 0f, 0.17f));
                DrawFittedSingleLineLabel(
                    new Rect(row.x + pad, row.y, Mathf.Max(1f, row.width - pad * 2f), row.height),
                    target,
                    TinkercadBlockTextStyle(TextAnchor.MiddleLeft),
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(7));
            }

            string settings = string.IsNullOrWhiteSpace(node.NativeSettingsSummary)
                ? node.NativeComponentTypeName
                : node.NativeSettingsSummary;
            if (string.IsNullOrWhiteSpace(settings))
                return;

            Rect settingsRow = new Rect(row.x, row.yMax + EsuHudLayout.Scale(4f) * uiScale, row.width, rowHeight);
            DrawFittedSingleLineLabel(
                settingsRow,
                settings,
                SingleLineRowStyle(DecorationEditorTheme.Mini),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(7));
        }

        private static Rect OffsetRect(Rect rect, float x, float y) =>
            new Rect(rect.x + x, rect.y + y, rect.width, rect.height);

        private Rect LocalBlocksCanvasFlyoutRect(Rect canvas)
        {
            Rect flyout = BlocksCanvasFlyoutRect(canvas);
            if (flyout.width <= 1f || flyout.height <= 1f)
                return Rect.zero;

            return new Rect(
                flyout.x - canvas.x,
                flyout.y - canvas.y,
                flyout.width,
                flyout.height);
        }

        private void DrawTinkercadWorkspaceBlock(
            Rect rect,
            AutomationBlockNode node,
            Rect inputBlocker)
        {
            if (node == null)
                return;

            Event current = Event.current;
            bool inputBlocked = inputBlocker.width > 1f &&
                                current != null &&
                                inputBlocker.Contains(current.mousePosition);
            bool selected = string.Equals(_blockWorkspace.SelectedNodeId, node.Id, StringComparison.Ordinal);
            float uiScale = TinkercadBlockUiScale(rect, node);
            DrawTinkercadBlockShape(
                rect,
                BlockCategoryColor(node.Category),
                BlockNodeTitle(node),
                node.IconKey,
                selected,
                node.Kind == AutomationBlockKind.WhenIf,
                node.SnappedToStack ? BlockNodeCompatibilityLabel(node) : "loose / not applied",
                uiScale);
            if (!inputBlocked)
                DrawTinkercadBlockControls(rect, node, uiScale);
            DrawTinkercadBlockPorts(rect, node, uiScale);
            if (!inputBlocked)
                HandleWorkspaceBlockInput(rect, node);
        }

        private void DrawTinkercadWorkspaceLinks(Dictionary<string, Rect> blockRectsByNodeId)
        {
            if (_blockWorkspace == null || blockRectsByNodeId == null)
                return;

            foreach (AutomationBlockLink link in _blockWorkspace.Links)
            {
                AutomationBlockPort from = _blockWorkspace.Ports.FirstOrDefault(port =>
                    string.Equals(port.Id, link.FromPortId, StringComparison.Ordinal));
                AutomationBlockPort to = _blockWorkspace.Ports.FirstOrDefault(port =>
                    string.Equals(port.Id, link.ToPortId, StringComparison.Ordinal));
                if (from == null ||
                    to == null ||
                    !blockRectsByNodeId.TryGetValue(from.NodeId, out Rect fromRect) ||
                    !blockRectsByNodeId.TryGetValue(to.NodeId, out Rect toRect))
                {
                    continue;
                }

                Vector2 start = TinkercadBlockPortCenter(fromRect, from);
                Vector2 end = TinkercadBlockPortCenter(toRect, to);
                Color lineColor = new Color(0.05f, 0.9f, 1f, 0.68f);
                DrawGuiLine(start, end, lineColor, EsuHudLayout.Scale(2f));
                Vector2 arrow = Vector2.Lerp(start, end, 0.78f);
                DrawGuiRect(
                    new Rect(
                        arrow.x - EsuHudLayout.Scale(4f),
                        arrow.y - EsuHudLayout.Scale(4f),
                        EsuHudLayout.Scale(8f),
                        EsuHudLayout.Scale(8f)),
                    new Color(1f, 0.86f, 0.24f, 0.9f));
                Rect labelRect = new Rect(
                    Mathf.Min(start.x, end.x) + EsuHudLayout.Scale(8f),
                    Mathf.Min(start.y, end.y) + EsuHudLayout.Scale(4f),
                    Mathf.Abs(end.x - start.x) + EsuHudLayout.Scale(64f),
                    EsuHudLayout.Scale(16f));
                DrawFittedSingleLineLabel(
                    labelRect,
                    from.Name + " -> " + to.Name,
                    SingleLineRowStyle(DecorationEditorTheme.Mini),
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(8));
            }
        }

        private void DrawTinkercadBlockPorts(Rect rect, AutomationBlockNode node, float uiScale)
        {
            if (_blockWorkspace == null || node == null)
                return;

            DrawTinkercadBlockPorts(rect, node, AutomationBlockPortDirection.Input, uiScale);
            DrawTinkercadBlockPorts(rect, node, AutomationBlockPortDirection.Output, uiScale);
        }

        private void DrawTinkercadBlockPorts(
            Rect rect,
            AutomationBlockNode node,
            AutomationBlockPortDirection direction,
            float uiScale)
        {
            AutomationBlockPort[] ports = _blockWorkspace.Ports
                .Where(port =>
                    string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                    port.Direction == direction)
                .ToArray();
            if (ports.Length == 0)
                return;

            for (int index = 0; index < ports.Length; index++)
            {
                AutomationBlockPort port = ports[index];
                Rect nub = TinkercadBlockPortRect(rect, port, index, ports.Length, uiScale);
                bool connected = BlockPortHasConnection(port);
                Color color = direction == AutomationBlockPortDirection.Input
                    ? connected
                        ? new Color(0.05f, 0.9f, 1f, 0.96f)
                        : new Color(0.7f, 0.86f, 0.9f, 0.86f)
                    : connected
                        ? new Color(1f, 0.86f, 0.24f, 0.96f)
                        : new Color(1f, 0.68f, 0.24f, 0.86f);
                DrawGuiRect(nub, color);
                DrawGuiBorder(nub, new Color(0f, 0f, 0f, 0.28f), EsuHudLayout.Scale(1f));

                Rect labelRect = direction == AutomationBlockPortDirection.Input
                    ? new Rect(
                        nub.xMax + EsuHudLayout.Scale(4f) * uiScale,
                        nub.y - EsuHudLayout.Scale(3f) * uiScale,
                        EsuHudLayout.Scale(78f) * uiScale,
                        EsuHudLayout.Scale(16f) * uiScale)
                    : new Rect(
                        rect.xMax - EsuHudLayout.Scale(88f) * uiScale,
                        nub.y - EsuHudLayout.Scale(3f) * uiScale,
                        EsuHudLayout.Scale(78f) * uiScale,
                        EsuHudLayout.Scale(16f) * uiScale);
                DrawFittedSingleLineLabel(
                    labelRect,
                    port.Name,
                    TinkercadPortLabelStyle(direction == AutomationBlockPortDirection.Input
                        ? TextAnchor.MiddleLeft
                        : TextAnchor.MiddleRight),
                    direction == AutomationBlockPortDirection.Input
                        ? TextAnchor.MiddleLeft
                        : TextAnchor.MiddleRight,
                    EsuHudLayout.FontSize(7));
                EsuCursorTooltip.Register(
                    nub,
                    (direction == AutomationBlockPortDirection.Input ? "Input" : "Output") +
                    " port: " +
                    port.Name +
                    (connected ? ". Linked in the ESU workspace preview." : ". No preview link yet."));
            }
        }

        private bool BlockPortHasConnection(AutomationBlockPort port)
        {
            if (_blockWorkspace == null || port == null)
                return false;

            return _blockWorkspace.Links.Any(link =>
                string.Equals(link.FromPortId, port.Id, StringComparison.Ordinal) ||
                string.Equals(link.ToPortId, port.Id, StringComparison.Ordinal));
        }

        private Vector2 TinkercadBlockPortCenter(Rect rect, AutomationBlockPort port)
        {
            if (_blockWorkspace == null || port == null)
                return rect.center;

            AutomationBlockPort[] ports = _blockWorkspace.Ports
                .Where(item =>
                    string.Equals(item.NodeId, port.NodeId, StringComparison.Ordinal) &&
                    item.Direction == port.Direction)
                .ToArray();
            int index = Array.FindIndex(ports, item => string.Equals(item.Id, port.Id, StringComparison.Ordinal));
            if (index < 0)
                index = 0;

            AutomationBlockNode node = _blockWorkspace.Nodes.FirstOrDefault(item =>
                string.Equals(item.Id, port.NodeId, StringComparison.Ordinal));
            return TinkercadBlockPortRect(rect, port, index, Math.Max(1, ports.Length), TinkercadBlockUiScale(rect, node)).center;
        }

        private static Rect TinkercadBlockPortRect(
            Rect rect,
            AutomationBlockPort port,
            int index,
            int count,
            float uiScale)
        {
            float size = Mathf.Max(EsuHudLayout.Scale(5f), EsuHudLayout.Scale(10f) * uiScale);
            float top = rect.y + EsuHudLayout.Scale(26f) * uiScale;
            float bottom = rect.yMax - EsuHudLayout.Scale(12f) * uiScale;
            float t = count <= 1 ? 0.5f : Mathf.Clamp01((index + 0.5f) / count);
            float y = Mathf.Lerp(top, bottom, t);
            bool output = port != null && port.Direction == AutomationBlockPortDirection.Output;
            float x = output ? rect.xMax - size * 0.5f : rect.x - size * 0.5f;
            return new Rect(x, y - size * 0.5f, size, size);
        }

        private void DrawTinkercadBlockControls(Rect rect, AutomationBlockNode node, float uiScale)
        {
            float pad = EsuHudLayout.Scale(10f) * uiScale;
            Rect control = new Rect(
                rect.x + pad,
                rect.y + EsuHudLayout.Scale(26f) * uiScale,
                rect.width - pad * 2f,
                Mathf.Max(EsuHudLayout.Scale(9f), rect.height - EsuHudLayout.Scale(32f) * uiScale));

            if (node.Kind == AutomationBlockKind.ReadTarget ||
                node.Kind == AutomationBlockKind.SetTarget)
            {
                float rowHeight = Mathf.Max(EsuHudLayout.Scale(14f), EsuHudLayout.Scale(28f) * uiScale);
                float gap = EsuHudLayout.Scale(5f) * uiScale;
                Rect targetRow = new Rect(control.x, control.y, control.width, rowHeight);
                Rect propertyRow = new Rect(control.x, targetRow.yMax + gap, control.width, rowHeight);
                Rect valueRow = new Rect(control.x, propertyRow.yMax + gap, control.width, rowHeight);
                DrawTinkercadTargetPropertyRow(targetRow, node, target: true, uiScale);
                DrawTinkercadTargetPropertyRow(propertyRow, node, target: false, uiScale);
                DrawTinkercadTargetValueRow(valueRow, node, uiScale);
                return;
            }

            if (node.Kind == AutomationBlockKind.Compare)
            {
                float rowHeight = Mathf.Max(EsuHudLayout.Scale(14f), EsuHudLayout.Scale(30f) * uiScale);
                Rect op = new Rect(control.x, control.y, EsuHudLayout.Scale(68f) * uiScale, rowHeight);
                if (AutomationGUIButton(
                        op,
                        new GUIContent(CompareOperatorLabel(node.Operator), "Cycle comparison operator."),
                        DecorationEditorTheme.Button))
                    CycleAutomationCompareOperator(node);
                DrawTinkercadFloatStepper(
                    new Rect(op.xMax + EsuHudLayout.Scale(8f) * uiScale, control.y, control.width - op.width - EsuHudLayout.Scale(8f) * uiScale, rowHeight),
                    node.NumericValue,
                    0.1f,
                    value =>
                    {
                        node.NumericValue = value;
                        InvalidateEsuBlockPlan();
                    });
                return;
            }

            if (node.Kind == AutomationBlockKind.MathScale ||
                node.Kind == AutomationBlockKind.Constant ||
                node.Kind == AutomationBlockKind.Delay)
            {
                DrawTinkercadFloatStepper(
                    new Rect(control.x, control.y, Mathf.Min(control.width, EsuHudLayout.Scale(260f) * uiScale), Mathf.Max(EsuHudLayout.Scale(14f), EsuHudLayout.Scale(30f) * uiScale)),
                    node.NumericValue,
                    node.Kind == AutomationBlockKind.Delay ? 0.05f : 0.1f,
                    value =>
                    {
                        node.NumericValue = node.Kind == AutomationBlockKind.Delay ? Mathf.Max(0f, value) : value;
                        InvalidateEsuBlockPlan();
                    });
                return;
            }

            if (node.Kind == AutomationBlockKind.MathEvaluator)
            {
                string next = GUI.TextField(control, node.Expression ?? string.Empty, DecorationEditorTheme.TextField);
                if (!string.Equals(next, node.Expression ?? string.Empty, StringComparison.Ordinal))
                {
                    node.Expression = next;
                    InvalidateEsuBlockPlan();
                }
                return;
            }

            if (node.Kind == AutomationBlockKind.Switch)
            {
                DrawTinkercadFloatStepper(
                    new Rect(control.x, control.y, Mathf.Min(control.width, EsuHudLayout.Scale(260f) * uiScale), Mathf.Max(EsuHudLayout.Scale(14f), EsuHudLayout.Scale(30f) * uiScale)),
                    node.SecondaryNumericValue,
                    0.1f,
                    value =>
                    {
                        node.SecondaryNumericValue = value;
                        InvalidateEsuBlockPlan();
                    });
                return;
            }

            if (node.Kind == AutomationBlockKind.NativeComponent)
            {
                string nativeText = string.IsNullOrWhiteSpace(node.NativeSettingsSummary)
                    ? string.IsNullOrWhiteSpace(node.NativeComponentTypeName)
                        ? "native component"
                        : node.NativeComponentTypeName
                    : node.NativeSettingsSummary;
                DrawFittedSingleLineLabel(
                    control,
                    nativeText,
                    TinkercadBlockTextStyle(TextAnchor.MiddleLeft),
                    TextAnchor.MiddleLeft,
                    EsuHudLayout.FontSize(8));
                return;
            }

            if (node.Kind == AutomationBlockKind.Comment)
            {
                string next = GUI.TextField(control, node.Comment ?? string.Empty, DecorationEditorTheme.TextField);
                if (!string.Equals(next, node.Comment ?? string.Empty, StringComparison.Ordinal))
                {
                    node.Comment = next;
                    InvalidateEsuBlockPlan();
                }
            }
        }

        private void DrawTinkercadTargetPropertyRow(
            Rect row,
            AutomationBlockNode node,
            bool target,
            float uiScale)
        {
            float labelWidth = Mathf.Clamp(row.width * 0.22f, EsuHudLayout.Scale(34f) * uiScale, EsuHudLayout.Scale(78f) * uiScale);
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect pillRect = new Rect(
                labelRect.xMax + EsuHudLayout.Scale(5f) * uiScale,
                row.y,
                Mathf.Max(0f, row.xMax - labelRect.xMax - EsuHudLayout.Scale(5f) * uiScale),
                row.height);
            DrawFittedSingleLineLabel(
                labelRect,
                target
                    ? node.Kind == AutomationBlockKind.SetTarget ? "write" : "read"
                    : "property",
                TinkercadBlockTextStyle(TextAnchor.MiddleLeft),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));

            AutomationTarget nodeTarget = _blockWorkspace.TargetForNode(node);
            if (target)
            {
                DrawAutomationPickerPill(
                    pillRect,
                    new GUIContent(
                        string.IsNullOrWhiteSpace(node.TargetLabel) ? "choose linked target" : node.TargetLabel,
                        DecorationEditorIconCatalog.Get(AutomationTargetIconKey(nodeTarget)),
                        "Choose a linked " + LinkDirectionLabel(node.LinkDirection).ToLowerInvariant() + " target for this ESU Block."),
                    DecorationEditorTheme.Button);
                if (PrimaryMouseDownIn(row))
                {
                    OpenAutomationTargetPicker(node);
                    Event.current.Use();
                }
                RegisterAutomationTargetPreview(pillRect, nodeTarget, node.Label);
                return;
            }

            string propertyLabel = node.PropertySelection == null || node.PropertySelection.IsClear
                ? AutomationBlockPropertyLabel(node)
                : node.PropertySelection.Label;
            DrawAutomationPickerPill(
                pillRect,
                new GUIContent(
                    propertyLabel,
                    DecorationEditorIconCatalog.Get("filter"),
                    "Choose the native Generic " + (node.Kind == AutomationBlockKind.ReadTarget ? "Getter" : "Setter") + " property used by Check and Apply."),
                DecorationEditorTheme.Button);
            if (PrimaryMouseDownIn(row))
            {
                OpenAutomationPropertyPicker(node);
                Event.current.Use();
            }
        }

        private void DrawTinkercadTargetValueRow(
            Rect row,
            AutomationBlockNode node,
            float uiScale)
        {
            float labelWidth = Mathf.Clamp(row.width * 0.22f, EsuHudLayout.Scale(34f) * uiScale, EsuHudLayout.Scale(78f) * uiScale);
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect valueRect = new Rect(
                labelRect.xMax + EsuHudLayout.Scale(5f) * uiScale,
                row.y,
                Mathf.Max(0f, row.xMax - labelRect.xMax - EsuHudLayout.Scale(5f) * uiScale),
                row.height);
            DrawFittedSingleLineLabel(
                labelRect,
                "current",
                TinkercadBlockTextStyle(TextAnchor.MiddleLeft),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));

            AutomationBlockLiveValueSnapshot snapshot = AutomationBlockLiveValueFor(node);
            GUI.Label(valueRect, GUIContent.none, DecorationEditorTheme.Row);
            DrawFittedSingleLineLabel(
                new Rect(
                    valueRect.x + EsuHudLayout.Scale(8f) * uiScale,
                    valueRect.y,
                    Mathf.Max(0f, valueRect.width - EsuHudLayout.Scale(16f) * uiScale),
                    valueRect.height),
                snapshot.Text,
                TinkercadValueStyle(),
                TextAnchor.MiddleCenter,
                EsuHudLayout.FontSize(8));
            EsuCursorTooltip.Register(valueRect, snapshot.Tooltip);
        }

        private AutomationBlockLiveValueSnapshot AutomationBlockLiveValueFor(AutomationBlockNode node)
        {
            if (node == null)
            {
                return new AutomationBlockLiveValueSnapshot(
                    "select block",
                    "Select a Read or Set block to inspect its current target value.",
                    0f);
            }

            AutomationTarget target = _blockWorkspace?.TargetForNode(node);
            if (target == null)
            {
                return new AutomationBlockLiveValueSnapshot(
                    "choose linked target",
                    "Pick the linked target before a current value can be read.",
                    0f);
            }

            if (node.PropertySelection == null || node.PropertySelection.IsClear)
            {
                return new AutomationBlockLiveValueSnapshot(
                    "choose property",
                    "Pick the native Generic " +
                    (node.Kind == AutomationBlockKind.ReadTarget ? "Getter" : "Setter") +
                    " property before a current value can be read.",
                    0f);
            }

            string key = AutomationBlockLiveValueCacheKey(node, target);
            float now = Time.realtimeSinceStartup;
            if (_blockLiveValueCache.TryGetValue(key, out AutomationBlockLiveValueSnapshot cached) &&
                cached.RefreshAfter > now)
            {
                return cached;
            }

            AutomationBlockLiveValueSnapshot snapshot;
            if (AutomationBreadboardInspector.TryReadSelectedTargetPropertyValue(
                    target,
                    node.PropertySelection,
                    out string value,
                    out string reason))
            {
                snapshot = new AutomationBlockLiveValueSnapshot(
                    value,
                    reason,
                    now + 0.35f);
            }
            else
            {
                snapshot = new AutomationBlockLiveValueSnapshot(
                    "unavailable",
                    reason ?? "The selected native property did not expose a current value.",
                    now + 1.25f);
            }

            _blockLiveValueCache[key] = snapshot;
            if (_blockLiveValueCache.Count > 64)
                _blockLiveValueCache.Clear();
            return snapshot;
        }

        private static string AutomationBlockLiveValueCacheKey(
            AutomationBlockNode node,
            AutomationTarget target)
        {
            AutomationProxyPropertySelection selection = node?.PropertySelection;
            if (node == null || target == null || selection == null)
                return string.Empty;

            return node.Id + "|" +
                   node.Kind + "|" +
                   target.StableKey + "|" +
                   selection.IsGetter + "|" +
                   selection.IsGetterReadable + "|" +
                   selection.ReadableAttributeId.ToString(CultureInfo.InvariantCulture) + "|" +
                   selection.BlockPropertyId.ToString(CultureInfo.InvariantCulture) + "|" +
                   selection.BlockSetId.ToString(CultureInfo.InvariantCulture);
        }

        private static void DrawAutomationPickerPill(
            Rect rect,
            GUIContent content,
            GUIStyle style)
        {
            GUI.Label(rect, GUIContent.none, style);
            DrawAutomationButtonContent(rect, content, style);
            EsuCursorTooltip.Register(rect, content?.tooltip);
        }

        private bool PrimaryMouseDownIn(Rect rect)
        {
            Event current = Event.current;
            if (ShouldSuppressAutomationBackgroundDirectInput(current))
                return false;

            return current != null &&
                   current.type == EventType.MouseDown &&
                   current.button == 0 &&
                   rect.Contains(current.mousePosition);
        }

        private void DrawTinkercadFloatStepper(Rect rect, float value, float step, Action<float> apply)
        {
            float button = Mathf.Min(EsuHudLayout.Scale(28f), Mathf.Max(EsuHudLayout.Scale(9f), rect.height));
            Rect minus = new Rect(rect.x, rect.y, button, rect.height);
            float gap = Mathf.Min(EsuHudLayout.Scale(4f), Mathf.Max(1f, rect.height * 0.18f));
            Rect plus = new Rect(rect.xMax - button, rect.y, button, rect.height);
            Rect valueRect = new Rect(minus.xMax + gap, rect.y, Mathf.Max(1f, plus.x - minus.xMax - gap * 2f), rect.height);
            if (AutomationGUIButton(minus, new GUIContent("-", "Decrease value."), DecorationEditorTheme.Button))
                apply?.Invoke(value - Mathf.Max(0.001f, step));
            DrawFittedSingleLineLabel(
                valueRect,
                value.ToString("0.###", CultureInfo.InvariantCulture),
                TinkercadValueStyle(),
                TextAnchor.MiddleCenter,
                EsuHudLayout.FontSize(8));
            if (AutomationGUIButton(plus, new GUIContent("+", "Increase value."), DecorationEditorTheme.Button))
                apply?.Invoke(value + Mathf.Max(0.001f, step));
        }

        private void HandleWorkspaceBlockInput(Rect rect, AutomationBlockNode node)
        {
            Event current = Event.current;
            if (current == null || node == null)
                return;
            if (ShouldSuppressAutomationBackgroundDirectInput(current))
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                CloseAutomationContextMenu();
                _blockWorkspace.Select(node.Id);
                _draggingWorkspaceBlock = true;
                _draggingPaletteBlock = false;
                _draggingNativePaletteBlock = false;
                _dragNativePaletteComponent = null;
                _dragWorkspaceBlockId = node.Id;
                _blockDropIndex = node.SnappedToStack ? BlockNodeIndex(node.Id) : -1;
                _blockDragMouseStart = current.mousePosition;
                _blockDragNodeOffset = current.mousePosition - rect.position;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 1 &&
                rect.Contains(current.mousePosition))
            {
                ClearAutomationBlockDrag();
                CloseAutomationPropertyPicker();
                OpenAutomationBlockContextMenu(node, GUIUtility.GUIToScreenPoint(current.mousePosition));
                AutomationInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private void HandleBlockCanvasDrop(Rect canvas, IReadOnlyList<Rect> nodeRects)
        {
            Event current = Event.current;
            if (current == null ||
                (!_draggingPaletteBlock && !_draggingNativePaletteBlock && !_draggingWorkspaceBlock))
            {
                return;
            }
            if (ShouldSuppressAutomationBackgroundDirectInput(current))
                return;

            Rect flyout = BlocksCanvasFlyoutRect(canvas);
            if (flyout.width > 1f && flyout.Contains(current.mousePosition))
            {
                if (current.type == EventType.MouseUp && current.button == 0)
                {
                    ClearAutomationBlockDrag();
                    current.Use();
                }
                return;
            }

            bool overCanvas = canvas.Contains(current.mousePosition);
            bool canSnapToStack = _blockWorkspace?.Mode != AutomationBlockWorkspaceMode.NativeExact;
            bool snapToStack = canSnapToStack && overCanvas && IsMouseNearExecutableStack(current.mousePosition);
            if (snapToStack)
            {
                _blockDropIndex = BlockDropIndexForMouse(_lastBlockStackNodeRects, current.mousePosition);
            }
            else
            {
                _blockDropIndex = -1;
            }

            if (current.type != EventType.MouseUp || current.button != 0)
                return;

            if (overCanvas)
            {
                if (snapToStack)
                {
                    if (_draggingNativePaletteBlock)
                    {
                        AddNativeAutomationBlock(_dragNativePaletteComponent, _blockDropIndex);
                    }
                    else if (_draggingPaletteBlock)
                    {
                        AddAutomationBlock(_dragPaletteBlockKind, _blockDropIndex);
                    }
                    else if (_draggingWorkspaceBlock)
                    {
                        int targetIndex = _blockDropIndex;
                        int currentIndex = BlockNodeIndex(_dragWorkspaceBlockId);
                        if (currentIndex >= 0 && currentIndex < targetIndex)
                            targetIndex--;
                        if (_blockWorkspace.MoveNodeToIndex(_dragWorkspaceBlockId, targetIndex))
                        {
                            InvalidateEsuBlockPlan();
                            _status = "Snapped ESU Block into the executable stack.";
                            AutomationUiSound.PlaySnap();
                        }
                    }
                }
                else
                {
                    AutomationBlockCanvasPosition dropPosition = CanvasPositionForMouse(canvas, current.mousePosition, _blockDragNodeOffset);
                    if (_draggingNativePaletteBlock)
                    {
                        AddNativeAutomationBlockAt(_dragNativePaletteComponent, dropPosition);
                    }
                    else if (_draggingPaletteBlock)
                    {
                        AddAutomationBlockAt(_dragPaletteBlockKind, dropPosition);
                    }
                    else if (_draggingWorkspaceBlock)
                    {
                        AutomationBlockNode draggedNode = _blockWorkspace?.Nodes.FirstOrDefault(node =>
                            string.Equals(node.Id, _dragWorkspaceBlockId, StringComparison.Ordinal));
                        if (TryMoveNativeExactWorkspaceBlock(
                                draggedNode,
                                dropPosition,
                                out bool nativeMoveHandled,
                                out string nativeMoveMessage))
                        {
                            RefreshNativeExactWorkspaceAfterNativeCommand(nativeMoveMessage);
                            AutomationUiSound.PlayAdd();
                        }
                        else if (nativeMoveHandled)
                        {
                            _status = nativeMoveMessage ?? "Could not move native Breadboard component.";
                        }
                        else if (_blockWorkspace.MoveNodeToCanvas(_dragWorkspaceBlockId, dropPosition))
                        {
                            InvalidateEsuBlockPlan();
                            _status = "Moved ESU Block as loose grid metadata.";
                            AutomationUiSound.PlayAdd();
                        }
                    }
                }
            }

            ClearAutomationBlockDrag();
            current.Use();
        }

        private bool IsMouseNearExecutableStack(Vector2 mouse)
        {
            if (_lastBlockStackRect.width <= 1f || _lastBlockStackRect.height <= 1f)
                return false;

            Rect expanded = ExpandRect(_lastBlockStackRect, EsuHudLayout.Scale(56f));
            return expanded.Contains(mouse);
        }

        private AutomationBlockCanvasPosition CanvasPositionForMouse(
            Rect canvas,
            Vector2 mouse,
            Vector2 dragOffset)
        {
            float zoom = Mathf.Clamp(_blockWorkspace?.CanvasZoom ?? 1f, 0.45f, 1.85f);
            AutomationBlockCanvasPosition pan = _blockWorkspace?.CanvasPan ?? new AutomationBlockCanvasPosition(0f, 0f);
            Vector2 local = mouse - new Vector2(canvas.x, canvas.y) - dragOffset;
            return new AutomationBlockCanvasPosition(
                (local.x - pan.X) / zoom,
                (local.y - pan.Y) / zoom);
        }

        private void HandleBlockCanvasNavigation(Rect canvas, IReadOnlyList<Rect> nodeRects)
        {
            Event current = Event.current;
            if (current == null || _blockWorkspace == null)
                return;
            if (ShouldSuppressAutomationBackgroundDirectInput(current))
                return;

            Rect flyout = BlocksCanvasFlyoutRect(canvas);
            if (flyout.width > 1f && flyout.Contains(current.mousePosition))
                return;

            bool overCanvas = canvas.Contains(current.mousePosition);
            bool overNode = nodeRects != null && nodeRects.Any(rect => rect.Contains(current.mousePosition));
            if (overCanvas && current.type == EventType.ScrollWheel)
            {
                float nextZoom = _blockWorkspace.CanvasZoom - current.delta.y * 0.055f;
                _blockWorkspace.SetCanvasZoom(nextZoom);
                AutomationInputScope.ClaimMouseWheelInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                overCanvas &&
                !overNode &&
                !_draggingPaletteBlock &&
                !_draggingNativePaletteBlock &&
                !_draggingWorkspaceBlock)
            {
                _panningBlockCanvas = true;
                _blockCanvasPanMouseStart = current.mousePosition;
                _blockCanvasPanStart = _blockWorkspace.CanvasPan;
                current.Use();
                return;
            }

            if (_panningBlockCanvas &&
                current.type == EventType.MouseDrag &&
                current.button == 0)
            {
                Vector2 delta = current.mousePosition - _blockCanvasPanMouseStart;
                _blockWorkspace.SetCanvasPan(
                    _blockCanvasPanStart.X + delta.x,
                    _blockCanvasPanStart.Y + delta.y);
                current.Use();
                return;
            }

            if (_panningBlockCanvas &&
                current.type == EventType.MouseUp &&
                current.button == 0)
            {
                _panningBlockCanvas = false;
                current.Use();
            }
        }

        private void DrawBlockDropIndicator(IReadOnlyList<Rect> nodeRects, Rect canvas)
        {
            if ((!_draggingPaletteBlock && !_draggingNativePaletteBlock && !_draggingWorkspaceBlock) ||
                _blockDropIndex < 0)
            {
                return;
            }

            Rect line;
            if (nodeRects == null || nodeRects.Count == 0)
            {
                line = new Rect(
                    canvas.x + EsuHudLayout.Scale(40f),
                    canvas.y + EsuHudLayout.Scale(150f),
                    Mathf.Max(1f, canvas.width - EsuHudLayout.Scale(80f)),
                    EsuHudLayout.Scale(3f));
            }
            else if (_blockDropIndex >= nodeRects.Count)
            {
                Rect last = nodeRects[nodeRects.Count - 1];
                line = new Rect(last.x, last.yMax + EsuHudLayout.Scale(4f), last.width, EsuHudLayout.Scale(3f));
            }
            else
            {
                Rect next = nodeRects[_blockDropIndex];
                line = new Rect(next.x, next.y - EsuHudLayout.Scale(5f), next.width, EsuHudLayout.Scale(3f));
            }

            DrawFilledRect(line, DecorationEditorTheme.Cyan);
        }

        private int BlockDropIndexForMouse(IReadOnlyList<Rect> nodeRects, Vector2 mouse)
        {
            if (nodeRects == null || nodeRects.Count == 0)
                return 0;

            for (int index = 0; index < nodeRects.Count; index++)
            {
                if (mouse.y < nodeRects[index].center.y)
                    return index;
            }

            return nodeRects.Count;
        }

        private void DrawTinkercadBlockInspector()
        {
            DrawCompactIconHeader("Selected block", "focus", DecorationEditorTheme.SubHeader);
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            AutomationBlockNode node = _blockWorkspace.SelectedNode();
            if (node == null)
            {
                GUILayout.Label(
                    "Select a block on the canvas to edit its target, property, threshold/value, or stack position. Loose blocks stay on the grid until snapped near the forever stack.",
                    DecorationEditorTheme.MiniWrap);
            }
            else
            {
                AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionForNode(node);
                LabelRow("Type", node.Label);
                LabelRow("Category", AutomationBlockCategoryLabel(node.Category));
                LabelRow("Role", AutomationBlockAudienceLabel(definition.Audience));
                LabelRow("Ports", AutomationBlockPortSummary(definition));
                LabelRow("Compatibility", AutomationBlockCompatibilityLabel(definition.Compatibility));
                LabelRow("Validation", BlockNodeValidationLabel(node, definition));
                LabelRow("Template", node.PaletteTemplateId);
                if (node.Kind == AutomationBlockKind.NativeComponent && node.NativeImported)
                {
                    LabelRow("Native exact", "Imported component " + node.NativeComponentId.ToString(CultureInfo.InvariantCulture));
                    if (!string.IsNullOrWhiteSpace(node.NativeBlockTypeName))
                        LabelRow("Block type", node.NativeBlockTypeName);
                    if (!string.IsNullOrWhiteSpace(node.NativeBlockFilter))
                        LabelRow("Block filter", node.NativeBlockFilter);
                    if (!string.IsNullOrWhiteSpace(node.NativeSettingsSummary))
                        LabelRow("Settings", node.NativeSettingsSummary);
                }
                DecorationEditorTheme.Separator();
                DrawAutomationBlockNodeControls(node);
                DecorationEditorTheme.Separator();
                GUILayout.BeginHorizontal();
                if (AutomationGUILayoutButton(
                        new GUIContent("Up", DecorationEditorIconCatalog.Get("up"), "Move the selected ESU Block up."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                    MoveSelectedEsuBlock(-1);
                if (AutomationGUILayoutButton(
                        new GUIContent("Down", DecorationEditorIconCatalog.Get("down"), "Move the selected ESU Block down."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                    MoveSelectedEsuBlock(1);
                GUILayout.EndHorizontal();
                if (AutomationGUILayoutButton(
                        new GUIContent("Remove", DecorationEditorIconCatalog.Get("delete"), "Remove the selected ESU Block."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                    RemoveSelectedEsuBlock();
            }

            GUILayout.EndVertical();
        }

        private void DrawAutomationBlockDragGhost()
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.Repaint ||
                (!_draggingPaletteBlock && !_draggingNativePaletteBlock && !_draggingWorkspaceBlock))
            {
                return;
            }

            string text;
            string icon;
            Color color;
            if (_draggingNativePaletteBlock)
            {
                AutomationBlockDefinition definition = AutomationBlockCatalog.NativeDefinition(_dragNativePaletteComponent);
                text = definition.Label;
                icon = definition.IconKey;
                color = BlockCategoryColor(AutomationBlockCategory.Advanced);
            }
            else if (_draggingPaletteBlock)
            {
                text = PaletteBlockPreviewText(_dragPaletteBlockKind);
                icon = DefaultBlockIcon(_dragPaletteBlockKind);
                color = BlockCategoryColor(AutomationBlockWorkspace.CategoryFor(_dragPaletteBlockKind));
            }
            else
            {
                AutomationBlockNode node = _blockWorkspace?.Nodes.FirstOrDefault(item =>
                    string.Equals(item.Id, _dragWorkspaceBlockId, StringComparison.Ordinal));
                text = BlockNodeTitle(node);
                icon = node?.IconKey ?? "outliner";
                color = BlockCategoryColor(node?.Category ?? AutomationBlockCategory.Control);
            }

            Rect ghost = new Rect(
                current.mousePosition.x + EsuHudLayout.Scale(16f),
                current.mousePosition.y + EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(210f),
                EsuHudLayout.Scale(44f));
            DrawTinkercadBlockShape(ghost, color, text, icon, selected: true, hat: false);
        }

        private AutomationBlockNode AddAutomationBlock(AutomationBlockKind kind, int insertIndex)
        {
            EnsureBlockWorkspace();
            AutomationBlockNode node = insertIndex < 0
                ? _blockWorkspace.AddBlock(kind)
                : _blockWorkspace.AddBlock(kind, insertIndex);
            _blockLoweringPlan = null;
            _blockLoweringStatus = "Block stack changed; Check ESU Blocks again before applying.";
            _status = "Added " + PaletteBlockPreviewText(kind) + " ESU Block.";
            _automationNativeDirty = true;
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
            AutomationUiSound.PlaySnap();
            return node;
        }

        private AutomationBlockNode AddNativeAutomationBlock(
            AutomationBreadboardAvailableComponent component,
            int insertIndex)
        {
            if (component == null)
                return null;

            EnsureBlockWorkspace();
            AutomationBlockNode node = _blockWorkspace.AddNativeComponentBlock(component, insertIndex);
            InvalidateEsuBlockPlan();
            _status = "Added native Breadboard block " + node.NativeComponentLabel + ".";
            AutomationUiSound.PlaySnap();
            return node;
        }

        private AutomationBlockNode AddAutomationBlockAt(
            AutomationBlockKind kind,
            AutomationBlockCanvasPosition canvasPosition)
        {
            EnsureBlockWorkspace();
            AutomationBlockNode node = _blockWorkspace.AddBlockAt(kind, canvasPosition, snappedToStack: false);
            InvalidateEsuBlockPlan();
            _status = "Added loose " + PaletteBlockPreviewText(kind) + " block. Drop it near forever to apply it.";
            AutomationUiSound.PlayAdd();
            return node;
        }

        private AutomationBlockNode AddNativeAutomationBlockAt(
            AutomationBreadboardAvailableComponent component,
            AutomationBlockCanvasPosition canvasPosition)
        {
            if (component == null)
                return null;

            EnsureBlockWorkspace();
            AutomationBlockNode node = _blockWorkspace.AddNativeComponentBlockAt(component, canvasPosition, snappedToStack: false);
            InvalidateEsuBlockPlan();
            _status = "Added loose native Breadboard block " + node.NativeComponentLabel + ". Drop it near forever to apply it.";
            AutomationUiSound.PlayAdd();
            return node;
        }

        private void AddAutomationBlockForLink(
            AutomationBlockKind kind,
            AutomationLink link)
        {
            if (link?.Target == null)
                return;

            AutomationLinkDirection direction = kind == AutomationBlockKind.ReadTarget
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output;
            AutomationBlockNode node = AddAutomationBlock(kind, -1);
            if (node != null &&
                _blockWorkspace.SetNodeTarget(node.Id, link.Target, direction))
            {
                InvalidateEsuBlockPlan();
                _status = "Added " + node.Label + " for " + link.DirectionLabel.ToLowerInvariant() + " " +
                          link.TargetLabel + ". Choose its property before Check.";
            }
        }

        private static string AutomationBlockPropertyLabel(AutomationBlockNode node)
        {
            if (node == null)
                return "Choose property";

            if (node.PropertyBindingMode == AutomationProxyPropertyBindingMode.Explicit &&
                node.PropertySelection != null &&
                !node.PropertySelection.IsClear)
            {
                return node.PropertySelection.Label;
            }

            return "Choose property";
        }

        private static string LoweringPlanPropertyLabel(
            AutomationProxyPropertySelection selection,
            string fallback)
        {
            return selection != null &&
                   !selection.IsClear &&
                   !string.IsNullOrWhiteSpace(selection.Label)
                ? selection.Label
                : fallback;
        }

        private void ClearAutomationBlockDrag()
        {
            _draggingPaletteBlock = false;
            _draggingNativePaletteBlock = false;
            _draggingWorkspaceBlock = false;
            _panningBlockCanvas = false;
            _dragNativePaletteComponent = null;
            _dragWorkspaceBlockId = string.Empty;
            _blockDropIndex = -1;
        }

        private int BlockNodeIndex(string nodeId)
        {
            if (_blockWorkspace == null ||
                string.IsNullOrWhiteSpace(nodeId))
            {
                return -1;
            }

            AutomationBlockNode[] executableNodes = _blockWorkspace.ExecutableNodes.ToArray();
            for (int index = 0; index < executableNodes.Length; index++)
            {
                if (string.Equals(executableNodes[index].Id, nodeId, StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }

        private static IReadOnlyList<AutomationBlockKind> BlockKindsForCategory(AutomationBlockCategory category)
        {
            switch (category)
            {
                case AutomationBlockCategory.Output:
                    return new[] { AutomationBlockKind.SetTarget };
                case AutomationBlockCategory.Input:
                    return new[] { AutomationBlockKind.ReadTarget, AutomationBlockKind.Constant };
                case AutomationBlockCategory.Control:
                    return new[] { AutomationBlockKind.Compare, AutomationBlockKind.WhenIf, AutomationBlockKind.Switch };
                case AutomationBlockCategory.Math:
                    return new[] { AutomationBlockKind.MathScale, AutomationBlockKind.MathEvaluator };
                case AutomationBlockCategory.Timing:
                    return new[] { AutomationBlockKind.Delay };
                case AutomationBlockCategory.Organization:
                    return new[] { AutomationBlockKind.Comment, AutomationBlockKind.SystemBlock };
                case AutomationBlockCategory.Variables:
                    return new[] { AutomationBlockKind.Constant };
                case AutomationBlockCategory.Advanced:
                    return Array.Empty<AutomationBlockKind>();
                default:
                    return new[] { AutomationBlockKind.Comment };
            }
        }

        private static string AutomationBlockCategoryLabel(AutomationBlockCategory category)
        {
            switch (category)
            {
                case AutomationBlockCategory.Output:
                    return "Outputs";
                case AutomationBlockCategory.Input:
                    return "Inputs";
                case AutomationBlockCategory.Control:
                    return "Logic";
                case AutomationBlockCategory.Math:
                    return "Math";
                case AutomationBlockCategory.Timing:
                    return "Timing";
                case AutomationBlockCategory.Organization:
                    return "Organization";
                case AutomationBlockCategory.Variables:
                    return "Inputs";
                case AutomationBlockCategory.Advanced:
                    return "Advanced Native";
                default:
                    return "Organization";
            }
        }

        private static string AutomationBlockAudienceLabel(AutomationBlockAudience audience)
        {
            switch (audience)
            {
                case AutomationBlockAudience.Advanced:
                    return "Advanced";
                case AutomationBlockAudience.NativeWrapper:
                    return "Native Wrapper";
                case AutomationBlockAudience.MetadataOnly:
                    return "Metadata Only";
                default:
                    return "Beginner";
            }
        }

        private static string AutomationBlockCompatibilityLabel(AutomationBlockCompatibility compatibility)
        {
            switch (compatibility)
            {
                case AutomationBlockCompatibility.NativeViaGetterSetter:
                    return "Native via Getter/Setter";
                case AutomationBlockCompatibility.LayoutTemplateOnly:
                    return "Layout/Template Only";
                case AutomationBlockCompatibility.NotLowerableYet:
                    return "Not Lowerable Yet";
                default:
                    return "Native";
            }
        }

        private static string AutomationBlockPortSummary(AutomationBlockDefinition definition)
        {
            if (definition == null)
                return "no ports";

            string inputs = definition.InputPorts.Count == 0
                ? "no inputs"
                : "in " + string.Join("/", definition.InputPorts.Select(port => port.Name).ToArray());
            string outputs = definition.OutputPorts.Count == 0
                ? "no outputs"
                : "out " + string.Join("/", definition.OutputPorts.Select(port => port.Name).ToArray());
            return inputs + "; " + outputs;
        }

        private static string BlockNodeCompatibilityLabel(AutomationBlockNode node)
        {
            AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionForNode(node);
            return AutomationBlockCompatibilityLabel(definition.Compatibility);
        }

        private string BlockNodeValidationLabel(
            AutomationBlockNode node,
            AutomationBlockDefinition definition)
        {
            if (node == null)
                return "Select a block";

            if (node.Kind == AutomationBlockKind.Comment)
                return "Metadata only";
            if (node.Kind == AutomationBlockKind.Delay)
                return "Not lowerable yet";
            if (node.Kind == AutomationBlockKind.SystemBlock)
                return "Needs exposed ports in Systems";
            if (node.Kind == AutomationBlockKind.NativeComponent)
                return string.IsNullOrWhiteSpace(node.NativeComponentTypeName)
                    ? "Missing native component type"
                    : "Ready for Apply as native wrapper";
            if ((node.Kind == AutomationBlockKind.ReadTarget ||
                 node.Kind == AutomationBlockKind.SetTarget) &&
                _blockWorkspace?.TargetForNode(node) == null)
            {
                return node.Kind == AutomationBlockKind.ReadTarget
                    ? "Needs linked Input target"
                    : "Needs linked Output target";
            }

            if ((node.Kind == AutomationBlockKind.ReadTarget ||
                 node.Kind == AutomationBlockKind.SetTarget) &&
                !HasNativePropertyBinding(node))
            {
                return node.Kind == AutomationBlockKind.ReadTarget
                    ? "Needs explicit Getter property"
                    : "Needs explicit Setter property";
            }

            return definition != null && definition.CanLowerToNative
                ? "Ready for Check"
                : "Layout metadata";
        }

        private static string PaletteBlockPreviewText(AutomationBlockKind kind)
        {
            switch (kind)
            {
                case AutomationBlockKind.WhenIf:
                    return "if value then";
                case AutomationBlockKind.ReadTarget:
                    return "read target";
                case AutomationBlockKind.Compare:
                    return "compare value";
                case AutomationBlockKind.MathScale:
                    return "scale value";
                case AutomationBlockKind.MathEvaluator:
                    return "math evaluator";
                case AutomationBlockKind.Switch:
                    return "switch";
                case AutomationBlockKind.SetTarget:
                    return "set target to";
                case AutomationBlockKind.Constant:
                    return "constant value";
                case AutomationBlockKind.Delay:
                    return "wait seconds";
                case AutomationBlockKind.SystemBlock:
                    return "system block";
                case AutomationBlockKind.NativeComponent:
                    return "native component";
                default:
                    return "note";
            }
        }

        private static string DefaultBlockIcon(AutomationBlockKind kind)
        {
            switch (kind)
            {
                case AutomationBlockKind.WhenIf:
                    return "risk";
                case AutomationBlockKind.ReadTarget:
                    return "visibility";
                case AutomationBlockKind.Compare:
                    return "filter";
                case AutomationBlockKind.MathScale:
                    return "settings";
                case AutomationBlockKind.MathEvaluator:
                    return "settings";
                case AutomationBlockKind.Switch:
                    return "filter";
                case AutomationBlockKind.SetTarget:
                    return "anchor";
                case AutomationBlockKind.Constant:
                    return "cube";
                case AutomationBlockKind.Delay:
                    return "time";
                case AutomationBlockKind.SystemBlock:
                    return "duplicate";
                case AutomationBlockKind.NativeComponent:
                    return "duplicate";
                default:
                    return "info";
            }
        }

        private static float TinkercadBlockHeight(AutomationBlockNode node)
        {
            if (node == null)
                return EsuHudLayout.Scale(58f);

            AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionForNode(node);
            int portRows = Math.Max(
                definition.InputPorts?.Count ?? 0,
                definition.OutputPorts?.Count ?? 0);
            float portExtra = Mathf.Max(0f, portRows - 1) * EsuHudLayout.Scale(14f);
            if (node.Kind == AutomationBlockKind.Comment)
                return EsuHudLayout.Scale(78f);
            if (node.Kind == AutomationBlockKind.ReadTarget ||
                node.Kind == AutomationBlockKind.SetTarget)
                return EsuHudLayout.Scale(148f) + portExtra;
            if (node.Kind == AutomationBlockKind.NativeComponent)
                return EsuHudLayout.Scale(82f) + portExtra;
            return EsuHudLayout.Scale(76f) + portExtra;
        }

        private float NativeExactDrawingScale()
        {
            float scale = _blockWorkspace == null || _blockWorkspace.NativeDisplayScale <= 0f
                ? 1f
                : _blockWorkspace.NativeDisplayScale;
            if (float.IsNaN(scale) || float.IsInfinity(scale))
                scale = 1f;
            return Mathf.Clamp(scale, 0.55f, 1.25f);
        }

        private static float NativeExactNodeWidth(AutomationBlockNode node)
        {
            if (node == null)
                return EsuHudLayout.Scale(150f);

            float nativeWidth = node.NativeWidth > 20f ? node.NativeWidth : 150f;
            return EsuHudLayout.Scale(Mathf.Clamp(nativeWidth, 130f, 260f));
        }

        private static float NativeExactNodeHeight(AutomationBlockNode node)
        {
            if (node == null)
                return EsuHudLayout.Scale(78f);

            AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionForNode(node);
            int portRows = Math.Max(
                definition.InputPorts?.Count ?? 0,
                definition.OutputPorts?.Count ?? 0);
            float fallback = Mathf.Max(78f, 48f + portRows * 13f);
            float nativeHeight = node.NativeHeight > 20f ? node.NativeHeight : fallback;
            return EsuHudLayout.Scale(Mathf.Clamp(nativeHeight, 62f, 180f));
        }

        private static float NativeExactBlockUiScale(Rect rect, AutomationBlockNode node)
        {
            float baseHeight = Mathf.Max(1f, NativeExactNodeHeight(node));
            return Mathf.Clamp(rect.height / baseHeight, 0.45f, 1.85f);
        }

        private static float TinkercadBlockUiScale(Rect rect, AutomationBlockNode node)
        {
            float baseHeight = Mathf.Max(1f, TinkercadBlockHeight(node));
            return Mathf.Clamp(rect.height / baseHeight, 0.45f, 1.85f);
        }

        private static Color BlockCategoryColor(AutomationBlockCategory category)
        {
            switch (category)
            {
                case AutomationBlockCategory.Output:
                    return new Color(0.16f, 0.53f, 1f, 0.94f);
                case AutomationBlockCategory.Input:
                    return new Color(0.55f, 0.32f, 0.96f, 0.94f);
                case AutomationBlockCategory.Control:
                    return new Color(1f, 0.62f, 0.08f, 0.96f);
                case AutomationBlockCategory.Math:
                    return new Color(0.2f, 0.74f, 0.28f, 0.94f);
                case AutomationBlockCategory.Variables:
                    return new Color(0.83f, 0.28f, 0.82f, 0.94f);
                case AutomationBlockCategory.Advanced:
                    return new Color(0.08f, 0.62f, 0.7f, 0.94f);
                default:
                    return new Color(0.58f, 0.6f, 0.62f, 0.94f);
            }
        }

        private static void DrawTinkercadBlockShape(
            Rect rect,
            Color color,
            string label,
            string iconKey,
            bool selected,
            bool hat,
            string badge = null,
            float uiScale = 1f)
        {
            uiScale = Mathf.Clamp(uiScale, 0.45f, 1.85f);
            Color baseColor = selected
                ? new Color(Mathf.Min(1f, color.r + 0.08f), Mathf.Min(1f, color.g + 0.08f), Mathf.Min(1f, color.b + 0.08f), color.a)
                : color;
            DrawFilledRect(rect, baseColor);
            DrawFilledRect(
                new Rect(rect.x, rect.yMax - Mathf.Max(1f, EsuHudLayout.Scale(2f) * uiScale), rect.width, Mathf.Max(1f, EsuHudLayout.Scale(2f) * uiScale)),
                new Color(0f, 0f, 0f, 0.18f));
            if (hat)
            {
                DrawFilledRect(
                    new Rect(rect.x + EsuHudLayout.Scale(16f) * uiScale, rect.y - EsuHudLayout.Scale(5f) * uiScale, EsuHudLayout.Scale(44f) * uiScale, EsuHudLayout.Scale(10f) * uiScale),
                    baseColor);
            }

            DrawFilledRect(
                new Rect(rect.x + EsuHudLayout.Scale(22f) * uiScale, rect.yMax - EsuHudLayout.Scale(5f) * uiScale, EsuHudLayout.Scale(34f) * uiScale, EsuHudLayout.Scale(8f) * uiScale),
                new Color(0f, 0f, 0f, 0.18f));
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            Rect iconRect = new Rect(
                rect.x + EsuHudLayout.Scale(9f) * uiScale,
                rect.y + EsuHudLayout.Scale(7f) * uiScale,
                Mathf.Max(EsuHudLayout.Scale(7f), EsuHudLayout.Scale(16f) * uiScale),
                Mathf.Max(EsuHudLayout.Scale(7f), EsuHudLayout.Scale(16f) * uiScale));
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
            Rect badgeRect = Rect.zero;
            if (!string.IsNullOrWhiteSpace(badge))
            {
                badgeRect = new Rect(
                    rect.xMax - EsuHudLayout.Scale(128f) * uiScale,
                    rect.y + EsuHudLayout.Scale(5f) * uiScale,
                    EsuHudLayout.Scale(118f) * uiScale,
                    Mathf.Max(EsuHudLayout.Scale(9f), EsuHudLayout.Scale(18f) * uiScale));
                DrawFilledRect(badgeRect, new Color(0f, 0f, 0f, 0.16f));
                DrawFittedSingleLineLabel(
                    badgeRect,
                    badge,
                    TinkercadBadgeStyle(),
                    TextAnchor.MiddleCenter,
                    EsuHudLayout.FontSize(7));
            }

            float titleRight = badgeRect.width > 0f
                ? badgeRect.x - EsuHudLayout.Scale(6f) * uiScale
                : rect.xMax - EsuHudLayout.Scale(12f) * uiScale;
            DrawFittedSingleLineLabel(
                new Rect(
                    iconRect.xMax + EsuHudLayout.Scale(6f) * uiScale,
                    rect.y + EsuHudLayout.Scale(3f) * uiScale,
                    Mathf.Max(EsuHudLayout.Scale(24f) * uiScale, titleRight - iconRect.xMax - EsuHudLayout.Scale(6f) * uiScale),
                    Mathf.Max(EsuHudLayout.Scale(10f), EsuHudLayout.Scale(24f) * uiScale)),
                label ?? string.Empty,
                TinkercadBlockTextStyle(TextAnchor.MiddleLeft),
                TextAnchor.MiddleLeft,
                EsuHudLayout.FontSize(8));
            if (selected)
            {
                float line = Mathf.Max(1f, EsuHudLayout.Scale(2f) * uiScale);
                DrawFilledRect(new Rect(rect.x, rect.y, rect.width, line), DecorationEditorTheme.Cyan);
                DrawFilledRect(new Rect(rect.x, rect.yMax - line, rect.width, line), DecorationEditorTheme.Cyan);
            }
        }

        private static void DrawFilledRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static GUIStyle TinkercadBlockTextStyle(TextAnchor alignment)
        {
            var style = new GUIStyle(DecorationEditorTheme.Body)
            {
                alignment = alignment,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontStyle = FontStyle.Bold
            };
            style.normal.background = null;
            style.normal.textColor = Color.white;
            return style;
        }

        private static GUIStyle TinkercadBadgeStyle()
        {
            var style = new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontStyle = FontStyle.Bold
            };
            style.normal.background = null;
            style.normal.textColor = Color.white;
            return style;
        }

        private static GUIStyle TinkercadPortLabelStyle(TextAnchor alignment)
        {
            var style = new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = alignment,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontStyle = FontStyle.Bold
            };
            style.normal.background = null;
            style.normal.textColor = Color.white;
            return style;
        }

        private static GUIStyle TinkercadValueStyle()
        {
            var style = new GUIStyle(DecorationEditorTheme.Button)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            style.normal.background = null;
            style.normal.textColor = Color.white;
            return style;
        }

        private void DrawBlockAddToolbar()
        {
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Add block", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            DrawAddAutomationBlockButton(AutomationBlockKind.WhenIf, "If", "risk");
            DrawAddAutomationBlockButton(AutomationBlockKind.ReadTarget, "Read", "visibility");
            DrawAddAutomationBlockButton(AutomationBlockKind.Compare, "Compare", "filter");
            DrawAddAutomationBlockButton(AutomationBlockKind.MathScale, "Math", "settings");
            DrawAddAutomationBlockButton(AutomationBlockKind.SetTarget, "Set", "anchor");
            DrawAddAutomationBlockButton(AutomationBlockKind.Constant, "Const", "cube");
            DrawAddAutomationBlockButton(AutomationBlockKind.Delay, "Delay", "time");
            DrawAddAutomationBlockButton(AutomationBlockKind.Comment, "Note", "info");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawAddAutomationBlockButton(
            AutomationBlockKind kind,
            string label,
            string icon)
        {
            if (AutomationGUILayoutButton(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), "Add " + label + " to the ESU Blocks stack."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                AddAutomationBlock(kind, -1);
            }
        }

        private void DrawAutomationBlockNodeRow(AutomationBlockNode node)
        {
            if (node == null)
                return;

            bool selected = string.Equals(_blockWorkspace.SelectedNodeId, node.Id, StringComparison.Ordinal);
            GUILayout.BeginVertical(selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
            Rect rowRect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(34f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(34f)));
            GUI.Label(rowRect, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
            DrawAutomationSingleLineIconRow(
                rowRect,
                node.IconKey,
                BlockNodeTitle(node),
                DecorationEditorTheme.Body);
            if (Event.current != null &&
                Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                rowRect.Contains(Event.current.mousePosition))
            {
                _blockWorkspace.Select(node.Id);
                Event.current.Use();
            }

            DrawAutomationBlockNodeControls(node);
            GUILayout.EndVertical();
        }

        private string BlockNodeTitle(AutomationBlockNode node)
        {
            if (node == null)
                return "Block";

            switch (node.Kind)
            {
                case AutomationBlockKind.WhenIf:
                    return node.Label + ": else " +
                           node.SecondaryNumericValue.ToString("0.###", CultureInfo.InvariantCulture);
                case AutomationBlockKind.ReadTarget:
                case AutomationBlockKind.SetTarget:
                    return node.Label +
                           ": " +
                           (string.IsNullOrWhiteSpace(node.TargetLabel) ? "choose linked target" : node.TargetLabel) +
                           " / " +
                           AutomationBlockPropertyLabel(node);
                case AutomationBlockKind.Compare:
                    return node.Label + ": " + CompareOperatorLabel(node.Operator) + " " +
                           node.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);
                case AutomationBlockKind.MathScale:
                    return node.Label + ": x " + node.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);
                case AutomationBlockKind.MathEvaluator:
                    return node.Label + ": " + (string.IsNullOrWhiteSpace(node.Expression) ? "input" : node.Expression);
                case AutomationBlockKind.Switch:
                    return node.Label + ": else " +
                           node.SecondaryNumericValue.ToString("0.###", CultureInfo.InvariantCulture);
                case AutomationBlockKind.Constant:
                    return node.Label + ": " + node.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);
                case AutomationBlockKind.Delay:
                    return node.Label + ": " + node.NumericValue.ToString("0.###", CultureInfo.InvariantCulture) + "s";
                case AutomationBlockKind.NativeComponent:
                    if (node.NativeImported)
                        return NativeExactBlockTitle(node);

                    return string.IsNullOrWhiteSpace(node.NativeComponentLabel)
                        ? "Native component"
                        : "Native: " + node.NativeComponentLabel;
                case AutomationBlockKind.Comment:
                    return string.IsNullOrWhiteSpace(node.Comment) ? "Comment" : "Comment: " + node.Comment;
                default:
                    return node.Label;
            }
        }

        private static string NativeExactBlockTitle(AutomationBlockNode node)
        {
            if (node == null)
                return "Native component";

            string target = NativeExactTargetLine(node);
            if (!string.IsNullOrWhiteSpace(target))
                return target;

            return string.IsNullOrWhiteSpace(node.NativeComponentLabel)
                ? "Native component"
                : node.NativeComponentLabel;
        }

        private static string NativeExactTargetLine(AutomationBlockNode node)
        {
            if (node == null)
                return string.Empty;

            string type = string.IsNullOrWhiteSpace(node.NativeBlockTypeName)
                ? string.Empty
                : node.NativeBlockTypeName.Trim();
            string filter = string.IsNullOrWhiteSpace(node.NativeBlockFilter)
                ? string.Empty
                : node.NativeBlockFilter.Trim();
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(filter))
                return type + " / " + filter;
            if (!string.IsNullOrWhiteSpace(type))
                return type;
            if (!string.IsNullOrWhiteSpace(filter))
                return filter;
            return string.Empty;
        }

        private void DrawAutomationBlockNodeControls(AutomationBlockNode node)
        {
            if (node.Kind == AutomationBlockKind.ReadTarget ||
                node.Kind == AutomationBlockKind.SetTarget)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    node.Kind == AutomationBlockKind.SetTarget ? "Writable target" : "Readable target",
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(110f)));
                if (AutomationGUILayoutButton(
                        new GUIContent(
                            string.IsNullOrWhiteSpace(node.TargetLabel) ? "Choose target" : node.TargetLabel,
                            DecorationEditorIconCatalog.Get(AutomationTargetIconKey(_blockWorkspace.TargetForNode(node))),
                            "Choose a linked target available to this ESU Block."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                    OpenAutomationTargetPicker(node);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Property", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(110f)));
                if (AutomationGUILayoutButton(
                        new GUIContent(
                            AutomationBlockPropertyLabel(node),
                            DecorationEditorIconCatalog.Get("filter"),
                            "Choose the native Breadboard Generic Getter/Setter property used by Check and Apply."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                    OpenAutomationPropertyPicker(node);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "Read/Set blocks require an explicit native Generic Getter/Setter property before Check or Apply.",
                    DecorationEditorTheme.MiniWrap);
                return;
            }

            if (node.Kind == AutomationBlockKind.WhenIf)
            {
                DrawEsuBlockFloatStepper("Else", node.SecondaryNumericValue, 0.1f, value =>
                {
                    node.SecondaryNumericValue = value;
                    InvalidateEsuBlockPlan();
                });
                return;
            }

            if (node.Kind == AutomationBlockKind.Compare)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Operator", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(76f)));
                if (AutomationGUILayoutButton(
                        new GUIContent(CompareOperatorLabel(node.Operator), DecorationEditorIconCatalog.Get("filter"), "Cycle comparison operator."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(92f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                    CycleAutomationCompareOperator(node);
                DrawEsuBlockFloatStepper("Threshold", node.NumericValue, 0.1f, value =>
                {
                    node.NumericValue = value;
                    InvalidateEsuBlockPlan();
                });
                GUILayout.EndHorizontal();
                return;
            }

            if (node.Kind == AutomationBlockKind.MathScale ||
                node.Kind == AutomationBlockKind.Constant ||
                node.Kind == AutomationBlockKind.Delay)
            {
                string label = node.Kind == AutomationBlockKind.Delay
                    ? "Seconds"
                    : node.Kind == AutomationBlockKind.Constant
                        ? "Value"
                        : "Scale";
                DrawEsuBlockFloatStepper(label, node.NumericValue, node.Kind == AutomationBlockKind.Delay ? 0.05f : 0.1f, value =>
                {
                    node.NumericValue = node.Kind == AutomationBlockKind.Delay ? Mathf.Max(0f, value) : value;
                    InvalidateEsuBlockPlan();
                });
                return;
            }

            if (node.Kind == AutomationBlockKind.MathEvaluator)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Expression", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
                string next = GUILayout.TextField(node.Expression ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Height(EsuHudLayout.Scale(24f)));
                if (!string.Equals(next, node.Expression ?? string.Empty, StringComparison.Ordinal))
                {
                    node.Expression = next;
                    InvalidateEsuBlockPlan();
                }
                GUILayout.EndHorizontal();
                return;
            }

            if (node.Kind == AutomationBlockKind.Switch)
            {
                DrawEsuBlockFloatStepper("Else", node.SecondaryNumericValue, 0.1f, value =>
                {
                    node.SecondaryNumericValue = value;
                    InvalidateEsuBlockPlan();
                });
                return;
            }

            if (node.Kind == AutomationBlockKind.NativeComponent)
            {
                LabelRow("Native type", string.IsNullOrWhiteSpace(node.NativeComponentTypeName)
                    ? "unresolved"
                    : node.NativeComponentTypeName);
                if (!string.IsNullOrWhiteSpace(node.NativeComponentDescription))
                    GUILayout.Label(node.NativeComponentDescription, DecorationEditorTheme.MiniWrap);
                GUILayout.Label(
                    "Advanced wrapper: Apply creates this advertised native Breadboard component; Revert removes only ESU-generated component ids.",
                    DecorationEditorTheme.MiniWrap);
                return;
            }

            if (node.Kind == AutomationBlockKind.Comment)
            {
                string next = GUILayout.TextField(node.Comment ?? string.Empty, DecorationEditorTheme.TextField);
                if (!string.Equals(next, node.Comment ?? string.Empty, StringComparison.Ordinal))
                {
                    node.Comment = next;
                    InvalidateEsuBlockPlan();
                }
            }
        }

        private void OpenAutomationTargetPicker(AutomationBlockNode node)
        {
            if (node == null ||
                (node.Kind != AutomationBlockKind.ReadTarget &&
                 node.Kind != AutomationBlockKind.SetTarget))
            {
                return;
            }

            EnsureBlockWorkspace();
            CloseAutomationContextMenu();
            CloseAutomationPropertyPicker();
            _blockWorkspace.Select(node.Id);
            _targetPickerNodeId = node.Id;
            _targetPickerFilter = string.Empty;
            _targetPickerScroll = Vector2.zero;
            float width = Mathf.Min(EsuHudLayout.Scale(440f), Screen.width - EsuHudLayout.Scale(24f));
            float height = Mathf.Min(EsuHudLayout.Scale(360f), Screen.height - EsuHudLayout.Scale(96f));
            Vector2 anchor = Event.current == null
                ? MouseGuiPosition()
                : GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            _targetPickerRect = ClampAutomationPopupRect(
                new Rect(anchor.x + EsuHudLayout.Scale(18f), anchor.y + EsuHudLayout.Scale(18f), width, height));
        }

        private void DrawAutomationTargetPicker()
        {
            if (string.IsNullOrWhiteSpace(_targetPickerNodeId) ||
                _blockWorkspace == null)
            {
                return;
            }

            AutomationBlockNode node = _blockWorkspace.Nodes.FirstOrDefault(item =>
                string.Equals(item.Id, _targetPickerNodeId, StringComparison.Ordinal));
            if (node == null)
            {
                CloseAutomationTargetPicker();
                return;
            }

            float width = Mathf.Min(EsuHudLayout.Scale(440f), Screen.width - EsuHudLayout.Scale(24f));
            float height = Mathf.Min(EsuHudLayout.Scale(360f), Screen.height - EsuHudLayout.Scale(96f));
            if (_targetPickerRect.width <= 1f || _targetPickerRect.height <= 1f)
                _targetPickerRect = ClampAutomationPopupRect(new Rect(EsuHudLayout.Scale(24f), EsuHudLayout.Scale(82f), width, height));
            _targetPickerRect.width = width;
            _targetPickerRect.height = height;
            _targetPickerRect = ClampAutomationPopupRect(_targetPickerRect);
            if (ConsumeOutsideAutomationPopupClick(_targetPickerRect, CloseAutomationTargetPicker))
                return;

            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -11000);
            try
            {
                GUI.Window(_targetPickerWindowId, _targetPickerRect, _ => DrawAutomationTargetPickerWindow(node, width, height), GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(_targetPickerWindowId);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private void SelectAutomationBlockTarget(
            AutomationBlockNode node,
            AutomationTarget target)
        {
            if (node == null ||
                target == null ||
                _blockWorkspace == null)
            {
                return;
            }

            if (string.Equals(node.TargetKey, target.StableKey, StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(node.TargetPersistenceKey) &&
                string.Equals(node.TargetPersistenceKey, target.PersistenceKey, StringComparison.Ordinal))
            {
                _status = node.Label + " already uses " + target.Label + ".";
                CloseAutomationTargetPicker();
                return;
            }

            AutomationLinkDirection direction = node.Kind == AutomationBlockKind.ReadTarget
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output;
            if (_blockWorkspace.SetNodeTarget(node.Id, target, direction))
            {
                InvalidateEsuBlockPlan();
                _status = node.Label + " now uses " + target.Label + ". Choose its property before Check.";
            }
            CloseAutomationTargetPicker();
        }

        private void DrawAutomationTargetPickerWindow(
            AutomationBlockNode node,
            float width,
            float height)
        {
            GUI.Box(new Rect(0f, 0f, width, height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, width - inset * 2f, height - inset * 2f));
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(
                (node.Kind == AutomationBlockKind.ReadTarget ? "Read input" : "Write output"),
                node.Kind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor",
                DecorationEditorTheme.SubHeader);
            if (AutomationGUILayoutButton(
                    new GUIContent("Close", "Close target picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(64f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                CloseAutomationTargetPicker();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(52f)));
            string nextFilter = GUILayout.TextField(_targetPickerFilter ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextFilter, _targetPickerFilter, StringComparison.Ordinal))
            {
                _targetPickerFilter = nextFilter ?? string.Empty;
                _targetPickerScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            AutomationLinkDirection direction = node.Kind == AutomationBlockKind.ReadTarget
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output;
            AutomationTarget[] options = BlockTargetOptions(node.Kind);
            AutomationTarget[] matchingOptions = options
                .Where(target => AutomationTargetPickerMatchesSearch(target, direction, _targetPickerFilter))
                .ToArray();
            GUILayout.Label(
                "Choose linked " +
                (direction == AutomationLinkDirection.Input ? "Input" : "Output") +
                " target. Changing target resets the property selection.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Targets shown: " +
                matchingOptions.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                options.Length.ToString(CultureInfo.InvariantCulture) +
                ".",
                DecorationEditorTheme.MiniWrap);

            _targetPickerScroll = GUILayout.BeginScrollView(
                _targetPickerScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(height - EsuHudLayout.Scale(118f)));
            if (options.Length == 0)
            {
                GUILayout.Label(
                    direction == AutomationLinkDirection.Input
                        ? "No linked Inputs are available. Use Signals or Link mode to add an input first."
                        : "No linked Outputs are available. Use Signals or Link mode to add an output first.",
                    DecorationEditorTheme.Warning);
            }
            else if (matchingOptions.Length == 0)
            {
                GUILayout.Label("No linked targets match the current filter.", DecorationEditorTheme.Warning);
            }

            foreach (AutomationTarget target in matchingOptions)
            {
                if (target == null)
                    continue;

                bool selected =
                    string.Equals(node.TargetKey, target.StableKey, StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(node.TargetPersistenceKey) &&
                    string.Equals(node.TargetPersistenceKey, target.PersistenceKey, StringComparison.Ordinal);
                Rect row = GUILayoutUtility.GetRect(
                    1f,
                    EsuHudLayout.Scale(46f),
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(EsuHudLayout.Scale(46f)));
                GUI.Label(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
                DrawAutomationIconRow(
                    row,
                    AutomationTargetIconKey(target),
                    target.Label,
                    AutomationTargetCatalog.CategoryLabel(target.Category) + " | " + AutomationTargetCatalog.RoleLabel(target),
                    selected ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini,
                    DecorationEditorTheme.MiniWrap);
                RegisterAutomationTargetPreview(row, target, direction == AutomationLinkDirection.Input ? "Input target" : "Output target");
                EsuCursorTooltip.Register(row, "Use this linked target for the selected ESU Block.");
                if (PrimaryMouseDownIn(row))
                {
                    SelectAutomationBlockTarget(node, target);
                    Event.current.Use();
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                    return;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void CloseAutomationTargetPicker()
        {
            _targetPickerNodeId = string.Empty;
            _targetPickerRect = Rect.zero;
            _targetPickerFilter = string.Empty;
            _targetPickerScroll = Vector2.zero;
        }

        private static bool AutomationTargetPickerMatchesSearch(
            AutomationTarget target,
            AutomationLinkDirection direction,
            string search)
        {
            if (target == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (direction == AutomationLinkDirection.Input ? "input read getter " : "output write setter ") +
                (target.Label ?? string.Empty) + " " +
                (target.StableKey ?? string.Empty) + " " +
                (target.RuntimeType ?? string.Empty) + " " +
                AutomationTargetCatalog.CategoryLabel(target.Category) + " " +
                AutomationTargetCatalog.RoleLabel(target) + " " +
                FormatCell(target.LocalPosition);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void OpenAutomationPropertyPicker(AutomationBlockNode node)
        {
            if (node == null ||
                (node.Kind != AutomationBlockKind.ReadTarget &&
                 node.Kind != AutomationBlockKind.SetTarget))
            {
                return;
            }

            EnsureBlockWorkspace();
            CloseAutomationContextMenu();
            CloseAutomationTargetPicker();
            _blockWorkspace.Select(node.Id);
            _propertyPickerNodeId = node.Id;
            _propertyPickerFilter = string.Empty;
            _propertyPickerScroll = Vector2.zero;
            float width = Mathf.Min(EsuHudLayout.Scale(520f), Screen.width - EsuHudLayout.Scale(24f));
            float height = Mathf.Min(EsuHudLayout.Scale(440f), Screen.height - EsuHudLayout.Scale(96f));
            Vector2 anchor = Event.current == null
                ? MouseGuiPosition()
                : GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            _propertyPickerRect = ClampAutomationPopupRect(
                new Rect(anchor.x + EsuHudLayout.Scale(20f), anchor.y + EsuHudLayout.Scale(20f), width, height));
            RefreshAutomationPropertyPickerOptions();
        }

        private void DrawAutomationPropertyPicker()
        {
            if (string.IsNullOrWhiteSpace(_propertyPickerNodeId) ||
                _blockWorkspace == null)
            {
                return;
            }

            AutomationBlockNode node = _blockWorkspace.Nodes.FirstOrDefault(item =>
                string.Equals(item.Id, _propertyPickerNodeId, StringComparison.Ordinal));
            if (node == null)
            {
                CloseAutomationPropertyPicker();
                return;
            }

            float width = Mathf.Min(EsuHudLayout.Scale(520f), Screen.width - EsuHudLayout.Scale(24f));
            float height = Mathf.Min(EsuHudLayout.Scale(440f), Screen.height - EsuHudLayout.Scale(96f));
            if (_propertyPickerRect.width <= 1f || _propertyPickerRect.height <= 1f)
                _propertyPickerRect = ClampAutomationPopupRect(new Rect(EsuHudLayout.Scale(24f), EsuHudLayout.Scale(82f), width, height));
            _propertyPickerRect.width = width;
            _propertyPickerRect.height = height;
            _propertyPickerRect = ClampAutomationPopupRect(_propertyPickerRect);
            if (ConsumeOutsideAutomationPopupClick(_propertyPickerRect, CloseAutomationPropertyPicker))
                return;

            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -11000);
            try
            {
                GUI.Window(_propertyPickerWindowId, _propertyPickerRect, _ => DrawAutomationPropertyPickerWindow(node, width, height), GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(_propertyPickerWindowId);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private static Rect ClampAutomationPopupRect(Rect rect)
        {
            float margin = EsuHudLayout.Scale(8f);
            rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, Screen.width - rect.width - margin));
            rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, Screen.height - rect.height - margin));
            return rect;
        }

        private bool ConsumeOutsideAutomationPopupClick(Rect rect, Action close)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.MouseDown ||
                rect.Contains(current.mousePosition))
            {
                return false;
            }

            close?.Invoke();
            AutomationInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private void SelectAutomationBlockProperty(
            AutomationBlockNode node,
            AutomationTargetPropertyOption option)
        {
            if (node == null ||
                option?.Selection == null)
            {
                return;
            }

            node.SelectProperty(option.Selection);
            InvalidateEsuBlockPlan();
            _status = node.Label + " property: " + AutomationBlockPropertyLabel(node) + ".";
            CloseAutomationPropertyPicker();
        }

        private void DrawAutomationPropertyPickerWindow(
            AutomationBlockNode node,
            float width,
            float height)
        {
            GUI.Box(new Rect(0f, 0f, width, height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, width - inset * 2f, height - inset * 2f));
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(
                (node.Kind == AutomationBlockKind.ReadTarget ? "Read property" : "Set property") +
                " - " +
                node.TargetLabel,
                "filter",
                DecorationEditorTheme.SubHeader);
            if (AutomationGUILayoutButton(
                    new GUIContent("Close", "Close property picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(64f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                CloseAutomationPropertyPicker();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(52f)));
            string nextFilter = GUILayout.TextField(_propertyPickerFilter ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextFilter, _propertyPickerFilter, StringComparison.Ordinal))
            {
                _propertyPickerFilter = nextFilter ?? string.Empty;
                RefreshAutomationPropertyPickerOptions();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Property picker discovers live FtD options through a temporary native Generic Getter/Setter proxy, then cleans it up. Selection stores ESU metadata; Apply performs the real GBG/GBS binding.",
                DecorationEditorTheme.MiniWrap);

            AutomationTarget target = _blockWorkspace.TargetForNode(node);
            if (target == null)
            {
                GUILayout.Label("Choose a linked target before choosing a native property.", DecorationEditorTheme.Warning);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Current: " + AutomationBlockPropertyLabel(node), DecorationEditorTheme.Mini);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Options shown: " +
                _propertyPickerOptions.Count.ToString(CultureInfo.InvariantCulture) +
                " / up to " +
                PropertyPickerVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                ". Filter narrows live native property names and tooltips.",
                DecorationEditorTheme.MiniWrap);
            if (_propertyPickerOptions.Count >= PropertyPickerVisibleLimit)
            {
                GUILayout.Label(
                    "Showing first " +
                    PropertyPickerVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                    " native properties. Use Filter to narrow large target property lists.",
                    DecorationEditorTheme.Warning);
            }

            _propertyPickerScroll = GUILayout.BeginScrollView(
                _propertyPickerScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(height - EsuHudLayout.Scale(142f)));
            if (_propertyPickerOptions.Count == 0)
                GUILayout.Label("No native properties matched this target and filter.", DecorationEditorTheme.Warning);
            foreach (AutomationTargetPropertyOption option in _propertyPickerOptions)
            {
                if (option == null ||
                    option.Selection == null ||
                    option.Selection.IsClear)
                {
                    continue;
                }

                bool selected = node.PropertySelection != null &&
                                option.Selection != null &&
                                node.PropertySelection.Matches(option.Selection);
                Rect row = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(42f), GUILayout.ExpandWidth(true), GUILayout.Height(EsuHudLayout.Scale(42f)));
                GUI.Label(row, GUIContent.none, selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
                DrawAutomationIconRow(
                    row,
                    node.Kind == AutomationBlockKind.ReadTarget ? "visibility" : "anchor",
                    option.Label,
                    option.Tooltip,
                    selected ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini,
                    DecorationEditorTheme.MiniWrap);
                EsuCursorTooltip.Register(row, option.Tooltip);
                if (PrimaryMouseDownIn(row))
                {
                    SelectAutomationBlockProperty(node, option);
                    Event.current.Use();
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                    return;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void CloseAutomationPropertyPicker()
        {
            _propertyPickerNodeId = string.Empty;
            _propertyPickerRect = Rect.zero;
            _propertyPickerFilter = string.Empty;
            _propertyPickerScroll = Vector2.zero;
            _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
        }

        private void RefreshAutomationPropertyPickerOptions()
        {
            AutomationBlockNode node = _blockWorkspace?.Nodes.FirstOrDefault(item =>
                string.Equals(item.Id, _propertyPickerNodeId, StringComparison.Ordinal));
            AutomationTarget target = node == null ? null : _blockWorkspace.TargetForNode(node);
            if (node == null || target == null)
            {
                _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
                return;
            }

            bool getter = node.Kind == AutomationBlockKind.ReadTarget;
            if (_selectedController == null)
            {
                _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
                _status = "Select a Breadboard/controller before choosing native properties.";
                return;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
                _status = reason ?? "Selected controller does not expose a native Breadboard.";
                return;
            }

            if (!inspector.TryCreateTargetProxy(
                    target,
                    getter,
                    !getter,
                    out AutomationBreadboardCompileResult tempResult,
                    out string proxyMessage))
            {
                _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
                _status = proxyMessage ?? "Could not create temporary native property picker proxy.";
                return;
            }

            try
            {
                AutomationBreadboardComponentSummary component = null;
                foreach (uint componentId in tempResult?.ComponentIds ?? Array.Empty<uint>())
                {
                    component = FindComponentById(inspector.Components, componentId);
                    if (component != null && component.IsGenericProxy)
                        break;
                }

                if (component == null)
                {
                    _propertyPickerOptions = Array.Empty<AutomationTargetPropertyOption>();
                    return;
                }

                _propertyPickerOptions = inspector
                    .ProxyPropertyOptions(component, _propertyPickerFilter, PropertyPickerVisibleLimit)
                    .Select(option => ToTargetPropertyOption(option, getter))
                    .Where(option => option != null)
                    .ToArray();
            }
            finally
            {
                int deleted = DeleteBreadboardComponents(
                    inspector,
                    tempResult?.ComponentIds ?? Array.Empty<uint>(),
                    out int missing,
                    out int failed);
                if (failed > 0 || missing > 0)
                {
                    _status =
                        "Temporary property picker proxy cleanup was incomplete: " +
                        deleted.ToString(CultureInfo.InvariantCulture) +
                        " deleted, " +
                        missing.ToString(CultureInfo.InvariantCulture) +
                        " missing, " +
                        failed.ToString(CultureInfo.InvariantCulture) +
                        " failed.";
                }
            }
        }

        private static AutomationTargetPropertyOption ToTargetPropertyOption(
            AutomationBreadboardProxyOption option,
            bool getter)
        {
            if (option == null)
                return null;
            if (option.IsClear)
                return null;

            var selection = new AutomationProxyPropertySelection(
                option.Label,
                option.Tooltip,
                getter,
                option.IsClear,
                option.IsGetterReadable,
                option.ReadableAttributeId,
                option.BlockPropertyId,
                option.BlockSetId);
            return new AutomationTargetPropertyOption(selection, option.Label, option.Tooltip);
        }

        private void RegisterAutomationTargetPreview(
            Rect sourceRect,
            AutomationTarget target,
            string reason)
        {
            if (target == null ||
                Event.current == null ||
                !sourceRect.Contains(Event.current.mousePosition))
            {
                return;
            }

            _previewTarget = target;
            _previewReason = reason ?? string.Empty;
            _previewSourceRect = GuiToScreenRect(sourceRect);
            _hoverTarget = target;
        }

        private void DrawAutomationTargetPreviewCard()
        {
            if (_previewTarget == null || Event.current == null)
                return;

            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -12000);
            try
            {
                float width = EsuHudLayout.Scale(330f);
                float height = EsuHudLayout.Scale(150f);
                Vector2 mouse = Event.current.mousePosition;
                Vector2 anchor = PreviewCardAnchor(mouse, _previewSourceRect);
                Rect rect = new Rect(
                    Mathf.Clamp(anchor.x + EsuHudLayout.Scale(18f), EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f)),
                    Mathf.Clamp(anchor.y + EsuHudLayout.Scale(18f), EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f)),
                    width,
                    height);
                GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

                Rect previewRect = new Rect(
                    rect.x + EsuHudLayout.Scale(10f),
                    rect.y + EsuHudLayout.Scale(32f),
                    EsuHudLayout.Scale(104f),
                    EsuHudLayout.Scale(104f));
                Texture preview = _targetPreviewRenderer.GetPreview(_previewTarget, 128, _targetPreviewSpin);
                if (preview != null)
                {
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, alphaBlend: true);
                }
                else
                {
                    GUI.Label(previewRect, new GUIContent(string.Empty, DecorationEditorIconCatalog.Get(AutomationTargetIconKey(_previewTarget))), DecorationEditorTheme.Row);
                }

                GUI.BeginGroup(rect);
                GUI.Label(
                    new Rect(EsuHudLayout.Scale(8f), EsuHudLayout.Scale(6f), rect.width - EsuHudLayout.Scale(16f), EsuHudLayout.Scale(24f)),
                    new GUIContent(" " + _previewReason, DecorationEditorIconCatalog.Get(AutomationTargetIconKey(_previewTarget))),
                    DecorationEditorTheme.SubHeader);
                float textX = EsuHudLayout.Scale(126f);
                float textWidth = rect.width - textX - EsuHudLayout.Scale(10f);
                GUI.Label(
                    new Rect(textX, EsuHudLayout.Scale(38f), textWidth, EsuHudLayout.Scale(24f)),
                    _previewTarget.Label,
                    DecorationEditorTheme.Body);
                GUI.Label(
                    new Rect(textX, EsuHudLayout.Scale(64f), textWidth, EsuHudLayout.Scale(28f)),
                    AutomationTargetCatalog.CategoryLabel(_previewTarget.Category) + " | " + FormatCell(_previewTarget.LocalPosition),
                    DecorationEditorTheme.Mini);
                GUI.Label(
                    new Rect(textX, EsuHudLayout.Scale(94f), textWidth, EsuHudLayout.Scale(44f)),
                    AutomationTargetCatalog.RoleLabel(_previewTarget),
                    DecorationEditorTheme.MiniWrap);
                GUI.EndGroup();
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private static Vector2 PreviewCardAnchor(Vector2 mouse, Rect sourceScreenRect)
        {
            if (sourceScreenRect.width <= 1f || sourceScreenRect.height <= 1f)
                return mouse;

            return new Vector2(
                Mathf.Max(mouse.x, sourceScreenRect.xMax),
                Mathf.Clamp(mouse.y, sourceScreenRect.yMin, sourceScreenRect.yMax));
        }

        private void DrawBlocksLoweringPanel()
        {
            GUILayout.Space(EsuHudLayout.Scale(8f));
            DrawCompactIconHeader("Native lowering", "settings", DecorationEditorTheme.SubHeader);
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            bool nativeExact = _blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact;
            GUILayout.Label(
                nativeExact
                    ? "Native exact import: these blocks mirror the selected vanilla Breadboard graph. Edits should preserve manual native work; semantic Check/Apply is for ESU recipe workspaces."
                    : "Check validates the native plan without mutation. Apply lowers through Breadboard nodes and tracks generated component ids for Revert.",
                DecorationEditorTheme.MiniWrap);
            if (nativeExact && !string.IsNullOrWhiteSpace(_blockWorkspace.NativeImportStatus))
                GUILayout.Label(_blockWorkspace.NativeImportStatus, DecorationEditorTheme.Body);
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent(
                        "Refresh from native",
                        DecorationEditorIconCatalog.Get("reload"),
                        "Rebuild ESU Blocks from the selected controller's live native Breadboard graph."),
                    _confirmNativeRefreshFromNative ? DecorationEditorTheme.Warning : DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(132f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                RefreshBlocksFromNative();
            bool previousSemanticEnabled = GUI.enabled;
            GUI.enabled = previousSemanticEnabled && !nativeExact;
            if (AutomationGUILayoutButton(
                    new GUIContent("Check blocks", DecorationEditorIconCatalog.Get("risk"), "Validate ESU Blocks and preview native lowering."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(98f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                CheckEsuBlocks();
            if (AutomationGUILayoutButton(
                    new GUIContent("Apply blocks", DecorationEditorIconCatalog.Get("save"), "Lower ESU Blocks into native Breadboard nodes."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(98f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                ApplyEsuBlocks();
            GUI.enabled = previousSemanticEnabled;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool previous = GUI.enabled;
            GUI.enabled = previous && CanRevertLastAutomationCompile();
            if (AutomationGUILayoutButton(
                    new GUIContent("Revert blocks", DecorationEditorIconCatalog.Get("cancel"), "Remove native nodes generated by the last ESU Blocks apply."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(116f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                RevertEsuBlocks();
            GUI.enabled = previous;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (_blockLoweringPlan != null)
            {
                int totalSteps = _blockLoweringPlan.Steps?.Count ?? 0;
                int visibleSteps = Math.Min(totalSteps, BlockLoweringVisibleStepLimit);
                GUILayout.Label(
                    "Plan: " +
                    _blockLoweringPlan.Summary +
                    " Showing " +
                    visibleSteps.ToString(CultureInfo.InvariantCulture) +
                    "/" +
                    totalSteps.ToString(CultureInfo.InvariantCulture) +
                    " native lowering step(s).",
                    DecorationEditorTheme.Body);
                GUILayout.Label(
                    "Graph complete: yes for the supported starter/native-wrapper slice. Check mutated no native data.",
                    DecorationEditorTheme.MiniWrap);
                if (_blockLoweringPlan.HasSemanticFlow)
                {
                    GUILayout.Label(
                        "Reads: " +
                        _blockLoweringPlan.ReadTargetLabel +
                        " / " +
                        LoweringPlanPropertyLabel(_blockLoweringPlan.ReadProperty, "Choose Getter property") +
                        ". Writes: " +
                        _blockLoweringPlan.OutputTargetLabel +
                        " / " +
                        LoweringPlanPropertyLabel(_blockLoweringPlan.OutputProperty, "Choose Setter property") +
                        ".",
                        DecorationEditorTheme.MiniWrap);
                }

                if (_blockLoweringPlan.HasNativeComponentRequests)
                {
                    GUILayout.Label(
                        "Advanced native wrappers: " +
                        _blockLoweringPlan.NativeComponentRequests.Count.ToString(CultureInfo.InvariantCulture) +
                        " advertised FtD component(s) will be created only on Apply.",
                        DecorationEditorTheme.MiniWrap);
                }

                foreach (string step in (_blockLoweringPlan.Steps ?? Array.Empty<string>()).Take(BlockLoweringVisibleStepLimit))
                    GUILayout.Label("- " + step, DecorationEditorTheme.MiniWrap);
                if (totalSteps > visibleSteps)
                {
                    GUILayout.Label(
                        "+" +
                        (totalSteps - visibleSteps).ToString(CultureInfo.InvariantCulture) +
                        " more native lowering step(s) hidden in this compact HUD. Check is still preview-only; Apply performs native writes.",
                        DecorationEditorTheme.MiniWrap);
                }
            }

            if (!string.IsNullOrWhiteSpace(_blockLoweringStatus))
            {
                GUILayout.Label(
                    _blockLoweringStatus,
                    _blockLoweringStatus.IndexOf("passed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    _blockLoweringStatus.IndexOf("Applied", StringComparison.OrdinalIgnoreCase) >= 0
                        ? DecorationEditorTheme.Body
                        : DecorationEditorTheme.Warning);
            }
            GUILayout.EndVertical();
        }

        private void DrawBlocksSystemBlockPanel()
        {
            GUILayout.Space(EsuHudLayout.Scale(8f));
            DrawCompactIconHeader("System Blocks", "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            GUILayout.Label("Select ESU Blocks and collapse them into a reusable System Block. It stays ESU metadata until checked/applied into native Breadboard nodes.", DecorationEditorTheme.MiniWrap);
            if (AutomationGUILayoutButton(
                    new GUIContent("Collapse to System Block", DecorationEditorIconCatalog.Get("duplicate"), "Create a reusable System Block from selected ESU Blocks."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                CollapseEsuBlocksToSystemBlock();
            if (AutomationGUILayoutButton(
                    new GUIContent("Open Systems", DecorationEditorIconCatalog.Get("open"), "Open Advanced System Block template tools."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
                _editorPage = AutomationEditorPage.System;
            DrawSystemBlockTemplateList();
            GUILayout.EndVertical();
        }

        private void DrawBlocksSystemBlockFlyout()
        {
            GUILayout.Label(
                "Collapse snapped ESU Blocks into a reusable System Block. Loose grid blocks stay scratch-only until snapped.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Collapse", DecorationEditorIconCatalog.Get("duplicate"), "Create a reusable System Block from snapped ESU Blocks."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
                CollapseEsuBlocksToSystemBlock();
            if (AutomationGUILayoutButton(
                    new GUIContent("Open Systems", DecorationEditorIconCatalog.Get("open"), "Open Advanced System Block template tools."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
                _editorPage = AutomationEditorPage.System;
            GUILayout.EndHorizontal();
            DrawSystemBlockTemplateList();
        }

        private void EnsureBlockWorkspace()
        {
            string controllerKey = _selectedController?.StableKey ?? string.Empty;
            if (_blockWorkspace == null ||
                !string.Equals(_blockWorkspaceControllerKey, controllerKey, StringComparison.Ordinal))
            {
                if (TryCreateNativeExactWorkspaceFromSelectedController(
                        out AutomationBlockWorkspace importedWorkspace,
                        out string importMessage))
                {
                    _blockWorkspace = importedWorkspace;
                    _blockWorkspaceControllerKey = controllerKey;
                    _blockLoweringPlan = null;
                    _blockLoweringStatus = importMessage;
                    _status = importMessage;
                    RefreshDirectionalBlockTargets();
                    return;
                }

                _blockWorkspace = AutomationBlockWorkspace.CreateDefault(
                    controllerKey,
                    _selectedController?.Label ?? "Automation controller",
                    SelectedLinks.Select(link => link.Target).Where(target => target != null).ToArray());
                _blockWorkspaceControllerKey = controllerKey;
                _blockLoweringPlan = null;
                _blockLoweringStatus = "ESU Blocks canvas is empty. Drag blocks onto the grid; drop near forever to make them executable.";
                if (!string.IsNullOrWhiteSpace(importMessage))
                    _blockLoweringStatus = importMessage + " New ESU semantic workspace opened instead.";
                RefreshDirectionalBlockTargets();
                return;
            }

            _blockWorkspace.ReplaceTargets(
                SelectedLinks.Select(link => link.Target).Where(target => target != null).ToArray());
            RefreshDirectionalBlockTargets();
        }

        private bool TryCreateNativeExactWorkspaceFromSelectedController(
            out AutomationBlockWorkspace workspace,
            out string message)
        {
            workspace = null;
            message = null;
            if (_selectedController == null ||
                !IsBreadboardController(_selectedController.Controller))
            {
                return false;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string inspectorMessage))
            {
                message = inspectorMessage ?? "Selected controller does not expose a native Breadboard graph.";
                return false;
            }

            if (!inspector.TryCaptureNativeGraphSnapshot(
                    out AutomationNativeGraphSnapshot snapshot,
                    out string importMessage))
            {
                message = importMessage;
                return false;
            }

            if (snapshot == null || !snapshot.HasNativeGraph)
            {
                message = "Native exact import found no vanilla Breadboard components.";
                return false;
            }

            workspace = AutomationBlockWorkspace.FromNativeGraphSnapshot(
                _selectedController.StableKey,
                _selectedController.Label,
                snapshot);
            message = importMessage;
            return true;
        }

        private void RefreshBlocksFromNative()
        {
            if (_selectedController == null)
            {
                _status = "Select a Breadboard controller before refreshing ESU Blocks from native.";
                return;
            }

            if (HasUnsavedAutomationWorkspaceChanges() &&
                !_confirmNativeRefreshFromNative)
            {
                _confirmNativeRefreshFromNative = true;
                _status = "Refresh from native will discard unsaved ESU Blocks layout. Click Refresh from native again to confirm.";
                InfoStore.Add(_status);
                return;
            }

            DiscardUnsavedAutomationWorkspaceStaging();
            if (!TryCreateNativeExactWorkspaceFromSelectedController(
                    out AutomationBlockWorkspace importedWorkspace,
                    out string importMessage))
            {
                _confirmNativeRefreshFromNative = false;
                _status = importMessage ?? "No native Breadboard graph was available to import.";
                _blockLoweringStatus = _status;
                return;
            }

            _blockWorkspace = importedWorkspace;
            _blockWorkspaceControllerKey = _selectedController.StableKey;
            _blockLoweringPlan = null;
            _blockLoweringStatus = importMessage;
            _automationNativeDirty = false;
            _confirmNativeRefreshFromNative = false;
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
            _status = importMessage + " Save to keep this ESU layout; vanilla behavior already lives in the native Breadboard.";
        }

        private bool TryMoveNativeExactWorkspaceBlock(
            AutomationBlockNode node,
            AutomationBlockCanvasPosition dropPosition,
            out bool handled,
            out string message)
        {
            handled = IsNativeExactImportedNode(node);
            message = null;
            if (!handled)
                return false;

            if (!TryFindNativeExactComponent(
                    node,
                    out AutomationBreadboardInspector inspector,
                    out AutomationBreadboardComponentSummary component,
                    out message))
            {
                return false;
            }

            float displayScale = _blockWorkspace?.NativeDisplayScale ?? 1f;
            if (float.IsNaN(displayScale) || float.IsInfinity(displayScale) || displayScale <= 0.05f)
                displayScale = 1f;
            float deltaX = (dropPosition.X - node.CanvasPosition.X) / displayScale;
            float deltaY = (dropPosition.Y - node.CanvasPosition.Y) / displayScale;
            if (Math.Abs(deltaX) < 0.01f && Math.Abs(deltaY) < 0.01f)
            {
                message = "Native component position unchanged.";
                return true;
            }

            return inspector.TryMoveComponent(component, deltaX, deltaY, out message);
        }

        private bool TryDeleteNativeExactWorkspaceBlock(
            AutomationBlockNode node,
            out bool handled,
            out string message)
        {
            handled = IsNativeExactImportedNode(node);
            message = null;
            if (!handled)
                return false;

            if (!TryFindNativeExactComponent(
                    node,
                    out AutomationBreadboardInspector inspector,
                    out AutomationBreadboardComponentSummary component,
                    out message))
            {
                return false;
            }

            uint deletedId = component.UniqueId;
            if (!inspector.TryDeleteComponent(component, out message))
                return false;

            ClearBreadboardComponentTransientState(deletedId);
            return true;
        }

        private bool TryFindNativeExactComponent(
            AutomationBlockNode node,
            out AutomationBreadboardInspector inspector,
            out AutomationBreadboardComponentSummary component,
            out string message)
        {
            inspector = null;
            component = null;
            message = null;

            if (!IsNativeExactImportedNode(node))
            {
                message = "Select an imported native Breadboard block first.";
                return false;
            }

            if (node.NativeComponentId == 0U)
            {
                message = "This native block has only a fingerprint identity; FtD command edits require a component id.";
                return false;
            }

            if (_selectedController == null)
            {
                message = "Select a Breadboard controller before editing native blocks.";
                return false;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out inspector,
                    out string inspectorMessage))
            {
                message = inspectorMessage ?? "Selected controller does not expose a native Breadboard graph.";
                return false;
            }

            component = FindComponentById(inspector.Components, node.NativeComponentId);
            if (component == null)
            {
                message = "The imported native component is no longer present; refresh ESU Blocks from native.";
                return false;
            }

            return true;
        }

        private bool IsNativeExactImportedNode(AutomationBlockNode node) =>
            _blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact &&
            node != null &&
            node.Kind == AutomationBlockKind.NativeComponent &&
            node.NativeImported;

        private void RefreshNativeExactWorkspaceAfterNativeCommand(string nativeMessage)
        {
            string controllerKey = _selectedController?.StableKey ?? string.Empty;
            string statusPrefix = string.IsNullOrWhiteSpace(nativeMessage)
                ? "Native Breadboard command applied."
                : nativeMessage;

            if (TryCreateNativeExactWorkspaceFromSelectedController(
                    out AutomationBlockWorkspace importedWorkspace,
                    out string importMessage))
            {
                _blockWorkspace = importedWorkspace;
                _blockWorkspaceControllerKey = controllerKey;
                _blockLoweringPlan = null;
                _blockLoweringStatus = statusPrefix + " " + importMessage;
                _automationNativeDirty = false;
                _confirmNativeRefreshFromNative = false;
                RefreshDirectionalBlockTargets();
                PersistSelectedAutomationWorkspaceState(saveProfile: false);
                _status = _blockLoweringStatus;
                return;
            }

            _blockWorkspace = AutomationBlockWorkspace.FromNativeGraphSnapshot(
                controllerKey,
                _selectedController?.Label ?? "Automation controller",
                new AutomationNativeGraphSnapshot(
                    _selectedController?.Label ?? "Automation controller",
                    Array.Empty<AutomationNativeComponentSnapshot>(),
                    Array.Empty<AutomationNativeWireSnapshot>()));
            _blockWorkspaceControllerKey = controllerKey;
            _blockLoweringPlan = null;
            _blockLoweringStatus = statusPrefix + " " + (importMessage ?? "Native Breadboard graph is now empty.");
            _automationNativeDirty = false;
            _confirmNativeRefreshFromNative = false;
            RefreshDirectionalBlockTargets();
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
            _status = _blockLoweringStatus;
        }

        private bool TryApplyContextualStarterTemplate()
        {
            if (_blockWorkspace == null)
                return false;

            AutomationTarget input = SelectedLinks
                .Where(link => link.Direction == AutomationLinkDirection.Input)
                .Select(link => link.Target)
                .FirstOrDefault(target => target?.Category == AutomationTargetCategory.TurretsWeapons);
            AutomationTarget output = SelectedLinks
                .Where(link => link.Direction == AutomationLinkDirection.Output)
                .Select(link => link.Target)
                .FirstOrDefault(target => target?.Category == AutomationTargetCategory.Spinblocks);
            return _blockWorkspace.TryApplyAmmoThresholdStarterTemplate(input, output);
        }

        private void RefreshDirectionalBlockTargets()
        {
            if (_blockWorkspace == null)
                return;

            foreach (AutomationBlockNode node in _blockWorkspace.Nodes)
            {
                if (node.Kind == AutomationBlockKind.ReadTarget)
                    EnsureBlockNodeTarget(node, AutomationLinkDirection.Input);
                else if (node.Kind == AutomationBlockKind.SetTarget)
                    EnsureBlockNodeTarget(node, AutomationLinkDirection.Output);
            }
        }

        private void EnsureBlockNodeTarget(
            AutomationBlockNode node,
            AutomationLinkDirection direction)
        {
            if (node == null)
                return;

            node.LinkDirection = direction;
            AutomationTarget[] options = BlockTargetOptions(node.Kind);
            if (options.Length == 0)
                return;

            if (options.Any(target => string.Equals(target.StableKey, node.TargetKey, StringComparison.Ordinal)))
                return;

            AutomationTarget persisted = options.FirstOrDefault(target =>
                !string.IsNullOrWhiteSpace(node.TargetPersistenceKey) &&
                string.Equals(target.PersistenceKey, node.TargetPersistenceKey, StringComparison.Ordinal));
            if (persisted != null &&
                _blockWorkspace?.RebindNodeTarget(node.Id, persisted) == true)
            {
                return;
            }

            _blockWorkspace?.SetNodeTarget(node.Id, options[0], direction);
        }

        private void MoveSelectedEsuBlock(int delta)
        {
            EnsureBlockWorkspace();
            if (_blockWorkspace.MoveSelected(delta))
            {
                InvalidateEsuBlockPlan();
                _status = delta < 0
                    ? "Moved selected ESU Block up in the forever stack."
                    : "Moved selected ESU Block down in the forever stack.";
                AutomationUiSound.PlaySnap();
            }
            else
            {
                _status = "Only snapped ESU Blocks inside forever can move up or down.";
            }
        }

        private void RemoveSelectedEsuBlock()
        {
            EnsureBlockWorkspace();
            AutomationBlockNode selectedNode = _blockWorkspace.SelectedNode();
            if (TryDeleteNativeExactWorkspaceBlock(
                    selectedNode,
                    out bool nativeDeleteHandled,
                    out string nativeDeleteMessage))
            {
                RefreshNativeExactWorkspaceAfterNativeCommand(nativeDeleteMessage);
                return;
            }

            if (nativeDeleteHandled)
            {
                _status = nativeDeleteMessage ?? "Could not delete native Breadboard component.";
                return;
            }

            if (_blockWorkspace.RemoveSelected())
            {
                InvalidateEsuBlockPlan();
                _status = "Removed selected ESU Block.";
            }
        }

        private void CycleAutomationBlockTarget(AutomationBlockNode node)
        {
            if (node == null)
                return;

            EnsureBlockWorkspace();
            AutomationTarget[] options = BlockTargetOptions(node.Kind);
            if (options.Length == 0)
            {
                _status = "Link a target before assigning this ESU Block.";
                return;
            }

            int current = Array.FindIndex(options, target =>
                string.Equals(target.StableKey, node.TargetKey, StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(node.TargetPersistenceKey) &&
                string.Equals(target.PersistenceKey, node.TargetPersistenceKey, StringComparison.Ordinal));
            int next = PositiveModulo(current + 1, options.Length);
            _blockWorkspace?.SetNodeTarget(node.Id, options[next], node.Kind == AutomationBlockKind.ReadTarget
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output);
            InvalidateEsuBlockPlan();
            _status = node.Label + " now uses " + options[next].Label + ". Choose its property before Check.";
        }

        private AutomationTarget[] BlockTargetOptions(AutomationBlockKind kind)
        {
            AutomationLinkDirection direction = kind == AutomationBlockKind.ReadTarget
                ? AutomationLinkDirection.Input
                : AutomationLinkDirection.Output;
            IEnumerable<AutomationTarget> targets = SelectedLinks
                .Where(link => link.Direction == direction)
                .Select(link => link.Target)
                .Where(target => target != null);
            if (kind == AutomationBlockKind.SetTarget)
                targets = targets.Where(AutomationTargetCatalog.IsBreadboardWritableTarget);
            else if (kind == AutomationBlockKind.ReadTarget)
                targets = targets.Where(AutomationTargetCatalog.IsBreadboardReadableTarget);

            return targets.ToArray();
        }

        private AutomationLink LinkForBlockNode(
            AutomationBlockNode node,
            AutomationLinkDirection direction)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.TargetKey))
                return null;

            return SelectedLinks.FirstOrDefault(link =>
                link.Direction == direction &&
                (string.Equals(link.TargetKey, node.TargetKey, StringComparison.Ordinal) ||
                 !string.IsNullOrWhiteSpace(node.TargetPersistenceKey) &&
                 string.Equals(link.TargetPersistenceKey, node.TargetPersistenceKey, StringComparison.Ordinal)));
        }

        private void CycleAutomationCompareOperator(AutomationBlockNode node)
        {
            if (node == null)
                return;

            int value = PositiveModulo((int)node.Operator + 1, Enum.GetValues(typeof(AutomationCompareOperator)).Length);
            node.Operator = (AutomationCompareOperator)value;
            InvalidateEsuBlockPlan();
        }

        private static string CompareOperatorLabel(AutomationCompareOperator compareOperator)
        {
            switch (compareOperator)
            {
                case AutomationCompareOperator.LessThan:
                    return "<";
                case AutomationCompareOperator.EqualOrGreater:
                    return ">=";
                case AutomationCompareOperator.EqualOrLess:
                    return "<=";
                default:
                    return ">";
            }
        }

        private void DrawEsuBlockFloatStepper(
            string label,
            float value,
            float step,
            Action<float> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
            if (AutomationGUILayoutButton(
                    new GUIContent("-", "Decrease " + label + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                apply?.Invoke(value - Mathf.Max(0.001f, step));
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (AutomationGUILayoutButton(
                    new GUIContent("+", "Increase " + label + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                apply?.Invoke(value + Mathf.Max(0.001f, step));
            GUILayout.EndHorizontal();
        }

        private void InvalidateEsuBlockPlan()
        {
            _blockLoweringPlan = null;
            if (_blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                _blockLoweringStatus = "Native exact ESU layout changed. Save to keep this imported layout; vanilla Breadboard behavior is not rewritten.";
                _automationNativeDirty = false;
            }
            else
            {
                _blockLoweringStatus = "Block stack changed; Check ESU Blocks again before applying.";
                _automationNativeDirty = true;
            }
            _confirmNativeRefreshFromNative = false;
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
        }

        private void InvalidateAutomationLinksChanged(string status)
        {
            _blockLoweringPlan = null;
            _blockLoweringStatus = "Linked targets changed; Check ESU Blocks again before applying.";
            _automationNativeDirty = true;
            CloseAutomationPropertyPicker();
            CloseAutomationTargetPicker();
            if (_blockWorkspace != null)
            {
                _blockWorkspace.ReplaceTargets(
                    SelectedLinks.Select(link => link.Target).Where(target => target != null).ToArray());
                RefreshDirectionalBlockTargets();
            }

            _status = string.IsNullOrWhiteSpace(status)
                ? _blockLoweringStatus
                : status;
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
        }

        private bool CheckEsuBlocks()
        {
            EnsureBlockWorkspace();
            if (_blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                _blockLoweringPlan = null;
                _blockLoweringStatus = "Native exact import already mirrors vanilla Breadboard nodes. Use Refresh from native or Advanced Graph for native edits; semantic Check/Apply is disabled for imported graphs.";
                _status = _blockLoweringStatus;
                return false;
            }

            if (!IsBreadboardController(_selectedController?.Controller))
            {
                _blockLoweringStatus = "ESU Blocks currently require a Breadboard controller.";
                _status = _blockLoweringStatus;
                return false;
            }

            string directionMessage;
            if (!ValidateBlockLinkDirections(out directionMessage))
            {
                _blockLoweringPlan = null;
                _blockLoweringStatus = directionMessage;
                _status = directionMessage;
                return false;
            }

            bool ok = AutomationBlockLowering.CheckBlocksToNative(
                _blockWorkspace,
                out AutomationLoweringPlan plan,
                out string message);
            _blockLoweringPlan = ok ? plan : null;
            _blockLoweringStatus = message;
            _status = message;
            if (ok && plan != null)
                _automationCodeOutputTargetKey = plan.OutputTargetKey;
            return ok;
        }

        private bool ValidateBlockLinkDirections(out string message)
        {
            message = null;
            if (_blockWorkspace == null)
            {
                message = "Open an ESU Blocks workspace before checking native lowering.";
                return false;
            }

            AutomationBlockNode read = _blockWorkspace.FirstExecutableNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode set = _blockWorkspace.FirstExecutableNode(AutomationBlockKind.SetTarget);
            if (_blockWorkspace.HasNativeComponentRequests &&
                !_blockWorkspace.HasConfiguredSemanticFlow)
            {
                return true;
            }

            if (read != null && LinkForBlockNode(read, AutomationLinkDirection.Input) == null)
            {
                message = "Read target must use an Input link. Click a target first, then click the Breadboard/controller.";
                return false;
            }

            if (read != null && !HasNativePropertyBinding(read))
            {
                message = "Read target block needs an explicit native Getter property before checking.";
                return false;
            }

            if (set != null && LinkForBlockNode(set, AutomationLinkDirection.Output) == null)
            {
                message = "Set target must use an Output link. Click the Breadboard/controller first, then click the target it affects.";
                return false;
            }

            if (set != null && !HasNativePropertyBinding(set))
            {
                message = "Set target block needs an explicit native Setter property before checking.";
                return false;
            }

            return true;
        }

        private static bool HasNativePropertyBinding(AutomationBlockNode node) =>
            node != null &&
            node.PropertyBindingMode == AutomationProxyPropertyBindingMode.Explicit &&
            node.PropertySelection != null &&
            !node.PropertySelection.IsClear;

        private void ApplyEsuBlocks()
        {
            if (_blockWorkspace?.Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                _blockLoweringPlan = null;
                _blockLoweringStatus = "Native exact imports preserve manual vanilla Breadboard work and are not rewritten by Apply blocks.";
                _status = _blockLoweringStatus;
                return;
            }

            if (_blockLoweringPlan == null && !CheckEsuBlocks())
                return;

            if (_blockLoweringPlan == null)
            {
                _blockLoweringStatus = "Check ESU Blocks before applying native lowering.";
                _status = _blockLoweringStatus;
                return;
            }

            _automationCodeControllerKey = _selectedController?.StableKey ?? string.Empty;
            _automationCodeOutputTargetKey = _blockLoweringPlan.OutputTargetKey;
            _automationCodeText = _blockLoweringPlan.ToNativeCode();
            bool applied = true;
            bool inputBindingOk = true;
            string inputBindingMessage = string.Empty;
            if (_blockLoweringPlan.HasDirectValueFlow)
            {
                applied = TryApplyDirectReadSetBlocks(_blockLoweringPlan, out inputBindingMessage);
                inputBindingOk = applied;
                if (!applied)
                    _status = inputBindingMessage;
            }
            else if (_blockLoweringPlan.HasSemanticFlow)
            {
                applied = CompileAutomationCodeExpression(returnToGraph: false);
                if (applied)
                    inputBindingOk = TryBindEsuBlockInputGetter(_blockLoweringPlan, out inputBindingMessage);
            }
            else
            {
                _lastCompileBoundOutput = false;
            }

            _editorPage = AutomationEditorPage.Blocks;
            if (applied)
            {
                bool nativeApplied = TryApplyNativeComponentBlocks(_blockLoweringPlan, out string nativeComponentMessage);
                if (nativeApplied)
                {
                    _automationNativeDirty = false;
                    PersistSelectedAutomationWorkspaceState(saveProfile: true);
                    string revertSummary = CanRevertLastAutomationCompile()
                        ? " Revert blocks can remove the generated component ids."
                        : " No new native proxy ids were generated for Revert.";
                    _blockLoweringStatus = inputBindingOk
                        ? "Applied ESU Blocks to native Breadboard nodes." +
                          revertSummary +
                          (string.IsNullOrWhiteSpace(inputBindingMessage) ? string.Empty : " " + inputBindingMessage) +
                          (string.IsNullOrWhiteSpace(nativeComponentMessage) ? string.Empty : " " + nativeComponentMessage)
                        : "Applied native Breadboard nodes, but ESU Blocks input binding needs attention: " +
                          inputBindingMessage +
                          revertSummary +
                          (string.IsNullOrWhiteSpace(nativeComponentMessage) ? string.Empty : " " + nativeComponentMessage);
                    _status = _blockLoweringStatus;
                }
                else
                {
                    _blockLoweringStatus = nativeComponentMessage;
                    _status = _blockLoweringStatus;
                }
            }
            else
            {
                _blockLoweringStatus = _status;
            }
        }

        private bool TryApplyDirectReadSetBlocks(
            AutomationLoweringPlan plan,
            out string message)
        {
            message = string.Empty;
            _lastCompileBoundOutput = false;
            _lastCompileRevert = null;
            if (plan == null || !plan.HasDirectValueFlow)
            {
                message = "Direct Read -> Set lowering requires a checked direct value plan.";
                return false;
            }

            AutomationTarget readTarget = PlanLinkedTarget(plan.ReadTargetKey, AutomationLinkDirection.Input);
            AutomationTarget setTarget = PlanLinkedTarget(plan.OutputTargetKey, AutomationLinkDirection.Output);
            if (readTarget == null)
            {
                message = "Direct Read -> Set apply failed: the linked input target is no longer live.";
                return false;
            }

            if (setTarget == null)
            {
                message = "Direct Read -> Set apply failed: the linked output target is no longer live.";
                return false;
            }

            if (!TryCreateSelectedBreadboardInspector(
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                message = "Direct Read -> Set apply failed: " +
                          (reason ?? "selected controller does not expose a native Breadboard") +
                          ".";
                return false;
            }

            var generatedIds = new List<uint>();
            if (!TryEnsureDirectFlowProxy(
                    inspector,
                    readTarget,
                    plan.ReadProperty,
                    getter: true,
                    out AutomationBreadboardComponentSummary getter,
                    out IReadOnlyList<uint> getterIds,
                    out message))
            {
                TrackPartialDirectFlowRevert(generatedIds);
                return false;
            }
            generatedIds.AddRange(getterIds ?? Array.Empty<uint>());

            if (!TryEnsureDirectFlowProxy(
                    inspector,
                    setTarget,
                    plan.OutputProperty,
                    getter: false,
                    out AutomationBreadboardComponentSummary setter,
                    out IReadOnlyList<uint> setterIds,
                    out message))
            {
                TrackPartialDirectFlowRevert(generatedIds);
                return false;
            }
            generatedIds.AddRange(setterIds ?? Array.Empty<uint>());

            AutomationBreadboardPortSummary getterOutput =
                inspector.OutputPorts(getter, 1).FirstOrDefault();
            AutomationBreadboardPortSummary setterInput =
                inspector.InputPorts(setter, 4).FirstOrDefault();
            if (getterOutput == null)
            {
                TrackPartialDirectFlowRevert(generatedIds);
                message = "Direct Read -> Set apply failed: the selected Generic Getter exposes no output port.";
                return false;
            }

            if (setterInput == null)
            {
                TrackPartialDirectFlowRevert(generatedIds);
                message = "Direct Read -> Set apply failed: the selected Generic Setter exposes no value input port.";
                return false;
            }

            if (!inspector.TryConnectPorts(
                    getter,
                    getterOutput.Index,
                    setter,
                    setterInput.Index,
                    out string wireMessage))
            {
                TrackPartialDirectFlowRevert(generatedIds);
                message = "Direct Read -> Set apply failed: " +
                          (wireMessage ?? "FtD rejected the native getter-to-setter wire") +
                          ".";
                return false;
            }

            uint[] ids = generatedIds
                .Where(id => id != 0U)
                .Distinct()
                .ToArray();
            if (ids.Length > 0)
            {
                _lastCompileRevert = new AutomationCompileRevertSet(
                    _selectedController?.StableKey,
                    new AutomationBreadboardCompileResult("direct read-to-set proxy flow", ids));
            }

            _lastCompileBoundOutput = true;
            message =
                "Direct Read -> Set wired " +
                readTarget.Label +
                " / " +
                LoweringPlanPropertyLabel(plan.ReadProperty, "Getter property") +
                " to " +
                setTarget.Label +
                " / " +
                LoweringPlanPropertyLabel(plan.OutputProperty, "Setter property") +
                " using native Generic Getter/Setter ports" +
                (ids.Length > 0
                    ? "; generated " + ids.Length.ToString(CultureInfo.InvariantCulture) + " proxy node(s)."
                    : "; reused existing proxy node(s).");
            return true;
        }

        private AutomationTarget PlanLinkedTarget(
            string targetKey,
            AutomationLinkDirection direction)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
                return null;

            return SelectedLinks
                .Where(link => link.Direction == direction)
                .Select(link => link.Target)
                .FirstOrDefault(target =>
                    target != null &&
                    string.Equals(target.StableKey, targetKey, StringComparison.Ordinal));
        }

        private bool TryEnsureDirectFlowProxy(
            AutomationBreadboardInspector inspector,
            AutomationTarget target,
            AutomationProxyPropertySelection selection,
            bool getter,
            out AutomationBreadboardComponentSummary proxy,
            out IReadOnlyList<uint> generatedIds,
            out string message)
        {
            proxy = null;
            generatedIds = Array.Empty<uint>();
            message = null;
            if (inspector == null || target == null)
            {
                message = "Direct flow proxy selection failed because the native board or target is missing.";
                return false;
            }

            if (selection == null || selection.IsClear)
            {
                message = "Direct flow proxy selection failed because " +
                          (getter ? "the Getter" : "the Setter") +
                          " property is not explicit.";
                return false;
            }

            AutomationBreadboardComponentSummary[] proxies = getter
                ? TargetGettersFor(inspector.Components, target)
                : TargetSettersFor(inspector.Components, target);
            proxy = proxies
                .Where(component => ProxyMatchesSelection(component, selection))
                .FirstOrDefault(component => getter
                    ? inspector.OutputPorts(component, 1).Count > 0
                    : inspector.InputPorts(component, 4).Count > 0);
            if (proxy != null)
                return true;

            if (!inspector.TryCreateTargetProxy(
                    target,
                    getter,
                    !getter,
                    out AutomationBreadboardCompileResult proxyResult,
                    out string proxyMessage))
            {
                message = "Could not create a Generic " +
                          (getter ? "Getter" : "Setter") +
                          " for " +
                          target.Label +
                          ": " +
                          (proxyMessage ?? "FtD rejected the native proxy") +
                          ".";
                return false;
            }

            generatedIds = proxyResult?.ComponentIds?.Where(id => id != 0U).ToArray() ?? Array.Empty<uint>();
            foreach (uint componentId in generatedIds)
            {
                AutomationBreadboardComponentSummary created = FindComponentById(inspector.Components, componentId);
                if (created == null ||
                    getter && !created.IsGenericGetter ||
                    !getter && !created.IsGenericSetter)
                {
                    continue;
                }

                if (!TryApplyProxyPropertySelection(inspector, created, selection, out string selectionMessage))
                {
                    message = "Created a Generic " +
                              (getter ? "Getter" : "Setter") +
                              " for " +
                              target.Label +
                              ", but could not select " +
                              LoweringPlanPropertyLabel(selection, "the requested property") +
                              ": " +
                              (selectionMessage ?? "property selection failed") +
                              ".";
                    return false;
                }

                bool hasPort = getter
                    ? inspector.OutputPorts(created, 1).Count > 0
                    : inspector.InputPorts(created, 4).Count > 0;
                if (!hasPort)
                {
                    message = "Created a Generic " +
                              (getter ? "Getter" : "Setter") +
                              " for " +
                              target.Label +
                              ", but its native value port was not visible.";
                    return false;
                }

                proxy = created;
                return true;
            }

            message = "Created a Generic " +
                      (getter ? "Getter" : "Setter") +
                      " for " +
                      target.Label +
                      ", but the generated proxy component was not visible.";
            return false;
        }

        private void TrackPartialDirectFlowRevert(IEnumerable<uint> generatedIds)
        {
            uint[] ids = generatedIds?
                .Where(id => id != 0U)
                .Distinct()
                .ToArray() ?? Array.Empty<uint>();
            if (ids.Length == 0)
                return;

            _lastCompileRevert = new AutomationCompileRevertSet(
                _selectedController?.StableKey,
                new AutomationBreadboardCompileResult("partial direct read-to-set proxy flow", ids));
        }

        private bool TryApplyNativeComponentBlocks(
            AutomationLoweringPlan plan,
            out string message)
        {
            message = string.Empty;
            if (plan == null || !plan.HasNativeComponentRequests)
                return true;

            if (!TryCreateSelectedBreadboardInspector(
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                message = "Native component blocks were skipped: " +
                          (reason ?? "selected controller does not expose a native Breadboard") +
                          ".";
                return false;
            }

            var createdIds = new List<uint>();
            var messages = new List<string>();
            foreach (AutomationNativeComponentRequest request in plan.NativeComponentRequests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.TypeName))
                    continue;

                if (!inspector.TryAddComponentTracked(
                        request.TypeName,
                        out AutomationBreadboardCompileResult result,
                        out string addMessage))
                {
                    uint[] partialIds = createdIds.Where(id => id != 0U).Distinct().ToArray();
                    if (partialIds.Length > 0)
                        AddGeneratedComponentIdsToLastCompileRevert(partialIds);
                    message = "Native component block apply failed for " +
                              request.Label +
                              ": " +
                              (addMessage ?? "FtD rejected the component") +
                              "." +
                              (partialIds.Length > 0
                                  ? " Revert blocks can remove " +
                                    partialIds.Length.ToString(CultureInfo.InvariantCulture) +
                                    " native component(s) created before the failure."
                                  : string.Empty);
                    return false;
                }

                createdIds.AddRange(result?.ComponentIds ?? Array.Empty<uint>());
                if (!string.IsNullOrWhiteSpace(addMessage))
                    messages.Add(addMessage);
            }

            uint[] ids = createdIds.Where(id => id != 0U).Distinct().ToArray();
            if (ids.Length > 0)
                AddGeneratedComponentIdsToLastCompileRevert(ids);

            message = ids.Length == 0
                ? "Native component blocks applied, but no component ids were reported."
                : "Created " + ids.Length.ToString(CultureInfo.InvariantCulture) +
                  " native component block(s)." +
                  (messages.Count == 0 ? string.Empty : " " + string.Join(" ", messages.Take(2).ToArray()));
            return true;
        }

        private bool TryBindEsuBlockInputGetter(
            AutomationLoweringPlan plan,
            out string message)
        {
            message = string.Empty;
            if (plan == null || _lastCompileRevert == null)
                return true;

            AutomationLink readLink = SelectedLinks.FirstOrDefault(link =>
                string.Equals(link.TargetKey, plan.ReadTargetKey, StringComparison.Ordinal));
            AutomationTarget readTarget = readLink?.Target;
            if (readTarget == null)
            {
                message = "Read target getter binding skipped because the linked target is no longer live.";
                return false;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                message = reason ?? "Read target getter binding skipped because the native board could not be reopened.";
                return false;
            }

            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            AutomationBreadboardComponentSummary[] getters = TargetGettersFor(components, readTarget);
            AutomationProxyPropertySelection readSelection = plan.ReadProperty;
            if (readSelection == null || readSelection.IsClear)
            {
                message = "Read target getter binding requires an explicit Getter property selection.";
                return false;
            }
            if (readSelection != null)
            {
                getters = getters
                    .Where(component => ProxyMatchesSelection(component, readSelection))
                    .ToArray();
            }
            IReadOnlyList<uint> getterComponentIds = Array.Empty<uint>();
            string proxyMessage = string.Empty;
            if (getters.Length == 0)
            {
                if (!inspector.TryCreateTargetProxy(
                        readTarget,
                        getter: true,
                        setter: false,
                        out AutomationBreadboardCompileResult proxyResult,
                        out proxyMessage))
                {
                    message = "Read target getter binding could not create a Generic Getter: " +
                              (proxyMessage ?? "FtD rejected the native proxy node.") +
                              ".";
                    return false;
                }

                getterComponentIds = proxyResult?.ComponentIds?.Where(id => id != 0U).ToArray() ?? Array.Empty<uint>();
                if (getterComponentIds.Count > 0)
                    AddGeneratedComponentIdsToLastCompileRevert(getterComponentIds);

                if (readSelection != null)
                {
                    bool propertyApplied = false;
                    foreach (uint componentId in getterComponentIds)
                    {
                        AutomationBreadboardComponentSummary created = FindComponentById(inspector.Components, componentId);
                        if (created != null && created.IsGenericGetter)
                        {
                            if (!TryApplyProxyPropertySelection(inspector, created, readSelection, out proxyMessage))
                            {
                                message = "Read target getter binding created a Generic Getter, but could not select the native property: " +
                                          (proxyMessage ?? "property selection failed") +
                                          ". Revert blocks can remove the generated getter.";
                                return false;
                            }

                            propertyApplied = true;
                            break;
                        }
                    }

                    if (!propertyApplied)
                    {
                        message = "Read target getter binding created a Generic Getter, but the generated component was not visible for property selection. Revert blocks can remove it.";
                        return false;
                    }
                }
                components = inspector.Components;
                getters = TargetGettersFor(components, readTarget);
                if (readSelection != null)
                {
                    getters = getters
                        .Where(component => ProxyMatchesSelection(component, readSelection))
                        .ToArray();
                }
            }

            AutomationBreadboardComponentSummary getter = getters.FirstOrDefault(item =>
                inspector.OutputPorts(item, 1).Count > 0);
            if (getter == null)
            {
                message = "Read target getter binding skipped because no Generic Getter output was visible.";
                return false;
            }

            int connected = 0;
            foreach (uint componentId in _lastCompileRevert.ComponentIds)
            {
                AutomationBreadboardComponentSummary component = FindComponentById(components, componentId);
                if (component == null || !component.IsEvaluator)
                    continue;

                AutomationBreadboardPortSummary input = inspector.InputPorts(component, 4).FirstOrDefault();
                if (input == null)
                    continue;

                if (inspector.TryConnectPorts(getter, 0, component, input.Index, out _))
                    connected++;
            }

            if (connected == 0)
            {
                message = "Read target getter is available, but generated evaluator input ports were not visible; inspect Advanced if the native graph needs manual wiring.";
                return false;
            }

            message =
                "Bound " +
                readLink.TargetLabel +
                " getter to " +
                connected.ToString(CultureInfo.InvariantCulture) +
                " generated evaluator input(s)" +
                (getterComponentIds.Count > 0 ? "; auto-created getter proxy." : ".") +
                (string.IsNullOrWhiteSpace(proxyMessage) ? string.Empty : " " + proxyMessage);
            return true;
        }

        private void RevertEsuBlocks()
        {
            RevertLastAutomationCompile();
            _blockLoweringStatus = _status;
            _editorPage = AutomationEditorPage.Blocks;
        }

        private void CollapseEsuBlocksToSystemBlock()
        {
            EnsureBlockWorkspace();
            AutomationSystemBlockDefinition definition = _blockWorkspace.CollapseSelectionToSystemBlock(
                "ESU " + (_selectedController?.Label ?? "Automation") + " System");
            if (definition.NodeIds.Count == 0)
            {
                _blockLoweringStatus = "Snap blocks into the executable stack before collapsing them into a System Block.";
                _status = _blockLoweringStatus;
                return;
            }

            string[] inputs = definition.InputPorts
                .Select(port => NormalizeSystemBlockPortName(port.Name))
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] outputs = definition.OutputPorts
                .Select(port => NormalizeSystemBlockPortName(port.Name))
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var template = new AutomationSystemBlockTemplate(
                definition.Name,
                _selectedController?.StableKey ?? string.Empty,
                _selectedController?.Label ?? "Controller",
                inputs,
                outputs,
                "Collapsed from ESU Blocks: " + definition.InternalSummary,
                "ESU Blocks: " + definition.InternalSummary);
            _systemBlockTemplates.Add(template);
            _activeSystemBlockTemplateIndex = _systemBlockTemplates.Count - 1;
            PersistSystemBlockTemplateLibrary();
            _blockLoweringStatus =
                "Created System Block '" +
                template.Name +
                "' with " +
                inputs.Length.ToString(CultureInfo.InvariantCulture) +
                " input port(s) and " +
                outputs.Length.ToString(CultureInfo.InvariantCulture) +
                " output port(s).";
            _status = _blockLoweringStatus;
        }

        private void DrawGraphEditor()
        {
            if (IsSystemBlockWorkspaceOpen())
            {
                DrawSystemBlockInternalGraphEditor();
                return;
            }

            GUILayout.Label("Controller", DecorationEditorTheme.SubHeader);
            DrawPanelNextStepPrompt();
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
            DrawSystemBlockHostNodes();
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

        private void DrawSystemBlockHostNodes()
        {
            GUILayout.Label("System Block nodes", DecorationEditorTheme.SubHeader);
            int[] controllerIndexes = Enumerable.Range(0, _systemBlockTemplates.Count)
                .Where(index => IsSystemBlockTemplateForSelectedController(_systemBlockTemplates[index]))
                .ToArray();
            if (controllerIndexes.Length > 0)
            {
                DrawSystemBlockHostNodeSearch();
                int[] matchingIndexes = controllerIndexes
                    .Where(index => SystemBlockHostNodeMatchesSearch(
                        _systemBlockTemplates[index],
                        _systemBlockHostNodeSearch))
                    .ToArray();
                GUILayout.Label(
                    "System Block nodes: " +
                    matchingIndexes.Length.ToString(CultureInfo.InvariantCulture) +
                    "/" +
                    controllerIndexes.Length.ToString(CultureInfo.InvariantCulture) +
                    " shown for selected controller.",
                    DecorationEditorTheme.MiniWrap);
                if (matchingIndexes.Length == 0)
                {
                    GUILayout.Label(
                        "No System Block nodes match the current graph-node search. Clear it to show reusable nested nodes for this controller.",
                        DecorationEditorTheme.Warning);
                    if (_openSystemBlockTemplateIndex >= 0 &&
                        controllerIndexes.Contains(_openSystemBlockTemplateIndex))
                    {
                        GUILayout.Label("Open System Block node is hidden by the current graph-node search.", DecorationEditorTheme.Warning);
                    }
                    return;
                }

                foreach (int index in matchingIndexes)
                    DrawSystemBlockHostNode(index, _systemBlockTemplates[index]);

                if (_openSystemBlockTemplateIndex >= 0 &&
                    controllerIndexes.Contains(_openSystemBlockTemplateIndex) &&
                    !matchingIndexes.Contains(_openSystemBlockTemplateIndex))
                {
                    GUILayout.Label("Open System Block node is hidden by the current graph-node search.", DecorationEditorTheme.Warning);
                }
                return;
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("No System Blocks yet", "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Create a System Block to collapse linked targets and native graph work into one visible nested node with named boundary ports.",
                DecorationEditorTheme.MiniWrap);
            if (AutomationGUILayoutButton(
                    new GUIContent("Create System Block", DecorationEditorIconCatalog.Get("duplicate"), "Open the System page and define a reusable nested block signature."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(154f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                EnsureSystemBlockDraft();
                _editorPage = AutomationEditorPage.System;
                _status = "Define named ports, Check, then Apply template to create a visible System Block node.";
            }
            GUILayout.EndVertical();
        }

        private void DrawSystemBlockHostNodeSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _systemBlockHostNodeSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _systemBlockHostNodeSearch, StringComparison.Ordinal))
                _systemBlockHostNodeSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_systemBlockHostNodeSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear System Block graph-node search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _systemBlockHostNodeSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search System Block graph nodes by name, controller, input/output ports, comment, or internal graph notes.", DecorationEditorTheme.MiniWrap);
        }

        private static bool SystemBlockHostNodeMatchesSearch(
            AutomationSystemBlockTemplate template,
            string search)
        {
            if (template == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (template.Name ?? string.Empty) + " " +
                (template.ControllerLabel ?? string.Empty) + " " +
                (template.ControllerKey ?? string.Empty) + " " +
                string.Join(" ", template.InputPorts ?? Array.Empty<string>()) + " " +
                string.Join(" ", template.OutputPorts ?? Array.Empty<string>()) + " " +
                (template.Comment ?? string.Empty) + " " +
                (template.InternalGraph ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawSystemBlockHostNode(int index, AutomationSystemBlockTemplate template)
        {
            if (template == null)
                return;

            bool active = index == _openSystemBlockTemplateIndex;
            GUILayout.BeginVertical(active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Panel);
            DrawCompactIconHeader(template.Name, "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Nested visible node | inputs " +
                template.InputPorts.Count.ToString(CultureInfo.InvariantCulture) +
                " | outputs " +
                template.OutputPorts.Count.ToString(CultureInfo.InvariantCulture) +
                " | lowering through native Graph/Code",
                DecorationEditorTheme.MiniWrap);
            DrawSystemBlockPortSummary("Inputs", template.InputPorts, "filter");
            DrawSystemBlockPortSummary("Outputs", template.OutputPorts, "anchor");
            GUILayout.Label(
                string.IsNullOrWhiteSpace(template.InternalGraph)
                    ? "Internal graph: default draft only. Enter the block to describe the nested plan."
                    : "Internal graph: saved ESU metadata. Enter the block to edit or lower behavior through Graph/Code.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Enter", DecorationEditorIconCatalog.Get("open"), "Open this System Block's internal graph workspace."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(68f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                EnterSystemBlockTemplate(index);
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Ports", DecorationEditorIconCatalog.Get("filter"), "Edit this System Block's exposed input/output ports."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(66f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                LoadSystemBlockTemplate(index);
                _editorPage = AutomationEditorPage.System;
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Code", DecorationEditorIconCatalog.Get("settings"), "Enter this System Block and use deterministic code lowering for native Breadboard nodes."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(66f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                EnterSystemBlockTemplate(index);
                _editorPage = AutomationEditorPage.Code;
                _status = "System Block Code page uses deterministic recipes and lowers into native Breadboard nodes.";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Check lowering", DecorationEditorIconCatalog.Get("risk"), "Check which exposed ports can lower to native Generic Getter/Setter proxies."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(122f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CheckSystemBlockNativeLowering(template);
            }
            if (AutomationGUILayoutButton(
                    new GUIContent("Apply proxies", DecorationEditorIconCatalog.Get("save"), "Create missing native Generic Getter/Setter proxy nodes for this System Block's linked ports."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(112f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                ApplySystemBlockNativeLowering(template);
            }
            bool previous = GUI.enabled;
            GUI.enabled = previous && CanRevertLastSystemBlockLowering();
            if (AutomationGUILayoutButton(
                    new GUIContent("Revert", DecorationEditorIconCatalog.Get("cancel"), "Remove the native proxy nodes created by the last System Block lowering apply."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                RevertLastSystemBlockNativeLowering();
            }
            GUI.enabled = previous;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
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

            GUILayout.Label(
                "Proxy Node shortcuts: create native GBG/GBS components now for this Target. Blocks/Code/System Apply-owned nodes remain the ones tracked by Revert compile.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Auto-pick is best effort; use the native property picker afterward if FtD exposes a different target property.",
                DecorationEditorTheme.MiniWrap);
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
            if (IsSystemBlockWorkspaceOpen())
                DrawSystemBlockCodeContextPanel();

            GUILayout.Label("Code node compiler", DecorationEditorTheme.SubHeader);
            DrawPanelNextStepPrompt();
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
            DrawAutomationCodeRecipeGuide();
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

        private void DrawAutomationCodeRecipeGuide()
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Recipe lowering", "settings", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Allowed code: one evaluator expression, or a four-line if/else recipe: if condition / out = expression / else / out = number-or-expression.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Native lowering: Math Evaluator, optional Logic Gate, Switch, and the selected output target's Generic Setter proxy.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "No arbitrary script runtime: unsupported syntax is rejected before native nodes are created; Revert compile removes generated component ids.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawSystemBlockInternalGraphEditor()
        {
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template == null)
            {
                GUILayout.Label("Open a saved System Block template before editing internals.", DecorationEditorTheme.Warning);
                _openSystemBlockTemplateIndex = -1;
                return;
            }

            EnsureSystemBlockInternalDraft(template);
            GUILayout.Label("System Block internal graph", DecorationEditorTheme.SubHeader);
            DrawPanelNextStepPrompt();
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader(template.Name, "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "This nested workspace records the block's internal graph plan, exposed boundary ports, and native lowering notes. It is ESU metadata until you lower behavior through Graph/Code into native Breadboard/ACB data.",
                DecorationEditorTheme.MiniWrap);
            DrawSystemBlockPortSummary("Input ports", template.InputPorts, "filter");
            DrawSystemBlockPortSummary("Output ports", template.OutputPorts, "anchor");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label("Internal graph draft", DecorationEditorTheme.Mini);
            string draft = GUILayout.TextArea(
                _systemBlockInternalDraft ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinHeight(EsuHudLayout.Scale(180f)));
            SetSystemBlockInternalDraftText(draft);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Check internals", DecorationEditorIconCatalog.Get("risk"), "Validate this nested System Block graph draft without mutating native data."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(126f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                CheckSystemBlockInternalGraph();
            }

            if (AutomationGUILayoutButton(
                    new GUIContent("Apply internal graph", DecorationEditorIconCatalog.Get("save"), "Save this nested graph plan as ESU-only template metadata."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(148f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                ApplySystemBlockInternalGraph();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _systemBlockInternalDirty;
            if (AutomationGUILayoutButton(
                    new GUIContent("Revert internal", DecorationEditorIconCatalog.Get("cancel"), "Restore the nested graph plan from the applied template metadata."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(126f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                RevertSystemBlockInternalGraph();
            }
            GUI.enabled = previous;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_systemBlockInternalStatus))
            {
                GUILayout.Label(
                    _systemBlockInternalStatus,
                    _systemBlockInternalStatus.StartsWith("System Block internal check passed", StringComparison.Ordinal)
                        ? DecorationEditorTheme.Body
                        : DecorationEditorTheme.Warning);
            }

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Native lowering", "settings", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Use the Code page to compile deterministic expressions into the selected native Breadboard, or use the host Graph workflow to add Generic Getter/Setter proxy nodes. The nested block itself is not a separate runtime.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Linked targets available to this System Block: " +
                SelectedLinks.Count.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawSystemBlockCodeContextPanel()
        {
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template == null)
                return;

            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Nested System Block", "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.Label(SystemBlockBreadcrumb(), DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Code compiled here still lowers into the selected native Breadboard. Return to System Graph afterward to record how those native nodes map to " +
                template.Name +
                ".",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawSystemBlockEditor()
        {
            GUILayout.Label("System Block signature", DecorationEditorTheme.SubHeader);
            DrawPanelNextStepPrompt();
            if (_selectedController == null)
            {
                GUILayout.Label("Select a controller before creating a System Block template.", DecorationEditorTheme.MiniWrap);
                return;
            }

            EnsureSystemBlockDraft();
            GUILayout.BeginVertical(DecorationEditorTheme.Panel);
            DrawCompactIconHeader("Breadcrumb", "duplicate", DecorationEditorTheme.SubHeader);
            GUILayout.Label(SystemBlockBreadcrumb(), DecorationEditorTheme.Body);
            GUILayout.Label(
                "This first System Block slice stores ESU-only template metadata: name, comments, breadcrumbs, and named ports. It does not create a parallel runtime; native behavior still lives in Graph/Code nodes.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
            DrawSystemBlockScopeGuide();

            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.Label("Name", DecorationEditorTheme.Mini);
            string name = GUILayout.TextField(_systemBlockDraftName ?? string.Empty, DecorationEditorTheme.TextField);
            SetSystemBlockDraftText(ref _systemBlockDraftName, name);

            GUILayout.Label("Input ports", DecorationEditorTheme.Mini);
            string inputs = GUILayout.TextArea(
                _systemBlockDraftInputs ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinHeight(EsuHudLayout.Scale(54f)));
            SetSystemBlockDraftText(ref _systemBlockDraftInputs, inputs);

            GUILayout.Label("Output ports", DecorationEditorTheme.Mini);
            string outputs = GUILayout.TextArea(
                _systemBlockDraftOutputs ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinHeight(EsuHudLayout.Scale(54f)));
            SetSystemBlockDraftText(ref _systemBlockDraftOutputs, outputs);

            GUILayout.Label("Comment", DecorationEditorTheme.Mini);
            string comment = GUILayout.TextArea(
                _systemBlockDraftComment ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinHeight(EsuHudLayout.Scale(46f)));
            SetSystemBlockDraftText(ref _systemBlockDraftComment, comment);
            GUILayout.EndVertical();

            DrawSystemBlockDraftPortPreview();

            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent("Suggest ports", DecorationEditorIconCatalog.Get("filter"), "Populate ports from currently linked readable/writable targets."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(116f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                SuggestSystemBlockPortsFromLinks();
            }

            if (AutomationGUILayoutButton(
                    new GUIContent("Check", DecorationEditorIconCatalog.Get("risk"), "Validate the System Block signature without mutating native data."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(72f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                CheckSystemBlockDraft();
            }

            if (AutomationGUILayoutButton(
                    new GUIContent("Apply template", DecorationEditorIconCatalog.Get("save"), "Save this ESU-only System Block template to the reusable library."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(122f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                ApplySystemBlockTemplate();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _systemBlockDraftDirty;
            if (AutomationGUILayoutButton(
                    new GUIContent("Revert draft", DecorationEditorIconCatalog.Get("cancel"), "Restore the System Block draft from the selected controller and linked targets."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(110f)),
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                ResetSystemBlockDraftFromSelection();
                _status = "System Block draft reverted from the current native workspace.";
            }
            GUI.enabled = previous;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_systemBlockValidationStatus))
            {
                GUILayout.Label(
                    _systemBlockValidationStatus,
                    _systemBlockValidationStatus.StartsWith("System Block check passed", StringComparison.Ordinal)
                        ? DecorationEditorTheme.Body
                        : DecorationEditorTheme.Warning);
            }

            DrawSystemBlockTemplateList();
        }

        private static void DrawSystemBlockScopeGuide()
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawCompactIconHeader("System Block scope", "duplicate", DecorationEditorTheme.Mini);
            GUILayout.Label(
                "Apply template saves ESU-only metadata: name, comments, breadcrumbs, named ports, and internal graph notes. Check validates the signature without native controller mutation.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Port rebinding uses the selected controller's saved linked Inputs/Outputs. Re-check ports only when duplicated unnamed blocks make portable target keys ambiguous.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawSystemBlockDraftPortPreview()
        {
            IReadOnlyList<string> inputs = ParseSystemBlockPorts(_systemBlockDraftInputs);
            IReadOnlyList<string> outputs = ParseSystemBlockPorts(_systemBlockDraftOutputs);
            string duplicate = FirstDuplicateSystemBlockPort(inputs, outputs);

            GUILayout.Space(EsuHudLayout.Scale(5f));
            DecorationEditorTheme.Separator();
            DrawCompactIconHeader("Port preview", "link", DecorationEditorTheme.Mini);
            GUILayout.Label(
                "Ports/nubs: " +
                inputs.Count.ToString(CultureInfo.InvariantCulture) +
                " input(s), " +
                outputs.Count.ToString(CultureInfo.InvariantCulture) +
                " output(s). Check normalizes names and verifies duplicates without native mutation.",
                DecorationEditorTheme.MiniWrap);
            DrawSystemBlockPortSummary("Input ports", inputs, "filter");
            DrawSystemBlockPortSummary("Output ports", outputs, "anchor");
            if (inputs.Count == 0 && outputs.Count == 0)
            {
                GUILayout.Label(
                    "System Block needs at least one input or output port before Apply template.",
                    DecorationEditorTheme.Warning);
            }

            if (!string.IsNullOrWhiteSpace(duplicate))
            {
                GUILayout.Label(
                    "Duplicate System Block port '" + duplicate + "' will fail Check/Apply until one copy is renamed.",
                    DecorationEditorTheme.Warning);
            }

            GUILayout.Label(
                "Use one name per line, comma, semicolon, or pipe. Names normalize to evaluator-safe port ids.",
                DecorationEditorTheme.MiniWrap);
            DecorationEditorTheme.Separator();
        }

        private void DrawSystemBlockTemplateList()
        {
            GUILayout.Space(EsuHudLayout.Scale(8f));
            GUILayout.Label("Reusable templates", DecorationEditorTheme.SubHeader);
            DrawSystemBlockTemplateLibraryCapHint();
            if (_systemBlockTemplates.Count == 0)
            {
                GUILayout.Label("No reusable System Block templates saved.", DecorationEditorTheme.MiniWrap);
                return;
            }

            DrawSystemBlockTemplateSearch();
            int[] matchingIndexes = Enumerable.Range(0, _systemBlockTemplates.Count)
                .Where(index => SystemBlockTemplateMatchesSearch(
                    _systemBlockTemplates[index],
                    _systemBlockTemplateSearch))
                .ToArray();
            GUILayout.Label(
                "Reusable templates: showing " +
                matchingIndexes.Length.ToString(CultureInfo.InvariantCulture) +
                " of " +
                _systemBlockTemplates.Count.ToString(CultureInfo.InvariantCulture) +
                " saved template(s).",
                DecorationEditorTheme.MiniWrap);
            if (matchingIndexes.Length == 0)
            {
                GUILayout.Label(
                    "No reusable System Block templates match the current search. Search name, controller, input/output ports, comments, or internal graph notes.",
                    DecorationEditorTheme.Warning);
                return;
            }

            foreach (int index in matchingIndexes)
            {
                AutomationSystemBlockTemplate template = _systemBlockTemplates[index];
                if (template == null)
                    continue;

                bool active = index == _activeSystemBlockTemplateIndex;
                GUILayout.BeginVertical(active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row);
                GUILayout.BeginHorizontal();
                GUILayout.Label(template.Name, DecorationEditorTheme.Body);
                GUILayout.FlexibleSpace();
                if (AutomationGUILayoutButton(
                        new GUIContent("Open", DecorationEditorIconCatalog.Get("open"), "Open this System Block template signature."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(62f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    LoadSystemBlockTemplate(index);
                }

                if (AutomationGUILayoutButton(
                        new GUIContent("Enter", DecorationEditorIconCatalog.Get("duplicate"), "Enter this System Block's nested internal graph workspace."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(68f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    EnterSystemBlockTemplate(index);
                }

                if (AutomationGUILayoutButton(
                        new GUIContent("Remove", DecorationEditorIconCatalog.Get("delete"), "Remove this reusable template."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(76f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    RemoveSystemBlockTemplateAt(index);
                    if (_activeSystemBlockTemplateIndex >= _systemBlockTemplates.Count)
                        _activeSystemBlockTemplateIndex = -1;
                    PersistSystemBlockTemplateLibrary();
                    _status = "Removed reusable System Block template.";
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    return;
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "Controller: " + template.ControllerLabel +
                    " | inputs " + template.InputPorts.Count.ToString(CultureInfo.InvariantCulture) +
                    " | outputs " + template.OutputPorts.Count.ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.MiniWrap);
                if (!string.IsNullOrWhiteSpace(template.Comment))
                    GUILayout.Label(template.Comment, DecorationEditorTheme.MiniWrap);
                GUILayout.EndVertical();
            }

            if (!string.IsNullOrWhiteSpace(_systemBlockTemplateSearch) &&
                _activeSystemBlockTemplateIndex >= 0 &&
                _activeSystemBlockTemplateIndex < _systemBlockTemplates.Count &&
                !matchingIndexes.Contains(_activeSystemBlockTemplateIndex))
            {
                GUILayout.Label(
                    "Active System Block template is hidden by the reusable template search filter.",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawSystemBlockTemplateLibraryCapHint()
        {
            GUILayout.Label(
                "Reusable template library: " +
                _systemBlockTemplates.Count.ToString(CultureInfo.InvariantCulture) +
                "/" +
                SystemBlockTemplateLibraryLimit.ToString(CultureInfo.InvariantCulture) +
                " ESU-only template(s) in this profile. Duplicate normalized name/ports keep the latest template.",
                DecorationEditorTheme.MiniWrap);
            if (_systemBlockTemplates.Count < SystemBlockTemplateLibraryLimit)
                return;

            GUILayout.Label(
                "Template library cap reached: additional unique System Block templates will not all persist until library paging/virtualization lands.",
                DecorationEditorTheme.Warning);
        }

        private void DrawSystemBlockTemplateSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _systemBlockTemplateSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _systemBlockTemplateSearch, StringComparison.Ordinal))
                _systemBlockTemplateSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_systemBlockTemplateSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear reusable System Block template search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _systemBlockTemplateSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search reusable System Block templates by name, controller, ports, comments, or internal graph notes.", DecorationEditorTheme.MiniWrap);
        }

        private static bool SystemBlockTemplateMatchesSearch(
            AutomationSystemBlockTemplate template,
            string search)
        {
            if (template == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (template.Name ?? string.Empty) + " " +
                (template.ControllerLabel ?? string.Empty) + " " +
                (template.ControllerKey ?? string.Empty) + " " +
                string.Join(" ", template.InputPorts ?? Array.Empty<string>()) + " " +
                string.Join(" ", template.OutputPorts ?? Array.Empty<string>()) + " " +
                (template.Comment ?? string.Empty) + " " +
                (template.InternalGraph ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void EnsureSystemBlockDraft()
        {
            string controllerKey = _selectedController?.StableKey ?? string.Empty;
            if (string.Equals(_systemBlockDraftControllerKey, controllerKey, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(_systemBlockDraftName))
            {
                return;
            }

            ResetSystemBlockDraftFromSelection();
        }

        private void ResetSystemBlockDraftFromSelection()
        {
            _systemBlockDraftControllerKey = _selectedController?.StableKey ?? string.Empty;
            _systemBlockDraftName = SuggestedSystemBlockName();
            _systemBlockDraftInputs = string.Join("\n", SuggestedSystemBlockPorts(readable: true).ToArray());
            _systemBlockDraftOutputs = string.Join("\n", SuggestedSystemBlockPorts(readable: false).ToArray());
            _systemBlockDraftComment =
                "ESU System Block template for " +
                (_selectedController?.Label ?? "selected controller") +
                ". Native lowering remains in Graph/Code until System Block lowering is implemented.";
            _systemBlockValidationStatus = string.Empty;
            _activeSystemBlockTemplateIndex = -1;
            _systemBlockDraftDirty = false;
        }

        private void SuggestSystemBlockPortsFromLinks()
        {
            _systemBlockDraftInputs = string.Join("\n", SuggestedSystemBlockPorts(readable: true).ToArray());
            _systemBlockDraftOutputs = string.Join("\n", SuggestedSystemBlockPorts(readable: false).ToArray());
            _systemBlockValidationStatus = string.Empty;
            _systemBlockDraftDirty = true;
            _status = "System Block ports suggested from linked readable/writable targets.";
        }

        private string SuggestedSystemBlockName()
        {
            string label = _selectedController?.Controller?.ShortLabel ??
                           _selectedController?.Label ??
                           "Automation";
            return NormalizeSystemBlockPortName(label) + "_system";
        }

        private IReadOnlyList<string> SuggestedSystemBlockPorts(bool readable)
        {
            var ports = new List<string>();
            foreach (AutomationLink link in SelectedLinks)
            {
                AutomationTarget target = link?.Target;
                if (target == null)
                    continue;

                bool matches = readable
                    ? AutomationTargetCatalog.IsBreadboardReadableTarget(target)
                    : AutomationTargetCatalog.IsBreadboardWritableTarget(target);
                if (!matches)
                    continue;

                string name = NormalizeSystemBlockPortName(AutomationCodeIdentifierForTarget(target));
                if (!ports.Contains(name, StringComparer.OrdinalIgnoreCase))
                    ports.Add(name);
            }

            if (ports.Count == 0)
                ports.Add(readable ? "input_signal" : "output_signal");
            return ports;
        }

        private void SetSystemBlockDraftText(ref string field, string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(field ?? string.Empty, value, StringComparison.Ordinal))
                return;

            field = value;
            _systemBlockDraftDirty = true;
            _systemBlockValidationStatus = string.Empty;
        }

        private void CheckSystemBlockDraft()
        {
            _systemBlockValidationStatus = ValidateSystemBlockDraft(out _, out _, out string message)
                ? "System Block check passed: " + message
                : message;
            _status = _systemBlockValidationStatus;
        }

        private void ApplySystemBlockTemplate()
        {
            if (!ValidateSystemBlockDraft(
                    out IReadOnlyList<string> inputs,
                    out IReadOnlyList<string> outputs,
                    out string message))
            {
                _systemBlockValidationStatus = message;
                _status = message;
                return;
            }

            var template = new AutomationSystemBlockTemplate(
                _systemBlockDraftName,
                _selectedController?.StableKey ?? string.Empty,
                _selectedController?.Label ?? "Controller",
                inputs,
                outputs,
                _systemBlockDraftComment,
                ExistingSystemBlockInternalGraph(_activeSystemBlockTemplateIndex));
            if (_activeSystemBlockTemplateIndex >= 0 &&
                _activeSystemBlockTemplateIndex < _systemBlockTemplates.Count)
            {
                _systemBlockTemplates[_activeSystemBlockTemplateIndex] = template;
                if (_openSystemBlockTemplateIndex == _activeSystemBlockTemplateIndex)
                    _systemBlockInternalApplied = template.InternalGraph;
            }
            else
            {
                _systemBlockTemplates.Add(template);
                _activeSystemBlockTemplateIndex = _systemBlockTemplates.Count - 1;
            }

            _systemBlockDraftDirty = false;
            PersistSystemBlockTemplateLibrary();
            _systemBlockValidationStatus =
                "System Block check passed: template stores ESU-only metadata and mutates no native controller data.";
            _status =
                "Applied System Block template '" +
                template.Name +
                "' with " +
                inputs.Count.ToString(CultureInfo.InvariantCulture) +
                " input port(s) and " +
                outputs.Count.ToString(CultureInfo.InvariantCulture) +
                " output port(s).";
        }

        private void LoadSystemBlockTemplate(int index)
        {
            if (index < 0 || index >= _systemBlockTemplates.Count)
                return;

            AutomationSystemBlockTemplate template = _systemBlockTemplates[index];
            if (template == null)
                return;

            _activeSystemBlockTemplateIndex = index;
            _systemBlockDraftControllerKey = _selectedController?.StableKey ?? template.ControllerKey;
            _systemBlockDraftName = template.Name;
            _systemBlockDraftInputs = string.Join("\n", template.InputPorts.ToArray());
            _systemBlockDraftOutputs = string.Join("\n", template.OutputPorts.ToArray());
            _systemBlockDraftComment = template.Comment;
            _systemBlockValidationStatus = "System Block template opened at breadcrumb " + SystemBlockBreadcrumb() + ".";
            _systemBlockDraftDirty = false;
            _status = _systemBlockValidationStatus;
        }

        private bool IsSystemBlockTemplateForSelectedController(AutomationSystemBlockTemplate template)
        {
            if (template == null || _selectedController == null)
                return false;

            return string.Equals(
                template.ControllerKey,
                _selectedController.StableKey,
                StringComparison.Ordinal) ||
                   string.IsNullOrWhiteSpace(template.ControllerKey);
        }

        private void CheckSystemBlockNativeLowering(AutomationSystemBlockTemplate template)
        {
            if (!TryPlanSystemBlockNativeLowering(
                    template,
                    out _,
                    out IReadOnlyList<AutomationSystemBlockLoweringCandidate> candidates,
                    out int alreadyLowered,
                    out string message))
            {
                _status = message;
                return;
            }

            int componentCount = SystemBlockLoweringComponentCount(candidates);
            _status =
                componentCount == 0
                    ? "System Block lowering check passed: " +
                      alreadyLowered.ToString(CultureInfo.InvariantCulture) +
                      " matching native proxy path(s) already exist."
                    : "System Block lowering check passed: Apply proxies can create " +
                      componentCount.ToString(CultureInfo.InvariantCulture) +
                      " native Generic Getter/Setter node(s); no native mutation during Check.";
        }

        private void ApplySystemBlockNativeLowering(AutomationSystemBlockTemplate template)
        {
            if (!TryPlanSystemBlockNativeLowering(
                    template,
                    out AutomationBreadboardInspector inspector,
                    out IReadOnlyList<AutomationSystemBlockLoweringCandidate> candidates,
                    out int alreadyLowered,
                    out string message))
            {
                _status = message;
                return;
            }

            if (candidates.Count == 0)
            {
                _status =
                    "System Block native lowering already has " +
                    alreadyLowered.ToString(CultureInfo.InvariantCulture) +
                    " matching proxy path(s); no new native nodes were needed.";
                return;
            }

            var createdComponentIds = new List<uint>();
            var messages = new List<string>();
            foreach (AutomationSystemBlockLoweringCandidate candidate in candidates)
            {
                if (!inspector.TryCreateTargetProxy(
                        candidate.Target,
                        candidate.Getter,
                        candidate.Setter,
                        out AutomationBreadboardCompileResult result,
                        out string proxyMessage))
                {
                    DeleteBreadboardComponents(inspector, createdComponentIds, out _, out _);
                    _status =
                        "System Block lowering failed for " +
                        candidate.TargetLabel +
                        ": " +
                        (proxyMessage ?? "FtD rejected the native proxy node.") +
                        ". Rolled back created proxy nodes.";
                    return;
                }

                int actualCreated = result?.ComponentIds?.Count ?? 0;
                if (actualCreated < candidate.ComponentCount)
                {
                    if (result?.ComponentIds != null)
                        createdComponentIds.AddRange(result.ComponentIds.Where(id => id != 0U));
                    DeleteBreadboardComponents(inspector, createdComponentIds, out _, out _);
                    _status =
                        "System Block lowering created only " +
                        actualCreated.ToString(CultureInfo.InvariantCulture) +
                        " of " +
                        candidate.ComponentCount.ToString(CultureInfo.InvariantCulture) +
                        " required proxy node(s) for " +
                        candidate.TargetLabel +
                        ". Rolled back created proxy nodes.";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(proxyMessage))
                    messages.Add(proxyMessage);
                if (result?.ComponentIds != null)
                    createdComponentIds.AddRange(result.ComponentIds.Where(id => id != 0U));
            }

            uint[] created = createdComponentIds
                .Distinct()
                .ToArray();
            _lastSystemBlockLoweringRevert = new AutomationCompileRevertSet(
                _selectedController?.StableKey,
                new AutomationBreadboardCompileResult("system block proxy lowering", created));
            _editorPage = AutomationEditorPage.Graph;
            _status =
                "Applied System Block '" +
                template.Name +
                "' to native Breadboard proxies: created " +
                created.Length.ToString(CultureInfo.InvariantCulture) +
                " Generic Getter/Setter node(s). Revert is available." +
                (messages.Count == 0 ? string.Empty : " " + messages[messages.Count - 1]);
        }

        private bool TryPlanSystemBlockNativeLowering(
            AutomationSystemBlockTemplate template,
            out AutomationBreadboardInspector inspector,
            out IReadOnlyList<AutomationSystemBlockLoweringCandidate> candidates,
            out int alreadyLowered,
            out string message)
        {
            inspector = null;
            candidates = Array.Empty<AutomationSystemBlockLoweringCandidate>();
            alreadyLowered = 0;
            message = null;
            if (!IsSystemBlockTemplateForSelectedController(template))
            {
                message = "Select the controller that owns this System Block before lowering it.";
                return false;
            }

            if (!IsBreadboardController(_selectedController.Controller))
            {
                message = "System Block native lowering currently targets Breadboard controllers through Generic Getter/Setter proxy nodes.";
                return false;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out inspector,
                    out string reason))
            {
                message = reason ?? "Selected controller does not expose a native Breadboard.";
                return false;
            }

            var inputPorts = new HashSet<string>(
                template.InputPorts.Select(NormalizeSystemBlockPortName),
                StringComparer.OrdinalIgnoreCase);
            var outputPorts = new HashSet<string>(
                template.OutputPorts.Select(NormalizeSystemBlockPortName),
                StringComparer.OrdinalIgnoreCase);
            if (inputPorts.Count == 0 && outputPorts.Count == 0)
            {
                message = "System Block needs at least one exposed port before native lowering.";
                return false;
            }

            bool canGetter = inspector.CanAddComponent("GenericBlockGetter");
            bool canSetter = inspector.CanAddComponent("GenericBlockSetter");
            if (!canGetter && inputPorts.Count > 0)
            {
                message = "This native board does not advertise Generic Getter components for System Block inputs.";
                return false;
            }

            if (!canSetter && outputPorts.Count > 0)
            {
                message = "This native board does not advertise Generic Setter components for System Block outputs.";
                return false;
            }

            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            var planned = new List<AutomationSystemBlockLoweringCandidate>();
            int matchedPorts = 0;
            foreach (AutomationLink link in SelectedLinks)
            {
                AutomationTarget target = link?.Target;
                if (target == null)
                    continue;

                string portName = NormalizeSystemBlockPortName(AutomationCodeIdentifierForTarget(target));
                bool inputMatch =
                    inputPorts.Contains(portName) &&
                    AutomationTargetCatalog.IsBreadboardReadableTarget(target);
                bool outputMatch =
                    outputPorts.Contains(portName) &&
                    AutomationTargetCatalog.IsBreadboardWritableTarget(target);
                if (!inputMatch && !outputMatch)
                    continue;

                matchedPorts++;
                bool needsGetter =
                    inputMatch &&
                    !TargetGettersFor(components, target).Any();
                bool needsSetter =
                    outputMatch &&
                    !TargetSettersFor(components, target).Any();
                if (!needsGetter && !needsSetter)
                {
                    alreadyLowered++;
                    continue;
                }

                planned.Add(new AutomationSystemBlockLoweringCandidate(
                    link,
                    portName,
                    needsGetter,
                    needsSetter));
            }

            if (matchedPorts == 0)
            {
                message = "No linked readable/writable targets match this System Block's exposed port names. Use Suggest ports or link matching targets first.";
                return false;
            }

            candidates = planned;
            return true;
        }

        private static int SystemBlockLoweringComponentCount(
            IEnumerable<AutomationSystemBlockLoweringCandidate> candidates)
        {
            if (candidates == null)
                return 0;

            return candidates.Sum(candidate =>
                (candidate?.Getter == true ? 1 : 0) +
                (candidate?.Setter == true ? 1 : 0));
        }

        private void EnterSystemBlockTemplate(int index)
        {
            if (index < 0 || index >= _systemBlockTemplates.Count)
                return;

            LoadSystemBlockTemplate(index);
            AutomationSystemBlockTemplate template = _systemBlockTemplates[index];
            _openSystemBlockTemplateIndex = index;
            EnsureSystemBlockInternalDraft(template, force: true);
            _editorPage = AutomationEditorPage.Graph;
            _systemBlockInternalStatus =
                "System Block internal check passed: entered nested graph metadata workspace.";
            _status =
                "Entered System Block '" +
                template.Name +
                "'. Internal edits are ESU metadata until lowered through native Graph/Code.";
        }

        private bool TryLeaveSystemBlockWorkspace()
        {
            if (!IsSystemBlockWorkspaceOpen())
                return true;

            if (_systemBlockInternalDirty)
            {
                _status = "Check/apply or revert the System Block internal draft before going up.";
                _systemBlockInternalStatus = _status;
                return false;
            }

            string name = OpenSystemBlockTemplate()?.Name ?? "System Block";
            ClearSystemBlockWorkspace();
            _editorPage = AutomationEditorPage.System;
            _status = "Returned to the host Automation workspace from " + name + ".";
            return true;
        }

        private bool IsSystemBlockWorkspaceOpen() =>
            _openSystemBlockTemplateIndex >= 0 &&
            _openSystemBlockTemplateIndex < _systemBlockTemplates.Count;

        private AutomationSystemBlockTemplate OpenSystemBlockTemplate()
        {
            return IsSystemBlockWorkspaceOpen()
                ? _systemBlockTemplates[_openSystemBlockTemplateIndex]
                : null;
        }

        private void ClearSystemBlockWorkspace()
        {
            _openSystemBlockTemplateIndex = -1;
            _systemBlockInternalDraft = string.Empty;
            _systemBlockInternalApplied = string.Empty;
            _systemBlockInternalStatus = string.Empty;
            _systemBlockInternalDirty = false;
        }

        private void ClearAutomationBlockWorkspace(bool persistCurrent = true)
        {
            if (persistCurrent)
                PersistSelectedAutomationWorkspaceState(saveProfile: false);

            _blockWorkspace = null;
            _blockLoweringPlan = null;
            _blockWorkspaceControllerKey = string.Empty;
            _blockLoweringStatus = string.Empty;
            CloseAutomationPropertyPicker();
            CloseAutomationTargetPicker();
        }

        private void RestoreSelectedAutomationWorkspaceState()
        {
            if (_selectedController == null)
                return;

            SerializationHudProfile.AutomationWorkspaceData stored =
                StoredAutomationWorkspaceFor(_selectedController);
            if (stored == null)
                return;

            string controllerKey = _selectedController.StableKey;
            _links.RemoveAll(link =>
                string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal));

            int restoredLinks = 0;
            foreach (SerializationHudProfile.AutomationLinkData linkData in stored.Links ?? new List<SerializationHudProfile.AutomationLinkData>())
            {
                AutomationTarget target = ResolvePersistedAutomationTarget(
                    linkData.TargetKey,
                    linkData.TargetPersistenceKey);
                if (target == null)
                    continue;

                AutomationLinkDirection direction = Enum.IsDefined(typeof(AutomationLinkDirection), linkData.Direction)
                    ? (AutomationLinkDirection)linkData.Direction
                    : AutomationLinkDirection.Output;
                if (_links.Any(link =>
                        string.Equals(link.ControllerKey, controllerKey, StringComparison.Ordinal) &&
                        string.Equals(link.TargetKey, target.StableKey, StringComparison.Ordinal) &&
                        link.Direction == direction))
                {
                    continue;
                }

                _links.Add(new AutomationLink(_selectedController, target, direction));
                restoredLinks++;
            }

            if (stored.Blocks != null && (stored.Blocks.Nodes?.Count ?? 0) > 0)
            {
                _blockWorkspace = AutomationBlockWorkspace.FromProfileData(
                    controllerKey,
                    _selectedController.Label,
                    SelectedLinks.Select(link => link.Target).Where(target => target != null).ToArray(),
                    stored.Blocks);
                _blockWorkspaceControllerKey = controllerKey;
                _blockLoweringPlan = null;
                _blockLoweringStatus = _blockWorkspace.Mode == AutomationBlockWorkspaceMode.NativeExact
                    ? "Restored native exact ESU layout for " +
                      _selectedController.Label +
                      ". Vanilla behavior is stored in the native Breadboard."
                    : "Restored saved ESU Blocks workspace for " +
                      _selectedController.Label +
                      ". Check ESU Blocks before applying native changes.";
                RefreshDirectionalBlockTargets();
            }

            if (restoredLinks > 0 || _blockWorkspace != null)
            {
                _status =
                    "Restored " +
                    restoredLinks.ToString(CultureInfo.InvariantCulture) +
                    " linked target(s)" +
                    (_blockWorkspace == null ? "." : " and saved ESU Blocks.");
            }
        }

        private void PersistSelectedAutomationWorkspaceState(
            bool saveProfile,
            bool preserveMissingWorkspace = true)
        {
            if (_selectedController == null)
                return;

            try
            {
                SerializationHudProfile.ProfileData profile = SerializationHudProfile.Data;
                if (profile == null)
                    return;

                if (!saveProfile)
                    MarkAutomationWorkspaceDirty();

                if (profile.AutomationWorkspaces == null)
                    profile.AutomationWorkspaces = new List<SerializationHudProfile.AutomationWorkspaceData>();

                int existingIndex = FindStoredAutomationWorkspaceIndex(
                    profile.AutomationWorkspaces,
                    _selectedController);
                SerializationHudProfile.AutomationWorkspaceData existing =
                    existingIndex >= 0 ? profile.AutomationWorkspaces[existingIndex] : null;

                SerializationHudProfile.AutomationBlockWorkspaceData blocks =
                    _blockWorkspace != null &&
                    string.Equals(_blockWorkspaceControllerKey, _selectedController.StableKey, StringComparison.Ordinal)
                        ? _blockWorkspace.ToProfileData()
                        : preserveMissingWorkspace
                            ? existing?.Blocks
                            : null;

                var stored = new SerializationHudProfile.AutomationWorkspaceData
                {
                    ControllerKey = _selectedController.StableKey,
                    ControllerPersistenceKey = _selectedController.PersistenceKey,
                    ControllerLabel = _selectedController.Label,
                    Links = SelectedLinks
                        .Where(link => link != null)
                        .Select(LinkToProfileData)
                        .ToList(),
                    Blocks = blocks
                };

                bool empty =
                    stored.Links.Count == 0 &&
                    (stored.Blocks == null || (stored.Blocks.Nodes?.Count ?? 0) == 0);
                if (existingIndex >= 0)
                    profile.AutomationWorkspaces.RemoveAt(existingIndex);
                if (!empty)
                    profile.AutomationWorkspaces.Add(stored);

                while (profile.AutomationWorkspaces.Count > AutomationWorkspaceProfileLimit)
                    profile.AutomationWorkspaces.RemoveAt(0);

                if (saveProfile)
                {
                    ProfileManager.Instance.Save(module => module is SerializationHudProfile);
                    _automationWorkspaceSnapshots.Clear();
                }
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception(
                    "Automation Editor",
                    exception,
                    "Automation workspace persistence failed");
            }
        }

        private SerializationHudProfile.AutomationWorkspaceData StoredAutomationWorkspaceFor(
            AutomationTarget controller)
        {
            if (controller == null)
                return null;

            try
            {
                List<SerializationHudProfile.AutomationWorkspaceData> stored =
                    SerializationHudProfile.Data?.AutomationWorkspaces;
                if (stored == null || stored.Count == 0)
                    return null;

                int index = FindStoredAutomationWorkspaceIndex(stored, controller);
                return index >= 0 ? stored[index] : null;
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception(
                    "Automation Editor",
                    exception,
                    "Automation workspace restore lookup failed");
                return null;
            }
        }

        private static int FindStoredAutomationWorkspaceIndex(
            IList<SerializationHudProfile.AutomationWorkspaceData> stored,
            AutomationTarget controller)
        {
            if (stored == null || controller == null)
                return -1;

            string controllerKey = controller.StableKey;
            string persistenceKey = controller.PersistenceKey;
            for (int index = stored.Count - 1; index >= 0; index--)
            {
                SerializationHudProfile.AutomationWorkspaceData data = stored[index];
                if (data == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(controllerKey) &&
                    string.Equals(data.ControllerKey, controllerKey, StringComparison.Ordinal))
                {
                    return index;
                }

                if (!string.IsNullOrWhiteSpace(persistenceKey) &&
                    string.Equals(data.ControllerPersistenceKey, persistenceKey, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static SerializationHudProfile.AutomationLinkData LinkToProfileData(
            AutomationLink link)
        {
            return new SerializationHudProfile.AutomationLinkData
            {
                TargetKey = link.TargetKey,
                TargetPersistenceKey = link.TargetPersistenceKey,
                TargetLabel = link.TargetLabel,
                Direction = (int)link.Direction
            };
        }

        private static string AutomationWorkspaceStorageKey(AutomationTarget controller)
        {
            return controller == null
                ? string.Empty
                : AutomationWorkspaceStorageKey(controller.PersistenceKey, controller.StableKey);
        }

        private static string AutomationWorkspaceStorageKey(
            SerializationHudProfile.AutomationWorkspaceData data)
        {
            return data == null
                ? string.Empty
                : AutomationWorkspaceStorageKey(data.ControllerPersistenceKey, data.ControllerKey);
        }

        private static string AutomationWorkspaceStorageKey(
            string persistenceKey,
            string stableKey)
        {
            return !string.IsNullOrWhiteSpace(persistenceKey)
                ? "p:" + persistenceKey
                : "k:" + (stableKey ?? string.Empty);
        }

        private static void RestoreAutomationWorkspaceProfileSnapshot(
            SerializationHudProfile.ProfileData profile,
            string storageKey,
            SerializationHudProfile.AutomationWorkspaceData snapshot)
        {
            if (profile == null || string.IsNullOrWhiteSpace(storageKey))
                return;

            if (profile.AutomationWorkspaces == null)
                profile.AutomationWorkspaces = new List<SerializationHudProfile.AutomationWorkspaceData>();

            for (int index = profile.AutomationWorkspaces.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        AutomationWorkspaceStorageKey(profile.AutomationWorkspaces[index]),
                        storageKey,
                        StringComparison.Ordinal))
                {
                    profile.AutomationWorkspaces.RemoveAt(index);
                }
            }

            if (snapshot != null)
                profile.AutomationWorkspaces.Add(CloneAutomationWorkspaceData(snapshot));
        }

        private static SerializationHudProfile.AutomationWorkspaceData CloneAutomationWorkspaceData(
            SerializationHudProfile.AutomationWorkspaceData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationWorkspaceData
            {
                ControllerKey = data.ControllerKey,
                ControllerPersistenceKey = data.ControllerPersistenceKey,
                ControllerLabel = data.ControllerLabel,
                Links = (data.Links ?? new List<SerializationHudProfile.AutomationLinkData>())
                    .Select(CloneAutomationLinkData)
                    .Where(link => link != null)
                    .ToList(),
                Blocks = CloneAutomationBlockWorkspaceData(data.Blocks)
            };
        }

        private static SerializationHudProfile.AutomationLinkData CloneAutomationLinkData(
            SerializationHudProfile.AutomationLinkData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationLinkData
            {
                TargetKey = data.TargetKey,
                TargetPersistenceKey = data.TargetPersistenceKey,
                TargetLabel = data.TargetLabel,
                Direction = data.Direction
            };
        }

        private static SerializationHudProfile.AutomationBlockWorkspaceData CloneAutomationBlockWorkspaceData(
            SerializationHudProfile.AutomationBlockWorkspaceData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationBlockWorkspaceData
            {
                Mode = data.Mode,
                CanvasPanX = data.CanvasPanX,
                CanvasPanY = data.CanvasPanY,
                CanvasZoom = data.CanvasZoom,
                NativeDisplayScale = data.NativeDisplayScale,
                NativeImportStatus = data.NativeImportStatus,
                SelectedNodeId = data.SelectedNodeId,
                Nodes = (data.Nodes ?? new List<SerializationHudProfile.AutomationBlockNodeData>())
                    .Select(CloneAutomationBlockNodeData)
                    .Where(node => node != null)
                    .ToList(),
                Links = (data.Links ?? new List<SerializationHudProfile.AutomationBlockLinkData>())
                    .Select(CloneAutomationBlockLinkData)
                    .Where(link => link != null)
                    .ToList()
            };
        }

        private static SerializationHudProfile.AutomationBlockNodeData CloneAutomationBlockNodeData(
            SerializationHudProfile.AutomationBlockNodeData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationBlockNodeData
            {
                Id = data.Id,
                Kind = data.Kind,
                Label = data.Label,
                IconKey = data.IconKey,
                Category = data.Category,
                PaletteTemplateId = data.PaletteTemplateId,
                ParentNodeId = data.ParentNodeId,
                CanvasOrder = data.CanvasOrder,
                CanvasX = data.CanvasX,
                CanvasY = data.CanvasY,
                SnappedToStack = data.SnappedToStack,
                TargetKey = data.TargetKey,
                TargetPersistenceKey = data.TargetPersistenceKey,
                TargetLabel = data.TargetLabel,
                LinkDirection = data.LinkDirection,
                Operator = data.Operator,
                NumericValue = data.NumericValue,
                SecondaryNumericValue = data.SecondaryNumericValue,
                Comment = data.Comment,
                Expression = data.Expression,
                PropertySelection = CloneAutomationProxyPropertySelectionData(data.PropertySelection),
                NativeComponentTypeName = data.NativeComponentTypeName,
                NativeComponentLabel = data.NativeComponentLabel,
                NativeComponentDescription = data.NativeComponentDescription,
                NativeBlockTypeName = data.NativeBlockTypeName,
                NativeBlockFilter = data.NativeBlockFilter,
                NativeComponentId = data.NativeComponentId,
                NativeComponentTypeId = data.NativeComponentTypeId,
                NativeComponentFingerprint = data.NativeComponentFingerprint,
                NativeImported = data.NativeImported,
                NativeEsuOwned = data.NativeEsuOwned,
                NativeX = data.NativeX,
                NativeY = data.NativeY,
                NativeWidth = data.NativeWidth,
                NativeHeight = data.NativeHeight,
                NativeSettingsSummary = data.NativeSettingsSummary,
                NativeInputPortLabels = (data.NativeInputPortLabels ?? new List<string>()).ToList(),
                NativeOutputPortLabels = (data.NativeOutputPortLabels ?? new List<string>()).ToList()
            };
        }

        private static SerializationHudProfile.AutomationBlockLinkData CloneAutomationBlockLinkData(
            SerializationHudProfile.AutomationBlockLinkData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationBlockLinkData
            {
                FromNodeId = data.FromNodeId,
                FromPortId = data.FromPortId,
                ToNodeId = data.ToNodeId,
                ToPortId = data.ToPortId,
                FromNativeComponentId = data.FromNativeComponentId,
                FromNativePortIndex = data.FromNativePortIndex,
                ToNativeComponentId = data.ToNativeComponentId,
                ToNativePortIndex = data.ToNativePortIndex
            };
        }

        private static SerializationHudProfile.AutomationProxyPropertySelectionData CloneAutomationProxyPropertySelectionData(
            SerializationHudProfile.AutomationProxyPropertySelectionData data)
        {
            if (data == null)
                return null;

            return new SerializationHudProfile.AutomationProxyPropertySelectionData
            {
                Label = data.Label,
                Tooltip = data.Tooltip,
                IsGetter = data.IsGetter,
                IsClear = data.IsClear,
                IsGetterReadable = data.IsGetterReadable,
                ReadableAttributeId = data.ReadableAttributeId,
                BlockPropertyId = data.BlockPropertyId,
                BlockSetId = data.BlockSetId
            };
        }

        private AutomationTarget ResolvePersistedAutomationTarget(
            string stableKey,
            string persistenceKey)
        {
            AutomationTarget target = _targets.FirstOrDefault(item =>
                string.Equals(item.StableKey, stableKey, StringComparison.Ordinal));
            if (target != null || string.IsNullOrWhiteSpace(persistenceKey))
                return target;

            AutomationTarget[] matches = _targets
                .Where(item => string.Equals(item.PersistenceKey, persistenceKey, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private void RemoveSystemBlockTemplateAt(int index)
        {
            if (index < 0 || index >= _systemBlockTemplates.Count)
                return;

            _systemBlockTemplates.RemoveAt(index);
            if (_openSystemBlockTemplateIndex == index)
                ClearSystemBlockWorkspace();
            else if (_openSystemBlockTemplateIndex > index)
                _openSystemBlockTemplateIndex--;

            if (_activeSystemBlockTemplateIndex == index)
                _activeSystemBlockTemplateIndex = -1;
            else if (_activeSystemBlockTemplateIndex > index)
                _activeSystemBlockTemplateIndex--;
        }

        private void LoadSystemBlockTemplateLibrary()
        {
            try
            {
                List<SerializationHudProfile.AutomationSystemBlockTemplateData> stored =
                    SerializationHudProfile.Data?.AutomationSystemBlockTemplates;
                if (stored == null || stored.Count == 0)
                    return;

                int loaded = 0;
                foreach (SerializationHudProfile.AutomationSystemBlockTemplateData data in stored)
                {
                    AutomationSystemBlockTemplate template = ProfileTemplateToSystemBlock(data);
                    if (template == null || HasSystemBlockLibraryTemplate(template))
                        continue;

                    _systemBlockTemplates.Add(template);
                    loaded++;
                }

                if (loaded > 0 && string.Equals(_status, "Select or place a Breadboard/ACB controller.", StringComparison.Ordinal))
                    _status = "Loaded " + loaded.ToString(CultureInfo.InvariantCulture) + " reusable System Block template(s).";
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not load Automation System Block templates",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private void PersistSystemBlockTemplateLibrary()
        {
            try
            {
                SerializationHudProfile.ProfileData profile = SerializationHudProfile.Data;
                if (profile == null)
                    return;

                profile.AutomationSystemBlockTemplates = _systemBlockTemplates
                    .Where(template => template != null)
                    .GroupBy(TemplateLibraryKey, StringComparer.OrdinalIgnoreCase)
                    .Select(group => SystemBlockToProfileTemplate(group.Last()))
                    .Where(template => template != null)
                    .Take(SystemBlockTemplateLibraryLimit)
                    .ToList();
                ProfileManager.Instance.Save(module => module is SerializationHudProfile);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not persist Automation System Block templates",
                    exception,
                    LogOptions._AlertDevInGame);
                _status = "Could not persist the reusable System Block template library.";
            }
        }

        private bool HasSystemBlockLibraryTemplate(AutomationSystemBlockTemplate template)
        {
            string key = TemplateLibraryKey(template);
            return _systemBlockTemplates.Any(existing =>
                string.Equals(TemplateLibraryKey(existing), key, StringComparison.OrdinalIgnoreCase));
        }

        private static SerializationHudProfile.AutomationSystemBlockTemplateData SystemBlockToProfileTemplate(
            AutomationSystemBlockTemplate template)
        {
            if (template == null)
                return null;

            return new SerializationHudProfile.AutomationSystemBlockTemplateData
            {
                Name = template.Name,
                InputPorts = template.InputPorts?.ToList() ?? new List<string>(),
                OutputPorts = template.OutputPorts?.ToList() ?? new List<string>(),
                Comment = template.Comment,
                InternalGraph = template.InternalGraph
            };
        }

        private static AutomationSystemBlockTemplate ProfileTemplateToSystemBlock(
            SerializationHudProfile.AutomationSystemBlockTemplateData data)
        {
            if (data == null)
                return null;

            string name = NormalizeSystemBlockPortName(data.Name);
            IReadOnlyList<string> inputs = (data.InputPorts ?? new List<string>())
                .Select(NormalizeSystemBlockPortName)
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            IReadOnlyList<string> outputs = (data.OutputPorts ?? new List<string>())
                .Select(NormalizeSystemBlockPortName)
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (string.IsNullOrWhiteSpace(name) || (inputs.Count == 0 && outputs.Count == 0))
                return null;

            return new AutomationSystemBlockTemplate(
                name,
                string.Empty,
                "Reusable template",
                inputs,
                outputs,
                data.Comment,
                data.InternalGraph);
        }

        private static string TemplateLibraryKey(AutomationSystemBlockTemplate template)
        {
            if (template == null)
                return string.Empty;

            return NormalizeSystemBlockPortName(template.Name) +
                   "|in:" +
                   string.Join(",", (template.InputPorts ?? Array.Empty<string>())
                       .Select(NormalizeSystemBlockPortName)
                       .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
                       .ToArray()) +
                   "|out:" +
                   string.Join(",", (template.OutputPorts ?? Array.Empty<string>())
                       .Select(NormalizeSystemBlockPortName)
                       .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
                       .ToArray());
        }

        private string ExistingSystemBlockInternalGraph(int index)
        {
            if (index >= 0 && index < _systemBlockTemplates.Count)
                return _systemBlockTemplates[index]?.InternalGraph ?? string.Empty;

            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            return template?.InternalGraph ?? _systemBlockInternalApplied ?? string.Empty;
        }

        private void EnsureSystemBlockInternalDraft(
            AutomationSystemBlockTemplate template,
            bool force = false)
        {
            if (template == null)
                return;

            if (!force && !string.IsNullOrWhiteSpace(_systemBlockInternalDraft))
                return;

            _systemBlockInternalApplied = string.IsNullOrWhiteSpace(template.InternalGraph)
                ? DefaultSystemBlockInternalGraph(template)
                : template.InternalGraph;
            _systemBlockInternalDraft = _systemBlockInternalApplied;
            _systemBlockInternalDirty = false;
            _systemBlockInternalStatus = string.Empty;
        }

        private static string DefaultSystemBlockInternalGraph(AutomationSystemBlockTemplate template)
        {
            string inputs = template == null || template.InputPorts.Count == 0
                ? "none"
                : string.Join(", ", template.InputPorts.ToArray());
            string outputs = template == null || template.OutputPorts.Count == 0
                ? "none"
                : string.Join(", ", template.OutputPorts.ToArray());
            string name = template?.Name ?? "system_block";
            return "# System Block: " + name + "\n" +
                   "inputs: " + inputs + "\n" +
                   "outputs: " + outputs + "\n" +
                   "internal graph:\n" +
                   "- Add native proxy nodes for linked targets in the host Graph page.\n" +
                   "- Compile deterministic expressions in Code when behavior should lower to Breadboard nodes.\n" +
                   "- Keep this note as ESU layout/group metadata; it is not a separate runtime.\n";
        }

        private void DrawSystemBlockPortSummary(
            string label,
            IReadOnlyList<string> ports,
            string iconKey)
        {
            GUILayout.BeginHorizontal(DecorationEditorTheme.Row);
            Texture2D icon = DecorationEditorIconCatalog.Get(iconKey);
            if (icon != null)
                GUILayout.Label(icon, GUILayout.Width(EsuHudLayout.Scale(18f)), GUILayout.Height(EsuHudLayout.Scale(18f)));
            GUILayout.Label(
                label + ": " +
                (ports == null || ports.Count == 0 ? "none" : string.Join(", ", ports.ToArray())),
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndHorizontal();
        }

        private void SetSystemBlockInternalDraftText(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(_systemBlockInternalDraft ?? string.Empty, value, StringComparison.Ordinal))
                return;

            _systemBlockInternalDraft = value;
            _systemBlockInternalDirty = true;
            _systemBlockInternalStatus = string.Empty;
        }

        private void CheckSystemBlockInternalGraph()
        {
            _systemBlockInternalStatus = ValidateSystemBlockInternalGraph(out string message)
                ? "System Block internal check passed: " + message
                : message;
            _status = _systemBlockInternalStatus;
        }

        private void ApplySystemBlockInternalGraph()
        {
            if (!ValidateSystemBlockInternalGraph(out string message))
            {
                _systemBlockInternalStatus = message;
                _status = message;
                return;
            }

            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template == null)
                return;

            _systemBlockTemplates[_openSystemBlockTemplateIndex] =
                template.WithInternalGraph(_systemBlockInternalDraft);
            _systemBlockInternalApplied = _systemBlockInternalDraft;
            _systemBlockInternalDirty = false;
            PersistSystemBlockTemplateLibrary();
            _systemBlockInternalStatus =
                "System Block internal check passed: ESU-only nested graph metadata saved, no native mutation.";
            _status =
                "Applied internal graph metadata for System Block '" +
                template.Name +
                "'. Native behavior remains in Breadboard/ACB data.";
        }

        private void RevertSystemBlockInternalGraph()
        {
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template == null)
                return;

            _systemBlockInternalDraft = string.IsNullOrWhiteSpace(_systemBlockInternalApplied)
                ? DefaultSystemBlockInternalGraph(template)
                : _systemBlockInternalApplied;
            _systemBlockInternalDirty = false;
            _systemBlockInternalStatus =
                "System Block internal draft reverted to the applied template metadata.";
            _status = _systemBlockInternalStatus;
        }

        private bool ValidateSystemBlockInternalGraph(out string message)
        {
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template == null)
            {
                message = "Open a saved System Block template before editing internals.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_systemBlockInternalDraft))
            {
                message = "System Block internal graph needs a draft plan before Apply.";
                return false;
            }

            bool referencesDeclaredPort = template.InputPorts
                .Concat(template.OutputPorts)
                .Any(port => !string.IsNullOrWhiteSpace(port) &&
                             _systemBlockInternalDraft.IndexOf(port, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!referencesDeclaredPort)
            {
                message = "System Block internal graph should reference at least one declared input or output port.";
                return false;
            }

            message =
                "nested graph metadata references declared ports and mutates no native data; use Graph/Code to lower behavior.";
            return true;
        }

        private bool ValidateSystemBlockDraft(
            out IReadOnlyList<string> inputs,
            out IReadOnlyList<string> outputs,
            out string message)
        {
            inputs = ParseSystemBlockPorts(_systemBlockDraftInputs);
            outputs = ParseSystemBlockPorts(_systemBlockDraftOutputs);
            string name = NormalizeSystemBlockPortName(_systemBlockDraftName);
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "System Block needs a stable template name.";
                return false;
            }

            if (inputs.Count == 0 && outputs.Count == 0)
            {
                message = "System Block needs at least one input or output port.";
                return false;
            }

            string duplicate = FirstDuplicateSystemBlockPort(inputs, outputs);
            if (!string.IsNullOrWhiteSpace(duplicate))
            {
                message = "System Block port '" + duplicate + "' is duplicated.";
                return false;
            }

            _systemBlockDraftName = name;
            _systemBlockDraftInputs = string.Join("\n", inputs.ToArray());
            _systemBlockDraftOutputs = string.Join("\n", outputs.ToArray());
            message =
                "metadata only, " +
                inputs.Count.ToString(CultureInfo.InvariantCulture) +
                " input port(s), " +
                outputs.Count.ToString(CultureInfo.InvariantCulture) +
                " output port(s), no native mutation.";
            return true;
        }

        private static IReadOnlyList<string> ParseSystemBlockPorts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            string[] parts = text.Split(new[] { '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            var ports = new List<string>();
            foreach (string part in parts)
            {
                string name = NormalizeSystemBlockPortName(part);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                ports.Add(name);
            }

            return ports;
        }

        private static string FirstDuplicateSystemBlockPort(
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs)
        {
            IEnumerable<string> allPorts = (inputs ?? Array.Empty<string>())
                .Concat(outputs ?? Array.Empty<string>())
                .Where(port => !string.IsNullOrWhiteSpace(port));
            return allPorts
                .GroupBy(port => port, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
        }

        private static string NormalizeSystemBlockPortName(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0)
                return string.Empty;

            var chars = new List<char>(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    chars.Add(char.ToLowerInvariant(character));
                else if (character == '_' || character == '-' || char.IsWhiteSpace(character))
                    chars.Add('_');
            }

            while (chars.Count > 0 && chars[0] == '_')
                chars.RemoveAt(0);
            while (chars.Count > 0 && chars[chars.Count - 1] == '_')
                chars.RemoveAt(chars.Count - 1);
            if (chars.Count == 0)
                return string.Empty;
            if (char.IsDigit(chars[0]))
                chars.Insert(0, '_');
            return new string(chars.ToArray());
        }

        private string SystemBlockBreadcrumb()
        {
            string root = _selectedController == null
                ? "Root"
                : "Root > " + _selectedController.Label;
            AutomationSystemBlockTemplate template = OpenSystemBlockTemplate();
            if (template != null)
            {
                string page = _editorPage == AutomationEditorPage.System
                    ? "Ports"
                    : _editorPage == AutomationEditorPage.Code
                        ? "Code"
                        : "Internal Graph";
                return root + " > " + template.Name + " > " + page;
            }

            if (_editorPage != AutomationEditorPage.System)
                return root;

            string name = string.IsNullOrWhiteSpace(_systemBlockDraftName)
                ? "Draft System Block"
                : _systemBlockDraftName.Trim();
            return root + " > " + name;
        }

        private void DrawAutomationCodeOutputTargetPicker()
        {
            AutomationLink[] writableLinks = SelectedLinks
                .Where(link => link != null &&
                               link.Direction == AutomationLinkDirection.Output &&
                               link?.Target != null &&
                               AutomationTargetCatalog.IsBreadboardWritableTarget(link.Target))
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

            DrawAutomationCodeOutputTargetSearch();
            AutomationLink[] matchingLinks = writableLinks
                .Where(link => AutomationCodeOutputTargetMatchesSearch(link, _automationCodeOutputTargetSearch))
                .ToArray();
            AutomationLink[] visibleLinks = matchingLinks
                .Take(CodeOutputTargetVisibleLimit)
                .ToArray();
            GUILayout.Label(
                "Output targets: " +
                visibleLinks.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                " matching from " +
                writableLinks.Length.ToString(CultureInfo.InvariantCulture) +
                " writable linked output(s).",
                DecorationEditorTheme.MiniWrap);
            if (matchingLinks.Length == 0)
            {
                GUILayout.Label("No writable Code output targets match the current search. Clear it to show linked writable outputs.", DecorationEditorTheme.Warning);
                GUILayout.EndVertical();
                return;
            }

            AutomationLink selected = AutomationCodeOutputLink();
            if (string.IsNullOrWhiteSpace(_automationCodeOutputTargetKey) ||
                selected == null)
            {
                selected = visibleLinks[0];
                _automationCodeOutputTargetKey = selected.TargetKey;
            }

            GUILayout.BeginHorizontal();
            foreach (AutomationLink link in visibleLinks)
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
            if (matchingLinks.Length > visibleLinks.Length)
            {
                GUILayout.Label(
                    "Showing first " +
                    visibleLinks.Length.ToString(CultureInfo.InvariantCulture) +
                    " of " +
                    matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                    " matching writable output target(s). Use search to narrow Code output targets.",
                    DecorationEditorTheme.Warning);
            }

            if (selected != null &&
                !matchingLinks.Any(link => string.Equals(link.TargetKey, selected.TargetKey, StringComparison.Ordinal)))
            {
                GUILayout.Label("Selected Code output target is hidden by the current search filter.", DecorationEditorTheme.Warning);
            }

            GUILayout.Label(
                "Selected output binds to a target-specific Generic Setter proxy.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private void DrawAutomationCodeOutputTargetSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _automationCodeOutputTargetSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _automationCodeOutputTargetSearch, StringComparison.Ordinal))
                _automationCodeOutputTargetSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_automationCodeOutputTargetSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear Code output target search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeOutputTargetSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search writable output links by label, category, role, runtime type, cell, or target key.", DecorationEditorTheme.MiniWrap);
        }

        private static bool AutomationCodeOutputTargetMatchesSearch(
            AutomationLink link,
            string search)
        {
            if (link?.Target == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            AutomationTarget target = link.Target;
            string haystack =
                (link.TargetLabel ?? string.Empty) + " " +
                (target.Label ?? string.Empty) + " " +
                AutomationTargetCatalog.CategoryLabel(target.Category) + " " +
                AutomationTargetCatalog.RoleLabel(target) + " " +
                (target.RuntimeType ?? string.Empty) + " " +
                FormatCell(target.LocalPosition) + " " +
                (target.StableKey ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawAutomationRecipePicker()
        {
            AutomationCodeRecipe recipe = SelectedCodeRecipe();
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
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
            DrawAutomationCodeRecipeSearchCatalog();
            GUILayout.EndVertical();
        }

        private void DrawAutomationCodeRecipeSearchCatalog()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _automationCodeRecipeSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _automationCodeRecipeSearch, StringComparison.Ordinal))
                _automationCodeRecipeSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_automationCodeRecipeSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear code recipe search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeRecipeSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search deterministic recipes by name, target category, or expression text.", DecorationEditorTheme.MiniWrap);

            int[] matchingRecipes = Enumerable.Range(0, s_codeRecipes.Length)
                .Where(index => AutomationCodeRecipeMatchesSearch(s_codeRecipes[index], _automationCodeRecipeSearch))
                .ToArray();
            GUILayout.Label(
                "Recipe catalog: " +
                matchingRecipes.Length.ToString(CultureInfo.InvariantCulture) +
                "/" +
                s_codeRecipes.Length.ToString(CultureInfo.InvariantCulture) +
                " deterministic recipe(s) matching.",
                DecorationEditorTheme.MiniWrap);
            if (matchingRecipes.Length == 0)
            {
                GUILayout.Label("No deterministic code recipes match the current search. Clear it to show the built-in recipe catalog.", DecorationEditorTheme.Warning);
                return;
            }

            int shown = Math.Min(matchingRecipes.Length, 4);
            for (int index = 0; index < shown; index++)
                DrawAutomationCodeRecipeCatalogRow(matchingRecipes[index]);

            if (matchingRecipes.Length > shown)
            {
                GUILayout.Label(
                    "+" +
                    (matchingRecipes.Length - shown).ToString(CultureInfo.InvariantCulture) +
                    " more deterministic recipe(s). Use search to narrow the catalog.",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawAutomationCodeRecipeCatalogRow(int recipeIndex)
        {
            if (recipeIndex < 0 || recipeIndex >= s_codeRecipes.Length)
                return;

            AutomationCodeRecipe recipe = s_codeRecipes[recipeIndex];
            bool active = recipeIndex == _automationCodeRecipeIndex;
            GUILayout.BeginHorizontal();
            if (AutomationGUILayoutButton(
                    new GUIContent(recipe.Label, "Select this deterministic code recipe."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeRecipeIndex = recipeIndex;
                _status = "Selected " + recipe.Label + " recipe.";
            }
            GUILayout.Label(
                AutomationTargetCatalog.CategoryLabel(recipe.Category),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(96f)));
            if (AutomationGUILayoutButton(
                    new GUIContent("load", DecorationEditorIconCatalog.Get("open"), "Load this deterministic recipe into the Code editor."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeRecipeIndex = recipeIndex;
                _automationCodeText = recipe.Code;
                _status = "Loaded " + recipe.Label + " recipe.";
            }
            GUILayout.EndHorizontal();
        }

        private static bool AutomationCodeRecipeMatchesSearch(
            AutomationCodeRecipe recipe,
            string search)
        {
            if (recipe == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (recipe.Label ?? string.Empty) + " " +
                AutomationTargetCatalog.CategoryLabel(recipe.Category) + " " +
                recipe.Category.ToString() + " " +
                (recipe.Code ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawAutomationLinkedIdentifierHints()
        {
            IReadOnlyList<string> identifiers = AutomationCodeLinkedIdentifiers();
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Linked identifiers", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(112f)));
            if (identifiers.Count == 0)
            {
                GUILayout.Label("none - link input targets to create evaluator-safe names.", DecorationEditorTheme.MiniWrap);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(
                "Click a name to insert it into the deterministic recipe.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawAutomationCodeIdentifierSearch();
            string[] matchingIdentifiers = identifiers
                .Where(identifier => AutomationCodeIdentifierMatchesSearch(identifier, _automationCodeIdentifierSearch))
                .ToArray();
            GUILayout.Label(
                "Linked identifiers: " +
                Math.Min(matchingIdentifiers.Length, CodeIdentifierVisibleLimit).ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingIdentifiers.Length.ToString(CultureInfo.InvariantCulture) +
                " matching from " +
                identifiers.Count.ToString(CultureInfo.InvariantCulture) +
                " input link(s).",
                DecorationEditorTheme.MiniWrap);
            if (matchingIdentifiers.Length == 0)
            {
                GUILayout.Label("No linked identifiers match the current search. Clear it to show all evaluator-safe input names.", DecorationEditorTheme.Warning);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            int shown = Math.Min(matchingIdentifiers.Length, CodeIdentifierVisibleLimit);
            for (int index = 0; index < shown; index++)
            {
                string identifier = matchingIdentifiers[index];
                if (GUILayout.Button(
                        identifier,
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(98f)),
                        GUILayout.Height(EsuHudLayout.Scale(22f))))
                {
                    InsertAutomationCodeIdentifier(identifier);
                }
            }

            if (matchingIdentifiers.Length > shown)
            {
                GUILayout.Label(
                    "+" + (matchingIdentifiers.Length - shown).ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(28f)));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (matchingIdentifiers.Length > shown)
            {
                GUILayout.Label(
                    "Showing first " +
                    shown.ToString(CultureInfo.InvariantCulture) +
                    " matching linked identifier(s). Use search to narrow the input names.",
                    DecorationEditorTheme.MiniWrap);
            }
            GUILayout.EndVertical();
        }

        private void DrawAutomationCodeIdentifierSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _automationCodeIdentifierSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _automationCodeIdentifierSearch, StringComparison.Ordinal))
                _automationCodeIdentifierSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_automationCodeIdentifierSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear linked identifier search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _automationCodeIdentifierSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search evaluator-safe linked input identifiers before inserting them into Code recipes.", DecorationEditorTheme.MiniWrap);
        }

        private static bool AutomationCodeIdentifierMatchesSearch(
            string identifier,
            string search)
        {
            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack = identifier ?? string.Empty;
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
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
            DrawBreadboardComponentReflectionCapHint(inspector);
            GUILayout.Label("Board type: " + inspector.BoardTypeName, DecorationEditorTheme.MiniWrap);
            DrawBreadboardNativeEditGuide();
            DrawBreadboardQuickAdds(inspector);
            DrawMissileBreadboardQuickAdds(inspector);
            DrawBreadboardProxyActions(inspector, SelectedLinks);
            DrawBreadboardGraphCanvas(inspector);
            DrawBreadboardComponentList(inspector);
            GUILayout.Label("These edits use FtD's stored Board/IBoard instance, Var.Us settings, and AddComponentCommand/NewPackage path.", DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
        }

        private static void DrawBreadboardComponentReflectionCapHint(AutomationBreadboardInspector inspector)
        {
            if (inspector == null || !inspector.ComponentReflectionMayBeCapped)
                return;

            GUILayout.Label(
                "Native graph reflection cap: ESU currently reads up to " +
                AutomationBreadboardInspector.ComponentReflectionLimit.ToString(CultureInfo.InvariantCulture) +
                " stored component(s) from this board. Large Breadboards may contain more; full compatibility still needs scalable native enumeration/virtualization.",
                DecorationEditorTheme.Warning);
        }

        private static void DrawBreadboardNativeEditGuide()
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawCompactIconHeader("Native edit mode", "settings", DecorationEditorTheme.Mini);
            GUILayout.Label(
                "Advanced Graph edits are direct native Breadboard edits: quick-adds, moves, wires, setting changes, and deletes apply through FtD board commands immediately.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Revert compile only removes ESU-generated component ids from the last Blocks/Code/System apply. Manual native graph edits are not owned by that Revert path.",
                DecorationEditorTheme.MiniWrap);
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
            DrawAcbNativeEditGuide(prefix, allowTrigger);
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
        }

        private static void DrawAcbNativeEditGuide(
            string label,
            bool allowTrigger)
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawCompactIconHeader("Native ACB edit mode", "settings", DecorationEditorTheme.Mini);
            GUILayout.Label(
                label +
                " fields write directly to the native ACB ControlBlockData package through FtD's Var.Us path.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                allowTrigger
                    ? "Trigger test now uses the native ACB test hook immediately; it is not an ESU recipe or Revert-owned generated node."
                    : "Linked ACB rows expose native rule data for inspection/editing; they are not ESU recipe nodes or Revert-owned generated nodes.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
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
            DrawAcbControllerNativeEditGuide(prefix);
            GUILayout.Label(
                inspector.Buttons.Count.ToString(CultureInfo.InvariantCulture) +
                " button data item(s). Type: " +
                inspector.ControllerTypeName,
                DecorationEditorTheme.MiniWrap);

            DrawAcbControllerButtonSearch();
            AutomationAcbControllerButtonSummary[] matchingButtons = inspector.Buttons
                .Where(button => AcbControllerButtonMatchesSearch(button, _acbControllerButtonSearch))
                .ToArray();
            int shown = Math.Min(matchingButtons.Length, Math.Max(1, maxButtons));
            GUILayout.Label(
                "Buttons shown: " +
                shown.ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingButtons.Length.ToString(CultureInfo.InvariantCulture) +
                " matching from " +
                inspector.Buttons.Count.ToString(CultureInfo.InvariantCulture) +
                " total.",
                DecorationEditorTheme.MiniWrap);
            if (matchingButtons.Length == 0)
            {
                GUILayout.Label(
                    "No ACB Controller buttons match the current button search. Clear it to edit native button data.",
                    DecorationEditorTheme.Warning);
            }

            for (int index = 0; index < shown; index++)
                DrawAcbControllerButtonEditor(inspector, matchingButtons[index], prefix);

            if (matchingButtons.Length > shown)
            {
                GUILayout.Label(
                    "Showing first " +
                    shown.ToString(CultureInfo.InvariantCulture) +
                    " of " +
                    matchingButtons.Length.ToString(CultureInfo.InvariantCulture) +
                    " matching ACB Controller buttons. Use button search to narrow results.",
                    DecorationEditorTheme.MiniWrap);
            }

            GUILayout.Label(
                "Breadboard output uses FtD's ACB Controller button keyword path, so Generic Getter nodes can read the button signal by keyword.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawAcbControllerButtonSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _acbControllerButtonSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _acbControllerButtonSearch, StringComparison.Ordinal))
                _acbControllerButtonSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_acbControllerButtonSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear ACB Controller button search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _acbControllerButtonSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search ACB Controller buttons by number, name, keyword, Breadboard output, shape, color, or reflected type.", DecorationEditorTheme.MiniWrap);
        }

        private static bool AcbControllerButtonMatchesSearch(
            AutomationAcbControllerButtonSummary button,
            string search)
        {
            if (button == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                "button " +
                (button.Index + 1).ToString(CultureInfo.InvariantCulture) + " " +
                (button.ButtonName ?? string.Empty) + " " +
                (button.Keyword ?? string.Empty) + " " +
                "shape " +
                button.ShapeId.ToString(CultureInfo.InvariantCulture) + " " +
                (button.IsUsedForBreadboard
                    ? "breadboard output out enabled true"
                    : "breadboard output off disabled false") + " " +
                "color " +
                button.ButtonColor.r.ToString(CultureInfo.InvariantCulture) + " " +
                button.ButtonColor.g.ToString(CultureInfo.InvariantCulture) + " " +
                button.ButtonColor.b.ToString(CultureInfo.InvariantCulture) + " " +
                button.ButtonColor.a.ToString(CultureInfo.InvariantCulture) + " " +
                (button.DataTypeName ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawAcbControllerNativeEditGuide(string label)
        {
            GUILayout.BeginVertical(DecorationEditorTheme.Row);
            DrawCompactIconHeader("Native ACB Controller edit mode", "settings", DecorationEditorTheme.Mini);
            GUILayout.Label(
                label +
                " button fields write directly to native ACB Controller button data. Name, keyword, Breadboard output, shape, and color changes are immediate native edits.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Breadboard output remains the native keyword bridge that Generic Getter proxy nodes can read; ESU Revert only owns generated proxy/component ids.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndVertical();
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
            GUILayout.Label(
                "These shortcuts add advertised native Breadboard components immediately. They are manual native graph edits, separate from Blocks/Code/System Apply nodes tracked by Revert compile.",
                DecorationEditorTheme.MiniWrap);
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
                "Reader/Writer/Pair create native variable nodes now. Use matching native variable names/settings after creating the bridge nodes.",
                DecorationEditorTheme.MiniWrap);
        }

        private void DrawMissileBreadboardQuickAdds(AutomationBreadboardInspector inspector)
        {
            if (_selectedController?.Controller?.Kind != AutomationControllerKind.MissileBreadboard)
                return;

            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.Label("Missile output components", DecorationEditorTheme.Mini);
            GUILayout.Label(
                "Missile shortcuts search the advertised native component list and add the first matching vanilla node immediately.",
                DecorationEditorTheme.MiniWrap);
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

            DrawBreadboardProxySearch();
            AutomationLink[] matchingLinks = links
                .Where(link => BreadboardProxyLinkMatchesSearch(link, _breadboardProxySearch))
                .ToArray();
            int shown = Math.Min(matchingLinks.Length, 8);
            GUILayout.Label(
                "Proxy links shown: " +
                shown.ToString(CultureInfo.InvariantCulture) +
                "/" +
                matchingLinks.Length.ToString(CultureInfo.InvariantCulture) +
                " matching from " +
                links.Count.ToString(CultureInfo.InvariantCulture) +
                " linked target(s).",
                DecorationEditorTheme.MiniWrap);
            if (matchingLinks.Length == 0)
            {
                GUILayout.Label(
                    "No linked targets match the current proxy search. Clear it to show native Generic Getter/Setter proxy actions.",
                    DecorationEditorTheme.Warning);
            }

            for (int index = 0; index < shown; index++)
            {
                AutomationLink link = matchingLinks[index];
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

            if (matchingLinks.Length > shown)
            {
                GUILayout.Label(
                    "+" + (matchingLinks.Length - shown).ToString(CultureInfo.InvariantCulture) + " matching linked target(s). Use proxy search to narrow GBG/GBS actions.",
                    DecorationEditorTheme.MiniWrap);
            }
            if (!string.IsNullOrWhiteSpace(_breadboardProxySearch) &&
                links.Any(IsSelectedAutomationLink) &&
                !matchingLinks.Any(IsSelectedAutomationLink))
            {
                GUILayout.Label("Selected link is hidden by the current proxy search.", DecorationEditorTheme.Warning);
            }
        }

        private void DrawBreadboardProxySearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _breadboardProxySearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _breadboardProxySearch, StringComparison.Ordinal))
                _breadboardProxySearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_breadboardProxySearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear linked target proxy search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _breadboardProxySearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search linked target proxy actions by Input/Output, GBG/GBS, label, category, role, runtime type, controller, cell, or target key.", DecorationEditorTheme.MiniWrap);
        }

        private static bool BreadboardProxyLinkMatchesSearch(
            AutomationLink link,
            string search)
        {
            if (link == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            AutomationTarget target = link.Target;
            string haystack =
                (link.DirectionLabel ?? string.Empty) + " " +
                (link.Direction == AutomationLinkDirection.Input
                    ? "read getter gbg generic block getter"
                    : "write setter gbs generic block setter") + " " +
                (link.ControllerLabel ?? string.Empty) + " " +
                (link.ControllerKey ?? string.Empty) + " " +
                (link.TargetLabel ?? string.Empty) + " " +
                (link.TargetKey ?? string.Empty) + " " +
                (target == null
                    ? "missing stale"
                    : AutomationTargetCatalog.CategoryLabel(target.Category) + " " +
                      AutomationTargetCatalog.RoleLabel(target) + " " +
                      target.RuntimeType + " " +
                      target.StableKey + " " +
                      target.LocalPosition.x.ToString(CultureInfo.InvariantCulture) + " " +
                      target.LocalPosition.y.ToString(CultureInfo.InvariantCulture) + " " +
                      target.LocalPosition.z.ToString(CultureInfo.InvariantCulture));
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
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
            var content = new GUIContent(
                label,
                "Add native " +
                typeName +
                " to the selected Breadboard immediately. Disabled means this board does not advertise that vanilla component; Revert compile only owns Blocks/Code/System Apply nodes.");
            if (GUILayout.Button(
                    content,
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
            var content = new GUIContent(
                label,
                "Search advertised native Breadboard components for " +
                string.Join(", ", searchTerms ?? Array.Empty<string>()) +
                " and add the first matching vanilla node immediately. Disabled means no advertised match is available on this board.");
            if (GUILayout.Button(
                    content,
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
                    inspector.InputPorts(component, NativeCanvasPortVisibleLimit);
                IReadOnlyList<AutomationBreadboardPortSummary> outputs =
                    inspector.OutputPorts(component, NativeCanvasPortVisibleLimit);
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
            DrawBreadboardPortDisplayCapHint(selected);
            DrawBreadboardComponentSettings(inspector, selected);
            if (selected.IsGenericProxy)
                DrawBreadboardProxyPropertyPicker(inspector, selected);
            DrawBreadboardWireControls(inspector, components, selected);
            GUILayout.EndVertical();
        }

        private void DrawBreadboardPortDisplayCapHint(AutomationBreadboardComponentSummary component)
        {
            if (component == null)
                return;

            bool canvasCapped =
                component.InputCount > NativeCanvasPortVisibleLimit ||
                component.OutputCount > NativeCanvasPortVisibleLimit;
            bool wireControlsCapped =
                component.InputCount > NativeWireControlPortVisibleLimit ||
                component.OutputCount > NativeWireControlPortVisibleLimit;
            if (!canvasCapped && !wireControlsCapped)
                return;

            GUILayout.Label(
                "Port display cap: canvas shows first " +
                NativeCanvasPortVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                " input/output nubs; wire controls show first " +
                NativeWireControlPortVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                " ports. Full vanilla compatibility still needs port virtualization/pagination.",
                DecorationEditorTheme.MiniWrap);
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

            DrawBreadboardStoredComponentSearch();
            bool hasProxy = components.Any(component => component.IsGenericProxy);
            if (hasProxy)
            {
                GUILayout.Label("Proxy property filter", DecorationEditorTheme.Mini);
                string nextFilter = GUILayout.TextField(_proxyPropertyFilter ?? string.Empty, DecorationEditorTheme.TextField);
                if (!string.Equals(_proxyPropertyFilter, nextFilter, StringComparison.Ordinal))
                    _proxyPropertyFilter = nextFilter;
            }

            DrawBreadboardWireSourceStatus(components);

            AutomationBreadboardComponentSummary[] matchingComponents = components
                .Where(component => StoredBreadboardComponentMatchesSearch(component, _storedComponentSearch))
                .ToArray();
            int shown = Math.Min(matchingComponents.Length, NativeComponentListVisibleLimit);
            GUILayout.Label(
                "Stored component list: showing " +
                shown.ToString(CultureInfo.InvariantCulture) +
                " of " +
                matchingComponents.Length.ToString(CultureInfo.InvariantCulture) +
                " matching reflected native component(s)" +
                (matchingComponents.Length == components.Count
                    ? string.Empty
                    : " from " + components.Count.ToString(CultureInfo.InvariantCulture) + " total") +
                ".",
                DecorationEditorTheme.MiniWrap);
            if (shown == 0)
            {
                GUILayout.Label(
                    "No stored native components match the current search. Search label, type, id, target, filter, or description.",
                    DecorationEditorTheme.Warning);
                return;
            }

            if (matchingComponents.Length > shown)
            {
                GUILayout.Label(
                    "Showing first " +
                    NativeComponentListVisibleLimit.ToString(CultureInfo.InvariantCulture) +
                    " matching stored native components. Use stored component search to narrow results; full compatibility still needs stored-component search improvements or virtualization.",
                    DecorationEditorTheme.Warning);
            }

            for (int index = 0; index < shown; index++)
            {
                AutomationBreadboardComponentSummary component = matchingComponents[index];
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

            if (matchingComponents.Length > shown)
            {
                GUILayout.Label(
                    "+" + (matchingComponents.Length - shown).ToString(CultureInfo.InvariantCulture) + " matching reflected component(s) hidden by the safe list cap.",
                    DecorationEditorTheme.MiniWrap);
            }

            if (!string.IsNullOrWhiteSpace(_storedComponentSearch) &&
                components.Any(component => component.UniqueId == _selectedCanvasComponentId) &&
                matchingComponents.All(component => component.UniqueId != _selectedCanvasComponentId))
            {
                GUILayout.Label(
                    "Selected native node is hidden by the stored component search filter.",
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawBreadboardStoredComponentSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _storedComponentSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _storedComponentSearch, StringComparison.Ordinal))
                _storedComponentSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_storedComponentSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear stored component search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _storedComponentSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search reflected native components by label, type, id, target, filter, or description.", DecorationEditorTheme.MiniWrap);
        }

        private static bool StoredBreadboardComponentMatchesSearch(
            AutomationBreadboardComponentSummary component,
            string search)
        {
            if (component == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (component.Label ?? string.Empty) + " " +
                (component.TypeName ?? string.Empty) + " " +
                component.UniqueId.ToString(CultureInfo.InvariantCulture) + " " +
                component.ComponentTypeId.ToString("D") + " " +
                (component.BlockTypeName ?? string.Empty) + " " +
                (component.BlockFilter ?? string.Empty) + " " +
                (component.Description ?? string.Empty);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
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
                inspector.OutputPorts(component, NativeWireControlPortVisibleLimit);
            IReadOnlyList<AutomationBreadboardPortSummary> inputs =
                inspector.InputPorts(component, NativeWireControlPortVisibleLimit);
            if (outputs.Count == 0 && inputs.Count == 0)
                return;

            bool selectedNode = component.UniqueId == _selectedCanvasComponentId;
            if (selectedNode)
                DrawNativeWirePortSearch();

            string wirePortSearch = selectedNode ? _nativeWirePortSearch : string.Empty;
            AutomationBreadboardPortSummary[] matchingOutputs = outputs
                .Where(port => NativeWirePortMatchesSearch(port, "output", wirePortSearch))
                .ToArray();
            AutomationBreadboardPortSummary[] matchingInputs = inputs
                .Where(port => NativeWirePortMatchesSearch(port, "input", wirePortSearch))
                .ToArray();
            if (selectedNode)
            {
                GUILayout.Label(
                    "Wire ports shown: " +
                    (matchingOutputs.Length + matchingInputs.Length).ToString(CultureInfo.InvariantCulture) +
                    "/" +
                    (outputs.Count + inputs.Count).ToString(CultureInfo.InvariantCulture) +
                    " visible under the current wire-control cap.",
                    DecorationEditorTheme.MiniWrap);
                if (matchingOutputs.Length == 0 &&
                    matchingInputs.Length == 0)
                {
                    GUILayout.Label(
                        "No visible native wire-control ports match the current port search. Clear it to show capped input/output nubs.",
                        DecorationEditorTheme.Warning);
                }
                if (!string.IsNullOrWhiteSpace(_nativeWirePortSearch) &&
                    component.UniqueId == _wireSourceComponentId &&
                    !matchingOutputs.Any(port => port.Index == _wireSourceOutputIndex))
                {
                    GUILayout.Label("Selected wire source output is hidden by the current native port search.", DecorationEditorTheme.Warning);
                }
            }

            AutomationBreadboardComponentSummary source =
                FindComponentById(components, _wireSourceComponentId);
            if (_wireSourceComponentId != NoWireSourceComponentId && source == null)
            {
                _wireSourceComponentId = NoWireSourceComponentId;
                _wireSourceOutputIndex = -1;
            }

            if (matchingOutputs.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "Outputs",
                    DecorationEditorTheme.Mini,
                    GUILayout.Width(EsuHudLayout.Scale(58f)));
                foreach (AutomationBreadboardPortSummary port in matchingOutputs)
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

            if (matchingInputs.Length == 0)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Inputs",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(58f)));
            foreach (AutomationBreadboardPortSummary port in matchingInputs)
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

            foreach (AutomationBreadboardPortSummary port in matchingInputs.Where(item => item.IsConnected))
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

        private void DrawNativeWirePortSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            string nextSearch = GUILayout.TextField(
                _nativeWirePortSearch ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (!string.Equals(nextSearch, _nativeWirePortSearch, StringComparison.Ordinal))
                _nativeWirePortSearch = nextSearch ?? string.Empty;

            bool previous = GUI.enabled;
            GUI.enabled = previous && !string.IsNullOrWhiteSpace(_nativeWirePortSearch);
            if (AutomationGUILayoutButton(
                    new GUIContent("clear", "Clear selected native port search."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _nativeWirePortSearch = string.Empty;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.Label("Search visible native wire-control ports by input/output, index, label, connection state, or connected source. This narrows the current capped port set only.", DecorationEditorTheme.MiniWrap);
        }

        private static bool NativeWirePortMatchesSearch(
            AutomationBreadboardPortSummary port,
            string direction,
            string search)
        {
            if (port == null)
                return false;

            string[] terms = (search ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            if (terms.Length == 0)
                return true;

            string haystack =
                (direction ?? string.Empty) + " " +
                (port.IsOutput ? "output out" : "input in") + " " +
                port.Index.ToString(CultureInfo.InvariantCulture) + " " +
                (port.Label ?? string.Empty) + " " +
                (port.IsConnected ? "connected wired" : "open unconnected") + " " +
                (port.ConnectedFrom ?? string.Empty) + " " +
                port.ConnectedFromComponentId.ToString(CultureInfo.InvariantCulture) + " " +
                port.ConnectedFromOutputIndex.ToString(CultureInfo.InvariantCulture);
            return terms.All(term =>
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private sealed class AutomationBlockLiveValueSnapshot
        {
            internal AutomationBlockLiveValueSnapshot(
                string text,
                string tooltip,
                float refreshAfter)
            {
                Text = string.IsNullOrWhiteSpace(text) ? "unavailable" : text;
                Tooltip = tooltip ?? string.Empty;
                RefreshAfter = refreshAfter;
            }

            internal string Text { get; }

            internal string Tooltip { get; }

            internal float RefreshAfter { get; }
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

        private sealed class AutomationSystemBlockTemplate
        {
            internal AutomationSystemBlockTemplate(
                string name,
                string controllerKey,
                string controllerLabel,
                IReadOnlyList<string> inputPorts,
                IReadOnlyList<string> outputPorts,
                string comment)
                : this(name, controllerKey, controllerLabel, inputPorts, outputPorts, comment, string.Empty)
            {
            }

            internal AutomationSystemBlockTemplate(
                string name,
                string controllerKey,
                string controllerLabel,
                IReadOnlyList<string> inputPorts,
                IReadOnlyList<string> outputPorts,
                string comment,
                string internalGraph)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "system_block" : name;
                ControllerKey = controllerKey ?? string.Empty;
                ControllerLabel = string.IsNullOrWhiteSpace(controllerLabel) ? "Controller" : controllerLabel;
                InputPorts = inputPorts?.ToArray() ?? Array.Empty<string>();
                OutputPorts = outputPorts?.ToArray() ?? Array.Empty<string>();
                Comment = comment ?? string.Empty;
                InternalGraph = internalGraph ?? string.Empty;
            }

            internal string Name { get; }

            internal string ControllerKey { get; }

            internal string ControllerLabel { get; }

            internal IReadOnlyList<string> InputPorts { get; }

            internal IReadOnlyList<string> OutputPorts { get; }

            internal string Comment { get; }

            internal string InternalGraph { get; }

            internal AutomationSystemBlockTemplate WithInternalGraph(string internalGraph)
            {
                return new AutomationSystemBlockTemplate(
                    Name,
                    ControllerKey,
                    ControllerLabel,
                    InputPorts,
                    OutputPorts,
                    Comment,
                    internalGraph);
            }
        }

        private sealed class AutomationSystemBlockLoweringCandidate
        {
            internal AutomationSystemBlockLoweringCandidate(
                AutomationLink link,
                string portName,
                bool getter,
                bool setter)
            {
                Link = link;
                PortName = portName ?? string.Empty;
                Getter = getter;
                Setter = setter;
            }

            internal AutomationLink Link { get; }

            internal string PortName { get; }

            internal bool Getter { get; }

            internal bool Setter { get; }

            internal int ComponentCount =>
                (Getter ? 1 : 0) +
                (Setter ? 1 : 0);

            internal AutomationTarget Target => Link?.Target;

            internal string TargetLabel => Link?.TargetLabel ?? Target?.Label ?? "target";
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

        private bool CompileAutomationCodeExpression(bool returnToGraph = true)
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
                if (returnToGraph)
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
            if (returnToGraph)
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

        private void AddGeneratedComponentIdsToLastCompileRevert(IEnumerable<uint> componentIds)
        {
            if (componentIds == null)
                return;

            uint[] nextIds = componentIds
                .Where(id => id != 0U)
                .Distinct()
                .ToArray();
            if (nextIds.Length == 0)
                return;

            if (_lastCompileRevert == null)
            {
                _lastCompileRevert = new AutomationCompileRevertSet(
                    _selectedController?.StableKey,
                    new AutomationBreadboardCompileResult("esu blocks lowering", nextIds));
                return;
            }

            uint[] combined = _lastCompileRevert.ComponentIds
                .Concat(componentIds)
                .Where(id => id != 0U)
                .Distinct()
                .ToArray();
            _lastCompileRevert = new AutomationCompileRevertSet(
                _selectedController?.StableKey,
                new AutomationBreadboardCompileResult("esu blocks lowering", combined));
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
            AutomationBlockNode setBlock = _blockWorkspace?.FirstExecutableNode(AutomationBlockKind.SetTarget);
            AutomationProxyPropertySelection outputSelection =
                setBlock != null &&
                setBlock.PropertyBindingMode == AutomationProxyPropertyBindingMode.Explicit
                    ? setBlock.PropertySelection
                    : null;
            if (outputSelection == null && setBlock == null)
                outputSelection = PreferredProxyPropertySelection(inspector, setters, outputTarget, getter: false);
            if (outputSelection != null)
            {
                setters = setters
                    .Where(component => ProxyMatchesSelection(component, outputSelection))
                    .ToArray();
            }
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
                if (outputSelection != null)
                {
                    bool propertyApplied = false;
                    foreach (uint componentId in bindingComponentIds)
                    {
                        AutomationBreadboardComponentSummary created = FindComponentById(inspector.Components, componentId);
                        if (created != null && created.IsGenericSetter)
                        {
                            if (!TryApplyProxyPropertySelection(inspector, created, outputSelection, out proxyMessage))
                            {
                                return "Code output binding created a target setter, but could not select the native property: " +
                                       (proxyMessage ?? "property selection failed") +
                                       ". Revert is available for the generated setter.";
                            }

                            propertyApplied = true;
                            break;
                        }
                    }

                    if (!propertyApplied)
                    {
                        return "Code output binding created a target setter, but the generated component was not visible for property selection. Revert is available for the generated setter.";
                    }
                }
                components = inspector.Components;
                setters = TargetSettersFor(components, outputTarget);
                if (outputSelection != null)
                {
                    setters = setters
                        .Where(component => ProxyMatchesSelection(component, outputSelection))
                        .ToArray();
                }
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

        private static bool ProxyMatchesSelection(
            AutomationBreadboardComponentSummary component,
            AutomationProxyPropertySelection selection)
        {
            if (component == null || selection == null)
                return true;

            if (selection.IsClear)
            {
                return component.ReadableAttributeId == 999999U &&
                       component.BlockPropertyId == 999999U &&
                       component.BlockSetId == 999999U;
            }

            if (component.IsGenericGetter && selection.IsGetterReadable)
                return component.ReadableAttributeId == selection.ReadableAttributeId;

            return component.BlockPropertyId == selection.BlockPropertyId &&
                   component.BlockSetId == selection.BlockSetId;
        }

        private static AutomationProxyPropertySelection PreferredProxyPropertySelection(
            AutomationBreadboardInspector inspector,
            IEnumerable<AutomationBreadboardComponentSummary> components,
            AutomationTarget target,
            bool getter)
        {
            if (inspector == null || components == null || target == null)
                return null;

            IReadOnlyList<string> terms = AutomationBreadboardInspector.TargetProxySearchTerms(target, getter);
            if (terms.Count == 0)
                return null;

            AutomationTargetPropertyOption best = null;
            int bestScore = int.MaxValue;
            foreach (AutomationBreadboardComponentSummary component in components)
            {
                if (component == null)
                    continue;

                foreach (AutomationBreadboardProxyOption option in inspector.ProxyPropertyOptions(component, string.Empty, 512))
                {
                    AutomationTargetPropertyOption targetOption = ToTargetPropertyOption(option, getter);
                    if (targetOption?.Selection == null ||
                        targetOption.Selection.IsClear ||
                        !TryScoreAutomationPropertyOption(targetOption, terms, out int score) ||
                        score >= bestScore)
                    {
                        continue;
                    }

                    best = targetOption;
                    bestScore = score;
                    if (bestScore == 0)
                        break;
                }

                if (bestScore == 0)
                    break;
            }

            return best?.Selection;
        }

        private static bool TryScoreAutomationPropertyOption(
            AutomationTargetPropertyOption option,
            IReadOnlyList<string> terms,
            out int score)
        {
            score = int.MaxValue;
            if (option == null || terms == null || terms.Count == 0)
                return false;

            string haystack = (option.Label ?? string.Empty) + " " + (option.Tooltip ?? string.Empty);
            for (int index = 0; index < terms.Count; index++)
            {
                string term = terms[index];
                if (string.IsNullOrWhiteSpace(term) ||
                    haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                score = index;
                return true;
            }

            return false;
        }

        private static bool TryApplyProxyPropertySelection(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component,
            AutomationProxyPropertySelection selection,
            out string message)
        {
            message = null;
            if (inspector == null || component == null || selection == null)
                return true;

            AutomationBreadboardProxyOption option = inspector
                .ProxyPropertyOptions(component, string.Empty, 512)
                .FirstOrDefault(selection.Matches);
            if (option == null)
            {
                message = "Selected native property was not available on the generated proxy.";
                return false;
            }

            return inspector.TrySelectProxyProperty(component, option, out message);
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

        private static AutomationBreadboardComponentSummary[] TargetGettersFor(
            IEnumerable<AutomationBreadboardComponentSummary> components,
            AutomationTarget target)
        {
            if (components == null || target == null)
                return Array.Empty<AutomationBreadboardComponentSummary>();

            return components
                .Where(component => GetterMatchesTarget(component, target))
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

        private static bool GetterMatchesTarget(
            AutomationBreadboardComponentSummary component,
            AutomationTarget target)
        {
            return component != null &&
                   component.IsGenericGetter &&
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
            return AutomationBreadboardInspector.TargetBlockNameFilter(target);
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

        private bool CanRevertLastSystemBlockLowering()
        {
            return _selectedController != null &&
                   _lastSystemBlockLoweringRevert != null &&
                   _lastSystemBlockLoweringRevert.ComponentIds.Count > 0 &&
                   string.Equals(
                       _lastSystemBlockLoweringRevert.ControllerKey,
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

            int deleted = DeleteBreadboardComponents(
                inspector,
                _lastCompileRevert.ComponentIds,
                out int missing,
                out int failed);

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

        private void RevertLastSystemBlockNativeLowering()
        {
            if (!CanRevertLastSystemBlockLowering())
            {
                _status = "No System Block native lowering is available to revert for this controller.";
                return;
            }

            if (!AutomationBreadboardInspector.TryCreate(
                    _selectedController.Block,
                    _selectedController.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                _status = reason ?? "Selected controller does not expose a native Breadboard.";
                return;
            }

            int deleted = DeleteBreadboardComponents(
                inspector,
                _lastSystemBlockLoweringRevert.ComponentIds,
                out int missing,
                out int failed);

            if (failed == 0)
                _lastSystemBlockLoweringRevert = null;

            _status =
                "Reverted " +
                deleted.ToString(CultureInfo.InvariantCulture) +
                " System Block proxy node(s)" +
                (missing > 0 ? ", " + missing.ToString(CultureInfo.InvariantCulture) + " already missing" : string.Empty) +
                (failed > 0 ? ", " + failed.ToString(CultureInfo.InvariantCulture) + " failed" : string.Empty) +
                ".";
        }

        private int DeleteBreadboardComponents(
            AutomationBreadboardInspector inspector,
            IEnumerable<uint> componentIds,
            out int missing,
            out int failed)
        {
            missing = 0;
            failed = 0;
            if (inspector == null || componentIds == null)
                return 0;

            Dictionary<uint, AutomationBreadboardComponentSummary> components = inspector.Components
                .GroupBy(component => component.UniqueId)
                .ToDictionary(group => group.Key, group => group.First());
            int deleted = 0;
            foreach (uint componentId in componentIds.Where(id => id != 0U).Distinct().Reverse())
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

            return deleted;
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
                if (link.Direction != AutomationLinkDirection.Input)
                    continue;

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
                string.Equals(link.TargetKey, _selectedLinkTargetKey, StringComparison.Ordinal) &&
                link.Direction == _selectedLinkDirection);
        }

        private void SelectAutomationLink(AutomationLink link)
        {
            if (link == null)
            {
                _selectedLinkTargetKey = string.Empty;
                _selectedLinkDirection = AutomationLinkDirection.Output;
                return;
            }

            _selectedLinkTargetKey = link.TargetKey;
            _selectedLinkDirection = link.Direction;
        }

        private bool IsSelectedAutomationLink(AutomationLink link) =>
            link != null &&
            string.Equals(_selectedLinkTargetKey, link.TargetKey, StringComparison.Ordinal) &&
            _selectedLinkDirection == link.Direction;

        private AutomationLink AutomationCodeOutputLink()
        {
            IReadOnlyList<AutomationLink> links = SelectedLinks;
            if (links.Count == 0)
                return null;

            AutomationLink selected = null;
            if (!string.IsNullOrWhiteSpace(_automationCodeOutputTargetKey))
            {
                selected = links.FirstOrDefault(link =>
                    link.Direction == AutomationLinkDirection.Output &&
                    string.Equals(link.TargetKey, _automationCodeOutputTargetKey, StringComparison.Ordinal));
            }

            if (selected?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(selected.Target))
            {
                return selected;
            }

            selected = SelectedLink();
            if (selected?.Target != null &&
                selected.Direction == AutomationLinkDirection.Output &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(selected.Target))
            {
                return selected;
            }

            return links.FirstOrDefault(link =>
                link.Direction == AutomationLinkDirection.Output &&
                link?.Target != null &&
                AutomationTargetCatalog.IsBreadboardWritableTarget(link.Target));
        }

        private AutomationLink ValidationOutputLink() =>
            SelectedLinks.FirstOrDefault(link =>
                link.Direction == AutomationLinkDirection.Output &&
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
            ClearAutomationBlockWorkspace(persistCurrent: false);
            PersistSelectedAutomationWorkspaceState(saveProfile: false);
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

        private int CountBrowserTargets(AutomationTargetBrowserMode mode) =>
            _targets.Count(target =>
                TargetVisibleInBrowser(target, mode) &&
                AutomationTargetCatalog.MatchesSearch(target, _targetSearchText));

        private bool TargetVisibleInBrowser(AutomationTarget target) =>
            TargetVisibleInBrowser(target, _targetBrowserMode);

        private bool TargetVisibleInBrowser(
            AutomationTarget target,
            AutomationTargetBrowserMode mode)
        {
            if (target == null)
                return false;

            bool important = IsImportantAutomationTarget(target);
            if (mode == AutomationTargetBrowserMode.Important && !important)
                return false;
            if (mode == AutomationTargetBrowserMode.Generic && important)
                return false;

            if (_showAdvancedFilters &&
                _filter != AutomationTargetCategory.All &&
                !AutomationTargetCatalog.PassesFilter(target, _filter))
            {
                return false;
            }

            return true;
        }

        private IReadOnlyList<AutomationTarget> FilteredWorldTargets()
        {
            return _targets
                .Where(target =>
                    TargetVisibleInBrowser(target) &&
                    AutomationTargetCatalog.MatchesSearch(target, _targetSearchText))
                .ToArray();
        }

        private void CycleFilter()
        {
            _targetBrowserMode = _targetBrowserMode == AutomationTargetBrowserMode.Important
                ? AutomationTargetBrowserMode.Generic
                : AutomationTargetBrowserMode.Important;
            _status = "Target browser: " + TargetBrowserModeLabel(_targetBrowserMode) + ".";
        }

        private static string TargetBrowserModeLabel(AutomationTargetBrowserMode mode) =>
            mode == AutomationTargetBrowserMode.Generic ? "Generic" : "Important";

        private string TargetBrowserSummary()
        {
            string summary = TargetBrowserModeLabel(_targetBrowserMode);
            if (_showAdvancedFilters && _filter != AutomationTargetCategory.All)
                summary += " / " + AutomationTargetCatalog.CategoryLabel(_filter);
            return summary;
        }

        private static string LinkIdentity(string targetKey, AutomationLinkDirection direction) =>
            direction.ToString() + "|" + (targetKey ?? string.Empty);

        private static string ToolLabel(AutomationTool tool) =>
            tool == AutomationTool.Place ? "Place controllers" : "Link targets";

        private static void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(96f)));
            string text = value ?? string.Empty;
            GUILayout.Label(
                text,
                text.Length > 26 ? DecorationEditorTheme.MiniWrap : DecorationEditorTheme.Body,
                GUILayout.ExpandWidth(true));
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
            _toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight);
            _statusRect = EsuHudLayout.BottomStripRect(BottomStripHeightScaled());

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
            s_showLeftPanel = _showLeftPanel;
            s_showRightPanel = _showRightPanel;
            s_showBlocksSection = _showBlocksSection;
            s_showFilterSection = _showFilterSection;
            s_showAdvancedFilters = _showAdvancedFilters;
        }

        private static bool ValidRect(Rect rect) =>
            rect.width > 1f &&
            rect.height > 1f &&
            !float.IsNaN(rect.x) &&
            !float.IsNaN(rect.y) &&
            !float.IsNaN(rect.width) &&
            !float.IsNaN(rect.height);

        private static float BottomStripHeightScaled() =>
            EsuHudLayout.BottomStripHeight();

        private static float ToolbarBottomLimit() =>
            EsuHudLayout.EditorPanelTopLimit(ToolbarHeight);

        private static float BottomPanelLimit() =>
            EsuHudLayout.BottomPanelLimit(BottomStripHeightScaled());

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
                EsuHudLayout.EditorSideMargin,
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
                Mathf.Max(EsuHudLayout.EditorSideMargin, Screen.width - width - EsuHudLayout.EditorSideMargin),
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
            if (_automationClosePromptOpen)
                return true;
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

        private bool ShouldSuppressAutomationBackgroundInput(
            Event current,
            bool foregroundOpen) =>
            _automationClosePromptOpen ||
            foregroundOpen && ShouldConsumeGuiEvent(current);

        private bool ShouldSuppressAutomationBackgroundDirectInput(Event current) =>
            _suppressAutomationBackgroundDirectInput &&
            ShouldConsumeGuiEvent(current);

        private bool IsAutomationForegroundPopupOpen()
        {
            if (_contextMenuKind != AutomationContextMenuKind.None)
                return true;
            if (!string.IsNullOrWhiteSpace(_targetPickerNodeId) &&
                _targetPickerRect.width > 1f &&
                _targetPickerRect.height > 1f)
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(_propertyPickerNodeId) &&
                _propertyPickerRect.width > 1f &&
                _propertyPickerRect.height > 1f)
            {
                return true;
            }
            return false;
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
                AutomationTarget target,
                AutomationLinkDirection direction = AutomationLinkDirection.Output)
            {
                ControllerKey = controller?.StableKey ?? string.Empty;
                ControllerPersistenceKey = controller?.PersistenceKey ?? string.Empty;
                ControllerLabel = controller?.Label ?? "Controller";
                TargetKey = target?.StableKey ?? string.Empty;
                TargetPersistenceKey = target?.PersistenceKey ?? string.Empty;
                TargetLabel = target?.Label ?? "Target";
                Target = target;
                Direction = direction;
            }

            internal string ControllerKey { get; }

            internal string ControllerPersistenceKey { get; }

            internal string ControllerLabel { get; }

            internal string TargetKey { get; private set; }

            internal string TargetPersistenceKey { get; private set; }

            internal AutomationLinkDirection Direction { get; }

            internal string TargetLabel { get; private set; }

            internal AutomationTarget Target { get; private set; }

            internal bool IsStale => Target == null;

            internal string DirectionLabel =>
                Direction == AutomationLinkDirection.Input ? "Input" : "Output";

            internal string SelectionKey =>
                Direction.ToString() + "|" + TargetKey;

            internal void RebindTarget(AutomationTarget target)
            {
                Target = target;
                if (target != null)
                {
                    TargetKey = target.StableKey;
                    TargetPersistenceKey = target.PersistenceKey;
                    TargetLabel = target.Label;
                }
            }
        }
    }
}
