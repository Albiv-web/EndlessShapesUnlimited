using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Networking;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ftd.Multiplayer.NetworkCommunication;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.SerializationHud;
using EndlessShapes2;
using EndlessShapes2.Polygon;
using UnityEngine;
using UnityEngine.Rendering;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditSession
    {
        private enum StackDividerKind
        {
            None,
            Left,
            Right
        }

        private enum SurfaceBuilderTool
        {
            Draw,
            Path,
            Circle,
            Move,
            Rotate,
            Scale
        }

        private enum SurfaceDraftActionTarget
        {
            Surface,
            Generator
        }

        private enum SurfaceContextTargetKind
        {
            None,
            SurfacePointSelection,
            SurfacePoint,
            SurfaceEdge,
            SurfaceFace,
            GeneratorPoint
        }

        private enum DecorationContextAction
        {
            None,
            Select,
            Move,
            Rotate,
            Scale,
            Anchor,
            Duplicate,
            ToggleAnchorMesh,
            Delete
        }

        private const float HandleLength = 1.25f;
        private const float SelectionRadiusPixels = 28f;
        private const float BoxSelectionClickThresholdPixels = 6f;
        private const float BoxSelectionVisibilityTolerance = 0.45f;
        private const float HandleRadiusPixels = 14f;
        private const int MaxOutlinerDrawRows = 180;
        private const int MaxWorldHintLines = 650;
        private const float OutlinerRowHeight = 22f;
        private const float MeshPaletteRowHeight = 24f;
        private const float MeshPreviewGridMinCardWidth = 112f;
        private const float MeshPreviewGridMinCardHeight = 86f;
        private const float MeshPreviewGridMaxCardHeight = 124f;
        private const float MeshPreviewGridCardAspect = 0.74f;
        private const float MeshPreviewGridGap = 8f;
        private const float MeshPreviewGridOuterPadding = 2f;
        private const int MeshPreviewGridTexturePixels = 96;
        private const int ViewModeMenuButtonCount = 9;
        private const float RotateGizmoRadius = 0.82f;
        private const float MinimumScale = 0.01f;
        private const float AnchorFollowMinimumDistance = 1f;
        private const float AnchorFollowMaximumDistance = 10f;
        private const int AnchorFollowMaximumSearchRadius = 8;
        private const float SymmetryCounterpartCenterTolerance = 0.075f;
        private const float LeftStackDefaultBottomRatio = 0.34f;
        private const float RightStackDefaultBottomRatio = 0.38f;
        private static Rect s_meshPaletteRect = new Rect(18f, 110f, 480f, 1050f);
        private static Rect s_rightPanelRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static float s_leftStackBottomRatio = LeftStackDefaultBottomRatio;
        private static float s_rightStackBottomRatio = RightStackDefaultBottomRatio;
        private static bool s_showMeshPalette = true;
        private static bool s_showInspectorPanel = true;
        private static bool s_showOutlinerPanel = true;
        private static bool s_showAnchorPanel = true;
        private static bool s_showSurfacePanel = true;
        private static bool s_showSurfaceToolsPanel = true;
        private static bool s_showSurfaceDraftList = true;
        private static bool s_showInspectorColorPicker = true;
        private static bool s_showGeneratorColorPicker = true;
        private static bool s_meshPreviewGrid;
        private static DecorationTransformOrientation s_transformOrientation = DecorationTransformOrientation.Global;
        private static bool s_anchorMenuOpen;
        private static bool s_anchorFollowDecoration;
        private static float s_anchorFollowDistance = AnchorFollowMinimumDistance;
        private static SurfaceExtraTool s_surfaceExtraTool = SurfaceExtraTool.Path;
        private static SurfaceBuilderTool s_surfaceBuilderTool = SurfaceBuilderTool.Draw;
        private static readonly DecorationGeneratorSettings s_generatorSettings = new DecorationGeneratorSettings();

        private readonly cBuild _build;
        private readonly int _modalWindowId = "EndlessShapesUnlimited.DecorationEditMode.Modal".GetHashCode();
        private Rect _meshPaletteRect = s_meshPaletteRect;
        private Rect _rightPanelRect = s_rightPanelRect;
        private Vector2 _meshScroll;
        private Vector2 _outlinerScroll;
        private Vector2 _inspectorScroll;
        private Vector2 _anchorListScroll;
        private bool _mouseOverWindow;
        private bool _draggingMeshPalette;
        private Vector2 _meshPaletteDragOffset;
        private bool _resizingMeshPalette;
        private Rect _meshPaletteResizeStart;
        private Vector2 _meshPaletteResizeMouseStart;
        private bool _resizingRightPanel;
        private Rect _rightPanelResizeStart;
        private Vector2 _rightPanelResizeMouseStart;
        private Rect _anchorSettingsButtonScreenRect;
        private int _layoutResetGeneration = s_layoutGeneration;
        private float _leftStackBottomRatio = s_leftStackBottomRatio;
        private float _rightStackBottomRatio = s_rightStackBottomRatio;
        private StackDividerKind _draggingStackDivider;
        private float _stackDividerDragStartMouseY;
        private float _stackDividerDragStartBottomRatio;
        private bool _meshPreviewGrid = s_meshPreviewGrid;
        private bool _textInputFocused;
        private bool _viewModeMenuOpen;
        private bool _anchorMenuOpen = s_anchorMenuOpen;
        private DecorationSelectionMode _selectionMode = DecorationSelectionMode.Single;
        private bool _selectionXray;
        private bool _boxSelecting;
        private Vector2 _boxSelectStartMouse;
        private Vector2 _boxSelectCurrentMouse;
        private int _boxSelectCandidateCount;
        private bool _showMeshPalette = s_showMeshPalette;
        private bool _showInspectorPanel = s_showInspectorPanel;
        private bool _showOutlinerPanel = s_showOutlinerPanel;
        private bool _showAnchorPanel = s_showAnchorPanel;
        private bool _showSurfacePanel = s_showSurfacePanel;
        private bool _showSurfaceToolsPanel = s_showSurfaceToolsPanel;
        private bool _showInspectorColorPicker = s_showInspectorColorPicker;
        private bool _showTetherPins;
        private bool _anchorFollowDecoration = s_anchorFollowDecoration;
        private float _anchorFollowDistance = s_anchorFollowDistance;
        private string _anchorFollowDistanceText =
            s_anchorFollowDistance.ToString("0.###", CultureInfo.InvariantCulture);
        private string _snapMoveText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.DecorationMoveSnap);
        private string _snapRotateText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.DecorationRotateSnapDegrees);
        private string _snapScaleText = EsuTransformSnapSettings.Format(EsuTransformSnapSettings.DecorationScaleSnap);
        private readonly HashSet<string> _collapsedConstructs = new HashSet<string>();

        private MBuild_Ftd _buildOptions;
        private bool _previousWireframe;
        private readonly DecorationEditHistory _history = new DecorationEditHistory();
        private readonly DecorationEditTransactionSet _transactions = new DecorationEditTransactionSet();
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly DecorationEditorViewModeController _viewModes;
        private DecorationMeshPreviewRenderer _previewRenderer;

        private readonly List<DecorationHit> _hits = new List<DecorationHit>();
        private readonly List<OutlinerRow> _outlinerRows = new List<OutlinerRow>();
        private readonly HashSet<Decoration> _selection = new HashSet<Decoration>();
        private Decoration _outlinerRangeAnchor;
        private readonly List<Guid> _recentMeshes = new List<Guid>();
        private readonly List<DecorationMeshCatalogEntry> _meshCatalog =
            new List<DecorationMeshCatalogEntry>();
        private readonly Dictionary<Guid, DecorationMeshCatalogEntry> _meshByGuid =
            new Dictionary<Guid, DecorationMeshCatalogEntry>();
        private float _nextLightRefresh;
        private float _nextForecastRefresh;
        private MainConstruct _currentMainConstruct;
        private SerializationForecast _forecast;
        private DecorationUsageSnapshot _lastUsage;

        private Decoration _selected;
        private AllConstruct _selectedConstruct;
        private DecorationEditSnapshot _snapshot;
        private bool _selectedCreatedInSession;
        private bool _dirty;
        private float _applyCancelAttentionUntil = -1f;
        private DecorationEditorViewMode _viewMode = DecorationEditorViewMode.Mixed;
        private DecorationEditorTool _tool = DecorationEditorTool.Select;
        private DecorationEditorTool _toolBeforePaint = DecorationEditorTool.Select;
        private bool _showMeshPaletteBeforePaint = s_showMeshPalette;
        private DecorationTransformOrientation _transformOrientation = s_transformOrientation;
        private DecorationEditAxis _dragAxis;
        private Vector2 _dragMouseStart;
        private Vector3 _dragPositionStart;
        private SymmetryFollowContext _dragSymmetryFollow;
        private Vector3 _freeDragCameraRight;
        private Vector3 _freeDragCameraUp;
        private float _freeDragMetresPerPixel;
        private DecorationEditSnapshot _dragSnapshotStart;
        private Decoration[] _multiTransformDecorations = Array.Empty<Decoration>();
        private DecorationEditSnapshot[] _multiTransformBefore = Array.Empty<DecorationEditSnapshot>();
        private Vector3[] _multiTransformStartCenters = Array.Empty<Vector3>();
        private Vector3 _multiTransformPivotStart;
        private DecorationEditAxis _rotateDragAxis;
        private Vector2 _rotateDragMouseStart;
        private Vector2 _rotateDragCenterScreen;
        private Vector2 _rotateDragStartVector;
        private Vector3 _rotateStart;
        private Quaternion _rotateStartQuaternion;
        private DecorationEditSnapshot _rotateDragSnapshotStart;
        private DecorationEditAxis _scaleDragAxis;
        private Vector2 _scaleDragMouseStart;
        private Vector3 _scaleStart;
        private DecorationEditSnapshot _scaleDragSnapshotStart;
        private DecorationEditAxis _anchorDragAxis;
        private int _anchorDragSign;
        private Vector2 _anchorDragMouseStart;
        private Vector3i _anchorDragBaseTether;
        private Vector3 _anchorDragBasePosition;
        private DecorationEditSnapshot _anchorDragSnapshotStart;
        private SymmetryFollowContext _anchorDragSymmetryFollow;
        private bool _anchorPreviewValid;
        private Vector3i _anchorPreviewTether;
        private Vector3 _anchorPreviewPosition;
        private Vector2 _lastMouseGui;

        private string _meshFilter = string.Empty;
        private string _outlinerFilter = string.Empty;
        private string _meshKindFilter = "all";
        private DecorationMeshCatalogEntry _selectedMesh;
        private DecorationMeshCatalogEntry _hoveredMesh;
        private string _hoveredMeshHint;
        private DecorationMeshCatalogEntry _placingMesh;
        private DecorationEditorTool _toolBeforePlacement = DecorationEditorTool.Select;
        private GameObject _placementGhost;
        private MeshFilter _placementGhostFilter;
        private MeshRenderer _placementGhostRenderer;
        private Material _placementGhostMaterial;
        private DecorationMeshCatalogEntry _placementGhostEntry;
        private AllConstruct _placementConstruct;
        private Vector3i _placementAnchor;
        private Vector3 _placementLocalPosition;
        private Vector3 _placementWorldPosition;
        private Vector3 _placementWorldNormal;
        private bool _placementValid;
        private float _previewSpin;
        private readonly SurfaceDraft _surfaceDraft = new SurfaceDraft();
        private SurfaceDecorationPlan _surfacePlan;
        private string _surfaceMessage = "Click three block-surface points to seed a freeform surface.";
        private Vector2 _surfaceScroll;
        private string _surfaceThicknessText = "0.05";
        private string _surfaceColorText = "0";
        private DecorationEditAxis _surfaceDragAxis;
        private Vector2 _surfaceDragMouseStart;
        private Vector3 _surfaceDragPointStart;
        private SurfaceDraftSnapshot _surfaceDragSnapshotStart;
        private Vector3 _surfaceFreeDragCameraRight;
        private Vector3 _surfaceFreeDragCameraUp;
        private float _surfaceFreeDragMetresPerPixel = 0.01f;
        private readonly DecorationGeneratorDraft _generatorDraft = new DecorationGeneratorDraft();
        private readonly DecorationGeneratorSettings _generatorSettings = s_generatorSettings;
        private DecorationGeneratorPlan _generatorPlan;
        private string _generatorMessage = "Extra Tools: draw a path or place a shape center.";
        private SurfaceExtraTool _surfaceExtraTool = s_surfaceExtraTool;
        private Vector2 _generatorScroll;
        private string _generatorDiameterText = "0.05";
        private string _generatorRadiusText = "2.00";
        private string _generatorSegmentsText = "24";
        private string _generatorArcText = "360";
        private string _generatorHeightText = "2.00";
        private string _generatorTopRadiusText = "1.00";
        private string _generatorRingsText = "6";
        private string _generatorColorText = "0";
        private string _generatorMaterialText = string.Empty;
        private DecorationEditAxis _generatorDragAxis;
        private Vector2 _generatorDragMouseStart;
        private Vector3 _generatorDragPointStart;
        private DecorationGeneratorEditSnapshot _generatorDragSnapshotStart;
        private DecorationEditAxis _generatorRotateDragAxis;
        private Vector2 _generatorRotateDragMouseStart;
        private Vector2 _generatorRotateDragCenterScreen;
        private Vector2 _generatorRotateDragStartVector;
        private DecorationGeneratorEditSnapshot _generatorRotateSnapshotStart;
        private DecorationGeneratorEditSnapshot _generatorScaleSnapshotStart;
        private float _generatorScaleRadiusStart;
        private Vector3 _generatorFreeDragCameraRight;
        private Vector3 _generatorFreeDragCameraUp;
        private float _generatorFreeDragMetresPerPixel = 0.01f;
        private DecorationEditAxis _sharedAnchorDragAxis;
        private int _sharedAnchorDragSign;
        private Vector2 _sharedAnchorDragMouseStart;
        private Vector3i _sharedAnchorDragStart;
        private SurfaceDraftSnapshot _sharedSurfaceAnchorDragSnapshotStart;
        private DecorationGeneratorEditSnapshot _sharedGeneratorAnchorDragSnapshotStart;
        private bool _sharedAnchorDragGenerator;
        private bool _pendingSurfaceSharedAnchorPick;
        private bool _pendingGeneratorSharedAnchorPick;
        private readonly string[] _positionText = new string[3];
        private readonly string[] _scaleText = new string[3];
        private readonly string[] _orientationText = new string[3];
        private string _colorText = string.Empty;
        private Vector2 _paintScroll;
        private int _paintColor;
        private string _materialText = string.Empty;
        private string _materialFilter = string.Empty;
        private string _generatorMeshFilter = string.Empty;
        private bool _showMaterialPicker;
        private bool _showGeneratorMeshPicker;
        private bool _showGeneratorMaterialPicker = true;
        private bool _showSurfaceDraftList = s_showSurfaceDraftList;
        private bool _showGeneratorColorPicker = s_showGeneratorColorPicker;
        private Vector2 _materialScroll;
        private Vector2 _generatorMeshScroll;
        private Vector2 _generatorMaterialScroll;
        private float _surfaceExtraToolsViewportHeight;
        private float _generatorMaterialListHeight;
        private List<MaterialCatalogEntry> _materialCatalog;
        private SurfaceBuilderTool _surfaceBuilderTool = s_surfaceBuilderTool;
        private SurfaceContextTargetKind _surfaceContextTargetKind = SurfaceContextTargetKind.None;
        private Rect _surfaceContextRect;
        private int _surfaceContextIndex = -1;
        private SurfaceEdge _surfaceContextEdge = new SurfaceEdge(-1, -1);
        private Decoration _decorationContextTarget;
        private AllConstruct _decorationContextConstruct;
        private Rect _decorationContextRect;
        private readonly List<DecorationDeletionRecord> _deletedDecorations =
            new List<DecorationDeletionRecord>();

        internal bool Active { get; private set; }

        internal bool CloseRequested { get; private set; }

        internal bool CloseApplies { get; private set; }

        internal bool SwitchToSmartBuildRequested { get; private set; }

        internal void ClearSwitchToSmartBuildRequest() =>
            SwitchToSmartBuildRequested = false;

        internal bool IsSurfaceMode =>
            _tool == DecorationEditorTool.Surface;

        internal bool HasUnappliedChanges =>
            _dirty ||
            _transactions.HasChanges ||
            _deletedDecorations.Count > 0 ||
            _placingMesh != null ||
            _dragAxis != DecorationEditAxis.None ||
            _rotateDragAxis != DecorationEditAxis.None ||
            _scaleDragAxis != DecorationEditAxis.None ||
            _anchorDragAxis != DecorationEditAxis.None ||
            _surfaceDragAxis != DecorationEditAxis.None ||
            _generatorDragAxis != DecorationEditAxis.None ||
            _generatorRotateDragAxis != DecorationEditAxis.None ||
            _surfaceDraft.HasDraft ||
            _generatorDraft.HasDraft;

        internal DecorationEditSession(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _pointerProbe = new DecorationPointerProbe(_build);
            _viewModes = new DecorationEditorViewModeController(_build);
        }

        internal void Begin()
        {
            Active = true;
            DecorationEditorInputScope.Begin();
            ApplyFocusView();
            _previewRenderer = new DecorationMeshPreviewRenderer();
            _meshCatalog.AddRange(DecorationMeshCatalog.Build());
            _meshByGuid.Clear();
            foreach (DecorationMeshCatalogEntry entry in _meshCatalog)
            {
                if (!_meshByGuid.ContainsKey(entry.Guid))
                    _meshByGuid.Add(entry.Guid, entry);
            }
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            _materialCatalog = BuildMaterialCatalog();
            _showMaterialPicker = true;
            SyncGeneratorTextFromSettings();
            SyncSnapTextFromSettings();
        }

        internal bool CanSwitchToSmartBuild(out string reason)
        {
            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                reason = "Place or cancel the pending symmetry plane before switching modes.";
                return false;
            }

            if (_textInputFocused)
            {
                reason = "Leave the active Decoration Edit text field before switching modes.";
                return false;
            }

            if (_placingMesh != null)
            {
                reason = "Cancel mesh placement before switching to Smart Builder.";
                return false;
            }

            if (_surfaceDraft.HasDraft || _generatorDraft.HasDraft)
            {
                reason = "Place or clear the Surface draft before switching to Smart Builder.";
                return false;
            }

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None ||
                _generatorDragAxis != DecorationEditAxis.None ||
                _generatorRotateDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty || _transactions.HasChanges || _deletedDecorations.Count > 0)
            {
                RefreshApplyCancelAttention();
                reason = "Apply or Cancel Decoration Edit changes before switching to Smart Builder.";
                return false;
            }

            reason = null;
            return true;
        }

        internal bool TrySwitchToSurfaceBuilder(out string reason)
        {
            if (IsSurfaceMode)
            {
                reason = null;
                return true;
            }

            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                reason = "Place or cancel the pending symmetry plane before switching modes.";
                return false;
            }

            if (_textInputFocused)
            {
                reason = "Leave the active Decoration Edit text field before switching modes.";
                return false;
            }

            if (_placingMesh != null)
            {
                reason = "Cancel mesh placement before switching to Surface Builder.";
                return false;
            }

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None ||
                _generatorDragAxis != DecorationEditAxis.None ||
                _generatorRotateDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty || _transactions.HasChanges || _deletedDecorations.Count > 0)
            {
                RefreshApplyCancelAttention();
                reason = "Apply or Cancel Decoration Edit changes before switching to Surface Builder.";
                return false;
            }

            if (_tool == DecorationEditorTool.Paint)
                _showMeshPalette = _showMeshPaletteBeforePaint;
            _tool = DecorationEditorTool.Surface;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            CloseDecorationContextMenu();
            reason = null;
            return true;
        }

        internal void End(bool apply, bool notify = true, bool preserveSharedHud = false)
        {
            if (!Active)
                return;

            try
            {
                if (apply)
                    ApplySelection(notify);
                else
                    CancelSelection(notify);
            }
            finally
            {
                DecorationEditorInputScope.End();
                Active = false;
                CloseRequested = false;
                SwitchToSmartBuildRequested = false;
                RestoreFocusView();
                _hits.Clear();
                _outlinerRows.Clear();
                _selection.Clear();
                _boxSelecting = false;
                _boxSelectCandidateCount = 0;
                ClearMultiTransformState();
                _meshByGuid.Clear();
                _meshCatalog.Clear();
                _selected = null;
                _selectedConstruct = null;
                _snapshot = null;
                _selectedCreatedInSession = false;
                _dirty = false;
                _dragAxis = DecorationEditAxis.None;
                _dragSnapshotStart = null;
                _dragSymmetryFollow = null;
                _rotateDragAxis = DecorationEditAxis.None;
                _rotateDragSnapshotStart = null;
                _scaleDragAxis = DecorationEditAxis.None;
                _scaleDragSnapshotStart = null;
                _anchorDragAxis = DecorationEditAxis.None;
                _anchorDragSnapshotStart = null;
                _anchorDragSymmetryFollow = null;
                _anchorPreviewValid = false;
                ClearSharedAnchorDragState();
                _pendingSurfaceSharedAnchorPick = false;
                _pendingGeneratorSharedAnchorPick = false;
                if (!preserveSharedHud)
                    DecorationEditorOverlay.Clear();
                _viewModeMenuOpen = false;
                _anchorMenuOpen = false;
                CloseDecorationContextMenu();
                _placingMesh = null;
                _placementConstruct = null;
                _placementLocalPosition = Vector3.zero;
                _placementWorldPosition = Vector3.zero;
                _placementWorldNormal = Vector3.zero;
                _placementValid = false;
                ClearSurfaceDraft(notify: false);
                ClearGeneratorDraft(notify: false);
                _surfaceDragAxis = DecorationEditAxis.None;
                DestroyPlacementGhost();
                _history.Clear();
                _transactions.Apply();
                _deletedDecorations.Clear();
                _previewRenderer?.Dispose();
                _previewRenderer = null;
                _materialCatalog = null;
                _forecast = null;
                _lastUsage = null;
            }
        }

        internal void Update()
        {
            if (!Active)
                return;

            EsuHudNotifications.SetActiveSource(CurrentModeLogSource());
            _lastMouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            DecorationEditorInputScope.SetMouseOverEditorUi(IsMouseOverEditorUi(_lastMouseGui));
            if (DecorationEditorInputScope.MouseOverEditorUi &&
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
            }
            RefreshDecorationCache(force: false);
            RefreshForecast(force: false);
            _viewModes.Tick(_viewMode);
            DecorationEditorOverlay.BeginFrame();
            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None ||
                _generatorDragAxis != DecorationEditAxis.None ||
                _generatorRotateDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
            }
            HandleEditorKeybinds();
            UpdatePlacementState();
            UpdatePlacementGhost();
            HandleSceneInput();
            RefreshLiveTransformFields();
            DrawWorldOverlay();
        }

        internal void OnGUI()
        {
            if (!Active)
                return;

            _hoveredMesh = null;
            _hoveredMeshHint = null;
            DecorationEditorTheme.Ensure();
            if (Event.current.type == EventType.Repaint)
            {
                _previewSpin += Time.unscaledDeltaTime * 70f;
                DecorationEditorOverlay.Render();
            }

            int previousDepth = GUI.depth;
            GUI.depth = -10000;
            try
            {
                GUI.ModalWindow(
                    _modalWindowId,
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    DrawModalEditorWindow,
                    GUIContent.none,
                    GUIStyle.none);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
            _textInputFocused = GUIUtility.keyboardControl != 0;
        }

        private void DrawModalEditorWindow(int id)
        {
            DrawEditorShell();
            Event current = Event.current;
            if (ShouldConsumeGuiEvent(current))
            {
                if (current.type == EventType.ScrollWheel)
                    DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
                else
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                }
                current.Use();
            }
        }

        private bool ShouldConsumeGuiEvent(Event current)
        {
            if (current == null)
                return false;

            bool mouseEvent =
                current.type == EventType.MouseDown ||
                current.type == EventType.MouseUp ||
                current.type == EventType.MouseDrag ||
                current.type == EventType.ScrollWheel;
            if (!mouseEvent)
                return false;

            bool overUi = IsMouseOverEditorUi(current.mousePosition);
            if (overUi)
                return true;

            if (current.type == EventType.ScrollWheel)
                return false;

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None ||
                _generatorDragAxis != DecorationEditAxis.None ||
                _generatorRotateDragAxis != DecorationEditAxis.None ||
                _boxSelecting ||
                _draggingStackDivider != StackDividerKind.None)
                return current.type != EventType.ScrollWheel;

            if (_placingMesh != null)
                return current.type == EventType.MouseDown &&
                       (current.button == 0 || current.button == 1);

            return current.button == 0 &&
                   (_tool == DecorationEditorTool.Select ||
                    _tool == DecorationEditorTool.Move ||
                    _tool == DecorationEditorTool.Rotate ||
                    _tool == DecorationEditorTool.Scale ||
                    _tool == DecorationEditorTool.Anchor ||
                    _tool == DecorationEditorTool.Surface ||
                    _tool == DecorationEditorTool.Paint);
        }

        private void DrawEditorShell()
        {
            EsuHudNotifications.SetActiveSource(CurrentModeLogSource());
            EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed());
            ApplyLayoutResetIfNeeded();
            Rect toolbarRect = ToolbarRect();
            Rect rightRect = RightPanelRect();
            Rect bottomRect = StatusRect(rightRect);
            Rect bottomInnerScreen = EsuHudLayout.PanelInnerRect(bottomRect);
            _anchorSettingsButtonScreenRect = BottomHeaderRects(
                new Rect(
                    bottomInnerScreen.x,
                    bottomInnerScreen.y,
                    bottomInnerScreen.width,
                    EsuHudLayout.Scale(24f))).AnchorSettings;
            Rect meshRect = MeshPaletteRect();
            bool leftStackVisible = IsLeftPanelStackVisible();
            bool rightStackVisible = IsRightPanelStackVisible();
            Vector2 mouse = Event.current.mousePosition;
            _mouseOverWindow = toolbarRect.Contains(mouse) ||
                               EsuHudNotifications.ContainsMouse(mouse) ||
                               (_viewModeMenuOpen && ViewModeMenuRect(toolbarRect).Contains(mouse)) ||
                               (_anchorMenuOpen && AnchorMenuRect().Contains(mouse)) ||
                               IsSurfaceContextMenuAt(mouse) ||
                               IsDecorationContextMenuAt(mouse) ||
                               (rightStackVisible && rightRect.Contains(mouse)) ||
                               bottomRect.Contains(mouse) ||
                               (leftStackVisible && meshRect.Contains(mouse));
            DecorationEditorInputScope.SetMouseOverEditorUi(_mouseOverWindow);

            if (leftStackVisible)
            {
                HandlePanelResize(
                    ref _meshPaletteRect,
                    ref _resizingMeshPalette,
                    ref _meshPaletteResizeStart,
                    ref _meshPaletteResizeMouseStart,
                    resizeFromLeft: false,
                    MinMeshPaletteWidth(),
                    MinMeshPaletteHeight(),
                    MaxMeshPaletteWidth(),
                    MaxMeshPaletteHeight(),
                    ToolbarBottomLimit(),
                    BottomPanelHeight() + EsuHudLayout.Scale(12f));
                meshRect = _meshPaletteRect;
                HandleMeshPaletteDrag(meshRect);
            }

            if (rightStackVisible)
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
                    MaxRightPanelHeight(),
                    ToolbarBottomLimit(),
                    BottomPanelHeight() + EsuHudLayout.Scale(12f));
                rightRect = _rightPanelRect;
            }

            GUI.Box(toolbarRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect toolbarInner = EsuHudLayout.PanelInnerRect(toolbarRect);
            GUILayout.BeginArea(toolbarInner);
            DrawTopToolbar(
                new Rect(0f, 0f, toolbarInner.width, toolbarInner.height),
                toolbarInner.position);
            GUILayout.EndArea();
            EsuHudNotifications.DrawExpandedPopup();

            if (leftStackVisible)
            {
                DrawLeftPanelStack(meshRect);
                EsuHudLayout.DrawResizeGrip(meshRect, leftEdge: false);
                EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(meshRect, leftEdge: false), "Drag to resize the left panel stack.");
            }

            if (rightStackVisible)
            {
                DrawRightPanel(rightRect);
                EsuHudLayout.DrawResizeGrip(rightRect, leftEdge: true);
                EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(rightRect, leftEdge: true), "Drag to resize the right panel stack.");
            }

            GUI.Box(bottomRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect bottomInner = EsuHudLayout.PanelInnerRect(bottomRect);
            GUILayout.BeginArea(bottomInner);
            DrawBottomPanel(bottomInner.width, bottomInner.height);
            GUILayout.EndArea();
            DrawAnchorMenu();

            DrawMeshPreviewCard();
            DrawSurfaceContextMenu();
            DrawDecorationContextMenu();
            DrawBoxSelectionMarquee();
            EsuConsoleWindow.Draw();
            DrawViewModeMenu(toolbarRect);
            EsuCursorTooltip.Draw();
            PersistPanelState();
        }

        private bool TooltipInputSuppressed() =>
            _dragAxis != DecorationEditAxis.None ||
            _rotateDragAxis != DecorationEditAxis.None ||
            _scaleDragAxis != DecorationEditAxis.None ||
            _anchorDragAxis != DecorationEditAxis.None ||
            _surfaceDragAxis != DecorationEditAxis.None ||
            _generatorRotateDragAxis != DecorationEditAxis.None ||
            _boxSelecting ||
            _draggingMeshPalette ||
            _resizingMeshPalette ||
            _resizingRightPanel ||
            _draggingStackDivider != StackDividerKind.None;

        private Rect ToolbarRect()
        {
            return EsuHudLayout.ToolbarRect(54f);
        }

        private Rect RightPanelRect()
        {
            if (_rightPanelRect.width < 1f ||
                _rightPanelRect.height < 1f)
            {
                _rightPanelRect = DefaultRightPanelRect();
            }

            _rightPanelRect = EsuHudLayout.ClampPanel(
                _rightPanelRect,
                MinRightPanelWidth(),
                MinRightPanelHeight(),
                MaxRightPanelWidth(),
                MaxRightPanelHeight(),
                ToolbarBottomLimit(),
                BottomPanelHeight() + EsuHudLayout.Scale(12f));
            s_rightPanelRect = _rightPanelRect;
            return _rightPanelRect;
        }

        private Rect StatusRect(Rect rightRect)
        {
            float height = BottomPanelHeight();
            float margin = EsuHudLayout.Scale(10f);
            return new Rect(margin, Screen.height - height - margin, Screen.width - margin * 2f, height);
        }

        private Rect MeshPaletteRect()
        {
            _meshPaletteRect = EsuHudLayout.ClampPanel(
                _meshPaletteRect,
                MinMeshPaletteWidth(),
                MinMeshPaletteHeight(),
                MaxMeshPaletteWidth(),
                MaxMeshPaletteHeight(),
                ToolbarBottomLimit(),
                BottomPanelHeight() + EsuHudLayout.Scale(12f));
            s_meshPaletteRect = _meshPaletteRect;
            return _meshPaletteRect;
        }

        private float BottomPanelHeight()
        {
            return Mathf.Clamp(Screen.height * 0.13f, EsuHudLayout.Scale(118f), EsuHudLayout.Scale(146f));
        }

        private float ToolbarBottomLimit() => ToolbarRect().yMax + EsuHudLayout.Scale(8f);

        private Rect DefaultMeshPaletteRect()
        {
            float width = Mathf.Min(EsuHudLayout.Scale(520f), Mathf.Max(EsuHudLayout.Scale(420f), Screen.width * 0.28f));
            float topLimit = ToolbarBottomLimit();
            float bottomLimit = BottomPanelHeight() + EsuHudLayout.Scale(12f);
            float height = Mathf.Min(
                MaxMeshPaletteHeight(),
                Mathf.Max(MinMeshPaletteHeight(), Screen.height - topLimit - bottomLimit));
            return new Rect(EsuHudLayout.Scale(18f), topLimit, width, height);
        }

        private Rect DefaultRightPanelRect()
        {
            float width = Mathf.Min(EsuHudLayout.Scale(390f), Mathf.Max(EsuHudLayout.Scale(300f), Screen.width * 0.23f));
            float topLimit = ToolbarBottomLimit();
            float bottomLimit = BottomPanelHeight() + EsuHudLayout.Scale(12f);
            float height = Mathf.Max(MinRightPanelHeight(), Screen.height - topLimit - bottomLimit);
            float margin = EsuHudLayout.Scale(10f);
            return new Rect(Screen.width - width - margin, topLimit, width, height);
        }

        private float MinMeshPaletteWidth() => EsuHudLayout.Scale(330f);

        private float MinMeshPaletteHeight() => EsuHudLayout.Scale(330f);

        private float MaxMeshPaletteWidth() => Mathf.Max(MinMeshPaletteWidth(), Screen.width * 0.62f);

        private float MaxMeshPaletteHeight() => Mathf.Max(MinMeshPaletteHeight(), Screen.height - ToolbarBottomLimit() - BottomPanelHeight() - EsuHudLayout.Scale(16f));

        private float MinRightPanelWidth() => EsuHudLayout.Scale(260f);

        private float MinRightPanelHeight() => EsuHudLayout.Scale(230f);

        private float MaxRightPanelWidth() => Mathf.Max(MinRightPanelWidth(), Screen.width * 0.58f);

        private float MaxRightPanelHeight() => Mathf.Max(MinRightPanelHeight(), Screen.height - ToolbarBottomLimit() - BottomPanelHeight() - EsuHudLayout.Scale(16f));

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _meshPaletteRect = DefaultMeshPaletteRect();
            _rightPanelRect = DefaultRightPanelRect();
            _leftStackBottomRatio = LeftStackDefaultBottomRatio;
            _rightStackBottomRatio = RightStackDefaultBottomRatio;
            s_leftStackBottomRatio = _leftStackBottomRatio;
            s_rightStackBottomRatio = _rightStackBottomRatio;
            _draggingStackDivider = StackDividerKind.None;
            _layoutResetGeneration = EsuHudLayout.ResetGeneration;
            s_layoutGeneration = _layoutResetGeneration;
        }

        private bool IsLeftPanelStackVisible() =>
            IsSurfaceMode
                ? _showSurfacePanel
                : _tool == DecorationEditorTool.Paint || _showMeshPalette || _showInspectorPanel;

        private bool IsRightPanelStackVisible() =>
            IsSurfaceMode
                ? _showSurfaceToolsPanel
                : _showOutlinerPanel || _showAnchorPanel;

        private bool IsMouseOverEditorUi(Vector2 mouse) =>
            ToolbarRect().Contains(mouse) ||
            EsuHudNotifications.ContainsMouse(mouse) ||
            (_viewModeMenuOpen && ViewModeMenuRect(ToolbarRect()).Contains(mouse)) ||
            (_anchorMenuOpen && AnchorMenuRect().Contains(mouse)) ||
            IsSurfaceContextMenuAt(mouse) ||
            IsDecorationContextMenuAt(mouse) ||
            (IsRightPanelStackVisible() && RightPanelRect().Contains(mouse)) ||
            StatusRect(RightPanelRect()).Contains(mouse) ||
            (IsLeftPanelStackVisible() && MeshPaletteRect().Contains(mouse));

        private void HandleMeshPaletteDrag(Rect meshRect)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (_resizingMeshPalette)
                return;

            Rect title = new Rect(meshRect.x, meshRect.y, meshRect.width, EsuHudLayout.Scale(28f));
            if (current.type == EventType.MouseDown)
                _draggingMeshPalette = false;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                title.Contains(current.mousePosition) &&
                !ShouldSkipLeftPanelDrag(current.mousePosition, meshRect))
            {
                _draggingMeshPalette = true;
                _meshPaletteDragOffset = current.mousePosition - new Vector2(meshRect.x, meshRect.y);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && _draggingMeshPalette)
            {
                _meshPaletteRect.x = current.mousePosition.x - _meshPaletteDragOffset.x;
                _meshPaletteRect.y = current.mousePosition.y - _meshPaletteDragOffset.y;
                _meshPaletteRect = EsuHudLayout.ClampPanel(
                    _meshPaletteRect,
                    MinMeshPaletteWidth(),
                    MinMeshPaletteHeight(),
                    MaxMeshPaletteWidth(),
                    MaxMeshPaletteHeight(),
                    ToolbarBottomLimit(),
                    BottomPanelHeight() + EsuHudLayout.Scale(12f));
                s_meshPaletteRect = _meshPaletteRect;
                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                _draggingMeshPalette = false;
            }
        }

        private bool ShouldSkipLeftPanelDrag(Vector2 mouse, Rect leftPanelRect)
        {
            bool showBuilderPanel = IsSurfaceMode || _tool == DecorationEditorTool.Paint || _showMeshPalette;
            if (showBuilderPanel || !_showInspectorPanel)
                return false;

            Rect hideRect = InspectorHeaderHideRect(InspectorHeaderRect(leftPanelRect, localToPanel: false));
            return hideRect.Contains(mouse);
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
                rect = EsuHudLayout.ClampPanel(next, minWidth, minHeight, maxWidth, maxHeight, topLimit, bottomLimit);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                resizing = false;
        }

        private void PersistPanelState()
        {
            s_meshPaletteRect = _meshPaletteRect;
            s_rightPanelRect = _rightPanelRect;
            s_layoutGeneration = _layoutResetGeneration;
            s_leftStackBottomRatio = _leftStackBottomRatio;
            s_rightStackBottomRatio = _rightStackBottomRatio;
            s_showMeshPalette = _tool == DecorationEditorTool.Paint
                ? _showMeshPaletteBeforePaint
                : _showMeshPalette;
            s_showInspectorPanel = _showInspectorPanel;
            s_showOutlinerPanel = _showOutlinerPanel;
            s_showAnchorPanel = _showAnchorPanel;
            s_showSurfacePanel = _showSurfacePanel;
            s_showSurfaceToolsPanel = _showSurfaceToolsPanel;
            s_showSurfaceDraftList = _showSurfaceDraftList;
            s_showInspectorColorPicker = _showInspectorColorPicker;
            s_showGeneratorColorPicker = _showGeneratorColorPicker;
            s_meshPreviewGrid = _meshPreviewGrid;
            s_transformOrientation = _transformOrientation;
            s_anchorMenuOpen = _anchorMenuOpen;
            s_anchorFollowDecoration = _anchorFollowDecoration;
            s_anchorFollowDistance = _anchorFollowDistance;
            s_surfaceExtraTool = _surfaceExtraTool;
            s_surfaceBuilderTool = _surfaceBuilderTool;
        }

        private void DrawTopToolbar(Rect toolbarRect, Vector2 toolbarScreenOrigin)
        {
            EsuHudLayout.CenteredToolbarFrame frame =
                EsuHudLayout.CalculateCenteredToolbarFrame(toolbarRect);
            EsuHudLayout.ToolbarBudget budget = frame.Budget;

            GUILayout.BeginArea(frame.Rect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth));
            {
                ModeSwitchButton();
                if (IsSurfaceMode)
                {
                    SurfaceToolButton(SurfaceBuilderTool.Draw, "draw", "Draw", "Place and edit triangle surface points.");
                    SurfaceToolButton(SurfaceBuilderTool.Path, "axis", "Path", "Draw pipe, cable, or rail paths.");
                    SurfaceToolButton(SurfaceBuilderTool.Circle, "axis", "Shape", "Place and edit a center-based generator shape.");
                    SurfaceToolButton(SurfaceBuilderTool.Move, "move", "Move", "Move selected surface or generator points.");
                    SurfaceToolButton(SurfaceBuilderTool.Rotate, "rotate", "Rotate", "Rotate the selected generator shape.");
                    SurfaceToolButton(SurfaceBuilderTool.Scale, "scale", "Scale", "Scale the selected generator shape radius.");
                    OrientationToggleButton();
                    SymmetryButton(DecorationEditAxis.X);
                    SymmetryButton(DecorationEditAxis.Y);
                    SymmetryButton(DecorationEditAxis.Z);
                    ToolButton(DecorationEditorTool.Focus, "visibility", "View", "Current view: " + ViewModeShortName());
                }
                else
                {
                    ToolButton(DecorationEditorTool.Select, "select", SelectToolLabel(), "Click decoration centers or drag a selection box.");
                    ToolButton(DecorationEditorTool.Move, "move", "Move", "Drag XYZ handles. Snaps to " + EsuTransformSnapSettings.Format(DecorationMoveSnap) + "m.");
                    ToolButton(DecorationEditorTool.Rotate, "rotate", "Rotate", "Drag RGB rotation rings. Snaps to " + EsuTransformSnapSettings.Format(DecorationRotateSnapDegrees) + " degrees.");
                    ToolButton(DecorationEditorTool.Scale, "scale", "Scale", "Drag RGB scale handles. Snaps to " + EsuTransformSnapSettings.Format(DecorationScaleSnap) + ".");
                    OrientationToggleButton();
                    SymmetryButton(DecorationEditAxis.X);
                    SymmetryButton(DecorationEditAxis.Y);
                    SymmetryButton(DecorationEditAxis.Z);
                    AnchorMenuButton();
                    ToolButton(DecorationEditorTool.Paint, "paint", "Paint", "Edit color and material replacement.");
                    ToolButton(DecorationEditorTool.Focus, "visibility", "View", "Current view: " + ViewModeShortName());
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(budget.Gap);
            EsuHudNotifications.DrawToolbarSlot(
                frame.Rect,
                budget.NotificationWidth,
                CurrentModeToolbarLabel(),
                toolbarScreenOrigin + frame.Rect.position);
            GUILayout.Space(budget.Gap);
            GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth));
            if (IsSurfaceMode)
            {
                PanelToggle("build", "Surf", ref _showSurfacePanel, "Show or hide the Surface Builder draft panel.");
                PanelToggle("settings", "Tools", ref _showSurfaceToolsPanel, "Show or hide Surface Builder extra tools.");
            }
            else
            {
                PanelToggle("mesh", "Pal", ref _showMeshPalette, "Show or hide the mesh palette.");
                PanelToggle("outliner", "Out", ref _showOutlinerPanel, "Show or hide the decoration outliner.");
                PanelToggle("settings", "Insp", ref _showInspectorPanel, "Show or hide the selected decoration inspector.");
                PanelToggle("anchor", "Anch", ref _showAnchorPanel, "Show or hide decorations sharing the selected anchor.");
            }
            if (IconButton("undo", $"U{_history.UndoCount}", DecorationEditorTheme.Button, "Ctrl+Z: undo the last editor action.", _history.CanUndo))
                UndoEdit();
            if (IconButton("redo", $"R{_history.RedoCount}", DecorationEditorTheme.Button, "Ctrl+Y/Ctrl+Shift+Z: redo the last editor action.", _history.CanRedo))
                RedoEdit();
            if (AttentionIconButton("save", "Apply", DecorationEditorTheme.Button, "Commit preview changes."))
                ApplySelection();
            if (AttentionIconButton("cancel", "Cancel", DecorationEditorTheme.Button, "Restore the current preview."))
                CancelSelection();
            if (IconButton("delete", "Close", DecorationEditorTheme.Button, "Close and restore un-applied changes."))
            {
                CloseRequested = true;
                CloseApplies = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private string CurrentModeToolbarLabel() =>
            IsSurfaceMode
                ? "ESU mode: Surface Builder."
                : "ESU mode: Decoration Edit.";

        private string CurrentModeLogSource() =>
            IsSurfaceMode
                ? "Surface Builder"
                : "Decoration Edit";

        private void ModeSwitchButton()
        {
            bool surface = IsSurfaceMode;
            if (GUILayout.Button(
                    new GUIContent(
                        surface ? "Surf" : "Deco",
                        DecorationEditorIconCatalog.Get("build"),
                        surface
                            ? "Tab: switch to Smart Builder when Surface Builder is clean."
                            : "Tab: switch to Surface Builder when Decoration Edit is clean."),
                    DecorationEditorTheme.ToolButton(true),
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                if (surface)
                {
                    SwitchToSmartBuildRequested = true;
                    return;
                }

                if (TrySwitchToSurfaceBuilder(out string reason))
                    InfoStore.Add("ESU mode: Surface Builder.");
                else
                    InfoStore.Add(reason);
            }
        }

        private void OrientationToggleButton()
        {
            bool local = _transformOrientation == DecorationTransformOrientation.Local;
            if (GUILayout.Button(
                    new GUIContent(
                        local ? "Local" : "Global",
                        DecorationEditorIconCatalog.Get("axis"),
                        "Transform orientation for Move, Rotate, and Scale."),
                    DecorationEditorTheme.ToolButton(local),
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                _transformOrientation = local
                    ? DecorationTransformOrientation.Global
                    : DecorationTransformOrientation.Local;
                s_transformOrientation = _transformOrientation;
            }
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
                InfoStore.Add(
                    active && !DecoLimitLifter.EsuSymmetry.IsPending(axis)
                        ? "Symmetry " + label + " updated."
                        : "Click the construct grid to place the " + label + " symmetry plane.");
            }
        }

        private void AnchorMenuButton()
        {
            if (IconButton(
                    "anchor",
                    "Anchor",
                    DecorationEditorTheme.ToolButton(_tool == DecorationEditorTool.Anchor),
                    "Select and move decoration anchors. Anchor settings are in the bottom status panel."))
            {
                SetActiveTool(DecorationEditorTool.Anchor);
                _anchorMenuOpen = false;
                _viewModeMenuOpen = false;
            }
        }

        private void PanelToggle(string icon, string label, ref bool state, string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    DecorationEditorTheme.ToolButton(state),
                    GUILayout.Width(EsuHudLayout.Scale(44f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                state = !state;
            }
        }

        private void SurfaceToolButton(
            SurfaceBuilderTool tool,
            string icon,
            string label,
            string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    DecorationEditorTheme.ToolButton(_surfaceBuilderTool == tool),
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                SetSurfaceBuilderTool(tool);
            }
        }

        private void SetSurfaceBuilderTool(SurfaceBuilderTool tool)
        {
            _surfaceBuilderTool = tool;
            s_surfaceBuilderTool = tool;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            CloseSurfacePointContextMenu();
            CloseDecorationContextMenu();
            if (tool == SurfaceBuilderTool.Path)
                SetSurfaceExtraTool(SurfaceExtraTool.Path);
            else if (tool == SurfaceBuilderTool.Circle &&
                     !DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool))
            {
                SetSurfaceExtraTool(SurfaceExtraTool.Circle);
            }
        }

        private void ToolButton(
            DecorationEditorTool tool,
            string icon,
            string label,
            string tooltip,
            bool enabled = true)
        {
            GUIStyle style = DecorationEditorTheme.ToolButton(_tool == tool, enabled);
            var content = new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip);
            if (GUILayout.Button(
                    content,
                    style,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(40f))) &&
                enabled)
            {
                if (tool == DecorationEditorTool.Focus)
                {
                    _viewModeMenuOpen = !_viewModeMenuOpen;
                }
                else
                {
                    SetActiveTool(tool);
                }
            }
        }

        private void SetActiveTool(DecorationEditorTool tool)
        {
            if (tool == DecorationEditorTool.Paint)
            {
                TogglePaintTool();
                return;
            }

            CloseDecorationContextMenu();
            if (_tool == DecorationEditorTool.Paint)
                _showMeshPalette = _showMeshPaletteBeforePaint;
            if (tool == DecorationEditorTool.Select && _tool == DecorationEditorTool.Select)
            {
                ToggleSelectionMode();
                return;
            }

            if (tool != DecorationEditorTool.Select)
                CancelBoxSelection();
            _tool = tool;
        }

        private string SelectToolLabel() =>
            _selectionMode == DecorationSelectionMode.Box ? "Box" : "Select";

        private void ToggleSelectionMode()
        {
            if (_selectionMode == DecorationSelectionMode.Box)
            {
                _selectionMode = DecorationSelectionMode.Single;
                CancelBoxSelection();
            }
            else
            {
                _selectionMode = DecorationSelectionMode.Box;
            }
        }

        private void TogglePaintTool()
        {
            CloseDecorationContextMenu();
            if (_tool == DecorationEditorTool.Paint)
            {
                _tool = IsPaintRestorableTool(_toolBeforePaint)
                    ? _toolBeforePaint
                    : DecorationEditorTool.Select;
                _showMeshPalette = _showMeshPaletteBeforePaint;
                return;
            }

            _toolBeforePaint = IsPaintRestorableTool(_tool)
                ? _tool
                : DecorationEditorTool.Select;
            _showMeshPaletteBeforePaint = _showMeshPalette;
            _showMeshPalette = true;
            CancelBoxSelection();
            _tool = DecorationEditorTool.Paint;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            CloseDecorationContextMenu();
        }

        private static bool IsPaintRestorableTool(DecorationEditorTool tool) =>
            tool != DecorationEditorTool.Paint &&
            tool != DecorationEditorTool.Surface &&
            tool != DecorationEditorTool.Mesh &&
            tool != DecorationEditorTool.Focus;

        private void DrawBoxSelectionMarquee()
        {
            if (!_boxSelecting)
                return;

            Rect rect = BoxSelectionRect();
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0.05f, 0.9f, 1f, 0.14f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(0.05f, 0.9f, 1f, 0.95f);
                DrawGuiBorder(rect, Mathf.Max(1f, EsuHudLayout.Scale(1f)));

                string label = _boxSelectCandidateCount.ToString("N0", CultureInfo.InvariantCulture);
                Rect labelRect = new Rect(
                    rect.xMin,
                    Mathf.Max(0f, rect.yMin - EsuHudLayout.Scale(22f)),
                    EsuHudLayout.Scale(118f),
                    EsuHudLayout.Scale(20f));
                GUI.color = new Color(0f, 0.08f, 0.1f, 0.86f);
                GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 1f);
                GUI.Label(
                    labelRect,
                    label + (_selectionXray ? " x-ray" : " visible"),
                    DecorationEditorTheme.Mini);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static void DrawGuiBorder(Rect rect, float thickness)
        {
            thickness = Mathf.Max(1f, thickness);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
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

        private Rect AnchorMenuRect()
        {
            float margin = EsuHudLayout.Scale(8f);
            float maxWidth = Mathf.Max(1f, Screen.width - margin * 2f);
            float minWidth = Mathf.Min(EsuHudLayout.Scale(300f), maxWidth);
            float width = Mathf.Clamp(AnchorMenuPreferredWidth(), minWidth, maxWidth);
            bool wrapsPresets = AnchorMenuNeedsPresetWrap(width);
            float height = EsuHudLayout.Scale(wrapsPresets ? 122f : 88f);
            Rect button = _anchorSettingsButtonScreenRect.width > 1f
                ? _anchorSettingsButtonScreenRect
                : new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
            float x = Mathf.Clamp(
                button.xMax - width,
                margin,
                Screen.width - width - margin);
            float y = Mathf.Clamp(
                button.y - height - EsuHudLayout.Scale(4f),
                margin,
                Mathf.Max(margin, Screen.height - height - margin));
            return new Rect(x, y, width, height);
        }

        private static float AnchorMenuPreferredWidth() =>
            EsuHudLayout.Scale(536f);

        private static bool AnchorMenuNeedsPresetWrap(float menuWidth)
        {
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(6f);
            float compactGap = EsuHudLayout.Scale(4f);
            float available = menuWidth - padding * 2f;
            float firstRowWidth =
                EsuHudLayout.Scale(86f) +
                gap +
                EsuHudLayout.Scale(42f) +
                compactGap +
                EsuHudLayout.Scale(52f) +
                compactGap +
                EsuHudLayout.Scale(42f) +
                gap +
                4f * EsuHudLayout.Scale(42f) +
                3f * compactGap;
            return available < firstRowWidth;
        }

        private void DrawAnchorMenu()
        {
            if (!_anchorMenuOpen)
                return;

            Rect rect = AnchorMenuRect();
            bool wrapsPresets = AnchorMenuNeedsPresetWrap(rect.width);
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(6f);
            float compactGap = EsuHudLayout.Scale(4f);
            float rowHeight = EsuHudLayout.Scale(26f);
            float x = padding;
            float y = padding;

            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(rect);
            if (GUI.Button(
                    new Rect(x, y, EsuHudLayout.Scale(86f), rowHeight),
                    new GUIContent(
                        _anchorFollowDecoration ? "Follow: on" : "Follow: off",
                        "Toggle automatic anchor retethering while moving a decoration."),
                    DecorationEditorTheme.ToolButton(_anchorFollowDecoration)))
            {
                ToggleAnchorFollow();
            }

            x += EsuHudLayout.Scale(86f) + gap;
            GUI.Label(
                new Rect(x, y + EsuHudLayout.Scale(2f), EsuHudLayout.Scale(42f), rowHeight),
                "Range",
                DecorationEditorTheme.Mini);
            x += EsuHudLayout.Scale(42f) + compactGap;
            Rect anchorDistanceRect = new Rect(x, y + EsuHudLayout.Scale(2f), EsuHudLayout.Scale(52f), EsuHudLayout.Scale(22f));
            _anchorFollowDistanceText = GUI.TextField(
                anchorDistanceRect,
                _anchorFollowDistanceText ?? string.Empty,
                DecorationEditorTheme.TextField);
            EsuCursorTooltip.Register(anchorDistanceRect, "Set the anchor follow distance in metres.");
            x += EsuHudLayout.Scale(52f) + compactGap;
            if (GUI.Button(
                    new Rect(x, y + EsuHudLayout.Scale(1f), EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)),
                    new GUIContent("Set", "Apply the anchor follow distance."),
                    DecorationEditorTheme.Button))
            {
                ApplyAnchorFollowDistanceText();
            }

            x += EsuHudLayout.Scale(42f) + gap;
            float presetY = y + EsuHudLayout.Scale(1f);
            if (wrapsPresets)
            {
                x = padding;
                presetY = y + rowHeight + EsuHudLayout.Scale(6f);
            }

            DrawAnchorFollowDistanceButton(new Rect(x, presetY, EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)), 1f, "1m");
            x += EsuHudLayout.Scale(42f) + compactGap;
            DrawAnchorFollowDistanceButton(new Rect(x, presetY, EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)), 2f, "2m");
            x += EsuHudLayout.Scale(42f) + compactGap;
            DrawAnchorFollowDistanceButton(new Rect(x, presetY, EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)), 3f, "3m");
            x += EsuHudLayout.Scale(42f) + compactGap;
            DrawAnchorFollowDistanceButton(new Rect(x, presetY, EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)), 5f, "5m");

            float helpY = wrapsPresets
                ? presetY + EsuHudLayout.Scale(32f)
                : y + rowHeight + EsuHudLayout.Scale(8f);
            GUI.Label(
                new Rect(
                    padding,
                    helpY,
                    rect.width - padding * 2f,
                    Mathf.Max(EsuHudLayout.Scale(34f), rect.height - helpY - padding)),
                "When enabled, moving a decoration retethers its anchor to the nearest valid block once the center is this far from the current anchor.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndArea();
        }

        private void ToggleAnchorFollow()
        {
            _anchorFollowDecoration = !_anchorFollowDecoration;
            s_anchorFollowDecoration = _anchorFollowDecoration;
            InfoStore.Add("Anchor follow " + (_anchorFollowDecoration ? "enabled." : "disabled."));
        }

        private void DrawAnchorFollowDistanceButton(Rect rect, float distance, string label)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(label, "Set the anchor follow distance to " + label + "."),
                    DecorationEditorTheme.ToolButton(Math.Abs(_anchorFollowDistance - distance) < 0.001f)))
            {
                SetAnchorFollowDistance(distance);
            }
        }

        private void ApplyAnchorFollowDistanceText()
        {
            string text = (_anchorFollowDistanceText ?? string.Empty).Trim();
            if (!FlexibleFloatParser.TryParse(text, out float value))
            {
                InfoStore.Add("Anchor follow range must be a finite number of metres.");
                _anchorFollowDistanceText = FormatFloat(_anchorFollowDistance);
                return;
            }

            SetAnchorFollowDistance(value);
        }

        private void SetAnchorFollowDistance(float value)
        {
            if (!DecorationEditMath.IsFinite(value))
            {
                InfoStore.Add("Anchor follow range must be finite.");
                return;
            }

            _anchorFollowDistance = Mathf.Clamp(
                value,
                AnchorFollowMinimumDistance,
                AnchorFollowMaximumDistance);
            _anchorFollowDistanceText = FormatFloat(_anchorFollowDistance);
            s_anchorFollowDistance = _anchorFollowDistance;
            InfoStore.Add("Anchor follow range: " + _anchorFollowDistanceText + "m.");
        }

        private void ViewModeButton(DecorationEditorViewMode mode, string label, Rect rect)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(label, "Switch decoration view to " + ViewModeDisplayName(mode) + "."),
                    DecorationEditorTheme.ToolButton(_viewMode == mode)))
            {
                SelectViewMode(mode);
            }
        }

        private void SelectViewMode(DecorationEditorViewMode mode)
        {
            _viewMode = mode;
            ApplyEditorViewMode();
            InfoStore.Add("Decoration Edit view: " + ViewModeDisplayName(_viewMode));
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

        private bool IconButton(string icon, string label, GUIStyle style, string tooltip) =>
            GUILayout.Button(
                new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                style,
                GUILayout.Width(EsuHudLayout.Scale(54f)),
                GUILayout.Height(EsuHudLayout.Scale(40f)));

        private bool AttentionIconButton(string icon, string label, GUIStyle style, string tooltip)
        {
            bool clicked = IconButton(icon, label, style, tooltip);
            EsuToolbarAttention.DrawLastButtonPulse(ApplyCancelAttentionActive);
            return clicked;
        }

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

        private bool ApplyCancelAttentionActive =>
            EsuToolbarAttention.IsActive(_applyCancelAttentionUntil);

        private void RefreshApplyCancelAttention() =>
            _applyCancelAttentionUntil = EsuToolbarAttention.RefreshUntil();

        private void ClearApplyCancelAttention() =>
            _applyCancelAttentionUntil = -1f;

        private void DrawLeftPanelStack(Rect stackRect)
        {
            if (IsSurfaceMode)
            {
                ClearStackDividerDrag(StackDividerKind.Left);
                DrawMeshPalette(stackRect);
                return;
            }

            bool showBuilderPanel = IsSurfaceMode || _tool == DecorationEditorTool.Paint || _showMeshPalette;
            if (!showBuilderPanel && !_showInspectorPanel)
                return;

            Rect meshRect;
            Rect dividerRect;
            Rect inspectorRect;
            SplitVerticalStack(
                stackRect,
                showBuilderPanel,
                _showInspectorPanel,
                _leftStackBottomRatio,
                StackDividerGap(),
                StackDividerMinimumPanelHeight(),
                out meshRect,
                out dividerRect,
                out inspectorRect,
                out _leftStackBottomRatio);
            if (dividerRect.height > 0f)
            {
                HandleStackDividerDrag(
                    StackDividerKind.Left,
                    stackRect,
                    dividerRect,
                    StackDividerGap(),
                    ref _leftStackBottomRatio);
                SplitVerticalStack(
                    stackRect,
                    showBuilderPanel,
                    _showInspectorPanel,
                    _leftStackBottomRatio,
                    StackDividerGap(),
                    StackDividerMinimumPanelHeight(),
                    out meshRect,
                    out dividerRect,
                    out inspectorRect,
                    out _leftStackBottomRatio);
            }
            else
            {
                ClearStackDividerDrag(StackDividerKind.Left);
            }

            if (showBuilderPanel)
                DrawMeshPalette(meshRect);
            if (_showInspectorPanel)
                DrawInspectorPanel(inspectorRect);
            if (dividerRect.height > 0f)
                DrawStackDividerGrip(dividerRect, StackDividerKind.Left);
        }

        private void DrawRightPanel(Rect rightRect)
        {
            if (IsSurfaceMode)
            {
                if (!_showSurfaceToolsPanel)
                    return;
                ClearStackDividerDrag(StackDividerKind.Right);
                DrawSurfaceExtraToolsPanel(rightRect);
                return;
            }

            if (!_showOutlinerPanel && !_showAnchorPanel)
                return;

            Rect outlinerRect;
            Rect dividerRect;
            Rect anchorRect;
            SplitVerticalStack(
                rightRect,
                _showOutlinerPanel,
                _showAnchorPanel,
                _rightStackBottomRatio,
                StackDividerGap(),
                StackDividerMinimumPanelHeight(),
                out outlinerRect,
                out dividerRect,
                out anchorRect,
                out _rightStackBottomRatio);
            if (dividerRect.height > 0f)
            {
                HandleStackDividerDrag(
                    StackDividerKind.Right,
                    rightRect,
                    dividerRect,
                    StackDividerGap(),
                    ref _rightStackBottomRatio);
                SplitVerticalStack(
                    rightRect,
                    _showOutlinerPanel,
                    _showAnchorPanel,
                    _rightStackBottomRatio,
                    StackDividerGap(),
                    StackDividerMinimumPanelHeight(),
                    out outlinerRect,
                    out dividerRect,
                    out anchorRect,
                    out _rightStackBottomRatio);
            }
            else
            {
                ClearStackDividerDrag(StackDividerKind.Right);
            }

            if (_showOutlinerPanel)
                DrawOutlinerPanel(outlinerRect);
            if (_showAnchorPanel)
                DrawAnchorPanel(anchorRect);
            if (dividerRect.height > 0f)
                DrawStackDividerGrip(dividerRect, StackDividerKind.Right);
        }

        private static void SplitVerticalStack(
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
            resolvedBottomRatio = ValidStackRatio(bottomRatio);
            if (showTop && showBottom)
            {
                float availableHeight = StackAvailableHeight(stackRect, gap);
                float bottomHeight = ClampStackBottomHeight(
                    availableHeight * resolvedBottomRatio,
                    availableHeight,
                    minimumPanelHeight);
                resolvedBottomRatio = availableHeight > 0f
                    ? bottomHeight / availableHeight
                    : resolvedBottomRatio;
                float topHeight = Mathf.Max(0f, availableHeight - bottomHeight);
                topRect = new Rect(stackRect.x, stackRect.y, stackRect.width, topHeight);
                dividerRect = new Rect(stackRect.x, topRect.yMax, stackRect.width, Mathf.Max(0f, gap));
                bottomRect = new Rect(stackRect.x, dividerRect.yMax, stackRect.width, bottomHeight);
                return;
            }

            if (showTop)
                topRect = stackRect;
            if (showBottom)
                bottomRect = stackRect;
        }

        private static float StackDividerGap() => EsuHudLayout.Scale(8f);

        private static float StackDividerMinimumPanelHeight() => EsuHudLayout.Scale(96f);

        private static float StackAvailableHeight(Rect stackRect, float gap) =>
            Mathf.Max(0f, stackRect.height - Mathf.Max(0f, gap));

        private static float ValidStackRatio(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio))
                return 0.5f;
            return Mathf.Clamp01(ratio);
        }

        private static float ClampStackBottomHeight(
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

        private static float ClampStackBottomRatioFromHeight(
            float bottomHeight,
            Rect stackRect,
            float gap,
            float minimumPanelHeight)
        {
            float availableHeight = StackAvailableHeight(stackRect, gap);
            if (availableHeight <= 0f)
                return 0.5f;

            return ClampStackBottomHeight(
                bottomHeight,
                availableHeight,
                minimumPanelHeight) / availableHeight;
        }

        private void HandleStackDividerDrag(
            StackDividerKind divider,
            Rect stackRect,
            Rect dividerRect,
            float gap,
            ref float bottomRatio)
        {
            Event current = Event.current;
            if (current == null || divider == StackDividerKind.None || dividerRect.height <= 0f)
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dividerRect.Contains(current.mousePosition))
            {
                _draggingStackDivider = divider;
                _stackDividerDragStartMouseY = current.mousePosition.y;
                _stackDividerDragStartBottomRatio = bottomRatio;
                current.Use();
                return;
            }

            if (_draggingStackDivider != divider)
                return;

            if (current.type == EventType.MouseDrag)
            {
                float availableHeight = StackAvailableHeight(stackRect, gap);
                float startBottomHeight = availableHeight * _stackDividerDragStartBottomRatio;
                float deltaY = current.mousePosition.y - _stackDividerDragStartMouseY;
                bottomRatio = ClampStackBottomRatioFromHeight(
                    startBottomHeight - deltaY,
                    stackRect,
                    gap,
                    StackDividerMinimumPanelHeight());
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
            {
                _draggingStackDivider = StackDividerKind.None;
                current.Use();
            }
        }

        private void ClearStackDividerDrag(StackDividerKind divider)
        {
            if (_draggingStackDivider == divider)
                _draggingStackDivider = StackDividerKind.None;
        }

        private void DrawStackDividerGrip(Rect dividerRect, StackDividerKind divider)
        {
            Event current = Event.current;
            bool active = _draggingStackDivider == divider;
            bool hovered = current != null && dividerRect.Contains(current.mousePosition);
            EsuHudLayout.DrawStackDividerGrip(dividerRect, active || hovered);
            EsuCursorTooltip.Register(dividerRect, "Drag to resize the panels in this stack.");
        }

        private void DrawOutlinerPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUI.BeginGroup(rect);
            Rect inner = new Rect(inset, inset, rect.width - inset * 2f, rect.height - inset * 2f);
            GUILayout.BeginArea(inner);
            DrawOutliner(inner.width, inner.height);
            GUILayout.EndArea();
            GUI.EndGroup();
        }

        private void DrawAnchorPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUI.BeginGroup(rect);
            Rect inner = new Rect(inset, inset, rect.width - inset * 2f, rect.height - inset * 2f);
            GUILayout.BeginArea(inner);
            DrawAnchorContext(inner.height);
            GUILayout.EndArea();
            GUI.EndGroup();
        }

        private void DrawAnchorContext(float height)
        {
            DrawCompactAnchorHeader();
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
            {
                GUILayout.Label("Select a decoration to show other decorations on the same anchor.", DecorationEditorTheme.Mini);
                return;
            }

            Vector3i tether = _selected.TetherPoint.Us;
            LabelRow("Anchor", FormatTether(tether));
            if (_tool == DecorationEditorTool.Anchor)
                DrawAnchorNudgePanel();
            GUILayout.Label("Linked decorations", DecorationEditorTheme.Mini);
            IEnumerable<Decoration> rows = SelectedAnchorDecorations(tether);

            _anchorListScroll = GUILayout.BeginScrollView(
                _anchorListScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(
                    0f,
                    height - EsuHudLayout.Scale(_tool == DecorationEditorTool.Anchor ? 84f : 52f))));
            foreach (Decoration decoration in rows)
            {
                GUIStyle style = ReferenceEquals(decoration, _selected)
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row;
                string meshName = CompactText(MeshName(decoration.MeshGuid.Us), 36);
                string detail = $"c{decoration.Color.Us} | {decoration.MeshGuid.Us.ToString("N").Substring(0, 8)}";
                if (GUILayout.Button(
                        new GUIContent(meshName + "  " + detail, DecorationEditorIconCatalog.Get("mesh"), "Select this decoration on the same anchor."),
                        style,
                        GUILayout.Height(EsuHudLayout.Scale(OutlinerRowHeight))))
                {
                    Select(decoration, _selectedConstruct);
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawCompactAnchorHeader()
        {
            Rect headerRow = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(24f), GUILayout.ExpandWidth(true));
            float gap = EsuHudLayout.Scale(6f);
            float hideWidth = EsuHudLayout.Scale(58f);
            Rect titleRect = new Rect(
                headerRow.x,
                headerRow.y,
                Mathf.Max(0f, headerRow.width - hideWidth - gap),
                headerRow.height);
            DrawCompactIconHeader(titleRect, "Selected anchor", "anchor");
            if (GUI.Button(
                    new Rect(titleRect.xMax + gap, headerRow.y, hideWidth, headerRow.height),
                    new GUIContent("Hide", "Hide the selected anchor panel."),
                    DecorationEditorTheme.Button))
            {
                _showAnchorPanel = false;
            }
        }

        private static void DrawCompactIconHeader(string text, string iconKey)
        {
            DrawCompactIconHeader(text, iconKey, GUILayout.ExpandWidth(true));
        }

        private static void DrawCompactIconHeader(
            string text,
            string iconKey,
            params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(22f), options);
            DrawCompactIconHeader(rect, text, iconKey);
        }

        private static void DrawCompactIconHeader(Rect rect, string text, string iconKey)
        {
            GUI.Label(rect, GUIContent.none, DecorationEditorTheme.SubHeader);

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
                GUI.Label(textRect, text, CompactHeaderTextStyle());
        }

        private static GUIStyle CompactHeaderTextStyle()
        {
            var style = new GUIStyle(DecorationEditorTheme.SubHeader)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            style.normal.background = null;
            return style;
        }

        private IEnumerable<Decoration> SelectedAnchorDecorations(Vector3i tether)
        {
            var decorations = _selectedConstruct?.Decorations as AllConstructDecorations;
            if (decorations == null)
                return Enumerable.Empty<Decoration>();

            return OrderedDecorations(
                decorations.DecorationList.Where(decoration =>
                    decoration != null &&
                    !decoration.IsDeleted &&
                    decoration.TetherPoint.Us.Equals(tether)));
        }

        private void DrawOutliner(float width, float height)
        {
            Rect headerRow = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(24f), GUILayout.ExpandWidth(true));
            float gap = EsuHudLayout.Scale(6f);
            float pinsWidth = EsuHudLayout.Scale(76f);
            float refreshWidth = EsuHudLayout.Scale(86f);
            float hideWidth = EsuHudLayout.Scale(58f);
            float actionWidth = pinsWidth + refreshWidth + hideWidth + gap * 3f;
            float titleWidth = Mathf.Max(0f, headerRow.width - actionWidth);
            Rect titleRect = new Rect(headerRow.x, headerRow.y, titleWidth, headerRow.height);
            DrawCompactIconHeader(titleRect, "Outliner", "outliner");
            float buttonX = titleRect.xMax + gap;
            if (GUI.Button(
                    new Rect(buttonX, headerRow.y, pinsWidth, headerRow.height),
                    new GUIContent(
                        _showTetherPins ? "Pins on" : "Pins off",
                        "Show or hide anchor pins in the viewport."),
                    DecorationEditorTheme.ToolButton(_showTetherPins)))
            {
                _showTetherPins = !_showTetherPins;
                RefreshDecorationCache(force: true);
            }
            buttonX += pinsWidth + gap;
            if (GUI.Button(
                    new Rect(buttonX, headerRow.y, refreshWidth, headerRow.height),
                    new GUIContent("Refresh", DecorationEditorIconCatalog.Get("create"), "Refresh the decoration outliner."),
                    DecorationEditorTheme.Button))
                RefreshDecorationCache(force: true);
            buttonX += refreshWidth + gap;
            if (GUI.Button(
                    new Rect(buttonX, headerRow.y, hideWidth, headerRow.height),
                    new GUIContent("Hide", "Hide the outliner panel."),
                    DecorationEditorTheme.Button))
            {
                _showOutlinerPanel = false;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            _outlinerFilter = GUILayout.TextField(_outlinerFilter ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Filter decorations by mesh name, construct, anchor, color, or GUID.");
            GUILayout.EndHorizontal();

            List<OutlinerRow> rows = FilterOutlinerRows();
            GUILayout.Label(
                $"{rows.Count:N0} rows | {_hits.Count:N0} visible/projected decorations",
                DecorationEditorTheme.Mini);

            float rowHeight = EsuHudLayout.Scale(OutlinerRowHeight);
            float listHeight = Mathf.Max(0f, height - EsuHudLayout.Scale(76f));
            _outlinerScroll = GUILayout.BeginScrollView(
                _outlinerScroll,
                GUILayout.Width(width),
                GUILayout.Height(listHeight));
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_outlinerScroll.y / rowHeight) - 4);
            int last = Mathf.Min(total, first + MaxOutlinerDrawRows);
            if (first > 0)
                GUILayout.Space(first * rowHeight);
            for (int i = first; i < last; i++)
                DrawOutlinerRow(rows, i);
            if (last < total)
                GUILayout.Space((total - last) * rowHeight);
            GUILayout.EndScrollView();
        }

        private List<OutlinerRow> FilterOutlinerRows()
        {
            string filter = (_outlinerFilter ?? string.Empty).Trim().ToLowerInvariant();
            if (filter.Length == 0)
                return _outlinerRows;
            return _outlinerRows
                .Where(row => row.SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              row.Kind != DecorationOutlinerRowKind.Decoration)
                .ToList();
        }

        private void DrawOutlinerRow(List<OutlinerRow> rows, int rowIndex)
        {
            OutlinerRow row = rows[rowIndex];
            float rowHeight = EsuHudLayout.Scale(OutlinerRowHeight);
            Rect rowRect = GUILayoutUtility.GetRect(1f, rowHeight, GUILayout.ExpandWidth(true));
            GUIStyle style = row.Decoration != null && _selection.Contains(row.Decoration)
                ? DecorationEditorTheme.RowSelected
                : DecorationEditorTheme.Row;
            string icon = row.Kind == DecorationOutlinerRowKind.Construct
                ? "build"
                : row.Kind == DecorationOutlinerRowKind.Tether
                    ? "anchor"
                    : "mesh";
            string dirty = row.Decoration != null && ReferenceEquals(row.Decoration, _selected) && _dirty
                ? " !"
                : string.Empty;
            if (GUI.Button(rowRect, GUIContent.none, style))
            {
                if (row.Kind == DecorationOutlinerRowKind.Construct)
                {
                    ToggleConstructCollapse(row.CollapseKey);
                    RefreshDecorationCache(force: true);
                }
                else if (row.Kind == DecorationOutlinerRowKind.Decoration && row.Decoration != null)
                {
                    SelectOutlinerDecorationRow(rows, rowIndex, row);
                }
            }
            EsuCursorTooltip.Register(
                rowRect,
                row.Kind == DecorationOutlinerRowKind.Decoration
                    ? "Select this decoration."
                    : "Expand or collapse this outliner group.");

            float iconSize = EsuHudLayout.Scale(18f);
            float gap = EsuHudLayout.Scale(4f);
            float indent = Mathf.Min(
                row.Depth * EsuHudLayout.Scale(14f),
                Mathf.Max(0f, rowRect.width - iconSize - gap));
            Rect iconRect = new Rect(
                rowRect.x + indent + gap,
                rowRect.y + Mathf.Max(0f, (rowRect.height - iconSize) * 0.5f),
                iconSize,
                iconSize);
            Texture texture = DecorationEditorIconCatalog.Get(icon);
            if (texture != null)
                GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, alphaBlend: true);

            float textX = iconRect.xMax + gap;
            float textWidth = Mathf.Max(0f, rowRect.xMax - textX - gap);
            float detailWidth = string.IsNullOrEmpty(row.Detail)
                ? 0f
                : Mathf.Min(EsuHudLayout.Scale(142f), textWidth * 0.45f);
            Rect detailRect = new Rect(rowRect.xMax - detailWidth - gap, rowRect.y, detailWidth, rowRect.height);
            Rect labelRect = new Rect(
                textX,
                rowRect.y,
                Mathf.Max(0f, detailRect.x - textX - gap),
                rowRect.height);
            GUIStyle textStyle = OutlinerRowTextStyle(style);
            if (labelRect.width > 1f)
                GUI.Label(labelRect, row.Label + dirty, textStyle);
            if (detailRect.width > 1f)
                GUI.Label(detailRect, row.Detail ?? string.Empty, textStyle);
        }

        private void SelectOutlinerDecorationRow(List<OutlinerRow> rows, int rowIndex, OutlinerRow row)
        {
            if (row == null || row.Decoration == null || row.Decoration.IsDeleted || row.Construct == null)
                return;

            bool control = IsControlHeld();
            bool shift = IsShiftHeld();
            if (shift &&
                _outlinerRangeAnchor != null &&
                TryFindOutlinerDecorationRow(rows, _outlinerRangeAnchor, out int anchorIndex) &&
                ReferenceEquals(rows[anchorIndex].Construct, row.Construct))
            {
                SelectOutlinerDecorationRange(rows, anchorIndex, rowIndex, row, additive: control);
                _outlinerRangeAnchor = row.Decoration;
                return;
            }

            if (control)
            {
                ToggleOutlinerDecorationSelection(row);
                _outlinerRangeAnchor = row.Decoration;
                return;
            }

            Select(row.Decoration, row.Construct);
            _outlinerRangeAnchor = row.Decoration;
        }

        private bool TryFindOutlinerDecorationRow(
            List<OutlinerRow> rows,
            Decoration decoration,
            out int rowIndex)
        {
            rowIndex = -1;
            if (rows == null || decoration == null || decoration.IsDeleted)
                return false;

            for (int index = 0; index < rows.Count; index++)
            {
                OutlinerRow row = rows[index];
                if (row.Kind == DecorationOutlinerRowKind.Decoration &&
                    ReferenceEquals(row.Decoration, decoration))
                {
                    rowIndex = index;
                    return true;
                }
            }

            return false;
        }

        private void SelectOutlinerDecorationRange(
            List<OutlinerRow> rows,
            int anchorIndex,
            int clickedIndex,
            OutlinerRow clicked,
            bool additive)
        {
            if (rows == null || clicked?.Decoration == null || clicked.Construct == null)
                return;

            if (!additive ||
                _selectedConstruct == null ||
                !ReferenceEquals(_selectedConstruct, clicked.Construct))
            {
                _selection.Clear();
            }

            int start = Mathf.Min(anchorIndex, clickedIndex);
            int end = Mathf.Max(anchorIndex, clickedIndex);
            for (int index = start; index <= end; index++)
            {
                OutlinerRow row = rows[index];
                if (row.Kind != DecorationOutlinerRowKind.Decoration ||
                    row.Decoration == null ||
                    row.Decoration.IsDeleted ||
                    !ReferenceEquals(row.Construct, clicked.Construct))
                {
                    continue;
                }

                _selection.Add(row.Decoration);
            }

            if (_selection.Count == 0)
                _selection.Add(clicked.Decoration);

            SetPrimarySelection(clicked.Decoration, clicked.Construct);
            if (_tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
                _tool = DecorationEditorTool.Move;
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
        }

        private void ToggleOutlinerDecorationSelection(OutlinerRow row)
        {
            if (row?.Decoration == null || row.Decoration.IsDeleted || row.Construct == null)
                return;

            if (_selectedConstruct == null ||
                !ReferenceEquals(_selectedConstruct, row.Construct))
            {
                Select(row.Decoration, row.Construct);
                return;
            }

            if (_selection.Contains(row.Decoration))
            {
                _selection.Remove(row.Decoration);
                if (ReferenceEquals(_selected, row.Decoration))
                    PromoteOutlinerSelection(row.Construct);
            }
            else
            {
                _selection.Add(row.Decoration);
                SetPrimarySelection(row.Decoration, row.Construct);
            }

            if (_selection.Count > 0 &&
                _tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
                _tool = DecorationEditorTool.Move;
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
        }

        private void PromoteOutlinerSelection(AllConstruct construct)
        {
            Decoration next = _selection.FirstOrDefault(decoration => decoration != null && !decoration.IsDeleted);
            if (next != null)
            {
                SetPrimarySelection(next, construct);
                return;
            }

            _selected = null;
            _selectedConstruct = null;
            _snapshot = null;
            _selectedCreatedInSession = false;
            ClearMultiTransformState();
        }

        private static GUIStyle OutlinerRowTextStyle(GUIStyle source)
        {
            var style = new GUIStyle(source)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0)
            };
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            return style;
        }

        private void DrawInspector(float height)
        {
            _inspectorScroll = GUILayout.BeginScrollView(
                _inspectorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(0f, height)));
            if (_selected == null || _selected.IsDeleted)
            {
                GUILayout.Label("No decoration selected.", DecorationEditorTheme.Body);
                GUILayout.Label("Use Select in the viewport or choose a decoration row in the outliner.", DecorationEditorTheme.Mini);
                GUILayout.EndScrollView();
                return;
            }

            GUILayout.Label(MeshName(_selected.MeshGuid.Us), DecorationEditorTheme.BodyWrap);
            DrawColorEditor();
            DrawMaterialEditor();
            DecorationEditorTheme.Separator();
            LabelRow("Owner", ConstructLabel(_selectedConstruct, -1));
            LabelRow("Mesh", MeshName(_selected.MeshGuid.Us));
            LabelRow("Mesh GUID", _selected.MeshGuid.Us.ToString("D"));
            if (_showTetherPins || _tool == DecorationEditorTool.Anchor)
                LabelRow("Tether", FormatTether(_selected.TetherPoint.Us));
            LabelRow("Material", MaterialDisplayName(_selected.MaterialReplacement.Us));
            GUILayout.EndScrollView();
        }

        private void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.BodyWrap);
            GUILayout.EndHorizontal();
        }

        private void DrawVectorEditor(string label, string[] values, Action<Vector3> apply)
        {
            GUILayout.Label(label, DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(12f)));
            values[0] = GUILayout.TextField(values[0] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(72f)));
            EsuCursorTooltip.RegisterLast("Edit the " + label.ToLowerInvariant() + " X value.");
            GUILayout.Label("Y", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(12f)));
            values[1] = GUILayout.TextField(values[1] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(72f)));
            EsuCursorTooltip.RegisterLast("Edit the " + label.ToLowerInvariant() + " Y value.");
            GUILayout.Label("Z", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(12f)));
            values[2] = GUILayout.TextField(values[2] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(72f)));
            EsuCursorTooltip.RegisterLast("Edit the " + label.ToLowerInvariant() + " Z value.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed " + label.ToLowerInvariant() + " values."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(42f))))
            {
                if (TryParseVector(values, out Vector3 parsed))
                    apply(parsed);
                else
                    InfoStore.Add($"{label} contains incomplete, invalid, NaN, or infinity input.");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawColorEditor()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Paint color", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(_showInspectorColorPicker ? "Hide list" : "Show list", "Show or hide the selected decoration paint color picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _showInspectorColorPicker = !_showInspectorColorPicker;
            }
            GUILayout.EndHorizontal();

            if (_showInspectorColorPicker)
            {
                for (int row = 0; row < 4; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int column = 0; column < 8; column++)
                    {
                        int color = row * 8 + column;
                        if (DrawPaintColorButton(
                                color,
                                _selected.Color.Us == color,
                                "Set the selected decoration paint color to #" + color.ToString(CultureInfo.InvariantCulture) + ".",
                                EsuHudLayout.Scale(36f),
                                EsuHudLayout.Scale(24f)))
                        {
                            SetSelectedColor(color);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _colorText = GUILayout.TextField(_colorText ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Type a paint color from 0 to 31.");
            if (GUILayout.Button(
                    new GUIContent("0-31", "Apply the typed paint color."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(52f))))
            {
                if (int.TryParse((_colorText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int color))
                    SetSelectedColor(Mathf.Clamp(color, 0, 31));
                else
                    InfoStore.Add("Color must be an integer from 0 to 31.");
            }
            GUILayout.EndHorizontal();
        }

        private void SetSelectedColor(int color)
        {
            if (_selected == null || _selected.IsDeleted)
                return;

            color = Mathf.Clamp(color, 0, 31);
            if (_selected.Color.Us == color)
            {
                _colorText = color.ToString(CultureInfo.InvariantCulture);
                _paintColor = color;
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.Color.Us = color;
            _colorText = _selected.Color.Us.ToString(CultureInfo.InvariantCulture);
            _paintColor = _selected.Color.Us;
            MarkSelectedDirty();
            RecordSnapshotEdit("Set color", before);
        }

        private void DrawMaterialEditor()
        {
            GUILayout.Label("Material override", DecorationEditorTheme.SubHeader);
            Guid materialOverride = _selected.MaterialReplacement.Us;
            bool hasMaterialOverride = materialOverride != Guid.Empty;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                    MaterialOverrideButtonLabel(materialOverride),
                    hasMaterialOverride
                        ? DecorationEditorTheme.ActiveButton
                        : DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (hasMaterialOverride &&
                GUILayout.Button(
                    new GUIContent("Clear", "Remove the material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetSelectedMaterial(Guid.Empty);
            }

            if (GUILayout.Button(
                    new GUIContent(_showMaterialPicker ? "Hide list" : "Show list", "Show or hide the material override picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _showMaterialPicker = !_showMaterialPicker;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Most reliable: alloy, metal, rubber, stone, heavy armour, and emissive-only. Other materials can swap texture layouts.",
                DecorationEditorTheme.MiniWrap);

            if (_showMaterialPicker)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                    GUILayout.Width(EsuHudLayout.Scale(24f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f)));
                _materialFilter = GUILayout.TextField(_materialFilter ?? string.Empty, DecorationEditorTheme.TextField);
                EsuCursorTooltip.RegisterLast("Filter material overrides by name or GUID.");
                GUILayout.EndHorizontal();

                List<MaterialCatalogEntry> materials = FilterMaterials().ToList();
                GUILayout.Label(MaterialCountText(materials.Count), DecorationEditorTheme.Mini);
                float height = Mathf.Min(
                    EsuHudLayout.Scale(180f),
                    Mathf.Max(EsuHudLayout.Scale(70f), materials.Count * EsuHudLayout.Scale(22f) + EsuHudLayout.Scale(8f)));
                _materialScroll = GUILayout.BeginScrollView(
                    _materialScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Height(height));
                foreach (MaterialCatalogEntry material in materials)
                {
                    bool active = _selected.MaterialReplacement.Us == material.Guid;
                    if (GUILayout.Button(
                            new GUIContent(material.DisplayName, "Apply this material override."),
                            active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                            GUILayout.Height(EsuHudLayout.Scale(22f))))
                    {
                        SetSelectedMaterial(material.Guid);
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("GUID", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _materialText = GUILayout.TextField(_materialText ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Type a material GUID, or leave blank for default.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(42f))))
            {
                string text = (_materialText ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(text) || text.Equals("default", StringComparison.OrdinalIgnoreCase))
                    SetSelectedMaterial(Guid.Empty);
                else if (Guid.TryParse(text, out Guid material))
                    SetSelectedMaterial(material);
                else
                    InfoStore.Add("Material must be empty/default or a GUID.");
            }
            GUILayout.EndHorizontal();
        }

        private string MaterialOverrideButtonLabel(Guid materialOverride)
        {
            return materialOverride == Guid.Empty
                ? "No material override"
                : "Material override: " + CompactText(MaterialDisplayName(materialOverride), 46);
        }

        private string MaterialCountText(int matchingCount)
        {
            int total = _materialCatalog?.Count ?? 0;
            string text = $"{matchingCount:N0} shown | {total:N0} total materials";
            string filter = (_materialFilter ?? string.Empty).Trim();
            if (filter.Length > 0)
                text += " | search: " + CompactText(filter, 32);
            return text;
        }

        private IEnumerable<MaterialCatalogEntry> FilterMaterials()
        {
            IEnumerable<MaterialCatalogEntry> materials = _materialCatalog ?? Enumerable.Empty<MaterialCatalogEntry>();
            string filter = (_materialFilter ?? string.Empty).Trim().ToLowerInvariant();
            if (filter.Length == 0)
                return materials;

            return materials
                .Where(material => material.SearchText.Contains(filter));
        }

        private void SetSelectedMaterial(Guid material)
        {
            if (_selected == null || _selected.IsDeleted)
                return;

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.MaterialReplacement.Us = material;
            _materialText = material == Guid.Empty ? string.Empty : material.ToString("D");
            MarkSelectedDirty();
            RecordSnapshotEdit("Set material", before);
        }

        private void DrawAnchorNudgePanel()
        {
            GUILayout.Label("Anchor retether: whole block nudge, visual mesh remains in place.", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            StyledAnchorButton("-X", new Vector3i(-1, 0, 0));
            StyledAnchorButton("+X", new Vector3i(1, 0, 0));
            StyledAnchorButton("-Y", new Vector3i(0, -1, 0));
            StyledAnchorButton("+Y", new Vector3i(0, 1, 0));
            StyledAnchorButton("-Z", new Vector3i(0, 0, -1));
            StyledAnchorButton("+Z", new Vector3i(0, 0, 1));
            GUILayout.EndHorizontal();
        }

        private void StyledAnchorButton(string label, Vector3i shift)
        {
            if (GUILayout.Button(
                    new GUIContent(label, "Move the selected decoration to this neighboring anchor block while keeping its visual position."),
                    DecorationEditorTheme.Button))
                ShiftAnchor(shift);
        }

        private void DrawMeshPalette(Rect rect)
        {
            if (_tool == DecorationEditorTool.Surface)
            {
                DrawSurfacePanel(rect);
                return;
            }

            if (_tool == DecorationEditorTool.Paint)
            {
                DrawPaintPalette(rect);
                return;
            }

            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUI.BeginGroup(rect);

            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            float headerHeight = Mathf.Min(EsuHudLayout.Scale(116f), inner.height);
            float countHeight = Mathf.Min(EsuHudLayout.Scale(18f), Mathf.Max(0f, inner.height - headerHeight));
            float placingHeight = _placingMesh == null ? 0f : EsuHudLayout.Scale(24f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect countRect = new Rect(inner.x, headerRect.yMax, inner.width, countHeight);
            float listBottom = inner.yMax - (placingHeight <= 0f ? 0f : placingHeight + EsuHudLayout.Scale(4f));
            float listHeight = Mathf.Max(0f, listBottom - countRect.yMax);
            Rect listRect = new Rect(inner.x, countRect.yMax, inner.width, listHeight);
            Rect placingRect = new Rect(inner.x, listRect.yMax + EsuHudLayout.Scale(4f), inner.width, placingHeight);
            bool mouseInListViewport = listRect.Contains(Event.current.mousePosition);

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginVertical();
            DrawCompactIconHeader("Mesh Palette", "mesh");
            GUILayout.Label("Drag this panel. Click a mesh to place it; Set swaps the selected decoration.", DecorationEditorTheme.Mini);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("List", "Show the mesh palette as a virtualized text list."),
                    DecorationEditorTheme.ToolButton(!_meshPreviewGrid),
                    GUILayout.Width(EsuHudLayout.Scale(62f))))
                _meshPreviewGrid = false;
            if (GUILayout.Button(
                    new GUIContent("3D grid", "Show the mesh palette as a virtualized 3D thumbnail grid."),
                    DecorationEditorTheme.ToolButton(_meshPreviewGrid),
                    GUILayout.Width(EsuHudLayout.Scale(76f))))
                _meshPreviewGrid = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Hide", "Hide the mesh palette panel."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
                _showMeshPalette = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("All", "Show all meshes."),
                    DecorationEditorTheme.ToolButton(_meshKindFilter == "all"),
                    GUILayout.Width(EsuHudLayout.Scale(48f))))
                _meshKindFilter = "all";
            if (GUILayout.Button(
                    new GUIContent("Items", "Show item meshes."),
                    DecorationEditorTheme.ToolButton(_meshKindFilter == "item"),
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
                _meshKindFilter = "item";
            if (GUILayout.Button(
                    new GUIContent("Objects", "Show object meshes."),
                    DecorationEditorTheme.ToolButton(_meshKindFilter == "object"),
                    GUILayout.Width(EsuHudLayout.Scale(68f))))
                _meshKindFilter = "object";
            if (GUILayout.Button(
                    new GUIContent("Recent", "Show recently used meshes."),
                    DecorationEditorTheme.ToolButton(_meshKindFilter == "recent"),
                    GUILayout.Width(EsuHudLayout.Scale(68f))))
                _meshKindFilter = "recent";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            _meshFilter = GUILayout.TextField(_meshFilter ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Filter meshes by name or GUID.");
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            List<DecorationMeshCatalogEntry> rows = FilterMeshCatalog().ToList();
            GUI.Label(countRect, MeshCountText(rows.Count), DecorationEditorTheme.Mini);

            if (listRect.height > 1f)
            {
                GUI.BeginGroup(listRect);
                _meshScroll = GUILayout.BeginScrollView(
                    _meshScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(listRect.width),
                    GUILayout.Height(listRect.height));
                if (_meshPreviewGrid)
                    DrawMeshPreviewGrid(rows, listRect.width, listRect.height, mouseInListViewport);
                else
                    DrawMeshListRows(rows, listRect.height, mouseInListViewport);
                GUILayout.EndScrollView();
                GUI.EndGroup();
            }

            if (_placingMesh != null && placingRect.height > 1f && placingRect.y < inner.yMax)
            {
                GUILayout.BeginArea(placingRect);
                GUILayout.Label($"Placing: {_placingMesh.Name} | click a valid block, right-click/Esc cancels.", DecorationEditorTheme.Warning);
                GUILayout.EndArea();
            }
            GUI.EndGroup();
        }

        private void DrawPaintPalette(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUI.BeginGroup(rect);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            float headerHeight = Mathf.Min(EsuHudLayout.Scale(106f), inner.height);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect listRect = new Rect(
                inner.x,
                headerRect.yMax + EsuHudLayout.Scale(4f),
                inner.width,
                Mathf.Max(0f, inner.yMax - headerRect.yMax - EsuHudLayout.Scale(4f)));

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginVertical();
            DrawCompactIconHeader("Paint Palette", "paint");
            GUILayout.Label("Pick a color, then click decoration centers or blocks in the viewport to paint them.", DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            DrawPaintSwatch(GUILayoutUtility.GetRect(EsuHudLayout.Scale(34f), EsuHudLayout.Scale(24f)), _paintColor);
            GUILayout.Label(
                "Brush: #" + _paintColor.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Body,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            GUILayout.FlexibleSpace();
            if (_selected != null && !_selected.IsDeleted)
            {
                GUILayout.Label(
                    "Selected: #" + _selected.Color.Us.ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.Mini,
                    GUILayout.Height(EsuHudLayout.Scale(24f)));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (listRect.height > 1f)
            {
                GUI.BeginGroup(listRect);
                _paintScroll = GUILayout.BeginScrollView(
                    _paintScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(listRect.width),
                    GUILayout.Height(listRect.height));
                for (int color = 0; color <= 31; color++)
                    DrawPaintColorRow(color);
                GUILayout.EndScrollView();
                GUI.EndGroup();
            }
            GUI.EndGroup();
        }

        private void DrawPaintColorRow(int color)
        {
            bool brush = color == _paintColor;
            bool selected = _selected != null && !_selected.IsDeleted && _selected.Color.Us == color;
            GUIStyle style = brush || selected
                ? DecorationEditorTheme.RowSelected
                : DecorationEditorTheme.Row;
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(30f),
                GUILayout.ExpandWidth(true));
            if (GUI.Button(row, GUIContent.none, style))
            {
                _paintColor = color;
                _colorText = color.ToString(CultureInfo.InvariantCulture);
                if (_selected != null && !_selected.IsDeleted)
                    SetSelectedColor(color);
                else
                    InfoStore.Add("Paint brush set to color #" + color.ToString(CultureInfo.InvariantCulture) + ".");
            }
            EsuCursorTooltip.Register(row, "Set the paint brush to color #" + color.ToString(CultureInfo.InvariantCulture) + ".");

            Rect swatch = new Rect(
                row.x + EsuHudLayout.Scale(7f),
                row.y + EsuHudLayout.Scale(5f),
                EsuHudLayout.Scale(34f),
                row.height - EsuHudLayout.Scale(10f));
            DrawPaintSwatch(swatch, color);

            Rect numberRect = new Rect(
                swatch.xMax + EsuHudLayout.Scale(10f),
                row.y,
                EsuHudLayout.Scale(64f),
                row.height);
            GUI.Label(numberRect, "#" + color.ToString("00", CultureInfo.InvariantCulture), DecorationEditorTheme.Body);

            Rect labelRect = new Rect(
                numberRect.xMax + EsuHudLayout.Scale(8f),
                row.y,
                Mathf.Max(EsuHudLayout.Scale(60f), row.xMax - numberRect.xMax - EsuHudLayout.Scale(12f)),
                row.height);
            string state = brush
                ? "brush"
                : selected
                    ? "selected"
                    : "color " + color.ToString(CultureInfo.InvariantCulture);
            GUI.Label(labelRect, state, brush || selected ? DecorationEditorTheme.Body : DecorationEditorTheme.Mini);
        }

        private static bool DrawPaintColorButton(
            int color,
            bool active,
            string tooltip,
            float width,
            float height)
        {
            Rect rect = GUILayoutUtility.GetRect(
                width,
                height,
                GUILayout.Width(width),
                GUILayout.Height(height));
            GUIStyle style = active
                ? DecorationEditorTheme.ActiveButton
                : DecorationEditorTheme.Button;
            bool clicked = GUI.Button(rect, GUIContent.none, style);
            Rect swatchRect = new Rect(
                rect.x + EsuHudLayout.Scale(3f),
                rect.y + EsuHudLayout.Scale(3f),
                Mathf.Max(1f, rect.width - EsuHudLayout.Scale(6f)),
                Mathf.Max(1f, rect.height - EsuHudLayout.Scale(6f)));
            DrawPaintSwatch(swatchRect, color);
            DrawPaintColorNumber(rect, color, active);
            EsuCursorTooltip.Register(rect, tooltip);
            return clicked;
        }

        private static void DrawPaintSwatchLayout(
            int color,
            string tooltip,
            float width,
            float height)
        {
            Rect rect = GUILayoutUtility.GetRect(
                width,
                height,
                GUILayout.Width(width),
                GUILayout.Height(height));
            DrawPaintSwatch(rect, color);
            EsuCursorTooltip.Register(rect, tooltip);
        }

        private static void DrawPaintColorNumber(Rect rect, int color, bool active)
        {
            Color preview = PaintPreviewColor(color);
            float luminance = preview.r * 0.299f + preview.g * 0.587f + preview.b * 0.114f;
            Color textColor = luminance > 0.58f
                ? new Color(0.02f, 0.025f, 0.025f, 1f)
                : Color.white;
            Color oldContent = GUI.contentColor;
            try
            {
                GUIStyle labelStyle = new GUIStyle(DecorationEditorTheme.Body)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    clipping = TextClipping.Clip
                };
                GUI.contentColor = textColor;
                GUI.Label(rect, color.ToString(CultureInfo.InvariantCulture), labelStyle);
            }
            finally
            {
                GUI.contentColor = oldContent;
            }
        }

        private static void DrawPaintSwatch(Rect rect, int color)
        {
            Color oldColor = GUI.color;
            try
            {
                GUI.color = PaintPreviewColor(color);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static Color PaintPreviewColor(int color)
        {
            switch (Mathf.Clamp(color, 0, 31))
            {
                case 0: return new Color(0.72f, 0.76f, 0.74f, 1f);
                case 1: return new Color(0.18f, 0.2f, 0.21f, 1f);
                case 2: return new Color(0.74f, 0.18f, 0.14f, 1f);
                case 3: return new Color(0.12f, 0.45f, 0.22f, 1f);
                case 4: return new Color(0.12f, 0.26f, 0.68f, 1f);
                case 5: return new Color(0.86f, 0.74f, 0.18f, 1f);
                case 6: return new Color(0.07f, 0.72f, 0.78f, 1f);
                case 7: return new Color(0.66f, 0.2f, 0.78f, 1f);
                case 8: return new Color(0.92f, 0.48f, 0.14f, 1f);
                case 9: return new Color(0.86f, 0.86f, 0.82f, 1f);
                case 10: return new Color(0.45f, 0.48f, 0.48f, 1f);
                case 11: return new Color(0.1f, 0.1f, 0.1f, 1f);
                case 12: return new Color(0.98f, 0.94f, 0.76f, 1f);
                case 13: return new Color(0.48f, 0.32f, 0.2f, 1f);
                case 14: return new Color(0.28f, 0.44f, 0.46f, 1f);
                case 15: return new Color(0.88f, 0.36f, 0.5f, 1f);
                case 16: return new Color(0.38f, 0.62f, 0.94f, 1f);
                case 17: return new Color(0.26f, 0.74f, 0.32f, 1f);
                case 18: return new Color(0.58f, 0.08f, 0.08f, 1f);
                case 19: return new Color(0.08f, 0.24f, 0.1f, 1f);
                case 20: return new Color(0.06f, 0.12f, 0.36f, 1f);
                case 21: return new Color(0.62f, 0.52f, 0.08f, 1f);
                case 22: return new Color(0.04f, 0.42f, 0.48f, 1f);
                case 23: return new Color(0.36f, 0.1f, 0.48f, 1f);
                case 24: return new Color(0.44f, 0.56f, 0.58f, 1f);
                case 25: return new Color(0.66f, 0.68f, 0.62f, 1f);
                case 26: return new Color(0.22f, 0.3f, 0.34f, 1f);
                case 27: return new Color(0.12f, 0.16f, 0.14f, 1f);
                case 28: return new Color(0.94f, 0.68f, 0.46f, 1f);
                case 29: return new Color(0.34f, 0.22f, 0.12f, 1f);
                case 30: return new Color(0.9f, 0.9f, 0.9f, 1f);
                default: return new Color(0.04f, 0.04f, 0.05f, 1f);
            }
        }

        private string MeshCountText(int matchingCount)
        {
            string scope =
                _meshKindFilter == "item" ? "items" :
                _meshKindFilter == "object" ? "objects" :
                _meshKindFilter == "recent" ? "recent" :
                "all";
            string filter = (_meshFilter ?? string.Empty).Trim();
            string text = $"{matchingCount:N0} shown | {_meshCatalog.Count:N0} total meshes | {scope}";
            if (filter.Length > 0)
                text += " | search: " + CompactText(filter, 32);
            return text;
        }

        private void DrawSurfacePanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUI.BeginGroup(rect);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            float actionHeight = Mathf.Min(EsuHudLayout.Scale(42f), Mathf.Max(0f, inner.height * 0.18f));
            float gap = EsuHudLayout.Scale(6f);
            float settingsHeight = SurfaceSettingsShelfHeight(inner.height, actionHeight, gap);
            Rect actionRect = new Rect(inner.x, inner.yMax - actionHeight, inner.width, actionHeight);
            Rect settingsRect = new Rect(
                inner.x,
                actionRect.y - gap - settingsHeight,
                inner.width,
                settingsHeight);
            Rect contentRect = new Rect(
                inner.x,
                inner.y,
                inner.width,
                Mathf.Max(1f, settingsRect.y - inner.y - gap));

            GUILayout.BeginArea(contentRect);
            DrawCompactIconHeader("Surface Builder", "build");
            GUILayout.Label("Click three craft-surface points to seed a triangle. Select an edge, then click a new point to extend.", DecorationEditorTheme.MiniWrap);

            DecorationEditorTheme.Separator();
            GUILayout.Label(SurfaceSummary(), DecorationEditorTheme.BodyWrap);
            if (!string.IsNullOrEmpty(_surfaceMessage))
                GUILayout.Label(_surfaceMessage, SurfaceMessageStyle());
            if (_surfacePlan != null && _surfacePlan.Warnings.Count > 0)
                GUILayout.Label(_surfacePlan.Warnings[0], DecorationEditorTheme.Warning);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("Draw", "Place and edit triangle surface points."),
                    DecorationEditorTheme.ToolButton(_surfaceBuilderTool == SurfaceBuilderTool.Draw),
                    GUILayout.Width(EsuHudLayout.Scale(64f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
            }
            GUILayout.Label("Draft", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(_showSurfaceDraftList ? "Hide list" : "Show list", "Show or hide surface draft points and faces."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _showSurfaceDraftList = !_showSurfaceDraftList;
            }
            GUILayout.EndHorizontal();

            if (_showSurfaceDraftList)
            {
                _surfaceScroll = GUILayout.BeginScrollView(
                    _surfaceScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Height(SurfaceDraftListHeight(contentRect.height)));
                DrawUnifiedSurfaceDraftRows();
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N0} surface point(s), {1:N0} face(s), {2:N0} generator point(s)",
                        _surfaceDraft.Points.Count,
                        _surfaceDraft.Faces.Count,
                        _generatorDraft.PointCount),
                    DecorationEditorTheme.MiniWrap);
            }
            GUILayout.Label("Shift-click points, then right-click to connect two points or create a face from three.", DecorationEditorTheme.MiniWrap);
            GUILayout.Label("Shift-click two edges, then Bridge, to connect them.", DecorationEditorTheme.MiniWrap);
            GUILayout.EndArea();

            GUILayout.BeginArea(settingsRect);
            DecorationEditorTheme.Separator();
            DrawSurfaceSettings();
            GUILayout.EndArea();

            DrawSurfaceActionBar(actionRect);
            GUI.EndGroup();
        }

        private static float SurfaceSettingsShelfHeight(
            float innerHeight,
            float actionHeight,
            float gap)
        {
            float desired = EsuHudLayout.Scale(108f);
            float minimumContent = EsuHudLayout.Scale(128f);
            float available = Mathf.Max(1f, innerHeight - actionHeight - gap * 2f);
            if (available <= minimumContent)
                return Mathf.Max(1f, available * 0.45f);

            return Mathf.Min(desired, Mathf.Max(EsuHudLayout.Scale(78f), available - minimumContent));
        }

        private float SurfaceDraftListHeight(float contentHeight)
        {
            int rowCount = UnifiedSurfaceDraftRowCount();
            float rowHeight = EsuHudLayout.Scale(24f);
            float chrome = EsuHudLayout.Scale(8f);
            float desired = Mathf.Max(EsuHudLayout.Scale(38f), rowCount * rowHeight + chrome);
            float cap = Mathf.Min(EsuHudLayout.Scale(184f), Mathf.Max(EsuHudLayout.Scale(42f), contentHeight * 0.42f));
            return Mathf.Min(desired, cap);
        }

        private int UnifiedSurfaceDraftRowCount()
        {
            int count = _surfaceDraft.Points.Count + _surfaceDraft.Faces.Count + _generatorDraft.PointCount;
            if (_surfaceDraft.HasDraft)
                count++;
            if (_generatorDraft.HasDraft)
                count++;
            return count;
        }

        private void DrawUnifiedSurfaceDraftRows()
        {
            if (_surfaceDraft.HasDraft)
            {
                GUILayout.Label("Surface", DecorationEditorTheme.Mini);
                for (int index = 0; index < _surfaceDraft.Points.Count; index++)
                {
                    GUIStyle style = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Point &&
                                     _surfaceDraft.SelectedPoint == index
                        ? DecorationEditorTheme.RowSelected
                        : DecorationEditorTheme.Row;
                    if (GUILayout.Button(
                            new GUIContent(PointLabel(index, _surfaceDraft.Points[index]), "Select this surface draft point."),
                            style,
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                        SelectSurfaceDraftPointRow(index);
                }

                for (int index = 0; index < _surfaceDraft.Faces.Count; index++)
                {
                    GUIStyle style = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                                     _surfaceDraft.SelectedFace == index
                        ? DecorationEditorTheme.RowSelected
                        : DecorationEditorTheme.Row;
                    if (GUILayout.Button(
                            new GUIContent(SurfaceFaceLabel(index), "Select this surface draft face."),
                            style,
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                        SelectSurfaceDraftFaceRow(index);
                }
            }

            if (_generatorDraft.HasDraft)
            {
                GUILayout.Label(GeneratorToolDisplayName(_generatorDraft.Tool), DecorationEditorTheme.Mini);
                for (int index = 0; index < _generatorDraft.PointCount; index++)
                {
                    GUIStyle style = _generatorDraft.SelectedPoint == index
                        ? DecorationEditorTheme.RowSelected
                        : DecorationEditorTheme.Row;
                    if (GUILayout.Button(
                            new GUIContent(GeneratorPointLabel(index), "Select this generated path or circle point."),
                            style,
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                        SelectGeneratorDraftPointRow(index);
                }
            }

            if (!_surfaceDraft.HasDraft && !_generatorDraft.HasDraft)
                GUILayout.Label("No draft points yet.", DecorationEditorTheme.MiniWrap);
        }

        private string SurfaceFaceLabel(int index)
        {
            if (index < 0 || index >= _surfaceDraft.Faces.Count)
                return "Face";

            SurfaceFace face = _surfaceDraft.Faces[index];
            SurfaceFaceStyle style = _surfaceDraft.FaceStyleAt(index);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Face {0}: {1}, {2}, {3} | color {4}",
                index + 1,
                face.A + 1,
                face.B + 1,
                face.C + 1,
                style.ColorIndex);
        }

        private void SelectSurfaceDraftPointRow(int index)
        {
            _surfaceDraft.SelectPoint(index);
            _generatorDraft.ClearSelection();
            _surfaceMessage = "Surface point selected.";
        }

        private void SelectSurfaceDraftFaceRow(int index)
        {
            _surfaceDraft.SelectFace(index);
            _generatorDraft.ClearSelection();
            SyncSharedPaintColorFromSurfaceFace(index);
            _surfaceMessage = "Surface face selected.";
        }

        private void SyncSharedPaintColorFromSurfaceFace(int index)
        {
            if (index < 0 || index >= _surfaceDraft.Faces.Count)
                return;

            int color = Mathf.Clamp(_surfaceDraft.FaceStyleAt(index).ColorIndex, 0, 31);
            _surfaceDraft.Settings.ColorIndex = color;
            _generatorSettings.ColorIndex = color;
            _surfaceColorText = color.ToString(CultureInfo.InvariantCulture);
            _generatorColorText = color.ToString(CultureInfo.InvariantCulture);
            _generatorPlan = null;
        }

        private void SelectGeneratorDraftPointRow(int index)
        {
            _surfaceDraft.ClearSelection();
            SetSurfaceBuilderTool(_generatorDraft.UsesCenterTool
                ? SurfaceBuilderTool.Circle
                : SurfaceBuilderTool.Path);
            _generatorDraft.SelectPoint(index);
            _generatorMessage = _generatorDraft.UsesCenterTool
                ? "Generator shape center selected."
                : "Path point selected.";
        }

        private string GeneratorPointLabel(int index)
        {
            Vector3 point = _generatorDraft.PointAt(index);
            string prefix = _generatorDraft.UsesCenterTool
                ? GeneratorToolDisplayName(_generatorDraft.Tool) + " center"
                : "Path P" + (index + 1).ToString(CultureInfo.InvariantCulture);
            return prefix + ": " + FormatPoint(point);
        }

        private void DrawSurfaceActionBar(Rect rect)
        {
            if (rect.height <= 1f)
                return;

            SurfaceDraftActionTarget target = ActiveSurfaceDraftActionTarget();
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("Preview", target == SurfaceDraftActionTarget.Surface
                        ? "Rebuild the surface decoration preview."
                        : "Rebuild the generated shape preview."),
                    DecorationEditorTheme.Button,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rect.height)))
                PreviewSurfaceActionTarget(target, showMessage: true);

            bool previous = GUI.enabled;
            bool canPlace = CanPlaceSurfaceActionTarget(target);
            GUI.enabled = previous && canPlace;
            if (GUILayout.Button(
                    new GUIContent("Place", target == SurfaceDraftActionTarget.Surface
                        ? "Create the planned surface decorations."
                        : "Create the generated shape decorations."),
                    DecorationEditorTheme.ToolButton(false, canPlace),
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rect.height)))
                PlaceSurfaceActionTarget(target);
            GUI.enabled = previous;

            bool canBridge = target == SurfaceDraftActionTarget.Surface &&
                             _surfaceDraft.BridgeEdgeSelection.Count == 2;
            GUI.enabled = previous && canBridge;
            if (GUILayout.Button(
                    new GUIContent("Bridge", "Create surface face(s) between the two Shift-selected edges."),
                    DecorationEditorTheme.ToolButton(false, canBridge),
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rect.height)))
                BridgeSurfaceEdges();
            GUI.enabled = previous;

            if (GUILayout.Button(
                    new GUIContent("Clear", target == SurfaceDraftActionTarget.Surface
                        ? "Clear the current surface draft."
                        : "Clear the current generator draft."),
                    DecorationEditorTheme.Button,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rect.height)))
                ClearSurfaceActionTarget(target, notify: true);
            if (GUILayout.Button(
                    new GUIContent("Delete", target == SurfaceDraftActionTarget.Surface
                        ? "Delete the selected surface point or face."
                        : "Delete the selected path point or shape center."),
                    DecorationEditorTheme.Button,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(rect.height)))
                DeleteSurfaceActionTarget(target);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private SurfaceDraftActionTarget ActiveSurfaceDraftActionTarget()
        {
            if (_generatorDraft.HasActiveSelection)
                return SurfaceDraftActionTarget.Generator;
            if (_surfaceDraft.HasActiveSelection)
                return SurfaceDraftActionTarget.Surface;
            if ((_surfaceBuilderTool == SurfaceBuilderTool.Path ||
                 _surfaceBuilderTool == SurfaceBuilderTool.Circle ||
                 _surfaceBuilderTool == SurfaceBuilderTool.Rotate ||
                 _surfaceBuilderTool == SurfaceBuilderTool.Scale) &&
                _generatorDraft.HasDraft)
                return SurfaceDraftActionTarget.Generator;
            if (_surfaceBuilderTool == SurfaceBuilderTool.Draw && _surfaceDraft.HasDraft)
                return SurfaceDraftActionTarget.Surface;
            if (!_surfaceDraft.HasDraft && _generatorDraft.HasDraft)
                return SurfaceDraftActionTarget.Generator;
            return SurfaceDraftActionTarget.Surface;
        }

        private void PreviewSurfaceActionTarget(SurfaceDraftActionTarget target, bool showMessage)
        {
            if (target == SurfaceDraftActionTarget.Generator)
                RebuildGeneratorPreview(showMessage);
            else
                RebuildSurfacePreview(showMessage);
        }

        private bool CanPlaceSurfaceActionTarget(SurfaceDraftActionTarget target) =>
            target == SurfaceDraftActionTarget.Generator
                ? _generatorPlan != null && _generatorPlan.DecorationCount > 0
                : _surfacePlan != null && _surfacePlan.DecorationCount > 0;

        private void PlaceSurfaceActionTarget(SurfaceDraftActionTarget target)
        {
            if (target == SurfaceDraftActionTarget.Generator)
                PlaceGeneratorPlan();
            else
                PlaceSurfacePlan();
        }

        private void ClearSurfaceActionTarget(SurfaceDraftActionTarget target, bool notify)
        {
            if (target == SurfaceDraftActionTarget.Generator)
                ClearGeneratorDraft(notify);
            else
                ClearSurfaceDraft(notify);
        }

        private void DeleteSurfaceActionTarget(SurfaceDraftActionTarget target)
        {
            if (target == SurfaceDraftActionTarget.Generator)
                DeleteGeneratorSelection();
            else
                DeleteSurfaceSelection();
        }

        private void DrawSurfaceSettings()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (GUILayout.Button(
                    new GUIContent("<", "Previous surface material."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f))))
                StepSurfaceMaterial(-1);
            GUILayout.Label(_surfaceDraft.Settings.StructureBlockType.ToString(), DecorationEditorTheme.Body, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(">", "Next surface material."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f))))
                StepSurfaceMaterial(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Thickness", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _surfaceThicknessText = GUILayout.TextField(_surfaceThicknessText ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(64f)));
            EsuCursorTooltip.RegisterLast("Type the surface thickness in metres.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed surface thickness."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(42f))))
                ApplySurfaceThicknessText();
            SurfaceThicknessButton(0.025f, "0.025");
            SurfaceThicknessButton(0.05f, "0.05");
            SurfaceThicknessButton(0.1f, "0.1");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            DrawPaintSwatchLayout(
                _surfaceDraft.Settings.ColorIndex,
                "Current surface paint color #" + _surfaceDraft.Settings.ColorIndex.ToString(CultureInfo.InvariantCulture) + ".",
                EsuHudLayout.Scale(34f),
                EsuHudLayout.Scale(22f));
            _surfaceColorText = GUILayout.TextField(_surfaceColorText ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(44f)));
            EsuCursorTooltip.RegisterLast("Type the surface paint color from 0 to 31.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed surface color."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(42f))))
                ApplySurfaceColorText();
            if (GUILayout.Button(
                    new GUIContent("-", "Decrease the surface color."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f))))
                SetSurfaceColor(_surfaceDraft.Settings.ColorIndex - 1);
            if (GUILayout.Button(
                    new GUIContent("+", "Increase the surface color."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f))))
                SetSurfaceColor(_surfaceDraft.Settings.ColorIndex + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent(
                        _surfaceDraft.Settings.NormalReversal ? "Normal flip: on" : "Normal flip: off",
                        "Flip the generated surface normal direction."),
                    DecorationEditorTheme.ToolButton(_surfaceDraft.Settings.NormalReversal),
                    GUILayout.Width(EsuHudLayout.Scale(126f))))
            {
                SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
                _surfaceDraft.Settings.NormalReversal = !_surfaceDraft.Settings.NormalReversal;
                InvalidateSurfacePlan("Surface normal direction changed.");
                RecordSurfaceEdit("Set surface normal direction", before);
            }
            DrawSurfaceAnchorModeButton(true);
            DrawSurfaceAnchorModeButton(false);
            GUILayout.EndHorizontal();

            if (!_surfaceDraft.Settings.NearestAnchor)
                DrawSurfaceSharedAnchorControls();
        }

        private void DrawSurfaceAnchorModeButton(bool nearest)
        {
            bool active = _surfaceDraft.Settings.NearestAnchor == nearest;
            string label = nearest ? "Nearest anchor" : "Same anchor";
            string tooltip = nearest
                ? "Resolve each surface decoration to its nearest valid block."
                : "Use one shared anchor for every surface decoration in the draft.";
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(122f))))
            {
                if (active)
                    return;

                SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
                _surfaceDraft.Settings.NearestAnchor = nearest;
                if (nearest)
                    _pendingSurfaceSharedAnchorPick = false;
                InvalidateSurfacePlan("Surface anchor mode changed.");
                RecordSurfaceEdit("Set surface anchor mode", before);
            }
        }

        private void DrawSurfaceSharedAnchorControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Anchor", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            string label = _surfaceDraft.HasSharedAnchor
                ? FormatTether(_surfaceDraft.SharedAnchor)
                : "auto";
            GUILayout.Label(label, DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(82f)));
            if (GUILayout.Button(
                    new GUIContent(_pendingSurfaceSharedAnchorPick ? "Picking" : "Pick", "Pick the shared surface anchor from the pointed craft block."),
                    DecorationEditorTheme.ToolButton(_pendingSurfaceSharedAnchorPick),
                    GUILayout.Width(EsuHudLayout.Scale(62f))))
            {
                _pendingSurfaceSharedAnchorPick = !_pendingSurfaceSharedAnchorPick;
                if (_pendingSurfaceSharedAnchorPick)
                    _surfaceMessage = "Click a craft block to set the shared surface anchor.";
            }

            if (GUILayout.Button(
                    new GUIContent("Clear", "Return same-anchor mode to automatic anchor selection."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
            {
                SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
                _surfaceDraft.ClearSharedAnchor();
                _pendingSurfaceSharedAnchorPick = false;
                InvalidateSurfacePlan("Surface shared anchor cleared.");
                RecordSurfaceEdit("Clear surface shared anchor", before);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private GUIStyle SurfaceMessageStyle()
        {
            if (_surfaceMessage.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _surfaceMessage.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _surfaceMessage.IndexOf("no valid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DecorationEditorTheme.Error;
            }

            return _surfaceMessage.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0
                ? DecorationEditorTheme.Warning
                : DecorationEditorTheme.MiniWrap;
        }

        private void StepSurfaceMaterial(int delta)
        {
            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            int count = Enum.GetValues(typeof(StructureBlockType)).Length;
            int next = ((int)_surfaceDraft.Settings.StructureBlockType + delta + count) % count;
            _surfaceDraft.Settings.StructureBlockType = (StructureBlockType)next;
            InvalidateSurfacePlan("Surface material changed.");
            RecordSurfaceEdit("Set surface material", before);
        }

        private void SurfaceThicknessButton(float value, string label)
        {
            if (GUILayout.Button(
                    new GUIContent(label, "Set surface thickness to " + label + "m."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(48f))))
                SetSurfaceThickness(value);
        }

        private void ApplySurfaceThicknessText()
        {
            if (!FlexibleFloatParser.TryParse(_surfaceThicknessText, out float value))
            {
                _surfaceThicknessText = FormatFloat(_surfaceDraft.Settings.FaceThickness);
                InfoStore.Add("Surface thickness must be a finite number.");
                return;
            }

            SetSurfaceThickness(value);
        }

        private void SetSurfaceThickness(float value)
        {
            if (!DecorationEditMath.IsFinite(value) || value <= 0f)
            {
                InfoStore.Add("Surface thickness must be finite and greater than zero.");
                return;
            }

            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            _surfaceDraft.Settings.FaceThickness = Mathf.Clamp(value, 0.001f, 10f);
            _surfaceThicknessText = FormatFloat(_surfaceDraft.Settings.FaceThickness);
            InvalidateSurfacePlan("Surface thickness changed.");
            RecordSurfaceEdit("Set surface thickness", before);
        }

        private void ApplySurfaceColorText()
        {
            if (!int.TryParse((_surfaceColorText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                _surfaceColorText = _surfaceDraft.Settings.ColorIndex.ToString(CultureInfo.InvariantCulture);
                InfoStore.Add("Surface color must be an integer from 0 to 31.");
                return;
            }

            SetSurfaceColor(value);
        }

        private void SetSurfaceColor(int value)
        {
            SetSurfaceBuilderPaintColor(value);
        }

        private string SurfaceSummary()
        {
            string count = _surfacePlan == null
                ? "no preview"
                : _surfacePlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) + " decorations";
            string free = _surfaceDraft.FreeTriangleSelectionCount.ToString(CultureInfo.InvariantCulture) + "/3 free";
            string bridge = _surfaceDraft.BridgeEdgeSelection.Count.ToString(CultureInfo.InvariantCulture) + "/2 bridge";
            string selection = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Point
                ? "point " + (_surfaceDraft.SelectedPoint + 1).ToString(CultureInfo.InvariantCulture)
                : _surfaceDraft.SelectionKind == SurfaceSelectionKind.Edge
                    ? "edge " + (_surfaceDraft.SelectedEdge.A + 1).ToString(CultureInfo.InvariantCulture) +
                      "-" + (_surfaceDraft.SelectedEdge.B + 1).ToString(CultureInfo.InvariantCulture)
                    : _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face
                        ? "face " + (_surfaceDraft.SelectedFace + 1).ToString(CultureInfo.InvariantCulture)
                        : _surfaceDraft.SharedAnchorSelected
                            ? "anchor"
                            : "none";
            string anchor = _surfaceDraft.Settings.NearestAnchor
                ? "nearest"
                : _surfaceDraft.HasSharedAnchor
                    ? "same " + FormatTether(_surfaceDraft.SharedAnchor)
                    : "same auto";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0} points | {1:N0} faces | {2} | {3} | {4} | anchor {5} | selected {6}",
                _surfaceDraft.Points.Count,
                _surfaceDraft.Faces.Count,
                count,
                free,
                bridge,
                anchor,
                selection);
        }

        private string PointLabel(int index, Vector3 point) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "P{0}: {1:0.###}, {2:0.###}, {3:0.###}",
                index + 1,
                point.x,
                point.y,
                point.z);

        private static string FormatPoint(Vector3 point) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###}, {1:0.###}, {2:0.###}",
                point.x,
                point.y,
                point.z);

        private void DrawInspectorPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUI.BeginGroup(rect);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            float gap = EsuHudLayout.Scale(6f);
            Rect headerRect = InspectorHeaderRect(rect, localToPanel: true);
            Rect contentRect = new Rect(
                inner.x,
                headerRect.yMax + gap,
                inner.width,
                Mathf.Max(0f, inner.yMax - headerRect.yMax - gap));

            if (contentRect.height > 1f)
            {
                GUILayout.BeginArea(contentRect);
                DrawInspector(contentRect.height);
                GUILayout.EndArea();
            }

            DrawInspectorHeader(headerRect);
            GUI.EndGroup();
        }

        private static Rect InspectorHeaderRect(Rect panelRect, bool localToPanel)
        {
            float inset = EsuHudLayout.Scale(8f);
            float headerHeight = EsuHudLayout.Scale(24f);
            Rect inner = new Rect(
                (localToPanel ? 0f : panelRect.x) + inset,
                (localToPanel ? 0f : panelRect.y) + inset,
                Mathf.Max(1f, panelRect.width - inset * 2f),
                Mathf.Max(1f, panelRect.height - inset * 2f));
            return new Rect(
                inner.x,
                inner.y,
                inner.width,
                Mathf.Min(headerHeight, inner.height));
        }

        private static Rect InspectorHeaderHideRect(Rect rect)
        {
            float hideWidth = Mathf.Min(EsuHudLayout.Scale(58f), rect.width);
            return new Rect(rect.xMax - hideWidth, rect.y, hideWidth, rect.height);
        }

        private void DrawInspectorHeader(Rect rect)
        {
            float gap = EsuHudLayout.Scale(6f);
            float selectedWidth = rect.width >= EsuHudLayout.Scale(260f)
                ? Mathf.Min(EsuHudLayout.Scale(132f), rect.width * 0.32f)
                : 0f;
            Rect hideRect = InspectorHeaderHideRect(rect);
            Rect selectedRect = new Rect(
                Mathf.Max(rect.x, hideRect.x - gap - selectedWidth),
                rect.y,
                selectedWidth,
                rect.height);
            Rect titleRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, selectedRect.x - rect.x - gap),
                rect.height);

            DrawCompactIconHeader(titleRect, "Inspector", "settings");
            if (selectedRect.width > 1f)
            {
                GUI.Label(
                    selectedRect,
                    _selected == null || _selected.IsDeleted ? "No selection" : MeshName(_selected.MeshGuid.Us),
                    DecorationEditorTheme.Mini);
            }

            if (GUI.Button(hideRect, new GUIContent("Hide", "Hide the inspector panel."), DecorationEditorTheme.Button))
                _showInspectorPanel = false;
        }

        private void DrawMeshListRows(
            List<DecorationMeshCatalogEntry> rows,
            float viewportHeight,
            bool mouseInListViewport)
        {
            float rowHeight = EsuHudLayout.Scale(MeshPaletteRowHeight);
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_meshScroll.y / rowHeight) - 4);
            int visible = Mathf.CeilToInt(viewportHeight / rowHeight) + 8;
            int last = Mathf.Min(total, first + visible);
            if (first > 0)
                GUILayout.Space(first * rowHeight);
            for (int index = first; index < last; index++)
            {
                DecorationMeshCatalogEntry entry = rows[index];
                bool active = ReferenceEquals(entry, _selectedMesh) || ReferenceEquals(entry, _placingMesh);
                GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"[{entry.Kind}] {entry.Name}", style, GUILayout.Height(rowHeight)))
                    StartMeshPlacement(entry);
                EsuCursorTooltip.RegisterLast("Start placing this mesh.");

                bool previous = GUI.enabled;
                GUI.enabled = previous && _selected != null && !_selected.IsDeleted;
                if (GUILayout.Button(
                        new GUIContent("Set", "Replace the selected decoration with this mesh."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(44f)),
                        GUILayout.Height(rowHeight)))
                    AssignMeshToSelected(entry);
                GUI.enabled = previous;
                GUILayout.EndHorizontal();

                Rect row = GUILayoutUtility.GetLastRect();
                if (mouseInListViewport && row.Contains(Event.current.mousePosition))
                    _hoveredMesh = entry;
            }
            if (last < total)
                GUILayout.Space((total - last) * rowHeight);
        }

        private void DrawMeshPreviewGrid(
            List<DecorationMeshCatalogEntry> rows,
            float viewportWidth,
            float viewportHeight,
            bool mouseInListViewport)
        {
            MeshPreviewGridLayout layout = MeshPreviewGridLayoutFor(viewportWidth);
            int totalRows = Mathf.CeilToInt(rows.Count / (float)layout.Columns);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_meshScroll.y / layout.RowHeight));
            int lastRow = Mathf.Min(
                totalRows,
                Mathf.CeilToInt((_meshScroll.y + viewportHeight) / layout.RowHeight));
            bool canRenderVisiblePreview = Event.current != null && Event.current.type == EventType.Repaint;
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

                    DecorationMeshCatalogEntry entry = rows[rowIndex];
                    bool active = ReferenceEquals(entry, _selectedMesh) || ReferenceEquals(entry, _placingMesh);
                    GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                    Rect card = new Rect(
                        rowRect.x + layout.OuterPadding + column * (layout.CardWidth + layout.Gap),
                        rowRect.y,
                        layout.CardWidth,
                        layout.CardHeight);
                    if (GUI.Button(card, GUIContent.none, style))
                        StartMeshPlacement(entry);
                    EsuCursorTooltip.Register(card, "Start placing this mesh.");
                    bool hovered =
                        Event.current != null &&
                        Event.current.type != EventType.Layout &&
                        mouseInListViewport &&
                        card.Contains(Event.current.mousePosition);
                    if (hovered)
                        _hoveredMesh = entry;

                    int previewPixels = Mathf.Clamp(
                        Mathf.RoundToInt(Mathf.Max(
                            EsuHudLayout.Scale(MeshPreviewGridTexturePixels),
                            Mathf.Min(card.width, layout.PreviewHeight))),
                        64,
                        160);
                    Texture preview = canRenderVisiblePreview
                        ? _previewRenderer?.GetPreview(entry, previewPixels, _previewSpin)
                        : _previewRenderer?.GetCachedPreview(entry, previewPixels);

                    Rect previewRect = new Rect(
                        card.x + EsuHudLayout.Scale(6f),
                        card.y + EsuHudLayout.Scale(4f),
                        card.width - EsuHudLayout.Scale(12f),
                        layout.PreviewHeight);
                    if (preview != null)
                        GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
                    else
                        GUI.Label(previewRect, "loading", DecorationEditorTheme.Mini);
                    GUI.Label(
                        new Rect(
                            card.x + EsuHudLayout.Scale(4f),
                            card.yMax - EsuHudLayout.Scale(26f),
                            card.width - EsuHudLayout.Scale(8f),
                            EsuHudLayout.Scale(22f)),
                        CompactText(
                            entry.Name,
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

        private static MeshPreviewGridLayout MeshPreviewGridLayoutFor(float viewportWidth)
        {
            float outerPadding = EsuHudLayout.Scale(MeshPreviewGridOuterPadding);
            float gap = EsuHudLayout.Scale(MeshPreviewGridGap);
            float minCardWidth = EsuHudLayout.Scale(MeshPreviewGridMinCardWidth);
            float available = Mathf.Max(minCardWidth, viewportWidth - outerPadding * 2f);
            int columns = Mathf.Max(
                1,
                Mathf.FloorToInt((available + gap) / (minCardWidth + gap)));
            float cardWidth = Mathf.Max(
                minCardWidth,
                (available - gap * Mathf.Max(0, columns - 1)) / columns);
            float cardHeight = Mathf.Clamp(
                cardWidth * MeshPreviewGridCardAspect,
                EsuHudLayout.Scale(MeshPreviewGridMinCardHeight),
                EsuHudLayout.Scale(MeshPreviewGridMaxCardHeight));
            float previewHeight = Mathf.Max(
                EsuHudLayout.Scale(52f),
                cardHeight - EsuHudLayout.Scale(32f));
            return new MeshPreviewGridLayout(
                columns,
                cardWidth,
                cardHeight,
                cardHeight + gap,
                previewHeight,
                gap,
                outerPadding);
        }

        private IEnumerable<DecorationMeshCatalogEntry> FilterMeshCatalog()
        {
            IEnumerable<DecorationMeshCatalogEntry> query = _meshCatalog;
            if (_meshKindFilter == "recent")
            {
                query = _recentMeshes
                    .Select(guid => _meshByGuid.TryGetValue(guid, out DecorationMeshCatalogEntry entry) ? entry : null)
                    .Where(entry => entry != null);
            }
            else if (_meshKindFilter == "item" || _meshKindFilter == "object")
            {
                query = query.Where(entry => string.Equals(entry.Kind, _meshKindFilter, StringComparison.OrdinalIgnoreCase));
            }

            string filter = (_meshFilter ?? string.Empty).Trim().ToLowerInvariant();
            if (filter.Length > 0)
                query = query.Where(entry => entry.SearchText.Contains(filter));
            return query;
        }

        private static List<MaterialCatalogEntry> BuildMaterialCatalog()
        {
            var result = new List<MaterialCatalogEntry>();
            try
            {
                foreach (MaterialDefinition material in Configured.i.Materials.Components)
                {
                    if (material?.ComponentId == null || material.ComponentId.Guid == Guid.Empty)
                        continue;

                    string name = material.ComponentId.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = "Material " + material.ComponentId.Guid.ToString("N").Substring(0, 8);
                    result.Add(new MaterialCatalogEntry(material.ComponentId.Guid, name));
                }
            }
            catch
            {
                // The manual GUID editor remains available if FTD's material container is unavailable.
            }

            return result
                .GroupBy(material => material.Guid)
                .Select(group => group.First())
                .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(material => material.Guid)
                .ToList();
        }

        private string MaterialDisplayName(Guid materialGuid)
        {
            if (materialGuid == Guid.Empty)
                return "default";

            MaterialCatalogEntry entry = (_materialCatalog ?? Enumerable.Empty<MaterialCatalogEntry>())
                .FirstOrDefault(material => material.Guid == materialGuid);
            return entry == null
                ? materialGuid.ToString("D")
                : entry.Name + " | " + materialGuid.ToString("N").Substring(0, 8);
        }

        private void DrawBottomPanel(float width, float height)
        {
            float headerHeight = EsuHudLayout.Scale(24f);
            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            float statusHeight = EsuHudLayout.Scale(24f);
            float snapHeight = EsuHudLayout.Scale(26f);
            float transformHeight = EsuHudLayout.Scale(30f);
            float footerHeight = EsuHudLayout.Scale(18f);
            float y = 0f;
            DrawBottomHeader(new Rect(0f, y, width, headerHeight));
            y += headerHeight;
            DrawCyanLine(new Rect(0f, y, width, separatorHeight));
            y += separatorHeight + EsuHudLayout.Scale(3f);

            GUI.Label(new Rect(0f, y, width, statusHeight), StatusLine(), DecorationEditorTheme.Status);
            y += statusHeight + EsuHudLayout.Scale(3f);

            DrawBottomSnapControls(new Rect(0f, y, width, snapHeight));
            y += snapHeight + EsuHudLayout.Scale(3f);

            DrawBottomTransformEditors(new Rect(0f, y, width, transformHeight));
            y += transformHeight + EsuHudLayout.Scale(3f);

            if (y < height)
            {
                GUI.Label(
                    new Rect(0f, y, width, Mathf.Min(footerHeight, height - y)),
                    "Tab switches ESU modes when clean | Ctrl+D/Esc closes and restores un-applied edits | Snap settings affect transform drags only",
                    DecorationEditorTheme.Mini);
            }
        }

        private void DrawBottomSnapControls(Rect rect)
        {
            Rect snapRect = BottomTransformGroupRect(rect);
            if (EsuTransformSnapHud.DrawSnapControls(
                    snapRect,
                    "Snap",
                    ref _snapMoveText,
                    ref _snapRotateText,
                    ref _snapScaleText,
                    "Move snap in metres for Decoration Edit and Surface Builder transform drags.",
                    "Rotation snap in degrees for decoration and generator rotation drags.",
                    "Scale snap for decoration and generator scale drags.",
                    out bool commitRequested))
            {
                ApplyDecorationSnapText(commitRequested);
            }
        }

        private float DecorationMoveSnap => EsuTransformSnapSettings.DecorationMoveSnap;

        private float DecorationRotateSnapDegrees => EsuTransformSnapSettings.DecorationRotateSnapDegrees;

        private float DecorationScaleSnap => EsuTransformSnapSettings.DecorationScaleSnap;

        private void SyncSnapTextFromSettings()
        {
            _snapMoveText = EsuTransformSnapSettings.Format(DecorationMoveSnap);
            _snapRotateText = EsuTransformSnapSettings.Format(DecorationRotateSnapDegrees);
            _snapScaleText = EsuTransformSnapSettings.Format(DecorationScaleSnap);
        }

        private void ApplyDecorationSnapText(bool commitRequested)
        {
            if (!EsuTransformSnapSettings.TryParseSnapText(_snapMoveText, out float move) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapRotateText, out float rotate) ||
                !EsuTransformSnapSettings.TryParseSnapText(_snapScaleText, out float scale))
            {
                if (commitRequested)
                {
                    InfoStore.Add("Snap settings must be finite numbers.");
                    SyncSnapTextFromSettings();
                }
                return;
            }

            EsuTransformSnapSettings.SetDecoration(move, rotate, scale);
            if (!commitRequested)
                return;

            SyncSnapTextFromSettings();
            InfoStore.Add(
                "Decoration snap set: move " + _snapMoveText +
                "m, rotate " + _snapRotateText +
                " degrees, scale " + _snapScaleText + ".");
        }

        private void DrawBottomHeader(Rect rect)
        {
            bool surface = IsSurfaceMode;
            BottomHeaderLayout slots = BottomHeaderRects(rect);

            GUI.Label(slots.Title, surface ? "Surface Builder" : "Decoration Edit Mode", DecorationEditorTheme.SubHeader);
            GUI.Label(
                slots.Mode,
                surface ? "Mode: Surface | Tab to Build when clean" : "Mode: Deco | Tab to Surface when clean",
                DecorationEditorTheme.Body);
            if (surface)
            {
                DrawHideOriginalMeshButton(slots.SelectControls);
                GUI.Label(slots.AnchorSettings, _showSurfaceToolsPanel ? "Tools: on" : "Tools: off", DecorationEditorTheme.Mini);
            }
            else
            {
                DrawBottomSelectionControls(slots.SelectControls);
                DrawBottomAnchorFollowToggle(slots.AnchorFollow);
                DrawBottomAnchorSettingsButton(slots.AnchorSettings);
            }
            GUI.Label(
                slots.Clean,
                HasUnappliedChanges ? "Dirty preview" : "Clean",
                HasUnappliedChanges ? DecorationEditorTheme.Warning : DecorationEditorTheme.Mini);
        }

        private void DrawSurfaceExtraToolsPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUI.BeginGroup(rect);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            Rect contentRect = inner;

            GUILayout.BeginArea(contentRect);
            DrawCompactIconHeader("Extra Tools", "settings");
            _surfaceExtraToolsViewportHeight = Mathf.Max(
                1f,
                contentRect.height - EsuHudLayout.Scale(30f));
            _generatorScroll = GUILayout.BeginScrollView(
                _generatorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Width(contentRect.width),
                GUILayout.Height(_surfaceExtraToolsViewportHeight));

            DrawGeneratorToolButtons();

            GUILayout.Label(GeneratorSummary(), DecorationEditorTheme.BodyWrap);
            GUILayout.Label(_generatorMessage, GeneratorMessageStyle());

            DecorationEditorTheme.Separator();
            DrawGeneratorMeshEditor();
            DrawGeneratorLengthAxisControl();

            DecorationEditorTheme.Separator();
            GUILayout.Label("Shape", DecorationEditorTheme.SubHeader);
            DrawGeneratorNumberField("Diameter", ref _generatorDiameterText, ApplyGeneratorDiameterText, EsuHudLayout.Scale(58f));
            DrawGeneratorDiameterPresets();
            if (DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool))
            {
                DrawGeneratorNumberField("Radius", ref _generatorRadiusText, ApplyGeneratorRadiusText, EsuHudLayout.Scale(58f));
                DrawGeneratorNumberField("Segments", ref _generatorSegmentsText, ApplyGeneratorSegmentsText, EsuHudLayout.Scale(42f));
                DrawGeneratorNumberField("Arc", ref _generatorArcText, ApplyGeneratorArcText, EsuHudLayout.Scale(54f));
            }

            if (GeneratorToolUsesHeight(_surfaceExtraTool))
                DrawGeneratorNumberField("Height", ref _generatorHeightText, ApplyGeneratorHeightText, EsuHudLayout.Scale(58f));
            if (_surfaceExtraTool == SurfaceExtraTool.Frustum)
                DrawGeneratorNumberField("Top radius", ref _generatorTopRadiusText, ApplyGeneratorTopRadiusText, EsuHudLayout.Scale(58f));
            if (_surfaceExtraTool == SurfaceExtraTool.Sphere ||
                _surfaceExtraTool == SurfaceExtraTool.PartialSphere)
            {
                DrawGeneratorNumberField("Rings", ref _generatorRingsText, ApplyGeneratorRingsText, EsuHudLayout.Scale(42f));
            }

            DrawGeneratorAnchorModeControl();

            DecorationEditorTheme.Separator();
            DrawGeneratorColorEditor();
            DrawGeneratorMaterialEditor();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.EndGroup();
        }

        private void DrawGeneratorMeshEditor()
        {
            GUILayout.Label("Mesh", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Using: " + GeneratorMeshSourceLabel(), DecorationEditorTheme.Mini, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent("Reset", "Use the default Metal pole (1m) mesh."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
                ResetGeneratorMesh();
            if (GUILayout.Button(
                    new GUIContent(_showGeneratorMeshPicker ? "Hide list" : "Show list", "Show or hide the generator mesh picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f))))
                _showGeneratorMeshPicker = !_showGeneratorMeshPicker;
            GUILayout.EndHorizontal();

            if (!_showGeneratorMeshPicker)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            _generatorMeshFilter = GUILayout.TextField(_generatorMeshFilter ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Filter generator meshes by name or GUID.");
            GUILayout.EndHorizontal();

            List<DecorationMeshCatalogEntry> rows = FilterGeneratorMeshCatalog().ToList();
            GUILayout.Label(GeneratorMeshCountText(rows.Count), DecorationEditorTheme.Mini);
            float rowHeight = EsuHudLayout.Scale(22f);
            float height = Mathf.Min(
                EsuHudLayout.Scale(180f),
                Mathf.Max(EsuHudLayout.Scale(76f), rows.Count * rowHeight + EsuHudLayout.Scale(8f)));
            _generatorMeshScroll = GUILayout.BeginScrollView(
                _generatorMeshScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(height));
            DrawGeneratorMeshRows(rows, height, rowHeight);
            GUILayout.EndScrollView();
        }

        private void DrawGeneratorToolButtons()
        {
            GUILayout.BeginHorizontal();
            DrawGeneratorToolButton(SurfaceExtraTool.Path, "Path", "Draw a path from clicked craft-surface points.", 58f);
            DrawGeneratorToolButton(SurfaceExtraTool.Circle, "Circle", "Generate a full segmented circle.", 66f);
            DrawGeneratorToolButton(SurfaceExtraTool.PartialCircle, "Arc", "Generate a partial circle using the Arc setting.", 50f);
            DrawGeneratorToolButton(SurfaceExtraTool.Cone2D, "2D cone", "Generate a flat cone or sector fan.", 72f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawGeneratorToolButton(SurfaceExtraTool.Sphere, "Sphere", "Generate a wire sphere from rings and meridians.", 66f);
            DrawGeneratorToolButton(SurfaceExtraTool.PartialSphere, "Part sph", "Generate a partial wire sphere using the Arc setting.", 74f);
            DrawGeneratorToolButton(SurfaceExtraTool.Cone, "Cone", "Generate a wire cone from a base ring and apex.", 58f);
            DrawGeneratorToolButton(SurfaceExtraTool.Frustum, "Frustum", "Generate a wire frustum with bottom and top radii.", 76f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorToolButton(
            SurfaceExtraTool tool,
            string label,
            string tooltip,
            float width)
        {
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(_surfaceExtraTool == tool),
                    GUILayout.Width(EsuHudLayout.Scale(width))))
            {
                SetSurfaceExtraTool(tool);
            }
        }

        private void DrawGeneratorMeshRows(
            List<DecorationMeshCatalogEntry> rows,
            float viewportHeight,
            float rowHeight)
        {
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_generatorMeshScroll.y / rowHeight) - 4);
            int visible = Mathf.CeilToInt(viewportHeight / rowHeight) + 8;
            int last = Mathf.Min(total, first + visible);
            if (first > 0)
                GUILayout.Space(first * rowHeight);
            for (int index = first; index < last; index++)
            {
                DecorationMeshCatalogEntry entry = rows[index];
                bool active = _generatorSettings.MeshGuid == entry.Guid;
                if (GUILayout.Button(
                        new GUIContent($"[{entry.Kind}] {entry.Name}", "Use this mesh for generated paths and circles."),
                        active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                        GUILayout.Height(rowHeight)))
                    SetGeneratorMesh(entry);

                Rect row = GUILayoutUtility.GetLastRect();
                if (Event.current != null && row.Contains(Event.current.mousePosition))
                {
                    _hoveredMesh = entry;
                    _hoveredMeshHint = "Click: use for generated Path/Circle decorations.";
                }
            }
            if (last < total)
                GUILayout.Space((total - last) * rowHeight);
        }

        private IEnumerable<DecorationMeshCatalogEntry> FilterGeneratorMeshCatalog()
        {
            IEnumerable<DecorationMeshCatalogEntry> query = _meshCatalog;
            string filter = (_generatorMeshFilter ?? string.Empty).Trim().ToLowerInvariant();
            if (filter.Length > 0)
                query = query.Where(entry => entry.SearchText.Contains(filter));
            return query;
        }

        private string GeneratorMeshCountText(int matchingCount)
        {
            string text = matchingCount.ToString("N0", CultureInfo.InvariantCulture) +
                          " shown | " +
                          _meshCatalog.Count.ToString("N0", CultureInfo.InvariantCulture) +
                          " total meshes";
            string filter = (_generatorMeshFilter ?? string.Empty).Trim();
            if (filter.Length > 0)
                text += " | search: " + CompactText(filter, 32);
            return text;
        }

        private void DrawGeneratorDiameterPresets()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Presets", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            DrawGeneratorDiameterPresetButton(0.025f, "0.025");
            DrawGeneratorDiameterPresetButton(0.05f, "0.05");
            DrawGeneratorDiameterPresetButton(0.1f, "0.1");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorDiameterPresetButton(float value, string label)
        {
            if (GUILayout.Button(
                    new GUIContent(label, "Set generator diameter to " + label + "m."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(48f))))
                SetGeneratorDiameter(value);
        }

        private void DrawGeneratorAnchorModeControl()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Anchor", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            DrawGeneratorAnchorModeButton(true);
            DrawGeneratorAnchorModeButton(false);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!_generatorSettings.NearestAnchor)
                DrawGeneratorSharedAnchorControls();
        }

        private void DrawGeneratorAnchorModeButton(bool nearest)
        {
            bool active = _generatorSettings.NearestAnchor == nearest;
            string label = nearest ? "Nearest" : "Same";
            string tooltip = nearest
                ? "Resolve each generated decoration to its nearest valid block."
                : "Use one shared anchor for every generated path or circle decoration.";
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(EsuHudLayout.Scale(74f))))
            {
                if (active)
                    return;

                DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
                _generatorSettings.NearestAnchor = nearest;
                if (nearest)
                    _pendingGeneratorSharedAnchorPick = false;
                InvalidateGeneratorPlan("Generator anchor mode changed.");
                RecordGeneratorEdit("Set generator anchor mode", before);
            }
        }

        private void DrawGeneratorSharedAnchorControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Anchor", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            string label = _generatorDraft.HasSharedAnchor
                ? FormatTether(_generatorDraft.SharedAnchor)
                : "auto";
            GUILayout.Label(label, DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(82f)));
            if (GUILayout.Button(
                    new GUIContent(_pendingGeneratorSharedAnchorPick ? "Picking" : "Pick", "Pick the shared generator anchor from the pointed craft block."),
                    DecorationEditorTheme.ToolButton(_pendingGeneratorSharedAnchorPick),
                    GUILayout.Width(EsuHudLayout.Scale(62f))))
            {
                _pendingGeneratorSharedAnchorPick = !_pendingGeneratorSharedAnchorPick;
                if (_pendingGeneratorSharedAnchorPick)
                    _generatorMessage = "Click a craft block to set the shared generator anchor.";
            }

            if (GUILayout.Button(
                    new GUIContent("Clear", "Return same-anchor mode to automatic anchor selection."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
            {
                DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
                _generatorDraft.ClearSharedAnchor();
                _pendingGeneratorSharedAnchorPick = false;
                InvalidateGeneratorPlan("Generator shared anchor cleared.");
                RecordGeneratorEdit("Clear generator shared anchor", before);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorNumberField(
            string label,
            ref string text,
            Action apply,
            float fieldWidth)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            text = GUILayout.TextField(text ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(fieldWidth));
            EsuCursorTooltip.RegisterLast("Type the generator " + label.ToLowerInvariant() + ".");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed " + label.ToLowerInvariant() + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(38f))))
                apply();
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorLengthAxisControl()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Axis", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(34f)));
            DrawGeneratorAxisButton(DecorationEditAxis.X);
            DrawGeneratorAxisButton(DecorationEditAxis.Y);
            DrawGeneratorAxisButton(DecorationEditAxis.Z);
            GUILayout.Space(EsuHudLayout.Scale(6f));
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorAxisButton(DecorationEditAxis axis)
        {
            if (GUILayout.Button(
                    new GUIContent(axis.ToString(), "Scale the generated segment length along mesh " + axis + "."),
                    DecorationEditorTheme.ToolButton(_generatorSettings.LengthAxis == axis),
                    GUILayout.Width(EsuHudLayout.Scale(26f))))
            {
                DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
                _generatorSettings.LengthAxis = axis;
                InvalidateGeneratorPlan("Generator length axis changed.");
                RecordGeneratorEdit("Set generator length axis", before);
            }
        }

        private void DrawGeneratorMaterialControl()
        {
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            if (GUILayout.Button(
                    new GUIContent("<", "Previous generator material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(24f))))
                StepGeneratorMaterial(-1);
            GUILayout.Label(
                CompactText(MaterialDisplayName(_generatorSettings.MaterialReplacement), 32),
                DecorationEditorTheme.Body,
                GUILayout.Width(EsuHudLayout.Scale(176f)));
            if (GUILayout.Button(
                    new GUIContent(">", "Next generator material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(24f))))
                StepGeneratorMaterial(1);
            if (GUILayout.Button(
                    new GUIContent("GUID", "Apply the typed material GUID for generated decorations."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(44f))))
                ApplyGeneratorMaterialText();
            _generatorMaterialText = GUILayout.TextField(
                _generatorMaterialText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Width(EsuHudLayout.Scale(92f)));
            EsuCursorTooltip.RegisterLast("Type a material GUID, or leave blank for default.");
            GUILayout.Space(EsuHudLayout.Scale(6f));
        }

        private void DrawGeneratorColorEditor()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Paint color", DecorationEditorTheme.SubHeader, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(_showGeneratorColorPicker ? "Hide list" : "Show list", "Show or hide the generator paint color picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _showGeneratorColorPicker = !_showGeneratorColorPicker;
            }
            GUILayout.EndHorizontal();

            if (_showGeneratorColorPicker)
            {
                for (int row = 0; row < 4; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int column = 0; column < 8; column++)
                    {
                        int color = row * 8 + column;
                        if (DrawPaintColorButton(
                                color,
                                _generatorSettings.ColorIndex == color,
                                "Set generated decoration paint color to #" + color.ToString(CultureInfo.InvariantCulture) + ".",
                                EsuHudLayout.Scale(36f),
                                EsuHudLayout.Scale(24f)))
                        {
                            SetGeneratorColor(color);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }

            DrawGeneratorNumberField("Color", ref _generatorColorText, ApplyGeneratorColorText, EsuHudLayout.Scale(44f));
        }

        private void DrawGeneratorMaterialEditor()
        {
            GUILayout.Label("Material override", DecorationEditorTheme.SubHeader);
            Guid materialOverride = _generatorSettings.MaterialReplacement;
            bool hasMaterialOverride = materialOverride != Guid.Empty;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                MaterialOverrideButtonLabel(materialOverride),
                hasMaterialOverride
                    ? DecorationEditorTheme.ActiveButton
                    : DecorationEditorTheme.Button,
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (hasMaterialOverride &&
                GUILayout.Button(
                    new GUIContent("Clear", "Remove the generated material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetGeneratorMaterial(Guid.Empty);
            }

            if (GUILayout.Button(
                    new GUIContent(_showGeneratorMaterialPicker ? "Hide list" : "Show list", "Show or hide the generator material override picker."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(76f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _showGeneratorMaterialPicker = !_showGeneratorMaterialPicker;
                _generatorMaterialListHeight = 0f;
            }
            GUILayout.EndHorizontal();

            if (_showGeneratorMaterialPicker)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                    GUILayout.Width(EsuHudLayout.Scale(24f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f)));
                _materialFilter = GUILayout.TextField(_materialFilter ?? string.Empty, DecorationEditorTheme.TextField);
                EsuCursorTooltip.RegisterLast("Filter generator material overrides by name or GUID.");
                GUILayout.EndHorizontal();

                List<MaterialCatalogEntry> materials = FilterMaterials().ToList();
                GUILayout.Label(MaterialCountText(materials.Count), DecorationEditorTheme.Mini);
                float rowHeight = EsuHudLayout.Scale(22f);
                float height = GeneratorMaterialListViewportHeight(
                    materials.Count,
                    rowHeight,
                    EsuHudLayout.Scale(32f));
                _generatorMaterialScroll = GUILayout.BeginScrollView(
                    _generatorMaterialScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Height(height));
                foreach (MaterialCatalogEntry material in materials)
                {
                    bool active = _generatorSettings.MaterialReplacement == material.Guid;
                    if (GUILayout.Button(
                            new GUIContent(material.DisplayName, "Apply this generator material override."),
                            active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                            GUILayout.Height(rowHeight)))
                    {
                        SetGeneratorMaterial(material.Guid);
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("GUID", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(58f)));
            _generatorMaterialText = GUILayout.TextField(_generatorMaterialText ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.RegisterLast("Type a generator material GUID, or leave blank for default.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Apply the typed generator material override."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(42f))))
                ApplyGeneratorMaterialText();
            GUILayout.EndHorizontal();
        }

        private float GeneratorMaterialListViewportHeight(
            int materialCount,
            float rowHeight,
            float footerReserve)
        {
            float minimum = EsuHudLayout.Scale(76f);
            float contentHeight = materialCount <= 0
                ? minimum
                : materialCount * rowHeight + EsuHudLayout.Scale(8f);
            float panelCap = Mathf.Max(
                minimum,
                _surfaceExtraToolsViewportHeight - Mathf.Max(0f, footerReserve));
            float fallback = Mathf.Clamp(
                contentHeight,
                minimum,
                Mathf.Max(minimum, Mathf.Min(contentHeight, panelCap)));
            float height = _generatorMaterialListHeight > 0f
                ? _generatorMaterialListHeight
                : fallback;
            height = Mathf.Clamp(
                height,
                minimum,
                Mathf.Max(minimum, Mathf.Min(contentHeight, panelCap)));

            Event current = Event.current;
            Rect anchor = GUILayoutUtility.GetLastRect();
            if (current != null &&
                current.type == EventType.Repaint &&
                anchor.height > 0f &&
                _surfaceExtraToolsViewportHeight > 1f)
            {
                float visibleBottom = _generatorScroll.y + _surfaceExtraToolsViewportHeight;
                float remaining = visibleBottom - anchor.yMax - Mathf.Max(0f, footerReserve);
                float adaptiveCap = Mathf.Max(minimum, remaining);
                _generatorMaterialListHeight = Mathf.Clamp(
                    contentHeight,
                    minimum,
                    Mathf.Max(minimum, Mathf.Min(contentHeight, adaptiveCap)));
            }

            return height;
        }

        private void SetSurfaceExtraTool(SurfaceExtraTool tool)
        {
            _surfaceExtraTool = tool == SurfaceExtraTool.None ? SurfaceExtraTool.Path : tool;
            s_surfaceExtraTool = _surfaceExtraTool;
            _generatorDraft.SetTool(_surfaceExtraTool);
            _surfaceBuilderTool = DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool)
                ? SurfaceBuilderTool.Circle
                : SurfaceBuilderTool.Path;
            s_surfaceBuilderTool = _surfaceBuilderTool;
            if ((_surfaceExtraTool == SurfaceExtraTool.PartialCircle ||
                 _surfaceExtraTool == SurfaceExtraTool.PartialSphere ||
                 _surfaceExtraTool == SurfaceExtraTool.Cone2D) &&
                _generatorSettings.ArcDegrees >= 359.999f)
            {
                _generatorSettings.ArcDegrees = 180f;
                _generatorArcText = "180";
            }
            InvalidateGeneratorPlan("Generator tool changed.");
        }

        private void SyncGeneratorTextFromSettings()
        {
            _generatorDiameterText = _generatorSettings.Diameter.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorRadiusText = _generatorSettings.CircleRadius.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorSegmentsText = _generatorSettings.CircleSegments.ToString(CultureInfo.InvariantCulture);
            _generatorArcText = _generatorSettings.ArcDegrees.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorHeightText = _generatorSettings.ShapeHeight.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorTopRadiusText = _generatorSettings.TopRadius.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorRingsText = _generatorSettings.RingCount.ToString(CultureInfo.InvariantCulture);
            _generatorColorText = _generatorSettings.ColorIndex.ToString(CultureInfo.InvariantCulture);
            _generatorMaterialText = _generatorSettings.MaterialReplacement == Guid.Empty
                ? string.Empty
                : _generatorSettings.MaterialReplacement.ToString("D");
        }

        private void InvalidateGeneratorPlan(string message)
        {
            _generatorPlan = null;
            if (!string.IsNullOrEmpty(message))
                _generatorMessage = message;
        }

        private GUIStyle GeneratorMessageStyle()
        {
            if (_generatorMessage.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _generatorMessage.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _generatorMessage.IndexOf("no valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _generatorMessage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DecorationEditorTheme.Error;
            }

            return _generatorMessage.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0
                ? DecorationEditorTheme.Warning
                : DecorationEditorTheme.MiniWrap;
        }

        private string GeneratorSummary()
        {
            string preview = _generatorPlan == null
                ? "no preview"
                : _generatorPlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) + " deco";
            string mesh = TryResolveGeneratorMesh(out DecorationMeshCatalogEntry entry, out string _)
                ? CompactText(entry.Name, 30)
                : "mesh unavailable";
            string draft = DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool)
                ? (_generatorDraft.HasCircleCenter ? "shape center set" : "no shape center")
                : _generatorDraft.PathPoints.Count.ToString(CultureInfo.InvariantCulture) + " path points";
            string anchor = _generatorSettings.NearestAnchor
                ? "nearest"
                : _generatorDraft.HasSharedAnchor
                    ? "same " + FormatTether(_generatorDraft.SharedAnchor)
                    : "same auto";
            return string.Format(
                CultureInfo.InvariantCulture,
                "Extra: {0} | {1} | diameter {2:0.###} | arc {3:0.###} | color {4} | anchor {5} | {6} | {7}",
                GeneratorToolDisplayName(_surfaceExtraTool),
                mesh,
                _generatorSettings.Diameter,
                _generatorSettings.ArcDegrees,
                _generatorSettings.ColorIndex,
                anchor,
                draft,
                preview);
        }

        private static bool GeneratorToolUsesHeight(SurfaceExtraTool tool) =>
            tool == SurfaceExtraTool.Cone ||
            tool == SurfaceExtraTool.Frustum;

        private static string GeneratorToolDisplayName(SurfaceExtraTool tool)
        {
            switch (tool)
            {
                case SurfaceExtraTool.PartialCircle:
                    return "Partial circle";
                case SurfaceExtraTool.Sphere:
                    return "Sphere";
                case SurfaceExtraTool.PartialSphere:
                    return "Partial sphere";
                case SurfaceExtraTool.Cone:
                    return "Cone";
                case SurfaceExtraTool.Frustum:
                    return "Frustum";
                case SurfaceExtraTool.Cone2D:
                    return "2D cone";
                case SurfaceExtraTool.Circle:
                    return "Circle";
                default:
                    return "Path";
            }
        }

        private string GeneratorMeshSourceLabel()
        {
            if (TryResolveGeneratorMesh(out DecorationMeshCatalogEntry entry, out string reason))
                return CompactText(entry.Name, 42);
            return reason;
        }

        private bool TryResolveGeneratorMesh(
            out DecorationMeshCatalogEntry entry,
            out string reason)
        {
            entry = null;
            reason = null;
            if (_generatorSettings.MeshGuid != Guid.Empty &&
                _meshByGuid.TryGetValue(_generatorSettings.MeshGuid, out DecorationMeshCatalogEntry configured))
            {
                entry = configured;
                return true;
            }

            DecorationMeshCatalogEntry preset = FindGeneratorPresetMesh();
            if (preset != null)
            {
                entry = preset;
                return true;
            }

            reason = "No generator mesh is available. Choose a mesh from the list.";
            return false;
        }

        private DecorationMeshCatalogEntry FindGeneratorPresetMesh()
        {
            if (_meshByGuid.TryGetValue(DecorationGeneratorSettings.MetalPole1mGuid, out DecorationMeshCatalogEntry metalPole))
                return metalPole;

            string[] tokens = { "metal pole", "pipe", "tube", "cylinder", "pole", "rod" };

            foreach (string token in tokens)
            {
                DecorationMeshCatalogEntry match = _meshCatalog.FirstOrDefault(entry =>
                    entry != null &&
                    entry.SearchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return match;
            }

            return null;
        }

        private bool PrepareGeneratorSettings(out string reason)
        {
            if (!TryResolveGeneratorMesh(out DecorationMeshCatalogEntry entry, out reason))
            {
                _generatorSettings.MeshGuid = Guid.Empty;
                return false;
            }

            _generatorSettings.MeshGuid = entry.Guid;
            return _generatorSettings.IsValid(out reason);
        }

        private void ApplyGeneratorDiameterText()
        {
            if (!FlexibleFloatParser.TryParse(_generatorDiameterText, out float value))
            {
                InfoStore.Add("Generator diameter must be a finite number.");
                return;
            }

            SetGeneratorDiameter(value);
        }

        private void SetGeneratorDiameter(float value)
        {
            if (!DecorationEditMath.IsFinite(value) || value <= 0f)
            {
                InfoStore.Add("Generator diameter must be greater than zero.");
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.Diameter = value;
            _generatorDiameterText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Generator diameter changed.");
            RecordGeneratorEdit("Set generator diameter", before);
        }

        private void SetGeneratorMesh(DecorationMeshCatalogEntry entry)
        {
            if (entry == null)
                return;

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.MeshGuid = entry.Guid;
            InvalidateGeneratorPlan("Generator mesh changed.");
            RecordGeneratorEdit("Set generator mesh", before);
        }

        private void ResetGeneratorMesh()
        {
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.MeshGuid = DecorationGeneratorSettings.MetalPole1mGuid;
            InvalidateGeneratorPlan("Generator mesh reset.");
            RecordGeneratorEdit("Reset generator mesh", before);
        }

        private void ApplyGeneratorRadiusText()
        {
            if (!FlexibleFloatParser.TryParse(_generatorRadiusText, out float value))
            {
                InfoStore.Add("Circle radius must be a finite number.");
                return;
            }

            if (!DecorationEditMath.IsFinite(value) || value <= 0f)
            {
                InfoStore.Add("Circle radius must be greater than zero.");
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.CircleRadius = value;
            _generatorRadiusText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Circle radius changed.");
            RecordGeneratorEdit("Set generator circle radius", before);
        }

        private void ApplyGeneratorSegmentsText()
        {
            if (!int.TryParse(_generatorSegmentsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                InfoStore.Add("Circle segments must be a whole number.");
                return;
            }

            value = Mathf.Clamp(value, 3, 256);
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.CircleSegments = value;
            _generatorSegmentsText = value.ToString(CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Circle segment count changed.");
            RecordGeneratorEdit("Set generator circle segments", before);
        }

        private void ApplyGeneratorArcText()
        {
            if (!FlexibleFloatParser.TryParse(_generatorArcText, out float value))
            {
                InfoStore.Add("Generator arc must be a finite number.");
                return;
            }

            if (!DecorationEditMath.IsFinite(value) || value <= 0f)
            {
                InfoStore.Add("Generator arc must be greater than zero.");
                return;
            }

            value = Mathf.Clamp(value, 0.001f, 360f);
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.ArcDegrees = value;
            _generatorArcText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Generator arc changed.");
            RecordGeneratorEdit("Set generator arc", before);
        }

        private void ApplyGeneratorHeightText()
        {
            if (!FlexibleFloatParser.TryParse(_generatorHeightText, out float value))
            {
                InfoStore.Add("Generator height must be a finite number.");
                return;
            }

            if (!DecorationEditMath.IsFinite(value) || value <= 0f)
            {
                InfoStore.Add("Generator height must be greater than zero.");
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.ShapeHeight = value;
            _generatorHeightText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Generator height changed.");
            RecordGeneratorEdit("Set generator height", before);
        }

        private void ApplyGeneratorTopRadiusText()
        {
            if (!FlexibleFloatParser.TryParse(_generatorTopRadiusText, out float value))
            {
                InfoStore.Add("Generator top radius must be a finite number.");
                return;
            }

            if (!DecorationEditMath.IsFinite(value) || value < 0f)
            {
                InfoStore.Add("Generator top radius must be non-negative.");
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.TopRadius = value;
            _generatorTopRadiusText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Generator top radius changed.");
            RecordGeneratorEdit("Set generator top radius", before);
        }

        private void ApplyGeneratorRingsText()
        {
            if (!int.TryParse(_generatorRingsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                InfoStore.Add("Generator rings must be a whole number.");
                return;
            }

            value = Mathf.Clamp(value, 1, 64);
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.RingCount = value;
            _generatorRingsText = value.ToString(CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Generator ring count changed.");
            RecordGeneratorEdit("Set generator rings", before);
        }

        private void ApplyGeneratorColorText()
        {
            if (!int.TryParse(_generatorColorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                InfoStore.Add("Generator color must be 0 through 31.");
                return;
            }

            SetGeneratorColor(value);
        }

        private void ApplyGeneratorMaterialText()
        {
            string text = (_generatorMaterialText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) || text.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                SetGeneratorMaterial(Guid.Empty);
                return;
            }

            if (Guid.TryParse(text, out Guid material))
            {
                SetGeneratorMaterial(material);
                return;
            }

            InfoStore.Add("Generator material must be empty/default or a GUID.");
        }

        private void StepGeneratorMaterial(int delta)
        {
            var guids = new List<Guid> { Guid.Empty };
            guids.AddRange((_materialCatalog ?? Enumerable.Empty<MaterialCatalogEntry>()).Select(material => material.Guid));
            int index = guids.IndexOf(_generatorSettings.MaterialReplacement);
            if (index < 0)
                index = 0;
            int next = (index + delta) % guids.Count;
            if (next < 0)
                next += guids.Count;
            SetGeneratorMaterial(guids[next]);
        }

        private void SetGeneratorMaterial(Guid material)
        {
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.MaterialReplacement = material;
            _generatorMaterialText = material == Guid.Empty ? string.Empty : material.ToString("D");
            InvalidateGeneratorPlan("Generator material changed.");
            RecordGeneratorEdit("Set generator material", before);
        }

        private void SetGeneratorColor(int value)
        {
            SetSurfaceBuilderPaintColor(value);
        }

        private void SetSurfaceBuilderPaintColor(int value)
        {
            value = Mathf.Clamp(value, 0, 31);
            SurfaceDraftSnapshot surfaceBefore = CaptureSurfaceSnapshot();
            DecorationGeneratorEditSnapshot generatorBefore = CaptureGeneratorEditSnapshot();

            _surfaceDraft.Settings.ColorIndex = value;
            _generatorSettings.ColorIndex = value;
            _surfaceColorText = value.ToString(CultureInfo.InvariantCulture);
            _generatorColorText = value.ToString(CultureInfo.InvariantCulture);

            bool selectedFace =
                _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                _surfaceDraft.SelectedFace >= 0 &&
                _surfaceDraft.SelectedFace < _surfaceDraft.Faces.Count;
            if (selectedFace)
                _surfaceDraft.TrySetFaceColor(_surfaceDraft.SelectedFace, value, out string _);
            else
                _surfaceDraft.SetAllFaceColors(value);

            InvalidateSurfacePlan(selectedFace
                ? "Surface face color changed."
                : "Surface paint color changed.");
            InvalidateGeneratorPlan("Generator paint color changed.");
            RecordSurfaceBuilderStyleEdit("Set Surface Builder paint color", surfaceBefore, generatorBefore);
        }

        private static BottomHeaderLayout BottomHeaderRects(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float titleWidth = EsuHudLayout.Scale(168f);
            float cleanWidth = EsuHudLayout.Scale(92f);
            float settingsWidth = EsuHudLayout.Scale(132f);
            float anchorWidth = EsuHudLayout.Scale(132f);
            float selectWidth = EsuHudLayout.Scale(272f);
            Rect cleanRect = new Rect(rect.xMax - cleanWidth, rect.y, cleanWidth, rect.height);
            Rect settingsRect = new Rect(cleanRect.x - gap - settingsWidth, rect.y, settingsWidth, rect.height);
            Rect anchorRect = new Rect(settingsRect.x - gap - anchorWidth, rect.y, anchorWidth, rect.height);
            Rect selectRect = new Rect(anchorRect.x - gap - selectWidth, rect.y, selectWidth, rect.height);
            Rect titleRect = new Rect(rect.x, rect.y, titleWidth, rect.height);
            Rect modeRect = new Rect(
                titleRect.xMax + gap,
                rect.y,
                Mathf.Max(1f, selectRect.x - titleRect.xMax - gap * 2f),
                rect.height);
            return new BottomHeaderLayout(
                titleRect,
                modeRect,
                selectRect,
                anchorRect,
                settingsRect,
                cleanRect);
        }

        private void DrawBottomSelectionControls(Rect rect)
        {
            float gap = EsuHudLayout.Scale(4f);
            float height = Mathf.Min(rect.height, EsuHudLayout.Scale(24f));
            float y = rect.y + (rect.height - height) * 0.5f;
            float singleWidth = EsuHudLayout.Scale(58f);
            float boxWidth = EsuHudLayout.Scale(46f);
            float xrayWidth = EsuHudLayout.Scale(58f);
            float hideMeshWidth = EsuHudLayout.Scale(78f);
            float total = singleWidth + boxWidth + xrayWidth + hideMeshWidth + gap * 3f;
            float x = rect.x + Mathf.Max(0f, (rect.width - total) * 0.5f);

            if (GUI.Button(
                    new Rect(x, y, singleWidth, height),
                    new GUIContent("Single", "Click the nearest decoration center."),
                    DecorationEditorTheme.ToolButton(_selectionMode == DecorationSelectionMode.Single)))
            {
                SetActiveTool(DecorationEditorTool.Select);
                _selectionMode = DecorationSelectionMode.Single;
                CancelBoxSelection();
            }

            x += singleWidth + gap;
            if (GUI.Button(
                    new Rect(x, y, boxWidth, height),
                    new GUIContent("Box", "Drag a rectangle to select decoration centers."),
                    DecorationEditorTheme.ToolButton(_selectionMode == DecorationSelectionMode.Box)))
            {
                SetActiveTool(DecorationEditorTool.Select);
                _selectionMode = DecorationSelectionMode.Box;
            }

            x += boxWidth + gap;
            if (GUI.Button(
                    new Rect(x, y, xrayWidth, height),
                    new GUIContent("X-ray", "When enabled, box select can select decorations behind blocks."),
                    DecorationEditorTheme.ToolButton(_selectionXray)))
            {
                _selectionXray = !_selectionXray;
                if (_boxSelecting)
                    _boxSelectCandidateCount = CountBoxSelectionCandidates(BoxSelectionRect(), _selectionXray);
            }

            x += xrayWidth + gap;
            DrawHideOriginalMeshButton(new Rect(x, y, hideMeshWidth, height));
        }

        private void DrawHideOriginalMeshButton(Rect rect)
        {
            float height = Mathf.Min(rect.height, EsuHudLayout.Scale(24f));
            float width = Mathf.Min(rect.width, EsuHudLayout.Scale(78f));
            float x = rect.x + Mathf.Max(0f, (rect.width - width) * 0.5f);
            float y = rect.y + Mathf.Max(0f, (rect.height - height) * 0.5f);
            bool hasSelection = _selected != null && !_selected.IsDeleted;
            bool active = hasSelection && IsSelectedOriginalMeshHidden();
            if (GUI.Button(
                    new Rect(x, y, width, height),
                    new GUIContent("Hide mesh", "Hide the mesh of the selected decoration's anchor block. Only works for static blocks."),
                    DecorationEditorTheme.ToolButton(active, hasSelection)))
            {
                if (!hasSelection)
                {
                    InfoStore.Add("Select a decoration before hiding its anchor block mesh.");
                    return;
                }

                ToggleSelectedOriginalMeshVisibility();
            }
        }

        private void ToggleSelectedOriginalMeshVisibility()
        {
            if (_selected == null || _selected.IsDeleted)
            {
                InfoStore.Add("Select a decoration before hiding its anchor block mesh.");
                return;
            }

            bool hidden = _selected.GetHideOriginalMesh();
            bool target = !hidden;
            Decoration[] affected = GetOriginalMeshVisibilityTargets(_selected);
            DecorationEditSnapshot[] before = CaptureSnapshots(affected);

            try
            {
                _selected.SetHideOriginalMesh(target);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Hide original mesh toggle failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Anchor block mesh visibility toggle failed.");
                return;
            }

            DecorationEditSnapshot[] after = CaptureSnapshots(affected);
            if (!AnySnapshotChanged(affected, before))
            {
                UpdateDirtyFromSelection();
                return;
            }

            for (int index = 0; index < affected.Length; index++)
                _transactions.TrackEdit(affected[index], before[index]);

            if (affected.Length == 1)
            {
                _history.Record(new DecorationSnapshotCommand(
                    target ? "Hide anchor mesh" : "Show anchor mesh",
                    _selectedConstruct,
                    affected[0],
                    before[0],
                    after[0]));
            }
            else
            {
                _history.Record(new DecorationSnapshotBatchCommand(
                    target ? "Hide anchor mesh" : "Show anchor mesh",
                    _selectedConstruct,
                    affected,
                    before,
                    after,
                    Array.IndexOf(affected, _selected)));
            }

            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add(target
                ? "Selected anchor block mesh hidden."
                : "Selected anchor block mesh shown.");
        }

        private bool IsSelectedOriginalMeshHidden()
        {
            if (_selected == null || _selected.IsDeleted)
                return false;

            Decoration[] affected = GetOriginalMeshVisibilityTargets(_selected);
            for (int index = 0; index < affected.Length; index++)
            {
                Decoration decoration = affected[index];
                if (decoration != null &&
                    !decoration.IsDeleted &&
                    decoration.HideOriginalMesh.Us)
                {
                    return true;
                }
            }

            return false;
        }

        private static DecorationEditSnapshot[] CaptureSnapshots(Decoration[] decorations)
        {
            if (decorations == null || decorations.Length == 0)
                return Array.Empty<DecorationEditSnapshot>();

            var snapshots = new DecorationEditSnapshot[decorations.Length];
            for (int index = 0; index < decorations.Length; index++)
                snapshots[index] = new DecorationEditSnapshot(decorations[index]);
            return snapshots;
        }

        private static bool AnySnapshotChanged(
            Decoration[] decorations,
            DecorationEditSnapshot[] before)
        {
            if (decorations == null || before == null || decorations.Length != before.Length)
                return false;

            for (int index = 0; index < decorations.Length; index++)
            {
                if (decorations[index] != null &&
                    before[index] != null &&
                    !before[index].Matches(decorations[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private Decoration[] GetOriginalMeshVisibilityTargets(Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted)
                return Array.Empty<Decoration>();

            var result = new List<Decoration>();
            var seen = new HashSet<Decoration>();
            AddDecoration(decoration);

            try
            {
                Vector3i[] blockPositions = null;
                if (decoration.GetBlock(out Block block))
                    blockPositions = block.LocalPositions;

                if (blockPositions == null || blockPositions.Length == 0)
                    blockPositions = new[] { decoration.TetherPoint.Us };

                for (int index = 0; index < blockPositions.Length; index++)
                {
                    if (!decoration.OurManager.GetDecorations(
                            blockPositions[index],
                            out List<Decoration> decorations))
                    {
                        continue;
                    }

                    foreach (Decoration related in decorations)
                        AddDecoration(related);
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not enumerate anchor block decorations for hide-original-mesh tracking",
                    exception,
                    LogOptions._AlertDevInGame);
            }

            return result.ToArray();

            void AddDecoration(Decoration candidate)
            {
                if (candidate == null || candidate.IsDeleted || !seen.Add(candidate))
                    return;
                result.Add(candidate);
            }
        }

        private void DrawBottomAnchorFollowToggle(Rect rect)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(
                        _anchorFollowDecoration ? "Anchor follow: on" : "Anchor follow: off",
                        "When enabled, moving a decoration retethers its anchor to the nearest valid block once the center is outside the follow range."),
                    DecorationEditorTheme.ToolButton(_anchorFollowDecoration)))
            {
                ToggleAnchorFollow();
            }
        }

        private void DrawBottomAnchorSettingsButton(Rect rect)
        {
            if (GUI.Button(
                    rect,
                    new GUIContent(
                        "Anchor settings",
                        "Open anchor follow range and preset settings."),
                    DecorationEditorTheme.ToolButton(_anchorMenuOpen)))
            {
                _anchorMenuOpen = !_anchorMenuOpen;
                _viewModeMenuOpen = false;
            }
        }

        private struct BottomHeaderLayout
        {
            internal BottomHeaderLayout(
                Rect title,
                Rect mode,
                Rect selectControls,
                Rect anchorFollow,
                Rect anchorSettings,
                Rect clean)
            {
                Title = title;
                Mode = mode;
                SelectControls = selectControls;
                AnchorFollow = anchorFollow;
                AnchorSettings = anchorSettings;
                Clean = clean;
            }

            internal Rect Title { get; }
            internal Rect Mode { get; }
            internal Rect SelectControls { get; }
            internal Rect AnchorFollow { get; }
            internal Rect AnchorSettings { get; }
            internal Rect Clean { get; }
        }

        private static void DrawCyanLine(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = DecorationEditorTheme.Cyan;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawBottomTransformEditors(Rect row)
        {
            float gap = EsuHudLayout.Scale(8f);
            Rect group = BottomTransformGroupRect(row);
            float width = Mathf.Max(1f, (group.width - gap * 2f) / 3f);
            float x = group.x;

            DrawBottomVectorEditor(
                new Rect(x, row.y, width, row.height),
                "Position",
                _positionText,
                ApplyPositionFromInspector);
            DrawBottomVectorEditor(
                new Rect(x + width + gap, row.y, width, row.height),
                "Rotation",
                _orientationText,
                ApplyOrientationFromInspector);
            DrawBottomVectorEditor(
                new Rect(x + (width + gap) * 2f, row.y, width, row.height),
                "Scale",
                _scaleText,
                ApplyScaleFromInspector);
        }

        private static Rect BottomTransformGroupRect(Rect row)
        {
            float edgePadding = EsuHudLayout.Scale(4f);
            float gap = EsuHudLayout.Scale(8f);
            float availableWidth = Mathf.Max(1f, row.width - edgePadding * 2f);
            float width = Mathf.Max(1f, Mathf.Min(EsuHudLayout.Scale(386f), (availableWidth - gap * 2f) / 3f));
            float totalWidth = width * 3f + gap * 2f;
            float x = row.xMax - edgePadding - totalWidth;
            if (x < row.x + edgePadding)
                x = row.x + edgePadding;
            return new Rect(x, row.y, totalWidth, row.height);
        }

        private void DrawBottomVectorEditor(
            Rect rect,
            string label,
            string[] values,
            Action<Vector3, bool> apply)
        {
            bool hasSelection = _selected != null && !_selected.IsDeleted;
            string previousX = values != null && values.Length > 0 ? values[0] : string.Empty;
            string previousY = values != null && values.Length > 1 ? values[1] : string.Empty;
            string previousZ = values != null && values.Length > 2 ? values[2] : string.Empty;
            float fieldHeight = Mathf.Min(EsuHudLayout.Scale(24f), rect.height);
            float verticalOffset = Mathf.Max(0f, (rect.height - fieldHeight) * 0.5f);
            float setWidth = EsuHudLayout.Scale(40f);
            float axisWidth = EsuHudLayout.Scale(18f);
            float gap = EsuHudLayout.Scale(4f);
            float labelWidth = Mathf.Min(EsuHudLayout.Scale(74f), rect.width * 0.22f);
            GUI.Label(
                new Rect(
                    rect.x,
                    rect.y + verticalOffset,
                    labelWidth,
                    fieldHeight),
                label,
                hasSelection ? DecorationEditorTheme.SubHeader : DecorationEditorTheme.DisabledButton);

            float x = rect.x + labelWidth + gap;
            float available = Mathf.Max(
                1f,
                rect.xMax - x - setWidth - gap * 7f - axisWidth * 3f);
            float fieldWidth = Mathf.Max(1f, available / 3f);
            DrawBottomVectorComponent(DecorationEditAxis.X, values, 0, rect.y, ref x, axisWidth, fieldWidth, fieldHeight, gap, hasSelection);
            DrawBottomVectorComponent(DecorationEditAxis.Y, values, 1, rect.y, ref x, axisWidth, fieldWidth, fieldHeight, gap, hasSelection);
            DrawBottomVectorComponent(DecorationEditAxis.Z, values, 2, rect.y, ref x, axisWidth, fieldWidth, fieldHeight, gap, hasSelection);
            if (hasSelection &&
                VectorTextChanged(values, previousX, previousY, previousZ) &&
                TryParseVector(values, out Vector3 liveParsed))
            {
                apply(liveParsed, false);
            }

            float setX = Mathf.Min(x + gap, rect.xMax - setWidth);
            if (GUI.Button(
                    new Rect(
                        setX,
                        rect.y + verticalOffset,
                        setWidth,
                        fieldHeight),
                    new GUIContent("Set", "Apply the typed " + label.ToLowerInvariant() + " values."),
                    hasSelection ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton) &&
                hasSelection)
            {
                if (TryParseVector(values, out Vector3 parsed))
                    apply(parsed, true);
                else
                    InfoStore.Add($"{label} contains incomplete, invalid, NaN, or infinity input.");
            }
        }

        private static bool VectorTextChanged(string[] values, string previousX, string previousY, string previousZ)
        {
            return values != null &&
                   values.Length >= 3 &&
                   (!string.Equals(previousX, values[0], StringComparison.Ordinal) ||
                    !string.Equals(previousY, values[1], StringComparison.Ordinal) ||
                    !string.Equals(previousZ, values[2], StringComparison.Ordinal));
        }

        private static void DrawBottomVectorComponent(
            DecorationEditAxis axis,
            string[] values,
            int index,
            float y,
            ref float x,
            float axisWidth,
            float fieldWidth,
            float fieldHeight,
            float gap,
            bool hasSelection)
        {
            float centeredY = y + Mathf.Max(0f, (EsuHudLayout.Scale(30f) - fieldHeight) * 0.5f);
            GUI.Label(
                new Rect(x, centeredY, axisWidth, fieldHeight),
                axis.ToString(),
                BottomVectorAxisStyle(axis));
            x += axisWidth + gap;
            Rect fieldRect = new Rect(x, centeredY, fieldWidth, fieldHeight);
            if (hasSelection)
            {
                values[index] = GUI.TextField(
                    fieldRect,
                    values[index] ?? string.Empty,
                    DecorationEditorTheme.TextField);
                EsuCursorTooltip.Register(fieldRect, "Edit the " + axis + " component.");
            }
            else
            {
                GUI.Label(fieldRect, values[index] ?? string.Empty, DecorationEditorTheme.TextField);
            }
            x += fieldWidth + gap;
        }

        private static GUIStyle BottomVectorAxisStyle(DecorationEditAxis axis)
        {
            return new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold,
                normal = { textColor = DecorationEditMath.AxisColor(axis) }
            };
        }

        private string StatusLine()
        {
            string selected = _selected == null || _selected.IsDeleted
                ? "none"
                : MeshName(_selected.MeshGuid.Us);
            string format = _forecast == null
                ? "--"
                : SerializationHudRenderer.FormatName(_forecast.Format) +
                  (_forecast.Exact ? " exact" : " estimated");
            string counts = _lastUsage == null
                ? "--"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:N0} decorations | manager max {1:N0}/100k",
                    _lastUsage.TotalDecorations,
                    _lastUsage.PeakManagerDecorations);
            string placing = _placingMesh == null ? string.Empty : $" | Placing: {_placingMesh.Name}";
            string surface = _surfaceDraft.HasDraft
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    " | Surface: {0}p/{1}f/{2}",
                    _surfaceDraft.Points.Count,
                    _surfaceDraft.Faces.Count,
                    _surfacePlan == null ? "no preview" : _surfacePlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) + " deco")
                : string.Empty;
            string generator = _generatorDraft.HasDraft
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    " | Generator: {0}/{1}",
                    _surfaceExtraTool,
                    _generatorPlan == null ? "no preview" : _generatorPlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) + " deco")
                : string.Empty;
            return $"View: {ViewModeDisplayName(_viewMode)} | Tool: {_tool}{placing}{surface}{generator} | Selected: {selected} | Dirty: {(_dirty ? "yes" : "no")} | Undo {_history.UndoCount}/Redo {_history.RedoCount} | Save: {format} | {counts}";
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
                    return "Deco only";
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

        private void DrawMeshPreviewCard()
        {
            if (_hoveredMesh == null)
                return;

            float cardWidth = EsuHudLayout.Scale(348f);
            float cardHeight = EsuHudLayout.Scale(160f);
            Rect rect = new Rect(
                Mathf.Clamp(
                    Event.current.mousePosition.x + EsuHudLayout.Scale(24f),
                    EsuHudLayout.Scale(8f),
                    Screen.width - cardWidth - EsuHudLayout.Scale(8f)),
                Mathf.Clamp(
                    Event.current.mousePosition.y + EsuHudLayout.Scale(24f),
                    EsuHudLayout.Scale(8f),
                    Screen.height - cardHeight - EsuHudLayout.Scale(8f)),
                cardWidth,
                cardHeight);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect previewRect = new Rect(
                rect.x + EsuHudLayout.Scale(10f),
                rect.y + EsuHudLayout.Scale(34f),
                EsuHudLayout.Scale(112f),
                EsuHudLayout.Scale(112f));
            Texture preview = _previewRenderer?.GetPreview(_hoveredMesh, 128, _previewSpin);
            if (preview != null)
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
                new GUIContent(" Mesh preview", DecorationEditorIconCatalog.Get("mesh")),
                DecorationEditorTheme.SubHeader);
            float textX = EsuHudLayout.Scale(132f);
            float textWidth = rect.width - textX - EsuHudLayout.Scale(10f);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(38f), textWidth, EsuHudLayout.Scale(24f)),
                $"[{_hoveredMesh.Kind}] {_hoveredMesh.Name}",
                DecorationEditorTheme.Body);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(64f), textWidth, EsuHudLayout.Scale(38f)),
                _hoveredMesh.Guid.ToString("D"),
                DecorationEditorTheme.Mini);
            GUI.Label(
                new Rect(textX, EsuHudLayout.Scale(106f), textWidth, EsuHudLayout.Scale(42f)),
                string.IsNullOrEmpty(_hoveredMeshHint)
                    ? "Click: place on pointer. Set: assign to selected."
                    : _hoveredMeshHint,
                DecorationEditorTheme.Mini);
            GUI.EndGroup();
        }

        /*
                string prefix = ReferenceEquals(entry, _selectedMesh) ? "● " : string.Empty;
        */
        private void HandleSceneInput()
        {
            if (IsMouseOverEditorUi(_lastMouseGui))
                return;

            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                    DecoLimitLifter.EsuSymmetry.CancelPending();
                    InfoStore.Add("Symmetry plane placement cancelled.");
                    return;
                }

                if (Input.GetMouseButtonDown(0))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                    PlacePendingSymmetryPlane();
                    return;
                }

                return;
            }

            if (_placingMesh != null)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                    CancelPlacement();
                    return;
                }

                if (Input.GetMouseButtonDown(0))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                    PlaceSelectedMeshAtPointer();
                }
                return;
            }

            if (_surfaceDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!Input.GetMouseButton(0))
                {
                    RecordSurfaceEdit("Move surface point", _surfaceDragSnapshotStart);
                    _surfaceDragSnapshotStart = null;
                    _surfaceDragAxis = DecorationEditAxis.None;
                    RebuildSurfacePreview(showMessage: false);
                    return;
                }

                TryUpdateSurfaceDrag(_lastMouseGui - _surfaceDragMouseStart);
                return;
            }

            if (_sharedAnchorDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!Input.GetMouseButton(0))
                {
                    CommitSharedAnchorDrag();
                    return;
                }

                TryUpdateSharedAnchorDrag(_lastMouseGui - _sharedAnchorDragMouseStart);
                return;
            }

            if (_generatorDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!Input.GetMouseButton(0))
                {
                    RecordGeneratorEdit("Move generator point", _generatorDragSnapshotStart);
                    _generatorDragSnapshotStart = null;
                    _generatorDragAxis = DecorationEditAxis.None;
                    RebuildGeneratorPreview(showMessage: false);
                    return;
                }

                TryUpdateGeneratorDrag(_lastMouseGui - _generatorDragMouseStart);
                return;
            }

            if (_generatorRotateDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!Input.GetMouseButton(0))
                {
                    RecordGeneratorEdit("Transform generator shape", _generatorRotateSnapshotStart ?? _generatorScaleSnapshotStart);
                    _generatorRotateSnapshotStart = null;
                    _generatorScaleSnapshotStart = null;
                    _generatorRotateDragAxis = DecorationEditAxis.None;
                    RebuildGeneratorPreview(showMessage: false);
                    return;
                }

                if (_surfaceBuilderTool == SurfaceBuilderTool.Scale)
                    TryUpdateGeneratorScale(_lastMouseGui - _generatorRotateDragMouseStart);
                else
                    TryUpdateGeneratorRotate();
                return;
            }

            if (_tool == DecorationEditorTool.Surface)
            {
                if (UseGeneratorSceneInput())
                    HandleGeneratorSceneInput();
                else
                    HandleSurfaceSceneInput();
                return;
            }

            if (_dragAxis != DecorationEditAxis.None)
            {
                if (!Input.GetMouseButton(0))
                {
                    CommitDragEdit();
                    _dragAxis = DecorationEditAxis.None;
                    return;
                }

                TryUpdateDrag(_lastMouseGui - _dragMouseStart);
                return;
            }

            if (_rotateDragAxis != DecorationEditAxis.None)
            {
                if (!Input.GetMouseButton(0))
                {
                    CommitRotateEdit();
                    _rotateDragAxis = DecorationEditAxis.None;
                    return;
                }

                TryUpdateRotate(_lastMouseGui - _rotateDragMouseStart);
                return;
            }

            if (_scaleDragAxis != DecorationEditAxis.None)
            {
                if (!Input.GetMouseButton(0))
                {
                    CommitScaleEdit();
                    _scaleDragAxis = DecorationEditAxis.None;
                    return;
                }

                TryUpdateScale(_lastMouseGui - _scaleDragMouseStart);
                return;
            }

            if (_anchorDragAxis != DecorationEditAxis.None)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!Input.GetMouseButton(0))
                {
                    CommitAnchorDrag();
                    _anchorDragAxis = DecorationEditAxis.None;
                    _anchorPreviewValid = false;
                    return;
                }

                TryUpdateAnchorDrag(_lastMouseGui - _anchorDragMouseStart);
                return;
            }

            if (_boxSelecting)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (Input.GetMouseButton(0))
                {
                    UpdateBoxSelectionDrag(_lastMouseGui);
                    return;
                }

                CommitBoxSelectionDrag(_lastMouseGui);
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (!TryOpenDecorationContextMenu())
                    InfoStore.Add("No decoration center near the cursor.");
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            if (_tool == DecorationEditorTool.Select &&
                _selectionMode == DecorationSelectionMode.Box)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                BeginBoxSelection(_lastMouseGui);
                return;
            }

            if (_tool == DecorationEditorTool.Anchor &&
                _selected != null && !_selected.IsDeleted &&
                TryPickAnchorHandle(_selected, _selectedConstruct, _lastMouseGui, out DecorationEditAxis anchorAxis, out int sign))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _anchorDragAxis = anchorAxis;
                _anchorDragSign = sign;
                _anchorDragMouseStart = _lastMouseGui;
                _anchorDragBaseTether = _selected.TetherPoint.Us;
                _anchorDragBasePosition = _selected.Positioning.Us;
                _anchorDragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _anchorDragSnapshotStart);
                _anchorDragSymmetryFollow = BeginSymmetryFollow(_anchorDragSnapshotStart, reportSkipped: true);
                TryUpdateAnchorDrag(Vector2.zero);
                return;
            }

            if (_tool == DecorationEditorTool.Rotate &&
                TryGetMultiSelectionPivot(out Vector3 multiRotatePivot) &&
                TryPickGroupRotateRing(multiRotatePivot, _lastMouseGui, out DecorationEditAxis multiRotateAxis) &&
                TryCaptureMultiTransformStart(out multiRotatePivot))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _rotateDragAxis = multiRotateAxis;
                _rotateDragMouseStart = _lastMouseGui;
                TryProject(_selectedConstruct, multiRotatePivot, out _rotateDragCenterScreen);
                _rotateDragStartVector = _lastMouseGui - _rotateDragCenterScreen;
                return;
            }

            if (_tool == DecorationEditorTool.Rotate &&
                _selected != null && !_selected.IsDeleted &&
                TryPickRotateRing(_selected, _selectedConstruct, _lastMouseGui, out DecorationEditAxis rotateAxis))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _rotateDragAxis = rotateAxis;
                _rotateDragMouseStart = _lastMouseGui;
                TryProject(_selectedConstruct, GetDecorationLocalCenter(_selected), out _rotateDragCenterScreen);
                _rotateDragStartVector = _lastMouseGui - _rotateDragCenterScreen;
                _rotateStart = _selected.Orientation.Us;
                _rotateStartQuaternion = Quaternion.Euler(_rotateStart);
                _rotateDragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _rotateDragSnapshotStart);
                return;
            }

            if (_tool == DecorationEditorTool.Scale &&
                TryGetMultiSelectionPivot(out Vector3 multiScalePivot) &&
                TryPickGroupHandle(multiScalePivot, _lastMouseGui, out DecorationEditAxis multiScaleAxis) &&
                TryCaptureMultiTransformStart(out multiScalePivot))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _scaleDragAxis = multiScaleAxis;
                _scaleDragMouseStart = _lastMouseGui;
                return;
            }

            if (_tool == DecorationEditorTool.Scale &&
                _selected != null && !_selected.IsDeleted &&
                TryPickHandle(_selected, _selectedConstruct, _lastMouseGui, out DecorationEditAxis scaleAxis))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _scaleDragAxis = scaleAxis;
                _scaleDragMouseStart = _lastMouseGui;
                _scaleStart = _selected.Scaling.Us;
                _scaleDragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _scaleDragSnapshotStart);
                return;
            }

            if (_tool == DecorationEditorTool.Move &&
                TryGetMultiSelectionPivot(out Vector3 multiMovePivot) &&
                TryPickGroupMoveHandle(multiMovePivot, _lastMouseGui, out DecorationEditAxis multiMoveAxis) &&
                TryCaptureMultiTransformStart(out multiMovePivot))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _dragAxis = multiMoveAxis;
                _dragMouseStart = _lastMouseGui;
                if (multiMoveAxis == DecorationEditAxis.Free)
                    PrepareFreeDragFrameAt(_selectedConstruct, multiMovePivot);
                return;
            }

            if (_tool == DecorationEditorTool.Move &&
                _selected != null && !_selected.IsDeleted &&
                TryPickMoveHandle(_selected, _selectedConstruct, _lastMouseGui, out DecorationEditAxis axis))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _dragAxis = axis;
                _dragMouseStart = _lastMouseGui;
                _dragPositionStart = _selected.Positioning.Us;
                if (axis == DecorationEditAxis.Free)
                    PrepareFreeDragFrame(_selected, _selectedConstruct);
                _dragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _dragSnapshotStart);
                _dragSymmetryFollow = BeginSymmetryFollow(_dragSnapshotStart, reportSkipped: true);
                return;
            }

            if (_tool == DecorationEditorTool.Paint)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                PaintNearest(_lastMouseGui);
                return;
            }

            if (_tool != DecorationEditorTool.Select &&
                _tool != DecorationEditorTool.Move &&
                _tool != DecorationEditorTool.Rotate &&
                _tool != DecorationEditorTool.Scale &&
                _tool != DecorationEditorTool.Anchor)
                return;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            SelectNearest(_lastMouseGui);
        }

        private void TryUpdateDrag(Vector2 mouseDelta)
        {
            if (MultiTransformActive)
            {
                if (_dragAxis == DecorationEditAxis.Free)
                {
                    Vector3 freeDelta = _freeDragCameraRight * (mouseDelta.x * _freeDragMetresPerPixel) -
                                        _freeDragCameraUp * (mouseDelta.y * _freeDragMetresPerPixel);
                    TryApplyMultiMove(DecorationEditMath.Snap(freeDelta, DecorationMoveSnap));
                    return;
                }

                if (!TryProject(_selectedConstruct, _multiTransformPivotStart, out Vector2 multiOrigin))
                    return;

                Vector3 multiAxisVector = GroupTransformAxisVector(_dragAxis);
                if (!TryProject(_selectedConstruct, _multiTransformPivotStart + multiAxisVector * HandleLength, out Vector2 multiAxisEnd))
                    return;

                float multiAxisDelta = DecorationEditMath.ProjectMouseDeltaToAxis(
                    mouseDelta,
                    multiOrigin,
                    multiAxisEnd,
                    HandleLength);
                multiAxisDelta = DecorationEditMath.Snap(multiAxisDelta, DecorationMoveSnap);
                TryApplyMultiMove(multiAxisVector * multiAxisDelta);
                return;
            }

            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (_dragAxis == DecorationEditAxis.Free)
            {
                Vector3 freeDelta = _freeDragCameraRight * (mouseDelta.x * _freeDragMetresPerPixel) -
                                    _freeDragCameraUp * (mouseDelta.y * _freeDragMetresPerPixel);
                Vector3 freeNext = DecorationEditMath.Snap(_dragPositionStart + freeDelta, DecorationMoveSnap);
                if (!DecorationEditMath.IsWithinPositionLimit(freeNext))
                    return;

                DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
                DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(_dragSymmetryFollow);
                Vector3 dragPositionRollback = _dragPositionStart;
                _selected.Positioning.Us = freeNext;
                _selected.Changed();
                _dirty = true;
                if (TryAutoFollowAnchor(out Vector3i freeShift))
                    _dragPositionStart -= ToVector3(freeShift);
                if (!TryApplySymmetryFollow(_dragSymmetryFollow, reportInvalid: true))
                {
                    RestoreSymmetryEditState(selectedRollback, _dragSymmetryFollow, targetRollback);
                    _dragPositionStart = dragPositionRollback;
                    return;
                }
                return;
            }

            if (!TryProject(_selectedConstruct, GetDecorationLocalCenter(_selected), out Vector2 origin))
                return;

            Vector3 axisVector = ActiveTransformAxisVector(_dragAxis);
            Vector3 endLocal = GetDecorationLocalCenter(_selected) + axisVector * HandleLength;
            if (!TryProject(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;

            float axisDelta = DecorationEditMath.ProjectMouseDeltaToAxis(
                mouseDelta,
                origin,
                axisEnd,
                HandleLength);
            axisDelta = DecorationEditMath.Snap(axisDelta, DecorationMoveSnap);
            Vector3 axisNext = _dragPositionStart + axisVector * axisDelta;
            if (!DecorationEditMath.IsWithinPositionLimit(axisNext))
                return;

            DecorationEditSnapshot selectedAxisRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetAxisRollback = CaptureSymmetryTargetSnapshots(_dragSymmetryFollow);
            Vector3 axisDragPositionRollback = _dragPositionStart;
            _selected.Positioning.Us = axisNext;
            _selected.Changed();
            _dirty = true;
            if (TryAutoFollowAnchor(out Vector3i axisShift))
                _dragPositionStart -= ToVector3(axisShift);
            if (!TryApplySymmetryFollow(_dragSymmetryFollow, reportInvalid: true))
            {
                RestoreSymmetryEditState(selectedAxisRollback, _dragSymmetryFollow, targetAxisRollback);
                _dragPositionStart = axisDragPositionRollback;
                return;
            }
        }

        private void CommitDragEdit()
        {
            if (MultiTransformActive)
            {
                RecordMultiTransformEdit("Move decorations");
                return;
            }

            DecorationEditSnapshot before = _dragSnapshotStart;
            _dragSnapshotStart = null;
            SymmetryFollowContext symmetryFollow = _dragSymmetryFollow;
            _dragSymmetryFollow = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Move decoration", before, symmetryFollow);
        }

        private void TryUpdateRotate(Vector2 mouseDelta)
        {
            if (MultiTransformActive)
            {
                Vector2 multiCurrent = _lastMouseGui - _rotateDragCenterScreen;
                if (_rotateDragStartVector.sqrMagnitude < 16f || multiCurrent.sqrMagnitude < 16f)
                    return;

                float multiDegrees = DecorationEditMath.Snap(
                    Vector2.SignedAngle(_rotateDragStartVector, multiCurrent),
                    DecorationRotateSnapDegrees);
                TryApplyMultiRotate(_rotateDragAxis, multiDegrees);
                return;
            }

            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            Vector2 current = _lastMouseGui - _rotateDragCenterScreen;
            if (_rotateDragStartVector.sqrMagnitude < 16f || current.sqrMagnitude < 16f)
                return;

            Vector3 axisVector = DecorationEditMath.AxisVector(_rotateDragAxis);
            float degrees = DecorationEditMath.Snap(
                Vector2.SignedAngle(_rotateDragStartVector, current),
                DecorationRotateSnapDegrees);
            Vector3 next = _transformOrientation == DecorationTransformOrientation.Local
                ? (_rotateStartQuaternion * Quaternion.AngleAxis(degrees, axisVector)).eulerAngles
                : _rotateStart + axisVector * degrees;
            if (!DecorationEditMath.IsFinite(next))
                return;

            _selected.Orientation.Us = next;
            _selected.Changed();
            _dirty = true;
        }

        private void CommitRotateEdit()
        {
            if (MultiTransformActive)
            {
                RecordMultiTransformEdit("Rotate decorations");
                return;
            }

            DecorationEditSnapshot before = _rotateDragSnapshotStart;
            _rotateDragSnapshotStart = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Rotate decoration", before);
        }

        private void TryUpdateScale(Vector2 mouseDelta)
        {
            if (MultiTransformActive)
            {
                if (!TryProject(_selectedConstruct, _multiTransformPivotStart, out Vector2 multiOrigin))
                    return;

                Vector3 multiAxis = GroupTransformAxisVector(_scaleDragAxis);
                if (!TryProject(_selectedConstruct, _multiTransformPivotStart + multiAxis * HandleLength, out Vector2 multiAxisEnd))
                    return;

                float multiDelta = DecorationEditMath.ProjectMouseDeltaToAxis(
                    mouseDelta,
                    multiOrigin,
                    multiAxisEnd,
                    HandleLength);
                float factor = Mathf.Max(
                    MinimumScale,
                    DecorationEditMath.Snap(1f + multiDelta, DecorationScaleSnap));
                TryApplyMultiScale(_scaleDragAxis, factor);
                return;
            }

            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (!TryProject(_selectedConstruct, GetDecorationLocalCenter(_selected), out Vector2 origin))
                return;

            Vector3 displayAxis = ActiveTransformAxisVector(_scaleDragAxis);
            Vector3 endLocal = GetDecorationLocalCenter(_selected) + displayAxis * HandleLength;
            if (!TryProject(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;

            float delta = DecorationEditMath.ProjectMouseDeltaToAxis(
                mouseDelta,
                origin,
                axisEnd,
                HandleLength);
            delta = DecorationEditMath.Snap(delta, DecorationScaleSnap);
            Vector3 next = _scaleStart + DecorationEditMath.AxisVector(_scaleDragAxis) * delta;
            if (!IsValidScale(next))
                return;

            DecorationScaleBounds.AllowExtendedScale(_selected);
            _selected.Scaling.Us = next;
            _selected.Changed();
            _dirty = true;
        }

        private void CommitScaleEdit()
        {
            if (MultiTransformActive)
            {
                RecordMultiTransformEdit("Scale decorations");
                return;
            }

            DecorationEditSnapshot before = _scaleDragSnapshotStart;
            _scaleDragSnapshotStart = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Scale decoration", before);
        }

        private void TryUpdateAnchorDrag(Vector2 mouseDelta)
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (!TryProject(_selectedConstruct, ToVector3(_anchorDragBaseTether), out Vector2 origin))
                return;

            Vector3 axisVector = DecorationEditMath.AxisVector(_anchorDragAxis) * _anchorDragSign;
            Vector3 endLocal = ToVector3(_anchorDragBaseTether) + axisVector * HandleLength;
            if (!TryProject(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;

            float projected = DecorationEditMath.ProjectMouseDeltaToAxis(
                mouseDelta,
                origin,
                axisEnd,
                HandleLength);
            int magnitude = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(projected)));
            Vector3i shift = AxisShift(_anchorDragAxis, _anchorDragSign * magnitude);
            _anchorPreviewValid = TryPreviewAnchorShift(
                shift,
                out _anchorPreviewTether,
                out _anchorPreviewPosition);
        }

        private void CommitAnchorDrag()
        {
            DecorationEditSnapshot before = _anchorDragSnapshotStart;
            _anchorDragSnapshotStart = null;
            SymmetryFollowContext symmetryFollow = _anchorDragSymmetryFollow;
            _anchorDragSymmetryFollow = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            if (!_anchorPreviewValid)
            {
                InfoStore.Add("Anchor move rejected: point at a valid tether block within +/-10 positioning.");
                _selected.TetherPoint.Us = _anchorDragBaseTether;
                _selected.Positioning.Us = _anchorDragBasePosition;
                _selected.Changed();
                return;
            }

            Vector3i current = _selected.TetherPoint.Us;
            Vector3i shift = new Vector3i(
                _anchorPreviewTether.x - current.x,
                _anchorPreviewTether.y - current.y,
                _anchorPreviewTether.z - current.z);
            ApplyAnchorShift(shift, before, symmetryFollow);
        }

        private void RecordSnapshotEdit(
            string label,
            DecorationEditSnapshot before,
            SymmetryFollowContext symmetryFollow = null)
        {
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            if (before.Matches(_selected))
            {
                UpdateDirtyFromSelection();
                return;
            }

            if (symmetryFollow != null && symmetryFollow.IsActive)
            {
                RecordSymmetrySnapshotEdit(label, before, symmetryFollow);
                return;
            }

            var after = new DecorationEditSnapshot(_selected);
            _transactions.TrackEdit(_selected, before);
            _history.Record(new DecorationSnapshotCommand(
                label,
                _selectedConstruct,
                _selected,
                before,
                after));
            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
        }

        private void RecordSymmetrySnapshotEdit(
            string label,
            DecorationEditSnapshot selectedBefore,
            SymmetryFollowContext symmetryFollow)
        {
            if (selectedBefore == null ||
                symmetryFollow == null ||
                !symmetryFollow.IsActive ||
                _selected == null ||
                _selected.IsDeleted)
            {
                return;
            }

            var decorations = new List<Decoration>(symmetryFollow.Targets.Length + 1)
            {
                _selected
            };
            var before = new List<DecorationEditSnapshot>(symmetryFollow.Targets.Length + 1)
            {
                selectedBefore
            };
            var after = new List<DecorationEditSnapshot>(symmetryFollow.Targets.Length + 1)
            {
                new DecorationEditSnapshot(_selected)
            };

            _transactions.TrackEdit(_selected, selectedBefore);
            for (int index = 0; index < symmetryFollow.Targets.Length; index++)
            {
                SymmetryFollowTarget target = symmetryFollow.Targets[index];
                if (target.Decoration == null || target.Decoration.IsDeleted)
                {
                    InfoStore.Add("Symmetry follow ended because a mirrored decoration no longer exists.");
                    continue;
                }

                decorations.Add(target.Decoration);
                before.Add(target.Before);
                after.Add(new DecorationEditSnapshot(target.Decoration));
                _transactions.TrackEdit(target.Decoration, target.Before);
            }

            if (decorations.Count <= 1)
            {
                _history.Record(new DecorationSnapshotCommand(
                    label,
                    _selectedConstruct,
                    _selected,
                    selectedBefore,
                    after[0]));
            }
            else
            {
                _history.Record(new DecorationSnapshotBatchCommand(
                    label,
                    _selectedConstruct,
                    decorations.ToArray(),
                    before.ToArray(),
                    after.ToArray(),
                    primaryIndex: 0));
            }

            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
        }

        private void UpdateDirtyFromSelection()
        {
            if (_selected == null || _selected.IsDeleted)
            {
                _dirty = _transactions.HasChanges || _deletedDecorations.Count > 0;
                return;
            }

            _dirty = _transactions.HasChanges || _deletedDecorations.Count > 0;
        }

        private Vector3 ActiveTransformAxisVector(DecorationEditAxis axis) =>
            ActiveTransformAxisVector(_selected, axis);

        private Vector3 GroupTransformAxisVector(DecorationEditAxis axis)
        {
            Vector3 baseAxis = DecorationEditMath.AxisVector(axis);
            if (_transformOrientation != DecorationTransformOrientation.Local ||
                _selected == null ||
                _selected.IsDeleted ||
                baseAxis.sqrMagnitude < 0.0001f)
            {
                return baseAxis;
            }

            Vector3 oriented = Quaternion.Euler(_selected.Orientation.Us) * baseAxis;
            return oriented.sqrMagnitude > 0.0001f && DecorationEditMath.IsFinite(oriented)
                ? oriented.normalized
                : baseAxis;
        }

        private Vector3 ActiveTransformAxisVector(Decoration decoration, DecorationEditAxis axis)
        {
            Vector3 baseAxis = DecorationEditMath.AxisVector(axis);
            if (_transformOrientation != DecorationTransformOrientation.Local ||
                decoration == null ||
                decoration.IsDeleted ||
                baseAxis.sqrMagnitude < 0.0001f)
            {
                return baseAxis;
            }

            Vector3 oriented = Quaternion.Euler(decoration.Orientation.Us) * baseAxis;
            return oriented.sqrMagnitude > 0.0001f && DecorationEditMath.IsFinite(oriented)
                ? oriented.normalized
                : baseAxis;
        }

        private bool MultiTransformActive => _multiTransformDecorations.Length > 1;

        private List<Decoration> CurrentPrimarySelectionDecorations()
        {
            var decorations = new List<Decoration>();
            if (_selectedConstruct == null)
                return decorations;

            if (_selected != null &&
                !_selected.IsDeleted &&
                _selection.Contains(_selected))
            {
                decorations.Add(_selected);
            }

            foreach (Decoration decoration in _selection)
            {
                if (decoration == null ||
                    decoration.IsDeleted ||
                    ReferenceEquals(decoration, _selected) ||
                    decorations.Contains(decoration))
                {
                    continue;
                }

                decorations.Add(decoration);
            }

            return decorations;
        }

        private bool TryGetMultiSelectionPivot(out Vector3 pivot)
        {
            pivot = Vector3.zero;
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
                return false;

            Vector3 sum = Vector3.zero;
            for (int index = 0; index < decorations.Count; index++)
            {
                Vector3 center = GetDecorationLocalCenter(decorations[index]);
                if (!DecorationEditMath.IsFinite(center))
                    return false;
                sum += center;
            }

            pivot = sum / decorations.Count;
            return DecorationEditMath.IsFinite(pivot);
        }

        private bool TryCaptureMultiTransformStart(out Vector3 pivot)
        {
            pivot = Vector3.zero;
            ClearMultiTransformState();

            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
                return false;

            var before = new DecorationEditSnapshot[decorations.Count];
            var centers = new Vector3[decorations.Count];
            Vector3 sum = Vector3.zero;
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                before[index] = new DecorationEditSnapshot(decoration);
                centers[index] = ToVector3(before[index].TetherPoint) + before[index].Positioning;
                if (!DecorationEditMath.IsFinite(centers[index]))
                    return false;
                sum += centers[index];
            }

            pivot = sum / decorations.Count;
            if (!DecorationEditMath.IsFinite(pivot))
                return false;

            _multiTransformDecorations = decorations.ToArray();
            _multiTransformBefore = before;
            _multiTransformStartCenters = centers;
            _multiTransformPivotStart = pivot;
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
                _transactions.TrackEdit(_multiTransformDecorations[index], _multiTransformBefore[index]);
            return true;
        }

        private void ClearMultiTransformState()
        {
            _multiTransformDecorations = Array.Empty<Decoration>();
            _multiTransformBefore = Array.Empty<DecorationEditSnapshot>();
            _multiTransformStartCenters = Array.Empty<Vector3>();
            _multiTransformPivotStart = Vector3.zero;
        }

        private bool TryPositioningFromCenter(
            DecorationEditSnapshot snapshot,
            Vector3 center,
            out Vector3 positioning)
        {
            positioning = Vector3.zero;
            if (snapshot == null || !DecorationEditMath.IsFinite(center))
                return false;

            positioning = center - ToVector3(snapshot.TetherPoint);
            return DecorationEditMath.IsFinite(positioning) &&
                   DecorationEditMath.IsWithinPositionLimit(positioning);
        }

        private bool TryResolveMultiTransformPlacement(
            DecorationEditSnapshot snapshot,
            Vector3 center,
            Vector3 orientation,
            Vector3 scale,
            out MultiTransformPlacement placement)
        {
            placement = null;
            if (snapshot == null ||
                _selectedConstruct == null ||
                !DecorationEditMath.IsFinite(center) ||
                !DecorationEditMath.IsFinite(orientation) ||
                !IsValidScale(scale))
            {
                return false;
            }

            Vector3 originalPositioning = center - ToVector3(snapshot.TetherPoint);
            bool originalPositionValid =
                DecorationEditMath.IsFinite(originalPositioning) &&
                DecorationEditMath.IsWithinPositionLimit(originalPositioning);
            float threshold = Mathf.Max(AnchorFollowMinimumDistance, _anchorFollowDistance);
            bool shouldFollow =
                _anchorFollowDecoration &&
                (center - ToVector3(snapshot.TetherPoint)).sqrMagnitude >= threshold * threshold;
            if (!shouldFollow && originalPositionValid)
            {
                placement = new MultiTransformPlacement(
                    snapshot.TetherPoint,
                    originalPositioning,
                    orientation,
                    scale);
                return true;
            }

            if (_anchorFollowDecoration &&
                TryFindAnchorFollowTarget(_selectedConstruct, center, snapshot.TetherPoint, out Vector3i target))
            {
                Vector3 followedPositioning = DecorationEditMath.Snap(center - ToVector3(target));
                if (DecorationEditMath.IsFinite(followedPositioning) &&
                    DecorationEditMath.IsWithinPositionLimit(followedPositioning))
                {
                    placement = new MultiTransformPlacement(
                        target,
                        followedPositioning,
                        orientation,
                        scale);
                    return true;
                }
            }

            if (originalPositionValid)
            {
                placement = new MultiTransformPlacement(
                    snapshot.TetherPoint,
                    originalPositioning,
                    orientation,
                    scale);
                return true;
            }

            return false;
        }

        private bool TryApplyMultiTransformPlacements(MultiTransformPlacement[] placements)
        {
            if (!MultiTransformActive ||
                placements == null ||
                placements.Length != _multiTransformDecorations.Length)
            {
                return false;
            }

            var rollback = new DecorationEditSnapshot[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                Decoration decoration = _multiTransformDecorations[index];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    placements[index] == null)
                {
                    return false;
                }

                rollback[index] = new DecorationEditSnapshot(decoration);
            }

            bool shiftedAny = false;
            try
            {
                for (int index = 0; index < _multiTransformDecorations.Length; index++)
                {
                    Decoration decoration = _multiTransformDecorations[index];
                    MultiTransformPlacement placement = placements[index];
                    Vector3i current = decoration.TetherPoint.Us;
                    if (!SameTether(current, placement.TetherPoint))
                    {
                        if (decoration.OurManager == null)
                        {
                            RestoreMultiTransformFrame(rollback);
                            return false;
                        }

                        var shift = new Vector3i(
                            placement.TetherPoint.x - current.x,
                            placement.TetherPoint.y - current.y,
                            placement.TetherPoint.z - current.z);
                        if (!decoration.OurManager.ShiftDecoration(decoration, shift))
                        {
                            RestoreMultiTransformFrame(rollback);
                            return false;
                        }

                        shiftedAny = true;
                    }

                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Orientation.Us = placement.Orientation;
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Changed();
                }
            }
            catch (Exception exception)
            {
                RestoreMultiTransformFrame(rollback);
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Multi-selection transform retether failed",
                    exception,
                    LogOptions._AlertDevInGame);
                return false;
            }

            _dirty = true;
            if (shiftedAny)
                RefreshDecorationCache(force: true);
            return true;
        }

        private void RestoreMultiTransformFrame(DecorationEditSnapshot[] rollback)
        {
            if (rollback == null)
                return;

            for (int index = 0; index < rollback.Length && index < _multiTransformDecorations.Length; index++)
            {
                Decoration decoration = _multiTransformDecorations[index];
                rollback[index]?.TryRestore(decoration);
            }

            RefreshDecorationCache(force: true);
        }

        private bool TryApplyMultiMove(Vector3 delta)
        {
            if (!MultiTransformActive || !DecorationEditMath.IsFinite(delta))
                return false;

            var placements = new MultiTransformPlacement[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                if (!TryResolveMultiTransformPlacement(
                        _multiTransformBefore[index],
                        _multiTransformStartCenters[index] + delta,
                        _multiTransformBefore[index].Orientation,
                        _multiTransformBefore[index].Scaling,
                        out placements[index]))
                {
                    return false;
                }
            }

            return TryApplyMultiTransformPlacements(placements);
        }

        private bool TryApplyMultiRotate(DecorationEditAxis axis, float degrees)
        {
            if (!MultiTransformActive)
                return false;

            Vector3 axisVector = GroupTransformAxisVector(axis);
            if (axisVector.sqrMagnitude < 0.0001f || !DecorationEditMath.IsFinite(axisVector))
                return false;

            Quaternion rotation = Quaternion.AngleAxis(degrees, axisVector.normalized);
            var placements = new MultiTransformPlacement[_multiTransformDecorations.Length];
            var orientations = new Vector3[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                Vector3 center = _multiTransformPivotStart +
                                 rotation * (_multiTransformStartCenters[index] - _multiTransformPivotStart);
                orientations[index] = (rotation * Quaternion.Euler(_multiTransformBefore[index].Orientation)).eulerAngles;
                if (!DecorationEditMath.IsFinite(orientations[index]))
                    return false;

                if (!TryResolveMultiTransformPlacement(
                        _multiTransformBefore[index],
                        center,
                        orientations[index],
                        _multiTransformBefore[index].Scaling,
                        out placements[index]))
                {
                    return false;
                }
            }

            return TryApplyMultiTransformPlacements(placements);
        }

        private bool TryApplyMultiScale(DecorationEditAxis axis, float factor)
        {
            if (!MultiTransformActive ||
                float.IsNaN(factor) ||
                float.IsInfinity(factor) ||
                factor < MinimumScale)
            {
                return false;
            }

            Vector3 axisVector = GroupTransformAxisVector(axis);
            if (axisVector.sqrMagnitude < 0.0001f || !DecorationEditMath.IsFinite(axisVector))
                return false;
            axisVector.Normalize();

            var placements = new MultiTransformPlacement[_multiTransformDecorations.Length];
            var scales = new Vector3[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                Vector3 offset = _multiTransformStartCenters[index] - _multiTransformPivotStart;
                Vector3 axial = axisVector * Vector3.Dot(offset, axisVector);
                Vector3 center = _multiTransformPivotStart + offset + axial * (factor - 1f);
                scales[index] = ScaleVectorAlongAxis(_multiTransformBefore[index].Scaling, axis, factor);
                if (!IsValidScale(scales[index]))
                    return false;

                if (!TryResolveMultiTransformPlacement(
                        _multiTransformBefore[index],
                        center,
                        _multiTransformBefore[index].Orientation,
                        scales[index],
                        out placements[index]))
                {
                    return false;
                }
            }

            return TryApplyMultiTransformPlacements(placements);
        }

        private static Vector3 ScaleVectorAlongAxis(Vector3 value, DecorationEditAxis axis, float factor)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Vector3(value.x * factor, value.y, value.z);
                case DecorationEditAxis.Y:
                    return new Vector3(value.x, value.y * factor, value.z);
                case DecorationEditAxis.Z:
                    return new Vector3(value.x, value.y, value.z * factor);
                default:
                    return value;
            }
        }

        private void RecordMultiTransformEdit(string label)
        {
            Decoration[] decorations = _multiTransformDecorations;
            DecorationEditSnapshot[] before = _multiTransformBefore;
            ClearMultiTransformState();
            if (decorations.Length <= 1 ||
                before.Length != decorations.Length ||
                _selectedConstruct == null)
            {
                UpdateDirtyFromSelection();
                return;
            }

            var changedDecorations = new List<Decoration>();
            var changedBefore = new List<DecorationEditSnapshot>();
            var changedAfter = new List<DecorationEditSnapshot>();
            int primaryIndex = 0;
            for (int index = 0; index < decorations.Length; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    before[index] == null ||
                    before[index].Matches(decoration))
                {
                    continue;
                }

                if (ReferenceEquals(decoration, _selected))
                    primaryIndex = changedDecorations.Count;
                changedDecorations.Add(decoration);
                changedBefore.Add(before[index]);
                changedAfter.Add(new DecorationEditSnapshot(decoration));
                _transactions.TrackEdit(decoration, before[index]);
            }

            if (changedDecorations.Count == 0)
            {
                UpdateDirtyFromSelection();
                return;
            }

            _history.Record(new DecorationSnapshotBatchCommand(
                label,
                _selectedConstruct,
                changedDecorations.ToArray(),
                changedBefore.ToArray(),
                changedAfter.ToArray(),
                primaryIndex));
            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
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

        private bool TryPickHandle(
            Decoration decoration,
            AllConstruct construct,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (decoration == null || construct == null)
                return false;

            Vector3 center = GetDecorationLocalCenter(decoration);
            if (!TryProject(construct, center, out Vector2 origin) ||
                !TryProject(construct, center + ActiveTransformAxisVector(decoration, DecorationEditAxis.X) * HandleLength, out Vector2 xEnd) ||
                !TryProject(construct, center + ActiveTransformAxisVector(decoration, DecorationEditAxis.Y) * HandleLength, out Vector2 yEnd) ||
                !TryProject(construct, center + ActiveTransformAxisVector(decoration, DecorationEditAxis.Z) * HandleLength, out Vector2 zEnd))
            {
                return false;
            }

            axis = DecorationEditMath.PickAxis(
                mouse,
                origin,
                xEnd,
                yEnd,
                zEnd,
                HandleRadiusPixels);
            return axis != DecorationEditAxis.None;
        }

        private bool TryPickMoveHandle(
            Decoration decoration,
            AllConstruct construct,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (decoration == null || construct == null)
                return false;

            Vector3 center = GetDecorationLocalCenter(decoration);
            if (!TryProject(construct, center, out Vector2 origin))
                return false;

            if (Vector2.Distance(mouse, origin) <= HandleRadiusPixels * 1.35f)
            {
                axis = DecorationEditAxis.Free;
                return true;
            }

            return TryPickHandle(decoration, construct, mouse, out axis);
        }

        private bool TryPickGroupHandle(
            Vector3 center,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_selectedConstruct == null)
                return false;

            if (!TryProject(_selectedConstruct, center, out Vector2 origin) ||
                !TryProject(_selectedConstruct, center + GroupTransformAxisVector(DecorationEditAxis.X) * HandleLength, out Vector2 xEnd) ||
                !TryProject(_selectedConstruct, center + GroupTransformAxisVector(DecorationEditAxis.Y) * HandleLength, out Vector2 yEnd) ||
                !TryProject(_selectedConstruct, center + GroupTransformAxisVector(DecorationEditAxis.Z) * HandleLength, out Vector2 zEnd))
            {
                return false;
            }

            axis = DecorationEditMath.PickAxis(
                mouse,
                origin,
                xEnd,
                yEnd,
                zEnd,
                HandleRadiusPixels);
            return axis != DecorationEditAxis.None;
        }

        private bool TryPickGroupMoveHandle(
            Vector3 center,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_selectedConstruct == null)
                return false;

            if (!TryProject(_selectedConstruct, center, out Vector2 origin))
                return false;

            if (Vector2.Distance(mouse, origin) <= HandleRadiusPixels * 1.35f)
            {
                axis = DecorationEditAxis.Free;
                return true;
            }

            return TryPickGroupHandle(center, mouse, out axis);
        }

        private void PrepareFreeDragFrame(Decoration decoration, AllConstruct construct)
        {
            _freeDragCameraRight = Vector3.right;
            _freeDragCameraUp = Vector3.up;
            _freeDragMetresPerPixel = 0.01f;
            Camera camera = Camera.main;
            if (camera == null || construct == null || decoration == null)
                return;

            try
            {
                _freeDragCameraRight = construct.myTransform.InverseTransformDirection(camera.transform.right).normalized;
                _freeDragCameraUp = construct.myTransform.InverseTransformDirection(camera.transform.up).normalized;
            }
            catch
            {
                _freeDragCameraRight = Vector3.right;
                _freeDragCameraUp = Vector3.up;
            }

            Vector3 center = GetDecorationLocalCenter(decoration);
            if (TryProject(construct, center, out Vector2 origin) &&
                TryProject(construct, center + _freeDragCameraRight, out Vector2 rightEnd))
            {
                float pixelsPerMetre = Mathf.Max(18f, Vector2.Distance(origin, rightEnd));
                _freeDragMetresPerPixel = 1f / pixelsPerMetre;
            }
        }

        private void PrepareFreeDragFrameAt(AllConstruct construct, Vector3 center)
        {
            _freeDragCameraRight = Vector3.right;
            _freeDragCameraUp = Vector3.up;
            _freeDragMetresPerPixel = 0.01f;
            Camera camera = Camera.main;
            if (camera == null || construct == null)
                return;

            try
            {
                _freeDragCameraRight = construct.myTransform.InverseTransformDirection(camera.transform.right).normalized;
                _freeDragCameraUp = construct.myTransform.InverseTransformDirection(camera.transform.up).normalized;
            }
            catch
            {
                _freeDragCameraRight = Vector3.right;
                _freeDragCameraUp = Vector3.up;
            }

            if (TryProject(construct, center, out Vector2 origin) &&
                TryProject(construct, center + _freeDragCameraRight, out Vector2 rightEnd))
            {
                float pixelsPerMetre = Mathf.Max(18f, Vector2.Distance(origin, rightEnd));
                _freeDragMetresPerPixel = 1f / pixelsPerMetre;
            }
        }

        private bool TryPickRotateRing(
            Decoration decoration,
            AllConstruct construct,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (decoration == null || construct == null)
                return false;

            Vector3 center = GetDecorationLocalCenter(decoration);
            if (!TryProject(construct, center, out Vector2 origin))
                return false;

            float best = HandleRadiusPixels * 1.35f;
            DecorationEditAxis pickedAxis = DecorationEditAxis.None;
            TryRing(DecorationEditAxis.X);
            TryRing(DecorationEditAxis.Y);
            TryRing(DecorationEditAxis.Z);
            axis = pickedAxis;
            return axis != DecorationEditAxis.None;

            void TryRing(DecorationEditAxis candidate)
            {
                BuildAxisBasis(
                    ActiveTransformAxisVector(decoration, candidate),
                    out Vector3 tangentA,
                    out Vector3 tangentB);
                const int steps = 40;
                bool havePrevious = false;
                Vector2 previous = origin;
                for (int step = 0; step <= steps; step++)
                {
                    float angle = step * Mathf.PI * 2f / steps;
                    Vector3 local = center +
                                    (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                                    RotateGizmoRadius;
                    if (!TryProject(construct, local, out Vector2 projected))
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
                            pickedAxis = candidate;
                        }
                    }

                    previous = projected;
                    havePrevious = true;
                }
            }
        }

        private bool TryPickAnchorHandle(
            Decoration decoration,
            AllConstruct construct,
            Vector2 mouse,
            out DecorationEditAxis axis,
            out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 0;
            if (decoration == null || construct == null)
                return false;

            Vector3 anchor = ToVector3(decoration.TetherPoint.Us);
            if (!TryProject(construct, anchor, out Vector2 origin))
                return false;

            float best = HandleRadiusPixels;
            DecorationEditAxis pickedAxis = DecorationEditAxis.None;
            int pickedSign = 0;
            TryAnchorAxis(DecorationEditAxis.X, 1);
            TryAnchorAxis(DecorationEditAxis.X, -1);
            TryAnchorAxis(DecorationEditAxis.Y, 1);
            TryAnchorAxis(DecorationEditAxis.Y, -1);
            TryAnchorAxis(DecorationEditAxis.Z, 1);
            TryAnchorAxis(DecorationEditAxis.Z, -1);
            axis = pickedAxis;
            sign = pickedSign;
            return axis != DecorationEditAxis.None;

            void TryAnchorAxis(DecorationEditAxis candidate, int candidateSign)
            {
                Vector3 end = anchor + DecorationEditMath.AxisVector(candidate) * candidateSign * HandleLength;
                if (!TryProject(construct, end, out Vector2 projectedEnd))
                    return;

                float distance = DistanceToSegment(mouse, origin, projectedEnd);
                if (distance >= best)
                    return;

                best = distance;
                pickedAxis = candidate;
                pickedSign = candidateSign;
            }
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

        private static bool PointInScreenTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = TriangleSign(point, a, b);
            float d2 = TriangleSign(point, b, c);
            float d3 = TriangleSign(point, c, a);
            bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static float TriangleSign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) -
            (p2.x - p3.x) * (p1.y - p3.y);

        private void BeginBoxSelection(Vector2 mouse)
        {
            _boxSelecting = true;
            _boxSelectStartMouse = mouse;
            _boxSelectCurrentMouse = mouse;
            _boxSelectCandidateCount = 0;
            RefreshDecorationCache(force: true);
        }

        private void UpdateBoxSelectionDrag(Vector2 mouse)
        {
            _boxSelectCurrentMouse = mouse;
            Rect rect = BoxSelectionRect();
            _boxSelectCandidateCount = rect.width >= BoxSelectionClickThresholdPixels &&
                                       rect.height >= BoxSelectionClickThresholdPixels
                ? CountBoxSelectionCandidates(rect, _selectionXray)
                : 0;
        }

        private void CommitBoxSelectionDrag(Vector2 mouse)
        {
            _boxSelectCurrentMouse = mouse;
            Rect rect = BoxSelectionRect();
            _boxSelecting = false;
            _boxSelectCandidateCount = 0;
            if (rect.width < BoxSelectionClickThresholdPixels ||
                rect.height < BoxSelectionClickThresholdPixels)
            {
                SelectNearest(_boxSelectStartMouse);
                return;
            }

            SelectBox(rect, _selectionXray);
        }

        private void CancelBoxSelection()
        {
            _boxSelecting = false;
            _boxSelectCandidateCount = 0;
        }

        private Rect BoxSelectionRect() =>
            NormalizedScreenRect(_boxSelectStartMouse, _boxSelectCurrentMouse);

        private static Rect NormalizedScreenRect(Vector2 start, Vector2 end)
        {
            float xMin = Mathf.Min(start.x, end.x);
            float xMax = Mathf.Max(start.x, end.x);
            float yMin = Mathf.Min(start.y, end.y);
            float yMax = Mathf.Max(start.y, end.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private int CountBoxSelectionCandidates(Rect rect, bool xray)
        {
            int count = 0;
            foreach (DecorationHit hit in EnumerateBoxSelectionHits(rect, xray, refresh: false))
                count++;
            return count;
        }

        private void SelectBox(Rect rect, bool xray)
        {
            List<DecorationHit> selected = EnumerateBoxSelectionHits(rect, xray, refresh: true).ToList();
            if (selected.Count == 0)
            {
                InfoStore.Add("No decoration centers inside the selection box.");
                return;
            }

            DecorationHit primary = selected
                .OrderBy(hit => (hit.ScreenPoint - _boxSelectStartMouse).sqrMagnitude)
                .First();
            SetPrimarySelection(primary.Decoration, primary.Construct);
            _selection.Clear();
            foreach (DecorationHit hit in selected)
                _selection.Add(hit.Decoration);
            ResetInspectorFields();
            InfoStore.Add(
                selected.Count.ToString("N0", CultureInfo.InvariantCulture) +
                (selected.Count == 1 ? " decoration selected" : " decorations selected") +
                (xray ? " with X-ray." : "."));
        }

        private IEnumerable<DecorationHit> EnumerateBoxSelectionHits(Rect rect, bool xray, bool refresh)
        {
            if (refresh)
                RefreshDecorationCache(force: true);

            AllConstruct primaryConstruct = BoxSelectionPrimaryConstruct();
            if (primaryConstruct == null)
                yield break;

            foreach (DecorationHit hit in _hits)
            {
                if (hit.Decoration == null ||
                    hit.Decoration.IsDeleted ||
                    !ReferenceEquals(hit.Construct, primaryConstruct) ||
                    !rect.Contains(hit.ScreenPoint))
                {
                    continue;
                }

                if (!xray && !IsDecorationHitVisible(hit, primaryConstruct))
                    continue;

                yield return hit;
            }
        }

        private AllConstruct BoxSelectionPrimaryConstruct() =>
            _selectedConstruct ?? FocusedConstruct();

        private bool IsDecorationHitVisible(DecorationHit hit, AllConstruct primaryConstruct)
        {
            Camera camera = Camera.main ?? Camera.current;
            if (camera == null || hit?.Construct == null || primaryConstruct == null)
                return true;

            Vector3 world;
            try
            {
                world = hit.Construct.SafeLocalToGlobal(hit.LocalCenter);
            }
            catch
            {
                return false;
            }

            Vector3 origin = camera.transform.position;
            Vector3 toCenter = world - origin;
            float distance = toCenter.magnitude;
            if (distance <= BoxSelectionVisibilityTolerance ||
                !DecorationEditMath.IsFinite(toCenter))
            {
                return true;
            }

            Ray ray = new Ray(origin, toCenter / distance);
            float rayDistance = Mathf.Max(0f, distance - BoxSelectionVisibilityTolerance);
            if (rayDistance <= 0.0001f)
                return true;

            if (CraftBlockOccludesDecorationRay(ray, rayDistance, primaryConstruct, hit))
                return false;

            return !CraftBlockSamplingOccludesDecorationRay(ray, rayDistance, primaryConstruct, hit);
        }

        private bool CraftBlockOccludesDecorationRay(
            Ray ray,
            float rayDistance,
            AllConstruct construct,
            DecorationHit hit)
        {
            RaycastHit[] rayHits = Physics.RaycastAll(ray, rayDistance);
            if (rayHits == null || rayHits.Length == 0)
                return false;

            Array.Sort(rayHits, (left, right) => left.distance.CompareTo(right.distance));
            Vector3 direction = ray.direction.normalized;
            for (int index = 0; index < rayHits.Length; index++)
            {
                RaycastHit rayHit = rayHits[index];
                if (rayHit.distance <= 0.0001f ||
                    rayHit.distance >= rayDistance)
                {
                    continue;
                }

                Vector3 sampleWorld = rayHit.point + direction * 0.06f;
                if (OccludingCraftBlockAtWorld(sampleWorld, construct, hit))
                    return true;
            }

            return false;
        }

        private bool CraftBlockSamplingOccludesDecorationRay(
            Ray ray,
            float rayDistance,
            AllConstruct construct,
            DecorationHit hit)
        {
            const float step = 0.35f;
            Vector3 direction = ray.direction.normalized;
            for (float distance = BoxSelectionVisibilityTolerance; distance < rayDistance; distance += step)
            {
                Vector3 sampleWorld = ray.origin + direction * distance;
                if (OccludingCraftBlockAtWorld(sampleWorld, construct, hit))
                    return true;
            }

            return false;
        }

        private bool OccludingCraftBlockAtWorld(
            Vector3 world,
            AllConstruct construct,
            DecorationHit hit)
        {
            if (!TryWorldToLocal(construct, world, out Vector3 local))
                return false;

            var cell = new Vector3i(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
            if (SameTether(cell, hit.Decoration.TetherPoint.Us))
                return false;

            return HasBlock(construct, cell);
        }

        private void SelectNearest(Vector2 mouse)
        {
            if (!TryFindNearestDecoration(mouse, out DecorationHit best))
            {
                InfoStore.Add("No decoration center near the cursor.");
                return;
            }

            Select(best.Decoration, best.Construct);
        }

        private void PaintNearest(Vector2 mouse)
        {
            if (!TryFindNearestDecoration(mouse, out DecorationHit best))
            {
                if (TryPaintPointedBlock(out string blockMessage))
                {
                    InfoStore.Add(blockMessage);
                    return;
                }

                InfoStore.Add("No decoration center or block near the cursor.");
                return;
            }

            Select(best.Decoration, best.Construct, notify: false);
            int before = _selected?.Color.Us ?? -1;
            SetSelectedColor(_paintColor);
            if (before == _paintColor)
                InfoStore.Add("Decoration is already color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
            else
                InfoStore.Add("Painted decoration color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private bool TryOpenDecorationContextMenu()
        {
            if (!TryFindNearestDecoration(_lastMouseGui, out DecorationHit hit) ||
                hit.Decoration == null ||
                hit.Decoration.IsDeleted ||
                hit.Construct == null)
            {
                CloseDecorationContextMenu();
                return false;
            }

            _decorationContextTarget = hit.Decoration;
            _decorationContextConstruct = hit.Construct;
            SelectDecorationContextTarget(notify: false);
            _decorationContextRect = DecorationContextRect(buttonCount: 8);
            return true;
        }

        private Rect DecorationContextRect(int buttonCount)
        {
            Vector2 mouse = _lastMouseGui;
            float width = EsuHudLayout.Scale(158f);
            float height = EsuHudLayout.Scale(34f + Mathf.Max(1, buttonCount) * 26f);
            var rect = new Rect(mouse.x, mouse.y, width, height);
            rect.x = Mathf.Clamp(rect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
            rect.y = Mathf.Clamp(rect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
            return rect;
        }

        private void DrawDecorationContextMenu()
        {
            if (_decorationContextTarget == null)
                return;

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_decorationContextRect.Contains(current.mousePosition))
            {
                CloseDecorationContextMenu();
                return;
            }

            if (!DecorationContextTargetValid())
            {
                CloseDecorationContextMenu();
                return;
            }

            DecorationContextAction action = DecorationContextAction.None;
            GUI.Box(_decorationContextRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect contentRect = EsuHudLayout.PanelInnerRect(_decorationContextRect, 5f);
            float y = contentRect.y;
            float headerHeight = EsuHudLayout.Scale(24f);
            float rowHeight = EsuHudLayout.Scale(24f);
            float rowGap = EsuHudLayout.Scale(2f);

            GUI.Label(
                new Rect(contentRect.x, y, contentRect.width, headerHeight),
                "Decoration",
                DecorationEditorTheme.SubHeader);
            y += headerHeight + rowGap;

            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Select", "Select this decoration."))
                action = DecorationContextAction.Select;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Move", "Select this decoration and switch to Move."))
                action = DecorationContextAction.Move;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Rotate", "Select this decoration and switch to Rotate."))
                action = DecorationContextAction.Rotate;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Scale", "Select this decoration and switch to Scale."))
                action = DecorationContextAction.Scale;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Anchor", "Select this decoration and switch to Anchor."))
                action = DecorationContextAction.Anchor;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Duplicate", "Duplicate this decoration."))
                action = DecorationContextAction.Duplicate;

            string meshLabel = IsSelectedOriginalMeshHidden()
                ? "Show anchor mesh"
                : "Hide anchor mesh";
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, meshLabel, "Toggle the selected decoration's anchor block mesh."))
                action = DecorationContextAction.ToggleAnchorMesh;

            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Delete", "Delete this decoration. Undo can restore it."))
                action = DecorationContextAction.Delete;

            if (ShouldConsumeDecorationContextEvent(current))
                current.Use();

            ExecuteDecorationContextAction(action);
        }

        private bool DrawDecorationContextButton(
            Rect contentRect,
            ref float y,
            float rowHeight,
            float rowGap,
            string label,
            string tooltip)
        {
            var buttonRect = new Rect(contentRect.x, y, contentRect.width, rowHeight);
            y += rowHeight + rowGap;
            return GUI.Button(
                buttonRect,
                new GUIContent(label, tooltip),
                DecorationEditorTheme.Button);
        }

        private bool ShouldConsumeDecorationContextEvent(Event current)
        {
            return current != null &&
                   _decorationContextRect.Contains(current.mousePosition) &&
                   (current.type == EventType.MouseDown ||
                    current.type == EventType.MouseUp ||
                    current.type == EventType.MouseDrag ||
                    current.type == EventType.ScrollWheel ||
                    current.type == EventType.ContextClick);
        }

        private void ExecuteDecorationContextAction(DecorationContextAction action)
        {
            switch (action)
            {
                case DecorationContextAction.Select:
                    SelectDecorationContextTarget(notify: true);
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.Move:
                    SelectContextTool(DecorationEditorTool.Move);
                    break;
                case DecorationContextAction.Rotate:
                    SelectContextTool(DecorationEditorTool.Rotate);
                    break;
                case DecorationContextAction.Scale:
                    SelectContextTool(DecorationEditorTool.Scale);
                    break;
                case DecorationContextAction.Anchor:
                    SelectContextTool(DecorationEditorTool.Anchor);
                    break;
                case DecorationContextAction.Duplicate:
                    SelectDecorationContextTarget(notify: false);
                    DuplicateSelectedDecoration();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.ToggleAnchorMesh:
                    SelectDecorationContextTarget(notify: false);
                    ToggleSelectedOriginalMeshVisibility();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.Delete:
                    SelectDecorationContextTarget(notify: false);
                    DeleteSelectedDecoration();
                    CloseDecorationContextMenu();
                    break;
            }
        }

        private void SelectContextTool(DecorationEditorTool tool)
        {
            SelectDecorationContextTarget(notify: false);
            SetActiveTool(tool);
            CloseDecorationContextMenu();
        }

        private bool DecorationContextTargetValid() =>
            _decorationContextTarget != null &&
            !_decorationContextTarget.IsDeleted &&
            _decorationContextConstruct != null;

        private void SelectDecorationContextTarget(bool notify)
        {
            if (!DecorationContextTargetValid())
                return;

            SetPrimarySelection(_decorationContextTarget, _decorationContextConstruct);
            _selection.Clear();
            _selection.Add(_decorationContextTarget);
            ResetInspectorFields();
            if (notify)
                InfoStore.Add("Decoration selected.");
        }

        private bool IsDecorationContextMenuAt(Vector2 mouse) =>
            _decorationContextTarget != null &&
            _decorationContextRect.Contains(mouse);

        private void CloseDecorationContextMenu()
        {
            _decorationContextTarget = null;
            _decorationContextConstruct = null;
            _decorationContextRect = Rect.zero;
        }

        private bool TryPaintPointedBlock(out string message)
        {
            message = null;
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit == null ||
                hit.Construct == null)
            {
                return false;
            }

            Block block = null;
            try
            {
                block = hit.Construct.AllBasics?.GetBlockViaLocalPosition(hit.Anchor);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint tool block lookup failed",
                    exception,
                    LogOptions._AlertDevInGame);
                message = "Paint block lookup failed.";
                return true;
            }

            if (block == null || block.IsDeleted || !block.IsAlive)
                return false;
            if (!block.OnPlayerTeam)
            {
                message = "Pointed block is not on your team.";
                return true;
            }

            int color = Mathf.Clamp(_paintColor, 0, 31);
            if (block.color == color)
            {
                message = "Block is already color #" + color.ToString(CultureInfo.InvariantCulture) + ".";
                return true;
            }

            PaintBlock(block, color);
            message = "Painted block color #" + color.ToString(CultureInfo.InvariantCulture) + ".";
            return true;
        }

        private static void PaintBlock(Block block, int color)
        {
            if (block == null || block.IsDeleted)
                return;

            block.SetColor(Mathf.Clamp(color, 0, 31));
            SendPaintBlockRpc(block, color);
        }

        private static void SendPaintBlockRpc(Block block, int color)
        {
            try
            {
                var constructable = block.GetConstructableOrSubConstructable();
                if (constructable?.NetworkIdentity == null)
                    return;

                Vector3i localPosition = block.LocalPosition;
                int paintColor = Mathf.Clamp(color, 0, 31);
                if (Net.IsServer)
                {
                    Coms.AddRpc(new RpcRequest(identity =>
                        ServerOutgoingRpcs.PaintBlock(identity, constructable.NetworkIdentity, localPosition, paintColor)));
                }
                else if (Net.IsClient)
                {
                    Coms.AddRpc(new RpcRequest(identity =>
                        ClientOutgoingRpcs.PaintBlock(identity, constructable.NetworkIdentity, localPosition, paintColor)));
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint tool block RPC failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private bool TryFindNearestDecoration(Vector2 mouse, out DecorationHit best)
        {
            RefreshDecorationCache(force: true);
            best = null;
            float bestDistance = SelectionRadiusPixels;
            foreach (DecorationHit hit in _hits)
            {
                if (hit.Decoration == null || hit.Decoration.IsDeleted)
                    continue;
                float distance = Vector2.Distance(mouse, hit.ScreenPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = hit;
                }
            }

            return best != null;
        }

        private void Select(Decoration decoration, AllConstruct construct, bool notify = true)
        {
            if (decoration == null || decoration.IsDeleted || construct == null)
                return;

            SetPrimarySelection(decoration, construct);
            _selection.Clear();
            _selection.Add(decoration);
            if (_tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
            {
                _tool = DecorationEditorTool.Move;
            }
            ResetInspectorFields();
            if (notify)
                InfoStore.Add("Decoration selected.");
        }

        private void SetPrimarySelection(Decoration decoration, AllConstruct construct)
        {
            if (decoration == null || decoration.IsDeleted || construct == null)
                return;

            _selected = decoration;
            _selectedConstruct = construct;
            _snapshot = _transactions.GetOriginal(decoration) ?? new DecorationEditSnapshot(decoration);
            _selectedCreatedInSession = _transactions.IsCreated(decoration);
            _dirty = _transactions.HasChanges;
            _dragAxis = DecorationEditAxis.None;
            _dragSnapshotStart = null;
            _dragSymmetryFollow = null;
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
            _anchorDragSymmetryFollow = null;
            _anchorPreviewValid = false;
            ClearMultiTransformState();
            DecorationMeshCatalogEntry matching = _meshCatalog.FirstOrDefault(
                entry => entry.Guid == decoration.MeshGuid.Us);
            if (matching != null)
                _selectedMesh = matching;
        }

        private void DuplicateSelectedDecoration()
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
            {
                InfoStore.Add("Select a decoration before duplicating it.");
                return;
            }

            var snapshot = new DecorationEditSnapshot(_selected);
            if (!TryCreateDecorationFromSnapshot(
                    _selectedConstruct,
                    snapshot,
                    "Decoration duplicate",
                    out Decoration duplicate))
            {
                return;
            }

            DecorationMeshCatalogEntry mesh = null;
            _meshByGuid.TryGetValue(snapshot.MeshGuid, out mesh);
            FinishCreatedDecorations(
                _selectedConstruct,
                new[] { duplicate },
                mesh,
                "Decoration duplicated.");
        }

        private void DeleteSelectedDecoration()
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
            {
                InfoStore.Add("Select a decoration before deleting it.");
                return;
            }

            Decoration decoration = _selected;
            AllConstruct construct = _selectedConstruct;
            bool createdInSession = _transactions.IsCreated(decoration);
            var deleted = new DecorationEditSnapshot(decoration);
            DecorationEditSnapshot original = createdInSession
                ? null
                : _transactions.GetOriginal(decoration) ?? deleted;

            try
            {
                decoration.Delete();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration context delete failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Decoration delete failed.");
                return;
            }

            if (createdInSession)
                _transactions.UnmarkCreated(decoration);
            else
                TrackDeletedDecoration(construct, original, deleted, decoration);

            _history.Record(new DecorationDeleteCommand(
                construct,
                decoration,
                deleted,
                original,
                createdInSession));
            ClearDeletedSelection(decoration);
            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add("Decoration deleted.");
        }

        private void ClearDeletedSelection(Decoration decoration)
        {
            if (decoration == null)
                return;

            _selection.Remove(decoration);
            if (ReferenceEquals(_selected, decoration))
            {
                _selected = null;
                _selectedConstruct = null;
                _snapshot = null;
                _selectedCreatedInSession = false;
            }
            ResetInspectorFields();
        }

        private void TrackDeletedDecoration(
            AllConstruct construct,
            DecorationEditSnapshot original,
            DecorationEditSnapshot deleted,
            Decoration decoration)
        {
            if (construct == null || original == null || deleted == null)
                return;

            _deletedDecorations.RemoveAll(record => ReferenceEquals(record.Original, original));
            _deletedDecorations.Add(new DecorationDeletionRecord(
                construct,
                original,
                deleted,
                decoration));
        }

        private void UntrackDeletedDecoration(DecorationEditSnapshot original)
        {
            if (original == null)
                return;

            _deletedDecorations.RemoveAll(record => ReferenceEquals(record.Original, original));
        }

        private void RestoreDeletedDecorationsForCancel()
        {
            if (_deletedDecorations.Count == 0)
                return;

            DecorationDeletionRecord[] records = _deletedDecorations.ToArray();
            _deletedDecorations.Clear();
            for (int index = 0; index < records.Length; index++)
            {
                TryCreateDecorationFromSnapshot(
                    records[index].Construct,
                    records[index].Original,
                    "Decoration delete cancel",
                    out _);
            }
        }

        private bool TryCreateDecorationFromSnapshot(
            AllConstruct construct,
            DecorationEditSnapshot snapshot,
            string context,
            out Decoration decoration)
        {
            decoration = null;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null || snapshot == null)
            {
                InfoStore.Add(context + " failed because the decoration manager is unavailable.");
                return false;
            }

            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    1,
                    out string compatibilityMessage))
            {
                InfoStore.Add(context + " failed: " + compatibilityMessage);
                return false;
            }

            decoration = decorations.NewDecoration(
                snapshot.TetherPoint,
                force: true,
                playSound: false,
                forceEvenIfMaxReached: true);
            if (decoration == null)
            {
                InfoStore.Add(context + " failed because FTD rejected the new decoration.");
                return false;
            }

            if (snapshot.TryRestore(decoration))
                return true;

            using (VanillaCompatibilityGuard.BeginSuppression(context + " cleanup"))
            {
                try { decoration.Delete(); }
                catch { /* The restore failure is the actionable result. */ }
            }

            decoration = null;
            InfoStore.Add(context + " failed because FTD rejected the restored decoration state.");
            return false;
        }

        private void ApplySelection(bool notify = true)
        {
            ClearApplyCancelAttention();
            CloseDecorationContextMenu();
            if (_selected != null && !_selected.IsDeleted)
                _snapshot = new DecorationEditSnapshot(_selected);
            _transactions.Apply();
            _deletedDecorations.Clear();
            ClearSurfaceDraft(notify: false);
            ClearGeneratorDraft(notify: false);
            _selectedCreatedInSession = false;
            _dirty = false;
            _history.Clear();
            if (_selected != null && !_selected.IsDeleted)
                _selected.Changed();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            if (notify)
                InfoStore.Add("Decoration edit session applied.");
        }

        private void CancelSelection(bool notify = true)
        {
            ClearApplyCancelAttention();
            CloseDecorationContextMenu();
            CancelPlacement();
            ClearSurfaceDraft(notify: false);
            ClearGeneratorDraft(notify: false);
            _history.Clear();
            _transactions.Cancel();
            RestoreDeletedDecorationsForCancel();
            if (notify)
                InfoStore.Add("Decoration edit session cancelled and restored.");
            if (_selected != null && _selected.IsDeleted)
                _selected = null;
            _selectedCreatedInSession = false;
            _dirty = false;
            _dragAxis = DecorationEditAxis.None;
            _dragSnapshotStart = null;
            _dragSymmetryFollow = null;
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
            _anchorDragSymmetryFollow = null;
            _anchorPreviewValid = false;
            _snapshot = _selected != null && !_selected.IsDeleted
                ? new DecorationEditSnapshot(_selected)
                : null;
            _selection.Clear();
            if (_selected != null && !_selected.IsDeleted)
                _selection.Add(_selected);
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
        }

        private void AssignMeshToSelected(DecorationMeshCatalogEntry entry)
        {
            if (entry == null || _selected == null || _selected.IsDeleted)
                return;

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.MeshGuid.Us = entry.Guid;
            _selectedMesh = entry;
            PushRecentMesh(entry.Guid);
            MarkSelectedDirty();
            RecordSnapshotEdit("Assign mesh", before);
        }

        private bool TryCreateDecorationsAtCursor(
            DecorationMeshCatalogEntry mesh,
            out AllConstruct construct,
            out List<Decoration> created)
        {
            construct = _placementConstruct;
            created = null;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
            {
                InfoStore.Add("The current construct decoration manager is unavailable.");
                return false;
            }

            if (!_placementValid)
            {
                InfoStore.Add("Point at a real craft block before placing a decoration.");
                return false;
            }

            Vector3 localPosition = DecorationEditMath.Snap(_placementLocalPosition - ToVector3(_placementAnchor));
            if (!DecorationEditMath.IsFinite(localPosition) ||
                !DecorationEditMath.IsWithinPositionLimit(localPosition))
            {
                InfoStore.Add("Pointer placement rejected because the local offset would exceed +/-10.");
                return false;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(construct, out string reason))
            {
                InfoStore.Add(reason);
                return false;
            }

            if (!TryBuildDecorationPlacementPlans(localPosition, out List<DecorationPlacementPlan> plans))
                return false;

            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    plans.Count,
                    out string compatibilityMessage))
            {
                InfoStore.Add(compatibilityMessage);
                return false;
            }

            created = new List<Decoration>(plans.Count);
            try
            {
                for (int index = 0; index < plans.Count; index++)
                {
                    DecorationPlacementPlan plan = plans[index];
                    Decoration decoration = decorations.NewDecoration(
                        plan.Anchor,
                        force: true,
                        playSound: index == 0,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a new decoration.");

                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = mesh.Guid;
                    decoration.Positioning.Us = plan.Positioning;
                    decoration.Scaling.Us = Vector3.one;
                    decoration.Orientation.Us = Vector3.zero;
                    decoration.Changed();
                    created.Add(decoration);
                }
            }
            catch (Exception exception)
            {
                using (VanillaCompatibilityGuard.BeginSuppression("decoration placement cleanup"))
                {
                    for (int index = created.Count - 1; index >= 0; index--)
                    {
                        try { created[index]?.Delete(); }
                        catch { /* The creation failure is the actionable result. */ }
                    }
                }

                created = null;
                InfoStore.Add("Decoration placement rejected: " + exception.Message);
                return false;
            }

            return true;
        }

        private bool TryBuildDecorationPlacementPlans(
            Vector3 originalPositioning,
            out List<DecorationPlacementPlan> plans)
        {
            plans = new List<DecorationPlacementPlan>();
            Vector3 originalCenter = ToVector3(_placementAnchor) + originalPositioning;
            var seen = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                Vector3i anchor = variant.Mirror(_placementAnchor);
                Vector3 center = variant.Mirror(originalCenter);
                Vector3 positioning = DecorationEditMath.Snap(center - ToVector3(anchor));
                string key = PlacementKey(anchor, positioning);
                if (!seen.Add(key))
                    continue;

                if (!HasBlock(_placementConstruct, anchor))
                {
                    InfoStore.Add("Symmetry placement rejected because a mirrored tether block is missing.");
                    return false;
                }

                if (!DecorationEditMath.IsFinite(positioning) ||
                    !DecorationEditMath.IsWithinPositionLimit(positioning))
                {
                    InfoStore.Add("Symmetry placement rejected because a mirrored offset would exceed +/-10.");
                    return false;
                }

                plans.Add(new DecorationPlacementPlan(anchor, positioning));
            }

            if (plans.Count == 0)
            {
                InfoStore.Add("No valid decoration placement was generated.");
                return false;
            }

            return true;
        }

        private void FinishCreatedDecorations(
            AllConstruct construct,
            IReadOnlyList<Decoration> decorations,
            DecorationMeshCatalogEntry mesh,
            string message)
        {
            if (decorations == null || decorations.Count == 0)
                return;

            Select(decorations[0], construct);
            var snapshots = new DecorationEditSnapshot[decorations.Count];
            var created = new Decoration[decorations.Count];
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                _transactions.MarkCreated(decoration);
                snapshots[index] = new DecorationEditSnapshot(decoration);
                created[index] = decoration;
            }

            _selectedCreatedInSession = true;
            _dirty = true;
            if (created.Length == 1)
            {
                _history.Record(new DecorationCreateCommand(
                    construct,
                    created[0],
                    snapshots[0]));
            }
            else
            {
                _history.Record(new DecorationCreateBatchCommand(
                    construct,
                    created,
                    snapshots));
            }

            if (mesh != null)
                PushRecentMesh(mesh.Guid);
            RefreshDecorationCache(force: true);
            InfoStore.Add(message);
        }

        private void ShiftAnchor(Vector3i shift)
        {
            if (_selected == null || _selected.IsDeleted)
            {
                InfoStore.Add("Select a decoration before moving its anchor.");
                return;
            }

            ApplyAnchorShift(shift, new DecorationEditSnapshot(_selected));
        }

        private bool TryPreviewAnchorShift(
            Vector3i shift,
            out Vector3i newTether,
            out Vector3 newPosition)
        {
            newTether = default;
            newPosition = Vector3.zero;
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return false;

            Vector3i current = _selected.TetherPoint.Us;
            newTether = new Vector3i(
                current.x + shift.x,
                current.y + shift.y,
                current.z + shift.z);
            if (!HasBlock(_selectedConstruct, newTether))
                return false;

            Vector3 oldCenter = ToVector3(current) + _selected.Positioning.Us;
            newPosition = DecorationEditMath.Snap(oldCenter - ToVector3(newTether));
            return DecorationEditMath.IsFinite(newPosition) &&
                   DecorationEditMath.IsWithinPositionLimit(newPosition);
        }

        private void ApplyAnchorShift(
            Vector3i shift,
            DecorationEditSnapshot before,
            SymmetryFollowContext symmetryFollow = null)
        {
            if (_selected == null || _selected.IsDeleted)
                return;

            if (shift.x == 0 && shift.y == 0 && shift.z == 0)
                return;

            if (!TryPreviewAnchorShift(shift, out _, out Vector3 moved))
            {
                InfoStore.Add("Anchor move rejected because the target block is invalid or positioning would exceed +/-10.");
                return;
            }

            symmetryFollow = symmetryFollow ?? BeginSymmetryFollow(before, reportSkipped: true);
            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            Vector3i oldTether = _selected.TetherPoint.Us;
            Vector3 oldPosition = _selected.Positioning.Us;
            bool shifted = false;
            try
            {
                if (!_selected.OurManager.ShiftDecoration(_selected, shift))
                {
                    InfoStore.Add("Anchor move rejected by FTD at the destination tether point.");
                    return;
                }

                shifted = true;
                _selected.Positioning.Us = moved;
                if (!TryApplySymmetryFollow(symmetryFollow, reportInvalid: true))
                {
                    RestoreSymmetryEditState(selectedRollback, symmetryFollow, targetRollback);
                    return;
                }

                MarkSelectedDirty();
                ResetInspectorFields();
                RecordSnapshotEdit("Move anchor", before, symmetryFollow);
                RefreshDecorationCache(force: true);
            }
            catch (Exception exception)
            {
                try
                {
                    if (shifted)
                    {
                        _selected.OurManager.ShiftDecoration(
                            _selected,
                            new Vector3i(-shift.x, -shift.y, -shift.z));
                    }
                    else
                    {
                        _selected.TetherPoint.Us = oldTether;
                    }
                    _selected.Positioning.Us = oldPosition;
                    _selected.Changed();
                    RestoreSymmetryTargets(symmetryFollow, targetRollback);
                }
                catch
                {
                    // The original exception is the actionable failure.
                }
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration anchor shift failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Anchor move failed and was rolled back.");
            }
        }

        private bool TryAutoFollowAnchor(out Vector3i appliedShift)
        {
            appliedShift = new Vector3i(0, 0, 0);
            if (!_anchorFollowDecoration ||
                _selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null)
            {
                return false;
            }

            Vector3i current = _selected.TetherPoint.Us;
            Vector3 center = GetDecorationLocalCenter(_selected);
            float threshold = Mathf.Max(AnchorFollowMinimumDistance, _anchorFollowDistance);
            if ((center - ToVector3(current)).sqrMagnitude < threshold * threshold)
                return false;

            if (!TryFindAnchorFollowTarget(center, current, out Vector3i target))
                return false;

            appliedShift = new Vector3i(
                target.x - current.x,
                target.y - current.y,
                target.z - current.z);
            if (appliedShift.x == 0 && appliedShift.y == 0 && appliedShift.z == 0)
                return false;

            Vector3 moved = DecorationEditMath.Snap(center - ToVector3(target));
            if (!DecorationEditMath.IsFinite(moved) ||
                !DecorationEditMath.IsWithinPositionLimit(moved))
            {
                return false;
            }

            Vector3 oldPosition = _selected.Positioning.Us;
            bool shifted = false;
            try
            {
                if (!_selected.OurManager.ShiftDecoration(_selected, appliedShift))
                    return false;

                shifted = true;
                _selected.Positioning.Us = moved;
                _selected.Changed();
                RefreshDecorationCache(force: true);
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    if (shifted)
                    {
                        _selected.OurManager.ShiftDecoration(
                            _selected,
                            new Vector3i(-appliedShift.x, -appliedShift.y, -appliedShift.z));
                    }

                    _selected.Positioning.Us = oldPosition;
                    _selected.Changed();
                }
                catch
                {
                    // The original exception is the actionable failure.
                }

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Anchor follow retether failed",
                    exception,
                    LogOptions._AlertDevInGame);
                return false;
            }
        }

        private bool TryFindAnchorFollowTarget(
            Vector3 center,
            Vector3i current,
            out Vector3i target)
        {
            return TryFindAnchorFollowTarget(_selectedConstruct, center, current, out target);
        }

        private bool TryFindAnchorFollowTarget(
            AllConstruct construct,
            Vector3 center,
            Vector3i current,
            out Vector3i target)
        {
            target = current;
            if (construct == null)
                return false;

            Vector3i rounded = new Vector3i(
                Mathf.RoundToInt(center.x),
                Mathf.RoundToInt(center.y),
                Mathf.RoundToInt(center.z));
            int searchRadius = Mathf.Clamp(
                Mathf.CeilToInt(_anchorFollowDistance),
                1,
                AnchorFollowMaximumSearchRadius);
            float bestDistance = float.MaxValue;
            Vector3i best = current;

            for (int x = -searchRadius; x <= searchRadius; x++)
                for (int y = -searchRadius; y <= searchRadius; y++)
                    for (int z = -searchRadius; z <= searchRadius; z++)
                    {
                        var candidate = new Vector3i(rounded.x + x, rounded.y + y, rounded.z + z);
                        if (!HasBlock(construct, candidate))
                            continue;

                        Vector3 moved = DecorationEditMath.Snap(center - ToVector3(candidate));
                        if (!DecorationEditMath.IsFinite(moved) ||
                            !DecorationEditMath.IsWithinPositionLimit(moved))
                        {
                            continue;
                        }

                        float distance = (center - ToVector3(candidate)).sqrMagnitude;
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        best = candidate;
                    }

            if (bestDistance == float.MaxValue || best.Equals(current))
                return false;

            target = best;
            return true;
        }

        private SymmetryFollowContext BeginSymmetryFollow(
            DecorationEditSnapshot selectedBefore,
            bool reportSkipped)
        {
            if (selectedBefore == null ||
                _selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null ||
                !DecoLimitLifter.EsuSymmetry.HasActivePlanes)
            {
                return null;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_selectedConstruct, out string constructReason))
            {
                ReportSymmetryFollowSkipped(constructReason, reportSkipped);
                return null;
            }

            Vector3 selectedCenter = ToVector3(selectedBefore.TetherPoint) + selectedBefore.Positioning;
            string selectedPlacementKey = PlacementKey(selectedBefore.TetherPoint, selectedBefore.Positioning);
            var targets = new List<SymmetryFollowTarget>();
            var seenDecorations = new HashSet<Decoration>();
            var seenPlacements = new HashSet<string>();

            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                if (variant.Axes.Count == 0)
                    continue;

                Vector3i expectedAnchor = variant.Mirror(selectedBefore.TetherPoint);
                Vector3 expectedCenter = variant.Mirror(selectedCenter);
                Vector3 expectedPosition = DecorationEditMath.Snap(expectedCenter - ToVector3(expectedAnchor));
                string placementKey = PlacementKey(expectedAnchor, expectedPosition);
                if (placementKey == selectedPlacementKey || !seenPlacements.Add(placementKey))
                    continue;

                if (!HasBlock(_selectedConstruct, expectedAnchor))
                {
                    ReportSymmetryFollowSkipped(
                        "mirrored tether block is missing",
                        reportSkipped);
                    return null;
                }

                if (!DecorationEditMath.IsFinite(expectedPosition) ||
                    !DecorationEditMath.IsWithinPositionLimit(expectedPosition))
                {
                    ReportSymmetryFollowSkipped(
                        "mirrored offset would exceed +/-10",
                        reportSkipped);
                    return null;
                }

                if (!TryFindSymmetryCounterpart(
                        expectedAnchor,
                        expectedCenter,
                        selectedBefore.MeshGuid,
                        seenDecorations,
                        out Decoration counterpart,
                        out string matchReason))
                {
                    ReportSymmetryFollowSkipped(matchReason, reportSkipped);
                    return null;
                }

                seenDecorations.Add(counterpart);
                targets.Add(new SymmetryFollowTarget(
                    variant,
                    counterpart,
                    new DecorationEditSnapshot(counterpart)));
            }

            if (targets.Count == 0)
                return null;

            for (int index = 0; index < targets.Count; index++)
                _transactions.TrackEdit(targets[index].Decoration, targets[index].Before);
            return new SymmetryFollowContext(selectedBefore, targets.ToArray());
        }

        private bool TryFindSymmetryCounterpart(
            Vector3i expectedAnchor,
            Vector3 expectedCenter,
            Guid meshGuid,
            HashSet<Decoration> alreadyUsed,
            out Decoration counterpart,
            out string reason)
        {
            counterpart = null;
            reason = string.Empty;

            float toleranceSquared =
                SymmetryCounterpartCenterTolerance * SymmetryCounterpartCenterTolerance;
            var matches = new List<Decoration>();
            foreach (Decoration decoration in SelectedAnchorDecorations(expectedAnchor))
            {
                if (decoration == null ||
                    decoration.IsDeleted ||
                    ReferenceEquals(decoration, _selected) ||
                    (alreadyUsed != null && alreadyUsed.Contains(decoration)) ||
                    decoration.MeshGuid.Us != meshGuid)
                {
                    continue;
                }

                Vector3 center = GetDecorationLocalCenter(decoration);
                if ((center - expectedCenter).sqrMagnitude <= toleranceSquared)
                    matches.Add(decoration);
            }

            if (matches.Count == 1)
            {
                counterpart = matches[0];
                return true;
            }

            reason = matches.Count == 0
                ? "no matching mirrored decoration was found on the mirrored anchor"
                : "multiple matching mirrored decorations were found on the mirrored anchor";
            return false;
        }

        private bool TryApplySymmetryFollow(
            SymmetryFollowContext symmetryFollow,
            bool reportInvalid)
        {
            if (symmetryFollow == null || !symmetryFollow.IsActive)
                return true;

            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return ReportSymmetryFollowInvalid(
                    symmetryFollow,
                    "selected decoration is no longer available",
                    reportInvalid);

            Vector3 selectedCenter = GetDecorationLocalCenter(_selected);
            var applications = new SymmetryFollowApplication[symmetryFollow.Targets.Length];
            for (int index = 0; index < symmetryFollow.Targets.Length; index++)
            {
                SymmetryFollowTarget target = symmetryFollow.Targets[index];
                if (target.Decoration == null || target.Decoration.IsDeleted)
                {
                    return ReportSymmetryFollowInvalid(
                        symmetryFollow,
                        "mirrored decoration is no longer available",
                        reportInvalid);
                }

                Vector3i targetAnchor = target.Variant.Mirror(_selected.TetherPoint.Us);
                Vector3 targetCenter = target.Variant.Mirror(selectedCenter);
                Vector3 targetPosition = DecorationEditMath.Snap(targetCenter - ToVector3(targetAnchor));
                if (!HasBlock(_selectedConstruct, targetAnchor))
                {
                    return ReportSymmetryFollowInvalid(
                        symmetryFollow,
                        "mirrored tether block is missing",
                        reportInvalid);
                }

                if (!DecorationEditMath.IsFinite(targetPosition) ||
                    !DecorationEditMath.IsWithinPositionLimit(targetPosition))
                {
                    return ReportSymmetryFollowInvalid(
                        symmetryFollow,
                        "mirrored offset would exceed +/-10",
                        reportInvalid);
                }

                applications[index] = new SymmetryFollowApplication(
                    target.Decoration,
                    targetAnchor,
                    targetPosition);
            }

            DecorationEditSnapshot[] rollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            for (int index = 0; index < applications.Length; index++)
            {
                SymmetryFollowApplication application = applications[index];
                if (TryApplyDecorationAnchorAndPosition(
                        application.Decoration,
                        application.Anchor,
                        application.Positioning,
                        out string applyReason))
                {
                    continue;
                }

                RestoreSymmetryTargets(symmetryFollow, rollback);
                return ReportSymmetryFollowInvalid(
                    symmetryFollow,
                    applyReason,
                    reportInvalid);
            }

            return true;
        }

        private bool TryApplyDecorationAnchorAndPosition(
            Decoration decoration,
            Vector3i anchor,
            Vector3 positioning,
            out string reason)
        {
            reason = string.Empty;
            if (decoration == null || decoration.IsDeleted)
            {
                reason = "mirrored decoration is no longer available";
                return false;
            }

            if (!HasBlock(_selectedConstruct, anchor))
            {
                reason = "mirrored tether block is missing";
                return false;
            }

            if (!DecorationEditMath.IsFinite(positioning) ||
                !DecorationEditMath.IsWithinPositionLimit(positioning))
            {
                reason = "mirrored offset would exceed +/-10";
                return false;
            }

            try
            {
                Vector3i current = decoration.TetherPoint.Us;
                if (!SameTether(current, anchor))
                {
                    if (decoration.OurManager == null)
                    {
                        reason = "mirrored decoration manager is unavailable";
                        return false;
                    }

                    var shift = new Vector3i(
                        anchor.x - current.x,
                        anchor.y - current.y,
                        anchor.z - current.z);
                    if (!decoration.OurManager.ShiftDecoration(decoration, shift))
                    {
                        reason = "FTD rejected the mirrored tether shift";
                        return false;
                    }
                }

                decoration.Positioning.Us = positioning;
                decoration.Changed();
                return true;
            }
            catch
            {
                reason = "FTD rejected the mirrored tether shift";
                return false;
            }
        }

        private static DecorationEditSnapshot[] CaptureSymmetryTargetSnapshots(
            SymmetryFollowContext symmetryFollow)
        {
            if (symmetryFollow == null || !symmetryFollow.IsActive)
                return Array.Empty<DecorationEditSnapshot>();

            var snapshots = new DecorationEditSnapshot[symmetryFollow.Targets.Length];
            for (int index = 0; index < symmetryFollow.Targets.Length; index++)
            {
                Decoration decoration = symmetryFollow.Targets[index].Decoration;
                if (decoration != null && !decoration.IsDeleted)
                    snapshots[index] = new DecorationEditSnapshot(decoration);
            }

            return snapshots;
        }

        private void RestoreSymmetryEditState(
            DecorationEditSnapshot selectedSnapshot,
            SymmetryFollowContext symmetryFollow,
            DecorationEditSnapshot[] targetSnapshots)
        {
            selectedSnapshot?.TryRestore(_selected);
            RestoreSymmetryTargets(symmetryFollow, targetSnapshots);
            ResetInspectorFields();
            UpdateDirtyFromSelection();
        }

        private static void RestoreSymmetryTargets(
            SymmetryFollowContext symmetryFollow,
            DecorationEditSnapshot[] snapshots)
        {
            if (symmetryFollow == null ||
                !symmetryFollow.IsActive ||
                snapshots == null)
            {
                return;
            }

            int count = Math.Min(symmetryFollow.Targets.Length, snapshots.Length);
            for (int index = 0; index < count; index++)
            {
                Decoration decoration = symmetryFollow.Targets[index].Decoration;
                DecorationEditSnapshot snapshot = snapshots[index];
                if (decoration != null && !decoration.IsDeleted && snapshot != null)
                    snapshot.TryRestore(decoration);
            }
        }

        private static bool ReportSymmetryFollowInvalid(
            SymmetryFollowContext symmetryFollow,
            string reason,
            bool reportInvalid)
        {
            if (reportInvalid &&
                symmetryFollow != null &&
                !symmetryFollow.ReportedInvalid)
            {
                InfoStore.Add("Symmetry follow rejected: " + reason + ".");
                symmetryFollow.ReportedInvalid = true;
            }

            return false;
        }

        private static void ReportSymmetryFollowSkipped(string reason, bool reportSkipped)
        {
            if (reportSkipped)
                InfoStore.Add("Symmetry follow skipped: " + reason + ".");
        }

        private void StartMeshPlacement(DecorationMeshCatalogEntry entry)
        {
            if (entry == null)
            {
                InfoStore.Add("Select a mesh before placing a decoration.");
                return;
            }

            _selectedMesh = entry;
            _placingMesh = entry;
            _toolBeforePlacement = _tool == DecorationEditorTool.Mesh ? DecorationEditorTool.Select : _tool;
            _tool = DecorationEditorTool.Mesh;
            PushRecentMesh(entry.Guid);
            UpdatePlacementState();
            InfoStore.Add("Mesh placement active. Click a valid craft block; right-click or Esc cancels.");
        }

        private void CancelPlacement()
        {
            if (_placingMesh == null)
                return;

            _placingMesh = null;
            _placementConstruct = null;
            _placementValid = false;
            HidePlacementGhost();
            _tool = _toolBeforePlacement;
            InfoStore.Add("Mesh placement cancelled.");
        }

        private void UpdatePlacementState()
        {
            _placementConstruct = null;
            _placementAnchor = default;
            _placementLocalPosition = Vector3.zero;
            _placementWorldPosition = Vector3.zero;
            _placementWorldNormal = Vector3.zero;
            _placementValid = false;
            if (_placingMesh == null)
            {
                HidePlacementGhost();
                return;
            }

            if (!_pointerProbe.TryProbe(DecorationPointerProbe.ProbeOptions.MeshPlacement, out DecorationPointerHit hit))
            {
                HidePlacementGhost();
                return;
            }

            _placementConstruct = hit.Construct;
            _placementAnchor = hit.Anchor;
            _placementLocalPosition = hit.LocalHit;
            _placementWorldPosition = hit.WorldHit;
            _placementWorldNormal = hit.WorldNormal;
            Vector3 localPosition = hit.LocalPositioning;
            _placementValid = DecorationEditMath.IsFinite(localPosition) &&
                              DecorationEditMath.IsWithinPositionLimit(localPosition);
        }

        private void PlaceSelectedMeshAtPointer()
        {
            DecorationMeshCatalogEntry mesh = _placingMesh;
            if (mesh == null)
                return;

            if (!_placementValid)
            {
                InfoStore.Add("Point at a valid craft block before placing the decoration.");
                return;
            }

            if (!TryCreateDecorationsAtCursor(mesh, out AllConstruct construct, out List<Decoration> decorations))
                return;

            _placingMesh = null;
            HidePlacementGhost();
            FinishCreatedDecorations(
                construct,
                decorations,
                mesh,
                decorations.Count == 1
                    ? "Decoration placed on the pointed block."
                    : $"Placed {decorations.Count:N0} symmetrical decorations.");
            _tool = DecorationEditorTool.Move;
        }

        private void HandleSurfaceSceneInput()
        {
            if (Input.GetMouseButtonDown(1))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _pendingSurfaceSharedAnchorPick = false;
                if (TryOpenSurfaceContextMenu())
                    return;

                if (_surfaceDraft.HasActiveSelection)
                {
                    _surfaceDraft.ClearSelection();
                    _surfaceMessage = "Surface selection cleared.";
                }
                else
                {
                    _surfaceMessage = "Surface selection is clear. Use Clear to remove the surface draft.";
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();

            if (TryHandleSurfaceSharedAnchorClick())
                return;

            if (TryPickSurfaceHandle(_lastMouseGui, out DecorationEditAxis axis))
            {
                BeginSurfacePointDrag(axis);
                return;
            }

            if (TryPickSurfacePoint(_lastMouseGui, out int pointIndex))
            {
                if (IsShiftHeld())
                {
                    SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
                    if (!_surfaceDraft.ToggleManualFacePoint(pointIndex, out _surfaceMessage))
                        InfoStore.Add(_surfaceMessage);
                    else
                    {
                        InvalidateSurfacePlan(_surfaceMessage);
                        RecordSurfaceEdit("Select surface points", before);
                    }
                }
                else
                {
                    _surfaceDraft.SelectPoint(pointIndex);
                    _surfaceMessage = "Surface point selected.";
                }
                return;
            }

            if (TryPickSurfaceEdge(_lastMouseGui, out SurfaceEdge edge))
            {
                if (IsShiftHeld())
                {
                    SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
                    if (!_surfaceDraft.ToggleBridgeEdge(edge, out _surfaceMessage))
                        InfoStore.Add(_surfaceMessage);
                    else
                        RecordSurfaceEdit("Select surface bridge edge", before);
                }
                else
                {
                    _surfaceDraft.SelectEdge(edge.A, edge.B);
                    _surfaceMessage = "Surface edge selected. Click a new point to extend.";
                }
                return;
            }

            if (TryPickSurfaceFace(_lastMouseGui, out int faceIndex))
            {
                _surfaceDraft.SelectFace(faceIndex);
                SyncSharedPaintColorFromSurfaceFace(faceIndex);
                _surfaceMessage = "Surface face selected.";
                return;
            }

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                _surfaceMessage = "Point at a real craft block to place a surface point.";
                return;
            }

            bool extend = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Edge &&
                          _surfaceDraft.SelectedEdge.IsValid &&
                          _surfaceDraft.Points.Count >= 3;
            SurfaceDraftSnapshot addBefore = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TryAddPoint(hit.Construct, hit.LocalHit, extend, out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RecordSurfaceEdit(extend ? "Extend surface edge" : "Add surface point", addBefore);
        }

        private bool UseGeneratorSceneInput()
        {
            if (_surfaceBuilderTool == SurfaceBuilderTool.Path ||
                _surfaceBuilderTool == SurfaceBuilderTool.Circle ||
                _surfaceBuilderTool == SurfaceBuilderTool.Rotate ||
                _surfaceBuilderTool == SurfaceBuilderTool.Scale)
            {
                return true;
            }

            return _surfaceBuilderTool == SurfaceBuilderTool.Move &&
                   (_generatorDraft.HasActiveSelection ||
                    _generatorDraft.HasSharedAnchor ||
                    _pendingGeneratorSharedAnchorPick);
        }

        private void HandleGeneratorSceneInput()
        {
            if (Input.GetMouseButtonDown(1))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _pendingGeneratorSharedAnchorPick = false;
                if (TryOpenGeneratorPointContextMenu())
                    return;

                if (_generatorDraft.HasActiveSelection)
                {
                    _generatorDraft.ClearSelection();
                    _generatorMessage = "Generator selection cleared.";
                }
                else
                {
                    _generatorMessage = "Generator selection is clear. Use Clear to remove the draft.";
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();

            if (TryHandleGeneratorSharedAnchorClick())
                return;

            if (_surfaceBuilderTool == SurfaceBuilderTool.Move &&
                TryPickGeneratorHandle(_lastMouseGui, out DecorationEditAxis axis))
            {
                BeginGeneratorPointDrag(axis);
                return;
            }

            if (_surfaceBuilderTool == SurfaceBuilderTool.Rotate &&
                TryPickGeneratorRotateRing(_lastMouseGui, out DecorationEditAxis rotateAxis))
            {
                BeginGeneratorRotate(rotateAxis);
                return;
            }

            if (_surfaceBuilderTool == SurfaceBuilderTool.Scale &&
                TryPickGeneratorScaleHandle(_lastMouseGui))
            {
                BeginGeneratorScale();
                return;
            }

            if (TryPickGeneratorPoint(_lastMouseGui, out int pointIndex))
            {
                _generatorDraft.SelectPoint(pointIndex);
                _generatorMessage = "Generator point selected.";
                return;
            }

            if (_surfaceBuilderTool != SurfaceBuilderTool.Path &&
                _surfaceBuilderTool != SurfaceBuilderTool.Circle)
            {
                _generatorMessage = "Use Path or Shape to place generator points.";
                return;
            }

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                _generatorMessage = "Point at a real craft block to place a generator point.";
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            if (DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool))
            {
                Vector3 normal = LocalNormalFromHit(hit);
                if (!_generatorDraft.TrySetCircleCenter(hit.Construct, hit.LocalHit, normal, out _generatorMessage))
                {
                    InfoStore.Add(_generatorMessage);
                    return;
                }
            }
            else
            {
                if (!_generatorDraft.TryAddPathPoint(hit.Construct, hit.LocalHit, out _generatorMessage))
                {
                    InfoStore.Add(_generatorMessage);
                    return;
                }
            }

            InvalidateGeneratorPlan(_generatorMessage);
            RecordGeneratorEdit(
                DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool) ? "Place generator shape" : "Add generator path point",
                before);
        }

        private bool TryHandleSurfaceSharedAnchorClick()
        {
            if (_surfaceDraft.Settings.NearestAnchor)
                return false;

            if (_pendingSurfaceSharedAnchorPick)
            {
                _pendingSurfaceSharedAnchorPick = false;
                return TrySetSurfaceSharedAnchorFromPointer();
            }

            if (_surfaceBuilderTool != SurfaceBuilderTool.Move)
                return false;

            if (_surfaceDraft.HasSharedAnchor &&
                _surfaceDraft.SharedAnchorSelected &&
                TryPickSharedAnchorHandle(
                    _surfaceDraft.Construct,
                    _surfaceDraft.SharedAnchor,
                    _lastMouseGui,
                    out DecorationEditAxis axis,
                    out int sign))
            {
                BeginSharedAnchorDrag(generator: false, axis, sign);
                return true;
            }

            if (TryPickSurfaceSharedAnchor(_lastMouseGui))
            {
                _surfaceDraft.SelectSharedAnchor();
                _surfaceMessage = "Surface shared anchor selected.";
                return true;
            }

            return false;
        }

        private bool TryHandleGeneratorSharedAnchorClick()
        {
            if (_generatorSettings.NearestAnchor)
                return false;

            if (_pendingGeneratorSharedAnchorPick)
            {
                _pendingGeneratorSharedAnchorPick = false;
                return TrySetGeneratorSharedAnchorFromPointer();
            }

            if (_surfaceBuilderTool != SurfaceBuilderTool.Move)
                return false;

            if (_generatorDraft.HasSharedAnchor &&
                _generatorDraft.SharedAnchorSelected &&
                TryPickSharedAnchorHandle(
                    _generatorDraft.Construct,
                    _generatorDraft.SharedAnchor,
                    _lastMouseGui,
                    out DecorationEditAxis axis,
                    out int sign))
            {
                BeginSharedAnchorDrag(generator: true, axis, sign);
                return true;
            }

            if (TryPickGeneratorSharedAnchor(_lastMouseGui))
            {
                _generatorDraft.SelectSharedAnchor();
                _generatorMessage = "Generator shared anchor selected.";
                return true;
            }

            return false;
        }

        private bool TrySetSurfaceSharedAnchorFromPointer()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                _surfaceMessage = "Point at a real craft block to pick the shared surface anchor.";
                InfoStore.Add(_surfaceMessage);
                return true;
            }

            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TrySetSharedAnchor(hit.Construct, hit.Anchor, out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return true;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RecordSurfaceEdit("Set surface shared anchor", before);
            return true;
        }

        private bool TrySetGeneratorSharedAnchorFromPointer()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                _generatorMessage = "Point at a real craft block to pick the shared generator anchor.";
                InfoStore.Add(_generatorMessage);
                return true;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            if (!_generatorDraft.TrySetSharedAnchor(hit.Construct, hit.Anchor, out _generatorMessage))
            {
                InfoStore.Add(_generatorMessage);
                return true;
            }

            InvalidateGeneratorPlan(_generatorMessage);
            RecordGeneratorEdit("Set generator shared anchor", before);
            return true;
        }

        private bool TryOpenSurfaceContextMenu()
        {
            if (TryOpenSurfacePointSelectionContextMenu(requirePointUnderMouse: true))
                return true;
            if (TryOpenSurfacePointContextMenu())
                return true;
            if (TryOpenSurfaceEdgeContextMenu())
                return true;
            if (TryOpenSurfaceFaceContextMenu())
                return true;
            return TryOpenSurfacePointSelectionContextMenu(requirePointUnderMouse: false);
        }

        private bool TryOpenSurfacePointSelectionContextMenu(bool requirePointUnderMouse)
        {
            if (_surfaceDraft.ManualFaceSelectionCount < 2)
                return false;

            if (requirePointUnderMouse)
            {
                if (!TryPickSurfacePoint(_lastMouseGui, out int pointIndex) ||
                    !_surfaceDraft.ManualFaceSelection.Contains(pointIndex))
                {
                    return false;
                }
            }

            _generatorDraft.ClearSelection();
            _surfaceContextTargetKind = SurfaceContextTargetKind.SurfacePointSelection;
            _surfaceContextIndex = -1;
            _surfaceContextEdge = new SurfaceEdge(-1, -1);
            _surfaceContextRect = SurfaceContextRect(buttonCount: _surfaceDraft.ManualFaceSelectionCount == 3 ? 2 : 2);
            _surfaceMessage = _surfaceDraft.ManualFaceSelectionCount == 3
                ? "Selected points ready for a face."
                : "Selected points ready to connect.";
            return true;
        }

        private bool TryOpenSurfacePointContextMenu()
        {
            if (!TryPickSurfacePoint(_lastMouseGui, out int pointIndex))
                return false;

            _surfaceDraft.SelectPoint(pointIndex);
            _generatorDraft.ClearSelection();
            _surfaceContextTargetKind = SurfaceContextTargetKind.SurfacePoint;
            _surfaceContextIndex = pointIndex;
            _surfaceContextEdge = new SurfaceEdge(-1, -1);
            _surfaceContextRect = SurfaceContextRect(buttonCount: 3);
            _surfaceMessage = "Surface point selected.";
            return true;
        }

        private bool TryOpenSurfaceEdgeContextMenu()
        {
            if (!TryPickSurfaceEdge(_lastMouseGui, out SurfaceEdge edge))
                return false;

            _surfaceDraft.SelectEdge(edge.A, edge.B, preserveBridgeSelection: true);
            _generatorDraft.ClearSelection();
            _surfaceContextTargetKind = SurfaceContextTargetKind.SurfaceEdge;
            _surfaceContextIndex = -1;
            _surfaceContextEdge = edge;
            _surfaceContextRect = SurfaceContextRect(buttonCount: 4);
            _surfaceMessage = "Surface edge selected. Click a new point to extend.";
            return true;
        }

        private bool TryOpenSurfaceFaceContextMenu()
        {
            if (!TryPickSurfaceFace(_lastMouseGui, out int faceIndex))
                return false;

            _surfaceDraft.SelectFace(faceIndex);
            _generatorDraft.ClearSelection();
            SyncSharedPaintColorFromSurfaceFace(faceIndex);
            _surfaceContextTargetKind = SurfaceContextTargetKind.SurfaceFace;
            _surfaceContextIndex = faceIndex;
            _surfaceContextEdge = new SurfaceEdge(-1, -1);
            _surfaceContextRect = SurfaceContextRect(buttonCount: 3);
            _surfaceMessage = "Surface face selected.";
            return true;
        }

        private bool TryOpenGeneratorPointContextMenu()
        {
            if (!TryPickGeneratorPoint(_lastMouseGui, out int pointIndex))
                return false;

            _surfaceDraft.ClearSelection();
            _generatorDraft.SelectPoint(pointIndex);
            _surfaceContextTargetKind = SurfaceContextTargetKind.GeneratorPoint;
            _surfaceContextIndex = pointIndex;
            _surfaceContextEdge = new SurfaceEdge(-1, -1);
            _surfaceContextRect = SurfaceContextRect(
                _generatorDraft.UsesCenterTool ? 5 : 3);
            _generatorMessage = _generatorDraft.UsesCenterTool
                ? "Generator shape center selected."
                : "Path point selected.";
            return true;
        }

        private Rect SurfaceContextRect(int buttonCount)
        {
            Vector2 mouse = _lastMouseGui;
            float width = EsuHudLayout.Scale(148f);
            float height = EsuHudLayout.Scale(34f + Mathf.Max(1, buttonCount) * 26f);
            var rect = new Rect(mouse.x, mouse.y, width, height);
            rect.x = Mathf.Clamp(rect.x, EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(8f));
            rect.y = Mathf.Clamp(rect.y, EsuHudLayout.Scale(8f), Screen.height - height - EsuHudLayout.Scale(8f));
            return rect;
        }

        private void DrawSurfaceContextMenu()
        {
            if (_surfaceContextTargetKind == SurfaceContextTargetKind.None)
                return;

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                !_surfaceContextRect.Contains(current.mousePosition))
            {
                CloseSurfacePointContextMenu();
                return;
            }

            if (!SurfaceContextTargetValid())
            {
                CloseSurfacePointContextMenu();
                return;
            }

            GUI.Box(_surfaceContextRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(EsuHudLayout.PanelInnerRect(_surfaceContextRect, 5f));
            bool generator = _surfaceContextTargetKind == SurfaceContextTargetKind.GeneratorPoint;
            bool radial = generator && _generatorDraft.UsesCenterTool;
            bool pointSelection = _surfaceContextTargetKind == SurfaceContextTargetKind.SurfacePointSelection;
            bool surfaceEdge = _surfaceContextTargetKind == SurfaceContextTargetKind.SurfaceEdge;
            bool surfaceFace = _surfaceContextTargetKind == SurfaceContextTargetKind.SurfaceFace;
            GUILayout.Label(
                generator
                    ? (radial ? "Shape center" : "Path point")
                    : pointSelection
                        ? "Selected points"
                        : surfaceEdge
                        ? "Surface edge"
                        : surfaceFace
                            ? "Surface face"
                            : "Surface point",
                DecorationEditorTheme.SubHeader);

            if (!pointSelection &&
                GUILayout.Button(new GUIContent("Select", "Select this draft target."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SelectSurfaceContextTarget();
                CloseSurfacePointContextMenu();
            }

            if (!pointSelection && !surfaceEdge && !surfaceFace &&
                GUILayout.Button(new GUIContent("Move", "Move this draft point."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SelectSurfaceContextTarget();
                SetSurfaceBuilderTool(SurfaceBuilderTool.Move);
                CloseSurfacePointContextMenu();
            }

            if (pointSelection)
            {
                if (_surfaceDraft.ManualFaceSelectionCount == 2)
                {
                    if (GUILayout.Button(new GUIContent("Connect points", "Use the two selected points as an edge; click a third point to create a face."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        ConnectSelectedSurfacePoints();
                        CloseSurfacePointContextMenu();
                    }
                }
                else if (_surfaceDraft.ManualFaceSelectionCount == 3)
                {
                    if (GUILayout.Button(new GUIContent("Create face", "Create a triangle face from the three selected points."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        CreateFaceFromSelectedSurfacePoints();
                        CloseSurfacePointContextMenu();
                    }
                }

                if (GUILayout.Button(new GUIContent("Clear", "Clear the selected point set."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    _surfaceDraft.ClearSelection();
                    _surfaceMessage = "Surface point selection cleared.";
                    CloseSurfacePointContextMenu();
                }
            }

            if (!pointSelection && (surfaceEdge || surfaceFace) &&
                GUILayout.Button(new GUIContent("Preview", "Rebuild the surface decoration preview without placing it."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SelectSurfaceContextTarget();
                RebuildSurfacePreview(showMessage: true);
                CloseSurfacePointContextMenu();
            }

            if (!pointSelection && surfaceEdge)
            {
                bool previous = GUI.enabled;
                bool canBridge = _surfaceDraft.BridgeEdgeSelection.Count == 2;
                GUI.enabled = previous && canBridge;
                if (GUILayout.Button(new GUIContent("Bridge", "Create surface face(s) between the two Shift-selected edges."), DecorationEditorTheme.ToolButton(false, canBridge), GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    BridgeSurfaceEdges();
                    CloseSurfacePointContextMenu();
                }
                GUI.enabled = previous;
            }

            if (!pointSelection && radial)
            {
                if (GUILayout.Button(new GUIContent("Rotate", "Rotate this generator shape."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    SelectSurfaceContextTarget();
                    SetSurfaceBuilderTool(SurfaceBuilderTool.Rotate);
                    CloseSurfacePointContextMenu();
                }

                if (GUILayout.Button(new GUIContent("Scale", "Scale this generator shape."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    SelectSurfaceContextTarget();
                    SetSurfaceBuilderTool(SurfaceBuilderTool.Scale);
                    CloseSurfacePointContextMenu();
                }
            }

            if (!pointSelection && !surfaceEdge &&
                GUILayout.Button(new GUIContent("Delete", generator ? "Delete this generator point." : surfaceFace ? "Delete this surface face." : "Delete this draft point."), DecorationEditorTheme.Button, GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SelectSurfaceContextTarget();
                DeleteSurfaceActionTarget(generator ? SurfaceDraftActionTarget.Generator : SurfaceDraftActionTarget.Surface);
                CloseSurfacePointContextMenu();
            }

            GUILayout.EndArea();
        }

        private void ConnectSelectedSurfacePoints()
        {
            if (!_surfaceDraft.TrySelectManualPointEdge(out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            _generatorDraft.ClearSelection();
            SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
        }

        private void CreateFaceFromSelectedSurfacePoints()
        {
            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TryCreateFaceFromManualSelection(out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RebuildSurfacePreview(showMessage: true);
            RecordSurfaceEdit("Create surface face from selected points", before);
        }

        private bool SurfaceContextTargetValid()
        {
            switch (_surfaceContextTargetKind)
            {
                case SurfaceContextTargetKind.SurfacePointSelection:
                    return _surfaceDraft.ManualFaceSelectionCount == 2 ||
                           _surfaceDraft.ManualFaceSelectionCount == 3;
                case SurfaceContextTargetKind.SurfacePoint:
                    return _surfaceContextIndex >= 0 &&
                           _surfaceContextIndex < _surfaceDraft.Points.Count;
                case SurfaceContextTargetKind.SurfaceEdge:
                    return _surfaceContextEdge.IsValid &&
                           _surfaceContextEdge.A < _surfaceDraft.Points.Count &&
                           _surfaceContextEdge.B < _surfaceDraft.Points.Count;
                case SurfaceContextTargetKind.SurfaceFace:
                    return _surfaceContextIndex >= 0 &&
                           _surfaceContextIndex < _surfaceDraft.Faces.Count;
                case SurfaceContextTargetKind.GeneratorPoint:
                    return _surfaceContextIndex >= 0 &&
                           _surfaceContextIndex < _generatorDraft.PointCount;
                default:
                    return false;
            }
        }

        private void SelectSurfaceContextTarget()
        {
            if (!SurfaceContextTargetValid())
                return;

            switch (_surfaceContextTargetKind)
            {
                case SurfaceContextTargetKind.SurfacePoint:
                    SelectSurfaceDraftPointRow(_surfaceContextIndex);
                    SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
                    break;
                case SurfaceContextTargetKind.SurfaceEdge:
                    _surfaceDraft.SelectEdge(_surfaceContextEdge.A, _surfaceContextEdge.B);
                    _generatorDraft.ClearSelection();
                    SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
                    _surfaceMessage = "Surface edge selected. Click a new point to extend.";
                    break;
                case SurfaceContextTargetKind.SurfaceFace:
                    SelectSurfaceDraftFaceRow(_surfaceContextIndex);
                    SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
                    break;
                case SurfaceContextTargetKind.GeneratorPoint:
                    SelectGeneratorDraftPointRow(_surfaceContextIndex);
                    break;
            }
        }

        private bool IsSurfaceContextMenuAt(Vector2 mouse) =>
            _surfaceContextTargetKind != SurfaceContextTargetKind.None &&
            _surfaceContextRect.Contains(mouse);

        private void CloseSurfacePointContextMenu()
        {
            _surfaceContextTargetKind = SurfaceContextTargetKind.None;
            _surfaceContextIndex = -1;
            _surfaceContextEdge = new SurfaceEdge(-1, -1);
            _surfaceContextRect = Rect.zero;
        }

        private bool TryPickGeneratorHandle(Vector2 mouse, out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_generatorDraft.SelectedPoint < 0 ||
                _generatorDraft.Construct == null)
            {
                return false;
            }

            Vector3 center = _generatorDraft.PointAt(_generatorDraft.SelectedPoint);
            if (!TryProject(_generatorDraft.Construct, center, out Vector2 origin))
                return false;

            if (Vector2.Distance(mouse, origin) <= HandleRadiusPixels * 1.35f)
            {
                axis = DecorationEditAxis.Free;
                return true;
            }

            if (!TryProject(_generatorDraft.Construct, center + Vector3.right * HandleLength, out Vector2 xEnd) ||
                !TryProject(_generatorDraft.Construct, center + Vector3.up * HandleLength, out Vector2 yEnd) ||
                !TryProject(_generatorDraft.Construct, center + Vector3.forward * HandleLength, out Vector2 zEnd))
            {
                return false;
            }

            axis = DecorationEditMath.PickAxis(mouse, origin, xEnd, yEnd, zEnd, HandleRadiusPixels);
            return axis != DecorationEditAxis.None;
        }

        private bool TryPickGeneratorRotateRing(Vector2 mouse, out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_generatorDraft.Construct == null ||
                !_generatorDraft.UsesCenterTool ||
                !_generatorDraft.HasCircleCenter)
            {
                return false;
            }

            Vector3 center = _generatorDraft.CircleCenter;
            if (!TryProject(_generatorDraft.Construct, center, out Vector2 origin))
                return false;

            float radius = Mathf.Max(RotateGizmoRadius, _generatorSettings.CircleRadius);
            float best = HandleRadiusPixels * 1.35f;
            DecorationEditAxis pickedAxis = DecorationEditAxis.None;
            TryRing(DecorationEditAxis.X);
            TryRing(DecorationEditAxis.Y);
            TryRing(DecorationEditAxis.Z);
            axis = pickedAxis;
            return axis != DecorationEditAxis.None;

            void TryRing(DecorationEditAxis candidate)
            {
                BuildAxisBasis(DecorationEditMath.AxisVector(candidate), out Vector3 tangentA, out Vector3 tangentB);
                const int steps = 40;
                bool havePrevious = false;
                Vector2 previous = origin;
                for (int step = 0; step <= steps; step++)
                {
                    float angle = step * Mathf.PI * 2f / steps;
                    Vector3 local = center +
                                    (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                                    radius;
                    if (!TryProject(_generatorDraft.Construct, local, out Vector2 projected))
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
                            pickedAxis = candidate;
                        }
                    }

                    previous = projected;
                    havePrevious = true;
                }
            }
        }

        private bool TryPickGeneratorScaleHandle(Vector2 mouse)
        {
            if (_generatorDraft.Construct == null ||
                !_generatorDraft.UsesCenterTool ||
                !_generatorDraft.HasCircleCenter)
            {
                return false;
            }

            Vector3 center = _generatorDraft.CircleCenter;
            Vector3 radiusPoint = center + _generatorDraft.CircleTangentA * _generatorSettings.CircleRadius;
            if (!TryProject(_generatorDraft.Construct, center, out Vector2 origin) ||
                !TryProject(_generatorDraft.Construct, radiusPoint, out Vector2 end))
            {
                return false;
            }

            return DistanceToSegment(mouse, origin, end) <= HandleRadiusPixels ||
                   Vector2.Distance(mouse, end) <= HandleRadiusPixels * 1.4f;
        }

        private void BeginGeneratorPointDrag(DecorationEditAxis axis)
        {
            if (_generatorDraft.SelectedPoint < 0)
                return;

            _generatorDragAxis = axis;
            _generatorDragMouseStart = _lastMouseGui;
            _generatorDragPointStart = _generatorDraft.PointAt(_generatorDraft.SelectedPoint);
            _generatorDragSnapshotStart = CaptureGeneratorEditSnapshot();
            if (axis == DecorationEditAxis.Free)
                PrepareGeneratorFreeDragFrame(_generatorDragPointStart, _generatorDraft.Construct);
        }

        private void BeginGeneratorRotate(DecorationEditAxis axis)
        {
            if (_generatorDraft.Construct == null ||
                !_generatorDraft.HasCircleCenter)
            {
                return;
            }

            _generatorRotateDragAxis = axis;
            _generatorRotateDragMouseStart = _lastMouseGui;
            _generatorRotateSnapshotStart = CaptureGeneratorEditSnapshot();
            _generatorScaleSnapshotStart = null;
            TryProject(_generatorDraft.Construct, _generatorDraft.CircleCenter, out _generatorRotateDragCenterScreen);
            _generatorRotateDragStartVector = _lastMouseGui - _generatorRotateDragCenterScreen;
        }

        private void BeginGeneratorScale()
        {
            if (_generatorDraft.Construct == null ||
                !_generatorDraft.HasCircleCenter)
            {
                return;
            }

            _generatorRotateDragAxis = DecorationEditAxis.Free;
            _generatorRotateDragMouseStart = _lastMouseGui;
            _generatorScaleSnapshotStart = CaptureGeneratorEditSnapshot();
            _generatorRotateSnapshotStart = null;
            _generatorScaleRadiusStart = _generatorSettings.CircleRadius;
            TryProject(_generatorDraft.Construct, _generatorDraft.CircleCenter, out _generatorRotateDragCenterScreen);
            _generatorRotateDragStartVector = _lastMouseGui - _generatorRotateDragCenterScreen;
        }

        private void TryUpdateGeneratorRotate()
        {
            Vector2 current = _lastMouseGui - _generatorRotateDragCenterScreen;
            if (_generatorRotateDragStartVector.sqrMagnitude < 16f || current.sqrMagnitude < 16f)
                return;

            float degrees = DecorationEditMath.Snap(
                Vector2.SignedAngle(_generatorRotateDragStartVector, current),
                DecorationRotateSnapDegrees);
            DecorationGeneratorEditSnapshot baseline = _generatorRotateSnapshotStart;
            if (baseline?.Draft == null)
                return;

            _generatorDraft.Restore(baseline.Draft);
            if (_generatorDraft.TryRotateCircle(_generatorRotateDragAxis, degrees, out string message))
            {
                _generatorPlan = null;
                _generatorMessage = "Generator shape rotated.";
            }
            else if (!string.IsNullOrEmpty(message))
            {
                _generatorMessage = message;
            }
        }

        private void TryUpdateGeneratorScale(Vector2 mouseDelta)
        {
            if (_generatorDraft.Construct == null ||
                !_generatorDraft.HasCircleCenter)
            {
                return;
            }

            Vector2 current = _lastMouseGui - _generatorRotateDragCenterScreen;
            float start = Mathf.Max(1f, _generatorRotateDragStartVector.magnitude);
            float radius = Mathf.Max(
                0.05f,
                DecorationEditMath.Snap(_generatorScaleRadiusStart * (current.magnitude / start), DecorationScaleSnap));
            _generatorSettings.CircleRadius = radius;
            _generatorRadiusText = radius.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorPlan = null;
            _generatorMessage = "Generator shape scaled.";
        }

        private void TryUpdateGeneratorDrag(Vector2 mouseDelta)
        {
            if (_generatorDraft.SelectedPoint < 0 ||
                _generatorDraft.Construct == null)
            {
                return;
            }

            Vector3 next;
            if (_generatorDragAxis == DecorationEditAxis.Free)
            {
                Vector3 freeDelta = _generatorFreeDragCameraRight * (mouseDelta.x * _generatorFreeDragMetresPerPixel) -
                                    _generatorFreeDragCameraUp * (mouseDelta.y * _generatorFreeDragMetresPerPixel);
                next = DecorationEditMath.Snap(_generatorDragPointStart + freeDelta, DecorationMoveSnap);
            }
            else
            {
                if (!TryProject(_generatorDraft.Construct, _generatorDragPointStart, out Vector2 origin))
                    return;

                Vector3 axisVector = DecorationEditMath.AxisVector(_generatorDragAxis);
                if (!TryProject(_generatorDraft.Construct, _generatorDragPointStart + axisVector * HandleLength, out Vector2 axisEnd))
                    return;

                float axisDelta = DecorationEditMath.ProjectMouseDeltaToAxis(
                    mouseDelta,
                    origin,
                    axisEnd,
                    HandleLength);
                next = _generatorDragPointStart + axisVector * DecorationEditMath.Snap(axisDelta, DecorationMoveSnap);
            }

            if (_generatorDraft.TryMoveSelectedPoint(next, _generatorDraft.CircleNormal, DecorationMoveSnap, out string message))
            {
                _generatorMessage = "Generator point moved.";
                _generatorPlan = null;
            }
            else if (!string.IsNullOrEmpty(message))
            {
                _generatorMessage = message;
            }
        }

        private void PrepareGeneratorFreeDragFrame(Vector3 center, AllConstruct construct)
        {
            _generatorFreeDragCameraRight = Vector3.right;
            _generatorFreeDragCameraUp = Vector3.up;
            _generatorFreeDragMetresPerPixel = 0.01f;
            Camera camera = Camera.main;
            if (camera == null || construct == null)
                return;

            try
            {
                _generatorFreeDragCameraRight = construct.myTransform.InverseTransformDirection(camera.transform.right).normalized;
                _generatorFreeDragCameraUp = construct.myTransform.InverseTransformDirection(camera.transform.up).normalized;
            }
            catch
            {
                _generatorFreeDragCameraRight = Vector3.right;
                _generatorFreeDragCameraUp = Vector3.up;
            }

            if (TryProject(construct, center, out Vector2 origin) &&
                TryProject(construct, center + _generatorFreeDragCameraRight, out Vector2 rightEnd))
            {
                float pixelsPerMetre = Mathf.Max(18f, Vector2.Distance(origin, rightEnd));
                _generatorFreeDragMetresPerPixel = 1f / pixelsPerMetre;
            }
        }

        private bool TryPickGeneratorPoint(Vector2 mouse, out int pointIndex)
        {
            pointIndex = -1;
            if (_generatorDraft.Construct == null)
                return false;

            float best = SelectionRadiusPixels;
            for (int index = 0; index < _generatorDraft.PointCount; index++)
            {
                if (!TryProject(_generatorDraft.Construct, _generatorDraft.PointAt(index), out Vector2 screen))
                    continue;
                float distance = Vector2.Distance(mouse, screen);
                if (distance >= best)
                    continue;
                best = distance;
                pointIndex = index;
            }

            return pointIndex >= 0;
        }

        private bool TryPickSurfaceSharedAnchor(Vector2 mouse) =>
            !_surfaceDraft.Settings.NearestAnchor &&
            _surfaceDraft.HasSharedAnchor &&
            TryPickSharedAnchorCenter(_surfaceDraft.Construct, _surfaceDraft.SharedAnchor, mouse);

        private bool TryPickGeneratorSharedAnchor(Vector2 mouse) =>
            !_generatorSettings.NearestAnchor &&
            _generatorDraft.HasSharedAnchor &&
            TryPickSharedAnchorCenter(_generatorDraft.Construct, _generatorDraft.SharedAnchor, mouse);

        private bool TryPickSharedAnchorCenter(
            AllConstruct construct,
            Vector3i anchor,
            Vector2 mouse)
        {
            if (construct == null)
                return false;

            if (!TryProject(construct, ToVector3(anchor), out Vector2 screen))
                return false;

            return Vector2.Distance(mouse, screen) <= SelectionRadiusPixels * 1.35f;
        }

        private bool TryPickSharedAnchorHandle(
            AllConstruct construct,
            Vector3i anchorCell,
            Vector2 mouse,
            out DecorationEditAxis axis,
            out int sign)
        {
            axis = DecorationEditAxis.None;
            sign = 0;
            if (construct == null)
                return false;

            Vector3 anchor = ToVector3(anchorCell);
            if (!TryProject(construct, anchor, out Vector2 origin))
                return false;

            float best = HandleRadiusPixels;
            DecorationEditAxis pickedAxis = DecorationEditAxis.None;
            int pickedSign = 0;
            TryAnchorAxis(DecorationEditAxis.X, 1);
            TryAnchorAxis(DecorationEditAxis.X, -1);
            TryAnchorAxis(DecorationEditAxis.Y, 1);
            TryAnchorAxis(DecorationEditAxis.Y, -1);
            TryAnchorAxis(DecorationEditAxis.Z, 1);
            TryAnchorAxis(DecorationEditAxis.Z, -1);
            axis = pickedAxis;
            sign = pickedSign;
            return axis != DecorationEditAxis.None;

            void TryAnchorAxis(DecorationEditAxis candidate, int candidateSign)
            {
                Vector3 end = anchor + DecorationEditMath.AxisVector(candidate) * candidateSign * HandleLength;
                if (!TryProject(construct, end, out Vector2 projectedEnd))
                    return;

                float distance = DistanceToSegment(mouse, origin, projectedEnd);
                if (distance >= best)
                    return;

                best = distance;
                pickedAxis = candidate;
                pickedSign = candidateSign;
            }
        }

        private void BeginSharedAnchorDrag(
            bool generator,
            DecorationEditAxis axis,
            int sign)
        {
            if (axis == DecorationEditAxis.None || sign == 0)
                return;

            if (generator)
            {
                if (!_generatorDraft.HasSharedAnchor)
                    return;
            }
            else if (!_surfaceDraft.HasSharedAnchor)
            {
                return;
            }

            _sharedAnchorDragGenerator = generator;
            _sharedAnchorDragAxis = axis;
            _sharedAnchorDragSign = sign;
            _sharedAnchorDragMouseStart = _lastMouseGui;
            _sharedSurfaceAnchorDragSnapshotStart = null;
            _sharedGeneratorAnchorDragSnapshotStart = null;
            if (generator)
            {
                _sharedAnchorDragStart = _generatorDraft.SharedAnchor;
                _sharedGeneratorAnchorDragSnapshotStart = CaptureGeneratorEditSnapshot();
            }
            else
            {
                _sharedAnchorDragStart = _surfaceDraft.SharedAnchor;
                _sharedSurfaceAnchorDragSnapshotStart = CaptureSurfaceSnapshot();
            }
        }

        private void TryUpdateSharedAnchorDrag(Vector2 mouseDelta)
        {
            AllConstruct construct = _sharedAnchorDragGenerator
                ? _generatorDraft.Construct
                : _surfaceDraft.Construct;
            if (construct == null)
                return;

            if (!TryProject(construct, ToVector3(_sharedAnchorDragStart), out Vector2 origin))
                return;

            Vector3 axisVector = DecorationEditMath.AxisVector(_sharedAnchorDragAxis) * _sharedAnchorDragSign;
            Vector3 endLocal = ToVector3(_sharedAnchorDragStart) + axisVector * HandleLength;
            if (!TryProject(construct, endLocal, out Vector2 axisEnd))
                return;

            float projected = DecorationEditMath.ProjectMouseDeltaToAxis(
                mouseDelta,
                origin,
                axisEnd,
                HandleLength);
            int magnitude = Mathf.RoundToInt(Mathf.Abs(projected));
            if (magnitude <= 0)
                return;

            Vector3i next = _sharedAnchorDragStart + AxisShift(_sharedAnchorDragAxis, _sharedAnchorDragSign * magnitude);
            if (!HasBlock(construct, next))
            {
                SetSharedAnchorDragMessage("Shared anchor must be an existing craft block.");
                return;
            }

            if (_sharedAnchorDragGenerator)
            {
                if (_generatorDraft.TryMoveSharedAnchor(next, out string message))
                {
                    _generatorPlan = null;
                    _generatorMessage = "Generator shared anchor moved.";
                }
                else if (!string.IsNullOrEmpty(message))
                {
                    _generatorMessage = message;
                }
            }
            else
            {
                if (_surfaceDraft.TryMoveSharedAnchor(next, out string message))
                {
                    _surfacePlan = null;
                    _surfaceMessage = "Surface shared anchor moved.";
                }
                else if (!string.IsNullOrEmpty(message))
                {
                    _surfaceMessage = message;
                }
            }
        }

        private void CommitSharedAnchorDrag()
        {
            bool generator = _sharedAnchorDragGenerator;
            SurfaceDraftSnapshot surfaceBefore = _sharedSurfaceAnchorDragSnapshotStart;
            DecorationGeneratorEditSnapshot generatorBefore = _sharedGeneratorAnchorDragSnapshotStart;
            ClearSharedAnchorDragState();
            if (generator)
            {
                RecordGeneratorEdit("Move generator shared anchor", generatorBefore);
                RebuildGeneratorPreview(showMessage: false);
            }
            else
            {
                RecordSurfaceEdit("Move surface shared anchor", surfaceBefore);
                RebuildSurfacePreview(showMessage: false);
            }
        }

        private void ClearSharedAnchorDragState()
        {
            _sharedAnchorDragAxis = DecorationEditAxis.None;
            _sharedAnchorDragSign = 0;
            _sharedAnchorDragMouseStart = Vector2.zero;
            _sharedAnchorDragStart = default;
            _sharedSurfaceAnchorDragSnapshotStart = null;
            _sharedGeneratorAnchorDragSnapshotStart = null;
            _sharedAnchorDragGenerator = false;
        }

        private void SetSharedAnchorDragMessage(string message)
        {
            if (_sharedAnchorDragGenerator)
                _generatorMessage = message;
            else
                _surfaceMessage = message;
        }

        private static Vector3 LocalNormalFromHit(DecorationPointerHit hit)
        {
            if (hit == null)
                return Vector3.up;

            if (hit.Construct != null &&
                DecorationEditMath.IsFinite(hit.WorldNormal) &&
                hit.WorldNormal.sqrMagnitude > 0.0001f)
            {
                try
                {
                    Vector3 local = hit.Construct.myTransform.InverseTransformDirection(hit.WorldNormal);
                    if (DecorationEditMath.IsFinite(local) && local.sqrMagnitude > 0.0001f)
                        return local.normalized;
                }
                catch
                {
                    // Fall back to the hit offset below.
                }
            }

            return DecorationPointerProbe.TryGetLocalFaceNormal(hit.Anchor, hit.LocalHit, out Vector3 faceNormal)
                ? faceNormal
                : Vector3.up;
        }

        private bool TryPickSurfaceHandle(Vector2 mouse, out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_surfaceDraft.SelectionKind != SurfaceSelectionKind.Point ||
                _surfaceDraft.SelectedPoint < 0 ||
                _surfaceDraft.SelectedPoint >= _surfaceDraft.Points.Count ||
                _surfaceDraft.Construct == null)
            {
                return false;
            }

            Vector3 center = _surfaceDraft.Points[_surfaceDraft.SelectedPoint];
            if (!TryProject(_surfaceDraft.Construct, center, out Vector2 origin))
                return false;

            if (Vector2.Distance(mouse, origin) <= HandleRadiusPixels * 1.35f)
            {
                axis = DecorationEditAxis.Free;
                return true;
            }

            if (!TryProject(_surfaceDraft.Construct, center + Vector3.right * HandleLength, out Vector2 xEnd) ||
                !TryProject(_surfaceDraft.Construct, center + Vector3.up * HandleLength, out Vector2 yEnd) ||
                !TryProject(_surfaceDraft.Construct, center + Vector3.forward * HandleLength, out Vector2 zEnd))
            {
                return false;
            }

            axis = DecorationEditMath.PickAxis(mouse, origin, xEnd, yEnd, zEnd, HandleRadiusPixels);
            return axis != DecorationEditAxis.None;
        }

        private void BeginSurfacePointDrag(DecorationEditAxis axis)
        {
            if (_surfaceDraft.SelectedPoint < 0 ||
                _surfaceDraft.SelectedPoint >= _surfaceDraft.Points.Count)
            {
                return;
            }

            _surfaceDragAxis = axis;
            _surfaceDragMouseStart = _lastMouseGui;
            _surfaceDragPointStart = _surfaceDraft.Points[_surfaceDraft.SelectedPoint];
            _surfaceDragSnapshotStart = CaptureSurfaceSnapshot();
            if (axis == DecorationEditAxis.Free)
                PrepareSurfaceFreeDragFrame(_surfaceDragPointStart, _surfaceDraft.Construct);
        }

        private void TryUpdateSurfaceDrag(Vector2 mouseDelta)
        {
            if (_surfaceDraft.SelectedPoint < 0 ||
                _surfaceDraft.SelectedPoint >= _surfaceDraft.Points.Count ||
                _surfaceDraft.Construct == null)
            {
                return;
            }

            Vector3 next;
            if (_surfaceDragAxis == DecorationEditAxis.Free)
            {
                Vector3 freeDelta = _surfaceFreeDragCameraRight * (mouseDelta.x * _surfaceFreeDragMetresPerPixel) -
                                    _surfaceFreeDragCameraUp * (mouseDelta.y * _surfaceFreeDragMetresPerPixel);
                next = DecorationEditMath.Snap(_surfaceDragPointStart + freeDelta, DecorationMoveSnap);
            }
            else
            {
                if (!TryProject(_surfaceDraft.Construct, _surfaceDragPointStart, out Vector2 origin))
                    return;

                Vector3 axisVector = DecorationEditMath.AxisVector(_surfaceDragAxis);
                if (!TryProject(_surfaceDraft.Construct, _surfaceDragPointStart + axisVector * HandleLength, out Vector2 axisEnd))
                    return;

                float axisDelta = DecorationEditMath.ProjectMouseDeltaToAxis(
                    mouseDelta,
                    origin,
                    axisEnd,
                    HandleLength);
                next = _surfaceDragPointStart + axisVector * DecorationEditMath.Snap(axisDelta, DecorationMoveSnap);
            }

            if (_surfaceDraft.TryMovePoint(_surfaceDraft.SelectedPoint, next, DecorationMoveSnap, out string message))
            {
                _surfaceMessage = "Surface point moved.";
                _surfacePlan = null;
            }
            else if (!string.IsNullOrEmpty(message))
            {
                _surfaceMessage = message;
            }
        }

        private void PrepareSurfaceFreeDragFrame(Vector3 center, AllConstruct construct)
        {
            _surfaceFreeDragCameraRight = Vector3.right;
            _surfaceFreeDragCameraUp = Vector3.up;
            _surfaceFreeDragMetresPerPixel = 0.01f;
            Camera camera = Camera.main;
            if (camera == null || construct == null)
                return;

            try
            {
                _surfaceFreeDragCameraRight = construct.myTransform.InverseTransformDirection(camera.transform.right).normalized;
                _surfaceFreeDragCameraUp = construct.myTransform.InverseTransformDirection(camera.transform.up).normalized;
            }
            catch
            {
                _surfaceFreeDragCameraRight = Vector3.right;
                _surfaceFreeDragCameraUp = Vector3.up;
            }

            if (TryProject(construct, center, out Vector2 origin) &&
                TryProject(construct, center + _surfaceFreeDragCameraRight, out Vector2 rightEnd))
            {
                float pixelsPerMetre = Mathf.Max(18f, Vector2.Distance(origin, rightEnd));
                _surfaceFreeDragMetresPerPixel = 1f / pixelsPerMetre;
            }
        }

        private bool TryPickSurfacePoint(Vector2 mouse, out int pointIndex)
        {
            pointIndex = -1;
            if (_surfaceDraft.Construct == null)
                return false;

            float best = SelectionRadiusPixels;
            for (int index = 0; index < _surfaceDraft.Points.Count; index++)
            {
                if (!TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[index], out Vector2 screen))
                    continue;
                float distance = Vector2.Distance(mouse, screen);
                if (distance >= best)
                    continue;
                best = distance;
                pointIndex = index;
            }

            return pointIndex >= 0;
        }

        private bool TryPickSurfaceEdge(Vector2 mouse, out SurfaceEdge edge)
        {
            SurfaceEdge bestEdge = new SurfaceEdge(-1, -1);
            if (_surfaceDraft.Construct == null)
            {
                edge = bestEdge;
                return false;
            }

            float best = HandleRadiusPixels;
            for (int faceIndex = 0; faceIndex < _surfaceDraft.Faces.Count; faceIndex++)
            {
                SurfaceFace face = _surfaceDraft.Faces[faceIndex];
                TryEdge(face.A, face.B);
                TryEdge(face.B, face.C);
                TryEdge(face.C, face.A);
            }

            edge = bestEdge;
            return edge.IsValid;

            void TryEdge(int a, int b)
            {
                if (!TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[a], out Vector2 first) ||
                    !TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[b], out Vector2 second))
                {
                    return;
                }

                float distance = DistanceToSegment(mouse, first, second);
                if (distance >= best)
                    return;

                best = distance;
                bestEdge = new SurfaceEdge(a, b);
            }
        }

        private bool TryPickSurfaceFace(Vector2 mouse, out int faceIndex)
        {
            faceIndex = -1;
            if (_surfaceDraft.Construct == null)
                return false;

            for (int index = _surfaceDraft.Faces.Count - 1; index >= 0; index--)
            {
                SurfaceFace face = _surfaceDraft.Faces[index];
                if (!TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[face.A], out Vector2 a) ||
                    !TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[face.B], out Vector2 b) ||
                    !TryProject(_surfaceDraft.Construct, _surfaceDraft.Points[face.C], out Vector2 c))
                {
                    continue;
                }

                if (!PointInScreenTriangle(mouse, a, b, c))
                    continue;

                faceIndex = index;
                return true;
            }

            return false;
        }

        private bool RebuildSurfacePreview(bool showMessage)
        {
            if (!SurfaceDecorationPlanner.TryPlanWithSymmetry(
                    _surfaceDraft,
                    new ConstructSurfaceAnchorResolver(_surfaceDraft.Construct),
                    out _surfacePlan,
                    out string message))
            {
                _surfacePlan = null;
                _surfaceMessage = message;
                if (showMessage && !string.IsNullOrEmpty(message))
                    InfoStore.Add("Surface preview rejected: " + message);
                return false;
            }

            _surfaceMessage = message;
            if (showMessage)
                InfoStore.Add("Surface preview: " + message);
            return true;
        }

        private bool RebuildGeneratorPreview(bool showMessage)
        {
            if (!PrepareGeneratorSettings(out string reason))
            {
                _generatorPlan = null;
                _generatorMessage = reason;
                if (showMessage && !string.IsNullOrEmpty(reason))
                    InfoStore.Add("Generator preview rejected: " + reason);
                return false;
            }

            if (!DecorationGeneratorPlanner.TryPlanWithSymmetry(
                    _generatorDraft,
                    _generatorSettings,
                    new ConstructSurfaceAnchorResolver(_generatorDraft.Construct),
                    out _generatorPlan,
                    out string message))
            {
                _generatorPlan = null;
                _generatorMessage = message;
                if (showMessage && !string.IsNullOrEmpty(message))
                    InfoStore.Add("Generator preview rejected: " + message);
                return false;
            }

            _generatorMessage = message;
            if (showMessage)
                InfoStore.Add("Generator preview: " + message);
            return true;
        }

        private void PlaceSurfacePlan()
        {
            if (!RebuildSurfacePreview(showMessage: false) || _surfacePlan == null)
            {
                if (!string.IsNullOrEmpty(_surfaceMessage))
                    InfoStore.Add("Surface placement rejected: " + _surfaceMessage);
                return;
            }

            AllConstruct construct = _surfacePlan.Construct;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
            {
                InfoStore.Add("Surface placement rejected: decoration manager is unavailable.");
                return;
            }

            long requestedTotal = (long)decorations.DecorationCount + _surfacePlan.DecorationCount;
            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    _surfacePlan.DecorationCount,
                    out string compatibilityMessage))
            {
                InfoStore.Add(compatibilityMessage);
                return;
            }

            if (requestedTotal > AllConstructDecorations._limitPerPacketManager)
            {
                InfoStore.Add(
                    "Surface placement rejected: needs " +
                    _surfacePlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " decorations, but the manager has insufficient remaining capacity.");
                return;
            }

            var created = new List<Decoration>(_surfacePlan.DecorationCount);
            try
            {
                for (int index = 0; index < _surfacePlan.Placements.Count; index++)
                {
                    SurfaceDecorationPlacement placement = _surfacePlan.Placements[index];
                    Decoration decoration = decorations.NewDecoration(
                        placement.Anchor,
                        force: true,
                        playSound: index == 0,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a generated surface decoration.");

                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = placement.MeshGuid;
                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Orientation.Us = placement.Orientation;
                    decoration.Color.Us = placement.Color;
                    decoration.Changed();
                    created.Add(decoration);
                }
            }
            catch (Exception exception)
            {
                using (VanillaCompatibilityGuard.BeginSuppression("surface placement cleanup"))
                {
                    for (int index = created.Count - 1; index >= 0; index--)
                    {
                        try { created[index]?.Delete(); }
                        catch { }
                    }
                }

                InfoStore.Add("Surface placement failed and was rolled back: " + exception.Message);
                return;
            }

            int placed = created.Count;
            ClearSurfaceDraft(notify: false);
            FinishCreatedDecorations(
                construct,
                created,
                null,
                "Surface placed " + placed.ToString("N0", CultureInfo.InvariantCulture) + " decorations.");
            _tool = DecorationEditorTool.Move;
        }

        private void PlaceGeneratorPlan()
        {
            if (!RebuildGeneratorPreview(showMessage: false) || _generatorPlan == null)
            {
                if (!string.IsNullOrEmpty(_generatorMessage))
                    InfoStore.Add("Generator placement rejected: " + _generatorMessage);
                return;
            }

            AllConstruct construct = _generatorPlan.Construct;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
            {
                InfoStore.Add("Generator placement rejected: decoration manager is unavailable.");
                return;
            }

            long requestedTotal = (long)decorations.DecorationCount + _generatorPlan.DecorationCount;
            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    _generatorPlan.DecorationCount,
                    out string compatibilityMessage))
            {
                InfoStore.Add(compatibilityMessage);
                return;
            }

            if (requestedTotal > AllConstructDecorations._limitPerPacketManager)
            {
                InfoStore.Add(
                    "Generator placement rejected: needs " +
                    _generatorPlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " decorations, but the manager has insufficient remaining capacity.");
                return;
            }

            var created = new List<Decoration>(_generatorPlan.DecorationCount);
            try
            {
                for (int index = 0; index < _generatorPlan.Placements.Count; index++)
                {
                    DecorationGeneratorPlacement placement = _generatorPlan.Placements[index];
                    Decoration decoration = decorations.NewDecoration(
                        placement.Anchor,
                        force: true,
                        playSound: index == 0,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a generated decoration.");

                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = placement.MeshGuid;
                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Orientation.Us = placement.Orientation;
                    decoration.Color.Us = placement.Color;
                    decoration.MaterialReplacement.Us = placement.MaterialReplacement;
                    decoration.Changed();
                    created.Add(decoration);
                }
            }
            catch (Exception exception)
            {
                using (VanillaCompatibilityGuard.BeginSuppression("generator placement cleanup"))
                {
                    for (int index = created.Count - 1; index >= 0; index--)
                    {
                        try { created[index]?.Delete(); }
                        catch { }
                    }
                }

                InfoStore.Add("Generator placement failed and was rolled back: " + exception.Message);
                return;
            }

            int placed = created.Count;
            DecorationMeshCatalogEntry mesh = null;
            _meshByGuid.TryGetValue(_generatorSettings.MeshGuid, out mesh);
            ClearGeneratorDraft(notify: false);
            FinishCreatedDecorations(
                construct,
                created,
                mesh,
                "Generator placed " + placed.ToString("N0", CultureInfo.InvariantCulture) + " decorations.");
            _tool = DecorationEditorTool.Move;
        }

        private void ClearSurfaceDraft(bool notify)
        {
            SurfaceDraftSnapshot before = notify ? CaptureSurfaceSnapshot() : null;
            _surfaceDraft.Clear();
            _surfacePlan = null;
            _surfaceDragAxis = DecorationEditAxis.None;
            if (!_sharedAnchorDragGenerator)
                ClearSharedAnchorDragState();
            _pendingSurfaceSharedAnchorPick = false;
            CloseSurfacePointContextMenu();
            _surfaceMessage = "Click three block-surface points to seed a freeform surface.";
            if (notify)
            {
                RecordSurfaceEdit("Clear surface draft", before);
                InfoStore.Add("Surface draft cleared.");
            }
        }

        private void ClearGeneratorDraft(bool notify)
        {
            DecorationGeneratorEditSnapshot before = notify ? CaptureGeneratorEditSnapshot() : null;
            _generatorDraft.Clear();
            _generatorPlan = null;
            _generatorDragAxis = DecorationEditAxis.None;
            _generatorDragSnapshotStart = null;
            _generatorRotateDragAxis = DecorationEditAxis.None;
            _generatorRotateSnapshotStart = null;
            _generatorScaleSnapshotStart = null;
            if (_sharedAnchorDragGenerator)
                ClearSharedAnchorDragState();
            _pendingGeneratorSharedAnchorPick = false;
            CloseSurfacePointContextMenu();
            _generatorMessage = "Generator draft cleared. Draw a path or place a shape center.";
            if (notify)
            {
                RecordGeneratorEdit("Clear generator draft", before);
                InfoStore.Add("Generator draft cleared.");
            }
        }

        private void DeleteSurfaceSelection()
        {
            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TryDeleteSelection(out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            _surfacePlan = null;
            CloseSurfacePointContextMenu();
            RecordSurfaceEdit("Delete surface selection", before);
            InfoStore.Add(_surfaceMessage);
        }

        private void DeleteGeneratorSelection()
        {
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            if (!_generatorDraft.TryDeleteSelection(out _generatorMessage))
            {
                InfoStore.Add(_generatorMessage);
                return;
            }

            _generatorPlan = null;
            CloseSurfacePointContextMenu();
            RecordGeneratorEdit("Delete generator selection", before);
            InfoStore.Add(_generatorMessage);
        }

        private void BridgeSurfaceEdges()
        {
            SurfaceDraftSnapshot before = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TryBridgeSelectedEdges(out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RecordSurfaceEdit("Bridge surface edges", before);
            InfoStore.Add(_surfaceMessage);
        }

        private void InvalidateSurfacePlan(string message)
        {
            _surfacePlan = null;
            if (!string.IsNullOrEmpty(message))
                _surfaceMessage = message;
        }

        private SurfaceDraftSnapshot CaptureSurfaceSnapshot() =>
            _surfaceDraft.CreateSnapshot();

        private DecorationGeneratorEditSnapshot CaptureGeneratorEditSnapshot() =>
            new DecorationGeneratorEditSnapshot(
                _generatorDraft.CreateSnapshot(),
                new DecorationGeneratorSettingsSnapshot(_generatorSettings));

        private void RecordSurfaceEdit(string label, SurfaceDraftSnapshot before)
        {
            SurfaceDraftSnapshot after = CaptureSurfaceSnapshot();
            if (before == null || before.SameAs(after))
                return;

            _history.Record(new SurfaceDraftHistoryCommand(label, before, after));
        }

        private void RecordGeneratorEdit(string label, DecorationGeneratorEditSnapshot before)
        {
            DecorationGeneratorEditSnapshot after = CaptureGeneratorEditSnapshot();
            if (before == null || before.SameAs(after))
                return;

            _history.Record(new GeneratorDraftHistoryCommand(label, before, after));
        }

        private void RecordSurfaceBuilderStyleEdit(
            string label,
            SurfaceDraftSnapshot surfaceBefore,
            DecorationGeneratorEditSnapshot generatorBefore)
        {
            SurfaceDraftSnapshot surfaceAfter = CaptureSurfaceSnapshot();
            DecorationGeneratorEditSnapshot generatorAfter = CaptureGeneratorEditSnapshot();
            bool surfaceChanged = surfaceBefore != null && !surfaceBefore.SameAs(surfaceAfter);
            bool generatorChanged = generatorBefore != null && !generatorBefore.SameAs(generatorAfter);
            if (!surfaceChanged && !generatorChanged)
                return;

            _history.Record(new SurfaceBuilderStyleHistoryCommand(
                label,
                surfaceBefore,
                surfaceAfter,
                generatorBefore,
                generatorAfter));
        }

        internal bool TryRestoreSurfaceDraftHistory(SurfaceDraftSnapshot snapshot, string context)
        {
            _surfaceDraft.Restore(snapshot);
            _surfacePlan = null;
            _generatorSettings.ColorIndex = _surfaceDraft.Settings.ColorIndex;
            _generatorPlan = null;
            _surfaceDragAxis = DecorationEditAxis.None;
            if (!_sharedAnchorDragGenerator)
                ClearSharedAnchorDragState();
            _pendingSurfaceSharedAnchorPick = false;
            CloseSurfacePointContextMenu();
            SyncSurfaceTextFromSettings();
            SyncGeneratorTextFromSettings();
            _surfaceMessage = string.IsNullOrEmpty(context)
                ? "Surface draft restored."
                : context + ".";
            return true;
        }

        internal bool TryRestoreGeneratorDraftHistory(
            DecorationGeneratorEditSnapshot snapshot,
            string context)
        {
            if (snapshot == null)
                return false;

            snapshot.Settings?.Restore(_generatorSettings);
            _generatorDraft.Restore(snapshot.Draft);
            _surfaceExtraTool = _generatorDraft.Tool == SurfaceExtraTool.None
                ? SurfaceExtraTool.Path
                : _generatorDraft.Tool;
            _surfaceBuilderTool = DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool)
                ? SurfaceBuilderTool.Circle
                : SurfaceBuilderTool.Path;
            s_surfaceExtraTool = _surfaceExtraTool;
            s_surfaceBuilderTool = _surfaceBuilderTool;
            _generatorPlan = null;
            _surfaceDraft.Settings.ColorIndex = _generatorSettings.ColorIndex;
            _surfacePlan = null;
            _generatorDragAxis = DecorationEditAxis.None;
            _generatorRotateDragAxis = DecorationEditAxis.None;
            if (_sharedAnchorDragGenerator)
                ClearSharedAnchorDragState();
            _pendingGeneratorSharedAnchorPick = false;
            _generatorDragSnapshotStart = null;
            _generatorRotateSnapshotStart = null;
            _generatorScaleSnapshotStart = null;
            CloseSurfacePointContextMenu();
            SyncGeneratorTextFromSettings();
            SyncSurfaceTextFromSettings();
            _generatorMessage = string.IsNullOrEmpty(context)
                ? "Generator draft restored."
                : context + ".";
            return true;
        }

        internal bool TryRestoreSurfaceBuilderStyleHistory(
            SurfaceDraftSnapshot surfaceSnapshot,
            DecorationGeneratorEditSnapshot generatorSnapshot,
            string context)
        {
            if (surfaceSnapshot == null || generatorSnapshot == null)
                return false;

            _surfaceDraft.Restore(surfaceSnapshot);
            generatorSnapshot.Settings?.Restore(_generatorSettings);
            _generatorDraft.Restore(generatorSnapshot.Draft);
            _surfacePlan = null;
            _generatorPlan = null;
            _surfaceDragAxis = DecorationEditAxis.None;
            _generatorDragAxis = DecorationEditAxis.None;
            _generatorRotateDragAxis = DecorationEditAxis.None;
            ClearSharedAnchorDragState();
            _pendingSurfaceSharedAnchorPick = false;
            _pendingGeneratorSharedAnchorPick = false;
            CloseSurfacePointContextMenu();
            SyncSurfaceTextFromSettings();
            SyncGeneratorTextFromSettings();
            string message = string.IsNullOrEmpty(context)
                ? "Surface Builder style restored."
                : context + ".";
            _surfaceMessage = message;
            _generatorMessage = message;
            return true;
        }

        private void SyncSurfaceTextFromSettings()
        {
            _surfaceThicknessText = FormatFloat(_surfaceDraft.Settings.FaceThickness);
            _surfaceColorText = _surfaceDraft.Settings.ColorIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsControlHeld() =>
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        private static bool IsShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);

        private void PlacePendingSymmetryPlane()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                InfoStore.Add("Point at a craft block to place the symmetry plane.");
                return;
            }

            Vector3i cell = hit.Anchor;
            if (!DecoLimitLifter.EsuSymmetry.TryPlacePending(
                    hit.Construct,
                    cell,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            InfoStore.Add("Symmetry plane placed.");
        }

        private void HandleEditorKeybinds()
        {
            if (_textInputFocused ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
                return;

            if (TryHandleEsuNumberShortcut())
                return;

            bool undo = false;
            bool redo = false;
            try
            {
                undo = SerializationHudKeyMap.Instance.Bool(
                    SerializationHudKeyInput.UndoDecorationEdit,
                    KeyInputEventType.Down);
                redo = SerializationHudKeyMap.Instance.Bool(
                    SerializationHudKeyInput.RedoDecorationEdit,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Fallback below keeps the editor usable if the key map is not ready.
            }

            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            undo = undo || (control && !shift && Input.GetKeyDown(KeyCode.Z));
            redo = redo || (control && Input.GetKeyDown(KeyCode.Y)) ||
                   (control && shift && Input.GetKeyDown(KeyCode.Z));

            if (undo)
                UndoEdit();
            else if (redo)
                RedoEdit();
        }

        private bool TryHandleEsuNumberShortcut()
        {
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(1))
                return CycleCreateSelectShortcut();
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(2))
                return CycleTransformToolShortcut();
            if (DecoLimitLifter.EsuInputState.IsEsuNumberShortcutDown(3))
                return CycleCommonViewModeShortcut();

            return false;
        }

        private bool CycleCreateSelectShortcut()
        {
            DecorationEditorInputScope.ClaimBuildInputForFrames();
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            if (IsSurfaceMode)
            {
                SurfaceBuilderTool next =
                    _surfaceBuilderTool == SurfaceBuilderTool.Draw ? SurfaceBuilderTool.Path :
                    _surfaceBuilderTool == SurfaceBuilderTool.Path ? SurfaceBuilderTool.Circle :
                    SurfaceBuilderTool.Draw;
                SetSurfaceBuilderTool(next);
                InfoStore.Add("Surface Builder tool: " + next + ".");
                return true;
            }

            if (_tool != DecorationEditorTool.Select)
            {
                SetActiveTool(DecorationEditorTool.Select);
                _selectionMode = DecorationSelectionMode.Single;
                CancelBoxSelection();
                InfoStore.Add("Selection: Single.");
                return true;
            }

            _selectionMode = _selectionMode == DecorationSelectionMode.Single
                ? DecorationSelectionMode.Box
                : DecorationSelectionMode.Single;
            if (_selectionMode == DecorationSelectionMode.Single)
                CancelBoxSelection();
            InfoStore.Add("Selection: " + (_selectionMode == DecorationSelectionMode.Single ? "Single." : "Box."));
            return true;
        }

        private bool CycleTransformToolShortcut()
        {
            DecorationEditorInputScope.ClaimBuildInputForFrames();
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            if (IsSurfaceMode)
            {
                SurfaceBuilderTool next =
                    _surfaceBuilderTool == SurfaceBuilderTool.Move ? SurfaceBuilderTool.Rotate :
                    _surfaceBuilderTool == SurfaceBuilderTool.Rotate ? SurfaceBuilderTool.Scale :
                    _surfaceBuilderTool == SurfaceBuilderTool.Scale ? SurfaceBuilderTool.Move :
                    SurfaceBuilderTool.Move;
                SetSurfaceBuilderTool(next);
                InfoStore.Add("Surface Builder transform: " + next + ".");
                return true;
            }

            DecorationEditorTool tool =
                _tool == DecorationEditorTool.Move ? DecorationEditorTool.Rotate :
                _tool == DecorationEditorTool.Rotate ? DecorationEditorTool.Scale :
                _tool == DecorationEditorTool.Scale ? DecorationEditorTool.Move :
                DecorationEditorTool.Move;
            SetActiveTool(tool);
            InfoStore.Add("Decoration tool: " + tool + ".");
            return true;
        }

        private bool CycleCommonViewModeShortcut()
        {
            DecorationEditorInputScope.ClaimBuildInputForFrames();
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            DecorationEditorViewMode next =
                _viewMode == DecorationEditorViewMode.Mixed ? DecorationEditorViewMode.Wireframe :
                _viewMode == DecorationEditorViewMode.Wireframe ? DecorationEditorViewMode.DecorationOnly :
                _viewMode == DecorationEditorViewMode.DecorationOnly ? DecorationEditorViewMode.Normal :
                DecorationEditorViewMode.Mixed;
            SelectViewMode(next);
            return true;
        }

        private void UndoEdit()
        {
            CancelPlacement();
            CloseDecorationContextMenu();
            if (!_history.Undo(this))
            {
                InfoStore.Add("No Decoration Edit action to undo.");
                return;
            }

            UpdateDirtyFromSelection();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add("Decoration Edit undo.");
        }

        private void RedoEdit()
        {
            CancelPlacement();
            CloseDecorationContextMenu();
            if (!_history.Redo(this))
            {
                InfoStore.Add("No Decoration Edit action to redo.");
                return;
            }

            UpdateDirtyFromSelection();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add("Decoration Edit redo.");
        }

        internal bool TryRestoreHistorySnapshot(
            AllConstruct construct,
            Decoration decoration,
            DecorationEditSnapshot snapshot,
            string context)
        {
            if (decoration == null || decoration.IsDeleted || snapshot == null)
            {
                InfoStore.Add($"{context}: decoration no longer exists.");
                return false;
            }

            if (!snapshot.TryRestore(decoration))
            {
                InfoStore.Add($"{context}: FTD rejected the tether restore.");
                return false;
            }

            SelectFromHistory(decoration, construct, createdInSession: false);
            return true;
        }

        internal bool TryRestoreHistorySnapshots(
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] snapshots,
            int primaryIndex,
            string context)
        {
            if (decorations == null ||
                snapshots == null ||
                decorations.Length == 0 ||
                decorations.Length != snapshots.Length)
            {
                InfoStore.Add($"{context}: mirrored edit history is incomplete.");
                return false;
            }

            var rollback = new DecorationEditSnapshot[decorations.Length];
            for (int index = 0; index < decorations.Length; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null || decoration.IsDeleted || snapshots[index] == null)
                {
                    InfoStore.Add($"{context}: a mirrored decoration no longer exists.");
                    return false;
                }

                rollback[index] = new DecorationEditSnapshot(decoration);
            }

            for (int index = 0; index < decorations.Length; index++)
            {
                if (snapshots[index].TryRestore(decorations[index]))
                    continue;

                for (int rollbackIndex = 0; rollbackIndex < decorations.Length; rollbackIndex++)
                    rollback[rollbackIndex].TryRestore(decorations[rollbackIndex]);
                InfoStore.Add($"{context}: FTD rejected a mirrored tether restore.");
                return false;
            }

            int selectedIndex = Mathf.Clamp(primaryIndex, 0, decorations.Length - 1);
            SelectFromHistory(decorations[selectedIndex], construct, createdInSession: false);
            return true;
        }

        internal bool TryUndoCreatedDecoration(
            AllConstruct construct,
            ref Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted)
            {
                _selected = null;
                _selectedConstruct = null;
                _snapshot = null;
                _selectedCreatedInSession = false;
                _dirty = false;
                _selection.Clear();
                return true;
            }

            try
            {
                decoration.Delete();
                _transactions.UnmarkCreated(decoration);
                decoration = null;
                _selected = null;
                _selectedConstruct = null;
                _snapshot = null;
                _selectedCreatedInSession = false;
                _dirty = false;
                _selection.Clear();
                ResetInspectorFields();
                return true;
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation undo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Undo failed while deleting the newly created decoration.");
                return false;
            }
        }

        internal bool TryRedoCreatedDecoration(
            AllConstruct construct,
            DecorationEditSnapshot snapshot,
            out Decoration decoration)
        {
            decoration = null;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null || snapshot == null)
            {
                InfoStore.Add("Redo failed because the decoration manager is unavailable.");
                return false;
            }

            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    1,
                    out string compatibilityMessage))
            {
                InfoStore.Add("Redo failed: " + compatibilityMessage);
                return false;
            }

            decoration = decorations.NewDecoration(
                snapshot.TetherPoint,
                force: true,
                playSound: false,
                forceEvenIfMaxReached: true);
            if (decoration == null)
            {
                InfoStore.Add("Redo failed because FTD rejected the recreated decoration.");
                return false;
            }

            if (!snapshot.TryRestore(decoration))
            {
                using (VanillaCompatibilityGuard.BeginSuppression("redo restore cleanup"))
                {
                    try { decoration.Delete(); }
                    catch { /* The restore failure is the actionable result. */ }
                }
                InfoStore.Add("Redo failed because FTD rejected the recreated decoration.");
                return false;
            }

            _transactions.MarkCreated(decoration);
            SelectFromHistory(decoration, construct, createdInSession: true);
            return true;
        }

        internal bool TryUndoDeletedDecoration(
            AllConstruct construct,
            DecorationEditSnapshot deleted,
            DecorationEditSnapshot original,
            bool createdInSession,
            out Decoration decoration)
        {
            decoration = null;
            if (createdInSession)
                return TryRedoCreatedDecoration(construct, deleted, out decoration);

            if (!TryCreateDecorationFromSnapshot(
                    construct,
                    deleted,
                    "Delete undo",
                    out decoration))
            {
                return false;
            }

            if (original != null)
                _transactions.TrackEdit(decoration, original);
            UntrackDeletedDecoration(original);
            SelectFromHistory(decoration, construct, createdInSession: false);
            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return true;
        }

        internal bool TryRedoDeletedDecoration(
            AllConstruct construct,
            ref Decoration decoration,
            DecorationEditSnapshot deleted,
            DecorationEditSnapshot original,
            bool createdInSession)
        {
            if (decoration == null || decoration.IsDeleted)
            {
                InfoStore.Add("Delete redo failed because the decoration no longer exists.");
                return false;
            }

            try
            {
                decoration.Delete();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration delete redo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Delete redo failed.");
                return false;
            }

            if (createdInSession)
                _transactions.UnmarkCreated(decoration);
            else
                TrackDeletedDecoration(construct, original, deleted, decoration);

            ClearDeletedSelection(decoration);
            decoration = null;
            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return true;
        }

        private void SelectFromHistory(
            Decoration decoration,
            AllConstruct construct,
            bool createdInSession)
        {
            if (decoration == null || decoration.IsDeleted)
                return;

            _selected = decoration;
            _selectedConstruct = construct;
            _snapshot = _transactions.GetOriginal(decoration) ?? new DecorationEditSnapshot(decoration);
            _selectedCreatedInSession = createdInSession || _transactions.IsCreated(decoration);
            _dragAxis = DecorationEditAxis.None;
            _dragSnapshotStart = null;
            _dragSymmetryFollow = null;
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
            _anchorDragSymmetryFollow = null;
            _selection.Clear();
            _selection.Add(decoration);
            if (_meshByGuid.TryGetValue(decoration.MeshGuid.Us, out DecorationMeshCatalogEntry entry))
                _selectedMesh = entry;
            _tool = DecorationEditorTool.Move;
            UpdateDirtyFromSelection();
            ResetInspectorFields();
        }

        private void RefreshDecorationCache(bool force)
        {
            MainConstruct main = _build.GetCC();
            float now = Time.unscaledTime;
            if (!force &&
                ReferenceEquals(main, _currentMainConstruct) &&
                now < _nextLightRefresh)
            {
                return;
            }

            _currentMainConstruct = main;
            _nextLightRefresh = now + 1f;
            _hits.Clear();
            _outlinerRows.Clear();
            if (main == null)
                return;

            var constructs = new List<AllConstruct>();
            main.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(constructs);
            for (int constructIndex = 0; constructIndex < constructs.Count; constructIndex++)
            {
                AllConstruct construct = constructs[constructIndex];
                var decorations = construct?.Decorations as AllConstructDecorations;
                if (decorations == null)
                    continue;

                List<Decoration> list = decorations.DecorationList
                    .Where(decoration => decoration != null && !decoration.IsDeleted)
                    .ToList();
                foreach (Decoration decoration in list)
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                string constructLabel = ConstructLabel(construct, constructIndex);
                string constructKey = ConstructKey(construct, constructIndex);
                bool collapsed = _collapsedConstructs.Contains(constructKey);
                _outlinerRows.Add(OutlinerRow.ForConstruct(
                    construct,
                    constructKey,
                    (collapsed ? "▸ " : "▾ ") + constructLabel,
                    $"{decorations.DecorationCount:N0}/100k",
                    $"{constructLabel} {decorations.DecorationCount}"));

                if (collapsed)
                {
                    AddDecorationHits(construct, list);
                    continue;
                }

                if (_showTetherPins)
                {
                    foreach (IGrouping<string, Decoration> group in list
                                 .GroupBy(decoration => FormatTether(decoration.TetherPoint.Us))
                                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        _outlinerRows.Add(OutlinerRow.ForTether(
                            construct,
                            group.Key,
                            $"{group.Count():N0}",
                            $"{constructLabel} {group.Key}"));

                        foreach (Decoration decoration in OrderedDecorations(group))
                            AddOutlinerDecorationRow(construct, constructLabel, group.Key, decoration, 2);
                    }
                }
                else
                {
                    foreach (Decoration decoration in OrderedDecorations(list))
                        AddOutlinerDecorationRow(
                            construct,
                            constructLabel,
                            FormatTether(decoration.TetherPoint.Us),
                            decoration,
                            1);
                }

                AddDecorationHits(construct, list);
            }
        }

        private void ToggleConstructCollapse(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!_collapsedConstructs.Add(key))
                _collapsedConstructs.Remove(key);
        }

        private static string ConstructKey(AllConstruct construct, int constructIndex)
        {
            if (construct == null)
                return "construct:null:" + constructIndex.ToString(CultureInfo.InvariantCulture);

            return construct.GetHashCode().ToString(CultureInfo.InvariantCulture) + ":" +
                   constructIndex.ToString(CultureInfo.InvariantCulture);
        }

        private IEnumerable<Decoration> OrderedDecorations(IEnumerable<Decoration> decorations) =>
            decorations
                .Where(decoration => decoration != null && !decoration.IsDeleted)
                .OrderBy(decoration => MeshName(decoration.MeshGuid.Us), StringComparer.OrdinalIgnoreCase)
                .ThenBy(decoration => decoration.MeshGuid.Us);

        private void AddOutlinerDecorationRow(
            AllConstruct construct,
            string constructLabel,
            string tether,
            Decoration decoration,
            int depth)
        {
            string meshName = MeshName(decoration.MeshGuid.Us);
            string guidPrefix = decoration.MeshGuid.Us.ToString("N").Substring(0, 8);
            string detail = $"c{decoration.Color.Us} | {guidPrefix}";
            _outlinerRows.Add(OutlinerRow.ForDecoration(
                construct,
                decoration,
                meshName,
                detail,
                $"{constructLabel} {tether} {meshName} {decoration.MeshGuid.Us:D} {decoration.Color.Us} {decoration.MaterialReplacement.Us:D}",
                depth));
        }

        private void AddDecorationHits(AllConstruct construct, IEnumerable<Decoration> decorations)
        {
            foreach (Decoration decoration in decorations)
            {
                Vector3 local = GetDecorationLocalCenter(decoration);
                if (!TryProject(construct, local, out Vector2 screenPoint))
                    continue;
                _hits.Add(new DecorationHit(decoration, construct, local, screenPoint));
            }
        }

        private void RefreshForecast(bool force)
        {
            MainConstruct main = _build.GetCC();
            float now = Time.unscaledTime;
            if (!force && now < _nextForecastRefresh)
                return;

            _nextForecastRefresh = now + 0.25f;
            if (main == null)
            {
                _forecast = null;
                _lastUsage = null;
                return;
            }

            try
            {
                _lastUsage = DecorationUsageSnapshot.Capture(main);
                SerializationTelemetry.TryGetHistory(
                    main,
                    out CraftSerializationSnapshot loaded,
                    out CraftSerializationSnapshot saved,
                    out CraftSerializationSnapshot measured);
                _forecast = SerializationForecastCalculator.Calculate(
                    main,
                    _lastUsage,
                    loaded,
                    saved,
                    measured);
            }
            catch
            {
                _forecast = null;
                _lastUsage = null;
            }
        }

        private void DrawWorldOverlay()
        {
            DrawFocusDecorationHints();
            DrawNearestHint();
            DrawPlacementGhost();
            DrawSurfaceOverlay();
            DrawGeneratorOverlay();
            DrawSymmetryOverlay();
            DrawSelectedSetHints();
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            Vector3 centerLocal = GetDecorationLocalCenter(_selected);
            Vector3 anchorLocal = ToVector3(_selected.TetherPoint.Us);
            Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 anchorWorld = _selectedConstruct.SafeLocalToGlobal(anchorLocal);

            float selectedScale = _viewMode == DecorationEditorViewMode.DecorationOnly
                ? 1.45f
                : _viewMode == DecorationEditorViewMode.Wireframe ? 1.2f : 1f;
            float selectedWidth = _viewMode == DecorationEditorViewMode.Normal ? 2f : 3f;
            if (_tool == DecorationEditorTool.Anchor)
                DrawSelectedAnchorConnections(_selected.TetherPoint.Us, anchorWorld);
            DecorationEditorOverlay.Line(anchorWorld, centerWorld, new Color(1f, 0.35f, 1f, 1f), selectedWidth);
            DecorationEditorOverlay.Circle(centerWorld, 0.24f * selectedScale, Color.yellow, Vector3.up, selectedWidth, 24);
            DecorationEditorOverlay.Cross(centerWorld, 0.32f * selectedScale, Color.yellow, selectedWidth);

            bool multiTransform = TryGetMultiSelectionPivot(out Vector3 multiPivot);
            Vector3 gizmoCenterLocal = multiTransform ? multiPivot : centerLocal;
            if (multiTransform)
            {
                Vector3 pivotWorld = _selectedConstruct.SafeLocalToGlobal(gizmoCenterLocal);
                DecorationEditorOverlay.Circle(pivotWorld, 0.28f * selectedScale, new Color(0.1f, 0.95f, 1f, 1f), Vector3.up, selectedWidth, 24);
                DecorationEditorOverlay.Cross(pivotWorld, 0.34f * selectedScale, new Color(0.1f, 0.95f, 1f, 1f), selectedWidth);
            }

            if (_tool == DecorationEditorTool.Move)
            {
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.X, multiTransform);
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.Y, multiTransform);
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.Z, multiTransform);
                if (_dragAxis == DecorationEditAxis.Free)
                    DecorationEditorOverlay.Circle(_selectedConstruct.SafeLocalToGlobal(gizmoCenterLocal), 0.34f * selectedScale, Color.yellow, Vector3.up, 4f, 28);
            }
            else if (_tool == DecorationEditorTool.Rotate)
            {
                DrawRotateGizmo(gizmoCenterLocal, multiTransform);
            }
            else if (_tool == DecorationEditorTool.Scale)
            {
                DrawScaleGizmo(gizmoCenterLocal, multiTransform);
            }
            else if (_tool == DecorationEditorTool.Anchor)
            {
                DrawAnchorGizmo(anchorLocal);
            }
        }

        private void DrawSelectedSetHints()
        {
            if (_selection.Count <= 1)
                return;

            int drawn = 0;
            Color color = new Color(0.1f, 0.95f, 1f, 0.82f);
            foreach (DecorationHit hit in _hits)
            {
                if (hit.Decoration == null ||
                    hit.Decoration.IsDeleted ||
                    ReferenceEquals(hit.Decoration, _selected) ||
                    !_selection.Contains(hit.Decoration))
                {
                    continue;
                }

                Vector3 world = hit.Construct.SafeLocalToGlobal(hit.LocalCenter);
                DecorationEditorOverlay.Circle(world, 0.21f, color, Vector3.up, 2.4f, 14);
                drawn++;
                if (drawn >= MaxWorldHintLines)
                    break;
            }
        }

        private void DrawSymmetryOverlay()
        {
            AllConstruct construct = _placementConstruct ??
                                     _selectedConstruct ??
                                     DecoLimitLifter.EsuSymmetry.Construct ??
                                     FocusedConstruct();
            Vector3 around = Vector3.zero;
            if (_placingMesh != null && _placementConstruct != null)
                around = _placementValid ? _placementLocalPosition : ToVector3(_placementAnchor);
            else if (_selected != null && !_selected.IsDeleted)
                around = GetDecorationLocalCenter(_selected);
            else if (construct != null)
                around = FocusedConstructLocalCenter(construct);

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

            if (_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                Vector3i cell = hit.Anchor;
                DecoLimitLifter.EsuSymmetry.DrawPlane(
                    hit.Construct,
                    DecoLimitLifter.EsuSymmetry.PendingAxis,
                    DecoLimitLifter.EsuSymmetry.AxisComponent(
                        cell,
                        DecoLimitLifter.EsuSymmetry.PendingAxis),
                    ToVector3(cell),
                    pending: true);
            }
        }

        private void DrawSelectedAnchorConnections(Vector3i tether, Vector3 anchorWorld)
        {
            if (_selectedConstruct == null)
                return;

            int drawn = 0;
            foreach (Decoration decoration in SelectedAnchorDecorations(tether))
            {
                if (decoration == null ||
                    decoration.IsDeleted ||
                    ReferenceEquals(decoration, _selected))
                {
                    continue;
                }

                Vector3 otherWorld = _selectedConstruct.SafeLocalToGlobal(GetDecorationLocalCenter(decoration));
                Color color = new Color(0.1f, 0.95f, 1f, 0.78f);
                DecorationEditorOverlay.Line(anchorWorld, otherWorld, color, 2f);
                DecorationEditorOverlay.Circle(otherWorld, 0.18f, color, Vector3.up, 2f, 12);
                drawn++;
                if (drawn >= MaxWorldHintLines)
                    break;
            }
        }

        private void DrawPlacementGhost()
        {
            if (_placingMesh == null || _placementConstruct == null)
                return;

            Vector3 anchorLocal = ToVector3(_placementAnchor);
            Vector3 anchorWorld = _placementConstruct.SafeLocalToGlobal(anchorLocal);
            Vector3 centerWorld = _placementValid
                ? _placementConstruct.SafeLocalToGlobal(_placementLocalPosition)
                : anchorWorld;
            Color color = _placementValid
                ? new Color(0.2f, 1f, 0.35f, 0.95f)
                : new Color(1f, 0.25f, 0.2f, 0.95f);
            DecorationEditorOverlay.Circle(anchorWorld, 0.35f, color, Vector3.up, 3f, 24);
            DecorationEditorOverlay.Cross(anchorWorld, 0.42f, color, 3f);
            DecorationEditorOverlay.Line(anchorWorld, centerWorld, color, 2f);
            DecorationEditorOverlay.Circle(centerWorld, 0.2f, color, Vector3.up, 3f, 18);
        }

        private static Color SurfaceDraftPaintColor(int colorIndex, float alpha)
        {
            Color color = PaintPreviewColor(Mathf.Clamp(colorIndex, 0, 31));
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void DrawSurfaceOverlay()
        {
            if (!_surfaceDraft.HasDraft || _surfaceDraft.Construct == null)
                return;

            AllConstruct construct = _surfaceDraft.Construct;
            Color edgeColor = new Color(0.1f, 0.95f, 1f, 0.92f);
            Color selected = new Color(1f, 0.9f, 0.2f, 1f);
            Color manual = new Color(1f, 0.45f, 0.95f, 1f);
            bool hasPreview = _surfacePlan != null;

            for (int index = 0; index < _surfaceDraft.Faces.Count; index++)
            {
                SurfaceFace face = _surfaceDraft.Faces[index];
                Vector3 a = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.A]);
                Vector3 b = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.B]);
                Vector3 c = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.C]);
                Color fill = SurfaceDraftPaintColor(
                    _surfaceDraft.FaceStyleAt(index).ColorIndex,
                    hasPreview ? 0.28f : 0.17f);
                Color faceFill = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                                 _surfaceDraft.SelectedFace == index
                    ? Color.Lerp(fill, new Color(selected.r, selected.g, selected.b, 0.34f), 0.45f)
                    : fill;
                DecorationEditorOverlay.Quad(a, b, c, c, faceFill);
                DrawSurfaceEdge(face.A, face.B, edgeColor);
                DrawSurfaceEdge(face.B, face.C, edgeColor);
                DrawSurfaceEdge(face.C, face.A, edgeColor);
            }

            DrawMirroredSurfaceOverlay(
                construct,
                hasPreview,
                !hasPreview
                    ? new Color(1f, 0.35f, 0.25f, 0.78f)
                    : new Color(0.1f, 0.95f, 1f, 0.58f));

            DrawSelectedSurfacePointSetPreview(construct, manual);

            for (int index = 0; index < _surfaceDraft.Points.Count; index++)
            {
                Vector3 world = construct.SafeLocalToGlobal(_surfaceDraft.Points[index]);
                bool isSelected = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Point &&
                                  _surfaceDraft.SelectedPoint == index;
                bool isManual = _surfaceDraft.ManualFaceSelection.Contains(index);
                Color color = isSelected ? selected : isManual ? manual : Color.cyan;
                float radius = isSelected ? 0.25f : 0.18f;
                DecorationEditorOverlay.Circle(world, radius, color, Vector3.up, isSelected ? 3.5f : 2.3f, 20);
                DecorationEditorOverlay.Cross(world, radius * 1.18f, color, isSelected ? 3.5f : 2.3f);
            }

            if (_surfaceDraft.SelectionKind == SurfaceSelectionKind.Point &&
                _surfaceDraft.SelectedPoint >= 0 &&
                _surfaceDraft.SelectedPoint < _surfaceDraft.Points.Count)
            {
                Vector3 center = _surfaceDraft.Points[_surfaceDraft.SelectedPoint];
                DrawSurfaceAxis(center, DecorationEditAxis.X);
                DrawSurfaceAxis(center, DecorationEditAxis.Y);
                DrawSurfaceAxis(center, DecorationEditAxis.Z);
                if (_surfaceDragAxis == DecorationEditAxis.Free)
                {
                    DecorationEditorOverlay.Circle(
                        construct.SafeLocalToGlobal(center),
                        0.32f,
                        selected,
                        Vector3.up,
                        4f,
                        28);
                }
            }

            DrawSurfaceSameAnchorPreview();
        }

        private void DrawSelectedSurfacePointSetPreview(
            AllConstruct construct,
            Color color)
        {
            if (construct == null)
                return;

            IReadOnlyList<int> selectedPoints = _surfaceDraft.ManualFaceSelection;
            if (selectedPoints.Count < 2)
                return;

            if (!TrySurfacePointWorld(construct, selectedPoints[0], out Vector3 first) ||
                !TrySurfacePointWorld(construct, selectedPoints[1], out Vector3 second))
            {
                return;
            }

            if (selectedPoints.Count == 2)
            {
                DecorationEditorOverlay.Line(first, second, color, 3.5f);
                return;
            }

            if (!TrySurfacePointWorld(construct, selectedPoints[2], out Vector3 third))
                return;

            DecorationEditorOverlay.Quad(
                first,
                second,
                third,
                third,
                new Color(color.r, color.g, color.b, 0.18f));
            DecorationEditorOverlay.Line(first, second, color, 3.5f);
            DecorationEditorOverlay.Line(second, third, color, 3.5f);
            DecorationEditorOverlay.Line(third, first, color, 3.5f);
        }

        private bool TrySurfacePointWorld(
            AllConstruct construct,
            int pointIndex,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (construct == null ||
                pointIndex < 0 ||
                pointIndex >= _surfaceDraft.Points.Count)
            {
                return false;
            }

            world = construct.SafeLocalToGlobal(_surfaceDraft.Points[pointIndex]);
            return true;
        }

        private void DrawMirroredSurfaceOverlay(
            AllConstruct construct,
            bool hasPreview,
            Color edgeColor)
        {
            if (!DecoLimitLifter.EsuSymmetry.HasActivePlanes ||
                !DecoLimitLifter.EsuSymmetry.CanUseWith(construct, out string _))
            {
                return;
            }

            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                if (variant.IsIdentity)
                    continue;

                SurfaceDraft mirrored = _surfaceDraft.CreateMirroredForSymmetry(variant);
                if (SurfaceDecorationPlanner.SameGeometry(_surfaceDraft, mirrored))
                    continue;

                for (int index = 0; index < mirrored.Faces.Count; index++)
                {
                    SurfaceFace face = mirrored.Faces[index];
                    Vector3 a = construct.SafeLocalToGlobal(mirrored.Points[face.A]);
                    Vector3 b = construct.SafeLocalToGlobal(mirrored.Points[face.B]);
                    Vector3 c = construct.SafeLocalToGlobal(mirrored.Points[face.C]);
                    Color fill = SurfaceDraftPaintColor(
                        mirrored.FaceStyleAt(index).ColorIndex,
                        hasPreview ? 0.16f : 0.1f);
                    DecorationEditorOverlay.Quad(a, b, c, c, fill);
                    DrawSurfacePreviewEdge(construct, mirrored, face.A, face.B, edgeColor);
                    DrawSurfacePreviewEdge(construct, mirrored, face.B, face.C, edgeColor);
                    DrawSurfacePreviewEdge(construct, mirrored, face.C, face.A, edgeColor);
                }

                for (int index = 0; index < mirrored.Points.Count; index++)
                {
                    Vector3 world = construct.SafeLocalToGlobal(mirrored.Points[index]);
                    DecorationEditorOverlay.Circle(world, 0.14f, edgeColor, Vector3.up, 1.8f, 16);
                    DecorationEditorOverlay.Cross(world, 0.17f, edgeColor, 1.8f);
                }
            }
        }

        private void DrawSurfaceSameAnchorPreview()
        {
            if (_surfaceDraft.Settings.NearestAnchor ||
                _surfaceDraft.Construct == null)
            {
                return;
            }

            if (_surfaceDraft.HasSharedAnchor)
                DrawSharedAnchorTarget(
                    _surfaceDraft.Construct,
                    _surfaceDraft.SharedAnchor,
                    _surfaceDraft.SharedAnchorSelected,
                    _sharedAnchorDragAxis != DecorationEditAxis.None && !_sharedAnchorDragGenerator);

            if (!_surfaceDraft.HasPlaceableFaces)
                return;

            if (_surfacePlan == null)
                RebuildSurfacePreview(showMessage: false);
            if (_surfacePlan == null || _surfacePlan.Placements.Count == 0)
                return;

            DrawSameAnchorPreview(
                _surfacePlan.Construct,
                _surfacePlan.Placements.Select(placement => new PlannedAnchorLine(
                    placement.Anchor,
                    ToVector3(placement.Anchor) + placement.Positioning)));
        }

        private void DrawGeneratorSameAnchorPreview()
        {
            if (_generatorSettings.NearestAnchor ||
                _generatorDraft.Construct == null)
            {
                return;
            }

            if (_generatorDraft.HasSharedAnchor)
                DrawSharedAnchorTarget(
                    _generatorDraft.Construct,
                    _generatorDraft.SharedAnchor,
                    _generatorDraft.SharedAnchorSelected,
                    _sharedAnchorDragAxis != DecorationEditAxis.None && _sharedAnchorDragGenerator);

            if (!_generatorDraft.HasPlaceableGeometry)
                return;

            if (_generatorPlan == null)
                RebuildGeneratorPreview(showMessage: false);
            if (_generatorPlan == null || _generatorPlan.Placements.Count == 0)
                return;

            DrawSameAnchorPreview(
                _generatorPlan.Construct,
                _generatorPlan.Placements.Select(placement => new PlannedAnchorLine(
                    placement.Anchor,
                    ToVector3(placement.Anchor) + placement.Positioning)));
        }

        private void DrawSameAnchorPreview(
            AllConstruct construct,
            IEnumerable<PlannedAnchorLine> lines)
        {
            if (construct == null || lines == null)
                return;

            Color anchorColor = new Color(0.1f, 0.95f, 1f, 0.88f);
            Color lineColor = new Color(1f, 0.9f, 0.2f, 0.72f);
            var drawnAnchors = new HashSet<string>();
            int drawnLines = 0;
            foreach (PlannedAnchorLine line in lines)
            {
                string key = PlacementKey(line.Anchor, Vector3.zero);
                if (drawnAnchors.Add(key))
                    DrawBlockWireframe(construct, line.Anchor, anchorColor, 2.5f);

                if (drawnLines >= MaxWorldHintLines)
                    continue;

                Vector3 anchorWorld = construct.SafeLocalToGlobal(ToVector3(line.Anchor));
                Vector3 centerWorld = construct.SafeLocalToGlobal(line.Center);
                DecorationEditorOverlay.Line(anchorWorld, centerWorld, lineColor, 1.8f);
                DecorationEditorOverlay.Circle(centerWorld, 0.12f, lineColor, Vector3.up, 1.6f, 12);
                drawnLines++;
            }
        }

        private void DrawSharedAnchorTarget(
            AllConstruct construct,
            Vector3i anchor,
            bool selected,
            bool dragging)
        {
            if (construct == null)
                return;

            Color color = selected
                ? new Color(1f, 0.9f, 0.2f, 1f)
                : new Color(0.1f, 0.95f, 1f, 0.95f);
            float width = selected ? 3.4f : 2.4f;
            DrawBlockWireframe(construct, anchor, color, width);
            Vector3 anchorLocal = ToVector3(anchor);
            Vector3 anchorWorld = construct.SafeLocalToGlobal(anchorLocal);
            DecorationEditorOverlay.Circle(anchorWorld, selected ? 0.32f : 0.24f, color, Vector3.up, width, 24);
            DecorationEditorOverlay.Cross(anchorWorld, selected ? 0.38f : 0.3f, color, width);

            if (!selected || _surfaceBuilderTool != SurfaceBuilderTool.Move)
                return;

            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.X, 1, dragging);
            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.X, -1, dragging);
            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.Y, 1, dragging);
            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.Y, -1, dragging);
            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.Z, 1, dragging);
            DrawSharedAnchorAxis(construct, anchorLocal, DecorationEditAxis.Z, -1, dragging);
        }

        private void DrawSharedAnchorAxis(
            AllConstruct construct,
            Vector3 anchorLocal,
            DecorationEditAxis axis,
            int sign,
            bool dragging)
        {
            if (construct == null)
                return;

            Vector3 axisVector = DecorationEditMath.AxisVector(axis) * sign;
            Color color = DecorationEditMath.AxisColor(axis);
            float width = dragging && _sharedAnchorDragAxis == axis && _sharedAnchorDragSign == sign ? 4f : 2.5f;
            Vector3 start = construct.SafeLocalToGlobal(anchorLocal);
            Vector3 end = construct.SafeLocalToGlobal(anchorLocal + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.16f);
        }

        private void DrawGeneratorOverlay()
        {
            if (!_generatorDraft.HasDraft || _generatorDraft.Construct == null)
                return;

            AllConstruct construct = _generatorDraft.Construct;
            Color lineColor = SurfaceDraftPaintColor(_generatorSettings.ColorIndex, _generatorPlan == null ? 0.88f : 0.95f);
            Color pointColor = new Color(0.1f, 0.95f, 1f, 1f);
            Color selected = new Color(1f, 0.9f, 0.2f, 1f);

            DrawGeneratorDraftOverlay(_generatorDraft, lineColor, pointColor, selected, drawHandles: true);
            DrawMirroredGeneratorOverlay(
                construct,
                SurfaceDraftPaintColor(_generatorSettings.ColorIndex, _generatorPlan == null ? 0.42f : 0.52f));
            DrawGeneratorSameAnchorPreview();
        }

        private void DrawGeneratorDraftOverlay(
            DecorationGeneratorDraft draft,
            Color lineColor,
            Color pointColor,
            Color selected,
            bool drawHandles)
        {
            if (draft == null || draft.Construct == null)
                return;

            if (DecorationGeneratorPlanner.TryBuildPreviewSegments(
                    draft,
                    _generatorSettings,
                    out IReadOnlyList<DecorationGeneratorSegment> segments,
                    out string _))
            {
                for (int index = 0; index < segments.Count; index++)
                {
                    DecorationEditorOverlay.Line(
                        draft.Construct.SafeLocalToGlobal(segments[index].Start),
                        draft.Construct.SafeLocalToGlobal(segments[index].End),
                        lineColor,
                        _generatorPlan == null ? 2.2f : 3f);
                }
            }

            if (draft.UsesCenterTool)
            {
                if (!draft.HasCircleCenter)
                    return;

                DrawGeneratorPoint(draft, 0, pointColor, selected);
                if (drawHandles && ReferenceEquals(draft, _generatorDraft))
                {
                    if (_surfaceBuilderTool == SurfaceBuilderTool.Rotate)
                        DrawGeneratorRotateGizmo(draft.CircleCenter);
                    else if (_surfaceBuilderTool == SurfaceBuilderTool.Scale)
                        DrawGeneratorScaleGizmo(draft.CircleCenter);
                }
            }
            else
            {
                for (int index = 0; index < draft.PathPoints.Count; index++)
                    DrawGeneratorPoint(draft, index, pointColor, selected);
            }

            if (drawHandles &&
                _surfaceBuilderTool == SurfaceBuilderTool.Move &&
                draft.SelectedPoint >= 0 &&
                draft.Construct == _generatorDraft.Construct)
            {
                Vector3 center = draft.PointAt(draft.SelectedPoint);
                DrawGeneratorAxis(center, DecorationEditAxis.X);
                DrawGeneratorAxis(center, DecorationEditAxis.Y);
                DrawGeneratorAxis(center, DecorationEditAxis.Z);
                if (_generatorDragAxis == DecorationEditAxis.Free)
                {
                    DecorationEditorOverlay.Circle(
                        draft.Construct.SafeLocalToGlobal(center),
                        0.32f,
                        selected,
                        Vector3.up,
                        4f,
                        28);
                }
            }
        }

        private void DrawGeneratorPoint(
            DecorationGeneratorDraft draft,
            int index,
            Color pointColor,
            Color selected)
        {
            Vector3 world = draft.Construct.SafeLocalToGlobal(draft.PointAt(index));
            bool isSelected = ReferenceEquals(draft, _generatorDraft) && draft.SelectedPoint == index;
            Color color = isSelected ? selected : pointColor;
            float radius = isSelected ? 0.24f : 0.17f;
            DecorationEditorOverlay.Circle(world, radius, color, Vector3.up, isSelected ? 3.4f : 2.2f, 20);
            DecorationEditorOverlay.Cross(world, radius * 1.18f, color, isSelected ? 3.4f : 2.2f);
        }

        private void DrawMirroredGeneratorOverlay(AllConstruct construct, Color color)
        {
            if (!DecoLimitLifter.EsuSymmetry.HasActivePlanes ||
                !DecoLimitLifter.EsuSymmetry.CanUseWith(construct, out string _))
            {
                return;
            }

            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                if (variant.IsIdentity)
                    continue;

                DecorationGeneratorDraft mirrored = _generatorDraft.CreateMirroredForSymmetry(variant);
                DrawGeneratorDraftOverlay(mirrored, color, color, color, drawHandles: false);
            }
        }

        private void DrawGeneratorAxis(Vector3 centerLocal, DecorationEditAxis axis)
        {
            if (_generatorDraft.Construct == null)
                return;

            Color color = DecorationEditMath.AxisColor(axis);
            float width = _generatorDragAxis == axis ? 4f : 2.5f;
            Vector3 start = _generatorDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _generatorDraft.Construct.SafeLocalToGlobal(
                centerLocal + DecorationEditMath.AxisVector(axis) * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
        }

        private void DrawGeneratorRotateGizmo(Vector3 centerLocal)
        {
            if (_generatorDraft.Construct == null)
                return;

            foreach (DecorationEditAxis axis in new[] { DecorationEditAxis.X, DecorationEditAxis.Y, DecorationEditAxis.Z })
            {
                Color color = DecorationEditMath.AxisColor(axis);
                float width = _generatorRotateDragAxis == axis ? 4.4f : 2.4f;
                Vector3 normalWorld = GeneratorNormalWorld(_generatorDraft.Construct, DecorationEditMath.AxisVector(axis));
                DecorationEditorOverlay.Circle(
                    _generatorDraft.Construct.SafeLocalToGlobal(centerLocal),
                    Mathf.Max(RotateGizmoRadius, _generatorSettings.CircleRadius),
                    color,
                    normalWorld,
                    width,
                    48);
            }
        }

        private void DrawGeneratorScaleGizmo(Vector3 centerLocal)
        {
            if (_generatorDraft.Construct == null)
                return;

            Color color = new Color(0.1f, 1f, 0.2f, 0.95f);
            Vector3 center = _generatorDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _generatorDraft.Construct.SafeLocalToGlobal(
                centerLocal + _generatorDraft.CircleTangentA * _generatorSettings.CircleRadius);
            DecorationEditorOverlay.Arrow(center, end, color, _generatorRotateDragAxis != DecorationEditAxis.None ? 4.4f : 2.8f, 0.18f);
        }

        private static Vector3 GeneratorNormalWorld(AllConstruct construct, Vector3 localNormal)
        {
            if (construct == null || !DecorationEditMath.IsFinite(localNormal))
                return Vector3.up;

            try
            {
                Vector3 world = construct.myTransform.TransformDirection(localNormal);
                return world.sqrMagnitude > 0.0001f ? world.normalized : Vector3.up;
            }
            catch
            {
                return Vector3.up;
            }
        }

        private bool TryPickGroupRotateRing(
            Vector3 center,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_selectedConstruct == null)
                return false;

            if (!TryProject(_selectedConstruct, center, out Vector2 origin))
                return false;

            float best = HandleRadiusPixels * 1.35f;
            DecorationEditAxis pickedAxis = DecorationEditAxis.None;
            TryRing(DecorationEditAxis.X);
            TryRing(DecorationEditAxis.Y);
            TryRing(DecorationEditAxis.Z);
            axis = pickedAxis;
            return axis != DecorationEditAxis.None;

            void TryRing(DecorationEditAxis candidate)
            {
                BuildAxisBasis(
                    GroupTransformAxisVector(candidate),
                    out Vector3 tangentA,
                    out Vector3 tangentB);
                const int steps = 40;
                bool havePrevious = false;
                Vector2 previous = origin;
                for (int step = 0; step <= steps; step++)
                {
                    float angle = step * Mathf.PI * 2f / steps;
                    Vector3 local = center +
                                    (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                                    RotateGizmoRadius;
                    if (!TryProject(_selectedConstruct, local, out Vector2 projected))
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
                            pickedAxis = candidate;
                        }
                    }

                    previous = projected;
                    havePrevious = true;
                }
            }
        }

        private static void DrawSurfacePreviewEdge(
            AllConstruct construct,
            SurfaceDraft draft,
            int a,
            int b,
            Color color)
        {
            if (construct == null ||
                draft == null ||
                a < 0 ||
                b < 0 ||
                a >= draft.Points.Count ||
                b >= draft.Points.Count)
            {
                return;
            }

            DecorationEditorOverlay.Line(
                construct.SafeLocalToGlobal(draft.Points[a]),
                construct.SafeLocalToGlobal(draft.Points[b]),
                color,
                1.8f);
        }

        private void DrawSurfaceEdge(int a, int b, Color defaultColor)
        {
            if (_surfaceDraft.Construct == null ||
                a < 0 ||
                b < 0 ||
                a >= _surfaceDraft.Points.Count ||
                b >= _surfaceDraft.Points.Count)
            {
                return;
            }

            bool active = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Edge &&
                          _surfaceDraft.SelectedEdge.Matches(a, b);
            bool bridge = _surfaceDraft.IsBridgeEdgeSelected(a, b);
            Color color = active
                ? new Color(1f, 0.9f, 0.2f, 1f)
                : bridge
                    ? new Color(0.2f, 1f, 0.55f, 1f)
                    : defaultColor;
            float width = active ? 4f : bridge ? 3.5f : 2.2f;
            DecorationEditorOverlay.Line(
                _surfaceDraft.Construct.SafeLocalToGlobal(_surfaceDraft.Points[a]),
                _surfaceDraft.Construct.SafeLocalToGlobal(_surfaceDraft.Points[b]),
                color,
                width);
        }

        private void DrawSurfaceAxis(Vector3 centerLocal, DecorationEditAxis axis)
        {
            if (_surfaceDraft.Construct == null)
                return;

            Color color = DecorationEditMath.AxisColor(axis);
            float width = _surfaceDragAxis == axis ? 4f : 2.5f;
            Vector3 start = _surfaceDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _surfaceDraft.Construct.SafeLocalToGlobal(
                centerLocal + DecorationEditMath.AxisVector(axis) * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
        }

        private void UpdatePlacementGhost()
        {
            if (_placingMesh == null || _placementConstruct == null)
            {
                HidePlacementGhost();
                return;
            }

            Mesh mesh = _previewRenderer?.GetMesh(_placingMesh);
            if (mesh == null)
            {
                HidePlacementGhost();
                return;
            }

            EnsurePlacementGhost();
            if (_placementGhost == null ||
                _placementGhostFilter == null ||
                _placementGhostRenderer == null)
            {
                return;
            }

            if (!ReferenceEquals(_placementGhostEntry, _placingMesh))
                _placementGhostEntry = _placingMesh;
            if (_placementGhostFilter.sharedMesh != mesh)
                _placementGhostFilter.sharedMesh = mesh;

            Color color = _placementValid
                ? new Color(0.25f, 1f, 0.65f, 0.48f)
                : new Color(1f, 0.25f, 0.2f, 0.38f);
            SetPlacementGhostColor(color);

            Vector3 centerLocal = DecorationEditMath.IsFinite(_placementLocalPosition)
                ? _placementLocalPosition
                : ToVector3(_placementAnchor);
            _placementGhost.transform.position = _placementConstruct.SafeLocalToGlobal(centerLocal);
            _placementGhost.transform.rotation = ConstructRotation(_placementConstruct);
            _placementGhost.transform.localScale = Vector3.one;
            if (!_placementGhost.activeSelf)
                _placementGhost.SetActive(true);
        }

        private void EnsurePlacementGhost()
        {
            if (_placementGhost != null)
                return;

            _placementGhost = new GameObject("ESU Decoration Placement Ghost")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _placementGhostFilter = _placementGhost.AddComponent<MeshFilter>();
            _placementGhostRenderer = _placementGhost.AddComponent<MeshRenderer>();
            _placementGhostMaterial = CreatePlacementGhostMaterial();
            _placementGhostRenderer.sharedMaterial = _placementGhostMaterial;
            _placementGhostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _placementGhostRenderer.receiveShadows = false;
            _placementGhost.SetActive(false);
        }

        private static Material CreatePlacementGhostMaterial()
        {
            Shader shader =
                Shader.Find("Transparent/Diffuse") ??
                Shader.Find("Standard") ??
                Shader.Find("Diffuse") ??
                Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.25f, 1f, 0.65f, 0.48f),
                renderQueue = (int)RenderQueue.Transparent
            };
            ConfigureTransparentMaterial(material);
            return material;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull"))
                material.SetInt("_Cull", (int)CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void SetPlacementGhostColor(Color color)
        {
            if (_placementGhostMaterial == null)
                return;

            _placementGhostMaterial.color = color;
            if (_placementGhostMaterial.HasProperty("_Color"))
                _placementGhostMaterial.SetColor("_Color", color);
            if (_placementGhostMaterial.HasProperty("_TintColor"))
                _placementGhostMaterial.SetColor("_TintColor", color);
        }

        private void HidePlacementGhost()
        {
            if (_placementGhost != null && _placementGhost.activeSelf)
                _placementGhost.SetActive(false);
            _placementGhostEntry = null;
        }

        private void DestroyPlacementGhost()
        {
            ReleasePlacementGhostObject(_placementGhostMaterial);
            ReleasePlacementGhostObject(_placementGhost);
            _placementGhost = null;
            _placementGhostFilter = null;
            _placementGhostRenderer = null;
            _placementGhostMaterial = null;
            _placementGhostEntry = null;
        }

        private static void ReleasePlacementGhostObject(UnityEngine.Object instance)
        {
            if (instance == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
        }

        private static Quaternion ConstructRotation(AllConstruct construct)
        {
            try
            {
                if (construct?.myTransform != null)
                    return construct.myTransform.rotation;
            }
            catch
            {
                // Placement ghost rendering should fall back to world axes if construct transform access fails.
            }

            return Quaternion.identity;
        }

        private void DrawFocusDecorationHints()
        {
            if (_viewMode == DecorationEditorViewMode.Normal)
                return;

            float radius = 0.12f;
            float width = 1f;
            Color color = new Color(0.1f, 0.95f, 1f, 0.55f);
            if (_viewMode == DecorationEditorViewMode.Wireframe)
            {
                radius = 0.165f;
                width = 2f;
                color = new Color(0.1f, 0.95f, 1f, 0.8f);
            }
            else if (_viewMode == DecorationEditorViewMode.DecorationOnly)
            {
                radius = 0.24f;
                width = 2.5f;
                color = new Color(0.25f, 1f, 1f, 0.95f);
            }

            int drawn = 0;
            foreach (DecorationHit hit in _hits)
            {
                if (hit.Decoration == null ||
                    hit.Decoration.IsDeleted ||
                    ReferenceEquals(hit.Decoration, _selected))
                {
                    continue;
                }

                Vector3 world = hit.Construct.SafeLocalToGlobal(hit.LocalCenter);
                DecorationEditorOverlay.Circle(
                    world,
                    radius,
                    color,
                    Vector3.up,
                    width,
                    10);
                drawn++;
                if (drawn >= MaxWorldHintLines)
                    break;
            }
        }

        private void DrawNearestHint()
        {
            if (_mouseOverWindow || _placingMesh != null)
                return;
            DecorationHit nearest = null;
            float nearestDistance = SelectionRadiusPixels;
            foreach (DecorationHit hit in _hits)
            {
                float distance = Vector2.Distance(_lastMouseGui, hit.ScreenPoint);
                if (distance < nearestDistance)
                {
                    nearest = hit;
                    nearestDistance = distance;
                }
            }
            if (nearest == null)
                return;

            Vector3 world = nearest.Construct.SafeLocalToGlobal(nearest.LocalCenter);
            float radius = _viewMode == DecorationEditorViewMode.DecorationOnly ? 0.36f : 0.24f;
            float width = _viewMode == DecorationEditorViewMode.Normal ? 2f : 3f;
            DecorationEditorOverlay.Circle(world, radius, new Color(0.1f, 0.9f, 1f, 1f), Vector3.up, width, 12);
        }

        private void DrawAxis(Vector3 centerLocal, DecorationEditAxis axis, bool groupTransform)
        {
            Vector3 axisVector = groupTransform
                ? GroupTransformAxisVector(axis)
                : ActiveTransformAxisVector(axis);
            Color color = DecorationEditMath.AxisColor(axis);
            float width = _dragAxis == axis ? 4.5f : 3f;
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(centerLocal + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.2f);
        }

        private void DrawRotateGizmo(Vector3 centerLocal, bool groupTransform)
        {
            DrawRotationRing(centerLocal, DecorationEditAxis.X, groupTransform);
            DrawRotationRing(centerLocal, DecorationEditAxis.Y, groupTransform);
            DrawRotationRing(centerLocal, DecorationEditAxis.Z, groupTransform);
        }

        private void DrawRotationRing(Vector3 centerLocal, DecorationEditAxis axis, bool groupTransform)
        {
            Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 normalWorld = _selectedConstruct.SafeLocalToGlobal(
                centerLocal + (groupTransform ? GroupTransformAxisVector(axis) : ActiveTransformAxisVector(axis))) - centerWorld;
            if (normalWorld.sqrMagnitude < 0.0001f)
                normalWorld = DecorationEditMath.AxisVector(axis);

            Color color = DecorationEditMath.AxisColor(axis);
            float width = _rotateDragAxis == axis ? 4f : 2.5f;
            DecorationEditorOverlay.Circle(centerWorld, RotateGizmoRadius, color, normalWorld.normalized, width, 48);
        }

        private void DrawScaleGizmo(Vector3 centerLocal, bool groupTransform)
        {
            DrawScaleAxis(centerLocal, DecorationEditAxis.X, groupTransform);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Y, groupTransform);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Z, groupTransform);
        }

        private void DrawScaleAxis(Vector3 centerLocal, DecorationEditAxis axis, bool groupTransform)
        {
            Vector3 axisVector = groupTransform
                ? GroupTransformAxisVector(axis)
                : ActiveTransformAxisVector(axis);
            Color color = DecorationEditMath.AxisColor(axis);
            float width = _scaleDragAxis == axis ? 4.5f : 3f;
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(centerLocal + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
            DecorationEditorOverlay.Cross(end, 0.16f, color, width);
        }

        private void DrawAnchorGizmo(Vector3 anchorLocal)
        {
            DrawBlockWireframe(_selectedConstruct, _selected.TetherPoint.Us, Color.cyan, 2f);

            DrawAnchorAxis(anchorLocal, DecorationEditAxis.X, 1);
            DrawAnchorAxis(anchorLocal, DecorationEditAxis.X, -1);
            DrawAnchorAxis(anchorLocal, DecorationEditAxis.Y, 1);
            DrawAnchorAxis(anchorLocal, DecorationEditAxis.Y, -1);
            DrawAnchorAxis(anchorLocal, DecorationEditAxis.Z, 1);
            DrawAnchorAxis(anchorLocal, DecorationEditAxis.Z, -1);

            if (_anchorDragAxis == DecorationEditAxis.None)
                return;

            Color preview = _anchorPreviewValid
                ? new Color(0.25f, 1f, 0.35f, 1f)
                : new Color(1f, 0.2f, 0.15f, 1f);
            DrawBlockWireframe(_selectedConstruct, _anchorPreviewTether, preview, 3f);
            DecorationEditorOverlay.Line(
                _selectedConstruct.SafeLocalToGlobal(anchorLocal),
                _selectedConstruct.SafeLocalToGlobal(ToVector3(_anchorPreviewTether)),
                preview,
                2f);
            if (_anchorPreviewValid)
            {
                Vector3 previewCenter = ToVector3(_anchorPreviewTether) + _anchorPreviewPosition;
                DecorationEditorOverlay.Circle(
                    _selectedConstruct.SafeLocalToGlobal(previewCenter),
                    0.22f,
                    preview,
                    Vector3.up,
                    2f,
                    18);
            }
        }

        private void DrawAnchorAxis(Vector3 anchorLocal, DecorationEditAxis axis, int sign)
        {
            Vector3 axisVector = DecorationEditMath.AxisVector(axis) * sign;
            Color color = DecorationEditMath.AxisColor(axis);
            float width = _anchorDragAxis == axis && _anchorDragSign == sign ? 4f : 2.5f;
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(anchorLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(anchorLocal + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.16f);
        }

        private static void DrawBlockWireframe(
            AllConstruct construct,
            Vector3i tether,
            Color color,
            float width)
        {
            if (construct == null)
                return;

            Vector3 center = ToVector3(tether);
            Vector3[] corners =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            };
            for (int index = 0; index < corners.Length; index++)
                corners[index] = construct.SafeLocalToGlobal(center + corners[index]);

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

        private static void DrawWireEdge(
            Vector3[] corners,
            int from,
            int to,
            Color color,
            float width) =>
            DecorationEditorOverlay.Line(corners[from], corners[to], color, width);

        private void ApplyFocusView()
        {
            try
            {
                _buildOptions = ProfileManager.Instance.GetModule<MBuild_Ftd>();
                if (_buildOptions != null)
                {
                    _previousWireframe = _buildOptions.DecorationWireframe;
                    _viewModes.Capture(_buildOptions);
                    ApplyEditorViewMode();
                }
            }
            catch
            {
                _buildOptions = null;
            }

        }

        private void ApplyEditorViewMode()
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
                    "[EndlessShapes Unlimited] Decoration Edit Mode view mode apply failed",
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

            if (failure != null)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit Mode focus restore failed",
                    failure,
                    LogOptions._AlertDevInGame);
            }
        }

        private void MarkSelectedDirty()
        {
            if (_selected == null || _selected.IsDeleted)
                return;

            _selected.Changed();
            _dirty = true;
            RefreshForecast(force: true);
        }

        private void ResetInspectorFields()
        {
            if (_selected == null || _selected.IsDeleted)
            {
                for (int index = 0; index < 3; index++)
                {
                    _positionText[index] = string.Empty;
                    _scaleText[index] = string.Empty;
                    _orientationText[index] = string.Empty;
                }
                _colorText = string.Empty;
                _materialText = string.Empty;
                return;
            }

            SetVectorText(_positionText, _selected.Positioning.Us);
            SetVectorText(_scaleText, _selected.Scaling.Us);
            SetVectorText(_orientationText, _selected.Orientation.Us);
            _colorText = _selected.Color.Us.ToString(CultureInfo.InvariantCulture);
            _materialText = _selected.MaterialReplacement.Us == Guid.Empty
                ? string.Empty
                : _selected.MaterialReplacement.Us.ToString("D");
        }

        private void RefreshLiveTransformFields()
        {
            if (_textInputFocused)
                return;

            if (_selected == null || _selected.IsDeleted)
            {
                for (int index = 0; index < 3; index++)
                {
                    _positionText[index] = string.Empty;
                    _scaleText[index] = string.Empty;
                    _orientationText[index] = string.Empty;
                }
                return;
            }

            SetVectorText(_positionText, _selected.Positioning.Us);
            SetVectorText(_scaleText, _selected.Scaling.Us);
            SetVectorText(_orientationText, _selected.Orientation.Us);
        }

        private static void SetVectorText(string[] destination, Vector3 value)
        {
            destination[0] = FormatFloat(value.x);
            destination[1] = FormatFloat(value.y);
            destination[2] = FormatFloat(value.z);
        }

        private static bool TryParseVector(string[] values, out Vector3 result)
        {
            result = Vector3.zero;
            if (values == null || values.Length < 3)
                return false;
            if (!FlexibleFloatParser.TryParse(values[0], out float x) ||
                !FlexibleFloatParser.TryParse(values[1], out float y) ||
                !FlexibleFloatParser.TryParse(values[2], out float z))
            {
                return false;
            }

            result = new Vector3(x, y, z);
            return DecorationEditMath.IsFinite(result);
        }

        private void ApplyPositionFromInspector(Vector3 value, bool syncText)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            if (!DecorationEditMath.IsWithinPositionLimit(value))
            {
                InfoStore.Add("Position must be finite and within +/-10m on every axis.");
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            SymmetryFollowContext symmetryFollow = BeginSymmetryFollow(before, reportSkipped: true);
            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            _selected.Positioning.Us = value;
            TryAutoFollowAnchor(out _);
            if (!TryApplySymmetryFollow(symmetryFollow, reportInvalid: true))
            {
                RestoreSymmetryEditState(selectedRollback, symmetryFollow, targetRollback);
                if (syncText)
                    SetVectorText(_positionText, _selected.Positioning.Us);
                RefreshDecorationCache(force: true);
                return;
            }

            if (syncText)
                SetVectorText(_positionText, _selected.Positioning.Us);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set position", before, symmetryFollow);
            RefreshDecorationCache(force: true);
        }

        private void ApplyScaleFromInspector(Vector3 value, bool syncText)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            if (!IsValidScale(value))
            {
                InfoStore.Add("Scale must be finite and non-collapsed on every axis.");
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            DecorationScaleBounds.AllowExtendedScale(_selected);
            _selected.Scaling.Us = value;
            if (syncText)
                SetVectorText(_scaleText, value);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set scale", before);
        }

        private static bool IsValidScale(Vector3 value) =>
            DecorationEditMath.IsFinite(value) &&
            Mathf.Abs(value.x) >= MinimumScale &&
            Mathf.Abs(value.y) >= MinimumScale &&
            Mathf.Abs(value.z) >= MinimumScale;

        private void ApplyOrientationFromInspector(Vector3 value, bool syncText)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            if (!DecorationEditMath.IsFinite(value))
            {
                InfoStore.Add("Rotation must be finite.");
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.Orientation.Us = value;
            if (syncText)
                SetVectorText(_orientationText, value);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set rotation", before);
        }

        private void PushRecentMesh(Guid guid)
        {
            if (guid == Guid.Empty)
                return;
            _recentMeshes.Remove(guid);
            _recentMeshes.Insert(0, guid);
            while (_recentMeshes.Count > 24)
                _recentMeshes.RemoveAt(_recentMeshes.Count - 1);
        }

        private string MeshName(Guid guid)
        {
            if (_meshByGuid.TryGetValue(guid, out DecorationMeshCatalogEntry entry) &&
                !string.IsNullOrEmpty(entry.Name))
            {
                return entry.Name;
            }

            string value = guid == Guid.Empty ? "no mesh" : guid.ToString("N");
            return value.Length > 12 ? "mesh " + value.Substring(0, 12) : value;
        }

        private static string ConstructLabel(AllConstruct construct, int index)
        {
            if (construct == null)
                return "Unknown construct";
            if (construct is MainConstruct)
                return "Main construct";
            return index >= 0
                ? "Subconstruct " + index.ToString(CultureInfo.InvariantCulture)
                : construct.GetType().Name;
        }

        private static string FormatTether(Vector3i value) =>
            string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", value.x, value.y, value.z);

        private static string FormatFloat(float value) =>
            value.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string CompactText(string value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxCharacters)
                return value ?? string.Empty;
            return value.Substring(0, Mathf.Max(1, maxCharacters - 3)) + "...";
        }

        private static Vector3 GetDecorationLocalCenter(Decoration decoration) =>
            ToVector3(decoration.TetherPoint.Us) + decoration.Positioning.Us;

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);

        private static bool SameTether(Vector3i left, Vector3i right) =>
            left.x == right.x && left.y == right.y && left.z == right.z;

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

        private static string PlacementKey(Vector3i anchor, Vector3 positioning) =>
            DecoLimitLifter.EsuSymmetry.CellKey(anchor) + ":" +
            Mathf.RoundToInt(positioning.x * 1000f).ToString(CultureInfo.InvariantCulture) + ":" +
            Mathf.RoundToInt(positioning.y * 1000f).ToString(CultureInfo.InvariantCulture) + ":" +
            Mathf.RoundToInt(positioning.z * 1000f).ToString(CultureInfo.InvariantCulture);

        private static Vector3i AxisShift(DecorationEditAxis axis, int amount)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Vector3i(amount, 0, 0);
                case DecorationEditAxis.Y:
                    return new Vector3i(0, amount, 0);
                case DecorationEditAxis.Z:
                    return new Vector3i(0, 0, amount);
                default:
                    return new Vector3i(0, 0, 0);
            }
        }

        private static bool HasBlock(AllConstruct construct, Vector3i position)
        {
            try
            {
                return construct?.AllBasics?.GetBlockViaLocalPosition(position) != null;
            }
            catch
            {
                return false;
            }
        }

        private static float DistanceToScreenSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
                return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private static bool TryWorldToLocal(AllConstruct construct, Vector3 world, out Vector3 local)
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

        private static bool TryProject(AllConstruct construct, Vector3 local, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (construct == null)
                return false;
            Camera camera = Camera.main;
            if (camera == null)
                return false;
            Vector3 world = construct.SafeLocalToGlobal(local);
            Vector3 projected = camera.WorldToScreenPoint(world);
            if (projected.z <= 0f)
                return false;
            screenPoint = new Vector2(projected.x, Screen.height - projected.y);
            return projected.x >= -50f &&
                   projected.x <= Screen.width + 50f &&
                   projected.y >= -50f &&
                   projected.y <= Screen.height + 50f;
        }

        private static string FormatVector(Vector3 value) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###}, {1:0.###}, {2:0.###}",
                value.x,
                value.y,
                value.z);

        private sealed class MultiTransformPlacement
        {
            internal MultiTransformPlacement(
                Vector3i tetherPoint,
                Vector3 positioning,
                Vector3 orientation,
                Vector3 scaling)
            {
                TetherPoint = tetherPoint;
                Positioning = positioning;
                Orientation = orientation;
                Scaling = scaling;
            }

            internal Vector3i TetherPoint { get; }

            internal Vector3 Positioning { get; }

            internal Vector3 Orientation { get; }

            internal Vector3 Scaling { get; }
        }

        private readonly struct PlannedAnchorLine
        {
            internal PlannedAnchorLine(Vector3i anchor, Vector3 center)
            {
                Anchor = anchor;
                Center = center;
            }

            internal Vector3i Anchor { get; }

            internal Vector3 Center { get; }
        }

        private sealed class SymmetryFollowContext
        {
            internal SymmetryFollowContext(
                DecorationEditSnapshot selectedBefore,
                SymmetryFollowTarget[] targets)
            {
                SelectedBefore = selectedBefore;
                Targets = targets ?? Array.Empty<SymmetryFollowTarget>();
            }

            internal DecorationEditSnapshot SelectedBefore { get; }

            internal SymmetryFollowTarget[] Targets { get; }

            internal bool IsActive => Targets.Length > 0;

            internal bool ReportedInvalid { get; set; }
        }

        private sealed class SymmetryFollowTarget
        {
            internal SymmetryFollowTarget(
                DecoLimitLifter.EsuSymmetry.SymmetryVariant variant,
                Decoration decoration,
                DecorationEditSnapshot before)
            {
                Variant = variant;
                Decoration = decoration;
                Before = before;
            }

            internal DecoLimitLifter.EsuSymmetry.SymmetryVariant Variant { get; }

            internal Decoration Decoration { get; }

            internal DecorationEditSnapshot Before { get; }
        }

        private readonly struct SymmetryFollowApplication
        {
            internal SymmetryFollowApplication(
                Decoration decoration,
                Vector3i anchor,
                Vector3 positioning)
            {
                Decoration = decoration;
                Anchor = anchor;
                Positioning = positioning;
            }

            internal Decoration Decoration { get; }

            internal Vector3i Anchor { get; }

            internal Vector3 Positioning { get; }
        }

        private readonly struct DecorationPlacementPlan
        {
            internal DecorationPlacementPlan(Vector3i anchor, Vector3 positioning)
            {
                Anchor = anchor;
                Positioning = positioning;
            }

            internal Vector3i Anchor { get; }

            internal Vector3 Positioning { get; }
        }

        private readonly struct MeshPreviewGridLayout
        {
            internal MeshPreviewGridLayout(
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

        private sealed class DecorationDeletionRecord
        {
            internal DecorationDeletionRecord(
                AllConstruct construct,
                DecorationEditSnapshot original,
                DecorationEditSnapshot deleted,
                Decoration decoration)
            {
                Construct = construct;
                Original = original;
                Deleted = deleted;
                Decoration = decoration;
            }

            internal AllConstruct Construct { get; }

            internal DecorationEditSnapshot Original { get; }

            internal DecorationEditSnapshot Deleted { get; }

            internal Decoration Decoration { get; }
        }

        private sealed class DecorationHit
        {
            internal DecorationHit(
                Decoration decoration,
                AllConstruct construct,
                Vector3 localCenter,
                Vector2 screenPoint)
            {
                Decoration = decoration;
                Construct = construct;
                LocalCenter = localCenter;
                ScreenPoint = screenPoint;
            }

            internal Decoration Decoration { get; }

            internal AllConstruct Construct { get; }

            internal Vector3 LocalCenter { get; }

            internal Vector2 ScreenPoint { get; }
        }

        private sealed class OutlinerRow
        {
            private OutlinerRow(
                DecorationOutlinerRowKind kind,
                AllConstruct construct,
                Decoration decoration,
                string label,
                string detail,
                string searchText,
                int depth,
                string collapseKey)
            {
                Kind = kind;
                Construct = construct;
                Decoration = decoration;
                Label = label ?? string.Empty;
                Detail = detail ?? string.Empty;
                SearchText = searchText ?? string.Empty;
                Depth = depth;
                CollapseKey = collapseKey ?? string.Empty;
            }

            internal DecorationOutlinerRowKind Kind { get; }
            internal AllConstruct Construct { get; }
            internal Decoration Decoration { get; }
            internal string Label { get; }
            internal string Detail { get; }
            internal string SearchText { get; }
            internal int Depth { get; }
            internal string CollapseKey { get; }

            internal static OutlinerRow ForConstruct(
                AllConstruct construct,
                string collapseKey,
                string label,
                string detail,
                string searchText) =>
                new OutlinerRow(
                    DecorationOutlinerRowKind.Construct,
                    construct,
                    null,
                    label,
                    detail,
                    searchText,
                    0,
                    collapseKey);

            internal static OutlinerRow ForTether(
                AllConstruct construct,
                string label,
                string detail,
                string searchText) =>
                new OutlinerRow(
                    DecorationOutlinerRowKind.Tether,
                    construct,
                    null,
                    "Tether " + label,
                    detail,
                    searchText,
                    1,
                    string.Empty);

            internal static OutlinerRow ForDecoration(
                AllConstruct construct,
                Decoration decoration,
                string label,
                string detail,
                string searchText,
                int depth) =>
                new OutlinerRow(
                    DecorationOutlinerRowKind.Decoration,
                    construct,
                    decoration,
                    label,
                    detail,
                    searchText,
                    depth,
                    string.Empty);
        }

        private sealed class MaterialCatalogEntry
        {
            internal MaterialCatalogEntry(Guid guid, string name)
            {
                Guid = guid;
                Name = name ?? string.Empty;
                DisplayName = Name + " | " + guid.ToString("N").Substring(0, 8);
                SearchText = (Name + " " + guid.ToString("D")).ToLowerInvariant();
            }

            internal Guid Guid { get; }
            internal string Name { get; }
            internal string DisplayName { get; }
            internal string SearchText { get; }
        }
    }
}
