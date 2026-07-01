using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed class SmartBuildSession
    {
        private const float ToolbarHeight = 54f;
        private const float LeftPanelWidth = 392f;
        private const float LeftPanelDefaultHeight = 507f;
        private const float RightPanelWidth = 282f;
        private const float LeftPanelMinHeight = 292f;
        private const float RightPanelMinHeight = 420f;
        private const float HandleLength = 1.8f;
        private const float AxisPickThresholdPixels = 24f;
        private const float RotateSnapDegrees = 90f;
        private const int ViewModeMenuButtonCount = 9;
        private const int SceneHistoryLimit = 64;

        private static Rect s_leftPanelRect = new Rect(18f, 110f, LeftPanelWidth, LeftPanelDefaultHeight);
        private static Rect s_rightPanelRect = new Rect(980f, 110f, RightPanelWidth, 680f);
        private static int s_layoutGeneration = -1;
        private static SmartBuildMaterial s_selectedMaterial = SmartBuildMaterial.Wood;
        private static bool s_showLeftPanel = true;
        private static bool s_showRightPanel = true;
        private static bool s_showMaterialSection = true;
        private static bool s_showShapePaletteSection = true;
        private static bool s_showSelectedSection = true;
        private static bool s_showSceneSection = true;

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.SmartBuild.Toolbar".GetHashCode();
        private readonly int _panelWindowId = "EndlessShapesUnlimited.SmartBuild.Panel".GetHashCode();
        private readonly int _rightPanelWindowId = "EndlessShapesUnlimited.SmartBuild.Shapes".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.SmartBuild.Status".GetHashCode();
        private readonly DecorationEditorViewModeController _viewModes;

        private Rect _toolbarRect;
        private Rect _leftPanelRect = s_leftPanelRect;
        private Rect _rightPanelRect = s_rightPanelRect;
        private Rect _statusRect;
        private MBuild_Ftd _buildOptions;
        private SmartBuildSource _source;
        private string _sourceReason;
        private SmartBuildPieceScene _scene;
        private SmartBuildPiece _draft;
        private SmartBuildPiece _dragPieceStart;
        private SmartBuildPlan _plan;
        private IReadOnlyList<Vector3i> _previewCells = Array.Empty<Vector3i>();
        private IReadOnlyList<IReadOnlyList<Vector3i>> _previewCellSets =
            Array.Empty<IReadOnlyList<Vector3i>>();
        private IReadOnlyList<SmartBuildVolume> _previewVolumes =
            Array.Empty<SmartBuildVolume>();
        private SmartBuildTool _tool = SmartBuildTool.Draw;
        private SmartBuildDrawPlane _drawPlane = SmartBuildDrawPlane.Camera;
        private SmartBuildOccupancyMode _occupancyMode = SmartBuildOccupancyMode.SkipOccupied;
        private SmartBuildMaterial _selectedMaterial = s_selectedMaterial;
        private SmartBuildMaterial _sourceMaterial = s_selectedMaterial;
        private SmartBuildShapeKind _selectedShape = SmartBuildShapeKind.Cuboid;
        private int _selectedSlopeLength = 1;
        private SmartBuildEditHandleMode _editHandleMode = SmartBuildEditHandleMode.Gizmo;
        private Vector2 _shapePanelScroll;
        private DecorationEditAxis _dragAxis = DecorationEditAxis.None;
        private DecorationEditAxis[] _dragAxes = Array.Empty<DecorationEditAxis>();
        private int[] _dragSigns = Array.Empty<int>();
        private SmartBuildTool _dragTool = SmartBuildTool.Draw;
        private Vector2 _dragMouseStart;
        private int _dragSign = 1;
        private bool _dragging;
        private bool _rotating;
        private bool _planDirty;
        private bool _dragStartedFromFace;
        private DecorationEditAxis _hoverFaceAxis = DecorationEditAxis.None;
        private int _hoverFaceSign = 1;
        private bool _closeRequested;
        private bool _resizingLeftPanel;
        private bool _resizingRightPanel;
        private Rect _leftPanelResizeStart;
        private Rect _rightPanelResizeStart;
        private Vector2 _leftPanelResizeMouseStart;
        private Vector2 _rightPanelResizeMouseStart;
        private int _layoutResetGeneration = s_layoutGeneration;
        private float _applyCancelAttentionUntil = -1f;
        private SmartBuildSlopeSupportMode _defaultSlopeSupportMode = SmartBuildSlopeSupportMode.Full;
        private SmartBuildPreviewMode _previewMode = SmartBuildPreviewMode.Wireframe;
        private DecorationEditorViewMode _viewMode = DecorationEditorViewMode.Mixed;
        private bool _viewModeMenuOpen;
        private bool _showLeftPanel = s_showLeftPanel;
        private bool _showRightPanel = s_showRightPanel;
        private bool _showMaterialSection = s_showMaterialSection;
        private bool _showShapePaletteSection = s_showShapePaletteSection;
        private bool _showSelectedSection = s_showSelectedSection;
        private bool _showSceneSection = s_showSceneSection;
        private string _snapMoveText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartMoveStepCells);
        private string _snapRotateText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartRotateSnapDegrees);
        private string _snapScaleText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartScaleStepCells);
        private readonly Stack<SmartBuildSceneSnapshot> _sceneUndo = new Stack<SmartBuildSceneSnapshot>();
        private readonly Stack<SmartBuildSceneSnapshot> _sceneRedo = new Stack<SmartBuildSceneSnapshot>();
        private SmartBuildSceneSnapshot _dragSceneStart;
        private DecorationEditAxis _rotateDragAxis = DecorationEditAxis.None;
        private Vector2 _rotateDragMouseStart;
        private Vector2 _rotateDragCenterScreen;
        private Vector2 _rotateDragStartVector;
        private Vector2 _rotateDragLastVector;
        private float _rotateDragAccumulatedDegrees;
        private SmartBuildPiece _rotatePieceStart;
        private SmartBuildSceneSnapshot _rotateSceneStart;
        private bool _contextMenuOpen;
        private Rect _contextMenuRect;
        private int _contextMenuPieceId;
        private DecorationEditAxis[] _hoverEdgeAxes = Array.Empty<DecorationEditAxis>();
        private int[] _hoverEdgeSigns = Array.Empty<int>();
        private DecorationEditAxis[] _hoverCornerAxes = Array.Empty<DecorationEditAxis>();
        private int[] _hoverCornerSigns = Array.Empty<int>();

        internal SmartBuildSession(cBuild build)
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
            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                reason = "Place or cancel the pending symmetry plane before switching modes.";
                return false;
            }

            if (_dragging)
            {
                reason = "Finish the Smart Builder handle drag before switching modes.";
                return false;
            }

            if (_rotating)
            {
                reason = "Finish the Smart Builder rotation drag before switching modes.";
                return false;
            }

            if (_draft != null)
            {
                RefreshApplyCancelAttention();
                reason = "Apply or cancel the Smart Builder preview before switching modes.";
                return false;
            }

            reason = null;
            return true;
        }

        internal void Begin()
        {
            Active = true;
            SmartBuildInputScope.Begin();
            ApplyFocusView();
            RefreshSelection();
            SyncSnapTextFromSettings();
        }

        internal void End(bool preserveSharedHud = false)
        {
            RestoreFocusView();
            SmartBuildInputScope.End();
            if (!preserveSharedHud)
                DecorationEditorOverlay.Clear();
            Active = false;
            _scene = null;
            _draft = null;
            _dragPieceStart = null;
            _plan = null;
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _dragAxes = Array.Empty<DecorationEditAxis>();
            _dragSigns = Array.Empty<int>();
            _dragSceneStart = null;
            ClearRotateDragState();
            _planDirty = false;
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
            _contextMenuOpen = false;
            _viewModeMenuOpen = false;
            SwitchToDecorationEditRequested = false;
        }

        internal void SuspendForModeSwitchHandoff()
        {
            RestoreFocusView();
            SmartBuildInputScope.End();
            Active = false;
            _dragging = false;
            _rotating = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _viewModeMenuOpen = false;
            SwitchToDecorationEditRequested = false;
        }

        internal void Update()
        {
            if (!Active)
                return;

            EsuHudNotifications.SetActiveSource("Smart Builder");
            RefreshMouseOverUiFromCurrentPointer();
            RefreshSelection();
            HandleKeyboard();
            HandleMouse();
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

        private void DrawGui(bool interactive)
        {
            DecorationEditorTheme.Ensure();
            if (interactive)
                EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed());
            if (Event.current.type == EventType.Repaint)
                DecorationEditorOverlay.Render();

            ApplyLayoutResetIfNeeded();
            float margin = EsuHudLayout.Scale(8f);
            _toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight);
            _statusRect = new Rect(
                margin,
                Screen.height - StatusHeightScaled() - margin,
                Screen.width - margin * 2f,
                StatusHeightScaled());
            if (_leftPanelRect.width < 1f || _leftPanelRect.height < 1f)
                _leftPanelRect = DefaultLeftPanelRect();
            if (_rightPanelRect.width < 1f || _rightPanelRect.height < 1f)
                _rightPanelRect = DefaultRightPanelRect();
            _leftPanelRect = EsuHudLayout.ClampPanel(
                _leftPanelRect,
                MinLeftPanelWidth(),
                MinLeftPanelHeight(),
                MaxLeftPanelWidth(),
                MaxLeftPanelHeight(),
                LeftPanelTopLimit(),
                StatusHeightScaled() + EsuHudLayout.Scale(12f));
            _rightPanelRect = EsuHudLayout.ClampPanel(
                _rightPanelRect,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight(),
                LeftPanelTopLimit(),
                StatusHeightScaled() + EsuHudLayout.Scale(12f));
            if (interactive)
            {
                if (_showLeftPanel)
                    HandleLeftPanelResize();
                if (_showRightPanel)
                    HandleRightPanelResize();
            }

            GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
            EsuHudNotifications.DrawExpandedPopup();
            if (interactive)
                DrawViewModeMenu(_toolbarRect);
            if (_showLeftPanel)
                _leftPanelRect = GUI.Window(_panelWindowId, _leftPanelRect, DrawLeftPanel, GUIContent.none, GUIStyle.none);
            if (_showRightPanel)
                _rightPanelRect = GUI.Window(_rightPanelWindowId, _rightPanelRect, DrawShapePanel, GUIContent.none, GUIStyle.none);
            GUI.Window(_statusWindowId, _statusRect, DrawStatusStrip, GUIContent.none, GUIStyle.none);
            if (interactive)
            {
                if (_showLeftPanel)
                {
                    EsuHudLayout.DrawResizeGrip(_leftPanelRect, leftEdge: false);
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_leftPanelRect, leftEdge: false), "Drag to resize the Smart Builder panel.");
                }

                if (_showRightPanel)
                {
                    EsuHudLayout.DrawResizeGrip(_rightPanelRect, leftEdge: true);
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_rightPanelRect, leftEdge: true), "Drag to resize the shape palette.");
                }

                DrawPreviewContextMenu();
                EsuConsoleWindow.Draw();
                EsuCursorTooltip.Draw();
            }

            s_leftPanelRect = _leftPanelRect;
            s_rightPanelRect = _rightPanelRect;
            s_layoutGeneration = _layoutResetGeneration;
            s_showLeftPanel = _showLeftPanel;
            s_showRightPanel = _showRightPanel;
            s_showMaterialSection = _showMaterialSection;
            s_showShapePaletteSection = _showShapePaletteSection;
            s_showSelectedSection = _showSelectedSection;
            s_showSceneSection = _showSceneSection;
            if (!interactive)
                return;

            bool overUi = ContainsMouse(_toolbarRect) ||
                          EsuHudNotifications.ContainsMouse(Event.current.mousePosition) ||
                          (_viewModeMenuOpen && ViewModeMenuRect(_toolbarRect).Contains(Event.current.mousePosition)) ||
                          (_showLeftPanel && ContainsMouse(_leftPanelRect)) ||
                          (_showRightPanel && ContainsMouse(_rightPanelRect)) ||
                          ContainsMouse(_statusRect) ||
                          (_contextMenuOpen && _contextMenuRect.Contains(Event.current.mousePosition));
            SmartBuildInputScope.SetMouseOverUi(overUi);
            if (overUi && ShouldConsumeGuiEvent(Event.current))
            {
                if (Event.current.type == EventType.ScrollWheel)
                    SmartBuildInputScope.ClaimMouseWheelInputForFrames();
                else
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                Event.current.Use();
            }
        }

        private bool TooltipInputSuppressed() =>
            _dragging ||
            _rotating ||
            _resizingLeftPanel ||
            _resizingRightPanel;

        private float ToolbarHeightScaled() =>
            EsuHudNotifications.ToolbarHeightScaled(ToolbarHeight, Screen.width - EsuHudLayout.Scale(16f));

        private float StatusHeightScaled() =>
            Mathf.Clamp(Screen.height * 0.13f, EsuHudLayout.Scale(118f), EsuHudLayout.Scale(146f));

        private float LeftPanelTopLimit() => ToolbarHeightScaled() + EsuHudLayout.Scale(14f);

        private float MinLeftPanelWidth() => EsuHudLayout.Scale(300f);

        private float MinLeftPanelHeight() => EsuHudLayout.Scale(LeftPanelMinHeight);

        private float MaxLeftPanelWidth() => Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.62f);

        private float MaxLeftPanelHeight() =>
            Mathf.Max(MinLeftPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private float MinRightPanelWidth() => EsuHudLayout.Scale(230f);

        private float MinRightPanelHeight() => EsuHudLayout.Scale(RightPanelMinHeight);

        private float MaxRightPanelWidth() => Mathf.Max(MinRightPanelWidth(), Screen.width * 0.42f);

        private float MaxRightPanelHeight() =>
            Mathf.Max(MinRightPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private Rect DefaultLeftPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(LeftPanelWidth),
                Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.28f));
            float height = Mathf.Min(
                EsuHudLayout.Scale(LeftPanelDefaultHeight),
                Mathf.Max(MinLeftPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f)));
            return new Rect(EsuHudLayout.Scale(18f), LeftPanelTopLimit(), width, height);
        }

        private Rect DefaultRightPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(RightPanelWidth),
                Mathf.Max(MinRightPanelWidth(), Screen.width * 0.22f));
            float height = Mathf.Min(
                Mathf.Max(EsuHudLayout.Scale(560f), MaxRightPanelHeight() * 0.92f),
                Mathf.Max(MinRightPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f)));
            float x = Mathf.Max(EsuHudLayout.Scale(18f), Screen.width - width - EsuHudLayout.Scale(18f));
            return new Rect(x, LeftPanelTopLimit(), width, height);
        }

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _leftPanelRect = DefaultLeftPanelRect();
            _rightPanelRect = DefaultRightPanelRect();
            _layoutResetGeneration = EsuHudLayout.ResetGeneration;
            s_layoutGeneration = _layoutResetGeneration;
        }

        private void HandleLeftPanelResize()
        {
            Event current = Event.current;
            if (current == null)
                return;

            Rect grip = EsuHudLayout.ResizeGripRect(_leftPanelRect, leftEdge: false);
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                grip.Contains(current.mousePosition))
            {
                _resizingLeftPanel = true;
                _leftPanelResizeStart = _leftPanelRect;
                _leftPanelResizeMouseStart = current.mousePosition;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && _resizingLeftPanel)
            {
                Vector2 delta = current.mousePosition - _leftPanelResizeMouseStart;
                Rect next = _leftPanelResizeStart;
                next.width = Mathf.Clamp(
                    _leftPanelResizeStart.width + delta.x,
                    MinLeftPanelWidth(),
                    MaxLeftPanelWidth());
                next.height = Mathf.Clamp(
                    _leftPanelResizeStart.height + delta.y,
                    MinLeftPanelHeight(),
                    MaxLeftPanelHeight());
                _leftPanelRect = EsuHudLayout.ClampPanel(
                    next,
                    MinLeftPanelWidth(),
                    MinLeftPanelHeight(),
                    MaxLeftPanelWidth(),
                    MaxLeftPanelHeight(),
                    LeftPanelTopLimit(),
                    StatusHeightScaled() + EsuHudLayout.Scale(12f));
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                _resizingLeftPanel = false;
        }

        private void HandleRightPanelResize()
        {
            Event current = Event.current;
            if (current == null)
                return;

            Rect grip = EsuHudLayout.ResizeGripRect(_rightPanelRect, leftEdge: true);
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                grip.Contains(current.mousePosition))
            {
                _resizingRightPanel = true;
                _rightPanelResizeStart = _rightPanelRect;
                _rightPanelResizeMouseStart = current.mousePosition;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && _resizingRightPanel)
            {
                Vector2 delta = current.mousePosition - _rightPanelResizeMouseStart;
                Rect next = _rightPanelResizeStart;
                next.x = _rightPanelResizeStart.x + delta.x;
                next.width = _rightPanelResizeStart.width - delta.x;
                if (next.width < MinRightPanelWidth())
                {
                    next.x -= MinRightPanelWidth() - next.width;
                    next.width = MinRightPanelWidth();
                }

                next.height = Mathf.Clamp(
                    _rightPanelResizeStart.height + delta.y,
                    MinRightPanelHeight(),
                    MaxRightPanelHeight());
                _rightPanelRect = EsuHudLayout.ClampPanel(
                    next,
                    MinRightPanelWidth(),
                    MinRightPanelHeight(),
                    MaxRightPanelWidth(),
                    MaxRightPanelHeight(),
                    LeftPanelTopLimit(),
                    StatusHeightScaled() + EsuHudLayout.Scale(12f));
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                _resizingRightPanel = false;
        }

        internal void DrawWorldPreview()
        {
            DecorationEditorOverlay.BeginFrame();
            if (!Active)
                return;

            try
            {
                if (_draft == null)
                {
                    DrawPlacementGhost();
                }
                else
                {
                    DrawDraftOutline();
                    if (_tool == SmartBuildTool.Draw)
                        DrawPlacementGhost();
                    DrawDraftFaceHighlight();
                    if (_tool == SmartBuildTool.Rotate)
                        DrawRotateGizmo();
                    if (_tool == SmartBuildTool.Move || _tool == SmartBuildTool.Scale)
                        DrawDraftHandles();
                }

                DrawSymmetryOverlay();
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception("Smart Builder", exception, "Smart Block Builder preview draw failed");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder preview draw failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private void HandleKeyboard()
        {
            if (GUIUtility.keyboardControl == 0 &&
                !DecoLimitLifter.EsuInputState.IsTextInputActive() &&
                TryHandleEsuNumberShortcut())
            {
                return;
            }

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                _draft != null)
            {
                ApplyPreview();
            }
        }

        private bool TryHandleEsuNumberShortcut()
        {
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(1))
                return CycleShapeShortcut();
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(2))
                return CycleTransformToolShortcut();
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(3))
                return CyclePreviewShortcut();

            return false;
        }

        private bool CycleShapeShortcut()
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            if (_tool == SmartBuildTool.Draw)
            {
                _selectedShape = _selectedShape == SmartBuildShapeKind.Cuboid
                    ? SmartBuildShapeKind.DownSlope
                    : SmartBuildShapeKind.Cuboid;
            }

            ArmAddMode();
            _viewModeMenuOpen = false;
            _contextMenuOpen = false;
            InfoStore.Add("Smart Builder shape: " + PendingShapeLabel() + ".");
            return true;
        }

        private bool CycleTransformToolShortcut()
        {
            SmartBuildTool next =
                _tool == SmartBuildTool.Move ? SmartBuildTool.Rotate :
                _tool == SmartBuildTool.Rotate ? SmartBuildTool.Scale :
                _tool == SmartBuildTool.Scale ? SmartBuildTool.Move :
                SmartBuildTool.Move;
            return SetActiveToolFromShortcut(next);
        }

        private bool CyclePreviewShortcut()
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            _previewMode = _previewMode == SmartBuildPreviewMode.Wireframe
                ? SmartBuildPreviewMode.Material
                : SmartBuildPreviewMode.Wireframe;
            _viewModeMenuOpen = false;
            InfoStore.Add("Smart Builder preview: " + (_previewMode == SmartBuildPreviewMode.Wireframe ? "Wireframe." : "Material."));
            return true;
        }

        private bool SetActiveToolFromShortcut(SmartBuildTool tool)
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            SetActiveTool(tool);
            return true;
        }

        private void HandleMouse()
        {
            if (SmartBuildInputScope.MouseOverUi)
            {
                if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
                    SmartBuildInputScope.ClaimMouseWheelInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(2) || Input.GetMouseButton(2) || Input.GetMouseButtonUp(2))
                return;

            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    DecoLimitLifter.EsuSymmetry.CancelPending();
                    InfoStore.Add("Symmetry plane placement cancelled.");
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    return;
                }

                if (Input.GetMouseButtonDown(0))
                {
                    PlacePendingSymmetryPlane();
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    return;
                }

                return;
            }

            if (_rotating)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    EndRotateDrag(resetDraft: true);
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                    return;
                }

                if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                {
                    EndRotateDrag(resetDraft: false);
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                    return;
                }

                UpdateRotateDrag();
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (_dragging)
                    EndDrag(resetDraft: true);
                else if (TryOpenPreviewContextMenu())
                    _contextMenuOpen = true;
                else if (_tool == SmartBuildTool.Draw)
                    CancelAddMode();
                SmartBuildInputScope.ClaimBuildInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (_draft != null && TryBeginHandleDrag())
                {
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                    return;
                }

                if (_draft != null &&
                    _tool == SmartBuildTool.Rotate &&
                    TryPickRotationRing(out DecorationEditAxis rotateAxis))
                {
                    BeginRotateDrag(rotateAxis);
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                    return;
                }

                if (_tool != SmartBuildTool.Draw && TrySelectPreviewPieceAtPointer())
                {
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    return;
                }

                if (_tool == SmartBuildTool.Draw || _draft == null)
                {
                    CreatePreviewAtPointer();
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                }

                return;
            }

            if (_dragging && Input.GetMouseButton(0))
            {
                UpdateHandleDrag();
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

            if (_dragging && Input.GetMouseButtonUp(0))
            {
                EndDrag(resetDraft: false);
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
            }
        }

        private void CreatePreviewAtPointer()
        {
            if (_source == null)
            {
                InfoStore.Add(_sourceReason ?? "Selected Smart Builder material is unavailable.");
                return;
            }

            if (TryPlacementCandidate(out SmartBuildPlacementCandidate candidate))
            {
                if (!candidate.Valid)
                {
                    InfoStore.Add(candidate.Reason ?? "Smart Builder target is invalid.");
                    return;
                }

                CreatePreviewAtSeed(candidate);
                return;
            }

            InfoStore.Add("Point at the focused construct grid to create a Smart Builder preview.");
        }

        private bool TryPlacementCandidate(out SmartBuildPlacementCandidate candidate)
        {
            candidate = default;

            if (TrySeedFromPreviewFace(
                    IsShiftHeld(),
                    out AllConstruct previewConstruct,
                    out Vector3i previewCell,
                    out SmartBuildAxis previewAxis,
                    out int previewSign,
                    out int previewWidth,
                    out int? previewRightStart,
                    out Vector3i previewCuboidSize))
            {
                candidate = BuildPlacementCandidate(
                    previewConstruct,
                    previewCell,
                    previewAxis,
                    previewSign,
                    previewWidth,
                    previewRightStart,
                    previewCuboidSize);
                return true;
            }

            if (TrySeedFromPointedBlock(
                    out AllConstruct hitConstruct,
                    out Vector3i hitCell,
                    out SmartBuildAxis hitAxis,
                    out int hitSign))
            {
                candidate = BuildPlacementCandidate(hitConstruct, hitCell, hitAxis, hitSign, 1, null, new Vector3i(1, 1, 1));
                return true;
            }

            if (TrySeedFromDrawPlane(out AllConstruct construct, out Vector3i cell))
            {
                candidate = BuildPlacementCandidate(construct, cell, SmartBuildAxis.Z, 1, 1, null, new Vector3i(1, 1, 1));
                return true;
            }

            return false;
        }

        private SmartBuildPlacementCandidate BuildPlacementCandidate(
            AllConstruct construct,
            Vector3i cell,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            int width,
            int? rightStart,
            Vector3i cuboidSize)
        {
            if (_selectedShape == SmartBuildShapeKind.DownSlope &&
                rightStart.HasValue)
            {
                DeriveRightAxis(forwardAxis, forwardSign, out SmartBuildAxis rightAxis, out _);
                cell = SmartBuildAxisHelper.Set(cell, rightAxis, rightStart.Value);
            }

            bool valid = CanPlaceDraftOrigin(construct, cell, out string reason);
            return new SmartBuildPlacementCandidate(
                construct,
                cell,
                forwardAxis,
                forwardSign,
                Math.Max(1, width),
                rightStart,
                SmartBuildDraft.ClampSize(cuboidSize),
                valid,
                reason);
        }

        private void CancelAddMode()
        {
            _tool = _draft != null ? SmartBuildTool.Scale : SmartBuildTool.Draw;
            InfoStore.Add(_draft != null
                ? "Add cancelled. Editing selected Smart Builder piece."
                : "Add cancelled.");
        }

        private void PlacePendingSymmetryPlane()
        {
            if (!TrySymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell))
            {
                InfoStore.Add("Point at the focused construct grid to place the symmetry plane.");
                return;
            }

            if (!DecoLimitLifter.EsuSymmetry.TryPlacePending(
                    construct,
                    cell,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            RebuildPlan();
            InfoStore.Add("Symmetry plane placed.");
        }

        private void CreatePreviewAtSeed(SmartBuildPlacementCandidate candidate)
        {
            CreatePreviewAtSeed(
                candidate.Construct,
                candidate.Cell,
                candidate.ForwardAxis,
                candidate.ForwardSign,
                candidate.Width,
                candidate.RightStart,
                candidate.CuboidSize);
        }

        private void CreatePreviewAtSeed(
            AllConstruct construct,
            Vector3i cell,
            SmartBuildAxis forwardAxis = SmartBuildAxis.Z,
            int forwardSign = 1,
            int width = 1,
            int? rightStart = null,
            Vector3i? cuboidSize = null)
        {
            if (!CanPlaceDraftOrigin(construct, cell, out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            if (_scene != null && !ReferenceEquals(_scene.Construct, construct))
            {
                InfoStore.Add("Apply or cancel the current Smart Builder scene before starting on another construct.");
                return;
            }

            RecordSceneHistory();
            if (_scene == null)
                _scene = new SmartBuildPieceScene(construct);

            if (_selectedShape == SmartBuildShapeKind.DownSlope &&
                rightStart.HasValue)
            {
                DeriveRightAxis(forwardAxis, forwardSign, out SmartBuildAxis rightAxis, out _);
                cell = SmartBuildAxisHelper.Set(cell, rightAxis, rightStart.Value);
            }

            _draft = _selectedShape == SmartBuildShapeKind.DownSlope
                ? SmartBuildPiece.CreateDownSlope(
                    construct,
                    cell,
                    _selectedSlopeLength,
                    forwardAxis,
                    forwardSign,
                    Math.Max(1, width),
                    _drawPlane,
                    _defaultSlopeSupportMode)
                : SmartBuildPiece.CreateCuboid(construct, cell, cuboidSize ?? new Vector3i(1, 1, 1), _drawPlane);
            _scene.Add(_draft);
            _tool = SmartBuildTool.Scale;
            RebuildPlan();
            InfoStore.Add(_draft.ShapeLabel() + " added. Scale mode active.");
        }

        private bool CanPlaceDraftOrigin(
            AllConstruct construct,
            Vector3i cell,
            out string reason)
        {
            if (construct == null)
            {
                reason = "Point at the focused construct grid to create a Smart Builder preview.";
                return false;
            }

            if (IsCellOccupied(construct, cell))
            {
                reason = "Smart Builder origin is occupied. Pick an empty cell.";
                return false;
            }

            reason = null;
            return true;
        }

        private bool TrySeedFromPointedBlock(
            out AllConstruct construct,
            out Vector3i cell,
            out SmartBuildAxis axis,
            out int sign)
        {
            construct = null;
            cell = default;
            axis = SmartBuildAxis.Z;
            sign = 1;
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
                return false;

            Vector3 localNormal = WorldNormalToLocal(hit);
            construct = hit.Construct;
            cell = AdjacentCellFromSurfaceHit(hit.Anchor, hit.LocalHit, localNormal, out axis, out sign);
            return construct != null;
        }

        internal static Vector3i AdjacentCellFromSurfaceHit(
            Vector3i anchor,
            Vector3 localHit,
            Vector3 localNormal)
        {
            return AdjacentCellFromSurfaceHit(anchor, localHit, localNormal, out _, out _);
        }

        private static Vector3i AdjacentCellFromSurfaceHit(
            Vector3i anchor,
            Vector3 localHit,
            Vector3 localNormal,
            out SmartBuildAxis axis,
            out int sign)
        {
            Vector3 center = new Vector3(anchor.x, anchor.y, anchor.z);
            Vector3 offset = localHit - center;
            axis = SmartBuildAxisHelper.FromLargestComponent(offset, out sign);
            if (Mathf.Abs(SmartBuildAxisHelper.Get(offset, axis)) < 0.18f)
                axis = SmartBuildAxisHelper.FromLargestComponent(localNormal, out sign);

            return anchor + SmartBuildAxisHelper.ToVector3i(axis, sign >= 0 ? 1 : -1);
        }

        private bool TrySeedFromPreviewFace(
            bool inheritPreviewScale,
            out AllConstruct construct,
            out Vector3i cell,
            out SmartBuildAxis axis,
            out int sign,
            out int width,
            out int? rightStart,
            out Vector3i cuboidSize)
        {
            construct = null;
            cell = default;
            axis = SmartBuildAxis.Z;
            sign = 1;
            width = 1;
            rightStart = null;
            cuboidSize = new Vector3i(1, 1, 1);
            if (_scene?.Construct == null || _scene.Count == 0)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!TryWorldToLocal(_scene.Construct, ray.origin, out Vector3 localOrigin) ||
                !TryWorldToLocal(_scene.Construct, ray.origin + ray.direction, out Vector3 localTarget))
            {
                return false;
            }

            Vector3 localDirection = localTarget - localOrigin;
            if (localDirection.sqrMagnitude <= 0.0001f)
                return false;

            localDirection.Normalize();
            float bestDistance = float.PositiveInfinity;
            SmartBuildPiece bestPiece = null;
            Vector3 bestHit = Vector3.zero;
            DecorationEditAxis bestAxis = DecorationEditAxis.None;
            int bestSign = 1;
            foreach (SmartBuildPiece piece in _scene.Pieces)
            {
                SmartBuildVolume volume = piece.ToVolume();
                if (volume == null)
                    continue;

                Vector3[] corners = volume.GetLocalCorners();
                Vector3 min = corners[0];
                Vector3 max = corners[6];
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.X, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.X, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Y, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Y, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Z, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Z, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
            }

            if (bestPiece == null || bestAxis == DecorationEditAxis.None)
                return false;

            SmartBuildVolume bestVolume = bestPiece.ToVolume();
            axis = SmartBuildDraft.ToSmartAxis(bestAxis);
            sign = bestSign >= 0 ? 1 : -1;
            cell = AdjacentCellFromPreviewFace(bestVolume, bestHit, axis, sign);
            if (_selectedShape != SmartBuildShapeKind.DownSlope &&
                inheritPreviewScale)
            {
                cell = SlabFromPreviewFace(bestVolume, axis, sign, out cuboidSize);
            }

            if (axis == SmartBuildAxis.Y)
            {
                axis = bestPiece.ShapeKind == SmartBuildShapeKind.DownSlope
                    ? bestPiece.ForwardAxis
                    : SmartBuildAxis.Z;
                sign = bestPiece.ShapeKind == SmartBuildShapeKind.DownSlope
                    ? bestPiece.ForwardSign
                    : 1;
            }

            if (_selectedShape == SmartBuildShapeKind.DownSlope &&
                inheritPreviewScale)
            {
                width = bestPiece.FaceHorizontalWidth(axis);
                DeriveRightAxis(axis, sign, out SmartBuildAxis rightAxis, out int rightSign);
                rightStart = rightSign >= 0
                    ? SmartBuildAxisHelper.Get(bestVolume.MinCell, rightAxis)
                    : SmartBuildAxisHelper.Get(bestVolume.MaxCell, rightAxis);
            }

            construct = _scene.Construct;
            return true;
        }

        private static Vector3i SlabFromPreviewFace(
            SmartBuildVolume volume,
            SmartBuildAxis normalAxis,
            int normalSign,
            out Vector3i size)
        {
            Vector3i min = volume.MinCell;
            Vector3i max = volume.MaxCell;
            int faceComponent = normalSign >= 0
                ? SmartBuildAxisHelper.Get(max, normalAxis) + 1
                : SmartBuildAxisHelper.Get(min, normalAxis) - 1;
            min = SmartBuildAxisHelper.Set(min, normalAxis, faceComponent);
            max = SmartBuildAxisHelper.Set(max, normalAxis, faceComponent);
            Vector3i slabMin = new Vector3i(
                Math.Min(min.x, max.x),
                Math.Min(min.y, max.y),
                Math.Min(min.z, max.z));
            Vector3i slabMax = new Vector3i(
                Math.Max(min.x, max.x),
                Math.Max(min.y, max.y),
                Math.Max(min.z, max.z));
            size = new Vector3i(
                slabMax.x - slabMin.x + 1,
                slabMax.y - slabMin.y + 1,
                slabMax.z - slabMin.z + 1);
            return slabMin;
        }

        private bool TrySelectPreviewPieceAtPointer()
        {
            if (!TryPickPreviewPiece(out SmartBuildPiece piece))
                return false;

            if (_scene.Select(piece.Id))
            {
                _draft = _scene.SelectedPiece;
                RebuildPlan();
                InfoStore.Add("Selected Smart Builder piece #" + piece.Id.ToString(CultureInfo.InvariantCulture) + ".");
                return true;
            }

            return false;
        }

        private bool TryOpenPreviewContextMenu()
        {
            if (!TryPickPreviewPiece(out SmartBuildPiece piece))
                return false;

            if (_scene.Select(piece.Id))
            {
                _draft = _scene.SelectedPiece;
                _contextMenuPieceId = piece.Id;
                Vector2 mouse = MouseGuiPosition();
                float width = EsuHudLayout.Scale(142f);
                float height = EsuHudLayout.Scale(168f);
                _contextMenuRect = new Rect(mouse.x, mouse.y, width, height);
                _contextMenuRect.x = Mathf.Clamp(_contextMenuRect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
                _contextMenuRect.y = Mathf.Clamp(_contextMenuRect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
                RebuildPlan();
                return true;
            }

            return false;
        }

        private bool TryPickPreviewPiece(out SmartBuildPiece bestPiece)
        {
            bestPiece = null;
            if (_scene?.Construct == null || _scene.Count == 0)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!TryWorldToLocal(_scene.Construct, ray.origin, out Vector3 localOrigin) ||
                !TryWorldToLocal(_scene.Construct, ray.origin + ray.direction, out Vector3 localTarget))
            {
                return false;
            }

            Vector3 localDirection = localTarget - localOrigin;
            if (localDirection.sqrMagnitude <= 0.0001f)
                return false;

            localDirection.Normalize();
            float bestDistance = float.PositiveInfinity;
            Vector3 bestHit = Vector3.zero;
            DecorationEditAxis bestAxis = DecorationEditAxis.None;
            int bestSign = 1;
            foreach (SmartBuildPiece piece in _scene.Pieces)
            {
                SmartBuildVolume volume = piece.ToVolume();
                if (volume == null)
                    continue;

                Vector3[] corners = volume.GetLocalCorners();
                Vector3 min = corners[0];
                Vector3 max = corners[6];
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.X, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.X, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Y, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Y, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Z, -1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
                TryPickFacePlaneHit(localOrigin, localDirection, min, max, DecorationEditAxis.Z, 1, ref bestPiece, piece, ref bestAxis, ref bestSign, ref bestHit, ref bestDistance);
            }

            return bestPiece != null;
        }

        private static void TryPickFacePlaneHit(
            Vector3 localOrigin,
            Vector3 localDirection,
            Vector3 min,
            Vector3 max,
            DecorationEditAxis candidate,
            int candidateSign,
            ref SmartBuildPiece bestPiece,
            SmartBuildPiece piece,
            ref DecorationEditAxis bestAxis,
            ref int bestSign,
            ref Vector3 bestHit,
            ref float bestDistance)
        {
            float direction = AxisComponent(localDirection, candidate);
            if (Mathf.Abs(direction) <= 0.0001f)
                return;

            float plane = candidateSign >= 0
                ? AxisComponent(max, candidate)
                : AxisComponent(min, candidate);
            float distance = (plane - AxisComponent(localOrigin, candidate)) / direction;
            if (distance < 0f || distance >= bestDistance)
                return;

            Vector3 hit = localOrigin + localDirection * distance;
            if (!PointInsideFace(hit, min, max, candidate))
                return;

            bestPiece = piece;
            bestAxis = candidate;
            bestSign = candidateSign;
            bestHit = hit;
            bestDistance = distance;
        }

        private static Vector3i AdjacentCellFromPreviewFace(
            SmartBuildVolume volume,
            Vector3 hit,
            SmartBuildAxis axis,
            int sign)
        {
            Vector3i min = volume.MinCell;
            Vector3i max = volume.MaxCell;
            int x = Mathf.Clamp(Mathf.RoundToInt(hit.x), min.x, max.x);
            int y = Mathf.Clamp(Mathf.RoundToInt(hit.y), min.y, max.y);
            int z = Mathf.Clamp(Mathf.RoundToInt(hit.z), min.z, max.z);
            Vector3i cell = new Vector3i(x, y, z);
            int component = sign >= 0
                ? SmartBuildAxisHelper.Get(max, axis) + 1
                : SmartBuildAxisHelper.Get(min, axis) - 1;
            return SmartBuildAxisHelper.Set(cell, axis, component);
        }

        private bool TrySymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell)
        {
            construct = null;
            cell = default;
            if (_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                construct = hit.Construct;
                cell = hit.Anchor;
                return construct != null;
            }

            return TrySeedFromDrawPlane(out construct, out cell);
        }

        private bool TrySeedFromDrawPlane(out AllConstruct construct, out Vector3i cell)
        {
            construct = FocusedConstruct();
            cell = default;
            Camera camera = Camera.main ?? Camera.current;
            if (construct == null || camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!TryWorldToLocal(construct, ray.origin, out Vector3 localOrigin) ||
                !TryWorldToLocal(construct, ray.origin + ray.direction, out Vector3 localTarget))
            {
                return false;
            }

            Vector3 localDirection = localTarget - localOrigin;
            if (localDirection.sqrMagnitude <= 0.0001f)
                return false;
            localDirection.Normalize();

            Vector3 planePoint = DrawPlanePoint(construct);
            Vector3 normal = DrawPlaneNormal(construct, camera);
            float denominator = Vector3.Dot(localDirection, normal);
            if (Mathf.Abs(denominator) <= 0.0001f)
                return false;

            float distance = Vector3.Dot(planePoint - localOrigin, normal) / denominator;
            if (distance < 0f || float.IsNaN(distance) || float.IsInfinity(distance))
                return false;

            Vector3 localHit = localOrigin + localDirection * distance;
            cell = RoundToCell(localHit);
            return true;
        }

        private AllConstruct FocusedConstruct()
        {
            try
            {
                return _build.GetC() ?? _build.GetCC();
            }
            catch
            {
                try
                {
                    return _build.GetCC();
                }
                catch
                {
                    return null;
                }
            }
        }

        private Vector3 DrawPlanePoint(AllConstruct construct)
        {
            if (_draft?.Construct == construct)
                return _draft.CenterLocal;

            try
            {
                return construct.SafeGlobalToLocal(construct.SafePosition);
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private Vector3 DrawPlaneNormal(AllConstruct construct, Camera camera)
        {
            switch (_drawPlane)
            {
                case SmartBuildDrawPlane.XY:
                    return Vector3.forward;
                case SmartBuildDrawPlane.XZ:
                    return Vector3.up;
                case SmartBuildDrawPlane.YZ:
                    return Vector3.right;
                default:
                    if (construct?.myTransform != null && camera != null)
                    {
                        Vector3 normal = construct.myTransform.InverseTransformDirection(camera.transform.forward);
                        if (normal.sqrMagnitude > 0.0001f)
                            return normal.normalized;
                    }
                    return Vector3.forward;
            }
        }

        private bool TryBeginHandleDrag()
        {
            if (_draft == null || (_tool != SmartBuildTool.Move && _tool != SmartBuildTool.Scale))
                return false;

            if (_tool == SmartBuildTool.Move)
            {
                if (TryPickHandle(out DecorationEditAxis moveAxis, out int moveSign))
                    return BeginDrag(moveAxis, moveSign, fromFace: false);
                return false;
            }

            switch (_editHandleMode)
            {
                case SmartBuildEditHandleMode.Face:
                    if (TryPickFace(out DecorationEditAxis faceAxis, out int faceSign))
                        return BeginDrag(faceAxis, faceSign, fromFace: true);
                    break;
                case SmartBuildEditHandleMode.Edge:
                    if (TryPickEdge(out DecorationEditAxis[] edgeAxes, out int[] edgeSigns))
                        return BeginDrag(edgeAxes, edgeSigns, fromFace: false);
                    break;
                case SmartBuildEditHandleMode.Corner:
                    if (TryPickCorner(out DecorationEditAxis[] cornerAxes, out int[] cornerSigns))
                        return BeginDrag(cornerAxes, cornerSigns, fromFace: false);
                    break;
                default:
                    if (TryPickHandle(out DecorationEditAxis axis, out int sign))
                        return BeginDrag(axis, sign, fromFace: false);
                    break;
            }

            return false;
        }

        private bool BeginDrag(DecorationEditAxis axis, int sign, bool fromFace)
        {
            return BeginDrag(
                new[] { axis },
                new[] { sign },
                fromFace);
        }

        private bool BeginDrag(
            DecorationEditAxis[] axes,
            int[] signs,
            bool fromFace)
        {
            _dragging = true;
            _dragTool = _tool;
            _dragAxes = axes ?? Array.Empty<DecorationEditAxis>();
            _dragSigns = signs ?? Array.Empty<int>();
            _dragAxis = _dragAxes.Length > 0 ? _dragAxes[0] : DecorationEditAxis.None;
            _dragSign = _dragSigns.Length > 0 ? _dragSigns[0] : 1;
            _dragStartedFromFace = fromFace;
            _dragMouseStart = MouseGuiPosition();
            _dragPieceStart = _draft.Clone();
            _dragSceneStart = CaptureSceneSnapshot();
            return true;
        }

        private void UpdateHandleDrag()
        {
            if (_draft == null || _dragAxis == DecorationEditAxis.None || _dragAxes.Length == 0)
                return;

            _draft.CopyFrom(_dragPieceStart);
            if (_dragTool == SmartBuildTool.Move)
            {
                int cells = ProjectDragDeltaToCells(_dragAxis, SmartMoveStepCells);
                _draft.MoveBy(
                    SmartBuildAxisHelper.ToVector3i(
                        SmartBuildDraft.ToSmartAxis(_dragAxis),
                        cells));
            }
            else if (_dragTool == SmartBuildTool.Scale)
            {
                for (int index = 0; index < _dragAxes.Length; index++)
                {
                    DecorationEditAxis axis = _dragAxes[index];
                    if (axis == DecorationEditAxis.None)
                        continue;

                    int sign = index < _dragSigns.Length ? _dragSigns[index] : 1;
                    int cells = ProjectDragDeltaToCells(axis, SmartScaleStepCells);
                    _draft.ResizeFromHandle(axis, sign, cells);
                }
            }

            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private void EndDrag(bool resetDraft)
        {
            if (resetDraft && _draft != null)
            {
                _draft.CopyFrom(_dragPieceStart);
            }

            if (_draft != null)
            {
                if (!resetDraft)
                    PushSceneSnapshot(_dragSceneStart);
                RebuildPlan();
            }
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _dragAxes = Array.Empty<DecorationEditAxis>();
            _dragSigns = Array.Empty<int>();
            _dragPieceStart = null;
            _dragSceneStart = null;
            _dragStartedFromFace = false;
        }

        private int ProjectDragDeltaToCells(DecorationEditAxis axis, int stepCells)
        {
            if (_draft?.Construct == null)
                return 0;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return 0;

            Vector2 mouseDelta = MouseGuiPosition() - _dragMouseStart;
            Vector3 center = _draft.CenterLocal;
            Vector3 axisVector = DecorationEditMath.AxisVector(axis);
            Vector3 start = _draft.Construct.SafeLocalToGlobal(center);
            Vector3 end = _draft.Construct.SafeLocalToGlobal(center + axisVector * HandleLength);
            Vector3 screenStart = camera.WorldToScreenPoint(start);
            Vector3 screenEnd = camera.WorldToScreenPoint(end);
            if (screenStart.z <= camera.nearClipPlane || screenEnd.z <= camera.nearClipPlane)
                return 0;

            Vector2 guiStart = ScreenToGui(screenStart);
            Vector2 guiEnd = ScreenToGui(screenEnd);
            float projected = DecorationEditMath.ProjectMouseDeltaToAxis(
                mouseDelta,
                guiStart,
                guiEnd,
                HandleLength);
            int step = Mathf.Max(1, stepCells);
            return Mathf.RoundToInt(projected / step) * step;
        }

        private bool TryPickHandle(out DecorationEditAxis axis, out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            if (_draft?.Construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Vector2 mouse = MouseGuiPosition();
            Vector3 center = _draft.CenterLocal;
            Vector3 centerWorld = _draft.Construct.SafeLocalToGlobal(center);
            Vector3 screenCenter = camera.WorldToScreenPoint(centerWorld);
            if (screenCenter.z <= camera.nearClipPlane)
                return false;

            Vector2 guiCenter = ScreenToGui(screenCenter);
            float best = AxisPickThresholdPixels;
            TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.X, 1, ref axis, ref sign, ref best);
            TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.Y, 1, ref axis, ref sign, ref best);
            TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.Z, 1, ref axis, ref sign, ref best);
            if (_tool == SmartBuildTool.Scale)
            {
                TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.X, -1, ref axis, ref sign, ref best);
                TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.Y, -1, ref axis, ref sign, ref best);
                TryPickAxisEndpoint(camera, mouse, guiCenter, center, DecorationEditAxis.Z, -1, ref axis, ref sign, ref best);
            }

            return axis != DecorationEditAxis.None;
        }

        private void TryPickAxisEndpoint(
            Camera camera,
            Vector2 mouse,
            Vector2 guiCenter,
            Vector3 center,
            DecorationEditAxis candidate,
            int candidateSign,
            ref DecorationEditAxis bestAxis,
            ref int bestSign,
            ref float bestDistance)
        {
            Vector3 axisVector = DecorationEditMath.AxisVector(candidate) * candidateSign;
            Vector3 world = _draft.Construct.SafeLocalToGlobal(center + axisVector * HandleLength);
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= camera.nearClipPlane)
                return;

            Vector2 guiEnd = ScreenToGui(screen);
            float distance = DistanceToSegment(mouse, guiCenter, guiEnd);
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            bestAxis = candidate;
            bestSign = candidateSign;
        }

        private bool TryPickFace(out DecorationEditAxis axis, out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            if (_draft?.Construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null ||
                !TryLocalMouseRay(camera, out Vector3 localOrigin, out Vector3 localDirection))
            {
                return false;
            }

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryPickDownSlopeFace(localOrigin, localDirection, out axis, out sign);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            Vector3 min = corners[0];
            Vector3 max = corners[6];
            float bestDistance = float.PositiveInfinity;
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.X, -1, ref axis, ref sign, ref bestDistance);
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.X, 1, ref axis, ref sign, ref bestDistance);
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.Y, -1, ref axis, ref sign, ref bestDistance);
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.Y, 1, ref axis, ref sign, ref bestDistance);
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.Z, -1, ref axis, ref sign, ref bestDistance);
            TryPickFacePlane(localOrigin, localDirection, min, max, DecorationEditAxis.Z, 1, ref axis, ref sign, ref bestDistance);
            return axis != DecorationEditAxis.None;
        }

        private bool TryPickCorner(
            out DecorationEditAxis[] axes,
            out int[] signs)
        {
            axes = Array.Empty<DecorationEditAxis>();
            signs = Array.Empty<int>();
            if (_draft?.Construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryPickDownSlopeCorner(camera, out axes, out signs);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            Vector3 min = corners[0];
            Vector3 max = corners[6];
            Vector2 mouse = MouseGuiPosition();
            float best = AxisPickThresholdPixels;
            int bestIndex = -1;
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 screen = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(corners[index]));
                if (screen.z <= camera.nearClipPlane)
                    continue;

                float distance = Vector2.Distance(mouse, ScreenToGui(screen));
                if (distance >= best)
                    continue;

                best = distance;
                bestIndex = index;
            }

            if (bestIndex < 0)
                return false;

            Vector3 corner = corners[bestIndex];
            axes = new[] { DecorationEditAxis.X, DecorationEditAxis.Y, DecorationEditAxis.Z };
            signs = new[]
            {
                SignForCoordinate(corner.x, min.x, max.x),
                SignForCoordinate(corner.y, min.y, max.y),
                SignForCoordinate(corner.z, min.z, max.z)
            };
            return true;
        }

        private bool TryPickEdge(
            out DecorationEditAxis[] axes,
            out int[] signs)
        {
            axes = Array.Empty<DecorationEditAxis>();
            signs = Array.Empty<int>();
            if (_draft?.Construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryPickDownSlopeEdge(camera, out axes, out signs);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            Vector3 min = corners[0];
            Vector3 max = corners[6];
            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
            };

            Vector2 mouse = MouseGuiPosition();
            float best = AxisPickThresholdPixels;
            int bestEdge = -1;
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                Vector3 a = corners[edges[edge, 0]];
                Vector3 b = corners[edges[edge, 1]];
                Vector3 screenA = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(a));
                Vector3 screenB = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(b));
                if (screenA.z <= camera.nearClipPlane || screenB.z <= camera.nearClipPlane)
                    continue;

                float distance = DistanceToSegment(mouse, ScreenToGui(screenA), ScreenToGui(screenB));
                if (distance >= best)
                    continue;

                best = distance;
                bestEdge = edge;
            }

            if (bestEdge < 0)
                return false;

            Vector3 first = corners[edges[bestEdge, 0]];
            Vector3 second = corners[edges[bestEdge, 1]];
            var edgeAxes = new List<DecorationEditAxis>(2);
            var edgeSigns = new List<int>(2);
            AddFixedEdgeAxis(DecorationEditAxis.X, first.x, second.x, min.x, max.x, edgeAxes, edgeSigns);
            AddFixedEdgeAxis(DecorationEditAxis.Y, first.y, second.y, min.y, max.y, edgeAxes, edgeSigns);
            AddFixedEdgeAxis(DecorationEditAxis.Z, first.z, second.z, min.z, max.z, edgeAxes, edgeSigns);
            if (edgeAxes.Count == 0)
                return false;

            axes = edgeAxes.ToArray();
            signs = edgeSigns.ToArray();
            return true;
        }

        private static void AddFixedEdgeAxis(
            DecorationEditAxis axis,
            float first,
            float second,
            float min,
            float max,
            List<DecorationEditAxis> axes,
            List<int> signs)
        {
            if (Mathf.Abs(first - second) > 0.001f)
                return;

            axes.Add(axis);
            signs.Add(SignForCoordinate(first, min, max));
        }

        private static int SignForCoordinate(float value, float min, float max) =>
            Mathf.Abs(value - max) <= Mathf.Abs(value - min) ? 1 : -1;

        private bool TryLocalMouseRay(
            Camera camera,
            out Vector3 localOrigin,
            out Vector3 localDirection)
        {
            localOrigin = Vector3.zero;
            localDirection = Vector3.zero;
            if (_draft?.Construct == null || camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!TryWorldToLocal(_draft.Construct, ray.origin, out localOrigin) ||
                !TryWorldToLocal(_draft.Construct, ray.origin + ray.direction, out Vector3 localTarget))
            {
                return false;
            }

            localDirection = localTarget - localOrigin;
            if (localDirection.sqrMagnitude <= 0.0001f)
                return false;

            localDirection.Normalize();
            return true;
        }

        private static void TryPickFacePlane(
            Vector3 localOrigin,
            Vector3 localDirection,
            Vector3 min,
            Vector3 max,
            DecorationEditAxis candidate,
            int candidateSign,
            ref DecorationEditAxis bestAxis,
            ref int bestSign,
            ref float bestDistance)
        {
            float direction = AxisComponent(localDirection, candidate);
            if (Mathf.Abs(direction) <= 0.0001f)
                return;

            float plane = candidateSign >= 0
                ? AxisComponent(max, candidate)
                : AxisComponent(min, candidate);
            float distance = (plane - AxisComponent(localOrigin, candidate)) / direction;
            if (distance < 0f || distance >= bestDistance)
                return;

            Vector3 hit = localOrigin + localDirection * distance;
            if (!PointInsideFace(hit, min, max, candidate))
                return;

            bestDistance = distance;
            bestAxis = candidate;
            bestSign = candidateSign;
        }

        private static bool PointInsideFace(
            Vector3 point,
            Vector3 min,
            Vector3 max,
            DecorationEditAxis normalAxis)
        {
            const float epsilon = 0.02f;
            switch (normalAxis)
            {
                case DecorationEditAxis.X:
                    return point.y >= min.y - epsilon &&
                           point.y <= max.y + epsilon &&
                           point.z >= min.z - epsilon &&
                           point.z <= max.z + epsilon;
                case DecorationEditAxis.Y:
                    return point.x >= min.x - epsilon &&
                           point.x <= max.x + epsilon &&
                           point.z >= min.z - epsilon &&
                           point.z <= max.z + epsilon;
                default:
                    return point.x >= min.x - epsilon &&
                           point.x <= max.x + epsilon &&
                           point.y >= min.y - epsilon &&
                           point.y <= max.y + epsilon;
            }
        }

        private void RebuildPlan()
        {
            if (_scene == null || _scene.Count == 0)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
                return;
            }

            if (_source == null)
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreview();
                _previewCells = preview.Cells;
                _previewCellSets = preview.CellSets;
                _previewVolumes = preview.Volumes;
                _plan = new SmartBuildPlan(
                    _scene.Construct,
                    VolumeFromCells(_scene.Construct, _previewCells),
                    Array.Empty<SmartBuildPlacement>(),
                    Array.Empty<Vector3i>(),
                    Array.Empty<string>(),
                    canCommit: false,
                    failureReason:
                    _sourceReason ?? "Selected Smart Builder material is unavailable.");
                _planDirty = false;
                return;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_scene.Construct, out string reason))
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreview();
                _previewCells = preview.Cells;
                _previewCellSets = preview.CellSets;
                _previewVolumes = preview.Volumes;
                _plan = new SmartBuildPlan(
                    _scene.Construct,
                    VolumeFromCells(_scene.Construct, _previewCells),
                    Array.Empty<SmartBuildPlacement>(),
                    Array.Empty<Vector3i>(),
                    Array.Empty<string>(),
                    canCommit: false,
                    failureReason: reason);
                _planDirty = false;
                return;
            }

            _plan = _scene.BuildPlan(
                _source,
                IsOccupied,
                new SmartBuildPlannerOptions
                {
                    SkipOccupiedCells = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied
                },
                out SmartBuildPreviewSnapshot scenePreview);
            _previewCells = scenePreview.Cells;
            _previewCellSets = scenePreview.CellSets;
            _previewVolumes = scenePreview.Volumes;
            if (_plan.CanCommit && !EveryPreviewSetTouchesExistingConstruct())
            {
                _plan = new SmartBuildPlan(
                    _scene.Construct,
                    VolumeFromCells(_scene.Construct, _previewCells),
                    Array.Empty<SmartBuildPlacement>(),
                    _plan.SkippedCells,
                    _plan.Warnings,
                    canCommit: false,
                    failureReason: "Every symmetry preview must touch an existing block before Apply.");
            }

            _planDirty = false;
        }

        private void BuildPreviewSymmetrySets(SmartBuildVolume volume)
        {
            if (_scene != null)
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreview();
                _previewCells = preview.Cells;
                _previewCellSets = preview.CellSets;
                _previewVolumes = preview.Volumes;
                return;
            }

            Vector3i[] baseCells = volume.EnumerateCells().ToArray();
            var allCells = new Dictionary<string, Vector3i>();
            var sets = new List<IReadOnlyList<Vector3i>>();
            var volumes = new List<SmartBuildVolume>();
            var seenSets = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                Vector3i[] cells = baseCells
                    .Select(variant.Mirror)
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray();
                string signature = string.Join(
                    "|",
                    cells.Select(DecoLimitLifter.EsuSymmetry.CellKey).ToArray());
                if (!seenSets.Add(signature))
                    continue;

                sets.Add(cells);
                foreach (Vector3i cell in cells)
                    allCells[DecoLimitLifter.EsuSymmetry.CellKey(cell)] = cell;
                SmartBuildVolume mirrored = MirrorVolume(volume, variant);
                if (mirrored != null)
                    volumes.Add(mirrored);
            }

            _previewCells = allCells.Values
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.z)
                .ToArray();
            _previewCellSets = sets;
            _previewVolumes = volumes;
        }

        private void RefreshPreviewVolumesOnly()
        {
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            if (_scene == null || _scene.Count == 0)
            {
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                return;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_scene.Construct, out _))
            {
                _previewVolumes = _scene.Pieces
                    .Select(piece => piece.ToVolume())
                    .Where(volume => volume != null)
                    .ToArray();
                return;
            }

            SmartBuildPreviewSnapshot preview = _scene.BuildPreview();
            _previewCells = preview.Cells;
            _previewCellSets = preview.CellSets;
            _previewVolumes = preview.Volumes;
        }

        private static IReadOnlyList<SmartBuildVolume> BuildPreviewVolumesOnly(SmartBuildVolume volume)
        {
            if (volume == null)
                return Array.Empty<SmartBuildVolume>();

            var volumes = new List<SmartBuildVolume>();
            var seen = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                SmartBuildVolume mirrored = MirrorVolume(volume, variant);
                if (mirrored == null)
                    continue;

                if (seen.Add(VolumeSignature(mirrored)))
                    volumes.Add(mirrored);
            }

            return volumes;
        }

        private static SmartBuildVolume MirrorVolume(
            SmartBuildVolume volume,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            if (volume == null)
                return null;

            Vector3i min = volume.MinCell;
            Vector3i max = volume.MaxCell;
            Vector3i[] corners =
            {
                new Vector3i(min.x, min.y, min.z),
                new Vector3i(max.x, min.y, min.z),
                new Vector3i(max.x, min.y, max.z),
                new Vector3i(min.x, min.y, max.z),
                new Vector3i(min.x, max.y, min.z),
                new Vector3i(max.x, max.y, min.z),
                new Vector3i(max.x, max.y, max.z),
                new Vector3i(min.x, max.y, max.z)
            };

            Vector3i mirroredMin = variant.Mirror(corners[0]);
            Vector3i mirroredMax = mirroredMin;
            for (int index = 1; index < corners.Length; index++)
            {
                Vector3i mirrored = variant.Mirror(corners[index]);
                mirroredMin.x = Math.Min(mirroredMin.x, mirrored.x);
                mirroredMin.y = Math.Min(mirroredMin.y, mirrored.y);
                mirroredMin.z = Math.Min(mirroredMin.z, mirrored.z);
                mirroredMax.x = Math.Max(mirroredMax.x, mirrored.x);
                mirroredMax.y = Math.Max(mirroredMax.y, mirrored.y);
                mirroredMax.z = Math.Max(mirroredMax.z, mirrored.z);
            }

            return SmartBuildVolume.FromBounds(volume.Construct, mirroredMin, mirroredMax);
        }

        private static string VolumeSignature(SmartBuildVolume volume)
        {
            Vector3i min = volume.MinCell;
            Vector3i max = volume.MaxCell;
            return FormatCell(min) + "|" + FormatCell(max);
        }

        private bool IsOccupied(Vector3i cell)
        {
            return IsCellOccupied(_scene?.Construct, cell);
        }

        private bool IsCellOccupied(AllConstruct construct, Vector3i cell)
        {
            try
            {
                return construct?.AllBasics?.GetBlockViaLocalPosition(cell) != null;
            }
            catch
            {
                return true;
            }
        }

        private bool PlanTouchesExistingConstruct(SmartBuildPlan plan)
        {
            if (plan?.Construct == null)
                return false;

            foreach (SmartBuildPlacement placement in plan.Placements)
            {
                foreach (Vector3i cell in placement.CoveredCells())
                {
                    foreach (Vector3i neighbor in NeighborCells(cell))
                    {
                        if (IsCellOccupied(plan.Construct, neighbor))
                            return true;
                    }
                }
            }

            return false;
        }

        private bool EveryPreviewSetTouchesExistingConstruct()
        {
            if (_scene?.Construct == null || _previewCellSets == null || _previewCellSets.Count == 0)
                return false;

            foreach (IReadOnlyList<Vector3i> rawSet in _previewCellSets)
            {
                var target = rawSet?
                    .Where(cell => !IsCellOccupied(_scene.Construct, cell))
                    .ToArray() ?? Array.Empty<Vector3i>();
                if (target.Length == 0)
                    continue;

                var targetKeys = new HashSet<string>(
                    target.Select(DecoLimitLifter.EsuSymmetry.CellKey));
                bool touches = false;
                foreach (Vector3i cell in target)
                {
                    foreach (Vector3i neighbor in NeighborCells(cell))
                    {
                        if (!targetKeys.Contains(DecoLimitLifter.EsuSymmetry.CellKey(neighbor)) &&
                            IsCellOccupied(_scene.Construct, neighbor))
                        {
                            touches = true;
                            break;
                        }
                    }

                    if (touches)
                        break;
                }

                if (!touches)
                    return false;
            }

            return true;
        }

        private static Vector3i[] NeighborCells(Vector3i cell)
        {
            return new[]
            {
                cell + new Vector3i(1, 0, 0),
                cell + new Vector3i(-1, 0, 0),
                cell + new Vector3i(0, 1, 0),
                cell + new Vector3i(0, -1, 0),
                cell + new Vector3i(0, 0, 1),
                cell + new Vector3i(0, 0, -1)
            };
        }

        private void ApplyPreview()
        {
            if (_dragging)
            {
                InfoStore.Add("Finish the Smart Builder drag before applying.");
                return;
            }

            if (_rotating)
            {
                InfoStore.Add("Finish the Smart Builder rotation before applying.");
                return;
            }

            if (_planDirty)
                RebuildPlan();

            if (_plan == null)
            {
                InfoStore.Add("Create a Smart Builder preview before applying.");
                return;
            }

            if (!SmartBuildCommitter.TryCommit(_build, _plan, out string message))
            {
                InfoStore.Add(message);
                return;
            }

            InfoStore.Add(message);
            CancelPreview();
        }

        private void RecordSceneHistory()
        {
            SmartBuildSceneSnapshot snapshot = CaptureSceneSnapshot();
            if (snapshot == null)
                return;

            _sceneUndo.Push(snapshot);
            while (_sceneUndo.Count > SceneHistoryLimit)
            {
                SmartBuildSceneSnapshot[] retained = _sceneUndo
                    .Take(SceneHistoryLimit)
                    .Reverse()
                    .ToArray();
                _sceneUndo.Clear();
                foreach (SmartBuildSceneSnapshot entry in retained)
                    _sceneUndo.Push(entry);
            }

            _sceneRedo.Clear();
        }

        private SmartBuildSceneSnapshot CaptureSceneSnapshot() =>
            new SmartBuildSceneSnapshot(
                _scene?.Construct ?? _draft?.Construct,
                _scene?.Pieces,
                _draft?.Id ?? _scene?.SelectedPiece?.Id ?? -1,
                _tool,
                _selectedShape,
                _selectedSlopeLength,
                _editHandleMode,
                _defaultSlopeSupportMode,
                _previewMode);

        private void PushSceneSnapshot(SmartBuildSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _sceneUndo.Push(snapshot);
            while (_sceneUndo.Count > SceneHistoryLimit)
            {
                SmartBuildSceneSnapshot[] retained = _sceneUndo
                    .Take(SceneHistoryLimit)
                    .Reverse()
                    .ToArray();
                _sceneUndo.Clear();
                foreach (SmartBuildSceneSnapshot entry in retained)
                    _sceneUndo.Push(entry);
            }

            _sceneRedo.Clear();
        }

        private void UndoSceneEdit()
        {
            if (_sceneUndo.Count == 0)
                return;

            SmartBuildSceneSnapshot redo = CaptureSceneSnapshot();
            SmartBuildSceneSnapshot undo = _sceneUndo.Pop();
            if (redo != null)
                _sceneRedo.Push(redo);
            RestoreSceneSnapshot(undo);
            InfoStore.Add("Smart Builder preview undo.");
        }

        private void RedoSceneEdit()
        {
            if (_sceneRedo.Count == 0)
                return;

            SmartBuildSceneSnapshot undo = CaptureSceneSnapshot();
            SmartBuildSceneSnapshot redo = _sceneRedo.Pop();
            if (undo != null)
                _sceneUndo.Push(undo);
            RestoreSceneSnapshot(redo);
            InfoStore.Add("Smart Builder preview redo.");
        }

        private void RestoreSceneSnapshot(SmartBuildSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _tool = snapshot.Tool;
            _selectedShape = snapshot.SelectedShape;
            _selectedSlopeLength = snapshot.SelectedSlopeLength;
            _editHandleMode = snapshot.EditHandleMode;
            _defaultSlopeSupportMode = snapshot.DefaultSlopeSupportMode;
            _previewMode = snapshot.PreviewMode;
            if (snapshot.Pieces.Count == 0 || snapshot.Construct == null)
            {
                _scene = null;
                _draft = null;
            }
            else
            {
                _scene = new SmartBuildPieceScene(snapshot.Construct);
                _scene.ReplaceWith(snapshot.Pieces.Select(piece => piece.Clone()), snapshot.SelectedPieceId);
                _draft = _scene.SelectedPiece;
            }

            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _dragAxes = Array.Empty<DecorationEditAxis>();
            _dragSigns = Array.Empty<int>();
            _dragPieceStart = null;
            _dragSceneStart = null;
            ClearRotateDragState();
            _contextMenuOpen = false;
            RebuildPlan();
        }

        private void CancelPreview()
        {
            ClearApplyCancelAttention();
            _scene = null;
            _draft = null;
            _dragPieceStart = null;
            _plan = null;
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _planDirty = false;
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
            _dragSceneStart = null;
            ClearRotateDragState();
            _contextMenuOpen = false;
            _sceneUndo.Clear();
            _sceneRedo.Clear();
        }

        private void RefreshSelection()
        {
            if (_source != null &&
                _sourceReason == null &&
                _sourceMaterial == _selectedMaterial)
            {
                return;
            }

            if (SmartBlockFamilyCatalog.TryCreateMaterialSource(
                    _selectedMaterial,
                    out SmartBuildSource source,
                    out string reason))
            {
                _source = source;
                _sourceMaterial = _selectedMaterial;
                _sourceReason = null;
                if (_draft != null)
                    RebuildPlan();
                return;
            }

            _source = null;
            _sourceMaterial = _selectedMaterial;
            _sourceReason = reason;
            if (_scene == null || _scene.Count == 0)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
            }
            else
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreview();
                _previewCells = preview.Cells;
                _previewCellSets = preview.CellSets;
                _previewVolumes = preview.Volumes;
                _plan = new SmartBuildPlan(
                    _scene.Construct,
                    VolumeFromCells(_scene.Construct, _previewCells),
                    Array.Empty<SmartBuildPlacement>(),
                    Array.Empty<Vector3i>(),
                    Array.Empty<string>(),
                    canCommit: false,
                    failureReason: reason);
                _planDirty = false;
            }
        }

        private void DrawToolbar(int id)
        {
            EsuHudNotifications.SetActiveSource("Smart Builder");
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
                ToolButton(SmartBuildTool.Draw, "create", "Add", "Click the focused construct grid to add the selected shape as a new preview piece.");
                ToolButton(SmartBuildTool.Move, "move", "Move", "Drag RGB handles to move the preview by " + EsuTransformSnapSettings.SmartMoveStepCells + " cell step(s).");
                ToolButton(SmartBuildTool.Rotate, "rotate", "Rotate", "Drag RGB rotation rings around the preview center.");
                ToolButton(SmartBuildTool.Scale, "scale", "Scale", "Drag RGB handles to resize the preview by " + EsuTransformSnapSettings.SmartScaleStepCells + " cell step(s).");
                PlaneButton();
                OccupancyButton();
                ViewButton();
                SymmetryButton(DecorationEditAxis.X);
                SymmetryButton(DecorationEditAxis.Y);
                SymmetryButton(DecorationEditAxis.Z);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(budget.Gap);
            EsuHudNotifications.DrawToolbarSlot(
                frame.Rect,
                budget.NotificationWidth,
                "ESU mode: Smart Builder.",
                new Vector2(_toolbarRect.x + frame.Rect.x, _toolbarRect.y + frame.Rect.y));
            GUILayout.Space(budget.Gap);
            GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth));
            ToolbarPanelToggle("settings", "Info", ref _showLeftPanel, "Show or hide Smart Builder information and material controls.");
            ToolbarPanelToggle("build", "Shapes", ref _showRightPanel, "Show or hide Smart Builder shape and scene lists.");
            if (CompactIconButton("undo", $"U{_sceneUndo.Count}", DecorationEditorTheme.Button, "Undo the last Smart Builder preview edit.", _sceneUndo.Count > 0))
                UndoSceneEdit();
            if (CompactIconButton("redo", $"R{_sceneRedo.Count}", DecorationEditorTheme.Button, "Redo the last Smart Builder preview edit.", _sceneRedo.Count > 0))
                RedoSceneEdit();
            if (AttentionIconButton("save", "Apply", DecorationEditorTheme.Button, "Place the planned blocks.", !_planDirty && _plan != null && _plan.CanCommit))
                ApplyPreview();
            if (AttentionIconButton("cancel", "Cancel", DecorationEditorTheme.Button, "Clear the runtime preview.", _draft != null))
                CancelPreview();
            if (IconButton("delete", "Close", DecorationEditorTheme.Button, "Close Smart Block Builder."))
                _closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
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

            Rect rect = ViewModeMenuRect(toolbarRect);
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
                    new GUIContent(label, "Switch Smart Builder view to " + ViewModeDisplayName(mode) + "."),
                    DecorationEditorTheme.ToolButton(_viewMode == mode)))
            {
                SelectViewMode(mode);
            }
        }

        private void SelectViewMode(DecorationEditorViewMode mode)
        {
            _viewMode = mode;
            _viewModeMenuOpen = false;
            ApplySmartViewMode();
            InfoStore.Add("Smart Builder view: " + ViewModeDisplayName(_viewMode));
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

        private void DrawPreviewContextMenu()
        {
            if (!_contextMenuOpen)
                return;

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_contextMenuRect.Contains(current.mousePosition))
            {
                _contextMenuOpen = false;
                return;
            }

            if (_scene == null || !_scene.Select(_contextMenuPieceId))
            {
                _contextMenuOpen = false;
                return;
            }

            _draft = _scene.SelectedPiece;
            GUI.Box(_contextMenuRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(_contextMenuRect, 5f));
            GUILayout.Label("Preview piece", DecorationEditorTheme.SubHeader);
            if (GUILayout.Button(new GUIContent("Select", "Select this preview piece for editing."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _contextMenuOpen = false;
                _tool = SmartBuildTool.Move;
            }

            if (GUILayout.Button(new GUIContent("Duplicate", "Duplicate this preview piece one cell to the right."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DuplicateSelectedPiece();
                _contextMenuOpen = false;
            }

            if (GUILayout.Button(new GUIContent("Delete", "Delete this preview piece."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DeleteSelectedPiece();
                _contextMenuOpen = false;
            }

            if (GUILayout.Button(new GUIContent(SmartYawLabel(), "Rotate this preview piece around construct Y."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                YawSelectedPiece();
                _contextMenuOpen = false;
            }

            bool canFlip = _draft?.ShapeKind == SmartBuildShapeKind.DownSlope;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canFlip;
            if (GUILayout.Button(new GUIContent("Flip", "Reverse this down-slope direction."), canFlip ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                FlipSelectedPiece();
                _contextMenuOpen = false;
            }
            GUI.enabled = previous;
            GUILayout.EndArea();

            if (current != null && _contextMenuRect.Contains(current.mousePosition))
            {
                if (current.type == EventType.MouseDown ||
                    current.type == EventType.MouseUp ||
                    current.type == EventType.MouseDrag ||
                    current.type == EventType.ScrollWheel)
                {
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    current.Use();
                }
            }
        }

        private void DrawPieceActionToolbar(float rightControlsWidth)
        {
            if (!ShouldShowPieceActionButtons(rightControlsWidth))
                return;

            YawQuickButton();
            FlipQuickButton();
        }

        private static bool ShouldShowPieceActionButtons(float rightControlsWidth)
        {
            const float pieceActionsWidth = 2f * 44f;
            const float applyCancelCloseWidth = 3f * 62f;
            return rightControlsWidth >= EsuHudLayout.Scale(
                pieceActionsWidth +
                applyCancelCloseWidth);
        }

        private void ArmAddMode()
        {
            _tool = SmartBuildTool.Draw;
            InfoStore.Add("Adding: " + PendingShapeLabel() + ". Click the construct grid to place another preview piece.");
        }

        private string SmartYawLabel() =>
            "Yaw " + EsuTransformSnapSettings.Format(SmartRotateSnapDegrees);

        private void YawQuickButton()
        {
            if (CompactIconButton(
                    "rotate",
                    "Yaw",
                    DecorationEditorTheme.Button,
                    "Rotate the selected Smart Builder piece around vertical.",
                    _draft != null))
            {
                YawSelectedPiece();
            }
        }

        private void FlipQuickButton()
        {
            if (CompactIconButton(
                    "mirror",
                    "Flip",
                    DecorationEditorTheme.Button,
                    "Reverse the selected down-slope direction.",
                    _draft?.ShapeKind == SmartBuildShapeKind.DownSlope))
            {
                FlipSelectedPiece();
            }
        }

        private void YawSelectedPiece()
        {
            if (_draft == null)
                return;

            RecordSceneHistory();
            RotateYaw(_draft);
            RebuildPlan();
        }

        private static void RotateYaw(SmartBuildPiece piece)
        {
            piece?.RotateAroundAxis(
                DecorationEditAxis.Y,
                EsuTransformSnapSettings.SmartRotateQuarterTurns);
        }

        private void FlipSelectedPiece()
        {
            if (_draft?.ShapeKind != SmartBuildShapeKind.DownSlope)
                return;

            RecordSceneHistory();
            _draft.FlipForward();
            RebuildPlan();
        }

        private void DuplicateSelectedPiece()
        {
            if (_scene == null || _draft == null)
                return;

            RecordSceneHistory();
            SmartBuildPiece duplicate = _scene.DuplicateSelected(new Vector3i(1, 0, 0));
            if (duplicate == null)
                return;

            _draft = duplicate;
            _tool = SmartBuildTool.Move;
            RebuildPlan();
            InfoStore.Add("Duplicated Smart Builder piece #" + duplicate.Id.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private void DeleteSelectedPiece()
        {
            if (_scene == null || _draft == null)
                return;

            int id = _draft.Id;
            RecordSceneHistory();
            if (!_scene.DeleteSelected())
                return;

            _draft = _scene.SelectedPiece;
            if (_draft == null)
            {
                _scene = null;
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
            }
            else
            {
                RebuildPlan();
            }

            InfoStore.Add("Deleted Smart Builder piece #" + id.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private void ModeSwitchButton()
        {
            if (GUILayout.Button(
                    new GUIContent(
                        "Build",
                        DecorationEditorIconCatalog.Get("build"),
                        "Tab: switch to Decoration Edit Mode when Smart Builder is clean."),
                    DecorationEditorTheme.ToolButton(true),
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
                SwitchToDecorationEditRequested = true;
        }

        private void DrawLeftPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, _leftPanelRect.width - inset * 2f, _leftPanelRect.height - inset * 2f));
            DrawSmartPanelHeader("Smart Block Builder", "build", ref _showLeftPanel);
            DecorationEditorTheme.Separator();
            if (DrawSmartSectionHeader("Material", ref _showMaterialSection, "Show or hide the Smart Builder material selector."))
            {
                DrawMaterialSelector();
                if (!string.IsNullOrWhiteSpace(_sourceReason))
                    LabelRow("Blocks", _sourceReason);
            }
            LabelRow("Tool", ToolDisplayName());
            LabelRow("Handles", _editHandleMode.ToString());
            LabelRow("Plane", _drawPlane.ToString());
            LabelRow("Occupancy", _occupancyMode == SmartBuildOccupancyMode.SkipOccupied ? "Skip occupied" : "Block on overlap");
            LabelRow("Symmetry", DecoLimitLifter.EsuSymmetry.FormatSummary());
            DecorationEditorTheme.Separator();
            if (_draft == null)
            {
                GUILayout.Label("No preview", DecorationEditorTheme.SubHeader);
                GUILayout.Label("Pick a shape on the right, then click the focused construct grid.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                GUILayout.Label("Selected piece", DecorationEditorTheme.SubHeader);
                LabelRow("Scene", (_scene?.Count ?? 0).ToString("N0", CultureInfo.InvariantCulture) + " piece(s)");
                LabelRow("Shape", _draft.ShapeLabel());
                LabelRow("Origin", FormatCell(_draft.Origin));
                LabelRow("Size", _draft.FormatDimensions());
                LabelRow("Cells", PreviewCellCount()
                    .ToString("N0", CultureInfo.InvariantCulture));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        new GUIContent(SmartYawLabel(), "Rotate the selected piece around construct Y."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                {
                    YawSelectedPiece();
                }

                bool canFlip = _draft.ShapeKind == SmartBuildShapeKind.DownSlope;
                bool previous = GUI.enabled;
                GUI.enabled = previous && canFlip;
                if (GUILayout.Button(
                        new GUIContent("Flip", "Reverse the selected down-slope direction."),
                        canFlip ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                        GUILayout.Height(EsuHudLayout.Scale(26f))))
                {
                    FlipSelectedPiece();
                }
                GUI.enabled = previous;
                GUILayout.EndHorizontal();
                DrawPlanSummary();
            }

            GUILayout.FlexibleSpace();
            DrawLeftPanelApplyCancelButtons();
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Middle mouse can show the FTD cursor without closing Smart Builder. Camera/WASD remain live unless a Smart Builder handle is being dragged.", DecorationEditorTheme.MiniWrap);
            GUI.DragWindow();
            GUILayout.EndArea();
        }

        private void DrawLeftPanelApplyCancelButtons()
        {
            GUILayout.BeginHorizontal();
            bool canApply = !_planDirty && _plan != null && _plan.CanCommit;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canApply;
            if (GUILayout.Button(
                    new GUIContent("Apply", "Place the planned blocks."),
                    canApply ? DecorationEditorTheme.ToolButton(false) : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
            {
                ApplyPreview();
            }

            GUI.enabled = previous && _draft != null;
            if (GUILayout.Button(
                    new GUIContent("Cancel", "Clear the full Smart Builder preview scene."),
                    _draft != null ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
            {
                CancelPreview();
            }

            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawShapePanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _rightPanelRect.width, _rightPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, _rightPanelRect.width - inset * 2f, _rightPanelRect.height - inset * 2f));
            DrawSmartPanelHeader("Shapes", "build", ref _showRightPanel);
            DecorationEditorTheme.Separator();
            if (DrawSmartSectionHeader("Palette", ref _showShapePaletteSection, "Show or hide the shape palette."))
            {
                DrawShapeButton(SmartBuildShapeKind.Cuboid, "Block", "Place normal cuboids and beams.");
                DrawShapeButton(SmartBuildShapeKind.DownSlope, "Down slope", "Place 1m to 4m down-slope ramp segments.");
                GUILayout.Space(EsuHudLayout.Scale(4f));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Slope length", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(86f)));
                for (int length = 1; length <= 4; length++)
                    DrawSlopeSizeButton(length);
                GUILayout.EndHorizontal();
            }
            DecorationEditorTheme.Separator();
            if (DrawSmartSectionHeader("Selected", ref _showSelectedSection, "Show or hide selected-piece actions."))
                DrawSelectedPieceActions();
            DecorationEditorTheme.Separator();
            if (DrawSmartSectionHeader("Scene", ref _showSceneSection, "Show or hide the Smart Builder scene list."))
            {
                _shapePanelScroll = GUILayout.BeginScrollView(_shapePanelScroll, false, true);
                if (_scene == null || _scene.Count == 0)
                {
                    GUILayout.Label("No pieces yet", DecorationEditorTheme.MiniWrap);
                }
                else
                {
                    foreach (SmartBuildPiece piece in _scene.Pieces)
                        DrawPieceRow(piece);
                }

                GUILayout.EndScrollView();
            }
            GUI.DragWindow();
            GUILayout.EndArea();
        }

        private void DrawShapeButton(
            SmartBuildShapeKind shape,
            string label,
            string tooltip)
        {
            bool active = _selectedShape == shape;
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(shape == SmartBuildShapeKind.Cuboid ? "cube" : "scale"), tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Height(EsuHudLayout.Scale(34f))))
            {
                _selectedShape = shape;
                ArmAddMode();
            }
        }

        private void DrawSlopeSizeButton(int length)
        {
            bool active = _selectedSlopeLength == length;
            if (GUILayout.Button(
                    new GUIContent(length.ToString(CultureInfo.InvariantCulture) + "m", length + "m down-slope length."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(42f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                _selectedShape = SmartBuildShapeKind.DownSlope;
                _selectedSlopeLength = length;
                ArmAddMode();
            }
        }

        private void DrawPieceRow(SmartBuildPiece piece)
        {
            bool selected = _draft != null && _draft.Id == piece.Id;
            string text = piece.CompactSceneLabel();
            string tooltip = piece.ShapeLabel() + " | " + piece.FormatDimensions() + " | origin " + FormatCell(piece.Origin);
            if (GUILayout.Button(
                    new GUIContent(text, tooltip),
                    DecorationEditorTheme.ToolButton(selected),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                if (_scene.Select(piece.Id))
                {
                    _draft = _scene.SelectedPiece;
                    RebuildPlan();
                }
            }
        }

        private void DrawSelectedPieceActions()
        {
            if (_draft == null)
            {
                GUILayout.Label("No selected piece", DecorationEditorTheme.MiniWrap);
                return;
            }

            GUILayout.Label(
                "#" + _draft.Id.ToString(CultureInfo.InvariantCulture) + " | " +
                _draft.ShapeCode() + " | " + _draft.FormatDimensions(),
                DecorationEditorTheme.MiniWrap);

            float rowHeight = EsuHudLayout.Scale(28f);
            float gap = EsuHudLayout.Scale(2f);
            Rect grid = GUILayoutUtility.GetRect(1f, rowHeight * 3f + gap * 2f, GUILayout.ExpandWidth(true));
            float columnWidth = (grid.width - gap) * 0.5f;
            Rect select = new Rect(grid.x, grid.y, columnWidth, rowHeight);
            Rect duplicate = new Rect(select.xMax + gap, grid.y, columnWidth, rowHeight);
            Rect delete = new Rect(grid.x, select.yMax + gap, columnWidth, rowHeight);
            Rect yaw = new Rect(delete.xMax + gap, delete.y, columnWidth, rowHeight);
            Rect flip = new Rect(grid.x, delete.yMax + gap, grid.width, rowHeight);

            if (GUI.Button(select, new GUIContent("Select", "Keep this piece selected for editing."), DecorationEditorTheme.Button))
                _tool = SmartBuildTool.Move;
            if (GUI.Button(duplicate, new GUIContent("Duplicate", "Duplicate the selected piece one cell to the right."), DecorationEditorTheme.Button))
                DuplicateSelectedPiece();
            if (GUI.Button(delete, new GUIContent("Delete", "Remove the selected preview piece."), DecorationEditorTheme.Button))
                DeleteSelectedPiece();
            if (GUI.Button(yaw, new GUIContent("Yaw", "Rotate the selected piece around construct Y."), DecorationEditorTheme.Button))
                YawSelectedPiece();

            bool canFlip = _draft?.ShapeKind == SmartBuildShapeKind.DownSlope;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canFlip;
            if (GUI.Button(flip, new GUIContent("Flip", "Reverse the selected down-slope direction."), canFlip ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                FlipSelectedPiece();
            GUI.enabled = previous;
        }

        private void DrawSmartPanelHeader(string text, string iconKey, ref bool panelVisible)
        {
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(text, iconKey);
            if (GUILayout.Button(
                    new GUIContent("Hide", "Hide this Smart Builder panel."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                panelVisible = false;
            }
            GUILayout.EndHorizontal();
        }

        private bool DrawSmartSectionHeader(string text, ref bool sectionVisible, string tooltip)
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
            Rect rect = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(22f), GUILayout.ExpandWidth(true));
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

        private void DrawStatusStrip(int id)
        {
            GUI.Box(new Rect(0f, 0f, _statusRect.width, _statusRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_statusRect.width, _statusRect.height, 4f);
            float headerHeight = EsuHudLayout.Scale(24f);
            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            float statusHeight = EsuHudLayout.Scale(24f);
            float snapHeight = EsuHudLayout.Scale(26f);
            float controlsHeight = EsuHudLayout.Scale(26f);
            float footerHeight = EsuHudLayout.Scale(18f);
            float gap = EsuHudLayout.Scale(3f);
            float y = inner.y;

            DrawSmartBottomHeader(new Rect(inner.x, y, inner.width, headerHeight));
            y += headerHeight;
            DrawCyanLine(new Rect(inner.x, y, inner.width, separatorHeight));
            y += separatorHeight + gap;

            GUI.Label(new Rect(inner.x, y, inner.width, statusHeight), StatusSummary(), DecorationEditorTheme.Status);
            y += statusHeight + gap;

            DrawSmartBottomSnapControls(new Rect(inner.x, y, inner.width, snapHeight));
            y += snapHeight + gap;

            DrawSmartBottomControls(new Rect(inner.x, y, inner.width, controlsHeight));
            y += controlsHeight + gap;

            if (y < inner.yMax)
            {
                GUI.Label(
                    new Rect(inner.x, y, inner.width, Mathf.Min(footerHeight, inner.yMax - y)),
                    "Right-click cancels Add/drag; Cancel clears scene.",
                    DecorationEditorTheme.Mini);
            }
        }

        private void DrawSmartBottomHeader(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float titleWidth = Mathf.Min(EsuHudLayout.Scale(168f), rect.width * 0.42f);
            float stateWidth = Mathf.Min(EsuHudLayout.Scale(118f), rect.width * 0.32f);
            Rect title = new Rect(rect.x, rect.y, titleWidth, rect.height);
            Rect state = new Rect(rect.xMax - stateWidth, rect.y, stateWidth, rect.height);
            Rect mode = new Rect(
                title.xMax + gap,
                rect.y,
                Mathf.Max(1f, state.x - title.xMax - gap * 2f),
                rect.height);

            GUI.Label(title, "Smart Block Builder", DecorationEditorTheme.SubHeader);
            GUI.Label(mode, "Mode: Smart | Tab to Deco when clean", DecorationEditorTheme.Body);
            DrawStatusRightLabel(state);
        }

        private void DrawStatusRightLabel(Rect rect)
        {
            GUIStyle style = DecorationEditorTheme.Mini;
            string text = "Ready";
            if (_planDirty)
            {
                text = "Preview changed";
                style = DecorationEditorTheme.Warning;
            }
            else if (_plan != null && !_plan.CanCommit)
            {
                text = _plan.FailureReason ?? "Plan blocked";
                style = DecorationEditorTheme.Error;
            }
            else if (_plan != null && _plan.SkippedCells.Count > 0)
            {
                text = $"Skipped {_plan.SkippedCells.Count:N0} occupied";
                style = DecorationEditorTheme.Warning;
            }

            GUI.Label(rect, text, style);
        }

        private int SmartMoveStepCells => EsuTransformSnapSettings.SmartMoveStepCells;

        private float SmartRotateSnapDegrees => EsuTransformSnapSettings.SmartRotateSnapDegrees;

        private int SmartScaleStepCells => EsuTransformSnapSettings.SmartScaleStepCells;

        private void DrawSmartBottomSnapControls(Rect rect)
        {
            if (EsuTransformSnapHud.DrawSnapControls(
                    rect,
                    "Snap",
                    ref _snapMoveText,
                    ref _snapRotateText,
                    ref _snapScaleText,
                    "Move step in whole construct cells for Smart Builder previews.",
                    "Yaw step in 90 degree turns for valid Smart Builder block orientation.",
                    "Scale step in whole construct cells for Smart Builder previews."))
            {
                ApplySmartSnapText();
            }
        }

        private void SyncSnapTextFromSettings()
        {
            _snapMoveText = EsuTransformSnapSettings.Format(SmartMoveStepCells);
            _snapRotateText = EsuTransformSnapSettings.Format(SmartRotateSnapDegrees);
            _snapScaleText = EsuTransformSnapSettings.Format(SmartScaleStepCells);
        }

        private void ApplySmartSnapText()
        {
            if (!EsuTransformSnapSettings.TryParseSnapText(_snapMoveText, out float move) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapRotateText, out float rotate) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapScaleText, out float scale))
            {
                InfoStore.Add("Smart Builder snap settings must be finite numbers.");
                SyncSnapTextFromSettings();
                return;
            }

            EsuTransformSnapSettings.SetSmart(move, rotate, scale);
            SyncSnapTextFromSettings();
            InfoStore.Add(
                "Smart Builder snap set: move " + _snapMoveText +
                " cell(s), yaw " + _snapRotateText +
                " degrees, scale " + _snapScaleText + " cell(s).");
        }

        private void DrawSmartBottomControls(Rect rect)
        {
            float x = rect.x;
            float gap = EsuHudLayout.Scale(4f);
            float groupGap = EsuHudLayout.Scale(12f);
            float buttonHeight = Mathf.Min(rect.height, EsuHudLayout.Scale(24f));
            float y = rect.y + (rect.height - buttonHeight) * 0.5f;
            Rect label = new Rect(x, y, EsuHudLayout.Scale(58f), buttonHeight);
            GUI.Label(label, "Handles", DecorationEditorTheme.Mini);
            x = label.xMax + gap;

            x = HandleModeStripButton(new Rect(x, y, EsuHudLayout.Scale(60f), buttonHeight), SmartBuildEditHandleMode.Gizmo, "Gizmo", "Use RGB vector-axis handles.") + gap;
            x = HandleModeStripButton(new Rect(x, y, EsuHudLayout.Scale(60f), buttonHeight), SmartBuildEditHandleMode.Face, "Face", "Drag a face plane to resize the selected piece.") + gap;
            x = HandleModeStripButton(new Rect(x, y, EsuHudLayout.Scale(60f), buttonHeight), SmartBuildEditHandleMode.Edge, "Edge", "Drag a highlighted edge to resize two axes together.") + gap;
            x = HandleModeStripButton(new Rect(x, y, EsuHudLayout.Scale(70f), buttonHeight), SmartBuildEditHandleMode.Corner, "Corner", "Drag a highlighted corner to resize three axes together.");

            x += groupGap;
            label = new Rect(x, y, EsuHudLayout.Scale(58f), buttonHeight);
            GUI.Label(label, "Preview", DecorationEditorTheme.Mini);
            x = label.xMax + gap;

            x = PreviewModeStripButton(new Rect(x, y, EsuHudLayout.Scale(88f), buttonHeight), SmartBuildPreviewMode.Wireframe, "Wireframe", "Draw clean ESU wireframe previews.") + gap;
            x = PreviewModeStripButton(new Rect(x, y, EsuHudLayout.Scale(76f), buttonHeight), SmartBuildPreviewMode.Material, "Material", "Draw solid material-family ghost faces with the wireframe overlay.");

            if (_selectedShape == SmartBuildShapeKind.DownSlope ||
                _draft?.ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                x += groupGap;
                Rect support = new Rect(x, y, EsuHudLayout.Scale(116f), buttonHeight);
                if (support.xMax <= rect.xMax &&
                    GUI.Button(
                        support,
                        new GUIContent(
                            SupportFillLabel(),
                            "Toggle full support columns or one support block directly under each down-slope cell."),
                        DecorationEditorTheme.Button))
                {
                    ToggleSlopeSupportMode();
                }
            }
        }

        private float HandleModeStripButton(
            Rect rect,
            SmartBuildEditHandleMode mode,
            string label,
            string tooltip)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(_editHandleMode == mode)))
            {
                _editHandleMode = mode;
                _tool = SmartBuildTool.Scale;
                _viewModeMenuOpen = false;
                _contextMenuOpen = false;
                InfoStore.Add("Smart Builder handles: " + label + ".");
            }

            return rect.xMax;
        }

        private float PreviewModeStripButton(
            Rect rect,
            SmartBuildPreviewMode mode,
            string label,
            string tooltip)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(_previewMode == mode)))
            {
                _previewMode = mode;
                InfoStore.Add("Smart Builder preview: " + label + ".");
            }

            return rect.xMax;
        }

        private static void DrawCyanLine(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = DecorationEditorTheme.Cyan;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private string SupportFillLabel()
        {
            SmartBuildSlopeSupportMode mode = _draft?.ShapeKind == SmartBuildShapeKind.DownSlope
                ? _draft.SupportMode
                : _defaultSlopeSupportMode;
            return mode == SmartBuildSlopeSupportMode.Full ? "Support: Full" : "Support: Step";
        }

        private void ToggleSlopeSupportMode()
        {
            SmartBuildSlopeSupportMode current = _draft?.ShapeKind == SmartBuildShapeKind.DownSlope
                ? _draft.SupportMode
                : _defaultSlopeSupportMode;
            SmartBuildSlopeSupportMode next = current == SmartBuildSlopeSupportMode.Full
                ? SmartBuildSlopeSupportMode.Step
                : SmartBuildSlopeSupportMode.Full;
            RecordSceneHistory();
            _defaultSlopeSupportMode = next;
            if (_draft?.ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                _draft.SetSupportMode(next);
                RebuildPlan();
            }

            InfoStore.Add("Down-slope support " + (next == SmartBuildSlopeSupportMode.Full ? "full fill." : "step blocks."));
        }

        private string StatusSummary()
        {
            if (_draft == null)
                return _tool == SmartBuildTool.Draw
                    ? "Smart Builder | Adding: " + PendingShapeLabel()
                    : "Smart Builder | runtime preview: none";

            if (_planDirty)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Smart Builder | {0} | {1} | preview | {2:N0} cells",
                    ModeStatusLabel(),
                    _draft.FormatDimensions(),
                    PreviewCellCount());
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Smart Builder | {0} | {1} | {2:N0} placements | {3:N0} cells",
                ModeStatusLabel(),
                _draft.FormatDimensions(),
                _plan?.EstimatedBlockCount ?? 0,
                _plan?.CoveredCellCount ?? 0);
        }

        private string ModeStatusLabel()
        {
            if (_tool == SmartBuildTool.Draw)
                return "Adding: " + PendingShapeLabel();

            if (_draft != null)
                return "Editing: Piece #" + _draft.Id.ToString(CultureInfo.InvariantCulture);

            return ToolDisplayName();
        }

        private string PendingShapeLabel()
        {
            if (_selectedShape == SmartBuildShapeKind.DownSlope)
                return _selectedSlopeLength.ToString(CultureInfo.InvariantCulture) + "m down slope";

            return "Block";
        }

        private string ToolDisplayName() =>
            _tool == SmartBuildTool.Draw ? "Add" : _tool.ToString();

        private void ToolButton(SmartBuildTool tool, string icon, string label, string tooltip)
        {
            if (IconButton(icon, label, DecorationEditorTheme.ToolButton(_tool == tool), tooltip))
                SetActiveTool(tool);
        }

        private void SetActiveTool(SmartBuildTool tool)
        {
            _tool = tool;
            _viewModeMenuOpen = false;
        }

        private void PlaneButton()
        {
            if (IconButton("axis", "Plane " + PlaneShortName(), DecorationEditorTheme.Button, "Cycle the free-space draw plane."))
            {
                switch (_drawPlane)
                {
                    case SmartBuildDrawPlane.Camera:
                        _drawPlane = SmartBuildDrawPlane.XY;
                        break;
                    case SmartBuildDrawPlane.XY:
                        _drawPlane = SmartBuildDrawPlane.XZ;
                        break;
                    case SmartBuildDrawPlane.XZ:
                        _drawPlane = SmartBuildDrawPlane.YZ;
                        break;
                    default:
                        _drawPlane = SmartBuildDrawPlane.Camera;
                        break;
                }

                if (_draft != null)
                    _draft.DrawPlane = _drawPlane;
                _viewModeMenuOpen = false;
            }
        }

        private void OccupancyButton()
        {
            string label = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied
                ? "Skip"
                : "Block";
            if (IconButton("filter", label, DecorationEditorTheme.Button, "Toggle occupied-cell handling."))
            {
                _occupancyMode = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied
                    ? SmartBuildOccupancyMode.BlockOnOverlap
                    : SmartBuildOccupancyMode.SkipOccupied;
                RebuildPlan();
                _viewModeMenuOpen = false;
            }
        }

        private void ViewButton()
        {
            if (IconButton(
                    "visibility",
                    "View",
                    DecorationEditorTheme.ToolButton(_viewModeMenuOpen),
                    "Current view: " + ViewModeShortName() + "."))
            {
                _viewModeMenuOpen = !_viewModeMenuOpen;
            }
        }

        private void DrawMaterialSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (GUILayout.Button(
                    new GUIContent("<", "Previous Smart Builder material."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                CycleSelectedMaterial(-1);
            }

            GUILayout.Label(
                SmartBlockFamilyCatalog.MaterialDisplayName(_selectedMaterial),
                DecorationEditorTheme.BodyWrap);
            if (GUILayout.Button(
                    new GUIContent(">", "Next Smart Builder material."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                CycleSelectedMaterial(1);
            }

            GUILayout.EndHorizontal();
        }

        private void CycleSelectedMaterial(int direction)
        {
            IReadOnlyList<SmartBuildMaterial> materials = SmartBlockFamilyCatalog.BasicMaterials;
            if (materials == null || materials.Count == 0)
                return;

            int index = 0;
            for (int candidate = 0; candidate < materials.Count; candidate++)
            {
                if (materials[candidate] == _selectedMaterial)
                {
                    index = candidate;
                    break;
                }
            }

            int next = (index + direction) % materials.Count;
            if (next < 0)
                next += materials.Count;
            SetSelectedMaterial(materials[next]);
        }

        private void SetSelectedMaterial(SmartBuildMaterial material)
        {
            if (_selectedMaterial == material)
                return;

            _selectedMaterial = material;
            s_selectedMaterial = material;
            _source = null;
            _sourceReason = null;
            RefreshSelection();
        }

        private void SymmetryButton(DecorationEditAxis axis)
        {
            bool active = DecoLimitLifter.EsuSymmetry.IsActive(axis) ||
                          DecoLimitLifter.EsuSymmetry.IsPending(axis);
            string label = DecoLimitLifter.EsuSymmetry.AxisName(axis);
            string tooltip = DecoLimitLifter.EsuSymmetry.IsPending(axis)
                ? "Click to cancel placing this symmetry plane."
                : DecoLimitLifter.EsuSymmetry.IsActive(axis)
                    ? "Click to clear this symmetry plane."
                    : "Click, then click the construct grid to place this symmetry plane.";
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get("axis"), tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(36f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                DecoLimitLifter.EsuSymmetry.ToggleAxis(axis);
                RebuildPlan();
                _viewModeMenuOpen = false;
                InfoStore.Add(
                    active && !DecoLimitLifter.EsuSymmetry.IsPending(axis)
                        ? "Symmetry " + label + " updated."
                        : "Click the construct grid to place the " + label + " symmetry plane.");
            }
        }

        private bool IconButton(
            string icon,
            string label,
            GUIStyle style,
            string tooltip,
            bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUILayout.Button(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                enabled ? style : DecorationEditorTheme.DisabledButton,
                GUILayout.Width(EsuHudLayout.Scale(62f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));
            GUI.enabled = previous;
            return clicked && enabled;
        }

        private bool CompactIconButton(
            string icon,
            string label,
            GUIStyle style,
            string tooltip,
            bool enabled = true)
        {
            bool clicked = GUILayout.Button(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                enabled ? style : DecorationEditorTheme.DisabledButton,
                GUILayout.Width(EsuHudLayout.Scale(44f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));
            return clicked && enabled;
        }

        private bool AttentionIconButton(
            string icon,
            string label,
            GUIStyle style,
            string tooltip,
            bool enabled = true)
        {
            bool clicked = IconButton(icon, label, style, tooltip, enabled);
            EsuToolbarAttention.DrawLastButtonPulse(ApplyCancelAttentionActive);
            return clicked;
        }

        private bool ApplyCancelAttentionActive =>
            EsuToolbarAttention.IsActive(_applyCancelAttentionUntil);

        private void RefreshApplyCancelAttention() =>
            _applyCancelAttentionUntil = EsuToolbarAttention.RefreshUntil();

        private void ClearApplyCancelAttention() =>
            _applyCancelAttentionUntil = -1f;

        private void ApplyFocusView()
        {
            try
            {
                _buildOptions = ProfileManager.Instance.GetModule<MBuild_Ftd>();
                if (_buildOptions != null)
                {
                    _viewModes.Capture(_buildOptions);
                    ApplySmartViewMode();
                }
            }
            catch
            {
                _buildOptions = null;
            }
        }

        private void ApplySmartViewMode()
        {
            try
            {
                if (_buildOptions == null)
                    return;

                _viewModes.Apply(_viewMode);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Builder view mode apply failed",
                    exception,
                    LogOptions._AlertDevInGame);
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
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Builder focus restore failed",
                    failure,
                    LogOptions._AlertDevInGame);
            }
        }

        private void DrawPlanSummary()
        {
            if (_plan == null)
            {
                LabelRow("Plan", "--");
                return;
            }

            if (_planDirty)
            {
                LabelRow("Plan", "release drag to update");
                return;
            }

            if (_plan.CanCommit)
                LabelRow("Plan", $"{_plan.EstimatedBlockCount:N0} placements / {_plan.CoveredCellCount:N0} cells");
            else
                LabelRow("Blocked", _plan.FailureReason ?? "Cannot commit");

            if (_plan.SkippedCells.Count > 0)
                LabelRow("Skipped", _plan.SkippedCells.Count.ToString("N0", CultureInfo.InvariantCulture));
            if (_plan.Warnings.Count > 0)
                GUILayout.Label(_plan.Warnings[0], DecorationEditorTheme.Warning);
        }

        private int PreviewCellCount()
        {
            if (_scene == null || _scene.Count == 0)
                return 0;
            if (_planDirty || _previewCells.Count == 0)
                return _scene.Pieces.Sum(piece => piece.EnumeratePreviewCells().Count());
            return _previewCells.Count;
        }

        private static void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.BodyWrap);
            GUILayout.EndHorizontal();
        }

        private void DrawDraftOutline()
        {
            if (_scene?.Construct == null)
                return;

            bool invalid = !_planDirty && _plan != null && !_plan.CanCommit;
            var drawn = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                foreach (SmartBuildPiece piece in _scene.Pieces)
                    DrawPiecePreview(piece, variant, invalid, drawn);
            }
        }

        private void DrawPiecePreview(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            bool invalid,
            HashSet<string> drawn)
        {
            if (piece?.Construct == null)
                return;

            Vector3i[] occupiedCells = piece.EnumeratePreviewCells()
                .Select(variant.Mirror)
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.First())
                .ToArray();
            if (occupiedCells.Length == 0)
                return;

            string signature = string.Join(
                "|",
                occupiedCells
                    .Select(DecoLimitLifter.EsuSymmetry.CellKey)
                    .OrderBy(key => key)
                    .ToArray());
            if (!drawn.Add(piece.Id.ToString(CultureInfo.InvariantCulture) + "|" + signature))
                return;

            bool selectedOriginal = _draft != null &&
                                    _draft.Id == piece.Id &&
                                    !variant.Axes.Any();
            Color color = invalid
                ? new Color(1f, 0.2f, 0.15f, 1f)
                : selectedOriginal
                    ? new Color(1f, 1f, 0.2f, 1f)
                    : new Color(0.1f, 0.95f, 1f, 1f);
            float width = selectedOriginal ? 3.4f : 2.6f;

            if (piece.ShapeKind != SmartBuildShapeKind.DownSlope)
            {
                SmartBuildVolume volume = VolumeFromCells(piece.Construct, occupiedCells);
                if (volume != null)
                {
                    if (_previewMode == SmartBuildPreviewMode.Material)
                        DrawVolumeFaces(volume.GetWorldCorners(), SolidMaterialPreviewColor(selectedOriginal));
                    DrawWireEdges(volume.GetWorldCorners(), color, width);
                }
                return;
            }

            DrawSlopePieceHulls(piece, variant, color, width);

            Color supportColor = invalid
                ? new Color(1f, 0.2f, 0.15f, 0.38f)
                : new Color(0.58f, 0.8f, 0.9f, selectedOriginal ? 0.42f : 0.28f);
            DrawSupportHulls(
                piece.Construct,
                piece.EnumerateSupportCells().Select(variant.Mirror),
                supportColor,
                selectedOriginal ? 1.6f : 1.2f);
        }

        private void DrawSlopePieceHulls(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            Color color,
            float width)
        {
            if (piece?.Construct == null)
                return;

            int forwardSign = MirroredAxisSign(piece.ForwardAxis, piece.ForwardSign, variant);
            int rightSign = MirroredAxisSign(piece.RightAxis, piece.RightSign, variant);
            int dropSign = MirroredAxisSign(piece.DropAxis, piece.DropSign, variant);
            IReadOnlyList<Vector3i[]> lines = piece.EnumerateSlopeLines()
                .Select(line => line.Select(variant.Mirror).ToArray())
                .ToArray();
            for (int step = 0; step < piece.SlopeSteps; step++)
            {
                Vector3i[][] stepLines = lines
                    .Skip(step * piece.SlopeWidth)
                    .Take(piece.SlopeWidth)
                    .ToArray();
                DrawSlopeStepHull(
                    piece.Construct,
                    piece.ForwardAxis,
                    forwardSign,
                    piece.RightAxis,
                    rightSign,
                    piece.DropAxis,
                    dropSign,
                    stepLines,
                    color,
                    width);
            }
        }

        private void DrawSlopeStepHull(
            AllConstruct construct,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            SmartBuildAxis rightAxis,
            int rightSign,
            SmartBuildAxis dropAxis,
            int dropSign,
            IReadOnlyList<IReadOnlyList<Vector3i>> stepLines,
            Color color,
            float width)
        {
            if (construct == null ||
                !TryBuildSlopeStepHullLocal(
                    stepLines,
                    forwardAxis,
                    forwardSign,
                    rightAxis,
                    rightSign,
                    dropAxis,
                    dropSign,
                    out Vector3[] local))
            {
                return;
            }

            Vector3[] world =
            {
                construct.SafeLocalToGlobal(local[0]),
                construct.SafeLocalToGlobal(local[1]),
                construct.SafeLocalToGlobal(local[2]),
                construct.SafeLocalToGlobal(local[3]),
                construct.SafeLocalToGlobal(local[4]),
                construct.SafeLocalToGlobal(local[5]),
                construct.SafeLocalToGlobal(local[6]),
                construct.SafeLocalToGlobal(local[7])
            };

            Color fill = _previewMode == SmartBuildPreviewMode.Material
                ? SolidMaterialPreviewColor(selected: false)
                : new Color(color.r, color.g, color.b, 0.12f);
            DrawDoubleSidedSlopeQuad(world[0], world[1], world[2], world[3], fill);
            DecorationEditorOverlay.Quad(world[0], world[4], world[5], world[1], fill);
            DecorationEditorOverlay.Quad(world[3], world[2], world[6], world[7], fill);
            DecorationEditorOverlay.Quad(world[0], world[3], world[7], world[4], fill);
            DecorationEditorOverlay.Quad(world[1], world[5], world[6], world[2], fill);
            DecorationEditorOverlay.Quad(world[4], world[7], world[6], world[5], new Color(fill.r, fill.g, fill.b, fill.a * 0.55f));
            DrawWireEdge(world, 0, 1, color, width);
            DrawWireEdge(world, 1, 2, color, width);
            DrawWireEdge(world, 2, 3, color, width);
            DrawWireEdge(world, 3, 0, color, width);
            DrawWireEdge(world, 4, 5, color, width * 0.7f);
            DrawWireEdge(world, 5, 6, color, width * 0.7f);
            DrawWireEdge(world, 6, 7, color, width * 0.7f);
            DrawWireEdge(world, 7, 4, color, width * 0.7f);
            DrawWireEdge(world, 0, 4, color, width);
            DrawWireEdge(world, 1, 5, color, width);
            DrawWireEdge(world, 2, 6, color, width);
            DrawWireEdge(world, 3, 7, color, width);
        }

        private static void DrawDoubleSidedSlopeQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color fill)
        {
            DecorationEditorOverlay.Quad(a, b, c, d, fill);
            DecorationEditorOverlay.Quad(d, c, b, a, fill);
        }

        private void DrawDownSlopeFaceHighlight(
            DecorationEditAxis axis,
            int sign,
            Color faceColor,
            Color edgeColor,
            float width)
        {
            if (_draft?.Construct == null)
                return;

            foreach (SmartBuildHullFace face in EnumerateDownSlopeHandleFaces(_draft))
            {
                if (face.Axis != axis || face.Sign != sign)
                    continue;

                Vector3[] world = ToWorld(face.Local, _draft.Construct);
                DecorationEditorOverlay.Quad(world[0], world[1], world[2], world[3], faceColor);
                DecorationEditorOverlay.Line(world[0], world[1], edgeColor, width);
                DecorationEditorOverlay.Line(world[1], world[2], edgeColor, width);
                DecorationEditorOverlay.Line(world[2], world[3], edgeColor, width);
                DecorationEditorOverlay.Line(world[3], world[0], edgeColor, width);
            }
        }

        private bool TryPickDownSlopeFace(
            Vector3 localOrigin,
            Vector3 localDirection,
            out DecorationEditAxis axis,
            out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            float bestDistance = float.PositiveInfinity;
            foreach (SmartBuildHullFace face in EnumerateDownSlopeHandleFaces(_draft))
            {
                if (!TryRayQuad(localOrigin, localDirection, face.Local, out float distance))
                    continue;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                axis = face.Axis;
                sign = face.Sign;
            }

            return axis != DecorationEditAxis.None;
        }

        private bool TryGetDownSlopeFaceWorldCorners(
            DecorationEditAxis axis,
            int sign,
            out Vector3[] face)
        {
            face = null;
            if (_draft?.Construct == null)
                return false;

            foreach (SmartBuildHullFace candidate in EnumerateDownSlopeHandleFaces(_draft))
            {
                if (candidate.Axis != axis || candidate.Sign != sign)
                    continue;

                face = ToWorld(candidate.Local, _draft.Construct);
                return true;
            }

            return false;
        }

        private bool TryPickDownSlopeEdge(
            Camera camera,
            out DecorationEditAxis[] axes,
            out int[] signs)
        {
            axes = Array.Empty<DecorationEditAxis>();
            signs = Array.Empty<int>();
            if (_draft?.Construct == null || camera == null)
                return false;

            Vector2 mouse = MouseGuiPosition();
            float best = AxisPickThresholdPixels;
            SmartBuildHullEdge bestEdge = null;
            foreach (SmartBuildHullEdge edge in EnumerateDownSlopeHandleEdges(_draft))
            {
                Vector3 screenA = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(edge.Start));
                Vector3 screenB = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(edge.End));
                if (screenA.z <= camera.nearClipPlane || screenB.z <= camera.nearClipPlane)
                    continue;

                float distance = DistanceToSegment(mouse, ScreenToGui(screenA), ScreenToGui(screenB));
                if (distance >= best)
                    continue;

                best = distance;
                bestEdge = edge;
            }

            if (bestEdge == null)
                return false;

            axes = bestEdge.Axes;
            signs = bestEdge.Signs;
            return axes.Length == 2;
        }

        private bool TryGetDownSlopeEdgeWorldCorners(
            DecorationEditAxis[] axes,
            int[] signs,
            out Vector3 start,
            out Vector3 end)
        {
            start = Vector3.zero;
            end = Vector3.zero;
            if (_draft?.Construct == null)
                return false;

            foreach (SmartBuildHullEdge edge in EnumerateDownSlopeHandleEdges(_draft))
            {
                if (!MatchesAxesSigns(edge.Axes, edge.Signs, axes, signs))
                    continue;

                start = _draft.Construct.SafeLocalToGlobal(edge.Start);
                end = _draft.Construct.SafeLocalToGlobal(edge.End);
                return true;
            }

            return false;
        }

        private bool TryPickDownSlopeCorner(
            Camera camera,
            out DecorationEditAxis[] axes,
            out int[] signs)
        {
            axes = Array.Empty<DecorationEditAxis>();
            signs = Array.Empty<int>();
            if (_draft?.Construct == null || camera == null)
                return false;

            Vector2 mouse = MouseGuiPosition();
            float best = AxisPickThresholdPixels;
            SmartBuildHullCorner bestCorner = null;
            foreach (SmartBuildHullCorner corner in EnumerateDownSlopeHandleCorners(_draft))
            {
                Vector3 screen = camera.WorldToScreenPoint(_draft.Construct.SafeLocalToGlobal(corner.Local));
                if (screen.z <= camera.nearClipPlane)
                    continue;

                float distance = Vector2.Distance(mouse, ScreenToGui(screen));
                if (distance >= best)
                    continue;

                best = distance;
                bestCorner = corner;
            }

            if (bestCorner == null)
                return false;

            axes = bestCorner.Axes;
            signs = bestCorner.Signs;
            return axes.Length == 3;
        }

        private bool TryGetDownSlopeCornerWorld(
            DecorationEditAxis[] axes,
            int[] signs,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (_draft?.Construct == null)
                return false;

            foreach (SmartBuildHullCorner corner in EnumerateDownSlopeHandleCorners(_draft))
            {
                if (!MatchesAxesSigns(corner.Axes, corner.Signs, axes, signs))
                    continue;

                world = _draft.Construct.SafeLocalToGlobal(corner.Local);
                return true;
            }

            return false;
        }

        private static IReadOnlyList<SmartBuildHullFace> EnumerateDownSlopeHandleFaces(SmartBuildPiece piece)
        {
            if (piece == null || piece.ShapeKind != SmartBuildShapeKind.DownSlope)
                return Array.Empty<SmartBuildHullFace>();

            var faces = new List<SmartBuildHullFace>();
            IReadOnlyList<IReadOnlyList<Vector3i>> lines = piece.EnumerateSlopeLines().ToArray();
            DecorationEditAxis forwardAxis = SmartBuildDraft.ToDecorationAxis(piece.ForwardAxis);
            DecorationEditAxis rightAxis = SmartBuildDraft.ToDecorationAxis(piece.RightAxis);
            DecorationEditAxis dropAxis = SmartBuildDraft.ToDecorationAxis(piece.DropAxis);
            for (int step = 0; step < piece.SlopeSteps; step++)
            {
                IReadOnlyList<IReadOnlyList<Vector3i>> stepLines = lines
                    .Skip(step * piece.SlopeWidth)
                    .Take(piece.SlopeWidth)
                    .ToArray();
                if (!TryBuildSlopeStepLocalHull(piece, stepLines, out Vector3[] hull))
                    continue;

                faces.Add(new SmartBuildHullFace(dropAxis, -piece.DropSign, hull[0], hull[1], hull[2], hull[3]));
                faces.Add(new SmartBuildHullFace(dropAxis, piece.DropSign, hull[4], hull[7], hull[6], hull[5]));
                faces.Add(new SmartBuildHullFace(forwardAxis, -piece.ForwardSign, hull[0], hull[4], hull[5], hull[1]));
                faces.Add(new SmartBuildHullFace(forwardAxis, piece.ForwardSign, hull[3], hull[2], hull[6], hull[7]));
                faces.Add(new SmartBuildHullFace(rightAxis, -piece.RightSign, hull[0], hull[3], hull[7], hull[4]));
                faces.Add(new SmartBuildHullFace(rightAxis, piece.RightSign, hull[1], hull[5], hull[6], hull[2]));
            }

            return faces;
        }

        private static IEnumerable<SmartBuildHullEdge> EnumerateDownSlopeHandleEdges(SmartBuildPiece piece)
        {
            IReadOnlyList<SmartBuildHullFace> faces = EnumerateDownSlopeHandleFaces(piece);
            var edges = new Dictionary<string, SmartBuildHullEdgeBuilder>();
            foreach (SmartBuildHullFace face in faces)
            {
                AddHullEdge(edges, face, 0, 1);
                AddHullEdge(edges, face, 1, 2);
                AddHullEdge(edges, face, 2, 3);
                AddHullEdge(edges, face, 3, 0);
            }

            SmartBuildBounds bounds = piece.Bounds;
            foreach (SmartBuildHullEdgeBuilder builder in edges.Values)
            {
                builder.AddFixedAxesFromBounds(bounds);
                SmartBuildHullEdge edge = builder.ToEdge();
                if (edge != null)
                    yield return edge;
            }
        }

        private static IEnumerable<SmartBuildHullCorner> EnumerateDownSlopeHandleCorners(SmartBuildPiece piece)
        {
            IReadOnlyList<SmartBuildHullFace> faces = EnumerateDownSlopeHandleFaces(piece);
            var corners = new Dictionary<string, SmartBuildHullCornerBuilder>();
            foreach (SmartBuildHullFace face in faces)
            {
                for (int index = 0; index < face.Local.Length; index++)
                {
                    string key = VertexKey(face.Local[index]);
                    if (!corners.TryGetValue(key, out SmartBuildHullCornerBuilder builder))
                    {
                        builder = new SmartBuildHullCornerBuilder(face.Local[index]);
                        corners[key] = builder;
                    }

                    builder.Add(face.Axis, face.Sign);
                }
            }

            SmartBuildBounds bounds = piece.Bounds;
            foreach (SmartBuildHullCornerBuilder builder in corners.Values)
            {
                builder.AddFixedAxesFromBounds(bounds);
                SmartBuildHullCorner corner = builder.ToCorner();
                if (corner != null)
                    yield return corner;
            }
        }

        private static bool TryBuildSlopeStepLocalHull(
            SmartBuildPiece piece,
            IReadOnlyList<IReadOnlyList<Vector3i>> stepLines,
            out Vector3[] hull)
        {
            hull = null;
            if (piece == null)
                return false;

            return TryBuildSlopeStepHullLocal(
                stepLines,
                piece.ForwardAxis,
                piece.ForwardSign,
                piece.RightAxis,
                piece.RightSign,
                piece.DropAxis,
                piece.DropSign,
                out hull);
        }

        private static bool TryBuildSlopeStepHullLocal(
            IReadOnlyList<IReadOnlyList<Vector3i>> stepLines,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            SmartBuildAxis rightAxis,
            int rightSign,
            SmartBuildAxis dropAxis,
            int dropSign,
            out Vector3[] hull)
        {
            hull = null;
            if (stepLines == null || stepLines.Count == 0)
                return false;

            IReadOnlyList<Vector3i> firstLine = stepLines[0];
            IReadOnlyList<Vector3i> lastLine = stepLines[stepLines.Count - 1];
            if (firstLine.Count == 0 || lastLine.Count == 0)
                return false;

            Vector3 forward = AxisVector(forwardAxis, forwardSign);
            Vector3 right = AxisVector(rightAxis, rightSign);
            Vector3 drop = AxisVector(dropAxis, dropSign);
            Vector3 first = CellCenter(firstLine[0]);
            Vector3 lastAtHigh = CellCenter(lastLine[0]);
            Vector3 firstLow = CellCenter(firstLine[firstLine.Count - 1]);
            Vector3 lastLow = CellCenter(lastLine[lastLine.Count - 1]);
            Vector3 highEdge = first - forward * 0.5f;
            Vector3 highFarEdge = lastAtHigh - forward * 0.5f;
            Vector3 lowEdge = firstLow + forward * 0.5f;
            Vector3 lowFarEdge = lastLow + forward * 0.5f;
            Vector3 highTopOffset = -drop * 0.5f;
            Vector3 lowBottomOffset = drop * 0.5f;

            hull = new[]
            {
                highEdge - right * 0.5f + highTopOffset,
                highFarEdge + right * 0.5f + highTopOffset,
                lowFarEdge + right * 0.5f + lowBottomOffset,
                lowEdge - right * 0.5f + lowBottomOffset,
                highEdge - right * 0.5f + lowBottomOffset,
                highFarEdge + right * 0.5f + lowBottomOffset,
                lowFarEdge + right * 0.5f + lowBottomOffset,
                lowEdge - right * 0.5f + lowBottomOffset
            };
            return true;
        }

        private static void AddHullEdge(
            Dictionary<string, SmartBuildHullEdgeBuilder> edges,
            SmartBuildHullFace face,
            int startIndex,
            int endIndex)
        {
            Vector3 start = face.Local[startIndex];
            Vector3 end = face.Local[endIndex];
            string key = EdgeKey(start, end);
            if (!edges.TryGetValue(key, out SmartBuildHullEdgeBuilder builder))
            {
                builder = new SmartBuildHullEdgeBuilder(start, end);
                edges[key] = builder;
            }

            builder.Add(face.Axis, face.Sign);
        }

        private static bool TryRayQuad(
            Vector3 localOrigin,
            Vector3 localDirection,
            Vector3[] quad,
            out float distance)
        {
            distance = 0f;
            if (quad == null || quad.Length < 4)
                return false;

            Vector3 normal = Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]);
            float denominator = Vector3.Dot(normal, localDirection);
            if (Mathf.Abs(denominator) <= 0.0001f)
                return false;

            distance = Vector3.Dot(quad[0] - localOrigin, normal) / denominator;
            if (distance < 0f || float.IsNaN(distance) || float.IsInfinity(distance))
                return false;

            Vector3 hit = localOrigin + localDirection * distance;
            return PointInTriangle(hit, quad[0], quad[1], quad[2]) ||
                   PointInTriangle(hit, quad[0], quad[2], quad[3]);
        }

        private static bool PointInTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            const float epsilon = 0.001f;
            Vector3 v0 = c - a;
            Vector3 v1 = b - a;
            Vector3 v2 = point - a;
            float dot00 = Vector3.Dot(v0, v0);
            float dot01 = Vector3.Dot(v0, v1);
            float dot02 = Vector3.Dot(v0, v2);
            float dot11 = Vector3.Dot(v1, v1);
            float dot12 = Vector3.Dot(v1, v2);
            float denominator = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denominator) <= 0.000001f)
                return false;

            float inv = 1f / denominator;
            float u = (dot11 * dot02 - dot01 * dot12) * inv;
            float v = (dot00 * dot12 - dot01 * dot02) * inv;
            return u >= -epsilon && v >= -epsilon && u + v <= 1f + epsilon;
        }

        private static Vector3[] ToWorld(Vector3[] local, AllConstruct construct)
        {
            Vector3[] world = new Vector3[local.Length];
            for (int index = 0; index < local.Length; index++)
                world[index] = construct.SafeLocalToGlobal(local[index]);
            return world;
        }

        private static string EdgeKey(Vector3 a, Vector3 b)
        {
            string first = VertexKey(a);
            string second = VertexKey(b);
            return string.CompareOrdinal(first, second) <= 0
                ? first + ">" + second
                : second + ">" + first;
        }

        private static string VertexKey(Vector3 value) =>
            value.x.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
            value.y.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
            value.z.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool MatchesAxesSigns(
            DecorationEditAxis[] candidateAxes,
            int[] candidateSigns,
            DecorationEditAxis[] requestedAxes,
            int[] requestedSigns)
        {
            if (candidateAxes == null || candidateSigns == null || requestedAxes == null || requestedSigns == null)
                return false;

            for (int index = 0; index < requestedAxes.Length; index++)
            {
                DecorationEditAxis axis = requestedAxes[index];
                int sign = index < requestedSigns.Length ? requestedSigns[index] : 1;
                bool found = false;
                for (int candidate = 0; candidate < candidateAxes.Length; candidate++)
                {
                    int candidateSign = candidate < candidateSigns.Length ? candidateSigns[candidate] : 1;
                    if (candidateAxes[candidate] == axis && candidateSign == sign)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private void DrawSupportHulls(
            AllConstruct construct,
            IEnumerable<Vector3i> rawCells,
            Color color,
            float width)
        {
            if (construct == null)
                return;

            foreach (IGrouping<int, Vector3i> group in (rawCells ?? Array.Empty<Vector3i>())
                         .GroupBy(cell => cell.y))
            {
                Vector3i[] cells = group.ToArray();
                SmartBuildVolume volume = VolumeFromCells(construct, cells);
                if (volume != null)
                    DrawWireEdges(volume.GetWorldCorners(), color, width);
            }
        }

        private static void DrawCellWire(
            AllConstruct construct,
            Vector3i cell,
            Color color,
            float width)
        {
            if (construct == null)
                return;

            SmartBuildVolume volume = SmartBuildVolume.FromBounds(construct, cell, cell);
            if (volume != null)
                DrawWireEdges(volume.GetWorldCorners(), color, width);
        }

        private static Vector3 CellCenter(Vector3i cell) =>
            new Vector3(cell.x, cell.y, cell.z);

        private static Vector3 AxisVector(SmartBuildAxis axis, int sign)
        {
            switch (axis)
            {
                case SmartBuildAxis.X:
                    return Vector3.right * (sign >= 0 ? 1f : -1f);
                case SmartBuildAxis.Y:
                    return Vector3.up * (sign >= 0 ? 1f : -1f);
                default:
                    return Vector3.forward * (sign >= 0 ? 1f : -1f);
            }
        }

        private static Vector3 ConstructDirection(AllConstruct construct, Vector3 localDirection)
        {
            try
            {
                if (construct?.myTransform != null)
                    return construct.myTransform.TransformDirection(localDirection).normalized;
            }
            catch
            {
                // Fall through to local axes when FtD transform access changes.
            }

            return localDirection.sqrMagnitude > 0.0001f
                ? localDirection.normalized
                : Vector3.forward;
        }

        private static void DeriveRightAxis(
            SmartBuildAxis forwardAxis,
            int forwardSign,
            out SmartBuildAxis rightAxis,
            out int rightSign)
        {
            if (forwardAxis == SmartBuildAxis.X)
            {
                rightAxis = SmartBuildAxis.Z;
                rightSign = forwardSign >= 0 ? -1 : 1;
                return;
            }

            rightAxis = SmartBuildAxis.X;
            rightSign = forwardSign >= 0 ? 1 : -1;
        }

        private static int MirroredAxisSign(
            SmartBuildAxis axis,
            int sign,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            int mirrored = sign >= 0 ? 1 : -1;
            if (variant.Axes.Any(candidate => SmartBuildDraft.ToSmartAxis(candidate) == axis))
                mirrored *= -1;
            return mirrored;
        }

        private void DrawDraftFaceHighlight()
        {
            if (_draft?.Construct == null ||
                _tool != SmartBuildTool.Scale ||
                (_editHandleMode != SmartBuildEditHandleMode.Face &&
                 !(_dragging && _dragStartedFromFace)))
            {
                _hoverFaceAxis = DecorationEditAxis.None;
                return;
            }

            DecorationEditAxis axis = DecorationEditAxis.None;
            int sign = 1;
            bool active = _dragging &&
                          _dragTool == SmartBuildTool.Scale &&
                          _dragStartedFromFace &&
                          _dragAxis != DecorationEditAxis.None;
            if (active)
            {
                axis = _dragAxis;
                sign = _dragSign;
            }
            else if (!SmartBuildInputScope.MouseOverUi &&
                     TryPickFace(out axis, out sign))
            {
                _hoverFaceAxis = axis;
                _hoverFaceSign = sign;
            }
            else
            {
                _hoverFaceAxis = DecorationEditAxis.None;
                return;
            }

            Color axisColor = DecorationEditMath.AxisColor(axis);
            Color faceColor = new Color(axisColor.r, axisColor.g, axisColor.b, active ? 0.20f : 0.12f);
            Color edgeColor = new Color(axisColor.r, axisColor.g, axisColor.b, active ? 1f : 0.86f);
            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                DrawDownSlopeFaceHighlight(axis, sign, faceColor, edgeColor, active ? 4.2f : 2.6f);
                return;
            }

            if (!TryGetFaceWorldCorners(axis, sign, out Vector3[] face))
                return;

            DecorationEditorOverlay.Quad(face[0], face[1], face[2], face[3], faceColor);
            DecorationEditorOverlay.Line(face[0], face[1], edgeColor, active ? 4.2f : 2.6f);
            DecorationEditorOverlay.Line(face[1], face[2], edgeColor, active ? 4.2f : 2.6f);
            DecorationEditorOverlay.Line(face[2], face[3], edgeColor, active ? 4.2f : 2.6f);
            DecorationEditorOverlay.Line(face[3], face[0], edgeColor, active ? 4.2f : 2.6f);
        }

        private void DrawDraftHandles()
        {
            if (_draft?.Construct == null)
                return;

            if (_tool == SmartBuildTool.Scale)
            {
                if (_editHandleMode == SmartBuildEditHandleMode.Face)
                    return;
                if (_editHandleMode == SmartBuildEditHandleMode.Edge)
                {
                    DrawEdgeHandles();
                    return;
                }
                if (_editHandleMode == SmartBuildEditHandleMode.Corner)
                {
                    DrawCornerHandles();
                    return;
                }
            }

            Vector3 center = _draft.CenterLocal;
            DrawHandleAxis(center, DecorationEditAxis.X, 1);
            DrawHandleAxis(center, DecorationEditAxis.Y, 1);
            DrawHandleAxis(center, DecorationEditAxis.Z, 1);
            if (_tool == SmartBuildTool.Scale)
            {
                DrawHandleAxis(center, DecorationEditAxis.X, -1);
                DrawHandleAxis(center, DecorationEditAxis.Y, -1);
                DrawHandleAxis(center, DecorationEditAxis.Z, -1);
            }
        }

        private void DrawHandleAxis(Vector3 center, DecorationEditAxis axis, int sign)
        {
            Color color = DecorationEditMath.AxisColor(axis);
            Vector3 axisVector = DecorationEditMath.AxisVector(axis) * sign;
            float width = _dragging && _dragAxis == axis && _dragSign == sign ? 4.4f : 2.7f;
            Vector3 start = _draft.Construct.SafeLocalToGlobal(center);
            Vector3 end = _draft.Construct.SafeLocalToGlobal(center + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
        }

        private void DrawRotateGizmo()
        {
            if (_draft?.Construct == null)
                return;

            DrawRotationRing(DecorationEditAxis.X);
            DrawRotationRing(DecorationEditAxis.Y);
            DrawRotationRing(DecorationEditAxis.Z);
        }

        private void DrawRotationRing(DecorationEditAxis axis)
        {
            if (_draft?.Construct == null)
                return;

            Vector3 centerLocal = _draft.RotationPivotLocal;
            Vector3 centerWorld = _draft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 axisWorld = _draft.Construct.SafeLocalToGlobal(centerLocal + DecorationEditMath.AxisVector(axis)) - centerWorld;
            if (axisWorld.sqrMagnitude <= 0.0001f)
                axisWorld = DecorationEditMath.AxisVector(axis);

            Color color = DecorationEditMath.AxisColor(axis);
            float width = _rotating && _rotateDragAxis == axis ? 4.4f : 2.7f;
            DecorationEditorOverlay.Circle(centerWorld, RotateGizmoRadius(), color, axisWorld.normalized, width, 56);
        }

        private bool TryPickRotationRing(out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_draft?.Construct == null)
                return false;

            Vector3 center = _draft.RotationPivotLocal;
            if (!TryProject(_draft.Construct, center, out Vector2 origin))
                return false;

            Vector2 mouse = MouseGuiPosition();
            float best = AxisPickThresholdPixels * 1.35f;
            DecorationEditAxis picked = DecorationEditAxis.None;
            TryRing(DecorationEditAxis.X);
            TryRing(DecorationEditAxis.Y);
            TryRing(DecorationEditAxis.Z);
            axis = picked;
            return axis != DecorationEditAxis.None;

            void TryRing(DecorationEditAxis candidate)
            {
                BuildAxisBasis(DecorationEditMath.AxisVector(candidate), out Vector3 tangentA, out Vector3 tangentB);
                const int steps = 48;
                bool havePrevious = false;
                Vector2 previous = origin;
                float radius = RotateGizmoRadius();
                for (int step = 0; step <= steps; step++)
                {
                    float angle = step * Mathf.PI * 2f / steps;
                    Vector3 local = center +
                                    (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                                    radius;
                    if (!TryProject(_draft.Construct, local, out Vector2 projected))
                    {
                        havePrevious = false;
                        continue;
                    }

                    if (havePrevious)
                    {
                        float distance = DistanceToSegment(mouse, previous, projected);
                        if (distance < best)
                        {
                            best = distance;
                            picked = candidate;
                        }
                    }

                    previous = projected;
                    havePrevious = true;
                }
            }
        }

        private void BeginRotateDrag(DecorationEditAxis axis)
        {
            if (_draft?.Construct == null || axis == DecorationEditAxis.None)
                return;

            _rotating = true;
            _rotateDragAxis = axis;
            _rotateDragMouseStart = MouseGuiPosition();
            _rotatePieceStart = _draft.Clone();
            _rotateSceneStart = CaptureSceneSnapshot();
            if (!TryProject(_draft.Construct, _draft.RotationPivotLocal, out _rotateDragCenterScreen))
                _rotateDragCenterScreen = _rotateDragMouseStart;
            _rotateDragStartVector = _rotateDragMouseStart - _rotateDragCenterScreen;
            _rotateDragLastVector = _rotateDragStartVector;
            _rotateDragAccumulatedDegrees = 0f;
        }

        private void UpdateRotateDrag()
        {
            if (!_rotating || _draft == null || _rotatePieceStart == null)
                return;

            Vector2 current = MouseGuiPosition() - _rotateDragCenterScreen;
            if (current.sqrMagnitude < 16f)
                return;

            if (_rotateDragLastVector.sqrMagnitude < 16f)
            {
                _rotateDragLastVector = current;
                return;
            }

            float previousAngle = Mathf.Atan2(_rotateDragLastVector.y, _rotateDragLastVector.x) * Mathf.Rad2Deg;
            float currentAngle = Mathf.Atan2(current.y, current.x) * Mathf.Rad2Deg;
            _rotateDragAccumulatedDegrees += Mathf.DeltaAngle(previousAngle, currentAngle);
            _rotateDragLastVector = current;
            int quarterTurns = Mathf.RoundToInt(_rotateDragAccumulatedDegrees / RotateSnapDegrees);
            _draft.CopyFrom(_rotatePieceStart);
            _draft.RotateAroundAxis(_rotateDragAxis, quarterTurns);
            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private void EndRotateDrag(bool resetDraft)
        {
            if (!_rotating)
                return;

            if (resetDraft && _draft != null && _rotatePieceStart != null)
                _draft.CopyFrom(_rotatePieceStart);

            if (_draft != null)
            {
                if (!resetDraft)
                    PushSceneSnapshot(_rotateSceneStart);
                RebuildPlan();
            }

            ClearRotateDragState();
        }

        private void ClearRotateDragState()
        {
            _rotating = false;
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragMouseStart = Vector2.zero;
            _rotateDragCenterScreen = Vector2.zero;
            _rotateDragStartVector = Vector2.zero;
            _rotateDragLastVector = Vector2.zero;
            _rotateDragAccumulatedDegrees = 0f;
            _rotatePieceStart = null;
            _rotateSceneStart = null;
        }

        private float RotateGizmoRadius()
        {
            if (_draft == null)
                return 1f;

            Vector3i size = _draft.Size;
            return Mathf.Max(1.1f, Math.Max(size.x, Math.Max(size.y, size.z)) * 0.68f);
        }

        private static void BuildAxisBasis(Vector3 normal, out Vector3 tangentA, out Vector3 tangentB)
        {
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            tangentA = Vector3.Cross(
                normal,
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f
                    ? Vector3.forward
                    : Vector3.up);
            if (tangentA.sqrMagnitude < 0.0001f)
                tangentA = Vector3.right;
            tangentA.Normalize();
            tangentB = Vector3.Cross(normal, tangentA).normalized;
        }

        private static bool TryProject(AllConstruct construct, Vector3 local, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Vector3 projected = camera.WorldToScreenPoint(construct.SafeLocalToGlobal(local));
            if (projected.z <= camera.nearClipPlane)
                return false;

            screenPoint = ScreenToGui(projected);
            return projected.x >= -50f &&
                   projected.x <= Screen.width + 50f &&
                   projected.y >= -50f &&
                   projected.y <= Screen.height + 50f;
        }

        private void DrawCornerHandles()
        {
            bool active = _dragging &&
                          _dragTool == SmartBuildTool.Scale &&
                          _editHandleMode == SmartBuildEditHandleMode.Corner &&
                          _dragAxes.Length == 3;
            DecorationEditAxis[] axes = _dragAxes;
            int[] signs = _dragSigns;
            if (!active)
            {
                if (SmartBuildInputScope.MouseOverUi ||
                    !TryPickCorner(out axes, out signs))
                {
                    _hoverCornerAxes = Array.Empty<DecorationEditAxis>();
                    _hoverCornerSigns = Array.Empty<int>();
                    return;
                }

                _hoverCornerAxes = axes;
                _hoverCornerSigns = signs;
            }

            if (!TryGetCornerWorld(axes, signs, out Vector3 world))
                return;

            Color color = new Color(0.1f, 1f, 0.18f, 0.98f);
            DecorationEditorOverlay.Cross(world, active ? 0.24f : 0.2f, color, active ? 4.8f : 3.4f);
        }

        private void DrawEdgeHandles()
        {
            bool active = _dragging &&
                          _dragTool == SmartBuildTool.Scale &&
                          _editHandleMode == SmartBuildEditHandleMode.Edge &&
                          _dragAxes.Length == 2;
            DecorationEditAxis[] axes = _dragAxes;
            int[] signs = _dragSigns;
            if (!active)
            {
                if (SmartBuildInputScope.MouseOverUi ||
                    !TryPickEdge(out axes, out signs))
                {
                    _hoverEdgeAxes = Array.Empty<DecorationEditAxis>();
                    _hoverEdgeSigns = Array.Empty<int>();
                    return;
                }

                _hoverEdgeAxes = axes;
                _hoverEdgeSigns = signs;
            }

            if (!TryGetEdgeWorldCorners(axes, signs, out Vector3 start, out Vector3 end))
                return;

            Color color = new Color(0.1f, 1f, 0.18f, 0.98f);
            DecorationEditorOverlay.Line(start, end, color, active ? 5f : 3.6f);
        }

        private bool TryGetEdgeWorldCorners(
            DecorationEditAxis[] axes,
            int[] signs,
            out Vector3 start,
            out Vector3 end)
        {
            start = Vector3.zero;
            end = Vector3.zero;
            if (_draft?.Construct == null ||
                axes == null ||
                signs == null ||
                axes.Length != 2)
            {
                return false;
            }

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryGetDownSlopeEdgeWorldCorners(axes, signs, out start, out end);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            Vector3 min = corners[0];
            Vector3 max = corners[6];
            var matches = new List<Vector3>(2);
            foreach (Vector3 corner in corners)
            {
                if (MatchesFixedAxes(corner, min, max, axes, signs))
                    matches.Add(corner);
            }

            if (matches.Count < 2)
                return false;

            start = _draft.Construct.SafeLocalToGlobal(matches[0]);
            end = _draft.Construct.SafeLocalToGlobal(matches[1]);
            return true;
        }

        private bool TryGetCornerWorld(
            DecorationEditAxis[] axes,
            int[] signs,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (_draft?.Construct == null ||
                axes == null ||
                signs == null ||
                axes.Length != 3)
            {
                return false;
            }

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryGetDownSlopeCornerWorld(axes, signs, out world);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            Vector3 min = corners[0];
            Vector3 max = corners[6];
            foreach (Vector3 corner in corners)
            {
                if (!MatchesFixedAxes(corner, min, max, axes, signs))
                    continue;

                world = _draft.Construct.SafeLocalToGlobal(corner);
                return true;
            }

            return false;
        }

        private static bool MatchesFixedAxes(
            Vector3 corner,
            Vector3 min,
            Vector3 max,
            DecorationEditAxis[] axes,
            int[] signs)
        {
            for (int index = 0; index < axes.Length; index++)
            {
                DecorationEditAxis axis = axes[index];
                int sign = index < signs.Length ? signs[index] : 1;
                float expected = sign >= 0
                    ? AxisComponent(max, axis)
                    : AxisComponent(min, axis);
                if (Mathf.Abs(AxisComponent(corner, axis) - expected) > 0.001f)
                    return false;
            }

            return true;
        }

        private void DrawPlacementGhost()
        {
            if (!TryPlacementCandidate(out SmartBuildPlacementCandidate candidate) ||
                candidate.Construct == null)
                return;

            Color color = candidate.Valid
                ? new Color(0.1f, 0.95f, 1f, 0.92f)
                : new Color(1f, 0.2f, 0.15f, 0.92f);
            if (_selectedShape != SmartBuildShapeKind.DownSlope)
            {
                Vector3i size = SmartBuildDraft.ClampSize(candidate.CuboidSize);
                Vector3i max = candidate.Cell + new Vector3i(size.x - 1, size.y - 1, size.z - 1);
                SmartBuildVolume volume = SmartBuildVolume.FromBounds(candidate.Construct, candidate.Cell, max);
                if (volume != null && _previewMode == SmartBuildPreviewMode.Material)
                    DrawVolumeFaces(volume.GetWorldCorners(), SolidMaterialPreviewColor(selected: false));
                if (volume != null)
                    DrawWireEdges(volume.GetWorldCorners(), color, 2.4f);
                return;
            }

            Vector3i origin = candidate.Cell;
            DeriveRightAxis(candidate.ForwardAxis, candidate.ForwardSign, out SmartBuildAxis rightAxis, out int rightSign);
            if (candidate.RightStart.HasValue)
                origin = SmartBuildAxisHelper.Set(origin, rightAxis, candidate.RightStart.Value);

            Vector3i forward = SmartBuildAxisHelper.ToVector3i(candidate.ForwardAxis, candidate.ForwardSign);
            Vector3i right = SmartBuildAxisHelper.ToVector3i(rightAxis, rightSign);
            var lines = new List<IReadOnlyList<Vector3i>>();
            for (int lane = 0; lane < candidate.Width; lane++)
            {
                Vector3i start = origin + Multiply(right, lane);
                var line = new List<Vector3i>(_selectedSlopeLength);
                for (int index = 0; index < _selectedSlopeLength; index++)
                    line.Add(start + Multiply(forward, index));
                lines.Add(line);
            }

            DrawSlopeStepHull(
                candidate.Construct,
                candidate.ForwardAxis,
                candidate.ForwardSign,
                rightAxis,
                rightSign,
                SmartBuildAxis.Y,
                -1,
                lines,
                color,
                2.4f);
        }

        private void DrawSymmetryOverlay()
        {
            AllConstruct construct = _draft?.Construct ??
                                     DecoLimitLifter.EsuSymmetry.Construct ??
                                     FocusedConstruct();
            Vector3 around = _draft != null
                ? _draft.CenterLocal
                : FocusedConstructLocalCenter(construct);

            foreach (KeyValuePair<DecorationEditAxis, int> plane in DecoLimitLifter.EsuSymmetry.ActivePlanes)
            {
                if (ReferenceEquals(DecoLimitLifter.EsuSymmetry.Construct, construct))
                    DecoLimitLifter.EsuSymmetry.DrawPlane(
                        construct,
                        plane.Key,
                        plane.Value,
                        around,
                        pending: false);
            }

            if (DecoLimitLifter.EsuSymmetry.PendingAxis == DecorationEditAxis.None)
                return;

            if (TrySymmetryPlaneCandidate(out AllConstruct candidateConstruct, out Vector3i cell))
            {
                DecoLimitLifter.EsuSymmetry.DrawPlane(
                    candidateConstruct,
                    DecoLimitLifter.EsuSymmetry.PendingAxis,
                    DecoLimitLifter.EsuSymmetry.AxisComponent(
                        cell,
                        DecoLimitLifter.EsuSymmetry.PendingAxis),
                    new Vector3(cell.x, cell.y, cell.z),
                    pending: true);
            }
        }

        private static void DrawWireEdges(Vector3[] corners, Color color, float width)
        {
            DrawWireEdge(corners, 0, 1, color, width);
            DrawWireEdge(corners, 1, 2, color, width);
            DrawWireEdge(corners, 2, 3, color, width);
            DrawWireEdge(corners, 3, 0, color, width);
            DrawWireEdge(corners, 4, 5, color, width);
            DrawWireEdge(corners, 5, 6, color, width);
            DrawWireEdge(corners, 6, 7, color, width);
            DrawWireEdge(corners, 7, 4, color, width);
            DrawWireEdge(corners, 0, 4, color, width);
            DrawWireEdge(corners, 1, 5, color, width);
            DrawWireEdge(corners, 2, 6, color, width);
            DrawWireEdge(corners, 3, 7, color, width);
        }

        private static void DrawWireEdge(Vector3[] corners, int from, int to, Color color, float width) =>
            DecorationEditorOverlay.Line(corners[from], corners[to], color, width);

        private static void DrawVolumeFaces(Vector3[] corners, Color color)
        {
            if (corners == null || corners.Length < 8)
                return;

            DecorationEditorOverlay.Quad(corners[0], corners[1], corners[2], corners[3], color);
            DecorationEditorOverlay.Quad(corners[4], corners[7], corners[6], corners[5], color);
            DecorationEditorOverlay.Quad(corners[0], corners[4], corners[5], corners[1], color);
            DecorationEditorOverlay.Quad(corners[1], corners[5], corners[6], corners[2], color);
            DecorationEditorOverlay.Quad(corners[2], corners[6], corners[7], corners[3], color);
            DecorationEditorOverlay.Quad(corners[3], corners[7], corners[4], corners[0], color);
        }

        private Color SolidMaterialPreviewColor(bool selected)
        {
            Color color;
            switch (_selectedMaterial)
            {
                case SmartBuildMaterial.Stone:
                    color = new Color(0.58f, 0.62f, 0.62f, 1f);
                    break;
                case SmartBuildMaterial.Metal:
                    color = new Color(0.42f, 0.48f, 0.56f, 1f);
                    break;
                case SmartBuildMaterial.Alloy:
                    color = new Color(0.68f, 0.78f, 0.8f, 1f);
                    break;
                case SmartBuildMaterial.Glass:
                    color = new Color(0.58f, 0.9f, 1f, 1f);
                    break;
                case SmartBuildMaterial.Lead:
                    color = new Color(0.32f, 0.34f, 0.42f, 1f);
                    break;
                case SmartBuildMaterial.HeavyArmour:
                    color = new Color(0.18f, 0.2f, 0.24f, 1f);
                    break;
                case SmartBuildMaterial.Rubber:
                    color = new Color(0.04f, 0.05f, 0.05f, 1f);
                    break;
                default:
                    color = new Color(0.62f, 0.42f, 0.24f, 1f);
                    break;
            }

            color.a = selected ? 0.94f : 0.86f;
            return color;
        }

        private bool TryGetFaceWorldCorners(
            DecorationEditAxis axis,
            int sign,
            out Vector3[] face)
        {
            face = null;
            if (_draft?.Construct == null)
                return false;

            if (_draft.ShapeKind == SmartBuildShapeKind.DownSlope)
                return TryGetDownSlopeFaceWorldCorners(axis, sign, out face);

            Vector3[] corners = _draft.ToVolume().GetLocalCorners();
            int[] indices;
            switch (axis)
            {
                case DecorationEditAxis.X:
                    indices = sign >= 0
                        ? new[] { 1, 5, 6, 2 }
                        : new[] { 3, 7, 4, 0 };
                    break;
                case DecorationEditAxis.Y:
                    indices = sign >= 0
                        ? new[] { 4, 7, 6, 5 }
                        : new[] { 0, 1, 2, 3 };
                    break;
                case DecorationEditAxis.Z:
                    indices = sign >= 0
                        ? new[] { 2, 6, 7, 3 }
                        : new[] { 0, 4, 5, 1 };
                    break;
                default:
                    return false;
            }

            face = new Vector3[4];
            try
            {
                for (int index = 0; index < face.Length; index++)
                    face[index] = _draft.Construct.SafeLocalToGlobal(corners[indices[index]]);
                return true;
            }
            catch
            {
                face = null;
                return false;
            }
        }

        private static float AxisComponent(Vector3 value, DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return value.x;
                case DecorationEditAxis.Y:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private Vector3 WorldNormalToLocal(DecorationPointerHit hit)
        {
            try
            {
                if (hit.Construct?.myTransform != null)
                    return hit.Construct.myTransform.InverseTransformDirection(hit.WorldNormal).normalized;
            }
            catch
            {
                // Fall back to world axes below.
            }

            return hit.WorldNormal.sqrMagnitude > 0.0001f
                ? hit.WorldNormal.normalized
                : Vector3.forward;
        }

        private static Vector3i RoundToCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

        private static Vector3i Multiply(Vector3i value, int amount) =>
            new Vector3i(value.x * amount, value.y * amount, value.z * amount);

        private static Vector3 FocusedConstructLocalCenter(AllConstruct construct)
        {
            if (construct == null)
                return Vector3.zero;

            try
            {
                return construct.SafeGlobalToLocal(construct.SafePosition);
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private static SmartBuildVolume VolumeFromCells(
            AllConstruct construct,
            IReadOnlyList<Vector3i> cells)
        {
            if (construct == null || cells == null || cells.Count == 0)
                return null;

            Vector3i min = cells[0];
            Vector3i max = cells[0];
            for (int index = 1; index < cells.Count; index++)
            {
                Vector3i cell = cells[index];
                min.x = Math.Min(min.x, cell.x);
                min.y = Math.Min(min.y, cell.y);
                min.z = Math.Min(min.z, cell.z);
                max.x = Math.Max(max.x, cell.x);
                max.y = Math.Max(max.y, cell.y);
                max.z = Math.Max(max.z, cell.z);
            }

            return new SmartBuildVolume(
                construct,
                min,
                SmartBuildAxis.X,
                SmartBuildAxis.Y,
                SmartBuildAxis.Z,
                max.x - min.x + 1,
                max.y - min.y + 1,
                max.z - min.z + 1);
        }

        private static string FormatCell(Vector3i cell) =>
            $"{cell.x}, {cell.y}, {cell.z}";

        private string PlaneShortName()
        {
            switch (_drawPlane)
            {
                case SmartBuildDrawPlane.XY:
                    return "XY";
                case SmartBuildDrawPlane.XZ:
                    return "XZ";
                case SmartBuildDrawPlane.YZ:
                    return "YZ";
                default:
                    return "Cam";
            }
        }

        private static Vector2 MouseGuiPosition() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        private static bool IsShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private static Vector2 ScreenToGui(Vector3 screen) =>
            new Vector2(screen.x, Screen.height - screen.y);

        private static bool ContainsMouse(Rect rect) =>
            rect.Contains(MouseGuiPosition());

        private void RefreshMouseOverUiFromCurrentPointer()
        {
            Vector2 mouse = MouseGuiPosition();
            bool overUi = _toolbarRect.Contains(mouse) ||
                          EsuHudNotifications.ContainsMouse(mouse) ||
                          (_viewModeMenuOpen && ViewModeMenuRect(_toolbarRect).Contains(mouse)) ||
                          (_showLeftPanel && _leftPanelRect.Contains(mouse)) ||
                          (_showRightPanel && _rightPanelRect.Contains(mouse)) ||
                          _statusRect.Contains(mouse) ||
                          (_contextMenuOpen && _contextMenuRect.Contains(mouse));
            SmartBuildInputScope.SetMouseOverUi(overUi);
            if (overUi &&
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                SmartBuildInputScope.ClaimMouseWheelInputForFrames();
            }
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

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private static bool TryWorldToLocal(
            AllConstruct construct,
            Vector3 world,
            out Vector3 local)
        {
            local = Vector3.zero;
            if (construct == null)
                return false;

            try
            {
                local = construct.SafeGlobalToLocal(world);
                return true;
            }
            catch
            {
                try
                {
                    if (construct.myTransform == null)
                        return false;
                    local = construct.myTransform.InverseTransformPoint(world);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class SmartBuildHullEdge
        {
            internal SmartBuildHullEdge(
                Vector3 start,
                Vector3 end,
                DecorationEditAxis[] axes,
                int[] signs)
            {
                Start = start;
                End = end;
                Axes = axes ?? Array.Empty<DecorationEditAxis>();
                Signs = signs ?? Array.Empty<int>();
            }

            internal Vector3 Start { get; }

            internal Vector3 End { get; }

            internal DecorationEditAxis[] Axes { get; }

            internal int[] Signs { get; }
        }

        private sealed class SmartBuildHullCorner
        {
            internal SmartBuildHullCorner(
                Vector3 local,
                DecorationEditAxis[] axes,
                int[] signs)
            {
                Local = local;
                Axes = axes ?? Array.Empty<DecorationEditAxis>();
                Signs = signs ?? Array.Empty<int>();
            }

            internal Vector3 Local { get; }

            internal DecorationEditAxis[] Axes { get; }

            internal int[] Signs { get; }
        }

        private sealed class SmartBuildHullEdgeBuilder
        {
            private readonly List<DecorationEditAxis> _axes = new List<DecorationEditAxis>(3);
            private readonly List<int> _signs = new List<int>(3);

            internal SmartBuildHullEdgeBuilder(Vector3 start, Vector3 end)
            {
                Start = start;
                End = end;
            }

            internal Vector3 Start { get; }

            internal Vector3 End { get; }

            internal void Add(DecorationEditAxis axis, int sign)
            {
                AddAxisSign(_axes, _signs, axis, sign);
            }

            internal void AddFixedAxesFromBounds(SmartBuildBounds bounds)
            {
                AddFixedAxisFromEdge(DecorationEditAxis.X, Start.x, End.x, bounds.Min.x - 0.5f, bounds.Max.x + 0.5f);
                AddFixedAxisFromEdge(DecorationEditAxis.Y, Start.y, End.y, bounds.Min.y - 0.5f, bounds.Max.y + 0.5f);
                AddFixedAxisFromEdge(DecorationEditAxis.Z, Start.z, End.z, bounds.Min.z - 0.5f, bounds.Max.z + 0.5f);
            }

            internal SmartBuildHullEdge ToEdge()
            {
                if (_axes.Count < 2)
                    return null;

                return new SmartBuildHullEdge(
                    Start,
                    End,
                    _axes.Take(2).ToArray(),
                    _signs.Take(2).ToArray());
            }

            private void AddFixedAxisFromEdge(
                DecorationEditAxis axis,
                float first,
                float second,
                float min,
                float max)
            {
                if (Mathf.Abs(first - second) > 0.001f)
                    return;

                Add(axis, SignForCoordinate(first, min, max));
            }
        }

        private sealed class SmartBuildHullCornerBuilder
        {
            private readonly List<DecorationEditAxis> _axes = new List<DecorationEditAxis>(4);
            private readonly List<int> _signs = new List<int>(4);

            internal SmartBuildHullCornerBuilder(Vector3 local)
            {
                Local = local;
            }

            internal Vector3 Local { get; }

            internal void Add(DecorationEditAxis axis, int sign)
            {
                AddAxisSign(_axes, _signs, axis, sign);
            }

            internal void AddFixedAxesFromBounds(SmartBuildBounds bounds)
            {
                Add(DecorationEditAxis.X, SignForCoordinate(Local.x, bounds.Min.x - 0.5f, bounds.Max.x + 0.5f));
                Add(DecorationEditAxis.Y, SignForCoordinate(Local.y, bounds.Min.y - 0.5f, bounds.Max.y + 0.5f));
                Add(DecorationEditAxis.Z, SignForCoordinate(Local.z, bounds.Min.z - 0.5f, bounds.Max.z + 0.5f));
            }

            internal SmartBuildHullCorner ToCorner()
            {
                if (_axes.Count < 3)
                    return null;

                return new SmartBuildHullCorner(
                    Local,
                    _axes.Take(3).ToArray(),
                    _signs.Take(3).ToArray());
            }
        }

        private readonly struct SmartBuildHullFace
        {
            internal SmartBuildHullFace(
                DecorationEditAxis axis,
                int sign,
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d)
            {
                Axis = axis;
                Sign = sign >= 0 ? 1 : -1;
                Local = new[] { a, b, c, d };
            }

            internal DecorationEditAxis Axis { get; }

            internal int Sign { get; }

            internal Vector3[] Local { get; }
        }

        private static void AddAxisSign(
            List<DecorationEditAxis> axes,
            List<int> signs,
            DecorationEditAxis axis,
            int sign)
        {
            if (axis == DecorationEditAxis.None || axis == DecorationEditAxis.Free)
                return;

            int normalized = sign >= 0 ? 1 : -1;
            for (int index = 0; index < axes.Count; index++)
            {
                if (axes[index] == axis && signs[index] == normalized)
                    return;
            }

            axes.Add(axis);
            signs.Add(normalized);
        }

        private sealed class SmartBuildSceneSnapshot
        {
            internal SmartBuildSceneSnapshot(
                AllConstruct construct,
                IEnumerable<SmartBuildPiece> pieces,
                int selectedPieceId,
                SmartBuildTool tool,
                SmartBuildShapeKind selectedShape,
                int selectedSlopeLength,
                SmartBuildEditHandleMode editHandleMode,
                SmartBuildSlopeSupportMode defaultSlopeSupportMode,
                SmartBuildPreviewMode previewMode)
            {
                Construct = construct;
                Pieces = (pieces ?? Array.Empty<SmartBuildPiece>())
                    .Where(piece => piece != null)
                    .Select(piece => piece.Clone())
                    .ToArray();
                SelectedPieceId = selectedPieceId;
                Tool = tool;
                SelectedShape = selectedShape;
                SelectedSlopeLength = selectedSlopeLength;
                EditHandleMode = editHandleMode;
                DefaultSlopeSupportMode = defaultSlopeSupportMode;
                PreviewMode = previewMode;
            }

            internal AllConstruct Construct { get; }

            internal IReadOnlyList<SmartBuildPiece> Pieces { get; }

            internal int SelectedPieceId { get; }

            internal SmartBuildTool Tool { get; }

            internal SmartBuildShapeKind SelectedShape { get; }

            internal int SelectedSlopeLength { get; }

            internal SmartBuildEditHandleMode EditHandleMode { get; }

            internal SmartBuildSlopeSupportMode DefaultSlopeSupportMode { get; }

            internal SmartBuildPreviewMode PreviewMode { get; }
        }

        private readonly struct SmartBuildPlacementCandidate
        {
            internal SmartBuildPlacementCandidate(
                AllConstruct construct,
                Vector3i cell,
                SmartBuildAxis forwardAxis,
                int forwardSign,
                int width,
                int? rightStart,
                Vector3i cuboidSize,
                bool valid,
                string reason)
            {
                Construct = construct;
                Cell = cell;
                ForwardAxis = forwardAxis;
                ForwardSign = forwardSign >= 0 ? 1 : -1;
                Width = Math.Max(1, width);
                RightStart = rightStart;
                CuboidSize = SmartBuildDraft.ClampSize(cuboidSize);
                Valid = valid;
                Reason = reason;
            }

            internal AllConstruct Construct { get; }

            internal Vector3i Cell { get; }

            internal SmartBuildAxis ForwardAxis { get; }

            internal int ForwardSign { get; }

            internal int Width { get; }

            internal int? RightStart { get; }

            internal Vector3i CuboidSize { get; }

            internal bool Valid { get; }

            internal string Reason { get; }
        }
    }
}
