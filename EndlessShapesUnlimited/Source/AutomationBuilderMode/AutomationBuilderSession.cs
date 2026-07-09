using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed partial class AutomationBuilderSession
    {
        private const float ToolbarHeight = 54f;
        private const float LeftPanelWidth = 364f;
        private const float RightPanelWidth = 318f;
        private const float LeftPanelMinHeight = 330f;
        private const float RightPanelMinHeight = 390f;
        private const float CanvasMinWidth = 760f;
        private const float CanvasMinHeight = 520f;
        private const float CanvasPaletteWidth = 292f;
        private const float CanvasLeftPaletteWidth = 292f;
        private const float CanvasPlanWidth = 330f;
        private const float LinkRowHeight = 28f;
        private const float GraphNodeWidth = 330f;
        private const float GraphValueNodeWidth = 206f;
        private const float GraphNodeHeight = 104f;
        private const float GraphSlotLabelWidth = 58f;
        private const int GraphSlotValueTextMaxCharacters = 56;
        private const int GraphCompactSlotValueTextMaxCharacters = 34;
        private const float GraphSlotDropdownWidth = 360f;
        private const float GraphSlotDropdownMaxHeight = 340f;
        private const float GraphPropertyPickerWidth = 360f;
        private const float GraphPropertyPickerMaxHeight = 340f;
        private const float GraphReadinessPopoverWidth = 334f;
        private const float GraphReadinessPopoverMaxHeight = 220f;
        private const float GraphZoomMin = 0.55f;
        private const float GraphZoomMax = 1.6f;
        private const float CanvasDragThreshold = 4f;
        private const float NativeRefreshIntervalSeconds = 0.75f;
        private const float LivePreviewIntervalSeconds = 0.25f;
        private const float NativeDiagnosticsLogCooldownSeconds = 5f;
        private const double NativeDiagnosticsSlowThresholdMs = 25d;
        private const float GraphInteractionDiagnosticsLogCooldownSeconds = 5f;
        private const int GraphDragStalledFrameThreshold = 8;
        private const float PanelDividerHeight = 8f;
        private const float PanelSectionMinHeight = 72f;
        private const int ViewModeMenuButtonCount = 9;

        private static readonly DecorationEditorViewMode[] s_viewModeCycle =
        {
            DecorationEditorViewMode.Mixed,
            DecorationEditorViewMode.Wireframe,
            DecorationEditorViewMode.DecorationOnly,
            DecorationEditorViewMode.Mass,
            DecorationEditorViewMode.Drag,
            DecorationEditorViewMode.Cost,
            DecorationEditorViewMode.Surface,
            DecorationEditorViewMode.Important,
            DecorationEditorViewMode.Normal
        };

        private enum AutomationBuilderTool
        {
            Select,
            LinkInput,
            LinkOutput,
            PlaceBreadboard
        }

        private enum AutomationLinkKind
        {
            InputToBreadboard,
            BreadboardToOutput
        }

        private enum AutomationLinkVisibility
        {
            All,
            InputOnly,
            OutputOnly
        }

        private enum AutomationContextAction
        {
            None,
            DeleteBlock,
            RemoveLink,
            ShowAllLinks,
            ShowInputLinks,
            ShowOutputLinks
        }

        private enum AutomationNodeKind
        {
            Forever,
            InputGetter,
            IfCondition,
            IfLessThan,
            Constant,
            Random,
            OutputSetter,
            LogicNot,
            LogicAnd,
            LogicOr,
            LogicXor,
            LogicNand,
            LogicNor,
            LogicXnor,
            CompareAboveThreshold,
            CompareBelowThreshold,
            MathAdd,
            MathSubtract,
            MathMultiply,
            MathMax,
            MathMin,
            Clamp,
            Smooth,
            Comment
        }

        private enum AutomationPanelDivider
        {
            None,
            LeftInputOutput
        }

        private enum AutomationPaletteCategory
        {
            Output,
            Input,
            Control,
            Math,
            Variables,
            Notation
        }

        private enum AutomationBlockShape
        {
            Stack,
            Value,
            Control,
            Notation
        }

        private enum AutomationValueSlotKind
        {
            Threshold,
            Amount,
            Factor,
            Min,
            Max,
            Seconds,
            Pass,
            LogicB,
            MathB,
            Else
        }

        private enum AutomationGraphSlotMenuKind
        {
            None,
            Target,
            Property
        }

        private enum AutomationCanvasInteractionKind
        {
            None,
            CanvasPan,
            NodeDrag,
            PaletteDrag,
            Dropdown,
            ContextMenu
        }

        private enum AutomationGraphContextAction
        {
            None,
            Deselect,
            Delete,
            Close
        }

        private enum AutomationGraphConnectionKind
        {
            Stack,
            Value,
            Body
        }

        private enum AutomationGraphPortKind
        {
            FlowIn,
            FlowOut,
            ValueIn,
            ValueOut,
            BodyIn,
            BodyOut
        }

        private enum AutomationGraphWireOrigin
        {
            EsuSnap,
            EsuNative,
            NativeImported
        }

        private static Rect s_leftPanelRect = Rect.zero;
        private static Rect s_rightPanelRect = Rect.zero;
        private static Rect s_canvasRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static bool s_showLeftPanel = true;
        private static bool s_showRightPanel = true;
        private static bool s_showLinksSection = true;
        private static bool s_showBreadboardSection = true;
        private static float s_leftInputOutputRatio = 0.48f;
        private static AutomationBreadboardVariant s_selectedBreadboardVariant = AutomationBreadboardVariant.Ai;

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly DecorationEditorViewModeController _viewModes;
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.AutomationBuilder.Toolbar".GetHashCode();
        private readonly int _leftPanelWindowId = "EndlessShapesUnlimited.AutomationBuilder.LeftPanel".GetHashCode();
        private readonly int _rightPanelWindowId = "EndlessShapesUnlimited.AutomationBuilder.RightPanel".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.AutomationBuilder.Status".GetHashCode();
        private readonly int _canvasWindowId = "EndlessShapesUnlimited.AutomationBuilder.Canvas".GetHashCode();
        private readonly int _viewModeMenuWindowId = "EndlessShapesUnlimited.AutomationBuilder.ViewModeMenu".GetHashCode();
        private readonly int _contextMenuWindowId = "EndlessShapesUnlimited.AutomationBuilder.ContextMenu".GetHashCode();

        private readonly List<AutomationLink> _links = new List<AutomationLink>();
        private readonly Dictionary<string, AutomationGraph> _graphs =
            new Dictionary<string, AutomationGraph>(StringComparer.Ordinal);
        private readonly Dictionary<uint, AutomationGraphNodeDraft> _pendingNativeNodeDrafts =
            new Dictionary<uint, AutomationGraphNodeDraft>();
        private readonly HashSet<uint> _pendingNativeNodeRemovals = new HashSet<uint>();
        private readonly Dictionary<string, string> _liveValuePreviewCache =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private Rect _toolbarRect;
        private Rect _leftPanelRect = s_leftPanelRect;
        private Rect _rightPanelRect = s_rightPanelRect;
        private Rect _statusRect;
        private Rect _canvasRect = s_canvasRect;
        private Rect _lastCanvasPaletteRect;
        private Rect _lastCanvasWorkspaceRect;
        private bool _draggingLeftPanel;
        private bool _draggingRightPanel;
        private bool _draggingCanvasPanel;
        private bool _resizingLeftPanel;
        private bool _resizingRightPanel;
        private bool _resizingCanvasPanel;
        private Vector2 _leftPanelDragOffset;
        private Vector2 _rightPanelDragOffset;
        private Vector2 _canvasPanelDragOffset;
        private Rect _leftPanelResizeStart;
        private Rect _rightPanelResizeStart;
        private Rect _canvasPanelResizeStart;
        private Vector2 _leftPanelResizeMouseStart;
        private Vector2 _rightPanelResizeMouseStart;
        private Vector2 _canvasPanelResizeMouseStart;
        private int _layoutResetGeneration = s_layoutGeneration;
        private bool _showLeftPanel = s_showLeftPanel;
        private bool _showRightPanel = s_showRightPanel;
        private bool _showLinksSection = s_showLinksSection;
        private bool _showBreadboardSection = s_showBreadboardSection;
        private AutomationBuilderTool _tool = AutomationBuilderTool.Select;
        private AutomationBlockRef _selectedBlock;
        private AutomationBlockRef _selectedBreadboard;
        private AutomationLink _selectedLink;
        private bool _placementArmed;
        private bool _canvasOpen;
        private bool _closeRequested;
        private bool _closePromptOpen;
        private bool _automationDirty;
        private MBuild_Ftd _buildOptions;
        private DecorationEditorViewMode _viewMode = DecorationEditorViewMode.Mixed;
        private bool _viewModeMenuOpen;
        private Rect _viewModeMenuRect;
        private AutomationBlockRef _contextBlock;
        private AutomationLink _contextLink;
        private Rect _contextMenuRect;
        private AutomationLinkVisibility _linkVisibility;
        private AutomationBreadboardVariant _selectedBreadboardVariant = s_selectedBreadboardVariant;
        private Vector2 _inputLinksScroll;
        private Vector2 _outputLinksScroll;
        private Vector2 _graphScroll;
        private float _graphZoom = 1.08f;
        private Vector2 _graphEventMousePosition;
        private bool _graphEventMousePositionValid;
        private Vector2 _blockPaletteScroll;
        private Vector2 _linkedHardwareScroll;
        private Vector2 _nativePlanScroll;
        private float _leftInputOutputRatio = s_leftInputOutputRatio;
        private AutomationPanelDivider _draggingPanelDivider = AutomationPanelDivider.None;
        private AutomationPaletteCategory _selectedPaletteCategory = AutomationPaletteCategory.Output;
        private bool _draggingPaletteBlock;
        private AutomationNodeKind _draggingPaletteKind;
        private bool _paletteDragMoved;
        private bool _paletteDropPending;
        private Vector2 _paletteDragLastWindowMouse;
        private float _panelDividerDragStartMouseY;
        private float _panelDividerDragStartRatio;
        private bool _panningGraphCanvas;
        private int _graphCanvasPanButton = -1;
        private Vector2 _graphCanvasPanMouseStart;
        private Vector2 _graphCanvasPanScrollStart;
        private int _draggingNodeId;
        private Vector2 _nodeDragStartMouse;
        private Rect _nodeDragStartRect;
        private Vector2 _nodeDragLastRawMouse;
        private int _nodeDragStalledFrames;
        private float _nextGraphInteractionDiagnosticsLogTime;
        private AutomationCanvasInteractionKind _canvasInteraction = AutomationCanvasInteractionKind.None;
        private int _canvasInteractionButton = -1;
        private Vector2 _canvasInteractionStartMouse;
        private bool _canvasInteractionMoved;
        private Rect _graphContextMenuRect;
        private int _graphContextMenuNodeId;
        private Rect _graphReadinessPopoverRect;
        private Rect _graphReadinessPopoverAnchorRect;
        private int _graphReadinessPopoverNodeId;
        private Vector2 _graphReadinessPopoverScroll;
        private Rect _graphPropertyPickerRect;
        private int _graphPropertyPickerNodeId;
        private Vector2 _graphPropertyPickerScroll;
        private bool _suppressNextCanvasRightClick;
        private bool _graphSlotConsumedInput;
        private AutomationGraphSlotMenuKind _graphSlotMenuKind = AutomationGraphSlotMenuKind.None;
        private int _graphSlotMenuNodeId;
        private Rect _graphSlotMenuAnchorRect;
        private Rect _graphSlotMenuRect;
        private Vector2 _graphSlotMenuScroll;
        private Rect _lastDrawnGraphSlotRect;
        private int _closedQuickChoicesNodeId;
        private string _propertyText = "value";
        private string _constantText = "30";
        private int _nextLinkId = 1;
        private float _lastLinkNotificationTime = -1f;
        private bool _nativeAutomationCacheDirty = true;
        private float _nextNativeAutomationRefreshTime;
        private int _nativeAutomationCacheVersion;
        private int _nativeRefreshCount;
        private int _nativeRefreshSkippedCount;
        private int _nativeGraphSyncCount;
        private double _lastNativeRefreshElapsedMs;
        private double _lastNativeGraphSyncElapsedMs;
        private int _lastNativeRefreshComponentCount;
        private int _lastNativeRefreshNodeCount;
        private int _lastNativeRefreshLinkCount;
        private string _lastNativeAutomationError;
        private float _nextNativeDiagnosticsLogTime;
        private bool _automationDisplayCacheDirty = true;
        private int _displayCacheNativeVersion = -1;
        private string _displayCacheGraphKey;
        private NativePlan _cachedSelectedNativePlan;
        private List<string> _cachedBlockProgramLines = new List<string>();
        private List<string> _cachedGraphLiveValueLines = new List<string>();
        private string _graphLiveValueCacheKey;
        private int _graphLiveValueCacheNativeVersion = -1;
        private float _nextGraphLiveValueRefreshTime;
        private float _nextLivePreviewRefreshTime;
        private int _livePreviewNativeVersion = -1;

        internal AutomationBuilderSession(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _pointerProbe = new DecorationPointerProbe(build);
            _viewModes = new DecorationEditorViewModeController(build);
        }

        internal bool Active { get; private set; }

        internal bool CloseRequested => _closeRequested;

        internal bool SwitchToDecorationEditRequested { get; private set; }

        internal void ClearSwitchToDecorationEditRequest() =>
            SwitchToDecorationEditRequested = false;

        internal bool CanSwitchToDecorationEdit(out string reason)
        {
            if (_placementArmed)
            {
                reason = "Place or cancel the pending breadboard before switching modes.";
                return false;
            }

            if (IsGraphDirty())
            {
                reason = "Apply or close without applying Automation Builder graph changes before switching modes.";
                return false;
            }

            reason = null;
            return true;
        }

        internal void Begin()
        {
            Active = true;
            AutomationBuilderInputScope.Begin();
            ApplyFocusView();
            RefreshSelectionFromPointer();
            ForceRefreshSelectedBreadboardNativeState();
        }

        internal void End(bool preserveSharedHud = false)
        {
            RestoreFocusView();
            AutomationBuilderInputScope.End();
            if (!preserveSharedHud)
                DecorationEditorOverlay.Clear();
            Active = false;
            _placementArmed = false;
            _canvasOpen = false;
            _draggingLeftPanel = false;
            _draggingRightPanel = false;
            _draggingCanvasPanel = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _resizingCanvasPanel = false;
            _draggingPanelDivider = AutomationPanelDivider.None;
            ResetCanvasInteractionState();
            CloseGraphSlotMenu();
            CloseGraphContextMenu();
            CloseGraphPropertyPicker();
            _graphEventMousePositionValid = false;
            _viewModeMenuOpen = false;
            CloseAutomationContextMenu();
            _closePromptOpen = false;
            SwitchToDecorationEditRequested = false;
        }

        internal void SuspendForModeSwitchHandoff()
        {
            RestoreFocusView();
            AutomationBuilderInputScope.End();
            Active = false;
            _placementArmed = false;
            _draggingLeftPanel = false;
            _draggingRightPanel = false;
            _draggingCanvasPanel = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _resizingCanvasPanel = false;
            _draggingPanelDivider = AutomationPanelDivider.None;
            ResetCanvasInteractionState();
            CloseGraphSlotMenu();
            CloseGraphContextMenu();
            CloseGraphPropertyPicker();
            _graphEventMousePositionValid = false;
            _viewModeMenuOpen = false;
            CloseAutomationContextMenu();
            _closePromptOpen = false;
            SwitchToDecorationEditRequested = false;
        }

        internal void Update()
        {
            if (!Active)
                return;

            EsuHudNotifications.SetActiveSource("Automation Builder");
            RefreshMouseOverUiFromCurrentPointer();
            UpdateActiveCanvasPointerInteraction();
            ClaimActiveCanvasInteractionInput();
            RefreshNativeAutomationCache();
            HandleKeyboard();
            HandleMouse();
            CompleteCanvasInteractionIfReleased();
            if (_canvasOpen &&
                (_canvasInteraction == AutomationCanvasInteractionKind.PaletteDrag || _draggingPaletteBlock) &&
                !Input.GetMouseButton(0))
            {
                _paletteDropPending = true;
            }
            if (_canvasOpen &&
                (_canvasInteraction == AutomationCanvasInteractionKind.PaletteDrag ||
                 _canvasInteraction == AutomationCanvasInteractionKind.NodeDrag) &&
                Input.GetMouseButtonDown(1))
            {
                CancelActiveCanvasInteraction(CurrentSelectedGraph(), restoreNode: true);
                _suppressNextCanvasRightClick = true;
                InfoStore.Add("Automation canvas drag cancelled.");
            }
            _viewModes.Tick(_viewMode);
            DrawWorldPreview();
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

        internal bool DismissCanvas()
        {
            if (_closePromptOpen)
            {
                CloseAutomationClosePrompt();
                InfoStore.Add("Automation Builder close cancelled.");
                return true;
            }

            if (_canvasOpen)
            {
                CloseCanvasEditor();
                return true;
            }

            return false;
        }

        private void CloseCanvasEditor()
        {
            CancelActiveCanvasInteraction(CurrentSelectedGraph(), restoreNode: true);
            CloseGraphSlotMenu();
            CloseGraphContextMenu();
            CloseGraphPropertyPicker();
            _canvasOpen = false;
        }

        internal void RequestClose()
        {
            if (!Active)
                return;

            if (_closePromptOpen)
                return;

            if (!HasUnappliedAutomationChanges)
            {
                _closeRequested = true;
                return;
            }

            OpenAutomationClosePrompt();
        }

        private void DrawGui(bool interactive)
        {
            DecorationEditorTheme.Ensure();
            if (interactive)
                EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed());
            if (Event.current.type == EventType.Repaint)
                DecorationEditorOverlay.Render();

            ApplyLayoutResetIfNeeded();
            _toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight);
            _statusRect = EsuHudLayout.BottomStripRect(StatusHeightScaled());
            if (_leftPanelRect.width < 1f || _leftPanelRect.height < 1f)
                _leftPanelRect = DefaultLeftPanelRect();
            if (_rightPanelRect.width < 1f || _rightPanelRect.height < 1f)
                _rightPanelRect = DefaultRightPanelRect();
            if (_canvasOpen &&
                (_canvasRect.width < 1f || _canvasRect.height < 1f))
            {
                _canvasRect = DefaultCanvasRect();
            }

            _leftPanelRect = EsuHudLayout.ClampPanel(
                _leftPanelRect,
                MinLeftPanelWidth(),
                MinLeftPanelHeight(),
                MaxLeftPanelWidth(),
                MaxLeftPanelHeight(),
                LeftPanelTopLimit(),
                EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
            _rightPanelRect = EsuHudLayout.ClampPanel(
                _rightPanelRect,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight(),
                LeftPanelTopLimit(),
                EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
            if (_canvasOpen)
            {
                _canvasRect = EsuHudLayout.ClampPanel(
                    _canvasRect,
                    MinCanvasWidth(),
                    MinCanvasHeight(),
                    MaxCanvasWidth(),
                    MaxCanvasHeight(),
                    LeftPanelTopLimit(),
                    EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
            }

            if (interactive)
            {
                if (_canvasOpen)
                {
                    HandleCanvasPanelResize();
                    HandleCanvasPanelDrag();
                }
                else
                {
                    if (_showLeftPanel)
                    {
                        HandleLeftPanelResize();
                        HandleLeftPanelDrag();
                    }

                    if (_showRightPanel)
                    {
                        HandleRightPanelResize();
                        HandleRightPanelDrag();
                    }
                }
            }

            GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
            if (_canvasOpen)
            {
                _canvasRect = GUI.Window(_canvasWindowId, _canvasRect, DrawGraphCanvas, GUIContent.none, GUIStyle.none);
            }
            else
            {
                if (_showLeftPanel)
                    _leftPanelRect = GUI.Window(_leftPanelWindowId, _leftPanelRect, DrawLeftPanel, GUIContent.none, GUIStyle.none);
                if (_showRightPanel)
                    _rightPanelRect = GUI.Window(_rightPanelWindowId, _rightPanelRect, DrawRightPanel, GUIContent.none, GUIStyle.none);
            }

            GUI.Window(_statusWindowId, _statusRect, DrawStatusStrip, GUIContent.none, GUIStyle.none);
            if (_closePromptOpen)
                DrawAutomationClosePrompt();

            if (interactive)
            {
                if (!_canvasOpen)
                {
                    if (_showLeftPanel)
                    {
                        EsuHudLayout.DrawResizeGrip(_leftPanelRect, leftEdge: false);
                        EsuCursorTooltip.Register(PanelHeaderDragRect(_leftPanelRect, 74f), "Drag to move the Automation Builder status panel.");
                        EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_leftPanelRect, leftEdge: false), "Drag to resize the Automation Builder status panel.");
                    }

                    if (_showRightPanel)
                    {
                        EsuHudLayout.DrawResizeGrip(_rightPanelRect, leftEdge: true);
                        EsuCursorTooltip.Register(PanelHeaderDragRect(_rightPanelRect, 74f), "Drag to move the breadboard palette.");
                        EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_rightPanelRect, leftEdge: true), "Drag to resize the breadboard palette.");
                    }
                }
                else
                {
                    EsuHudLayout.DrawResizeGrip(_canvasRect, leftEdge: false);
                    EsuCursorTooltip.Register(PanelHeaderDragRect(_canvasRect, 286f), "Drag to move the Automation graph editor.");
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_canvasRect, leftEdge: false), "Drag to resize the Automation graph editor.");
                }

                EsuHudNotifications.DrawExpandedPopup();
                DrawAutomationContextMenu();
                DrawViewModeMenu(_toolbarRect);
                EsuConsoleWindow.DrawForegroundWindow();
                EsuCursorTooltip.Draw();
            }
            else
            {
                EsuHudNotifications.DrawExpandedPopup();
            }

            s_leftPanelRect = _leftPanelRect;
            s_rightPanelRect = _rightPanelRect;
            s_canvasRect = _canvasRect;
            s_layoutGeneration = _layoutResetGeneration;
            s_showLeftPanel = _showLeftPanel;
            s_showRightPanel = _showRightPanel;
            s_showLinksSection = _showLinksSection;
            s_showBreadboardSection = _showBreadboardSection;
            s_leftInputOutputRatio = _leftInputOutputRatio;
            s_selectedBreadboardVariant = _selectedBreadboardVariant;

            if (!interactive)
                return;

            bool overUi = _closePromptOpen ||
                          ContainsMouse(_toolbarRect) ||
                          ContainsMouse(_statusRect) ||
                          EsuHudNotifications.ContainsMouse(Event.current.mousePosition) ||
                          (_viewModeMenuOpen && ViewModeMenuRect(_toolbarRect).Contains(Event.current.mousePosition)) ||
                          (_contextBlock != null && _contextMenuRect.Contains(Event.current.mousePosition)) ||
                          (_canvasOpen && ContainsMouse(_canvasRect)) ||
                          (!_canvasOpen && _showLeftPanel && ContainsMouse(_leftPanelRect)) ||
                          (!_canvasOpen && _showRightPanel && ContainsMouse(_rightPanelRect));
            AutomationBuilderInputScope.SetMouseOverUi(overUi);
            if (overUi && ShouldConsumeGuiEvent(Event.current))
            {
                if (Event.current.type == EventType.ScrollWheel)
                    AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
                else
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                Event.current.Use();
            }
        }

        private bool TooltipInputSuppressed() =>
            _draggingLeftPanel ||
            _draggingRightPanel ||
            _draggingCanvasPanel ||
            _resizingLeftPanel ||
            _resizingRightPanel ||
            _resizingCanvasPanel ||
            _draggingPanelDivider != AutomationPanelDivider.None ||
            _panningGraphCanvas ||
            _draggingNodeId != 0 ||
            GraphPointerInputActive() ||
            _closePromptOpen;

        private float StatusHeightScaled() =>
            EsuHudLayout.BottomStripHeight();

        private float LeftPanelTopLimit() => EsuHudLayout.EditorPanelTopLimit(ToolbarHeight);

        private float MinLeftPanelWidth() => EsuHudLayout.Scale(292f);

        private float MinLeftPanelHeight() => EsuHudLayout.Scale(LeftPanelMinHeight);

        private float MaxLeftPanelWidth() => Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.52f);

        private float MaxLeftPanelHeight() =>
            Mathf.Max(MinLeftPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private float MinRightPanelWidth() => EsuHudLayout.Scale(248f);

        private float MinRightPanelHeight() => EsuHudLayout.Scale(RightPanelMinHeight);

        private float MaxRightPanelWidth() => Mathf.Max(MinRightPanelWidth(), Screen.width * 0.38f);

        private float MaxRightPanelHeight() =>
            Mathf.Max(MinRightPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private float MinCanvasWidth() => EsuHudLayout.Scale(CanvasMinWidth);

        private float MinCanvasHeight() => EsuHudLayout.Scale(CanvasMinHeight);

        private float MaxCanvasWidth() =>
            Mathf.Max(MinCanvasWidth(), Screen.width - EsuHudLayout.EditorSideMargin * 2f);

        private float MaxCanvasHeight() =>
            Mathf.Max(MinCanvasHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private Rect DefaultLeftPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(LeftPanelWidth),
                Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.28f));
            float height = MaxLeftPanelHeight();
            return new Rect(EsuHudLayout.EditorSideMargin, LeftPanelTopLimit(), width, height);
        }

        private Rect DefaultRightPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(RightPanelWidth),
                Mathf.Max(MinRightPanelWidth(), Screen.width * 0.24f));
            float height = MaxRightPanelHeight();
            float x = Mathf.Max(EsuHudLayout.EditorSideMargin, Screen.width - width - EsuHudLayout.EditorSideMargin);
            return new Rect(x, LeftPanelTopLimit(), width, height);
        }

        private Rect DefaultCanvasRect()
        {
            float margin = EsuHudLayout.EditorSideMargin;
            float top = LeftPanelTopLimit();
            float bottom = EsuHudLayout.BottomPanelLimit(StatusHeightScaled());
            float height = Mathf.Max(EsuHudLayout.Scale(CanvasMinHeight), Screen.height - top - bottom);
            height = Mathf.Min(height, Screen.height - top - bottom);
            return new Rect(
                margin,
                top,
                Mathf.Max(1f, Screen.width - margin * 2f),
                Mathf.Max(1f, height));
        }

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _leftPanelRect = DefaultLeftPanelRect();
            _rightPanelRect = DefaultRightPanelRect();
            _canvasRect = DefaultCanvasRect();
            _draggingLeftPanel = false;
            _draggingRightPanel = false;
            _draggingCanvasPanel = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _resizingCanvasPanel = false;
            _graphZoom = 1.08f;
            _graphScroll = Vector2.zero;
            _layoutResetGeneration = EsuHudLayout.ResetGeneration;
            s_layoutGeneration = _layoutResetGeneration;
        }

        private void HandleLeftPanelResize()
        {
            HandlePanelResize(
                ref _leftPanelRect,
                ref _resizingLeftPanel,
                ref _leftPanelResizeStart,
                ref _leftPanelResizeMouseStart,
                resizeFromLeft: false,
                MinLeftPanelWidth(),
                MinLeftPanelHeight(),
                MaxLeftPanelWidth(),
                MaxLeftPanelHeight());
        }

        private void HandleRightPanelResize()
        {
            HandlePanelResize(
                ref _rightPanelRect,
                ref _resizingRightPanel,
                ref _rightPanelResizeStart,
                ref _rightPanelResizeMouseStart,
                resizeFromLeft: true,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight());
        }

        private void HandleCanvasPanelResize()
        {
            HandlePanelResize(
                ref _canvasRect,
                ref _resizingCanvasPanel,
                ref _canvasPanelResizeStart,
                ref _canvasPanelResizeMouseStart,
                resizeFromLeft: false,
                MinCanvasWidth(),
                MinCanvasHeight(),
                MaxCanvasWidth(),
                MaxCanvasHeight());
        }

        private void HandleLeftPanelDrag()
        {
            HandlePanelDrag(
                ref _leftPanelRect,
                ref _draggingLeftPanel,
                ref _leftPanelDragOffset,
                PanelHeaderDragRect(_leftPanelRect, 74f),
                _resizingLeftPanel,
                MinLeftPanelWidth(),
                MinLeftPanelHeight(),
                MaxLeftPanelWidth(),
                MaxLeftPanelHeight());
        }

        private void HandleRightPanelDrag()
        {
            HandlePanelDrag(
                ref _rightPanelRect,
                ref _draggingRightPanel,
                ref _rightPanelDragOffset,
                PanelHeaderDragRect(_rightPanelRect, 74f),
                _resizingRightPanel,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight());
        }

        private void HandleCanvasPanelDrag()
        {
            HandlePanelDrag(
                ref _canvasRect,
                ref _draggingCanvasPanel,
                ref _canvasPanelDragOffset,
                PanelHeaderDragRect(_canvasRect, 286f),
                _resizingCanvasPanel,
                MinCanvasWidth(),
                MinCanvasHeight(),
                MaxCanvasWidth(),
                MaxCanvasHeight());
        }

        private void HandlePanelResize(
            ref Rect rect,
            ref bool resizing,
            ref Rect resizeStart,
            ref Vector2 resizeMouseStart,
            bool resizeFromLeft,
            float minWidth,
            float minHeight,
            float maxWidth,
            float maxHeight)
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
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
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
                    LeftPanelTopLimit(),
                    EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && resizing)
            {
                resizing = false;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private void HandlePanelDrag(
            ref Rect rect,
            ref bool dragging,
            ref Vector2 dragOffset,
            Rect dragRect,
            bool resizing,
            float minWidth,
            float minHeight,
            float maxWidth,
            float maxHeight)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (resizing)
                return;

            if (current.type == EventType.MouseDown)
                dragging = false;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dragRect.Contains(current.mousePosition))
            {
                dragging = true;
                dragOffset = current.mousePosition - new Vector2(rect.x, rect.y);
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && dragging)
            {
                rect.x = current.mousePosition.x - dragOffset.x;
                rect.y = current.mousePosition.y - dragOffset.y;
                rect = EsuHudLayout.ClampPanel(
                    rect,
                    minWidth,
                    minHeight,
                    maxWidth,
                    maxHeight,
                    LeftPanelTopLimit(),
                    EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private void DrawToolbar(int id)
        {
            EsuHudNotifications.SetActiveSource("Automation Builder");
            GUI.Box(new Rect(0f, 0f, _toolbarRect.width, _toolbarRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_toolbarRect.width, _toolbarRect.height);
            EsuHudLayout.CenteredToolbarFrame frame =
                EsuHudLayout.CalculateCenteredToolbarFrame(inner);
            EsuHudLayout.ToolbarBudget budget = frame.Budget;

            GUILayout.BeginArea(frame.Rect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth));
            {
                ModeSwitchButton();
                ToolButton(AutomationBuilderTool.Select, "select", "Select", "Select breadboards and target blocks.");
                ToolButton(AutomationBuilderTool.PlaceBreadboard, "gear", "Board", "Pick a breadboard from the right panel and place it on the craft.");
                ToolButton(AutomationBuilderTool.LinkInput, "chevron1", "Input", "Link a target block into the selected breadboard as a getter/input.");
                ToolButton(AutomationBuilderTool.LinkOutput, "chevron3", "Output", "Link the selected breadboard out to a target block as a setter/output.");
                if (IconButton("settings", "Graph", DecorationEditorTheme.ToolButton(_canvasOpen), "Open the selected breadboard graph canvas.", CanOpenCanvas))
                    OpenCanvas();
                if (IconButton(
                        "visibility",
                        "View",
                        DecorationEditorTheme.ToolButton(_viewModeMenuOpen),
                        "Current view: " + ViewModeShortName() + "."))
                {
                    _viewModeMenuOpen = !_viewModeMenuOpen;
                    if (_viewModeMenuOpen)
                        CloseAutomationContextMenu();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(budget.Gap);
            EsuHudNotifications.DrawToolbarSlot(
                frame.Rect,
                budget.NotificationWidth,
                "ESU mode: Automation Builder.",
                new Vector2(_toolbarRect.x + frame.Rect.x, _toolbarRect.y + frame.Rect.y));
            GUILayout.Space(budget.Gap);
            GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth));
            ToolbarPanelToggle("settings", "Info", ref _showLeftPanel, "Show or hide Automation Builder selection and link information.");
            ToolbarPanelToggle("build", "Blocks", ref _showRightPanel, "Show or hide the breadboard block palette.");
            if (CompactIconButton("cancel", "Clear", DecorationEditorTheme.Button, "Clear current Automation Builder selection.", _selectedBlock != null || _selectedLink != null))
                ClearSelection();
            if (CompactIconButton("delete", "Unlink", DecorationEditorTheme.Button, "Remove the selected Automation link.", _selectedLink != null))
                RemoveSelectedLink();
            if (IconButton("close", "Close", DecorationEditorTheme.Button, "Close Automation Builder."))
                RequestClose();
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ModeSwitchButton()
        {
            if (IconButton(
                    "chevron2",
                    "Deco",
                    DecorationEditorTheme.Button,
                    "Switch to Decoration Edit Mode."))
            {
                SwitchToDecorationEditRequested = true;
            }
        }

        private void ToolbarPanelToggle(string icon, string label, ref bool state, string tooltip)
        {
            if (CompactIconButton(icon, label, DecorationEditorTheme.ToolButton(state), tooltip))
                state = !state;
        }

        private void DrawViewModeMenu(Rect toolbarRect)
        {
            if (!_viewModeMenuOpen)
                return;

            _viewModeMenuRect = ViewModeMenuRect(toolbarRect);
            _viewModeMenuRect = GUI.Window(
                _viewModeMenuWindowId,
                _viewModeMenuRect,
                DrawViewModeMenuWindow,
                GUIContent.none,
                GUIStyle.none);
            GUI.BringWindowToFront(_viewModeMenuWindowId);
        }

        private void DrawViewModeMenuWindow(int id)
        {
            Rect rect = new Rect(0f, 0f, _viewModeMenuRect.width, _viewModeMenuRect.height);
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(4f);
            float buttonHeight = EsuHudLayout.Scale(26f);
            int perRow = ViewModeButtonsPerRow(rect.width);
            float buttonWidth = ViewModeButtonWidth(rect.width, perRow);

            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(rect);
            ViewModeButton(DecorationEditorViewMode.Mixed, "Mixed", ViewModeButtonRect(0, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Wireframe, "Wire", ViewModeButtonRect(1, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.DecorationOnly, "Deco", ViewModeButtonRect(2, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Mass, "Mass", ViewModeButtonRect(3, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Drag, "Drag", ViewModeButtonRect(4, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Cost, "Cost", ViewModeButtonRect(5, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Surface, "Surface", ViewModeButtonRect(6, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Important, "Important", ViewModeButtonRect(7, perRow, buttonWidth, buttonHeight, padding, gap));
            ViewModeButton(DecorationEditorViewMode.Normal, "Normal", ViewModeButtonRect(8, perRow, buttonWidth, buttonHeight, padding, gap));
            GUILayout.EndArea();
        }

        private Rect ViewModeMenuRect(Rect toolbarRect)
        {
            float margin = EsuHudLayout.Scale(8f);
            float maxWidth = Mathf.Max(1f, Screen.width - margin * 2f);
            float minWidth = Mathf.Min(EsuHudLayout.Scale(220f), maxWidth);
            float width = Mathf.Clamp(ViewModePreferredWidth(), minWidth, maxWidth);
            int rows = Mathf.CeilToInt(ViewModeMenuButtonCount / (float)ViewModeButtonsPerRow(width));
            float height = EsuHudLayout.Scale(8f) * 2f +
                           rows * EsuHudLayout.Scale(26f) +
                           Mathf.Max(0, rows - 1) * EsuHudLayout.Scale(4f);
            float x = Mathf.Clamp(
                toolbarRect.xMax - width,
                margin,
                Screen.width - width - margin);
            return new Rect(x, toolbarRect.yMax + EsuHudLayout.Scale(4f), width, height);
        }

        private static float ViewModePreferredWidth() =>
            EsuHudLayout.Scale(8f) * 2f +
            ViewModeMenuButtonCount * EsuHudLayout.Scale(68f) +
            (ViewModeMenuButtonCount - 1) * EsuHudLayout.Scale(4f);

        private static int ViewModeButtonsPerRow(float menuWidth)
        {
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(4f);
            float available = Mathf.Max(EsuHudLayout.Scale(120f), menuWidth - padding * 2f);
            float preferred = EsuHudLayout.Scale(68f);
            int preferredFit = Mathf.FloorToInt((available + gap) / (preferred + gap));
            if (preferredFit >= ViewModeMenuButtonCount)
                return ViewModeMenuButtonCount;

            float minimum = EsuHudLayout.Scale(48f);
            int minimumFit = Mathf.FloorToInt((available + gap) / (minimum + gap));
            return Mathf.Clamp(minimumFit, 1, ViewModeMenuButtonCount);
        }

        private static float ViewModeButtonWidth(float menuWidth, int perRow)
        {
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(4f);
            float available = Mathf.Max(EsuHudLayout.Scale(120f), menuWidth - padding * 2f);
            return Mathf.Max(
                EsuHudLayout.Scale(42f),
                Mathf.Floor((available - Mathf.Max(0, perRow - 1) * gap) / Mathf.Max(1, perRow)));
        }

        private static Rect ViewModeButtonRect(
            int index,
            int perRow,
            float width,
            float height,
            float padding,
            float gap)
        {
            int safePerRow = Mathf.Max(1, perRow);
            int row = index / safePerRow;
            int column = index % safePerRow;
            return new Rect(
                padding + column * (width + gap),
                padding + row * (height + gap),
                width,
                height);
        }

        private void ViewModeButton(DecorationEditorViewMode mode, string label, Rect rect)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(label, "Switch Automation Builder view to " + ViewModeDisplayName(mode) + "."),
                    DecorationEditorTheme.ToolButton(_viewMode == mode)))
            {
                SelectViewMode(mode);
            }
        }

        private void SelectViewMode(DecorationEditorViewMode mode)
        {
            _viewMode = mode;
            _viewModeMenuOpen = false;
            ApplyAutomationViewMode();
            InfoStore.Add("Automation Builder view: " + ViewModeDisplayName(_viewMode));
        }

        private void CycleAutomationViewMode()
        {
            int index = Array.IndexOf(s_viewModeCycle, _viewMode);
            if (index < 0)
                index = 0;

            SelectViewMode(s_viewModeCycle[(index + 1) % s_viewModeCycle.Length]);
        }

        private string ViewModeShortName()
        {
            switch (_viewMode)
            {
                case DecorationEditorViewMode.Normal:
                    return "Normal";
                case DecorationEditorViewMode.Wireframe:
                    return "Wire";
                case DecorationEditorViewMode.DecorationOnly:
                    return "Deco";
                case DecorationEditorViewMode.Mass:
                    return "Mass";
                case DecorationEditorViewMode.Drag:
                    return "Drag";
                case DecorationEditorViewMode.Cost:
                    return "Cost";
                case DecorationEditorViewMode.Surface:
                    return "Surface";
                case DecorationEditorViewMode.Important:
                    return "Important";
                default:
                    return "Mixed";
            }
        }

        private static string ViewModeDisplayName(DecorationEditorViewMode mode)
        {
            switch (mode)
            {
                case DecorationEditorViewMode.Normal:
                    return "Normal";
                case DecorationEditorViewMode.Wireframe:
                    return "Wireframe";
                case DecorationEditorViewMode.DecorationOnly:
                    return "Decorations only";
                case DecorationEditorViewMode.Mass:
                    return "Mass";
                case DecorationEditorViewMode.Drag:
                    return "Drag";
                case DecorationEditorViewMode.Cost:
                    return "Cost";
                case DecorationEditorViewMode.Surface:
                    return "Surface";
                case DecorationEditorViewMode.Important:
                    return "Important";
                default:
                    return "Mixed";
            }
        }

        private bool HasUnappliedAutomationChanges => _automationDirty;

        private Rect AutomationClosePromptRect()
        {
            float availableWidth = Mathf.Max(1f, Screen.width - EsuHudLayout.Scale(40f));
            float availableHeight = Mathf.Max(1f, Screen.height - EsuHudLayout.Scale(40f));
            float width = Mathf.Max(
                Mathf.Min(EsuHudLayout.Scale(430f), availableWidth),
                Mathf.Min(EsuHudLayout.Scale(660f), availableWidth));
            float height = Mathf.Max(
                Mathf.Min(EsuHudLayout.Scale(210f), availableHeight),
                Mathf.Min(EsuHudLayout.Scale(260f), availableHeight));
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        private void DrawAutomationClosePrompt()
        {
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                DecorationEditorTheme.DimTexture);

            Rect rect = AutomationClosePromptRect();
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect inner = EsuHudLayout.PanelInnerRect(rect, 10f);
            float gap = EsuHudLayout.Scale(8f);
            float headerHeight = EsuHudLayout.Scale(34f);
            float warningHeight = EsuHudLayout.Scale(26f);
            float actionHeight = EsuHudLayout.Scale(40f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            GUI.Box(headerRect, GUIContent.none, DecorationEditorTheme.DialogHeader);
            GUI.Label(
                headerRect,
                new GUIContent("Unapplied automation", DecorationEditorIconCatalog.Get("save")),
                DecorationEditorTheme.DialogTitle);

            float y = headerRect.yMax + gap;
            Rect warningRect = new Rect(inner.x, y, inner.width, warningHeight);
            GUI.Label(
                warningRect,
                "Automation Builder has graph changes that have not been applied.",
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
                Mathf.Max(EsuHudLayout.Scale(42f), actionsRect.y - gap - y));
            GUI.Label(
                bodyRect,
                "Apply validates and connects the visual ESU graph. Imported native wires are shown read-only. Close anyway keeps native breadboard components as they are.",
                DecorationEditorTheme.DialogBody);

            DrawAutomationClosePromptActions(actionsRect);
            HandleAutomationClosePromptKeyboard();
        }

        private void DrawAutomationClosePromptActions(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float buttonWidth = Mathf.Max(1f, (rect.width - gap * 2f) / 3f);
            Rect applyRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect closeAnywayRect = new Rect(applyRect.xMax + gap, rect.y, buttonWidth, rect.height);
            Rect keepEditingRect = new Rect(closeAnywayRect.xMax + gap, rect.y, buttonWidth, rect.height);

            if (PromptActionButton(
                    applyRect,
                    "save",
                    "Apply and close",
                    "Apply graph connections, then close Automation Builder.",
                    DecorationEditorTheme.DialogActiveButton))
                ApplyAndCloseFromAutomationPrompt();

            if (PromptActionButton(
                    closeAnywayRect,
                    "cancel",
                    "Close anyway",
                    "Close Automation Builder without applying graph connections.",
                    DecorationEditorTheme.DialogButton))
                CloseAnywayFromAutomationPrompt();

            if (PromptActionButton(
                    keepEditingRect,
                    "close",
                    "Keep editing",
                    "Close this warning and return to Automation Builder.",
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

            if (current.keyCode == KeyCode.Return ||
                current.keyCode == KeyCode.KeypadEnter)
            {
                ApplyAndCloseFromAutomationPrompt();
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                CloseAutomationClosePrompt();
                InfoStore.Add("Automation Builder close cancelled.");
                current.Use();
                return;
            }

            current.Use();
        }

        private void OpenAutomationClosePrompt()
        {
            _closePromptOpen = true;
            _viewModeMenuOpen = false;
            CloseAutomationContextMenu();
            InfoStore.Add("Unapplied Automation Builder graph changes. Apply, close anyway, or keep editing.");
        }

        private void CloseAutomationClosePrompt()
        {
            _closePromptOpen = false;
        }

        private void ApplyAndCloseFromAutomationPrompt()
        {
            if (!CanOpenCanvas)
            {
                InfoStore.Add("Automation Builder could not apply because the selected breadboard is no longer available.");
                return;
            }

            if (!ApplyGraphToNativeBoard())
            {
                InfoStore.Add("Automation Builder close blocked until Apply succeeds or you choose Close anyway.");
                return;
            }

            CloseAutomationClosePrompt();
            _closeRequested = true;
        }

        private void CloseAnywayFromAutomationPrompt()
        {
            CloseAutomationClosePrompt();
            ClearAutomationDirty();
            _closeRequested = true;
            InfoStore.Add("Automation Builder closed without applying graph connections.");
        }

        private void MarkAutomationDirty()
        {
            _automationDirty = true;
            InvalidateAutomationDisplayCache();
        }

        private void ClearAutomationDirty()
        {
            _automationDirty = false;
        }

        private void InvalidateNativeAutomationCache()
        {
            _nativeAutomationCacheDirty = true;
            _nextNativeAutomationRefreshTime = 0f;
            InvalidateAutomationDisplayCache();
        }

        private void InvalidateAutomationDisplayCache()
        {
            _automationDisplayCacheDirty = true;
            _cachedSelectedNativePlan = null;
            _cachedBlockProgramLines = new List<string>();
            _cachedGraphLiveValueLines = new List<string>();
            _displayCacheNativeVersion = -1;
            _displayCacheGraphKey = null;
            _graphLiveValueCacheKey = null;
            _graphLiveValueCacheNativeVersion = -1;
            _nextGraphLiveValueRefreshTime = 0f;
            _nextLivePreviewRefreshTime = 0f;
            _livePreviewNativeVersion = -1;
            _liveValuePreviewCache.Clear();
        }

        private void InvalidateLivePreviewCache()
        {
            _nextLivePreviewRefreshTime = 0f;
            _livePreviewNativeVersion = -1;
            _liveValuePreviewCache.Clear();
        }

        private void ForceRefreshSelectedBreadboardNativeState()
        {
            if (_selectedBreadboard == null)
                return;

            RefreshNativeAutomationCache(force: true);
            SyncSelectedGraphFromNativeIfLoaded(force: true);
        }

        private void ToolButton(
            AutomationBuilderTool tool,
            string icon,
            string label,
            string tooltip)
        {
            if (IconButton(icon, label, DecorationEditorTheme.ToolButton(_tool == tool), tooltip))
                SetTool(tool);
        }

        private void SetTool(AutomationBuilderTool tool)
        {
            _tool = tool;
            _viewModeMenuOpen = false;
            CloseAutomationContextMenu();
            _placementArmed = tool == AutomationBuilderTool.PlaceBreadboard;
            if (_placementArmed)
                InfoStore.Add("Automation Builder: choose a surface cell for the breadboard.");
        }

        private bool CanOpenCanvas =>
            _selectedBreadboard != null &&
            _selectedBreadboard.IsStillValidBreadboard &&
            TryGetSelectedNativeBreadboard(out _);

        private void OpenCanvas()
        {
            if (!CanOpenCanvas)
            {
                InfoStore.Add("Select a placed breadboard before opening the Automation graph.");
                return;
            }

            ForceRefreshSelectedBreadboardNativeState();
            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard, force: true);
            EnsureStarterGraph(graph, _selectedBreadboard);
            _canvasOpen = true;
            _viewModeMenuOpen = false;
            CloseAutomationContextMenu();
            _placementArmed = false;
            _tool = AutomationBuilderTool.Select;
        }

        private void DrawLeftPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, _leftPanelRect.width - inset * 2f, _leftPanelRect.height - inset * 2f);
            float headerHeight = EsuHudLayout.Scale(28f);
            float selectionHeight = EsuHudLayout.Scale(150f);
            float actionsHeight = EsuHudLayout.Scale(34f);
            float gap = EsuHudLayout.Scale(6f);

            GUILayout.BeginArea(new Rect(inner.x, inner.y, inner.width, headerHeight));
            DrawPanelHeader("Automation Builder", "settings", ref _showLeftPanel);
            GUILayout.EndArea();

            Rect selectionRect = new Rect(inner.x, inner.y + headerHeight + gap, inner.width, selectionHeight);
            GUI.Box(selectionRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(selectionRect, 5f));
            DrawSelectionSummary();
            GUILayout.EndArea();

            Rect actionsRect = new Rect(inner.x, inner.yMax - actionsHeight, inner.width, actionsHeight);
            Rect linksRect = new Rect(
                inner.x,
                selectionRect.yMax + gap,
                inner.width,
                Mathf.Max(1f, actionsRect.y - selectionRect.yMax - gap));
            if (_showLinksSection)
                DrawSplitLinkPanel(linksRect);
            else
                GUI.Label(linksRect, "Links hidden", DecorationEditorTheme.MiniWrap);

            GUILayout.BeginArea(actionsRect);
            DrawLeftPanelActions();
            GUILayout.EndArea();
        }

        private void DrawSelectionSummary()
        {
            GUILayout.Label("Selection", DecorationEditorTheme.SubHeader);
            if (_selectedBlock == null)
            {
                GUILayout.Label("No block selected", DecorationEditorTheme.MiniWrap);
                return;
            }

            LabelRow("Block", _selectedBlock.Name);
            LabelRow("Cell", FormatCell(_selectedBlock.Cell));
            LabelRow("Type", _selectedBlock.IsBreadboard ? "Breadboard" : "Target block");
            if (_selectedBreadboard != null)
            {
                LabelRow("Breadboard", _selectedBreadboard.Name);
                LabelRow("Graph", SyncedGraphFor(_selectedBreadboard).Nodes.Count.ToString(CultureInfo.InvariantCulture) + " node(s)");
            }
        }

        private void DrawSplitLinkPanel(Rect rect)
        {
            List<AutomationLink> inputLinks = _links
                .Where(link => link.Kind == AutomationLinkKind.InputToBreadboard)
                .ToList();
            List<AutomationLink> outputLinks = _links
                .Where(link => link.Kind == AutomationLinkKind.BreadboardToOutput)
                .ToList();

            SplitAutomationVerticalStack(
                rect,
                _leftInputOutputRatio,
                AutomationDividerGap(),
                AutomationDividerMinimumPanelHeight(),
                out Rect inputRect,
                out Rect dividerRect,
                out Rect outputRect,
                out _leftInputOutputRatio);
            HandleAutomationDividerDrag(
                AutomationPanelDivider.LeftInputOutput,
                rect,
                dividerRect,
                AutomationDividerGap(),
                ref _leftInputOutputRatio);
            DrawLinkListBox(inputRect, "Input Links", inputLinks, ref _inputLinksScroll);
            DrawAutomationDividerGrip(dividerRect, AutomationPanelDivider.LeftInputOutput);
            DrawLinkListBox(outputRect, "Output Links", outputLinks, ref _outputLinksScroll);
        }

        private void DrawLinkListBox(
            Rect rect,
            string title,
            List<AutomationLink> links,
            ref Vector2 scroll)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect header = new Rect(rect.x + EsuHudLayout.Scale(6f), rect.y + EsuHudLayout.Scale(4f), rect.width - EsuHudLayout.Scale(12f), EsuHudLayout.Scale(24f));
            GUI.Label(header, title + " (" + links.Count.ToString(CultureInfo.InvariantCulture) + ")", DecorationEditorTheme.SubHeader);
            Rect scrollRect = new Rect(
                rect.x + EsuHudLayout.Scale(5f),
                header.yMax + EsuHudLayout.Scale(3f),
                rect.width - EsuHudLayout.Scale(10f),
                Mathf.Max(1f, rect.yMax - header.yMax - EsuHudLayout.Scale(8f)));
            float rowHeight = EsuHudLayout.Scale(LinkRowHeight);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(1f, scrollRect.width - EsuHudLayout.Scale(18f)), Mathf.Max(scrollRect.height, links.Count * rowHeight + EsuHudLayout.Scale(4f)));
            scroll = GUI.BeginScrollView(scrollRect, scroll, viewRect);
            if (links.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, viewRect.width - 8f, rowHeight), "No " + title.ToLowerInvariant() + " yet", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                for (int index = 0; index < links.Count; index++)
                {
                    AutomationLink link = links[index];
                    Rect row = new Rect(0f, index * rowHeight, viewRect.width, rowHeight - EsuHudLayout.Scale(2f));
                    GUIStyle style = ReferenceEquals(link, _selectedLink)
                        ? DecorationEditorTheme.RowSelected
                        : DecorationEditorTheme.Row;
                    if (GUI.Button(row, GUIContent.none, style))
                        SelectLink(link);

                    Rect swatch = new Rect(row.x + EsuHudLayout.Scale(5f), row.y + EsuHudLayout.Scale(7f), EsuHudLayout.Scale(12f), EsuHudLayout.Scale(12f));
                    Color previous = GUI.color;
                    GUI.color = link.Color;
                    GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                    GUI.color = previous;

                    Rect label = new Rect(swatch.xMax + EsuHudLayout.Scale(6f), row.y + EsuHudLayout.Scale(2f), row.width - swatch.width - EsuHudLayout.Scale(16f), row.height);
                    GUI.Label(label, LinkListLabel(link), DecorationEditorTheme.Mini);
                }
            }

            GUI.EndScrollView();
        }

        private void SelectLink(AutomationLink link)
        {
            if (link == null)
                return;

            _selectedLink = link;
            _selectedBlock = link.Kind == AutomationLinkKind.InputToBreadboard
                ? link.Source
                : link.Target;
            if (_selectedBlock?.IsResolved != true)
                _selectedBlock = BreadboardForLink(link);
            if (link.Source?.IsBreadboard == true)
                _selectedBreadboard = link.Source;
            else if (link.Target?.IsBreadboard == true)
                _selectedBreadboard = link.Target;
        }

        private static string LinkListLabel(AutomationLink link)
        {
            if (link == null)
                return "Missing link";

            string prefix = link.Kind == AutomationLinkKind.InputToBreadboard
                ? "IN "
                : "OUT";
            string source = BlockRefCurrentName(link.Source);
            string target = BlockRefCurrentName(link.Target);
            string native = string.IsNullOrWhiteSpace(link.NativeStatus)
                ? "native"
                : link.NativeStatus;
            return prefix + " " + source + " -> " + target + " | " + link.Property + " | " + native;
        }

        private static void SplitAutomationVerticalStack(
            Rect stackRect,
            float bottomRatio,
            float gap,
            float minHeight,
            out Rect topRect,
            out Rect dividerRect,
            out Rect bottomRect,
            out float resolvedBottomRatio)
        {
            resolvedBottomRatio = Mathf.Clamp(bottomRatio, 0.18f, 0.82f);
            float available = Mathf.Max(0f, stackRect.height - gap);
            float bottomHeight = Mathf.Clamp(available * resolvedBottomRatio, minHeight, Mathf.Max(minHeight, available - minHeight));
            float topHeight = Mathf.Max(0f, available - bottomHeight);
            topRect = new Rect(stackRect.x, stackRect.y, stackRect.width, topHeight);
            dividerRect = new Rect(stackRect.x, topRect.yMax, stackRect.width, gap);
            bottomRect = new Rect(stackRect.x, dividerRect.yMax, stackRect.width, bottomHeight);
            resolvedBottomRatio = available > 0f
                ? bottomHeight / available
                : resolvedBottomRatio;
        }

        private static float AutomationDividerGap() =>
            EsuHudLayout.Scale(PanelDividerHeight);

        private static float AutomationDividerMinimumPanelHeight() =>
            EsuHudLayout.Scale(PanelSectionMinHeight);

        private void HandleAutomationDividerDrag(
            AutomationPanelDivider divider,
            Rect stackRect,
            Rect dividerRect,
            float gap,
            ref float bottomRatio)
        {
            Event current = Event.current;
            if (current == null || divider == AutomationPanelDivider.None)
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dividerRect.Contains(current.mousePosition))
            {
                _draggingPanelDivider = divider;
                _panelDividerDragStartMouseY = current.mousePosition.y;
                _panelDividerDragStartRatio = bottomRatio;
                current.Use();
                return;
            }

            if (_draggingPanelDivider != divider)
                return;

            if (current.type == EventType.MouseDrag)
            {
                float available = Mathf.Max(1f, stackRect.height - gap);
                float startBottomHeight = available * _panelDividerDragStartRatio;
                float deltaY = current.mousePosition.y - _panelDividerDragStartMouseY;
                float nextBottomHeight = Mathf.Clamp(
                    startBottomHeight - deltaY,
                    AutomationDividerMinimumPanelHeight(),
                    Mathf.Max(AutomationDividerMinimumPanelHeight(), available - AutomationDividerMinimumPanelHeight()));
                bottomRatio = Mathf.Clamp(nextBottomHeight / available, 0.18f, 0.82f);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
            {
                _draggingPanelDivider = AutomationPanelDivider.None;
                current.Use();
            }
        }

        private void DrawAutomationDividerGrip(
            Rect dividerRect,
            AutomationPanelDivider divider)
        {
            bool active = _draggingPanelDivider == divider;
            bool hovered = dividerRect.Contains(Event.current?.mousePosition ?? Vector2.zero);
            EsuHudLayout.DrawStackDividerGrip(dividerRect, active || hovered);
            if (hovered)
                EsuCursorTooltip.Register(dividerRect, "Drag to resize Automation Builder sections.");
        }

        private void DrawLeftPanelActions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("Open Graph", "Open the selected breadboard graph canvas."),
                    CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                OpenCanvas();
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _selectedLink != null;
            if (GUILayout.Button(
                    new GUIContent("Remove Link", "Remove the selected Automation link."),
                    _selectedLink != null ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                RemoveSelectedLink();
            }

            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawRightPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _rightPanelRect.width, _rightPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, _rightPanelRect.width - inset * 2f, _rightPanelRect.height - inset * 2f);
            float headerHeight = EsuHudLayout.Scale(28f);
            float gap = EsuHudLayout.Scale(6f);

            GUILayout.BeginArea(new Rect(inner.x, inner.y, inner.width, headerHeight));
            DrawPanelHeader("Breadboard Blocks", "build", ref _showRightPanel);
            GUILayout.EndArea();

            Rect bodyRect = new Rect(inner.x, inner.y + headerHeight + gap, inner.width, inner.height - headerHeight - gap);
            GUI.Box(bodyRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(bodyRect, 5f));
            if (DrawSectionHeader("Place", ref _showBreadboardSection, "Show or hide breadboard placement tools."))
                DrawBreadboardPlacementSection();
            DecorationEditorTheme.Separator();
            DrawLinkModeSection();
            GUILayout.EndArea();
        }

        private void DrawBreadboardPlacementSection()
        {
            bool found = AutomationBreadboardCatalog.TryResolveBreadboard(
                _selectedBreadboardVariant,
                out ItemDefinition definition,
                out string message);
            string name = found
                ? AutomationBreadboardCatalog.ItemName(definition)
                : "Breadboard";
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("AI", "Place the AI breadboard variant."),
                    DecorationEditorTheme.ToolButton(_selectedBreadboardVariant == AutomationBreadboardVariant.Ai),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                _selectedBreadboardVariant = AutomationBreadboardVariant.Ai;
            }

            if (GUILayout.Button(
                    new GUIContent("Basic", "Place the basic breadboard variant."),
                    DecorationEditorTheme.ToolButton(_selectedBreadboardVariant == AutomationBreadboardVariant.Basic),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                _selectedBreadboardVariant = AutomationBreadboardVariant.Basic;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(name, DecorationEditorTheme.SubHeader);
            GUILayout.Label(message ?? "Loaded breadboard item resolver.", found ? DecorationEditorTheme.MiniWrap : DecorationEditorTheme.Warning);
            if (GUILayout.Button(
                    new GUIContent("Select Breadboard", DecorationEditorIconCatalog.Get("gear"), "Arm breadboard placement. The block follows the mouse until placed."),
                    DecorationEditorTheme.ToolButton(_placementArmed),
                    GUILayout.Height(EsuHudLayout.Scale(42f))))
            {
                SetTool(AutomationBuilderTool.PlaceBreadboard);
            }
        }

        private void DrawLinkModeSection()
        {
            GUILayout.Label("Link Mode", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("Input", "Click a block then a breadboard, or select a breadboard and click a target block."),
                    DecorationEditorTheme.ToolButton(_tool == AutomationBuilderTool.LinkInput),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                SetTool(AutomationBuilderTool.LinkInput);
            }

            if (GUILayout.Button(
                    new GUIContent("Output", "Click a breadboard then a target block to create a setter/output link."),
                    DecorationEditorTheme.ToolButton(_tool == AutomationBuilderTool.LinkOutput),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                SetTool(AutomationBuilderTool.LinkOutput);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Input links flow from a block into the breadboard. Output links flow from the breadboard into a block.", DecorationEditorTheme.MiniWrap);
        }

        private void DrawStatusStrip(int id)
        {
            GUI.Box(new Rect(0f, 0f, _statusRect.width, _statusRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_statusRect.width, _statusRect.height, 4f);
            float headerHeight = EsuHudLayout.Scale(24f);
            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            float statusHeight = EsuHudLayout.Scale(25f);
            float controlsHeight = EsuHudLayout.Scale(28f);
            float footerHeight = EsuHudLayout.Scale(18f);
            float gap = EsuHudLayout.Scale(4f);
            float y = inner.y;

            DrawBottomHeader(new Rect(inner.x, y, inner.width, headerHeight));
            y += headerHeight;
            DrawCyanLine(new Rect(inner.x, y, inner.width, separatorHeight));
            y += separatorHeight + gap;

            GUI.Label(new Rect(inner.x, y, inner.width, statusHeight), StatusSummary(), DecorationEditorTheme.Status);
            y += statusHeight + gap;

            DrawBottomControls(new Rect(inner.x, y, inner.width, controlsHeight));
            y += controlsHeight + gap;

            if (y < inner.yMax)
            {
                GUI.Label(
                    new Rect(inner.x, y, inner.width, Mathf.Min(footerHeight, inner.yMax - y)),
                    _canvasOpen
                        ? "Graph canvas open | code flow runs top to bottom through connected nodes."
                        : "Select order matters: breadboard -> block creates output, block -> breadboard creates input.",
                    DecorationEditorTheme.Mini);
            }
        }

        private void DrawBottomHeader(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float titleWidth = Mathf.Min(EsuHudLayout.Scale(188f), rect.width * 0.42f);
            float stateWidth = Mathf.Min(EsuHudLayout.Scale(138f), rect.width * 0.32f);
            Rect title = new Rect(rect.x, rect.y, titleWidth, rect.height);
            Rect state = new Rect(rect.xMax - stateWidth, rect.y, stateWidth, rect.height);
            Rect mode = new Rect(
                title.xMax + gap,
                rect.y,
                Mathf.Max(1f, state.x - title.xMax - gap * 2f),
                rect.height);

            GUI.Label(title, "Automation Builder", DecorationEditorTheme.SubHeader);
            GUI.Label(mode, "Mode: Automation | Tab to Decoration Edit when clean", DecorationEditorTheme.Body);
            GUI.Label(state, _placementArmed ? "Placing board" : (_canvasOpen ? "Graph open" : "Ready"), _placementArmed ? DecorationEditorTheme.Warning : DecorationEditorTheme.Mini);
        }

        private void DrawBottomControls(Rect rect)
        {
            float gap = EsuHudLayout.Scale(6f);
            float width = Mathf.Max(EsuHudLayout.Scale(52f), (rect.width - gap * 8f) / 9f);
            Rect button = new Rect(rect.x, rect.y, width, rect.height);
            if (GUI.Button(button, new GUIContent("Place", "Arm breadboard placement."), DecorationEditorTheme.ToolButton(_tool == AutomationBuilderTool.PlaceBreadboard)))
                SetTool(AutomationBuilderTool.PlaceBreadboard);
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Input", "Use input/getter link mode."), DecorationEditorTheme.ToolButton(_tool == AutomationBuilderTool.LinkInput)))
                SetTool(AutomationBuilderTool.LinkInput);
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Output", "Use output/setter link mode."), DecorationEditorTheme.ToolButton(_tool == AutomationBuilderTool.LinkOutput)))
                SetTool(AutomationBuilderTool.LinkOutput);
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Graph", "Open the selected breadboard graph."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                OpenCanvas();
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Check", "Build a non-mutating native plan for the selected graph."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                CheckNativeGraphPlan();
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Apply", "Emit the selected graph to the native breadboard board."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                ApplyGraphToNativeBoard();
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Revert", "Remove only ESU-owned generated native automation from this breadboard."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                RevertEsuOwnedNativeGraph();
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Cancel", "Cancel placement and close canvas."), DecorationEditorTheme.Button))
            {
                _placementArmed = false;
                CloseCanvasEditor();
                if (_tool == AutomationBuilderTool.PlaceBreadboard)
                    _tool = AutomationBuilderTool.Select;
            }
            button.x += width + gap;
            if (GUI.Button(button, new GUIContent("Close", "Close Automation Builder."), DecorationEditorTheme.Button))
                RequestClose();
        }

        private string StatusSummary()
        {
            string selection = _selectedBlock == null
                ? "no block selected"
                : _selectedBlock.Name + " at " + FormatCell(_selectedBlock.Cell);
            string breadboard = _selectedBreadboard == null
                ? "no breadboard"
                : _selectedBreadboard.Name;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Automation | {0} | links {1:N0} ({2}) | selected {3} | {4}",
                ToolLabel(),
                _links.Count,
                AutomationLinkVisibilityLabel(_linkVisibility),
                selection,
                breadboard);
        }

        private string ToolLabel()
        {
            switch (_tool)
            {
                case AutomationBuilderTool.PlaceBreadboard:
                    return "Placing breadboard";
                case AutomationBuilderTool.LinkInput:
                    return "Input linking";
                case AutomationBuilderTool.LinkOutput:
                    return "Output linking";
                default:
                    return "Selecting";
            }
        }

        private void DrawGraphCanvas(int id)
        {
            GUI.Box(new Rect(0f, 0f, _canvasRect.width, _canvasRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_canvasRect.width, _canvasRect.height);
            float headerHeight = EsuHudLayout.Scale(32f);
            Rect header = new Rect(inner.x, inner.y, inner.width, headerHeight);
            DrawCanvasHeader(header);

            float y = header.yMax + EsuHudLayout.Scale(6f);
            float gap = EsuHudLayout.Scale(8f);
            Rect paletteRect = new Rect(
                inner.x,
                y,
                EsuHudLayout.Scale(CanvasLeftPaletteWidth),
                Mathf.Max(1f, inner.yMax - y));
            Rect planRect = new Rect(
                inner.xMax - EsuHudLayout.Scale(CanvasPlanWidth),
                y,
                EsuHudLayout.Scale(CanvasPlanWidth),
                Mathf.Max(1f, inner.yMax - y));
            Rect canvasRect = new Rect(
                paletteRect.xMax + gap,
                y,
                Mathf.Max(1f, planRect.x - paletteRect.xMax - gap * 2f),
                Mathf.Max(1f, inner.yMax - y));
            _lastCanvasWorkspaceRect = canvasRect;
            _lastCanvasPaletteRect = new Rect(
                _canvasRect.x + paletteRect.x,
                _canvasRect.y + paletteRect.y,
                paletteRect.width,
                paletteRect.height);

            GUI.Box(canvasRect, GUIContent.none, DecorationEditorTheme.Panel);
            AutomationGraph foregroundGraph = _selectedBreadboard == null ? null : GraphFor(_selectedBreadboard);
            RefreshGraphPropertyPickerRect(foregroundGraph, canvasRect, planRect);
            ConsumeGraphPropertyPickerOutsideMouseDown();
            DrawGraphWorkspace(canvasRect);
            GUI.Box(paletteRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(paletteRect, 6f));
            DrawBlockPalettePanel();
            GUILayout.EndArea();
            GUI.Box(planRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(planRect, 6f));
            DrawGeneratedNativePlanPanel();
            GUILayout.EndArea();
            DrawGraphPropertyPicker(foregroundGraph, canvasRect, planRect);
            DrawPaletteSnapPreview(canvasRect);
            DrawPaletteDragGhost(canvasRect);
            HandlePaletteBlockDrop(canvasRect);
        }

        private void DrawCanvasHeader(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Header);
            Rect title = new Rect(rect.x + EsuHudLayout.Scale(8f), rect.y, Mathf.Max(1f, rect.width - EsuHudLayout.Scale(494f)), rect.height);
            string board = _selectedBreadboard == null ? "No breadboard selected" : _selectedBreadboard.Name + " graph";
            GUI.Label(title, board, DecorationEditorTheme.DialogTitle);
            Rect close = new Rect(rect.xMax - EsuHudLayout.Scale(76f), rect.y + EsuHudLayout.Scale(4f), EsuHudLayout.Scale(68f), rect.height - EsuHudLayout.Scale(8f));
            Rect apply = new Rect(close.x - EsuHudLayout.Scale(84f), close.y, EsuHudLayout.Scale(76f), close.height);
            Rect revert = new Rect(apply.x - EsuHudLayout.Scale(84f), close.y, EsuHudLayout.Scale(76f), close.height);
            Rect check = new Rect(revert.x - EsuHudLayout.Scale(84f), close.y, EsuHudLayout.Scale(76f), close.height);
            Rect arrange = new Rect(check.x - EsuHudLayout.Scale(96f), close.y, EsuHudLayout.Scale(88f), close.height);
            if (GUI.Button(arrange, new GUIContent("Arrange", "Lay out native-wired visual blocks into readable top-to-bottom chains without changing breadboard logic."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                ArrangeNativeGraphForReadability();
            if (GUI.Button(check, new GUIContent("Check", "Build a non-mutating native plan for this graph."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                CheckNativeGraphPlan();
            if (GUI.Button(revert, new GUIContent("Revert", "Remove only ESU-owned generated native automation from this breadboard."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                RevertEsuOwnedNativeGraph();
            if (GUI.Button(apply, new GUIContent("Apply", "Emit this visual graph into the selected native breadboard board."), CanOpenCanvas ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                ApplyGraphToNativeBoard();
            if (GUI.Button(close, new GUIContent("Close", "Close the graph canvas."), DecorationEditorTheme.Button))
                CloseCanvasEditor();
        }

        private void DrawBlockPalettePanel()
        {
            GUILayout.Label("ESU Blocks", DecorationEditorTheme.SubHeader);
            GUILayout.Label("Drag blocks onto the workspace. They compile to native breadboard components.", DecorationEditorTheme.MiniWrap);
            DrawStarterFlowAction();
            DecorationEditorTheme.Separator();
            DrawLinkedHardwareQuickBlocks();
            DecorationEditorTheme.Separator();
            DrawPaletteCategoryButtons();
            DecorationEditorTheme.Separator();
            _blockPaletteScroll = GUILayout.BeginScrollView(_blockPaletteScroll);
            foreach (AutomationNodeKind kind in PaletteKinds(_selectedPaletteCategory))
                DrawPaletteBlock(kind);
            GUILayout.EndScrollView();
        }

        private void DrawStarterFlowAction()
        {
            bool enabled = CanOpenCanvas;
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            if (GUILayout.Button(
                    new GUIContent(
                        "Starter Flow",
                        DecorationEditorIconCatalog.Get("build"),
                        "Stage a Read -> Below Threshold -> If True -> Set starter program from the single linked input and output."),
                    enabled ? DecorationEditorTheme.ToolButton(false) : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(32f))))
            {
                if (TryAddStagedStarterFlow(out string message))
                {
                    SyncedGraphFor(_selectedBreadboard);
                    MarkAutomationDirty();
                }

                InfoStore.Add(message ?? "Automation Builder could not create a starter flow.");
            }

            GUI.enabled = previous;
        }

        private bool TryAddStagedStarterFlow(out string message)
        {
            message = null;
            if (_selectedBreadboard == null)
            {
                message = "Select a breadboard before staging a starter flow.";
                return false;
            }

            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard);
            List<AutomationLink> inputChoices = StarterLinkChoices(AutomationLinkKind.InputToBreadboard).ToList();
            List<AutomationLink> outputChoices = StarterLinkChoices(AutomationLinkKind.BreadboardToOutput).ToList();
            if (inputChoices.Count != 1 || outputChoices.Count != 1)
            {
                message = "Starter Flow needs exactly one linked input and one linked output. Use the block dropdowns when there are multiple choices.";
                return false;
            }

            AutomationLink inputLink = inputChoices[0];
            AutomationLink outputLink = outputChoices[0];
            if (inputLink.Source == null ||
                outputLink.Target == null ||
                !inputLink.Source.TryGetBlock(out _) ||
                !outputLink.Target.TryGetBlock(out _))
            {
                message = "Starter Flow could not resolve the linked input/output blocks.";
                return false;
            }

            AutomationGraphNode readNode = EnsureStagedLinkGraphNode(inputLink);
            AutomationGraphNode setNode = EnsureStagedLinkGraphNode(outputLink);
            if (readNode == null || setNode == null)
            {
                message = "Starter Flow could not stage the linked Read/Set blocks.";
                return false;
            }

            if (!CanStarterFlowOwnNode(readNode) ||
                !CanStarterFlowOwnNode(setNode))
            {
                message = "Starter Flow uses staged or ESU-owned Read/Set blocks. Imported native Read/Set blocks stay read-only.";
                return false;
            }

            if (graph.Nodes.Any(node =>
                    node != null &&
                    !ReferenceEquals(node, readNode) &&
                    !ReferenceEquals(node, setNode)))
            {
                message = "Starter Flow starts from a clean staged graph. Remove extra graph blocks or assemble the flow manually.";
                return false;
            }

            Rect readRect = StarterFlowRect(readNode, AutomationNodeKind.InputGetter, EsuHudLayout.Scale(80f), EsuHudLayout.Scale(56f));
            if (readNode.IsStaged)
                readNode.Rect = readRect;
            else
                readRect = readNode.Rect;

            Rect thresholdRect = new Rect(readRect.x, readRect.yMax - EsuHudLayout.Scale(2f), GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.CompareBelowThreshold));
            AutomationGraphNode thresholdNode = graph.AddStagedNode(AutomationNodeKind.CompareBelowThreshold, thresholdRect);
            ApplyNativeNodeEdits(thresholdNode, DefaultNodeLabel(AutomationNodeKind.CompareBelowThreshold), DefaultProperty(AutomationNodeKind.CompareBelowThreshold), "threshold 10");

            Rect switchRect = new Rect(thresholdRect.x, thresholdRect.yMax - EsuHudLayout.Scale(2f), GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.IfCondition));
            AutomationGraphNode switchNode = graph.AddStagedNode(AutomationNodeKind.IfCondition, switchRect);

            Rect setRect = new Rect(switchRect.x, switchRect.yMax - EsuHudLayout.Scale(2f), GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.OutputSetter));
            if (setNode.IsStaged)
                setNode.Rect = setRect;
            else
                setRect = setNode.Rect;

            Rect thresholdValueRect = ValueSlotRect(thresholdRect, AutomationNodeKind.CompareBelowThreshold, AutomationValueSlotKind.Threshold);
            AutomationGraphNode thresholdValue = graph.AddStagedNode(AutomationNodeKind.Constant, thresholdValueRect);
            ApplyNativeNodeEdits(thresholdValue, DefaultNodeLabel(AutomationNodeKind.Constant), DefaultProperty(AutomationNodeKind.Constant), "10");

            Rect thenValueRect = ValueSlotRect(switchRect, AutomationNodeKind.IfCondition, AutomationValueSlotKind.Pass);
            AutomationGraphNode thenValue = graph.AddStagedNode(AutomationNodeKind.Constant, thenValueRect);
            ApplyNativeNodeEdits(thenValue, DefaultNodeLabel(AutomationNodeKind.Constant), DefaultProperty(AutomationNodeKind.Constant), "45");

            var starterNodeIds = new HashSet<int>
            {
                readNode.Id,
                thresholdNode.Id,
                thresholdValue.Id,
                switchNode.Id,
                thenValue.Id,
                setNode.Id
            };
            var connections = graph.Connections
                .Where(connection =>
                    connection != null &&
                    !starterNodeIds.Contains(connection.FromNodeId) &&
                    !starterNodeIds.Contains(connection.ToNodeId))
                .ToList();
            connections.Add(new AutomationGraphConnection(AutomationGraphConnectionKind.Stack, readNode, thresholdNode));
            connections.Add(new AutomationGraphConnection(AutomationGraphConnectionKind.Value, thresholdValue, thresholdNode, AutomationValueSlotKind.Threshold));
            connections.Add(new AutomationGraphConnection(AutomationGraphConnectionKind.Stack, thresholdNode, switchNode));
            connections.Add(new AutomationGraphConnection(AutomationGraphConnectionKind.Value, thenValue, switchNode, AutomationValueSlotKind.Pass));
            connections.Add(new AutomationGraphConnection(AutomationGraphConnectionKind.Stack, switchNode, setNode));
            graph.RebuildConnections(connections);
            graph.SelectedNodeId = switchNode.Id;

            message = "Starter Flow staged: read " + BlockRefCurrentName(inputLink.Source) + ", below 10, if true, set " + BlockRefCurrentName(outputLink.Target) + " to 45 else 0. Press Apply to create native components and connections.";
            return true;
        }

        private IEnumerable<AutomationLink> StarterLinkChoices(AutomationLinkKind kind)
        {
            if (_selectedBreadboard == null)
                yield break;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationLink link in _links)
            {
                if (link == null || link.Kind != kind || !link.IsResolved)
                    continue;

                AutomationBlockRef breadboard = kind == AutomationLinkKind.InputToBreadboard
                    ? link.Target
                    : link.Source;
                AutomationBlockRef target = kind == AutomationLinkKind.InputToBreadboard
                    ? link.Source
                    : link.Target;
                if (breadboard?.SameBlock(_selectedBreadboard) != true || target == null)
                    continue;

                string key = (target.StableKey ?? string.Empty) + "|" + (link.Property ?? string.Empty);
                if (seen.Add(key))
                    yield return link;
            }
        }

        private bool CanStarterFlowOwnNode(AutomationGraphNode node)
        {
            return node != null &&
                   (node.IsStaged || IsEsuOwnedNativeNode(node));
        }

        private static Rect StarterFlowRect(
            AutomationGraphNode node,
            AutomationNodeKind kind,
            float fallbackX,
            float fallbackY)
        {
            if (node?.Rect.width > 0f && node.Rect.height > 0f)
            {
                return new Rect(
                    node.Rect.x,
                    node.Rect.y,
                    GraphNodeWidthForKind(kind),
                    GraphNodeHeightForKind(kind));
            }

            return new Rect(
                fallbackX,
                fallbackY,
                GraphNodeWidthForKind(kind),
                GraphNodeHeightForKind(kind));
        }

        private void DrawLinkedHardwareQuickBlocks()
        {
            GUILayout.Label("Linked Hardware", DecorationEditorTheme.SubHeader);
            if (_selectedBreadboard == null)
            {
                GUILayout.Label("Select a breadboard to see linked craft hardware.", DecorationEditorTheme.MiniWrap);
                return;
            }

            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard);
            List<AutomationLink> inputs = _links
                .Where(link => link?.Kind == AutomationLinkKind.InputToBreadboard)
                .OrderBy(link => BlockRefCurrentName(link.Source))
                .ThenBy(link => link.Property)
                .ToList();
            List<AutomationLink> outputs = _links
                .Where(link => link?.Kind == AutomationLinkKind.BreadboardToOutput)
                .OrderBy(link => BlockRefCurrentName(link.Target))
                .ThenBy(link => link.Property)
                .ToList();
            if (inputs.Count == 0 && outputs.Count == 0)
            {
                GUILayout.Label("Link input/output craft blocks in world view first.", DecorationEditorTheme.MiniWrap);
                return;
            }

            int rowCount = inputs.Count + outputs.Count;
            if (inputs.Count > 0)
                rowCount++;
            if (outputs.Count > 0)
                rowCount++;
            float listHeight = Mathf.Min(
                EsuHudLayout.Scale(168f),
                EsuHudLayout.Scale(26f * Math.Max(2, rowCount)));
            _linkedHardwareScroll = GUILayout.BeginScrollView(
                _linkedHardwareScroll,
                GUILayout.Height(listHeight));
            DrawLinkedHardwareQuickBlockGroup(graph, "Inputs", inputs, "Read");
            DrawLinkedHardwareQuickBlockGroup(graph, "Outputs", outputs, "Set");
            GUILayout.EndScrollView();
        }

        private void DrawLinkedHardwareQuickBlockGroup(
            AutomationGraph graph,
            string title,
            List<AutomationLink> links,
            string verb)
        {
            if (links == null || links.Count == 0)
                return;

            GUILayout.Label(title, DecorationEditorTheme.Mini);
            foreach (AutomationLink link in links)
                DrawLinkedHardwareQuickBlock(graph, link, verb);
        }

        private void DrawLinkedHardwareQuickBlock(
            AutomationGraph graph,
            AutomationLink link,
            string verb)
        {
            if (link == null)
                return;

            AutomationGraphNode node = GraphNodeForNativeLink(graph, link);
            bool selected = IsSelectedGraphNode(graph, node);
            string targetName = link.Kind == AutomationLinkKind.InputToBreadboard
                ? BlockRefCurrentName(link.Source)
                : BlockRefCurrentName(link.Target);
            string label = verb + " " + ShortText(targetName, 18) + "." + ShortText(link.Property, 14);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(25f),
                GUILayout.ExpandWidth(true));
            Rect chip = new Rect(row.x + EsuHudLayout.Scale(3f), row.y + EsuHudLayout.Scale(7f), EsuHudLayout.Scale(10f), EsuHudLayout.Scale(10f));
            Rect button = new Rect(row.x + EsuHudLayout.Scale(16f), row.y, row.width - EsuHudLayout.Scale(16f), row.height);
            Color previous = GUI.color;
            GUI.color = link.Color;
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = previous;
            if (GUI.Button(
                    button,
                    new GUIContent(label, "Jump to native graph block for this linked craft target."),
                    node == null ? DecorationEditorTheme.DisabledButton : DecorationEditorTheme.ToolButton(selected)))
            {
                FocusLinkedHardwareGraphNode(graph, link);
            }
        }

        private static AutomationGraphNode GraphNodeForNativeLink(
            AutomationGraph graph,
            AutomationLink link)
        {
            if (graph == null || link == null)
                return null;

            AutomationGraphNode exactNode = graph.Nodes.FirstOrDefault(node =>
                node != null &&
                (node.Id == link.Id ||
                 link.NativeComponent != null && ReferenceEquals(node.NativeComponent, link.NativeComponent)));
            if (exactNode != null)
                return exactNode;

            List<AutomationGraphNode> targetMatches = graph.Nodes
                .Where(node => NodeBindingTargetsMatchLink(node, link))
                .ToList();
            return targetMatches.FirstOrDefault(node => MatchesPropertyText(node.TargetBinding?.Property, link.Property)) ??
                   targetMatches.FirstOrDefault();
        }

        private void FocusLinkedHardwareGraphNode(
            AutomationGraph graph,
            AutomationLink link)
        {
            AutomationGraphNode node = GraphNodeForNativeLink(graph, link);
            if (node == null)
            {
                InfoStore.Add("Automation Builder could not find that native linked graph block.");
                return;
            }

            graph.SelectedNodeId = node.Id;
            _graphScroll = new Vector2(
                node.Rect.x - EsuHudLayout.Scale(120f) / Mathf.Max(0.001f, _graphZoom),
                node.Rect.y - EsuHudLayout.Scale(80f) / Mathf.Max(0.001f, _graphZoom));
            InfoStore.Add("Automation Builder focused " + BlockSentenceTitle(node) + ".");
        }

        private void DrawPaletteCategoryButtons()
        {
            GUILayout.BeginHorizontal();
            DrawPaletteCategoryButton(AutomationPaletteCategory.Output, "Output");
            DrawPaletteCategoryButton(AutomationPaletteCategory.Control, "Control");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawPaletteCategoryButton(AutomationPaletteCategory.Input, "Input");
            DrawPaletteCategoryButton(AutomationPaletteCategory.Math, "Math");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawPaletteCategoryButton(AutomationPaletteCategory.Notation, "Notation");
            DrawPaletteCategoryButton(AutomationPaletteCategory.Variables, "Variables");
            GUILayout.EndHorizontal();
        }

        private void DrawPaletteCategoryButton(AutomationPaletteCategory category, string label)
        {
            Color color = PaletteCategoryColor(category);
            Rect rect = GUILayoutUtility.GetRect(
                new GUIContent(label),
                DecorationEditorTheme.ToolButton(_selectedPaletteCategory == category),
                GUILayout.Height(EsuHudLayout.Scale(26f)),
                GUILayout.ExpandWidth(true));
            Rect swatch = new Rect(rect.x + EsuHudLayout.Scale(6f), rect.y + EsuHudLayout.Scale(7f), EsuHudLayout.Scale(12f), EsuHudLayout.Scale(12f));
            if (GUI.Button(rect, "   " + label, DecorationEditorTheme.ToolButton(_selectedPaletteCategory == category)))
                _selectedPaletteCategory = category;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawPaletteBlock(AutomationNodeKind kind)
        {
            bool enabled = CanOpenCanvas;
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(GraphNodeHeightForKind(kind) * 0.72f),
                GUILayout.ExpandWidth(true));
            rect = new Rect(
                rect.x + EsuHudLayout.Scale(4f),
                rect.y + EsuHudLayout.Scale(4f),
                rect.width - EsuHudLayout.Scale(8f),
                rect.height - EsuHudLayout.Scale(8f));
            Color oldColor = GUI.color;
            if (!enabled)
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
            DrawAutomationBlockShape(rect, kind, selected: false, compact: true);
            DrawPaletteBlockText(rect, kind);
            GUI.color = oldColor;

            Event current = Event.current;
            if (enabled &&
                current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                _draggingPaletteBlock = true;
                _draggingPaletteKind = kind;
                _paletteDragMoved = false;
                _paletteDropPending = false;
                _paletteDragLastWindowMouse = current.mousePosition;
                CloseGraphSlotMenu();
                CloseGraphContextMenu();
                BeginCanvasInteraction(AutomationCanvasInteractionKind.PaletteDrag, 0, current.mousePosition);
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }

            if (rect.Contains(Event.current?.mousePosition ?? Vector2.zero))
                EsuCursorTooltip.Register(rect, "Click to place " + NodeTitle(kind) + " in the visible workspace, or drag it to a precise spot.");
        }

        private void DrawPaletteBlockText(Rect rect, AutomationNodeKind kind)
        {
            Rect label = new Rect(rect.x + EsuHudLayout.Scale(12f), rect.y + EsuHudLayout.Scale(8f), rect.width - EsuHudLayout.Scale(24f), rect.height - EsuHudLayout.Scale(16f));
            GUI.Label(label, PaletteBlockSentence(kind), DecorationEditorTheme.BodyWrap);
        }

        private void DrawPaletteDragGhost(Rect workspaceRect)
        {
            if (!_draggingPaletteBlock || Event.current == null)
                return;

            Vector2 mouse = PaletteDragWindowMouse();
            Rect ghost = new Rect(
                mouse.x - EsuHudLayout.Scale(GraphNodeWidthForKind(_draggingPaletteKind) * 0.5f),
                mouse.y - EsuHudLayout.Scale(34f),
                EsuHudLayout.Scale(GraphNodeWidthForKind(_draggingPaletteKind)),
                EsuHudLayout.Scale(GraphNodeHeightForKind(_draggingPaletteKind)));
            Color previous = GUI.color;
            GUI.color = workspaceRect.Contains(mouse)
                ? new Color(1f, 1f, 1f, 0.82f)
                : new Color(1f, 1f, 1f, 0.38f);
            DrawAutomationBlockShape(ghost, _draggingPaletteKind, selected: false, compact: false);
            DrawBlockSentence(ghost, TemporaryPaletteNode(_draggingPaletteKind), compact: true);
            GUI.color = previous;
        }

        private void DrawPaletteSnapPreview(Rect workspaceRect)
        {
            if (!_draggingPaletteBlock ||
                Event.current == null ||
                _selectedBreadboard == null ||
                !workspaceRect.Contains(PaletteDragWindowMouse()))
            {
                return;
            }

            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard);
            AutomationGraphNode preview = TemporaryPaletteNode(_draggingPaletteKind);
            preview.Rect = PaletteDropGraphRect(workspaceRect, _paletteDragLastWindowMouse);

            if (CanProduceValueBlock(preview.Kind) &&
                TryFindValueSnapPreview(graph, preview, out Rect valueTarget, out AutomationValueSlotKind slotKind, out AutomationNodeKind hostKind))
            {
                DrawWorkspaceSnapPreviewTarget(workspaceRect, valueTarget, "drop into " + ValueSlotLabel(hostKind, slotKind), NodeColor(preview.Kind));
                DrawWorkspaceCanvasFlow(
                    workspaceRect,
                    new Vector2(preview.Rect.xMax, preview.Rect.center.y),
                    new Vector2(valueTarget.x, valueTarget.center.y),
                    NodeColor(preview.Kind),
                    preview.Id);
                return;
            }

            if (DrawsAsValueBlock(preview.Kind, preview.Rect))
                return;

            if (TryFindBodySnapPreview(graph, preview, out Rect bodyTarget))
            {
                DrawWorkspaceSnapPreviewTarget(workspaceRect, bodyTarget, "drop into body", NodeColor(preview.Kind));
                return;
            }

            if (TryFindStackSnapPreview(graph, preview, out Rect stackTarget, out bool snapBelow))
                DrawWorkspaceSnapPreviewTarget(workspaceRect, stackTarget, snapBelow ? "drop below" : "drop above", NodeColor(preview.Kind));
        }

        private Rect PaletteDropGraphRect(Rect workspaceRect) =>
            PaletteDropGraphRect(workspaceRect, PaletteDragWindowMouse());

        private Rect PaletteDropGraphRect(Rect workspaceRect, Vector2 windowMouse)
        {
            Vector2 position = WindowToGraphPoint(workspaceRect, windowMouse);
            float width = GraphNodeWidthForKind(_draggingPaletteKind);
            return new Rect(
                Mathf.Max(0f, position.x - width * 0.5f),
                Mathf.Max(0f, position.y - GraphNodeHeightForKind(_draggingPaletteKind) * 0.5f),
                width,
                GraphNodeHeightForKind(_draggingPaletteKind));
        }

        private Rect VisibleWorkspaceDropGraphRect(AutomationNodeKind kind)
        {
            Rect workspaceRect = _lastCanvasWorkspaceRect;
            float width = GraphNodeWidthForKind(kind);
            float height = GraphNodeHeightForKind(kind);
            Vector2 center = _graphScroll + new Vector2(
                Mathf.Max(1f, workspaceRect.width) * 0.5f / Mathf.Max(0.001f, _graphZoom),
                Mathf.Max(1f, workspaceRect.height) * 0.5f / Mathf.Max(0.001f, _graphZoom));
            return new Rect(
                Mathf.Max(0f, center.x - width * 0.5f),
                Mathf.Max(0f, center.y - height * 0.5f),
                width,
                height);
        }

        private Rect GraphToWorkspaceRect(
            Rect workspaceRect,
            Rect graphRect) =>
            new Rect(
                (graphRect.x - _graphScroll.x) * _graphZoom + workspaceRect.x,
                (graphRect.y - _graphScroll.y) * _graphZoom + workspaceRect.y,
                graphRect.width * _graphZoom,
                graphRect.height * _graphZoom);

        private Vector2 GraphToWorkspacePoint(
            Rect workspaceRect,
            Vector2 graphPoint) =>
            new Vector2(
                (graphPoint.x - _graphScroll.x) * _graphZoom + workspaceRect.x,
                (graphPoint.y - _graphScroll.y) * _graphZoom + workspaceRect.y);

        private void DrawWorkspaceSnapPreviewTarget(
            Rect workspaceRect,
            Rect graphTarget,
            string label,
            Color color)
        {
            Rect workspaceTarget = GraphToWorkspaceRect(workspaceRect, graphTarget);
            GUI.BeginGroup(workspaceRect);
            DrawSnapPreviewTarget(
                new Rect(
                    workspaceTarget.x - workspaceRect.x,
                    workspaceTarget.y - workspaceRect.y,
                    workspaceTarget.width,
                    workspaceTarget.height),
                label,
                color);
            GUI.EndGroup();
        }

        private void DrawWorkspaceCanvasFlow(
            Rect workspaceRect,
            Vector2 graphStart,
            Vector2 graphEnd,
            Color color,
            int seed)
        {
            Vector2 workspaceStart = GraphToWorkspacePoint(workspaceRect, graphStart);
            Vector2 workspaceEnd = GraphToWorkspacePoint(workspaceRect, graphEnd);
            GUI.BeginGroup(workspaceRect);
            DrawCanvasFlow(
                workspaceStart - workspaceRect.position,
                workspaceEnd - workspaceRect.position,
                color,
                seed);
            GUI.EndGroup();
        }

        private void HandlePaletteBlockDrop(Rect workspaceRect)
        {
            Event current = Event.current;
            if ((_canvasInteraction != AutomationCanvasInteractionKind.PaletteDrag &&
                 !_draggingPaletteBlock) ||
                current == null)
            {
                return;
            }

            Vector2 mouse = PaletteDragWindowMouse();
            if (current.type == EventType.MouseDrag)
            {
                Vector2 delta = mouse - _canvasInteractionStartMouse;
                if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
                {
                    _paletteDragMoved = true;
                    _canvasInteractionMoved = true;
                }

                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            bool released = current.type == EventType.MouseUp;
            bool lostRelease = _paletteDropPending ||
                               ((current.type == EventType.Layout ||
                                 current.type == EventType.Repaint) &&
                                !Input.GetMouseButton(0));
            if (!released && !lostRelease)
                return;

            FinishPaletteDrag(workspaceRect, released ? current : null);
        }

        private Vector2 PaletteDragWindowMouse()
        {
            Event current = Event.current;
            if (current != null &&
                current.isMouse)
            {
                _paletteDragLastWindowMouse = current.mousePosition;
            }

            return _paletteDragLastWindowMouse;
        }

        private static IEnumerable<AutomationNodeKind> PaletteKinds(AutomationPaletteCategory category)
        {
            switch (category)
            {
                case AutomationPaletteCategory.Input:
                    return new[] { AutomationNodeKind.InputGetter };
                case AutomationPaletteCategory.Control:
                    return new[]
                    {
                        AutomationNodeKind.Forever,
                        AutomationNodeKind.IfCondition,
                        AutomationNodeKind.IfLessThan,
                        AutomationNodeKind.LogicNot,
                        AutomationNodeKind.LogicAnd,
                        AutomationNodeKind.LogicOr,
                        AutomationNodeKind.LogicXor,
                        AutomationNodeKind.LogicNand,
                        AutomationNodeKind.LogicNor,
                        AutomationNodeKind.LogicXnor,
                        AutomationNodeKind.CompareAboveThreshold,
                        AutomationNodeKind.CompareBelowThreshold
                    };
                case AutomationPaletteCategory.Math:
                    return new[]
                    {
                        AutomationNodeKind.MathAdd,
                        AutomationNodeKind.MathSubtract,
                        AutomationNodeKind.MathMultiply,
                        AutomationNodeKind.MathMax,
                        AutomationNodeKind.MathMin,
                        AutomationNodeKind.Clamp,
                        AutomationNodeKind.Smooth
                    };
                case AutomationPaletteCategory.Variables:
                    return new[] { AutomationNodeKind.Constant, AutomationNodeKind.Random };
                case AutomationPaletteCategory.Notation:
                    return new[] { AutomationNodeKind.Comment };
                default:
                    return new[] { AutomationNodeKind.OutputSetter };
            }
        }

        private void DrawGeneratedNativePlanPanel()
        {
            GUILayout.Label("Generated Native Plan", DecorationEditorTheme.SubHeader);
            GUILayout.Label("Continuous breadboard evaluation. ESU lowers these blocks into vanilla components and connections; no ESU runtime loop is created.", DecorationEditorTheme.MiniWrap);
            DecorationEditorTheme.Separator();

            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard);
            _nativePlanScroll = GUILayout.BeginScrollView(_nativePlanScroll, GUILayout.MinHeight(EsuHudLayout.Scale(190f)));
            if (graph == null || graph.Nodes.Count == 0)
            {
                GUILayout.Label("No ESU graph blocks yet. Drag blocks from the palette onto the workspace.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                NativePlan plan = CachedSelectedNativePlan(graph);
                DrawNativePlanCheck(plan);
                DecorationEditorTheme.Separator();
                DrawGraphLiveValues(graph);
                DecorationEditorTheme.Separator();

                foreach (string line in CachedBlockProgramLines(graph))
                    GUILayout.Label(line, DecorationEditorTheme.MiniWrap);

                DecorationEditorTheme.Separator();
                GUILayout.Label("Native Plan", DecorationEditorTheme.SubHeader);
                foreach (string line in plan.DetailLines())
                    GUILayout.Label(line, DecorationEditorTheme.MiniWrap);
            }

            GUILayout.EndScrollView();
            DecorationEditorTheme.Separator();
            DrawSelectedNodeInspector();
        }

        private NativePlan CachedSelectedNativePlan(AutomationGraph graph)
        {
            EnsureDisplayCache(graph);
            return _cachedSelectedNativePlan;
        }

        private IReadOnlyList<string> CachedBlockProgramLines(AutomationGraph graph)
        {
            EnsureDisplayCache(graph);
            return _cachedBlockProgramLines;
        }

        private void EnsureDisplayCache(AutomationGraph graph)
        {
            string graphKey = graph?.Key ?? string.Empty;
            if (!_automationDisplayCacheDirty &&
                _cachedSelectedNativePlan != null &&
                _displayCacheNativeVersion == _nativeAutomationCacheVersion &&
                string.Equals(_displayCacheGraphKey, graphKey, StringComparison.Ordinal))
            {
                return;
            }

            _cachedSelectedNativePlan = BuildSelectedNativePlan(graph);
            _cachedBlockProgramLines = BlockProgramLines(graph).ToList();
            _displayCacheGraphKey = graphKey;
            _displayCacheNativeVersion = _nativeAutomationCacheVersion;
            _automationDisplayCacheDirty = false;
        }

        private static void DrawNativePlanCheck(NativePlan plan)
        {
            GUILayout.Label("Check", DecorationEditorTheme.SubHeader);
            if (plan == null)
            {
                GUILayout.Label("No native plan is available.", DecorationEditorTheme.MiniWrap);
                return;
            }

            GUILayout.Label(plan.Summary, plan.HasErrors ? DecorationEditorTheme.Warning : DecorationEditorTheme.MiniWrap);
            foreach (string error in plan.Errors.Take(4))
                GUILayout.Label("Error: " + error, DecorationEditorTheme.MiniWrap);
            foreach (string warning in plan.Warnings.Take(Math.Max(0, 4 - plan.Errors.Count)))
                GUILayout.Label("Warning: " + warning, DecorationEditorTheme.MiniWrap);
        }

        private void DrawNativeGraphReadiness(AutomationGraph graph)
        {
            GUILayout.Label("Readiness", DecorationEditorTheme.SubHeader);
            List<string> issues = NativeGraphReadinessIssues(graph).ToList();
            if (issues.Count == 0)
            {
                GUILayout.Label("OK: visual blocks are backed by native breadboard components and have no obvious missing settings.", DecorationEditorTheme.MiniWrap);
                return;
            }

            foreach (string issue in issues.Take(8))
                GUILayout.Label("Needs: " + issue, DecorationEditorTheme.MiniWrap);

            if (issues.Count > 8)
            {
                GUILayout.Label(
                    string.Format(CultureInfo.InvariantCulture, "...and {0:N0} more readiness issue{1}.", issues.Count - 8, issues.Count - 8 == 1 ? string.Empty : "s"),
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawGraphLiveValues(AutomationGraph graph)
        {
            GUILayout.Label("Live Values", DecorationEditorTheme.SubHeader);
            IReadOnlyList<string> lines = CachedGraphLiveValueLines(graph);
            if (lines.Count == 0)
            {
                GUILayout.Label("Add a Read or Set block with a linked target to see current native property values.", DecorationEditorTheme.MiniWrap);
                return;
            }

            foreach (string line in lines.Take(8))
                GUILayout.Label(line, DecorationEditorTheme.MiniWrap);

            if (lines.Count > 8)
            {
                GUILayout.Label(
                    string.Format(CultureInfo.InvariantCulture, "...and {0:N0} more live value{1}.", lines.Count - 8, lines.Count - 8 == 1 ? string.Empty : "s"),
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private IReadOnlyList<string> CachedGraphLiveValueLines(AutomationGraph graph)
        {
            string graphKey = graph?.Key ?? string.Empty;
            float now = Time.unscaledTime;
            if (_cachedGraphLiveValueLines != null &&
                _graphLiveValueCacheNativeVersion == _nativeAutomationCacheVersion &&
                string.Equals(_graphLiveValueCacheKey, graphKey, StringComparison.Ordinal) &&
                now < _nextGraphLiveValueRefreshTime)
            {
                return _cachedGraphLiveValueLines;
            }

            _cachedGraphLiveValueLines = GraphLiveValueLines(graph).ToList();
            _graphLiveValueCacheKey = graphKey;
            _graphLiveValueCacheNativeVersion = _nativeAutomationCacheVersion;
            _nextGraphLiveValueRefreshTime = now + LivePreviewIntervalSeconds;
            return _cachedGraphLiveValueLines;
        }

        private IEnumerable<string> GraphLiveValueLines(AutomationGraph graph)
        {
            if (graph == null)
                yield break;

            foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
            {
                if (node == null)
                    continue;

                if (node.Kind == AutomationNodeKind.InputGetter)
                {
                    yield return "read " + GraphNodeTargetProperty(node) + " = " + InputGetterCurrentValueText(node);
                    continue;
                }

                if (node.Kind == AutomationNodeKind.OutputSetter)
                {
                    AutomationGraphNode outputValue = SnappedValueForHost(graph, node);
                    yield return "set " + GraphNodeTargetProperty(node) + " currently " + OutputSetterCurrentValueText(node) + " <- " + OutputSetterSourceText(graph, node, outputValue);
                }
            }
        }

        private static string GraphNodeTargetProperty(AutomationGraphNode node)
        {
            string target = BlockTitleTargetLabel(node);
            string property = DisplayNodeProperty(node);
            return ShortText(target, 28) + "." + ShortText(property, 18);
        }

        private IEnumerable<string> BlockProgramLines(AutomationGraph graph)
        {
            yield return "Block Program";
            List<List<AutomationGraphNode>> chains = OrderedBlockProgramChains(graph);

            if (chains.Count == 0)
                yield return "  (all visible blocks are plugged into other blocks)";

            foreach (List<AutomationGraphNode> chain in chains)
            {
                foreach (AutomationGraphNode node in chain)
                {
                    foreach (string line in BlockProgramNodeLines(graph, node, 0))
                        yield return line;
                }
            }

            yield return "Apply: validates this block program and writes vanilla breadboard connections";
        }

        private static List<List<AutomationGraphNode>> OrderedBlockProgramChains(AutomationGraph graph)
        {
            if (graph == null)
                return new List<List<AutomationGraphNode>>();

            List<AutomationGraphNode> roots = graph.Nodes
                .Where(node => IsBlockProgramRoot(graph, node))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            List<AutomationGraphNode> stackRoots = roots
                .Where(CanStackSnapNode)
                .ToList();
            IReadOnlyList<AutomationGraphConnection> stackConnections = GraphConnections(graph, AutomationGraphConnectionKind.Stack);
            var visited = new HashSet<AutomationGraphNode>();
            var chains = new List<List<AutomationGraphNode>>();

            foreach (AutomationGraphNode root in stackRoots
                         .Where(node => PreviousGraphStackNode(stackConnections, stackRoots, node) == null)
                         .OrderBy(node => node.Rect.y)
                         .ThenBy(node => node.Rect.x))
            {
                AppendBlockProgramChain(graph, stackConnections, stackRoots, root, visited, chains);
            }

            foreach (AutomationGraphNode root in roots)
            {
                if (visited.Contains(root))
                    continue;

                if (CanStackSnapNode(root))
                    AppendBlockProgramChain(graph, stackConnections, stackRoots, root, visited, chains);
                else
                    chains.Add(new List<AutomationGraphNode> { root });
            }

            return chains
                .OrderBy(chain => chain.Count == 0 ? 0f : chain[0].Rect.y)
                .ThenBy(chain => chain.Count == 0 ? 0f : chain[0].Rect.x)
                .ToList();
        }

        private static void AppendBlockProgramChain(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphConnection> stackConnections,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode start,
            HashSet<AutomationGraphNode> visited,
            List<List<AutomationGraphNode>> chains)
        {
            if (start == null || visited == null || chains == null || visited.Contains(start))
                return;

            var chain = new List<AutomationGraphNode>();
            AutomationGraphNode current = start;
            while (current != null && visited.Add(current))
            {
                chain.Add(current);
                current = NextGraphStackNode(stackConnections, candidates, current, visited);
            }

            if (chain.Count > 0)
                chains.Add(chain);
        }

        private static IEnumerable<string> BlockProgramNodeLines(
            AutomationGraph graph,
            AutomationGraphNode node,
            int indent)
        {
            string prefix = BlockProgramIndent(indent);
            switch (node.Kind)
            {
                case AutomationNodeKind.Forever:
                    yield return prefix + "forever (native breadboard evaluation)";
                    foreach (string line in BlockProgramBodyLines(graph, node, indent + 1))
                        yield return line;
                    yield break;
                case AutomationNodeKind.IfLessThan:
                    yield return prefix + BlockProgramSentence(graph, node);
                    AutomationGraphNode thresholdValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Threshold);
                    AutomationGraphNode thenValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode elseValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Else);
                    yield return prefix + "  threshold value " + (thresholdValue == null
                        ? "constant " + SwitchThresholdSlotText(node)
                        : BlockValueText(thresholdValue));
                    yield return prefix + "  then value " + BlockValueText(thenValue);
                    yield return prefix + "  else value " + (elseValue == null
                        ? "constant " + SwitchFailValueSlotText(node)
                        : BlockValueText(elseValue));
                    yield return prefix + "  emits selected value to stack actions below";
                    foreach (string line in BlockProgramBodyLines(graph, node, indent + 1))
                        yield return line;
                    yield break;
                case AutomationNodeKind.IfCondition:
                    yield return prefix + BlockProgramSentence(graph, node);
                    AutomationGraphNode conditionThenValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode conditionElseValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Else);
                    yield return prefix + "  then value " + BlockValueText(conditionThenValue);
                    yield return prefix + "  else value " + (conditionElseValue == null
                        ? "constant " + SwitchFailValueSlotText(node)
                        : BlockValueText(conditionElseValue));
                    yield return prefix + "  emits selected value to stack actions below";
                    foreach (string line in BlockProgramBodyLines(graph, node, indent + 1))
                        yield return line;
                    yield break;
                case AutomationNodeKind.Comment:
                    yield return prefix + BlockProgramSentence(graph, node);
                    yield break;
                default:
                    yield return prefix + BlockProgramSentence(graph, node);
                    yield break;
            }
        }

        private static IEnumerable<string> BlockProgramBodyLines(
            AutomationGraph graph,
            AutomationGraphNode host,
            int indent)
        {
            List<AutomationGraphNode> body = BodyChildrenForHost(graph, host).ToList();
            if (body.Count == 0)
                yield break;

            yield return BlockProgramIndent(indent) + "do";
            foreach (AutomationGraphNode child in body)
            {
                foreach (string line in BlockProgramNodeLines(graph, child, indent + 1))
                    yield return line;
            }
        }

        private static string BlockProgramSentence(AutomationGraph graph, AutomationGraphNode node)
        {
            switch (node.Kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "read " + ShortText(BlockTitleTargetLabel(node), 34) + "." + ShortText(DisplayNodeProperty(node), 18);
                case AutomationNodeKind.OutputSetter:
                    return "set " + ShortText(BlockTitleTargetLabel(node), 34) + "." + ShortText(DisplayNodeProperty(node), 18) + " to " + BlockValueText(SnappedValueForHost(graph, node));
                case AutomationNodeKind.IfLessThan:
                    return "if incoming signal > " + SwitchThresholdBlockText(graph, node);
                case AutomationNodeKind.IfCondition:
                    return "if " + ConditionInputBlockText(graph, node) + " is true";
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return LogicBlockProgramSentence(graph, node);
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return ThresholdBlockProgramSentence(graph, node);
                case AutomationNodeKind.Constant:
                    return BlockValueText(node);
                case AutomationNodeKind.Random:
                    return BlockValueText(node);
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                    return MathBlockProgramSentence(graph, node);
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return MaxMinBlockProgramSentence(graph, node);
                case AutomationNodeKind.Clamp:
                    return "clamp " + ClampInputBlockText(graph, node) + " from " + ClampMinBlockText(graph, node) + " to " + ClampMaxBlockText(graph, node);
                case AutomationNodeKind.Smooth:
                    return "smooth " + SmoothInputBlockText(graph, node) + " over " + SmoothSecondsBlockText(graph, node);
                case AutomationNodeKind.Comment:
                    return "note " + ShortText(node.ValueText, 44);
                default:
                    return ShortText(node.Label, 44);
            }
        }

        private static string BlockValueText(AutomationGraphNode node)
        {
            if (node == null)
                return "incoming signal";

            switch (node.Kind)
            {
                case AutomationNodeKind.Constant:
                    return "constant " + ShortText(node.ValueText, 18);
                case AutomationNodeKind.Random:
                    return "random " + RandomRangeSlotText(node);
                case AutomationNodeKind.InputGetter:
                    return "read " + ShortText(BlockTitleTargetLabel(node), 34) + "." + ShortText(DisplayNodeProperty(node), 18);
                default:
                    return NativePlanSentence(node);
            }
        }

        private static bool IsBlockProgramRoot(AutomationGraph graph, AutomationGraphNode node)
        {
            return node != null &&
                   SnappedBodyParent(graph, node) == null &&
                   SnappedValueHost(graph, node) == null;
        }

        private static string BlockProgramIndent(int indent)
        {
            return new string(' ', Mathf.Max(0, indent) * 2);
        }

        private IEnumerable<string> NativePlanLines(AutomationGraph graph)
        {
            yield return "forever: native breadboard evaluates continuously";
            List<List<AutomationGraphNode>> chains = OrderedGraphFlowChains(graph);
            yield return "Visual ESU Stack Chains";
            if (chains.Count == 0)
            {
                yield return "   (no snapped ESU action chains yet)";
            }
            else
            {
                for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
                {
                    List<AutomationGraphNode> chain = chains[chainIndex];
                    yield return string.Format(CultureInfo.InvariantCulture, "   chain {0:N0}", chainIndex + 1);
                    for (int nodeIndex = 0; nodeIndex < chain.Count; nodeIndex++)
                    {
                        AutomationGraphNode node = chain[nodeIndex];
                        yield return string.Format(
                            CultureInfo.InvariantCulture,
                            "      {0:N0}. {1} -> {2}",
                            nodeIndex + 1,
                            NativePlanSentence(node),
                            NativeComponentName(node.Kind));
                        foreach (string line in NativePlanNodeDetailLines(graph, node))
                            yield return "         " + line;
                    }
                }
            }

            yield return "Native Component Inventory";
            List<AutomationGraphNode> ordered = graph.Nodes
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            for (int index = 0; index < ordered.Count; index++)
            {
                AutomationGraphNode node = ordered[index];
                AutomationGraphNode bodyParent = SnappedBodyParent(graph, node);
                if (bodyParent != null)
                {
                    yield return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}. body {1} inside {2}",
                        index + 1,
                        NativePlanSentence(node),
                        NativePlanSentence(bodyParent));
                    continue;
                }

                AutomationGraphNode snappedHost = SnappedValueHost(graph, node);
                if (snappedHost != null)
                {
                    AutomationValueSlotKind slotKind = SnappedValueSlotKind(graph, node, snappedHost);
                    yield return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}. value {1} plugged into {2} {3} socket",
                        index + 1,
                        NativePlanSentence(node),
                        NativePlanSentence(snappedHost),
                        ValueSlotLabel(snappedHost.Kind, slotKind));
                    continue;
                }

                yield return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}. {1} -> {2}",
                    index + 1,
                    NativePlanSentence(node),
                    NativeComponentName(node.Kind));
                foreach (string line in NativePlanNodeDetailLines(graph, node))
                    yield return "   " + line;
            }

            yield return "Apply Preview";
            foreach (string line in NativeConnectionPreviewLines(graph))
                yield return line;
            yield return "Apply: connect visual stack chains top-to-bottom and socketed value blocks into native inputs";
        }

        private static IEnumerable<string> NativePlanNodeDetailLines(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, node))
            {
                AutomationGraphNode value = SnappedValueForHost(graph, node, slotKind);
                if (value != null)
                    yield return ValueSlotLabel(node.Kind, slotKind) + " socket: " + NativePlanSentence(value);
            }

            foreach (AutomationGraphNode bodyNode in BodyChildrenForHost(graph, node))
                yield return "body: " + NativePlanSentence(bodyNode);

            if (node.Kind == AutomationNodeKind.IfLessThan)
                yield return "switch output: then/else value drives the next stack action";
        }

        private IEnumerable<string> NativeConnectionPreviewLines(
            AutomationGraph graph,
            bool onlyAppliedConnections = false)
        {
            bool any = false;
            IReadOnlyList<AutomationGraphConnection> stackConnections = AppliedPreviewConnections(
                GraphConnections(graph, AutomationGraphConnectionKind.Stack),
                onlyAppliedConnections);
            IReadOnlyList<AutomationGraphConnection> valueConnections = AppliedPreviewConnections(
                GraphConnections(graph, AutomationGraphConnectionKind.Value),
                onlyAppliedConnections);
            IReadOnlyList<AutomationGraphConnection> bodyConnections = AppliedPreviewConnections(
                GraphConnections(graph, AutomationGraphConnectionKind.Body),
                onlyAppliedConnections);
            foreach (AutomationGraphConnection connection in stackConnections)
            {
                AutomationGraphNode from = connection.From;
                AutomationGraphNode to = connection.To;
                if (from == null || to == null)
                    continue;

                any = true;
                if (to.Kind == AutomationNodeKind.IfLessThan ||
                    to.Kind == AutomationNodeKind.IfCondition)
                {
                    yield return "   condition: " + NativePlanSentence(from) + " -> " + NativePlanSentence(to) + " switcher input";
                }
                else
                {
                    yield return "   stack: " + NativePlanSentence(from) + " -> " + NativePlanSentence(to);
                }
            }

            foreach (AutomationGraphConnection connection in valueConnections)
            {
                AutomationGraphNode value = connection.From;
                AutomationGraphNode host = connection.To;
                AutomationValueSlotKind slotKind = connection.SlotKind;
                if (value == null || host == null)
                    continue;

                any = true;
                if (host.Kind == AutomationNodeKind.IfLessThan &&
                    slotKind == AutomationValueSlotKind.Threshold)
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " constant threshold property";
                    continue;
                }

                if (IsFuzzyThresholdKind(host.Kind) &&
                    slotKind == AutomationValueSlotKind.Threshold)
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " constant threshold property";
                    continue;
                }

                if (IsMathEvaluatorKind(host.Kind) &&
                    slotKind == MathOperandSlotKind(host.Kind))
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " native b input";
                    continue;
                }

                if (host.Kind == AutomationNodeKind.Clamp &&
                    slotKind == AutomationValueSlotKind.Min)
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " constant minimum property";
                    continue;
                }

                if (host.Kind == AutomationNodeKind.Clamp &&
                    slotKind == AutomationValueSlotKind.Max)
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " constant maximum property";
                    continue;
                }

                if (host.Kind == AutomationNodeKind.Smooth &&
                    slotKind == AutomationValueSlotKind.Seconds)
                {
                    yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " constant seconds property";
                    continue;
                }

                yield return "   value: " + NativePlanSentence(value) + " -> " + NativePlanSentence(host) + " " + ValueSlotLabel(host.Kind, slotKind) + " input";
            }

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsValueSlot(node.Kind)))
            {
                if ((host.Kind == AutomationNodeKind.IfLessThan ||
                     host.Kind == AutomationNodeKind.IfCondition) &&
                    valueConnections.All(connection =>
                        !ReferenceEquals(connection.To, host) ||
                        connection.SlotKind != AutomationValueSlotKind.Else))
                {
                    any = true;
                    yield return "   value: native Switch else/fail = constant " + SwitchFailValueSlotText(host);
                }

                if (host.Kind == AutomationNodeKind.IfLessThan ||
                    host.Kind == AutomationNodeKind.IfCondition)
                {
                    bool drivesAction = stackConnections.Any(connection => ReferenceEquals(connection.From, host)) ||
                                        bodyConnections.Any(connection => ReferenceEquals(connection.From, host));
                    if (!drivesAction)
                        continue;

                    any = true;
                    yield return "   switch output: selected then/else value feeds the next stack action";
                }
            }

            foreach (AutomationGraphConnection connection in bodyConnections)
            {
                if (connection.From == null || connection.To == null)
                    continue;

                any = true;
                yield return "   body: " + NativePlanSentence(connection.To) + " runs inside " + NativePlanSentence(connection.From);
            }

            if (!any)
                yield return "   (no stack or value connections to apply yet)";
        }

        private IReadOnlyList<AutomationGraphConnection> AppliedPreviewConnections(
            IEnumerable<AutomationGraphConnection> connections,
            bool onlyAppliedConnections)
        {
            List<AutomationGraphConnection> candidates = (connections ?? Enumerable.Empty<AutomationGraphConnection>())
                .Where(connection => connection?.From != null && connection.To != null)
                .ToList();
            if (!onlyAppliedConnections)
                return candidates;

            return candidates
                .Where(connection => IsGraphNodeApplyWritable(connection.To))
                .ToList();
        }

        private bool IsGraphNodeApplyWritable(AutomationGraphNode node)
        {
            return node != null &&
                   (node.IsStaged || IsEsuOwnedNativeNode(node));
        }

        private static string NativePlanSentence(AutomationGraphNode node)
        {
            switch (node.Kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "read " + ShortText(BlockTitleTargetLabel(node), 34) + "." + ShortText(DisplayNodeProperty(node), 18);
                case AutomationNodeKind.OutputSetter:
                    return "set " + ShortText(BlockTitleTargetLabel(node), 34) + "." + ShortText(DisplayNodeProperty(node), 18) + " from incoming signal";
                case AutomationNodeKind.Forever:
                    return "native continuous evaluation starts here";
                case AutomationNodeKind.IfLessThan:
                    return "native switch pass when switcher > " + SwitchThresholdSlotText(node) + " fail " + SwitchFailValueSlotText(node);
                case AutomationNodeKind.IfCondition:
                    return "native switch condition > 0.5 fail " + SwitchFailValueSlotText(node);
                case AutomationNodeKind.Constant:
                    return "emit constant " + ShortText(node.ValueText, 18);
                case AutomationNodeKind.Random:
                    return "emit random " + RandomRangeSlotText(node);
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                    return MathNativePlanSentence(node);
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return MaxMinNativePlanSentence(null, node);
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return LogicNativePlanSentence(null, node);
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return ThresholdNativePlanSentence(null, node);
                case AutomationNodeKind.Clamp:
                    return "clamp signal to " + ClampRangeSlotText(node);
                case AutomationNodeKind.Smooth:
                    return "smooth signal over " + SmoothSecondsSlotText(node);
                case AutomationNodeKind.Comment:
                    return "note " + ShortText(node.ValueText, 44);
                default:
                    return ShortText(node.Label, 44);
            }
        }

        private static string NativeComponentName(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "GenericBlockGetter";
                case AutomationNodeKind.OutputSetter:
                    return "GenericBlockSetter";
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                    return "Evaluator";
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return "MaxMin";
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return "LogicGate";
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return "FuzzyThreshold";
                case AutomationNodeKind.IfLessThan:
                case AutomationNodeKind.IfCondition:
                    return "Switch";
                case AutomationNodeKind.Constant:
                    return "ConstantInput";
                case AutomationNodeKind.Random:
                    return "RandomInput";
                case AutomationNodeKind.Clamp:
                    return "Clamp";
                case AutomationNodeKind.Smooth:
                    return "Delay";
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.Comment:
                    return "Comment";
                default:
                    return "Native component";
            }
        }

        private void DrawGraphWorkspace(Rect canvasRect)
        {
            if (_selectedBreadboard == null)
            {
                GUI.Label(EsuHudLayout.PanelInnerRect(canvasRect, 10f), "Select a breadboard to edit its graph.", DecorationEditorTheme.BodyWrap);
                return;
            }

            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard);

            UpdateGraphEventMousePosition(canvasRect);
            RefreshGraphSlotDropdownRect(graph, GraphViewportRect(canvasRect));
            if (_graphPropertyPickerNodeId == 0)
            {
                if (HandleGraphCanvasRightClick(canvasRect, graph))
                    return;
                HandleGraphCanvasZoom(canvasRect);
                HandleGraphCanvasPan(canvasRect, graph);
            }
            else
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
            }

            GUI.BeginGroup(canvasRect);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = previousMatrix * Matrix4x4.TRS(
                new Vector3(-_graphScroll.x * _graphZoom, -_graphScroll.y * _graphZoom, 0f),
                Quaternion.identity,
                new Vector3(_graphZoom, _graphZoom, 1f));
            try
            {
                Rect graphViewport = GraphViewportRect(canvasRect);
                DrawGraphBackground(graphViewport);
                List<List<AutomationGraphNode>> executionChains = OrderedGraphFlowChains(graph);
                List<AutomationGraphNode> executionFlow = executionChains.SelectMany(chain => chain).ToList();
                DrawGraphConnections(graph, executionChains);
                ConsumeGraphSlotDropdownOutsideMouseDown();
                foreach (AutomationGraphNode node in graph.Nodes.ToArray())
                    DrawGraphNodeCard(graph, node, executionFlow, executionChains);
                DrawGraphSnapPreview(graph);
                DrawGraphReadinessPopover(graph, graphViewport);
                DrawGraphContextMenu(graph, graphViewport);
                DrawGraphSlotDropdown(graph, graphViewport);
                CompleteGraphNodeDragIfReleased(graph);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.EndGroup();
                _graphEventMousePositionValid = false;
            }
        }

        private void HandleGraphCanvasPan(
            Rect canvasRect,
            AutomationGraph graph)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (_panningGraphCanvas &&
                (current.type == EventType.Layout || current.type == EventType.Repaint) &&
                (_graphCanvasPanButton < 0 || !Input.GetMouseButton(_graphCanvasPanButton)))
            {
                ResetCanvasInteractionState();
                return;
            }

            if (!current.isMouse)
                return;

            if ((_canvasInteraction != AutomationCanvasInteractionKind.None &&
                 _canvasInteraction != AutomationCanvasInteractionKind.CanvasPan) ||
                _draggingNodeId != 0 ||
                _draggingPaletteBlock ||
                IsWindowMouseInsideGraphSlotDropdown(current, canvasRect) ||
                IsWindowMouseInsideGraphContextMenu(current, canvasRect) ||
                IsWindowMouseInsideGraphReadinessPopover(current, canvasRect))
            {
                if (_panningGraphCanvas)
                    ResetCanvasInteractionState();
                return;
            }

            if (current.type == EventType.MouseDown &&
                (current.button == 0 || current.button == 2) &&
                canvasRect.Contains(current.mousePosition) &&
                CanStartGraphCanvasPan(current, canvasRect, graph))
            {
                _panningGraphCanvas = true;
                _graphCanvasPanButton = current.button;
                _graphCanvasPanMouseStart = current.mousePosition;
                _graphCanvasPanScrollStart = _graphScroll;
                CloseGraphSlotMenu();
                CloseGraphContextMenu();
                CloseGraphReadinessPopover();
                BeginCanvasInteraction(AutomationCanvasInteractionKind.CanvasPan, current.button, current.mousePosition);
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (!_panningGraphCanvas)
                return;

            if (current.type == EventType.MouseDrag)
            {
                Vector2 delta = current.mousePosition - _graphCanvasPanMouseStart;
                if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
                    _canvasInteractionMoved = true;
                _graphScroll = _graphCanvasPanScrollStart - delta / Mathf.Max(0.001f, _graphZoom);
                ClampGraphScroll(canvasRect, graph);
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp &&
                (_graphCanvasPanButton < 0 || current.button == _graphCanvasPanButton))
            {
                ResetCanvasInteractionState();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private bool HandleGraphCanvasRightClick(
            Rect canvasRect,
            AutomationGraph graph)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.MouseDown ||
                current.button != 1 ||
                !canvasRect.Contains(current.mousePosition))
            {
                return false;
            }

            if (_suppressNextCanvasRightClick)
            {
                _suppressNextCanvasRightClick = false;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return true;
            }

            if (_canvasInteraction == AutomationCanvasInteractionKind.NodeDrag ||
                _canvasInteraction == AutomationCanvasInteractionKind.PaletteDrag ||
                _draggingNodeId != 0 ||
                _draggingPaletteBlock)
            {
                CancelActiveCanvasInteraction(graph, restoreNode: true);
                CloseGraphSlotMenu();
                CloseGraphContextMenu();
                CloseGraphReadinessPopover();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return true;
            }

            Vector2 graphPoint = WindowToGraphPoint(canvasRect, current.mousePosition);
            AutomationGraphNode node = FindGraphNodeAtPoint(graph, graphPoint);
            CloseGraphSlotMenu();
            CloseGraphReadinessPopover();
            StopGraphCanvasPan();
            if (node == null)
            {
                if (graph != null)
                    graph.SelectedNodeId = 0;
                _closedQuickChoicesNodeId = 0;
                CloseGraphContextMenu();
            }
            else
            {
                if (graph.SelectedNodeId != node.Id || _closedQuickChoicesNodeId == node.Id)
                    _closedQuickChoicesNodeId = 0;
                graph.SelectedNodeId = node.Id;
                OpenGraphContextMenu(node, graphPoint, GraphViewportRect(canvasRect));
            }

            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private bool CanStartGraphCanvasPan(
            Event current,
            Rect canvasRect,
            AutomationGraph graph)
        {
            if (current.button == 2)
                return true;

            Vector2 graphPoint = WindowToGraphPoint(canvasRect, current.mousePosition);
            if (_graphSlotMenuKind != AutomationGraphSlotMenuKind.None &&
                (_graphSlotMenuRect.Contains(graphPoint) ||
                 _graphSlotMenuAnchorRect.Contains(graphPoint)))
            {
                return false;
            }

            if (_graphContextMenuNodeId != 0 &&
                _graphContextMenuRect.Contains(graphPoint))
            {
                return false;
            }

            if (_graphReadinessPopoverNodeId != 0 &&
                (_graphReadinessPopoverRect.Contains(graphPoint) ||
                 _graphReadinessPopoverAnchorRect.Contains(graphPoint)))
            {
                return false;
            }

            if (IsGraphPointInsideQuickChoices(graphPoint, graph))
                return false;

            return graph == null ||
                   graph.Nodes.All(node => node == null || !node.Rect.Contains(graphPoint));
        }

        private bool IsWindowMouseInsideGraphSlotDropdown(
            Event current,
            Rect canvasRect)
        {
            if (current == null ||
                _graphSlotMenuKind == AutomationGraphSlotMenuKind.None)
            {
                return false;
            }

            Vector2 graphPoint = WindowToGraphPoint(canvasRect, current.mousePosition);
            return _graphSlotMenuRect.Contains(graphPoint) ||
                   _graphSlotMenuAnchorRect.Contains(graphPoint);
        }

        private bool IsWindowMouseInsideGraphContextMenu(
            Event current,
            Rect canvasRect)
        {
            if (current == null ||
                _graphContextMenuNodeId == 0 ||
                !current.isMouse)
            {
                return false;
            }

            return _graphContextMenuRect.Contains(WindowToGraphPoint(canvasRect, current.mousePosition));
        }

        private bool IsWindowMouseInsideGraphReadinessPopover(
            Event current,
            Rect canvasRect)
        {
            if (current == null ||
                _graphReadinessPopoverNodeId == 0 ||
                !current.isMouse)
            {
                return false;
            }

            Vector2 graphPoint = WindowToGraphPoint(canvasRect, current.mousePosition);
            return _graphReadinessPopoverRect.Contains(graphPoint) ||
                   _graphReadinessPopoverAnchorRect.Contains(graphPoint);
        }

        private void HandleGraphCanvasZoom(Rect canvasRect)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.ScrollWheel ||
                !canvasRect.Contains(current.mousePosition))
            {
                return;
            }

            if (IsWindowMouseInsideGraphSlotDropdown(current, canvasRect))
            {
                AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
                return;
            }

            Vector2 before = WindowToGraphPoint(canvasRect, current.mousePosition);
            float step = -current.delta.y * 0.08f;
            _graphZoom = Mathf.Clamp(_graphZoom * (1f + step), GraphZoomMin, GraphZoomMax);
            _graphScroll = before - (current.mousePosition - canvasRect.position) / Mathf.Max(0.001f, _graphZoom);
            ClampGraphScroll(canvasRect, _selectedBreadboard == null ? null : GraphFor(_selectedBreadboard));
            AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
            current.Use();
        }

        private void ClampGraphScroll(
            Rect canvasRect,
            AutomationGraph graph)
        {
            float zoom = Mathf.Max(0.001f, _graphZoom);
            float viewWidth = Mathf.Max(1f, canvasRect.width) / zoom;
            float viewHeight = Mathf.Max(1f, canvasRect.height) / zoom;
            float minX = 0f;
            float minY = 0f;
            float maxX = viewWidth;
            float maxY = viewHeight;
            bool hasNode = false;

            if (graph != null)
            {
                foreach (AutomationGraphNode node in graph.Nodes)
                {
                    if (node == null)
                        continue;

                    if (!hasNode)
                    {
                        minX = node.Rect.xMin;
                        minY = node.Rect.yMin;
                        maxX = node.Rect.xMax;
                        maxY = node.Rect.yMax;
                        hasNode = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, node.Rect.xMin);
                        minY = Mathf.Min(minY, node.Rect.yMin);
                        maxX = Mathf.Max(maxX, node.Rect.xMax);
                        maxY = Mathf.Max(maxY, node.Rect.yMax);
                    }
                }
            }

            float margin = Mathf.Max(240f, Mathf.Max(viewWidth, viewHeight) * 0.5f);
            float scrollMinX = Mathf.Min(-margin, minX - margin);
            float scrollMinY = Mathf.Min(-margin, minY - margin);
            float scrollMaxX = Mathf.Max(scrollMinX, maxX + margin - viewWidth);
            float scrollMaxY = Mathf.Max(scrollMinY, maxY + margin - viewHeight);
            _graphScroll = new Vector2(
                Mathf.Clamp(_graphScroll.x, scrollMinX, scrollMaxX),
                Mathf.Clamp(_graphScroll.y, scrollMinY, scrollMaxY));
        }

        private void UpdateGraphEventMousePosition(Rect canvasRect)
        {
            Event current = Event.current;
            if (current == null || !current.isMouse)
            {
                _graphEventMousePositionValid = false;
                return;
            }

            _graphEventMousePosition = WindowToGraphPoint(canvasRect, current.mousePosition);
            _graphEventMousePositionValid = canvasRect.Contains(current.mousePosition);
        }

        private Vector2 CurrentGraphMousePosition(Event current) =>
            _graphEventMousePositionValid
                ? _graphEventMousePosition
                : current == null
                    ? Vector2.zero
                    : current.mousePosition;

        private Vector2 WindowToGraphPoint(Rect workspaceRect, Vector2 windowMouse) =>
            _graphScroll + (windowMouse - workspaceRect.position) / Mathf.Max(0.001f, _graphZoom);

        private Rect GraphViewportRect(Rect workspaceRect) =>
            new Rect(
                _graphScroll.x,
                _graphScroll.y,
                Mathf.Max(1f, workspaceRect.width) / Mathf.Max(0.001f, _graphZoom),
                Mathf.Max(1f, workspaceRect.height) / Mathf.Max(0.001f, _graphZoom));

        private bool IsGraphPointInsideQuickChoices(
            Vector2 graphPoint,
            AutomationGraph graph)
        {
            return false;
        }

        private void StopGraphCanvasPan()
        {
            _panningGraphCanvas = false;
            _graphCanvasPanButton = -1;
        }

        private void UpdateActiveCanvasPointerInteraction()
        {
            if (!_canvasOpen)
            {
                AutomationBuilderInputScope.SetGraphPointerInputOwned(false);
                return;
            }

            bool active = GraphPointerInputActive();
            AutomationBuilderInputScope.SetGraphPointerInputOwned(active);
            if (!active)
            {
                _nodeDragStalledFrames = 0;
                return;
            }

            AutomationBuilderInputScope.ClaimBuildInputForFrames(3);
            AutomationBuilderInputScope.ClaimCameraInputForFrames(3);

            if (_canvasInteraction == AutomationCanvasInteractionKind.NodeDrag &&
                _draggingNodeId != 0)
            {
                if (!UpdateNodeDragFromRawMouse())
                    MaybeLogGraphDragRawUpdateStall("node raw update unavailable");
                return;
            }

            _nodeDragStalledFrames = 0;
            if (_canvasInteraction == AutomationCanvasInteractionKind.PaletteDrag ||
                _draggingPaletteBlock)
            {
                UpdatePaletteDragFromRawMouse();
                return;
            }

            if (_canvasInteraction == AutomationCanvasInteractionKind.CanvasPan ||
                _panningGraphCanvas)
            {
                if (!UpdateCanvasPanFromRawMouse())
                    MaybeLogGraphDragRawUpdateStall("canvas pan raw update unavailable");
            }
        }

        private bool UpdateNodeDragFromRawMouse()
        {
            if (!Input.GetMouseButton(0))
                return true;

            AutomationGraph graph = CurrentSelectedGraph();
            if (graph == null || !LastCanvasWorkspaceUsable())
                return false;

            AutomationGraphNode node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == _draggingNodeId);
            if (node == null)
                return false;

            Vector2 graphMouse = WindowToGraphPoint(_lastCanvasWorkspaceRect, CurrentCanvasWindowMousePosition());
            _nodeDragLastRawMouse = graphMouse;
            Vector2 delta = graphMouse - _nodeDragStartMouse;
            if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
                _canvasInteractionMoved = true;

            Rect next = _nodeDragStartRect;
            next.x = Mathf.Max(0f, _nodeDragStartRect.x + delta.x);
            next.y = Mathf.Max(0f, _nodeDragStartRect.y + delta.y);
            if (Mathf.Abs(node.Rect.x - next.x) > 0.001f ||
                Mathf.Abs(node.Rect.y - next.y) > 0.001f)
            {
                node.Rect = next;
            }

            _nodeDragStalledFrames = 0;
            return true;
        }

        private void UpdatePaletteDragFromRawMouse()
        {
            if (!Input.GetMouseButton(0))
                return;

            Vector2 mouse = CurrentCanvasWindowMousePosition();
            _paletteDragLastWindowMouse = mouse;
            Vector2 delta = mouse - _canvasInteractionStartMouse;
            if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
            {
                _paletteDragMoved = true;
                _canvasInteractionMoved = true;
            }
        }

        private bool UpdateCanvasPanFromRawMouse()
        {
            if (_graphCanvasPanButton < 0 || !Input.GetMouseButton(_graphCanvasPanButton))
                return true;

            if (!LastCanvasWorkspaceUsable())
                return false;

            Vector2 mouse = CurrentCanvasWindowMousePosition();
            Vector2 delta = mouse - _graphCanvasPanMouseStart;
            if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
                _canvasInteractionMoved = true;

            _graphScroll = _graphCanvasPanScrollStart - delta / Mathf.Max(0.001f, _graphZoom);
            ClampGraphScroll(_lastCanvasWorkspaceRect, CurrentSelectedGraph());
            return true;
        }

        private bool GraphPointerInputActive() =>
            _canvasOpen &&
            (_canvasInteraction != AutomationCanvasInteractionKind.None ||
             _draggingPaletteBlock ||
             _draggingNodeId != 0 ||
             _panningGraphCanvas ||
             _graphSlotMenuKind != AutomationGraphSlotMenuKind.None ||
             _graphContextMenuNodeId != 0 ||
             _graphReadinessPopoverNodeId != 0 ||
             _graphPropertyPickerNodeId != 0);

        private Vector2 CurrentCanvasWindowMousePosition() =>
            MouseGuiPosition() - _canvasRect.position;

        private bool LastCanvasWorkspaceUsable() =>
            _lastCanvasWorkspaceRect.width > 1f &&
            _lastCanvasWorkspaceRect.height > 1f;

        private void MaybeLogGraphDragRawUpdateStall(string reason)
        {
            _nodeDragStalledFrames++;
            if (_nodeDragStalledFrames < GraphDragStalledFrameThreshold)
                return;

            float now = Time.unscaledTime;
            if (now < _nextGraphInteractionDiagnosticsLogTime)
                return;

            _nextGraphInteractionDiagnosticsLogTime = now + GraphInteractionDiagnosticsLogCooldownSeconds;
            EsuRuntimeLog.Warning(
                "Automation Builder",
                "Automation graph pointer capture could not update from raw mouse input.",
                "reason=" + reason +
                "\ninteraction=" + _canvasInteraction +
                "\ndragged_node=" + _draggingNodeId.ToString(CultureInfo.InvariantCulture) +
                "\nleft_down=" + (Input.GetMouseButton(0) ? "true" : "false") +
                "\ncanvas_open=" + (_canvasOpen ? "true" : "false") +
                "\nworkspace_valid=" + (LastCanvasWorkspaceUsable() ? "true" : "false") +
                "\nowns_graph_input=" + (AutomationBuilderInputScope.OwnsGraphPointerInput ? "true" : "false") +
                "\nlast_raw_mouse=" + _nodeDragLastRawMouse.x.ToString("0.0", CultureInfo.InvariantCulture) +
                "," + _nodeDragLastRawMouse.y.ToString("0.0", CultureInfo.InvariantCulture));
        }

        private void BeginCanvasInteraction(
            AutomationCanvasInteractionKind kind,
            int mouseButton,
            Vector2 startMouse)
        {
            _canvasInteraction = kind;
            _canvasInteractionButton = mouseButton;
            _canvasInteractionStartMouse = startMouse;
            _canvasInteractionMoved = false;
            _nodeDragStalledFrames = 0;
            AutomationBuilderInputScope.SetGraphPointerInputOwned(true);
            AutomationBuilderInputScope.ClaimBuildInputForFrames(3);
            AutomationBuilderInputScope.ClaimCameraInputForFrames(3);
        }

        private void ClaimActiveCanvasInteractionInput()
        {
            if (!_canvasOpen)
            {
                AutomationBuilderInputScope.SetGraphPointerInputOwned(false);
                return;
            }

            bool active = GraphPointerInputActive();
            AutomationBuilderInputScope.SetGraphPointerInputOwned(active);
            if (!active)
                return;

            AutomationBuilderInputScope.ClaimBuildInputForFrames(3);
            AutomationBuilderInputScope.ClaimCameraInputForFrames(3);
        }

        private void CompleteCanvasInteractionIfReleased()
        {
            if (!_canvasOpen)
                return;

            if (_canvasInteraction == AutomationCanvasInteractionKind.NodeDrag &&
                _draggingNodeId != 0 &&
                !Input.GetMouseButton(0))
            {
                FinishNodeDrag(CurrentSelectedGraph());
                return;
            }

            if (_canvasInteraction == AutomationCanvasInteractionKind.CanvasPan &&
                _graphCanvasPanButton >= 0 &&
                !Input.GetMouseButton(_graphCanvasPanButton))
            {
                ResetCanvasInteractionState();
            }
        }

        private void ResetCanvasInteractionState()
        {
            _canvasInteraction = AutomationCanvasInteractionKind.None;
            _canvasInteractionButton = -1;
            _canvasInteractionStartMouse = Vector2.zero;
            _canvasInteractionMoved = false;
            _draggingPaletteBlock = false;
            _paletteDragMoved = false;
            _paletteDropPending = false;
            _panningGraphCanvas = false;
            _graphCanvasPanButton = -1;
            _draggingNodeId = 0;
            _suppressNextCanvasRightClick = false;
            _nodeDragStalledFrames = 0;
            _nodeDragLastRawMouse = Vector2.zero;
            AutomationBuilderInputScope.SetGraphPointerInputOwned(false);
        }

        private void CancelActiveCanvasInteraction(
            AutomationGraph graph,
            bool restoreNode)
        {
            if (_canvasInteraction == AutomationCanvasInteractionKind.NodeDrag &&
                restoreNode &&
                _draggingNodeId != 0)
            {
                AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _draggingNodeId);
                if (node != null)
                {
                    node.Rect = _nodeDragStartRect;
                    SyncNativeNodeRect(node);
                }
            }

            ResetCanvasInteractionState();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private void FinishNodeDrag(
            AutomationGraph graph,
            Event current = null)
        {
            if (graph == null || _draggingNodeId == 0)
            {
                ResetCanvasInteractionState();
                return;
            }

            AutomationGraphNode node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == _draggingNodeId);
            bool moved = _canvasInteractionMoved;
            ResetCanvasInteractionState();
            if (node != null)
            {
                if (moved && TrySnapGraphNode(graph, node))
                    moved = true;

                if (moved)
                    RefreshGraphConnections(graph);
                SyncNativeNodeRect(node);
                if (moved)
                    MarkAutomationDirty();
            }

            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current?.Use();
        }

        private void FinishPaletteDrag(
            Rect workspaceRect,
            Event current = null)
        {
            if (_canvasInteraction != AutomationCanvasInteractionKind.PaletteDrag &&
                !_draggingPaletteBlock)
            {
                ResetCanvasInteractionState();
                return;
            }

            Vector2 mouse = PaletteDragWindowMouse();
            bool moved = _canvasInteractionMoved || _paletteDragMoved;
            if (workspaceRect.Contains(mouse))
            {
                AddGraphNode(_draggingPaletteKind, hasPreferredRect: true, preferredRect: PaletteDropGraphRect(workspaceRect, mouse));
            }
            else if (!moved)
            {
                AddGraphNode(_draggingPaletteKind, hasPreferredRect: true, preferredRect: VisibleWorkspaceDropGraphRect(_draggingPaletteKind));
            }

            ResetCanvasInteractionState();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current?.Use();
        }

        private AutomationGraph CurrentSelectedGraph() =>
            _selectedBreadboard == null ? null : GraphFor(_selectedBreadboard);

        private void CompleteGraphNodeDragIfReleased(AutomationGraph graph)
        {
            if (graph == null || _draggingNodeId == 0)
                return;

            Event current = Event.current;
            bool released = current != null &&
                            current.type == EventType.MouseUp;
            bool lostRelease = current != null &&
                               (current.type == EventType.Layout ||
                                current.type == EventType.Repaint) &&
                               !Input.GetMouseButton(0);
            if (!released && !lostRelease)
                return;

            FinishNodeDrag(graph, released ? current : null);
        }

        private void ArrangeNativeGraphForReadability()
        {
            if (_selectedBreadboard == null)
            {
                InfoStore.Add("Automation Builder needs a selected breadboard before arranging blocks.");
                return;
            }

            AutomationGraph graph = SyncedGraphFor(_selectedBreadboard, force: true);
            if (graph == null || graph.Nodes.Count == 0)
            {
                InfoStore.Add("Automation Builder has no native graph blocks to arrange.");
                return;
            }

            int moved = ArrangeGraphNodes(graph);
            if (moved == 0)
            {
                InfoStore.Add("Automation Builder graph is already arranged.");
                return;
            }

            MarkAutomationDirty();
            InfoStore.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Automation Builder arranged {0:N0} native visual block{1}.",
                    moved,
                    moved == 1 ? string.Empty : "s"));
        }

        private int ArrangeGraphNodes(AutomationGraph graph)
        {
            var arranged = new HashSet<AutomationGraphNode>();
            var moved = 0;
            float startX = EsuHudLayout.Scale(80f);
            float startY = EsuHudLayout.Scale(56f);
            float chainGap = EsuHudLayout.Scale(122f);
            float looseGap = EsuHudLayout.Scale(42f);
            Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> socketValues =
                CaptureSocketValues(graph);
            Dictionary<AutomationGraphNode, List<AutomationGraphNode>> bodyChildren = CaptureBodyChildren(graph);
            Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>> bodyChains =
                CaptureBodyLayoutChains(graph, bodyChildren);
            var bodyChildNodes = new HashSet<AutomationGraphNode>(bodyChildren.SelectMany(pair => pair.Value));
            var chainNodes = new HashSet<AutomationGraphNode>(graph.Nodes
                .Where(node => node != null && !DrawsAsValueBlock(node.Kind, node.Rect)));
            List<AutomationGraphNode> topLevel = graph.Nodes
                .Where(node => node != null)
                .Where(node => !bodyChildNodes.Contains(node))
                .Where(node => SnappedValueHost(graph, node) == null)
                .Where(node => IsStackFlowNode(node) || AcceptsControlBody(node.Kind))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            List<List<AutomationGraphNode>> chains = OrderedLayoutChainsFromCandidates(graph, topLevel);

            float x = startX;
            foreach (List<AutomationGraphNode> chain in chains.Where(chain => chain.Count > 0))
            {
                float y = startY;
                ArrangeLayoutChain(
                    graph,
                    chain,
                    x,
                    ref y,
                    arranged,
                    chainNodes,
                    socketValues,
                    bodyChains,
                    ref moved);
                x += GraphNodeWidth + chainGap;
            }

            List<AutomationGraphNode> remaining = graph.Nodes
                .Where(node => node != null && !arranged.Contains(node))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            if (remaining.Count > 0)
            {
                float y = startY;
                foreach (AutomationGraphNode node in remaining)
                {
                    float width = GraphNodeWidthForKind(node.Kind);
                    float height = GraphNodeHeightForKind(node.Kind);
                    moved += SetGraphNodeRect(node, new Rect(x, y, width, height));
                    arranged.Add(node);
                    y = node.Rect.yMax + looseGap;
                }
            }

            return moved;
        }

        private static Dictionary<AutomationGraphNode, List<AutomationGraphNode>> CaptureBodyChildren(
            AutomationGraph graph)
        {
            var bodies = new Dictionary<AutomationGraphNode, List<AutomationGraphNode>>();
            if (graph == null)
                return bodies;

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsControlBody(node.Kind)))
            {
                List<AutomationGraphNode> children = BodyChildrenForHost(graph, host).ToList();
                if (children.Count > 0)
                    bodies[host] = children;
            }

            return bodies;
        }

        private static Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>> CaptureBodyLayoutChains(
            AutomationGraph graph,
            Dictionary<AutomationGraphNode, List<AutomationGraphNode>> bodyChildren)
        {
            var result = new Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>>();
            if (graph == null || bodyChildren == null)
                return result;

            foreach (KeyValuePair<AutomationGraphNode, List<AutomationGraphNode>> pair in bodyChildren)
                result[pair.Key] = OrderedLayoutChainsFromCandidates(graph, pair.Value);

            return result;
        }

        private static List<List<AutomationGraphNode>> OrderedLayoutChainsFromCandidates(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> candidates)
        {
            if (graph == null || candidates == null)
                return new List<List<AutomationGraphNode>>();

            List<AutomationGraphNode> ordered = candidates
                .Where(node => node != null)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            var chains = new List<List<AutomationGraphNode>>();
            var visited = new HashSet<AutomationGraphNode>();
            foreach (AutomationGraphNode node in ordered
                         .Where(node => PreviousSnappedStackNode(graph, ordered, node) == null))
            {
                AppendLayoutChain(graph, ordered, node, visited, chains);
            }

            foreach (AutomationGraphNode node in ordered)
                AppendLayoutChain(graph, ordered, node, visited, chains);

            return chains;
        }

        private static void AppendLayoutChain(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode start,
            HashSet<AutomationGraphNode> visited,
            List<List<AutomationGraphNode>> chains)
        {
            if (graph == null ||
                candidates == null ||
                start == null ||
                visited == null ||
                chains == null ||
                visited.Contains(start))
            {
                return;
            }

            var chain = new List<AutomationGraphNode>();
            AutomationGraphNode current = start;
            while (current != null && visited.Add(current))
            {
                chain.Add(current);
                current = NextSnappedStackNode(graph, candidates, current, visited);
            }

            if (chain.Count > 0)
                chains.Add(chain);
        }

        private void ArrangeLayoutChain(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> chain,
            float x,
            ref float y,
            HashSet<AutomationGraphNode> arranged,
            HashSet<AutomationGraphNode> chainNodes,
            Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> socketValues,
            Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>> bodyChains,
            ref int moved)
        {
            if (chain == null)
                return;

            foreach (AutomationGraphNode node in chain)
            {
                ArrangeLayoutNode(
                    graph,
                    node,
                    x,
                    y,
                    arranged,
                    chainNodes,
                    socketValues,
                    bodyChains,
                    ref moved);
                y = node.Rect.yMax - EsuHudLayout.Scale(2f);
            }
        }

        private void ArrangeLayoutNode(
            AutomationGraph graph,
            AutomationGraphNode node,
            float x,
            float y,
            HashSet<AutomationGraphNode> arranged,
            HashSet<AutomationGraphNode> chainNodes,
            Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> socketValues,
            Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>> bodyChains,
            ref int moved)
        {
            if (node == null || arranged == null)
                return;

            moved += SetGraphNodeRect(
                node,
                new Rect(
                    x,
                    y,
                    GraphNodeWidthForKind(node.Kind),
                    GraphNodeHeightForKind(node.Kind)));
            arranged.Add(node);

            if (AcceptsControlBody(node.Kind) &&
                bodyChains != null &&
                bodyChains.TryGetValue(node, out List<List<AutomationGraphNode>> childChains))
            {
                ArrangeControlBodyChains(
                    graph,
                    node,
                    childChains,
                    arranged,
                    chainNodes,
                    socketValues,
                    bodyChains,
                    ref moved);
            }

            ArrangeSocketValuesForHosts(graph, new[] { node }, arranged, chainNodes, socketValues, ref moved);
        }

        private void ArrangeControlBodyChains(
            AutomationGraph graph,
            AutomationGraphNode host,
            IReadOnlyList<List<AutomationGraphNode>> childChains,
            HashSet<AutomationGraphNode> arranged,
            HashSet<AutomationGraphNode> chainNodes,
            Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> socketValues,
            Dictionary<AutomationGraphNode, List<List<AutomationGraphNode>>> bodyChains,
            ref int moved)
        {
            if (host == null || childChains == null || childChains.Count == 0)
                return;

            Rect body = ControlBodyRect(host.Rect, host.Kind);
            float y = body.y + EsuHudLayout.Scale(24f);
            float childX = body.x + EsuHudLayout.Scale(14f);
            foreach (List<AutomationGraphNode> chain in childChains)
            {
                ArrangeLayoutChain(
                    graph,
                    chain,
                    childX,
                    ref y,
                    arranged,
                    chainNodes,
                    socketValues,
                    bodyChains,
                    ref moved);
                y += EsuHudLayout.Scale(10f);
            }

            float requiredHeight = y - host.Rect.y + EsuHudLayout.Scale(24f);
            if (requiredHeight > host.Rect.height)
            {
                Rect expanded = host.Rect;
                expanded.height = requiredHeight;
                moved += SetGraphNodeRect(host, expanded);
            }
        }

        private static Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> CaptureSocketValues(
            AutomationGraph graph,
            IEnumerable<AutomationGraphNode> hosts = null)
        {
            var values = new Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode>();
            if (graph == null)
                return values;

            IEnumerable<AutomationGraphNode> hostNodes = hosts ?? graph.Nodes;
            foreach (AutomationGraphNode host in hostNodes)
            {
                if (host == null || !AcceptsValueSlot(host.Kind))
                    continue;

                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
                {
                    AutomationGraphNode value = SnappedValueForHost(graph, host, slotKind);
                    if (value != null)
                        values[Tuple.Create(host, slotKind)] = value;
                }
            }

            return values;
        }

        private void ArrangeSocketValuesForHosts(
            AutomationGraph graph,
            IEnumerable<AutomationGraphNode> hosts,
            HashSet<AutomationGraphNode> arranged,
            HashSet<AutomationGraphNode> chainNodes,
            Dictionary<Tuple<AutomationGraphNode, AutomationValueSlotKind>, AutomationGraphNode> socketValues,
            ref int moved)
        {
            if (hosts == null || arranged == null || socketValues == null)
                return;

            foreach (AutomationGraphNode host in hosts)
            {
                if (host == null || !AcceptsValueSlot(host.Kind))
                    continue;

                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
                {
                    socketValues.TryGetValue(Tuple.Create(host, slotKind), out AutomationGraphNode value);
                    if (value == null ||
                        ReferenceEquals(value, host) ||
                        arranged.Contains(value) ||
                        chainNodes != null &&
                        chainNodes.Contains(value) &&
                        !DrawsAsValueBlock(value.Kind, value.Rect))
                    {
                        continue;
                    }

                    Rect slot = ValueSlotRect(host.Rect, host.Kind, slotKind);
                    moved += SetGraphNodeRect(value, slot);
                    arranged.Add(value);
                }
            }
        }

        private int SetGraphNodeRect(
            AutomationGraphNode node,
            Rect rect)
        {
            if (node == null)
                return 0;

            rect.x = Mathf.Max(0f, rect.x);
            rect.y = Mathf.Max(0f, rect.y);
            bool valueFootprint = CanProduceValueBlock(node.Kind) && IsValueFootprint(rect);
            rect = NormalizeGraphNodeRect(node.Kind, rect, valueFootprint);
            bool changed =
                Mathf.Abs(node.Rect.x - rect.x) > 0.001f ||
                Mathf.Abs(node.Rect.y - rect.y) > 0.001f ||
                Mathf.Abs(node.Rect.width - rect.width) > 0.001f ||
                Mathf.Abs(node.Rect.height - rect.height) > 0.001f;
            node.Rect = rect;
            SyncNativeNodeRect(node);
            return changed ? 1 : 0;
        }

        private void DrawGraphBackground(Rect rect)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(DecorationEditorTheme.Cyan.r, DecorationEditorTheme.Cyan.g, DecorationEditorTheme.Cyan.b, 0.16f);
                float grid = EsuHudLayout.Scale(48f);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(1f));
                float startX = Mathf.Floor(rect.xMin / grid) * grid;
                float startY = Mathf.Floor(rect.yMin / grid) * grid;
                for (float x = startX; x < rect.xMax; x += grid)
                    GUI.DrawTexture(new Rect(x, rect.yMin, line, rect.height), Texture2D.whiteTexture);
                for (float y = startY; y < rect.yMax; y += grid)
                    GUI.DrawTexture(new Rect(rect.xMin, y, rect.width, line), Texture2D.whiteTexture);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private void DrawGraphConnections(
            AutomationGraph graph,
            IReadOnlyList<List<AutomationGraphNode>> chains)
        {
            if (graph == null)
                return;

            DrawImportedNativeGraphConnections(graph);

            foreach (AutomationGraphConnection connection in GraphConnections(graph, AutomationGraphConnectionKind.Stack))
            {
                AutomationGraphNode from = connection.From;
                AutomationGraphNode to = connection.To;
                if (from == null || to == null)
                    continue;

                Vector2 start = new Vector2(from.Rect.center.x, from.Rect.yMax);
                Vector2 end = new Vector2(to.Rect.center.x, to.Rect.y);
                Color color = NodeColor(from.Kind);
                DrawCanvasFlow(start, end, color, from.Id);
            }

            foreach (AutomationGraphConnection connection in GraphConnections(graph, AutomationGraphConnectionKind.Value))
            {
                AutomationGraphNode value = connection.From;
                AutomationGraphNode host = connection.To;
                if (value == null || host == null)
                    continue;

                Rect slot = ValueSlotRect(host.Rect, host.Kind, connection.SlotKind);
                DrawCanvasFlow(
                    new Vector2(value.Rect.xMax, value.Rect.center.y),
                    new Vector2(slot.x, slot.center.y),
                    NodeColor(value.Kind),
                    value.Id);
            }
        }

        private void DrawImportedNativeGraphConnections(AutomationGraph graph)
        {
            if (graph == null)
                return;

            foreach (AutomationGraphConnection connection in ImportedNativeConnections(graph))
            {
                AutomationGraphNode from = connection.From;
                AutomationGraphNode to = connection.To;
                if (from == null || to == null)
                    continue;

                if (connection.Kind == AutomationGraphConnectionKind.Stack)
                {
                    DrawCanvasImportedFlow(
                        new Vector2(from.Rect.center.x, from.Rect.yMax),
                        new Vector2(to.Rect.center.x, to.Rect.y));
                    continue;
                }

                if (connection.Kind == AutomationGraphConnectionKind.Value)
                {
                    Rect slot = ValueSlotRect(to.Rect, to.Kind, connection.SlotKind);
                    DrawCanvasImportedFlow(
                        new Vector2(from.Rect.xMax, from.Rect.center.y),
                        new Vector2(slot.x, slot.center.y));
                }
            }
        }

        private void DrawGraphSnapPreview(AutomationGraph graph)
        {
            if (graph == null || _draggingNodeId == 0)
                return;

            AutomationGraphNode node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == _draggingNodeId);
            if (node == null)
                return;

            if (CanProduceValueBlock(node.Kind) &&
                TryFindValueSnapPreview(graph, node, out Rect valueTarget, out AutomationValueSlotKind slotKind, out AutomationNodeKind hostKind))
            {
                DrawSnapPreviewTarget(valueTarget, "snap " + ValueSlotLabel(hostKind, slotKind), NodeColor(node.Kind));
                DrawCanvasFlow(
                    new Vector2(node.Rect.xMax, node.Rect.center.y),
                    new Vector2(valueTarget.x, valueTarget.center.y),
                    NodeColor(node.Kind),
                    node.Id);
                return;
            }

            if (DrawsAsValueBlock(node.Kind, node.Rect))
                return;

            if (TryFindBodySnapPreview(graph, node, out Rect bodyTarget))
            {
                DrawSnapPreviewTarget(bodyTarget, "snap into body", NodeColor(node.Kind));
                return;
            }

            if (TryFindStackSnapPreview(graph, node, out Rect stackTarget, out bool snapBelow))
                DrawSnapPreviewTarget(stackTarget, snapBelow ? "snap below" : "snap above", NodeColor(node.Kind));
        }

        private static bool TryFindValueSnapPreview(
            AutomationGraph graph,
            AutomationGraphNode node,
            out Rect target,
            out AutomationValueSlotKind slotKind,
            out AutomationNodeKind hostKind)
        {
            target = Rect.zero;
            slotKind = AutomationValueSlotKind.Pass;
            hostKind = AutomationNodeKind.OutputSetter;
            if (graph == null || node == null)
                return false;

            float threshold = EsuHudLayout.Scale(46f);
            AutomationGraphNode best = null;
            float bestDistance = threshold;
            foreach (AutomationGraphNode other in graph.Nodes)
            {
                if (other == null ||
                    ReferenceEquals(other, node) ||
                    !AcceptsValueSlot(other.Kind))
                {
                    continue;
                }

                foreach (AutomationValueSlotKind candidateSlotKind in ActiveValueSlotKinds(graph, other))
                {
                    if (!CanSnapIntoValueSlot(node, other.Kind, candidateSlotKind))
                        continue;

                    Rect slot = ValueSlotRect(other.Rect, other.Kind, candidateSlotKind);
                    float distance = Vector2.Distance(node.Rect.center, slot.center);
                    if (distance >= bestDistance)
                        continue;

                    best = other;
                    target = slot;
                    slotKind = candidateSlotKind;
                    hostKind = other.Kind;
                    bestDistance = distance;
                }
            }

            return best != null;
        }

        private static bool TryFindBodySnapPreview(
            AutomationGraph graph,
            AutomationGraphNode node,
            out Rect target)
        {
            target = Rect.zero;
            if (graph == null || node == null || !IsBodyFlowNode(node))
                return false;

            float threshold = EsuHudLayout.Scale(42f);
            AutomationGraphNode best = null;
            Rect bestBody = Rect.zero;
            float bestDistance = threshold;
            foreach (AutomationGraphNode host in graph.Nodes)
            {
                if (host == null ||
                    ReferenceEquals(host, node) ||
                    !AcceptsControlBody(host.Kind))
                {
                    continue;
                }

                Rect body = ControlBodyRect(host.Rect, host.Kind);
                Rect padded = ExpandRect(body, threshold);
                if (!padded.Contains(node.Rect.center))
                    continue;

                float distance = body.Contains(node.Rect.center)
                    ? 0f
                    : DistanceToRect(node.Rect.center, body);
                if (distance >= bestDistance)
                    continue;

                best = host;
                bestBody = body;
                bestDistance = distance;
            }

            if (best == null)
                return false;

            List<AutomationGraphNode> siblings = BodyChildrenForHost(graph, best)
                .Where(child => !ReferenceEquals(child, node))
                .ToList();
            float y = siblings.Count == 0
                ? bestBody.y + EsuHudLayout.Scale(24f)
                : siblings.Max(child => child.Rect.yMax) + EsuHudLayout.Scale(8f);
            target = new Rect(
                bestBody.x + EsuHudLayout.Scale(14f),
                y,
                GraphNodeWidthForKind(node.Kind),
                node.Rect.height);
            return true;
        }

        private static bool TryFindStackSnapPreview(
            AutomationGraph graph,
            AutomationGraphNode node,
            out Rect target,
            out bool snapBelow)
        {
            target = Rect.zero;
            snapBelow = true;

            if (!TryFindStackSnapTarget(graph, node, out AutomationGraphNode best, out snapBelow))
                return false;

            target = node.Rect;
            target.x = best.Rect.x;
            target.y = snapBelow
                ? best.Rect.yMax - EsuHudLayout.Scale(2f)
                : Mathf.Max(0f, best.Rect.y - node.Rect.height + EsuHudLayout.Scale(2f));
            return true;
        }

        private static bool TryFindStackSnapTarget(
            AutomationGraph graph,
            AutomationGraphNode node,
            out AutomationGraphNode best,
            out bool snapBelow)
        {
            best = null;
            snapBelow = true;
            if (graph == null || !CanStackSnapNode(node))
                return false;

            float bestScore = EsuHudLayout.Scale(30f);
            foreach (AutomationGraphNode other in graph.Nodes)
            {
                if (!CanStackSnapNode(other) || ReferenceEquals(other, node))
                    continue;

                if (TryScoreStackSnap(node.Rect, other.Rect, below: true, out float belowScore) &&
                    belowScore < bestScore)
                {
                    best = other;
                    bestScore = belowScore;
                    snapBelow = true;
                }

                if (TryScoreStackSnap(node.Rect, other.Rect, below: false, out float aboveScore) &&
                    aboveScore < bestScore)
                {
                    best = other;
                    bestScore = aboveScore;
                    snapBelow = false;
                }
            }

            return best != null;
        }

        private static bool TryScoreStackSnap(
            Rect moving,
            Rect target,
            bool below,
            out float score)
        {
            score = float.MaxValue;
            float verticalDistance = below
                ? Mathf.Abs(moving.y - target.yMax)
                : Mathf.Abs(moving.yMax - target.y);
            float verticalThreshold = EsuHudLayout.Scale(30f);
            if (verticalDistance > verticalThreshold)
                return false;

            float overlap = Mathf.Min(moving.xMax, target.xMax) - Mathf.Max(moving.xMin, target.xMin);
            float centerDistance = Mathf.Abs(moving.center.x - target.center.x);
            float horizontalThreshold = Mathf.Min(
                EsuHudLayout.Scale(80f),
                Mathf.Max(EsuHudLayout.Scale(30f), Mathf.Min(moving.width, target.width) * 0.42f));
            if (overlap <= EsuHudLayout.Scale(16f) &&
                centerDistance > horizontalThreshold)
            {
                return false;
            }

            score = verticalDistance + centerDistance * 0.18f;
            return true;
        }

        private static void DrawSnapPreviewTarget(Rect rect, string label, Color color)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(color.r, color.g, color.b, 0.2f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(color.r, color.g, color.b, 0.95f);
                float line = Mathf.Max(2f, EsuHudLayout.Scale(2f));
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - line, rect.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, line, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - line, rect.y, line, rect.height), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(
                    new Rect(rect.x + EsuHudLayout.Scale(6f), rect.y - EsuHudLayout.Scale(20f), rect.width - EsuHudLayout.Scale(12f), EsuHudLayout.Scale(18f)),
                    label,
                    DecorationEditorTheme.Mini);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static IEnumerable<AutomationGraphNode> OrderedGraphFlowNodes(AutomationGraph graph)
        {
            return OrderedGraphFlowChains(graph).SelectMany(chain => chain);
        }

        private void RefreshGraphConnections(
            AutomationGraph graph,
            bool preserveRestoredNative = false)
        {
            if (graph == null)
                return;

            var connections = new List<AutomationGraphConnection>();
            foreach (List<AutomationGraphNode> flow in GeometryOrderedGraphFlowChains(graph))
            {
                for (int index = 0; index < flow.Count - 1; index++)
                {
                    AutomationGraphNode from = flow[index];
                    AutomationGraphNode to = flow[index + 1];
                    if (CanStoreEsuGeometryConnection(AutomationGraphConnectionKind.Stack, from, to))
                    {
                        AddConnectionIfMissing(
                            connections,
                            new AutomationGraphConnection(AutomationGraphConnectionKind.Stack, from, to));
                    }
                }
            }

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsValueSlot(node.Kind)))
            {
                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
                {
                    AutomationGraphNode value = GeometrySnappedValueForHost(graph, host, slotKind);
                    if (value != null &&
                        CanStoreEsuGeometryConnection(AutomationGraphConnectionKind.Value, value, host))
                    {
                        AddConnectionIfMissing(
                            connections,
                            new AutomationGraphConnection(AutomationGraphConnectionKind.Value, value, host, slotKind));
                    }
                }
            }

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsControlBody(node.Kind)))
            {
                foreach (AutomationGraphNode child in GeometryBodyChildrenForHost(graph, host))
                {
                    if (CanStoreEsuGeometryConnection(AutomationGraphConnectionKind.Body, host, child))
                    {
                        AddConnectionIfMissing(
                            connections,
                            new AutomationGraphConnection(AutomationGraphConnectionKind.Body, host, child));
                    }
                }
            }

            if (preserveRestoredNative)
            {
                foreach (AutomationGraphConnection connection in graph.Connections.Where(connection =>
                             connection?.Origin == AutomationGraphWireOrigin.EsuNative))
                {
                    AddConnectionIfMissing(connections, connection);
                }
            }

            RemoveStackFedPrimaryValueConnections(connections);
            graph.RebuildConnections(connections);
        }

        private bool CanStoreEsuGeometryConnection(
            AutomationGraphConnectionKind kind,
            AutomationGraphNode from,
            AutomationGraphNode to)
        {
            if (from == null || to == null)
                return false;

            if (kind == AutomationGraphConnectionKind.Body)
            {
                return IsGraphNodeApplyWritable(from) &&
                       IsGraphNodeApplyWritable(to);
            }

            return IsGraphNodeApplyWritable(to);
        }

        private static IReadOnlyList<AutomationGraphConnection> GraphConnections(
            AutomationGraph graph,
            AutomationGraphConnectionKind kind)
        {
            return graph?.Connections
                       .Where(connection => connection?.Kind == kind)
                       .ToList() ??
                   new List<AutomationGraphConnection>();
        }

        private void RefreshNativeGraphConnections(AutomationGraph graph)
        {
            if (graph == null)
                return;

            var editableConnections = graph.Connections
                .Where(connection =>
                    connection != null &&
                    !connection.IsImportedNative &&
                    ShouldPreserveExistingGraphConnection(connection))
                .ToList();
            var importedConnections = new List<AutomationGraphConnection>();
            List<AutomationGraphNode> nativeNodes = graph.Nodes
                .Where(node => node?.NativeComponent != null)
                .ToList();
            foreach (AutomationGraphNode from in nativeNodes)
            {
                foreach (AutomationGraphNode to in nativeNodes)
                {
                    if (ReferenceEquals(from, to) ||
                        !NativeStackConnectionExists(from, to))
                    {
                        continue;
                    }

                    AddNativeGraphConnection(
                        editableConnections,
                        importedConnections,
                        new AutomationGraphConnection(
                            AutomationGraphConnectionKind.Stack,
                            from,
                            to,
                            origin: NativeConnectionOrigin(to)));
                }
            }

            foreach (AutomationGraphNode host in nativeNodes.Where(node => AcceptsValueSlot(node.Kind)))
            {
                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
                {
                    foreach (AutomationGraphNode value in nativeNodes)
                    {
                        if (ReferenceEquals(value, host) ||
                            !NativeValueConnectionExists(value, host, slotKind))
                        {
                            continue;
                        }

                        AddNativeGraphConnection(
                            editableConnections,
                            importedConnections,
                            new AutomationGraphConnection(
                                AutomationGraphConnectionKind.Value,
                                value,
                                host,
                                slotKind,
                                NativeConnectionOrigin(host)));
                    }
                }
            }

            RemoveStackFedPrimaryValueConnections(editableConnections);
            RemoveStackFedPrimaryValueConnections(importedConnections);
            graph.RebuildConnections(editableConnections);
            graph.RebuildImportedNativeConnections(importedConnections);
        }

        private bool ShouldPreserveExistingGraphConnection(AutomationGraphConnection connection)
        {
            if (connection?.From == null || connection.To == null)
                return false;

            if (connection.Origin == AutomationGraphWireOrigin.EsuNative ||
                connection.Origin == AutomationGraphWireOrigin.NativeImported)
            {
                return false;
            }

            return _automationDirty || !ConnectionBetweenNativeNodes(connection);
        }

        private static bool ConnectionBetweenNativeNodes(AutomationGraphConnection connection)
        {
            return connection?.From?.NativeComponent != null &&
                   connection.To?.NativeComponent != null;
        }

        private AutomationGraphWireOrigin NativeConnectionOrigin(AutomationGraphNode target)
        {
            return IsEsuOwnedNativeNode(target)
                ? AutomationGraphWireOrigin.EsuNative
                : AutomationGraphWireOrigin.NativeImported;
        }

        private static void AddNativeGraphConnection(
            List<AutomationGraphConnection> editableConnections,
            List<AutomationGraphConnection> importedConnections,
            AutomationGraphConnection connection)
        {
            if (connection == null)
                return;

            if (connection.Origin == AutomationGraphWireOrigin.NativeImported)
                AddConnectionIfMissing(importedConnections, connection);
            else
                AddConnectionIfMissing(editableConnections, connection);
        }

        private static void AddConnectionIfMissing(
            List<AutomationGraphConnection> connections,
            AutomationGraphConnection candidate)
        {
            if (connections == null ||
                candidate?.From == null ||
                candidate.To == null)
            {
                return;
            }

            if (connections.Any(existing => ConnectionsEquivalent(existing, candidate)))
                return;

            connections.Add(candidate);
        }

        private static void RemoveStackFedPrimaryValueConnections(
            List<AutomationGraphConnection> connections)
        {
            if (connections == null || connections.Count == 0)
                return;

            var stackFedPrimaryHosts = new HashSet<int>(connections
                .Where(connection =>
                    connection?.Kind == AutomationGraphConnectionKind.Stack &&
                    UsesStackAsPrimaryInput(connection.To?.Kind ?? AutomationNodeKind.Comment))
                .Select(connection => connection.ToNodeId));
            if (stackFedPrimaryHosts.Count == 0)
                return;

            connections.RemoveAll(connection =>
                connection?.Kind == AutomationGraphConnectionKind.Value &&
                UsesStackAsPrimaryInput(connection.To?.Kind ?? AutomationNodeKind.Comment) &&
                connection.SlotKind == AutomationValueSlotKind.Pass &&
                stackFedPrimaryHosts.Contains(connection.ToNodeId));
        }

        private static bool UsesStackAsPrimaryInput(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.OutputSetter ||
                   IsLogicGateKind(kind) ||
                   IsFuzzyThresholdKind(kind) ||
                   IsMathEvaluatorKind(kind) ||
                   IsMaxMinKind(kind) ||
                   kind == AutomationNodeKind.Clamp ||
                   kind == AutomationNodeKind.Smooth;
        }

        private static bool IsStackFedPrimaryValueSlot(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            return graph != null &&
                   host != null &&
                   slotKind == AutomationValueSlotKind.Pass &&
                   UsesStackAsPrimaryInput(host.Kind) &&
                   PreviousFlowNode(graph, host) != null;
        }

        private static IEnumerable<AutomationValueSlotKind> ActiveValueSlotKinds(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            if (host == null)
                yield break;

            foreach (AutomationValueSlotKind slotKind in ValueSlotKinds(host.Kind))
            {
                if (!IsStackFedPrimaryValueSlot(graph, host, slotKind))
                    yield return slotKind;
            }
        }

        private static bool ConnectionsEquivalent(
            AutomationGraphConnection left,
            AutomationGraphConnection right)
        {
            return left != null &&
                   right != null &&
                   left.Kind == right.Kind &&
                   left.FromNodeId == right.FromNodeId &&
                   left.ToNodeId == right.ToNodeId &&
                   left.SlotKind == right.SlotKind;
        }

        private static IReadOnlyList<AutomationGraphConnection> ImportedNativeConnections(AutomationGraph graph)
        {
            return graph?.ImportedNativeConnections ??
                   new List<AutomationGraphConnection>();
        }

        private static AutomationGraphConnection GraphValueConnection(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (graph == null || host == null)
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Value)
                .FirstOrDefault(connection =>
                    ReferenceEquals(connection.To, host) &&
                    connection.SlotKind == slotKind);
        }

        private static List<List<AutomationGraphNode>> OrderedGraphFlowChains(AutomationGraph graph)
        {
            if (graph == null)
                return new List<List<AutomationGraphNode>>();

            IReadOnlyList<AutomationGraphConnection> stackConnections = GraphConnections(graph, AutomationGraphConnectionKind.Stack);
            var result = new List<List<AutomationGraphNode>>();
            List<AutomationGraphNode> topLevel = graph.Nodes
                .Where(node => node != null)
                .Where(node => SnappedBodyParent(graph, node) == null)
                .Where(node => SnappedValueHost(graph, node) == null)
                .Where(node => IsStackFlowNode(node) || AcceptsControlBody(node.Kind))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();

            var visited = new HashSet<AutomationGraphNode>();
            List<AutomationGraphNode> roots = topLevel
                .Where(node => PreviousGraphStackNode(stackConnections, topLevel, node) == null)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            foreach (AutomationGraphNode node in roots)
                AppendGraphFlowChain(graph, stackConnections, topLevel, node, visited, result);
            foreach (AutomationGraphNode node in topLevel)
                AppendGraphFlowChain(graph, stackConnections, topLevel, node, visited, result);

            return result;
        }

        private static void AppendGraphFlowChain(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphConnection> stackConnections,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode start,
            HashSet<AutomationGraphNode> visited,
            List<List<AutomationGraphNode>> result)
        {
            if (start == null || visited == null || result == null || visited.Contains(start))
                return;

            var chain = new List<AutomationGraphNode>();
            AutomationGraphNode current = start;
            while (current != null && visited.Add(current))
            {
                AppendGraphFlowNode(graph, current, chain);
                current = NextGraphStackNode(stackConnections, candidates, current, visited);
            }

            if (chain.Count > 0)
                result.Add(chain);
        }

        private static void AppendGraphFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node,
            List<AutomationGraphNode> result)
        {
            if (node == null || result == null)
                return;

            if (IsStackFlowNode(node))
                result.Add(node);

            foreach (AutomationGraphNode child in BodyChildrenForHost(graph, node))
                AppendGraphFlowNode(graph, child, result);
        }

        private static AutomationGraphNode PreviousGraphStackNode(
            IReadOnlyList<AutomationGraphConnection> stackConnections,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode node)
        {
            if (stackConnections == null || candidates == null || node == null)
                return null;

            return stackConnections
                .Where(connection => ReferenceEquals(connection.To, node))
                .Select(connection => connection.From)
                .Where(candidate => candidate != null && candidates.Contains(candidate))
                .OrderBy(candidate => Mathf.Abs(node.Rect.y - (candidate.Rect.yMax - EsuHudLayout.Scale(2f))))
                .ThenBy(candidate => Mathf.Abs(node.Rect.x - candidate.Rect.x))
                .FirstOrDefault();
        }

        private static AutomationGraphNode NextGraphStackNode(
            IReadOnlyList<AutomationGraphConnection> stackConnections,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode node,
            HashSet<AutomationGraphNode> visited)
        {
            if (stackConnections == null || candidates == null || node == null)
                return null;

            return stackConnections
                .Where(connection => ReferenceEquals(connection.From, node))
                .Select(connection => connection.To)
                .Where(candidate => candidate != null && candidates.Contains(candidate))
                .Where(candidate => visited == null || !visited.Contains(candidate))
                .OrderBy(candidate => Mathf.Abs(candidate.Rect.y - (node.Rect.yMax - EsuHudLayout.Scale(2f))))
                .ThenBy(candidate => Mathf.Abs(candidate.Rect.x - node.Rect.x))
                .FirstOrDefault();
        }

        private static AutomationGraphNode PreviousSnappedStackNode(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode node)
        {
            if (graph == null || candidates == null || node == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, node))
                .Where(candidate => AreStackNodesSnapped(graph, candidate, node))
                .OrderBy(candidate => Mathf.Abs(node.Rect.y - (candidate.Rect.yMax - EsuHudLayout.Scale(2f))))
                .ThenBy(candidate => Mathf.Abs(node.Rect.x - candidate.Rect.x))
                .FirstOrDefault();
        }

        private static AutomationGraphNode NextSnappedStackNode(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode node,
            HashSet<AutomationGraphNode> visited)
        {
            if (graph == null || candidates == null || node == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, node))
                .Where(candidate => visited == null || !visited.Contains(candidate))
                .Where(candidate => AreStackNodesSnapped(graph, node, candidate))
                .OrderBy(candidate => Mathf.Abs(candidate.Rect.y - (node.Rect.yMax - EsuHudLayout.Scale(2f))))
                .ThenBy(candidate => Mathf.Abs(candidate.Rect.x - node.Rect.x))
                .FirstOrDefault();
        }

        private static bool AreStackNodesSnapped(
            AutomationGraph graph,
            AutomationGraphNode from,
            AutomationGraphNode to)
        {
            if (graph == null ||
                !CanStackSnapNode(from) ||
                !CanStackSnapNode(to) ||
                !ReferenceEquals(GeometrySnappedBodyParent(graph, from), GeometrySnappedBodyParent(graph, to)))
            {
                return false;
            }

            float expectedY = from.Rect.yMax - EsuHudLayout.Scale(2f);
            return Mathf.Abs(to.Rect.x - from.Rect.x) <= EsuHudLayout.Scale(14f) &&
                   Mathf.Abs(to.Rect.y - expectedY) <= EsuHudLayout.Scale(14f);
        }

        private static void DrawCanvasImportedFlow(Vector2 start, Vector2 end)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0.78f, 0.9f, 0.95f, 0.28f * previous.a);
                float width = Mathf.Max(1f, EsuHudLayout.Scale(1f));
                float midY = Mathf.Lerp(start.y, end.y, 0.5f);
                DrawCanvasLine(new Vector2(start.x, start.y), new Vector2(start.x, midY), width);
                DrawCanvasLine(new Vector2(start.x, midY), new Vector2(end.x, midY), width);
                DrawCanvasLine(new Vector2(end.x, midY), new Vector2(end.x, end.y), width);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private void DrawCanvasFlow(Vector2 start, Vector2 end, Color color, int seed)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(color.r, color.g, color.b, 0.58f);
                float width = Mathf.Max(2f, EsuHudLayout.Scale(2f));
                float midY = Mathf.Lerp(start.y, end.y, 0.5f);
                DrawCanvasLine(new Vector2(start.x, start.y), new Vector2(start.x, midY), width);
                DrawCanvasLine(new Vector2(start.x, midY), new Vector2(end.x, midY), width);
                DrawCanvasLine(new Vector2(end.x, midY), new Vector2(end.x, end.y), width);

                float t = Mathf.Repeat(Time.unscaledTime * 0.85f + seed * 0.13f, 1f);
                Vector2 pulse = Vector2.Lerp(start, end, t);
                GUI.color = new Color(color.r, color.g, color.b, 0.95f);
                GUI.DrawTexture(new Rect(pulse.x - width * 2f, pulse.y - width * 2f, width * 4f, width * 4f), Texture2D.whiteTexture);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static bool IsStackFlowNode(AutomationGraphNode node)
        {
            return node != null &&
                   !DrawsAsValueBlock(node.Kind, node.Rect) &&
                   node.Kind != AutomationNodeKind.Comment &&
                   node.Kind != AutomationNodeKind.Forever;
        }

        private static bool CanStackSnapNode(AutomationGraphNode node)
        {
            return node != null &&
                   !DrawsAsValueBlock(node.Kind, node.Rect) &&
                   node.Kind != AutomationNodeKind.Comment &&
                   (IsStackFlowNode(node) || AcceptsControlBody(node.Kind));
        }

        private static List<List<AutomationGraphNode>> GeometryOrderedGraphFlowChains(AutomationGraph graph)
        {
            if (graph == null)
                return new List<List<AutomationGraphNode>>();

            var result = new List<List<AutomationGraphNode>>();
            List<AutomationGraphNode> topLevel = graph.Nodes
                .Where(node => node != null)
                .Where(node => GeometrySnappedBodyParent(graph, node) == null)
                .Where(node => GeometrySnappedValueHost(graph, node) == null)
                .Where(node => IsStackFlowNode(node) || AcceptsControlBody(node.Kind))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();

            var visited = new HashSet<AutomationGraphNode>();
            List<AutomationGraphNode> roots = topLevel
                .Where(node => PreviousSnappedStackNode(graph, topLevel, node) == null)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            foreach (AutomationGraphNode node in roots)
                AppendGeometryFlowChain(graph, topLevel, node, visited, result);
            foreach (AutomationGraphNode node in topLevel)
                AppendGeometryFlowChain(graph, topLevel, node, visited, result);

            return result;
        }

        private static void AppendGeometryFlowChain(
            AutomationGraph graph,
            IReadOnlyList<AutomationGraphNode> candidates,
            AutomationGraphNode start,
            HashSet<AutomationGraphNode> visited,
            List<List<AutomationGraphNode>> result)
        {
            if (start == null || visited == null || result == null || visited.Contains(start))
                return;

            var chain = new List<AutomationGraphNode>();
            AutomationGraphNode current = start;
            while (current != null && visited.Add(current))
            {
                AppendGeometryFlowNode(graph, current, chain);
                current = NextSnappedStackNode(graph, candidates, current, visited);
            }

            if (chain.Count > 0)
                result.Add(chain);
        }

        private static void AppendGeometryFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node,
            List<AutomationGraphNode> result)
        {
            if (node == null || result == null)
                return;

            if (IsStackFlowNode(node))
                result.Add(node);

            foreach (AutomationGraphNode child in GeometryBodyChildrenForHost(graph, node))
                AppendGeometryFlowNode(graph, child, result);
        }

        private static AutomationGraphNode SnappedValueForHost(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            return SnappedValueForHost(graph, host, AutomationValueSlotKind.Pass);
        }

        private static AutomationGraphNode SnappedValueForHost(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (IsStackFedPrimaryValueSlot(graph, host, slotKind))
                return null;

            return GraphValueConnection(graph, host, slotKind)?.From;
        }

        private static AutomationGraphNode GeometrySnappedValueForHost(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            return ValueSlotCandidateNodes(graph, host, slotKind).FirstOrDefault();
        }

        private static List<AutomationGraphNode> ValueSlotCandidateNodes(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            var candidates = new List<AutomationGraphNode>();
            if (graph == null || host == null || !AcceptsValueSlot(host.Kind))
                return candidates;

            Rect slot = ValueSlotRect(host.Rect, host.Kind, slotKind);
            float bestDistance = EsuHudLayout.Scale(52f);
            foreach (AutomationGraphNode node in graph.Nodes
                         .Where(node =>
                             node != null &&
                             !ReferenceEquals(node, host) &&
                             CanSnapIntoValueSlot(node, host.Kind, slotKind))
                         .Select(node => new
                         {
                             Node = node,
                             Distance = Vector2.Distance(node.Rect.center, slot.center)
                         })
                         .Where(candidate => candidate.Distance < bestDistance)
                         .OrderBy(candidate => candidate.Distance)
                         .ThenBy(candidate => candidate.Node.Rect.y)
                         .ThenBy(candidate => candidate.Node.Rect.x)
                         .Select(candidate => candidate.Node))
            {
                if (!candidates.Contains(node))
                    candidates.Add(node);
            }

            return candidates;
        }

        private static IEnumerable<Tuple<AutomationGraphNode, AutomationValueSlotKind, AutomationGraphNode>> ValueSlotAssignments(
            AutomationGraph graph)
        {
            if (graph == null)
                yield break;

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsValueSlot(node.Kind)))
            {
                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
                {
                    AutomationGraphNode value = SnappedValueForHost(graph, host, slotKind);
                    if (value != null)
                        yield return Tuple.Create(host, slotKind, value);
                }
            }
        }

        private static AutomationGraphNode SnappedValueHost(
            AutomationGraph graph,
            AutomationGraphNode value)
        {
            if (graph == null ||
                value == null ||
                !CanProduceValueBlock(value.Kind))
            {
                return null;
            }

            return GraphConnections(graph, AutomationGraphConnectionKind.Value)
                .FirstOrDefault(connection => ReferenceEquals(connection.From, value))
                ?.To;
        }

        private static AutomationGraphNode GeometrySnappedValueHost(
            AutomationGraph graph,
            AutomationGraphNode value)
        {
            if (graph == null ||
                value == null ||
                !CanProduceValueBlock(value.Kind))
            {
                return null;
            }

            foreach (AutomationGraphNode host in graph.Nodes)
            {
                if (host == null || !AcceptsValueSlot(host.Kind))
                    continue;

                if (ActiveValueSlotKinds(graph, host).Any(slotKind => ReferenceEquals(GeometrySnappedValueForHost(graph, host, slotKind), value)))
                    return host;
            }

            return null;
        }

        private static IEnumerable<AutomationGraphNode> BodyChildrenForHost(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            if (graph == null || host == null || !AcceptsControlBody(host.Kind))
                return Enumerable.Empty<AutomationGraphNode>();

            return GraphConnections(graph, AutomationGraphConnectionKind.Body)
                .Where(connection => ReferenceEquals(connection.From, host))
                .Select(connection => connection.To)
                .Where(node => node != null)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x);
        }

        private static IEnumerable<AutomationGraphNode> GeometryBodyChildrenForHost(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            if (graph == null || host == null || !AcceptsControlBody(host.Kind))
                return Enumerable.Empty<AutomationGraphNode>();

            return graph.Nodes
                .Where(node => node != null && !ReferenceEquals(node, host) && IsBodyFlowNode(node))
                .Where(node => ControlBodyRect(host.Rect, host.Kind).Contains(node.Rect.center))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x);
        }

        private static AutomationGraphNode SnappedBodyParent(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null || !IsBodyFlowNode(node))
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Body)
                .FirstOrDefault(connection => ReferenceEquals(connection.To, node))
                ?.From;
        }

        private static AutomationGraphNode GeometrySnappedBodyParent(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null || !IsBodyFlowNode(node))
                return null;

            foreach (AutomationGraphNode host in graph.Nodes)
            {
                if (host == null ||
                    ReferenceEquals(host, node) ||
                    !AcceptsControlBody(host.Kind))
                {
                    continue;
                }

                if (ControlBodyRect(host.Rect, host.Kind).Contains(node.Rect.center))
                    return host;
            }

            return null;
        }

        private static AutomationValueSlotKind SnappedValueSlotKind(
            AutomationGraph graph,
            AutomationGraphNode value,
            AutomationGraphNode host)
        {
            if (graph == null || value == null || host == null)
                return AutomationValueSlotKind.Pass;

            foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, host))
            {
                if (ReferenceEquals(SnappedValueForHost(graph, host, slotKind), value))
                    return slotKind;
            }

            return AutomationValueSlotKind.Pass;
        }

        private static void DrawCanvasLine(Vector2 start, Vector2 end, float width)
        {
            if (Mathf.Abs(start.x - end.x) < 0.001f)
            {
                float y = Mathf.Min(start.y, end.y);
                GUI.DrawTexture(new Rect(start.x - width * 0.5f, y, width, Mathf.Abs(end.y - start.y) + width), Texture2D.whiteTexture);
                return;
            }

            if (Mathf.Abs(start.y - end.y) < 0.001f)
            {
                float x = Mathf.Min(start.x, end.x);
                GUI.DrawTexture(new Rect(x, start.y - width * 0.5f, Mathf.Abs(end.x - start.x) + width, width), Texture2D.whiteTexture);
            }
        }

        private static void DrawValueSocketHint(
            Rect socket,
            bool filled,
            string label)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = filled
                    ? new Color(0.06f, 0.28f, 0.32f, 0.72f)
                    : new Color(0f, 0.07f, 0.085f, 0.82f);
                GUI.DrawTexture(socket, Texture2D.whiteTexture);
                GUI.color = filled
                    ? new Color(0.55f, 1f, 0.8f, 0.88f)
                    : new Color(DecorationEditorTheme.Cyan.r, DecorationEditorTheme.Cyan.g, DecorationEditorTheme.Cyan.b, 0.64f);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(2f));
                GUI.DrawTexture(new Rect(socket.x, socket.y, socket.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(socket.x, socket.yMax - line, socket.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(socket.x, socket.y, line, socket.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(socket.xMax - line, socket.y, line, socket.height), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(
                    new Rect(socket.x + EsuHudLayout.Scale(6f), socket.y + EsuHudLayout.Scale(3f), socket.width - EsuHudLayout.Scale(12f), EsuHudLayout.Scale(18f)),
                    label,
                    DecorationEditorTheme.Mini);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static void DrawControlBodyHint(
            Rect body,
            bool filled,
            string label)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = filled
                    ? new Color(0f, 0.13f, 0.15f, 0.76f)
                    : new Color(0f, 0.06f, 0.075f, 0.72f);
                GUI.DrawTexture(body, Texture2D.whiteTexture);
                GUI.color = new Color(DecorationEditorTheme.Cyan.r, DecorationEditorTheme.Cyan.g, DecorationEditorTheme.Cyan.b, filled ? 0.82f : 0.44f);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(2f));
                GUI.DrawTexture(new Rect(body.x, body.y, body.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(body.x, body.yMax - line, body.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(body.x, body.y, line, body.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(body.xMax - line, body.y, line, body.height), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(
                    new Rect(body.x + EsuHudLayout.Scale(8f), body.y + EsuHudLayout.Scale(4f), body.width - EsuHudLayout.Scale(16f), EsuHudLayout.Scale(18f)),
                    label,
                    DecorationEditorTheme.Mini);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private void DrawGraphNodeCard(
            AutomationGraph graph,
            AutomationGraphNode node,
            IReadOnlyList<AutomationGraphNode> executionFlow,
            IReadOnlyList<List<AutomationGraphNode>> executionChains)
        {
            Rect rect = node.Rect;
            bool selected = ReferenceEquals(node, graph.SelectedNode);
            DrawAutomationBlockShape(rect, node.Kind, selected, compact: false);
            if (AcceptsControlBody(node.Kind))
                DrawControlBodyHint(ControlBodyRect(rect, node.Kind), BodyChildrenForHost(graph, node).Any(), ControlBodyLabel(node.Kind));
            if (AcceptsValueSlot(node.Kind))
            {
                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, node))
                {
                    if (ShouldDrawValueSocketHint(graph, node, slotKind))
                    {
                        DrawValueSocketHint(
                            ValueSlotRect(rect, node.Kind, slotKind),
                            SnappedValueForHost(graph, node, slotKind) != null,
                            ValueSlotLabel(node.Kind, slotKind));
                    }
                }
            }
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 8f);
            Rect close = GraphNodeCloseRect(rect);
            bool foregroundOwnsInput = GraphForegroundMenuOpen() || GraphPointerInputActive();
            bool canUseNodeControls = !foregroundOwnsInput;
            if (canUseNodeControls &&
                (TryConsumeGraphLeftMouseUp(close) ||
                 GUI.Button(close, new GUIContent("X", "Remove this native graph component."), DecorationEditorTheme.Button)))
            {
                RemoveGraphNode(graph, node);
                MarkAutomationDirty();
                return;
            }

            _graphSlotConsumedInput = false;
            DrawBlockSentence(rect, node, compact: false, graph);
            DrawExecutionOrderBadge(rect, node, executionFlow);
            if (!foregroundOwnsInput)
                DrawReadinessBadge(rect, graph, node, executionChains);

            if (foregroundOwnsInput)
                return;

            Event current = Event.current;
            if (current == null)
                return;

            if (IsMouseInsideGraphSlotDropdown(current))
                return;

            Vector2 graphMouse = CurrentGraphMousePosition(current);
            if (IsMouseInsideGraphReadinessPopover(current))
                return;
            if (IsMouseInsideGraphContextMenu(current))
                return;
            if (_graphContextMenuNodeId != 0 &&
                current.isMouse &&
                current.type == EventType.MouseDown)
            {
                CloseGraphContextMenu();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (close.Contains(graphMouse))
                return;

            if (_graphSlotConsumedInput &&
                current.isMouse &&
                rect.Contains(graphMouse))
            {
                graph.SelectedNodeId = node.Id;
                _draggingNodeId = 0;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                rect.Contains(graphMouse))
            {
                if (graph.SelectedNodeId != node.Id || _closedQuickChoicesNodeId == node.Id)
                    _closedQuickChoicesNodeId = 0;
                graph.SelectedNodeId = node.Id;
                _draggingNodeId = node.Id;
                _nodeDragStartMouse = graphMouse;
                _nodeDragStartRect = node.Rect;
                CloseGraphContextMenu();
                BeginCanvasInteraction(AutomationCanvasInteractionKind.NodeDrag, 0, current.mousePosition);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag &&
                _draggingNodeId == node.Id &&
                _canvasInteraction == AutomationCanvasInteractionKind.NodeDrag)
            {
                Vector2 delta = graphMouse - _nodeDragStartMouse;
                if (delta.sqrMagnitude >= CanvasDragThreshold * CanvasDragThreshold)
                    _canvasInteractionMoved = true;
                Rect next = _nodeDragStartRect;
                next.x = Mathf.Max(0f, _nodeDragStartRect.x + delta.x);
                next.y = Mathf.Max(0f, _nodeDragStartRect.y + delta.y);
                node.Rect = next;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp &&
                _draggingNodeId == node.Id)
            {
                FinishNodeDrag(graph, current);
            }
        }

        private static void DrawExecutionOrderBadge(
            Rect rect,
            AutomationGraphNode node,
            IReadOnlyList<AutomationGraphNode> executionFlow)
        {
            int index = FlowNodeIndex(executionFlow, node);
            if (index < 0)
                return;

            Rect badge = new Rect(
                rect.xMax - EsuHudLayout.Scale(72f),
                rect.y + EsuHudLayout.Scale(2f),
                EsuHudLayout.Scale(34f),
                EsuHudLayout.Scale(20f));
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0f, 0.08f, 0.09f, 0.86f * previous.a);
                GUI.DrawTexture(badge, Texture2D.whiteTexture);
                GUI.color = new Color(DecorationEditorTheme.Cyan.r, DecorationEditorTheme.Cyan.g, DecorationEditorTheme.Cyan.b, 0.92f * previous.a);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(1f));
                GUI.DrawTexture(new Rect(badge.x, badge.y, badge.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.x, badge.yMax - line, badge.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.x, badge.y, line, badge.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.xMax - line, badge.y, line, badge.height), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(
                    EsuHudLayout.PanelInnerRect(badge, 2f),
                    "#" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.Mini);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private void DrawReadinessBadge(
            Rect rect,
            AutomationGraph graph,
            AutomationGraphNode node,
            IReadOnlyList<List<AutomationGraphNode>> executionChains)
        {
            List<string> issues = NativeNodeReadinessIssues(graph, node).ToList();
            if (issues.Count == 0)
                return;

            Rect badge = new Rect(
                rect.xMax - EsuHudLayout.Scale(108f),
                rect.y + EsuHudLayout.Scale(2f),
                EsuHudLayout.Scale(28f),
                EsuHudLayout.Scale(20f));
            string tooltip = string.Join("\n", issues.Take(3).ToArray());
            if (issues.Count > 3)
            {
                tooltip += "\n+" + (issues.Count - 3).ToString(CultureInfo.InvariantCulture) + " more issue" +
                           (issues.Count == 4 ? string.Empty : "s");
            }

            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0.82f, 0.36f, 0.05f, 0.9f * previous.a);
                GUI.DrawTexture(badge, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.78f, 0.24f, 0.95f * previous.a);
                float line = Mathf.Max(1f, EsuHudLayout.Scale(1f));
                GUI.DrawTexture(new Rect(badge.x, badge.y, badge.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.x, badge.yMax - line, badge.width, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.x, badge.y, line, badge.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(badge.xMax - line, badge.y, line, badge.height), Texture2D.whiteTexture);
                GUI.color = previous;
                GUIStyle style = new GUIStyle(DecorationEditorTheme.Mini)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                MarkGraphSlotInputIfMouseInside(badge);
                bool clicked = GUI.Button(badge, GUIContent.none, GUIStyle.none) ||
                               TryConsumeGraphLeftMouseUp(badge);
                GUI.Label(EsuHudLayout.PanelInnerRect(badge, 2f), new GUIContent("!", tooltip), style);
                EsuCursorTooltip.Register(badge, tooltip);
                if (clicked)
                    OpenGraphReadinessPopover(node, badge);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private void OpenGraphReadinessPopover(
            AutomationGraphNode node,
            Rect anchorRect)
        {
            if (node == null)
            {
                CloseGraphReadinessPopover();
                return;
            }

            _graphReadinessPopoverNodeId = node.Id;
            _graphReadinessPopoverAnchorRect = anchorRect;
            _graphReadinessPopoverScroll = Vector2.zero;
            CloseGraphContextMenu();
            CloseGraphSlotMenu();
            CloseGraphPropertyPicker();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private void CloseGraphReadinessPopover()
        {
            _graphReadinessPopoverNodeId = 0;
            _graphReadinessPopoverRect = Rect.zero;
            _graphReadinessPopoverAnchorRect = Rect.zero;
            _graphReadinessPopoverScroll = Vector2.zero;
        }

        private void DrawGraphReadinessPopover(
            AutomationGraph graph,
            Rect viewRect)
        {
            if (_graphReadinessPopoverNodeId == 0)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphReadinessPopoverNodeId);
            if (node == null)
            {
                CloseGraphReadinessPopover();
                return;
            }

            List<string> issues = NativeNodeReadinessIssues(graph, node).ToList();
            if (issues.Count == 0)
            {
                CloseGraphReadinessPopover();
                return;
            }

            Rect rect = GraphReadinessPopoverRect(_graphReadinessPopoverAnchorRect, viewRect, issues.Count);
            _graphReadinessPopoverRect = rect;
            MarkGraphDropdownInputIfMouseInside(rect);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 6f);
            Rect title = new Rect(inner.x, inner.y, inner.width - EsuHudLayout.Scale(30f), EsuHudLayout.Scale(24f));
            Rect close = new Rect(inner.xMax - EsuHudLayout.Scale(26f), inner.y, EsuHudLayout.Scale(26f), EsuHudLayout.Scale(22f));
            GUI.Label(title, "Block Issue", DecorationEditorTheme.SubHeader);
            if (GUI.Button(close, new GUIContent("X", "Close issue details."), DecorationEditorTheme.Button) ||
                TryConsumeGraphLeftMouseUp(close))
            {
                CloseGraphReadinessPopover();
                return;
            }

            Rect scrollRect = new Rect(
                inner.x,
                title.yMax + EsuHudLayout.Scale(4f),
                inner.width,
                Mathf.Max(1f, inner.yMax - title.yMax - EsuHudLayout.Scale(4f)));
            float rowHeight = EsuHudLayout.Scale(48f);
            Rect view = new Rect(
                0f,
                0f,
                Mathf.Max(1f, scrollRect.width - EsuHudLayout.Scale(18f)),
                Mathf.Max(scrollRect.height, issues.Count * rowHeight));
            _graphReadinessPopoverScroll = GUI.BeginScrollView(scrollRect, _graphReadinessPopoverScroll, view);
            for (int index = 0; index < issues.Count; index++)
            {
                Rect row = new Rect(0f, index * rowHeight, view.width, rowHeight - EsuHudLayout.Scale(4f));
                GUI.Label(row, issues[index], DecorationEditorTheme.MiniWrap);
            }

            GUI.EndScrollView();

            Event current = Event.current;
            if (current != null &&
                current.isMouse &&
                current.type == EventType.MouseDown &&
                !_graphReadinessPopoverRect.Contains(CurrentGraphMousePosition(current)) &&
                !_graphReadinessPopoverAnchorRect.Contains(CurrentGraphMousePosition(current)))
            {
                CloseGraphReadinessPopover();
                current.Use();
            }
        }

        private static Rect GraphReadinessPopoverRect(
            Rect anchorRect,
            Rect viewRect,
            int issueCount)
        {
            float width = EsuHudLayout.Scale(GraphReadinessPopoverWidth);
            float height = Mathf.Min(
                EsuHudLayout.Scale(GraphReadinessPopoverMaxHeight),
                EsuHudLayout.Scale(48f + Math.Max(1, issueCount) * 48f));
            Rect rect = new Rect(
                anchorRect.x,
                anchorRect.yMax + EsuHudLayout.Scale(8f),
                width,
                height);
            if (rect.yMax > viewRect.yMax - EsuHudLayout.Scale(8f))
                rect.y = anchorRect.y - height - EsuHudLayout.Scale(8f);
            rect.x = Mathf.Clamp(rect.x, viewRect.x + EsuHudLayout.Scale(8f), viewRect.xMax - width - EsuHudLayout.Scale(8f));
            rect.y = Mathf.Clamp(rect.y, viewRect.y + EsuHudLayout.Scale(8f), viewRect.yMax - height - EsuHudLayout.Scale(8f));
            return rect;
        }

        private static Rect GraphNodeCloseRect(Rect nodeRect)
        {
            Rect inner = EsuHudLayout.PanelInnerRect(nodeRect, 8f);
            return new Rect(
                inner.xMax - EsuHudLayout.Scale(24f),
                inner.y,
                EsuHudLayout.Scale(24f),
                EsuHudLayout.Scale(22f));
        }

        private static Rect GraphQuickChoicesRect(Rect nodeRect) =>
            new Rect(
                nodeRect.xMax + EsuHudLayout.Scale(10f),
                nodeRect.y,
                EsuHudLayout.Scale(236f),
                EsuHudLayout.Scale(242f));

        private void DrawGraphNodeQuickChoices(
            AutomationGraphNode node,
            Rect nodeRect)
        {
            if (node == null ||
                _closedQuickChoicesNodeId == node.Id ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return;
            }

            Rect rect = GraphQuickChoicesRect(nodeRect);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 6f);
            Rect closeRect = new Rect(
                inner.xMax - EsuHudLayout.Scale(28f),
                inner.y,
                EsuHudLayout.Scale(28f),
                EsuHudLayout.Scale(22f));
            GUILayout.BeginArea(inner);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Block Choices", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            bool close = GUILayout.Button(
                new GUIContent("X", "Close block choices."),
                DecorationEditorTheme.Button,
                GUILayout.Width(EsuHudLayout.Scale(28f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            close |= TryConsumeGraphLeftMouseUp(closeRect);
            if (close)
            {
                _closedQuickChoicesNodeId = node.Id;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label(node.Kind == AutomationNodeKind.InputGetter ? "Input Target" : "Output Target", DecorationEditorTheme.Mini);
            List<AutomationLink> targetChoices = TargetChoicesForNode(node).Take(3).ToList();
            if (targetChoices.Count == 0)
            {
                GUILayout.Label("Link a craft block first.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                foreach (AutomationLink link in targetChoices)
                {
                    string targetName = LinkChoiceTargetName(node, link);
                    bool selected = string.Equals(targetName, BlockTargetLabel(node), StringComparison.OrdinalIgnoreCase);
                    if (!GUILayout.Button(
                            ShortText(targetName, 28),
                            DecorationEditorTheme.ToolButton(selected),
                            GUILayout.Height(EsuHudLayout.Scale(22f))))
                    {
                        continue;
                    }

                    if (TryApplyNativeLinkTarget(node, link, out string message))
                    {
                        MarkAutomationDirty();
                        InfoStore.Add(message);
                    }
                    else
                    {
                        InfoStore.Add(message ?? "Automation Builder could not assign that linked target.");
                    }
                }
            }

            GUILayout.Label("Native Property", DecorationEditorTheme.Mini);
            List<string> propertyChoices = NativePropertyOptionsForNode(node)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Take(4)
                .ToList();
            if (propertyChoices.Count == 0)
            {
                GUILayout.Label("Choose a target first.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                foreach (string option in propertyChoices)
                {
                    bool selected = MatchesPropertyText(node.Property, option);
                    if (!GUILayout.Button(
                            ShortText(option, 30),
                            DecorationEditorTheme.ToolButton(selected),
                            GUILayout.Height(EsuHudLayout.Scale(22f))))
                    {
                        continue;
                    }

                    ApplyNativeNodeEdits(node, node.Label, option, node.ValueText);
                    MarkAutomationDirty();
                }
            }

            GUILayout.EndArea();
        }

        private bool TrySnapGraphNode(AutomationGraph graph, AutomationGraphNode node)
        {
            if (graph == null || node == null || node.Kind == AutomationNodeKind.Comment)
                return false;

            if (CanProduceValueBlock(node.Kind) &&
                TrySnapValueNode(graph, node))
            {
                return true;
            }

            if (DrawsAsValueBlock(node.Kind, node.Rect))
                return TrySnapValueNode(graph, node);

            if (TrySnapBodyNode(graph, node))
                return true;

            if (!CanStackSnapNode(node))
                return false;

            if (!TryFindStackSnapTarget(graph, node, out AutomationGraphNode best, out bool snapBelow))
                return false;

            Rect next = node.Rect;
            next.x = best.Rect.x;
            next.y = snapBelow
                ? best.Rect.yMax - EsuHudLayout.Scale(2f)
                : Mathf.Max(0f, best.Rect.y - node.Rect.height + EsuHudLayout.Scale(2f));
            node.Rect = next;
            return true;
        }

        private bool TrySnapBodyNode(AutomationGraph graph, AutomationGraphNode node)
        {
            if (graph == null || node == null || !IsBodyFlowNode(node))
                return false;

            float threshold = EsuHudLayout.Scale(42f);
            AutomationGraphNode best = null;
            Rect bestBody = Rect.zero;
            float bestDistance = threshold;
            foreach (AutomationGraphNode host in graph.Nodes)
            {
                if (host == null ||
                    ReferenceEquals(host, node) ||
                    !AcceptsControlBody(host.Kind))
                {
                    continue;
                }

                Rect body = ControlBodyRect(host.Rect, host.Kind);
                Rect padded = ExpandRect(body, threshold);
                if (!padded.Contains(node.Rect.center))
                    continue;

                float distance = body.Contains(node.Rect.center)
                    ? 0f
                    : DistanceToRect(node.Rect.center, body);
                if (distance >= bestDistance)
                    continue;

                best = host;
                bestBody = body;
                bestDistance = distance;
            }

            if (best == null)
                return false;

            List<AutomationGraphNode> siblings = BodyChildrenForHost(graph, best)
                .Where(child => !ReferenceEquals(child, node))
                .ToList();
            float y = siblings.Count == 0
                ? bestBody.y + EsuHudLayout.Scale(24f)
                : siblings.Max(child => child.Rect.yMax) + EsuHudLayout.Scale(8f);
            node.Rect = new Rect(
                bestBody.x + EsuHudLayout.Scale(14f),
                y,
                GraphNodeWidthForKind(node.Kind),
                node.Rect.height);

            float requiredHeight = node.Rect.yMax - best.Rect.y + EsuHudLayout.Scale(24f);
            if (requiredHeight > best.Rect.height)
            {
                best.Rect = new Rect(best.Rect.x, best.Rect.y, best.Rect.width, requiredHeight);
                SyncNativeNodeRect(best);
            }

            return true;
        }

        private bool TrySnapValueNode(AutomationGraph graph, AutomationGraphNode node)
        {
            float threshold = EsuHudLayout.Scale(46f);
            AutomationGraphNode best = null;
            Rect bestSlot = Rect.zero;
            float bestDistance = threshold;
            foreach (AutomationGraphNode other in graph.Nodes)
            {
                if (other == null ||
                    ReferenceEquals(other, node) ||
                    !AcceptsValueSlot(other.Kind))
                {
                    continue;
                }

                foreach (AutomationValueSlotKind slotKind in ActiveValueSlotKinds(graph, other))
                {
                    if (!CanSnapIntoValueSlot(node, other.Kind, slotKind))
                        continue;

                    Rect slot = ValueSlotRect(other.Rect, other.Kind, slotKind);
                    float distance = Vector2.Distance(node.Rect.center, slot.center);
                    if (distance >= bestDistance)
                        continue;

                    best = other;
                    bestSlot = slot;
                    bestDistance = distance;
                }
            }

            if (best == null)
                return false;

            node.Rect = new Rect(
                Mathf.Max(0f, bestSlot.x),
                Mathf.Max(0f, bestSlot.y),
                bestSlot.width,
                bestSlot.height);
            return true;
        }

        private void DrawAutomationBlockShape(
            Rect rect,
            AutomationNodeKind kind,
            bool selected,
            bool compact)
        {
            Color color = NodeColor(kind);
            Color oldColor = GUI.color;
            float alpha = compact ? 0.86f : 0.94f;
            GUI.color = new Color(color.r, color.g, color.b, alpha * oldColor.a);

            if (DrawsAsValueBlock(kind, rect))
            {
                float cap = Mathf.Min(rect.height * 0.5f, EsuHudLayout.Scale(26f));
                GUI.DrawTexture(new Rect(rect.x + cap * 0.5f, rect.y, rect.width - cap, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y + cap * 0.28f, cap, rect.height - cap * 0.56f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - cap, rect.y + cap * 0.28f, cap, rect.height - cap * 0.56f), Texture2D.whiteTexture);
            }
            else
            {
                float tabWidth = EsuHudLayout.Scale(50f);
                float tabHeight = EsuHudLayout.Scale(12f);
                float notchWidth = EsuHudLayout.Scale(46f);
                float notchHeight = EsuHudLayout.Scale(10f);
                GUI.DrawTexture(new Rect(rect.x, rect.y + tabHeight * 0.5f, rect.width, rect.height - tabHeight * 0.5f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x + EsuHudLayout.Scale(22f), rect.y, tabWidth, tabHeight), Texture2D.whiteTexture);
                GUI.color = new Color(0f, 0.075f, 0.085f, 0.88f * oldColor.a);
                GUI.DrawTexture(new Rect(rect.x + EsuHudLayout.Scale(24f), rect.yMax - notchHeight, notchWidth, notchHeight), Texture2D.whiteTexture);
                if (BlockShape(kind) == AutomationBlockShape.Control)
                {
                    GUI.DrawTexture(new Rect(
                        rect.x + EsuHudLayout.Scale(18f),
                        rect.y + rect.height * 0.46f,
                        rect.width - EsuHudLayout.Scale(42f),
                        EsuHudLayout.Scale(18f)),
                        Texture2D.whiteTexture);
                }
            }

            GUI.color = oldColor;
            if (selected)
            {
                DrawCyanLine(new Rect(rect.x, rect.y, rect.width, Mathf.Max(2f, EsuHudLayout.Scale(2f))));
                DrawCyanLine(new Rect(rect.x, rect.yMax - Mathf.Max(2f, EsuHudLayout.Scale(2f)), rect.width, Mathf.Max(2f, EsuHudLayout.Scale(2f))));
            }
        }

        private void DrawBlockSentence(
            Rect rect,
            AutomationGraphNode node,
            bool compact,
            AutomationGraph graph = null)
        {
            Rect inner = EsuHudLayout.PanelInnerRect(rect, compact ? 8f : 10f);
            bool valueFootprint = DrawsAsValueBlock(node.Kind, rect);
            bool selected = IsSelectedGraphNode(graph, node) && !GraphForegroundMenuOpen();
            float y = inner.y + (valueFootprint ? 0f : EsuHudLayout.Scale(4f));
            if (!compact &&
                valueFootprint &&
                node.Kind == AutomationNodeKind.InputGetter)
            {
                DrawCompactReadValueSentence(inner, node, selected);
                return;
            }

            Rect title = new Rect(inner.x, y, inner.width - EsuHudLayout.Scale(compact ? 4f : 92f), EsuHudLayout.Scale(compact ? 22f : 24f));
            string titleText = BlockSentenceTitle(node);
            GUI.Label(title, titleText, compact ? DecorationEditorTheme.Mini : DecorationEditorTheme.Body);
            RegisterFullTextTooltip(title, BlockSentenceTitleTooltip(node), titleText);
            y = title.yMax + EsuHudLayout.Scale(4f);

            if (compact)
                return;

            switch (node.Kind)
            {
                case AutomationNodeKind.InputGetter:
                    if (DrawBlockSlot(
                            ref y,
                            inner,
                            "target",
                            BlockTitleTargetLabel(node),
                            selected,
                            TargetSlotTooltip(node, input: true)))
                    {
                        OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Target, _lastDrawnGraphSlotRect);
                    }

                    if (DrawBlockSlot(
                            ref y,
                            inner,
                            "property",
                            DisplayNodeProperty(node),
                            selected,
                            "Choose native readable property."))
                    {
                        OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Property, _lastDrawnGraphSlotRect);
                    }

                    DrawBlockSlot(ref y, inner, "current", InputGetterCurrentValueText(node));
                    break;
                case AutomationNodeKind.OutputSetter:
                    AutomationGraphNode outputValue = graph == null ? null : SnappedValueForHost(graph, node);
                    if (DrawBlockSlot(
                            ref y,
                            inner,
                            "target",
                            BlockTitleTargetLabel(node),
                            selected,
                            TargetSlotTooltip(node, input: false)))
                    {
                        OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Target, _lastDrawnGraphSlotRect);
                    }

                    if (DrawBlockSlot(
                            ref y,
                            inner,
                            "property",
                            DisplayNodeProperty(node),
                            selected,
                            "Choose native writable property."))
                    {
                        OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Property, _lastDrawnGraphSlotRect);
                    }

                    DrawBlockSlot(ref y, inner, "current", OutputSetterCurrentValueText(node));
                    DrawBlockSlot(ref y, inner, "source", OutputSetterSourceText(graph, node, outputValue));
                    break;
                case AutomationNodeKind.Forever:
                    DrawBlockSlot(ref y, inner, "forever", "native board evaluates continuously");
                    DrawBlockSlot(ref y, inner, "body", "stack actions below");
                    break;
                case AutomationNodeKind.IfCondition:
                    AutomationGraphNode conditionThenValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode conditionElseValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Else);
                    DrawBlockSlot(ref y, inner, "condition", ConditionInputBlockText(graph, node));
                    string conditionIncomingCurrent = IncomingSignalCurrentText(graph, node);
                    if (!string.IsNullOrWhiteSpace(conditionIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", conditionIncomingCurrent);
                    DrawBlockSlot(ref y, inner, "then", conditionThenValue == null ? "snap a value block" : BlockValueText(conditionThenValue));
                    if (conditionElseValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Else), SwitchFailValueSlotText(node), selected, "Edit native Switch constant fail value.", nextValue => ApplySwitchFailValueSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Else), BlockValueText(conditionElseValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", "then/else value drives next action");
                    break;
                case AutomationNodeKind.IfLessThan:
                    AutomationGraphNode thresholdValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Threshold);
                    AutomationGraphNode thenValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode elseValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Else);
                    DrawBlockSlot(ref y, inner, "input", IncomingSignalText(graph, node));
                    string incomingCurrent = IncomingSignalCurrentText(graph, node);
                    if (!string.IsNullOrWhiteSpace(incomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", incomingCurrent);

                    if (thresholdValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Threshold), SwitchThresholdSlotText(node), selected, "Edit native Switch constant threshold.", nextValue => ApplySwitchThresholdSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Threshold), BlockValueText(thresholdValue));
                    }

                    DrawBlockSlot(ref y, inner, "then", thenValue == null ? "snap a value block" : BlockValueText(thenValue));
                    if (elseValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Else), SwitchFailValueSlotText(node), selected, "Edit native Switch constant fail value.", nextValue => ApplySwitchFailValueSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Else), BlockValueText(elseValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", "then/else value drives next action");
                    break;
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    AutomationGraphNode logicSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    DrawBlockSlot(ref y, inner, LogicFirstInputLabel(node.Kind), logicSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(logicSourceValue));
                    string logicIncomingCurrent = logicSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(logicSourceValue);
                    if (!string.IsNullOrWhiteSpace(logicIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", logicIncomingCurrent);
                    if (node.Kind != AutomationNodeKind.LogicNot)
                    {
                        AutomationGraphNode logicBValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.LogicB);
                        DrawBlockSlot(ref y, inner, "b", logicBValue == null ? "snap value block" : BlockValueText(logicBValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", LogicOutputText(node.Kind));
                    break;
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    AutomationGraphNode thresholdSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode compareThresholdValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Threshold);
                    DrawBlockSlot(ref y, inner, "input", thresholdSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(thresholdSourceValue));
                    string thresholdIncomingCurrent = thresholdSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(thresholdSourceValue);
                    if (!string.IsNullOrWhiteSpace(thresholdIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", thresholdIncomingCurrent);
                    if (compareThresholdValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Threshold), ThresholdSlotText(node), selected, "Edit native FuzzyThreshold constant threshold.", nextValue => ApplyThresholdSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Threshold), BlockValueText(compareThresholdValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", ThresholdOutputText(node.Kind));
                    break;
                case AutomationNodeKind.Constant:
                    DrawEditableValueSlot(ref y, inner, node, "number", node.ValueText, selected, "Edit native ConstantInput value.");
                    break;
                case AutomationNodeKind.Random:
                    DrawEditableValueSlot(ref y, inner, node, "range", RandomRangeSlotText(node), selected, "Edit native RandomInput range.", nextValue => ApplyRandomRangeSlotEdit(node, nextValue));
                    DrawBlockSlot(ref y, inner, "output", "random value");
                    break;
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                    AutomationValueSlotKind operandSlotKind = MathOperandSlotKind(node.Kind);
                    AutomationGraphNode addSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode operandValue = graph == null ? null : SnappedValueForHost(graph, node, operandSlotKind);
                    DrawBlockSlot(ref y, inner, "input", addSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(addSourceValue));
                    string addIncomingCurrent = addSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(addSourceValue);
                    if (!string.IsNullOrWhiteSpace(addIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", addIncomingCurrent);
                    if (operandValue == null)
                    {
                        string operandLabel = MathOperandLabel(node.Kind);
                        DrawEditableValueSlot(
                            ref y,
                            inner,
                            node,
                            operandLabel,
                            MathOperandSlotText(node),
                            selected,
                            "Edit native Evaluator " + operandLabel + ".",
                            nextValue => ApplyMathOperandSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, MathOperandLabel(node.Kind), BlockValueText(operandValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", MathOutputText(node.Kind));
                    break;
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    AutomationGraphNode maxMinSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode maxMinBValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.MathB);
                    DrawBlockSlot(ref y, inner, "a", maxMinSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(maxMinSourceValue));
                    string maxMinIncomingCurrent = maxMinSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(maxMinSourceValue);
                    if (!string.IsNullOrWhiteSpace(maxMinIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", maxMinIncomingCurrent);
                    DrawBlockSlot(ref y, inner, "b", maxMinBValue == null ? "snap value block" : BlockValueText(maxMinBValue));
                    DrawBlockSlot(ref y, inner, "output", MaxMinOutputText(node.Kind));
                    break;
                case AutomationNodeKind.Clamp:
                    AutomationGraphNode clampSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode minValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Min);
                    AutomationGraphNode maxValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Max);
                    DrawBlockSlot(ref y, inner, "input", clampSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(clampSourceValue));
                    string clampIncomingCurrent = clampSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(clampSourceValue);
                    if (!string.IsNullOrWhiteSpace(clampIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", clampIncomingCurrent);
                    if (minValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Min), ClampMinSlotText(node), selected, "Edit native Clamp constant minimum.", nextValue => ApplyClampMinSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Min), BlockValueText(minValue));
                    }

                    if (maxValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Max), ClampMaxSlotText(node), selected, "Edit native Clamp constant maximum.", nextValue => ApplyClampMaxSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Max), BlockValueText(maxValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", "limited value drives next action");
                    break;
                case AutomationNodeKind.Smooth:
                    AutomationGraphNode smoothSourceValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
                    AutomationGraphNode secondsValue = graph == null ? null : SnappedValueForHost(graph, node, AutomationValueSlotKind.Seconds);
                    DrawBlockSlot(ref y, inner, "input", smoothSourceValue == null ? IncomingSignalText(graph, node) : BlockValueText(smoothSourceValue));
                    string smoothIncomingCurrent = smoothSourceValue == null
                        ? IncomingSignalCurrentText(graph, node)
                        : ValueSignalCurrentText(smoothSourceValue);
                    if (!string.IsNullOrWhiteSpace(smoothIncomingCurrent))
                        DrawBlockSlot(ref y, inner, "now", smoothIncomingCurrent);
                    if (secondsValue == null)
                    {
                        DrawEditableValueSlot(ref y, inner, node, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Seconds), SmoothSecondsSlotText(node), selected, "Edit native Delay constant seconds.", nextValue => ApplySmoothSecondsSlotEdit(node, nextValue));
                    }
                    else
                    {
                        DrawBlockSlot(ref y, inner, ValueSlotLabel(node.Kind, AutomationValueSlotKind.Seconds), BlockValueText(secondsValue));
                    }

                    DrawBlockSlot(ref y, inner, "output", "delayed value drives next action");
                    break;
                case AutomationNodeKind.Comment:
                    DrawEditableValueSlot(ref y, inner, node, "note", string.IsNullOrWhiteSpace(node.ValueText) ? node.Label : node.ValueText, selected, "Edit native Comment text.");
                    break;
            }
        }

        private void DrawEditableValueSlot(
            ref float y,
            Rect inner,
            AutomationGraphNode node,
            string label,
            string value,
            bool editable,
            string tooltip,
            Action<string> applyValue = null)
        {
            if (!editable)
            {
                DrawBlockSlot(ref y, inner, label, value);
                return;
            }

            float height = EsuHudLayout.Scale(20f);
            Rect labelRect = new Rect(inner.x, y, EsuHudLayout.Scale(70f), height);
            Rect valueRect = new Rect(labelRect.xMax + EsuHudLayout.Scale(6f), y, Mathf.Max(1f, inner.xMax - labelRect.xMax - EsuHudLayout.Scale(6f)), height);
            GUI.Label(labelRect, label, DecorationEditorTheme.Mini);
            MarkGraphSlotInputIfMouseInside(valueRect);
            string controlName = "ESU_AB_value_" +
                                 (node?.Id.ToString(CultureInfo.InvariantCulture) ?? "node") +
                                 "_" +
                                 SanitizeControlName(label);
            GUI.SetNextControlName(controlName);
            string nextValue = GUI.TextField(valueRect, value ?? string.Empty, DecorationEditorTheme.TextField);
            if (!string.Equals(nextValue, value ?? string.Empty, StringComparison.Ordinal))
            {
                if (applyValue == null)
                    ApplyNativeNodeEdits(node, node.Label, node.Property, nextValue);
                else
                    applyValue(nextValue);
                MarkAutomationDirty();
            }

            if (!string.IsNullOrWhiteSpace(tooltip))
                EsuCursorTooltip.Register(valueRect, tooltip);

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return ||
                 current.keyCode == KeyCode.KeypadEnter) &&
                string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }

            y += height + EsuHudLayout.Scale(3f);
        }

        private static string SanitizeControlName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "value";

            var builder = new StringBuilder(text.Length);
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }

        private void ApplySwitchThresholdSlotEdit(
            AutomationGraphNode node,
            string thresholdText)
        {
            ParseSwitchSlotValues(node, out float threshold, out float failValue);
            float nextThreshold = ParseFloat(thresholdText, threshold);
            ApplyNativeNodeEdits(node, node.Label, node.Property, SwitchValueText(nextThreshold, failValue));
        }

        private void ApplySwitchFailValueSlotEdit(
            AutomationGraphNode node,
            string failValueText)
        {
            ParseSwitchSlotValues(node, out float threshold, out float failValue);
            float nextFailValue = ParseFloat(failValueText, failValue);
            ApplyNativeNodeEdits(node, node.Label, node.Property, SwitchValueText(threshold, nextFailValue));
        }

        private void ApplyThresholdSlotEdit(
            AutomationGraphNode node,
            string thresholdText)
        {
            float nextThreshold = ParseFloat(thresholdText, ThresholdSlotValue(node, 10f));
            ApplyNativeNodeEdits(node, node.Label, node.Property, ThresholdValueText(nextThreshold));
        }

        private void ApplyMathAddAmountSlotEdit(
            AutomationGraphNode node,
            string amountText)
        {
            ApplyMathOperandSlotEdit(node, amountText);
        }

        private void ApplyMathOperandSlotEdit(
            AutomationGraphNode node,
            string operandText)
        {
            ApplyNativeNodeEdits(node, node.Label, node.Property, MathExpressionText(node.Kind, operandText));
        }

        private void ApplyRandomRangeSlotEdit(
            AutomationGraphNode node,
            string rangeText)
        {
            ParseRange(rangeText, 0f, 1f, out float lower, out float upper);
            ApplyNativeNodeEdits(node, node.Label, node.Property, RandomValueText(lower, upper));
        }

        private void ApplyClampMinSlotEdit(
            AutomationGraphNode node,
            string minText)
        {
            ParseClampSlotValues(node, out float lower, out float upper);
            float nextLower = ParseFloat(minText, lower);
            ApplyNativeNodeEdits(node, node.Label, node.Property, ClampValueText(nextLower, upper));
        }

        private void ApplyClampMaxSlotEdit(
            AutomationGraphNode node,
            string maxText)
        {
            ParseClampSlotValues(node, out float lower, out float upper);
            float nextUpper = ParseFloat(maxText, upper);
            ApplyNativeNodeEdits(node, node.Label, node.Property, ClampValueText(lower, nextUpper));
        }

        private void ApplySmoothSecondsSlotEdit(
            AutomationGraphNode node,
            string secondsText)
        {
            ApplyNativeNodeEdits(node, node.Label, node.Property, SmoothSecondsValueText(secondsText));
        }

        private static string SwitchThresholdSlotText(AutomationGraphNode node)
        {
            ParseSwitchSlotValues(node, out float threshold, out _);
            return FormatGraphFloat(threshold);
        }

        private static string SwitchThresholdBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode thresholdValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Threshold);
            return thresholdValue == null
                ? SwitchThresholdSlotText(node)
                : BlockValueText(thresholdValue);
        }

        private static string ConditionInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null)
                return "incoming signal";

            AutomationGraphNode previous = PreviousFlowNode(graph, node);
            return previous == null
                ? "incoming signal"
                : BlockValueText(previous);
        }

        private static string MathAddAmountBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return MathOperandBlockText(graph, node);
        }

        private static string MathAddInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return MathInputBlockText(graph, node);
        }

        private static string MathAddAmountSlotText(AutomationGraphNode node)
        {
            return MathOperandSlotText(node);
        }

        private static bool IsMathEvaluatorKind(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathAdd ||
                   kind == AutomationNodeKind.MathSubtract ||
                   kind == AutomationNodeKind.MathMultiply;
        }

        private static bool IsMaxMinKind(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMax ||
                   kind == AutomationNodeKind.MathMin;
        }

        private static string MathBlockProgramSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            string verb = MathVerb(node.Kind);
            string operand = MathOperandBlockText(graph, node);
            string input = MathInputBlockText(graph, node);
            return node.Kind == AutomationNodeKind.MathMultiply
                ? "multiply " + input + " by " + operand
                : verb + " " + operand + " " + MathPreposition(node.Kind) + " " + input;
        }

        private static string MathNativePlanSentence(AutomationGraphNode node)
        {
            return "transform by " + MathNoun(node.Kind) + " " + MathOperandSlotText(node);
        }

        private static string ThresholdBlockProgramSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return ThresholdVerb(node.Kind) + " " + ThresholdInputBlockText(graph, node) + " threshold " + ThresholdBlockText(graph, node);
        }

        private static string ThresholdNativePlanSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            string input = graph == null
                ? "input"
                : ThresholdInputBlockText(graph, node);
            string threshold = graph == null
                ? ThresholdSlotText(node)
                : ThresholdBlockText(graph, node);
            return "native threshold " + ThresholdSymbol(node.Kind) + " " + threshold + " from " + input;
        }

        private static string MaxMinBlockProgramSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return MaxMinVerb(node.Kind) + " of " + MaxMinFirstInputText(graph, node) + " and " + MaxMinSecondInputText(graph, node);
        }

        private static string MaxMinNativePlanSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            string first = graph == null
                ? "a"
                : MaxMinFirstInputText(graph, node);
            string second = graph == null
                ? "b"
                : MaxMinSecondInputText(graph, node);
            return "native " + MaxMinVerb(node.Kind) + " of " + first + " and " + second;
        }

        private static string LogicBlockProgramSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            string first = LogicFirstInputText(graph, node);
            if (node.Kind == AutomationNodeKind.LogicNot)
                return "not " + first;

            return LogicVerb(node.Kind) + " " + first + " with " + LogicSecondInputText(graph, node);
        }

        private static string LogicNativePlanSentence(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            string first = graph == null
                ? "input"
                : LogicFirstInputText(graph, node);
            if (node.Kind == AutomationNodeKind.LogicNot)
                return "logic NOT " + first;

            string second = graph == null
                ? "b"
                : LogicSecondInputText(graph, node);
            return "logic " + LogicGateName(node.Kind) + " " + first + " with " + second;
        }

        private static string LogicFirstInputText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string LogicSecondInputText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.LogicB);
            return inputValue == null
                ? "snap value block"
                : BlockValueText(inputValue);
        }

        private static bool IsLogicGateKind(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.LogicNot ||
                   kind == AutomationNodeKind.LogicAnd ||
                   kind == AutomationNodeKind.LogicOr ||
                   kind == AutomationNodeKind.LogicXor ||
                   kind == AutomationNodeKind.LogicNand ||
                   kind == AutomationNodeKind.LogicNor ||
                   kind == AutomationNodeKind.LogicXnor;
        }

        private static string LogicFirstInputLabel(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.LogicNot
                ? "input"
                : "a";
        }

        private static string LogicVerb(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.LogicNot:
                    return "not";
                case AutomationNodeKind.LogicOr:
                    return "or";
                case AutomationNodeKind.LogicXor:
                    return "xor";
                case AutomationNodeKind.LogicNand:
                    return "nand";
                case AutomationNodeKind.LogicNor:
                    return "nor";
                case AutomationNodeKind.LogicXnor:
                    return "xnor";
                default:
                    return "and";
            }
        }

        private static string LogicGateName(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.LogicNot:
                    return "NOT";
                case AutomationNodeKind.LogicOr:
                    return "OR";
                case AutomationNodeKind.LogicXor:
                    return "XOR";
                case AutomationNodeKind.LogicNand:
                    return "NAND";
                case AutomationNodeKind.LogicNor:
                    return "NOR";
                case AutomationNodeKind.LogicXnor:
                    return "XNOR";
                default:
                    return "AND";
            }
        }

        private static string LogicOutputText(AutomationNodeKind kind)
        {
            return LogicGateName(kind) + " result drives next action";
        }

        private static bool IsFuzzyThresholdKind(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.CompareAboveThreshold ||
                   kind == AutomationNodeKind.CompareBelowThreshold;
        }

        private static string ThresholdInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string ThresholdBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode thresholdValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Threshold);
            return thresholdValue == null
                ? ThresholdSlotText(node)
                : BlockValueText(thresholdValue);
        }

        private static string ThresholdSlotText(AutomationGraphNode node)
        {
            return FormatGraphFloat(ThresholdSlotValue(node, 10f));
        }

        private static float ThresholdSlotValue(
            AutomationGraphNode node,
            float fallback)
        {
            List<float> numbers = ExtractFloatTokens(node?.ValueText).ToList();
            return numbers.Count > 0
                ? numbers[0]
                : fallback;
        }

        private static string ThresholdValueText(float threshold)
        {
            return "threshold " + FormatGraphFloat(threshold);
        }

        private static string ThresholdVerb(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.CompareBelowThreshold
                ? "below"
                : "above";
        }

        private static string ThresholdSymbol(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.CompareBelowThreshold
                ? "<"
                : ">";
        }

        private static string ThresholdOutputText(AutomationNodeKind kind)
        {
            return "1 when " + ThresholdVerb(kind) + ", else 0";
        }

        private static string MaxMinFirstInputText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string MaxMinSecondInputText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.MathB);
            return inputValue == null
                ? "snap value block"
                : BlockValueText(inputValue);
        }

        private static string MaxMinVerb(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMin
                ? "minimum"
                : "maximum";
        }

        private static string MaxMinName(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMin
                ? "MIN"
                : "MAX";
        }

        private static string MaxMinOutputText(AutomationNodeKind kind)
        {
            return MaxMinVerb(kind) + " drives next action";
        }

        private static string MathInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string MathOperandBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode operandValue = SnappedValueForHost(graph, node, MathOperandSlotKind(node.Kind));
            return operandValue == null
                ? MathOperandSlotText(node)
                : BlockValueText(operandValue);
        }

        private static string MathOperandSlotText(AutomationGraphNode node)
        {
            return MathOperandFromExpression(node?.ValueText, MathOperator(node?.Kind ?? AutomationNodeKind.MathAdd), MathDefaultOperand(node?.Kind ?? AutomationNodeKind.MathAdd));
        }

        private static AutomationValueSlotKind MathOperandSlotKind(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMultiply
                ? AutomationValueSlotKind.Factor
                : AutomationValueSlotKind.Amount;
        }

        private static string MathOperandLabel(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMultiply
                ? "factor"
                : "amount";
        }

        private static string MathOutputText(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.MathSubtract:
                    return "difference drives next action";
                case AutomationNodeKind.MathMultiply:
                    return "product drives next action";
                default:
                    return "sum drives next action";
            }
        }

        private static string MathVerb(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.MathSubtract:
                    return "subtract";
                case AutomationNodeKind.MathMultiply:
                    return "multiply";
                default:
                    return "add";
            }
        }

        private static string MathPreposition(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathSubtract
                ? "from"
                : "to";
        }

        private static string MathNoun(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.MathSubtract:
                    return "subtraction";
                case AutomationNodeKind.MathMultiply:
                    return "multiplication";
                default:
                    return "addition";
            }
        }

        private static string MathOperator(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.MathSubtract:
                    return "-";
                case AutomationNodeKind.MathMultiply:
                    return "*";
                default:
                    return "+";
            }
        }

        private static string MathDefaultOperand(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMultiply
                ? "1"
                : "0";
        }

        private static string ClampInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string ClampMinBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode minValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Min);
            return minValue == null
                ? ClampMinSlotText(node)
                : BlockValueText(minValue);
        }

        private static string ClampMaxBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode maxValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Max);
            return maxValue == null
                ? ClampMaxSlotText(node)
                : BlockValueText(maxValue);
        }

        private static string ClampMinSlotText(AutomationGraphNode node)
        {
            ParseClampSlotValues(node, out float lower, out _);
            return FormatGraphFloat(lower);
        }

        private static string ClampMaxSlotText(AutomationGraphNode node)
        {
            ParseClampSlotValues(node, out _, out float upper);
            return FormatGraphFloat(upper);
        }

        private static string ClampRangeSlotText(AutomationGraphNode node)
        {
            ParseClampSlotValues(node, out float lower, out float upper);
            return FormatGraphFloat(lower) + ".." + FormatGraphFloat(upper);
        }

        private static string SmoothInputBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode inputValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass);
            return inputValue == null
                ? "incoming signal"
                : BlockValueText(inputValue);
        }

        private static string SmoothSecondsBlockText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode secondsValue = SnappedValueForHost(graph, node, AutomationValueSlotKind.Seconds);
            return secondsValue == null
                ? SmoothSecondsSlotText(node)
                : BlockValueText(secondsValue);
        }

        private static string SmoothSecondsSlotText(AutomationGraphNode node)
        {
            return FormatGraphFloat(ParseSeconds(node?.ValueText, 0.25f)) + "s";
        }

        private string IncomingSignalText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode previous = PreviousFlowNode(graph, node);
            return previous == null
                ? "snap a signal block above"
                : BlockValueText(previous);
        }

        private string IncomingSignalCurrentText(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            AutomationGraphNode previous = PreviousFlowNode(graph, node);
            if (previous == null)
                return null;

            return ValueSignalCurrentText(previous);
        }

        private string ValueSignalCurrentText(AutomationGraphNode node)
        {
            if (node == null)
                return null;

            if (node.Kind == AutomationNodeKind.InputGetter)
                return InputGetterCurrentValueText(node);

            if (node.Kind == AutomationNodeKind.Constant)
                return BlockValueText(node);

            return null;
        }

        private string OutputSetterSourceText(
            AutomationGraph graph,
            AutomationGraphNode node,
            AutomationGraphNode outputValue)
        {
            AutomationGraphNode previous = PreviousFlowNode(graph, node);
            if (previous != null)
                return "stack " + BlockValueText(previous);

            if (outputValue != null)
                return "socket " + BlockValueText(outputValue);

            return "snap value block or stack above";
        }

        private static string SwitchFailValueSlotText(AutomationGraphNode node)
        {
            ParseSwitchSlotValues(node, out _, out float failValue);
            return FormatGraphFloat(failValue);
        }

        private static void ParseSwitchSlotValues(
            AutomationGraphNode node,
            out float threshold,
            out float failValue)
        {
            ParseIfSwitchValue(node?.ValueText, 10f, 0f, out threshold, out failValue);
        }

        private static string SwitchValueText(float threshold, float failValue)
        {
            return "threshold " + FormatGraphFloat(threshold) + " else " + FormatGraphFloat(failValue);
        }

        private static void ParseClampSlotValues(
            AutomationGraphNode node,
            out float lower,
            out float upper)
        {
            ParseRange(node?.ValueText, 0f, 100f, out lower, out upper);
        }

        private static string ClampValueText(float lower, float upper)
        {
            if (lower > upper)
            {
                float swap = lower;
                lower = upper;
                upper = swap;
            }

            return FormatGraphFloat(lower) + ".." + FormatGraphFloat(upper);
        }

        private static void ParseRandomSlotValues(
            AutomationGraphNode node,
            out float lower,
            out float upper)
        {
            ParseRange(node?.ValueText, 0f, 1f, out lower, out upper);
        }

        private static string RandomRangeSlotText(AutomationGraphNode node)
        {
            ParseRandomSlotValues(node, out float lower, out float upper);
            return RandomValueText(lower, upper);
        }

        private static string RandomValueText(float lower, float upper)
        {
            if (lower > upper)
            {
                float swap = lower;
                lower = upper;
                upper = swap;
            }

            return FormatGraphFloat(lower) + ".." + FormatGraphFloat(upper);
        }

        private static string SmoothSecondsValueText(string secondsText)
        {
            return FormatGraphFloat(Mathf.Max(0f, ParseSeconds(secondsText, 0.25f))) + "s";
        }

        private static string MathOperandFromExpression(
            string expression,
            string operatorText,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return fallback;

            string trimmed = expression.Trim();
            string spacedOperator = " " + operatorText + " ";
            int spacedIndex = trimmed.LastIndexOf(spacedOperator, StringComparison.Ordinal);
            if (spacedIndex >= 0 && spacedIndex + spacedOperator.Length < trimmed.Length)
                return trimmed.Substring(spacedIndex + spacedOperator.Length).Trim();

            int operatorIndex = trimmed.LastIndexOf(operatorText, StringComparison.Ordinal);
            if (operatorIndex >= 0 && operatorIndex + operatorText.Length < trimmed.Length)
                return trimmed.Substring(operatorIndex + operatorText.Length).Trim();

            return trimmed.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? fallback
                : trimmed;
        }

        private static string MathExpressionText(
            AutomationNodeKind kind,
            string operandText)
        {
            float operand = ParseFloat(operandText, ParseFloat(MathDefaultOperand(kind), 0f));
            return "a " + MathOperator(kind) + " " + FormatGraphFloat(operand);
        }

        private static string MathExpressionInputText(AutomationNodeKind kind)
        {
            return "a " + MathOperator(kind) + " b";
        }

        private static string FormatGraphFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private bool DrawBlockSlot(
            ref float y,
            Rect inner,
            string label,
            string value,
            bool interactive = false,
            string tooltip = null)
        {
            float height = EsuHudLayout.Scale(20f);
            Rect labelRect = new Rect(inner.x, y, EsuHudLayout.Scale(GraphSlotLabelWidth), height);
            Rect valueRect = new Rect(labelRect.xMax + EsuHudLayout.Scale(6f), y, Mathf.Max(1f, inner.xMax - labelRect.xMax - EsuHudLayout.Scale(6f)), height);
            _lastDrawnGraphSlotRect = valueRect;
            GUI.Label(labelRect, label, DecorationEditorTheme.Mini);
            bool clicked = false;
            string displayValue = ShortMiddleText(value, GraphSlotValueTextMaxCharacters);
            if (interactive)
            {
                MarkGraphSlotInputIfMouseInside(valueRect);
                clicked = GUI.Button(
                    valueRect,
                    new GUIContent(displayValue, tooltip ?? value ?? "Cycle this block slot."),
                    DecorationEditorTheme.ToolButton(false));
            }
            else
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(0f, 0.07f, 0.085f, 0.72f * oldColor.a);
                GUI.DrawTexture(valueRect, Texture2D.whiteTexture);
                GUI.color = oldColor;
                GUI.Label(EsuHudLayout.PanelInnerRect(valueRect, 3f), displayValue, GraphSlotValueStyle());
            }

            RegisterFullTextTooltip(valueRect, value, displayValue);
            y += height + EsuHudLayout.Scale(3f);
            return clicked;
        }

        private bool DrawCompactBlockSlot(
            ref float y,
            Rect inner,
            string label,
            string value,
            bool interactive = false,
            string tooltip = null)
        {
            float height = EsuHudLayout.Scale(18f);
            Rect labelRect = new Rect(inner.x, y, EsuHudLayout.Scale(34f), height);
            Rect valueRect = new Rect(labelRect.xMax + EsuHudLayout.Scale(4f), y, Mathf.Max(1f, inner.xMax - labelRect.xMax - EsuHudLayout.Scale(4f)), height);
            _lastDrawnGraphSlotRect = valueRect;
            GUI.Label(labelRect, label, DecorationEditorTheme.Mini);
            bool clicked = false;
            string displayValue = ShortMiddleText(value, GraphCompactSlotValueTextMaxCharacters);
            if (interactive)
            {
                MarkGraphSlotInputIfMouseInside(valueRect);
                clicked = GUI.Button(
                    valueRect,
                    new GUIContent(displayValue, tooltip ?? value ?? "Choose this value block slot."),
                    DecorationEditorTheme.ToolButton(false));
            }
            else
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(0f, 0.07f, 0.085f, 0.72f * oldColor.a);
                GUI.DrawTexture(valueRect, Texture2D.whiteTexture);
                GUI.color = oldColor;
                GUI.Label(EsuHudLayout.PanelInnerRect(valueRect, 3f), displayValue, GraphSlotValueStyle());
            }

            RegisterFullTextTooltip(valueRect, value, displayValue);
            y += height + EsuHudLayout.Scale(2f);
            return clicked;
        }

        private void DrawCompactReadValueSentence(
            Rect inner,
            AutomationGraphNode node,
            bool interactive)
        {
            float height = EsuHudLayout.Scale(18f);
            float y = inner.y + EsuHudLayout.Scale(1f);
            Rect labelRect = new Rect(inner.x, y, EsuHudLayout.Scale(34f), height);
            Rect targetRect = new Rect(
                labelRect.xMax + EsuHudLayout.Scale(4f),
                y,
                Mathf.Max(1f, inner.xMax - labelRect.xMax - EsuHudLayout.Scale(34f)),
                height);
            y += height + EsuHudLayout.Scale(2f);
            float gap = EsuHudLayout.Scale(3f);
            float halfWidth = Mathf.Max(1f, (inner.width - gap) * 0.5f);
            Rect propertyRect = new Rect(inner.x, y, halfWidth, height);
            Rect currentRect = new Rect(propertyRect.xMax + gap, y, halfWidth, height);

            string property = DisplayNodeProperty(node);
            GUI.Label(labelRect, "read", DecorationEditorTheme.Mini);
            if (interactive)
            {
                MarkGraphSlotInputIfMouseInside(targetRect);
                MarkGraphSlotInputIfMouseInside(propertyRect);
                if (GUI.Button(
                        targetRect,
                        new GUIContent(ShortMiddleText(BlockTitleTargetLabel(node), 24), TargetSlotTooltip(node, input: true)),
                        DecorationEditorTheme.ToolButton(false)))
                {
                    _lastDrawnGraphSlotRect = targetRect;
                    OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Target, targetRect);
                }

                if (GUI.Button(
                        propertyRect,
                        new GUIContent(ShortMiddleText(property, 24), "Choose native readable property."),
                        DecorationEditorTheme.ToolButton(false)))
                {
                    _lastDrawnGraphSlotRect = propertyRect;
                    OpenGraphSlotMenu(node, AutomationGraphSlotMenuKind.Property, propertyRect);
                }
            }
            else
            {
                DrawCompactReadValueField(targetRect, BlockTitleTargetLabel(node));
                DrawCompactReadValueField(propertyRect, property);
            }

            DrawCompactReadValueField(currentRect, "now " + InputGetterCurrentValueText(node));
        }

        private static void DrawCompactReadValueField(Rect rect, string value)
        {
            string displayValue = ShortMiddleText(value, GraphCompactSlotValueTextMaxCharacters);
            Color oldColor = GUI.color;
            GUI.color = new Color(0f, 0.07f, 0.085f, 0.72f * oldColor.a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(EsuHudLayout.PanelInnerRect(rect, 3f), displayValue, GraphSlotValueStyle());
            RegisterFullTextTooltip(rect, value, displayValue);
        }

        private static GUIStyle GraphSlotValueStyle()
        {
            return new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
        }

        private void OpenGraphSlotMenu(
            AutomationGraphNode node,
            AutomationGraphSlotMenuKind kind,
            Rect anchorRect)
        {
            if (node == null || kind == AutomationGraphSlotMenuKind.None)
            {
                CloseGraphSlotMenu();
                return;
            }

            _graphSlotMenuKind = kind;
            _graphSlotMenuNodeId = node.Id;
            _graphSlotMenuAnchorRect = anchorRect;
            _graphSlotMenuScroll = Vector2.zero;
            CloseGraphReadinessPopover();
            CloseGraphContextMenu();
            CloseGraphPropertyPicker();
            BeginCanvasInteraction(AutomationCanvasInteractionKind.Dropdown, -1, Vector2.zero);
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private void CloseGraphSlotMenu()
        {
            _graphSlotMenuKind = AutomationGraphSlotMenuKind.None;
            _graphSlotMenuNodeId = 0;
            _graphSlotMenuAnchorRect = Rect.zero;
            _graphSlotMenuRect = Rect.zero;
            if (_canvasInteraction == AutomationCanvasInteractionKind.Dropdown)
                ResetCanvasInteractionState();
        }

        private void OpenGraphPropertyPicker(AutomationGraphNode node)
        {
            if (node == null)
            {
                CloseGraphPropertyPicker();
                return;
            }

            _graphPropertyPickerNodeId = node.Id;
            _graphPropertyPickerScroll = Vector2.zero;
            CloseGraphSlotMenu();
            CloseGraphReadinessPopover();
            CloseGraphContextMenu();
            BeginCanvasInteraction(AutomationCanvasInteractionKind.Dropdown, -1, Vector2.zero);
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private void CloseGraphPropertyPicker()
        {
            _graphPropertyPickerNodeId = 0;
            _graphPropertyPickerRect = Rect.zero;
            _graphPropertyPickerScroll = Vector2.zero;
            if (_canvasInteraction == AutomationCanvasInteractionKind.Dropdown &&
                _graphSlotMenuKind == AutomationGraphSlotMenuKind.None)
            {
                ResetCanvasInteractionState();
            }
        }

        private bool GraphForegroundMenuOpen() =>
            _graphPropertyPickerNodeId != 0 ||
            _graphSlotMenuKind != AutomationGraphSlotMenuKind.None ||
            _graphContextMenuNodeId != 0 ||
            _graphReadinessPopoverNodeId != 0;

        private bool IsMouseInsideGraphSlotDropdown(Event current) =>
            current != null &&
            _graphSlotMenuKind != AutomationGraphSlotMenuKind.None &&
            current.isMouse &&
            _graphSlotMenuRect.Contains(CurrentGraphMousePosition(current));

        private bool IsMouseInsideGraphContextMenu(Event current) =>
            current != null &&
            _graphContextMenuNodeId != 0 &&
            current.isMouse &&
            _graphContextMenuRect.Contains(CurrentGraphMousePosition(current));

        private bool IsMouseInsideGraphReadinessPopover(Event current) =>
            current != null &&
            _graphReadinessPopoverNodeId != 0 &&
            current.isMouse &&
            (_graphReadinessPopoverRect.Contains(CurrentGraphMousePosition(current)) ||
             _graphReadinessPopoverAnchorRect.Contains(CurrentGraphMousePosition(current)));

        private void DrawGraphSlotDropdown(
            AutomationGraph graph,
            Rect viewRect)
        {
            if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.None)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphSlotMenuNodeId);
            if (node == null)
            {
                CloseGraphSlotMenu();
                return;
            }

            if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.Target)
                DrawTargetSlotDropdown(node, viewRect);
            else if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.Property)
                DrawPropertySlotDropdown(node, viewRect);

            ConsumeGraphSlotDropdownMouseInput();
        }

        private void ConsumeGraphSlotDropdownMouseInput()
        {
            Event current = Event.current;
            if (current == null ||
                !current.isMouse ||
                _graphSlotMenuKind == AutomationGraphSlotMenuKind.None)
            {
                return;
            }

            Vector2 graphMouse = CurrentGraphMousePosition(current);
            bool insideMenu = _graphSlotMenuRect.Contains(graphMouse) ||
                              _graphSlotMenuAnchorRect.Contains(graphMouse);
            if (insideMenu)
            {
                if (current.type == EventType.ScrollWheel)
                    AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();

                if (current.type == EventType.MouseDown ||
                    current.type == EventType.MouseUp ||
                    current.type == EventType.MouseDrag ||
                    current.type == EventType.ScrollWheel)
                {
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                    current.Use();
                }

                return;
            }

            if (current.type == EventType.MouseDown)
            {
                CloseGraphSlotMenu();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private bool ConsumeGraphSlotDropdownOutsideMouseDown()
        {
            Event current = Event.current;
            if (current == null ||
                _graphSlotMenuKind == AutomationGraphSlotMenuKind.None ||
                current.type != EventType.MouseDown)
            {
                return false;
            }

            Vector2 graphMouse = CurrentGraphMousePosition(current);
            if (_graphSlotMenuRect.Contains(graphMouse) ||
                _graphSlotMenuAnchorRect.Contains(graphMouse))
            {
                return false;
            }

            CloseGraphSlotMenu();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private void RefreshGraphPropertyPickerRect(
            AutomationGraph graph,
            Rect canvasRect,
            Rect planRect)
        {
            if (_graphPropertyPickerNodeId == 0)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphPropertyPickerNodeId);
            if (node == null)
                return;

            int choiceCount = NativePropertyOptionsForNode(node)
                .Count(option => !string.IsNullOrWhiteSpace(option));
            if (choiceCount <= 0)
                return;

            _graphPropertyPickerRect = GraphPropertyPickerRect(canvasRect, planRect, choiceCount);
        }

        private void RefreshGraphSlotDropdownRect(
            AutomationGraph graph,
            Rect viewRect)
        {
            if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.None)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphSlotMenuNodeId);
            if (node == null)
                return;

            int choiceCount = 1;
            if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.Target)
                choiceCount = Math.Max(1, TargetChoicesForNode(node).Count());
            else if (_graphSlotMenuKind == AutomationGraphSlotMenuKind.Property)
                choiceCount = Math.Max(
                    1,
                    NativePropertyOptionsForNode(node).Count(option => !string.IsNullOrWhiteSpace(option)));

            _graphSlotMenuRect = GraphSlotDropdownRect(viewRect, choiceCount);
        }

        private void DrawGraphPropertyPicker(
            AutomationGraph graph,
            Rect canvasRect,
            Rect planRect)
        {
            if (_graphPropertyPickerNodeId == 0)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphPropertyPickerNodeId);
            if (node == null)
            {
                CloseGraphPropertyPicker();
                return;
            }

            List<string> choices = NativePropertyOptionsForNode(node)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();
            if (choices.Count == 0)
            {
                CloseGraphPropertyPicker();
                return;
            }

            Rect rect = GraphPropertyPickerRect(canvasRect, planRect, choices.Count);
            _graphPropertyPickerRect = rect;
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 6f);
            GUILayout.BeginArea(inner);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Native Property", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent("X", "Close native property choices."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                ConsumeCurrentGraphMenuMouseEvent();
                CloseGraphPropertyPicker();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            GUILayout.EndHorizontal();
            _graphPropertyPickerScroll = GUILayout.BeginScrollView(
                _graphPropertyPickerScroll,
                GUILayout.Height(Mathf.Max(1f, inner.height - EsuHudLayout.Scale(30f))));
            foreach (string option in choices)
            {
                bool selected = MatchesPropertyText(node.Property, option);
                if (!GUILayout.Button(
                        new GUIContent(ShortMiddleText(option, 58), option),
                        DecorationEditorTheme.ToolButton(selected),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    continue;
                }

                ApplyNativeNodeEdits(node, node.Label, option, node.ValueText);
                MarkAutomationDirty();
                ConsumeCurrentGraphMenuMouseEvent();
                CloseGraphPropertyPicker();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            ConsumeGraphPropertyPickerMouseInput();
        }

        private Rect GraphPropertyPickerRect(
            Rect canvasRect,
            Rect planRect,
            int rowCount)
        {
            float pad = EsuHudLayout.Scale(6f);
            float width = EsuHudLayout.Scale(GraphPropertyPickerWidth);
            float headerAndPadding = EsuHudLayout.Scale(56f);
            float rowHeight = EsuHudLayout.Scale(27f);
            float height = Mathf.Clamp(
                headerAndPadding + Mathf.Max(1, rowCount) * rowHeight,
                EsuHudLayout.Scale(86f),
                EsuHudLayout.Scale(GraphPropertyPickerMaxHeight));
            Rect rect = new Rect(
                planRect.x - width - pad,
                planRect.y + EsuHudLayout.Scale(118f),
                width,
                height);
            if (rect.xMin < canvasRect.xMin + pad)
                rect.x = canvasRect.xMin + pad;
            if (rect.xMax > planRect.xMax - pad)
                rect.x = Mathf.Max(canvasRect.xMin + pad, planRect.xMax - rect.width - pad);
            if (rect.yMax > canvasRect.yMax - pad)
                rect.y = Mathf.Max(canvasRect.yMin + pad, canvasRect.yMax - rect.height - pad);
            if (rect.yMin < canvasRect.yMin + pad)
                rect.y = canvasRect.yMin + pad;
            return rect;
        }

        private bool ConsumeGraphPropertyPickerOutsideMouseDown()
        {
            Event current = Event.current;
            if (current == null ||
                _graphPropertyPickerNodeId == 0 ||
                current.type != EventType.MouseDown ||
                !current.isMouse ||
                _graphPropertyPickerRect.Contains(current.mousePosition))
            {
                return false;
            }

            CloseGraphPropertyPicker();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private void ConsumeGraphPropertyPickerMouseInput()
        {
            Event current = Event.current;
            if (current == null ||
                !current.isMouse ||
                _graphPropertyPickerNodeId == 0)
            {
                return;
            }

            bool inside = _graphPropertyPickerRect.Contains(current.mousePosition);
            if (inside)
            {
                if (current.type == EventType.ScrollWheel)
                    AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();

                if (current.type == EventType.MouseDown ||
                    current.type == EventType.MouseUp ||
                    current.type == EventType.MouseDrag ||
                    current.type == EventType.ScrollWheel)
                {
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                    current.Use();
                }

                return;
            }

            if (current.type == EventType.MouseDown)
            {
                CloseGraphPropertyPicker();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp ||
                current.type == EventType.MouseDrag ||
                current.type == EventType.ScrollWheel)
            {
                if (current.type == EventType.ScrollWheel)
                    AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                current.Use();
            }
        }

        private void OpenGraphContextMenu(
            AutomationGraphNode node,
            Vector2 graphPoint,
            Rect viewRect)
        {
            if (node == null)
            {
                CloseGraphContextMenu();
                return;
            }

            _graphContextMenuNodeId = node.Id;
            _graphContextMenuRect = GraphContextMenuRect(graphPoint, viewRect);
            CloseGraphReadinessPopover();
            CloseGraphPropertyPicker();
            BeginCanvasInteraction(AutomationCanvasInteractionKind.ContextMenu, -1, Vector2.zero);
        }

        private void CloseGraphContextMenu()
        {
            _graphContextMenuNodeId = 0;
            _graphContextMenuRect = Rect.zero;
            if (_canvasInteraction == AutomationCanvasInteractionKind.ContextMenu)
                ResetCanvasInteractionState();
        }

        private Rect GraphContextMenuRect(
            Vector2 graphPoint,
            Rect viewRect)
        {
            Rect rect = new Rect(
                graphPoint.x,
                graphPoint.y,
                EsuHudLayout.Scale(178f),
                EsuHudLayout.Scale(126f));
            return ClampGraphContextMenuRect(rect, viewRect);
        }

        private Rect ClampGraphContextMenuRect(
            Rect rect,
            Rect viewRect)
        {
            float pad = EsuHudLayout.Scale(6f);
            if (rect.xMax > viewRect.xMax)
                rect.x = Mathf.Max(viewRect.xMin + pad, viewRect.xMax - rect.width - pad);
            if (rect.yMax > viewRect.yMax)
                rect.y = Mathf.Max(viewRect.yMin + pad, viewRect.yMax - rect.height - pad);
            if (rect.x < viewRect.xMin)
                rect.x = viewRect.xMin + pad;
            if (rect.y < viewRect.yMin)
                rect.y = viewRect.yMin + pad;
            return rect;
        }

        private AutomationGraphNode FindGraphNodeAtPoint(
            AutomationGraph graph,
            Vector2 graphPoint)
        {
            if (graph == null)
                return null;

            for (int index = graph.Nodes.Count - 1; index >= 0; index--)
            {
                AutomationGraphNode node = graph.Nodes[index];
                if (node != null && node.Rect.Contains(graphPoint))
                    return node;
            }

            return null;
        }

        private void DrawGraphContextMenu(
            AutomationGraph graph,
            Rect viewRect)
        {
            if (_graphContextMenuNodeId == 0)
                return;

            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == _graphContextMenuNodeId);
            if (node == null)
            {
                CloseGraphContextMenu();
                return;
            }

            _graphContextMenuRect = ClampGraphContextMenuRect(_graphContextMenuRect, viewRect);
            GUI.Box(_graphContextMenuRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(_graphContextMenuRect, 6f);
            Rect title = new Rect(inner.x, inner.y, inner.width, EsuHudLayout.Scale(24f));
            GUI.Label(title, "Block", DecorationEditorTheme.SubHeader);
            float rowHeight = EsuHudLayout.Scale(26f);
            float gap = EsuHudLayout.Scale(4f);
            Rect row = new Rect(inner.x, title.yMax + gap, inner.width, rowHeight);
            AutomationGraphContextAction action = AutomationGraphContextAction.None;
            if (DrawGraphContextMenuButton(row, "Deselect", "Clear the selected automation block."))
                action = AutomationGraphContextAction.Deselect;
            row.y += rowHeight + gap;
            if (DrawGraphContextMenuButton(row, "Delete", "Remove this automation block."))
                action = AutomationGraphContextAction.Delete;
            row.y += rowHeight + gap;
            if (DrawGraphContextMenuButton(row, "Close", "Close this menu."))
                action = AutomationGraphContextAction.Close;

            Event current = Event.current;
            if (action == AutomationGraphContextAction.None &&
                current != null &&
                current.type == EventType.MouseDown &&
                current.isMouse &&
                !_graphContextMenuRect.Contains(CurrentGraphMousePosition(current)))
            {
                action = AutomationGraphContextAction.Close;
                current.Use();
            }

            if (action == AutomationGraphContextAction.None)
                return;

            if (action == AutomationGraphContextAction.Deselect)
            {
                graph.SelectedNodeId = 0;
                _closedQuickChoicesNodeId = 0;
            }
            else if (action == AutomationGraphContextAction.Delete)
            {
                RemoveGraphNode(graph, node);
                MarkAutomationDirty();
            }

            CloseGraphContextMenu();
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private bool DrawGraphContextMenuButton(
            Rect rect,
            string label,
            string tooltip) =>
            GUI.Button(rect, new GUIContent(label, tooltip), DecorationEditorTheme.Button) ||
            TryConsumeGraphLeftMouseUp(rect);

        private void DrawTargetSlotDropdown(
            AutomationGraphNode node,
            Rect viewRect)
        {
            List<AutomationLink> choices = TargetChoicesForNode(node).ToList();
            Rect rect = GraphSlotDropdownRect(viewRect, Math.Max(1, choices.Count));
            _graphSlotMenuRect = rect;
            MarkGraphDropdownInputIfMouseInside(rect);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 6f);
            GUILayout.BeginArea(inner);
            if (DrawGraphSlotDropdownHeader(node.Kind == AutomationNodeKind.InputGetter ? "Input Target" : "Output Target", inner))
            {
                GUILayout.EndArea();
                return;
            }

            if (choices.Count == 0)
            {
                GUILayout.Label(
                    node.Kind == AutomationNodeKind.InputGetter
                        ? "Link an input block from the craft first."
                        : "Link an output block from the craft first.",
                    DecorationEditorTheme.MiniWrap);
            }
            else
            {
                _graphSlotMenuScroll = GUILayout.BeginScrollView(
                    _graphSlotMenuScroll,
                    GUILayout.Height(Mathf.Max(1f, inner.height - EsuHudLayout.Scale(30f))));
                foreach (AutomationLink link in choices)
                {
                    string targetName = LinkChoiceTargetName(node, link);
                    bool selected = string.Equals(targetName, BlockTargetLabel(node), StringComparison.OrdinalIgnoreCase) &&
                                    MatchesPropertyText(node.Property, link.Property);
                    string label = ShortMiddleText(targetName, 36) + " | " + ShortMiddleText(link.Property, 32);
                    if (!GUILayout.Button(
                            new GUIContent(label, targetName + " | " + link.Property),
                            DecorationEditorTheme.ToolButton(selected),
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        continue;
                    }

                    if (TryApplyNativeLinkTarget(node, link, out string message))
                    {
                        MarkAutomationDirty();
                        InfoStore.Add(message);
                    }
                    else
                    {
                        InfoStore.Add(message ?? "Automation Builder could not assign that linked target.");
                    }

                    ConsumeCurrentGraphMenuMouseEvent();
                    CloseGraphSlotMenu();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void DrawPropertySlotDropdown(
            AutomationGraphNode node,
            Rect viewRect)
        {
            List<string> choices = NativePropertyOptionsForNode(node)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();
            Rect rect = GraphSlotDropdownRect(viewRect, Math.Max(1, choices.Count));
            _graphSlotMenuRect = rect;
            MarkGraphDropdownInputIfMouseInside(rect);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect, 6f);
            GUILayout.BeginArea(inner);
            if (DrawGraphSlotDropdownHeader("Native Property", inner))
            {
                GUILayout.EndArea();
                return;
            }

            if (choices.Count == 0)
            {
                GUILayout.Label("Choose a linked target first.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                _graphSlotMenuScroll = GUILayout.BeginScrollView(
                    _graphSlotMenuScroll,
                    GUILayout.Height(Mathf.Max(1f, inner.height - EsuHudLayout.Scale(30f))));
                foreach (string option in choices)
                {
                    bool selected = MatchesPropertyText(node.Property, option);
                    if (!GUILayout.Button(
                            new GUIContent(ShortMiddleText(option, 58), option),
                            DecorationEditorTheme.ToolButton(selected),
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        continue;
                    }

                    ApplyNativeNodeEdits(node, node.Label, option, node.ValueText);
                    MarkAutomationDirty();
                    ConsumeCurrentGraphMenuMouseEvent();
                    CloseGraphSlotMenu();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private static void ConsumeCurrentGraphMenuMouseEvent()
        {
            Event current = Event.current;
            if (current == null || !current.isMouse)
                return;

            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current.Use();
        }

        private bool DrawGraphSlotDropdownHeader(string title, Rect areaRect)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            bool close = GUILayout.Button(
                new GUIContent("X", "Close this block submenu."),
                DecorationEditorTheme.Button,
                GUILayout.Width(EsuHudLayout.Scale(28f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            Rect closeRect = new Rect(
                areaRect.xMax - EsuHudLayout.Scale(28f),
                areaRect.y,
                EsuHudLayout.Scale(28f),
                EsuHudLayout.Scale(22f));
            close |= TryConsumeGraphLeftMouseUp(closeRect);
            GUILayout.EndHorizontal();
            if (close)
            {
                CloseGraphSlotMenu();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
            }

            return close;
        }

        private bool TryConsumeGraphLeftMouseUp(Rect graphRect)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.MouseUp ||
                current.button != 0 ||
                !graphRect.Contains(CurrentGraphMousePosition(current)))
            {
                return false;
            }

            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            current.Use();
            return true;
        }

        private Rect GraphSlotDropdownRect(
            Rect viewRect,
            int rowCount)
        {
            float width = EsuHudLayout.Scale(GraphSlotDropdownWidth);
            float headerAndPadding = EsuHudLayout.Scale(56f);
            float rowHeight = EsuHudLayout.Scale(27f);
            float height = Mathf.Clamp(
                headerAndPadding + Mathf.Max(1, rowCount) * rowHeight,
                EsuHudLayout.Scale(86f),
                EsuHudLayout.Scale(GraphSlotDropdownMaxHeight));
            Rect rect = new Rect(
                _graphSlotMenuAnchorRect.x,
                _graphSlotMenuAnchorRect.yMax + EsuHudLayout.Scale(4f),
                width,
                height);
            if (rect.xMax > viewRect.xMax)
                rect.x = Mathf.Max(viewRect.xMin, viewRect.xMax - rect.width - EsuHudLayout.Scale(6f));
            if (rect.xMin < viewRect.xMin)
                rect.x = viewRect.xMin + EsuHudLayout.Scale(6f);
            if (rect.yMax > viewRect.yMax)
                rect.y = Mathf.Max(viewRect.yMin, _graphSlotMenuAnchorRect.y - rect.height - EsuHudLayout.Scale(4f));
            if (rect.yMin < viewRect.yMin)
                rect.y = viewRect.yMin + EsuHudLayout.Scale(6f);
            return rect;
        }

        private void MarkGraphDropdownInputIfMouseInside(Rect rect)
        {
            Event current = Event.current;
            if (current == null || !current.isMouse || !rect.Contains(CurrentGraphMousePosition(current)))
                return;

            _graphSlotConsumedInput = true;
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
            if (current.type == EventType.ScrollWheel)
                AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
        }

        private void MarkGraphSlotInputIfMouseInside(Rect rect)
        {
            Event current = Event.current;
            if (current == null || !current.isMouse || !rect.Contains(CurrentGraphMousePosition(current)))
                return;

            _graphSlotConsumedInput = true;
        }

        private static AutomationGraphNode TemporaryPaletteNode(AutomationNodeKind kind) =>
            new AutomationGraphNode(
                0,
                kind,
                Rect.zero,
                DefaultNodeLabel(kind),
                DefaultProperty(kind),
                DefaultValue(kind));

        private static string PaletteBlockSentence(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "read linked block property";
                case AutomationNodeKind.OutputSetter:
                    return "set linked block property";
                case AutomationNodeKind.Forever:
                    return "forever / native evaluation";
                case AutomationNodeKind.IfCondition:
                    return "if signal is true";
                case AutomationNodeKind.IfLessThan:
                    return "pass when signal > threshold";
                case AutomationNodeKind.LogicNot:
                    return "not signal";
                case AutomationNodeKind.LogicAnd:
                    return "signal AND value";
                case AutomationNodeKind.LogicOr:
                    return "signal OR value";
                case AutomationNodeKind.LogicXor:
                    return "signal XOR value";
                case AutomationNodeKind.LogicNand:
                    return "signal NAND value";
                case AutomationNodeKind.LogicNor:
                    return "signal NOR value";
                case AutomationNodeKind.LogicXnor:
                    return "signal XNOR value";
                case AutomationNodeKind.CompareAboveThreshold:
                    return "signal above threshold";
                case AutomationNodeKind.CompareBelowThreshold:
                    return "signal below threshold";
                case AutomationNodeKind.Constant:
                    return "number value";
                case AutomationNodeKind.Random:
                    return "random number";
                case AutomationNodeKind.MathAdd:
                    return "add to signal";
                case AutomationNodeKind.MathSubtract:
                    return "subtract from signal";
                case AutomationNodeKind.MathMultiply:
                    return "multiply signal";
                case AutomationNodeKind.MathMax:
                    return "maximum of two inputs";
                case AutomationNodeKind.MathMin:
                    return "minimum of two inputs";
                case AutomationNodeKind.Clamp:
                    return "clamp signal";
                case AutomationNodeKind.Smooth:
                    return "smooth signal";
                default:
                    return "note";
            }
        }

        private static string BlockSentenceTitle(AutomationGraphNode node)
        {
            switch (node.Kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "read " + ShortMiddleText(BlockTitleTargetLabel(node), 34);
                case AutomationNodeKind.OutputSetter:
                    return "set " + ShortMiddleText(BlockTitleTargetLabel(node), 34);
                case AutomationNodeKind.Forever:
                    return "forever";
                case AutomationNodeKind.IfCondition:
                    return "if true";
                case AutomationNodeKind.IfLessThan:
                    return "if input is above threshold";
                case AutomationNodeKind.LogicNot:
                    return "not";
                case AutomationNodeKind.LogicAnd:
                    return "and";
                case AutomationNodeKind.LogicOr:
                    return "or";
                case AutomationNodeKind.LogicXor:
                    return "xor";
                case AutomationNodeKind.LogicNand:
                    return "nand";
                case AutomationNodeKind.LogicNor:
                    return "nor";
                case AutomationNodeKind.LogicXnor:
                    return "xnor";
                case AutomationNodeKind.CompareAboveThreshold:
                    return "above threshold";
                case AutomationNodeKind.CompareBelowThreshold:
                    return "below threshold";
                case AutomationNodeKind.Constant:
                    return "constant";
                case AutomationNodeKind.Random:
                    return "random";
                case AutomationNodeKind.MathAdd:
                    return "add";
                case AutomationNodeKind.MathSubtract:
                    return "subtract";
                case AutomationNodeKind.MathMultiply:
                    return "multiply";
                case AutomationNodeKind.MathMax:
                    return "max";
                case AutomationNodeKind.MathMin:
                    return "min";
                case AutomationNodeKind.Clamp:
                    return "clamp";
                case AutomationNodeKind.Smooth:
                    return "smooth";
                default:
                    return ShortText(node.Label, 28);
            }
        }

        private void DrawSelectedNodeInspector()
        {
            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard);
            AutomationGraphNode node = graph?.SelectedNode;
            GUILayout.Label("Selected Node", DecorationEditorTheme.SubHeader);
            if (node == null)
            {
                GUILayout.Label("Select a node on the canvas.", DecorationEditorTheme.MiniWrap);
                return;
            }

            GUILayout.Label(NodeTitle(node.Kind), DecorationEditorTheme.Body);
            DrawLinkedTargetSuggestions(node);
            GUILayout.Label("Label", DecorationEditorTheme.Mini);
            string nextLabel = GUILayout.TextField(node.Label ?? string.Empty, DecorationEditorTheme.TextField);
            GUILayout.Label("Property", DecorationEditorTheme.Mini);
            string nextProperty = GUILayout.TextField(node.Property ?? string.Empty, DecorationEditorTheme.TextField);
            DrawNativePropertySuggestions(node);
            GUILayout.Label("Value", DecorationEditorTheme.Mini);
            string nextValue = GUILayout.TextField(node.ValueText ?? string.Empty, DecorationEditorTheme.TextField);
            if (nextLabel != node.Label ||
                nextProperty != node.Property ||
                nextValue != node.ValueText)
            {
                ApplyNativeNodeEdits(node, nextLabel, nextProperty, nextValue);
                MarkAutomationDirty();
            }
        }

        private void CycleLinkedTarget(AutomationGraphNode node)
        {
            if (node == null ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return;
            }

            List<AutomationLink> choices = TargetChoicesForNode(node).ToList();
            if (choices.Count == 0)
            {
                InfoStore.Add(node.Kind == AutomationNodeKind.InputGetter
                    ? "Link an input block from the craft before cycling this target slot."
                    : "Link an output block from the craft before cycling this target slot.");
                return;
            }

            string currentTarget = BlockTargetLabel(node);
            int currentIndex = choices.FindIndex(link =>
                string.Equals(LinkChoiceTargetName(node, link), currentTarget, StringComparison.OrdinalIgnoreCase) &&
                MatchesPropertyText(node.Property, link.Property));
            if (currentIndex < 0)
            {
                currentIndex = choices.FindIndex(link =>
                    string.Equals(LinkChoiceTargetName(node, link), currentTarget, StringComparison.OrdinalIgnoreCase));
            }

            AutomationLink next = choices[(currentIndex + 1 + choices.Count) % choices.Count];
            if (TryApplyNativeLinkTarget(node, next, out string message))
            {
                MarkAutomationDirty();
                InfoStore.Add(message);
            }
            else
            {
                InfoStore.Add(message ?? "Automation Builder could not cycle that linked target.");
            }
        }

        private void CycleNativeProperty(AutomationGraphNode node)
        {
            if (node == null)
                return;

            List<string> options = NativePropertyOptionsForNode(node)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();
            if (options.Count == 0)
            {
                InfoStore.Add("No native property options are available for this block yet.");
                return;
            }

            int currentIndex = options.FindIndex(option => MatchesPropertyText(node.Property, option));
            string next = options[(currentIndex + 1 + options.Count) % options.Count];
            ApplyNativeNodeEdits(node, node.Label, next, node.ValueText);
            MarkAutomationDirty();
            InfoStore.Add("Automation block property set to " + next + ".");
        }

        private static bool IsSelectedGraphNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return graph != null &&
                   node != null &&
                   ReferenceEquals(graph.SelectedNode, node);
        }

        private static string BlockTargetLabel(AutomationGraphNode node)
        {
            if (node == null)
                return "linked block";

            AutomationBlockRef boundTarget = BoundTargetRef(node);
            if (boundTarget != null)
                return BlockRefCurrentName(boundTarget);

            string label = node.Label ?? string.Empty;
            if (node.Kind == AutomationNodeKind.InputGetter &&
                label.StartsWith("Read ", StringComparison.OrdinalIgnoreCase))
            {
                return label.Substring(5);
            }

            if (node.Kind == AutomationNodeKind.OutputSetter &&
                label.StartsWith("Set ", StringComparison.OrdinalIgnoreCase))
            {
                return label.Substring(4);
            }

            return string.IsNullOrWhiteSpace(label) ? "linked block" : label;
        }

        private static string BlockTitleTargetLabel(AutomationGraphNode node)
        {
            string label = BlockTargetLabel(node);
            if (!IsGeneratedAutomationBlockName(label) ||
                !TryGetBoundTargetBlock(node, out Block block))
            {
                return label;
            }

            string itemName = AutomationBreadboardCatalog.ItemName(block.item);
            string baseName = !string.IsNullOrWhiteSpace(itemName)
                ? itemName
                : HumanizeTypeName(block.GetType().Name);
            string suffix = GeneratedAutomationBlockSuffix(label);
            return string.IsNullOrWhiteSpace(suffix)
                ? baseName
                : baseName + " " + suffix;
        }

        private static string BlockSentenceTitleTooltip(AutomationGraphNode node)
        {
            string title = BlockSentenceTitle(node);
            if (node == null ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return title;
            }

            string exact = BlockTargetLabel(node);
            string display = BlockTitleTargetLabel(node);
            if (string.IsNullOrWhiteSpace(exact) ||
                string.Equals(exact, display, StringComparison.Ordinal))
            {
                return title;
            }

            return title + "\nExact target: " + exact;
        }

        private static string TargetSlotTooltip(
            AutomationGraphNode node,
            bool input)
        {
            string exact = BlockTargetLabel(node);
            string prefix = input
                ? "Choose linked input target."
                : "Choose linked output target.";
            string display = BlockTitleTargetLabel(node);
            if (string.IsNullOrWhiteSpace(exact) ||
                string.Equals(exact, display, StringComparison.Ordinal))
            {
                return prefix;
            }

            return prefix + "\nExact target: " + exact;
        }

        private static string BlockRefCurrentName(AutomationBlockRef target)
        {
            if (target == null)
                return "linked block";

            return target.TryGetBlock(out Block block)
                ? AutomationBreadboardCatalog.BlockName(block)
                : target.Name;
        }

        private static bool IsGeneratedAutomationBlockName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   (name.StartsWith(AutoNamePrefix, StringComparison.Ordinal) ||
                    name.StartsWith(RetiredAutoNamePrefix, StringComparison.Ordinal) ||
                     name.StartsWith("ESU_AB_", StringComparison.OrdinalIgnoreCase));
        }

        private static string GeneratedAutomationBlockSuffix(string name)
        {
            string letters = LettersOnly(name);
            if (letters.Length <= AutoNameTokenLength)
                return string.Empty;

            return letters.Substring(letters.Length - AutoNameTokenLength);
        }

        private static string HumanizeTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return "linked block";

            string text = typeName;
            if (text.StartsWith("ESU_AB_", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(7);
            if (text.StartsWith(RetiredAutoNamePrefix, StringComparison.Ordinal))
                text = text.Substring(RetiredAutoNamePrefix.Length);
            if (text.StartsWith(AutoNamePrefix, StringComparison.Ordinal))
                text = text.Substring(AutoNamePrefix.Length);

            var words = new List<string>();
            int start = 0;
            for (int index = 1; index < text.Length; index++)
            {
                if (!char.IsUpper(text[index]) || !char.IsLetterOrDigit(text[index - 1]))
                    continue;

                words.Add(text.Substring(start, index - start));
                start = index;
            }

            if (start < text.Length)
                words.Add(text.Substring(start));

            return words.Count == 0
                ? text
                : string.Join(" ", words).ToLowerInvariant();
        }

        private static AutomationBlockRef BoundTargetRef(AutomationGraphNode node)
        {
            if (node?.TargetBinding == null)
                return null;

            return node.Kind == AutomationNodeKind.InputGetter
                ? node.TargetBinding.Source
                : node.Kind == AutomationNodeKind.OutputSetter
                    ? node.TargetBinding.Target
                    : null;
        }

        private static bool TryGetBoundTargetBlock(
            AutomationGraphNode node,
            out Block block)
        {
            block = null;
            AutomationBlockRef target = BoundTargetRef(node);
            return target != null && target.TryGetBlock(out block);
        }

        private static string LinkChoiceTargetName(
            AutomationGraphNode node,
            AutomationLink link)
        {
            if (node == null || link == null)
                return string.Empty;

            AutomationBlockRef target = node.Kind == AutomationNodeKind.InputGetter
                ? link.Source
                : link.Target;
            return BlockRefCurrentName(target);
        }

        private void DrawLinkedTargetSuggestions(AutomationGraphNode node)
        {
            if (node == null ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return;
            }

            List<AutomationLink> choices = TargetChoicesForNode(node).Take(6).ToList();
            GUILayout.Label(node.Kind == AutomationNodeKind.InputGetter ? "Input Target" : "Output Target", DecorationEditorTheme.Mini);
            if (choices.Count == 0)
            {
                GUILayout.Label(
                    node.Kind == AutomationNodeKind.InputGetter
                        ? "Link an input block from the craft first."
                        : "Link an output block from the craft first.",
                    DecorationEditorTheme.MiniWrap);
                return;
            }

            foreach (AutomationLink link in choices)
            {
                string label = LinkNodeLabel(link) + " | " + ShortText(link.Property, 24);
                if (!GUILayout.Button(label, DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(26f))))
                    continue;

                if (TryApplyNativeLinkTarget(node, link, out string message))
                {
                    MarkAutomationDirty();
                    InfoStore.Add(message);
                }
                else
                {
                    InfoStore.Add(message ?? "Automation Builder could not assign that linked target.");
                }
            }
        }

        private IEnumerable<AutomationLink> TargetChoicesForNode(AutomationGraphNode node)
        {
            if (node == null ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                yield break;
            }

            AutomationLinkKind kind = node.Kind == AutomationNodeKind.InputGetter
                ? AutomationLinkKind.InputToBreadboard
                : AutomationLinkKind.BreadboardToOutput;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationLink link in _links)
            {
                if (link == null || link.Kind != kind || !link.IsResolved)
                    continue;

                AutomationBlockRef target = kind == AutomationLinkKind.InputToBreadboard
                    ? link.Source
                    : link.Target;
                string key = (target?.StableKey ?? string.Empty) + "|" + (link.Property ?? string.Empty);
                if (seen.Add(key))
                    yield return link;
            }
        }

        private void DrawNativePropertySuggestions(AutomationGraphNode node)
        {
            List<string> options = NativePropertyOptionsForNode(node).Take(8).ToList();
            if (options.Count == 0)
                return;

            GUILayout.Label("Native Properties", DecorationEditorTheme.Mini);
            bool open = node != null && _graphPropertyPickerNodeId == node.Id;
            string property = string.IsNullOrWhiteSpace(node?.Property)
                ? "Choose native property"
                : node.Property;
            if (GUILayout.Button(
                    new GUIContent(ShortMiddleText(property, 42), "Open native property choices."),
                    DecorationEditorTheme.ToolButton(open),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                OpenGraphPropertyPicker(node);
            }
        }

        private void AddGraphNode(AutomationNodeKind kind)
        {
            AddGraphNode(kind, hasPreferredRect: false, preferredRect: Rect.zero);
        }

        private void AddGraphNode(
            AutomationNodeKind kind,
            bool hasPreferredRect,
            Rect preferredRect)
        {
            if (_selectedBreadboard == null)
                return;

            AutomationGraph graph = GraphFor(_selectedBreadboard);
            AutomationGraphNode node = graph.AddStagedNode(
                kind,
                hasPreferredRect
                    ? preferredRect
                    : VisibleWorkspaceDropGraphRect(kind));
            graph.SelectedNodeId = node.Id;
            TryAutoBindStagedGraphNode(graph, node);
            if (hasPreferredRect)
                TrySnapNewGraphNode(graph, node.Id);
            InfoStore.Add("Automation Builder staged " + NodeTitle(kind) + ". Apply will lower staged blocks into native components.");
            MarkAutomationDirty();
        }

        private void TryAutoBindStagedGraphNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null ||
                node == null ||
                node.NativeComponent != null ||
                node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return;
            }

            List<AutomationLink> choices = TargetChoicesForNode(node).ToList();
            if (choices.Count != 1)
                return;

            if (TryApplyNativeLinkTarget(node, choices[0], out string message) &&
                !string.IsNullOrWhiteSpace(message))
            {
                InfoStore.Add(message);
            }
        }

        private void TrySnapNewGraphNode(AutomationGraph graph, int nativeId)
        {
            AutomationGraphNode node = graph?.Nodes.FirstOrDefault(candidate => candidate.Id == nativeId);
            if (node == null)
                return;

            if (TrySnapGraphNode(graph, node))
            {
                RefreshGraphConnections(graph);
                SyncNativeNodeRect(node);
            }
        }

        private void RemoveGraphNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null)
                return;

            List<AutomationLink> orphanedStagedLinks = StagedLinksForGraphNode(node).ToList();
            if (node.NativeComponent != null &&
                !RemoveNativeGraphNode(node))
            {
                return;
            }

            graph.RemoveConnectionsTouchingNode(node.Id);
            graph.Nodes.RemoveAll(candidate => ReferenceEquals(candidate, node) || candidate.Id == node.Id);
            RemoveUnusedStagedLinks(orphanedStagedLinks, graph);
            if (graph.SelectedNodeId == node.Id)
                graph.SelectedNodeId = 0;
            if (_graphSlotMenuNodeId == node.Id)
                CloseGraphSlotMenu();
            if (_graphContextMenuNodeId == node.Id)
                CloseGraphContextMenu();
            if (_graphReadinessPopoverNodeId == node.Id)
                CloseGraphReadinessPopover();
            if (_graphPropertyPickerNodeId == node.Id)
                CloseGraphPropertyPicker();
            if (_draggingNodeId == node.Id)
                ResetCanvasInteractionState();
            RefreshGraphConnections(graph);
        }

        private IEnumerable<AutomationLink> StagedLinksForGraphNode(AutomationGraphNode node)
        {
            if (node?.TargetBinding == null)
                yield break;

            foreach (AutomationLink link in _links)
            {
                if (link?.IsStaged == true && NodeBindingMatchesLink(node, link))
                    yield return link;
            }
        }

        private void RemoveUnusedStagedLinks(
            IEnumerable<AutomationLink> links,
            AutomationGraph graph)
        {
            if (links == null || graph == null)
                return;

            foreach (AutomationLink link in links.ToList())
            {
                if (link == null ||
                    graph.Nodes.Any(node => node != null && NodeBindingMatchesLink(node, link)))
                {
                    continue;
                }

                _links.Remove(link);
                if (ReferenceEquals(_selectedLink, link))
                    _selectedLink = null;
            }
        }

        private void EnsureStarterGraph(AutomationGraph graph, AutomationBlockRef breadboard)
        {
            // Native breadboard components are the graph source of truth.
        }

        private AutomationGraph GraphFor(AutomationBlockRef breadboard)
        {
            string key = breadboard?.StableKey ?? "global";
            if (!_graphs.TryGetValue(key, out AutomationGraph graph))
            {
                graph = new AutomationGraph(key);
                _graphs[key] = graph;
            }

            return graph;
        }

        private AutomationGraph SyncedGraphFor(
            AutomationBlockRef breadboard,
            bool force = false)
        {
            if (breadboard == null)
                return null;

            // This wrapper must stay thin: GraphFor is the only graph creation path.
            AutomationGraph graph = GraphFor(breadboard);
            if (!breadboard.IsStillValidBreadboard)
                return graph;

            SyncGraphFromNativeBreadboardIfNeeded(breadboard, graph, force);
            NormalizeGraphNodeRects(graph);
            return graph;
        }

        private void NormalizeGraphNodeRects(AutomationGraph graph)
        {
            if (graph == null)
                return;

            bool moved = false;
            foreach (AutomationGraphNode node in graph.Nodes)
            {
                if (node == null)
                    continue;

                bool valueFootprint = CanProduceValueBlock(node.Kind) && IsValueFootprint(node.Rect);
                Rect normalized = NormalizeGraphNodeRect(node.Kind, node.Rect, valueFootprint);
                bool changed =
                    Mathf.Abs(node.Rect.x - normalized.x) > 0.001f ||
                    Mathf.Abs(node.Rect.y - normalized.y) > 0.001f ||
                    Mathf.Abs(node.Rect.width - normalized.width) > 0.001f ||
                    Mathf.Abs(node.Rect.height - normalized.height) > 0.001f;
                if (!changed)
                    continue;

                node.Rect = normalized;
                SyncNativeNodeRect(node);
                moved = true;
            }

            if (moved)
                RefreshGraphConnections(graph, preserveRestoredNative: true);
        }

        private void HandleKeyboard()
        {
            if (_closePromptOpen ||
                GUIUtility.keyboardControl != 0 ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
            {
                return;
            }

            if (_canvasOpen &&
                Input.GetKeyDown(KeyCode.Escape) &&
                (_canvasInteraction != AutomationCanvasInteractionKind.None ||
                 _graphSlotMenuKind != AutomationGraphSlotMenuKind.None ||
                 _graphContextMenuNodeId != 0 ||
                 _graphReadinessPopoverNodeId != 0 ||
                 _graphPropertyPickerNodeId != 0))
            {
                CancelActiveCanvasInteraction(CurrentSelectedGraph(), restoreNode: true);
                CloseGraphSlotMenu();
                CloseGraphContextMenu();
                CloseGraphReadinessPopover();
                CloseGraphPropertyPicker();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return;
            }

            if (_canvasOpen && GraphForegroundMenuOpen())
                return;

            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(1))
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                SetTool(_tool == AutomationBuilderTool.LinkInput
                    ? AutomationBuilderTool.Select
                    : AutomationBuilderTool.LinkInput);
                return;
            }

            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(2))
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                SetTool(_tool == AutomationBuilderTool.LinkOutput
                    ? AutomationBuilderTool.Select
                    : AutomationBuilderTool.LinkOutput);
                return;
            }

            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(3))
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                CycleAutomationViewMode();
                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                OpenCanvas();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete) && _selectedLink != null)
            {
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                RemoveSelectedLink();
            }
        }

        private void HandleMouse()
        {
            if (AutomationBuilderInputScope.MouseOverUi)
                return;

            if (_canvasOpen)
                return;

            if (Input.GetMouseButtonDown(1))
            {
                if (_placementArmed)
                {
                    _placementArmed = false;
                    _tool = AutomationBuilderTool.Select;
                    InfoStore.Add("Automation Builder breadboard placement cancelled.");
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                    return;
                }

                if (IsLinkTool(_tool))
                {
                    SetTool(AutomationBuilderTool.Select);
                    InfoStore.Add("Automation Builder link mode cancelled.");
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                    return;
                }

                ClearSelection();
                if (TryOpenAutomationContextMenu())
                {
                    AutomationBuilderInputScope.ClaimBuildInputForFrames();
                    return;
                }

                SetTool(AutomationBuilderTool.Select);
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            if (_placementArmed || _tool == AutomationBuilderTool.PlaceBreadboard)
            {
                TryPlaceBreadboardAtPointer();
                AutomationBuilderInputScope.ClaimBuildInputForFrames();
                return;
            }

            if (!TryResolvePointedBlock(out AutomationBlockRef blockRef, out _))
                return;

            HandleBlockClick(blockRef);
            AutomationBuilderInputScope.ClaimBuildInputForFrames();
        }

        private void HandleBlockClick(AutomationBlockRef clicked)
        {
            if (clicked == null)
                return;

            if (_selectedBlock == null)
            {
                SelectBlock(clicked);
                return;
            }

            if (TryCreateLink(_selectedBlock, clicked))
            {
                if (IsLinkTool(_tool))
                    SetTool(AutomationBuilderTool.Select);
                return;
            }

            SelectBlock(clicked);
        }

        private static bool IsLinkTool(AutomationBuilderTool tool) =>
            tool == AutomationBuilderTool.LinkInput ||
            tool == AutomationBuilderTool.LinkOutput;

        private bool TryCreateLink(AutomationBlockRef first, AutomationBlockRef second)
        {
            if (first == null || second == null || first.SameBlock(second))
                return false;

            AutomationBlockRef breadboard = null;
            AutomationBlockRef target = null;
            AutomationLinkKind kind;

            if (_tool == AutomationBuilderTool.LinkInput)
            {
                if (first.IsBreadboard && !second.IsBreadboard)
                {
                    breadboard = first;
                    target = second;
                }
                else if (!first.IsBreadboard && second.IsBreadboard)
                {
                    target = first;
                    breadboard = second;
                }

                if (breadboard == null)
                    return false;

                kind = AutomationLinkKind.InputToBreadboard;
                return AddLink(target, breadboard, kind);
            }

            if (_tool == AutomationBuilderTool.LinkOutput)
            {
                if (first.IsBreadboard && !second.IsBreadboard)
                {
                    breadboard = first;
                    target = second;
                }
                else if (!first.IsBreadboard && second.IsBreadboard)
                {
                    breadboard = second;
                    target = first;
                }

                if (breadboard == null)
                    return false;

                kind = AutomationLinkKind.BreadboardToOutput;
                return AddLink(breadboard, target, kind);
            }

            if (first.IsBreadboard && !second.IsBreadboard)
                return AddLink(first, second, AutomationLinkKind.BreadboardToOutput);
            if (!first.IsBreadboard && second.IsBreadboard)
                return AddLink(first, second, AutomationLinkKind.InputToBreadboard);

            return false;
        }

        private bool AddLink(
            AutomationBlockRef source,
            AutomationBlockRef target,
            AutomationLinkKind kind)
        {
            if (source == null || target == null)
                return false;

            string propertyText = string.IsNullOrWhiteSpace(_propertyText)
                ? "value"
                : _propertyText.Trim();
            RefreshNativeAutomationCache(force: true);
            if (_links.Any(link =>
                    link.Kind == kind &&
                    link.Source != null &&
                    link.Target != null &&
                    link.Source.SameBlock(source) &&
                    link.Target.SameBlock(target) &&
                    string.Equals(link.Property, propertyText, StringComparison.OrdinalIgnoreCase)))
            {
                InfoStore.Add("Automation Builder link already exists.");
                SelectLink(_links.First(link =>
                    link.Kind == kind &&
                    link.Source != null &&
                    link.Target != null &&
                    link.Source.SameBlock(source) &&
                    link.Target.SameBlock(target) &&
                    string.Equals(link.Property, propertyText, StringComparison.OrdinalIgnoreCase)));
                return true;
            }

            AutomationLink stagedLink = CreateStagedLink(source, target, kind, propertyText);
            _links.Add(stagedLink);
            SelectLink(stagedLink);
            EnsureStagedLinkGraphNode(stagedLink);

            if (Time.unscaledTime - _lastLinkNotificationTime > 0.2f)
            {
                InfoStore.Add(kind == AutomationLinkKind.InputToBreadboard
                    ? "Automation input staged: " + BlockRefCurrentName(source) + " -> " + BlockRefCurrentName(target) + "."
                    : "Automation output staged: " + BlockRefCurrentName(source) + " -> " + BlockRefCurrentName(target) + ".");
                _lastLinkNotificationTime = Time.unscaledTime;
            }

            MarkAutomationDirty();
            return true;
        }

        private AutomationGraphNode EnsureStagedLinkGraphNode(AutomationLink link)
        {
            if (link == null ||
                link.Kind != AutomationLinkKind.InputToBreadboard &&
                link.Kind != AutomationLinkKind.BreadboardToOutput)
            {
                return null;
            }

            AutomationBlockRef breadboard = link.Kind == AutomationLinkKind.InputToBreadboard
                ? link.Target
                : link.Source;
            if (breadboard == null || !breadboard.IsBreadboard)
                return null;

            _selectedBreadboard = breadboard;
            AutomationNodeKind kind = link.Kind == AutomationLinkKind.InputToBreadboard
                ? AutomationNodeKind.InputGetter
                : AutomationNodeKind.OutputSetter;
            AutomationGraph graph = SyncedGraphFor(breadboard);
            AutomationGraphNode existing = graph.Nodes.FirstOrDefault(node => NodeBindingMatchesLink(node, link));
            if (existing != null)
                return existing;

            Rect rect = AutoLinkNodeRect(graph, kind);
            AutomationGraphNode node = graph.AddStagedNode(kind, rect);
            if (TryApplyNativeLinkTarget(node, link, out string message) &&
                !string.IsNullOrWhiteSpace(message))
            {
                InfoStore.Add(message);
            }

            graph.SelectedNodeId = node.Id;
            return node;
        }

        private Rect AutoLinkNodeRect(
            AutomationGraph graph,
            AutomationNodeKind kind)
        {
            Rect rect = VisibleWorkspaceDropGraphRect(kind);
            if (_canvasOpen && _lastCanvasWorkspaceRect.width > 1f && _lastCanvasWorkspaceRect.height > 1f)
                return rect;

            int index = graph?.Nodes.Count ?? 0;
            return new Rect(
                EsuHudLayout.Scale(80f) + (index % 2) * (GraphNodeWidthForKind(kind) + EsuHudLayout.Scale(46f)),
                EsuHudLayout.Scale(56f) + index * EsuHudLayout.Scale(128f),
                GraphNodeWidthForKind(kind),
                GraphNodeHeightForKind(kind));
        }

        private static bool NodeBindingMatchesLink(
            AutomationGraphNode node,
            AutomationLink link)
        {
            return NodeBindingTargetsMatchLink(node, link) &&
                   string.Equals(node.TargetBinding.Property, link.Property, StringComparison.OrdinalIgnoreCase);
        }

        private static bool NodeBindingTargetsMatchLink(
            AutomationGraphNode node,
            AutomationLink link)
        {
            if (node?.TargetBinding == null || link == null)
                return false;

            return node.TargetBinding.Kind == link.Kind &&
                   node.TargetBinding.Source?.SameBlock(link.Source) == true &&
                   node.TargetBinding.Target?.SameBlock(link.Target) == true;
        }

        private AutomationLink CreateStagedLink(
            AutomationBlockRef source,
            AutomationBlockRef target,
            AutomationLinkKind kind,
            string propertyText)
        {
            int id = _nextLinkId++;
            return new AutomationLink(
                id,
                source,
                target,
                kind,
                propertyText,
                LinkColor(id),
                nativeComponent: null,
                nativeStatus: "staged");
        }

        private static bool LinksMatch(
            AutomationLink left,
            AutomationLink right)
        {
            return left != null &&
                   right != null &&
                   left.Kind == right.Kind &&
                   left.Source != null &&
                   right.Source != null &&
                   left.Target != null &&
                   right.Target != null &&
                   LinkBlockRefsMatch(left.Source, right.Source) &&
                   LinkBlockRefsMatch(left.Target, right.Target) &&
                   string.Equals(left.Property, right.Property, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LinkBlockRefsMatch(
            AutomationBlockRef left,
            AutomationBlockRef right)
        {
            if (left == null || right == null)
                return false;

            if (left.SameBlock(right))
                return true;

            return !left.IsResolved &&
                   !right.IsResolved &&
                   string.Equals(left.StableKey, right.StableKey, StringComparison.Ordinal);
        }

        private void RemoveSelectedLink()
        {
            if (_selectedLink == null)
                return;

            if (!RemoveAutomationLink(_selectedLink))
            {
                RefreshNativeAutomationCache(force: true);
                CloseAutomationContextMenu();
                return;
            }

            RefreshNativeAutomationCache(force: true);
            InfoStore.Add("Automation Builder link removed or staged for Apply.");
            MarkAutomationDirty();
            _selectedLink = null;
            CloseAutomationContextMenu();
        }

        private bool TryOpenAutomationContextMenu()
        {
            if (!TryResolvePointedBlock(out AutomationBlockRef blockRef, out _))
                return false;

            RefreshNativeAutomationCache(force: true);
            _contextBlock = blockRef;
            _contextLink = FirstContextLink(blockRef);
            _viewModeMenuOpen = false;
            _contextMenuRect = AutomationContextRect(AutomationContextButtonCount(_contextLink != null));
            return true;
        }

        private Rect AutomationContextRect(int buttonCount)
        {
            Vector2 mouse = MouseGuiPosition();
            float width = EsuHudLayout.Scale(166f);
            float height = EsuHudLayout.Scale(34f + Mathf.Max(1, buttonCount) * 26f);
            var rect = new Rect(mouse.x, mouse.y, width, height);
            rect.x = Mathf.Clamp(rect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
            rect.y = Mathf.Clamp(rect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
            return rect;
        }

        private static int AutomationContextButtonCount(bool hasContextLink) =>
            hasContextLink ? 5 : 4;

        private void DrawAutomationContextMenu()
        {
            if (_contextBlock == null)
                return;

            if (_closePromptOpen || _canvasOpen)
            {
                CloseAutomationContextMenu();
                return;
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_contextMenuRect.Contains(current.mousePosition))
            {
                CloseAutomationContextMenu();
                return;
            }

            if (!AutomationContextTargetValid())
            {
                CloseAutomationContextMenu();
                return;
            }

            _contextMenuRect = GUI.Window(
                _contextMenuWindowId,
                _contextMenuRect,
                DrawAutomationContextMenuWindow,
                GUIContent.none,
                GUIStyle.none);
            GUI.BringWindowToFront(_contextMenuWindowId);

            if (ShouldConsumeAutomationContextEvent(current))
                current.Use();
        }

        private void DrawAutomationContextMenuWindow(int id)
        {
            Rect frame = new Rect(0f, 0f, _contextMenuRect.width, _contextMenuRect.height);
            GUI.Box(frame, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.LocalPanelInnerRect(_contextMenuRect.width, _contextMenuRect.height, 5f));
            GUILayout.Label(_contextBlock.IsBreadboard ? "Breadboard" : "Block", DecorationEditorTheme.SubHeader);

            AutomationContextAction action = AutomationContextAction.None;
            if (GUILayout.Button(
                    new GUIContent("Delete", "Delete this block from the construct. Undo can restore it."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action = AutomationContextAction.DeleteBlock;
            }

            if (_contextLink != null &&
                GUILayout.Button(
                    new GUIContent("Remove Link", "Remove one Automation link touching this block."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action = AutomationContextAction.RemoveLink;
            }

            if (GUILayout.Button(
                    new GUIContent("All Links", "Show input and output Automation links."),
                    DecorationEditorTheme.ToolButton(_linkVisibility == AutomationLinkVisibility.All),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action = AutomationContextAction.ShowAllLinks;
            }

            if (GUILayout.Button(
                    new GUIContent("Only Inputs", "Show only block-to-breadboard input links."),
                    DecorationEditorTheme.ToolButton(_linkVisibility == AutomationLinkVisibility.InputOnly),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action = AutomationContextAction.ShowInputLinks;
            }

            if (GUILayout.Button(
                    new GUIContent("Only Outputs", "Show only breadboard-to-block output links."),
                    DecorationEditorTheme.ToolButton(_linkVisibility == AutomationLinkVisibility.OutputOnly),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action = AutomationContextAction.ShowOutputLinks;
            }

            GUILayout.EndArea();
            ExecuteAutomationContextAction(action);
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

        private bool AutomationContextTargetValid() =>
            _contextBlock != null &&
            _contextBlock.TryGetBlock(out _);

        private void ExecuteAutomationContextAction(AutomationContextAction action)
        {
            switch (action)
            {
                case AutomationContextAction.DeleteBlock:
                    DeleteAutomationContextBlock();
                    break;
                case AutomationContextAction.RemoveLink:
                    RemoveAutomationContextLink();
                    break;
                case AutomationContextAction.ShowAllLinks:
                    SetAutomationLinkVisibility(AutomationLinkVisibility.All);
                    break;
                case AutomationContextAction.ShowInputLinks:
                    SetAutomationLinkVisibility(AutomationLinkVisibility.InputOnly);
                    break;
                case AutomationContextAction.ShowOutputLinks:
                    SetAutomationLinkVisibility(AutomationLinkVisibility.OutputOnly);
                    break;
            }
        }

        private void DeleteAutomationContextBlock()
        {
            AutomationBlockRef blockRef = _contextBlock;
            if (blockRef == null)
                return;

            List<AutomationLink> attachedLinks = ContextLinksForBlock(blockRef).ToList();
            if (!AutomationBuilderBlockCommands.TryDeleteBlock(
                    _build,
                    blockRef.Construct,
                    blockRef.Cell,
                    out string message))
            {
                InfoStore.Add(message);
                CloseAutomationContextMenu();
                return;
            }

            if (!blockRef.IsBreadboard)
            {
                foreach (AutomationLink link in attachedLinks)
                    RemoveAutomationLink(link);
            }

            if (_selectedBlock != null && _selectedBlock.SameBlock(blockRef))
                _selectedBlock = null;
            if (_selectedBreadboard != null && _selectedBreadboard.SameBlock(blockRef))
            {
                _graphs.Remove(_selectedBreadboard.StableKey);
                _selectedBreadboard = null;
                _links.Clear();
            }
            else
            {
                RefreshNativeAutomationCache(force: true);
            }

            _selectedLink = null;
            MarkAutomationDirty();
            InfoStore.Add(message);
            CloseAutomationContextMenu();
        }

        private void RemoveAutomationContextLink()
        {
            AutomationLink link = _contextLink;
            if (link == null)
                return;

            if (!RemoveAutomationLink(link))
            {
                RefreshNativeAutomationCache(force: true);
                CloseAutomationContextMenu();
                return;
            }

            RefreshNativeAutomationCache(force: true);
            MarkAutomationDirty();
            InfoStore.Add("Automation Builder link removed or staged for Apply.");
            _selectedLink = null;
            CloseAutomationContextMenu();
        }

        private bool RemoveAutomationLink(AutomationLink link)
        {
            if (link == null)
                return false;

            if (link.NativeComponent != null)
            {
                return RemoveNativeLink(link);
            }
            else
            {
                RemoveStagedLinkGraphNodes(link);
                _links.Remove(link);
                return true;
            }
        }

        private void RemoveStagedLinkGraphNodes(AutomationLink link)
        {
            AutomationBlockRef breadboard = BreadboardForLink(link);
            if (breadboard == null)
                return;

            AutomationGraph graph = GraphFor(breadboard);
            foreach (AutomationGraphNode node in graph.Nodes
                         .Where(node => node?.IsStaged == true && NodeBindingMatchesLink(node, link))
                         .ToList())
            {
                RemoveGraphNode(graph, node);
            }

            foreach (AutomationGraphNode node in graph.Nodes
                         .Where(node => node != null && !node.IsStaged && NodeBindingMatchesLink(node, link))
                         .ToList())
            {
                ClearStagedLinkTargetBinding(node);
            }
        }

        private static AutomationBlockRef BreadboardForLink(AutomationLink link)
        {
            if (link == null)
                return null;

            return link.Kind == AutomationLinkKind.InputToBreadboard
                ? link.Target
                : link.Source;
        }

        private void SetAutomationLinkVisibility(AutomationLinkVisibility visibility)
        {
            _linkVisibility = visibility;
            InfoStore.Add("Automation Builder links: " + AutomationLinkVisibilityLabel(visibility) + ".");
            CloseAutomationContextMenu();
        }

        private static string AutomationLinkVisibilityLabel(AutomationLinkVisibility visibility)
        {
            switch (visibility)
            {
                case AutomationLinkVisibility.InputOnly:
                    return "inputs only";
                case AutomationLinkVisibility.OutputOnly:
                    return "outputs only";
                default:
                    return "all";
            }
        }

        private AutomationLink FirstContextLink(AutomationBlockRef blockRef)
        {
            if (_selectedLink != null && LinkTouchesBlock(_selectedLink, blockRef))
                return _selectedLink;

            return ContextLinksForBlock(blockRef).FirstOrDefault();
        }

        private IEnumerable<AutomationLink> ContextLinksForBlock(AutomationBlockRef blockRef)
        {
            if (blockRef == null)
                yield break;

            foreach (AutomationLink link in _links)
            {
                if (LinkTouchesBlock(link, blockRef))
                    yield return link;
            }
        }

        private static bool LinkTouchesBlock(AutomationLink link, AutomationBlockRef blockRef) =>
            link != null &&
            blockRef != null &&
            (link.Source?.SameBlock(blockRef) == true ||
             link.Target?.SameBlock(blockRef) == true);

        private void CloseAutomationContextMenu()
        {
            _contextBlock = null;
            _contextLink = null;
        }

        private void ClearSelection()
        {
            _selectedBlock = null;
            _selectedLink = null;
            CloseAutomationContextMenu();
        }

        private void ApplyFocusView()
        {
            try
            {
                _buildOptions = ProfileManager.Instance.GetModule<MBuild_Ftd>();
                if (_buildOptions != null)
                {
                    _viewModes.Capture(_buildOptions);
                    ApplyAutomationViewMode();
                }
            }
            catch
            {
                _buildOptions = null;
            }
        }

        private void ApplyAutomationViewMode()
        {
            try
            {
                if (_buildOptions == null)
                    return;

                _viewModes.Apply(_viewMode);
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception(
                    "Automation Builder",
                    exception,
                    "Automation Builder view mode apply failed");
            }
        }

        private void RestoreFocusView()
        {
            Exception failure = null;
            try
            {
                _viewModes.Restore();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _buildOptions = null;
            }

            if (failure != null)
            {
                EsuRuntimeLog.Exception(
                    "Automation Builder",
                    failure,
                    "Automation Builder focus restore failed");
            }
        }

        private void SelectBlock(AutomationBlockRef blockRef, bool notify = true)
        {
            _selectedBlock = blockRef;
            _selectedLink = null;
            if (blockRef.IsBreadboard)
            {
                _selectedBreadboard = blockRef;
                ForceRefreshSelectedBreadboardNativeState();
            }
            if (notify)
                InfoStore.Add("Automation Builder selected: " + blockRef.Name + ".");
        }

        private void RefreshSelectionFromPointer()
        {
            if (_selectedBlock != null)
                return;

            if (TryResolvePointedBlock(out AutomationBlockRef blockRef, out _) &&
                blockRef.IsBreadboard)
            {
                _selectedBreadboard = blockRef;
            }
        }

        private bool TryPlaceBreadboardAtPointer()
        {
            if (!AutomationBreadboardCatalog.TryResolveBreadboard(
                    _selectedBreadboardVariant,
                    out ItemDefinition definition,
                    out string resolveMessage))
            {
                InfoStore.Add(resolveMessage);
                return false;
            }

            if (!TryPlacementCandidate(out AllConstruct construct, out Vector3i cell, out string reason))
            {
                InfoStore.Add(reason ?? "Point at a craft surface before placing the breadboard.");
                return false;
            }

            if (!AutomationBuilderBlockCommands.TryPlaceBreadboard(
                    _build,
                    construct,
                    cell,
                    definition,
                    out string message))
            {
                InfoStore.Add(message);
                return false;
            }

            var placed = new AutomationBlockRef(
                construct,
                cell,
                AutomationBreadboardCatalog.ItemName(definition),
                isBreadboard: true);
            SelectBlock(placed);
            _selectedBreadboard = placed;
            _placementArmed = false;
            _tool = AutomationBuilderTool.Select;
            InfoStore.Add(message);
            return true;
        }

        private bool TryPlacementCandidate(
            out AllConstruct construct,
            out Vector3i cell,
            out string reason)
        {
            construct = null;
            cell = default;
            reason = null;
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit?.Construct == null)
            {
                reason = "Point at a craft surface before placing the breadboard.";
                return false;
            }

            construct = hit.Construct;
            Vector3i normal = ResolvePlacementNormal(hit);
            cell = hit.Anchor + normal;
            try
            {
                if (construct.AllBasics.GetBlockViaLocalPosition(cell) != null)
                {
                    reason = "The breadboard target cell is occupied.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "Breadboard placement preflight failed: " + exception.Message;
                return false;
            }

            return true;
        }

        private static Vector3i ResolvePlacementNormal(DecorationPointerHit hit)
        {
            if (hit != null &&
                DecorationPointerProbe.TryGetLocalFaceNormal(hit.Anchor, hit.LocalHit, out Vector3 normal))
            {
                return DominantAxis(normal);
            }

            return new Vector3i(0, 1, 0);
        }

        private bool TryResolvePointedBlock(
            out AutomationBlockRef blockRef,
            out DecorationPointerHit hit)
        {
            blockRef = null;
            hit = null;
            if (!_pointerProbe.TryProbe(out hit) ||
                hit?.Construct == null)
            {
                return false;
            }

            Block block = null;
            try
            {
                block = hit.Construct.AllBasics.GetBlockViaLocalPosition(hit.Anchor);
            }
            catch
            {
                return false;
            }

            if (block == null || block.IsDeleted)
                return false;

            blockRef = new AutomationBlockRef(
                hit.Construct,
                hit.Anchor,
                AutomationBreadboardCatalog.BlockName(block),
                AutomationBreadboardCatalog.IsBreadboardBlock(block));
            return true;
        }

        private void RefreshMouseOverUiFromCurrentPointer()
        {
            Vector2 mouse = MouseGuiPosition();
            bool overUi = _closePromptOpen ||
                          _toolbarRect.Contains(mouse) ||
                          _statusRect.Contains(mouse) ||
                          EsuHudNotifications.ContainsMouse(mouse) ||
                          (_viewModeMenuOpen && ViewModeMenuRect(_toolbarRect).Contains(mouse)) ||
                          (_contextBlock != null && _contextMenuRect.Contains(mouse)) ||
                          (_canvasOpen && _canvasRect.Contains(mouse)) ||
                          (!_canvasOpen && _showLeftPanel && _leftPanelRect.Contains(mouse)) ||
                          (!_canvasOpen && _showRightPanel && _rightPanelRect.Contains(mouse));
            AutomationBuilderInputScope.SetMouseOverUi(overUi);
            if (overUi &&
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                AutomationBuilderInputScope.ClaimMouseWheelInputForFrames();
            }
        }

        private void DrawWorldPreview()
        {
            DecorationEditorOverlay.BeginFrame();
            if (!Active)
                return;

            if (_placementArmed || _tool == AutomationBuilderTool.PlaceBreadboard)
                DrawPlacementGhost();

            DrawAutomationLinks();
            DrawSelectedBlockHighlight();
        }

        private void DrawPlacementGhost()
        {
            bool valid = TryPlacementCandidate(
                out AllConstruct construct,
                out Vector3i cell,
                out _);
            if (construct == null)
                return;

            Color color = valid
                ? new Color(0.05f, 0.95f, 1f, 0.72f)
                : new Color(1f, 0.25f, 0.2f, 0.72f);
            DrawCellWire(construct, cell, color, valid ? 3.2f : 2.4f);
        }

        private void DrawAutomationLinks()
        {
            foreach (AutomationLink link in _links)
            {
                if (!ShouldDrawAutomationLink(link))
                    continue;

                if (link.Source == null ||
                    link.Target == null ||
                    !link.Source.TryWorldCenter(out Vector3 start) ||
                    !link.Target.TryWorldCenter(out Vector3 end))
                {
                    continue;
                }

                Color color = link.Color;
                bool selected = ReferenceEquals(link, _selectedLink);
                Color linkColor = new Color(color.r, color.g, color.b, selected ? 0.95f : 0.7f);
                DrawAutomationLinkEndpointWireframes(link, linkColor, selected);
                DecorationEditorOverlay.Arrow(start, end, linkColor, selected ? 4.2f : 2.8f, 0.24f);
                DrawFlowPulse(start, end, color, link.Id);
            }
        }

        private bool ShouldDrawAutomationLink(AutomationLink link)
        {
            if (link == null)
                return false;

            switch (_linkVisibility)
            {
                case AutomationLinkVisibility.InputOnly:
                    return link.Kind == AutomationLinkKind.InputToBreadboard;
                case AutomationLinkVisibility.OutputOnly:
                    return link.Kind == AutomationLinkKind.BreadboardToOutput;
                default:
                    return true;
            }
        }

        private static void DrawFlowPulse(Vector3 start, Vector3 end, Color color, int seed)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
                return;

            Vector3 direction = delta / length;
            float t = Mathf.Repeat(Time.unscaledTime * 0.9f + seed * 0.17f, 1f);
            float segmentLength = Mathf.Min(0.9f, length * 0.35f);
            Vector3 pulseStart = Vector3.Lerp(start, end, t);
            Vector3 pulseEnd = pulseStart + direction * segmentLength;
            if ((pulseEnd - start).magnitude > length)
                pulseEnd = end;
            DecorationEditorOverlay.Line(pulseStart, pulseEnd, new Color(color.r, color.g, color.b, 1f), 5.2f);
        }

        private static void DrawAutomationLinkEndpointWireframes(
            AutomationLink link,
            Color color,
            bool selected)
        {
            if (link == null)
                return;

            Color wireColor = new Color(color.r, color.g, color.b, selected ? 1f : 0.78f);
            float width = selected ? 4f : 2.7f;
            DrawLinkEndpointWireframe(link.Source, wireColor, width);
            DrawLinkEndpointWireframe(link.Target, wireColor, width);
        }

        private static void DrawLinkEndpointWireframe(
            AutomationBlockRef blockRef,
            Color color,
            float width)
        {
            if (blockRef?.Construct == null)
                return;

            DrawCellWire(blockRef.Construct, blockRef.Cell, color, width);
        }

        private void DrawSelectedBlockHighlight()
        {
            if (_selectedBlock == null ||
                !_selectedBlock.TryWorldCenter(out Vector3 world))
            {
                return;
            }

            Color color = _selectedBlock.IsBreadboard
                ? DecorationEditorTheme.Cyan
                : new Color(1f, 0.72f, 0.2f, 1f);
            DecorationEditorOverlay.Circle(world, 0.58f, color, Vector3.up, 3.4f, 28);
            DecorationEditorOverlay.Cross(world, 0.38f, color, 3f);
        }

        private static void DrawCellWire(
            AllConstruct construct,
            Vector3i cell,
            Color color,
            float width)
        {
            Vector3 center = new Vector3(cell.x, cell.y, cell.z);
            Vector3[] corners =
            {
                center + new Vector3(-0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(-0.5f, -0.5f, 0.5f),
                center + new Vector3(-0.5f, 0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                center + new Vector3(-0.5f, 0.5f, 0.5f)
            };

            for (int index = 0; index < corners.Length; index++)
                corners[index] = construct.SafeLocalToGlobal(corners[index]);

            DrawEdge(corners, 0, 1, color, width);
            DrawEdge(corners, 1, 2, color, width);
            DrawEdge(corners, 2, 3, color, width);
            DrawEdge(corners, 3, 0, color, width);
            DrawEdge(corners, 4, 5, color, width);
            DrawEdge(corners, 5, 6, color, width);
            DrawEdge(corners, 6, 7, color, width);
            DrawEdge(corners, 7, 4, color, width);
            DrawEdge(corners, 0, 4, color, width);
            DrawEdge(corners, 1, 5, color, width);
            DrawEdge(corners, 2, 6, color, width);
            DrawEdge(corners, 3, 7, color, width);
        }

        private static void DrawEdge(Vector3[] corners, int from, int to, Color color, float width) =>
            DecorationEditorOverlay.Line(corners[from], corners[to], color, width);

        private static void DrawPanelHeader(string text, string iconKey, ref bool panelVisible)
        {
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(text, iconKey);
            if (GUILayout.Button(
                    new GUIContent("Hide", "Hide this Automation Builder panel."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                panelVisible = false;
            }

            GUILayout.EndHorizontal();
        }

        private static bool DrawSectionHeader(string text, ref bool sectionVisible, string tooltip)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(text, DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(sectionVisible ? "Hide list" : "Show list", tooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                sectionVisible = !sectionVisible;
            }

            GUILayout.EndHorizontal();
            return sectionVisible;
        }

        private static void DrawCompactIconHeader(string text, string iconKey)
        {
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(EsuHudLayout.CompactHeaderHeightBase),
                GUILayout.ExpandWidth(true));
            GUI.Label(rect, "      " + text, DecorationEditorTheme.SubHeader);

            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            if (icon == null)
                return;

            var iconRect = new Rect(
                rect.x + EsuHudLayout.Scale(5f),
                rect.y + EsuHudLayout.Scale(3f),
                EsuHudLayout.Scale(16f),
                EsuHudLayout.Scale(16f));
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        private static void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(82f)));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.BodyWrap);
            GUILayout.EndHorizontal();
        }

        private bool IconButton(string icon, string label, GUIStyle style, string tooltip) =>
            GUILayout.Button(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                style,
                GUILayout.Width(EsuHudLayout.Scale(54f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));

        private bool IconButton(string icon, string label, GUIStyle style, string tooltip, bool enabled)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            try
            {
                GUIStyle actual = enabled ? style : DecorationEditorTheme.DisabledButton;
                return GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    actual,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f)));
            }
            finally
            {
                GUI.enabled = previous;
            }
        }

        private bool CompactIconButton(
            string icon,
            string label,
            GUIStyle style,
            string tooltip,
            bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            try
            {
                GUIStyle actual = enabled ? style : DecorationEditorTheme.DisabledButton;
                return GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    actual,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f)));
            }
            finally
            {
                GUI.enabled = previous;
            }
        }

        private static void DrawCyanLine(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = DecorationEditorTheme.Cyan;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Vector3i DominantAxis(Vector3 value)
        {
            float x = Mathf.Abs(value.x);
            float y = Mathf.Abs(value.y);
            float z = Mathf.Abs(value.z);
            if (x >= y && x >= z)
                return new Vector3i(value.x >= 0f ? 1 : -1, 0, 0);
            if (y >= z)
                return new Vector3i(0, value.y >= 0f ? 1 : -1, 0);
            return new Vector3i(0, 0, value.z >= 0f ? 1 : -1);
        }

        private static string FormatCell(Vector3i cell) =>
            cell.x.ToString(CultureInfo.InvariantCulture) + "," +
            cell.y.ToString(CultureInfo.InvariantCulture) + "," +
            cell.z.ToString(CultureInfo.InvariantCulture);

        private static Vector2 MouseGuiPosition() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        private static Rect PanelHeaderDragRect(Rect panelRect, float reservedRightBase)
        {
            float height = EsuHudLayout.Scale(40f);
            float reservedRight = EsuHudLayout.Scale(reservedRightBase);
            return new Rect(
                panelRect.x,
                panelRect.y,
                Mathf.Max(1f, panelRect.width - reservedRight),
                Mathf.Min(height, panelRect.height));
        }

        private static bool ContainsMouse(Rect rect) =>
            rect.Contains(MouseGuiPosition());

        private static bool ShouldConsumeGuiEvent(Event current)
        {
            if (current == null)
                return false;

            return current.type == EventType.MouseDown ||
                   current.type == EventType.MouseUp ||
                   current.type == EventType.MouseDrag ||
                   current.type == EventType.ScrollWheel;
        }

        private bool IsGraphDirty() => HasUnappliedAutomationChanges;

        private static Color LinkColor(int seed)
        {
            float hue = Mathf.Repeat(seed * 0.177f, 1f);
            return Color.HSVToRGB(hue, 0.78f, 1f);
        }

        private static Color NodeColor(AutomationNodeKind kind)
        {
            return PaletteCategoryColor(PaletteCategoryForNode(kind));
        }

        private static Color NodePanelColor(AutomationNodeKind kind)
        {
            Color color = NodeColor(kind);
            return new Color(color.r * 0.16f, color.g * 0.22f, color.b * 0.24f, 0.92f);
        }

        private static AutomationPaletteCategory PaletteCategoryForNode(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return AutomationPaletteCategory.Input;
                case AutomationNodeKind.OutputSetter:
                    return AutomationPaletteCategory.Output;
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return AutomationPaletteCategory.Control;
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                case AutomationNodeKind.Clamp:
                case AutomationNodeKind.Smooth:
                    return AutomationPaletteCategory.Math;
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.Random:
                    return AutomationPaletteCategory.Variables;
                case AutomationNodeKind.Comment:
                    return AutomationPaletteCategory.Notation;
                default:
                    return AutomationPaletteCategory.Output;
            }
        }

        private static Color PaletteCategoryColor(AutomationPaletteCategory category)
        {
            switch (category)
            {
                case AutomationPaletteCategory.Input:
                    return new Color(0.13f, 0.82f, 0.42f, 1f);
                case AutomationPaletteCategory.Control:
                    return new Color(1f, 0.58f, 0.18f, 1f);
                case AutomationPaletteCategory.Math:
                    return new Color(0.66f, 0.42f, 1f, 1f);
                case AutomationPaletteCategory.Variables:
                    return new Color(0.96f, 0.34f, 0.38f, 1f);
                case AutomationPaletteCategory.Notation:
                    return new Color(0.62f, 0.76f, 0.84f, 1f);
                default:
                    return new Color(0.04f, 0.66f, 0.94f, 1f);
            }
        }

        private static AutomationBlockShape BlockShape(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                    return AutomationBlockShape.Control;
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.Random:
                    return AutomationBlockShape.Value;
                case AutomationNodeKind.Comment:
                    return AutomationBlockShape.Notation;
                default:
                    return AutomationBlockShape.Stack;
            }
        }

        private static float GraphNodeHeightForKind(AutomationNodeKind kind)
        {
            if (kind == AutomationNodeKind.IfCondition)
                return 420f;
            if (kind == AutomationNodeKind.IfLessThan)
                return 492f;
            if (kind == AutomationNodeKind.Forever)
                return 244f;
            if (kind == AutomationNodeKind.InputGetter)
                return 154f;
            if (kind == AutomationNodeKind.OutputSetter)
                return 284f;
            if (kind == AutomationNodeKind.LogicNot)
                return 178f;
            if (IsLogicGateKind(kind))
            {
                return 246f;
            }
            if (IsFuzzyThresholdKind(kind))
                return 246f;
            if (IsMathEvaluatorKind(kind))
                return 246f;
            if (IsMaxMinKind(kind))
                return 246f;
            if (kind == AutomationNodeKind.Clamp)
                return 324f;
            if (kind == AutomationNodeKind.Smooth)
                return 246f;
            if (kind == AutomationNodeKind.Random)
                return 108f;

            switch (BlockShape(kind))
            {
                case AutomationBlockShape.Control:
                    return 244f;
                case AutomationBlockShape.Value:
                    return 78f;
                case AutomationBlockShape.Notation:
                    return 108f;
                default:
                    return 116f;
            }
        }

        private static float GraphNodeWidthForKind(AutomationNodeKind kind)
        {
            return BlockShape(kind) == AutomationBlockShape.Value
                ? GraphValueNodeWidth
                : GraphNodeWidth;
        }

        private static Rect NormalizeGraphNodeRect(
            AutomationNodeKind kind,
            Rect rect,
            bool valueFootprint = false,
            bool preserveCenter = false)
        {
            Vector2 center = rect.center;
            float minWidth = valueFootprint
                ? GraphValueNodeWidth
                : GraphNodeWidthForKind(kind);
            float minHeight = valueFootprint
                ? BlockShape(kind) == AutomationBlockShape.Value
                    ? GraphNodeHeightForKind(kind)
                    : GraphNodeHeightForKind(AutomationNodeKind.Constant)
                : GraphNodeHeightForKind(kind);

            rect.width = Mathf.Max(minWidth, rect.width);
            rect.height = Mathf.Max(minHeight, rect.height);
            if (!valueFootprint &&
                (kind == AutomationNodeKind.InputGetter ||
                 kind == AutomationNodeKind.OutputSetter ||
                 IsMathEvaluatorKind(kind)))
            {
                rect.width = Mathf.Max(minWidth, rect.width);
                rect.height = minHeight;
            }

            if (preserveCenter)
                rect.center = center;

            rect.x = Mathf.Max(0f, rect.x);
            rect.y = Mathf.Max(0f, rect.y);
            return rect;
        }

        private static bool AcceptsValueSlot(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.IfLessThan ||
                   kind == AutomationNodeKind.IfCondition ||
                   kind == AutomationNodeKind.OutputSetter ||
                   IsLogicGateKind(kind) ||
                   IsFuzzyThresholdKind(kind) ||
                   IsMathEvaluatorKind(kind) ||
                   IsMaxMinKind(kind) ||
                   kind == AutomationNodeKind.Clamp ||
                   kind == AutomationNodeKind.Smooth;
        }

        private static bool AcceptsControlBody(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.Forever ||
                   kind == AutomationNodeKind.IfCondition ||
                   kind == AutomationNodeKind.IfLessThan;
        }

        private static bool IsBodyFlowNode(AutomationGraphNode node)
        {
            return node != null &&
                   !DrawsAsValueBlock(node.Kind, node.Rect) &&
                   node.Kind != AutomationNodeKind.Comment &&
                   node.Kind != AutomationNodeKind.Forever;
        }

        private static bool CanSnapIntoValueSlot(
            AutomationGraphNode node,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (node == null)
                return false;

            return CanNodeKindSnapIntoValueSlot(node.Kind, hostKind, slotKind);
        }

        private static bool CanNodeKindSnapIntoValueSlot(
            AutomationNodeKind nodeKind,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (!CanProduceValueBlock(nodeKind))
                return false;

            if (CanFeedSignalValueSlot(hostKind, slotKind))
                return true;

            return nodeKind == AutomationNodeKind.Constant &&
                   IsConstantOnlyValueSlot(hostKind, slotKind);
        }

        private bool ShouldDrawValueSocketHint(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (host == null)
                return false;

            if (SnappedValueForHost(graph, host, slotKind) != null)
                return true;

            if (IsStackFedPrimaryValueSlot(graph, host, slotKind))
                return false;

            if (IsMathEvaluatorKind(host.Kind) &&
                slotKind == MathOperandSlotKind(host.Kind))
            {
                return IsDraggingValueBlockForSlot(graph, host.Kind, slotKind);
            }

            return true;
        }

        private bool IsDraggingValueBlockForSlot(
            AutomationGraph graph,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (_draggingPaletteBlock)
                return CanNodeKindSnapIntoValueSlot(_draggingPaletteKind, hostKind, slotKind);

            if (graph == null || _draggingNodeId == 0)
                return false;

            AutomationGraphNode dragged = graph.Nodes.FirstOrDefault(node => node.Id == _draggingNodeId);
            return dragged != null &&
                   CanNodeKindSnapIntoValueSlot(dragged.Kind, hostKind, slotKind);
        }

        private static bool CanFeedSignalValueSlot(
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            return slotKind == AutomationValueSlotKind.Pass ||
                   IsLogicGateKind(hostKind) && slotKind == AutomationValueSlotKind.LogicB ||
                   IsMathEvaluatorKind(hostKind) && slotKind == MathOperandSlotKind(hostKind) ||
                   IsMaxMinKind(hostKind) && slotKind == AutomationValueSlotKind.MathB;
        }

        private static bool IsConstantOnlyValueSlot(
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (slotKind == AutomationValueSlotKind.Threshold ||
                slotKind == AutomationValueSlotKind.Else)
            {
                return true;
            }

            if (hostKind == AutomationNodeKind.Clamp &&
                (slotKind == AutomationValueSlotKind.Min ||
                 slotKind == AutomationValueSlotKind.Max))
            {
                return true;
            }

            return hostKind == AutomationNodeKind.Smooth &&
                   slotKind == AutomationValueSlotKind.Seconds;
        }

        private static bool CanProduceValueBlock(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.Random:
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                case AutomationNodeKind.Clamp:
                case AutomationNodeKind.Smooth:
                    return true;
                default:
                    return false;
            }
        }

        private static bool DrawsAsValueBlock(AutomationNodeKind kind, Rect rect)
        {
            return BlockShape(kind) == AutomationBlockShape.Value ||
                   CanProduceValueBlock(kind) && IsValueFootprint(rect);
        }

        private static bool IsValueFootprint(Rect rect)
        {
            return rect.width <= GraphValueNodeWidth + 18f &&
                   rect.height <= GraphNodeHeightForKind(AutomationNodeKind.Constant) + 18f;
        }

        private static Rect ControlBodyRect(Rect hostRect, AutomationNodeKind hostKind)
        {
            if (hostKind == AutomationNodeKind.IfCondition)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(28f),
                    hostRect.y + EsuHudLayout.Scale(258f),
                    Mathf.Max(1f, hostRect.width - EsuHudLayout.Scale(46f)),
                    Mathf.Max(52f, hostRect.height - EsuHudLayout.Scale(282f)));
            }

            if (hostKind == AutomationNodeKind.IfLessThan)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(28f),
                    hostRect.y + EsuHudLayout.Scale(324f),
                    Mathf.Max(1f, hostRect.width - EsuHudLayout.Scale(46f)),
                    Mathf.Max(52f, hostRect.height - EsuHudLayout.Scale(348f)));
            }

            return new Rect(
                hostRect.x + EsuHudLayout.Scale(28f),
                hostRect.y + EsuHudLayout.Scale(76f),
                Mathf.Max(1f, hostRect.width - EsuHudLayout.Scale(46f)),
                Mathf.Max(52f, hostRect.height - EsuHudLayout.Scale(100f)));
        }

        private static Rect ExpandRect(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static float DistanceToRect(Vector2 point, Rect rect)
        {
            float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static IEnumerable<AutomationValueSlotKind> ValueSlotKinds(AutomationNodeKind kind)
        {
            if (kind == AutomationNodeKind.IfCondition)
                return new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.Else };
            if (kind == AutomationNodeKind.IfLessThan)
                return new[] { AutomationValueSlotKind.Threshold, AutomationValueSlotKind.Pass, AutomationValueSlotKind.Else };
            if (kind == AutomationNodeKind.OutputSetter)
                return new[] { AutomationValueSlotKind.Pass };
            if (IsFuzzyThresholdKind(kind))
                return new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.Threshold };
            if (IsMathEvaluatorKind(kind))
                return new[] { AutomationValueSlotKind.Pass, MathOperandSlotKind(kind) };
            if (IsMaxMinKind(kind))
                return new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.MathB };
            if (IsLogicGateKind(kind))
            {
                return kind == AutomationNodeKind.LogicNot
                    ? new[] { AutomationValueSlotKind.Pass }
                    : new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.LogicB };
            }
            if (kind == AutomationNodeKind.Clamp)
                return new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.Min, AutomationValueSlotKind.Max };
            if (kind == AutomationNodeKind.Smooth)
                return new[] { AutomationValueSlotKind.Pass, AutomationValueSlotKind.Seconds };
            return Array.Empty<AutomationValueSlotKind>();
        }

        private static string ValueSlotLabel(AutomationValueSlotKind slotKind)
        {
            switch (slotKind)
            {
                case AutomationValueSlotKind.Threshold:
                    return "threshold";
                case AutomationValueSlotKind.Amount:
                    return "amount";
                case AutomationValueSlotKind.Factor:
                    return "factor";
                case AutomationValueSlotKind.Min:
                    return "min";
                case AutomationValueSlotKind.Max:
                    return "max";
                case AutomationValueSlotKind.Seconds:
                    return "seconds";
                case AutomationValueSlotKind.LogicB:
                case AutomationValueSlotKind.MathB:
                    return "b";
                case AutomationValueSlotKind.Else:
                    return "else";
                default:
                    return "then";
            }
        }

        private static string ValueSlotLabel(
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (hostKind == AutomationNodeKind.OutputSetter)
                return "value";
            if (IsMathEvaluatorKind(hostKind))
                return slotKind == MathOperandSlotKind(hostKind)
                    ? MathOperandLabel(hostKind)
                    : "input";
            if (IsLogicGateKind(hostKind))
                return slotKind == AutomationValueSlotKind.LogicB
                    ? "b"
                    : LogicFirstInputLabel(hostKind);
            if (IsFuzzyThresholdKind(hostKind))
                return slotKind == AutomationValueSlotKind.Threshold
                    ? "threshold const"
                    : "input";
            if (IsMaxMinKind(hostKind))
                return slotKind == AutomationValueSlotKind.MathB
                    ? "b"
                    : "a";
            if (hostKind == AutomationNodeKind.Clamp)
            {
                if (slotKind == AutomationValueSlotKind.Min)
                    return "min const";
                if (slotKind == AutomationValueSlotKind.Max)
                    return "max const";
                return "input";
            }
            if (hostKind == AutomationNodeKind.Smooth)
                return slotKind == AutomationValueSlotKind.Seconds
                    ? "seconds const"
                    : "input";

            if ((hostKind == AutomationNodeKind.IfCondition ||
                 hostKind == AutomationNodeKind.IfLessThan) &&
                slotKind == AutomationValueSlotKind.Else)
            {
                return "else const";
            }

            if (hostKind == AutomationNodeKind.IfLessThan &&
                slotKind == AutomationValueSlotKind.Threshold)
            {
                return "threshold const";
            }

            return ValueSlotLabel(slotKind);
        }

        private static string ControlBodyLabel(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.IfLessThan ||
                   kind == AutomationNodeKind.IfCondition
                ? "driven actions"
                : "body";
        }

        private static Rect ValueSlotRect(Rect hostRect, AutomationNodeKind hostKind)
        {
            return ValueSlotRect(hostRect, hostKind, AutomationValueSlotKind.Pass);
        }

        private static Rect ValueSlotRect(
            Rect hostRect,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            float width = GraphNodeWidthForKind(AutomationNodeKind.Constant);
            float height = GraphNodeHeightForKind(AutomationNodeKind.Constant);
            if (hostKind == AutomationNodeKind.OutputSetter)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(58f),
                    hostRect.y + EsuHudLayout.Scale(172f),
                    width,
                    height);
            }

            if (IsMathEvaluatorKind(hostKind))
            {
                if (slotKind == AutomationValueSlotKind.Pass)
                {
                    return new Rect(
                        hostRect.x + EsuHudLayout.Scale(50f),
                        hostRect.y + EsuHudLayout.Scale(92f),
                        width,
                        height);
                }

                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(160f),
                    width,
                    height);
            }

            if (IsLogicGateKind(hostKind))
            {
                float y = slotKind == AutomationValueSlotKind.LogicB
                    ? 142f
                    : 76f;
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(y),
                    width,
                    height);
            }

            if (IsFuzzyThresholdKind(hostKind))
            {
                float y = slotKind == AutomationValueSlotKind.Threshold
                    ? 142f
                    : 76f;
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(y),
                    width,
                    height);
            }

            if (IsMaxMinKind(hostKind))
            {
                float y = slotKind == AutomationValueSlotKind.MathB
                    ? 142f
                    : 76f;
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(y),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.Clamp)
            {
                float y = 76f;
                if (slotKind == AutomationValueSlotKind.Min)
                    y = 142f;
                else if (slotKind == AutomationValueSlotKind.Max)
                    y = 208f;

                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(y),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.Smooth)
            {
                float y = slotKind == AutomationValueSlotKind.Seconds
                    ? 142f
                    : 76f;

                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(y),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.IfCondition && slotKind == AutomationValueSlotKind.Else)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(192f),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.IfCondition && slotKind == AutomationValueSlotKind.Pass)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(126f),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.IfLessThan && slotKind == AutomationValueSlotKind.Else)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(258f),
                    width,
                    height);
            }

            if (hostKind == AutomationNodeKind.IfLessThan && slotKind == AutomationValueSlotKind.Pass)
            {
                return new Rect(
                    hostRect.x + EsuHudLayout.Scale(50f),
                    hostRect.y + EsuHudLayout.Scale(192f),
                    width,
                    height);
            }

            return new Rect(
                hostRect.x + EsuHudLayout.Scale(50f),
                hostRect.y + EsuHudLayout.Scale(126f),
                width,
                height);
        }

        private static string ShortText(string text, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string trimmed = text.Trim();
            if (trimmed.Length <= maxCharacters)
                return trimmed;

            if (maxCharacters <= 3)
                return trimmed.Substring(0, maxCharacters);

            return trimmed.Substring(0, maxCharacters - 3) + "...";
        }

        private static string ShortMiddleText(string text, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string trimmed = text.Trim();
            if (trimmed.Length <= maxCharacters)
                return trimmed;

            if (maxCharacters <= 3)
                return trimmed.Substring(0, maxCharacters);

            int remaining = maxCharacters - 3;
            int leading = Math.Max(1, (remaining + 1) / 2);
            int trailing = Math.Max(1, remaining - leading);
            if (leading + trailing >= trimmed.Length)
                return trimmed;

            return trimmed.Substring(0, leading) + "..." + trimmed.Substring(trimmed.Length - trailing, trailing);
        }

        private static void RegisterFullTextTooltip(Rect rect, string fullText, string displayText)
        {
            if (string.IsNullOrWhiteSpace(fullText))
                return;

            string trimmed = fullText.Trim();
            if (string.Equals(trimmed, displayText, StringComparison.Ordinal))
                return;

            EsuCursorTooltip.Register(rect, trimmed);
        }

        private static bool MatchesPropertyText(string current, string candidate)
        {
            if (string.Equals(
                    current?.Trim(),
                    candidate?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return MatchesQuery(current, candidate);
        }

        private static string NodeIcon(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "chevron1";
                case AutomationNodeKind.OutputSetter:
                    return "chevron3";
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return "filter";
                case AutomationNodeKind.Constant:
                    return "count";
                case AutomationNodeKind.Random:
                    return "count";
                case AutomationNodeKind.Comment:
                    return "open";
                default:
                    return "gear";
            }
        }

        private static string NodeTitle(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "Input Getter";
                case AutomationNodeKind.Forever:
                    return "Forever";
                case AutomationNodeKind.IfCondition:
                    return "If True";
                case AutomationNodeKind.IfLessThan:
                    return "Switch > Threshold";
                case AutomationNodeKind.LogicNot:
                    return "Logic Not";
                case AutomationNodeKind.LogicAnd:
                    return "Logic And";
                case AutomationNodeKind.LogicOr:
                    return "Logic Or";
                case AutomationNodeKind.LogicXor:
                    return "Logic Xor";
                case AutomationNodeKind.LogicNand:
                    return "Logic Nand";
                case AutomationNodeKind.LogicNor:
                    return "Logic Nor";
                case AutomationNodeKind.LogicXnor:
                    return "Logic Xnor";
                case AutomationNodeKind.CompareAboveThreshold:
                    return "Above Threshold";
                case AutomationNodeKind.CompareBelowThreshold:
                    return "Below Threshold";
                case AutomationNodeKind.Constant:
                    return "Constant";
                case AutomationNodeKind.Random:
                    return "Random";
                case AutomationNodeKind.OutputSetter:
                    return "Output Setter";
                case AutomationNodeKind.MathAdd:
                    return "Math Add";
                case AutomationNodeKind.MathSubtract:
                    return "Math Subtract";
                case AutomationNodeKind.MathMultiply:
                    return "Math Multiply";
                case AutomationNodeKind.MathMax:
                    return "Math Max";
                case AutomationNodeKind.MathMin:
                    return "Math Min";
                case AutomationNodeKind.Clamp:
                    return "Clamp";
                case AutomationNodeKind.Smooth:
                    return "Smooth";
                default:
                    return "Note";
            }
        }

        private static string DefaultNodeLabel(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return "Read linked block property";
                case AutomationNodeKind.Forever:
                    return "Native continuous evaluation";
                case AutomationNodeKind.IfCondition:
                    return "If incoming condition is true";
                case AutomationNodeKind.IfLessThan:
                    return "Pass when incoming value > threshold";
                case AutomationNodeKind.LogicNot:
                    return "Invert boolean signal";
                case AutomationNodeKind.LogicAnd:
                    return "Require both boolean inputs";
                case AutomationNodeKind.LogicOr:
                    return "Accept either boolean input";
                case AutomationNodeKind.LogicXor:
                    return "Require exactly one boolean input";
                case AutomationNodeKind.LogicNand:
                    return "Reject both boolean inputs";
                case AutomationNodeKind.LogicNor:
                    return "Require both boolean inputs to be false";
                case AutomationNodeKind.LogicXnor:
                    return "Require matching boolean inputs";
                case AutomationNodeKind.CompareAboveThreshold:
                    return "Return 1 when signal is above threshold";
                case AutomationNodeKind.CompareBelowThreshold:
                    return "Return 1 when signal is below threshold";
                case AutomationNodeKind.Constant:
                    return "Return fixed numeric output";
                case AutomationNodeKind.Random:
                    return "Return random numeric output";
                case AutomationNodeKind.OutputSetter:
                    return "Set linked block property";
                case AutomationNodeKind.MathAdd:
                    return "Add value to signal";
                case AutomationNodeKind.MathSubtract:
                    return "Subtract value from signal";
                case AutomationNodeKind.MathMultiply:
                    return "Multiply signal by value";
                case AutomationNodeKind.MathMax:
                    return "Take maximum of two values";
                case AutomationNodeKind.MathMin:
                    return "Take minimum of two values";
                case AutomationNodeKind.Clamp:
                    return "Clamp signal range";
                case AutomationNodeKind.Smooth:
                    return "Smooth signal over time";
                default:
                    return "Graph note";
            }
        }

        private static string DefaultProperty(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                case AutomationNodeKind.OutputSetter:
                    return string.Empty;
                case AutomationNodeKind.Forever:
                    return "body";
                case AutomationNodeKind.IfCondition:
                    return "condition";
                case AutomationNodeKind.IfLessThan:
                    return "threshold";
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return "logic";
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return "threshold";
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return "operation";
                case AutomationNodeKind.Constant:
                    return "number";
                case AutomationNodeKind.Random:
                    return "range";
                default:
                    return "value";
            }
        }

        private static string NormalizeNodeProperty(
            AutomationNodeKind kind,
            string property)
        {
            if (!string.IsNullOrWhiteSpace(property))
                return property.Trim();

            return kind == AutomationNodeKind.InputGetter ||
                   kind == AutomationNodeKind.OutputSetter
                ? string.Empty
                : "value";
        }

        private static string DisplayNodeProperty(AutomationGraphNode node)
        {
            return string.IsNullOrWhiteSpace(node?.Property)
                ? "unresolved property"
                : node.Property;
        }

        private static string DefaultValue(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.IfCondition:
                    return "threshold 0.5 else 0";
                case AutomationNodeKind.IfLessThan:
                    return "threshold 10 else 0";
                case AutomationNodeKind.Forever:
                    return "Native breadboard evaluates continuously";
                case AutomationNodeKind.LogicNot:
                    return "NOT";
                case AutomationNodeKind.LogicAnd:
                    return "AND";
                case AutomationNodeKind.LogicOr:
                    return "OR";
                case AutomationNodeKind.LogicXor:
                    return "XOR";
                case AutomationNodeKind.LogicNand:
                    return "NAND";
                case AutomationNodeKind.LogicNor:
                    return "NOR";
                case AutomationNodeKind.LogicXnor:
                    return "XNOR";
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return "threshold 10";
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.OutputSetter:
                    return "30";
                case AutomationNodeKind.Random:
                    return "0..1";
                case AutomationNodeKind.MathAdd:
                    return "a + 0";
                case AutomationNodeKind.MathSubtract:
                    return "a - 0";
                case AutomationNodeKind.MathMultiply:
                    return "a * 1";
                case AutomationNodeKind.MathMax:
                    return "MAX";
                case AutomationNodeKind.MathMin:
                    return "MIN";
                case AutomationNodeKind.Clamp:
                    return "0..100";
                case AutomationNodeKind.Smooth:
                    return "0.25s";
                default:
                    return string.Empty;
            }
        }

        private static string LinkNodeLabel(AutomationLink link)
        {
            if (link == null)
                return "Linked block";
            return link.Kind == AutomationLinkKind.InputToBreadboard
                ? "Read " + BlockRefCurrentName(link.Source)
                : "Set " + BlockRefCurrentName(link.Target);
        }

        private sealed class AutomationBlockRef
        {
            internal AutomationBlockRef(
                AllConstruct construct,
                Vector3i cell,
                string name,
                bool isBreadboard,
                string stableKey = null)
            {
                Construct = construct;
                Cell = cell;
                Name = string.IsNullOrWhiteSpace(name) ? "Block" : name;
                IsBreadboard = isBreadboard;
                StableKey = string.IsNullOrWhiteSpace(stableKey)
                    ? ConstructKey(construct) + ":" + FormatCell(cell)
                    : stableKey;
            }

            internal AllConstruct Construct { get; }

            internal Vector3i Cell { get; }

            internal string Name { get; }

            internal bool IsBreadboard { get; }

            internal string StableKey { get; }

            internal bool IsResolved => Construct != null;

            internal bool IsStillValidBreadboard
            {
                get
                {
                    try
                    {
                        Block block = Construct?.AllBasics?.GetBlockViaLocalPosition(Cell);
                        return block != null &&
                               !block.IsDeleted &&
                               AutomationBreadboardCatalog.IsBreadboardBlock(block);
                    }
                    catch
                    {
                        return IsBreadboard;
                    }
                }
            }

            internal bool SameBlock(AutomationBlockRef other) =>
                ReferenceEquals(this, other) ||
                other != null &&
                Construct != null &&
                other.Construct != null &&
                ReferenceEquals(Construct, other.Construct) &&
                Cell.x == other.Cell.x &&
                Cell.y == other.Cell.y &&
                Cell.z == other.Cell.z;

            internal static AutomationBlockRef Unresolved(
                string name,
                string stableKey)
            {
                return new AutomationBlockRef(
                    null,
                    default(Vector3i),
                    name,
                    isBreadboard: false,
                    stableKey: string.IsNullOrWhiteSpace(stableKey)
                        ? "unresolved:" + (name ?? string.Empty)
                        : stableKey);
            }

            internal bool TryWorldCenter(out Vector3 world)
            {
                world = Vector3.zero;
                if (Construct == null)
                    return false;

                try
                {
                    world = Construct.SafeLocalToGlobal(new Vector3(Cell.x, Cell.y, Cell.z));
                    return true;
                }
                catch
                {
                    try
                    {
                        if (Construct.myTransform == null)
                            return false;
                        world = Construct.myTransform.TransformPoint(new Vector3(Cell.x, Cell.y, Cell.z));
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            internal bool TryGetBlock(out Block block)
            {
                block = null;
                try
                {
                    block = Construct?.AllBasics?.GetBlockViaLocalPosition(Cell);
                    return block != null && !block.IsDeleted;
                }
                catch
                {
                    return false;
                }
            }

            private static string ConstructKey(AllConstruct construct) =>
                construct == null
                    ? "none"
                    : RuntimeHelpers.GetHashCode(construct).ToString(CultureInfo.InvariantCulture);
        }

        private sealed class AutomationLink
        {
            internal AutomationLink(
                int id,
                AutomationBlockRef source,
                AutomationBlockRef target,
                AutomationLinkKind kind,
                string property,
                Color color,
                object nativeComponent = null,
                string nativeStatus = null)
            {
                Id = id;
                Source = source;
                Target = target;
                Kind = kind;
                Property = string.IsNullOrWhiteSpace(property) ? "value" : property;
                Color = color;
                NativeComponent = nativeComponent;
                NativeStatus = nativeStatus ?? "native";
            }

            internal int Id { get; }

            internal AutomationBlockRef Source { get; }

            internal AutomationBlockRef Target { get; }

            internal AutomationLinkKind Kind { get; }

            internal string Property { get; private set; }

            internal Color Color { get; }

            internal object NativeComponent { get; }

            internal string NativeStatus { get; }

            internal bool IsStaged => NativeComponent == null;

            internal bool IsResolved => Source?.IsResolved == true && Target?.IsResolved == true;

            internal void SetProperty(string property)
            {
                Property = string.IsNullOrWhiteSpace(property) ? "value" : property.Trim();
            }
        }

        private sealed class AutomationGraphTargetBinding
        {
            private AutomationGraphTargetBinding(
                AutomationBlockRef source,
                AutomationBlockRef target,
                AutomationLinkKind kind,
                string property)
            {
                Source = source;
                Target = target;
                Kind = kind;
                Property = NormalizeTargetProperty(property);
            }

            internal AutomationBlockRef Source { get; }

            internal AutomationBlockRef Target { get; }

            internal AutomationLinkKind Kind { get; }

            internal string Property { get; private set; }

            internal static AutomationGraphTargetBinding FromLink(
                AutomationLink link,
                string property = null)
            {
                return link == null
                    ? null
                    : new AutomationGraphTargetBinding(
                        link.Source,
                        link.Target,
                        link.Kind,
                        property ?? link.Property);
            }

            internal void SetProperty(string property)
            {
                Property = NormalizeTargetProperty(property);
            }

            internal AutomationGraphTargetBinding Clone() =>
                new AutomationGraphTargetBinding(Source, Target, Kind, Property);

            private static string NormalizeTargetProperty(string property) =>
                string.IsNullOrWhiteSpace(property)
                    ? string.Empty
                    : property.Trim();
        }

        private sealed class AutomationGraphNodeDraft
        {
            internal AutomationGraphNodeDraft(AutomationGraphNode node)
            {
                Label = node?.Label ?? string.Empty;
                Property = node?.Property ?? string.Empty;
                ValueText = node?.ValueText ?? string.Empty;
                Rect = node?.Rect ?? Rect.zero;
                TargetBinding = node?.TargetBinding?.Clone();
            }

            internal string Label { get; }

            internal string Property { get; }

            internal string ValueText { get; }

            internal Rect Rect { get; }

            internal AutomationGraphTargetBinding TargetBinding { get; }

            internal void ApplyTo(AutomationGraphNode node)
            {
                if (node == null)
                    return;

                node.Label = Label;
                node.Property = NormalizeNodeProperty(node.Kind, Property);
                node.ValueText = ValueText;
                if (Rect.width > 0f && Rect.height > 0f)
                    node.Rect = Rect;
                node.BindTarget(TargetBinding);
            }
        }

        private sealed class AutomationGraphPortRef
        {
            internal AutomationGraphPortRef(
                AutomationGraphNode node,
                AutomationGraphPortKind kind,
                AutomationValueSlotKind slotKind = AutomationValueSlotKind.Pass,
                int nodeId = 0)
            {
                Node = node;
                NodeId = nodeId != 0 ? nodeId : node?.Id ?? 0;
                Kind = kind;
                SlotKind = slotKind;
            }

            internal AutomationGraphNode Node { get; }

            internal int NodeId { get; }

            internal AutomationGraphPortKind Kind { get; }

            internal AutomationValueSlotKind SlotKind { get; }
        }

        private sealed class AutomationGraphConnection
        {
            internal AutomationGraphConnection(
                AutomationGraphConnectionKind kind,
                AutomationGraphNode from,
                AutomationGraphNode to,
                AutomationValueSlotKind slotKind = AutomationValueSlotKind.Pass,
                AutomationGraphWireOrigin origin = AutomationGraphWireOrigin.EsuSnap,
                int fromNodeId = 0,
                int toNodeId = 0)
            {
                Kind = kind;
                From = from;
                To = to;
                FromNodeId = fromNodeId != 0 ? fromNodeId : from?.Id ?? 0;
                ToNodeId = toNodeId != 0 ? toNodeId : to?.Id ?? 0;
                SlotKind = slotKind;
                Origin = origin;
                FromPort = BuildFromPort(kind, from, slotKind, FromNodeId);
                ToPort = BuildToPort(kind, to, slotKind, ToNodeId);
            }

            internal AutomationGraphConnectionKind Kind { get; }

            internal AutomationGraphWireOrigin Origin { get; }

            internal AutomationGraphNode From { get; }

            internal AutomationGraphNode To { get; }

            internal AutomationGraphPortRef FromPort { get; }

            internal AutomationGraphPortRef ToPort { get; }

            internal int FromNodeId { get; }

            internal int ToNodeId { get; }

            internal AutomationValueSlotKind SlotKind { get; }

            internal bool IsImportedNative => Origin == AutomationGraphWireOrigin.NativeImported;

            private static AutomationGraphPortRef BuildFromPort(
                AutomationGraphConnectionKind kind,
                AutomationGraphNode from,
                AutomationValueSlotKind slotKind,
                int fromNodeId)
            {
                switch (kind)
                {
                    case AutomationGraphConnectionKind.Value:
                        return new AutomationGraphPortRef(from, AutomationGraphPortKind.ValueOut, slotKind, fromNodeId);
                    case AutomationGraphConnectionKind.Body:
                        return new AutomationGraphPortRef(from, AutomationGraphPortKind.BodyOut, nodeId: fromNodeId);
                    default:
                        return new AutomationGraphPortRef(from, AutomationGraphPortKind.FlowOut, nodeId: fromNodeId);
                }
            }

            private static AutomationGraphPortRef BuildToPort(
                AutomationGraphConnectionKind kind,
                AutomationGraphNode to,
                AutomationValueSlotKind slotKind,
                int toNodeId)
            {
                switch (kind)
                {
                    case AutomationGraphConnectionKind.Value:
                        return new AutomationGraphPortRef(to, AutomationGraphPortKind.ValueIn, slotKind, toNodeId);
                    case AutomationGraphConnectionKind.Body:
                        return new AutomationGraphPortRef(to, AutomationGraphPortKind.BodyIn, nodeId: toNodeId);
                    default:
                        return new AutomationGraphPortRef(to, AutomationGraphPortKind.FlowIn, nodeId: toNodeId);
                }
            }
        }

        private sealed class AutomationGraph
        {
            private const float StartX = 80f;
            private const float StartY = 48f;
            private const float StepY = 128f;

            private int _nextNodeId = 1;
            private int _nextStagedNodeId = -1;
            private bool _connectionsInitialized;

            internal AutomationGraph(string key)
            {
                Key = key;
            }

            internal string Key { get; }

            internal List<AutomationGraphNode> Nodes { get; } = new List<AutomationGraphNode>();

            internal List<AutomationGraphConnection> Connections { get; } = new List<AutomationGraphConnection>();

            internal List<AutomationGraphConnection> ImportedNativeConnections { get; } = new List<AutomationGraphConnection>();

            internal bool ConnectionsInitialized => _connectionsInitialized;

            internal int NativeSyncVersion { get; set; } = -1;

            internal int SelectedNodeId { get; set; }

            internal AutomationGraphNode SelectedNode =>
                Nodes.FirstOrDefault(node => node.Id == SelectedNodeId);

            internal void RebuildNativeNodes(
                IEnumerable<AutomationGraphNode> nativeNodes)
            {
                int selected = SelectedNodeId;
                List<AutomationGraphNode> stagedNodes = Nodes
                    .Where(node => node?.IsStaged == true)
                    .ToList();

                Nodes.Clear();
                if (nativeNodes != null)
                    Nodes.AddRange(nativeNodes.Where(node => node != null));
                Nodes.AddRange(stagedNodes);

                if (stagedNodes.Count > 0)
                    _nextStagedNodeId = Math.Min(_nextStagedNodeId, stagedNodes.Min(node => node.Id) - 1);

                if (selected != 0 && Nodes.Any(node => node.Id == selected))
                    SelectedNodeId = selected;
                else if (Nodes.Count > 0 && !Nodes.Any(node => node.Id == SelectedNodeId))
                    SelectedNodeId = 0;
                else if (Nodes.Count == 0)
                    SelectedNodeId = 0;

                RebindConnectionsToCurrentNodes();
            }

            internal void RebuildConnections(
                IEnumerable<AutomationGraphConnection> connections)
            {
                Connections.Clear();
                if (connections != null)
                    Connections.AddRange(connections.Where(connection =>
                        connection?.From != null &&
                        connection.To != null &&
                        !connection.IsImportedNative));
                _connectionsInitialized = true;
            }

            internal void RebuildImportedNativeConnections(
                IEnumerable<AutomationGraphConnection> connections)
            {
                ImportedNativeConnections.Clear();
                if (connections != null)
                    ImportedNativeConnections.AddRange(connections.Where(connection =>
                        connection?.From != null &&
                        connection.To != null &&
                        connection.IsImportedNative));
            }

            internal void RebindConnectionsToCurrentNodes()
            {
                var nodesById = Nodes
                    .Where(node => node != null)
                    .GroupBy(node => node.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                RebindConnections(Connections, nodesById, imported: false);
                RebindConnections(ImportedNativeConnections, nodesById, imported: true);
            }

            internal void RetargetConnectionNodeIds(
                IReadOnlyDictionary<int, int> nodeIdMap)
            {
                if (nodeIdMap == null || nodeIdMap.Count == 0)
                    return;

                RetargetConnections(Connections, nodeIdMap);
                RetargetConnections(ImportedNativeConnections, nodeIdMap);
            }

            internal int RemoveConnectionsTouchingNode(int nodeId)
            {
                if (nodeId == 0)
                    return 0;

                int before = Connections.Count;
                Connections.RemoveAll(connection =>
                    connection != null &&
                    (connection.FromNodeId == nodeId || connection.ToNodeId == nodeId));
                ImportedNativeConnections.RemoveAll(connection =>
                    connection != null &&
                    (connection.FromNodeId == nodeId || connection.ToNodeId == nodeId));
                return before - Connections.Count;
            }

            private static void RebindConnections(
                List<AutomationGraphConnection> connections,
                IReadOnlyDictionary<int, AutomationGraphNode> nodesById,
                bool imported)
            {
                if (connections == null || nodesById == null)
                    return;

                var rebound = new List<AutomationGraphConnection>();
                foreach (AutomationGraphConnection connection in connections)
                {
                    if (connection == null ||
                        !nodesById.TryGetValue(connection.FromNodeId, out AutomationGraphNode from) ||
                        !nodesById.TryGetValue(connection.ToNodeId, out AutomationGraphNode to))
                    {
                        continue;
                    }

                    var next = new AutomationGraphConnection(
                        connection.Kind,
                        from,
                        to,
                        connection.SlotKind,
                        connection.Origin,
                        connection.FromNodeId,
                        connection.ToNodeId);
                    if (next.IsImportedNative == imported)
                        rebound.Add(next);
                }

                connections.Clear();
                connections.AddRange(rebound);
            }

            private static void RetargetConnections(
                List<AutomationGraphConnection> connections,
                IReadOnlyDictionary<int, int> nodeIdMap)
            {
                if (connections == null)
                    return;

                for (int index = 0; index < connections.Count; index++)
                {
                    AutomationGraphConnection connection = connections[index];
                    if (connection == null)
                        continue;

                    int fromId = nodeIdMap.TryGetValue(connection.FromNodeId, out int mappedFrom)
                        ? mappedFrom
                        : connection.FromNodeId;
                    int toId = nodeIdMap.TryGetValue(connection.ToNodeId, out int mappedTo)
                        ? mappedTo
                        : connection.ToNodeId;
                    connections[index] = new AutomationGraphConnection(
                        connection.Kind,
                        connection.From,
                        connection.To,
                        connection.SlotKind,
                        connection.Origin,
                        fromId,
                        toId);
                }
            }

            internal void AddNode(
                AutomationNodeKind kind,
                string label,
                string property,
                string value)
            {
                int index = Nodes.Count;
                Rect rect = NormalizeGraphNodeRect(
                    kind,
                    new Rect(
                        StartX + (index % 2) * (GraphNodeWidth + 46f),
                        StartY + index * StepY,
                        GraphNodeWidthForKind(kind),
                        GraphNodeHeightForKind(kind)));
                Nodes.Add(new AutomationGraphNode(
                    _nextNodeId++,
                    kind,
                    rect,
                    label,
                    property,
                    value));
            }

            internal AutomationGraphNode AddStagedNode(
                AutomationNodeKind kind,
                Rect rect)
            {
                Rect nodeRect = rect;
                if (nodeRect.width <= 0f)
                    nodeRect.width = GraphNodeWidthForKind(kind);
                if (nodeRect.height <= 0f)
                    nodeRect.height = GraphNodeHeightForKind(kind);
                nodeRect = NormalizeGraphNodeRect(kind, nodeRect);

                var node = new AutomationGraphNode(
                    _nextStagedNodeId--,
                    kind,
                    nodeRect,
                    DefaultNodeLabel(kind),
                    DefaultProperty(kind),
                    DefaultValue(kind));
                Nodes.Add(node);
                return node;
            }

            internal float ContentHeight()
            {
                if (Nodes.Count == 0)
                    return 700f;

                return Mathf.Max(
                    700f,
                    Nodes.Max(node => node.Rect.yMax) + 120f);
            }

            internal float ContentWidth()
            {
                if (Nodes.Count == 0)
                    return 900f;

                return Mathf.Max(
                    900f,
                    Nodes.Max(node => node.Rect.xMax) + 160f);
            }
        }

        private sealed class AutomationGraphNode
        {
            internal AutomationGraphNode(
                int id,
                AutomationNodeKind kind,
                Rect rect,
                string label,
                string property,
                string valueText,
                object nativeComponent = null)
            {
                Id = id;
                Kind = kind;
                Rect = rect;
                Label = label ?? string.Empty;
                Property = NormalizeNodeProperty(kind, property);
                ValueText = valueText ?? string.Empty;
                NativeComponent = nativeComponent;
            }

            internal int Id { get; }

            internal AutomationNodeKind Kind { get; }

            internal Rect Rect { get; set; }

            internal string Label { get; set; }

            internal string Property { get; set; }

            internal string ValueText { get; set; }

            internal object NativeComponent { get; }

            internal bool IsStaged => NativeComponent == null;

            internal AutomationGraphTargetBinding TargetBinding { get; private set; }

            internal void BindTarget(
                AutomationLink link,
                string property = null)
            {
                TargetBinding = AutomationGraphTargetBinding.FromLink(link, property);
            }

            internal void BindTarget(AutomationGraphTargetBinding binding)
            {
                TargetBinding = binding?.Clone();
            }

            internal void SetBindingProperty(string property)
            {
                TargetBinding?.SetProperty(property);
            }
        }
    }
}
