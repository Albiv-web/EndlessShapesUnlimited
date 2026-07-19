using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.Presets;
using DecoLimitLifter.SerializationHud;
using EndlessShapes2;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    /// <summary>
    /// Immutable identity captured from the last craft block accepted by the Smart
    /// Builder eyedropper.  Target material/shape controls may subsequently change;
    /// conditional replacement can still use this source descriptor without reading
    /// mutable editor state.
    /// </summary>
    internal sealed class SmartBuildCraftBlockSampleDescriptor
    {
        internal SmartBuildCraftBlockSampleDescriptor(
            Guid definitionGuid,
            SmartBuildMaterial material,
            string shapeDescriptorKey,
            int length,
            Vector3i occupiedSize,
            Vector3i occupiedOriginOffset,
            Quaternion localRotation,
            SmartBuildAxis forwardAxis,
            int forwardSign,
            SmartBuildAxis rightAxis,
            int rightSign,
            SmartBuildAxis dropAxis,
            int dropSign)
        {
            DefinitionGuid = definitionGuid;
            Material = material;
            ShapeDescriptorKey = string.IsNullOrWhiteSpace(shapeDescriptorKey)
                ? SmartBuildShapeDescriptors.CuboidKey
                : shapeDescriptorKey;
            Length = Mathf.Clamp(length, 1, 4);
            OccupiedSize = SmartBuildDraft.ClampSize(occupiedSize);
            OccupiedOriginOffset = occupiedOriginOffset;
            LocalRotation = localRotation;
            ForwardAxis = forwardAxis;
            ForwardSign = forwardSign >= 0 ? 1 : -1;
            RightAxis = rightAxis;
            RightSign = rightSign >= 0 ? 1 : -1;
            DropAxis = dropAxis;
            DropSign = dropSign >= 0 ? 1 : -1;
        }

        internal Guid DefinitionGuid { get; }

        internal SmartBuildMaterial Material { get; }

        internal string ShapeDescriptorKey { get; }

        internal int Length { get; }

        internal Vector3i OccupiedSize { get; }

        /// <summary>
        /// Minimum occupied-cell offset from the native block origin. This retains
        /// the signed half of a cardinal footprint (for example a 4m beam running
        /// from the clicked origin toward -X instead of silently moving it to +X).
        /// </summary>
        internal Vector3i OccupiedOriginOffset { get; }

        internal Quaternion LocalRotation { get; }

        internal SmartBuildAxis ForwardAxis { get; }

        internal int ForwardSign { get; }

        internal SmartBuildAxis RightAxis { get; }

        internal int RightSign { get; }

        internal SmartBuildAxis DropAxis { get; }

        internal int DropSign { get; }

        internal bool IsCuboid =>
            string.Equals(
                ShapeDescriptorKey,
                SmartBuildShapeDescriptors.CuboidKey,
                StringComparison.OrdinalIgnoreCase);

        internal string RotationLabel =>
            ForwardAxis + (ForwardSign >= 0 ? "+" : "-") +
            " / up " + DropAxis + (DropSign < 0 ? "+" : "-");
    }

    internal sealed partial class SmartBuildSession
    {
        private const float ToolbarHeight = 54f;
        private const float LeftPanelWidth = 392f;
        private const float RightPanelWidth = 282f;
        private const float LeftPanelMinHeight = 520f;
        private const float RightPanelMinHeight = 420f;
        private const float HandleLength = 1.8f;
        private const float AxisPickThresholdPixels = 24f;
        private const float RotateSnapDegrees = 90f;
        private const int ViewModeMenuButtonCount = 9;
        private const int SceneHistoryLimit = 64;
        private const string SceneRecoverySlot = "smart-builder-scene";
        private const float SceneRecoveryDebounceSeconds = 0.75f;
        private const float ShapePaletteRowHeight = 28f;
        private const float ShapePreviewGridMinCardWidth = 112f;
        private const float ShapePreviewGridMinCardHeight = 86f;
        private const float ShapePreviewGridMaxCardHeight = 124f;
        private const float ShapePreviewGridCardAspect = 0.74f;
        private const float ShapePreviewGridGap = 8f;
        private const float ShapePreviewGridOuterPadding = 2f;
        private const int ShapePreviewGridTexturePixels = 96;
        private const int MaxExactMeshPreviewPlacements = 768;
        private const int GeneratedPreviewCacheLimit = 24;
        private const int MaxGeneratedPreviewWireEdges = 1400;
        private const int MaxGeneratedPreviewMaterialFaces = 1600;
        private const float LeftWorkspaceDefaultLowerRatio = 0.62f;
        private const float SelectedSceneDefaultLowerRatio = 0.5f;

        private enum SmartRightPanelPage
        {
            Shapes,
            Generators
        }

        private static Rect s_leftPanelRect = Rect.zero;
        private static Rect s_rightPanelRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static SmartBuildMaterial s_selectedMaterial = SmartBuildMaterial.Wood;
        private static bool s_showLeftPanel = true;
        private static bool s_showRightPanel = true;
        private static bool s_showMaterialSection = true;
        private static bool s_showShapePaletteSection = true;
        private static bool s_showSelectedSection = true;
        private static bool s_showSceneSection = true;
        private static SmartRightPanelPage s_rightPanelPage = SmartRightPanelPage.Shapes;
        private static bool s_shapePreviewGrid;
        private static string s_shapeCategoryFilter = "all";
        private static float s_leftWorkspaceBottomRatio = LeftWorkspaceDefaultLowerRatio;
        private static float s_selectedSceneStackBottomRatio = SelectedSceneDefaultLowerRatio;

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly Dictionary<SmartBuildCellSetFingerprint, SmartGeneratedHullPreview>
            _generatedHullPreviewCache =
                new Dictionary<SmartBuildCellSetFingerprint, SmartGeneratedHullPreview>();
        private readonly Dictionary<SmartBuildMaterial, SmartBuildSource> _pieceMaterialSources =
            new Dictionary<SmartBuildMaterial, SmartBuildSource>();
        private readonly Dictionary<SmartBuildMaterial, string> _pieceMaterialSourceIssues =
            new Dictionary<SmartBuildMaterial, string>();
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.SmartBuild.Toolbar".GetHashCode();
        private readonly int _panelWindowId = "EndlessShapesUnlimited.SmartBuild.Panel".GetHashCode();
        private readonly int _rightPanelWindowId = "EndlessShapesUnlimited.SmartBuild.Shapes".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.SmartBuild.Status".GetHashCode();
        private readonly int _gizmoSettingsWindowId = "EndlessShapesUnlimited.SmartBuild.GizmoSettings".GetHashCode();
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
        private readonly SmartBuildPlanCoordinator _planCoordinator =
            new SmartBuildPlanCoordinator();
        private bool _hasCraftOccupancyFingerprint;
        private long _craftOccupancyFingerprint;
        private object _craftOccupancyFingerprintConstruct;
        private float _nextCraftOccupancyFingerprintAt = -1f;
        private bool _hasObservedFocusedConstruct;
        private AllConstruct _observedFocusedConstruct;
        private static readonly Vector3i[] CraftOccupancyFingerprintOffsets =
        {
            new Vector3i(0, 0, 0),
            new Vector3i(-1, 0, 0),
            new Vector3i(1, 0, 0),
            new Vector3i(0, -1, 0),
            new Vector3i(0, 1, 0),
            new Vector3i(0, 0, -1),
            new Vector3i(0, 0, 1)
        };
        private IReadOnlyList<Vector3i> _previewCells = Array.Empty<Vector3i>();
        private IReadOnlyList<IReadOnlyList<Vector3i>> _previewCellSets =
            Array.Empty<IReadOnlyList<Vector3i>>();
        private IReadOnlyList<SmartBuildVolume> _previewVolumes =
            Array.Empty<SmartBuildVolume>();
        private SmartBuildTool _tool = SmartBuildTool.Draw;
        private SmartBuildDrawPlane _drawPlane = SmartBuildDrawPlane.Camera;
        private SmartBuildOccupancyMode _occupancyMode = SmartBuildOccupancyMode.SkipOccupied;
        private SmartBlockItemPreviewRenderer _itemPreviewRenderer;
        private SmartBuildMaterial _selectedMaterial = s_selectedMaterial;
        private SmartBuildMaterial _sourceMaterial = s_selectedMaterial;
        private SmartBuildShapeKind _selectedShape = SmartBuildShapeKind.Cuboid;
        private string _selectedShapeDescriptorKey = SmartBuildShapeDescriptors.CuboidKey;
        private int _selectedSlopeLength = 1;
        private SmartBuildEditHandleMode _editHandleMode = SmartBuildEditHandleMode.Gizmo;
        private Vector2 _shapePaletteScroll;
        private Vector2 _generatorPaletteScroll;
        private Vector2 _overviewScroll;
        private Vector2 _sceneListScroll;
        private Vector2 _selectedPieceScroll;
        private bool _shapePreviewGrid = s_shapePreviewGrid;
        private string _shapeCategoryFilter = s_shapeCategoryFilter;
        private string _shapeFilter = string.Empty;
        private SmartBuildShapePaletteEntry _hoveredShapeEntry;
        private float _shapePreviewSpin;
        private DecorationEditAxis _dragAxis = DecorationEditAxis.None;
        private DecorationEditAxis[] _dragAxes = Array.Empty<DecorationEditAxis>();
        private int[] _dragSigns = Array.Empty<int>();
        private SmartBuildTool _dragTool = SmartBuildTool.Draw;
        private Vector2 _dragMouseStart;
        private int _dragSign = 1;
        private bool _dragging;
        private bool _rotating;
        private bool _hudPointerGestureOwned;
        private bool _planDirty;
        private string _lastPlanIssueNotificationKey;
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
        private SmartRightPanelPage _rightPanelPage = s_rightPanelPage;
        private float _leftWorkspaceBottomRatio = s_leftWorkspaceBottomRatio;
        private float _selectedSceneStackBottomRatio = s_selectedSceneStackBottomRatio;
        private SmartShapeStackDividerKind _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
        private float _shapeStackDividerDragStartMouseY;
        private float _shapeStackDividerDragStartBottomRatio;
        private string _snapMoveText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartMoveStepCells);
        private string _snapRotateText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartRotateSnapDegrees);
        private string _snapScaleText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.SmartScaleStepCells);
        private bool _gizmoSettingsOpen;
        private Rect _gizmoSettingsButtonScreenRect;
        private string _gizmoMoveSizeText = "1";
        private string _gizmoRotateSizeText = "1";
        private string _gizmoScaleSizeText = "1";
        private string _gizmoThicknessText = "1";
        private string _gizmoHitAreaText = "18";
        private readonly Stack<SmartBuildSceneDelta> _sceneUndo = new Stack<SmartBuildSceneDelta>();
        private readonly Stack<SmartBuildSceneDelta> _sceneRedo = new Stack<SmartBuildSceneDelta>();
        private SmartBuildSceneSnapshot _pendingSceneHistory;
        private SmartBuildSceneSnapshot _dragSceneStart;
        private string _lastGroupScaleIssue;
        private DecorationEditAxis _rotateDragAxis = DecorationEditAxis.None;
        private Vector2 _rotateDragMouseStart;
        private Vector2 _rotateDragCenterScreen;
        private Vector2 _rotateDragStartVector;
        private Vector2 _rotateDragLastVector;
        private float _rotateDragAccumulatedDegrees;
        private float _rotateDragSensitivityScale = 1f;
        private SmartBuildPiece _rotatePieceStart;
        private SmartBuildSceneSnapshot _rotateSceneStart;
        private Vector3i _rotateSelectionPivot;
        private bool _contextMenuOpen;
        private Rect _contextMenuRect;
        private int _contextMenuPieceId;
        private int _sceneSelectionAnchorId = -1;
        private int _arrayCopyCount = 3;
        private int _arraySpacing = 1;
        private bool _brushEnabled;
        private bool _brushPainting;
        private string _brushLastCellKey;
        private int _brushAddedCount;
        private SmartBuildSceneSnapshot _brushSceneStart;
        private bool _blockEyedropperArmed;
        private bool _hasSampledBlockRotation;
        private Quaternion _sampledBlockRotation = Quaternion.identity;
        private string _sampledBlockRotationLabel;
        private Vector3i? _sampledCuboidSize;
        private SmartBuildCraftBlockSampleDescriptor _lastCraftBlockSample;
        private IReadOnlyList<EsuPresetEntry> _smartScenePresets = Array.Empty<EsuPresetEntry>();
        private int _selectedSmartScenePresetIndex = -1;
        private string _smartScenePresetName = "Smart scene";
        private string _smartScenePresetStatus;
        private bool _smartScenePresetsLoaded;
        private bool _smartSceneRecoveryAvailable;
        private bool _smartScenePresetTargetArmed;
        private AllConstruct _smartScenePresetTargetConstruct;
        private Vector3i? _smartScenePresetTargetAnchor;
        private string _lastSavedSceneRecoverySignature;
        private string _pendingSceneRecoverySignature;
        private float _sceneRecoverySaveAt = -1f;
        private bool _sceneRecoveryDirty = true;
        private bool _boxSelecting;
        private Vector2 _boxSelectionStart;
        private Vector2 _boxSelectionCurrent;
        private bool _boxSelectionAdditive;
        private bool _boxSelectionToggle;
        private int[] _boxSelectionBaseIds = Array.Empty<int>();
        private int _boxSelectionBasePrimaryId = -1;
        private DecorationEditAxis[] _hoverEdgeAxes = Array.Empty<DecorationEditAxis>();
        private int[] _hoverEdgeSigns = Array.Empty<int>();
        private DecorationEditAxis[] _hoverCornerAxes = Array.Empty<DecorationEditAxis>();
        private int[] _hoverCornerSigns = Array.Empty<int>();

        private enum SmartShapeStackDividerKind
        {
            None,
            LeftWorkspace,
            SelectedScene
        }

        internal SmartBuildSession(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _pointerProbe = new DecorationPointerProbe(build);
            _viewModes = new DecorationEditorViewModeController(build);
        }

        internal bool Active { get; private set; }

        internal bool CloseRequested => _closeRequested;

        /// <summary>
        /// The last successfully sampled source block. Failed samples never replace it.
        /// </summary>
        internal SmartBuildCraftBlockSampleDescriptor LastCraftBlockSample =>
            _lastCraftBlockSample;

        internal bool SwitchToDecorationEditRequested { get; private set; }

        internal void ClearSwitchToDecorationEditRequest() =>
            SwitchToDecorationEditRequested = false;

        internal bool DismissOpenPopup()
        {
            if (_smartScenePresetTargetArmed)
            {
                _smartScenePresetTargetArmed = false;
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                InfoStore.Add("Smart Builder preset target pick cancelled.");
                return true;
            }

            if (_blockEyedropperArmed)
            {
                _blockEyedropperArmed = false;
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                InfoStore.Add("Smart Builder block eyedropper cancelled.");
                return true;
            }

            if (_contextMenuOpen)
            {
                _contextMenuOpen = false;
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                InfoStore.Add("Smart Builder context menu closed.");
                return true;
            }

            if (!_gizmoSettingsOpen)
                return false;

            CloseGizmoSettingsMenu();
            InfoStore.Add("Gizmo settings closed.");
            return true;
        }

        private SmartBuildShapeDescriptor SelectedShapeDescriptor =>
            SmartBuildShapeDescriptors.ByKey(_selectedShapeDescriptorKey) ??
            SmartBuildShapeDescriptors.Cuboid;

        private bool HasActivePreviewScene =>
            _scene != null && _scene.Count > 0;

        internal bool CanSwitchToDecorationEdit(out string reason)
        {
            if (_gizmoSettingsOpen)
            {
                reason = "Close Gizmo settings before switching modes.";
                return false;
            }

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

            if (HasActivePreviewScene)
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
            ClearEditablePatternViewportGestureState();
            SmartBlockFamilyCatalog.BeginModeActivationCatalogSnapshot();
            _hudPointerGestureOwned = false;
            SmartBuildInputScope.Begin();
            SmartBuildInputScope.SetMouseOverUiProbe(IsCurrentMouseOverSmartUi);
            EsuPanelUiHitTestRegistry.Register(this, HitTestSmartUi);
            DecoLimitLifter.EsuHudDiagnostics.LogGateStatus("Smart Builder opened");
            _itemPreviewRenderer = new SmartBlockItemPreviewRenderer();
            ApplyFocusView();
            RefreshSelection();
            RefreshSmartScenePresetList();
            RefreshSmartSceneRecoveryAvailability();
            SyncSnapTextFromSettings();
            SyncGizmoSettingsText();
        }

        internal void End(bool preserveSharedHud = false)
        {
            if (_brushPainting)
                EndBrushStroke(cancel: true);
            if (_patternViewportGesture != null)
                CancelEditablePatternViewportGesture(notify: false);
            SaveSmartSceneRecoveryImmediately();
            RestoreFocusView();
            EsuPanelUiHitTestRegistry.Unregister(this);
            SmartBuildInputScope.End();
            if (!preserveSharedHud)
                DecorationEditorOverlay.Clear();
            Active = false;
            _scene = null;
            _draft = null;
            _sceneSelectionAnchorId = -1;
            _dragPieceStart = null;
            _plan = null;
            _planCoordinator.Reset();
            ResetCraftOccupancyTracking();
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _hudPointerGestureOwned = false;
            _dragAxis = DecorationEditAxis.None;
            _dragAxes = Array.Empty<DecorationEditAxis>();
            _dragSigns = Array.Empty<int>();
            _dragSceneStart = null;
            ClearRotateDragState();
            ClearBoxSelectionState();
            _planDirty = false;
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
            _contextMenuOpen = false;
            _blockEyedropperArmed = false;
            _smartScenePresetTargetArmed = false;
            _smartScenePresetTargetConstruct = null;
            _smartScenePresetTargetAnchor = null;
            _viewModeMenuOpen = false;
            _gizmoSettingsOpen = false;
            _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
            _itemPreviewRenderer?.Dispose();
            _itemPreviewRenderer = null;
            _generatedHullPreviewCache.Clear();
            ResetPrecisionSessionState();
            SwitchToDecorationEditRequested = false;
        }

        internal void SuspendForModeSwitchHandoff()
        {
            RestoreFocusView();
            EsuPanelUiHitTestRegistry.Unregister(this);
            SmartBuildInputScope.End();
            Active = false;
            _dragging = false;
            _rotating = false;
            _hudPointerGestureOwned = false;
            _resizingLeftPanel = false;
            _resizingRightPanel = false;
            _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
            _viewModeMenuOpen = false;
            _gizmoSettingsOpen = false;
            SwitchToDecorationEditRequested = false;
        }

        internal void Update()
        {
            if (!Active)
                return;

            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership("Smart Builder update");
            EsuHudNotifications.SetActiveSource("Smart Builder");
            RefreshMouseOverUiFromCurrentPointer();
            RefreshSelection();
            TickCraftOccupancyRevision();
            if (_gizmoSettingsOpen)
            {
                SmartBuildInputScope.SetMouseOverUi(true);
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                _viewModes.Tick(_viewMode);
                DrawWorldPreview();
                return;
            }
            if (_contextMenuOpen)
            {
                SmartBuildInputScope.SetMouseOverUi(true);
                if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
                    SmartBuildInputScope.ClaimMouseWheelInputForFrames();
                else
                {
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                }
                _viewModes.Tick(_viewMode);
                DrawWorldPreview();
                return;
            }
            HandleKeyboard();
            HandleMouse();
            TickSmartSceneRecovery();
            _viewModes.Tick(_viewMode);
            DrawWorldPreview();
        }

        internal void OnGUI()
        {
            if (!Active)
                return;

            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership("Smart Builder OnGUI");
            DrawGui(interactive: true);
        }

        internal void DrawModeSwitchHandoffGui()
        {
            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership("Smart Builder handoff GUI");
            DrawGui(interactive: false);
        }

        private void DrawGui(bool interactive)
        {
            Event foregroundEvent = Event.current;
            CaptureHudPointerEventAtStart(foregroundEvent, interactive);
            bool notificationWasForeground =
                interactive &&
                !_gizmoSettingsOpen &&
                !_contextMenuOpen &&
                !_viewModeMenuOpen &&
                !EsuConsoleWindow.ContainsMouse(foregroundEvent?.mousePosition ?? Vector2.zero) &&
                EsuHudNotifications.ExpandedPopupOwnsEvent(foregroundEvent);
            if (!interactive ||
                (!_gizmoSettingsOpen &&
                 !_contextMenuOpen &&
                 !notificationWasForeground))
            {
                DrawGuiCore(interactive);
                return;
            }

            DecorationEditorTheme.Ensure();
            EsuCursorTooltip.BeginFrame(Event.current.mousePosition, suppress: true);
            bool contextMenuWasOpen = _contextMenuOpen;
            EventType foregroundEventType = foregroundEvent == null
                ? EventType.Ignore
                : foregroundEvent.type;
            DecoLimitLifter.EsuModalInputPolicy.SuppressForDisabledBackground(
                foregroundEvent);
            bool previousEnabled = GUI.enabled;
            try
            {
                GUI.enabled = previousEnabled &&
                              !EsuHudPreferences.FadeHudBehindModalPopups;
                DrawGuiCore(interactive: false);
            }
            finally
            {
                DecoLimitLifter.EsuModalInputPolicy.RestoreForForeground(
                    foregroundEvent,
                    foregroundEventType);
                GUI.enabled = previousEnabled;
            }

            EsuConsoleWindow.DrawForegroundWindow(interactive: false);
            EsuCursorTooltip.BeginFrame(
                Event.current.mousePosition,
                suppress: false,
                allowGuiTooltipFallback: true);
            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -20000);
            try
            {
                if (_gizmoSettingsOpen)
                    DrawGizmoSettingsMenu();
                else if (contextMenuWasOpen)
                    DrawPreviewContextMenu();
                else if (notificationWasForeground)
                    EsuHudNotifications.DrawExpandedPopup();
            }
            finally
            {
                GUI.depth = previousDepth;
            }
            EsuCursorTooltip.Draw();
            SmartBuildInputScope.SetMouseOverUi(true);
            if (_gizmoSettingsOpen)
                ConsumeGizmoSettingsInput(foregroundEvent);
            else
                ConsumePreviewContextMenuInput(foregroundEvent, foregroundEventType);
        }

        private static void ConsumePreviewContextMenuInput(
            Event current,
            EventType originalType)
        {
            if (!DecoLimitLifter.EsuModalInputPolicy.IsBlockingEventType(originalType))
                return;

            if (originalType == EventType.ScrollWheel)
                SmartBuildInputScope.ClaimMouseWheelInputForFrames();
            else
            {
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
            }

            if (current != null && current.type != EventType.Used)
                current.Use();
        }

        private void DrawGuiCore(bool interactive)
        {
            _hoveredShapeEntry = default;
            DecorationEditorTheme.Ensure();
            if (interactive)
                EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed());
            if (Event.current.type == EventType.Repaint)
            {
                _shapePreviewSpin += Time.unscaledDeltaTime * 70f;
                DecorationEditorOverlay.Render();
            }

            ApplyLayoutResetIfNeeded();
            _toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight);
            _statusRect = EsuHudLayout.BottomStripRect(StatusHeightScaled());
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
                EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
            _rightPanelRect = EsuHudLayout.ClampPanel(
                _rightPanelRect,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight(),
                LeftPanelTopLimit(),
                EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
            if (interactive)
            {
                if (_showLeftPanel)
                    HandleLeftPanelResize();
                if (_showRightPanel)
                    HandleRightPanelResize();

                bool overUi = IsMouseOverSmartUi(MouseGuiPosition());
                SmartBuildInputScope.SetMouseOverUi(overUi);
                ClaimMouseWheelOverSmartUi(Event.current, overUi);
            }

            DrawViewportBoxSelectionMarquee();
            GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
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
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_rightPanelRect, leftEdge: true), "Drag to resize the shape and generator library.");
                }

                EsuHudNotifications.DrawExpandedPopup();
                DrawViewModeMenu(_toolbarRect);
                DrawPreviewContextMenu();
                DrawShapePreviewCard();
                EsuConsoleWindow.DrawForegroundWindow();
                EsuCursorTooltip.Draw();
            }
            else
            {
                EsuHudNotifications.DrawExpandedPopup();
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
            s_rightPanelPage = _rightPanelPage;
            s_shapePreviewGrid = _shapePreviewGrid;
            s_shapeCategoryFilter = _shapeCategoryFilter;
            // Persist the user's preferred ratios, not the geometry-constrained
            // effective ratios calculated while drawing a temporarily small panel.
            s_leftWorkspaceBottomRatio = ValidSmartStackRatio(_leftWorkspaceBottomRatio);
            s_selectedSceneStackBottomRatio = ValidSmartStackRatio(_selectedSceneStackBottomRatio);
            if (!interactive)
                return;

            bool mouseOverUiAfterControls = IsMouseOverSmartUi(MouseGuiPosition());
            SmartBuildInputScope.SetMouseOverUi(mouseOverUiAfterControls);
            if (mouseOverUiAfterControls && ShouldConsumeGuiEvent(Event.current))
            {
                if (Event.current.type == EventType.ScrollWheel)
                    SmartBuildInputScope.ClaimMouseWheelInputForFrames();
                else
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                Event.current.Use();
            }
        }

        private bool TooltipInputSuppressed() =>
            _gizmoSettingsOpen ||
            _contextMenuOpen ||
            _dragging ||
            _rotating ||
            _patternViewportDragging ||
            _boxSelecting ||
            _resizingLeftPanel ||
            _resizingRightPanel ||
            _draggingShapeStackDivider != SmartShapeStackDividerKind.None;

        private float StatusHeightScaled() =>
            EsuHudLayout.BottomStripHeight();

        private float LeftPanelTopLimit() => EsuHudLayout.EditorPanelTopLimit(ToolbarHeight);

        private float MinLeftPanelWidth() => EsuHudLayout.Scale(300f);

        private float MinLeftPanelHeight() =>
            ClampSmartPanelMinimumHeight(
                EsuHudLayout.Scale(LeftPanelMinHeight),
                AvailablePanelHeight());

        private float MaxLeftPanelWidth() => Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.62f);

        private float MaxLeftPanelHeight() => AvailablePanelHeight();

        private float MinRightPanelWidth() => EsuHudLayout.Scale(230f);

        private float MinRightPanelHeight() =>
            ClampSmartPanelMinimumHeight(
                EsuHudLayout.Scale(RightPanelMinHeight),
                AvailablePanelHeight());

        private float MaxRightPanelWidth() => Mathf.Max(MinRightPanelWidth(), Screen.width * 0.42f);

        private float MaxRightPanelHeight() => AvailablePanelHeight();

        private float AvailablePanelHeight() =>
            EsuHudLayout.AvailablePanelHeight(
                LeftPanelTopLimit(),
                EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));

        internal static float ClampSmartPanelMinimumHeight(
            float desiredMinimum,
            float availableHeight)
        {
            return EsuHudLayout.ClampPanelMinimum(
                desiredMinimum,
                availableHeight);
        }

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
                Mathf.Max(MinRightPanelWidth(), Screen.width * 0.22f));
            float height = MaxRightPanelHeight();
            float x = Mathf.Max(EsuHudLayout.EditorSideMargin, Screen.width - width - EsuHudLayout.EditorSideMargin);
            return new Rect(x, LeftPanelTopLimit(), width, height);
        }

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _leftPanelRect = DefaultLeftPanelRect();
            _rightPanelRect = DefaultRightPanelRect();
            _leftWorkspaceBottomRatio = LeftWorkspaceDefaultLowerRatio;
            _selectedSceneStackBottomRatio = SelectedSceneDefaultLowerRatio;
            s_leftWorkspaceBottomRatio = _leftWorkspaceBottomRatio;
            s_selectedSceneStackBottomRatio = _selectedSceneStackBottomRatio;
            _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
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
                    EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
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
                    EsuHudLayout.BottomPanelLimit(StatusHeightScaled()));
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
                    if (HasActivePreviewScene)
                        DrawDraftOutline();
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

                DrawEditablePatternViewportHandles();
                DrawDestructivePlanFootprintOverlay();
                DrawSmartBuildDiagnosticOverlay();
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
            if (GUIUtility.keyboardControl != 0 ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
                return;

            if ((Input.GetKeyDown(KeyCode.Return) ||
                 Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                TryCommitEditablePatternViewportGesture())
            {
                return;
            }

            // A viewport pattern edit is an explicit Enter/Escape transaction.
            // Do not let another shortcut mutate the scene underneath it.
            if (_patternViewportGesture != null)
                return;

            if (TryHandlePrecisionKeyboardShortcuts())
                return;

            if (TryHandleEsuNumberShortcut())
                return;

            if (TryHandleFacingSymmetryShortcut())
                return;

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                _draft != null)
                ApplyPreview();
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

        private bool TryHandleFacingSymmetryShortcut()
        {
            if (!DecoLimitLifter.EsuInputState.IsFacingSymmetryShortcutDown())
                return false;

            SmartBuildInputScope.ClaimBuildInputForFrames();
            SmartBuildInputScope.ClaimCameraInputForFrames();
            if (!TrySymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell))
            {
                InfoStore.Add("Point at the focused construct grid before toggling a symmetry mirror.");
                return true;
            }

            if (!DecoLimitLifter.EsuSymmetry.TryCameraFacingLocal(construct, out Vector3 localFacing))
            {
                InfoStore.Add("A camera direction is required before toggling a symmetry mirror.");
                return true;
            }

            string activeSymmetryBefore = ActiveSymmetryPlanStamp();
            bool changed = DecoLimitLifter.EsuSymmetry.TryToggleFacingPlane(
                construct,
                cell,
                localFacing,
                out string message);
            if (changed)
                HandleSymmetryRevision(activeSymmetryBefore);
            InfoStore.Add(message);
            return true;
        }

        private void HandleSymmetryRevision(string activeSymmetryBefore)
        {
            if (!string.Equals(
                    activeSymmetryBefore,
                    ActiveSymmetryPlanStamp(),
                    StringComparison.Ordinal))
            {
                RebuildPlan(SmartBuildPlanRevisionKind.Symmetry);
                return;
            }

            // Arming or cancelling a pending plane changes controls/overlays only.
            RegisterPresentationRevision();
        }

        private static string ActiveSymmetryPlanStamp()
        {
            IReadOnlyDictionary<DecorationEditAxis, int> planes =
                DecoLimitLifter.EsuSymmetry.ActivePlanes;
            if (planes == null || planes.Count == 0)
                return "off";

            AllConstruct symmetryConstruct = DecoLimitLifter.EsuSymmetry.Construct;
            int constructIdentity = symmetryConstruct == null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(
                    symmetryConstruct);
            return constructIdentity +
                   "|" +
                   string.Join(
                       ",",
                       planes
                           .OrderBy(pair => pair.Key)
                           .Select(pair =>
                               ((int)pair.Key).ToString(CultureInfo.InvariantCulture) +
                               ":" +
                               pair.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private bool CycleShapeShortcut()
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            IReadOnlyList<SmartBuildShapeDescriptor> descriptors = OneMetreShapeDescriptors();
            if (descriptors.Count == 0)
            {
                InfoStore.Add(_sourceReason ?? "No 1m Smart Builder shapes are available for this material.");
                return true;
            }

            int index = descriptors
                .Select((descriptor, descriptorIndex) => new { descriptor, descriptorIndex })
                .FirstOrDefault(entry => entry.descriptor.Key == _selectedShapeDescriptorKey)
                ?.descriptorIndex ?? -1;
            _selectedSlopeLength = 1;
            SelectShapeDescriptor(descriptors[(index + 1) % descriptors.Count]);
            _selectedSlopeLength = 1;
            _viewModeMenuOpen = false;
            _contextMenuOpen = false;
            InfoStore.Add("Smart Builder shape: " + PendingShapeLabel() + ".");
            return true;
        }

        private IReadOnlyList<SmartBuildShapeDescriptor> OneMetreShapeDescriptors() =>
            AvailableShapeDescriptors()
                .Where(HasOneMetreShapeCandidate)
                .ToArray();

        private bool HasOneMetreShapeCandidate(SmartBuildShapeDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            if (!descriptor.UsesLengthSelector && Math.Max(1, descriptor.TransitionTo) != 1)
                return false;

            return PaletteCandidateForLength(descriptor, 1) != null;
        }

        private IReadOnlyList<SmartBuildShapeDescriptor> AvailableShapeDescriptors()
        {
            IReadOnlyList<SmartBuildShapeDescriptor> descriptors = _source?.AvailableShapeDescriptors;
            if (descriptors != null && descriptors.Count > 0)
                return descriptors;

            return new[]
            {
                SmartBuildShapeDescriptors.Cuboid,
                SmartBuildShapeDescriptors.DownSlope
            };
        }

        private IReadOnlyList<SmartBuildShapeDescriptor> AvailablePaletteShapeDescriptors() =>
            AvailableShapeDescriptors()
                .Where(descriptor => descriptor?.IsGenerator != true)
                .ToArray();

        private IReadOnlyList<SmartBuildShapeDescriptor> AvailableGeneratorDescriptors() =>
            AvailableShapeDescriptors()
                .Where(descriptor => descriptor?.IsGenerator == true)
                .ToArray();

        private void SelectShapeDescriptor(SmartBuildShapeDescriptor descriptor, bool armAddMode = true)
        {
            descriptor ??= SmartBuildShapeDescriptors.Cuboid;
            string previousKey = _selectedShapeDescriptorKey;
            int previousLength = _selectedSlopeLength;
            _selectedShapeDescriptorKey = descriptor.Key;
            _selectedShape = descriptor.Kind;
            if (descriptor.UsesLengthSelector)
                _selectedSlopeLength = Mathf.Clamp(_selectedSlopeLength, 1, 4);
            else if (descriptor.TransitionTo > 0)
                _selectedSlopeLength = Mathf.Clamp(descriptor.TransitionTo, 1, 4);
            if (!string.Equals(previousKey, _selectedShapeDescriptorKey, StringComparison.OrdinalIgnoreCase) ||
                previousLength != _selectedSlopeLength)
            {
                InvalidateConditionalReplacementPreview();
            }
            if (armAddMode)
                ArmAddMode();
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
            _previewMode =
                _previewMode == SmartBuildPreviewMode.Wireframe ? SmartBuildPreviewMode.Material :
                _previewMode == SmartBuildPreviewMode.Material ? SmartBuildPreviewMode.MaterialOnly :
                SmartBuildPreviewMode.Wireframe;
            RegisterPresentationRevision();
            _viewModeMenuOpen = false;
            InfoStore.Add("Smart Builder preview: " + PreviewModeLabel(_previewMode) + ".");
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
            if (HandleHudPointerInputOwnership())
                return;

            if (Input.GetMouseButtonDown(2) || Input.GetMouseButton(2) || Input.GetMouseButtonUp(2))
                return;

            if (TryHandlePrecisionPointerInput())
                return;

            if (_smartScenePresetTargetArmed)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    _smartScenePresetTargetArmed = false;
                    InfoStore.Add("Smart Builder preset target pick cancelled.");
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    TryCaptureSmartScenePresetTargetFromPointer();
                }

                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

            if (_blockEyedropperArmed)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    _blockEyedropperArmed = false;
                    InfoStore.Add("Smart Builder block eyedropper cancelled.");
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    TrySamplePointedCraftBlock();
                }

                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

            if (_boxSelecting)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    ClearBoxSelectionState();
                }
                else if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                {
                    EndViewportBoxSelection();
                }
                else
                {
                    _boxSelectionCurrent = MouseGuiPosition();
                }

                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

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

            if (HandleEditablePatternViewportMouse())
                return;

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

            if (_brushPainting)
            {
                if (Input.GetMouseButtonDown(1))
                    EndBrushStroke(cancel: true);
                else if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                    EndBrushStroke(cancel: false);
                else
                    ContinueBrushStroke();

                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (_dragging)
                    EndDrag(resetDraft: true);
                else if (_tool == SmartBuildTool.Draw)
                    CancelAddMode();
                else if (TryOpenPreviewContextMenu())
                    _contextMenuOpen = true;
                else
                    DeselectSmartBuilderPiece();
                SmartBuildInputScope.ClaimBuildInputForFrames();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (_tool == SmartBuildTool.Draw && _brushEnabled)
                {
                    BeginBrushStroke();
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
                    return;
                }

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

                if (_tool != SmartBuildTool.Draw && HasActivePreviewScene)
                {
                    BeginViewportBoxSelection();
                    SmartBuildInputScope.ClaimBuildInputForFrames();
                    SmartBuildInputScope.ClaimCameraInputForFrames();
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

        private bool HandleHudPointerInputOwnership()
        {
            bool pointerDown = AnyMouseButtonDown();
            bool pointerHeld = AnyMouseButtonHeld();
            bool pointerUp = AnyMouseButtonUp();
            bool pointerOverHud =
                SmartBuildInputScope.MouseOverUi ||
                IsCurrentMouseOverSmartUi();
            bool blockWorldInput = ResolveHudPointerGestureOwnership(
                _hudPointerGestureOwned,
                pointerOverHud,
                pointerDown,
                pointerHeld,
                pointerUp,
                out bool ownedAfterInput);
            _hudPointerGestureOwned = ownedAfterInput;
            if (!blockWorldInput)
                return false;

            if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
                SmartBuildInputScope.ClaimMouseWheelInputForFrames();

            if (pointerDown || pointerHeld || pointerUp)
            {
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
            }

            // A world gesture that crosses onto any HUD surface must be cancelled,
            // not committed by a later release after the HUD consumes the event.
            if (_dragging)
                EndDrag(resetDraft: true);
            if (_rotating)
                EndRotateDrag(resetDraft: true);
            if (_brushPainting)
                EndBrushStroke(cancel: true);
            if (_patternViewportDragging)
                CancelEditablePatternViewportGesture(notify: false);
            return true;
        }

        internal static bool ResolveHudPointerGestureOwnership(
            bool ownedAtStart,
            bool pointerOverHud,
            bool pointerDown,
            bool pointerHeld,
            bool pointerUp,
            out bool ownedAfterInput)
        {
            bool pointerLifecycleEvent = pointerDown || pointerHeld || pointerUp;
            bool capturedByHud =
                ownedAtStart ||
                pointerOverHud && pointerLifecycleEvent;
            bool blockWorldInput = pointerOverHud || capturedByHud;
            ownedAfterInput =
                capturedByHud &&
                !pointerUp &&
                (pointerDown || pointerHeld);
            return blockWorldInput;
        }

        private void CaptureHudPointerEventAtStart(Event current, bool interactive)
        {
            if (!interactive || current == null)
                return;

            bool pointerDown = current.type == EventType.MouseDown;
            bool pointerHeld = current.type == EventType.MouseDrag;
            bool pointerUp = current.type == EventType.MouseUp;
            bool pointerOverHud = IsMouseOverSmartUi(current.mousePosition);
            if (current.type == EventType.ScrollWheel)
            {
                if (pointerOverHud)
                    SmartBuildInputScope.ClaimMouseWheelInputForFrames();
                return;
            }

            if (!pointerDown && !pointerHeld && !pointerUp)
                return;

            bool blockWorldInput = ResolveHudPointerGestureOwnership(
                _hudPointerGestureOwned,
                pointerOverHud,
                pointerDown,
                pointerHeld,
                pointerUp,
                out bool ownedAfterInput);
            // OnGUI normally follows Update, but retain a consumed release until
            // the next Update as well so a control that closes its own panel can
            // never expose that same release to world input.
            _hudPointerGestureOwned = ownedAfterInput || (blockWorldInput && pointerUp);
            if (!blockWorldInput)
                return;

            // Claim before a control mutates or hides its panel. The event remains
            // available to IMGUI so the intended HUD action still executes.
            SmartBuildInputScope.ClaimBuildInputForFrames();
            SmartBuildInputScope.ClaimCameraInputForFrames();
        }

        private static bool AnyMouseButtonDown() =>
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2);

        private static bool AnyMouseButtonHeld() =>
            Input.GetMouseButton(0) ||
            Input.GetMouseButton(1) ||
            Input.GetMouseButton(2);

        private static bool AnyMouseButtonUp() =>
            Input.GetMouseButtonUp(0) ||
            Input.GetMouseButtonUp(1) ||
            Input.GetMouseButtonUp(2);

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

        private void BeginBrushStroke()
        {
            if (_source == null)
            {
                InfoStore.Add(_sourceReason ?? "Selected Smart Builder material is unavailable.");
                return;
            }

            _brushSceneStart = CaptureSceneSnapshot();
            _brushLastCellKey = null;
            _brushAddedCount = 0;
            _brushPainting = true;
            ContinueBrushStroke();
        }

        private void ContinueBrushStroke()
        {
            if (!_brushPainting ||
                (_scene?.Count ?? 0) >= SmartBuildPieceScene.MaximumScenePieces ||
                !TryPlacementCandidate(out SmartBuildPlacementCandidate candidate) ||
                !candidate.Valid)
            {
                return;
            }

            string key = candidate.Cell.x.ToString(CultureInfo.InvariantCulture) + "," +
                         candidate.Cell.y.ToString(CultureInfo.InvariantCulture) + "," +
                         candidate.Cell.z.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(key, _brushLastCellKey, StringComparison.Ordinal))
                return;

            if (CreatePreviewAtSeed(
                    candidate.Construct,
                    candidate.Cell,
                    candidate.ForwardAxis,
                    candidate.ForwardSign,
                    candidate.Width,
                    candidate.RightStart,
                    candidate.CuboidSize,
                    recordHistory: false,
                    activateScale: false,
                    notify: false))
            {
                _brushLastCellKey = key;
                _brushAddedCount++;
            }
        }

        private void EndBrushStroke(bool cancel)
        {
            if (!_brushPainting)
                return;

            SmartBuildSceneSnapshot before = _brushSceneStart;
            int added = _brushAddedCount;
            _brushPainting = false;
            _brushSceneStart = null;
            _brushLastCellKey = null;
            _brushAddedCount = 0;
            if (cancel)
            {
                RestoreSceneSnapshot(before);
                InfoStore.Add("Smart Builder brush stroke cancelled.");
                return;
            }

            if (added > 0)
            {
                PushSceneSnapshot(before);
                _tool = SmartBuildTool.Draw;
                RebuildPlan();
                RefreshApplyCancelAttention();
                InfoStore.Add(
                    "Smart Builder brushed " + added.ToString(CultureInfo.InvariantCulture) +
                    (added == 1 ? " preview piece." : " preview pieces.") +
                    " Undo removes the complete stroke.");
            }
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
            SmartBuildShapeDescriptor selectedDescriptor = SelectedShapeDescriptor;
            if ((_selectedShape == SmartBuildShapeKind.DownSlope || selectedDescriptor.IsFixedGeometry || selectedDescriptor.IsGenerator) &&
                rightStart.HasValue)
            {
                DeriveRightAxis(forwardAxis, forwardSign, out SmartBuildAxis rightAxis, out _);
                cell = SmartBuildAxisHelper.Set(cell, rightAxis, rightStart.Value);
            }

            Vector3i resolvedCuboidSize = SmartBuildDraft.ClampSize(cuboidSize);
            bool useExactSampledCuboid =
                selectedDescriptor.IsCuboid &&
                _sampledCuboidSize.HasValue &&
                resolvedCuboidSize.Equals(new Vector3i(1, 1, 1));
            if (useExactSampledCuboid)
            {
                resolvedCuboidSize = _sampledCuboidSize.Value;
                if (_hasSampledBlockRotation && LastCraftBlockSample?.IsCuboid == true)
                    cell += LastCraftBlockSample.OccupiedOriginOffset;
            }

            bool valid = CanPlaceDraftOrigin(construct, cell, out string reason);
            return new SmartBuildPlacementCandidate(
                construct,
                cell,
                forwardAxis,
                forwardSign,
                Math.Max(1, width),
                rightStart,
                resolvedCuboidSize,
                valid,
                reason);
        }

        private void CancelAddMode()
        {
            _tool = _draft != null
                ? SmartBuildTool.Scale
                : HasActivePreviewScene
                    ? SmartBuildTool.Move
                    : SmartBuildTool.Draw;
            InfoStore.Add(_draft != null
                ? "Add cancelled. Editing selected Smart Builder piece."
                : "Add cancelled.");
        }

        private bool DeselectSmartBuilderPiece()
        {
            if (_scene == null || _draft == null)
                return false;

            _scene.ClearSelection();
            _draft = null;
            _sceneSelectionAnchorId = -1;
            _tool = SmartBuildTool.Move;
            _contextMenuOpen = false;
            ClearRotateDragState();
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add("Smart Builder selection cleared.");
            return true;
        }

        private void PlacePendingSymmetryPlane()
        {
            if (!TrySymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell))
            {
                InfoStore.Add("Point at the focused construct grid to place the symmetry plane.");
                return;
            }

            string activeSymmetryBefore = ActiveSymmetryPlanStamp();
            if (!DecoLimitLifter.EsuSymmetry.TryPlacePending(
                    construct,
                    cell,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            HandleSymmetryRevision(activeSymmetryBefore);
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

        private bool CreatePreviewAtSeed(
            AllConstruct construct,
            Vector3i cell,
            SmartBuildAxis forwardAxis = SmartBuildAxis.Z,
            int forwardSign = 1,
            int width = 1,
            int? rightStart = null,
            Vector3i? cuboidSize = null,
            bool recordHistory = true,
            bool activateScale = true,
            bool notify = true)
        {
            if (!CanPlaceDraftOrigin(construct, cell, out string reason))
            {
                if (notify)
                    InfoStore.Add(reason);
                return false;
            }

            if (_scene != null && !ReferenceEquals(_scene.Construct, construct))
            {
                InfoStore.Add("Apply or cancel the current Smart Builder scene before starting on another construct.");
                return false;
            }

            if (_scene != null && !_scene.CanAddPieces(1, out reason))
            {
                if (notify)
                    InfoStore.Add(reason);
                return false;
            }

            SmartBuildSceneSnapshot historyBefore = recordHistory
                ? CaptureSceneSnapshot()
                : null;
            if (_scene == null)
                _scene = new SmartBuildPieceScene(construct);

            SmartBuildShapeDescriptor selectedDescriptor = SelectedShapeDescriptor;
            if ((_selectedShape == SmartBuildShapeKind.DownSlope || selectedDescriptor.IsFixedGeometry || selectedDescriptor.IsGenerator) &&
                rightStart.HasValue)
            {
                DeriveRightAxis(forwardAxis, forwardSign, out SmartBuildAxis rightAxis, out _);
                cell = SmartBuildAxisHelper.Set(cell, rightAxis, rightStart.Value);
            }

            if (selectedDescriptor.IsGenerator)
            {
                _draft = SmartBuildPiece.CreateGeneratedShape(
                    construct,
                    cell,
                    selectedDescriptor,
                    forwardAxis,
                    forwardSign,
                    Math.Max(1, width),
                    _drawPlane);
            }
            else if (selectedDescriptor.ProceduralDownSlope)
            {
                _draft = SmartBuildPiece.CreateDownSlope(
                    construct,
                    cell,
                    _selectedSlopeLength,
                    forwardAxis,
                    forwardSign,
                    Math.Max(1, width),
                    _drawPlane,
                    _defaultSlopeSupportMode);
            }
            else if (selectedDescriptor.IsFixedGeometry)
            {
                _draft = SmartBuildPiece.CreateFixedShape(
                    construct,
                    cell,
                    selectedDescriptor,
                    _selectedSlopeLength,
                    forwardAxis,
                    forwardSign,
                    Math.Max(1, width),
                    _drawPlane);
            }
            else
            {
                Vector3i seedSize = cuboidSize ??
                                    _sampledCuboidSize ??
                                    new Vector3i(1, 1, 1);
                _draft = SmartBuildPiece.CreateCuboid(
                    construct,
                    cell,
                    SmartBuildDraft.ClampSize(seedSize),
                    _drawPlane);
            }
            _draft.SetMaterialOverride(_selectedMaterial);
            ApplySampledBlockRotation(_draft);
            if (!_scene.Add(_draft))
            {
                _draft = _scene.SelectedPiece;
                if (notify)
                {
                    InfoStore.Add(
                        "The preview was not added because the Smart Builder scene reached its " +
                        SmartBuildPieceScene.MaximumScenePieces.ToString(CultureInfo.InvariantCulture) +
                        "-piece limit.");
                }
                return false;
            }
            if (recordHistory)
                PushSceneSnapshot(historyBefore);
            _sceneSelectionAnchorId = _draft.Id;
            _tool = activateScale ? SmartBuildTool.Scale : SmartBuildTool.Draw;
            RebuildPlan();
            if (notify)
            {
                InfoStore.Add(
                    _draft.ShapeLabel() +
                    (activateScale ? " added. Scale mode active." : " brushed into the preview."));
            }
            return true;
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

            if (IsCellOccupied(construct, cell) &&
                _occupancyMode != SmartBuildOccupancyMode.ReplaceOccupied &&
                _occupancyMode != SmartBuildOccupancyMode.EraseOccupied)
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
                SmartBuildVolume volume = piece.ToVolume(SourceForPiece(piece));
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

            SmartBuildVolume bestVolume = bestPiece.ToVolume(SourceForPiece(bestPiece));
            axis = SmartBuildDraft.ToSmartAxis(bestAxis);
            sign = bestSign >= 0 ? 1 : -1;
            cell = AdjacentCellFromPreviewFace(bestVolume, bestHit, axis, sign);
            SmartBuildShapeDescriptor selectedDescriptor = SelectedShapeDescriptor;
            if (selectedDescriptor.IsCuboid &&
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

            if ((_selectedShape == SmartBuildShapeKind.DownSlope || selectedDescriptor.IsFixedGeometry || selectedDescriptor.IsGenerator) &&
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

            bool additive = IsShiftHeld();
            bool toggle = IsControlHeld();
            if (toggle)
                _scene.ToggleSelection(piece.Id);
            else if (additive)
                _scene.AddToSelection(piece.Id);
            else
                _scene.Select(piece.Id);

            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = piece.Id;
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add(SelectionStatusMessage());
            return true;
        }

        private string SelectionStatusMessage()
        {
            int count = _scene?.SelectionCount ?? 0;
            if (count <= 0)
                return "Smart Builder selection cleared.";
            if (count == 1)
            {
                return "Selected Smart Builder piece #" +
                       (_scene?.SelectedPiece?.Id ?? -1).ToString(CultureInfo.InvariantCulture) +
                       ".";
            }

            return "Selected " + count.ToString(CultureInfo.InvariantCulture) +
                   " Smart Builder pieces; primary #" +
                   (_scene?.SelectedPiece?.Id ?? -1).ToString(CultureInfo.InvariantCulture) +
                   ".";
        }

        private bool SelectSceneRow(SmartBuildPiece piece, bool shift, bool control)
        {
            if (_scene == null || piece == null)
                return false;

            bool changed;
            if (shift && _sceneSelectionAnchorId >= 0)
            {
                changed = _scene.SelectRange(
                    _sceneSelectionAnchorId,
                    piece.Id,
                    additive: control);
            }
            else if (control)
            {
                changed = _scene.ToggleSelection(piece.Id);
                _sceneSelectionAnchorId = piece.Id;
            }
            else
            {
                changed = _scene.Select(piece.Id);
                _sceneSelectionAnchorId = piece.Id;
            }

            if (!changed)
                return false;

            _draft = _scene.SelectedPiece;
            if (_draft == null)
                _sceneSelectionAnchorId = -1;
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add(SelectionStatusMessage());
            return true;
        }

        private void BeginViewportBoxSelection()
        {
            _boxSelecting = true;
            _boxSelectionStart = MouseGuiPosition();
            _boxSelectionCurrent = _boxSelectionStart;
            _boxSelectionAdditive = IsShiftHeld();
            _boxSelectionToggle = IsControlHeld();
            _boxSelectionBaseIds = _scene?.SelectedPieceIds.ToArray() ?? Array.Empty<int>();
            _boxSelectionBasePrimaryId = _scene?.SelectedPiece?.Id ?? -1;
        }

        private void EndViewportBoxSelection()
        {
            _boxSelectionCurrent = MouseGuiPosition();
            if (_scene == null ||
                (_boxSelectionCurrent - _boxSelectionStart).sqrMagnitude < 36f)
            {
                ClearBoxSelectionState();
                return;
            }

            Rect marquee = RectFromPoints(_boxSelectionStart, _boxSelectionCurrent);
            SmartBuildPiece[] hits = _scene.Pieces
                .Where(piece => TryProjectPieceGuiBounds(piece, out Rect bounds) && marquee.Overlaps(bounds))
                .ToArray();
            var selectedIds = new HashSet<int>(
                _boxSelectionAdditive || _boxSelectionToggle
                    ? _boxSelectionBaseIds
                    : Array.Empty<int>());
            if (_boxSelectionToggle)
            {
                foreach (SmartBuildPiece hit in hits)
                {
                    if (!selectedIds.Remove(hit.Id))
                        selectedIds.Add(hit.Id);
                }
            }
            else
            {
                foreach (SmartBuildPiece hit in hits)
                    selectedIds.Add(hit.Id);
            }

            int primaryId = hits
                .AsEnumerable()
                .Reverse()
                .Select(piece => piece.Id)
                .FirstOrDefault(id => selectedIds.Contains(id));
            if (primaryId == 0 || !selectedIds.Contains(primaryId))
            {
                primaryId = selectedIds.Contains(_boxSelectionBasePrimaryId)
                    ? _boxSelectionBasePrimaryId
                    : _scene.Pieces
                        .Where(piece => selectedIds.Contains(piece.Id))
                        .Select(piece => piece.Id)
                        .LastOrDefault();
            }

            _scene.SetSelection(selectedIds, primaryId);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            ClearBoxSelectionState();
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add(SelectionStatusMessage());
        }

        private bool TryProjectPieceGuiBounds(SmartBuildPiece piece, out Rect bounds)
        {
            bounds = Rect.zero;
            if (piece?.Construct == null)
                return false;

            Camera camera = Camera.main ?? Camera.current;
            SmartBuildVolume volume = piece.ToVolume(SourceForPiece(piece));
            if (camera == null || volume == null)
                return false;

            bool any = false;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector3 world in volume.GetWorldCorners())
            {
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                    continue;

                Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
                min.x = Mathf.Min(min.x, gui.x);
                min.y = Mathf.Min(min.y, gui.y);
                max.x = Mathf.Max(max.x, gui.x);
                max.y = Mathf.Max(max.y, gui.y);
                any = true;
            }

            if (!any)
                return false;

            const float minimumPickSize = 8f;
            float width = Mathf.Max(minimumPickSize, max.x - min.x);
            float height = Mathf.Max(minimumPickSize, max.y - min.y);
            bounds = new Rect(
                (min.x + max.x - width) * 0.5f,
                (min.y + max.y - height) * 0.5f,
                width,
                height);
            return true;
        }

        private static Rect RectFromPoints(Vector2 first, Vector2 second) =>
            new Rect(
                Mathf.Min(first.x, second.x),
                Mathf.Min(first.y, second.y),
                Mathf.Abs(second.x - first.x),
                Mathf.Abs(second.y - first.y));

        private void ClearBoxSelectionState()
        {
            _boxSelecting = false;
            _boxSelectionStart = Vector2.zero;
            _boxSelectionCurrent = Vector2.zero;
            _boxSelectionAdditive = false;
            _boxSelectionToggle = false;
            _boxSelectionBaseIds = Array.Empty<int>();
            _boxSelectionBasePrimaryId = -1;
        }

        private void DrawViewportBoxSelectionMarquee()
        {
            if (!_boxSelecting)
                return;

            Rect marquee = RectFromPoints(_boxSelectionStart, _boxSelectionCurrent);
            Color previous = GUI.color;
            GUI.color = new Color(0.3f, 0.92f, 1f, 0.58f);
            GUI.Box(marquee, GUIContent.none, DecorationEditorTheme.RowSelected);
            GUI.color = previous;
        }

        private bool TryOpenPreviewContextMenu()
        {
            if (!TryPickPreviewPiece(out SmartBuildPiece piece))
                return false;

            bool selected = _scene.IsSelected(piece.Id)
                ? _scene.AddToSelection(piece.Id)
                : _scene.Select(piece.Id);
            if (selected)
            {
                _draft = _scene.SelectedPiece;
                _sceneSelectionAnchorId = piece.Id;
                _contextMenuPieceId = piece.Id;
                Vector2 mouse = MouseGuiPosition();
                float width = EsuHudLayout.Scale(142f);
                float height = EsuHudLayout.Scale(168f);
                _contextMenuRect = new Rect(mouse.x, mouse.y, width, height);
                _contextMenuRect.x = Mathf.Clamp(_contextMenuRect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
                _contextMenuRect.y = Mathf.Clamp(_contextMenuRect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
                RebuildPlan(SmartBuildPlanRevisionKind.Selection);
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
                SmartBuildVolume volume = piece.ToVolume(SourceForPiece(piece));
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

            RestoreSelectedPiecesFromSnapshot(_dragSceneStart);
            IReadOnlyList<SmartBuildPiece> selected = _scene?.SelectedPieces;
            if (selected == null || selected.Count == 0)
                return;

            if (_dragTool == SmartBuildTool.Move)
            {
                int cells = ProjectDragDeltaToCells(_dragAxis, SmartMoveStepCells);
                Vector3i delta = SmartBuildAxisHelper.ToVector3i(
                    SmartBuildDraft.ToSmartAxis(_dragAxis),
                    cells);
                foreach (SmartBuildPiece piece in selected)
                    piece.MoveBy(delta);
            }
            else if (_dragTool == SmartBuildTool.Scale)
            {
                if (selected.Count > 1)
                    TryScaleSelectionFromDraggedHandles();
                else
                    ResizePieceFromDraggedHandles(selected[0]);
            }

            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private bool TryScaleSelectionFromDraggedHandles()
        {
            if (_draft == null || _scene == null || _scene.SelectionCount <= 1)
                return false;

            SmartBuildBounds originalPrimary = _draft.GroupScaleBounds;
            Vector3 pivot = originalPrimary.Center;

            // Derive scale factors with the primary piece's familiar handle behavior,
            // then restore the immutable drag snapshot and spatially transform the whole
            // group. This scales offsets and extents from one shared pivot without
            // accumulating rounding drift between mouse-move frames.
            ResizePieceFromDraggedHandles(_draft);
            SmartBuildBounds resizedPrimary = _draft.GroupScaleBounds;
            Vector3i originalSize = originalPrimary.Size;
            Vector3i resizedSize = resizedPrimary.Size;
            Vector3 factors = new Vector3(
                resizedSize.x / (float)Math.Max(1, originalSize.x),
                resizedSize.y / (float)Math.Max(1, originalSize.y),
                resizedSize.z / (float)Math.Max(1, originalSize.z));

            RestoreSelectedPiecesFromSnapshot(_dragSceneStart);
            foreach (SmartBuildPiece piece in _scene.SelectedPieces)
            {
                if (piece.TryScaleAboutPivot(pivot, factors, out string reason))
                    continue;

                RestoreSelectedPiecesFromSnapshot(_dragSceneStart);
                if (!string.Equals(_lastGroupScaleIssue, reason, StringComparison.Ordinal))
                {
                    _lastGroupScaleIssue = reason;
                    InfoStore.Add("Group scale rejected: " + reason);
                }
                return false;
            }

            _lastGroupScaleIssue = null;
            return true;
        }

        private void ResizePieceFromDraggedHandles(SmartBuildPiece piece)
        {
            if (piece == null)
                return;

            if (ShouldUseCardinalSlopeStretch(piece))
            {
                ResizeDownSlopeCardinalFromDraggedHandles(piece);
                return;
            }

            bool roundLockResize = ReferenceEquals(piece, _draft)
                ? PrimaryGeneratedRoundLockUsesMultiAxisDrag()
                : piece.IsGeneratedShape && piece.GeneratorRoundLock && _dragAxes.Length > 1;
            if (roundLockResize)
            {
                int cells = ProjectDragDeltaToCells(_dragAxis, SmartScaleStepCells);
                piece.ResizeFromHandle(_dragAxis, _dragSign, cells);
                return;
            }

            for (int index = 0; index < _dragAxes.Length; index++)
            {
                DecorationEditAxis axis = _dragAxes[index];
                if (axis == DecorationEditAxis.None)
                    continue;

                int sign = index < _dragSigns.Length ? _dragSigns[index] : 1;
                int cells = ProjectDragDeltaToCells(axis, SmartScaleStepCells);
                piece.ResizeFromHandle(axis, sign, cells);
            }
        }

        private bool PrimaryGeneratedRoundLockUsesMultiAxisDrag() =>
            _draft.IsGeneratedShape && _draft.GeneratorRoundLock && _dragAxes.Length > 1;

        private static bool ShouldUseCardinalSlopeStretch(SmartBuildPiece piece) =>
            piece?.ShapeKind == SmartBuildShapeKind.DownSlope && IsCardinalSlopeStretchHeld();

        private void ResizeDownSlopeCardinalFromDraggedHandles(SmartBuildPiece piece)
        {
            bool convertedDropAxis = false;
            for (int index = 0; index < _dragAxes.Length; index++)
            {
                DecorationEditAxis axis = _dragAxes[index];
                if (axis == DecorationEditAxis.None ||
                    axis == DecorationEditAxis.Free ||
                    SmartBuildDraft.ToSmartAxis(axis) != piece.DropAxis)
                {
                    continue;
                }

                int sign = index < _dragSigns.Length ? _dragSigns[index] : 1;
                int cells = ProjectDragDeltaToCells(axis, SmartScaleStepCells);
                piece.ResizeDownSlopeCardinalFromHandle(axis, sign, cells);
                convertedDropAxis = true;
            }

            for (int index = 0; index < _dragAxes.Length; index++)
            {
                DecorationEditAxis axis = _dragAxes[index];
                if (axis == DecorationEditAxis.None ||
                    axis == DecorationEditAxis.Free)
                {
                    continue;
                }

                SmartBuildAxis smartAxis = SmartBuildDraft.ToSmartAxis(axis);
                if (smartAxis == piece.DropAxis ||
                    convertedDropAxis && smartAxis == piece.ForwardAxis)
                {
                    continue;
                }

                int sign = index < _dragSigns.Length ? _dragSigns[index] : 1;
                int cells = ProjectDragDeltaToCells(axis, SmartScaleStepCells);
                piece.ResizeDownSlopeCardinalFromHandle(axis, sign, cells);
            }
        }

        private void EndDrag(bool resetDraft)
        {
            if (resetDraft)
                RestoreSelectedPiecesFromSnapshot(_dragSceneStart);

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
            _lastGroupScaleIssue = null;
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
            float visualLength = SmartGizmoHandleLength(_dragTool);
            Vector3 start = _draft.Construct.SafeLocalToGlobal(center);
            Vector3 visualEnd = _draft.Construct.SafeLocalToGlobal(
                center + axisVector * visualLength);
            Vector3 referenceEnd = _draft.Construct.SafeLocalToGlobal(
                center + axisVector * HandleLength);
            Vector3 screenStart = camera.WorldToScreenPoint(start);
            Vector3 screenVisualEnd = camera.WorldToScreenPoint(visualEnd);
            Vector3 screenReferenceEnd = camera.WorldToScreenPoint(referenceEnd);
            if (screenStart.z <= camera.nearClipPlane ||
                screenVisualEnd.z <= camera.nearClipPlane ||
                screenReferenceEnd.z <= camera.nearClipPlane)
            {
                return 0;
            }

            Vector2 guiStart = ScreenToGui(screenStart);
            if (!DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    guiStart,
                    ScreenToGui(screenVisualEnd),
                    ScreenToGui(screenReferenceEnd),
                    HandleLength,
                    out float projected))
            {
                return 0;
            }
            int step = Mathf.Max(1, stepCells);
            return Mathf.RoundToInt(projected / step) * step;
        }

        private static float SmartGizmoHandleLength(SmartBuildTool tool)
        {
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float multiplier = tool == SmartBuildTool.Scale
                ? style.ScaleSize
                : style.MoveSize;
            return HandleLength * multiplier;
        }

        private bool TryPickHandle(out DecorationEditAxis axis, out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            if (_draft?.Construct == null)
                return false;

            Vector3 center = _draft.CenterLocal;
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                MouseGuiPosition(),
                ProjectSmartGizmoAxes(
                    center,
                    SmartGizmoHandleLength(_tool),
                    includeNegative: _tool == SmartBuildTool.Scale),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false);
            axis = pick.Axis;
            sign = pick.Sign == 0 ? 1 : pick.Sign;
            return pick.IsHit;
        }

        private DecorationGizmoProjections ProjectSmartGizmoAxes(
            Vector3 center,
            float length,
            bool includeNegative)
        {
            Vector2? origin = TryProjectSmartGizmoPoint(center);
            if (!origin.HasValue)
                return new DecorationGizmoProjections(null, null, null, null);

            Vector3 x = DecorationEditMath.AxisVector(DecorationEditAxis.X) * length;
            Vector3 y = DecorationEditMath.AxisVector(DecorationEditAxis.Y) * length;
            Vector3 z = DecorationEditMath.AxisVector(DecorationEditAxis.Z) * length;
            return includeNegative
                ? new DecorationGizmoProjections(
                    origin,
                    TryProjectSmartGizmoPoint(center + x),
                    TryProjectSmartGizmoPoint(center - x),
                    TryProjectSmartGizmoPoint(center + y),
                    TryProjectSmartGizmoPoint(center - y),
                    TryProjectSmartGizmoPoint(center + z),
                    TryProjectSmartGizmoPoint(center - z))
                : new DecorationGizmoProjections(
                    origin,
                    TryProjectSmartGizmoPoint(center + x),
                    TryProjectSmartGizmoPoint(center + y),
                    TryProjectSmartGizmoPoint(center + z));
        }

        private Vector2? TryProjectSmartGizmoPoint(Vector3 local) =>
            TryProjectSmartGizmo(_draft?.Construct, local, out Vector2 screen)
                ? (Vector2?)screen
                : null;

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

            if (_draft.IsFixedGeometry &&
                TryPickFixedGeometryFace(localOrigin, localDirection, out axis, out sign))
            {
                return true;
            }

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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

        private void RebuildPlan(
            SmartBuildPlanRevisionKind revisions = SmartBuildPlanRevisionKind.Geometry)
        {
            FinalizePendingSceneHistory();
            if ((revisions & SmartBuildPlanRevisionKind.Geometry) != 0)
                _scene?.MarkGeometryChanged();
            if (_planDirty &&
                (revisions & SmartBuildPlanRevisionKind.PlanningInputs) == 0)
            {
                // A drag-time preview is deliberately not commit-current. Never let a
                // coincident selection/presentation update promote its older plan.
                revisions |= SmartBuildPlanRevisionKind.Geometry;
            }
            _planCoordinator.ObserveCraftIdentity(FocusedConstruct());
            _planCoordinator.RegisterRevision(revisions);

            // Selection changes invalidate a conditional scan scope even though the
            // normal geometry plan itself remains reusable.
            if (_conditionalReplacementPlan != null || _conditionalReplacementInputs != null)
                ClearConditionalReplacementPreview(rebuildNormalPlan: false);

            if (_planCoordinator.TryReuseNormalPlan(out SmartBuildCoordinatedPlan cached))
            {
                AdoptCoordinatedPlan(cached);
                NotifyPlanIssueIfNeeded();
                return;
            }

            _planDirty = true;
            long planningStarted = System.Diagnostics.Stopwatch.GetTimestamp();

            if (_scene == null || _scene.Count == 0)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                RecordCoordinatedPlan(planningStarted);
                ClearPlanIssueNotification();
                return;
            }

            if (!ReferenceEquals(FocusedConstruct(), _scene.Construct))
            {
                SmartBuildPreviewSnapshot preview =
                    _scene.BuildPreviewWithSources(SourceForPiece);
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
                    "The Smart Builder scene is bound to a different focused construct. Return focus to that construct or cancel the preview.");
                RecordCoordinatedPlan(planningStarted);
                NotifyPlanIssueIfNeeded();
                return;
            }

            string materialIssue = FirstPieceMaterialIssue();
            if (!string.IsNullOrWhiteSpace(materialIssue))
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreviewWithSources(SourceForPiece);
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
                    failureReason: materialIssue);
                RecordCoordinatedPlan(planningStarted);
                NotifyPlanIssueIfNeeded();
                return;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_scene.Construct, out string reason))
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreviewWithSources(SourceForPiece);
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
                RecordCoordinatedPlan(planningStarted);
                NotifyPlanIssueIfNeeded();
                return;
            }

            _plan = _scene.BuildPlanWithSources(
                SourceForPiece,
                IsOccupied,
                new SmartBuildPlannerOptions
                {
                    SkipOccupiedCells = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied,
                    AllowOccupiedCells =
                        _occupancyMode == SmartBuildOccupancyMode.ReplaceOccupied ||
                        _occupancyMode == SmartBuildOccupancyMode.EraseOccupied
                },
                out SmartBuildPreviewSnapshot scenePreview);
            _previewCells = scenePreview.Cells;
            _previewCellSets = scenePreview.CellSets;
            _previewVolumes = scenePreview.Volumes;
            if (_plan.CanCommit &&
                (_occupancyMode == SmartBuildOccupancyMode.ReplaceOccupied ||
                 _occupancyMode == SmartBuildOccupancyMode.EraseOccupied))
            {
                Vector3i[] removals = _previewCells
                    .Where(IsOccupied)
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray();
                if (_occupancyMode == SmartBuildOccupancyMode.EraseOccupied)
                {
                    _plan = removals.Length == 0
                        ? new SmartBuildPlan(
                            _scene.Construct,
                            VolumeFromCells(_scene.Construct, _previewCells),
                            Array.Empty<SmartBuildPlacement>(),
                            Array.Empty<Vector3i>(),
                            Array.Empty<string>(),
                            canCommit: false,
                            failureReason: "Erase preview does not intersect any existing blocks.")
                        : new SmartBuildPlan(
                            _scene.Construct,
                            VolumeFromCells(_scene.Construct, _previewCells),
                            Array.Empty<SmartBuildPlacement>(),
                            Array.Empty<Vector3i>(),
                            new[] { "Erase removes complete block items touched by the preview." },
                            canCommit: true,
                            failureReason: null)
                            .WithCommitOperation(SmartBuildCommitOperation.Erase, removals);
                }
                else
                {
                    _plan.WithCommitOperation(SmartBuildCommitOperation.Replace, removals);
                }
            }
            else if (_plan.CanCommit && !EveryPreviewSetTouchesExistingConstruct())
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

            RecordCoordinatedPlan(planningStarted);
            NotifyPlanIssueIfNeeded();
        }

        private void AdoptCoordinatedPlan(SmartBuildCoordinatedPlan coordinated)
        {
            _plan = coordinated?.Plan;
            _previewCells = coordinated?.Cells ?? Array.Empty<Vector3i>();
            _previewCellSets = coordinated?.CellSets ??
                Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = coordinated?.Volumes ?? Array.Empty<SmartBuildVolume>();
            _planDirty = false;
        }

        private void RegisterPresentationRevision()
        {
            _scene?.MarkPresentationChanged();
            _planCoordinator.RegisterRevision(SmartBuildPlanRevisionKind.Presentation);
        }

        private void RecordCoordinatedPlan(long planningStarted)
        {
            long elapsedTicks = Math.Max(
                0L,
                System.Diagnostics.Stopwatch.GetTimestamp() - planningStarted);
            TimeSpan duration = TimeSpan.FromSeconds(
                elapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency);
            if (_plan != null)
            {
                _plan.WithConstructToken(
                    SmartBuildConstructToken.Capture(
                        _scene?.Construct,
                        _planCoordinator.Diagnostics.OccupancyRevision));
            }
            var coordinated = new SmartBuildCoordinatedPlan(
                _plan,
                _previewCells,
                _previewCellSets,
                _previewVolumes);
            _planCoordinator.RecordNormalPlan(
                coordinated,
                duration,
                _scene?.Count ?? 0,
                _previewCells?.Count ?? 0,
                _plan?.Placements?.Count ?? 0);
            _planDirty = false;
            CaptureCraftOccupancyFingerprintBaseline();
        }

        private void TickCraftOccupancyRevision()
        {
            AllConstruct focused = FocusedConstruct();
            if (!_hasObservedFocusedConstruct ||
                !ReferenceEquals(_observedFocusedConstruct, focused))
            {
                _hasObservedFocusedConstruct = true;
                _observedFocusedConstruct = focused;
                _hasCraftOccupancyFingerprint = false;
                if (_scene != null && _scene.Count > 0)
                {
                    RebuildPlan(SmartBuildPlanRevisionKind.Craft);
                    return;
                }
            }

            if (_planDirty || _dragging || _rotating ||
                _scene?.Construct == null || _previewCells.Count == 0 ||
                Time.unscaledTime < _nextCraftOccupancyFingerprintAt)
            {
                return;
            }

            _nextCraftOccupancyFingerprintAt = Time.unscaledTime + 0.75f;
            if (!TryComputeCraftOccupancyFingerprint(out long fingerprint))
                return;
            if (!_hasCraftOccupancyFingerprint ||
                !ReferenceEquals(_craftOccupancyFingerprintConstruct, _scene.Construct))
            {
                _hasCraftOccupancyFingerprint = true;
                _craftOccupancyFingerprintConstruct = _scene.Construct;
                _craftOccupancyFingerprint = fingerprint;
                return;
            }
            if (_craftOccupancyFingerprint == fingerprint)
                return;

            _craftOccupancyFingerprint = fingerprint;
            RebuildPlan(
                SmartBuildPlanRevisionKind.Craft |
                SmartBuildPlanRevisionKind.Occupancy);
        }

        private void CaptureCraftOccupancyFingerprintBaseline()
        {
            _nextCraftOccupancyFingerprintAt = Time.unscaledTime + 0.75f;
            if (!TryComputeCraftOccupancyFingerprint(out long fingerprint))
            {
                _hasCraftOccupancyFingerprint = false;
                _craftOccupancyFingerprintConstruct = null;
                return;
            }
            _hasCraftOccupancyFingerprint = true;
            _craftOccupancyFingerprintConstruct = _scene?.Construct;
            _craftOccupancyFingerprint = fingerprint;
        }

        private bool TryComputeCraftOccupancyFingerprint(out long fingerprint)
        {
            fingerprint = 0L;
            if (_scene?.Construct == null || _previewCells == null ||
                _previewCells.Count == 0)
            {
                return false;
            }

            unchecked
            {
                long hash = 1469598103934665603L;
                int probes = 0;
                for (int cellIndex = 0;
                     cellIndex < _previewCells.Count &&
                     probes < SmartBuildLimits.MaximumOccupancyFingerprintCells;
                     cellIndex++)
                {
                    Vector3i baseCell = _previewCells[cellIndex];
                    for (int offsetIndex = 0;
                         offsetIndex < CraftOccupancyFingerprintOffsets.Length &&
                         probes < SmartBuildLimits.MaximumOccupancyFingerprintCells;
                         offsetIndex++)
                    {
                        Vector3i cell = baseCell +
                                        CraftOccupancyFingerprintOffsets[offsetIndex];
                        Block block = null;
                        bool lookupFailed = false;
                        try
                        {
                            block = _scene.Construct.AllBasics?.GetBlockViaLocalPosition(cell);
                        }
                        catch
                        {
                            // Treat an unreadable cell as occupied/unknown. A later
                            // readable state changes the fingerprint and replans.
                            lookupFailed = true;
                        }

                        bool occupied = lookupFailed || (block != null && !block.IsDeleted);
                        ItemDefinition definition = null;
                        Vector3i itemOrigin = cell;
                        Quaternion rotation = Quaternion.identity;
                        int footprintCellCount = 0;
                        if (occupied && block != null)
                        {
                            try
                            {
                                definition = block.item;
                                itemOrigin = block.LocalPosition;
                                rotation = block.LocalRotation;
                                footprintCellCount = Math.Max(
                                    0,
                                    definition?.SizeInfo?.ArrayPositionsUsed ?? 0);
                            }
                            catch
                            {
                                definition = null;
                                itemOrigin = cell;
                                rotation = Quaternion.identity;
                                footprintCellCount = -1;
                            }
                        }

                        hash = MixCraftCellFingerprint(
                            hash,
                            cell,
                            occupied,
                            definition == null
                                ? 0
                                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(definition),
                            itemOrigin,
                            rotation,
                            footprintCellCount);
                        probes++;
                    }
                }
                hash = (hash ^ probes) * 1099511628211L;
                hash = (hash ^ _previewCells.Count) * 1099511628211L;
                fingerprint = hash;
                return true;
            }
        }

        internal static long MixCraftCellFingerprint(
            long hash,
            Vector3i cell,
            bool occupied,
            int definitionIdentity,
            Vector3i itemOrigin,
            Quaternion rotation,
            int footprintCellCount)
        {
            unchecked
            {
                hash = (hash ^ cell.x) * 1099511628211L;
                hash = (hash ^ cell.y) * 1099511628211L;
                hash = (hash ^ cell.z) * 1099511628211L;
                hash = (hash ^ (occupied ? 1L : 0L)) * 1099511628211L;
                if (!occupied)
                    return hash;

                hash = (hash ^ definitionIdentity) * 1099511628211L;
                hash = (hash ^ itemOrigin.x) * 1099511628211L;
                hash = (hash ^ itemOrigin.y) * 1099511628211L;
                hash = (hash ^ itemOrigin.z) * 1099511628211L;
                hash = (hash ^ rotation.x.GetHashCode()) * 1099511628211L;
                hash = (hash ^ rotation.y.GetHashCode()) * 1099511628211L;
                hash = (hash ^ rotation.z.GetHashCode()) * 1099511628211L;
                hash = (hash ^ rotation.w.GetHashCode()) * 1099511628211L;
                hash = (hash ^ footprintCellCount) * 1099511628211L;
                return hash;
            }
        }

        private void ResetCraftOccupancyTracking()
        {
            _hasCraftOccupancyFingerprint = false;
            _craftOccupancyFingerprint = 0L;
            _craftOccupancyFingerprintConstruct = null;
            _nextCraftOccupancyFingerprintAt = -1f;
            _hasObservedFocusedConstruct = false;
            _observedFocusedConstruct = null;
        }

        private void NotifyPlanIssueIfNeeded()
        {
            if (!TryGetPlanIssueMessage(out string message, out EsuHudNotificationKind kind))
            {
                ClearPlanIssueNotification();
                return;
            }

            string key = kind + "|" + message;
            if (string.Equals(_lastPlanIssueNotificationKey, key, StringComparison.Ordinal))
                return;

            _lastPlanIssueNotificationKey = key;
            EsuHudNotifications.ShowSystem(
                "Smart Builder",
                message,
                kind);
        }

        private bool TryGetPlanIssueMessage(
            out string message,
            out EsuHudNotificationKind kind)
        {
            message = null;
            kind = EsuHudNotificationKind.Info;
            if (_planDirty || _plan == null)
                return false;

            if (!_plan.CanCommit)
            {
                message = string.IsNullOrWhiteSpace(_plan.FailureReason)
                    ? "Smart Builder plan is blocked."
                    : _plan.FailureReason.Trim();
                kind = EsuHudNotificationKind.Warning;
                return true;
            }

            if (_plan.SkippedCells.Count > 0)
            {
                message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Smart Builder skipped {0:N0} occupied preview cell{1}.",
                    _plan.SkippedCells.Count,
                    _plan.SkippedCells.Count == 1 ? string.Empty : "s");
                kind = EsuHudNotificationKind.Warning;
                return true;
            }

            if (_plan.Warnings.Count > 0 &&
                !string.IsNullOrWhiteSpace(_plan.Warnings[0]))
            {
                message = _plan.Warnings[0].Trim();
                kind = EsuHudNotificationKind.Warning;
                return true;
            }

            return false;
        }

        private void ClearPlanIssueNotification() =>
            _lastPlanIssueNotificationKey = null;

        private void BuildPreviewSymmetrySets(SmartBuildVolume volume)
        {
            if (_scene != null)
            {
                SmartBuildPreviewSnapshot preview = _scene.BuildPreviewWithSources(SourceForPiece);
                _previewCells = preview.Cells;
                _previewCellSets = preview.CellSets;
                _previewVolumes = preview.Volumes;
                return;
            }

            if (!SmartBuildLimits.TryMaterializeBounded(
                    volume.EnumerateCells(),
                    SmartBuildLimits.MaximumPlannerInputCells,
                    out Vector3i[] baseCells,
                    out _))
            {
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                return;
            }
            var allCells = new HashSet<Vector3i>();
            var sets = new List<IReadOnlyList<Vector3i>>();
            var volumes = new List<SmartBuildVolume>();
            var seenSets = new HashSet<SmartBuildCellSetFingerprint>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                Vector3i[] cells = baseCells
                    .Select(variant.Mirror)
                    .Distinct()
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .ToArray();
                if (!seenSets.Add(SmartBuildCellSetFingerprint.From(cells)))
                    continue;

                sets.Add(cells);
                foreach (Vector3i cell in cells)
                {
                    if (allCells.Count >= SmartBuildLimits.MaximumPlannerInputCells &&
                        !allCells.Contains(cell))
                    {
                        _previewCells = Array.Empty<Vector3i>();
                        _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                        _previewVolumes = Array.Empty<SmartBuildVolume>();
                        return;
                    }
                    allCells.Add(cell);
                }
                SmartBuildVolume mirrored = MirrorVolume(volume, variant);
                if (mirrored != null)
                    volumes.Add(mirrored);
            }

            _previewCells = allCells
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
                    .Select(piece => piece.ToVolume(SourceForPiece(piece)))
                    .Where(volume => volume != null)
                    .ToArray();
                return;
            }

            SmartBuildPreviewSnapshot preview = _scene.BuildPreviewWithSources(SourceForPiece);
            _previewCells = preview.Cells;
            _previewCellSets = preview.CellSets;
            _previewVolumes = preview.Volumes;
        }

        private static IReadOnlyList<SmartBuildVolume> BuildPreviewVolumesOnly(SmartBuildVolume volume)
        {
            if (volume == null)
                return Array.Empty<SmartBuildVolume>();

            var volumes = new List<SmartBuildVolume>();
            var seen = new HashSet<SmartBuildVolumeKey>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                SmartBuildVolume mirrored = MirrorVolume(volume, variant);
                if (mirrored == null)
                    continue;

                if (seen.Add(new SmartBuildVolumeKey(mirrored.MinCell, mirrored.MaxCell)))
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

            bool conditionalPlanWasDirty = _planDirty && ConditionalReplacementPreviewActive;
            if (_planDirty)
                RebuildPlan();

            if (conditionalPlanWasDirty)
            {
                InfoStore.Add(
                    "Smart Builder conditional replacement was not applied because its preview became dirty. Preview it again first.");
                return;
            }

            if (ConditionalReplacementPreviewActive &&
                !ConditionalReplacementPreviewMatchesCurrentInputs())
            {
                InvalidateConditionalReplacementPreview();
                InfoStore.Add(
                    "Smart Builder conditional replacement was not applied because its source, target, or selected scan region changed. Preview it again first.");
                return;
            }

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
            FinalizePendingSceneHistory();
            _pendingSceneHistory = CaptureSceneSnapshot();
        }

        private SmartBuildSceneSnapshot CaptureSceneSnapshot() =>
            new SmartBuildSceneSnapshot(
                _scene?.CaptureState() ??
                    new SmartBuildSceneState(
                        _draft?.Construct,
                        _draft == null
                            ? Array.Empty<SmartBuildSceneNodeState>()
                            : new[] { new SmartBuildSceneNodeState(_draft) },
                        _draft == null ? Array.Empty<int>() : new[] { _draft.Id },
                        _draft?.Id ?? -1),
                _tool,
                _selectedShape,
                _selectedShapeDescriptorKey,
                _selectedSlopeLength,
                _selectedMaterial,
                _drawPlane,
                _occupancyMode,
                _editHandleMode,
                _defaultSlopeSupportMode,
                _previewMode);

        private void RestoreSelectedPiecesFromSnapshot(SmartBuildSceneSnapshot snapshot)
        {
            if (snapshot == null || _scene == null)
                return;

            var startsById = snapshot.Pieces.ToDictionary(piece => piece.Id);
            foreach (int id in snapshot.SelectedPieceIds)
            {
                SmartBuildPiece live = _scene.Pieces.FirstOrDefault(piece => piece.Id == id);
                if (live != null && startsById.TryGetValue(id, out SmartBuildPiece start))
                    live.CopyFrom(start);
            }

            _scene.SetSelection(snapshot.SelectedPieceIds, snapshot.SelectedPieceId);
            _draft = _scene.SelectedPiece;
        }

        private void PushSceneSnapshot(SmartBuildSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;
            FinalizePendingSceneHistory();
            PushSceneDelta(SmartBuildSceneDelta.Create(snapshot, CaptureSceneSnapshot()));
        }

        private void FinalizePendingSceneHistory()
        {
            if (_pendingSceneHistory == null)
                return;
            SmartBuildSceneSnapshot before = _pendingSceneHistory;
            _pendingSceneHistory = null;
            PushSceneDelta(SmartBuildSceneDelta.Create(before, CaptureSceneSnapshot()));
        }

        private void PushSceneDelta(SmartBuildSceneDelta delta)
        {
            if (delta == null)
                return;
            _sceneUndo.Push(delta);
            while (_sceneUndo.Count > SceneHistoryLimit)
            {
                SmartBuildSceneDelta[] retained = _sceneUndo
                    .Take(SceneHistoryLimit)
                    .Reverse()
                    .ToArray();
                _sceneUndo.Clear();
                foreach (SmartBuildSceneDelta entry in retained)
                    _sceneUndo.Push(entry);
            }
            _sceneRedo.Clear();
            MarkSmartSceneRecoveryDirty();
        }

        private void UndoSceneEdit()
        {
            FinalizePendingSceneHistory();
            if (_sceneUndo.Count == 0)
                return;

            SmartBuildSceneDelta delta = _sceneUndo.Pop();
            SmartBuildSceneSnapshot current = CaptureSceneSnapshot();
            _sceneRedo.Push(delta);
            RestoreSceneSnapshot(delta.Resolve(current, forward: false));
            InfoStore.Add("Smart Builder preview undo.");
        }

        private void RedoSceneEdit()
        {
            FinalizePendingSceneHistory();
            if (_sceneRedo.Count == 0)
                return;

            SmartBuildSceneDelta delta = _sceneRedo.Pop();
            SmartBuildSceneSnapshot current = CaptureSceneSnapshot();
            _sceneUndo.Push(delta);
            RestoreSceneSnapshot(delta.Resolve(current, forward: true));
            InfoStore.Add("Smart Builder preview redo.");
        }

        private void RestoreSceneSnapshot(SmartBuildSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _tool = snapshot.Tool;
            _selectedShape = snapshot.SelectedShape;
            _selectedShapeDescriptorKey = snapshot.SelectedShapeDescriptorKey;
            _selectedSlopeLength = snapshot.SelectedSlopeLength;
            _selectedMaterial = snapshot.SelectedMaterial;
            s_selectedMaterial = snapshot.SelectedMaterial;
            _drawPlane = snapshot.DrawPlane;
            _occupancyMode = snapshot.OccupancyMode;
            _editHandleMode = snapshot.EditHandleMode;
            _defaultSlopeSupportMode = snapshot.DefaultSlopeSupportMode;
            _previewMode = snapshot.PreviewMode;
            if (snapshot.SceneState.Nodes.Count == 0 || snapshot.Construct == null)
            {
                _scene = null;
                _draft = null;
                _sceneSelectionAnchorId = -1;
            }
            else
            {
                _scene = SmartBuildPieceScene.RestoreState(snapshot.SceneState);
                _draft = _scene.SelectedPiece;
                _sceneSelectionAnchorId = _draft?.Id ?? -1;
            }

            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _dragAxes = Array.Empty<DecorationEditAxis>();
            _dragSigns = Array.Empty<int>();
            _dragPieceStart = null;
            _dragSceneStart = null;
            ClearEditablePatternViewportGestureState();
            ClearRotateDragState();
            ClearBoxSelectionState();
            _contextMenuOpen = false;
            _source = null;
            _sourceReason = null;
            ClearSmartPreviewRendererCache();
            RefreshSelection();
            RebuildPlan();
        }

        private void CancelPreview()
        {
            CancelPrecisionSnapFaceMode(notify: false);
            ClearEditablePatternViewportGestureState();
            ClearConditionalReplacementPreview(rebuildNormalPlan: false);
            ClearApplyCancelAttention();
            _brushPainting = false;
            _brushSceneStart = null;
            _brushLastCellKey = null;
            _brushAddedCount = 0;
            ClearSmartSceneRecovery();
            _scene = null;
            _draft = null;
            _sceneSelectionAnchorId = -1;
            _dragPieceStart = null;
            _plan = null;
            _planCoordinator.Reset();
            ResetCraftOccupancyTracking();
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _planDirty = false;
            ClearPlanIssueNotification();
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
            _dragSceneStart = null;
            ClearRotateDragState();
            ClearBoxSelectionState();
            _contextMenuOpen = false;
            _sceneUndo.Clear();
            _sceneRedo.Clear();
            _pendingSceneHistory = null;
        }

        private void RefreshSelection()
        {
            if (_source != null &&
                _sourceReason == null &&
                _sourceMaterial == _selectedMaterial)
            {
                return;
            }

            bool materialChanged = _sourceMaterial != _selectedMaterial;
            if (SmartBlockFamilyCatalog.TryCreateMaterialSource(
                    _selectedMaterial,
                    out SmartBuildSource source,
                    out string reason))
            {
                _source = source;
                _sourceMaterial = _selectedMaterial;
                _sourceReason = null;
                _pieceMaterialSources[_selectedMaterial] = source;
                _pieceMaterialSourceIssues.Remove(_selectedMaterial);
                if (materialChanged)
                    ClearSmartPreviewRendererCache();
                EnsureSelectedShapeAvailable();
                if (HasActivePreviewScene)
                    RebuildPlan(SmartBuildPlanRevisionKind.Material);
                return;
            }

            _source = null;
            _sourceMaterial = _selectedMaterial;
            _sourceReason = reason;
            _pieceMaterialSources.Remove(_selectedMaterial);
            _pieceMaterialSourceIssues[_selectedMaterial] = reason;
            if (materialChanged)
                ClearSmartPreviewRendererCache();
            if (_scene == null || _scene.Count == 0)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
                ClearPlanIssueNotification();
            }
            else
            {
                RebuildPlan(SmartBuildPlanRevisionKind.Material);
            }
        }

        private SmartBuildMaterial MaterialForPiece(SmartBuildPiece piece) =>
            piece?.MaterialOverride ?? _selectedMaterial;

        private SmartBuildSource SourceForPiece(SmartBuildPiece piece)
        {
            SmartBuildMaterial material = MaterialForPiece(piece);
            if (_source != null &&
                _sourceReason == null &&
                _sourceMaterial == material)
            {
                return _source;
            }

            if (_pieceMaterialSources.TryGetValue(material, out SmartBuildSource cached))
                return cached;

            if (SmartBlockFamilyCatalog.TryCreateMaterialSource(
                    material,
                    out SmartBuildSource source,
                    out string reason))
            {
                _pieceMaterialSources[material] = source;
                _pieceMaterialSourceIssues.Remove(material);
                return source;
            }

            _pieceMaterialSourceIssues[material] = reason;
            return null;
        }

        private string FirstPieceMaterialIssue()
        {
            foreach (SmartBuildPiece piece in _scene?.Pieces ?? Array.Empty<SmartBuildPiece>())
            {
                if (SourceForPiece(piece) != null)
                    continue;

                SmartBuildMaterial material = MaterialForPiece(piece);
                string label = SmartBlockFamilyCatalog.MaterialDisplayName(material);
                return _pieceMaterialSourceIssues.TryGetValue(material, out string reason) &&
                       !string.IsNullOrWhiteSpace(reason)
                    ? label + ": " + reason
                    : label + " blocks are unavailable.";
            }

            return null;
        }

        private void DrawToolbar(int id)
        {
            EsuHudNotifications.SetActiveSource("Smart Builder");
            EsuHudChrome.DrawPanel(new Rect(0f, 0f, _toolbarRect.width, _toolbarRect.height));
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_toolbarRect.width, _toolbarRect.height);
            EsuHudLayout.CenteredToolbarFrame frame =
                EsuHudLayout.CalculateCenteredToolbarFrame(inner);
            EsuHudLayout.ToolbarBudget budget = frame.Budget;
            float leftControlWidth = EsuHudLayout.ToolbarControlWidth(
                budget.LeftRailWidth,
                11,
                54f);
            float expandedRightControlWidth = EsuHudLayout.ToolbarControlWidth(
                budget.RightControlsWidth,
                7,
                54f);
            bool compressed =
                leftControlWidth < EsuHudLayout.Scale(48f) ||
                expandedRightControlWidth < EsuHudLayout.Scale(48f);
            int rightControlCount = compressed ? 5 : 7;
            float rightControlWidth = compressed
                ? EsuHudLayout.ToolbarControlWidth(
                    budget.RightControlsWidth,
                    rightControlCount,
                    54f)
                : expandedRightControlWidth;

            GUILayout.BeginArea(frame.Rect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth));
            {
                ModeSwitchButton(leftControlWidth);
                ToolButton(SmartBuildTool.Draw, "create", "Add", "+", "Click the focused construct grid to add the selected shape as a new preview piece.", leftControlWidth);
                ToolButton(SmartBuildTool.Move, "move", "Move", "M", "Drag RGB handles to move the preview by " + EsuTransformSnapSettings.SmartMoveStepCells + " cell step(s).", leftControlWidth);
                ToolButton(SmartBuildTool.Rotate, "rotate", "Rotate", "R", "Drag RGB rotation rings around the preview center.", leftControlWidth);
                ToolButton(SmartBuildTool.Scale, "scale", "Scale", "S", "Drag RGB handles to resize the preview by " + EsuTransformSnapSettings.SmartScaleStepCells + " cell step(s). Hold Shift on down-slopes to keep the first slope cell anchored and stretch along the cardinal run.", leftControlWidth);
                PlaneButton(leftControlWidth);
                OccupancyButton(leftControlWidth);
                ViewButton(leftControlWidth);
                SymmetryButton(DecorationEditAxis.X, leftControlWidth);
                SymmetryButton(DecorationEditAxis.Y, leftControlWidth);
                SymmetryButton(DecorationEditAxis.Z, leftControlWidth);
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
            ToolbarPanelToggle("settings", "Info", "I", ref _showLeftPanel, "Show or hide Smart Builder overview, scene, and selected-piece controls.", rightControlWidth);
            ToolbarPanelToggle("build", "Library", "Lib", ref _showRightPanel, "Show or hide the Smart Builder shape and generator library.", rightControlWidth);
            if (ToolbarIconButton(
                    "undo",
                    "Undo",
                    "Z",
                    DecorationEditorTheme.Button,
                    $"Ctrl+Z: undo the last Smart Builder preview edit ({_sceneUndo.Count} available).",
                    rightControlWidth,
                    _sceneUndo.Count > 0))
                UndoSceneEdit();
            if (ToolbarIconButton(
                    "redo",
                    "Redo",
                    "Y",
                    DecorationEditorTheme.Button,
                    $"Ctrl+Y/Ctrl+Shift+Z: redo the last Smart Builder preview edit ({_sceneRedo.Count} available).",
                    rightControlWidth,
                    _sceneRedo.Count > 0))
                RedoSceneEdit();
            if (!compressed)
            {
                if (ToolbarAttentionIconButton("save", "Apply", "OK", DecorationEditorTheme.Button, "Place the planned blocks.", rightControlWidth, !_planDirty && _plan != null && _plan.CanCommit))
                    ApplyPreview();
                if (ToolbarAttentionIconButton("cancel", "Cancel", "Clr", DecorationEditorTheme.Button, "Clear the runtime preview.", rightControlWidth, HasActivePreviewScene))
                    CancelPreview();
            }
            if (ToolbarIconButton("close", "Close", "X", DecorationEditorTheme.Button, "Close Smart Block Builder.", rightControlWidth))
                _closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ToolbarPanelToggle(
            string icon,
            string label,
            string compactLabel,
            ref bool state,
            string tooltip,
            float controlWidth)
        {
            if (ToolbarIconButton(
                    icon,
                    label,
                    compactLabel,
                    DecorationEditorTheme.ToolButton(state),
                    tooltip,
                    controlWidth))
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
            if (SmartGUIButton(
                    rect,
                    new GUIContent(label, "Switch Smart Builder view to " + ViewModeDisplayName(mode) + "."),
                    DecorationEditorTheme.ToolButton(_viewMode == mode)))
            {
                SelectViewMode(mode);
            }
        }

        private void SelectViewMode(DecorationEditorViewMode mode)
        {
            if (_viewMode == mode)
            {
                _viewModeMenuOpen = false;
                return;
            }
            _viewMode = mode;
            RegisterPresentationRevision();
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

            if (_scene == null || !_scene.IsSelected(_contextMenuPieceId))
            {
                _contextMenuOpen = false;
                return;
            }

            _scene.AddToSelection(_contextMenuPieceId);
            _draft = _scene.SelectedPiece;
            int selectionCount = Math.Max(1, _scene.SelectionCount);
            bool group = selectionCount > 1;
            GUI.Box(_contextMenuRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(_contextMenuRect, 5f));
            GUILayout.Label(
                group
                    ? selectionCount.ToString(CultureInfo.InvariantCulture) + " preview pieces"
                    : "Preview piece",
                DecorationEditorTheme.SubHeader);
            if (SmartGUILayoutButton(new GUIContent("Move", group ? "Move the selected preview group." : "Move this preview piece."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _contextMenuOpen = false;
                _tool = SmartBuildTool.Move;
            }

            if (SmartGUILayoutButton(new GUIContent("Duplicate", group ? "Duplicate the selected preview group one cell to the right." : "Duplicate this preview piece one cell to the right."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DuplicateSelectedPiece();
                _contextMenuOpen = false;
            }

            if (SmartGUILayoutButton(new GUIContent("Delete", group ? "Delete the selected preview group." : "Delete this preview piece."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DeleteSelectedPiece();
                _contextMenuOpen = false;
            }

            if (SmartGUILayoutButton(new GUIContent(SmartYawLabel(), group ? "Rotate the selected preview group around its primary piece." : "Rotate this preview piece around construct Y."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                YawSelectedPiece();
                _contextMenuOpen = false;
            }

            bool canFlip = CanFlipSelection();
            bool previous = GUI.enabled;
            GUI.enabled = previous && canFlip;
            if (SmartGUILayoutButton(new GUIContent("Flip", "Reverse this shape's forward direction."), canFlip ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton, GUILayout.Height(EsuHudLayout.Scale(24f))))
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
                    (_scene?.SelectionCount ?? 0) > 1
                        ? "Rotate the selected Smart Builder group around its primary piece."
                        : "Rotate the selected Smart Builder piece around vertical.",
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
                    (_scene?.SelectionCount ?? 0) > 1
                        ? "Reverse every compatible shape in the selected group."
                        : "Reverse the selected shape's forward direction.",
                    CanFlipSelection()))
            {
                FlipSelectedPiece();
            }
        }

        private void YawSelectedPiece()
        {
            if (_draft == null || _scene == null)
                return;

            RecordSceneHistory();
            if (_scene.SelectionCount <= 1)
            {
                RotateYaw(_draft);
            }
            else
            {
                RotateSelectionAroundAxis(
                    DecorationEditAxis.Y,
                    EsuTransformSnapSettings.SmartRotateQuarterTurns,
                    _draft.RotationPivotCell);
            }
            RebuildPlan();
        }

        private static void RotateYaw(SmartBuildPiece piece)
        {
            piece?.RotateAroundAxis(
                DecorationEditAxis.Y,
                EsuTransformSnapSettings.SmartRotateQuarterTurns);
        }

        private void RotateSelectionAroundAxis(
            DecorationEditAxis axis,
            int quarterTurns,
            Vector3i pivot)
        {
            foreach (SmartBuildPiece piece in _scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>())
                piece.RotateAroundAxis(axis, quarterTurns, pivot);
        }

        private void FlipSelectedPiece()
        {
            if (!CanFlipSelection())
                return;

            RecordSceneHistory();
            foreach (SmartBuildPiece piece in _scene.SelectedPieces)
                piece.FlipForward();
            RebuildPlan();
        }

        private bool CanFlipSelection()
        {
            IReadOnlyList<SmartBuildPiece> selected = _scene?.SelectedPieces;
            return selected != null &&
                   selected.Count > 0 &&
                   selected.All(
                       piece => piece.ShapeKind == SmartBuildShapeKind.DownSlope ||
                                piece.IsFixedGeometry ||
                                piece.IsGeneratedShape);
        }

        private void DuplicateSelectedPiece()
        {
            if (_scene == null || _draft == null)
                return;

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryDuplicateSelection(
                    new Vector3i(1, 0, 0),
                    out IReadOnlyList<SmartBuildPiece> duplicates,
                    out string reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    InfoStore.Add(reason);
                return;
            }

            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _tool = SmartBuildTool.Move;
            RebuildPlan();
            InfoStore.Add(
                duplicates.Count == 1
                    ? "Duplicated Smart Builder piece #" + duplicates[0].Id.ToString(CultureInfo.InvariantCulture) + "."
                    : "Duplicated " + duplicates.Count.ToString(CultureInfo.InvariantCulture) +
                      " Smart Builder pieces as one group.");
        }

        private void DeleteSelectedPiece()
        {
            if (_scene == null || _draft == null)
                return;

            int primaryId = _draft.Id;
            int requested = Math.Max(1, _scene.SelectionCount);
            RecordSceneHistory();
            int deleted = _scene.DeleteSelection();
            if (deleted <= 0)
                return;

            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
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

            InfoStore.Add(
                requested == 1
                    ? "Deleted Smart Builder piece #" + primaryId.ToString(CultureInfo.InvariantCulture) + "."
                    : "Deleted " + deleted.ToString(CultureInfo.InvariantCulture) +
                      " Smart Builder pieces as one group.");
        }

        private void ModeSwitchButton(float controlWidth)
        {
            if (ToolbarIconButton(
                    "build",
                    "Deco",
                    "D",
                    DecorationEditorTheme.ToolButton(true),
                    "Tab: switch to Decoration Edit when Smart Builder is clean.",
                    controlWidth))
                SwitchToDecorationEditRequested = true;
        }

        private void DrawLeftPanel(int id)
        {
            EsuHudChrome.DrawPanel(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height));
            float inset = EsuHudLayout.Scale(8f);
            float gap = SmartStackDividerGap();
            Rect inner = new Rect(
                inset,
                inset,
                Mathf.Max(1f, _leftPanelRect.width - inset * 2f),
                Mathf.Max(1f, _leftPanelRect.height - inset * 2f));
            float footerHeight = Mathf.Min(
                EsuHudLayout.Scale(50f),
                inner.height);
            Rect footerRect = new Rect(
                inner.x,
                inner.yMax - footerHeight,
                inner.width,
                footerHeight);
            Rect workspaceRect = new Rect(
                inner.x,
                inner.y,
                inner.width,
                Mathf.Max(0f, footerRect.y - inner.y - gap));

            SplitSmartVerticalStack(
                workspaceRect,
                true,
                true,
                _leftWorkspaceBottomRatio,
                gap,
                SmartLeftOverviewMinimumHeight(),
                out Rect overviewRect,
                out Rect workspaceDividerRect,
                out Rect sceneSelectedRect,
                out float resolvedWorkspaceBottomRatio);
            if (workspaceDividerRect.height > 0f)
            {
                if (HandleSmartStackDividerDrag(
                    SmartShapeStackDividerKind.LeftWorkspace,
                    workspaceRect,
                    workspaceDividerRect,
                    gap,
                    SmartLeftOverviewMinimumHeight(),
                    ref resolvedWorkspaceBottomRatio))
                {
                    _leftWorkspaceBottomRatio = resolvedWorkspaceBottomRatio;
                    SplitSmartVerticalStack(
                        workspaceRect,
                        true,
                        true,
                        _leftWorkspaceBottomRatio,
                        gap,
                        SmartLeftOverviewMinimumHeight(),
                        out overviewRect,
                        out workspaceDividerRect,
                        out sceneSelectedRect,
                        out resolvedWorkspaceBottomRatio);
                }
            }
            else
            {
                ClearSmartStackDividerDrag(SmartShapeStackDividerKind.LeftWorkspace);
            }

            // Keep the resolved ratio local. Temporary screen/HUD constraints may
            // clamp it without replacing the user's preferred divider position.

            DrawLeftOverviewSection(overviewRect);
            DrawLeftSceneSelectedStack(sceneSelectedRect);
            if (workspaceDividerRect.height > 0f)
            {
                DrawSmartStackDividerGrip(
                    workspaceDividerRect,
                    SmartShapeStackDividerKind.LeftWorkspace);
            }

            GUILayout.BeginArea(footerRect);
            DrawLeftPanelApplyCancelButtons();
            GUILayout.Space(EsuHudLayout.Scale(3f));
            GUILayout.Label(
                LeftPanelFooterStatus(),
                DecorationEditorTheme.Mini,
                GUILayout.Height(EsuHudLayout.Scale(16f)));
            GUILayout.EndArea();
            if (GUI.enabled)
                GUI.DragWindow(new Rect(0f, 0f, _leftPanelRect.width, EsuHudLayout.Scale(32f)));
        }

        private void DrawLeftOverviewSection(Rect rect)
        {
            GUILayout.BeginArea(rect);
            DrawSmartPanelHeader(
                "Smart Block Builder",
                "build",
                ref _showLeftPanel,
                "Hide Smart Builder overview, scene, and selected-piece controls.");
            DecorationEditorTheme.Separator();
            _overviewScroll = GUILayout.BeginScrollView(
                _overviewScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(
                    1f,
                    rect.height - EsuHudLayout.Scale(36f))));
            if (DrawSmartSectionHeader("Material", ref _showMaterialSection, "Show or hide the Smart Builder material selector."))
            {
                DrawMaterialSelector();
                if (!string.IsNullOrWhiteSpace(_sourceReason))
                    LabelRow("Blocks", _sourceReason);
            }
            if (SmartGUILayoutButton(
                    new GUIContent(
                        _brushEnabled ? "Brush: on" : "Brush: off",
                        "When enabled, hold left mouse in Draw mode to paint a continuous sequence of preview pieces as one undoable edit."),
                    DecorationEditorTheme.ToolButton(_brushEnabled),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _brushEnabled = !_brushEnabled;
                if (_brushEnabled)
                    ArmAddMode();
            }
            LabelRow("Tool", ToolDisplayName());
            LabelRow("Handles", _editHandleMode.ToString());
            LabelRow("Plane", _drawPlane.ToString());
            LabelRow("Operation", OccupancyDisplayName(_occupancyMode));
            LabelRow("Symmetry", DecoLimitLifter.EsuSymmetry.FormatSummary());
            GUILayout.Space(EsuHudLayout.Scale(3f));
            GUILayout.Label(
                _draft == null
                    ? "Pick a shape on the right, then click the focused construct grid."
                    : (_scene?.Count ?? 0).ToString("N0", CultureInfo.InvariantCulture) +
                      " preview piece(s) | selected #" +
                      _draft.Id.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static float SmartLeftOverviewMinimumHeight() =>
            EsuHudLayout.Scale(80f);

        private void DrawLeftSceneSelectedStack(Rect stackRect)
        {
            SplitSmartCollapsibleStack(
                stackRect,
                _showSceneSection,
                _showSelectedSection,
                _selectedSceneStackBottomRatio,
                SmartStackDividerGap(),
                SmartStackMinimumPanelHeight(),
                SmartSectionShelfHeight(),
                out Rect sceneRect,
                out Rect dividerRect,
                out Rect selectedRect,
                out float resolvedSelectedBottomRatio);
            if (dividerRect.height > 0f)
            {
                if (HandleSmartStackDividerDrag(
                    SmartShapeStackDividerKind.SelectedScene,
                    stackRect,
                    dividerRect,
                    SmartStackDividerGap(),
                    SmartStackMinimumPanelHeight(),
                    ref resolvedSelectedBottomRatio))
                {
                    _selectedSceneStackBottomRatio = resolvedSelectedBottomRatio;
                    SplitSmartCollapsibleStack(
                        stackRect,
                        _showSceneSection,
                        _showSelectedSection,
                        _selectedSceneStackBottomRatio,
                        SmartStackDividerGap(),
                        SmartStackMinimumPanelHeight(),
                        SmartSectionShelfHeight(),
                        out sceneRect,
                        out dividerRect,
                        out selectedRect,
                        out resolvedSelectedBottomRatio);
                }
            }
            else
            {
                ClearSmartStackDividerDrag(SmartShapeStackDividerKind.SelectedScene);
            }

            // Keep the resolved ratio local. The preference changes only when the
            // user actually drags this divider or resets the HUD layout.

            DrawSceneSection(sceneRect);
            DrawSelectedPieceSection(selectedRect);
            if (dividerRect.height > 0f)
            {
                DrawSmartStackDividerGrip(
                    dividerRect,
                    SmartShapeStackDividerKind.SelectedScene);
            }
        }

        private string LeftPanelFooterStatus()
        {
            int pieceCount = _scene?.Count ?? 0;
            if (pieceCount == 0)
                return "Preview empty";
            if (_planDirty)
                return pieceCount.ToString("N0", CultureInfo.InvariantCulture) + " piece(s) | Updating";
            return pieceCount.ToString("N0", CultureInfo.InvariantCulture) +
                   (_plan?.CanCommit == true ? " piece(s) | Ready" : " piece(s) | Blocked");
        }

        private void DrawLeftPanelApplyCancelButtons()
        {
            GUILayout.BeginHorizontal();
            bool canApply = !_planDirty && _plan != null && _plan.CanCommit;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canApply;
            if (SmartGUILayoutButton(
                    new GUIContent("Apply", "Place the planned blocks."),
                    canApply ? DecorationEditorTheme.ToolButton(false) : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
            {
                ApplyPreview();
            }

            bool canCancel = HasActivePreviewScene;
            GUI.enabled = previous && canCancel;
            if (SmartGUILayoutButton(
                    new GUIContent("Cancel", "Clear the full Smart Builder preview scene."),
                    canCancel ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
            {
                CancelPreview();
            }

            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawShapePanel(int id)
        {
            EsuHudChrome.DrawPanel(new Rect(0f, 0f, _rightPanelRect.width, _rightPanelRect.height));
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, _rightPanelRect.width - inset * 2f, _rightPanelRect.height - inset * 2f);
            float headerHeight = EsuHudLayout.Scale(24f);
            GUILayout.BeginArea(new Rect(inner.x, inner.y, inner.width, headerHeight));
            DrawSmartPanelHeader(
                "Library",
                "build",
                ref _showRightPanel,
                "Hide the Smart Builder shape and generator library.");
            GUILayout.EndArea();
            float y = inner.y + headerHeight + EsuHudLayout.Scale(4f);
            float pageTabsHeight = EsuHudLayout.Scale(28f);
            Rect pageTabs = new Rect(inner.x, y, inner.width, pageTabsHeight);
            DrawRightPanelPageTabs(pageTabs);
            y += pageTabsHeight + EsuHudLayout.Scale(4f);
            Rect stackRect = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));
            DrawShapePanelStack(stackRect);
            if (GUI.enabled)
                GUI.DragWindow(new Rect(0f, 0f, _rightPanelRect.width, headerHeight + inset));
        }

        private void DrawRightPanelPageTabs(Rect rect)
        {
            float gap = EsuHudLayout.Scale(4f);
            float width = (rect.width - gap) * 0.5f;
            Rect shapes = new Rect(rect.x, rect.y, width, rect.height);
            Rect generators = new Rect(shapes.xMax + gap, rect.y, width, rect.height);
            DrawRightPanelPageTab(
                shapes,
                SmartRightPanelPage.Shapes,
                "Shapes",
                "Browse structural Smart Builder shapes.");
            DrawRightPanelPageTab(
                generators,
                SmartRightPanelPage.Generators,
                "Generators",
                "Browse procedural block generators.");
        }

        private void DrawRightPanelPageTab(
            Rect rect,
            SmartRightPanelPage page,
            string label,
            string tooltip)
        {
            if (SmartGUIButton(
                    rect,
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(_rightPanelPage == page)))
            {
                _rightPanelPage = page;
                _hoveredShapeEntry = default;
            }
        }

        private void DrawShapePanelStack(Rect stackRect)
        {
            if (_rightPanelPage == SmartRightPanelPage.Generators)
                DrawGeneratorBrowserSection(stackRect, AvailableGeneratorDescriptors());
            else
                DrawShapePaletteSection(stackRect);
        }

        private void DrawShapePaletteSection(Rect rect)
        {
            GUILayout.BeginArea(rect);
            if (DrawSmartSectionHeader("Shapes", ref _showShapePaletteSection, "Show or hide the shape library."))
                DrawShapePalette(Mathf.Max(0f, rect.height - EsuHudLayout.Scale(28f)));
            GUILayout.EndArea();
        }

        private void DrawGeneratorBrowserSection(
            Rect rect,
            IReadOnlyList<SmartBuildShapeDescriptor> descriptors)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Label("Generators", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            float height = Mathf.Max(0f, rect.height - EsuHudLayout.Scale(30f));
            _generatorPaletteScroll = GUILayout.BeginScrollView(
                _generatorPaletteScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(height));
            DrawGeneratorToolGrid(descriptors, Mathf.Max(1f, rect.width - EsuHudLayout.Scale(18f)));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSelectedPieceSection(Rect rect)
        {
            GUILayout.BeginArea(rect);
            if (DrawSmartSectionHeader("Selected", ref _showSelectedSection, "Show or hide selected-piece actions."))
            {
                _selectedPieceScroll = GUILayout.BeginScrollView(
                    _selectedPieceScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Height(Mathf.Max(0f, rect.height - EsuHudLayout.Scale(32f))));
                DrawSelectedPieceActions();
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawSceneSection(Rect rect)
        {
            GUILayout.BeginArea(rect);
            if (DrawSmartSectionHeader("Scene", ref _showSceneSection, "Show or hide the Smart Builder scene list."))
            {
                _sceneListScroll = GUILayout.BeginScrollView(
                    _sceneListScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Height(Mathf.Max(0f, rect.height - EsuHudLayout.Scale(32f))));
                DrawSmartScenePresetControls();
                DecorationEditorTheme.Separator();
                if (_scene == null || _scene.Count == 0)
                {
                    GUILayout.Label("No pieces yet", DecorationEditorTheme.MiniWrap);
                }
                else
                {
                    DrawBulkSceneSelectionControls();
                    DecorationEditorTheme.Separator();
                    DrawVirtualizedSceneRows(Mathf.Max(0f, rect.height - EsuHudLayout.Scale(32f)));
                }

                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawVirtualizedSceneRows(float viewportHeight)
        {
            IReadOnlyList<SmartBuildPiece> pieces = _scene?.Pieces ??
                Array.Empty<SmartBuildPiece>();
            if (pieces.Count == 0)
                return;

            float rowHeight = EsuHudLayout.Scale(28f);
            const int overscanRows = 16;
            // The scroll also contains the preset and bulk-selection controls. A
            // generous overscan absorbs that prefix while still bounding IMGUI work.
            int first = Mathf.Clamp(
                Mathf.FloorToInt(_sceneListScroll.y / Mathf.Max(1f, rowHeight)) - overscanRows,
                0,
                pieces.Count - 1);
            int visible = Mathf.CeilToInt(viewportHeight / Mathf.Max(1f, rowHeight)) +
                          overscanRows * 2;
            int end = Math.Min(pieces.Count, first + Math.Max(1, visible));
            if (first > 0)
                GUILayout.Space(rowHeight * first);
            for (int index = first; index < end; index++)
                DrawPieceRow(pieces[index]);
            if (end < pieces.Count)
                GUILayout.Space(rowHeight * (pieces.Count - end));
        }

        private void DrawBulkSceneSelectionControls()
        {
            GUILayout.Label("Bulk selection", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("All", "Select every Smart Builder preview piece."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BulkSelectAllPieces();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("None", "Clear the Smart Builder preview selection without deleting pieces."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BulkClearPieceSelection();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Invert", "Invert the Smart Builder preview selection."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BulkInvertPieceSelection();
            }
            GUILayout.EndHorizontal();

            bool hasPrimary = _scene?.SelectedPiece != null;
            bool previous = GUI.enabled;
            GUI.enabled = previous && hasPrimary;
            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Same shape", "Select every preview piece with the primary piece's exact structural shape, dimensions, and fill variant."),
                    hasPrimary ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BulkSelectPiecesByPrimaryShape();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Same material", "Select every preview piece with the primary piece's material."),
                    hasPrimary ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BulkSelectPiecesByPrimaryMaterial();
            }
            GUILayout.EndHorizontal();
            GUI.enabled = previous;
        }

        private void BulkSelectAllPieces()
        {
            if (_scene == null || _scene.Count == 0)
                return;

            int primaryId = _scene.SelectedPiece?.Id ?? _scene.Pieces[_scene.Count - 1].Id;
            SetBulkSceneSelection(_scene.Pieces.Select(piece => piece.Id), primaryId);
        }

        private void BulkClearPieceSelection()
        {
            if (_scene == null)
                return;

            _scene.ClearSelection();
            _draft = null;
            _sceneSelectionAnchorId = -1;
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add("Smart Builder preview selection cleared.");
        }

        private void BulkInvertPieceSelection()
        {
            if (_scene == null || _scene.Count == 0)
                return;

            int[] inverted = _scene.Pieces
                .Where(piece => !_scene.IsSelected(piece.Id))
                .Select(piece => piece.Id)
                .ToArray();
            int primaryId = inverted.Length > 0 ? inverted[inverted.Length - 1] : -1;
            SetBulkSceneSelection(inverted, primaryId);
        }

        private void BulkSelectPiecesByPrimaryShape()
        {
            SmartBuildPiece primary = _scene?.SelectedPiece;
            if (primary == null)
                return;

            SetBulkSceneSelection(
                _scene.Pieces
                    .Where(piece => SameExactSmartPieceShape(piece, primary))
                    .Select(piece => piece.Id),
                primary.Id);
        }

        /// <summary>
        /// Shape matching deliberately excludes position, orientation, draw plane, and
        /// material while including every setting that changes occupied geometry.
        /// </summary>
        internal static bool SameExactSmartPieceShape(
            SmartBuildPiece first,
            SmartBuildPiece second) =>
            first != null &&
            second != null &&
            string.Equals(
                ExactSmartPieceShapeSignature(first),
                ExactSmartPieceShapeSignature(second),
                StringComparison.Ordinal);

        internal static string ExactSmartPieceShapeSignature(SmartBuildPiece piece)
        {
            if (piece == null)
                return string.Empty;

            Vector3i size = piece.PresetCuboidSize;
            return string.Join(
                "|",
                new[]
                {
                    (piece.ShapeDescriptorKey ?? string.Empty).Trim().ToLowerInvariant(),
                    ((int)piece.ShapeKind).ToString(CultureInfo.InvariantCulture),
                    size.x.ToString(CultureInfo.InvariantCulture),
                    size.y.ToString(CultureInfo.InvariantCulture),
                    size.z.ToString(CultureInfo.InvariantCulture),
                    piece.CuboidHollow ? "hollow" : "solid",
                    piece.SlopeLength.ToString(CultureInfo.InvariantCulture),
                    piece.SlopeSteps.ToString(CultureInfo.InvariantCulture),
                    piece.SlopeWidth.ToString(CultureInfo.InvariantCulture),
                    ((int)piece.SupportMode).ToString(CultureInfo.InvariantCulture),
                    piece.SelectedLength.ToString(CultureInfo.InvariantCulture),
                    piece.FixedForwardCells.ToString(CultureInfo.InvariantCulture),
                    piece.FixedForwardTiles.ToString(CultureInfo.InvariantCulture),
                    piece.FixedRightTiles.ToString(CultureInfo.InvariantCulture),
                    piece.FixedDropTiles.ToString(CultureInfo.InvariantCulture),
                    piece.GeneratorSides.ToString(CultureInfo.InvariantCulture),
                    ((int)piece.GeneratorFillMode).ToString(CultureInfo.InvariantCulture),
                    ((int)piece.GeneratorSmoothingMode).ToString(CultureInfo.InvariantCulture),
                    piece.GeneratorRoundLock ? "round" : "free",
                    piece.GeneratorArcDegrees.ToString(CultureInfo.InvariantCulture),
                    piece.GeneratorTopScalePercent.ToString(CultureInfo.InvariantCulture)
                });
        }

        private void BulkSelectPiecesByPrimaryMaterial()
        {
            SmartBuildPiece primary = _scene?.SelectedPiece;
            if (primary == null)
                return;

            SetBulkSceneSelection(
                _scene.Pieces
                    .Where(piece => SameSmartPieceMaterial(
                        piece,
                        primary,
                        MaterialForPiece))
                    .Select(piece => piece.Id),
                primary.Id);
        }

        internal static bool SameSmartPieceMaterial(
            SmartBuildPiece first,
            SmartBuildPiece second,
            Func<SmartBuildPiece, SmartBuildMaterial> materialForPiece) =>
            first != null &&
            second != null &&
            materialForPiece != null &&
            materialForPiece(first) == materialForPiece(second);

        private void SetBulkSceneSelection(IEnumerable<int> ids, int primaryId)
        {
            if (_scene == null)
                return;

            _scene.SetSelection(ids, primaryId);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            InfoStore.Add(SelectionStatusMessage());
        }

        private void DrawSmartScenePresetControls()
        {
            if (!_smartScenePresetsLoaded)
                RefreshSmartScenePresetList();

            GUILayout.Label("Scene presets", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            _smartScenePresetName = GUILayout.TextField(
                _smartScenePresetName ?? string.Empty,
                GUILayout.MinWidth(EsuHudLayout.Scale(88f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            bool previous = GUI.enabled;
            GUI.enabled = previous && HasActivePreviewScene;
            if (SmartGUILayoutButton(
                    new GUIContent("Save", "Save or update this named Smart Builder scene."),
                    HasActivePreviewScene ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(52f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SaveNamedSmartScenePreset();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            DrawSmartScenePresetTargetControls();

            EsuPresetEntry selected = SelectedSmartScenePreset();
            GUILayout.BeginHorizontal();
            GUI.enabled = previous && _smartScenePresets.Count > 1;
            if (SmartGUILayoutButton(
                    new GUIContent("<", "Previous saved Smart Builder scene."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                CycleSmartScenePreset(-1);
                selected = SelectedSmartScenePreset();
            }
            GUI.enabled = previous;
            GUILayout.Label(
                selected?.Name ?? "No saved scenes",
                DecorationEditorTheme.MiniWrap,
                GUILayout.MinWidth(EsuHudLayout.Scale(80f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            GUI.enabled = previous && _smartScenePresets.Count > 1;
            if (SmartGUILayoutButton(
                    new GUIContent(">", "Next saved Smart Builder scene."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                CycleSmartScenePreset(1);
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = previous && selected != null;
            if (SmartGUILayoutButton(
                    new GUIContent("Load", "Replace the preview with this saved scene at the chosen target, or at its saved coordinates when no target is set. This can be undone."),
                    selected != null ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                LoadNamedSmartScenePreset();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Delete", "Delete this named Smart Builder scene preset."),
                    selected != null ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                DeleteNamedSmartScenePreset();
            }
            GUI.enabled = previous && _smartSceneRecoveryAvailable;
            if (SmartGUILayoutButton(
                    new GUIContent("Restore", "Restore the latest autosaved Smart Builder recovery scene."),
                    _smartSceneRecoveryAvailable ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                RestoreSmartSceneRecovery();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_smartScenePresetStatus))
                GUILayout.Label(_smartScenePresetStatus, DecorationEditorTheme.MiniWrap);
        }

        private void DrawSmartScenePresetTargetControls()
        {
            string targetLabel = _smartScenePresetTargetAnchor.HasValue
                ? "Target " + FormatCell(_smartScenePresetTargetAnchor.Value)
                : "Target: saved position";
            GUILayout.Label(targetLabel, DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            bool previous = GUI.enabled;
            GUI.enabled = previous && _draft?.Construct != null;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Use primary",
                        "Relocate the loaded named scene so its preset anchor lands on the selected primary preview piece."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                _smartScenePresetTargetConstruct = _draft.Construct;
                _smartScenePresetTargetAnchor = _draft.Origin;
                _smartScenePresetTargetArmed = false;
                InfoStore.Add("Smart Builder preset target set to the primary preview piece.");
            }
            GUI.enabled = previous;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        _smartScenePresetTargetArmed ? "Picking..." : "Pick block",
                        "Arm a world pick, then click a craft block to use it as the named-scene placement anchor."),
                    DecorationEditorTheme.ToolButton(_smartScenePresetTargetArmed),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                _smartScenePresetTargetArmed = !_smartScenePresetTargetArmed;
                if (_smartScenePresetTargetArmed)
                {
                    _blockEyedropperArmed = false;
                    InfoStore.Add("Click a craft block to set the Smart Builder preset target; right-click or Escape cancels.");
                }
            }
            GUI.enabled = previous && _smartScenePresetTargetAnchor.HasValue;
            if (SmartGUILayoutButton(
                    new GUIContent("Saved", "Load named scenes at their saved local coordinates."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(52f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                _smartScenePresetTargetConstruct = null;
                _smartScenePresetTargetAnchor = null;
                _smartScenePresetTargetArmed = false;
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private bool TryCaptureSmartScenePresetTargetFromPointer()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) || hit?.Construct == null)
            {
                InfoStore.Add("Point at a craft block for the Smart Builder preset target.");
                return false;
            }

            _smartScenePresetTargetConstruct = hit.Construct;
            _smartScenePresetTargetAnchor = hit.Anchor;
            _smartScenePresetTargetArmed = false;
            InfoStore.Add("Smart Builder preset target set to " + FormatCell(hit.Anchor) + ".");
            return true;
        }

        private EsuPresetEntry SelectedSmartScenePreset()
        {
            if (_smartScenePresets == null || _smartScenePresets.Count == 0)
                return null;

            _selectedSmartScenePresetIndex = Mathf.Clamp(
                _selectedSmartScenePresetIndex,
                0,
                _smartScenePresets.Count - 1);
            return _smartScenePresets[_selectedSmartScenePresetIndex];
        }

        private void CycleSmartScenePreset(int direction)
        {
            if (_smartScenePresets == null || _smartScenePresets.Count == 0)
                return;

            _selectedSmartScenePresetIndex =
                (_selectedSmartScenePresetIndex + direction) % _smartScenePresets.Count;
            if (_selectedSmartScenePresetIndex < 0)
                _selectedSmartScenePresetIndex += _smartScenePresets.Count;
            EsuPresetEntry selected = SelectedSmartScenePreset();
            if (selected != null)
                _smartScenePresetName = selected.Name;
        }

        private void RefreshSmartScenePresetList(string preferredId = null)
        {
            _smartScenePresetsLoaded = true;
            if (!EsuPresetLibrary.Default.TryList(
                    out IReadOnlyList<EsuPresetEntry> entries,
                    out string message))
            {
                _smartScenePresets = Array.Empty<EsuPresetEntry>();
                _selectedSmartScenePresetIndex = -1;
                _smartScenePresetStatus = message;
                return;
            }

            _smartScenePresets = entries
                .Where(entry => entry != null && entry.Kind == EsuPresetKind.SmartScene)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (_smartScenePresets.Count == 0)
            {
                _selectedSmartScenePresetIndex = -1;
                return;
            }

            int preferred = string.IsNullOrWhiteSpace(preferredId)
                ? -1
                : _smartScenePresets
                    .Select((entry, index) => new { entry, index })
                    .Where(pair => string.Equals(pair.entry.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.index)
                    .DefaultIfEmpty(-1)
                    .First();
            _selectedSmartScenePresetIndex = preferred >= 0
                ? preferred
                : Mathf.Clamp(_selectedSmartScenePresetIndex, 0, _smartScenePresets.Count - 1);
        }

        private void SaveNamedSmartScenePreset()
        {
            if (!TryCaptureSmartScenePayload(out SmartBuildScenePresetPayload payload, out string reason))
            {
                _smartScenePresetStatus = reason;
                InfoStore.Add(reason);
                return;
            }

            if (!EsuPresetLibrary.Default.TrySave(
                    _smartScenePresetName,
                    EsuPresetKind.SmartScene,
                    payload,
                    overwrite: true,
                    out EsuPresetEntry saved,
                    out string message,
                    description: "Portable Smart Builder preview scene.",
                    tags: new[] { "smart-builder", "scene" }))
            {
                _smartScenePresetStatus = message;
                InfoStore.Add(message);
                return;
            }

            _smartScenePresetName = saved.Name;
            _smartScenePresetStatus = message;
            RefreshSmartScenePresetList(saved.Id);
            InfoStore.Add(message);
        }

        private void LoadNamedSmartScenePreset()
        {
            EsuPresetEntry selected = SelectedSmartScenePreset();
            if (selected == null)
                return;

            if (!EsuPresetLibrary.Default.TryRead(
                    selected.Id,
                    out SmartBuildScenePresetPayload payload,
                    out _,
                    out string message))
            {
                _smartScenePresetStatus = message;
                InfoStore.Add(message);
                return;
            }

            LoadSmartScenePayload(
                payload,
                "Loaded Smart Builder scene '" + selected.Name + "'.",
                relocateToChosenTarget: true);
        }

        private void DeleteNamedSmartScenePreset()
        {
            EsuPresetEntry selected = SelectedSmartScenePreset();
            if (selected == null)
                return;

            if (!EsuPresetLibrary.Default.TryDelete(selected.Id, out string message))
            {
                _smartScenePresetStatus = message;
                InfoStore.Add(message);
                return;
            }

            _smartScenePresetStatus = message;
            RefreshSmartScenePresetList();
            InfoStore.Add(message);
        }

        private void RestoreSmartSceneRecovery()
        {
            if (!TryReadSmartSceneRecovery(
                    out SmartBuildScenePresetPayload payload,
                    out string message))
            {
                _smartScenePresetStatus = message;
                _smartSceneRecoveryAvailable = false;
                InfoStore.Add(message);
                return;
            }

            LoadSmartScenePayload(
                payload,
                "Restored the autosaved Smart Builder scene.",
                relocateToChosenTarget: false);
        }

        private void LoadSmartScenePayload(
            SmartBuildScenePresetPayload payload,
            string successMessage,
            bool relocateToChosenTarget)
        {
            bool relocate = relocateToChosenTarget &&
                            _smartScenePresetTargetConstruct != null &&
                            _smartScenePresetTargetAnchor.HasValue;
            AllConstruct construct = relocate
                ? _smartScenePresetTargetConstruct
                : FocusedConstruct();
            Vector3i? anchorOverride = relocate
                ? _smartScenePresetTargetAnchor
                : null;
            string reason = null;
            if (payload == null ||
                !payload.TryRestoreScene(
                    construct,
                    anchorOverride,
                    out SmartBuildPieceScene restoredScene,
                    out IReadOnlyList<int> selectedIds,
                    out int primaryId,
                    out reason))
            {
                _smartScenePresetStatus = reason ?? "Smart Builder scene payload is empty.";
                InfoStore.Add(_smartScenePresetStatus);
                return;
            }

            RecordSceneHistory();
            _selectedMaterial = payload.ActiveMaterial;
            s_selectedMaterial = payload.ActiveMaterial;
            _tool = payload.Tool;
            _selectedShape = payload.SelectedShape;
            _selectedShapeDescriptorKey = payload.SelectedShapeDescriptorKey;
            _selectedSlopeLength = payload.SelectedSlopeLength;
            _drawPlane = payload.DrawPlane;
            _editHandleMode = payload.EditHandleMode;
            _defaultSlopeSupportMode = payload.DefaultSlopeSupportMode;
            _previewMode = payload.PreviewMode;
            _occupancyMode = payload.OccupancyMode;
            _scene = restoredScene;
            _scene.SetSelection(selectedIds, primaryId);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            if (_draft == null && _tool != SmartBuildTool.Draw)
                _tool = SmartBuildTool.Move;
            _source = null;
            _sourceReason = null;
            ClearSmartPreviewRendererCache();
            RefreshSelection();
            RebuildPlan();
            _lastSavedSceneRecoverySignature = null;
            _pendingSceneRecoverySignature = null;
            _sceneRecoveryDirty = true;
            _smartScenePresetStatus = successMessage;
            RefreshApplyCancelAttention();
            InfoStore.Add(successMessage);
        }

        private bool TryCaptureSmartScenePayload(
            out SmartBuildScenePresetPayload payload,
            out string reason)
        {
            payload = null;
            if (!HasActivePreviewScene)
            {
                reason = "Create a Smart Builder preview before saving a scene.";
                return false;
            }

            payload = SmartBuildScenePresetPayload.Capture(
                _scene,
                _selectedMaterial,
                _tool,
                _selectedShape,
                _selectedShapeDescriptorKey,
                _selectedSlopeLength,
                _drawPlane,
                _editHandleMode,
                _defaultSlopeSupportMode,
                _previewMode,
                _occupancyMode);
            if (!payload.TryValidate(out reason))
            {
                payload = null;
                return false;
            }

            return true;
        }

        private void RefreshSmartSceneRecoveryAvailability()
        {
            _smartSceneRecoveryAvailable =
                TryReadSmartSceneRecovery(
                    out SmartBuildScenePresetPayload payload,
                    out _) &&
                payload?.TryValidate(out _) == true;
        }

        private void TickSmartSceneRecovery()
        {
            if (!_sceneRecoveryDirty ||
                !HasActivePreviewScene || _dragging || _rotating ||
                _patternViewportGesture != null || _planDirty)
                return;
            if (_sceneRecoverySaveAt < 0f)
            {
                _sceneRecoverySaveAt = Time.unscaledTime + SceneRecoveryDebounceSeconds;
                return;
            }
            if (Time.unscaledTime < _sceneRecoverySaveAt)
                return;

            if (!TryCaptureSmartScenePayload(out SmartBuildScenePresetPayload payload, out _))
                return;
            string signature = payload.ContentSignature();
            if (string.Equals(signature, _lastSavedSceneRecoverySignature, StringComparison.Ordinal))
            {
                _sceneRecoveryDirty = false;
                _pendingSceneRecoverySignature = null;
                _sceneRecoverySaveAt = -1f;
                return;
            }

            _pendingSceneRecoverySignature = signature;
            SaveSmartSceneRecovery(payload, signature);
        }

        private void SaveSmartSceneRecoveryImmediately()
        {
            if (!_sceneRecoveryDirty || !HasActivePreviewScene ||
                !TryCaptureSmartScenePayload(out SmartBuildScenePresetPayload payload, out _))
            {
                return;
            }

            string signature = payload.ContentSignature();
            if (!string.Equals(signature, _lastSavedSceneRecoverySignature, StringComparison.Ordinal))
                SaveSmartSceneRecovery(payload, signature);
        }

        private void SaveSmartSceneRecovery(
            SmartBuildScenePresetPayload payload,
            string signature)
        {
            if (!EsuPresetLibrary.Default.TrySaveRecovery(
                    CurrentSceneRecoverySlot(),
                    EsuPresetKind.SmartScene,
                    payload,
                    out string message))
            {
                _smartScenePresetStatus = message;
                _sceneRecoverySaveAt = Time.unscaledTime + 5f;
                return;
            }

            _lastSavedSceneRecoverySignature = signature;
            _pendingSceneRecoverySignature = null;
            _sceneRecoverySaveAt = -1f;
            _smartSceneRecoveryAvailable = true;
            _sceneRecoveryDirty = false;
        }

        private void ClearSmartSceneRecovery()
        {
            EsuPresetLibrary.Default.TryClearRecovery(CurrentSceneRecoverySlot(), out _);
            EsuPresetLibrary.Default.TryClearRecovery(SceneRecoverySlot, out _);
            _lastSavedSceneRecoverySignature = null;
            _pendingSceneRecoverySignature = null;
            _sceneRecoverySaveAt = -1f;
            _smartSceneRecoveryAvailable = false;
            _sceneRecoveryDirty = false;
        }

        private void MarkSmartSceneRecoveryDirty()
        {
            _sceneRecoveryDirty = true;
            _pendingSceneRecoverySignature = null;
            _sceneRecoverySaveAt = Time.unscaledTime + SceneRecoveryDebounceSeconds;
        }

        private string CurrentSceneRecoverySlot()
        {
            AllConstruct construct = _scene?.Construct ?? _draft?.Construct ?? FocusedConstruct();
            string scope = DecorationWorkspaceObjectIdentity.ConstructScope(construct);
            string hash = EsuPresetLibrary.HashPayload(scope ?? "construct:null");
            string suffix = string.IsNullOrWhiteSpace(hash)
                ? "unknown"
                : hash.Substring(0, Math.Min(24, hash.Length)).ToLowerInvariant();
            return "smart-builder-" + suffix;
        }

        private bool TryReadSmartSceneRecovery(
            out SmartBuildScenePresetPayload payload,
            out string message)
        {
            if (EsuPresetLibrary.Default.TryReadRecovery(
                    CurrentSceneRecoverySlot(),
                    out payload,
                    out _,
                    out message))
            {
                return true;
            }

            // One-time compatibility fallback for the pre-1.2 global recovery slot.
            return EsuPresetLibrary.Default.TryReadRecovery(
                SceneRecoverySlot,
                out payload,
                out _,
                out message);
        }

        internal static void SplitSmartVerticalStack(
            Rect stackRect,
            bool showTop,
            bool showBottom,
            float bottomRatio,
            float gap,
            float minimumPanelHeight,
            out Rect topRect,
            out Rect dividerRect,
            out Rect bottomRect,
            out float resolvedBottomRatio)
        {
            topRect = Rect.zero;
            dividerRect = Rect.zero;
            bottomRect = Rect.zero;
            resolvedBottomRatio = ValidSmartStackRatio(bottomRatio);
            stackRect.width = Mathf.Max(0f, stackRect.width);
            stackRect.height = Mathf.Max(0f, stackRect.height);
            if (showTop && showBottom)
            {
                float dividerHeight = Mathf.Min(
                    Mathf.Max(0f, gap),
                    stackRect.height);
                float availableHeight = SmartStackAvailableHeight(
                    stackRect,
                    dividerHeight);
                resolvedBottomRatio = ResolveSmartStackEffectiveRatio(
                    bottomRatio,
                    availableHeight,
                    minimumPanelHeight);
                float bottomHeight = availableHeight * resolvedBottomRatio;
                float topHeight = Mathf.Max(0f, availableHeight - bottomHeight);
                topRect = new Rect(stackRect.x, stackRect.y, stackRect.width, topHeight);
                dividerRect = new Rect(stackRect.x, topRect.yMax, stackRect.width, dividerHeight);
                bottomRect = new Rect(stackRect.x, dividerRect.yMax, stackRect.width, bottomHeight);
                return;
            }

            if (showTop)
                topRect = stackRect;
            if (showBottom)
                bottomRect = stackRect;
        }

        internal static void SplitSmartCollapsibleStack(
            Rect stackRect,
            bool showTop,
            bool showBottom,
            float bottomRatio,
            float gap,
            float minimumPanelHeight,
            float collapsedHeaderHeight,
            out Rect topRect,
            out Rect dividerRect,
            out Rect bottomRect,
            out float resolvedBottomRatio)
        {
            stackRect.width = Mathf.Max(0f, stackRect.width);
            stackRect.height = Mathf.Max(0f, stackRect.height);
            resolvedBottomRatio = ValidSmartStackRatio(bottomRatio);
            if (showTop && showBottom)
            {
                SplitSmartVerticalStack(
                    stackRect,
                    true,
                    true,
                    bottomRatio,
                    gap,
                    minimumPanelHeight,
                    out topRect,
                    out dividerRect,
                    out bottomRect,
                    out resolvedBottomRatio);
                return;
            }

            dividerRect = Rect.zero;
            float shelfHeight = Mathf.Min(
                Mathf.Max(0f, collapsedHeaderHeight),
                stackRect.height);
            if (showTop)
            {
                float resolvedGap = Mathf.Min(
                    Mathf.Max(0f, gap),
                    Mathf.Max(0f, stackRect.height - shelfHeight));
                float topHeight = Mathf.Max(
                    0f,
                    stackRect.height - shelfHeight - resolvedGap);
                topRect = new Rect(
                    stackRect.x,
                    stackRect.y,
                    stackRect.width,
                    topHeight);
                bottomRect = new Rect(
                    stackRect.x,
                    stackRect.yMax - shelfHeight,
                    stackRect.width,
                    shelfHeight);
                return;
            }

            if (showBottom)
            {
                float resolvedGap = Mathf.Min(
                    Mathf.Max(0f, gap),
                    Mathf.Max(0f, stackRect.height - shelfHeight));
                topRect = new Rect(
                    stackRect.x,
                    stackRect.y,
                    stackRect.width,
                    shelfHeight);
                bottomRect = new Rect(
                    stackRect.x,
                    stackRect.y + shelfHeight + resolvedGap,
                    stackRect.width,
                    Mathf.Max(0f, stackRect.height - shelfHeight - resolvedGap));
                return;
            }

            float topShelfHeight = shelfHeight;
            float remainingHeight = Mathf.Max(0f, stackRect.height - topShelfHeight);
            float shelfGap = Mathf.Min(Mathf.Max(0f, gap), remainingHeight);
            float bottomShelfHeight = Mathf.Min(
                shelfHeight,
                Mathf.Max(0f, remainingHeight - shelfGap));
            topRect = new Rect(
                stackRect.x,
                stackRect.y,
                stackRect.width,
                topShelfHeight);
            bottomRect = new Rect(
                stackRect.x,
                topRect.yMax + shelfGap,
                stackRect.width,
                bottomShelfHeight);
        }

        private static float SmartStackDividerGap() => EsuHudLayout.Scale(8f);

        private static float SmartStackMinimumPanelHeight() => EsuHudLayout.Scale(96f);

        private static float SmartSectionShelfHeight() =>
            EsuHudLayout.Scale(EsuHudLayout.SectionHeaderHeightBase);

        private static float SmartStackAvailableHeight(Rect stackRect, float gap) =>
            Mathf.Max(0f, stackRect.height - Mathf.Max(0f, gap));

        private static float ValidSmartStackRatio(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio))
                return 0.5f;
            return Mathf.Clamp01(ratio);
        }

        internal static float ResolveSmartStackEffectiveRatio(
            float preferredBottomRatio,
            float availableHeight,
            float minimumPanelHeight)
        {
            float preferred = ValidSmartStackRatio(preferredBottomRatio);
            if (float.IsNaN(availableHeight) ||
                float.IsInfinity(availableHeight) ||
                availableHeight <= 0f)
            {
                return preferred;
            }

            float bottomHeight = ClampSmartStackBottomHeight(
                availableHeight * preferred,
                availableHeight,
                minimumPanelHeight);
            return bottomHeight / availableHeight;
        }

        private static float ClampSmartStackBottomHeight(
            float bottomHeight,
            float availableHeight,
            float minimumPanelHeight)
        {
            if (availableHeight <= 0f)
                return 0f;

            float minimum = Mathf.Min(Mathf.Max(0f, minimumPanelHeight), availableHeight * 0.5f);
            float maximum = Mathf.Max(minimum, availableHeight - minimum);
            return Mathf.Clamp(bottomHeight, minimum, maximum);
        }

        private static float ClampSmartStackBottomRatioFromHeight(
            float bottomHeight,
            Rect stackRect,
            float gap,
            float minimumPanelHeight)
        {
            float availableHeight = SmartStackAvailableHeight(stackRect, gap);
            if (availableHeight <= 0f)
                return 0.5f;

            return ClampSmartStackBottomHeight(
                bottomHeight,
                availableHeight,
                minimumPanelHeight) / availableHeight;
        }

        private bool HandleSmartStackDividerDrag(
            SmartShapeStackDividerKind divider,
            Rect stackRect,
            Rect dividerRect,
            float gap,
            float minimumPanelHeight,
            ref float bottomRatio)
        {
            Event current = Event.current;
            if (!GUI.enabled ||
                current == null ||
                divider == SmartShapeStackDividerKind.None ||
                dividerRect.height <= 0f)
                return false;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dividerRect.Contains(current.mousePosition))
            {
                _draggingShapeStackDivider = divider;
                _shapeStackDividerDragStartMouseY = current.mousePosition.y;
                _shapeStackDividerDragStartBottomRatio = bottomRatio;
                current.Use();
                return false;
            }

            if (_draggingShapeStackDivider != divider)
                return false;

            if (current.type == EventType.MouseDrag)
            {
                float availableHeight = SmartStackAvailableHeight(stackRect, gap);
                float startBottomHeight = availableHeight * _shapeStackDividerDragStartBottomRatio;
                float deltaY = current.mousePosition.y - _shapeStackDividerDragStartMouseY;
                bottomRatio = ClampSmartStackBottomRatioFromHeight(
                    startBottomHeight - deltaY,
                    stackRect,
                    gap,
                    minimumPanelHeight);
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseUp)
            {
                _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
                current.Use();
            }

            return false;
        }

        private void ClearSmartStackDividerDrag(SmartShapeStackDividerKind divider)
        {
            if (_draggingShapeStackDivider == divider)
                _draggingShapeStackDivider = SmartShapeStackDividerKind.None;
        }

        private void DrawSmartStackDividerGrip(Rect dividerRect, SmartShapeStackDividerKind divider)
        {
            Event current = Event.current;
            bool active = _draggingShapeStackDivider == divider;
            bool hovered = current != null && dividerRect.Contains(current.mousePosition);
            EsuHudLayout.DrawStackDividerGrip(dividerRect, active || hovered);
            string tooltip = divider == SmartShapeStackDividerKind.LeftWorkspace
                ? "Drag to balance Overview against Scene and Selected."
                : "Drag to balance Scene and Selected.";
            EsuCursorTooltip.Register(dividerRect, tooltip);
        }

        private void DrawShapePalette(float availableHeight)
        {
            List<SmartBuildShapePaletteEntry> rows = FilterShapePaletteEntries().ToList();
            DrawShapePaletteToolbar(rows.Count);
            DrawShapeSizeSelector();

            float paletteHeight = ShapePaletteViewportHeight(availableHeight);
            float viewportWidth = Mathf.Max(1f, _rightPanelRect.width - EsuHudLayout.Scale(32f));
            ClampShapePaletteScroll(rows, viewportWidth, paletteHeight);
            _shapePaletteScroll = GUILayout.BeginScrollView(
                _shapePaletteScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(paletteHeight));
            Rect viewport = GUILayoutUtility.GetLastRect();
            if (viewport.width > 1f)
                viewportWidth = viewport.width;
            if (rows.Count == 0)
                DrawShapePaletteEmptyRow();
            else if (_shapePreviewGrid)
                DrawShapePreviewGrid(rows, viewportWidth, paletteHeight);
            else
                DrawShapeListRows(rows, paletteHeight);
            GUILayout.EndScrollView();
        }

        private void DrawShapePaletteEmptyRow()
        {
            string message = AvailablePaletteShapeDescriptors().Count == 0
                ? (_sourceReason ?? "No Smart Builder shapes are available for this material.")
                : "No shapes match the current palette filter.";
            GUILayout.Label(
                message,
                DecorationEditorTheme.MiniWrap,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EsuHudLayout.Scale(54f)));
        }

        private void DrawShapePaletteToolbar(int visibleCount)
        {
            if (string.Equals(_shapeCategoryFilter, "generated", StringComparison.OrdinalIgnoreCase))
                _shapeCategoryFilter = "all";

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("List", "Show Smart Builder shapes as a compact list."),
                    DecorationEditorTheme.ToolButton(!_shapePreviewGrid),
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(25f))))
            {
                _shapePreviewGrid = false;
                _shapePaletteScroll = Vector2.zero;
            }
            if (SmartGUILayoutButton(
                    new GUIContent("3D grid", "Show Smart Builder shapes as rotating item thumbnails."),
                    DecorationEditorTheme.ToolButton(_shapePreviewGrid),
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(25f))))
            {
                _shapePreviewGrid = true;
                _shapePaletteScroll = Vector2.zero;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                visibleCount.ToString(CultureInfo.InvariantCulture) + " shown",
                DecorationEditorTheme.Mini,
                GUILayout.Height(EsuHudLayout.Scale(25f)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawShapeCategoryFilterButton("all", "All", 38f);
            DrawShapeCategoryFilterButton("basic", "Basic", 52f);
            DrawShapeCategoryFilterButton("slopes", "Slopes", 56f);
            DrawShapeCategoryFilterButton("corners", "Corners", 68f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawShapeCategoryFilterButton("wedges", "Wedges", 66f);
            DrawShapeCategoryFilterButton("transitions", "Transitions", 92f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(22f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            string previous = _shapeFilter;
            _shapeFilter = GUILayout.TextField(_shapeFilter ?? string.Empty, DecorationEditorTheme.TextField);
            if (!string.Equals(previous, _shapeFilter, StringComparison.Ordinal))
                _shapePaletteScroll = Vector2.zero;
            EsuCursorTooltip.RegisterLast("Filter shapes by label, item name, or FtD geometry name.");
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorToolGrid(
            IReadOnlyList<SmartBuildShapeDescriptor> descriptors,
            float viewportWidth)
        {
            if (descriptors == null || descriptors.Count == 0)
            {
                GUILayout.Label(
                    _sourceReason ?? "No generated Smart Builder tools are available for this material.",
                    DecorationEditorTheme.MiniWrap,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(EsuHudLayout.Scale(46f)));
                return;
            }

            float gap = EsuHudLayout.Scale(6f);
            int columns = Mathf.Clamp(
                Mathf.FloorToInt((viewportWidth + gap) / Mathf.Max(1f, EsuHudLayout.Scale(118f))),
                1,
                3);
            float cardWidth = Mathf.Max(
                EsuHudLayout.Scale(104f),
                (viewportWidth - gap * Math.Max(0, columns - 1)) / columns);
            float cardHeight = Mathf.Clamp(
                cardWidth * 0.74f,
                EsuHudLayout.Scale(74f),
                EsuHudLayout.Scale(118f));
            int rows = Mathf.CeilToInt(descriptors.Count / (float)columns);
            for (int row = 0; row < rows; row++)
            {
                Rect rowRect = GUILayoutUtility.GetRect(
                    Mathf.Max(1f, viewportWidth),
                    cardHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(cardHeight));
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= descriptors.Count)
                        continue;

                    SmartBuildShapeDescriptor descriptor = descriptors[index];
                    Rect card = new Rect(
                        rowRect.x + column * (cardWidth + gap),
                        rowRect.y,
                        cardWidth,
                        cardHeight - EsuHudLayout.Scale(2f));
                    DrawGeneratorToolCard(card, descriptor);
                }
            }
        }

        private void DrawGeneratorToolCard(Rect card, SmartBuildShapeDescriptor descriptor)
        {
            if (descriptor == null)
                return;

            bool active = descriptor.Key == _selectedShapeDescriptorKey;
            if (GUI.Button(card, GUIContent.none, active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row))
                SelectShapeDescriptor(descriptor);

            SmartBuildShapePaletteEntry entry = SmartBuildShapePaletteEntry.Create(
                descriptor,
                _source?.FamilyForShape(descriptor),
                PaletteCandidateFor(descriptor));
            EsuCursorTooltip.Register(card, descriptor.Tooltip);
            if (Event.current != null &&
                Event.current.type != EventType.Layout &&
                card.Contains(Event.current.mousePosition))
            {
                _hoveredShapeEntry = entry;
            }

            Rect previewRect = new Rect(
                card.x + EsuHudLayout.Scale(6f),
                card.y + EsuHudLayout.Scale(6f),
                card.width - EsuHudLayout.Scale(12f),
                Mathf.Max(EsuHudLayout.Scale(32f), card.height - EsuHudLayout.Scale(28f)));
            DrawGeneratorDescriptorPreview(previewRect, descriptor, active);

            GUI.Label(
                new Rect(
                    card.x + EsuHudLayout.Scale(6f),
                    previewRect.yMax + EsuHudLayout.Scale(2f),
                    card.width - EsuHudLayout.Scale(12f),
                    EsuHudLayout.Scale(20f)),
                CompactText(descriptor.Label, 20),
                active ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini);
        }

        private void DrawShapeCategoryFilterButton(string key, string label, float width)
        {
            bool active = string.Equals(_shapeCategoryFilter, key, StringComparison.OrdinalIgnoreCase);
            if (SmartGUILayoutButton(
                    new GUIContent(label, "Filter the Smart Builder shape palette to " + label + "."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(width)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _shapeCategoryFilter = key;
                _shapePaletteScroll = Vector2.zero;
            }
        }

        private void DrawShapeSizeSelector()
        {
            SmartBuildShapeDescriptor selected = SelectedShapeDescriptor;
            if (!selected.UsesLengthSelector)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(48f)));
            for (int length = 1; length <= 4; length++)
                DrawShapeSizeButton(length);
            GUILayout.EndHorizontal();
        }

        private IEnumerable<SmartBuildShapePaletteEntry> FilterShapePaletteEntries()
        {
            IEnumerable<SmartBuildShapePaletteEntry> query = AvailablePaletteShapeDescriptors()
                .Select(descriptor => SmartBuildShapePaletteEntry.Create(
                    descriptor,
                    _source?.FamilyForShape(descriptor),
                    PaletteCandidateFor(descriptor)))
                .Where(entry => entry.IsValid);

            string category = (_shapeCategoryFilter ?? "all").Trim().ToLowerInvariant();
            if (category.Length > 0 && category != "all")
            {
                query = query.Where(entry =>
                    string.Equals(
                        CategoryFilterKey(entry.Descriptor.Category),
                        category,
                        StringComparison.OrdinalIgnoreCase));
            }

            string filter = (_shapeFilter ?? string.Empty).Trim().ToLowerInvariant();
            if (filter.Length > 0)
                query = query.Where(entry => entry.SearchText.Contains(filter));
            return query;
        }

        private float ShapePaletteViewportHeight(float availableHeight)
        {
            float controlsHeight = EsuHudLayout.Scale(
                SelectedShapeDescriptor.UsesLengthSelector ? 128f : 94f);
            float remainingHeight = Mathf.Max(0f, availableHeight - controlsHeight);
            return Mathf.Max(EsuHudLayout.Scale(54f), remainingHeight);
        }

        private void ClampShapePaletteScroll(
            List<SmartBuildShapePaletteEntry> rows,
            float viewportWidth,
            float viewportHeight)
        {
            float rowHeight = EsuHudLayout.Scale(ShapePaletteRowHeight);
            int contentRows;
            if (_shapePreviewGrid)
            {
                ShapePreviewGridLayout layout = ShapePreviewGridLayoutFor(viewportWidth);
                rowHeight = layout.RowHeight;
                contentRows = Mathf.CeilToInt((rows?.Count ?? 0) / (float)layout.Columns);
            }
            else
            {
                contentRows = rows?.Count ?? 0;
            }

            float contentHeight = Math.Max(0f, contentRows * rowHeight);
            float maxY = Math.Max(0f, contentHeight - Math.Max(1f, viewportHeight));
            if (_shapePaletteScroll.y > maxY || _shapePaletteScroll.y < 0f)
                _shapePaletteScroll.y = Mathf.Clamp(_shapePaletteScroll.y, 0f, maxY);
        }

        private SmartBlockCandidate PaletteCandidateFor(SmartBuildShapeDescriptor descriptor)
        {
            int length = descriptor.UsesLengthSelector
                ? _selectedSlopeLength
                : Math.Max(1, descriptor.TransitionTo);
            return PaletteCandidateForLength(descriptor, length);
        }

        private SmartBlockCandidate PaletteCandidateForLength(
            SmartBuildShapeDescriptor descriptor,
            int length)
        {
            SmartBlockFamily family = _source?.FamilyForShape(descriptor);
            if (family == null || !family.IsSupported)
                return null;

            return family.CandidateForLength(length);
        }

        private void DrawShapeListRows(
            List<SmartBuildShapePaletteEntry> rows,
            float viewportHeight)
        {
            float rowHeight = EsuHudLayout.Scale(ShapePaletteRowHeight);
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_shapePaletteScroll.y / rowHeight) - 4);
            int visible = Mathf.CeilToInt(viewportHeight / rowHeight) + 8;
            int last = Mathf.Min(total, first + visible);
            if (first > 0)
                GUILayout.Space(first * rowHeight);
            for (int index = first; index < last; index++)
            {
                SmartBuildShapePaletteEntry entry = rows[index];
                bool active = entry.Descriptor?.Key == _selectedShapeDescriptorKey;
                GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                string label = entry.Descriptor.Label;
                string detail = entry.Candidate == null
                    ? string.Empty
                    : " | " + entry.Candidate.Length.ToString(CultureInfo.InvariantCulture) + "m";
                Rect row = GUILayoutUtility.GetRect(
                    1f,
                    rowHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rowHeight));
                if (GUI.Button(row, GUIContent.none, style))
                    SelectShapeDescriptor(entry.Descriptor);

                Rect iconRect = new Rect(
                    row.x + EsuHudLayout.Scale(5f),
                    row.y + EsuHudLayout.Scale(4f),
                    EsuHudLayout.Scale(20f),
                    row.height - EsuHudLayout.Scale(8f));
                Texture icon = DecorationEditorIconCatalog.Get(entry.Descriptor?.IsCuboid == true ? "cube" : "scale");
                if (icon != null)
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
                GUI.Label(
                    new Rect(
                        iconRect.xMax + EsuHudLayout.Scale(6f),
                        row.y,
                        row.width - iconRect.width - EsuHudLayout.Scale(16f),
                        row.height),
                    label + detail,
                    active ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini);
                EsuCursorTooltip.Register(row, entry.Descriptor.Tooltip);
                if (Event.current != null &&
                    Event.current.type != EventType.Layout &&
                    row.Contains(Event.current.mousePosition))
                {
                    _hoveredShapeEntry = entry;
                }
            }

            if (last < total)
                GUILayout.Space((total - last) * rowHeight);
        }

        private void DrawShapePreviewGrid(
            List<SmartBuildShapePaletteEntry> rows,
            float viewportWidth,
            float viewportHeight)
        {
            ShapePreviewGridLayout layout = ShapePreviewGridLayoutFor(viewportWidth);
            int totalRows = Mathf.CeilToInt(rows.Count / (float)layout.Columns);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_shapePaletteScroll.y / layout.RowHeight));
            int lastRow = Mathf.Min(
                totalRows,
                Mathf.CeilToInt((_shapePaletteScroll.y + viewportHeight) / layout.RowHeight));
            bool canRenderPreview = Event.current != null && Event.current.type == EventType.Repaint;
            if (firstRow > 0)
                GUILayout.Space(firstRow * layout.RowHeight);
            for (int gridRow = firstRow; gridRow < lastRow; gridRow++)
            {
                Rect rowRect = GUILayoutUtility.GetRect(
                    Mathf.Max(1f, viewportWidth),
                    layout.RowHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(layout.RowHeight));
                for (int column = 0; column < layout.Columns; column++)
                {
                    int rowIndex = gridRow * layout.Columns + column;
                    if (rowIndex >= rows.Count)
                        continue;

                    SmartBuildShapePaletteEntry entry = rows[rowIndex];
                    bool active = entry.Descriptor?.Key == _selectedShapeDescriptorKey;
                    Rect card = new Rect(
                        rowRect.x + layout.OuterPadding + column * (layout.CardWidth + layout.Gap),
                        rowRect.y,
                        layout.CardWidth,
                        layout.CardHeight);
                    if (GUI.Button(card, GUIContent.none, active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row))
                        SelectShapeDescriptor(entry.Descriptor);
                    EsuCursorTooltip.Register(card, entry.Descriptor.Tooltip);
                    if (Event.current != null &&
                        Event.current.type != EventType.Layout &&
                        card.Contains(Event.current.mousePosition))
                    {
                        _hoveredShapeEntry = entry;
                    }

                    int previewPixels = Mathf.Clamp(
                        Mathf.RoundToInt(Mathf.Max(
                            EsuHudLayout.Scale(ShapePreviewGridTexturePixels),
                            Mathf.Min(card.width, layout.PreviewHeight))),
                        64,
                        160);
                    Texture preview = canRenderPreview
                        ? _itemPreviewRenderer?.GetPreview(entry.Candidate, previewPixels, _shapePreviewSpin)
                        : _itemPreviewRenderer?.GetCachedPreview(entry.Candidate, previewPixels);
                    Rect previewRect = new Rect(
                        card.x + EsuHudLayout.Scale(6f),
                        card.y + EsuHudLayout.Scale(4f),
                        card.width - EsuHudLayout.Scale(12f),
                        layout.PreviewHeight);
                    if (preview != null)
                        GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
                    else
                        GUI.Label(previewRect, "no mesh", DecorationEditorTheme.Mini);
                    GUI.Label(
                        new Rect(
                            card.x + EsuHudLayout.Scale(4f),
                            card.yMax - EsuHudLayout.Scale(26f),
                            card.width - EsuHudLayout.Scale(8f),
                            EsuHudLayout.Scale(22f)),
                        CompactText(
                            entry.Descriptor.Label,
                            Mathf.Clamp(
                                Mathf.FloorToInt(card.width / EsuHudLayout.Scale(5.8f)),
                                14,
                                34)),
                        active ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini);
                }
            }

            if (lastRow < totalRows)
                GUILayout.Space((totalRows - lastRow) * layout.RowHeight);
        }

        private static ShapePreviewGridLayout ShapePreviewGridLayoutFor(float viewportWidth)
        {
            float outerPadding = EsuHudLayout.Scale(ShapePreviewGridOuterPadding);
            float gap = EsuHudLayout.Scale(ShapePreviewGridGap);
            float minCardWidth = EsuHudLayout.Scale(ShapePreviewGridMinCardWidth);
            float available = Mathf.Max(minCardWidth, viewportWidth - outerPadding * 2f);
            int columns = Mathf.Max(
                1,
                Mathf.FloorToInt((available + gap) / (minCardWidth + gap)));
            float cardWidth = Mathf.Max(
                minCardWidth,
                (available - gap * Mathf.Max(0, columns - 1)) / columns);
            float cardHeight = Mathf.Clamp(
                cardWidth * ShapePreviewGridCardAspect,
                EsuHudLayout.Scale(ShapePreviewGridMinCardHeight),
                EsuHudLayout.Scale(ShapePreviewGridMaxCardHeight));
            float previewHeight = Mathf.Max(
                EsuHudLayout.Scale(52f),
                cardHeight - EsuHudLayout.Scale(32f));
            return new ShapePreviewGridLayout(
                columns,
                cardWidth,
                cardHeight,
                cardHeight + gap,
                previewHeight,
                gap,
                outerPadding);
        }

        private void EnsureSelectedShapeAvailable()
        {
            IReadOnlyList<SmartBuildShapeDescriptor> descriptors = AvailableShapeDescriptors();
            if (descriptors.Any(descriptor => descriptor.Key == _selectedShapeDescriptorKey))
                return;

            SelectShapeDescriptor(
                descriptors.FirstOrDefault() ?? SmartBuildShapeDescriptors.Cuboid,
                armAddMode: false);
        }

        private static string CategoryLabel(SmartBuildShapeCategory category)
        {
            switch (category)
            {
                case SmartBuildShapeCategory.Slopes:
                    return "Slopes";
                case SmartBuildShapeCategory.Corners:
                    return "Corners";
                case SmartBuildShapeCategory.Wedges:
                    return "Wedges";
                case SmartBuildShapeCategory.Transitions:
                    return "Transitions";
                case SmartBuildShapeCategory.Generated:
                    return "Generated";
                default:
                    return "Basic";
            }
        }

        private static string CategoryFilterKey(SmartBuildShapeCategory category)
        {
            switch (category)
            {
                case SmartBuildShapeCategory.Slopes:
                    return "slopes";
                case SmartBuildShapeCategory.Corners:
                    return "corners";
                case SmartBuildShapeCategory.Wedges:
                    return "wedges";
                case SmartBuildShapeCategory.Transitions:
                    return "transitions";
                case SmartBuildShapeCategory.Generated:
                    return "generated";
                default:
                    return "basic";
            }
        }

        private void DrawShapePreviewCard()
        {
            if (!_hoveredShapeEntry.IsValid)
                return;

            float cardWidth = EsuHudLayout.Scale(322f);
            float cardHeight = EsuHudLayout.Scale(148f);
            Rect rect = new Rect(
                Mathf.Clamp(
                    Event.current.mousePosition.x + EsuHudLayout.Scale(22f),
                    EsuHudLayout.Scale(8f),
                    Screen.width - cardWidth - EsuHudLayout.Scale(8f)),
                Mathf.Clamp(
                    Event.current.mousePosition.y + EsuHudLayout.Scale(22f),
                    EsuHudLayout.Scale(8f),
                    Screen.height - cardHeight - EsuHudLayout.Scale(8f)),
                cardWidth,
                cardHeight);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect previewRect = new Rect(
                rect.x + EsuHudLayout.Scale(10f),
                rect.y + EsuHudLayout.Scale(32f),
                EsuHudLayout.Scale(104f),
                EsuHudLayout.Scale(104f));
            bool generator = _hoveredShapeEntry.Descriptor?.IsGenerator == true;
            Texture preview = generator
                ? null
                : _itemPreviewRenderer?.GetPreview(
                    _hoveredShapeEntry.Candidate,
                    128,
                    _shapePreviewSpin);
            if (generator)
                DrawGeneratorDescriptorPreview(previewRect, _hoveredShapeEntry.Descriptor, active: true);
            else if (preview != null)
                GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, alphaBlend: true);
            else
                GUI.Label(previewRect, "no preview", DecorationEditorTheme.Mini);

            GUI.BeginGroup(rect);
            GUI.Label(
                new Rect(
                    EsuHudLayout.Scale(8f),
                    EsuHudLayout.Scale(6f),
                    rect.width - EsuHudLayout.Scale(16f),
                    EsuHudLayout.Scale(24f)),
                new GUIContent(" Shape preview", DecorationEditorIconCatalog.Get("build")),
                DecorationEditorTheme.SubHeader);
            float textX = EsuHudLayout.Scale(124f);
            float textWidth = rect.width - textX - EsuHudLayout.Scale(10f);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(36f), textWidth, EsuHudLayout.Scale(24f)),
                _hoveredShapeEntry.Descriptor.Label,
                DecorationEditorTheme.Body);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(62f), textWidth, EsuHudLayout.Scale(36f)),
                _hoveredShapeEntry.Candidate == null
                    ? CategoryLabel(_hoveredShapeEntry.Descriptor.Category)
                    : CompactText(_hoveredShapeEntry.Candidate.DisplayName, 42),
                DecorationEditorTheme.MiniWrap);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(100f), textWidth, EsuHudLayout.Scale(40f)),
                generator
                    ? "Click to select this generated block tool."
                    : "Click to select. Current size: " +
                      (_hoveredShapeEntry.Candidate?.Length ?? Math.Max(1, _selectedSlopeLength)).ToString(CultureInfo.InvariantCulture) +
                      "m.",
                DecorationEditorTheme.MiniWrap);
            GUI.EndGroup();
        }

        private void DrawGeneratorDescriptorPreview(
            Rect rect,
            SmartBuildShapeDescriptor descriptor,
            bool active)
        {
            if (descriptor == null)
                return;

            SmartBuildPiece previewPiece = SmartBuildPiece.CreateGeneratedShapeForPreview(
                descriptor,
                descriptor.Kind == SmartBuildShapeKind.GeneratedSphere ? 9 : 9);
            Vector3i[] cells = previewPiece
                .EnumeratePreviewCells(_source)
                .ToArray();
            DrawGeneratorCellSilhouette(rect, cells, active);
        }

        private static void DrawGeneratorCellSilhouette(
            Rect rect,
            IReadOnlyList<Vector3i> cells,
            bool active)
        {
            if (cells == null || cells.Count == 0 || rect.width <= 1f || rect.height <= 1f)
                return;

            var projected = cells
                .Select(cell => new Vector2Int(cell.x, cell.z))
                .Distinct()
                .OrderBy(cell => cell.y)
                .ThenBy(cell => cell.x)
                .ToArray();
            if (projected.Length == 0)
                return;

            int minX = projected.Min(cell => cell.x);
            int maxX = projected.Max(cell => cell.x);
            int minY = projected.Min(cell => cell.y);
            int maxY = projected.Max(cell => cell.y);
            int width = Math.Max(1, maxX - minX + 1);
            int height = Math.Max(1, maxY - minY + 1);
            float cellSize = Mathf.Max(
                1f,
                Mathf.Min(rect.width / width, rect.height / height) * 0.82f);
            float totalWidth = cellSize * width;
            float totalHeight = cellSize * height;
            float startX = rect.x + (rect.width - totalWidth) * 0.5f;
            float startY = rect.y + (rect.height - totalHeight) * 0.5f;

            Color oldColor = GUI.color;
            Color fill = active
                ? new Color(0.12f, 0.95f, 1f, 0.88f)
                : new Color(0.66f, 0.84f, 0.86f, 0.82f);
            Color shadow = new Color(0f, 0.08f, 0.1f, 0.7f);
            Color edge = active
                ? new Color(1f, 1f, 0.26f, 0.9f)
                : new Color(0.02f, 0.18f, 0.22f, 0.9f);
            try
            {
                foreach (Vector2Int cell in projected)
                {
                    Rect block = new Rect(
                        startX + (cell.x - minX) * cellSize,
                        startY + (maxY - cell.y) * cellSize,
                        Mathf.Max(1f, cellSize - 1f),
                        Mathf.Max(1f, cellSize - 1f));
                    GUI.color = shadow;
                    GUI.DrawTexture(new Rect(block.x + 1f, block.y + 1f, block.width, block.height), Texture2D.whiteTexture);
                    GUI.color = fill;
                    GUI.DrawTexture(block, Texture2D.whiteTexture);
                    DrawGuiBorder(block, edge);
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static void DrawGuiBorder(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private void DrawShapeButton(SmartBuildShapeDescriptor descriptor)
        {
            bool active = descriptor?.Key == _selectedShapeDescriptorKey;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        descriptor?.Label ?? "Shape",
                        DecorationEditorIconCatalog.Get(descriptor?.IsCuboid == true ? "cube" : "scale"),
                        descriptor?.Tooltip ?? "Place this structural shape."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Height(EsuHudLayout.Scale(30f))))
            {
                SelectShapeDescriptor(descriptor);
            }
        }

        private void DrawShapeSizeButton(int length)
        {
            bool active = _selectedSlopeLength == length;
            if (SmartGUILayoutButton(
                    new GUIContent(length.ToString(CultureInfo.InvariantCulture), length + "m shape size."),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(36f)),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                if (_selectedSlopeLength != length)
                {
                    _selectedSlopeLength = length;
                    InvalidateConditionalReplacementPreview();
                }
                ArmAddMode();
            }
        }

        private void DrawPieceRow(SmartBuildPiece piece)
        {
            bool selected = _scene?.IsSelected(piece.Id) == true;
            bool primary = _draft != null && _draft.Id == piece.Id;
            string text = (primary ? "P | " : selected ? "+ | " : string.Empty) +
                          piece.CompactSceneLabel();
            string tooltip = (primary ? "Primary selection | " : selected ? "Selected | " : string.Empty) +
                             piece.ShapeLabel() + " | " + piece.FormatDimensions() + " | origin " + FormatCell(piece.Origin) +
                             " | Ctrl-click toggles; Shift-click selects a range.";
            Event current = Event.current;
            bool control = current?.control == true || current?.command == true;
            bool shift = current?.shift == true;
            if (SmartGUILayoutButton(
                    new GUIContent(text, tooltip),
                    DecorationEditorTheme.ToolButton(selected),
                    GUILayout.Height(EsuHudLayout.Scale(28f))))
            {
                SelectSceneRow(piece, shift, control);
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
                ((_scene?.SelectionCount ?? 0) > 1
                    ? (_scene.SelectionCount.ToString(CultureInfo.InvariantCulture) + " selected | primary #")
                    : "#") +
                _draft.Id.ToString(CultureInfo.InvariantCulture) + " | " +
                _draft.ShapeCode() + " | " + _draft.FormatDimensions(),
                DecorationEditorTheme.MiniWrap);
            LabelRow("Shape", _draft.ShapeLabel());
            LabelRow(
                "Material",
                SmartBlockFamilyCatalog.MaterialDisplayName(MaterialForPiece(_draft)));
            LabelRow("Origin", FormatCell(_draft.Origin));
            LabelRow("Size", _draft.FormatDimensions());
            LabelRow(
                "Cells",
                PreviewCellCount().ToString("N0", CultureInfo.InvariantCulture));

            float rowHeight = EsuHudLayout.Scale(28f);
            float gap = EsuHudLayout.Scale(2f);
            Rect grid = GUILayoutUtility.GetRect(1f, rowHeight * 3f + gap * 2f, GUILayout.ExpandWidth(true));
            float columnWidth = (grid.width - gap) * 0.5f;
            Rect select = new Rect(grid.x, grid.y, columnWidth, rowHeight);
            Rect duplicate = new Rect(select.xMax + gap, grid.y, columnWidth, rowHeight);
            Rect delete = new Rect(grid.x, select.yMax + gap, columnWidth, rowHeight);
            Rect yaw = new Rect(delete.xMax + gap, delete.y, columnWidth, rowHeight);
            Rect flip = new Rect(grid.x, delete.yMax + gap, grid.width, rowHeight);

            if (SmartGUIButton(select, new GUIContent("Select", "Keep this piece selected for editing."), DecorationEditorTheme.Button))
                _tool = SmartBuildTool.Move;
            bool groupSelection = (_scene?.SelectionCount ?? 0) > 1;
            if (SmartGUIButton(duplicate, new GUIContent("Duplicate", groupSelection ? "Duplicate the selected group one cell to the right." : "Duplicate the selected piece one cell to the right."), DecorationEditorTheme.Button))
                DuplicateSelectedPiece();
            if (SmartGUIButton(delete, new GUIContent("Delete", groupSelection ? "Remove the selected preview group." : "Remove the selected preview piece."), DecorationEditorTheme.Button))
                DeleteSelectedPiece();
            if (SmartGUIButton(yaw, new GUIContent("Yaw", groupSelection ? "Rotate the selected group around its primary piece." : "Rotate the selected piece around construct Y."), DecorationEditorTheme.Button))
                YawSelectedPiece();

            bool canFlip = CanFlipSelection();
            bool previous = GUI.enabled;
            GUI.enabled = previous && canFlip;
            if (SmartGUIButton(flip, new GUIContent("Flip", "Reverse the selected shape's forward direction."), canFlip ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton))
                FlipSelectedPiece();
            GUI.enabled = previous;

            DrawPrecisionTransformLayoutControls();
            DrawShapeConversionControls();
            DrawCuboidFillControls();
            DrawSelectionArrayControls();
            DrawAdvancedArrayBrushControls();
            DrawConditionalReplacementControls();

            if (_draft?.IsGeneratedShape == true)
                DrawGeneratedPieceSettings();
            DrawPlanSummary();
        }

        private void DrawShapeConversionControls()
        {
            SmartBuildShapeDescriptor target = SelectedShapeDescriptor;
            if (target == null || _scene?.SelectionCount <= 0)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            bool canConvert = CanConvertSelectionTo(target, out string reason);
            bool previous = GUI.enabled;
            GUI.enabled = previous && canConvert;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Convert to " + target.Label,
                        canConvert
                            ? "Convert every selected piece to the active Library shape while preserving its center, orientation, material, and approximate extents."
                            : reason),
                    canConvert ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                ConvertSelectionTo(target);
            }
            GUI.enabled = previous;
        }

        private bool CanConvertSelectionTo(
            SmartBuildShapeDescriptor descriptor,
            out string reason)
        {
            IReadOnlyList<SmartBuildPiece> selected = _scene?.SelectedPieces;
            if (descriptor == null || selected == null || selected.Count == 0)
            {
                reason = "Select one or more Smart Builder pieces.";
                return false;
            }

            bool anyChange = selected.Any(
                piece => !string.Equals(
                    piece.ShapeDescriptorKey,
                    descriptor.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (!anyChange)
            {
                reason = "The selected pieces already use this shape.";
                return false;
            }

            foreach (SmartBuildPiece piece in selected)
            {
                SmartBuildSource source = SourceForPiece(piece);
                if (source == null)
                {
                    reason = "One selected piece material is unavailable.";
                    return false;
                }

                SmartBlockFamily family = source.FamilyForShape(descriptor);
                if (family?.IsSupported != true)
                {
                    reason = family?.UnsupportedReason ??
                             descriptor.Label + " is unavailable for " + source.DisplayName + ".";
                    return false;
                }
                if (descriptor.ProceduralDownSlope &&
                    !source.HasDownSlopeLength(new[] { _selectedSlopeLength }))
                {
                    reason = _selectedSlopeLength.ToString(CultureInfo.InvariantCulture) +
                             "m slopes are unavailable for " + source.DisplayName + ".";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private void ConvertSelectionTo(SmartBuildShapeDescriptor descriptor)
        {
            if (!CanConvertSelectionTo(descriptor, out string reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    InfoStore.Add(reason);
                return;
            }

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryConvertSelectionTo(
                    descriptor,
                    _selectedSlopeLength,
                    out int converted,
                    out reason))
            {
                InfoStore.Add(
                    string.IsNullOrWhiteSpace(reason)
                        ? "The selected Smart Builder group cannot be converted without losing geometry."
                        : reason);
                return;
            }

            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add(
                "Converted " + converted.ToString(CultureInfo.InvariantCulture) +
                (converted == 1 ? " Smart Builder piece" : " Smart Builder pieces") +
                " to " + descriptor.Label + ".");
        }

        private void DrawCuboidFillControls()
        {
            IReadOnlyList<SmartBuildPiece> selected = _scene?.SelectedPieces;
            if (selected == null || selected.Count == 0 ||
                selected.Any(piece => piece.ShapeKind != SmartBuildShapeKind.Cuboid))
            {
                return;
            }

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Cuboid fill", DecorationEditorTheme.SubHeader);
            bool allHollow = selected.All(piece => piece.CuboidHollow);
            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Solid", "Fill every cell in the selected cuboid volumes."),
                    DecorationEditorTheme.ToolButton(!allHollow),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetSelectedCuboidsHollow(false);
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Hollow", "Keep only the one-cell outer shell of each selected cuboid."),
                    DecorationEditorTheme.ToolButton(allHollow),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetSelectedCuboidsHollow(true);
            }
            GUILayout.EndHorizontal();
        }

        private void SetSelectedCuboidsHollow(bool hollow)
        {
            SmartBuildPiece[] cuboids = (_scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece.ShapeKind == SmartBuildShapeKind.Cuboid)
                .ToArray();
            if (cuboids.Length == 0 || cuboids.All(piece => piece.CuboidHollow == hollow))
                return;

            RecordSceneHistory();
            foreach (SmartBuildPiece piece in cuboids)
                piece.SetCuboidHollow(hollow);
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder cuboid fill: " + (hollow ? "Hollow shell" : "Solid") + ".");
        }

        private void DrawSelectionArrayControls()
        {
            if (_scene?.SelectionCount <= 0)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Array / brush", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Copies", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(46f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-", "Use fewer array copies."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _arrayCopyCount = Mathf.Clamp(_arrayCopyCount - 1, 1, 64);
            }
            GUILayout.Label(
                _arrayCopyCount.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(28f)));
            if (SmartGUILayoutButton(
                    new GUIContent("+", "Use more array copies."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _arrayCopyCount = Mathf.Clamp(_arrayCopyCount + 1, 1, 64);
            }
            GUILayout.Label("Gap", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(28f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-", "Reduce spacing between brushed copies."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _arraySpacing = Mathf.Clamp(_arraySpacing - 1, 1, 64);
            }
            GUILayout.Label(
                _arraySpacing.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(28f)));
            if (SmartGUILayoutButton(
                    new GUIContent("+", "Increase spacing between brushed copies."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _arraySpacing = Mathf.Clamp(_arraySpacing + 1, 1, 64);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawSelectionArrayButton(SmartBuildAxis.X, -1, "-X");
            DrawSelectionArrayButton(SmartBuildAxis.X, 1, "+X");
            DrawSelectionArrayButton(SmartBuildAxis.Y, -1, "-Y");
            DrawSelectionArrayButton(SmartBuildAxis.Y, 1, "+Y");
            DrawSelectionArrayButton(SmartBuildAxis.Z, -1, "-Z");
            DrawSelectionArrayButton(SmartBuildAxis.Z, 1, "+Z");
            GUILayout.EndHorizontal();
        }

        private void DrawSelectionArrayButton(
            SmartBuildAxis axis,
            int sign,
            string label)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        "Brush " + _arrayCopyCount.ToString(CultureInfo.InvariantCulture) +
                        " copy layer(s) every " + _arraySpacing.ToString(CultureInfo.InvariantCulture) +
                        " cell(s) along " + label + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateSelectionArray(axis, sign);
            }
        }

        private void CreateSelectionArray(SmartBuildAxis axis, int sign)
        {
            if (_scene?.SelectionCount <= 0)
                return;

            Vector3i step = SmartBuildAxisHelper.ToVector3i(
                axis,
                (sign >= 0 ? 1 : -1) * Math.Max(1, _arraySpacing));
            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryDuplicateSelectionArray(
                    step,
                    _arrayCopyCount,
                    out IReadOnlyList<SmartBuildPiece> duplicates,
                    out string reason))
            {
                InfoStore.Add(
                    string.IsNullOrWhiteSpace(reason)
                        ? "The complete Smart Builder array could not be created."
                        : reason);
                return;
            }

            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _tool = SmartBuildTool.Move;
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add(
                "Brushed " + duplicates.Count.ToString(CultureInfo.InvariantCulture) +
                " Smart Builder array copies along " +
                (sign >= 0 ? "+" : "-") + axis + ".");
        }

        private void DrawGeneratedPieceSettings()
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Generator", DecorationEditorTheme.SubHeader);

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Shell", "Use only the outline or outer shell cells."),
                    DecorationEditorTheme.ToolButton(_draft.GeneratorFillMode == SmartBuildGeneratorFillMode.OutlineShell),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedFillMode(SmartBuildGeneratorFillMode.OutlineShell);
            }

            if (SmartGUILayoutButton(
                    new GUIContent("Filled", "Fill the generated shape interior."),
                    DecorationEditorTheme.ToolButton(_draft.GeneratorFillMode == SmartBuildGeneratorFillMode.Filled),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedFillMode(SmartBuildGeneratorFillMode.Filled);
            }

            GUILayout.EndHorizontal();

            if (_draft.ShapeKind == SmartBuildShapeKind.GeneratedPolygon)
                DrawPolygonSideControls();

            if (_draft.ShapeKind == SmartBuildShapeKind.GeneratedArc)
                DrawArcSweepControls();

            if (_draft.ShapeKind == SmartBuildShapeKind.GeneratedFrustum)
                DrawFrustumTopControls();

            if (SmartBuildPiece.SupportsRoundGeneratorLock(_draft.ShapeKind))
            {
                if (SmartGUILayoutButton(
                        new GUIContent(
                            _draft.GeneratorRoundLock ? "Round lock: on" : "Round lock: off",
                            "Keep circle/sphere radii equal while resizing."),
                        DecorationEditorTheme.ToolButton(_draft.GeneratorRoundLock),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    ToggleGeneratedRoundLock();
                }
            }

            if (SmartGUILayoutButton(
                    new GUIContent(
                        _draft.GeneratorSmoothingMode == SmartBuildGeneratorSmoothingMode.PerimeterDownSlope
                            ? "Smooth: perimeter"
                            : "Smooth: off",
                        "Use 1m down slopes on visible perimeter cells when this material supports them."),
                    DecorationEditorTheme.ToolButton(_draft.GeneratorSmoothingMode == SmartBuildGeneratorSmoothingMode.PerimeterDownSlope),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                ToggleGeneratedSmoothing();
            }
        }

        private void DrawArcSweepControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sweep", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(42f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-15", "Reduce arc sweep by 15 degrees."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedArcDegrees(_draft.GeneratorArcDegrees - 15);
            }
            GUILayout.Label(
                _draft.GeneratorArcDegrees.ToString(CultureInfo.InvariantCulture) + "°",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(38f)));
            if (SmartGUILayoutButton(
                    new GUIContent("+15", "Increase arc sweep by 15 degrees."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedArcDegrees(_draft.GeneratorArcDegrees + 15);
            }
            foreach (int preset in new[] { 90, 180, 270, 360 })
            {
                if (SmartGUILayoutButton(
                        new GUIContent(preset.ToString(CultureInfo.InvariantCulture), preset + " degree arc."),
                        DecorationEditorTheme.ToolButton(_draft.GeneratorArcDegrees == preset),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    SetGeneratedArcDegrees(preset);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawFrustumTopControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Top %", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(42f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-5", "Narrow the frustum top radius."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedTopScale(_draft.GeneratorTopScalePercent - 5);
            }
            GUILayout.Label(
                _draft.GeneratorTopScalePercent.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(32f)));
            if (SmartGUILayoutButton(
                    new GUIContent("+5", "Widen the frustum top radius."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedTopScale(_draft.GeneratorTopScalePercent + 5);
            }
            foreach (int preset in new[] { 25, 50, 75 })
            {
                if (SmartGUILayoutButton(
                        new GUIContent(preset.ToString(CultureInfo.InvariantCulture), preset + "% top radius."),
                        DecorationEditorTheme.ToolButton(_draft.GeneratorTopScalePercent == preset),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    SetGeneratedTopScale(preset);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void SetGeneratedArcDegrees(int degrees)
        {
            if (_draft?.ShapeKind != SmartBuildShapeKind.GeneratedArc)
                return;

            int clamped = Mathf.Clamp(degrees, 15, 360);
            if (_draft.GeneratorArcDegrees == clamped)
                return;
            RecordSceneHistory();
            _draft.SetGeneratorArcDegrees(clamped);
            RebuildPlan();
            RefreshApplyCancelAttention();
        }

        private void SetGeneratedTopScale(int percent)
        {
            if (_draft?.ShapeKind != SmartBuildShapeKind.GeneratedFrustum)
                return;

            int clamped = Mathf.Clamp(percent, 5, 95);
            if (_draft.GeneratorTopScalePercent == clamped)
                return;
            RecordSceneHistory();
            _draft.SetGeneratorTopScalePercent(clamped);
            RebuildPlan();
            RefreshApplyCancelAttention();
        }

        private void DrawPolygonSideControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sides", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(42f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-", "Reduce polygon sides."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(30f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedSides(_draft.GeneratorSides - 1);
            }

            GUILayout.Label(
                _draft.GeneratorSides.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(34f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));

            if (SmartGUILayoutButton(
                    new GUIContent("+", "Increase polygon sides."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(30f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratedSides(_draft.GeneratorSides + 1);
            }

            foreach (int preset in new[] { 3, 4, 6, 8, 10 })
            {
                if (SmartGUILayoutButton(
                        new GUIContent(preset.ToString(CultureInfo.InvariantCulture), preset.ToString(CultureInfo.InvariantCulture) + "-sided polygon preset."),
                        DecorationEditorTheme.ToolButton(_draft.GeneratorSides == preset),
                        GUILayout.Width(EsuHudLayout.Scale(32f)),
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    SetGeneratedSides(preset);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void SetGeneratedFillMode(SmartBuildGeneratorFillMode mode)
        {
            if (_draft?.IsGeneratedShape != true ||
                _draft.GeneratorFillMode == mode)
            {
                return;
            }

            RecordSceneHistory();
            _draft.SetGeneratorFillMode(mode);
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder generator fill: " + (mode == SmartBuildGeneratorFillMode.Filled ? "Filled" : "Shell") + ".");
        }

        private void ToggleGeneratedSmoothing()
        {
            if (_draft?.IsGeneratedShape != true)
                return;

            SmartBuildGeneratorSmoothingMode next =
                _draft.GeneratorSmoothingMode == SmartBuildGeneratorSmoothingMode.PerimeterDownSlope
                    ? SmartBuildGeneratorSmoothingMode.None
                    : SmartBuildGeneratorSmoothingMode.PerimeterDownSlope;
            RecordSceneHistory();
            _draft.SetGeneratorSmoothingMode(next);
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder generator smoothing: " + (next == SmartBuildGeneratorSmoothingMode.PerimeterDownSlope ? "Perimeter down slopes" : "Off") + ".");
        }

        private void ToggleGeneratedRoundLock()
        {
            if (_draft?.IsGeneratedShape != true)
                return;

            RecordSceneHistory();
            _draft.SetGeneratorRoundLock(!_draft.GeneratorRoundLock);
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder generator round lock: " + (_draft.GeneratorRoundLock ? "on" : "off") + ".");
        }

        private void SetGeneratedSides(int sides)
        {
            if (_draft?.ShapeKind != SmartBuildShapeKind.GeneratedPolygon)
                return;

            int clamped = Mathf.Clamp(sides, 1, 64);
            if (_draft.GeneratorSides == clamped)
                return;

            RecordSceneHistory();
            _draft.SetGeneratorSides(clamped);
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder polygon sides: " + clamped.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private void DrawSmartPanelHeader(
            string text,
            string iconKey,
            ref bool panelVisible,
            string tooltip)
        {
            EsuHudChrome.DrawPanelHeader(
                text,
                iconKey,
                ref panelVisible,
                tooltip);
            EsuCursorTooltip.RegisterLast(tooltip);
        }

        private bool DrawSmartSectionHeader(string text, ref bool sectionVisible, string tooltip)
        {
            bool visible = EsuHudChrome.DrawSectionHeader(
                text,
                ref sectionVisible,
                tooltip);
            EsuCursorTooltip.RegisterLast(tooltip);
            return visible;
        }

        private void DrawStatusStrip(int id)
        {
            EsuHudChrome.DrawPanel(new Rect(0f, 0f, _statusRect.width, _statusRect.height));
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
            float gap = EsuHudLayout.Scale(6f);
            float buttonWidth = Mathf.Min(EsuHudLayout.Scale(34f), rect.width * 0.12f);
            float titleWidth = Mathf.Min(EsuHudLayout.Scale(168f), rect.width * 0.36f);
            float stateWidth = Mathf.Min(EsuHudLayout.Scale(118f), rect.width * 0.32f);
            Rect gizmoSettings = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect title = new Rect(gizmoSettings.xMax + gap, rect.y, titleWidth, rect.height);
            Rect state = new Rect(rect.xMax - stateWidth, rect.y, stateWidth, rect.height);
            Rect mode = new Rect(
                title.xMax + gap,
                rect.y,
                Mathf.Max(1f, state.x - title.xMax - gap * 2f),
                rect.height);

            _gizmoSettingsButtonScreenRect = new Rect(
                _statusRect.x + gizmoSettings.x,
                _statusRect.y + gizmoSettings.y,
                gizmoSettings.width,
                gizmoSettings.height);
            DrawSmartBottomGizmoSettingsButton(gizmoSettings);
            GUI.Label(title, "Smart Block Builder", DecorationEditorTheme.SubHeader);
            GUI.Label(mode, "Mode: Smart | Tab to Decoration when clean", DecorationEditorTheme.Body);
            DrawStatusRightLabel(state);
        }

        private void DrawSmartBottomGizmoSettingsButton(Rect rect)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && !HasActiveSmartGizmoDrag();
            if (SmartGUIButton(
                    rect,
                    new GUIContent(
                        string.Empty,
                        DecorationEditorIconCatalog.Get("settings"),
                        "Open shared move, rotate, scale, thickness, and click-area gizmo settings."),
                    DecorationEditorTheme.ToolButton(_gizmoSettingsOpen)))
            {
                if (_gizmoSettingsOpen)
                {
                    CloseGizmoSettingsMenu();
                }
                else
                {
                    _gizmoSettingsOpen = true;
                    SyncGizmoSettingsText();
                    _viewModeMenuOpen = false;
                    _contextMenuOpen = false;
                }
            }
            GUI.enabled = previous;
        }

        private bool HasActiveSmartGizmoDrag() =>
            _dragging || _rotating || _patternViewportDragging;

        private Rect GizmoSettingsMenuRect()
        {
            float margin = EsuHudLayout.Scale(8f);
            float maxWidth = Mathf.Max(1f, Screen.width - margin * 2f);
            float width = Mathf.Clamp(
                EsuHudLayout.Scale(720f),
                Mathf.Min(EsuHudLayout.Scale(420f), maxWidth),
                maxWidth);
            float height = EsuHudLayout.Scale(122f);
            Rect button = _gizmoSettingsButtonScreenRect.width > 1f
                ? _gizmoSettingsButtonScreenRect
                : new Rect(
                    margin,
                    Screen.height - height - margin,
                    EsuHudLayout.Scale(34f),
                    EsuHudLayout.Scale(24f));
            float x = Mathf.Clamp(
                button.x,
                margin,
                Mathf.Max(margin, Screen.width - width - margin));
            float y = Mathf.Clamp(
                button.y - height - EsuHudLayout.Scale(4f),
                margin,
                Mathf.Max(margin, Screen.height - height - margin));
            return new Rect(x, y, width, height);
        }

        private void CloseGizmoSettingsMenu()
        {
            _gizmoSettingsOpen = false;
            try { GUIUtility.keyboardControl = 0; }
            catch { }
        }

        private void DrawGizmoSettingsMenu()
        {
            if (!_gizmoSettingsOpen)
                return;

            GUI.ModalWindow(
                _gizmoSettingsWindowId,
                GizmoSettingsMenuRect(),
                DrawGizmoSettingsWindow,
                GUIContent.none,
                GUIStyle.none);
        }

        private void DrawGizmoSettingsWindow(int id)
        {
            Rect screenRect = GizmoSettingsMenuRect();
            Rect rect = new Rect(0f, 0f, screenRect.width, screenRect.height);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(6f);
            float headerHeight = EsuHudLayout.Scale(22f);
            float fieldRowY = padding + headerHeight;
            float fieldRowHeight = EsuHudLayout.Scale(46f);
            float available = rect.width - padding * 2f - gap * 4f;
            float cellWidth = Mathf.Max(1f, available / 5f);
            GUI.Label(
                new Rect(padding, padding, rect.width - padding * 2f, headerHeight),
                "Gizmo settings (shared across ESU editors; visual size does not change sensitivity)",
                DecorationEditorTheme.SubHeader);

            DrawGizmoSettingField(
                new Rect(padding, fieldRowY, cellWidth, fieldRowHeight),
                "Move size",
                ref _gizmoMoveSizeText,
                "0.5-3.0x Smart Builder move gizmo length.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap), fieldRowY, cellWidth, fieldRowHeight),
                "Rotate size",
                ref _gizmoRotateSizeText,
                "0.5-3.0x Smart Builder rotation ring radius.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 2f, fieldRowY, cellWidth, fieldRowHeight),
                "Scale size",
                ref _gizmoScaleSizeText,
                "0.5-3.0x Smart Builder scale gizmo length.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 3f, fieldRowY, cellWidth, fieldRowHeight),
                "Thickness",
                ref _gizmoThicknessText,
                "0.5-3.0x Smart Builder gizmo line thickness.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 4f, fieldRowY, cellWidth, fieldRowHeight),
                "Click area px",
                ref _gizmoHitAreaText,
                "8-40 pixel full-shaft click radius.");

            bool previous = GUI.enabled;
            GUI.enabled = previous && !HasActiveSmartGizmoDrag();
            float buttonY = rect.height - padding - EsuHudLayout.Scale(26f);
            float buttonWidth = EsuHudLayout.Scale(112f);
            if (SmartGUIButton(
                    new Rect(padding, buttonY, buttonWidth, EsuHudLayout.Scale(24f)),
                    new GUIContent("Set", "Clamp, save, and apply these shared profile preferences."),
                    DecorationEditorTheme.Button))
            {
                ApplyGizmoSettingsText();
            }
            if (SmartGUIButton(
                    new Rect(
                        padding + buttonWidth + gap,
                        buttonY,
                        EsuHudLayout.Scale(132f),
                        EsuHudLayout.Scale(24f)),
                    new GUIContent("Reset defaults", "Restore 1x sizes and thickness with an 18 px click area."),
                    DecorationEditorTheme.Button))
            {
                bool saved = EsuGizmoSettings.ResetDefaults();
                SyncGizmoSettingsText();
                InfoStore.Add(saved
                    ? "Gizmo settings reset to defaults and saved."
                    : "Gizmo defaults applied for this session, but the profile could not be saved.");
            }
            if (SmartGUIButton(
                    new Rect(
                        rect.width - padding - EsuHudLayout.Scale(72f),
                        buttonY,
                        EsuHudLayout.Scale(72f),
                        EsuHudLayout.Scale(24f)),
                    new GUIContent("Close", "Close Gizmo settings without applying unsaved typed values."),
                    DecorationEditorTheme.Button))
            {
                CloseGizmoSettingsMenu();
            }
            GUI.enabled = previous;
        }

        private static void DrawGizmoSettingField(
            Rect rect,
            string label,
            ref string text,
            string tooltip)
        {
            GUI.Label(
                new Rect(rect.x, rect.y, rect.width, EsuHudLayout.Scale(18f)),
                label,
                DecorationEditorTheme.Mini);
            Rect field = new Rect(
                rect.x,
                rect.y + EsuHudLayout.Scale(19f),
                rect.width,
                EsuHudLayout.Scale(23f));
            text = GUI.TextField(field, text ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.Register(field, tooltip);
        }

        private void SyncGizmoSettingsText()
        {
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            _gizmoMoveSizeText = EsuGizmoSettings.FormatMultiplier(style.MoveSize);
            _gizmoRotateSizeText = EsuGizmoSettings.FormatMultiplier(style.RotateSize);
            _gizmoScaleSizeText = EsuGizmoSettings.FormatMultiplier(style.ScaleSize);
            _gizmoThicknessText = EsuGizmoSettings.FormatMultiplier(style.Thickness);
            _gizmoHitAreaText = EsuGizmoSettings.FormatPixels(style.HitAreaPixels);
        }

        private void ApplyGizmoSettingsText()
        {
            if (!FlexibleFloatParser.TryParse(_gizmoMoveSizeText, out float move) ||
                !FlexibleFloatParser.TryParse(_gizmoRotateSizeText, out float rotate) ||
                !FlexibleFloatParser.TryParse(_gizmoScaleSizeText, out float scale) ||
                !FlexibleFloatParser.TryParse(_gizmoThicknessText, out float thickness) ||
                !FlexibleFloatParser.TryParse(_gizmoHitAreaText, out float hitArea))
            {
                InfoStore.Add("Gizmo settings must be finite numbers.");
                SyncGizmoSettingsText();
                return;
            }

            bool saved = EsuGizmoSettings.Set(move, rotate, scale, thickness, hitArea);
            SyncGizmoSettingsText();
            InfoStore.Add(
                (saved
                    ? "Gizmo settings saved: move "
                    : "Gizmo settings applied for this session (profile save failed): move ") +
                _gizmoMoveSizeText + "x, rotate " + _gizmoRotateSizeText +
                "x, scale " + _gizmoScaleSizeText +
                "x, thickness " + _gizmoThicknessText +
                "x, click area " + _gizmoHitAreaText + "px.");
        }

        private static void ConsumeGizmoSettingsInput(Event current)
        {
            if (current == null)
                return;

            if (!DecoLimitLifter.EsuModalInputPolicy.IsBlockingEventType(current.type))
                return;

            if (current.type == EventType.ScrollWheel)
            {
                SmartBuildInputScope.ClaimMouseWheelInputForFrames();
            }
            else
            {
                SmartBuildInputScope.ClaimBuildInputForFrames();
                SmartBuildInputScope.ClaimCameraInputForFrames();
            }

            if (current.type != EventType.Used)
                current.Use();
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
                text = "Blocked";
                style = DecorationEditorTheme.Warning;
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
                    "Scale step in whole construct cells for Smart Builder previews.",
                    out bool commitRequested))
            {
                ApplySmartSnapText(commitRequested);
            }
        }

        private void SyncSnapTextFromSettings()
        {
            _snapMoveText = EsuTransformSnapSettings.Format(SmartMoveStepCells);
            _snapRotateText = EsuTransformSnapSettings.Format(SmartRotateSnapDegrees);
            _snapScaleText = EsuTransformSnapSettings.Format(SmartScaleStepCells);
        }

        private void ApplySmartSnapText(bool commitRequested)
        {
            if (!EsuTransformSnapSettings.TryParseSnapText(_snapMoveText, out float move) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapRotateText, out float rotate) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapScaleText, out float scale))
            {
                if (commitRequested)
                {
                    InfoStore.Add("Smart Builder snap settings must be finite numbers.");
                    SyncSnapTextFromSettings();
                }
                return;
            }

            EsuTransformSnapSettings.SetSmart(move, rotate, scale);
            if (!commitRequested)
                return;

            SyncSnapTextFromSettings();
            EsuTransformSnapSettings.SaveProfileBestEffort();
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
            x = PreviewModeStripButton(new Rect(x, y, EsuHudLayout.Scale(82f), buttonHeight), SmartBuildPreviewMode.Material, "Material", "Draw solid material-family ghost faces with the wireframe overlay.") + gap;
            x = PreviewModeStripButton(new Rect(x, y, EsuHudLayout.Scale(96f), buttonHeight), SmartBuildPreviewMode.MaterialOnly, "Mat only", "Draw material-family ghost faces without the wireframe overlay.");

            if (_selectedShape == SmartBuildShapeKind.DownSlope ||
                _draft?.ShapeKind == SmartBuildShapeKind.DownSlope)
            {
                x += groupGap;
                Rect support = new Rect(x, y, EsuHudLayout.Scale(116f), buttonHeight);
                if (support.xMax <= rect.xMax &&
                    SmartGUIButton(
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
            if (SmartGUIButton(
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
            if (SmartGUIButton(
                    rect,
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(_previewMode == mode)))
            {
                if (_previewMode != mode)
                {
                    _previewMode = mode;
                    RegisterPresentationRevision();
                }
                InfoStore.Add("Smart Builder preview: " + PreviewModeLabel(mode) + ".");
            }

            return rect.xMax;
        }

        private static string PreviewModeLabel(SmartBuildPreviewMode mode)
        {
            switch (mode)
            {
                case SmartBuildPreviewMode.Material:
                    return "Material";
                case SmartBuildPreviewMode.MaterialOnly:
                    return "Material only";
                default:
                    return "Wireframe";
            }
        }

        private bool ShouldDrawMaterialPreview() =>
            _previewMode == SmartBuildPreviewMode.Material ||
            _previewMode == SmartBuildPreviewMode.MaterialOnly;

        private bool ShouldDrawPreviewWire() =>
            _previewMode != SmartBuildPreviewMode.MaterialOnly;

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
            SmartBuildShapeDescriptor descriptor = SelectedShapeDescriptor;
            if (descriptor.UsesLengthSelector)
            {
                return _selectedSlopeLength.ToString(CultureInfo.InvariantCulture) + "m " +
                       descriptor.Label.ToLowerInvariant();
            }

            if (descriptor != null && !descriptor.IsCuboid)
                return descriptor.Label;

            return "Block";
        }

        private string ToolDisplayName() =>
            _tool == SmartBuildTool.Draw ? "Add" : _tool.ToString();

        private void ToolButton(
            SmartBuildTool tool,
            string icon,
            string label,
            string compactLabel,
            string tooltip,
            float controlWidth)
        {
            if (ToolbarIconButton(
                    icon,
                    label,
                    compactLabel,
                    DecorationEditorTheme.ToolButton(_tool == tool),
                    tooltip,
                    controlWidth))
                SetActiveTool(tool);
        }

        private void SetActiveTool(SmartBuildTool tool)
        {
            _tool = tool;
            _viewModeMenuOpen = false;
        }

        private void PlaneButton(float controlWidth)
        {
            if (ToolbarIconButton(
                    "axis",
                    "Plane " + PlaneShortName(),
                    PlaneShortName(),
                    DecorationEditorTheme.Button,
                    "Cycle the free-space draw plane.",
                    controlWidth))
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

        private void OccupancyButton(float controlWidth)
        {
            string label;
            string compactLabel;
            switch (_occupancyMode)
            {
                case SmartBuildOccupancyMode.BlockOnOverlap:
                    label = "Block";
                    compactLabel = "B";
                    break;
                case SmartBuildOccupancyMode.ReplaceOccupied:
                    label = "Replace";
                    compactLabel = "R";
                    break;
                case SmartBuildOccupancyMode.EraseOccupied:
                    label = "Erase";
                    compactLabel = "E";
                    break;
                default:
                    label = "Skip";
                    compactLabel = "S";
                    break;
            }
            if (ToolbarIconButton(
                    "filter",
                    label,
                    compactLabel,
                    DecorationEditorTheme.Button,
                    "Toggle occupied-cell handling. Cycle Place/Skip, Block on overlap, transactional Replace, and transactional Erase.",
                    controlWidth))
            {
                _occupancyMode = (SmartBuildOccupancyMode)
                    (((int)_occupancyMode + 1) %
                     Enum.GetValues(typeof(SmartBuildOccupancyMode)).Length);
                RebuildPlan(SmartBuildPlanRevisionKind.Occupancy);
                _viewModeMenuOpen = false;
            }
        }

        private void ViewButton(float controlWidth)
        {
            if (ToolbarIconButton(
                    "visibility",
                    "View",
                    "V",
                    DecorationEditorTheme.ToolButton(_viewModeMenuOpen),
                    "Current view: " + ViewModeShortName() + ".",
                    controlWidth))
            {
                _viewModeMenuOpen = !_viewModeMenuOpen;
            }
        }

        private void DrawMaterialSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (SmartGUILayoutButton(
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
            if (SmartGUILayoutButton(
                    new GUIContent(">", "Next Smart Builder material."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                CycleSelectedMaterial(1);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent(
                        _blockEyedropperArmed ? "Pick: click craft" : "Pick craft block",
                        "Arm the Smart Builder eyedropper, then click an existing craft block to copy its material, structural shape, size, and cardinal local rotation."),
                    DecorationEditorTheme.ToolButton(_blockEyedropperArmed),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _blockEyedropperArmed = !_blockEyedropperArmed;
                if (_blockEyedropperArmed)
                {
                    _tool = SmartBuildTool.Draw;
                    InfoStore.Add("Smart Builder eyedropper armed. Click a real craft block; right-click or Escape cancels.");
                }
            }

            bool previous = GUI.enabled;
            GUI.enabled = previous && _hasSampledBlockRotation;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Clear rotation",
                        "Stop applying the sampled craft-block rotation to newly drawn Smart Builder pieces."),
                    _hasSampledBlockRotation ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(96f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _hasSampledBlockRotation = false;
                _sampledBlockRotation = Quaternion.identity;
                _sampledBlockRotationLabel = null;
                InfoStore.Add("Smart Builder sampled rotation cleared.");
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            if (_hasSampledBlockRotation)
            {
                GUILayout.Label(
                    "Sampled rotation: " + (_sampledBlockRotationLabel ?? "cardinal"),
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private bool TrySamplePointedCraftBlock()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit?.Construct == null)
            {
                InfoStore.Add("Point at an existing craft block for the Smart Builder eyedropper.");
                return false;
            }

            Block block;
            try
            {
                block = hit.Construct.AllBasics?.GetBlockViaLocalPosition(hit.Anchor);
            }
            catch
            {
                block = null;
            }

            ItemDefinition definition = null;
            try
            {
                definition = block?.item;
            }
            catch
            {
                // Unusual modded blocks are rejected conservatively below.
            }

            string reason = null;
            if (block == null || block.IsDeleted ||
                !SmartBlockFamilyCatalog.TryIdentifyBlock(
                    definition,
                    out SmartBuildMaterial material,
                    out SmartBuildSource source,
                    out SmartBlockCandidate candidate,
                    out reason))
            {
                InfoStore.Add(reason ?? "The pointed craft block cannot be mapped to a Smart Builder shape.");
                return false;
            }

            SmartBuildShapeDescriptor descriptor = candidate.Descriptor ?? SmartBuildShapeDescriptors.Cuboid;
            bool rotationReadable = false;
            Quaternion localRotation = Quaternion.identity;
            try
            {
                localRotation = block.LocalRotation;
                rotationReadable = true;
            }
            catch
            {
                // Sampling must remain armed and atomic when FtD cannot expose rotation.
            }

            Guid definitionGuid = Guid.Empty;
            try
            {
                definitionGuid = definition.ComponentId.Guid;
            }
            catch
            {
                // Reference identity still allowed the catalog match. The descriptor
                // retains Guid.Empty to make the unavailable exact ID explicit.
            }

            if (!TryCreateCraftBlockSampleDescriptor(
                    definitionGuid,
                    material,
                    candidate,
                    descriptor,
                    rotationReadable,
                    localRotation,
                    _drawPlane,
                    out SmartBuildCraftBlockSampleDescriptor sample,
                    out string sampleReason))
            {
                InfoStore.Add("Smart Builder eyedropper rejected the pointed block: " + sampleReason);
                return false;
            }

            // Every fallible read/validation is complete. Apply the sample as one state
            // transition so rejected blocks cannot partially change the active tool.
            _selectedMaterial = sample.Material;
            s_selectedMaterial = sample.Material;
            _sourceMaterial = sample.Material;
            _source = source;
            _sourceReason = null;
            _pieceMaterialSources[sample.Material] = source;
            _selectedShape = descriptor.Kind;
            _selectedShapeDescriptorKey = sample.ShapeDescriptorKey;
            _selectedSlopeLength = sample.Length;
            _sampledBlockRotation = sample.LocalRotation;
            _hasSampledBlockRotation = true;
            _sampledBlockRotationLabel = sample.RotationLabel;
            _sampledCuboidSize = sample.IsCuboid
                ? sample.OccupiedSize
                : (Vector3i?)null;
            _lastCraftBlockSample = sample;
            InvalidateConditionalReplacementPreview();
            ClearSmartPreviewRendererCache();
            _blockEyedropperArmed = false;
            ArmAddMode();
            InfoStore.Add(
                "Smart Builder picked " + SmartBlockFamilyCatalog.ItemName(definition) +
                ": " + SmartBlockFamilyCatalog.MaterialDisplayName(material) +
                " " + descriptor.Label +
                (descriptor.UsesLengthSelector || candidate.Length > 1
                    ? " " + candidate.Length.ToString(CultureInfo.InvariantCulture) + "m"
                    : string.Empty) +
                ", exact occupied size " + FormatCell(sample.OccupiedSize) +
                ", and cardinal local rotation.");
            return true;
        }

        internal static bool TryCreateCraftBlockSampleDescriptor(
            Guid definitionGuid,
            SmartBuildMaterial material,
            SmartBlockCandidate candidate,
            SmartBuildShapeDescriptor descriptor,
            bool rotationReadable,
            Quaternion localRotation,
            SmartBuildDrawPlane drawPlane,
            out SmartBuildCraftBlockSampleDescriptor sample,
            out string reason)
        {
            sample = null;
            descriptor ??= SmartBuildShapeDescriptors.Cuboid;
            if (candidate == null)
            {
                reason = "the item has no supported structural candidate.";
                return false;
            }
            if (candidate.Length < 1 || candidate.Length > 4)
            {
                reason = "the sampled block length is outside the supported 1-4m range.";
                return false;
            }
            if (!rotationReadable)
            {
                reason = "its local rotation metadata could not be read; the picker remains armed.";
                return false;
            }
            float rotationMagnitudeSquared =
                localRotation.x * localRotation.x +
                localRotation.y * localRotation.y +
                localRotation.z * localRotation.z +
                localRotation.w * localRotation.w;
            if (float.IsNaN(rotationMagnitudeSquared) ||
                float.IsInfinity(rotationMagnitudeSquared) ||
                rotationMagnitudeSquared < 0.9f ||
                rotationMagnitudeSquared > 1.1f)
            {
                reason = "its local rotation is unreadable or non-normalized; the picker remains armed.";
                return false;
            }

            SmartBuildPiece orientationProbe = SmartBuildPiece.CreateFixedShapePreview(
                null,
                default,
                descriptor,
                candidate.Length,
                SmartBuildAxis.Z,
                1,
                1,
                drawPlane);
            if (!orientationProbe.TrySetOrientationFromRotation(localRotation))
            {
                reason = "its local rotation is not a finite cardinal orientation; the picker remains armed.";
                return false;
            }

            Vector3i[] occupied;
            try
            {
                occupied = candidate.CoveredCellsFrom(default, localRotation)
                    .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                    .Select(group => group.First())
                    .ToArray();
            }
            catch
            {
                occupied = Array.Empty<Vector3i>();
            }
            if (occupied.Length == 0 || occupied.Length > 64)
            {
                reason = "its occupied-cell metadata is empty or exceeds the safe sample bound.";
                return false;
            }

            int minX = occupied.Min(cell => cell.x);
            int minY = occupied.Min(cell => cell.y);
            int minZ = occupied.Min(cell => cell.z);
            int maxX = occupied.Max(cell => cell.x);
            int maxY = occupied.Max(cell => cell.y);
            int maxZ = occupied.Max(cell => cell.z);
            var occupiedSize = new Vector3i(
                maxX - minX + 1,
                maxY - minY + 1,
                maxZ - minZ + 1);
            if (occupiedSize.x < 1 || occupiedSize.y < 1 || occupiedSize.z < 1 ||
                occupiedSize.x > 4 || occupiedSize.y > 4 || occupiedSize.z > 4)
            {
                reason = "its occupied bounds are outside the supported 1-4m sample range.";
                return false;
            }

            sample = new SmartBuildCraftBlockSampleDescriptor(
                definitionGuid,
                material,
                descriptor.Key,
                candidate.Length,
                occupiedSize,
                new Vector3i(minX, minY, minZ),
                localRotation,
                orientationProbe.ForwardAxis,
                orientationProbe.ForwardSign,
                orientationProbe.RightAxis,
                orientationProbe.RightSign,
                orientationProbe.DropAxis,
                orientationProbe.DropSign);
            reason = null;
            return true;
        }

        private void ApplySampledBlockRotation(SmartBuildPiece piece)
        {
            if (!_hasSampledBlockRotation || piece == null ||
                !piece.TrySetOrientationFromRotation(_sampledBlockRotation))
            {
                return;
            }
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

            IReadOnlyList<SmartBuildPiece> selectedPieces = _scene?.SelectedPieces;
            if (selectedPieces != null && selectedPieces.Count > 0)
            {
                RecordSceneHistory();
                foreach (SmartBuildPiece piece in selectedPieces)
                    piece.SetMaterialOverride(material);
            }

            _selectedMaterial = material;
            s_selectedMaterial = material;
            _source = null;
            _sourceReason = null;
            ClearSmartPreviewRendererCache();
            RefreshSelection();
            if (selectedPieces != null && selectedPieces.Count > 0)
            {
                InfoStore.Add(
                    selectedPieces.Count == 1
                        ? "Selected Smart Builder piece changed to " +
                          SmartBlockFamilyCatalog.MaterialDisplayName(material) + "."
                        : selectedPieces.Count.ToString(CultureInfo.InvariantCulture) +
                          " selected Smart Builder pieces changed to " +
                          SmartBlockFamilyCatalog.MaterialDisplayName(material) + ".");
            }
        }

        private static string OccupancyDisplayName(SmartBuildOccupancyMode mode)
        {
            switch (mode)
            {
                case SmartBuildOccupancyMode.BlockOnOverlap:
                    return "Place | block on overlap";
                case SmartBuildOccupancyMode.ReplaceOccupied:
                    return "Replace occupied";
                case SmartBuildOccupancyMode.EraseOccupied:
                    return "Erase touched blocks";
                default:
                    return "Place | skip occupied";
            }
        }

        private void ClearSmartPreviewRendererCache()
        {
            _itemPreviewRenderer?.ClearCache();
            _shapePaletteScroll = Vector2.zero;
            _generatorPaletteScroll = Vector2.zero;
            _hoveredShapeEntry = default;
        }

        private void SymmetryButton(
            DecorationEditAxis axis,
            float controlWidth)
        {
            bool active = DecoLimitLifter.EsuSymmetry.IsActive(axis) ||
                          DecoLimitLifter.EsuSymmetry.IsPending(axis);
            string label = DecoLimitLifter.EsuSymmetry.AxisName(axis);
            string tooltip = DecoLimitLifter.EsuSymmetry.IsPending(axis)
                ? "Click to cancel placing this symmetry plane."
                : DecoLimitLifter.EsuSymmetry.IsActive(axis)
                    ? "Click to clear this symmetry plane."
                    : "Click, then click the construct grid to place this symmetry plane.";
            if (ToolbarIconButton(
                    SymmetryIconKey(axis),
                    label,
                    label,
                    DecorationEditorTheme.ToolButton(active),
                    tooltip,
                    controlWidth))
            {
                string activeSymmetryBefore = ActiveSymmetryPlanStamp();
                DecoLimitLifter.EsuSymmetry.ToggleAxis(axis);
                HandleSymmetryRevision(activeSymmetryBefore);
                _viewModeMenuOpen = false;
                InfoStore.Add(
                    active && !DecoLimitLifter.EsuSymmetry.IsPending(axis)
                        ? "Symmetry " + label + " updated."
                        : "Click the construct grid to place the " + label + " symmetry plane.");
            }
        }

        private static bool SmartGUILayoutButton(
            GUIContent content,
            GUIStyle style,
            params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(content, style, options);
            EsuCursorTooltip.RegisterLast(content?.tooltip);
            return clicked;
        }

        private static string SymmetryIconKey(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return "symmetryX";
                case DecorationEditAxis.Y:
                    return "symmetryY";
                default:
                    return "symmetryZ";
            }
        }

        private static bool SmartGUIButton(
            Rect rect,
            GUIContent content,
            GUIStyle style)
        {
            bool clicked = GUI.Button(rect, content, style);
            EsuCursorTooltip.Register(rect, content?.tooltip);
            return clicked;
        }

        private bool ToolbarIconButton(
            string icon,
            string label,
            string compactLabel,
            GUIStyle style,
            string tooltip,
            float controlWidth,
            bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            try
            {
                string resolvedLabel = EsuHudLayout.ToolbarLabel(
                    label,
                    compactLabel,
                    controlWidth);
                return SmartGUILayoutButton(
                           new GUIContent(
                               resolvedLabel,
                               DecorationEditorIconCatalog.Get(icon),
                               tooltip),
                           enabled ? style : DecorationEditorTheme.DisabledButton,
                           GUILayout.Width(Mathf.Max(1f, controlWidth)),
                           GUILayout.Height(EsuHudLayout.Scale(40f))) &&
                       enabled;
            }
            finally
            {
                GUI.enabled = previous;
            }
        }

        private bool ToolbarAttentionIconButton(
            string icon,
            string label,
            string compactLabel,
            GUIStyle style,
            string tooltip,
            float controlWidth,
            bool enabled = true)
        {
            bool clicked = ToolbarIconButton(
                icon,
                label,
                compactLabel,
                style,
                tooltip,
                controlWidth,
                enabled);
            EsuToolbarAttention.DrawLastButtonPulse(ApplyCancelAttentionActive);
            return clicked;
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
            bool clicked = SmartGUILayoutButton(
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
            bool clicked = SmartGUILayoutButton(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                enabled ? style : DecorationEditorTheme.DisabledButton,
                GUILayout.Width(EsuHudLayout.Scale(44f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));
            return clicked && enabled;
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
            {
                if (_plan.CommitOperation == SmartBuildCommitOperation.Erase)
                {
                    LabelRow(
                        "Plan",
                        $"erase {_plan.RemovalItems.Count:N0} complete item(s) / {_plan.RemovalFootprintCells.Count:N0} cells");
                }
                else if (_plan.CommitOperation == SmartBuildCommitOperation.Replace)
                {
                    LabelRow(
                        "Plan",
                        $"{_plan.EstimatedBlockCount:N0} placements / replace {_plan.RemovalItems.Count:N0} complete item(s) ({_plan.RemovalFootprintCells.Count:N0} cells)");
                }
                else
                {
                    LabelRow("Plan", $"{_plan.EstimatedBlockCount:N0} placements / {_plan.CoveredCellCount:N0} cells");
                }
            }
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
            if (_previewCells.Count > 0)
                return _previewCells.Count;
            IReadOnlyList<SmartBuildPiece> previewPieces = _scene.Pieces;
            if (_scene.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> expanded, out _, out _))
                previewPieces = expanded;
            return previewPieces.Sum(
                piece => piece.EnumeratePreviewCells(SourceForPiece(piece)).Count());
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
            bool drewExactMaterialMeshes =
                !_planDirty &&
                ShouldDrawMaterialPreview() &&
                _plan?.Placements.Count > 0;
            if (!_planDirty &&
                ShouldDrawMaterialPreview() &&
                _plan?.Placements.Count > 0)
            {
                DrawPlanPlacementPreview(_plan, invalid, drawWire: false);
            }

            var drawn = new HashSet<SmartBuildPreviewDrawKey>();
            IReadOnlyList<SmartBuildPiece> previewPieces = PreviewPiecesForRendering();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                foreach (SmartBuildPiece piece in previewPieces)
                    DrawPiecePreview(piece, variant, invalid, drawn, drawMaterialFill: !drewExactMaterialMeshes);
            }
        }

        private void DrawPiecePreview(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            bool invalid,
            HashSet<SmartBuildPreviewDrawKey> drawn,
            bool drawMaterialFill = true)
        {
            if (piece?.Construct == null)
                return;

            Vector3i[] occupiedCells = CachedPreviewCells(
                piece,
                variant,
                out SmartBuildCellSetFingerprint cellFingerprint);
            if (occupiedCells.Length == 0)
                return;

            if (!drawn.Add(new SmartBuildPreviewDrawKey(piece.Id, cellFingerprint)))
                return;

            bool selectedOriginal = _scene?.IsSelected(piece.Id) == true &&
                                    !variant.Axes.Any();
            bool primaryOriginal = selectedOriginal &&
                                   _draft != null &&
                                   _draft.Id == piece.Id;
            Color color = invalid
                ? new Color(1f, 0.2f, 0.15f, 1f)
                : primaryOriginal
                    ? new Color(1f, 1f, 0.2f, 1f)
                    : selectedOriginal
                        ? new Color(1f, 0.66f, 0.12f, 1f)
                    : new Color(0.1f, 0.95f, 1f, 1f);
            float width = primaryOriginal ? 3.6f : selectedOriginal ? 3.1f : 2.6f;

            if (piece.IsGeneratedShape)
            {
                DrawGeneratedPieceHull(
                    piece.Construct,
                    occupiedCells,
                    cellFingerprint,
                    color,
                    width,
                    SolidMaterialPreviewColor(MaterialForPiece(piece), selectedOriginal),
                    drawMaterialFill);
                return;
            }

            if (piece.IsFixedGeometry &&
                TryDrawFixedGeometryPiecePreview(
                    piece,
                    variant,
                    occupiedCells,
                    color,
                    width,
                    drawMaterialFill))
            {
                return;
            }

            if (piece.ShapeKind != SmartBuildShapeKind.DownSlope)
            {
                SmartBuildVolume volume = VolumeFromCells(piece.Construct, occupiedCells);
                if (volume != null)
                {
                    if (ShouldDrawMaterialPreview() && drawMaterialFill)
                        DrawVolumeFaces(
                            volume.GetWorldCorners(),
                            SolidMaterialPreviewColor(MaterialForPiece(piece), selectedOriginal));
                    if (ShouldDrawPreviewWire())
                        DrawWireEdges(volume.GetWorldCorners(), color, width);
                }
                return;
            }

            DrawSlopePieceHulls(piece, variant, color, width, drawMaterialFill);

            Color supportColor = invalid
                ? new Color(1f, 0.2f, 0.15f, 0.38f)
                : new Color(0.58f, 0.8f, 0.9f, selectedOriginal ? 0.42f : 0.28f);
            if (ShouldDrawPreviewWire())
            {
                DrawSupportHulls(
                    piece.Construct,
                    piece.EnumerateSupportCells().Select(variant.Mirror),
                    supportColor,
                    selectedOriginal ? 1.6f : 1.2f);
            }
        }

        private bool TryDrawFixedGeometryPiecePreview(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            Vector3i[] occupiedCells,
            Color wireColor,
            float width,
            bool drawMaterialFill)
        {
            SmartBuildSource pieceSource = SourceForPiece(piece);
            IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(
                pieceSource,
                out _);
            if (placements.Count == 0)
                return false;

            var mirroredPlacements = new List<SmartBuildPlacement>(placements.Count);
            for (int index = 0; index < placements.Count; index++)
            {
                SmartBuildPlacement mirrored = SmartBuildPieceScene.MirrorPlacement(
                    placements[index],
                    variant,
                    pieceSource);
                if (mirrored == null)
                    return false;

                mirroredPlacements.Add(mirrored);
            }

            IReadOnlyList<SmartBuildInternalFace> internalFaces =
                BuildInternalPreviewFaces(occupiedCells);
            var drawnEdges = new HashSet<string>();
            Color materialColor = wireColor.r > 0.9f && wireColor.g < 0.4f
                ? new Color(1f, 0.2f, 0.15f, 0.34f)
                : new Color(1f, 1f, 1f, 0.38f);
            int exact = Math.Min(mirroredPlacements.Count, MaxExactMeshPreviewPlacements);
            bool drewAnyPlacementPreview = false;
            for (int index = 0; index < exact; index++)
            {
                SmartBuildPlacement placement = mirroredPlacements[index];
                Matrix4x4 localMatrix = PlacementLocalMatrix(placement);
                Matrix4x4 worldMatrix = PlacementMatrix(piece.Construct, placement);
                if (ShouldDrawMaterialPreview() && drawMaterialFill)
                {
                    _itemPreviewRenderer?.DrawPlacementMesh(placement, worldMatrix, materialColor);
                    drewAnyPlacementPreview = true;
                }

                if (ShouldDrawPreviewWire())
                {
                    bool drewWire = _itemPreviewRenderer?.DrawPlacementWire(
                        placement,
                        worldMatrix,
                        localMatrix,
                        (start, end) => ShouldDrawFixedGeometryPreviewEdge(
                            start,
                            end,
                            internalFaces,
                            drawnEdges),
                        wireColor,
                        width) == true;
                    drewAnyPlacementPreview |= drewWire;
                }
            }

            return drewAnyPlacementPreview;
        }

        private static IReadOnlyList<SmartBuildInternalFace> BuildInternalPreviewFaces(
            IEnumerable<Vector3i> occupiedCells)
        {
            Vector3i[] cells = occupiedCells?.ToArray() ?? Array.Empty<Vector3i>();
            if (cells.Length <= 1)
                return Array.Empty<SmartBuildInternalFace>();

            var occupied = new HashSet<string>(
                cells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
            var faces = new List<SmartBuildInternalFace>();
            for (int index = 0; index < cells.Length; index++)
            {
                Vector3i cell = cells[index];
                AddInternalFaceIfOccupied(
                    faces,
                    occupied,
                    cell,
                    new Vector3i(cell.x + 1, cell.y, cell.z),
                    DecorationEditAxis.X,
                    cell.x + 0.5f,
                    DecorationEditAxis.Y,
                    cell.y - 0.5f,
                    cell.y + 0.5f,
                    DecorationEditAxis.Z,
                    cell.z - 0.5f,
                    cell.z + 0.5f);
                AddInternalFaceIfOccupied(
                    faces,
                    occupied,
                    cell,
                    new Vector3i(cell.x, cell.y + 1, cell.z),
                    DecorationEditAxis.Y,
                    cell.y + 0.5f,
                    DecorationEditAxis.X,
                    cell.x - 0.5f,
                    cell.x + 0.5f,
                    DecorationEditAxis.Z,
                    cell.z - 0.5f,
                    cell.z + 0.5f);
                AddInternalFaceIfOccupied(
                    faces,
                    occupied,
                    cell,
                    new Vector3i(cell.x, cell.y, cell.z + 1),
                    DecorationEditAxis.Z,
                    cell.z + 0.5f,
                    DecorationEditAxis.X,
                    cell.x - 0.5f,
                    cell.x + 0.5f,
                    DecorationEditAxis.Y,
                    cell.y - 0.5f,
                    cell.y + 0.5f);
            }

            return faces;
        }

        private static void AddInternalFaceIfOccupied(
            ICollection<SmartBuildInternalFace> faces,
            ICollection<string> occupied,
            Vector3i cell,
            Vector3i neighbor,
            DecorationEditAxis normalAxis,
            float coordinate,
            DecorationEditAxis axisA,
            float minA,
            float maxA,
            DecorationEditAxis axisB,
            float minB,
            float maxB)
        {
            if (faces == null || occupied == null ||
                !occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell)) ||
                !occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(neighbor)))
            {
                return;
            }

            faces.Add(new SmartBuildInternalFace(
                normalAxis,
                coordinate,
                axisA,
                minA,
                maxA,
                axisB,
                minB,
                maxB));
        }

        private static bool ShouldDrawFixedGeometryPreviewEdge(
            Vector3 start,
            Vector3 end,
            IReadOnlyList<SmartBuildInternalFace> internalFaces,
            ISet<string> drawnEdges)
        {
            if (IsAxisAlignedPreviewEdge(start, end))
            {
                for (int index = 0; index < internalFaces.Count; index++)
                {
                    if (internalFaces[index].Contains(start, end))
                        return false;
                }
            }

            return drawnEdges == null || drawnEdges.Add(EdgeKey(start, end));
        }

        private static bool IsAxisAlignedPreviewEdge(Vector3 start, Vector3 end)
        {
            const float epsilon = 0.0005f;
            Vector3 delta = end - start;
            int movingAxes = 0;
            if (Mathf.Abs(delta.x) > epsilon)
                movingAxes++;
            if (Mathf.Abs(delta.y) > epsilon)
                movingAxes++;
            if (Mathf.Abs(delta.z) > epsilon)
                movingAxes++;

            return movingAxes <= 1;
        }

        private void DrawPlanPlacementPreview(
            SmartBuildPlan plan,
            bool invalid,
            bool drawWire)
        {
            if (plan?.Construct == null || plan.Placements.Count == 0)
                return;

            Color color = invalid
                ? new Color(1f, 0.2f, 0.15f, 1f)
                : new Color(0.1f, 0.95f, 1f, 1f);
            Color materialColor = invalid
                ? new Color(1f, 0.2f, 0.15f, 0.34f)
                : new Color(1f, 1f, 1f, 0.38f);
            int exact = Math.Min(plan.Placements.Count, MaxExactMeshPreviewPlacements);
            for (int index = 0; index < exact; index++)
                DrawPlacementPreview(plan.Construct, plan.Placements[index], color, materialColor, 2.2f, drawWire);

            if (plan.Placements.Count <= exact ||
                plan.Volume == null)
                return;

            Color fallback = invalid
                ? new Color(1f, 0.2f, 0.15f, 0.65f)
                : new Color(0.1f, 0.95f, 1f, 0.58f);
            if (_scene?.Pieces.Any(piece => piece.IsGeneratedShape) == true &&
                _previewCells.Count > 0)
            {
                Color fallbackMaterial = invalid
                    ? new Color(1f, 0.2f, 0.15f, 0.24f)
                    : SolidMaterialPreviewColor(selected: false);
                DrawGeneratedPieceHull(
                    plan.Construct,
                    _previewCells,
                    default,
                    fallback,
                    1.4f,
                    fallbackMaterial,
                    drawMaterialFill: true);
                return;
            }

            Vector3[] fallbackCorners = plan.Volume.GetWorldCorners();
            if (ShouldDrawMaterialPreview())
            {
                Color fallbackMaterial = invalid
                    ? new Color(1f, 0.2f, 0.15f, 0.24f)
                    : SolidMaterialPreviewColor(selected: false);
                DrawVolumeFaces(fallbackCorners, fallbackMaterial);
            }

            if (drawWire && ShouldDrawPreviewWire())
                DrawWireEdges(fallbackCorners, fallback, 1.4f);
        }

        private void DrawPlacementPreview(
            AllConstruct construct,
            SmartBuildPlacement placement,
            Color wireColor,
            Color materialColor,
            float width,
            bool drawWire = true)
        {
            if (construct == null || placement == null)
                return;

            Matrix4x4 matrix = PlacementMatrix(construct, placement);
            if (ShouldDrawMaterialPreview())
                _itemPreviewRenderer?.DrawPlacementMesh(placement, matrix, materialColor);
            if (!drawWire || !ShouldDrawPreviewWire())
                return;
            if (_itemPreviewRenderer?.DrawPlacementWire(placement, matrix, wireColor, width) == true)
                return;

            SmartBuildVolume volume = VolumeFromCells(construct, placement.CoveredCells());
            if (volume == null)
                return;
            if (ShouldDrawMaterialPreview())
                DrawVolumeFaces(volume.GetWorldCorners(), SolidMaterialPreviewColor(selected: false));
            DrawWireEdges(volume.GetWorldCorners(), wireColor, width);
        }

        private void DrawSlopePieceHulls(
            SmartBuildPiece piece,
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
            Color color,
            float width,
            bool drawMaterialFill = true)
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
                    width,
                    drawMaterialFill);
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
            float width,
            bool drawMaterialFill = true)
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

            Color fill = ShouldDrawMaterialPreview()
                ? SolidMaterialPreviewColor(selected: false)
                : new Color(color.r, color.g, color.b, 0.12f);
            if (!ShouldDrawMaterialPreview() || drawMaterialFill)
            {
                DrawDoubleSidedSlopeQuad(world[0], world[1], world[2], world[3], fill);
                DecorationEditorOverlay.Quad(world[0], world[4], world[5], world[1], fill);
                DecorationEditorOverlay.Quad(world[3], world[2], world[6], world[7], fill);
                DecorationEditorOverlay.Quad(world[0], world[3], world[7], world[4], fill);
                DecorationEditorOverlay.Quad(world[1], world[5], world[6], world[2], fill);
                DecorationEditorOverlay.Quad(world[4], world[7], world[6], world[5], new Color(fill.r, fill.g, fill.b, fill.a * 0.55f));
            }
            if (!ShouldDrawPreviewWire())
                return;

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

        private bool DrawFixedGeometryFaceHighlight(
            DecorationEditAxis axis,
            int sign,
            Color faceColor,
            Color edgeColor,
            float width)
        {
            if (_draft?.Construct == null)
                return false;

            bool drew = false;
            foreach (SmartBuildHullFace face in EnumerateFixedGeometryHandleFaces(_draft))
            {
                if (face.Axis != axis || face.Sign != sign)
                    continue;

                Vector3[] world = ToWorld(face.Local, _draft.Construct);
                DecorationEditorOverlay.Quad(world[0], world[1], world[2], world[3], faceColor);
                DecorationEditorOverlay.Line(world[0], world[1], edgeColor, width);
                DecorationEditorOverlay.Line(world[1], world[2], edgeColor, width);
                DecorationEditorOverlay.Line(world[2], world[3], edgeColor, width);
                DecorationEditorOverlay.Line(world[3], world[0], edgeColor, width);
                drew = true;
            }

            return drew;
        }

        private bool TryPickFixedGeometryFace(
            Vector3 localOrigin,
            Vector3 localDirection,
            out DecorationEditAxis axis,
            out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            float bestDistance = float.PositiveInfinity;
            foreach (SmartBuildHullFace face in EnumerateFixedGeometryHandleFaces(_draft))
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

        private IEnumerable<SmartBuildHullFace> EnumerateFixedGeometryHandleFaces(SmartBuildPiece piece)
        {
            if (piece?.IsFixedGeometry != true || _itemPreviewRenderer == null)
                yield break;

            SmartBuildSource pieceSource = SourceForPiece(piece);
            IReadOnlyList<SmartBuildPlacement> placements = piece.BuildFixedPlacements(pieceSource, out _);
            if (placements == null || placements.Count == 0)
                yield break;

            SmartBuildVolume volume = piece.ToVolume(pieceSource);
            if (volume == null)
                yield break;

            Vector3[] volumeCorners = volume.GetLocalCorners();
            Vector3 min = volumeCorners[0];
            Vector3 max = volumeCorners[6];
            int placementCount = Math.Min(placements.Count, MaxExactMeshPreviewPlacements);
            for (int placementIndex = 0; placementIndex < placementCount; placementIndex++)
            {
                SmartBuildPlacement placement = placements[placementIndex];
                Mesh mesh = _itemPreviewRenderer.GetMesh(placement?.Candidate);
                if (mesh == null)
                    continue;

                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length < 3)
                    continue;

                Matrix4x4 localMatrix = PlacementLocalMatrix(placement);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    int ia = triangles[index];
                    int ib = triangles[index + 1];
                    int ic = triangles[index + 2];
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= vertices.Length ||
                        ib >= vertices.Length ||
                        ic >= vertices.Length)
                    {
                        continue;
                    }

                    Vector3 a = localMatrix.MultiplyPoint3x4(vertices[ia]);
                    Vector3 b = localMatrix.MultiplyPoint3x4(vertices[ib]);
                    Vector3 c = localMatrix.MultiplyPoint3x4(vertices[ic]);
                    if (!TryClassifyFixedGeometryHandleFace(a, b, c, min, max, out DecorationEditAxis faceAxis, out int faceSign))
                        continue;

                    yield return new SmartBuildHullFace(faceAxis, faceSign, a, b, c, a);
                }
            }
        }

        private static bool TryClassifyFixedGeometryHandleFace(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 min,
            Vector3 max,
            out DecorationEditAxis axis,
            out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 1;
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 0.000001f)
                return false;

            normal.Normalize();
            SmartBuildAxis smartAxis = SmartBuildAxisHelper.FromLargestComponent(normal, out int normalSign);
            axis = SmartBuildDraft.ToDecorationAxis(smartAxis);
            float minComponent = AxisComponent(min, axis);
            float maxComponent = AxisComponent(max, axis);
            if (Mathf.Abs(maxComponent - minComponent) <= 0.001f)
            {
                sign = normalSign;
                return true;
            }

            Vector3 center = (a + b + c) / 3f;
            sign = SignForCoordinate(AxisComponent(center, axis), minComponent, maxComponent);
            return true;
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

        private void DrawGeneratedPieceHull(
            AllConstruct construct,
            IEnumerable<Vector3i> rawCells,
            SmartBuildCellSetFingerprint cacheKey,
            Color wireColor,
            float wireWidth,
            Color materialColor,
            bool drawMaterialFill)
        {
            SmartGeneratedHullPreview preview = GetGeneratedHullPreview(rawCells, cacheKey);
            DrawGeneratedHullPreview(
                construct,
                preview,
                wireColor,
                wireWidth,
                materialColor,
                drawMaterialFill);
        }

        private SmartGeneratedHullPreview GetGeneratedHullPreview(
            IEnumerable<Vector3i> rawCells,
            SmartBuildCellSetFingerprint cacheKey)
        {
            Vector3i[] cells = (rawCells ?? Array.Empty<Vector3i>())
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.First())
                .ToArray();
            if (cells.Length == 0)
                return SmartGeneratedHullPreview.Empty;

            cacheKey = cacheKey.IsEmpty
                ? GeneratedHullCacheKey(cells)
                : cacheKey;
            if (_generatedHullPreviewCache.TryGetValue(cacheKey, out SmartGeneratedHullPreview cached))
                return cached;

            SmartGeneratedHullPreview built = BuildGeneratedHullPreview(cells);
            if (_generatedHullPreviewCache.Count >= GeneratedPreviewCacheLimit)
                _generatedHullPreviewCache.Clear();
            _generatedHullPreviewCache[cacheKey] = built;
            return built;
        }

        private static SmartBuildCellSetFingerprint GeneratedHullCacheKey(
            IEnumerable<Vector3i> cells) =>
            SmartBuildCellSetFingerprint.From(cells);

        private static SmartGeneratedHullPreview BuildGeneratedHullPreview(Vector3i[] cells)
        {
            if (cells == null || cells.Length == 0)
                return SmartGeneratedHullPreview.Empty;

            var occupied = new HashSet<string>(cells.Select(DecoLimitLifter.EsuSymmetry.CellKey));
            var drawnEdges = new HashSet<string>();
            var faces = new List<SmartGeneratedHullFace>();
            var edges = new List<SmartGeneratedHullEdge>();
            for (int index = 0; index < cells.Length; index++)
            {
                Vector3i cell = cells[index];
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.X, 1, occupied, drawnEdges, faces, edges);
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.X, -1, occupied, drawnEdges, faces, edges);
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.Y, 1, occupied, drawnEdges, faces, edges);
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.Y, -1, occupied, drawnEdges, faces, edges);
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.Z, 1, occupied, drawnEdges, faces, edges);
                AddGeneratedVoxelFaceIfExposed(cell, DecorationEditAxis.Z, -1, occupied, drawnEdges, faces, edges);
            }

            return new SmartGeneratedHullPreview(
                cells.Length,
                faces.ToArray(),
                MergeGeneratedHullEdges(edges));
        }

        private static SmartGeneratedHullEdge[] MergeGeneratedHullEdges(
            IReadOnlyList<SmartGeneratedHullEdge> edges)
        {
            if (edges == null || edges.Count <= 1)
                return edges?.ToArray() ?? Array.Empty<SmartGeneratedHullEdge>();

            var passthrough = new List<SmartGeneratedHullEdge>();
            var groups = new Dictionary<string, GeneratedEdgeRunGroup>(StringComparer.Ordinal);
            for (int index = 0; index < edges.Count; index++)
            {
                SmartGeneratedHullEdge edge = edges[index];
                if (!TryCreateGeneratedEdgeRun(
                        edge,
                        out DecorationEditAxis axis,
                        out int fixedA,
                        out int fixedB,
                        out int start,
                        out int end))
                {
                    passthrough.Add(edge);
                    continue;
                }

                string key = GeneratedEdgeRunGroup.Key(axis, fixedA, fixedB);
                if (!groups.TryGetValue(key, out GeneratedEdgeRunGroup group))
                {
                    group = new GeneratedEdgeRunGroup(axis, fixedA, fixedB);
                    groups[key] = group;
                }

                group.Runs.Add(new GeneratedEdgeRun(start, end));
            }

            var merged = new List<SmartGeneratedHullEdge>(passthrough.Count + groups.Count * 2);
            merged.AddRange(passthrough);
            foreach (GeneratedEdgeRunGroup group in groups.Values)
                group.AddMergedEdges(merged);
            return merged.ToArray();
        }

        private static bool TryCreateGeneratedEdgeRun(
            SmartGeneratedHullEdge edge,
            out DecorationEditAxis axis,
            out int fixedA,
            out int fixedB,
            out int start,
            out int end)
        {
            axis = DecorationEditAxis.None;
            fixedA = 0;
            fixedB = 0;
            start = 0;
            end = 0;

            Vector3 delta = edge.End - edge.Start;
            if (Mathf.Abs(delta.x) > 0.001f &&
                Mathf.Abs(delta.y) <= 0.001f &&
                Mathf.Abs(delta.z) <= 0.001f)
            {
                axis = DecorationEditAxis.X;
                fixedA = HalfCellCoordinate(edge.Start.y);
                fixedB = HalfCellCoordinate(edge.Start.z);
                start = HalfCellCoordinate(edge.Start.x);
                end = HalfCellCoordinate(edge.End.x);
            }
            else if (Mathf.Abs(delta.y) > 0.001f &&
                     Mathf.Abs(delta.x) <= 0.001f &&
                     Mathf.Abs(delta.z) <= 0.001f)
            {
                axis = DecorationEditAxis.Y;
                fixedA = HalfCellCoordinate(edge.Start.x);
                fixedB = HalfCellCoordinate(edge.Start.z);
                start = HalfCellCoordinate(edge.Start.y);
                end = HalfCellCoordinate(edge.End.y);
            }
            else if (Mathf.Abs(delta.z) > 0.001f &&
                     Mathf.Abs(delta.x) <= 0.001f &&
                     Mathf.Abs(delta.y) <= 0.001f)
            {
                axis = DecorationEditAxis.Z;
                fixedA = HalfCellCoordinate(edge.Start.x);
                fixedB = HalfCellCoordinate(edge.Start.y);
                start = HalfCellCoordinate(edge.Start.z);
                end = HalfCellCoordinate(edge.End.z);
            }
            else
            {
                return false;
            }

            if (start == end)
                return false;
            if (start > end)
            {
                int swap = start;
                start = end;
                end = swap;
            }

            return true;
        }

        private static int HalfCellCoordinate(float value) =>
            Mathf.RoundToInt(value * 2f);

        private static float FromHalfCellCoordinate(int value) =>
            value * 0.5f;

        private static void AddGeneratedVoxelFaceIfExposed(
            Vector3i cell,
            DecorationEditAxis axis,
            int sign,
            ISet<string> occupied,
            ISet<string> drawnEdges,
            ICollection<SmartGeneratedHullFace> faces,
            ICollection<SmartGeneratedHullEdge> edges)
        {
            Vector3i normalOffset = AxisOffset(axis, sign);
            if (occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell + normalOffset)))
                return;

            Vector3[] corners = VoxelFaceLocalCorners(cell, axis, sign);
            faces.Add(new SmartGeneratedHullFace(corners[0], corners[1], corners[2], corners[3]));
            Vector3i[] edgeSideOffsets = VoxelFaceEdgeSideOffsets(axis, sign);
            AddGeneratedVoxelFaceEdge(corners, 0, 1, cell, normalOffset, edgeSideOffsets[0], occupied, drawnEdges, edges);
            AddGeneratedVoxelFaceEdge(corners, 1, 2, cell, normalOffset, edgeSideOffsets[1], occupied, drawnEdges, edges);
            AddGeneratedVoxelFaceEdge(corners, 2, 3, cell, normalOffset, edgeSideOffsets[2], occupied, drawnEdges, edges);
            AddGeneratedVoxelFaceEdge(corners, 3, 0, cell, normalOffset, edgeSideOffsets[3], occupied, drawnEdges, edges);
        }

        private static void AddGeneratedVoxelFaceEdge(
            Vector3[] corners,
            int from,
            int to,
            Vector3i cell,
            Vector3i normalOffset,
            Vector3i sideOffset,
            ISet<string> occupied,
            ISet<string> drawnEdges,
            ICollection<SmartGeneratedHullEdge> edges)
        {
            Vector3i sideCell = cell + sideOffset;
            if (occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(sideCell)) &&
                !occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(sideCell + normalOffset)))
            {
                return;
            }

            string edge = EdgeKey(corners[from], corners[to]);
            if (drawnEdges != null && !drawnEdges.Add(edge))
                return;

            edges.Add(new SmartGeneratedHullEdge(corners[from], corners[to]));
        }

        private void DrawGeneratedHullPreview(
            AllConstruct construct,
            SmartGeneratedHullPreview preview,
            Color wireColor,
            float wireWidth,
            Color materialColor,
            bool drawMaterialFill)
        {
            if (construct == null || preview == null || preview.IsEmpty)
                return;

            bool drawMaterial = ShouldDrawMaterialPreview() && drawMaterialFill;
            bool drawWire = ShouldDrawPreviewWire();
            if (!drawMaterial && !drawWire)
                return;

            if (drawMaterial)
            {
                int stride = PreviewStride(preview.Faces.Count, MaxGeneratedPreviewMaterialFaces);
                for (int index = 0; index < preview.Faces.Count; index += stride)
                {
                    SmartGeneratedHullFace face = preview.Faces[index];
                    DecorationEditorOverlay.Quad(
                        construct.SafeLocalToGlobal(face.A),
                        construct.SafeLocalToGlobal(face.B),
                        construct.SafeLocalToGlobal(face.C),
                        construct.SafeLocalToGlobal(face.D),
                        materialColor);
                }
            }

            if (!drawWire)
                return;

            int edgeStride = PreviewStride(preview.Edges.Count, MaxGeneratedPreviewWireEdges);
            for (int index = 0; index < preview.Edges.Count; index += edgeStride)
            {
                SmartGeneratedHullEdge edge = preview.Edges[index];
                DecorationEditorOverlay.Line(
                    construct.SafeLocalToGlobal(edge.Start),
                    construct.SafeLocalToGlobal(edge.End),
                    wireColor,
                    wireWidth);
            }
        }

        private static int PreviewStride(int count, int budget) =>
            count <= Math.Max(1, budget)
                ? 1
                : Mathf.CeilToInt(count / (float)Math.Max(1, budget));

        private void DrawVoxelOuterHull(
            AllConstruct construct,
            IEnumerable<Vector3i> rawCells,
            Color wireColor,
            float wireWidth,
            Color materialColor,
            bool drawMaterialFill)
        {
            DrawGeneratedHullPreview(
                construct,
                GetGeneratedHullPreview(rawCells, default),
                wireColor,
                wireWidth,
                materialColor,
                drawMaterialFill);
        }

        private static void DrawVoxelFaceIfExposed(
            AllConstruct construct,
            Vector3i cell,
            DecorationEditAxis axis,
            int sign,
            ISet<string> occupied,
            ISet<string> drawnEdges,
            Color wireColor,
            float wireWidth,
            Color materialColor,
            bool drawMaterial,
            bool drawWire)
        {
            Vector3i normalOffset = AxisOffset(axis, sign);
            if (occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell + normalOffset)))
                return;

            Vector3[] corners = VoxelFaceWorldCorners(construct, cell, axis, sign);
            if (drawMaterial)
                DecorationEditorOverlay.Quad(corners[0], corners[1], corners[2], corners[3], materialColor);
            if (!drawWire)
                return;

            Vector3i[] edgeSideOffsets = VoxelFaceEdgeSideOffsets(axis, sign);
            DrawVoxelFaceEdge(corners, 0, 1, cell, normalOffset, edgeSideOffsets[0], occupied, drawnEdges, wireColor, wireWidth);
            DrawVoxelFaceEdge(corners, 1, 2, cell, normalOffset, edgeSideOffsets[1], occupied, drawnEdges, wireColor, wireWidth);
            DrawVoxelFaceEdge(corners, 2, 3, cell, normalOffset, edgeSideOffsets[2], occupied, drawnEdges, wireColor, wireWidth);
            DrawVoxelFaceEdge(corners, 3, 0, cell, normalOffset, edgeSideOffsets[3], occupied, drawnEdges, wireColor, wireWidth);
        }

        private static void DrawVoxelFaceEdge(
            Vector3[] corners,
            int from,
            int to,
            Vector3i cell,
            Vector3i normalOffset,
            Vector3i sideOffset,
            ISet<string> occupied,
            ISet<string> drawnEdges,
            Color color,
            float width)
        {
            Vector3i sideCell = cell + sideOffset;
            if (occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(sideCell)) &&
                !occupied.Contains(DecoLimitLifter.EsuSymmetry.CellKey(sideCell + normalOffset)))
            {
                return;
            }

            string edge = EdgeKey(corners[from], corners[to]);
            if (drawnEdges != null && !drawnEdges.Add(edge))
                return;

            DecorationEditorOverlay.Line(corners[from], corners[to], color, width);
        }

        private static Vector3i AxisOffset(DecorationEditAxis axis, int sign)
        {
            int normalized = sign >= 0 ? 1 : -1;
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Vector3i(normalized, 0, 0);
                case DecorationEditAxis.Y:
                    return new Vector3i(0, normalized, 0);
                case DecorationEditAxis.Z:
                    return new Vector3i(0, 0, normalized);
                default:
                    return new Vector3i(0, 0, 0);
            }
        }

        private static Vector3i[] VoxelFaceEdgeSideOffsets(DecorationEditAxis axis, int sign)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3i(0, 0, -1),
                            new Vector3i(0, 1, 0),
                            new Vector3i(0, 0, 1),
                            new Vector3i(0, -1, 0)
                        }
                        : new[]
                        {
                            new Vector3i(0, 0, 1),
                            new Vector3i(0, 1, 0),
                            new Vector3i(0, 0, -1),
                            new Vector3i(0, -1, 0)
                        };
                case DecorationEditAxis.Y:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3i(-1, 0, 0),
                            new Vector3i(0, 0, 1),
                            new Vector3i(1, 0, 0),
                            new Vector3i(0, 0, -1)
                        }
                        : new[]
                        {
                            new Vector3i(-1, 0, 0),
                            new Vector3i(0, 0, -1),
                            new Vector3i(1, 0, 0),
                            new Vector3i(0, 0, 1)
                        };
                default:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3i(-1, 0, 0),
                            new Vector3i(0, 1, 0),
                            new Vector3i(1, 0, 0),
                            new Vector3i(0, -1, 0)
                        }
                        : new[]
                        {
                            new Vector3i(1, 0, 0),
                            new Vector3i(0, 1, 0),
                            new Vector3i(-1, 0, 0),
                            new Vector3i(0, -1, 0)
                        };
            }
        }

        private static Vector3[] VoxelFaceWorldCorners(
            AllConstruct construct,
            Vector3i cell,
            DecorationEditAxis axis,
            int sign)
        {
            Vector3[] local = VoxelFaceLocalCorners(cell, axis, sign);
            for (int index = 0; index < local.Length; index++)
                local[index] = construct.SafeLocalToGlobal(local[index]);
            return local;
        }

        private static Vector3[] VoxelFaceLocalCorners(
            Vector3i cell,
            DecorationEditAxis axis,
            int sign)
        {
            Vector3 min = new Vector3(cell.x - 0.5f, cell.y - 0.5f, cell.z - 0.5f);
            Vector3 max = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3(max.x, min.y, min.z),
                            new Vector3(max.x, max.y, min.z),
                            new Vector3(max.x, max.y, max.z),
                            new Vector3(max.x, min.y, max.z)
                        }
                        : new[]
                        {
                            new Vector3(min.x, min.y, max.z),
                            new Vector3(min.x, max.y, max.z),
                            new Vector3(min.x, max.y, min.z),
                            new Vector3(min.x, min.y, min.z)
                        };
                case DecorationEditAxis.Y:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3(min.x, max.y, min.z),
                            new Vector3(min.x, max.y, max.z),
                            new Vector3(max.x, max.y, max.z),
                            new Vector3(max.x, max.y, min.z)
                        }
                        : new[]
                        {
                            new Vector3(min.x, min.y, max.z),
                            new Vector3(min.x, min.y, min.z),
                            new Vector3(max.x, min.y, min.z),
                            new Vector3(max.x, min.y, max.z)
                        };
                default:
                    return sign >= 0
                        ? new[]
                        {
                            new Vector3(min.x, min.y, max.z),
                            new Vector3(min.x, max.y, max.z),
                            new Vector3(max.x, max.y, max.z),
                            new Vector3(max.x, min.y, max.z)
                        }
                        : new[]
                        {
                            new Vector3(max.x, min.y, min.z),
                            new Vector3(max.x, max.y, min.z),
                            new Vector3(min.x, max.y, min.z),
                            new Vector3(min.x, min.y, min.z)
                        };
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

            if (_draft.IsFixedGeometry &&
                DrawFixedGeometryFaceHighlight(axis, sign, faceColor, edgeColor, active ? 4.2f : 2.6f))
            {
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
            DecorationEditAxis hoverAxis = DecorationEditAxis.None;
            int hoverSign = 0;
            if (!_dragging && !SmartBuildInputScope.MouseOverUi)
                TryPickHandle(out hoverAxis, out hoverSign);
            DrawHandleAxis(center, DecorationEditAxis.X, 1, hoverAxis, hoverSign);
            DrawHandleAxis(center, DecorationEditAxis.Y, 1, hoverAxis, hoverSign);
            DrawHandleAxis(center, DecorationEditAxis.Z, 1, hoverAxis, hoverSign);
            if (_tool == SmartBuildTool.Scale)
            {
                DrawHandleAxis(center, DecorationEditAxis.X, -1, hoverAxis, hoverSign);
                DrawHandleAxis(center, DecorationEditAxis.Y, -1, hoverAxis, hoverSign);
                DrawHandleAxis(center, DecorationEditAxis.Z, -1, hoverAxis, hoverSign);
            }
        }

        private void DrawHandleAxis(
            Vector3 center,
            DecorationEditAxis axis,
            int sign,
            DecorationEditAxis hoverAxis,
            int hoverSign)
        {
            Color color = DecorationEditMath.AxisColor(axis);
            Vector3 axisVector = DecorationEditMath.AxisVector(axis) * sign;
            bool active = _dragging && _dragAxis == axis && _dragSign == sign;
            bool hovered = !_dragging && hoverAxis == axis && hoverSign == sign;
            if (hovered)
            {
                color = new Color(
                    Mathf.Lerp(color.r, 1f, 0.48f),
                    Mathf.Lerp(color.g, 1f, 0.48f),
                    Mathf.Lerp(color.b, 1f, 0.48f),
                    1f);
            }
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(active || hovered ? 4.4f : 2.7f);
            Vector3 start = _draft.Construct.SafeLocalToGlobal(center);
            Vector3 end = _draft.Construct.SafeLocalToGlobal(
                center + axisVector * SmartGizmoHandleLength(_tool));
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
        }

        private void DrawRotateGizmo()
        {
            if (_draft?.Construct == null)
                return;

            DecorationEditAxis hoverAxis = DecorationEditAxis.None;
            if (!_rotating && !SmartBuildInputScope.MouseOverUi)
                TryPickRotationRing(out hoverAxis);
            DrawRotationRing(DecorationEditAxis.X, hoverAxis);
            DrawRotationRing(DecorationEditAxis.Y, hoverAxis);
            DrawRotationRing(DecorationEditAxis.Z, hoverAxis);
        }

        private void DrawRotationRing(
            DecorationEditAxis axis,
            DecorationEditAxis hoverAxis)
        {
            if (_draft?.Construct == null)
                return;

            Vector3 centerLocal = _draft.RotationPivotLocal;
            Vector3 centerWorld = _draft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 axisWorld = _draft.Construct.SafeLocalToGlobal(centerLocal + DecorationEditMath.AxisVector(axis)) - centerWorld;
            if (axisWorld.sqrMagnitude <= 0.0001f)
                axisWorld = DecorationEditMath.AxisVector(axis);

            Color color = DecorationEditMath.AxisColor(axis);
            bool active = _rotating && _rotateDragAxis == axis;
            bool hovered = !_rotating && hoverAxis == axis;
            if (hovered)
            {
                color = new Color(
                    Mathf.Lerp(color.r, 1f, 0.48f),
                    Mathf.Lerp(color.g, 1f, 0.48f),
                    Mathf.Lerp(color.b, 1f, 0.48f),
                    1f);
            }
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(active || hovered ? 4.4f : 2.7f);
            DecorationEditorOverlay.Circle(centerWorld, RotateGizmoRadius(), color, axisWorld.normalized, width, 56);
        }

        private bool TryPickRotationRing(out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_draft?.Construct == null)
                return false;

            Vector3 center = _draft.RotationPivotLocal;
            if (!TryProjectSmartGizmo(_draft.Construct, center, out Vector2 origin))
                return false;

            Vector2 mouse = MouseGuiPosition();
            float best = EsuGizmoSettings.Current.HitAreaPixels;
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
                    if (!TryProjectSmartGizmo(_draft.Construct, local, out Vector2 projected))
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
            _rotateSelectionPivot = _draft.RotationPivotCell;
            if (!TryProjectSmartGizmo(_draft.Construct, _draft.RotationPivotLocal, out _rotateDragCenterScreen))
                _rotateDragCenterScreen = _rotateDragMouseStart;
            _rotateDragStartVector = _rotateDragMouseStart - _rotateDragCenterScreen;
            _rotateDragLastVector = _rotateDragStartVector;
            _rotateDragAccumulatedDegrees = 0f;
            _rotateDragSensitivityScale = RotationDragSensitivityScale(
                _draft.Construct,
                _draft.RotationPivotLocal,
                DecorationEditMath.AxisVector(axis),
                RotateGizmoRadius(),
                RotateGizmoBaseRadius(),
                _rotateDragMouseStart);
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
            _rotateDragAccumulatedDegrees +=
                Mathf.DeltaAngle(previousAngle, currentAngle) *
                _rotateDragSensitivityScale;
            _rotateDragLastVector = current;
            int quarterTurns = Mathf.RoundToInt(_rotateDragAccumulatedDegrees / RotateSnapDegrees);
            RestoreSelectedPiecesFromSnapshot(_rotateSceneStart);
            RotateSelectionAroundAxis(
                _rotateDragAxis,
                quarterTurns,
                _rotateSelectionPivot);
            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private void EndRotateDrag(bool resetDraft)
        {
            if (!_rotating)
                return;

            if (resetDraft)
                RestoreSelectedPiecesFromSnapshot(_rotateSceneStart);

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
            _rotateDragSensitivityScale = 1f;
            _rotatePieceStart = null;
            _rotateSceneStart = null;
            _rotateSelectionPivot = new Vector3i(0, 0, 0);
        }

        private float RotateGizmoRadius() =>
            RotateGizmoBaseRadius() * EsuGizmoSettings.Current.RotateSize;

        private float RotateGizmoBaseRadius()
        {
            if (_draft == null)
                return 1f;

            Vector3i size = _draft.Size;
            return Mathf.Max(1.1f, Math.Max(size.x, Math.Max(size.y, size.z)) * 0.68f);
        }

        private static float RotationDragSensitivityScale(
            AllConstruct construct,
            Vector3 center,
            Vector3 axisVector,
            float visualRadius,
            float referenceRadius,
            Vector2 mouse)
        {
            if (construct == null ||
                !DecorationEditMath.IsFinite(center) ||
                !DecorationEditMath.IsFinite(axisVector) ||
                axisVector.sqrMagnitude < 0.0001f ||
                !DecorationEditMath.IsFinite(visualRadius) ||
                !DecorationEditMath.IsFinite(referenceRadius) ||
                visualRadius <= 0f ||
                referenceRadius <= 0f ||
                !TryProjectSmartGizmo(construct, center, out Vector2 centerScreen))
            {
                return 1f;
            }

            BuildAxisBasis(axisVector, out Vector3 tangentA, out Vector3 tangentB);
            float bestDistance = float.PositiveInfinity;
            Vector3 bestDirection = tangentA;
            const int steps = 48;
            for (int step = 0; step < steps; step++)
            {
                float angle = step * Mathf.PI * 2f / steps;
                Vector3 direction = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                if (!TryProjectSmartGizmo(
                        construct,
                        center + direction * visualRadius,
                        out Vector2 projected))
                {
                    continue;
                }

                float distance = (mouse - projected).sqrMagnitude;
                if (!DecorationEditMath.IsFinite(distance) || distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestDirection = direction;
            }

            if (float.IsPositiveInfinity(bestDistance) ||
                !TryProjectSmartGizmo(
                    construct,
                    center + bestDirection * visualRadius,
                    out Vector2 visualPoint) ||
                !TryProjectSmartGizmo(
                    construct,
                    center + bestDirection * referenceRadius,
                    out Vector2 referencePoint))
            {
                return 1f;
            }

            float visualPixels = Vector2.Distance(centerScreen, visualPoint);
            float referencePixels = Vector2.Distance(centerScreen, referencePoint);
            if (!DecorationEditMath.IsFinite(visualPixels) ||
                !DecorationEditMath.IsFinite(referencePixels) ||
                visualPixels < DecorationEditMath.MinimumProjectedAxisLengthPixels ||
                referencePixels < DecorationEditMath.MinimumProjectedAxisLengthPixels)
            {
                return 1f;
            }

            return Mathf.Clamp(visualPixels / referencePixels, 0.1f, 10f);
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

        private static bool TryProjectSmartGizmo(
            AllConstruct construct,
            Vector3 local,
            out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (construct == null || !DecorationEditMath.IsFinite(local))
                return false;

            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Vector3 projected = camera.WorldToScreenPoint(construct.SafeLocalToGlobal(local));
            if (projected.z <= camera.nearClipPlane ||
                !DecorationEditMath.IsFinite(projected.x) ||
                !DecorationEditMath.IsFinite(projected.y))
            {
                return false;
            }

            screenPoint = ScreenToGui(projected);
            return DecorationEditMath.IsFinite(screenPoint);
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

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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
            SmartBuildShapeDescriptor selectedDescriptor = SelectedShapeDescriptor;
            if (selectedDescriptor.IsCuboid)
            {
                Vector3i size = SmartBuildDraft.ClampSize(candidate.CuboidSize);
                Vector3i max = candidate.Cell + new Vector3i(size.x - 1, size.y - 1, size.z - 1);
                SmartBuildVolume volume = SmartBuildVolume.FromBounds(candidate.Construct, candidate.Cell, max);
                if (volume != null && ShouldDrawMaterialPreview())
                    DrawVolumeFaces(volume.GetWorldCorners(), SolidMaterialPreviewColor(selected: false));
                if (volume != null && ShouldDrawPreviewWire())
                    DrawWireEdges(volume.GetWorldCorners(), color, 2.4f);
                return;
            }

            if (selectedDescriptor.IsGenerator)
            {
                Vector3i generatorOrigin = candidate.Cell;
                DeriveRightAxis(candidate.ForwardAxis, candidate.ForwardSign, out SmartBuildAxis generatorRightAxis, out int _);
                if (candidate.RightStart.HasValue)
                    generatorOrigin = SmartBuildAxisHelper.Set(generatorOrigin, generatorRightAxis, candidate.RightStart.Value);

                SmartBuildPiece ghost = SmartBuildPiece.CreateGeneratedShapePreview(
                    candidate.Construct,
                    generatorOrigin,
                    selectedDescriptor,
                    candidate.ForwardAxis,
                    candidate.ForwardSign,
                    candidate.Width,
                    _drawPlane);
                ApplySampledBlockRotation(ghost);
                Color materialColor = candidate.Valid
                    ? SolidMaterialPreviewColor(selected: false)
                    : new Color(1f, 0.2f, 0.15f, 0.34f);
                DrawGeneratedPieceHull(
                    candidate.Construct,
                    ghost.EnumeratePreviewCells(_source).ToArray(),
                    default,
                    color,
                    2.4f,
                    materialColor,
                    drawMaterialFill: true);
                return;
            }

            if (selectedDescriptor.IsFixedGeometry)
            {
                Vector3i fixedOrigin = candidate.Cell;
                DeriveRightAxis(candidate.ForwardAxis, candidate.ForwardSign, out SmartBuildAxis fixedRightAxis, out int fixedRightSign);
                if (candidate.RightStart.HasValue)
                    fixedOrigin = SmartBuildAxisHelper.Set(fixedOrigin, fixedRightAxis, candidate.RightStart.Value);

                SmartBuildPiece ghost = SmartBuildPiece.CreateFixedShapePreview(
                    candidate.Construct,
                    fixedOrigin,
                    selectedDescriptor,
                    _selectedSlopeLength,
                    candidate.ForwardAxis,
                    candidate.ForwardSign,
                    candidate.Width,
                    _drawPlane);
                ApplySampledBlockRotation(ghost);
                IReadOnlyList<SmartBuildPlacement> placements = ghost.BuildFixedPlacements(
                    _source,
                    out _);
                if (placements.Count > 0)
                {
                    Color materialColor = candidate.Valid
                        ? new Color(1f, 1f, 1f, 0.38f)
                        : new Color(1f, 0.2f, 0.15f, 0.34f);
                    bool drewExactMaterialMeshes = ShouldDrawMaterialPreview();
                    if (drewExactMaterialMeshes)
                    {
                        foreach (SmartBuildPlacement placement in placements.Take(MaxExactMeshPreviewPlacements))
                            DrawPlacementPreview(candidate.Construct, placement, color, materialColor, 2.4f, drawWire: false);
                    }

                    DrawPiecePreview(
                        ghost,
                        new DecoLimitLifter.EsuSymmetry.SymmetryVariant(Array.Empty<DecorationEditAxis>()),
                        !candidate.Valid,
                        new HashSet<SmartBuildPreviewDrawKey>(),
                        !drewExactMaterialMeshes);
                }
                else
                {
                    SmartBuildVolume volume = VolumeFromCells(
                        candidate.Construct,
                        ghost.EnumeratePreviewCells(_source).ToArray());
                    if (volume != null && ShouldDrawMaterialPreview())
                        DrawVolumeFaces(volume.GetWorldCorners(), SolidMaterialPreviewColor(selected: false));
                    if (volume != null && ShouldDrawPreviewWire())
                        DrawWireEdges(volume.GetWorldCorners(), color, 2.4f);
                }
                return;
            }

            Vector3i origin = candidate.Cell;
            DeriveRightAxis(candidate.ForwardAxis, candidate.ForwardSign, out SmartBuildAxis rightAxis, out int rightSign);
            if (candidate.RightStart.HasValue)
                origin = SmartBuildAxisHelper.Set(origin, rightAxis, candidate.RightStart.Value);

            SmartBuildPiece slopeGhost = SmartBuildPiece.CreateDownSlope(
                candidate.Construct,
                origin,
                _selectedSlopeLength,
                candidate.ForwardAxis,
                candidate.ForwardSign,
                candidate.Width,
                _drawPlane,
                _defaultSlopeSupportMode);
            ApplySampledBlockRotation(slopeGhost);
            IReadOnlyList<SmartBuildPlacement> slopePlacements = slopeGhost.BuildFixedPlacements(
                _source,
                out _);
            if (slopePlacements.Count > 0)
            {
                Color materialColor = candidate.Valid
                    ? new Color(1f, 1f, 1f, 0.38f)
                    : new Color(1f, 0.2f, 0.15f, 0.34f);
                bool drewExactMaterialMeshes = ShouldDrawMaterialPreview();
                if (drewExactMaterialMeshes)
                {
                    foreach (SmartBuildPlacement placement in slopePlacements.Take(MaxExactMeshPreviewPlacements))
                        DrawPlacementPreview(candidate.Construct, placement, color, materialColor, 2.4f, drawWire: false);
                }

                DrawPiecePreview(
                    slopeGhost,
                    new DecoLimitLifter.EsuSymmetry.SymmetryVariant(Array.Empty<DecorationEditAxis>()),
                    !candidate.Valid,
                    new HashSet<SmartBuildPreviewDrawKey>(),
                    !drewExactMaterialMeshes);
                return;
            }

            Vector3i forward = SmartBuildAxisHelper.ToVector3i(
                slopeGhost.ForwardAxis,
                slopeGhost.ForwardSign);
            rightAxis = slopeGhost.RightAxis;
            rightSign = slopeGhost.RightSign;
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
                slopeGhost.ForwardAxis,
                slopeGhost.ForwardSign,
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

        private Color SolidMaterialPreviewColor(bool selected) =>
            SolidMaterialPreviewColor(_selectedMaterial, selected);

        private static Color SolidMaterialPreviewColor(
            SmartBuildMaterial material,
            bool selected)
        {
            Color color;
            switch (material)
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

            Vector3[] corners = _draft.ToVolume(SourceForPiece(_draft)).GetLocalCorners();
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

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);

        private static Matrix4x4 PlacementMatrix(
            AllConstruct construct,
            SmartBuildPlacement placement)
        {
            Matrix4x4 localPlacement = PlacementLocalMatrix(placement);
            try
            {
                if (construct?.myTransform != null)
                    return construct.myTransform.localToWorldMatrix * localPlacement;
            }
            catch
            {
            }

            try
            {
                Vector3 local = ToVector3(placement.Position);
                return Matrix4x4.TRS(
                    construct.SafeLocalToGlobal(local),
                    placement.Rotation,
                    Vector3.one);
            }
            catch
            {
                return localPlacement;
            }
        }

        private static Matrix4x4 PlacementLocalMatrix(SmartBuildPlacement placement) =>
            Matrix4x4.TRS(ToVector3(placement.Position), placement.Rotation, Vector3.one);

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

        private static string CompactText(string value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxCharacters)
                return value ?? string.Empty;
            return value.Substring(0, Mathf.Max(1, maxCharacters - 3)) + "...";
        }

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

        private static bool IsControlHeld() =>
            Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        private static bool IsCardinalSlopeStretchHeld() =>
            IsShiftHeld();

        private static Vector2 ScreenToGui(Vector3 screen) =>
            new Vector2(screen.x, Screen.height - screen.y);

        private static bool ContainsMouse(Rect rect) =>
            rect.Contains(MouseGuiPosition());

        private void RefreshMouseOverUiFromCurrentPointer()
        {
            Vector2 mouse = MouseGuiPosition();
            bool overUi = IsMouseOverSmartUi(mouse);
            SmartBuildInputScope.SetMouseOverUi(overUi);
            if (overUi &&
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                SmartBuildInputScope.ClaimMouseWheelInputForFrames();
            }
        }

        private EsuPanelUiHit HitTestSmartUi(Vector2 mouse)
        {
            if (!Active)
                return EsuPanelUiHit.Miss(mouse);

            Rect toolbar = _toolbarRect.width > 1f && _toolbarRect.height > 1f
                ? _toolbarRect
                : EsuHudLayout.ToolbarRect(ToolbarHeight);
            Rect status = _statusRect.width > 1f && _statusRect.height > 1f
                ? _statusRect
                : EsuHudLayout.BottomStripRect(StatusHeightScaled());
            Rect leftPanel = _leftPanelRect.width > 1f && _leftPanelRect.height > 1f
                ? _leftPanelRect
                : DefaultLeftPanelRect();
            Rect rightPanel = _rightPanelRect.width > 1f && _rightPanelRect.height > 1f
                ? _rightPanelRect
                : DefaultRightPanelRect();

            if (_contextMenuOpen && _contextMenuRect.Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "context_menu", mouse);
            if (toolbar.Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "toolbar", mouse);
            if (EsuConsoleWindow.ContainsMouse(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "console", mouse);
            if (EsuHudNotifications.ContainsMouse(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "notification", mouse);
            if (_gizmoSettingsOpen && GizmoSettingsMenuRect().Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "gizmo_settings_menu", mouse);
            if (_viewModeMenuOpen && ViewModeMenuRect(toolbar).Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "view_mode_menu", mouse);
            if (_showLeftPanel && leftPanel.Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "left_panel", mouse);
            if (_showRightPanel && rightPanel.Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "right_panel", mouse);
            if (status.Contains(mouse))
                return EsuPanelUiHit.Found("Smart Builder", "status", mouse);
            return EsuPanelUiHit.Miss(mouse);
        }

        private bool IsMouseOverSmartUi(Vector2 mouse) =>
            HitTestSmartUi(mouse).IsHit;

        private bool IsCurrentMouseOverSmartUi() =>
            IsMouseOverSmartUi(MouseGuiPosition());

        private static void ClaimMouseWheelOverSmartUi(Event current, bool overUi)
        {
            if (!overUi ||
                current == null ||
                current.type != EventType.ScrollWheel)
            {
                return;
            }

            SmartBuildInputScope.ClaimMouseWheelInputForFrames();
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

        private readonly struct SmartBuildInternalFace
        {
            private const float EdgePlaneEpsilon = 0.0025f;

            internal SmartBuildInternalFace(
                DecorationEditAxis normalAxis,
                float coordinate,
                DecorationEditAxis axisA,
                float minA,
                float maxA,
                DecorationEditAxis axisB,
                float minB,
                float maxB)
            {
                NormalAxis = normalAxis;
                Coordinate = coordinate;
                AxisA = axisA;
                MinA = Mathf.Min(minA, maxA);
                MaxA = Mathf.Max(minA, maxA);
                AxisB = axisB;
                MinB = Mathf.Min(minB, maxB);
                MaxB = Mathf.Max(minB, maxB);
            }

            private DecorationEditAxis NormalAxis { get; }

            private float Coordinate { get; }

            private DecorationEditAxis AxisA { get; }

            private float MinA { get; }

            private float MaxA { get; }

            private DecorationEditAxis AxisB { get; }

            private float MinB { get; }

            private float MaxB { get; }

            internal bool Contains(Vector3 start, Vector3 end)
            {
                if (Mathf.Abs(AxisComponent(start, NormalAxis) - Coordinate) > EdgePlaneEpsilon ||
                    Mathf.Abs(AxisComponent(end, NormalAxis) - Coordinate) > EdgePlaneEpsilon)
                {
                    return false;
                }

                Vector3 middle = (start + end) * 0.5f;
                return InRange(AxisComponent(middle, AxisA), MinA, MaxA) &&
                       InRange(AxisComponent(middle, AxisB), MinB, MaxB);
            }

            private static bool InRange(float value, float min, float max) =>
                value >= min - EdgePlaneEpsilon &&
                value <= max + EdgePlaneEpsilon;
        }

        private sealed class SmartGeneratedHullPreview
        {
            internal static readonly SmartGeneratedHullPreview Empty =
                new SmartGeneratedHullPreview(
                    0,
                    Array.Empty<SmartGeneratedHullFace>(),
                    Array.Empty<SmartGeneratedHullEdge>());

            internal SmartGeneratedHullPreview(
                int sourceCellCount,
                IReadOnlyList<SmartGeneratedHullFace> faces,
                IReadOnlyList<SmartGeneratedHullEdge> edges)
            {
                SourceCellCount = Math.Max(0, sourceCellCount);
                Faces = faces ?? Array.Empty<SmartGeneratedHullFace>();
                Edges = edges ?? Array.Empty<SmartGeneratedHullEdge>();
            }

            internal int SourceCellCount { get; }

            internal IReadOnlyList<SmartGeneratedHullFace> Faces { get; }

            internal IReadOnlyList<SmartGeneratedHullEdge> Edges { get; }

            internal bool IsEmpty => SourceCellCount == 0 || (Faces.Count == 0 && Edges.Count == 0);
        }

        private readonly struct SmartGeneratedHullFace
        {
            internal SmartGeneratedHullFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                A = a;
                B = b;
                C = c;
                D = d;
            }

            internal Vector3 A { get; }

            internal Vector3 B { get; }

            internal Vector3 C { get; }

            internal Vector3 D { get; }
        }

        private readonly struct SmartGeneratedHullEdge
        {
            internal SmartGeneratedHullEdge(Vector3 start, Vector3 end)
            {
                Start = start;
                End = end;
            }

            internal Vector3 Start { get; }

            internal Vector3 End { get; }
        }

        private readonly struct GeneratedEdgeRun
        {
            internal GeneratedEdgeRun(int start, int end)
            {
                Start = Math.Min(start, end);
                End = Math.Max(start, end);
            }

            internal int Start { get; }

            internal int End { get; }
        }

        private sealed class GeneratedEdgeRunGroup
        {
            internal GeneratedEdgeRunGroup(DecorationEditAxis axis, int fixedA, int fixedB)
            {
                Axis = axis;
                FixedA = fixedA;
                FixedB = fixedB;
            }

            internal DecorationEditAxis Axis { get; }

            internal int FixedA { get; }

            internal int FixedB { get; }

            internal List<GeneratedEdgeRun> Runs { get; } = new List<GeneratedEdgeRun>();

            internal static string Key(DecorationEditAxis axis, int fixedA, int fixedB) =>
                axis.ToString() + "|" +
                fixedA.ToString(CultureInfo.InvariantCulture) + "|" +
                fixedB.ToString(CultureInfo.InvariantCulture);

            internal void AddMergedEdges(ICollection<SmartGeneratedHullEdge> output)
            {
                if (output == null || Runs.Count == 0)
                    return;

                Runs.Sort((left, right) =>
                {
                    int compare = left.Start.CompareTo(right.Start);
                    return compare != 0 ? compare : left.End.CompareTo(right.End);
                });

                int start = Runs[0].Start;
                int end = Runs[0].End;
                for (int index = 1; index < Runs.Count; index++)
                {
                    GeneratedEdgeRun run = Runs[index];
                    if (run.Start <= end)
                    {
                        end = Math.Max(end, run.End);
                        continue;
                    }

                    AddEdge(output, start, end);
                    start = run.Start;
                    end = run.End;
                }

                AddEdge(output, start, end);
            }

            private void AddEdge(ICollection<SmartGeneratedHullEdge> output, int start, int end)
            {
                if (start == end)
                    return;

                output.Add(new SmartGeneratedHullEdge(ToVector(start), ToVector(end)));
            }

            private Vector3 ToVector(int coordinate)
            {
                float value = FromHalfCellCoordinate(coordinate);
                float a = FromHalfCellCoordinate(FixedA);
                float b = FromHalfCellCoordinate(FixedB);
                switch (Axis)
                {
                    case DecorationEditAxis.X:
                        return new Vector3(value, a, b);
                    case DecorationEditAxis.Y:
                        return new Vector3(a, value, b);
                    default:
                        return new Vector3(a, b, value);
                }
            }
        }

        private readonly struct ShapePreviewGridLayout
        {
            internal ShapePreviewGridLayout(
                int columns,
                float cardWidth,
                float cardHeight,
                float rowHeight,
                float previewHeight,
                float gap,
                float outerPadding)
            {
                Columns = Math.Max(1, columns);
                CardWidth = cardWidth;
                CardHeight = cardHeight;
                RowHeight = rowHeight;
                PreviewHeight = previewHeight;
                Gap = gap;
                OuterPadding = outerPadding;
            }

            internal int Columns { get; }

            internal float CardWidth { get; }

            internal float CardHeight { get; }

            internal float RowHeight { get; }

            internal float PreviewHeight { get; }

            internal float Gap { get; }

            internal float OuterPadding { get; }
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
                SmartBuildSceneState sceneState,
                SmartBuildTool tool,
                SmartBuildShapeKind selectedShape,
                string selectedShapeDescriptorKey,
                int selectedSlopeLength,
                SmartBuildMaterial selectedMaterial,
                SmartBuildDrawPlane drawPlane,
                SmartBuildOccupancyMode occupancyMode,
                SmartBuildEditHandleMode editHandleMode,
                SmartBuildSlopeSupportMode defaultSlopeSupportMode,
                SmartBuildPreviewMode previewMode)
            {
                SceneState = sceneState ?? new SmartBuildSceneState(null, null, null, -1);
                Tool = tool;
                SelectedShape = selectedShape;
                SelectedShapeDescriptorKey = string.IsNullOrWhiteSpace(selectedShapeDescriptorKey)
                    ? SmartBuildShapeDescriptors.CuboidKey
                    : selectedShapeDescriptorKey;
                SelectedSlopeLength = selectedSlopeLength;
                SelectedMaterial = selectedMaterial;
                DrawPlane = drawPlane;
                OccupancyMode = occupancyMode;
                EditHandleMode = editHandleMode;
                DefaultSlopeSupportMode = defaultSlopeSupportMode;
                PreviewMode = previewMode;
            }

            internal SmartBuildSceneState SceneState { get; }

            internal AllConstruct Construct => SceneState.Construct;

            internal IReadOnlyList<SmartBuildPiece> Pieces =>
                SceneState.Nodes.Select(node => node.HostPiece).ToArray();

            internal int SelectedPieceId => SceneState.PrimaryNodeId;

            internal IReadOnlyList<int> SelectedPieceIds => SceneState.SelectedNodeIds;

            internal SmartBuildTool Tool { get; }

            internal SmartBuildShapeKind SelectedShape { get; }

            internal string SelectedShapeDescriptorKey { get; }

            internal int SelectedSlopeLength { get; }

            internal SmartBuildMaterial SelectedMaterial { get; }

            internal SmartBuildDrawPlane DrawPlane { get; }

            internal SmartBuildOccupancyMode OccupancyMode { get; }

            internal SmartBuildEditHandleMode EditHandleMode { get; }

            internal SmartBuildSlopeSupportMode DefaultSlopeSupportMode { get; }

            internal SmartBuildPreviewMode PreviewMode { get; }
        }

        private sealed class SmartBuildSceneDelta
        {
            private SmartBuildSceneDelta(
                SmartBuildSceneStateDelta sceneDelta,
                SmartBuildSceneSettings before,
                SmartBuildSceneSettings after)
            {
                SceneDelta = sceneDelta;
                Before = before;
                After = after;
            }

            private SmartBuildSceneStateDelta SceneDelta { get; }

            private SmartBuildSceneSettings Before { get; }

            private SmartBuildSceneSettings After { get; }

            internal static SmartBuildSceneDelta Create(
                SmartBuildSceneSnapshot before,
                SmartBuildSceneSnapshot after)
            {
                if (before == null || after == null)
                    return null;
                SmartBuildSceneStateDelta sceneDelta =
                    SmartBuildSceneStateDelta.Create(before.SceneState, after.SceneState);
                var beforeSettings = new SmartBuildSceneSettings(before);
                var afterSettings = new SmartBuildSceneSettings(after);
                if (!sceneDelta.HasSceneChanges && beforeSettings.ContentEquals(afterSettings))
                    return null;
                return new SmartBuildSceneDelta(sceneDelta, beforeSettings, afterSettings);
            }

            internal SmartBuildSceneSnapshot Resolve(
                SmartBuildSceneSnapshot current,
                bool forward)
            {
                SmartBuildSceneState currentState = current?.SceneState ??
                    new SmartBuildSceneState(null, null, null, -1);
                SmartBuildSceneState state = currentState.Apply(SceneDelta, forward);
                return (forward ? After : Before).CreateSnapshot(state);
            }
        }

        private sealed class SmartBuildSceneSettings
        {
            internal SmartBuildSceneSettings(SmartBuildSceneSnapshot snapshot)
            {
                Tool = snapshot.Tool;
                SelectedShape = snapshot.SelectedShape;
                SelectedShapeDescriptorKey = snapshot.SelectedShapeDescriptorKey;
                SelectedSlopeLength = snapshot.SelectedSlopeLength;
                SelectedMaterial = snapshot.SelectedMaterial;
                DrawPlane = snapshot.DrawPlane;
                OccupancyMode = snapshot.OccupancyMode;
                EditHandleMode = snapshot.EditHandleMode;
                DefaultSlopeSupportMode = snapshot.DefaultSlopeSupportMode;
                PreviewMode = snapshot.PreviewMode;
            }

            internal SmartBuildTool Tool { get; }
            internal SmartBuildShapeKind SelectedShape { get; }
            internal string SelectedShapeDescriptorKey { get; }
            internal int SelectedSlopeLength { get; }
            internal SmartBuildMaterial SelectedMaterial { get; }
            internal SmartBuildDrawPlane DrawPlane { get; }
            internal SmartBuildOccupancyMode OccupancyMode { get; }
            internal SmartBuildEditHandleMode EditHandleMode { get; }
            internal SmartBuildSlopeSupportMode DefaultSlopeSupportMode { get; }
            internal SmartBuildPreviewMode PreviewMode { get; }

            internal bool ContentEquals(SmartBuildSceneSettings other) =>
                other != null &&
                Tool == other.Tool &&
                SelectedShape == other.SelectedShape &&
                string.Equals(
                    SelectedShapeDescriptorKey,
                    other.SelectedShapeDescriptorKey,
                    StringComparison.Ordinal) &&
                SelectedSlopeLength == other.SelectedSlopeLength &&
                SelectedMaterial == other.SelectedMaterial &&
                DrawPlane == other.DrawPlane &&
                OccupancyMode == other.OccupancyMode &&
                EditHandleMode == other.EditHandleMode &&
                DefaultSlopeSupportMode == other.DefaultSlopeSupportMode &&
                PreviewMode == other.PreviewMode;

            internal SmartBuildSceneSnapshot CreateSnapshot(SmartBuildSceneState state) =>
                new SmartBuildSceneSnapshot(
                    state,
                    Tool,
                    SelectedShape,
                    SelectedShapeDescriptorKey,
                    SelectedSlopeLength,
                    SelectedMaterial,
                    DrawPlane,
                    OccupancyMode,
                    EditHandleMode,
                    DefaultSlopeSupportMode,
                    PreviewMode);
        }

        private readonly struct SmartBuildShapePaletteEntry
        {
            internal static SmartBuildShapePaletteEntry Create(
                SmartBuildShapeDescriptor descriptor,
                SmartBlockFamily family,
                SmartBlockCandidate candidate)
            {
                string search = string.Join(
                    " ",
                    new[]
                    {
                        descriptor?.Label,
                        descriptor?.Tooltip,
                        CategoryLabel(descriptor?.Category ?? SmartBuildShapeCategory.Basic),
                        family?.DisplayName,
                        candidate?.DisplayName,
                        candidate?.GeometryName
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                    .ToLowerInvariant();
                return new SmartBuildShapePaletteEntry(descriptor, family, candidate, search);
            }

            private SmartBuildShapePaletteEntry(
                SmartBuildShapeDescriptor descriptor,
                SmartBlockFamily family,
                SmartBlockCandidate candidate,
                string searchText)
            {
                Descriptor = descriptor;
                Family = family;
                Candidate = candidate;
                SearchText = searchText ?? string.Empty;
            }

            internal SmartBuildShapeDescriptor Descriptor { get; }

            internal SmartBlockFamily Family { get; }

            internal SmartBlockCandidate Candidate { get; }

            internal string SearchText { get; }

            internal bool IsValid => Descriptor != null && Family?.IsSupported == true;
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
