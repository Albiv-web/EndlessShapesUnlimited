using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Networking;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core;
using BrilliantSkies.DataManagement.CopyPasting;
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
            Right,
            SurfaceCoordinates
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
            FocusDecoration,
            CopySettings,
            PasteSettings,
            Duplicate,
            ToggleAnchorMesh,
            Delete
        }

        private enum DecorationGroupPivotMode
        {
            BoundsCenter,
            AverageCenter,
            SelectedDecoration,
            SelectedAnchor
        }

        private const float SelectionRadiusPixels = 28f;
        private const float BoxSelectionClickThresholdPixels = 6f;
        private const float BoxSelectionVisibilityTolerance = 0.45f;
        private const int MaxBoxPaintBlocks = 1024;
        private const int MaxBoxPaintScanBlocks = 20000;
        private const int MaxBoxPaintScanCells = 80000;
        private const int MaxBoxPaintVisibilityRays = 2048;
        private const int BoxPaintVisibilityHitCapacity = 48;
        private const int MaxSurfacePreviewResources = 256;
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
        private const float MinimumNonZeroScale = 0.00001f;
        private const float AnchorFollowMinimumDistance = 1f;
        private const float AnchorFollowMaximumDistance = 10f;
        private const int AnchorFollowMaximumSearchRadius = 8;
        private const float SymmetryCounterpartCenterTolerance = 0.075f;
        private const string SurfaceCoordinateTextControlPrefix = "ESU.SurfaceCoordinate.";
        private const float LeftStackDefaultBottomRatio = 0.34f;
        private const float RightStackDefaultBottomRatio = 0.38f;
        private static Rect s_meshPaletteRect = new Rect(18f, 110f, 480f, 1050f);
        private static Rect s_rightPanelRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static float s_leftStackBottomRatio = LeftStackDefaultBottomRatio;
        private static float s_rightStackBottomRatio = RightStackDefaultBottomRatio;
        private static float s_surfaceCoordinateBottomRatio = 0.5f;
        private static bool s_surfaceCoordinateSplitCustomized;
        private static bool s_showMeshPalette = true;
        private static bool s_showInspectorPanel = true;
        private static bool s_showOutlinerPanel = true;
        private static bool s_showAnchorPanel = true;
        private static bool s_showSurfacePanel = true;
        private static bool s_showSurfaceToolsPanel = true;
        private static bool s_showSurfaceDraftList = true;
        private static bool s_showSurfaceCoordinates = false;
        private static bool s_showInspectorColorPicker = true;
        private static bool s_showGeneratorColorPicker = true;
        private static bool s_meshPreviewGrid;
        private static DecorationTransformOrientation s_transformOrientation = DecorationTransformOrientation.Global;
        private static bool s_anchorMenuOpen;
        private static bool s_anchorFollowDecoration;
        private static float s_anchorFollowDistance = AnchorFollowMinimumDistance;
        private static DecorationGroupPivotMode s_groupPivotMode = DecorationGroupPivotMode.BoundsCenter;
        private static bool s_standardClipboardRoutesToDecorationSelection;
        private static readonly RaycastHit[] s_boxPaintVisibilityHits =
            new RaycastHit[BoxPaintVisibilityHitCapacity];
        private static SurfaceExtraTool s_surfaceExtraTool = SurfaceExtraTool.Path;
        private static SurfaceBuilderTool s_surfaceBuilderTool = SurfaceBuilderTool.Draw;
        private static readonly SurfaceExtraTool[] SurfaceCreateToolCycle =
        {
            SurfaceExtraTool.Path,
            SurfaceExtraTool.Circle,
            SurfaceExtraTool.PartialCircle,
            SurfaceExtraTool.Cone2D,
            SurfaceExtraTool.Sphere,
            SurfaceExtraTool.PartialSphere,
            SurfaceExtraTool.Cone,
            SurfaceExtraTool.Frustum,
            SurfaceExtraTool.Quad,
            SurfaceExtraTool.Polygon,
            SurfaceExtraTool.Tube
        };
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
        private Rect _gizmoSettingsButtonScreenRect;
        private int _layoutResetGeneration = s_layoutGeneration;
        private float _leftStackBottomRatio = s_leftStackBottomRatio;
        private float _rightStackBottomRatio = s_rightStackBottomRatio;
        private float _surfaceCoordinateBottomRatio = s_surfaceCoordinateBottomRatio;
        private bool _surfaceCoordinateSplitCustomized = s_surfaceCoordinateSplitCustomized;
        private StackDividerKind _draggingStackDivider;
        private float _stackDividerDragStartMouseY;
        private float _stackDividerDragStartBottomRatio;
        private bool _meshPreviewGrid = s_meshPreviewGrid;
        private bool _textInputFocused;
        private bool _viewModeMenuOpen;
        private bool _anchorMenuOpen = s_anchorMenuOpen;
        private bool _gizmoSettingsOpen;
        private string _gizmoMoveSizeText = "1";
        private string _gizmoRotateSizeText = "1";
        private string _gizmoScaleSizeText = "1";
        private string _gizmoThicknessText = "1";
        private string _gizmoHitAreaText = "18";
        private bool _unappliedClosePromptOpen;
        private bool _unappliedClosePromptAlwaysApply;
        private DecorationSelectionMode _selectionMode = DecorationSelectionMode.Single;
        private bool _selectionFocusLocked;
        private Decoration _selectionFocusDecoration;
        private AllConstruct _selectionFocusConstruct;
        private float _selectionFocusBlockedNoticeUntil = -1f;
        private bool _selectionXray;
        private bool _boxSelecting;
        private bool _boxSelectionPaint;
        private AllConstruct _boxSelectionConstruct;
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
        private DecorationGroupPivotMode _groupPivotMode = s_groupPivotMode;
        private readonly HashSet<string> _collapsedConstructs = new HashSet<string>();

        private MBuild_Ftd _buildOptions;
        private bool _previousWireframe;
        private readonly DecorationEditHistory _history = new DecorationEditHistory();
        private readonly DecorationEditTransactionSet _transactions = new DecorationEditTransactionSet();
        private readonly Dictionary<Block, int> _blockPaintOriginalColors =
            new Dictionary<Block, int>();
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly DecorationEditorViewModeController _viewModes;
        private DecorationMeshPreviewRenderer _previewRenderer;

        private readonly List<DecorationHit> _hits = new List<DecorationHit>();
        private readonly List<OutlinerRow> _outlinerRows = new List<OutlinerRow>();
        private readonly HashSet<Decoration> _selection = new HashSet<Decoration>();
        private Decoration _outlinerRangeAnchor;
        private Decoration _anchorRangeAnchor;
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
        private float _toolbarLeftControlWidth;
        private float _toolbarRightControlWidth;
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
        private float _rotateDragSensitivityScale = 1f;
        private DecorationEditSnapshot _rotateDragSnapshotStart;
        private SymmetryFollowContext _rotateDragSymmetryFollow;
        private DecorationEditAxis _scaleDragAxis;
        private bool _scaleDragUniformGroup;
        private Vector2 _scaleDragMouseStart;
        private Vector3 _scaleStart;
        private DecorationEditSnapshot _scaleDragSnapshotStart;
        private SymmetryFollowContext _scaleDragSymmetryFollow;
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
        private readonly List<PlacementGhostInstance> _symmetryPlacementGhosts =
            new List<PlacementGhostInstance>();
        private readonly Dictionary<string, SurfacePreviewResource> _surfacePreviewResources =
            new Dictionary<string, SurfacePreviewResource>(StringComparer.OrdinalIgnoreCase);
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
        private Vector2 _surfaceCompactWorkspaceScroll;
        private Vector2 _surfaceSettingsScroll;
        private string _surfaceThicknessText = "0.05";
        private string _surfaceColorText = "0";
        private readonly string[][] _surfaceCoordinateText =
        {
            new string[3],
            new string[3],
            new string[3]
        };
        private readonly string[] _surfaceCoordinateRangeMinimumText =
            { "-10", "-10", "-10" };
        private readonly string[] _surfaceCoordinateRangeMaximumText =
            { "10", "10", "10" };
        private readonly bool[] _surfaceCoordinateDisplayRangeValid =
            { true, true, true };
        private string _surfaceCoordinateBinding = string.Empty;
        private bool _surfaceCoordinateTargetBindingInitialized;
        private bool _surfaceCoordinateTargetGenerator;
        private SurfaceExtraTool _surfaceCoordinateTargetGeneratorTool = SurfaceExtraTool.None;
        private AllConstruct _surfaceCoordinateTargetConstruct;
        private int[] _surfaceCoordinateTargetIndexes = Array.Empty<int>();
        private string[] _surfaceCoordinateTargetLabels = Array.Empty<string>();
        private bool _showSurfaceCoordinates = s_showSurfaceCoordinates;
        private bool _surfaceCoordinateRangesExpanded;
        private int _surfaceCoordinateHoverVectorIndex = -1;
        private int _surfaceCoordinateHoverPointIndex = -1;
        private bool _surfaceCoordinateHoverGenerator;
        private bool _surfaceCoordinateHoverSeenThisDraw;
        private int _surfaceCoordinateStepEditorComponent = -1;
        private int _surfaceCoordinateStepEditorVectorIndex = -1;
        private string _surfaceCoordinateStepText = "0.1";
        private Vector2 _surfaceCoordinateEditorScroll;
        private Vector3 _surfaceCoordinateDisplayMinimum = Vector3.one * -10f;
        private Vector3 _surfaceCoordinateDisplayMaximum = Vector3.one * 10f;
        private bool _surfaceCoordinateBaselineValid;
        private bool _surfaceCoordinateBaselineGenerator;
        private AllConstruct _surfaceCoordinateBaselineConstruct;
        private int[] _surfaceCoordinateBaselineIndexes = Array.Empty<int>();
        private Vector3[] _surfaceCoordinateBaselineValues = Array.Empty<Vector3>();
        private bool _surfaceCoordinateLiveInteractionActive;
        private bool _surfaceCoordinateLiveInteractionGenerator;
        private SurfaceDraftSnapshot _surfaceCoordinateLiveSurfaceBefore;
        private DecorationGeneratorEditSnapshot _surfaceCoordinateLiveGeneratorBefore;
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
        private string _generatorMessage = "Extra Tools: draw a path/tube or place a shape center.";
        private SurfaceExtraTool _surfaceExtraTool = s_surfaceExtraTool;
        private Vector2 _generatorScroll;
        private string _generatorDiameterText = "0.05";
        private string _generatorRadiusText = "2.00";
        private string _generatorSegmentsText = "24";
        private string _generatorArcText = "360";
        private string _generatorHeightText = "2.00";
        private string _generatorTopRadiusText = "1.00";
        private string _generatorRingsText = "6";
        private string _generatorQuadWidthText = "4.00";
        private string _generatorQuadHeightText = "2.00";
        private string _generatorPolygonSidesText = "6";
        private string _generatorTubeDiameterText = "1.00";
        private string _generatorTubeSegmentsText = "8";
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
        private float _generatorRotateDragSensitivityScale = 1f;
        private DecorationGeneratorEditSnapshot _generatorRotateSnapshotStart;
        private DecorationGeneratorEditSnapshot _generatorScaleSnapshotStart;
        private float _generatorScaleRadiusStart;
        private float _generatorScaleQuadWidthStart;
        private float _generatorScaleQuadHeightStart;
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
        private bool _decorationContextPreserveSelection;
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
            HasPendingBlockPaintChanges() ||
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

        internal bool HasOpenPrompt => _unappliedClosePromptOpen;

        internal void RequestHotkeyClose()
        {
            if (!Active)
                return;

            if (_gizmoSettingsOpen)
            {
                CloseGizmoSettingsMenu();
                InfoStore.Add("Gizmo settings closed.");
                return;
            }

            if (_unappliedClosePromptOpen)
                return;

            if (!HasUnappliedChanges)
            {
                CloseRequested = true;
                CloseApplies = false;
                return;
            }

            RefreshApplyCancelAttention();
            if (!ShouldPromptBeforeHotkeyClose())
            {
                CloseRequested = true;
                CloseApplies = true;
                InfoStore.Add("Decoration Edit changes will be applied before closing.");
                return;
            }

            OpenUnappliedClosePrompt();
        }

        internal bool DismissOpenPrompt()
        {
            if (ForegroundContextMenuOpen())
            {
                CloseSurfacePointContextMenu();
                CloseDecorationContextMenu();
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                InfoStore.Add("Decoration context menu closed.");
                return true;
            }

            if (_gizmoSettingsOpen)
            {
                CloseGizmoSettingsMenu();
                InfoStore.Add("Gizmo settings closed.");
                return true;
            }

            if (_boxSelecting)
            {
                bool wasPaint = _boxSelectionPaint;
                CancelBoxSelection();
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                InfoStore.Add(wasPaint
                    ? "Paint selection cancelled."
                    : "Box selection cancelled.");
                return true;
            }

            if (!_unappliedClosePromptOpen)
                return false;

            CloseUnappliedClosePrompt();
            InfoStore.Add("Decoration Edit close cancelled.");
            return true;
        }

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
            DecorationEditorInputScope.SetMouseOverEditorUiProbe(IsCurrentMouseOverEditorUi);
            DecoLimitLifter.EsuPanelUiHitTestRegistry.Register(this, HitTestEditorUi);
            DecoLimitLifter.EsuHudDiagnostics.LogGateStatus("Decoration Edit opened");
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
            SyncSurfaceCoordinateRangeText();
            SyncSurfaceCoordinateText(force: true);
            SyncGizmoSettingsText();
        }

        internal bool CanSwitchToSmartBuild(out string reason)
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
                _generatorRotateDragAxis != DecorationEditAxis.None ||
                _surfaceCoordinateLiveInteractionActive)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty ||
                _transactions.HasChanges ||
                HasPendingBlockPaintChanges() ||
                _deletedDecorations.Count > 0)
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

            if (_dirty ||
                _transactions.HasChanges ||
                HasPendingBlockPaintChanges() ||
                _deletedDecorations.Count > 0)
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

            FinalizeSurfaceCoordinateLiveInteraction();
            try
            {
                if (apply)
                    ApplySelection(notify);
                else
                    CancelSelection(notify);
            }
            finally
            {
                DecoLimitLifter.EsuPanelUiHitTestRegistry.Unregister(this);
                DecorationEditorInputScope.End();
                Active = false;
                CloseRequested = false;
                SwitchToSmartBuildRequested = false;
                _unappliedClosePromptOpen = false;
                _unappliedClosePromptAlwaysApply = false;
                RestoreFocusView();
                _hits.Clear();
                _outlinerRows.Clear();
                _selection.Clear();
                ClearSelectionFocusLock(notify: false);
                _boxSelecting = false;
                _boxSelectionPaint = false;
                _boxSelectionConstruct = null;
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
                _rotateDragSymmetryFollow = null;
                _scaleDragAxis = DecorationEditAxis.None;
                _scaleDragUniformGroup = false;
                _scaleDragSnapshotStart = null;
                _scaleDragSymmetryFollow = null;
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
                _gizmoSettingsOpen = false;
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
                CloseSurfaceCoordinateStepEditor();
                ClearSurfaceCoordinateBaseline();
                DestroyPlacementGhost();
                _history.Clear();
                _transactions.Apply();
                _blockPaintOriginalColors.Clear();
                _deletedDecorations.Clear();
                _previewRenderer?.Dispose();
                _previewRenderer = null;
                _materialCatalog = null;
                _forecast = null;
                _lastUsage = null;
            }
        }

        internal void SuspendForModeSwitchHandoff()
        {
            FinalizeSurfaceCoordinateLiveInteraction();
            DecoLimitLifter.EsuPanelUiHitTestRegistry.Unregister(this);
            DecorationEditorInputScope.End();
            Active = false;
            CloseRequested = false;
            SwitchToSmartBuildRequested = false;
            _unappliedClosePromptOpen = false;
            _unappliedClosePromptAlwaysApply = false;
            RestoreFocusView();
            ClearSelectionFocusLock(notify: false);
            _boxSelecting = false;
            _boxSelectionPaint = false;
            _boxSelectionConstruct = null;
            _draggingMeshPalette = false;
            _resizingMeshPalette = false;
            _resizingRightPanel = false;
            _draggingStackDivider = StackDividerKind.None;
            ClearMultiTransformState();
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            _gizmoSettingsOpen = false;
            CloseSurfaceCoordinateStepEditor();
            CloseSurfacePointContextMenu();
            CloseDecorationContextMenu();
            if (!HasPendingBlockPaintChanges())
                _blockPaintOriginalColors.Clear();
        }

        internal void Update()
        {
            if (!Active)
                return;

            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership(CurrentModeLogSource() + " update");
            EsuHudNotifications.SetActiveSource(CurrentModeLogSource());
            _lastMouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_surfaceCoordinateLiveInteractionActive && !Input.GetMouseButton(0))
                FinalizeSurfaceCoordinateLiveInteraction();
            DecorationEditorInputScope.SetMouseOverEditorUi(IsMouseOverEditorUi(_lastMouseGui));
            if (DecorationEditorInputScope.MouseOverEditorUi &&
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
            {
                DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
            }
            RefreshDecorationCache(force: false);
            RefreshForecast(force: false);
            RefreshSelectionFocusLock();
            _viewModes.Tick(_viewMode);
            DecorationEditorOverlay.BeginFrame();
            if (_unappliedClosePromptOpen)
            {
                DecorationEditorInputScope.SetMouseOverEditorUi(true);
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                DrawWorldOverlay();
                return;
            }

            if (_gizmoSettingsOpen)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                RefreshLiveTransformFields();
                DrawWorldOverlay();
                return;
            }

            if (ForegroundContextMenuOpen())
            {
                DecorationEditorInputScope.SetMouseOverEditorUi(true);
                if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f)
                    DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
                else
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    DecorationEditorInputScope.ClaimCameraInputForFrames();
                }
                RefreshLiveTransformFields();
                DrawWorldOverlay();
                return;
            }

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None ||
                _generatorDragAxis != DecorationEditAxis.None ||
                _generatorRotateDragAxis != DecorationEditAxis.None ||
                _surfaceCoordinateLiveInteractionActive)
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

            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership(CurrentModeLogSource() + " OnGUI");
            DrawGui(interactive: true);
        }

        internal void DrawModeSwitchHandoffGui()
        {
            DecoLimitLifter.EsuEditorScope.ClaimGuiOwnership(CurrentModeLogSource() + " handoff GUI");
            DrawGui(interactive: false);
        }

        private void DrawGui(bool interactive)
        {
            _hoveredMesh = null;
            _hoveredMeshHint = null;
            DecorationEditorTheme.Ensure();
            if (Event.current.type == EventType.Repaint)
            {
                _previewSpin += Time.unscaledDeltaTime * 70f;
                DecorationEditorOverlay.Render();
            }

            int previousDepth = GUI.depth;
            bool previousEnabled = GUI.enabled;
            GUI.depth = -10000;
            if (!interactive &&
                Event.current.type != EventType.Repaint &&
                Event.current.type != EventType.Layout)
            {
                GUI.enabled = false;
            }

            try
            {
                GUI.WindowFunction windowFunction = interactive
                    ? DrawModalEditorWindow
                    : DrawModeSwitchHandoffWindow;
                GUI.ModalWindow(
                    _modalWindowId,
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    windowFunction,
                    GUIContent.none,
                    GUIStyle.none);
            }
            finally
            {
                GUI.depth = previousDepth;
                GUI.enabled = previousEnabled;
            }

            if (interactive)
                _textInputFocused = GUIUtility.keyboardControl != 0;
        }

        private void DrawModalEditorWindow(int id)
        {
            bool promptWasOpen = _unappliedClosePromptOpen;
            bool gizmoSettingsWasOpen = _gizmoSettingsOpen;
            bool contextMenuWasOpen = ForegroundContextMenuOpen();
            Event foregroundEvent = Event.current;
            EventType foregroundEventType = foregroundEvent == null
                ? EventType.Ignore
                : foregroundEvent.type;
            bool notificationWasForeground =
                !promptWasOpen &&
                !gizmoSettingsWasOpen &&
                !contextMenuWasOpen &&
                !_viewModeMenuOpen &&
                !_anchorMenuOpen &&
                !EsuConsoleWindow.ContainsMouse(foregroundEvent?.mousePosition ?? Vector2.zero) &&
                EsuHudNotifications.ExpandedPopupOwnsEvent(foregroundEvent);
            if (promptWasOpen)
                DrawDisabledEditorShellBehindPrompt();
            else if (gizmoSettingsWasOpen)
                DrawDisabledEditorShellBehindPrompt();
            else if (contextMenuWasOpen)
                DrawDisabledEditorShellBehindPrompt();
            else if (notificationWasForeground)
                DrawDisabledEditorShellBehindPrompt();
            else
                DrawEditorShell(interactive: true);

            if (promptWasOpen || _unappliedClosePromptOpen)
            {
                Event promptEvent = Event.current;
                EventType promptEventType = promptEvent == null
                    ? EventType.Ignore
                    : promptEvent.type;
                DrawUnappliedClosePrompt();
                ConsumeUnappliedClosePromptInput(promptEvent, promptEventType);
                return;
            }

            if (gizmoSettingsWasOpen || _gizmoSettingsOpen)
            {
                DrawGizmoSettingsMenu();
                Event popupEvent = Event.current;
                if (popupEvent != null &&
                    popupEvent.type != EventType.Used &&
                    IsPromptBlockingEventType(popupEvent.type))
                {
                    if (popupEvent.type == EventType.ScrollWheel)
                        DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
                    else
                    {
                        DecorationEditorInputScope.ClaimBuildInputForFrames();
                        DecorationEditorInputScope.ClaimCameraInputForFrames();
                    }
                    popupEvent.Use();
                }
                return;
            }

            if (contextMenuWasOpen || ForegroundContextMenuOpen())
            {
                Event contextEvent = Event.current;
                EventType contextEventType = contextEvent == null
                    ? EventType.Ignore
                    : contextEvent.type;
                if (contextMenuWasOpen)
                {
                    int previousDepth = GUI.depth;
                    GUI.depth = Math.Min(previousDepth, -20000);
                    try
                    {
                        DrawSurfaceContextMenu();
                        DrawDecorationContextMenu();
                    }
                    finally
                    {
                        GUI.depth = previousDepth;
                    }
                }
                ConsumeForegroundContextMenuInput(contextEvent, contextEventType);
                return;
            }

            if (notificationWasForeground)
            {
                EsuCursorTooltip.BeginFrame(
                    foregroundEvent.mousePosition,
                    suppress: false,
                    allowGuiTooltipFallback: true);
                int previousDepth = GUI.depth;
                GUI.depth = Math.Min(previousDepth, -20000);
                try
                {
                    EsuHudNotifications.DrawExpandedPopup();
                }
                finally
                {
                    GUI.depth = previousDepth;
                }
                EsuCursorTooltip.Draw();
                ConsumeForegroundContextMenuInput(
                    foregroundEvent,
                    foregroundEventType);
                return;
            }

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

        private void DrawDisabledEditorShellBehindPrompt()
        {
            Event current = Event.current;
            EventType originalType =
                DecoLimitLifter.EsuModalInputPolicy.SuppressForDisabledBackground(current);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled &&
                          !EsuHudPreferences.FadeHudBehindModalPopups;
            try
            {
                DrawEditorShell(interactive: false);
            }
            finally
            {
                DecoLimitLifter.EsuModalInputPolicy.RestoreForForeground(
                    current,
                    originalType);
                GUI.enabled = previousEnabled;
            }
        }

        private bool ForegroundContextMenuOpen() =>
            _surfaceContextTargetKind != SurfaceContextTargetKind.None ||
            _decorationContextTarget != null;

        private static void ConsumeForegroundContextMenuInput(
            Event current,
            EventType originalType)
        {
            if (!IsPromptBlockingEventType(originalType))
                return;

            if (originalType == EventType.ScrollWheel)
                DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
            else
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
            }

            if (current != null && current.type != EventType.Used)
                current.Use();
        }

        private static bool IsPromptBlockingEvent(Event current)
        {
            if (current == null)
                return false;

            return IsPromptBlockingEventType(current.type);
        }

        private static bool IsPromptBlockingEventType(EventType eventType) =>
            DecoLimitLifter.EsuModalInputPolicy.IsBlockingEventType(eventType);

        private void ClaimUnappliedClosePromptInput(EventType eventType)
        {
            if (eventType == EventType.ScrollWheel)
            {
                DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
                return;
            }

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
        }

        private void ConsumeUnappliedClosePromptInput(Event current, EventType originalType)
        {
            if (!IsPromptBlockingEventType(originalType))
                return;

            ClaimUnappliedClosePromptInput(originalType);
            if (current != null && current.type != EventType.Used)
                current.Use();
        }

        private void DrawModeSwitchHandoffWindow(int id)
        {
            DrawEditorShell(interactive: false);
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

            if (_unappliedClosePromptOpen)
                return true;

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

        private void DrawEditorShell(bool interactive)
        {
            EsuHudNotifications.SetActiveSource(CurrentModeLogSource());
            if (interactive)
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
            _gizmoSettingsButtonScreenRect = BottomHeaderRects(
                new Rect(
                    bottomInnerScreen.x,
                    bottomInnerScreen.y,
                    bottomInnerScreen.width,
                    EsuHudLayout.Scale(24f))).GizmoSettings;
            Rect meshRect = MeshPaletteRect();
            bool leftStackVisible = IsLeftPanelStackVisible();
            bool rightStackVisible = IsRightPanelStackVisible();
            Vector2 mouse = Event.current.mousePosition;
            _mouseOverWindow = toolbarRect.Contains(mouse) ||
                               EsuConsoleWindow.ContainsMouse(mouse) ||
                               EsuHudNotifications.ContainsMouse(mouse) ||
                               (_unappliedClosePromptOpen && UnappliedClosePromptRect().Contains(mouse)) ||
                               (_viewModeMenuOpen && ViewModeMenuRect(toolbarRect).Contains(mouse)) ||
                               (_anchorMenuOpen && AnchorMenuRect().Contains(mouse)) ||
                               (_gizmoSettingsOpen && GizmoSettingsMenuRect().Contains(mouse)) ||
                               IsSurfaceContextMenuAt(mouse) ||
                               IsDecorationContextMenuAt(mouse) ||
                               (rightStackVisible && rightRect.Contains(mouse)) ||
                               bottomRect.Contains(mouse) ||
                               (leftStackVisible && meshRect.Contains(mouse));
            if (interactive)
            {
                DecorationEditorInputScope.SetMouseOverEditorUi(_mouseOverWindow);
                ClaimMouseWheelOverEditorUi(Event.current, _mouseOverWindow);
            }

            if (interactive && leftStackVisible)
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
                    EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));
                meshRect = _meshPaletteRect;
                HandleMeshPaletteDrag(meshRect);
            }

            if (interactive && rightStackVisible)
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
                    EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));
                rightRect = _rightPanelRect;
            }

            EsuHudChrome.DrawPanel(toolbarRect);
            Rect toolbarInner = EsuHudLayout.PanelInnerRect(toolbarRect);
            GUILayout.BeginArea(toolbarInner);
            DrawTopToolbar(
                new Rect(0f, 0f, toolbarInner.width, toolbarInner.height),
                toolbarInner.position);
            GUILayout.EndArea();

            if (leftStackVisible)
            {
                DrawLeftPanelStack(meshRect);
                EsuHudLayout.DrawResizeGrip(meshRect, leftEdge: false);
                if (interactive)
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(meshRect, leftEdge: false), "Drag to resize the left panel stack.");
            }

            if (rightStackVisible)
            {
                DrawRightPanel(rightRect);
                EsuHudLayout.DrawResizeGrip(rightRect, leftEdge: true);
                if (interactive)
                    EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(rightRect, leftEdge: true), "Drag to resize the right panel stack.");
            }

            EsuHudChrome.DrawPanel(bottomRect);
            Rect bottomInner = EsuHudLayout.PanelInnerRect(bottomRect);
            GUILayout.BeginArea(bottomInner);
            DrawBottomPanel(bottomInner.width);
            GUILayout.EndArea();

            // Keep expanded notifications above every editor panel while leaving
            // world/context menus and the console as the final modal foregrounds.
            EsuHudNotifications.DrawExpandedPopup();
            if (interactive)
            {
                DrawAnchorMenu();
                DrawGizmoSettingsMenu();
            }

            if (interactive)
            {
                DrawMeshPreviewCard();
                DrawSurfaceContextMenu();
                DrawDecorationContextMenu();
                DrawBoxSelectionMarquee();
            }
            if (interactive)
                EsuConsoleWindow.Draw();
            else
                EsuConsoleWindow.Draw(interactive: false);
            if (interactive)
            {
                DrawViewModeMenu(toolbarRect);
                EsuCursorTooltip.Draw();
                PersistPanelState();
            }
        }

        private bool TooltipInputSuppressed() =>
            _unappliedClosePromptOpen ||
            ForegroundContextMenuOpen() ||
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
                EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));
            s_rightPanelRect = _rightPanelRect;
            return _rightPanelRect;
        }

        private Rect StatusRect(Rect rightRect)
        {
            float height = BottomPanelHeight();
            return EsuHudLayout.BottomStripRect(height);
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
                EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));
            s_meshPaletteRect = _meshPaletteRect;
            return _meshPaletteRect;
        }

        private float BottomPanelHeight()
        {
            return EsuHudLayout.BottomStripHeight();
        }

        private float ToolbarBottomLimit() => EsuHudLayout.EditorPanelTopLimit(54f);

        private float AvailableEditorPanelHeight() =>
            EsuHudLayout.AvailablePanelHeight(
                ToolbarBottomLimit(),
                EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));

        private Rect DefaultMeshPaletteRect()
        {
            float width = Mathf.Min(EsuHudLayout.Scale(520f), Mathf.Max(EsuHudLayout.Scale(420f), Screen.width * 0.28f));
            float topLimit = ToolbarBottomLimit();
            float height = Mathf.Min(
                MaxMeshPaletteHeight(),
                Mathf.Max(MinMeshPaletteHeight(), AvailableEditorPanelHeight()));
            return new Rect(EsuHudLayout.EditorSideMargin, topLimit, width, height);
        }

        private Rect DefaultRightPanelRect()
        {
            float width = Mathf.Min(EsuHudLayout.Scale(390f), Mathf.Max(EsuHudLayout.Scale(300f), Screen.width * 0.23f));
            float topLimit = ToolbarBottomLimit();
            float height = Mathf.Min(
                MaxRightPanelHeight(),
                Mathf.Max(MinRightPanelHeight(), AvailableEditorPanelHeight()));
            float margin = EsuHudLayout.EditorSideMargin;
            return new Rect(Screen.width - width - margin, topLimit, width, height);
        }

        private float MinMeshPaletteWidth() => EsuHudLayout.Scale(330f);

        private float MinMeshPaletteHeight() =>
            EsuHudLayout.ClampPanelMinimum(
                EsuHudLayout.Scale(330f),
                AvailableEditorPanelHeight());

        private float MaxMeshPaletteWidth() => Mathf.Max(MinMeshPaletteWidth(), Screen.width * 0.62f);

        private float MaxMeshPaletteHeight() =>
            Mathf.Max(MinMeshPaletteHeight(), AvailableEditorPanelHeight());

        private float MinRightPanelWidth() => EsuHudLayout.Scale(260f);

        private float MinRightPanelHeight() =>
            EsuHudLayout.ClampPanelMinimum(
                EsuHudLayout.Scale(230f),
                AvailableEditorPanelHeight());

        private float MaxRightPanelWidth() => Mathf.Max(MinRightPanelWidth(), Screen.width * 0.58f);

        private float MaxRightPanelHeight() =>
            Mathf.Max(MinRightPanelHeight(), AvailableEditorPanelHeight());

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _meshPaletteRect = DefaultMeshPaletteRect();
            _rightPanelRect = DefaultRightPanelRect();
            _leftStackBottomRatio = LeftStackDefaultBottomRatio;
            _rightStackBottomRatio = RightStackDefaultBottomRatio;
            _surfaceCoordinateBottomRatio = 0.5f;
            _surfaceCoordinateSplitCustomized = false;
            s_leftStackBottomRatio = _leftStackBottomRatio;
            s_rightStackBottomRatio = _rightStackBottomRatio;
            s_surfaceCoordinateBottomRatio = _surfaceCoordinateBottomRatio;
            s_surfaceCoordinateSplitCustomized = false;
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

        private EsuPanelUiHit HitTestEditorUi(Vector2 mouse)
        {
            if (!Active)
                return EsuPanelUiHit.Miss(mouse);

            string editor = CurrentModeLogSource();
            if (ToolbarRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, "toolbar", mouse);
            if (EsuConsoleWindow.ContainsMouse(mouse))
                return EsuPanelUiHit.Found(editor, "console", mouse);
            if (EsuHudNotifications.ContainsMouse(mouse))
                return EsuPanelUiHit.Found(editor, "notification", mouse);
            if (_unappliedClosePromptOpen && UnappliedClosePromptRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, "prompt", mouse);
            if (_viewModeMenuOpen && ViewModeMenuRect(ToolbarRect()).Contains(mouse))
                return EsuPanelUiHit.Found(editor, "view_mode_menu", mouse);
            if (_anchorMenuOpen && AnchorMenuRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, "anchor_menu", mouse);
            if (_gizmoSettingsOpen && GizmoSettingsMenuRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, "gizmo_settings_menu", mouse);
            if (IsSurfaceContextMenuAt(mouse))
                return EsuPanelUiHit.Found(editor, "surface_context_menu", mouse);
            if (IsDecorationContextMenuAt(mouse))
                return EsuPanelUiHit.Found(editor, "decoration_context_menu", mouse);
            if (IsRightPanelStackVisible() && RightPanelRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, IsSurfaceMode ? "surface_right_panel" : "right_panel", mouse);
            if (StatusRect(RightPanelRect()).Contains(mouse))
                return EsuPanelUiHit.Found(editor, "status", mouse);
            if (IsLeftPanelStackVisible() && MeshPaletteRect().Contains(mouse))
                return EsuPanelUiHit.Found(editor, IsSurfaceMode ? "surface_left_panel" : "left_panel", mouse);

            return EsuPanelUiHit.Miss(mouse);
        }

        private bool IsMouseOverEditorUi(Vector2 mouse) =>
            HitTestEditorUi(mouse).IsHit;

        private bool IsCurrentMouseOverEditorUi() =>
            IsMouseOverEditorUi(MouseGuiPosition());

        private static Vector2 MouseGuiPosition() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        private static void ClaimMouseWheelOverEditorUi(Event current, bool overUi)
        {
            if (!overUi ||
                current == null ||
                current.type != EventType.ScrollWheel)
            {
                return;
            }

            DecorationEditorInputScope.ClaimMouseWheelInputForFrames();
        }

        private Rect UnappliedClosePromptRect()
        {
            float availableWidth = Mathf.Max(1f, Screen.width - EsuHudLayout.Scale(40f));
            float availableHeight = Mathf.Max(1f, Screen.height - EsuHudLayout.Scale(40f));
            float width = Mathf.Max(
                Mathf.Min(EsuHudLayout.Scale(420f), availableWidth),
                Mathf.Min(EsuHudLayout.Scale(640f), availableWidth));
            float height = Mathf.Max(
                Mathf.Min(EsuHudLayout.Scale(220f), availableHeight),
                Mathf.Min(EsuHudLayout.Scale(276f), availableHeight));
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        private void DrawUnappliedClosePrompt()
        {
            if (!_unappliedClosePromptOpen)
                return;

            if (EsuHudPreferences.FadeHudBehindModalPopups)
            {
                GUI.DrawTexture(
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    DecorationEditorTheme.DimTexture);
            }

            Rect rect = UnappliedClosePromptRect();
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect inner = EsuHudLayout.PanelInnerRect(rect, 10f);
            float gap = EsuHudLayout.Scale(8f);
            float headerHeight = EsuHudLayout.Scale(34f);
            float warningHeight = EsuHudLayout.Scale(26f);
            float toggleHeight = EsuHudLayout.Scale(32f);
            float actionHeight = EsuHudLayout.Scale(40f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            GUI.Box(headerRect, GUIContent.none, DecorationEditorTheme.DialogHeader);
            GUI.Label(
                headerRect,
                new GUIContent("Unapplied decorations", DecorationEditorIconCatalog.Get("save")),
                DecorationEditorTheme.DialogTitle);

            float y = headerRect.yMax + gap;
            Rect warningRect = new Rect(inner.x, y, inner.width, warningHeight);
            GUI.Label(
                warningRect,
                "Decoration Edit has unapplied preview changes. Apply them before closing?",
                DecorationEditorTheme.DialogWarning);
            y = warningRect.yMax + EsuHudLayout.Scale(4f);

            Rect actionsRect = new Rect(
                inner.x,
                inner.yMax - actionHeight,
                inner.width,
                actionHeight);
            Rect toggleRect = new Rect(
                inner.x,
                actionsRect.y - gap - toggleHeight,
                inner.width,
                toggleHeight);
            Rect bodyRect = new Rect(
                inner.x,
                y,
                inner.width,
                Mathf.Max(EsuHudLayout.Scale(36f), toggleRect.y - gap - y));
            GUI.Label(
                bodyRect,
                "Apply commits the current preview to the craft. Discard restores the last applied state. Keep editing leaves the editor open.",
                DecorationEditorTheme.DialogBody);

            bool alwaysApply = GUI.Toggle(
                toggleRect,
                _unappliedClosePromptAlwaysApply,
                new GUIContent(
                    (_unappliedClosePromptAlwaysApply ? "[x] " : "[ ] ") +
                    "Always apply with Ctrl+D next time",
                    "Store this preference in ESU options. You can re-enable the warning from the ESU options screen."),
                _unappliedClosePromptAlwaysApply
                    ? DecorationEditorTheme.DialogToggleSelected
                    : DecorationEditorTheme.DialogToggle);
            _unappliedClosePromptAlwaysApply = alwaysApply;

            DrawUnappliedClosePromptActions(actionsRect);
            HandleUnappliedClosePromptKeyboard();
        }

        private void DrawUnappliedClosePromptActions(Rect rect)
        {
            float gap = EsuHudLayout.Scale(8f);
            float buttonWidth = Mathf.Max(1f, (rect.width - gap * 2f) / 3f);
            Rect applyRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect discardRect = new Rect(applyRect.xMax + gap, rect.y, buttonWidth, rect.height);
            Rect keepEditingRect = new Rect(discardRect.xMax + gap, rect.y, buttonWidth, rect.height);

            if (PromptActionButton(
                    applyRect,
                    "save",
                    "Apply and close",
                    "Commit the preview changes and close Decoration Edit Mode.",
                    DecorationEditorTheme.DialogActiveButton))
                ApplyAndCloseFromPrompt();

            if (PromptActionButton(
                    discardRect,
                    "cancel",
                    "Discard",
                    "Restore the last applied Decoration Edit state and close.",
                    DecorationEditorTheme.DialogButton))
                DiscardAndCloseFromPrompt();

            if (PromptActionButton(
                    keepEditingRect,
                    "close",
                    "Keep editing",
                    "Close this warning and return to Decoration Edit Mode.",
                    DecorationEditorTheme.DialogButton))
                CloseUnappliedClosePrompt();
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

        private void HandleUnappliedClosePromptKeyboard()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
                return;

            if (current.keyCode == KeyCode.Return ||
                current.keyCode == KeyCode.KeypadEnter)
            {
                ApplyAndCloseFromPrompt();
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                CloseUnappliedClosePrompt();
                InfoStore.Add("Decoration Edit close cancelled.");
                current.Use();
                return;
            }

            current.Use();
        }

        private void OpenUnappliedClosePrompt()
        {
            _unappliedClosePromptOpen = true;
            _unappliedClosePromptAlwaysApply = false;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            _gizmoSettingsOpen = false;
            CloseSurfacePointContextMenu();
            CloseDecorationContextMenu();
            InfoStore.Add("Unapplied Decoration Edit changes. Apply, discard, or keep editing.");
        }

        private void CloseUnappliedClosePrompt()
        {
            _unappliedClosePromptOpen = false;
            _unappliedClosePromptAlwaysApply = false;
        }

        private void ApplyAndCloseFromPrompt()
        {
            if (_unappliedClosePromptAlwaysApply)
                DisableHotkeyClosePrompt();

            CloseUnappliedClosePrompt();
            CloseRequested = true;
            CloseApplies = true;
            InfoStore.Add("Decoration Edit changes will be applied before closing.");
        }

        private void DiscardAndCloseFromPrompt()
        {
            CloseUnappliedClosePrompt();
            CloseRequested = true;
            CloseApplies = false;
            InfoStore.Add("Decoration Edit changes will be discarded before closing.");
        }

        private static bool ShouldPromptBeforeHotkeyClose()
        {
            try
            {
                return SerializationHudProfile.Data?.DecorationEditPromptBeforeHotkeyClose != false;
            }
            catch
            {
                return true;
            }
        }

        private static void DisableHotkeyClosePrompt()
        {
            try
            {
                SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
                if (data == null)
                    return;

                data.DecorationEditPromptBeforeHotkeyClose = false;
                ProfileManager.Instance.Save(module => module is SerializationHudProfile);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not persist Decoration Edit Ctrl+D warning preference",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

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
                    EsuHudLayout.BottomPanelLimit(BottomPanelHeight()));
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
            s_surfaceCoordinateBottomRatio = _surfaceCoordinateBottomRatio;
            s_surfaceCoordinateSplitCustomized = _surfaceCoordinateSplitCustomized;
            s_showMeshPalette = _tool == DecorationEditorTool.Paint
                ? _showMeshPaletteBeforePaint
                : _showMeshPalette;
            s_showInspectorPanel = _showInspectorPanel;
            s_showOutlinerPanel = _showOutlinerPanel;
            s_showAnchorPanel = _showAnchorPanel;
            s_showSurfacePanel = _showSurfacePanel;
            s_showSurfaceToolsPanel = _showSurfaceToolsPanel;
            s_showSurfaceDraftList = _showSurfaceDraftList;
            s_showSurfaceCoordinates = _showSurfaceCoordinates;
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
            int leftControlCount = IsSurfaceMode ? 10 : 12;
            int rightControlCount = IsSurfaceMode ? 7 : 9;
            _toolbarLeftControlWidth = EsuHudLayout.ToolbarControlWidth(
                budget.LeftRailWidth,
                leftControlCount,
                58f);
            _toolbarRightControlWidth = EsuHudLayout.ToolbarControlWidth(
                budget.RightControlsWidth,
                rightControlCount,
                54f);

            GUILayout.BeginArea(frame.Rect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth));
            {
                ModeSwitchButton();
                if (IsSurfaceMode)
                {
                    SurfaceCreateToolButton();
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
                    ToolButton(DecorationEditorTool.Select, SelectToolIcon(), SelectToolLabel(), "Click decoration centers or drag a selection box.");
                    ToolButton(DecorationEditorTool.Move, "move", "Move", "Drag XYZ handles. Multi-selection moves as one group. Snaps to " + EsuTransformSnapSettings.Format(DecorationMoveSnap) + "m.");
                    ToolButton(DecorationEditorTool.Rotate, "rotate", "Rotate", "Drag RGB rotation rings. Multi-selection rotates around the group center. Snaps to " + EsuTransformSnapSettings.Format(DecorationRotateSnapDegrees) + " degrees.");
                    ToolButton(DecorationEditorTool.Scale, "scale", "Scale", "Drag RGB axes for axis scale, or the center handle to scale a selected group uniformly. Snaps to " + EsuTransformSnapSettings.Format(DecorationScaleSnap) + ".");
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
            if (IconButton(
                    "undo",
                    "Z",
                    DecorationEditorTheme.Button,
                    $"Ctrl+Z: undo the last editor action ({_history.UndoCount} available).",
                    _history.CanUndo))
                UndoEdit();
            if (IconButton(
                    "redo",
                    "Y",
                    DecorationEditorTheme.Button,
                    $"Ctrl+Y/Ctrl+Shift+Z: redo the last editor action ({_history.RedoCount} available).",
                    _history.CanRedo))
                RedoEdit();
            if (AttentionIconButton("save", "Apply", DecorationEditorTheme.Button, "Commit preview changes."))
                ApplySelection();
            if (AttentionIconButton("cancel", "Cancel", DecorationEditorTheme.Button, "Restore the current preview."))
                CancelSelection();
            if (IconButton("close", "Close", DecorationEditorTheme.Button, "Close Decoration Edit Mode. Dirty previews ask whether to apply or discard."))
                RequestHotkeyClose();
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
            string fullLabel = surface ? "Surf" : "Deco";
            if (GUILayout.Button(
                    new GUIContent(
                        LeftToolbarLabel(fullLabel, surface ? "S" : "D"),
                        DecorationEditorIconCatalog.Get("build"),
                        surface
                            ? "Tab: switch to Smart Builder when Surface Builder is clean."
                            : "Tab: switch to Surface Builder when Decoration Edit is clean."),
                    DecorationEditorTheme.ToolButton(true),
                    GUILayout.Width(_toolbarLeftControlWidth),
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
            string fullLabel = local ? "Local" : "Global";
            if (GUILayout.Button(
                    new GUIContent(
                        LeftToolbarLabel(fullLabel, local ? "L" : "G"),
                        DecorationEditorIconCatalog.Get("axis"),
                        "Transform orientation for Move, Rotate, and Scale."),
                    DecorationEditorTheme.ToolButton(local),
                    GUILayout.Width(_toolbarLeftControlWidth),
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
                    new GUIContent(
                        LeftToolbarLabel(label, label),
                        DecorationEditorIconCatalog.Get(SymmetryIconKey(axis)),
                        tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.Width(_toolbarLeftControlWidth),
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
            if (GUILayout.Button(
                    new GUIContent(
                        LeftToolbarLabel("Anchor", "A"),
                        DecorationEditorIconCatalog.Get("anchor"),
                        "Select and move decoration anchors. Anchor settings are in the bottom status panel."),
                    DecorationEditorTheme.ToolButton(_tool == DecorationEditorTool.Anchor),
                    GUILayout.Width(_toolbarLeftControlWidth),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                SetActiveTool(DecorationEditorTool.Anchor);
                _anchorMenuOpen = false;
                _viewModeMenuOpen = false;
            }
        }

        private void PanelToggle(string icon, string label, ref bool state, string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(
                        RightToolbarLabel(label, CompactToolbarLabel(label)),
                        DecorationEditorIconCatalog.Get(icon),
                        tooltip),
                    DecorationEditorTheme.ToolButton(state),
                    GUILayout.Width(_toolbarRightControlWidth),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                state = !state;
            }
        }

        private void SurfaceCreateToolButton()
        {
            string label = SurfaceCreateToolLabel();
            if (GUILayout.Button(
                    new GUIContent(
                        LeftToolbarLabel(label, CompactToolbarLabel(label)),
                        DecorationEditorIconCatalog.Get(SurfaceCreateToolIcon()),
                        "Cycle Surface Builder creation tools."),
                    DecorationEditorTheme.ToolButton(IsSurfaceCreateToolActive()),
                    GUILayout.Width(_toolbarLeftControlWidth),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                CycleSurfaceCreateTool();
            }
        }

        private bool IsSurfaceCreateToolActive() =>
            _surfaceBuilderTool == SurfaceBuilderTool.Draw ||
            _surfaceBuilderTool == SurfaceBuilderTool.Path ||
            _surfaceBuilderTool == SurfaceBuilderTool.Circle;

        private string SurfaceCreateToolIcon() =>
            _surfaceBuilderTool == SurfaceBuilderTool.Path ||
            _surfaceBuilderTool == SurfaceBuilderTool.Circle
                ? GeneratorToolIcon(_surfaceExtraTool)
                : "draw";

        private string SurfaceCreateToolLabel()
        {
            if (_surfaceBuilderTool != SurfaceBuilderTool.Path &&
                _surfaceBuilderTool != SurfaceBuilderTool.Circle)
            {
                return "Draw";
            }

            return GeneratorToolToolbarLabel(_surfaceExtraTool);
        }

        private static string GeneratorToolToolbarLabel(SurfaceExtraTool tool)
        {
            switch (tool)
            {
                case SurfaceExtraTool.Circle:
                    return "Circle";
                case SurfaceExtraTool.PartialCircle:
                    return "Arc";
                case SurfaceExtraTool.Cone2D:
                    return "2D";
                case SurfaceExtraTool.Sphere:
                    return "Sphere";
                case SurfaceExtraTool.PartialSphere:
                    return "P.sph";
                case SurfaceExtraTool.Cone:
                    return "Cone";
                case SurfaceExtraTool.Frustum:
                    return "Frust";
                case SurfaceExtraTool.Quad:
                    return "Quad";
                case SurfaceExtraTool.Polygon:
                    return "Poly";
                case SurfaceExtraTool.Tube:
                    return "Tube";
                default:
                    return "Path";
            }
        }

        private static string GeneratorToolIcon(SurfaceExtraTool tool)
        {
            switch (tool)
            {
                case SurfaceExtraTool.Circle:
                    return "circle";
                case SurfaceExtraTool.PartialCircle:
                    return "arc";
                case SurfaceExtraTool.Cone2D:
                    return "cone2d";
                case SurfaceExtraTool.Sphere:
                    return "sphere";
                case SurfaceExtraTool.PartialSphere:
                    return "partialSphere";
                case SurfaceExtraTool.Cone:
                    return "cone";
                case SurfaceExtraTool.Frustum:
                    return "frustum";
                case SurfaceExtraTool.Quad:
                    return "cube";
                case SurfaceExtraTool.Polygon:
                    return "circle";
                case SurfaceExtraTool.Tube:
                    return "path";
                default:
                    return "path";
            }
        }

        private void SurfaceToolButton(
            SurfaceBuilderTool tool,
            string icon,
            string label,
            string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(
                        LeftToolbarLabel(label, CompactToolbarLabel(label)),
                        DecorationEditorIconCatalog.Get(icon),
                        tooltip),
                    DecorationEditorTheme.ToolButton(_surfaceBuilderTool == tool),
                    GUILayout.Width(_toolbarLeftControlWidth),
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
            var content = new GUIContent(
                LeftToolbarLabel(label, CompactToolbarLabel(label)),
                DecorationEditorIconCatalog.Get(icon),
                tooltip);
            if (GUILayout.Button(
                    content,
                    style,
                    GUILayout.Width(_toolbarLeftControlWidth),
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

        private string SelectToolIcon() =>
            _selectionMode == DecorationSelectionMode.Box ? "boxSelect" : "select";

        private string LeftToolbarLabel(string fullLabel, string compactLabel) =>
            EsuHudLayout.ToolbarLabel(
                fullLabel,
                compactLabel,
                _toolbarLeftControlWidth);

        private string RightToolbarLabel(string fullLabel, string compactLabel) =>
            EsuHudLayout.ToolbarLabel(
                fullLabel,
                compactLabel,
                _toolbarRightControlWidth);

        private static string CompactToolbarLabel(string label) =>
            string.IsNullOrEmpty(label) ? string.Empty : label.Substring(0, 1);

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

        private void ToggleSelectionMode()
        {
            if (_selectionMode != DecorationSelectionMode.Box &&
                TryHandleSelectionFocusBulkRequest())
            {
                return;
            }

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
                Color accent = _boxSelectionPaint
                    ? PaintDisplayColor(_paintColor)
                    : new Color(0.05f, 0.9f, 1f, 1f);
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.14f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f);
                DrawGuiBorder(rect, Mathf.Max(1f, EsuHudLayout.Scale(1f)));

                string label = _boxSelectCandidateCount.ToString("N0", CultureInfo.InvariantCulture);
                Rect labelRect = new Rect(
                    rect.xMin,
                    Mathf.Max(0f, rect.yMin - EsuHudLayout.Scale(22f)),
                    EsuHudLayout.Scale(_boxSelectionPaint ? 220f : 118f),
                    EsuHudLayout.Scale(20f));
                GUI.color = new Color(0f, 0.08f, 0.1f, 0.86f);
                GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 1f);
                GUI.Label(
                    labelRect,
                    _boxSelectionPaint
                        ? label + " deco | blocks on release"
                        : label + (_selectionXray ? " x-ray" : " visible"),
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
                new GUIContent(
                    RightToolbarLabel(label, CompactToolbarLabel(label)),
                    DecorationEditorIconCatalog.Get(icon),
                    tooltip),
                style,
                GUILayout.Width(_toolbarRightControlWidth),
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
                    new GUIContent(
                        RightToolbarLabel(label, CompactToolbarLabel(label)),
                        DecorationEditorIconCatalog.Get(icon),
                        tooltip),
                    actual,
                    GUILayout.Width(_toolbarRightControlWidth),
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
            return Math.Min(Math.Max(ratio, 0f), 1f);
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
            EsuHudChrome.DrawPanel(rect);
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
            EsuHudChrome.DrawPanel(rect);
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
            List<Decoration> rows = SelectedAnchorDecorations(tether).ToList();

            _anchorListScroll = GUILayout.BeginScrollView(
                _anchorListScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(
                    0f,
                    height - EsuHudLayout.Scale(_tool == DecorationEditorTool.Anchor ? 84f : 52f))));
            for (int index = 0; index < rows.Count; index++)
            {
                Decoration decoration = rows[index];
                GUIStyle style = _selection.Contains(decoration) || ReferenceEquals(decoration, _selected)
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row;
                string meshName = CompactText(MeshName(decoration.MeshGuid.Us), 36);
                string detail = $"c{decoration.Color.Us} | {decoration.MeshGuid.Us.ToString("N").Substring(0, 8)}";
                Rect rowRect = GUILayoutUtility.GetRect(
                    1f,
                    EsuHudLayout.Scale(OutlinerRowHeight),
                    GUILayout.ExpandWidth(true));
                bool contextOpened = TryOpenDecorationContextMenuFromList(
                    rowRect,
                    decoration,
                    _selectedConstruct);
                if (!contextOpened &&
                    GUI.Button(
                        rowRect,
                        new GUIContent(meshName + "  " + detail, DecorationEditorIconCatalog.Get("mesh"), "Select this decoration on the same anchor. Ctrl toggles one row; Shift selects a range; right-click opens actions."),
                        style))
                {
                    SelectAnchorDecorationRow(rows, index, decoration);
                }
            }
            GUILayout.EndScrollView();
        }

        private void SelectAnchorDecorationRow(List<Decoration> rows, int clickedIndex, Decoration clicked)
        {
            if (rows == null ||
                clicked == null ||
                clicked.IsDeleted ||
                _selectedConstruct == null)
            {
                return;
            }

            if (TryHandleSelectionFocusRequest(clicked, _selectedConstruct))
                return;

            bool control = IsControlHeld();
            bool shift = IsShiftHeld();
            if (shift &&
                _anchorRangeAnchor != null &&
                TryFindAnchorDecorationIndex(rows, _anchorRangeAnchor, out int anchorIndex))
            {
                SelectAnchorDecorationRange(rows, anchorIndex, clickedIndex, clicked, additive: control);
                _anchorRangeAnchor = clicked;
                return;
            }

            if (control || shift)
            {
                ToggleAnchorDecorationSelection(clicked);
                _anchorRangeAnchor = clicked;
                return;
            }

            Select(clicked, _selectedConstruct);
            _anchorRangeAnchor = clicked;
        }

        private static bool TryFindAnchorDecorationIndex(
            List<Decoration> rows,
            Decoration decoration,
            out int rowIndex)
        {
            rowIndex = -1;
            if (rows == null || decoration == null || decoration.IsDeleted)
                return false;

            for (int index = 0; index < rows.Count; index++)
            {
                if (ReferenceEquals(rows[index], decoration))
                {
                    rowIndex = index;
                    return true;
                }
            }

            return false;
        }

        private void SelectAnchorDecorationRange(
            List<Decoration> rows,
            int anchorIndex,
            int clickedIndex,
            Decoration clicked,
            bool additive)
        {
            if (rows == null ||
                clicked == null ||
                clicked.IsDeleted ||
                _selectedConstruct == null)
            {
                return;
            }

            if (TryHandleSelectionFocusRequest(clicked, _selectedConstruct))
                return;

            if (!additive)
                _selection.Clear();

            int start = Mathf.Min(anchorIndex, clickedIndex);
            int end = Mathf.Max(anchorIndex, clickedIndex);
            for (int index = start; index <= end; index++)
            {
                Decoration decoration = rows[index];
                if (decoration != null && !decoration.IsDeleted)
                    _selection.Add(decoration);
            }

            if (_selection.Count == 0)
                _selection.Add(clicked);

            SetPrimarySelection(clicked, _selectedConstruct);
            if (_tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
            {
                _tool = DecorationEditorTool.Move;
            }
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
        }

        private void ToggleAnchorDecorationSelection(Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted || _selectedConstruct == null)
                return;

            if (TryHandleSelectionFocusRequest(decoration, _selectedConstruct))
                return;

            if (_selection.Contains(decoration))
            {
                _selection.Remove(decoration);
                if (ReferenceEquals(_selected, decoration))
                    PromoteOutlinerSelection(_selectedConstruct);
            }
            else
            {
                _selection.Add(decoration);
                SetPrimarySelection(decoration, _selectedConstruct);
            }

            if (_selection.Count > 0 &&
                _tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
            {
                _tool = DecorationEditorTool.Move;
            }
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
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
            EsuHudChrome.DrawCompactIconHeader(text, iconKey);
        }

        private static void DrawCompactIconHeader(
            string text,
            string iconKey,
            params GUILayoutOption[] options)
        {
            EsuHudChrome.DrawCompactIconHeader(text, iconKey, options);
        }

        private static void DrawCompactIconHeader(Rect rect, string text, string iconKey)
        {
            EsuHudChrome.DrawCompactIconHeader(rect, text, iconKey);
        }

        private IEnumerable<Decoration> SelectedAnchorDecorations(Vector3i tether)
        {
            return SelectedAnchorDecorations(_selectedConstruct, tether);
        }

        private IEnumerable<Decoration> SelectedAnchorDecorations(
            AllConstruct construct,
            Vector3i tether)
        {
            var decorations = construct?.Decorations as AllConstructDecorations;
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
            bool contextOpened = row.Kind == DecorationOutlinerRowKind.Decoration &&
                                 row.Decoration != null &&
                                 TryOpenDecorationContextMenuFromList(
                                     rowRect,
                                     row.Decoration,
                                     row.Construct);
            if (!contextOpened && GUI.Button(rowRect, GUIContent.none, style))
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
                    ? "Select this decoration. Right-click for selection actions."
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

            if (TryHandleSelectionFocusRequest(row.Decoration, row.Construct))
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

            if (TryHandleSelectionFocusRequest(clicked.Decoration, clicked.Construct))
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

            if (TryHandleSelectionFocusRequest(row.Decoration, row.Construct))
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

        private void DrawInspector(float height, float availableWidth)
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
                DrawDecorationClipboardRows();
                GUILayout.EndScrollView();
                return;
            }

            GUILayout.Label(MeshName(_selected.MeshGuid.Us), DecorationEditorTheme.BodyWrap);
            DrawDecorationClipboardRows();
            DecorationEditorTheme.Separator();
            DrawColorEditor(availableWidth);
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

        private void DrawColorEditor(float availableWidth)
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
                DrawPaintColorGrid(
                    availableWidth,
                    8,
                    SelectedColorMatches,
                    color => "Set the selected decoration paint color to #" +
                             color.ToString(CultureInfo.InvariantCulture) + ".",
                    SetSelectedColor);
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
            _colorText = color.ToString(CultureInfo.InvariantCulture);
            _paintColor = color;

            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count > 1)
            {
                SetSelectedDecorationsColor(decorations, color);
                return;
            }

            if (_selected.Color.Us == color)
                return;

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            SymmetryFollowContext symmetryFollow = BeginSymmetryFollow(before, reportSkipped: true);
            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            _selected.Color.Us = color;
            if (!TryApplySymmetryFollow(symmetryFollow, reportInvalid: true))
            {
                RestoreSymmetryEditState(selectedRollback, symmetryFollow, targetRollback);
                return;
            }

            MarkSelectedDirty();
            RecordSnapshotEdit("Set color", before, symmetryFollow);
        }

        private bool SelectedColorMatches(int color)
        {
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
                return _selected != null && !_selected.IsDeleted && _selected.Color.Us == color;

            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    decoration.Color.Us != color)
                {
                    return false;
                }
            }

            return true;
        }

        private int CountSelectedDecorationsWithColor(int color)
        {
            int count = 0;
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration != null &&
                    !decoration.IsDeleted &&
                    decoration.Color.Us == color)
                {
                    count++;
                }
            }

            return count;
        }

        private void SetSelectedDecorationsColor(List<Decoration> decorations, int color)
        {
            if (decorations == null || decorations.Count == 0 || _selectedConstruct == null)
                return;

            var historyDecorations = new List<Decoration>();
            var before = new List<DecorationEditSnapshot>();
            var after = new List<DecorationEditSnapshot>();
            int changedCount = 0;
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    decoration.Color.Us == color)
                {
                    continue;
                }

                var snapshot = new DecorationEditSnapshot(decoration);
                decoration.Color.Us = color;
                decoration.Changed();
                historyDecorations.Add(decoration);
                before.Add(snapshot);
                after.Add(new DecorationEditSnapshot(decoration));
                _transactions.TrackEdit(decoration, snapshot);
                changedCount++;
            }

            if (changedCount == 0)
            {
                UpdateDirtyFromSelection();
                return;
            }

            // Keep the actual marquee/selection primary in the command even when
            // it already had the requested color. Its identical before/after
            // snapshots make selection restoration deterministic without adding
            // a false edit to the session transaction.
            if (_selected != null &&
                !_selected.IsDeleted &&
                decorations.Contains(_selected) &&
                !historyDecorations.Contains(_selected))
            {
                var unchangedPrimary = new DecorationEditSnapshot(_selected);
                historyDecorations.Add(_selected);
                before.Add(unchangedPrimary);
                after.Add(unchangedPrimary);
            }

            int primaryIndex = ResolvePaintHistoryPrimaryIndex(
                historyDecorations,
                _selected);

            _dirty = true;
            _history.Record(new DecorationSnapshotBatchCommand(
                "Set color",
                _selectedConstruct,
                historyDecorations.ToArray(),
                before.ToArray(),
                after.ToArray(),
                primaryIndex));
            UpdateDirtyFromSelection();
            RefreshForecast(force: true);
        }

        internal static int ResolvePaintHistoryPrimaryIndex(
            IReadOnlyList<Decoration> decorations,
            Decoration primary)
        {
            if (decorations == null || decorations.Count == 0)
                return -1;

            for (int index = 0; index < decorations.Count; index++)
            {
                if (ReferenceEquals(decorations[index], primary))
                    return index;
            }

            return 0;
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

            EsuHudChrome.DrawPanel(rect);
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
            EsuHudChrome.DrawPanel(rect);
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
            GUILayout.Label("Pick a color. Click a decoration or block, or drag a rectangle to paint everything inside it.", DecorationEditorTheme.MiniWrap);
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
                if (EsuHudPreferences.ResponsivePaintPalettes)
                {
                    DrawPaintColorGrid(
                        Mathf.Max(1f, listRect.width - EsuHudLayout.Scale(22f)),
                        1,
                        color => color == _paintColor || SelectedColorMatches(color),
                        color => "Set the paint brush to color #" +
                                 color.ToString(CultureInfo.InvariantCulture) + ".",
                        SetPaintPaletteColor);
                }
                else
                {
                    for (int color = 0; color <= 31; color++)
                        DrawPaintColorRow(color);
                }
                GUILayout.EndScrollView();
                GUI.EndGroup();
            }
            GUI.EndGroup();
        }

        private void DrawPaintColorRow(int color)
        {
            bool brush = color == _paintColor;
            bool selected = SelectedColorMatches(color);
            GUIStyle style = brush || selected
                ? DecorationEditorTheme.RowSelected
                : DecorationEditorTheme.Row;
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(30f),
                GUILayout.ExpandWidth(true));
            if (GUI.Button(row, GUIContent.none, style))
                SetPaintPaletteColor(color);
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

        private void SetPaintPaletteColor(int color)
        {
            color = Mathf.Clamp(color, 0, 31);
            _paintColor = color;
            _colorText = color.ToString(CultureInfo.InvariantCulture);
            if (_selected != null && !_selected.IsDeleted)
                SetSelectedColor(color);
            else
                InfoStore.Add("Paint brush set to color #" + color.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private void DrawPaintColorGrid(
            float availableWidth,
            int basicColumns,
            Func<int, bool> isActive,
            Func<int, string> tooltip,
            Action<int> select)
        {
            float minimumButtonWidth = EsuHudLayout.Scale(36f);
            float buttonHeight = EsuHudLayout.Scale(24f);
            bool responsive = EsuHudPreferences.ResponsivePaintPalettes;
            float gap = responsive ? EsuHudLayout.Scale(3f) : 0f;
            int columns = ResolvePaintPaletteColumns(
                availableWidth,
                minimumButtonWidth,
                gap,
                responsive,
                basicColumns);
            float cellWidth = responsive
                ? Mathf.Max(
                    1f,
                    (Mathf.Max(1f, availableWidth) - gap * (columns - 1)) /
                    columns)
                : minimumButtonWidth;
            int rows = Mathf.CeilToInt(32f / columns);
            for (int row = 0; row < rows; row++)
            {
                if (responsive)
                    GUILayout.BeginHorizontal(GUILayout.Width(Mathf.Max(1f, availableWidth)));
                else
                    GUILayout.BeginHorizontal();

                int first = row * columns;
                int last = Mathf.Min(32, first + columns);
                for (int color = first; color < last; color++)
                {
                    if (color > first && gap > 0f)
                        GUILayout.Space(gap);
                    if (DrawPaintColorButton(
                            color,
                            isActive?.Invoke(color) == true,
                            tooltip?.Invoke(color) ?? string.Empty,
                            cellWidth,
                            buttonHeight))
                    {
                        select?.Invoke(color);
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        internal static int ResolvePaintPaletteColumns(
            float availableWidth,
            float minimumButtonWidth,
            float gap,
            bool responsive,
            int basicColumns)
        {
            int fallback = Mathf.Clamp(basicColumns, 1, 32);
            if (!responsive)
                return fallback;
            if (!DecorationEditMath.IsFinite(availableWidth) || availableWidth <= 0f)
                return 1;

            float minimum = DecorationEditMath.IsFinite(minimumButtonWidth)
                ? Mathf.Max(1f, minimumButtonWidth)
                : 1f;
            float spacing = DecorationEditMath.IsFinite(gap)
                ? Mathf.Max(0f, gap)
                : 0f;
            int fit = Mathf.FloorToInt(
                (availableWidth + spacing) /
                Mathf.Max(1f, minimum + spacing));

            // A maximum of sixteen columns guarantees that all thirty-two
            // vanilla paint colors retain at least two visible rows.
            return Mathf.Clamp(fit, 1, 16);
        }

        private bool DrawPaintColorButton(
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

        private void DrawPaintSwatchLayout(
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

        private void DrawPaintColorNumber(Rect rect, int color, bool active)
        {
            Color preview = PaintDisplayColor(color);
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

        private void DrawPaintSwatch(Rect rect, int color)
        {
            Color oldColor = GUI.color;
            try
            {
                Color paintColor = PaintPreviewColor(color);
                GUI.color = PaintDisplayColor(paintColor);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                if (!PaintColorApplies(paintColor))
                {
                    float markerHeight = Mathf.Max(1f, EsuHudLayout.Scale(2f));
                    GUI.color = new Color(
                        DecorationEditorTheme.Cyan.r,
                        DecorationEditorTheme.Cyan.g,
                        DecorationEditorTheme.Cyan.b,
                        0.82f);
                    GUI.DrawTexture(
                        new Rect(
                            rect.x + EsuHudLayout.Scale(4f),
                            rect.center.y - markerHeight * 0.5f,
                            Mathf.Max(1f, rect.width - EsuHudLayout.Scale(8f)),
                            markerHeight),
                        Texture2D.whiteTexture);
                }

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

        private Color PaintPreviewColor(int color)
        {
            return NativeDecorationPreviewColor(
                PaintPreviewConstruct(),
                null,
                Mathf.Clamp(color, 0, 31));
        }

        private static Color NativeDecorationPreviewColor(
            AllConstruct construct,
            Vector3i? anchor,
            int colorIndex)
        {
            colorIndex = Mathf.Clamp(colorIndex, 0, 31);
            Color raw = FallbackPaintPreviewColor(colorIndex);
            int camouflageFlag = 0;
            try
            {
                IColorsRestricted colors = construct?.Main?.ColorsRestricted;
                if (colors != null)
                {
                    raw = colors.GetColor(colorIndex);
                    camouflageFlag = colors.GetCamoFlagFor(colorIndex);
                }
            }
            catch
            {
            }

            float healthFraction = 1f;
            if (anchor.HasValue && construct != null)
            {
                try
                {
                    Block anchorBlock = construct.AllBasics?.GetBlockViaLocalPosition(anchor.Value);
                    if (anchorBlock != null && !anchorBlock.IsDeleted && anchorBlock.IsAlive)
                        healthFraction = Mathf.Clamp01(anchorBlock.GetHealthFraction());
                }
                catch
                {
                }
            }

            int redFlag = BrilliantSkies.Core.Help.Colors.DecodeFlag(raw.r);
            raw = ApplyNativeDecorationDamageTint(raw, healthFraction);
            float damageFlag = BrilliantSkies.Core.Help.Interp.TwoPointsClamped(
                1f,
                0.6f,
                0f,
                1f,
                healthFraction);
            Color native = BrilliantSkies.Core.Help.Colors.CleanUpColorBeforePassingOut(
                raw,
                damageFlag,
                0,
                redFlag,
                camouflageFlag);
            native.r = ClampVisiblePaintChannel(native.r);
            native.g = ClampVisiblePaintChannel(native.g);
            native.b = ClampVisiblePaintChannel(native.b);
            native.a = ClampVisiblePaintChannel(native.a);
            return native;
        }

        internal static Color ApplyNativeDecorationDamageTint(
            Color raw,
            float healthFraction)
        {
            healthFraction = Mathf.Clamp01(healthFraction);
            if (healthFraction >= 1f)
                return raw;

            // Decoration.GetColor saves the red shader flag first, then applies
            // this transform before CleanUpColorBeforePassingOut. Keeping it here
            // makes Surface previews match placed decorations on damaged tethers.
            raw.r *= healthFraction;
            raw.g *= healthFraction;
            raw.b *= healthFraction;
            raw.a = Mathf.Min(
                raw.a + 1f - Mathf.Max(healthFraction, 0.2f),
                0.99f);
            return raw;
        }

        private AllConstruct PaintPreviewConstruct()
        {
            if (_surfaceDraft.Construct != null)
                return _surfaceDraft.Construct;
            if (_generatorDraft.Construct != null)
                return _generatorDraft.Construct;
            if (_placementConstruct != null)
                return _placementConstruct;
            if (_selectedConstruct != null)
                return _selectedConstruct;
            return FocusedConstruct();
        }

        private Color PaintDisplayColor(int color) =>
            PaintDisplayColor(PaintPreviewColor(color));

        private static Color PaintDisplayColor(Color paintColor)
        {
            Color baseColor = new Color(0.22f, 0.3f, 0.33f, 1f);
            float alpha = ClampVisiblePaintChannel(paintColor.a);
            if (alpha <= 0.1f)
                return baseColor;

            Color visible = new Color(paintColor.r, paintColor.g, paintColor.b, 1f);
            float displayAlpha = Mathf.Lerp(
                0.45f,
                1f,
                Mathf.InverseLerp(0.1f, 1f, alpha));
            Color display = Color.Lerp(baseColor, visible, displayAlpha);
            display.a = 1f;
            return display;
        }

        private static bool PaintColorApplies(Color paintColor) =>
            ClampVisiblePaintChannel(paintColor.a) > 0.1f;

        private static float ClampVisiblePaintChannel(float channel) =>
            float.IsNaN(channel) || float.IsInfinity(channel)
                ? 0f
                : Mathf.Clamp01(channel);

        private static Color FallbackPaintPreviewColor(int color)
        {
            switch (Mathf.Clamp(color, 0, 31))
            {
                case 0: return new Color(0.01f, 0f, 0f, 0f);
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
            EsuHudChrome.DrawPanel(rect);
            float inset = EsuHudLayout.Scale(8f);
            GUI.BeginGroup(rect);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            float actionHeight = Mathf.Min(EsuHudLayout.Scale(42f), Mathf.Max(0f, inner.height * 0.18f));
            float gap = EsuHudLayout.Scale(6f);
            float settingsHeight = SurfaceSettingsShelfHeight(inner.height, actionHeight, gap);
            bool compactWorkspace = SurfacePanelUsesCompactWorkspace(inner.height);
            Rect actionRect = new Rect(inner.x, inner.yMax - actionHeight, inner.width, actionHeight);
            Rect settingsRect = new Rect(
                inner.x,
                actionRect.y - gap - settingsHeight,
                inner.width,
                settingsHeight);
            Rect workspaceRect = new Rect(
                inner.x,
                inner.y,
                inner.width,
                Mathf.Max(1f, settingsRect.y - inner.y - gap));

            if (!SurfaceWorkspaceUsesSimultaneousPanels(
                    inner.height,
                    _showSurfaceCoordinates))
            {
                ClearStackDividerDrag(StackDividerKind.SurfaceCoordinates);
                DrawCompactSurfaceWorkspace(workspaceRect);
            }
            else
            {
                DrawExpandedSurfaceWorkspace(workspaceRect, gap);
            }

            DrawSurfaceSettingsShelf(settingsRect, compactWorkspace);
            DrawSurfaceActionBar(actionRect);
            GUI.EndGroup();
        }

        internal static bool SurfacePanelUsesCompactWorkspace(float innerHeight) =>
            !DecorationEditMath.IsFinite(innerHeight) ||
            innerHeight < 520f;

        internal static bool SurfaceWorkspaceUsesSimultaneousPanels(
            float innerHeight,
            bool coordinatesOpen) =>
            coordinatesOpen || !SurfacePanelUsesCompactWorkspace(innerHeight);

        private void DrawCompactSurfaceWorkspace(Rect workspaceRect)
        {
            if (workspaceRect.width <= 1f || workspaceRect.height <= 1f)
                return;

            float gap = EsuHudLayout.Scale(4f);
            float headerHeight = Mathf.Min(
                EsuHudLayout.Scale(26f),
                workspaceRect.height);
            Rect headerRect = new Rect(
                workspaceRect.x,
                workspaceRect.y,
                workspaceRect.width,
                headerHeight);
            float availableWidth = Mathf.Max(1f, headerRect.width - gap * 2f);
            float tabWidth = Mathf.Max(1f, availableWidth * 0.28f);
            float titleWidth = Mathf.Max(1f, availableWidth - tabWidth * 2f);
            Rect titleRect = new Rect(
                headerRect.x,
                headerRect.y,
                titleWidth,
                headerRect.height);
            Rect draftTab = new Rect(
                titleRect.xMax + gap,
                headerRect.y,
                tabWidth,
                headerRect.height);
            Rect coordinateTab = new Rect(
                draftTab.xMax + gap,
                headerRect.y,
                tabWidth,
                headerRect.height);
            DrawCompactIconHeader(titleRect, "Surface", "build");
            if (GUI.Button(
                    draftTab,
                    new GUIContent("Draft", "Show the Surface draft workspace."),
                    DecorationEditorTheme.ToolButton(!_showSurfaceCoordinates)))
            {
                SelectCompactSurfaceWorkspace(showCoordinates: false);
            }
            if (GUI.Button(
                    coordinateTab,
                    new GUIContent("Coordinates", "Show the selected draft coordinates."),
                    DecorationEditorTheme.ToolButton(_showSurfaceCoordinates)))
            {
                SelectCompactSurfaceWorkspace(showCoordinates: true);
            }

            float bodyY = headerRect.yMax + gap;
            Rect bodyRect = new Rect(
                workspaceRect.x,
                bodyY,
                workspaceRect.width,
                Mathf.Max(1f, workspaceRect.yMax - bodyY));
            if (_showSurfaceCoordinates)
                DrawSurfaceCoordinateEditorShelf(bodyRect);
            else
                DrawSurfaceDraftWorkspace(bodyRect, scrollEntireWorkspace: true);
        }

        private void SelectCompactSurfaceWorkspace(bool showCoordinates)
        {
            if (_showSurfaceCoordinates == showCoordinates)
                return;

            if (showCoordinates)
            {
                _showSurfaceCoordinates = true;
                SyncSurfaceCoordinateText(force: true);
                return;
            }

            FinalizeSurfaceCoordinateLiveInteraction();
            CloseSurfaceCoordinateStepEditor();
            _showSurfaceCoordinates = false;
            ClearSurfaceCoordinateHover();
            ClearStackDividerDrag(StackDividerKind.SurfaceCoordinates);
        }

        private void DrawExpandedSurfaceWorkspace(Rect workspaceRect, float gap)
        {
            Rect contentRect;
            Rect coordinateDividerRect;
            Rect coordinateRect;
            if (_showSurfaceCoordinates)
            {
                float coordinateRatio = _surfaceCoordinateSplitCustomized
                    ? _surfaceCoordinateBottomRatio
                    : SurfaceCoordinateAutoBottomRatio(workspaceRect, gap);
                SplitSurfaceCoordinateWorkspace(
                    workspaceRect,
                    coordinateRatio,
                    gap,
                    SurfaceDraftMinimumWorkspaceHeight(),
                    EsuHudLayout.Scale(58f),
                    out contentRect,
                    out coordinateDividerRect,
                    out coordinateRect,
                    out coordinateRatio);
                HandleSurfaceCoordinateDividerDrag(
                    workspaceRect,
                    coordinateDividerRect,
                    gap,
                    ref coordinateRatio);
                SplitSurfaceCoordinateWorkspace(
                    workspaceRect,
                    coordinateRatio,
                    gap,
                    SurfaceDraftMinimumWorkspaceHeight(),
                    EsuHudLayout.Scale(58f),
                    out contentRect,
                    out coordinateDividerRect,
                    out coordinateRect,
                    out coordinateRatio);
                if (_surfaceCoordinateSplitCustomized)
                    _surfaceCoordinateBottomRatio = coordinateRatio;
            }
            else
            {
                ClearStackDividerDrag(StackDividerKind.SurfaceCoordinates);
                float collapsedCoordinateHeight = Mathf.Min(
                    SurfaceCoordinateCollapsedShelfHeight(),
                    workspaceRect.height);
                float collapsedGap = Mathf.Min(
                    gap,
                    Mathf.Max(0f, workspaceRect.height - collapsedCoordinateHeight));
                contentRect = new Rect(
                    workspaceRect.x,
                    workspaceRect.y,
                    workspaceRect.width,
                    Mathf.Max(
                        0f,
                        workspaceRect.height - collapsedCoordinateHeight - collapsedGap));
                coordinateDividerRect = Rect.zero;
                coordinateRect = new Rect(
                    workspaceRect.x,
                    contentRect.yMax + collapsedGap,
                    workspaceRect.width,
                    collapsedCoordinateHeight);
            }

            DrawSurfaceDraftWorkspace(contentRect, scrollEntireWorkspace: false);
            if (_showSurfaceCoordinates)
            {
                DrawStackDividerGrip(
                    coordinateDividerRect,
                    StackDividerKind.SurfaceCoordinates);
            }
            DrawSurfaceCoordinateEditorShelf(coordinateRect);
        }

        private void DrawSurfaceDraftWorkspace(
            Rect contentRect,
            bool scrollEntireWorkspace)
        {
            if (contentRect.width <= 1f || contentRect.height <= 1f)
                return;

            GUILayout.BeginArea(contentRect);
            if (scrollEntireWorkspace)
            {
                _surfaceCompactWorkspaceScroll = GUILayout.BeginScrollView(
                    _surfaceCompactWorkspaceScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(contentRect.width),
                    GUILayout.Height(contentRect.height));
            }
            else
            {
                DrawCompactIconHeader("Surface Builder", "build");
            }
            GUILayout.Label("Click three craft-surface points to seed a triangle. Select an edge, then click a new point to extend.", DecorationEditorTheme.MiniWrap);

            DecorationEditorTheme.Separator();
            GUILayout.Label(SurfaceSummary(), DecorationEditorTheme.BodyWrap);
            if (!string.IsNullOrEmpty(_surfaceMessage))
                GUILayout.Label(_surfaceMessage, SurfaceMessageStyle());
            if (_surfacePlan != null && _surfacePlan.Warnings.Count > 0)
                GUILayout.Label(_surfacePlan.Warnings[0], DecorationEditorTheme.Warning);

            GUILayout.BeginHorizontal();
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
                if (scrollEntireWorkspace)
                {
                    DrawUnifiedSurfaceDraftRows();
                }
                else
                {
                    _surfaceScroll = _showSurfaceCoordinates
                        ? GUILayout.BeginScrollView(
                            _surfaceScroll,
                            alwaysShowHorizontal: false,
                            alwaysShowVertical: true,
                            GUILayout.Height(SurfaceDraftListHeight(contentRect.height)))
                        : GUILayout.BeginScrollView(
                            _surfaceScroll,
                            alwaysShowHorizontal: false,
                            alwaysShowVertical: true,
                            GUILayout.ExpandHeight(true));
                    DrawUnifiedSurfaceDraftRows();
                    GUILayout.EndScrollView();
                }
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
            if (scrollEntireWorkspace)
                GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSurfaceSettingsShelf(Rect settingsRect, bool scrollable)
        {
            if (settingsRect.width <= 1f || settingsRect.height <= 1f)
                return;

            GUILayout.BeginArea(settingsRect);
            if (scrollable)
            {
                _surfaceSettingsScroll = GUILayout.BeginScrollView(
                    _surfaceSettingsScroll,
                    alwaysShowHorizontal: false,
                    alwaysShowVertical: true,
                    GUILayout.Width(settingsRect.width),
                    GUILayout.Height(settingsRect.height));
            }
            DecorationEditorTheme.Separator();
            DrawSurfaceSettings();
            if (scrollable)
                GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private float SurfaceCoordinateShelfHeight(float availableAboveSettings)
        {
            float minimumDraftHeight = SurfaceDraftMinimumWorkspaceHeight();
            float minimumShelfHeight = EsuHudLayout.Scale(58f);
            float shelfGap = EsuHudLayout.Scale(6f);
            return AllocateSurfaceCoordinateShelfHeight(
                availableAboveSettings,
                SurfaceDraftWorkspaceDesiredHeight(),
                minimumDraftHeight,
                minimumShelfHeight,
                shelfGap);
        }

        private float SurfaceCoordinateAutoBottomRatio(Rect workspaceRect, float gap)
        {
            float availableHeight = StackAvailableHeight(workspaceRect, gap);
            if (availableHeight <= 0f)
                return 0.5f;

            float desired = SurfaceCoordinateShelfHeight(workspaceRect.height);
            return ClampSurfaceCoordinateBottomHeight(
                       desired,
                       availableHeight,
                       SurfaceDraftMinimumWorkspaceHeight(),
                       EsuHudLayout.Scale(58f)) /
                   availableHeight;
        }

        internal static void SplitSurfaceCoordinateWorkspace(
            Rect workspaceRect,
            float coordinateBottomRatio,
            float gap,
            float minimumDraftHeight,
            float minimumCoordinateHeight,
            out Rect draftRect,
            out Rect dividerRect,
            out Rect coordinateRect,
            out float resolvedBottomRatio)
        {
            float safeX = DecorationEditMath.IsFinite(workspaceRect.x)
                ? workspaceRect.x
                : 0f;
            float safeY = DecorationEditMath.IsFinite(workspaceRect.y)
                ? workspaceRect.y
                : 0f;
            float safeWidth = DecorationEditMath.IsFinite(workspaceRect.width)
                ? Math.Max(0f, workspaceRect.width)
                : 0f;
            float safeHeight = DecorationEditMath.IsFinite(workspaceRect.height)
                ? Math.Max(0f, workspaceRect.height)
                : 0f;
            float safeGap = DecorationEditMath.IsFinite(gap)
                ? Math.Min(Math.Max(gap, 0f), safeHeight)
                : 0f;
            Rect safeWorkspace = new Rect(safeX, safeY, safeWidth, safeHeight);
            float availableHeight = Math.Max(0f, safeWorkspace.height - Math.Max(0f, safeGap));
            float coordinateHeight = ClampSurfaceCoordinateBottomHeight(
                availableHeight * ValidStackRatio(coordinateBottomRatio),
                availableHeight,
                minimumDraftHeight,
                minimumCoordinateHeight);
            float draftHeight = Math.Max(0f, availableHeight - coordinateHeight);
            resolvedBottomRatio = availableHeight > 0f
                ? coordinateHeight / availableHeight
                : 0.5f;
            draftRect = new Rect(
                safeWorkspace.x,
                safeWorkspace.y,
                safeWorkspace.width,
                draftHeight);
            dividerRect = new Rect(
                safeWorkspace.x,
                draftRect.yMax,
                safeWorkspace.width,
                safeGap);
            coordinateRect = new Rect(
                safeWorkspace.x,
                dividerRect.yMax,
                safeWorkspace.width,
                coordinateHeight);
        }

        private static float ClampSurfaceCoordinateBottomHeight(
            float coordinateHeight,
            float availableHeight,
            float minimumDraftHeight,
            float minimumCoordinateHeight)
        {
            if (!DecorationEditMath.IsFinite(availableHeight) || availableHeight <= 0f)
                return 0f;
            if (availableHeight <= 2f)
                return availableHeight * 0.5f;

            float requestedMinimumCoordinates = DecorationEditMath.IsFinite(minimumCoordinateHeight)
                ? Math.Max(1f, minimumCoordinateHeight)
                : 58f;
            float minimumCoordinates = Math.Min(
                requestedMinimumCoordinates,
                availableHeight - 1f);
            float requestedMinimumDraft = DecorationEditMath.IsFinite(minimumDraftHeight)
                ? Math.Max(1f, minimumDraftHeight)
                : 188f;
            float minimumDraft = Math.Min(
                requestedMinimumDraft,
                Math.Max(0f, availableHeight - minimumCoordinates));
            float maximumCoordinates = Math.Max(
                minimumCoordinates,
                availableHeight - minimumDraft);
            float requested = DecorationEditMath.IsFinite(coordinateHeight)
                ? coordinateHeight
                : availableHeight * 0.5f;
            return Math.Min(
                Math.Max(requested, minimumCoordinates),
                maximumCoordinates);
        }

        private void HandleSurfaceCoordinateDividerDrag(
            Rect workspaceRect,
            Rect dividerRect,
            float gap,
            ref float coordinateBottomRatio)
        {
            Event current = Event.current;
            if (current == null || dividerRect.height <= 0f)
                return;

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dividerRect.Contains(current.mousePosition))
            {
                _draggingStackDivider = StackDividerKind.SurfaceCoordinates;
                _stackDividerDragStartMouseY = current.mousePosition.y;
                _stackDividerDragStartBottomRatio = coordinateBottomRatio;
                _surfaceCoordinateSplitCustomized = true;
                _surfaceCoordinateBottomRatio = coordinateBottomRatio;
                current.Use();
                return;
            }

            if (_draggingStackDivider != StackDividerKind.SurfaceCoordinates)
                return;

            if (current.type == EventType.MouseDrag)
            {
                float availableHeight = StackAvailableHeight(workspaceRect, gap);
                float startBottomHeight = availableHeight *
                                          _stackDividerDragStartBottomRatio;
                float deltaY = current.mousePosition.y -
                               _stackDividerDragStartMouseY;
                float bottomHeight = ClampSurfaceCoordinateBottomHeight(
                    startBottomHeight - deltaY,
                    availableHeight,
                    SurfaceDraftMinimumWorkspaceHeight(),
                    EsuHudLayout.Scale(58f));
                coordinateBottomRatio = availableHeight > 0f
                    ? bottomHeight / availableHeight
                    : 0.5f;
                _surfaceCoordinateBottomRatio = coordinateBottomRatio;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
            {
                _draggingStackDivider = StackDividerKind.None;
                current.Use();
            }
        }

        internal static float AllocateSurfaceCoordinateShelfHeight(
            float availableAboveSettings,
            float desiredDraftHeight,
            float minimumDraftHeight,
            float minimumShelfHeight,
            float shelfGap)
        {
            if (!DecorationEditMath.IsFinite(availableAboveSettings) ||
                !DecorationEditMath.IsFinite(desiredDraftHeight) ||
                !DecorationEditMath.IsFinite(minimumDraftHeight) ||
                !DecorationEditMath.IsFinite(minimumShelfHeight) ||
                !DecorationEditMath.IsFinite(shelfGap))
            {
                return 1f;
            }

            float available = Mathf.Max(1f, availableAboveSettings);
            float gap = Mathf.Max(0f, shelfGap);
            float usable = Mathf.Max(1f, available - gap);
            float minimumDraft = Mathf.Max(1f, minimumDraftHeight);
            float minimumShelf = Mathf.Max(1f, minimumShelfHeight);
            if (usable < minimumDraft + minimumShelf)
                return Mathf.Max(1f, usable * 0.42f);

            float maximumDraft = usable - minimumShelf;
            float draftHeight = Mathf.Clamp(
                desiredDraftHeight,
                minimumDraft,
                maximumDraft);
            return Mathf.Max(minimumShelf, usable - draftHeight);
        }

        private float SurfaceDraftWorkspaceDesiredHeight()
        {
            float listHeight = _showSurfaceDraftList
                ? Mathf.Min(
                    SurfaceDraftListDesiredHeight(),
                    EsuHudLayout.Scale(184f))
                : EsuHudLayout.Scale(24f);
            return SurfaceDraftWorkspaceChromeHeight() + listHeight;
        }

        private float SurfaceDraftMinimumWorkspaceHeight() =>
            SurfaceDraftWorkspaceChromeHeight() +
            (_showSurfaceDraftList
                ? EsuHudLayout.Scale(38f)
                : EsuHudLayout.Scale(24f));

        private float SurfaceDraftWorkspaceChromeHeight()
        {
            float chromeHeight = EsuHudLayout.Scale(150f);
            if (!string.IsNullOrEmpty(_surfaceMessage))
                chromeHeight += EsuHudLayout.Scale(20f);
            if (_surfacePlan != null && _surfacePlan.Warnings.Count > 0)
                chromeHeight += EsuHudLayout.Scale(20f);
            return chromeHeight;
        }

        private static float SurfaceCoordinateCollapsedShelfHeight() =>
            EsuHudLayout.Scale(38f);

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
            return AllocateSurfaceDraftListHeight(
                contentHeight,
                SurfaceDraftWorkspaceChromeHeight(),
                EsuHudLayout.Scale(38f));
        }

        internal static float AllocateSurfaceDraftListHeight(
            float contentHeight,
            float reservedChromeHeight,
            float minimumHeight)
        {
            if (!DecorationEditMath.IsFinite(contentHeight) || contentHeight <= 0f)
                return 0f;

            float reserved = DecorationEditMath.IsFinite(reservedChromeHeight)
                ? Mathf.Max(0f, reservedChromeHeight)
                : 0f;
            float minimum = DecorationEditMath.IsFinite(minimumHeight)
                ? Mathf.Max(1f, minimumHeight)
                : 1f;
            return Mathf.Min(
                contentHeight,
                Mathf.Max(minimum, contentHeight - reserved));
        }

        private float SurfaceDraftListDesiredHeight()
        {
            int rowCount = UnifiedSurfaceDraftRowCount();
            float rowHeight = EsuHudLayout.Scale(24f);
            float chrome = EsuHudLayout.Scale(8f);
            return Mathf.Max(EsuHudLayout.Scale(38f), rowCount * rowHeight + chrome);
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
                            new GUIContent(GeneratorPointLabel(index), "Select this generated path, tube, or shape point."),
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
            ActivateGeneratorDraftSelectionTool();
            _generatorDraft.SelectPoint(index);
            _generatorMessage = _generatorDraft.UsesCenterTool
                ? "Generator shape center selected."
                : GeneratorToolDisplayName(_generatorDraft.Tool) + " point selected.";
        }

        private void ActivateGeneratorDraftSelectionTool()
        {
            SurfaceExtraTool draftTool = ResolveGeneratorSelectionExtraTool(
                _generatorDraft.Tool);
            _surfaceExtraTool = draftTool;
            s_surfaceExtraTool = draftTool;
            _surfaceBuilderTool = DecorationGeneratorDraft.UsesCenter(draftTool)
                ? SurfaceBuilderTool.Circle
                : SurfaceBuilderTool.Path;
            s_surfaceBuilderTool = _surfaceBuilderTool;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            CloseSurfacePointContextMenu();
            CloseDecorationContextMenu();
        }

        internal static SurfaceExtraTool ResolveGeneratorSelectionExtraTool(
            SurfaceExtraTool draftTool) =>
            draftTool == SurfaceExtraTool.None
                ? SurfaceExtraTool.Path
                : draftTool;

        private string GeneratorPointLabel(int index)
        {
            Vector3 point = _generatorDraft.PointAt(index);
            string prefix = _generatorDraft.UsesCenterTool
                ? GeneratorToolDisplayName(_generatorDraft.Tool) + " center"
                : GeneratorToolDisplayName(_generatorDraft.Tool) + " P" +
                  (index + 1).ToString(CultureInfo.InvariantCulture);
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
            EsuHudChrome.DrawPanel(rect);
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
                DrawInspector(
                    contentRect.height,
                    Mathf.Max(1f, contentRect.width - EsuHudLayout.Scale(22f)));
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

        private void DrawBottomPanel(float width)
        {
            float headerHeight = EsuHudLayout.Scale(24f);
            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            float statusHeight = EsuHudLayout.Scale(24f);
            float snapHeight = EsuHudLayout.Scale(26f);
            float transformHeight = EsuHudLayout.Scale(30f);
            float y = 0f;
            DrawBottomHeader(new Rect(0f, y, width, headerHeight));
            y += headerHeight;
            DrawCyanLine(new Rect(0f, y, width, separatorHeight));
            y += separatorHeight + EsuHudLayout.Scale(3f);

            GUI.Label(new Rect(0f, y, width, statusHeight), StatusLine(), DecorationEditorTheme.Status);
            y += statusHeight + EsuHudLayout.Scale(3f);

            DrawBottomSnapControls(new Rect(0f, y, width, snapHeight));
            y += snapHeight + EsuHudLayout.Scale(3f);

            if (IsSurfaceMode)
                DrawBottomSurfaceCoordinateSummary(new Rect(0f, y, width, transformHeight));
            else
                DrawBottomTransformEditors(new Rect(0f, y, width, transformHeight));
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
            EsuTransformSnapSettings.SaveProfileBestEffort();
            InfoStore.Add(
                "Decoration snap set: move " + _snapMoveText +
                "m, rotate " + _snapRotateText +
                " degrees, scale " + _snapScaleText + ".");
        }

        private void DrawBottomHeader(Rect rect)
        {
            bool surface = IsSurfaceMode;
            BottomHeaderLayout slots = BottomHeaderRects(rect);

            DrawBottomGizmoSettingsButton(slots.GizmoSettings);
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
            EsuHudChrome.DrawPanel(rect);
            GUI.BeginGroup(rect);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(inset, inset, Mathf.Max(1f, rect.width - inset * 2f), Mathf.Max(1f, rect.height - inset * 2f));
            // The summary and validation message can wrap to several lines at
            // narrow widths. Keep the complete tool surface in one viewport so
            // no fixed header allocation can clip it, and so every spare pixel
            // remains usable by the controls below it.
            AllocateSurfaceExtraToolsRects(
                inner,
                desiredPinnedHeight: 0f,
                gap: 0f,
                out Rect _,
                out Rect scrollRect);

            _surfaceExtraToolsViewportHeight = Mathf.Max(1f, scrollRect.height);
            GUILayout.BeginArea(scrollRect);
            _generatorScroll = GUILayout.BeginScrollView(
                _generatorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Width(scrollRect.width),
                GUILayout.Height(scrollRect.height));

            DrawCompactIconHeader("Extra Tools", "settings");
            DrawGeneratorToolButtons();
            GUILayout.Label(GeneratorSummary(), DecorationEditorTheme.BodyWrap);
            GUILayout.Label(_generatorMessage, GeneratorMessageStyle());
            DecorationEditorTheme.Separator();

            DrawGeneratorMeshEditor();
            DrawGeneratorLengthAxisControl();

            DecorationEditorTheme.Separator();
            GUILayout.Label("Shape", DecorationEditorTheme.SubHeader);
            DrawGeneratorNumberField("Strut dia", ref _generatorDiameterText, ApplyGeneratorDiameterText, EsuHudLayout.Scale(58f));
            DrawGeneratorDiameterPresets();
            if (_surfaceExtraTool == SurfaceExtraTool.Quad)
            {
                DrawGeneratorNumberField("Width", ref _generatorQuadWidthText, ApplyGeneratorQuadWidthText, EsuHudLayout.Scale(58f));
                DrawGeneratorNumberField("Height", ref _generatorQuadHeightText, ApplyGeneratorQuadHeightText, EsuHudLayout.Scale(58f));
            }
            else if (_surfaceExtraTool == SurfaceExtraTool.Polygon)
            {
                DrawGeneratorNumberField("Radius", ref _generatorRadiusText, ApplyGeneratorRadiusText, EsuHudLayout.Scale(58f));
                DrawGeneratorNumberField("Sides", ref _generatorPolygonSidesText, ApplyGeneratorPolygonSidesText, EsuHudLayout.Scale(42f));
            }
            else if (_surfaceExtraTool == SurfaceExtraTool.Tube)
            {
                DrawGeneratorNumberField("Tube dia", ref _generatorTubeDiameterText, ApplyGeneratorTubeDiameterText, EsuHudLayout.Scale(58f));
                DrawGeneratorNumberField("Sides", ref _generatorTubeSegmentsText, ApplyGeneratorTubeSegmentsText, EsuHudLayout.Scale(42f));
            }
            else if (DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool))
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
            DrawGeneratorColorEditor(
                Mathf.Max(1f, scrollRect.width - EsuHudLayout.Scale(22f)));
            DrawGeneratorMaterialEditor();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.EndGroup();
        }

        internal static void AllocateSurfaceExtraToolsRects(
            Rect inner,
            float desiredPinnedHeight,
            float gap,
            out Rect pinnedRect,
            out Rect scrollRect)
        {
            float safeWidth = DecorationEditMath.IsFinite(inner.width)
                ? Mathf.Max(0f, inner.width)
                : 0f;
            float safeHeight = DecorationEditMath.IsFinite(inner.height)
                ? Mathf.Max(0f, inner.height)
                : 0f;
            float safeGap = DecorationEditMath.IsFinite(gap)
                ? Mathf.Clamp(gap, 0f, safeHeight)
                : 0f;
            float maximumPinned = Mathf.Max(0f, safeHeight - safeGap - 1f);
            float pinnedHeight = DecorationEditMath.IsFinite(desiredPinnedHeight)
                ? Mathf.Clamp(desiredPinnedHeight, 0f, maximumPinned)
                : 0f;
            pinnedRect = new Rect(inner.x, inner.y, safeWidth, pinnedHeight);
            float scrollY = pinnedRect.yMax + safeGap;
            scrollRect = new Rect(
                inner.x,
                scrollY,
                safeWidth,
                Mathf.Max(0f, inner.y + safeHeight - scrollY));
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

        private void DrawDecorationClipboardRows()
        {
            bool settingsEnabled = CanUseDecorationClipboard(
                wholeSelection: false,
                requireSelection: true,
                out string _);
            bool wholeCopyEnabled = CanUseDecorationClipboard(
                wholeSelection: true,
                requireSelection: true,
                out string _);
            bool wholePasteEnabled = DecorationSelectionClipboard.HasValue &&
                                     CanUseDecorationClipboard(
                                         wholeSelection: true,
                                         requireSelection: false,
                                         out string _);
            DrawDecorationClipboardRow(
                "Settings",
                "Copy",
                "Paste",
                CopyDecorationSettings,
                PasteDecorationSettings,
                "Copy this decoration's vanilla-compatible settings.",
                "Paste the native decoration settings onto every explicitly selected decoration.",
                settingsEnabled,
                settingsEnabled);
            DrawDecorationClipboardRow(
                "Decorations",
                "Copy selection",
                "Paste in place",
                CopyDecorationSelection,
                PasteDecorationSelectionInPlace,
                "Copy the explicit selection as an immutable in-memory batch.",
                "Create exact in-place clones on the same live decoration manager.",
                wholeCopyEnabled,
                wholePasteEnabled);
        }

        private void DrawDecorationClipboardRow(
            string label,
            string copyLabel,
            string pasteLabel,
            Action copy,
            Action paste,
            string copyTooltip,
            string pasteTooltip,
            bool copyEnabled,
            bool pasteEnabled)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(82f)));
            bool previous = GUI.enabled;
            GUI.enabled = previous && copyEnabled;
            if (GUILayout.Button(
                    new GUIContent(copyLabel, copyTooltip),
                    copyEnabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                copy();
            GUI.enabled = previous && pasteEnabled;
            if (GUILayout.Button(
                    new GUIContent(pasteLabel, pasteTooltip),
                    pasteEnabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
                paste();
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawGeneratorToolButtons()
        {
            GUILayout.BeginHorizontal();
            DrawSurfaceDrawToolButton();
            DrawGeneratorToolButton(SurfaceExtraTool.Path, "Path", "Draw a path from clicked craft-surface points.");
            DrawGeneratorToolButton(SurfaceExtraTool.Circle, "Circle", "Generate a full segmented circle.");
            DrawGeneratorToolButton(SurfaceExtraTool.PartialCircle, "Arc", "Generate a partial circle using the Arc setting.");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawGeneratorToolButton(SurfaceExtraTool.Cone2D, "2D cone", "Generate a flat cone or sector fan.");
            DrawGeneratorToolButton(SurfaceExtraTool.Sphere, "Sphere", "Generate a wire sphere from rings and meridians.");
            DrawGeneratorToolButton(SurfaceExtraTool.PartialSphere, "Part sph", "Generate a partial wire sphere using the Arc setting.");
            DrawGeneratorToolButton(SurfaceExtraTool.Quad, "Quad", "Generate a rectangular frame with adjustable width and height.");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawGeneratorToolButton(SurfaceExtraTool.Cone, "Cone", "Generate a wire cone from a base ring and apex.");
            DrawGeneratorToolButton(SurfaceExtraTool.Frustum, "Frustum", "Generate a wire frustum with bottom and top radii.");
            DrawGeneratorToolButton(SurfaceExtraTool.Polygon, "Polygon", "Generate a regular closed frame with 3 through 12 sides.");
            DrawGeneratorToolButton(SurfaceExtraTool.Tube, "Tube", "Sweep configurable rings and rails along a clicked path.");
            GUILayout.EndHorizontal();
        }

        private void DrawSurfaceDrawToolButton()
        {
            if (GUILayout.Button(
                    new GUIContent("Draw", "Place and edit triangle surface points."),
                    DecorationEditorTheme.ToolButton(
                        _surfaceBuilderTool == SurfaceBuilderTool.Draw),
                    GUILayout.ExpandWidth(true)))
            {
                SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
            }
        }

        private void DrawGeneratorToolButton(
            SurfaceExtraTool tool,
            string label,
            string tooltip)
        {
            bool active = (_surfaceBuilderTool == SurfaceBuilderTool.Path ||
                           _surfaceBuilderTool == SurfaceBuilderTool.Circle) &&
                          _surfaceExtraTool == tool;
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    DecorationEditorTheme.ToolButton(active),
                    GUILayout.ExpandWidth(true)))
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
                        new GUIContent($"[{entry.Kind}] {entry.Name}", "Use this mesh for generated paths and shapes."),
                        active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                        GUILayout.Height(rowHeight)))
                    SetGeneratorMesh(entry);

                Rect row = GUILayoutUtility.GetLastRect();
                if (Event.current != null && row.Contains(Event.current.mousePosition))
                {
                    _hoveredMesh = entry;
                    _hoveredMeshHint = "Click: use for generated Extra Tools decorations.";
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
                : "Use one shared anchor for every generated Extra Tools decoration.";
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

        private void DrawGeneratorColorEditor(float availableWidth)
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
                DrawPaintColorGrid(
                    availableWidth,
                    8,
                    color => _generatorSettings.ColorIndex == color,
                    color => "Set generated decoration paint color to #" +
                             color.ToString(CultureInfo.InvariantCulture) + ".",
                    SetGeneratorColor);
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
            _generatorScroll = Vector2.zero;
            _generatorMaterialListHeight = 0f;
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
            _generatorQuadWidthText = _generatorSettings.QuadWidth.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorQuadHeightText = _generatorSettings.QuadHeight.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorPolygonSidesText = _generatorSettings.PolygonSides.ToString(CultureInfo.InvariantCulture);
            _generatorTubeDiameterText = _generatorSettings.TubeDiameter.ToString("0.###", CultureInfo.InvariantCulture);
            _generatorTubeSegmentsText = _generatorSettings.TubeSegments.ToString(CultureInfo.InvariantCulture);
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
            string shape = GeneratorShapeSettingsSummary(_surfaceExtraTool);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Extra: {0} | {1} | strut {2:0.###} | {3} | color {4} | anchor {5} | {6} | {7}",
                GeneratorToolDisplayName(_surfaceExtraTool),
                mesh,
                _generatorSettings.Diameter,
                shape,
                _generatorSettings.ColorIndex,
                anchor,
                draft,
                preview);
        }

        private string GeneratorShapeSettingsSummary(SurfaceExtraTool tool)
        {
            switch (tool)
            {
                case SurfaceExtraTool.Quad:
                    return "width " + _generatorSettings.QuadWidth.ToString("0.###", CultureInfo.InvariantCulture) +
                           " height " + _generatorSettings.QuadHeight.ToString("0.###", CultureInfo.InvariantCulture);
                case SurfaceExtraTool.Polygon:
                    return "radius " + _generatorSettings.CircleRadius.ToString("0.###", CultureInfo.InvariantCulture) +
                           " sides " + _generatorSettings.PolygonSides.ToString(CultureInfo.InvariantCulture);
                case SurfaceExtraTool.Tube:
                    return "tube dia " + _generatorSettings.TubeDiameter.ToString("0.###", CultureInfo.InvariantCulture) +
                           " sides " + _generatorSettings.TubeSegments.ToString(CultureInfo.InvariantCulture);
                case SurfaceExtraTool.Path:
                    return "open path";
                default:
                    return "radius " + _generatorSettings.CircleRadius.ToString("0.###", CultureInfo.InvariantCulture) +
                           " segments " + _generatorSettings.CircleSegments.ToString(CultureInfo.InvariantCulture) +
                           " arc " + _generatorSettings.ArcDegrees.ToString("0.###", CultureInfo.InvariantCulture);
            }
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
                case SurfaceExtraTool.Quad:
                    return "Quad";
                case SurfaceExtraTool.Polygon:
                    return "Polygon";
                case SurfaceExtraTool.Tube:
                    return "Tube";
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
            if (!DecorationEditMath.IsFinite(value) || value <= 0f ||
                !DecorationEditMath.IsFinite(value * value))
            {
                InfoStore.Add("Generator strut diameter must be finite, greater than zero, and inside the supported numeric range.");
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

            if (!DecorationEditMath.IsFinite(value) || value <= 0f ||
                !DecorationEditMath.IsFinite(value * value))
            {
                InfoStore.Add("Circle radius must be finite, greater than zero, and inside the supported numeric range.");
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

        private void ApplyGeneratorQuadWidthText()
        {
            if (!TryParsePositiveGeneratorValue(
                    _generatorQuadWidthText,
                    "Quad width",
                    out float value))
            {
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.QuadWidth = value;
            _generatorQuadWidthText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Quad width changed.");
            RecordGeneratorEdit("Set generator quad width", before);
        }

        private void ApplyGeneratorQuadHeightText()
        {
            if (!TryParsePositiveGeneratorValue(
                    _generatorQuadHeightText,
                    "Quad height",
                    out float value))
            {
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.QuadHeight = value;
            _generatorQuadHeightText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Quad height changed.");
            RecordGeneratorEdit("Set generator quad height", before);
        }

        private void ApplyGeneratorPolygonSidesText()
        {
            if (!int.TryParse(
                    _generatorPolygonSidesText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                InfoStore.Add("Polygon sides must be a whole number from 3 through 12.");
                return;
            }

            value = Mathf.Clamp(value, 3, 12);
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.PolygonSides = value;
            _generatorPolygonSidesText = value.ToString(CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Polygon side count changed.");
            RecordGeneratorEdit("Set generator polygon sides", before);
        }

        private void ApplyGeneratorTubeDiameterText()
        {
            if (!TryParsePositiveGeneratorValue(
                    _generatorTubeDiameterText,
                    "Tube diameter",
                    out float value))
            {
                return;
            }

            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.TubeDiameter = value;
            _generatorTubeDiameterText = value.ToString("0.###", CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Tube diameter changed.");
            RecordGeneratorEdit("Set generator tube diameter", before);
        }

        private void ApplyGeneratorTubeSegmentsText()
        {
            if (!int.TryParse(
                    _generatorTubeSegmentsText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                InfoStore.Add("Tube sides must be a whole number from 3 through 64.");
                return;
            }

            value = Mathf.Clamp(value, 3, 64);
            DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
            _generatorSettings.TubeSegments = value;
            _generatorTubeSegmentsText = value.ToString(CultureInfo.InvariantCulture);
            InvalidateGeneratorPlan("Tube side count changed.");
            RecordGeneratorEdit("Set generator tube sides", before);
        }

        private static bool TryParsePositiveGeneratorValue(
            string text,
            string label,
            out float value)
        {
            if (!FlexibleFloatParser.TryParse(text, out value) ||
                !DecorationEditMath.IsFinite(value) ||
                value <= 0f ||
                !DecorationEditMath.IsFinite(value * value))
            {
                InfoStore.Add(label + " must be a finite number greater than zero.");
                value = 0f;
                return false;
            }

            return true;
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

        private BottomHeaderLayout BottomHeaderRects(Rect rect)
        {
            bool surface = IsSurfaceMode;
            float gap = Mathf.Min(EsuHudLayout.Scale(8f), rect.width * 0.008f);
            float gearWidth = Mathf.Min(EsuHudLayout.Scale(28f), rect.width * 0.035f);
            float titleWidth = Mathf.Min(EsuHudLayout.Scale(160f), rect.width * 0.14f);
            float cleanWidth = Mathf.Min(EsuHudLayout.Scale(92f), rect.width * 0.07f);
            float settingsWidth = Mathf.Min(
                EsuHudLayout.Scale(surface ? 88f : 132f),
                rect.width * (surface ? 0.075f : 0.105f));
            float anchorWidth = surface
                ? 0f
                : Mathf.Min(EsuHudLayout.Scale(132f), rect.width * 0.105f);
            float selectWidth = Mathf.Min(
                EsuHudLayout.Scale(surface ? 96f : 476f),
                rect.width * (surface ? 0.09f : 0.35f));
            Rect cleanRect = new Rect(rect.xMax - cleanWidth, rect.y, cleanWidth, rect.height);
            Rect settingsRect = new Rect(cleanRect.x - gap - settingsWidth, rect.y, settingsWidth, rect.height);
            Rect anchorRect = new Rect(settingsRect.x - gap - anchorWidth, rect.y, anchorWidth, rect.height);
            Rect selectRect = new Rect(anchorRect.x - gap - selectWidth, rect.y, selectWidth, rect.height);
            Rect gearRect = new Rect(rect.x, rect.y, gearWidth, rect.height);
            Rect titleRect = new Rect(gearRect.xMax + EsuHudLayout.Scale(4f), rect.y, titleWidth, rect.height);
            Rect modeRect = new Rect(
                titleRect.xMax + gap,
                rect.y,
                Mathf.Max(1f, selectRect.x - titleRect.xMax - gap * 2f),
                rect.height);
            return new BottomHeaderLayout(
                gearRect,
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
            float focusWidth = EsuHudLayout.Scale(82f);
            bool pivotEnabled = HasMultiSelectionForTransform();
            float pivotWidth = EsuHudLayout.Scale(112f);
            float total = pivotWidth + focusWidth + singleWidth + boxWidth + xrayWidth + hideMeshWidth + gap * 5f;
            float compact = total > rect.width && rect.width > 1f
                ? Mathf.Clamp(rect.width / total, 0.35f, 1f)
                : 1f;
            gap *= compact;
            pivotWidth *= compact;
            focusWidth *= compact;
            singleWidth *= compact;
            boxWidth *= compact;
            xrayWidth *= compact;
            hideMeshWidth *= compact;
            total = pivotWidth + focusWidth + singleWidth + boxWidth + xrayWidth + hideMeshWidth + gap * 5f;
            float x = rect.x + Mathf.Max(0f, (rect.width - total) * 0.5f);

            DrawGroupPivotButton(new Rect(x, y, pivotWidth, height), pivotEnabled);
            x += pivotWidth + gap;

            DrawSelectionFocusButton(new Rect(x, y, focusWidth, height));
            x += focusWidth + gap;

            if (GUI.Button(
                    new Rect(x, y, singleWidth, height),
                    new GUIContent("Single", "Click the nearest decoration center. Hold Shift and click to add more decorations to the current selection."),
                    DecorationEditorTheme.ToolButton(_selectionMode == DecorationSelectionMode.Single)))
            {
                SetActiveTool(DecorationEditorTool.Select);
                _selectionMode = DecorationSelectionMode.Single;
                CancelBoxSelection();
            }

            x += singleWidth + gap;
            if (GUI.Button(
                    new Rect(x, y, boxWidth, height),
                    new GUIContent("Box", "Drag a rectangle to select multiple decoration centers; Move, Rotate, and Scale then transform them as one group."),
                    DecorationEditorTheme.ToolButton(_selectionMode == DecorationSelectionMode.Box)))
            {
                if (TryHandleSelectionFocusBulkRequest())
                    return;

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

        private void DrawGroupPivotButton(Rect rect, bool enabled)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            if (GUI.Button(
                    rect,
                    new GUIContent(
                        "Pivot: " + GroupPivotModeLabel(_groupPivotMode),
                        enabled
                            ? "Cycle the origin used by multi-selection move, rotate, and scale."
                            : "Select multiple decorations to choose the group transform pivot."),
                    DecorationEditorTheme.ToolButton(enabled, enabled)))
            {
                CycleGroupPivotMode();
            }
            GUI.enabled = previous;
        }

        private void DrawSelectionFocusButton(Rect rect)
        {
            RefreshSelectionFocusLock();
            bool available = _selected != null && !_selected.IsDeleted && _selectedConstruct != null;
            bool active = HasActiveSelectionFocusLock();
            bool previous = GUI.enabled;
            GUI.enabled = previous && available;
            if (GUI.Button(
                    rect,
                    new GUIContent(
                        "Focus deco",
                        available
                            ? "Keep selection locked to the current decoration."
                            : "Select a decoration before locking focus."),
                    DecorationEditorTheme.ToolButton(active, available)))
            {
                ToggleSelectionFocusLock();
            }
            GUI.enabled = previous;
        }

        private bool HasActiveSelectionFocusLock()
        {
            Decoration decoration;
            AllConstruct construct;
            return TryGetSelectionFocus(out decoration, out construct);
        }

        private bool TryGetSelectionFocus(out Decoration decoration, out AllConstruct construct)
        {
            decoration = null;
            construct = null;
            if (!_selectionFocusLocked)
                return false;

            if (_selectionFocusDecoration == null ||
                _selectionFocusDecoration.IsDeleted ||
                _selectionFocusConstruct == null)
            {
                ClearSelectionFocusLock(notify: false);
                return false;
            }

            decoration = _selectionFocusDecoration;
            construct = _selectionFocusConstruct;
            return true;
        }

        private void RefreshSelectionFocusLock()
        {
            if (!_selectionFocusLocked)
                return;

            Decoration decoration;
            AllConstruct construct;
            if (!TryGetSelectionFocus(out decoration, out construct))
                return;

            if (_selected == null ||
                _selected.IsDeleted ||
                !ReferenceEquals(_selected, decoration) ||
                !ReferenceEquals(_selectedConstruct, construct) ||
                _selection.Count != 1 ||
                !_selection.Contains(decoration))
            {
                KeepSelectionFocused(decoration, construct);
            }
        }

        private void ToggleSelectionFocusLock()
        {
            if (HasActiveSelectionFocusLock())
            {
                ClearSelectionFocusLock(notify: true);
                return;
            }

            EnableSelectionFocusLock();
        }

        private void EnableSelectionFocusLock()
        {
            if (_selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null)
            {
                InfoStore.Add("Select a decoration before locking focus.");
                return;
            }

            _selectionFocusLocked = true;
            _selectionFocusDecoration = _selected;
            _selectionFocusConstruct = _selectedConstruct;
            KeepSelectionFocused(_selected, _selectedConstruct);
            InfoStore.Add("Focus deco enabled. Selection locked to the current decoration.");
        }

        private void ClearSelectionFocusLock(bool notify)
        {
            bool wasLocked = _selectionFocusLocked;
            _selectionFocusLocked = false;
            _selectionFocusDecoration = null;
            _selectionFocusConstruct = null;
            _selectionFocusBlockedNoticeUntil = -1f;
            if (notify && wasLocked)
                InfoStore.Add("Focus deco disabled.");
        }

        private bool TryHandleSelectionFocusRequest(Decoration decoration, AllConstruct construct)
        {
            Decoration focusDecoration;
            AllConstruct focusConstruct;
            if (!TryGetSelectionFocus(out focusDecoration, out focusConstruct))
                return false;

            if (!ReferenceEquals(decoration, focusDecoration) ||
                !ReferenceEquals(construct, focusConstruct))
            {
                NotifySelectionFocusBlocked();
            }

            KeepSelectionFocused(focusDecoration, focusConstruct);
            return true;
        }

        private bool TryBlockSelectionFocusContextTarget(Decoration decoration, AllConstruct construct)
        {
            Decoration focusDecoration;
            AllConstruct focusConstruct;
            if (!TryGetSelectionFocus(out focusDecoration, out focusConstruct))
                return false;

            if (ReferenceEquals(decoration, focusDecoration) &&
                ReferenceEquals(construct, focusConstruct))
            {
                return false;
            }

            NotifySelectionFocusBlocked();
            KeepSelectionFocused(focusDecoration, focusConstruct);
            return true;
        }

        private bool TryHandleSelectionFocusBulkRequest()
        {
            Decoration decoration;
            AllConstruct construct;
            if (!TryGetSelectionFocus(out decoration, out construct))
                return false;

            NotifySelectionFocusBlocked();
            KeepSelectionFocused(decoration, construct);
            return true;
        }

        private void NotifySelectionFocusBlocked()
        {
            float now = Time.unscaledTime;
            if (now < _selectionFocusBlockedNoticeUntil)
                return;

            _selectionFocusBlockedNoticeUntil = now + 0.9f;
            InfoStore.Add("Focus deco is locked. Turn it off to select another decoration.");
        }

        private void KeepSelectionFocused(Decoration decoration, AllConstruct construct)
        {
            if (decoration == null || decoration.IsDeleted || construct == null)
                return;

            SetPrimarySelection(decoration, construct);
            _selection.Clear();
            _selection.Add(decoration);
            _selectionMode = DecorationSelectionMode.Single;
            CancelBoxSelection();
            ResetInspectorFields();
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
                if (_anchorMenuOpen)
                    _gizmoSettingsOpen = false;
            }
        }

        private void DrawBottomGizmoSettingsButton(Rect rect)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && !HasActiveGizmoDrag();
            if (GUI.Button(
                    rect,
                    new GUIContent(
                        string.Empty,
                        DecorationEditorIconCatalog.Get("settings"),
                        "Open move, rotate, scale, thickness, and click-area gizmo settings."),
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
                    _anchorMenuOpen = false;
                    _viewModeMenuOpen = false;
                }
            }
            GUI.enabled = previous;
        }

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
                : new Rect(margin, Screen.height - height - margin, EsuHudLayout.Scale(28f), EsuHudLayout.Scale(24f));
            float x = Mathf.Clamp(button.x, margin, Mathf.Max(margin, Screen.width - width - margin));
            float y = Mathf.Clamp(
                button.y - height - EsuHudLayout.Scale(4f),
                margin,
                Mathf.Max(margin, Screen.height - height - margin));
            return new Rect(x, y, width, height);
        }

        private void CloseGizmoSettingsMenu()
        {
            _gizmoSettingsOpen = false;
            _textInputFocused = false;
            try { GUIUtility.keyboardControl = 0; }
            catch { }
        }

        private void DrawGizmoSettingsMenu()
        {
            if (!_gizmoSettingsOpen)
                return;

            Rect rect = GizmoSettingsMenuRect();
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(rect);
            float padding = EsuHudLayout.Scale(8f);
            float gap = EsuHudLayout.Scale(6f);
            float headerHeight = EsuHudLayout.Scale(22f);
            float fieldRowY = padding + headerHeight;
            float fieldRowHeight = EsuHudLayout.Scale(46f);
            float available = rect.width - padding * 2f - gap * 4f;
            float cellWidth = Mathf.Max(1f, available / 5f);
            GUI.Label(
                new Rect(padding, padding, rect.width - padding * 2f, headerHeight),
                "Gizmo settings (visual size does not change transform sensitivity)",
                DecorationEditorTheme.SubHeader);

            DrawGizmoSettingField(
                new Rect(padding, fieldRowY, cellWidth, fieldRowHeight),
                "Move size",
                ref _gizmoMoveSizeText,
                "0.5-3.0x move, point, and anchor gizmo length.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap), fieldRowY, cellWidth, fieldRowHeight),
                "Rotate size",
                ref _gizmoRotateSizeText,
                "0.5-3.0x rotation ring radius.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 2f, fieldRowY, cellWidth, fieldRowHeight),
                "Scale size",
                ref _gizmoScaleSizeText,
                "0.5-3.0x scale gizmo length.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 3f, fieldRowY, cellWidth, fieldRowHeight),
                "Thickness",
                ref _gizmoThicknessText,
                "0.5-3.0x gizmo line thickness.");
            DrawGizmoSettingField(
                new Rect(padding + (cellWidth + gap) * 4f, fieldRowY, cellWidth, fieldRowHeight),
                "Click area px",
                ref _gizmoHitAreaText,
                "8-40 pixel full-shaft click radius.");

            bool previous = GUI.enabled;
            GUI.enabled = previous && !HasActiveGizmoDrag();
            float buttonY = rect.height - padding - EsuHudLayout.Scale(26f);
            float buttonWidth = EsuHudLayout.Scale(112f);
            if (GUI.Button(
                    new Rect(padding, buttonY, buttonWidth, EsuHudLayout.Scale(24f)),
                    new GUIContent("Set", "Clamp, save, and apply these profile-only preferences."),
                    DecorationEditorTheme.Button))
                ApplyGizmoSettingsText();
            if (GUI.Button(
                    new Rect(padding + buttonWidth + gap, buttonY, EsuHudLayout.Scale(132f), EsuHudLayout.Scale(24f)),
                    new GUIContent("Reset defaults", "Restore 1x sizes and thickness with an 18 px click area."),
                    DecorationEditorTheme.Button))
            {
                bool saved = EsuGizmoSettings.ResetDefaults();
                SyncGizmoSettingsText();
                InfoStore.Add(saved
                    ? "Gizmo settings reset to defaults and saved."
                    : "Gizmo defaults applied for this session, but the profile could not be saved.");
            }
            if (GUI.Button(
                    new Rect(
                        rect.width - padding - EsuHudLayout.Scale(72f),
                        buttonY,
                        EsuHudLayout.Scale(72f),
                        EsuHudLayout.Scale(24f)),
                    new GUIContent("Close", "Close Gizmo settings without changing typed values that were not set."),
                    DecorationEditorTheme.Button))
                CloseGizmoSettingsMenu();
            GUI.enabled = previous;
            GUILayout.EndArea();
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
                (saved ? "Gizmo settings saved: move " : "Gizmo settings applied for this session (profile save failed): move ") + _gizmoMoveSizeText +
                "x, rotate " + _gizmoRotateSizeText +
                "x, scale " + _gizmoScaleSizeText +
                "x, thickness " + _gizmoThicknessText +
                "x, click area " + _gizmoHitAreaText + "px.");
        }

        private bool HasActiveGizmoDrag() =>
            _dragAxis != DecorationEditAxis.None ||
            _rotateDragAxis != DecorationEditAxis.None ||
            _scaleDragAxis != DecorationEditAxis.None ||
            _anchorDragAxis != DecorationEditAxis.None ||
            _surfaceDragAxis != DecorationEditAxis.None ||
            _generatorDragAxis != DecorationEditAxis.None ||
            _generatorRotateDragAxis != DecorationEditAxis.None ||
            _sharedAnchorDragAxis != DecorationEditAxis.None ||
            _surfaceCoordinateLiveInteractionActive;

        private void DrawBottomSurfaceCoordinateSummary(Rect rect)
        {
            if (!_showSurfaceCoordinates)
            {
                GUI.Label(
                    rect,
                    "Coordinates collapsed | use Show in the left-panel Coordinates header",
                    DecorationEditorTheme.Mini);
                return;
            }

            string target = SurfaceCoordinateTargetLabel();
            GUI.Label(
                rect,
                "Coordinates: use the left Surface Builder panel | target " + target +
                " | sliders and +/- update live | press Enter to apply exact text",
                DecorationEditorTheme.Mini);
        }

        private void DrawSurfaceCoordinateEditorShelf(Rect rect)
        {
            if (rect.height <= 1f || rect.width <= 1f)
                return;

            _surfaceCoordinateHoverSeenThisDraw = false;
            bool expandedAtStart = _showSurfaceCoordinates;

            string binding = SurfaceCoordinateBindingKey();
            if (!string.Equals(binding, _surfaceCoordinateBinding, StringComparison.Ordinal))
                SyncSurfaceCoordinateText(force: true);

            bool hasTarget = TryGetSurfaceCoordinateTarget(
                out bool targetGenerator,
                out int[] targetIndexes,
                out string[] targetLabels);
            int vectorCount = hasTarget ? targetIndexes.Length : 0;
            EsuHudChrome.DrawPanel(rect);
            GUI.BeginGroup(rect);
            float inset = EsuHudLayout.Scale(6f);
            Rect inner = new Rect(
                inset,
                inset,
                Mathf.Max(1f, rect.width - inset * 2f),
                Mathf.Max(1f, rect.height - inset * 2f));
            float desiredHeaderHeight = !expandedAtStart
                ? EsuHudLayout.Scale(26f)
                : inner.width < EsuHudLayout.Scale(420f)
                    ? EsuHudLayout.Scale(52f)
                    : EsuHudLayout.Scale(26f);
            float headerHeight = Mathf.Min(desiredHeaderHeight, inner.height);
            Rect header = new Rect(inner.x, inner.y, inner.width, headerHeight);
            DrawSurfaceCoordinateEditorHeader(header, vectorCount);

            if (!expandedAtStart || !_showSurfaceCoordinates)
            {
                ClearSurfaceCoordinateHover();
                GUI.EndGroup();
                return;
            }

            float separatorHeight = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            float bodyY = header.yMax + EsuHudLayout.Scale(3f);
            DrawCyanLine(new Rect(inner.x, bodyY, inner.width, separatorHeight));
            bodyY += separatorHeight + EsuHudLayout.Scale(3f);
            Rect body = new Rect(
                inner.x,
                bodyY,
                inner.width,
                Mathf.Max(1f, inner.yMax - bodyY));
            GUILayout.BeginArea(body);
            _surfaceCoordinateEditorScroll = GUILayout.BeginScrollView(
                _surfaceCoordinateEditorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Width(body.width),
                GUILayout.Height(body.height));
            DrawSurfaceCoordinateRangeControls();

            if (vectorCount == 0)
            {
                GUILayout.Label(
                    "Select a Surface point, edge, face, generator path point, or shape center.",
                    DecorationEditorTheme.MiniWrap);
            }
            else
            {
                for (int index = 0; index < vectorCount; index++)
                {
                    DrawSurfaceCoordinateVectorWorkbench(
                        index,
                        targetGenerator,
                        targetIndexes[index],
                        targetLabels[index],
                        _surfaceCoordinateText[index]);
                }
            }

            HandleSurfaceCoordinateTextCommitKeyboard();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (!_surfaceCoordinateHoverSeenThisDraw)
                ClearSurfaceCoordinateHover();
            GUI.EndGroup();
        }

        private void DrawSurfaceCoordinateEditorHeader(Rect rect, int vectorCount)
        {
            float gap = EsuHudLayout.Scale(4f);
            if (!_showSurfaceCoordinates)
            {
                float showWidth = Mathf.Min(
                    EsuHudLayout.Scale(82f),
                    Mathf.Max(1f, rect.width * 0.32f));
                Rect show = new Rect(
                    rect.xMax - showWidth,
                    rect.y,
                    showWidth,
                    rect.height);
                Rect collapsedTitle = new Rect(
                    rect.x,
                    rect.y,
                    Mathf.Max(1f, show.x - rect.x - gap),
                    rect.height);
                GUI.Label(
                    collapsedTitle,
                    new GUIContent(
                        "Coordinates",
                        "Construct-local coordinates in metres."),
                    DecorationEditorTheme.SubHeader);
                if (GUI.Button(
                        show,
                        new GUIContent(
                            "Show",
                            "Expand Coordinates above the pinned Surface settings."),
                        DecorationEditorTheme.Button))
                {
                    _showSurfaceCoordinates = true;
                    SyncSurfaceCoordinateText(force: true);
                }
                return;
            }

            bool stacked = rect.height > EsuHudLayout.Scale(36f);
            Rect title;
            Rect actions;
            if (stacked)
            {
                float rowHeight = Mathf.Max(1f, (rect.height - gap) * 0.5f);
                title = new Rect(rect.x, rect.y, rect.width, rowHeight);
                actions = new Rect(rect.x, rect.y + rowHeight + gap, rect.width, rowHeight);
            }
            else
            {
                float actionsWidth = Mathf.Min(EsuHudLayout.Scale(190f), rect.width * 0.46f);
                float titleWidth = Mathf.Max(1f, rect.width - actionsWidth - gap);
                title = new Rect(rect.x, rect.y, titleWidth, rect.height);
                actions = new Rect(rect.xMax - actionsWidth, rect.y, actionsWidth, rect.height);
            }
            GUI.Label(
                title,
                new GUIContent(
                    "Coordinates",
                    "Construct-local coordinates in metres."),
                DecorationEditorTheme.SubHeader);

            float actionGap = EsuHudLayout.Scale(3f);
            float buttonWidth = Mathf.Max(1f, (actions.width - actionGap) * 0.5f);
            Rect revert = new Rect(actions.x, actions.y, buttonWidth, actions.height);
            Rect hide = new Rect(revert.xMax + actionGap, actions.y, buttonWidth, actions.height);

            bool previous = GUI.enabled;
            GUI.enabled = previous && vectorCount > 0;
            if (GUI.Button(
                    revert,
                    new GUIContent(
                        "Revert",
                        "Restore coordinates from when this target was selected, or discard unapplied typed text."),
                    vectorCount > 0
                        ? DecorationEditorTheme.Button
                        : DecorationEditorTheme.DisabledButton))
            {
                RevertSurfaceCoordinateText();
            }
            GUI.enabled = previous;
            if (GUI.Button(
                    hide,
                    new GUIContent("Hide", "Hide Coordinates and give its space to the draft workspace."),
                    DecorationEditorTheme.Button))
            {
                FinalizeSurfaceCoordinateLiveInteraction();
                CloseSurfaceCoordinateStepEditor();
                _showSurfaceCoordinates = false;
                ClearSurfaceCoordinateHover();
                ClearStackDividerDrag(StackDividerKind.SurfaceCoordinates);
            }
        }

        private void DrawSurfaceCoordinateRangeControls()
        {
            SurfaceCoordinateSliderRange range = SurfaceCoordinateSliderSettings.Current;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slider ranges", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(82f)));
            GUILayout.Label(
                "X " + SurfaceCoordinateSliderSettings.Format(range.Minimum.x) + ".." +
                SurfaceCoordinateSliderSettings.Format(range.Maximum.x) + " | Y " +
                SurfaceCoordinateSliderSettings.Format(range.Minimum.y) + ".." +
                SurfaceCoordinateSliderSettings.Format(range.Maximum.y) + " | Z " +
                SurfaceCoordinateSliderSettings.Format(range.Minimum.z) + ".." +
                SurfaceCoordinateSliderSettings.Format(range.Maximum.z),
                DecorationEditorTheme.Mini,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(
                        _surfaceCoordinateRangesExpanded ? "Hide ranges" : "Edit ranges",
                        "Edit the saved slider navigation range for each construct-local axis."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(82f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                _surfaceCoordinateRangesExpanded = !_surfaceCoordinateRangesExpanded;
            }
            GUILayout.EndHorizontal();

            if (!_surfaceCoordinateRangesExpanded)
                return;

            for (int component = 0; component < 3; component++)
                DrawSurfaceCoordinateRangeRow(component);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Set", "Validate, save, and apply all three slider ranges."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(92f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplySurfaceCoordinateRangeText();
            }
            if (GUILayout.Button(
                    new GUIContent("Reset -10/+10", "Reset every slider axis to -10 through +10 metres."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(112f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                bool saved = SurfaceCoordinateSliderSettings.ResetDefaults();
                SyncSurfaceCoordinateRangeText();
                InfoStore.Add(saved
                    ? "Surface coordinate slider ranges reset to -10..10 and saved."
                    : "Surface coordinate slider ranges reset for this session, but the profile could not be saved.");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSurfaceCoordinateRangeRow(int component)
        {
            DecorationEditAxis axis = SurfaceCoordinateAxis(component);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                axis.ToString(),
                BottomVectorAxisStyle(axis),
                GUILayout.Width(EsuHudLayout.Scale(18f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            GUILayout.Label("Min", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(28f)));
            _surfaceCoordinateRangeMinimumText[component] = GUILayout.TextField(
                _surfaceCoordinateRangeMinimumText[component] ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinWidth(EsuHudLayout.Scale(52f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            EsuCursorTooltip.RegisterLast(axis + " slider minimum in construct-local metres.");
            GUILayout.Label("Max", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(28f)));
            _surfaceCoordinateRangeMaximumText[component] = GUILayout.TextField(
                _surfaceCoordinateRangeMaximumText[component] ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinWidth(EsuHudLayout.Scale(52f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            EsuCursorTooltip.RegisterLast(axis + " slider maximum in construct-local metres.");
            GUILayout.EndHorizontal();
        }

        private void SyncSurfaceCoordinateRangeText()
        {
            SurfaceCoordinateSliderRange range = SurfaceCoordinateSliderSettings.Current;
            for (int component = 0; component < 3; component++)
            {
                _surfaceCoordinateRangeMinimumText[component] =
                    SurfaceCoordinateSliderSettings.Format(range.MinimumFor(component));
                _surfaceCoordinateRangeMaximumText[component] =
                    SurfaceCoordinateSliderSettings.Format(range.MaximumFor(component));
            }

            ResetSurfaceCoordinateDisplayRanges();
        }

        private void ApplySurfaceCoordinateRangeText()
        {
            var minimum = new Vector3();
            var maximum = new Vector3();
            for (int component = 0; component < 3; component++)
            {
                if (!FlexibleFloatParser.TryParse(
                        _surfaceCoordinateRangeMinimumText[component],
                        out float parsedMinimum) ||
                    !FlexibleFloatParser.TryParse(
                        _surfaceCoordinateRangeMaximumText[component],
                        out float parsedMaximum))
                {
                    InfoStore.Add(
                        SurfaceCoordinateAxis(component) +
                        " slider range needs complete, finite Min and Max values.");
                    return;
                }

                SetSurfaceCoordinateComponent(ref minimum, component, parsedMinimum);
                SetSurfaceCoordinateComponent(ref maximum, component, parsedMaximum);
            }

            if (!SurfaceCoordinateSliderSettings.TrySet(
                    minimum,
                    maximum,
                    out bool _,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }

            SyncSurfaceCoordinateRangeText();
            InfoStore.Add(message);
        }

        private void ResetSurfaceCoordinateDisplayRanges()
        {
            SurfaceCoordinateSliderRange configured = SurfaceCoordinateSliderSettings.Current;
            _surfaceCoordinateDisplayMinimum = configured.Minimum;
            _surfaceCoordinateDisplayMaximum = configured.Maximum;
            for (int component = 0; component < 3; component++)
            {
                _surfaceCoordinateDisplayRangeValid[component] =
                    SurfaceCoordinateSliderSettings.TryExpandDisplayRange(
                        configured.MinimumFor(component),
                        configured.MaximumFor(component),
                        SurfaceCoordinateStagedValues(component),
                        out float displayMinimum,
                        out float displayMaximum);
                if (!_surfaceCoordinateDisplayRangeValid[component])
                    continue;

                SetSurfaceCoordinateComponent(
                    ref _surfaceCoordinateDisplayMinimum,
                    component,
                    displayMinimum);
                SetSurfaceCoordinateComponent(
                    ref _surfaceCoordinateDisplayMaximum,
                    component,
                    displayMaximum);
            }
        }

        private IEnumerable<float> SurfaceCoordinateStagedValues(int component)
        {
            int vectorCount = SurfaceCoordinateVectorCount();
            for (int index = 0; index < vectorCount; index++)
            {
                if (FlexibleFloatParser.TryParse(
                        _surfaceCoordinateText[index][component],
                        out float value))
                {
                    yield return value;
                }
            }
        }

        private void EnsureSurfaceCoordinateDisplayRangeIncludes(
            int component,
            float value)
        {
            if (component < 0 || component >= 3 ||
                !DecorationEditMath.IsFinite(value))
            {
                return;
            }

            float currentMinimum = SurfaceCoordinateDisplayMinimumFor(component);
            float currentMaximum = SurfaceCoordinateDisplayMaximumFor(component);
            if (_surfaceCoordinateDisplayRangeValid[component] &&
                value >= currentMinimum &&
                value <= currentMaximum)
            {
                return;
            }

            if (!SurfaceCoordinateSliderSettings.TryExpandDisplayRange(
                    currentMinimum,
                    currentMaximum,
                    new[] { value },
                    out float displayMinimum,
                    out float displayMaximum))
            {
                _surfaceCoordinateDisplayRangeValid[component] = false;
                return;
            }

            SetSurfaceCoordinateComponent(
                ref _surfaceCoordinateDisplayMinimum,
                component,
                displayMinimum);
            SetSurfaceCoordinateComponent(
                ref _surfaceCoordinateDisplayMaximum,
                component,
                displayMaximum);
            _surfaceCoordinateDisplayRangeValid[component] = true;
        }

        private float SurfaceCoordinateDisplayMinimumFor(int component) =>
            SurfaceCoordinateComponent(_surfaceCoordinateDisplayMinimum, component);

        private float SurfaceCoordinateDisplayMaximumFor(int component) =>
            SurfaceCoordinateComponent(_surfaceCoordinateDisplayMaximum, component);

        private static float SurfaceCoordinateComponent(Vector3 value, int component) =>
            component == 0 ? value.x : component == 1 ? value.y : value.z;

        private static void SetSurfaceCoordinateComponent(
            ref Vector3 value,
            int component,
            float componentValue)
        {
            if (component == 0)
                value.x = componentValue;
            else if (component == 1)
                value.y = componentValue;
            else
                value.z = componentValue;
        }

        private static DecorationEditAxis SurfaceCoordinateAxis(int component) =>
            component == 0
                ? DecorationEditAxis.X
                : component == 1
                    ? DecorationEditAxis.Y
                    : DecorationEditAxis.Z;

        private void DrawSurfaceCoordinateVectorWorkbench(
            int vectorIndex,
            bool generator,
            int pointIndex,
            string label,
            string[] values)
        {
            DecorationEditorTheme.Separator();
            Rect header = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(24f),
                GUILayout.ExpandWidth(true));
            bool headerHovered = RegisterSurfaceCoordinateHover(
                header,
                vectorIndex,
                generator,
                pointIndex);
            if (headerHovered || _surfaceCoordinateHoverVectorIndex == vectorIndex)
                GUI.Box(header, GUIContent.none, DecorationEditorTheme.RowSelected);
            GUI.Label(header, label, DecorationEditorTheme.SubHeader);
            for (int component = 0; component < 3; component++)
            {
                bool drawStepEditor =
                    _surfaceCoordinateStepEditorVectorIndex == vectorIndex &&
                    _surfaceCoordinateStepEditorComponent == component;
                DrawSurfaceCoordinateAxisSlider(
                    vectorIndex,
                    generator,
                    pointIndex,
                    label,
                    values,
                    component);
                if (drawStepEditor)
                    DrawSurfaceCoordinateStepEditor(component);
            }
        }

        private void DrawSurfaceCoordinateAxisSlider(
            int vectorIndex,
            bool generator,
            int pointIndex,
            string label,
            string[] values,
            int component)
        {
            DecorationEditAxis axis = SurfaceCoordinateAxis(component);
            Rect row = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(27f),
                GUILayout.ExpandWidth(true));
            float gap = EsuHudLayout.Scale(4f);
            float axisWidth = EsuHudLayout.Scale(18f);
            float fieldWidth = Mathf.Min(EsuHudLayout.Scale(82f), row.width * 0.19f);
            float boundWidth = Mathf.Min(EsuHudLayout.Scale(46f), row.width * 0.095f);
            float stepWidth = Mathf.Min(EsuHudLayout.Scale(74f), row.width * 0.14f);
            Rect axisRect = new Rect(row.x, row.y, axisWidth, row.height);
            Rect field = new Rect(axisRect.xMax + gap, row.y, fieldWidth, row.height);
            Rect minimum = new Rect(field.xMax + gap, row.y, boundWidth, row.height);
            Rect maximum = new Rect(row.xMax - boundWidth, row.y, boundWidth, row.height);
            Rect decrease = new Rect(minimum.xMax + gap, row.y, stepWidth, row.height);
            Rect increase = new Rect(maximum.x - gap - stepWidth, row.y, stepWidth, row.height);
            Rect slider = new Rect(
                decrease.xMax + gap,
                row.y,
                Mathf.Max(1f, increase.x - decrease.xMax - gap * 2f),
                row.height);

            bool rowHovered = RegisterSurfaceCoordinateHover(
                row,
                vectorIndex,
                generator,
                pointIndex);
            if (rowHovered)
                GUI.Box(row, GUIContent.none, DecorationEditorTheme.RowSelected);

            GUI.Label(axisRect, axis.ToString(), BottomVectorAxisStyle(axis));
            GUI.SetNextControlName(SurfaceCoordinateTextControlName(vectorIndex, component));
            values[component] = GUI.TextField(
                field,
                values[component] ?? string.Empty,
                DecorationEditorTheme.TextField);
            EsuCursorTooltip.Register(
                field,
                label + " " + axis + " coordinate in construct-local metres. Press Enter to apply all exact typed values atomically.");

            bool parsed = FlexibleFloatParser.TryParse(values[component], out float parsedValue);
            if (parsed)
                EnsureSurfaceCoordinateDisplayRangeIncludes(component, parsedValue);

            float displayMinimum = SurfaceCoordinateDisplayMinimumFor(component);
            float displayMaximum = SurfaceCoordinateDisplayMaximumFor(component);
            GUI.Label(
                minimum,
                SurfaceCoordinateSliderSettings.Format(displayMinimum),
                DecorationEditorTheme.Mini);
            GUI.Label(
                maximum,
                SurfaceCoordinateSliderSettings.Format(displayMaximum),
                DecorationEditorTheme.Mini);

            bool previous = GUI.enabled;
            bool sliderEnabled = _surfaceCoordinateDisplayRangeValid[component] &&
                                 parsed;
            float step = SurfaceCoordinateSliderSettings.CurrentStep(component);
            string formattedStep = SurfaceCoordinateSliderSettings.Format(step);
            bool stepContextOpened = HandleSurfaceCoordinateStepContextClick(
                decrease,
                increase,
                vectorIndex,
                component);
            GUI.enabled = previous && sliderEnabled;
            if (!stepContextOpened &&
                GUI.Button(
                    decrease,
                    "-" + formattedStep,
                    DecorationEditorTheme.Button))
            {
                ApplySurfaceCoordinateLiveStep(
                    vectorIndex,
                    component,
                    parsed ? parsedValue : 0f,
                    -1f);
            }
            if (!stepContextOpened &&
                GUI.Button(
                    increase,
                    "+" + formattedStep,
                    DecorationEditorTheme.Button))
            {
                ApplySurfaceCoordinateLiveStep(
                    vectorIndex,
                    component,
                    parsed ? parsedValue : 0f,
                    1f);
            }
            float current = parsed ? parsedValue : 0f;
            float clamped = Mathf.Clamp(current, displayMinimum, displayMaximum);
            float next = GUI.HorizontalSlider(
                slider,
                clamped,
                displayMinimum,
                displayMaximum);
            EsuCursorTooltip.Register(
                slider,
                sliderEnabled
                    ? "Adjust " + label + " " + axis + " live at 0.001 m resolution. One drag is one undo action."
                    : "Enter a complete finite coordinate before using this slider.");
            EsuCursorTooltip.Register(
                decrease,
                "Subtract " + formattedStep + " m. Right-click either step button to choose or type the " + axis + " step.");
            EsuCursorTooltip.Register(
                increase,
                "Add " + formattedStep + " m. Right-click either step button to choose or type the " + axis + " step.");
            GUI.enabled = previous;

            if (sliderEnabled && Mathf.Abs(next - clamped) >= 0.0005f)
            {
                float snapped = DecorationEditMath.Snap(
                    next,
                    SurfaceCoordinateSliderSettings.ResolutionMetres);
                if (DecorationEditMath.IsFinite(snapped))
                {
                    BeginSurfaceCoordinateLiveInteraction(generator);
                    TryApplySurfaceCoordinateLiveValue(
                        vectorIndex,
                        component,
                        snapped,
                        out string _);
                }
            }
        }

        private static string SurfaceCoordinateTextControlName(
            int vectorIndex,
            int component) =>
            SurfaceCoordinateTextControlPrefix +
            vectorIndex.ToString(CultureInfo.InvariantCulture) + "." +
            component.ToString(CultureInfo.InvariantCulture);

        private void HandleSurfaceCoordinateTextCommitKeyboard()
        {
            Event current = Event.current;
            if (!GUI.enabled ||
                current == null ||
                current.type != EventType.KeyDown ||
                (current.keyCode != KeyCode.Return &&
                 current.keyCode != KeyCode.KeypadEnter))
            {
                return;
            }

            string focusedControl = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(focusedControl) ||
                !focusedControl.StartsWith(
                    SurfaceCoordinateTextControlPrefix,
                    StringComparison.Ordinal))
            {
                return;
            }

            ApplySurfaceCoordinateText();
            GUI.FocusControl(null);
            current.Use();
        }

        private bool HandleSurfaceCoordinateStepContextClick(
            Rect decrease,
            Rect increase,
            int vectorIndex,
            int component)
        {
            Event current = Event.current;
            if (!GUI.enabled ||
                current == null ||
                !(current.type == EventType.ContextClick ||
                  (current.type == EventType.MouseDown && current.button == 1)) ||
                (!decrease.Contains(current.mousePosition) &&
                 !increase.Contains(current.mousePosition)))
            {
                return false;
            }

            if (_surfaceCoordinateStepEditorVectorIndex == vectorIndex &&
                _surfaceCoordinateStepEditorComponent == component)
            {
                CloseSurfaceCoordinateStepEditor();
            }
            else
            {
                _surfaceCoordinateStepEditorVectorIndex = vectorIndex;
                _surfaceCoordinateStepEditorComponent = component;
                _surfaceCoordinateStepText = SurfaceCoordinateSliderSettings.Format(
                    SurfaceCoordinateSliderSettings.CurrentStep(component));
            }

            current.Use();
            return true;
        }

        private bool RegisterSurfaceCoordinateHover(
            Rect rect,
            int vectorIndex,
            bool generator,
            int pointIndex)
        {
            Event current = Event.current;
            if (current == null ||
                !rect.Contains(current.mousePosition) ||
                vectorIndex < 0 ||
                vectorIndex >= _surfaceCoordinateText.Length)
            {
                return false;
            }

            if (pointIndex < 0 ||
                (generator && pointIndex >= _generatorDraft.PointCount) ||
                (!generator && pointIndex >= _surfaceDraft.Points.Count))
            {
                return false;
            }

            _surfaceCoordinateHoverSeenThisDraw = true;
            _surfaceCoordinateHoverVectorIndex = vectorIndex;
            _surfaceCoordinateHoverPointIndex = pointIndex;
            _surfaceCoordinateHoverGenerator = generator;
            return true;
        }

        private void ClearSurfaceCoordinateHover()
        {
            _surfaceCoordinateHoverVectorIndex = -1;
            _surfaceCoordinateHoverPointIndex = -1;
            _surfaceCoordinateHoverGenerator = false;
            _surfaceCoordinateHoverSeenThisDraw = false;
        }

        private void DrawSurfaceCoordinateStepEditor(int component)
        {
            DecorationEditAxis axis = SurfaceCoordinateAxis(component);
            GUILayout.BeginVertical(
                DecorationEditorTheme.Panel,
                GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                axis + " step (metres)",
                BottomVectorAxisStyle(axis),
                GUILayout.Width(EsuHudLayout.Scale(92f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            _surfaceCoordinateStepText = GUILayout.TextField(
                _surfaceCoordinateStepText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinWidth(EsuHudLayout.Scale(62f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            EsuCursorTooltip.RegisterLast(
                "Custom positive step from 0.001 m through 1000 m. Values normalize to 0.001 m.");
            if (GUILayout.Button(
                    new GUIContent("Set", "Validate and save this axis step."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplySurfaceCoordinateStepText(component);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Default", "Reset this axis to the default 0.1 m step."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(66f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                bool reset = SurfaceCoordinateSliderSettings.ResetStep(
                    component,
                    out bool _,
                    out string message);
                if (reset)
                {
                    _surfaceCoordinateStepText = SurfaceCoordinateSliderSettings.Format(
                        SurfaceCoordinateSliderSettings.CurrentStep(component));
                    CloseSurfaceCoordinateStepEditor();
                }
                InfoStore.Add(message);
            }
            if (GUILayout.Button(
                    new GUIContent("Close", "Close the step chooser."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                CloseSurfaceCoordinateStepEditor();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawSurfaceCoordinateStepPreset(component, "0.001", 0.001f);
            DrawSurfaceCoordinateStepPreset(component, "0.01", 0.01f);
            DrawSurfaceCoordinateStepPreset(component, "0.05", 0.05f);
            DrawSurfaceCoordinateStepPreset(component, "0.1", 0.1f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawSurfaceCoordinateStepPreset(component, "1/8", 0.125f);
            DrawSurfaceCoordinateStepPreset(component, "1/4", 0.25f);
            DrawSurfaceCoordinateStepPreset(component, "1/2", 0.5f);
            DrawSurfaceCoordinateStepPreset(component, "1", 1f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawSurfaceCoordinateStepPreset(
            int component,
            string label,
            float value)
        {
            if (!GUILayout.Button(
                    new GUIContent(label, "Use a " + SurfaceCoordinateSliderSettings.Format(value) + " m step."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                return;
            }

            if (SurfaceCoordinateSliderSettings.TrySetStep(
                    component,
                    value,
                    out bool _,
                    out string message))
            {
                _surfaceCoordinateStepText = SurfaceCoordinateSliderSettings.Format(
                    SurfaceCoordinateSliderSettings.CurrentStep(component));
                CloseSurfaceCoordinateStepEditor();
            }
            InfoStore.Add(message);
        }

        private void ApplySurfaceCoordinateStepText(int component)
        {
            string message = null;
            if (!FlexibleFloatParser.TryParse(
                    _surfaceCoordinateStepText,
                    out float value) ||
                !SurfaceCoordinateSliderSettings.TrySetStep(
                    component,
                    value,
                    out bool _,
                    out message))
            {
                InfoStore.Add(
                    string.IsNullOrEmpty(message)
                        ? "Surface coordinate step must be a complete finite number."
                        : message);
                return;
            }

            _surfaceCoordinateStepText = SurfaceCoordinateSliderSettings.Format(
                SurfaceCoordinateSliderSettings.CurrentStep(component));
            CloseSurfaceCoordinateStepEditor();
            InfoStore.Add(message);
        }

        private void CloseSurfaceCoordinateStepEditor()
        {
            _surfaceCoordinateStepEditorComponent = -1;
            _surfaceCoordinateStepEditorVectorIndex = -1;
        }

        private int SurfaceCoordinateVectorCount()
        {
            return TryGetSurfaceCoordinateTarget(
                out bool _,
                out int[] _,
                out string[] labels)
                ? labels.Length
                : 0;
        }

        private string SurfaceCoordinateTargetLabel()
        {
            if (!TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] _,
                    out string[] labels))
            {
                return "none";
            }

            if (generator)
                return labels[0];
            if (_surfaceDraft.SelectionKind == SurfaceSelectionKind.Edge)
                return "Edge A/B";
            if (_surfaceDraft.SelectionKind == SurfaceSelectionKind.Face)
                return "Face A/B/C";
            return labels[0];
        }

        private bool TryGetSurfaceCoordinateTarget(
            out bool generator,
            out int[] pointIndexes,
            out string[] labels)
        {
            EnsureSurfaceCoordinateTargetBinding();
            generator = _surfaceCoordinateTargetGenerator;
            pointIndexes = _surfaceCoordinateTargetIndexes;
            labels = _surfaceCoordinateTargetLabels;
            return pointIndexes.Length > 0;
        }

        private void EnsureSurfaceCoordinateTargetBinding()
        {
            if (SurfaceCoordinateTargetBindingMatchesSelection())
                return;

            _surfaceCoordinateTargetBindingInitialized = true;
            _surfaceCoordinateTargetGenerator = false;
            _surfaceCoordinateTargetGeneratorTool = SurfaceExtraTool.None;
            _surfaceCoordinateTargetConstruct = null;
            _surfaceCoordinateTargetIndexes = Array.Empty<int>();
            _surfaceCoordinateTargetLabels = Array.Empty<string>();

            int generatorPoint = _generatorDraft.SelectedPoint;
            if (generatorPoint >= 0 && generatorPoint < _generatorDraft.PointCount)
            {
                _surfaceCoordinateTargetGenerator = true;
                _surfaceCoordinateTargetGeneratorTool = _generatorDraft.Tool;
                _surfaceCoordinateTargetConstruct = _generatorDraft.Construct;
                _surfaceCoordinateTargetIndexes = new[] { generatorPoint };
                _surfaceCoordinateTargetLabels = new[]
                {
                    _generatorDraft.UsesCenterTool
                        ? "Center"
                        : "P" + (generatorPoint + 1).ToString(CultureInfo.InvariantCulture)
                };
                return;
            }

            _surfaceCoordinateTargetConstruct = _surfaceDraft.Construct;
            switch (_surfaceDraft.SelectionKind)
            {
                case SurfaceSelectionKind.Point:
                    if (_surfaceDraft.SelectedPoint < 0 ||
                        _surfaceDraft.SelectedPoint >= _surfaceDraft.Points.Count)
                        return;
                    _surfaceCoordinateTargetIndexes = new[] { _surfaceDraft.SelectedPoint };
                    _surfaceCoordinateTargetLabels = new[]
                    {
                        "P" + (_surfaceDraft.SelectedPoint + 1).ToString(CultureInfo.InvariantCulture)
                    };
                    return;

                case SurfaceSelectionKind.Edge:
                    SurfaceEdge edge = _surfaceDraft.SelectedEdge;
                    if (!edge.IsValid ||
                        edge.A >= _surfaceDraft.Points.Count ||
                        edge.B >= _surfaceDraft.Points.Count)
                        return;
                    _surfaceCoordinateTargetIndexes = new[] { edge.A, edge.B };
                    _surfaceCoordinateTargetLabels = new[]
                    {
                        "A P" + (edge.A + 1).ToString(CultureInfo.InvariantCulture),
                        "B P" + (edge.B + 1).ToString(CultureInfo.InvariantCulture)
                    };
                    return;

                case SurfaceSelectionKind.Face:
                    int faceIndex = _surfaceDraft.SelectedFace;
                    if (faceIndex < 0 || faceIndex >= _surfaceDraft.Faces.Count)
                        return;
                    SurfaceFace face = _surfaceDraft.Faces[faceIndex];
                    if (face.A < 0 || face.B < 0 || face.C < 0 ||
                        face.A >= _surfaceDraft.Points.Count ||
                        face.B >= _surfaceDraft.Points.Count ||
                        face.C >= _surfaceDraft.Points.Count)
                        return;
                    _surfaceCoordinateTargetIndexes = new[] { face.A, face.B, face.C };
                    _surfaceCoordinateTargetLabels = new[]
                    {
                        "A P" + (face.A + 1).ToString(CultureInfo.InvariantCulture),
                        "B P" + (face.B + 1).ToString(CultureInfo.InvariantCulture),
                        "C P" + (face.C + 1).ToString(CultureInfo.InvariantCulture)
                    };
                    return;
            }
        }

        private bool SurfaceCoordinateTargetBindingMatchesSelection()
        {
            if (!_surfaceCoordinateTargetBindingInitialized)
                return false;

            int generatorPoint = _generatorDraft.SelectedPoint;
            if (generatorPoint >= 0 && generatorPoint < _generatorDraft.PointCount)
            {
                return _surfaceCoordinateTargetGenerator &&
                       _surfaceCoordinateTargetGeneratorTool == _generatorDraft.Tool &&
                       ReferenceEquals(_surfaceCoordinateTargetConstruct, _generatorDraft.Construct) &&
                       _surfaceCoordinateTargetIndexes.Length == 1 &&
                       _surfaceCoordinateTargetIndexes[0] == generatorPoint;
            }

            if (_surfaceCoordinateTargetGenerator)
                return false;

            switch (_surfaceDraft.SelectionKind)
            {
                case SurfaceSelectionKind.Point:
                    return _surfaceDraft.SelectedPoint >= 0 &&
                           _surfaceDraft.SelectedPoint < _surfaceDraft.Points.Count &&
                           ReferenceEquals(_surfaceCoordinateTargetConstruct, _surfaceDraft.Construct) &&
                           _surfaceCoordinateTargetIndexes.Length == 1 &&
                           _surfaceCoordinateTargetIndexes[0] == _surfaceDraft.SelectedPoint;

                case SurfaceSelectionKind.Edge:
                    SurfaceEdge edge = _surfaceDraft.SelectedEdge;
                    return edge.IsValid &&
                           edge.A < _surfaceDraft.Points.Count &&
                           edge.B < _surfaceDraft.Points.Count &&
                           ReferenceEquals(_surfaceCoordinateTargetConstruct, _surfaceDraft.Construct) &&
                           _surfaceCoordinateTargetIndexes.Length == 2 &&
                           _surfaceCoordinateTargetIndexes[0] == edge.A &&
                           _surfaceCoordinateTargetIndexes[1] == edge.B;

                case SurfaceSelectionKind.Face:
                    int faceIndex = _surfaceDraft.SelectedFace;
                    if (faceIndex < 0 || faceIndex >= _surfaceDraft.Faces.Count)
                        break;
                    SurfaceFace face = _surfaceDraft.Faces[faceIndex];
                    return face.A >= 0 && face.B >= 0 && face.C >= 0 &&
                           face.A < _surfaceDraft.Points.Count &&
                           face.B < _surfaceDraft.Points.Count &&
                           face.C < _surfaceDraft.Points.Count &&
                           ReferenceEquals(_surfaceCoordinateTargetConstruct, _surfaceDraft.Construct) &&
                           _surfaceCoordinateTargetIndexes.Length == 3 &&
                           _surfaceCoordinateTargetIndexes[0] == face.A &&
                           _surfaceCoordinateTargetIndexes[1] == face.B &&
                           _surfaceCoordinateTargetIndexes[2] == face.C;
            }

            return _surfaceCoordinateTargetIndexes.Length == 0;
        }

        private string SurfaceCoordinateBindingKey()
        {
            if (!TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] indexes,
                    out string[] _))
                return "none";

            var key = new System.Text.StringBuilder(generator ? "generator" : "surface");
            for (int index = 0; index < indexes.Length; index++)
            {
                Vector3 point = generator
                    ? _generatorDraft.PointAt(indexes[index])
                    : _surfaceDraft.Points[indexes[index]];
                key.Append('|').Append(indexes[index].ToString(CultureInfo.InvariantCulture));
                key.Append(':').Append(point.x.ToString("R", CultureInfo.InvariantCulture));
                key.Append(':').Append(point.y.ToString("R", CultureInfo.InvariantCulture));
                key.Append(':').Append(point.z.ToString("R", CultureInfo.InvariantCulture));
            }

            return key.ToString();
        }

        private void SyncSurfaceCoordinateText(bool force)
        {
            string binding = SurfaceCoordinateBindingKey();
            if (!force && string.Equals(binding, _surfaceCoordinateBinding, StringComparison.Ordinal))
                return;

            FinalizeSurfaceCoordinateLiveInteraction();
            CloseSurfaceCoordinateStepEditor();
            ClearSurfaceCoordinateHover();
            ClearSurfaceCoordinateText();
            bool generator = false;
            int[] indexes = Array.Empty<int>();
            bool hasTarget = TryGetSurfaceCoordinateTarget(
                out generator,
                out indexes,
                out string[] _);
            if (hasTarget)
            {
                for (int index = 0; index < indexes.Length && index < 3; index++)
                {
                    Vector3 point = generator
                        ? _generatorDraft.PointAt(indexes[index])
                        : _surfaceDraft.Points[indexes[index]];
                    _surfaceCoordinateText[index][0] = FormatFloat(point.x);
                    _surfaceCoordinateText[index][1] = FormatFloat(point.y);
                    _surfaceCoordinateText[index][2] = FormatFloat(point.z);
                }

                CaptureSurfaceCoordinateBaseline(generator, indexes);
            }
            else
            {
                ClearSurfaceCoordinateBaseline();
            }

            _surfaceCoordinateBinding = binding;
            ResetSurfaceCoordinateDisplayRanges();
        }

        private void ClearSurfaceCoordinateText()
        {
            for (int row = 0; row < _surfaceCoordinateText.Length; row++)
            {
                for (int component = 0; component < _surfaceCoordinateText[row].Length; component++)
                    _surfaceCoordinateText[row][component] = string.Empty;
            }
        }

        private void CaptureSurfaceCoordinateBaseline(bool generator, int[] indexes)
        {
            ClearSurfaceCoordinateBaseline();
            if (indexes == null || indexes.Length == 0 || indexes.Length > 3)
                return;

            var values = new Vector3[indexes.Length];
            for (int index = 0; index < indexes.Length; index++)
            {
                int pointIndex = indexes[index];
                if (pointIndex < 0 ||
                    (generator && pointIndex >= _generatorDraft.PointCount) ||
                    (!generator && pointIndex >= _surfaceDraft.Points.Count))
                {
                    return;
                }

                values[index] = generator
                    ? _generatorDraft.PointAt(pointIndex)
                    : _surfaceDraft.Points[pointIndex];
            }

            _surfaceCoordinateBaselineGenerator = generator;
            _surfaceCoordinateBaselineConstruct = generator
                ? _generatorDraft.Construct
                : _surfaceDraft.Construct;
            _surfaceCoordinateBaselineIndexes = (int[])indexes.Clone();
            _surfaceCoordinateBaselineValues = values;
            _surfaceCoordinateBaselineValid = true;
        }

        private void ClearSurfaceCoordinateBaseline()
        {
            _surfaceCoordinateBaselineValid = false;
            _surfaceCoordinateBaselineGenerator = false;
            _surfaceCoordinateBaselineConstruct = null;
            _surfaceCoordinateBaselineIndexes = Array.Empty<int>();
            _surfaceCoordinateBaselineValues = Array.Empty<Vector3>();
        }

        private bool SurfaceCoordinateBaselineMatches(
            bool generator,
            int[] indexes)
        {
            if (!_surfaceCoordinateBaselineValid ||
                _surfaceCoordinateBaselineGenerator != generator ||
                indexes == null ||
                indexes.Length != _surfaceCoordinateBaselineIndexes.Length ||
                !ReferenceEquals(
                    _surfaceCoordinateBaselineConstruct,
                    generator ? _generatorDraft.Construct : _surfaceDraft.Construct))
            {
                return false;
            }

            for (int index = 0; index < indexes.Length; index++)
            {
                if (indexes[index] != _surfaceCoordinateBaselineIndexes[index])
                    return false;
            }

            return true;
        }

        private void BeginSurfaceCoordinateLiveInteraction(bool generator)
        {
            if (_surfaceCoordinateLiveInteractionActive)
            {
                if (_surfaceCoordinateLiveInteractionGenerator == generator)
                    return;
                FinalizeSurfaceCoordinateLiveInteraction();
            }

            _surfaceCoordinateLiveInteractionActive = true;
            _surfaceCoordinateLiveInteractionGenerator = generator;
            _surfaceCoordinateLiveSurfaceBefore = generator
                ? null
                : CaptureSurfaceSnapshot();
            _surfaceCoordinateLiveGeneratorBefore = generator
                ? CaptureGeneratorEditSnapshot()
                : null;
        }

        private void FinalizeSurfaceCoordinateLiveInteraction()
        {
            if (!_surfaceCoordinateLiveInteractionActive)
                return;

            bool generator = _surfaceCoordinateLiveInteractionGenerator;
            SurfaceDraftSnapshot surfaceBefore = _surfaceCoordinateLiveSurfaceBefore;
            DecorationGeneratorEditSnapshot generatorBefore =
                _surfaceCoordinateLiveGeneratorBefore;
            _surfaceCoordinateLiveInteractionActive = false;
            _surfaceCoordinateLiveInteractionGenerator = false;
            _surfaceCoordinateLiveSurfaceBefore = null;
            _surfaceCoordinateLiveGeneratorBefore = null;

            if (generator)
            {
                DecorationGeneratorEditSnapshot after = CaptureGeneratorEditSnapshot();
                if (generatorBefore == null || generatorBefore.SameAs(after))
                    return;
                RecordGeneratorEdit("Adjust generator coordinate", generatorBefore);
                RebuildGeneratorPreview(showMessage: false);
                InfoStore.Add("Generator coordinate adjusted.");
                return;
            }

            SurfaceDraftSnapshot surfaceAfter = CaptureSurfaceSnapshot();
            if (surfaceBefore == null || surfaceBefore.SameAs(surfaceAfter))
                return;
            RecordSurfaceEdit("Adjust surface coordinate", surfaceBefore);
            InfoStore.Add("Surface coordinate adjusted.");
        }

        private bool TryApplySurfaceCoordinateLiveValue(
            int vectorIndex,
            int component,
            float requestedValue,
            out string message)
        {
            message = string.Empty;
            if (vectorIndex < 0 || vectorIndex >= 3 || component < 0 || component >= 3 ||
                !TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] indexes,
                    out string[] _) ||
                vectorIndex >= indexes.Length)
            {
                message = "The selected Surface coordinate target is no longer available.";
                return false;
            }

            float snapped = DecorationEditMath.Snap(
                requestedValue,
                SurfaceCoordinateSliderSettings.ResolutionMetres);
            if (!DecorationEditMath.IsFinite(snapped))
            {
                message = "Surface coordinates must be finite numbers.";
                return false;
            }

            int pointIndex = indexes[vectorIndex];
            Vector3 current = generator
                ? _generatorDraft.PointAt(pointIndex)
                : _surfaceDraft.Points[pointIndex];
            SetSurfaceCoordinateComponent(ref current, component, snapped);

            bool changed;
            if (generator)
            {
                changed = _generatorDraft.TrySetSelectedPointCoordinate(
                    current,
                    out _generatorMessage);
                message = _generatorMessage;
                if (changed)
                    InvalidateGeneratorPlan(_generatorMessage);
            }
            else
            {
                changed = _surfaceDraft.TrySetPointCoordinateLive(
                    pointIndex,
                    current,
                    out _surfaceMessage);
                message = _surfaceMessage;
                if (changed)
                    InvalidateSurfacePlan(_surfaceMessage);
            }

            if (!changed)
                return false;

            Vector3 applied = generator
                ? _generatorDraft.PointAt(pointIndex)
                : _surfaceDraft.Points[pointIndex];
            _surfaceCoordinateText[vectorIndex][component] =
                FormatFloat(SurfaceCoordinateComponent(applied, component));
            _surfaceCoordinateBinding = SurfaceCoordinateBindingKey();
            EnsureSurfaceCoordinateDisplayRangeIncludes(
                component,
                SurfaceCoordinateComponent(applied, component));
            return true;
        }

        private void ApplySurfaceCoordinateLiveStep(
            int vectorIndex,
            int component,
            float currentValue,
            float direction)
        {
            float step = SurfaceCoordinateSliderSettings.CurrentStep(component);
            float requested = currentValue + direction * step;
            if (!DecorationEditMath.IsFinite(requested))
            {
                InfoStore.Add("Surface coordinate step overflowed; choose a smaller step.");
                return;
            }

            float snapped = DecorationEditMath.Snap(
                requested,
                SurfaceCoordinateSliderSettings.ResolutionMetres);
            if (!DecorationEditMath.IsFinite(snapped))
            {
                InfoStore.Add("Surface coordinate step produced an unsafe value.");
                return;
            }

            if (!TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] _,
                    out string[] _))
            {
                InfoStore.Add("Select a Surface or generator point before adjusting coordinates.");
                return;
            }

            BeginSurfaceCoordinateLiveInteraction(generator);
            bool changed = TryApplySurfaceCoordinateLiveValue(
                vectorIndex,
                component,
                snapped,
                out string message);
            FinalizeSurfaceCoordinateLiveInteraction();
            if (!changed && !string.IsNullOrEmpty(message))
                InfoStore.Add(message);
        }

        private void RevertSurfaceCoordinateText()
        {
            FinalizeSurfaceCoordinateLiveInteraction();
            if (!TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] indexes,
                    out string[] _) ||
                !SurfaceCoordinateBaselineMatches(generator, indexes))
            {
                SyncSurfaceCoordinateText(force: true);
                InfoStore.Add("Surface coordinate text reloaded from the current target.");
                return;
            }

            bool differs = false;
            for (int index = 0; index < indexes.Length; index++)
            {
                Vector3 current = generator
                    ? _generatorDraft.PointAt(indexes[index])
                    : _surfaceDraft.Points[indexes[index]];
                if (current != _surfaceCoordinateBaselineValues[index])
                {
                    differs = true;
                    break;
                }
            }

            if (!differs)
            {
                SyncSurfaceCoordinateText(force: true);
                InfoStore.Add("Staged Surface coordinate text reverted.");
                return;
            }

            if (generator)
            {
                DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
                if (!_generatorDraft.TrySetSelectedPointCoordinate(
                        _surfaceCoordinateBaselineValues[0],
                        out _generatorMessage))
                {
                    InfoStore.Add(_generatorMessage);
                    return;
                }

                InvalidateGeneratorPlan(_generatorMessage);
                RecordGeneratorEdit("Revert generator coordinates", before);
                RebuildGeneratorPreview(showMessage: false);
                SyncSurfaceCoordinateText(force: true);
                InfoStore.Add("Generator coordinates reverted.");
                return;
            }

            var updates = new Dictionary<int, Vector3>(indexes.Length);
            for (int index = 0; index < indexes.Length; index++)
                updates.Add(indexes[index], _surfaceCoordinateBaselineValues[index]);
            SurfaceDraftSnapshot surfaceBefore = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TrySetPointCoordinates(updates, out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RecordSurfaceEdit("Revert surface coordinates", surfaceBefore);
            SyncSurfaceCoordinateText(force: true);
            InfoStore.Add("Surface coordinates reverted.");
        }

        private void ApplySurfaceCoordinateText()
        {
            if (!TryGetSurfaceCoordinateTarget(
                    out bool generator,
                    out int[] indexes,
                    out string[] _) ||
                !TryParseSurfaceCoordinateVectors(indexes.Length, out Vector3[] values))
            {
                if (indexes == null || indexes.Length == 0)
                    InfoStore.Add("Select a Surface point, edge, face, or generator point first.");
                return;
            }

            if (generator)
            {
                DecorationGeneratorEditSnapshot before = CaptureGeneratorEditSnapshot();
                if (!_generatorDraft.TrySetSelectedPointCoordinate(values[0], out _generatorMessage))
                {
                    InfoStore.Add(_generatorMessage);
                    return;
                }

                InvalidateGeneratorPlan(_generatorMessage);
                RecordGeneratorEdit("Set generator coordinates", before);
                SyncSurfaceCoordinateText(force: true);
                InfoStore.Add(_generatorMessage);
                return;
            }

            var updates = new Dictionary<int, Vector3>(indexes.Length);
            for (int index = 0; index < indexes.Length; index++)
                updates.Add(indexes[index], values[index]);
            SurfaceDraftSnapshot surfaceBefore = CaptureSurfaceSnapshot();
            if (!_surfaceDraft.TrySetPointCoordinates(updates, out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
            RecordSurfaceEdit("Set surface coordinates", surfaceBefore);
            SyncSurfaceCoordinateText(force: true);
            InfoStore.Add(_surfaceMessage);
        }

        private bool TryParseSurfaceCoordinateVectors(int count, out Vector3[] values)
        {
            values = Array.Empty<Vector3>();
            if (count < 1 || count > 3)
                return false;

            var parsed = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                if (!TryParseVector(_surfaceCoordinateText[index], out parsed[index]))
                {
                    InfoStore.Add(
                        "Coordinate " + ((char)('A' + index)) +
                        " contains incomplete, invalid, NaN, or infinity input.");
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        private struct BottomHeaderLayout
        {
            internal BottomHeaderLayout(
                Rect gizmoSettings,
                Rect title,
                Rect mode,
                Rect selectControls,
                Rect anchorFollow,
                Rect anchorSettings,
                Rect clean)
            {
                GizmoSettings = gizmoSettings;
                Title = title;
                Mode = mode;
                SelectControls = selectControls;
                AnchorFollow = anchorFollow;
                AnchorSettings = anchorSettings;
                Clean = clean;
            }

            internal Rect GizmoSettings { get; }
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
            bool multiSelection = HasMultiSelectionForTransform();
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
                ApplyScaleFromInspector,
                liveApply: !multiSelection);
        }

        private void CycleGroupPivotMode()
        {
            switch (_groupPivotMode)
            {
                case DecorationGroupPivotMode.BoundsCenter:
                    _groupPivotMode = DecorationGroupPivotMode.AverageCenter;
                    break;
                case DecorationGroupPivotMode.AverageCenter:
                    _groupPivotMode = DecorationGroupPivotMode.SelectedDecoration;
                    break;
                case DecorationGroupPivotMode.SelectedDecoration:
                    _groupPivotMode = DecorationGroupPivotMode.SelectedAnchor;
                    break;
                default:
                    _groupPivotMode = DecorationGroupPivotMode.BoundsCenter;
                    break;
            }

            s_groupPivotMode = _groupPivotMode;
            InfoStore.Add("Group pivot: " + GroupPivotModeLabel(_groupPivotMode) + ".");
        }

        private static string GroupPivotModeLabel(DecorationGroupPivotMode mode)
        {
            switch (mode)
            {
                case DecorationGroupPivotMode.AverageCenter:
                    return "Average";
                case DecorationGroupPivotMode.SelectedDecoration:
                    return "Selected";
                case DecorationGroupPivotMode.SelectedAnchor:
                    return "Anchor";
                default:
                    return "Bounds";
            }
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
            Action<Vector3, bool> apply,
            bool liveApply = true)
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
                liveApply &&
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
            int selectionCount = CurrentPrimarySelectionDecorations().Count;
            string selected = selectionCount > 1
                ? selectionCount.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected"
                : _selected == null || _selected.IsDeleted
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
            if (_boxSelecting)
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (Input.GetMouseButtonDown(1))
                {
                    bool wasPaint = _boxSelectionPaint;
                    CancelBoxSelection();
                    if (!wasPaint)
                        _selectionMode = DecorationSelectionMode.Single;
                    InfoStore.Add(wasPaint
                        ? "Paint selection cancelled."
                        : "Box selection cancelled.");
                    return;
                }

                if (Input.GetMouseButton(0))
                {
                    UpdateBoxSelectionDrag(_lastMouseGui);
                    return;
                }

                CommitBoxSelectionDrag(_lastMouseGui);
                return;
            }

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
                    _generatorRotateDragSensitivityScale = 1f;
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
                    _scaleDragUniformGroup = false;
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

            if (Input.GetMouseButtonDown(1))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                if (TryOpenDecorationContextMenu())
                    return;

                if (_tool == DecorationEditorTool.Select &&
                    _selectionMode == DecorationSelectionMode.Box)
                {
                    CancelBoxSelection();
                    _selectionMode = DecorationSelectionMode.Single;
                    InfoStore.Add("Box select disabled.");
                    return;
                }

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
                if (TryHandleSelectionFocusBulkRequest())
                    return;

                BeginBoxSelection(_lastMouseGui, paint: false);
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
                TryProjectGizmo(_selectedConstruct, multiRotatePivot, out _rotateDragCenterScreen);
                _rotateDragStartVector = _lastMouseGui - _rotateDragCenterScreen;
                _rotateDragSensitivityScale = RotationDragSensitivityScale(
                    _selectedConstruct,
                    multiRotatePivot,
                    GroupTransformAxisVector(multiRotateAxis),
                    EsuGizmoSettings.Current.RotationRadius,
                    EsuGizmoSettings.BaseRotationRadius,
                    _lastMouseGui);
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
                Vector3 selectedRotateCenter = GetDecorationLocalCenter(_selected);
                TryProjectGizmo(_selectedConstruct, selectedRotateCenter, out _rotateDragCenterScreen);
                _rotateDragStartVector = _lastMouseGui - _rotateDragCenterScreen;
                _rotateDragSensitivityScale = RotationDragSensitivityScale(
                    _selectedConstruct,
                    selectedRotateCenter,
                    ActiveTransformAxisVector(_selected, rotateAxis),
                    EsuGizmoSettings.Current.RotationRadius,
                    EsuGizmoSettings.BaseRotationRadius,
                    _lastMouseGui);
                _rotateStart = _selected.Orientation.Us;
                _rotateStartQuaternion = Quaternion.Euler(_rotateStart);
                _rotateDragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _rotateDragSnapshotStart);
                _rotateDragSymmetryFollow = BeginSymmetryFollow(_rotateDragSnapshotStart, reportSkipped: true);
                return;
            }

            if (_tool == DecorationEditorTool.Scale &&
                TryGetMultiSelectionPivot(out Vector3 multiScalePivot) &&
                TryPickGroupUniformScaleHandle(multiScalePivot, _lastMouseGui) &&
                TryCaptureMultiTransformStart(out multiScalePivot))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _scaleDragAxis = DecorationEditAxis.Free;
                _scaleDragUniformGroup = true;
                _scaleDragMouseStart = _lastMouseGui;
                return;
            }

            if (_tool == DecorationEditorTool.Scale &&
                TryGetMultiSelectionPivot(out multiScalePivot) &&
                TryPickGroupHandle(multiScalePivot, _lastMouseGui, out DecorationEditAxis multiScaleAxis) &&
                TryCaptureMultiTransformStart(out multiScalePivot))
            {
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                _scaleDragAxis = multiScaleAxis;
                _scaleDragUniformGroup = false;
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
                _scaleDragUniformGroup = false;
                _scaleDragMouseStart = _lastMouseGui;
                _scaleStart = _selected.Scaling.Us;
                _scaleDragSnapshotStart = new DecorationEditSnapshot(_selected);
                _transactions.TrackEdit(_selected, _scaleDragSnapshotStart);
                _scaleDragSymmetryFollow = BeginSymmetryFollow(_scaleDragSnapshotStart, reportSkipped: true);
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
                BeginBoxSelection(_lastMouseGui, paint: true);
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
            SelectNearest(
                _lastMouseGui,
                additive: _selectionMode == DecorationSelectionMode.Single && IsShiftHeld());
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

                if (!TryProjectGizmo(_selectedConstruct, _multiTransformPivotStart, out Vector2 multiOrigin))
                    return;

                float moveLength = EsuGizmoSettings.Current.MoveLength;
                Vector3 multiAxisVector = GroupTransformAxisVector(_dragAxis);
                if (!TryProjectGizmo(_selectedConstruct, _multiTransformPivotStart + multiAxisVector * moveLength, out Vector2 multiAxisEnd))
                    return;
                if (!TryProjectGizmo(
                        _selectedConstruct,
                        _multiTransformPivotStart + multiAxisVector * EsuGizmoSettings.BaseMoveLength,
                        out Vector2 multiReferenceEnd) ||
                    !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                        mouseDelta,
                        multiOrigin,
                        multiAxisEnd,
                        multiReferenceEnd,
                        EsuGizmoSettings.BaseMoveLength,
                        out float multiAxisDelta))
                {
                    return;
                }
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

            if (!TryProjectGizmo(_selectedConstruct, GetDecorationLocalCenter(_selected), out Vector2 origin))
                return;

            float selectedMoveLength = EsuGizmoSettings.Current.MoveLength;
            Vector3 axisVector = ActiveTransformAxisVector(_dragAxis);
            Vector3 endLocal = GetDecorationLocalCenter(_selected) + axisVector * selectedMoveLength;
            if (!TryProjectGizmo(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;
            if (!TryProjectGizmo(
                    _selectedConstruct,
                    GetDecorationLocalCenter(_selected) + axisVector * EsuGizmoSettings.BaseMoveLength,
                    out Vector2 referenceEnd) ||
                !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    origin,
                    axisEnd,
                    referenceEnd,
                    EsuGizmoSettings.BaseMoveLength,
                    out float axisDelta))
            {
                return;
            }
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
                    Vector2.SignedAngle(_rotateDragStartVector, multiCurrent) *
                    _rotateDragSensitivityScale,
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
                Vector2.SignedAngle(_rotateDragStartVector, current) *
                _rotateDragSensitivityScale,
                DecorationRotateSnapDegrees);
            Vector3 next = _transformOrientation == DecorationTransformOrientation.Local
                ? (_rotateStartQuaternion * Quaternion.AngleAxis(degrees, axisVector)).eulerAngles
                : _rotateStart + axisVector * degrees;
            if (!DecorationEditMath.IsFinite(next))
                return;

            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(_rotateDragSymmetryFollow);
            _selected.Orientation.Us = next;
            _selected.Changed();
            _dirty = true;
            if (!TryApplySymmetryFollow(_rotateDragSymmetryFollow, reportInvalid: true))
                RestoreSymmetryEditState(selectedRollback, _rotateDragSymmetryFollow, targetRollback);
        }

        private void CommitRotateEdit()
        {
            if (MultiTransformActive)
            {
                RecordMultiTransformEdit("Rotate decorations");
                _rotateDragSensitivityScale = 1f;
                return;
            }

            DecorationEditSnapshot before = _rotateDragSnapshotStart;
            _rotateDragSnapshotStart = null;
            _rotateDragSensitivityScale = 1f;
            SymmetryFollowContext symmetryFollow = _rotateDragSymmetryFollow;
            _rotateDragSymmetryFollow = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Rotate decoration", before, symmetryFollow);
        }

        private void TryUpdateScale(Vector2 mouseDelta)
        {
            if (MultiTransformActive)
            {
                if (_scaleDragUniformGroup)
                {
                    float uniformFactor = UniformGroupScaleFactorFromMouseDelta(mouseDelta);
                    TryApplyMultiUniformScale(uniformFactor);
                    return;
                }

                if (!TryProjectGizmo(_selectedConstruct, _multiTransformPivotStart, out Vector2 multiOrigin))
                    return;

                float scaleLength = EsuGizmoSettings.Current.ScaleLength;
                Vector3 multiAxis = GroupTransformAxisVector(_scaleDragAxis);
                if (!TryProjectGizmo(_selectedConstruct, _multiTransformPivotStart + multiAxis * scaleLength, out Vector2 multiAxisEnd))
                    return;
                if (!TryProjectGizmo(
                        _selectedConstruct,
                        _multiTransformPivotStart + multiAxis * EsuGizmoSettings.BaseScaleLength,
                        out Vector2 multiReferenceEnd) ||
                    !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                        mouseDelta,
                        multiOrigin,
                        multiAxisEnd,
                        multiReferenceEnd,
                        EsuGizmoSettings.BaseScaleLength,
                        out float multiDelta))
                {
                    return;
                }
                float factor = Mathf.Max(
                    0f,
                    DecorationEditMath.Snap(1f + multiDelta, DecorationScaleSnap));
                TryApplyMultiScale(_scaleDragAxis, factor);
                return;
            }

            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (!TryProjectGizmo(_selectedConstruct, GetDecorationLocalCenter(_selected), out Vector2 origin))
                return;

            float selectedScaleLength = EsuGizmoSettings.Current.ScaleLength;
            Vector3 displayAxis = ActiveTransformAxisVector(_scaleDragAxis);
            Vector3 endLocal = GetDecorationLocalCenter(_selected) + displayAxis * selectedScaleLength;
            if (!TryProjectGizmo(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;
            if (!TryProjectGizmo(
                    _selectedConstruct,
                    GetDecorationLocalCenter(_selected) + displayAxis * EsuGizmoSettings.BaseScaleLength,
                    out Vector2 referenceEnd) ||
                !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    origin,
                    axisEnd,
                    referenceEnd,
                    EsuGizmoSettings.BaseScaleLength,
                    out float delta))
            {
                return;
            }
            delta = DecorationEditMath.Snap(delta, DecorationScaleSnap);
            Vector3 next = _scaleStart + DecorationEditMath.AxisVector(_scaleDragAxis) * delta;
            if (!IsValidScale(next))
                return;

            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(_scaleDragSymmetryFollow);
            DecorationScaleBounds.AllowExtendedScale(_selected);
            _selected.Scaling.Us = next;
            _selected.Changed();
            _dirty = true;
            if (!TryApplySymmetryFollow(_scaleDragSymmetryFollow, reportInvalid: true))
                RestoreSymmetryEditState(selectedRollback, _scaleDragSymmetryFollow, targetRollback);
        }

        private void CommitScaleEdit()
        {
            if (MultiTransformActive)
            {
                RecordMultiTransformEdit(_scaleDragUniformGroup ? "Uniform scale decorations" : "Scale decorations");
                _scaleDragUniformGroup = false;
                return;
            }

            DecorationEditSnapshot before = _scaleDragSnapshotStart;
            _scaleDragSnapshotStart = null;
            SymmetryFollowContext symmetryFollow = _scaleDragSymmetryFollow;
            _scaleDragSymmetryFollow = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Scale decoration", before, symmetryFollow);
        }

        private float UniformGroupScaleFactorFromMouseDelta(Vector2 mouseDelta)
        {
            float raw = 1f + (mouseDelta.x - mouseDelta.y) / Mathf.Max(1f, EsuHudLayout.Scale(120f));
            return Mathf.Max(
                0f,
                DecorationEditMath.Snap(raw, DecorationScaleSnap));
        }

        private void TryUpdateAnchorDrag(Vector2 mouseDelta)
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (!TryProjectGizmo(_selectedConstruct, ToVector3(_anchorDragBaseTether), out Vector2 origin))
                return;

            float anchorLength = EsuGizmoSettings.Current.AnchorLength;
            Vector3 axisVector = DecorationEditMath.AxisVector(_anchorDragAxis) * _anchorDragSign;
            Vector3 endLocal = ToVector3(_anchorDragBaseTether) + axisVector * anchorLength;
            if (!TryProjectGizmo(_selectedConstruct, endLocal, out Vector2 axisEnd))
                return;
            if (!TryProjectGizmo(
                    _selectedConstruct,
                    ToVector3(_anchorDragBaseTether) + axisVector * EsuGizmoSettings.BaseAnchorLength,
                    out Vector2 referenceEnd) ||
                !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    origin,
                    axisEnd,
                    referenceEnd,
                    EsuGizmoSettings.BaseAnchorLength,
                    out float projected))
            {
                return;
            }
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
            bool blockPaintDirty = HasPendingBlockPaintChanges();
            if (_selected == null || _selected.IsDeleted)
            {
                _dirty = _transactions.HasChanges ||
                         blockPaintDirty ||
                         _deletedDecorations.Count > 0;
                return;
            }

            _dirty = _transactions.HasChanges ||
                     blockPaintDirty ||
                     _deletedDecorations.Count > 0;
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

        private bool HasMultiSelectionForTransform() =>
            CurrentPrimarySelectionDecorations().Count > 1;

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
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
            {
                pivot = Vector3.zero;
                return false;
            }

            return TryResolveGroupPivotFromLiveSelection(decorations, out pivot);
        }

        private bool TryGetMultiSelectionBoundsPivot(out Vector3 pivot)
        {
            pivot = Vector3.zero;
            if (!TryGetMultiSelectionBounds(out Vector3 min, out Vector3 max))
                return false;

            pivot = (min + max) * 0.5f;
            return DecorationEditMath.IsFinite(pivot);
        }

        private bool TryGetMultiSelectionBounds(out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
                return false;

            for (int index = 0; index < decorations.Count; index++)
            {
                Vector3 center = GetDecorationLocalCenter(decorations[index]);
                if (!DecorationEditMath.IsFinite(center))
                    return false;

                if (index == 0)
                {
                    min = center;
                    max = center;
                }
                else
                {
                    min = Vector3.Min(min, center);
                    max = Vector3.Max(max, center);
                }
            }

            return DecorationEditMath.IsFinite(min) &&
                   DecorationEditMath.IsFinite(max);
        }

        private bool TryGetMultiSelectionAveragePivot(out Vector3 pivot)
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

        private bool TryResolveGroupPivotFromLiveSelection(
            List<Decoration> decorations,
            out Vector3 pivot)
        {
            pivot = Vector3.zero;
            if (decorations == null || decorations.Count <= 1)
                return false;

            switch (_groupPivotMode)
            {
                case DecorationGroupPivotMode.AverageCenter:
                    return TryGetMultiSelectionAveragePivot(out pivot);
                case DecorationGroupPivotMode.SelectedDecoration:
                    if (_selected != null &&
                        !_selected.IsDeleted &&
                        decorations.Contains(_selected))
                    {
                        pivot = GetDecorationLocalCenter(_selected);
                        return DecorationEditMath.IsFinite(pivot);
                    }
                    break;
                case DecorationGroupPivotMode.SelectedAnchor:
                    if (_selected != null &&
                        !_selected.IsDeleted &&
                        decorations.Contains(_selected))
                    {
                        pivot = ToVector3(_selected.TetherPoint.Us);
                        return DecorationEditMath.IsFinite(pivot);
                    }
                    break;
            }

            return TryGetMultiSelectionBoundsPivot(out pivot);
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
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                before[index] = new DecorationEditSnapshot(decoration);
                centers[index] = ToVector3(before[index].TetherPoint) + before[index].Positioning;
                if (!DecorationEditMath.IsFinite(centers[index]))
                    return false;
                sum += centers[index];

                if (index == 0)
                {
                    min = centers[index];
                    max = centers[index];
                }
                else
                {
                    min = Vector3.Min(min, centers[index]);
                    max = Vector3.Max(max, centers[index]);
                }
            }

            pivot = ResolveGroupPivotFromCapturedSelection(decorations, before, centers, min, max, sum);
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

        private Vector3 ResolveGroupPivotFromCapturedSelection(
            List<Decoration> decorations,
            DecorationEditSnapshot[] before,
            Vector3[] centers,
            Vector3 min,
            Vector3 max,
            Vector3 sum)
        {
            switch (_groupPivotMode)
            {
                case DecorationGroupPivotMode.AverageCenter:
                    return sum / centers.Length;
                case DecorationGroupPivotMode.SelectedDecoration:
                    if (TryGetSelectedDecorationIndex(decorations, out int selectedIndex))
                        return centers[selectedIndex];
                    break;
                case DecorationGroupPivotMode.SelectedAnchor:
                    if (TryGetSelectedDecorationIndex(decorations, out selectedIndex))
                        return ToVector3(before[selectedIndex].TetherPoint);
                    break;
            }

            return (min + max) * 0.5f;
        }

        private bool TryGetSelectedDecorationIndex(List<Decoration> decorations, out int selectedIndex)
        {
            selectedIndex = -1;
            if (decorations == null || _selected == null || _selected.IsDeleted)
                return false;

            for (int index = 0; index < decorations.Count; index++)
            {
                if (ReferenceEquals(decorations[index], _selected))
                {
                    selectedIndex = index;
                    return true;
                }
            }

            return false;
        }

        private void ClearMultiTransformState()
        {
            _multiTransformDecorations = Array.Empty<Decoration>();
            _multiTransformBefore = Array.Empty<DecorationEditSnapshot>();
            _multiTransformStartCenters = Array.Empty<Vector3>();
            _multiTransformPivotStart = Vector3.zero;
            _scaleDragUniformGroup = false;
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
                factor < 0f)
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

        private bool TryApplyMultiUniformScale(float factor)
        {
            if (!MultiTransformActive ||
                float.IsNaN(factor) ||
                float.IsInfinity(factor) ||
                factor < 0f)
            {
                return false;
            }

            var placements = new MultiTransformPlacement[_multiTransformDecorations.Length];
            var scales = new Vector3[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                Vector3 offset = _multiTransformStartCenters[index] - _multiTransformPivotStart;
                Vector3 center = _multiTransformPivotStart + offset * factor;
                scales[index] = _multiTransformBefore[index].Scaling * factor;
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

        private bool TryApplyMultiScaleFactors(Vector3 factors)
        {
            if (!MultiTransformActive || !IsValidGroupScaleFactors(factors))
                return false;

            Vector3 xAxis = GroupTransformAxisVector(DecorationEditAxis.X);
            Vector3 yAxis = GroupTransformAxisVector(DecorationEditAxis.Y);
            Vector3 zAxis = GroupTransformAxisVector(DecorationEditAxis.Z);
            if (!TryNormalizeBasis(ref xAxis, ref yAxis, ref zAxis))
                return false;

            var placements = new MultiTransformPlacement[_multiTransformDecorations.Length];
            var scales = new Vector3[_multiTransformDecorations.Length];
            for (int index = 0; index < _multiTransformDecorations.Length; index++)
            {
                Vector3 offset = _multiTransformStartCenters[index] - _multiTransformPivotStart;
                Vector3 center = _multiTransformPivotStart +
                                 xAxis * (Vector3.Dot(offset, xAxis) * factors.x) +
                                 yAxis * (Vector3.Dot(offset, yAxis) * factors.y) +
                                 zAxis * (Vector3.Dot(offset, zAxis) * factors.z);
                Vector3 sourceScale = _multiTransformBefore[index].Scaling;
                scales[index] = new Vector3(
                    sourceScale.x * factors.x,
                    sourceScale.y * factors.y,
                    sourceScale.z * factors.z);
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

        private static bool TryNormalizeBasis(ref Vector3 xAxis, ref Vector3 yAxis, ref Vector3 zAxis)
        {
            if (!DecorationEditMath.IsFinite(xAxis) ||
                !DecorationEditMath.IsFinite(yAxis) ||
                !DecorationEditMath.IsFinite(zAxis) ||
                xAxis.sqrMagnitude < 0.0001f ||
                yAxis.sqrMagnitude < 0.0001f ||
                zAxis.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            xAxis.Normalize();
            yAxis.Normalize();
            zAxis.Normalize();
            return true;
        }

        private static bool GroupScaleFactorsAreUniform(Vector3 factors) =>
            Mathf.Abs(factors.x - factors.y) <= 0.00001f &&
            Mathf.Abs(factors.x - factors.z) <= 0.00001f;

        private static bool IsValidGroupScaleFactors(Vector3 value) =>
            DecorationEditMath.IsFinite(value) &&
            IsValidGroupScaleFactor(value.x) &&
            IsValidGroupScaleFactor(value.y) &&
            IsValidGroupScaleFactor(value.z);

        private static bool IsValidGroupScaleFactor(float value) =>
            value >= 0f &&
            (value == 0f || Mathf.Abs(value) >= MinimumNonZeroScale);

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
                !TryProjectGizmo(construct, center, out Vector2 centerScreen))
            {
                return 1f;
            }

            BuildAxisBasis(axisVector, out Vector3 tangentA, out Vector3 tangentB);
            float bestDistance = float.PositiveInfinity;
            Vector3 bestDirection = tangentA;
            const int steps = 72;
            for (int step = 0; step < steps; step++)
            {
                float angle = step * Mathf.PI * 2f / steps;
                Vector3 direction = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                if (!TryProjectGizmo(
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
                !TryProjectGizmo(
                    construct,
                    center + bestDirection * visualRadius,
                    out Vector2 visualPoint) ||
                !TryProjectGizmo(
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    construct,
                    center,
                    candidate => ActiveTransformAxisVector(decoration, candidate),
                    style.ScaleLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false);
            axis = pick.Axis;
            return pick.IsHit;
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    construct,
                    center,
                    candidate => ActiveTransformAxisVector(decoration, candidate),
                    style.MoveLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: true);
            axis = pick.Axis;
            return pick.IsHit;
        }

        private bool TryPickGroupHandle(
            Vector3 center,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_selectedConstruct == null)
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    _selectedConstruct,
                    center,
                    GroupTransformAxisVector,
                    style.ScaleLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false);
            axis = pick.Axis;
            return pick.IsHit;
        }

        private bool TryPickGroupMoveHandle(
            Vector3 center,
            Vector2 mouse,
            out DecorationEditAxis axis)
        {
            axis = DecorationEditAxis.None;
            if (_selectedConstruct == null)
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    _selectedConstruct,
                    center,
                    GroupTransformAxisVector,
                    style.MoveLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: true);
            axis = pick.Axis;
            return pick.IsHit;
        }

        private bool TryPickGroupUniformScaleHandle(Vector3 center, Vector2 mouse)
        {
            if (_selectedConstruct == null)
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            return TryProjectGizmo(_selectedConstruct, center, out Vector2 origin) &&
                   Vector2.Distance(mouse, origin) <= style.HitAreaPixels;
        }

        private DecorationGizmoProjections ProjectPositiveGizmoAxes(
            AllConstruct construct,
            Vector3 center,
            Func<DecorationEditAxis, Vector3> axisVector,
            float length)
        {
            Vector2? origin = TryProjectNullable(construct, center);
            if (!origin.HasValue || axisVector == null)
                return new DecorationGizmoProjections(null, null, null, null);

            return new DecorationGizmoProjections(
                origin,
                TryProjectNullable(construct, center + axisVector(DecorationEditAxis.X) * length),
                TryProjectNullable(construct, center + axisVector(DecorationEditAxis.Y) * length),
                TryProjectNullable(construct, center + axisVector(DecorationEditAxis.Z) * length));
        }

        private DecorationGizmoProjections ProjectSignedGizmoAxes(
            AllConstruct construct,
            Vector3 center,
            Func<DecorationEditAxis, Vector3> axisVector,
            float length)
        {
            Vector2? origin = TryProjectNullable(construct, center);
            if (!origin.HasValue || axisVector == null)
                return new DecorationGizmoProjections(null, null, null, null);

            Vector3 x = axisVector(DecorationEditAxis.X) * length;
            Vector3 y = axisVector(DecorationEditAxis.Y) * length;
            Vector3 z = axisVector(DecorationEditAxis.Z) * length;
            return new DecorationGizmoProjections(
                origin,
                TryProjectNullable(construct, center + x),
                TryProjectNullable(construct, center - x),
                TryProjectNullable(construct, center + y),
                TryProjectNullable(construct, center - y),
                TryProjectNullable(construct, center + z),
                TryProjectNullable(construct, center - z));
        }

        private static Vector2? TryProjectNullable(AllConstruct construct, Vector3 local)
        {
            return TryProjectGizmo(construct, local, out Vector2 screen)
                ? (Vector2?)screen
                : null;
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
            if (!TryProjectGizmo(construct, center, out Vector2 origin))
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float best = style.HitAreaPixels;
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
                                    style.RotationRadius;
                    if (!TryProjectGizmo(construct, local, out Vector2 projected))
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectSignedGizmoAxes(
                    construct,
                    anchor,
                    DecorationEditMath.AxisVector,
                    style.AnchorLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false);
            axis = pick.Axis;
            sign = pick.Sign;
            return pick.IsHit;
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

        private void BeginBoxSelection(Vector2 mouse, bool paint)
        {
            _boxSelecting = true;
            _boxSelectionPaint = paint;
            _boxSelectionConstruct = ResolveBoxSelectionConstruct(mouse, paint);
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
            bool paint = _boxSelectionPaint;
            _boxSelecting = false;
            _boxSelectionPaint = false;
            _boxSelectCandidateCount = 0;
            try
            {
                if (rect.width < BoxSelectionClickThresholdPixels ||
                    rect.height < BoxSelectionClickThresholdPixels)
                {
                    if (paint)
                        PaintNearest(_boxSelectStartMouse);
                    else
                        SelectNearest(_boxSelectStartMouse);
                    return;
                }

                if (paint)
                    PaintBox(rect, _selectionXray);
                else
                    SelectBox(rect, _selectionXray);
            }
            finally
            {
                _boxSelectionConstruct = null;
            }
        }

        private void CancelBoxSelection()
        {
            _boxSelecting = false;
            _boxSelectionPaint = false;
            _boxSelectionConstruct = null;
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
            if (TryHandleSelectionFocusBulkRequest())
                return;

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

        private void PaintBox(Rect rect, bool xray)
        {
            AllConstruct construct = BoxSelectionPrimaryConstruct();
            if (construct == null)
            {
                InfoStore.Add("Paint selection needs a focused construct.");
                return;
            }

            List<DecorationHit> decorationHits = EnumerateBoxSelectionHits(
                    rect,
                    xray,
                    refresh: true)
                .ToList();
            try
            {
                if (!TryCollectBoxPaintBlocks(
                        rect,
                        construct,
                        xray,
                        out List<Block> blocks,
                        out string rejection))
                {
                    InfoStore.Add(rejection);
                    return;
                }

                PaintCollectedBoxTargets(decorationHits, blocks, construct);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint selection block enumeration failed",
                    exception,
                    LogOptions._AlertDevInGame);
                InfoStore.Add("Paint selection could not enumerate the construct blocks.");
            }
        }

        private void PaintCollectedBoxTargets(
            List<DecorationHit> decorationHits,
            List<Block> blocks,
            AllConstruct construct)
        {
            int color = Mathf.Clamp(_paintColor, 0, 31);
            var decorations = new List<Decoration>(decorationHits.Count);
            var seenDecorations = new HashSet<Decoration>();
            for (int index = 0; index < decorationHits.Count; index++)
            {
                Decoration decoration = decorationHits[index]?.Decoration;
                if (decoration != null &&
                    !decoration.IsDeleted &&
                    seenDecorations.Add(decoration))
                {
                    decorations.Add(decoration);
                }
            }

            if (decorations.Count == 0 && blocks.Count == 0)
            {
                InfoStore.Add("No decoration centers or block cells are inside the paint selection.");
                return;
            }

            Decoration primary = _selected;
            AllConstruct selectionConstruct = _selectedConstruct;
            Decoration[] historySelection;
            if (decorations.Count > 0)
            {
                DecorationHit primaryHit = decorationHits
                    .OrderBy(hit => (hit.ScreenPoint - _boxSelectStartMouse).sqrMagnitude)
                    .First();
                SetPrimarySelection(primaryHit.Decoration, primaryHit.Construct);
                _selection.Clear();
                for (int index = 0; index < decorations.Count; index++)
                    _selection.Add(decorations[index]);
                primary = primaryHit.Decoration;
                selectionConstruct = primaryHit.Construct;
                historySelection = decorations.ToArray();
                ResetInspectorFields();
            }
            else
            {
                historySelection = CapturePaintHistorySelection();
            }

            if (!TryApplyPaintSelection(
                    selectionConstruct,
                    decorations,
                    blocks,
                    color,
                    primary,
                    historySelection,
                    "Paint selection",
                    out int changedDecorations,
                    out int changedBlocks))
                return;

            InfoStore.Add(
                "Paint selection color #" + color.ToString(CultureInfo.InvariantCulture) +
                ": " + changedDecorations.ToString("N0", CultureInfo.InvariantCulture) +
                " decoration(s) and " + changedBlocks.ToString("N0", CultureInfo.InvariantCulture) +
                " block(s) changed.");
        }

        private bool TryCollectBoxPaintBlocks(
            Rect rect,
            AllConstruct construct,
            bool xray,
            out List<Block> blocks,
            out string rejection)
        {
            blocks = new List<Block>();
            rejection = null;
            var aliveAndDead = construct?.AllBasics?.AliveAndDead;
            if (aliveAndDead?.Blocks == null)
                return true;

            int declaredBlockCount = aliveAndDead.Count;
            if (!PaintScanWithinBudget(declaredBlockCount, 0))
            {
                rejection =
                    "Paint selection rejected before scanning: this construct has " +
                    declaredBlockCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " block entries; the safe scan limit is " +
                    MaxBoxPaintScanBlocks.ToString("N0", CultureInfo.InvariantCulture) +
                    ". Use direct paint or a smaller construct.";
                return false;
            }

            var seen = new HashSet<Block>();
            int scannedBlocks = 0;
            int scannedCells = 0;
            int visibilityRays = 0;
            foreach (Block block in aliveAndDead.Blocks)
            {
                scannedBlocks++;
                if (!PaintScanWithinBudget(scannedBlocks, scannedCells))
                {
                    rejection = PaintScanBudgetMessage();
                    return false;
                }

                if (block == null ||
                    block.IsDeleted ||
                    !block.IsAlive ||
                    !block.OnPlayerTeam ||
                    !seen.Add(block))
                {
                    continue;
                }

                Vector3i[] positions = block.LocalPositions;
                if (positions == null || positions.Length == 0)
                {
                    scannedCells++;
                    if (!PaintScanWithinBudget(scannedBlocks, scannedCells))
                    {
                        rejection = PaintScanBudgetMessage();
                        return false;
                    }

                    bool matches = BoxPaintCellMatches(
                        rect,
                        construct,
                        block,
                        block.LocalPosition,
                        xray,
                        ref visibilityRays,
                        out bool visibilityBudgetExceeded);
                    if (visibilityBudgetExceeded)
                    {
                        rejection = PaintScanBudgetMessage();
                        return false;
                    }
                    if (matches)
                        blocks.Add(block);
                }
                else
                {
                    for (int index = 0; index < positions.Length; index++)
                    {
                        scannedCells++;
                        if (!PaintScanWithinBudget(scannedBlocks, scannedCells))
                        {
                            rejection = PaintScanBudgetMessage();
                            return false;
                        }

                        bool matches = BoxPaintCellMatches(
                            rect,
                            construct,
                            block,
                            positions[index],
                            xray,
                            ref visibilityRays,
                            out bool visibilityBudgetExceeded);
                        if (visibilityBudgetExceeded)
                        {
                            rejection = PaintScanBudgetMessage();
                            return false;
                        }
                        if (!matches)
                            continue;
                        blocks.Add(block);
                        break;
                    }
                }

                if (blocks.Count > MaxBoxPaintBlocks)
                {
                    rejection =
                        "Paint selection rejected: more than " +
                        MaxBoxPaintBlocks.ToString("N0", CultureInfo.InvariantCulture) +
                        " blocks are inside the rectangle. Use a smaller selection.";
                    return false;
                }
            }

            return true;
        }

        private bool BoxPaintCellMatches(
            Rect rect,
            AllConstruct construct,
            Block block,
            Vector3i position,
            bool xray,
            ref int visibilityRayCount,
            out bool budgetExceeded)
        {
            budgetExceeded = false;
            if (!TryProject(construct, ToVector3(position), out Vector2 screen) ||
                !rect.Contains(screen))
            {
                return false;
            }

            if (xray)
                return true;

            visibilityRayCount++;
            if (!PaintScanWithinBudget(0, 0, visibilityRayCount))
            {
                budgetExceeded = true;
                return false;
            }

            return IsPaintBlockCellVisible(construct, block, position);
        }

        internal static bool PaintScanWithinBudget(
            int blockCount,
            int cellCount,
            int visibilityRayCount = 0) =>
            blockCount >= 0 &&
            cellCount >= 0 &&
            visibilityRayCount >= 0 &&
            blockCount <= MaxBoxPaintScanBlocks &&
            cellCount <= MaxBoxPaintScanCells &&
            visibilityRayCount <= MaxBoxPaintVisibilityRays;

        private static string PaintScanBudgetMessage() =>
            "Paint selection rejected before applying: scanning exceeded the safe " +
            MaxBoxPaintScanBlocks.ToString("N0", CultureInfo.InvariantCulture) +
            " block / " +
            MaxBoxPaintScanCells.ToString("N0", CultureInfo.InvariantCulture) +
            " cell / " +
            MaxBoxPaintVisibilityRays.ToString("N0", CultureInfo.InvariantCulture) +
            " visibility-ray budget. Use direct paint or a smaller construct.";

        private static bool IsPaintBlockCellVisible(
            AllConstruct construct,
            Block target,
            Vector3i localPosition)
        {
            Camera camera = Camera.main ?? Camera.current;
            if (construct == null || target == null || camera == null)
                return true;

            Vector3 world;
            try { world = construct.SafeLocalToGlobal(ToVector3(localPosition)); }
            catch { return false; }

            Vector3 origin = camera.transform.position;
            Vector3 toCell = world - origin;
            float distance = toCell.magnitude;
            if (!DecorationEditMath.IsFinite(toCell) ||
                distance <= BoxSelectionVisibilityTolerance)
            {
                return true;
            }

            Vector3 direction = toCell / distance;
            float rayDistance = Mathf.Max(0f, distance - BoxSelectionVisibilityTolerance);
            int hitCount;
            try
            {
                hitCount = Physics.RaycastNonAlloc(
                    new Ray(origin, direction),
                    s_boxPaintVisibilityHits,
                    rayDistance);
            }
            catch
            {
                return true;
            }

            Block nearestBlock = null;
            float nearestDistance = float.MaxValue;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = s_boxPaintVisibilityHits[index];
                Vector3 sampleWorld = hit.point + direction * 0.06f;
                if (!TryWorldToLocal(construct, sampleWorld, out Vector3 local))
                    continue;

                var cell = new Vector3i(
                    Mathf.RoundToInt(local.x),
                    Mathf.RoundToInt(local.y),
                    Mathf.RoundToInt(local.z));
                Block sampled = null;
                try { sampled = construct.AllBasics?.GetBlockViaLocalPosition(cell); }
                catch { }
                if (sampled == null || sampled.IsDeleted || !sampled.IsAlive)
                    continue;
                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestBlock = sampled;
                }
            }

            return nearestBlock == null || ReferenceEquals(nearestBlock, target);
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
            _boxSelectionConstruct ?? _selectedConstruct ?? FocusedConstruct();

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

        private void SelectNearest(Vector2 mouse, bool additive = false)
        {
            if (!TryFindNearestDecoration(mouse, out DecorationHit best))
            {
                InfoStore.Add("No decoration center near the cursor.");
                return;
            }

            if (TryHandleSelectionFocusRequest(best.Decoration, best.Construct))
                return;

            if (additive)
            {
                AddNearestToSelection(best);
                return;
            }

            if (TryPromoteSelectedDecorationInMultiSelection(best))
                return;

            Select(best.Decoration, best.Construct);
        }

        private bool TryPromoteSelectedDecorationInMultiSelection(DecorationHit hit)
        {
            if (hit?.Decoration == null ||
                hit.Decoration.IsDeleted ||
                hit.Construct == null ||
                !HasMultiSelectionForTransform() ||
                _selectedConstruct == null ||
                !ReferenceEquals(_selectedConstruct, hit.Construct) ||
                !_selection.Contains(hit.Decoration))
            {
                return false;
            }

            SetPrimarySelection(hit.Decoration, hit.Construct);
            if (_tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
            {
                _tool = DecorationEditorTool.Move;
            }
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
            return true;
        }

        private void AddNearestToSelection(DecorationHit hit)
        {
            if (hit?.Decoration == null ||
                hit.Decoration.IsDeleted ||
                hit.Construct == null)
            {
                InfoStore.Add("No decoration center near the cursor.");
                return;
            }

            if (TryHandleSelectionFocusRequest(hit.Decoration, hit.Construct))
                return;

            if (_selectedConstruct != null &&
                !ReferenceEquals(_selectedConstruct, hit.Construct))
            {
                Select(hit.Decoration, hit.Construct);
                InfoStore.Add("Selection moved to another construct.");
                return;
            }

            var previousSelection = _selection.ToList();
            SetPrimarySelection(hit.Decoration, hit.Construct);
            for (int index = 0; index < previousSelection.Count; index++)
            {
                Decoration decoration = previousSelection[index];
                if (decoration != null && !decoration.IsDeleted)
                    _selection.Add(decoration);
            }

            _selection.Add(hit.Decoration);
            ResetInspectorFields();
            InfoStore.Add(_selection.Count.ToString("N0", CultureInfo.InvariantCulture) + " decorations selected.");
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

            bool paintSelection =
                HasMultiSelectionForTransform() &&
                _selection.Contains(best.Decoration) &&
                ReferenceEquals(best.Construct, _selectedConstruct);
            int before = paintSelection
                ? CountSelectedDecorationsWithColor(_paintColor)
                : best.Decoration.Color.Us;
            if (paintSelection)
                SetPrimarySelection(best.Decoration, best.Construct);
            else
                Select(best.Decoration, best.Construct, notify: false);
            SetSelectedColor(_paintColor);
            if (paintSelection)
            {
                int selectedCount = CurrentPrimarySelectionDecorations().Count;
                if (before == selectedCount)
                    InfoStore.Add("Selected decorations are already color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
                else
                    InfoStore.Add("Painted " + selectedCount.ToString("N0", CultureInfo.InvariantCulture) + " selected decorations color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
            }
            else if (before == _paintColor)
            {
                InfoStore.Add("Decoration is already color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
            }
            else
            {
                InfoStore.Add("Painted decoration color #" + _paintColor.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private bool TryOpenDecorationContextMenu()
        {
            if (TryFindNearestDecoration(_lastMouseGui, out DecorationHit hit) &&
                hit.Decoration != null &&
                !hit.Decoration.IsDeleted &&
                hit.Construct != null)
            {
                return TryOpenDecorationContextMenuForTarget(
                    hit.Decoration,
                    hit.Construct);
            }

            if (!TryHitMultiSelectionContext(_lastMouseGui))
            {
                CloseDecorationContextMenu();
                return false;
            }

            return TryOpenDecorationContextMenuForTarget(
                _selected,
                _selectedConstruct);
        }

        private bool TryHitMultiSelectionContext(Vector2 mouse)
        {
            if (!HasMultiSelectionForTransform() ||
                _selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null)
            {
                return false;
            }

            if (TryGetMultiSelectionPivot(out Vector3 pivot) &&
                TryProjectGizmo(_selectedConstruct, pivot, out Vector2 pivotScreen) &&
                Vector2.Distance(mouse, pivotScreen) <= SelectionRadiusPixels * 1.5f)
            {
                return true;
            }

            if (!TryGetMultiSelectionBounds(out Vector3 min, out Vector3 max))
                return false;

            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
            bool projected = false;
            float xMin = float.PositiveInfinity;
            float yMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity;
            float yMax = float.NegativeInfinity;
            for (int index = 0; index < corners.Length; index++)
            {
                if (!TryProjectGizmo(
                        _selectedConstruct,
                        corners[index],
                        out Vector2 screen))
                {
                    continue;
                }

                projected = true;
                xMin = Mathf.Min(xMin, screen.x);
                yMin = Mathf.Min(yMin, screen.y);
                xMax = Mathf.Max(xMax, screen.x);
                yMax = Mathf.Max(yMax, screen.y);
            }

            if (!projected)
                return false;

            float padding = SelectionRadiusPixels;
            return Rect.MinMaxRect(
                    xMin - padding,
                    yMin - padding,
                    xMax + padding,
                    yMax + padding)
                .Contains(mouse);
        }

        private bool TryOpenDecorationContextMenuFromList(
            Rect rowRect,
            Decoration decoration,
            AllConstruct construct)
        {
            Event current = Event.current;
            if (!GUI.enabled ||
                current == null ||
                !(current.type == EventType.ContextClick ||
                  (current.type == EventType.MouseDown && current.button == 1)) ||
                !rowRect.Contains(current.mousePosition))
            {
                return false;
            }

            _lastMouseGui = GUIUtility.GUIToScreenPoint(current.mousePosition);
            bool opened = TryOpenDecorationContextMenuForTarget(
                decoration,
                construct);
            current.Use();
            return opened;
        }

        private bool TryOpenDecorationContextMenuForTarget(
            Decoration decoration,
            AllConstruct construct)
        {
            if (decoration == null || decoration.IsDeleted || construct == null)
            {
                CloseDecorationContextMenu();
                return false;
            }

            if (TryBlockSelectionFocusContextTarget(decoration, construct))
            {
                CloseDecorationContextMenu();
                return true;
            }

            bool preserveSelection =
                _selectedConstruct != null &&
                ReferenceEquals(_selectedConstruct, construct) &&
                _selection.Contains(decoration);
            _decorationContextTarget = decoration;
            _decorationContextConstruct = construct;
            _decorationContextPreserveSelection = preserveSelection;
            SelectDecorationContextTarget(
                notify: false,
                preserveSelectedGroup: preserveSelection);
            _decorationContextRect = DecorationContextRect(
                DecorationContextButtonCount());
            return true;
        }

        private int DecorationContextButtonCount()
        {
            bool multiple = CurrentPrimarySelectionDecorations().Count > 1;
            return multiple ? 9 : 11;
        }

        private Rect DecorationContextRect(int buttonCount)
        {
            Vector2 mouse = _lastMouseGui;
            float width = EsuHudLayout.Scale(184f);
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
            bool multiple = CurrentPrimarySelectionDecorations().Count > 1;

            GUI.Label(
                new Rect(contentRect.x, y, contentRect.width, headerHeight),
                multiple
                    ? CurrentPrimarySelectionDecorations().Count.ToString(
                        "N0",
                        CultureInfo.InvariantCulture) + " decorations"
                    : "Decoration",
                DecorationEditorTheme.SubHeader);
            y += headerHeight + rowGap;

            if (DrawDecorationContextButton(
                    contentRect,
                    ref y,
                    rowHeight,
                    rowGap,
                    multiple ? "Select only this" : "Select",
                    multiple
                        ? "Replace the current group with only this decoration."
                        : "Select this decoration."))
                action = DecorationContextAction.Select;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Move", "Keep this selection and switch to Move."))
                action = DecorationContextAction.Move;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Rotate", "Keep this selection and switch to Rotate."))
                action = DecorationContextAction.Rotate;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Scale", "Keep this selection and switch to Scale."))
                action = DecorationContextAction.Scale;
            if (!multiple &&
                DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Anchor", "Select this decoration and switch to Anchor."))
                action = DecorationContextAction.Anchor;
            bool focusActive = HasActiveSelectionFocusLock();
            if (DrawDecorationContextButton(
                    contentRect,
                    ref y,
                    rowHeight,
                    rowGap,
                    "Focus deco",
                    focusActive
                        ? "Disable the current decoration focus lock."
                        : "Lock selection to this decoration.",
                    focusActive))
            {
                action = DecorationContextAction.FocusDecoration;
            }
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Copy settings", "Copy settings from the primary decoration to FTD's native clipboard."))
                action = DecorationContextAction.CopySettings;
            if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, "Paste settings", "Atomically paste native settings onto the complete selection."))
                action = DecorationContextAction.PasteSettings;
            if (DrawDecorationContextButton(
                    contentRect,
                    ref y,
                    rowHeight,
                    rowGap,
                    multiple ? "Duplicate selection" : "Duplicate in place",
                    multiple
                        ? "Create exact in-place copies of the selected decorations as one undoable operation."
                        : "Create an exact in-place copy without replacing either clipboard."))
                action = DecorationContextAction.Duplicate;

            if (!multiple)
            {
                string meshLabel = IsSelectedOriginalMeshHidden()
                    ? "Show anchor mesh"
                    : "Hide anchor mesh";
                if (DrawDecorationContextButton(contentRect, ref y, rowHeight, rowGap, meshLabel, "Toggle the selected decoration's anchor block mesh."))
                    action = DecorationContextAction.ToggleAnchorMesh;
            }

            if (DrawDecorationContextButton(
                    contentRect,
                    ref y,
                    rowHeight,
                    rowGap,
                    multiple ? "Delete selection" : "Delete",
                    multiple
                        ? "Delete the selected decorations as one undoable operation."
                        : "Delete this decoration. Undo can restore it."))
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
            string tooltip,
            bool active = false)
        {
            var buttonRect = new Rect(contentRect.x, y, contentRect.width, rowHeight);
            y += rowHeight + rowGap;
            return GUI.Button(
                buttonRect,
                new GUIContent(label, tooltip),
                active
                    ? DecorationEditorTheme.ToolButton(true)
                    : DecorationEditorTheme.Button);
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
                    SelectDecorationContextTarget(
                        notify: true,
                        preserveSelectedGroup: false);
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
                case DecorationContextAction.FocusDecoration:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: false);
                    ToggleSelectionFocusLock();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.CopySettings:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: true);
                    CopyDecorationSettings();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.PasteSettings:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: true);
                    PasteDecorationSettings();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.Duplicate:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: true);
                    DuplicateSelectedDecoration();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.ToggleAnchorMesh:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: false);
                    ToggleSelectedOriginalMeshVisibility();
                    CloseDecorationContextMenu();
                    break;
                case DecorationContextAction.Delete:
                    SelectDecorationContextTarget(
                        notify: false,
                        preserveSelectedGroup: true);
                    DeleteSelectedDecoration();
                    CloseDecorationContextMenu();
                    break;
            }
        }

        private void SelectContextTool(DecorationEditorTool tool)
        {
            SelectDecorationContextTarget(
                notify: false,
                preserveSelectedGroup: true);
            SetActiveTool(tool);
            CloseDecorationContextMenu();
        }

        private bool DecorationContextTargetValid()
        {
            if (_decorationContextTarget == null ||
                _decorationContextTarget.IsDeleted ||
                _decorationContextConstruct == null)
            {
                return false;
            }

            try
            {
                var manager = _decorationContextConstruct.Decorations as AllConstructDecorations;
                return manager != null &&
                       ReferenceEquals(_decorationContextTarget.OurManager, manager) &&
                       TryIsDecorationInManager(
                           manager,
                           _decorationContextTarget,
                           out bool isMember) &&
                       isMember;
            }
            catch
            {
                return false;
            }
        }

        private void SelectDecorationContextTarget(
            bool notify,
            bool preserveSelectedGroup)
        {
            if (!DecorationContextTargetValid())
                return;

            if (TryBlockSelectionFocusContextTarget(_decorationContextTarget, _decorationContextConstruct))
                return;

            bool keepGroup = preserveSelectedGroup &&
                             _decorationContextPreserveSelection &&
                             _selectedConstruct != null &&
                             ReferenceEquals(
                                 _selectedConstruct,
                                 _decorationContextConstruct) &&
                             _selection.Contains(_decorationContextTarget);
            SetPrimarySelection(_decorationContextTarget, _decorationContextConstruct);
            if (!keepGroup)
                _selection.Clear();
            _selection.Add(_decorationContextTarget);
            ResetInspectorFields();
            if (notify)
            {
                InfoStore.Add(keepGroup
                    ? _selection.Count.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations selected."
                    : "Decoration selected.");
            }
        }

        private bool IsDecorationContextMenuAt(Vector2 mouse) =>
            _decorationContextTarget != null &&
            _decorationContextRect.Contains(mouse);

        private void CloseDecorationContextMenu()
        {
            _decorationContextTarget = null;
            _decorationContextConstruct = null;
            _decorationContextRect = Rect.zero;
            _decorationContextPreserveSelection = false;
        }

        private Decoration[] CapturePaintHistorySelection()
        {
            var selection = new List<Decoration>(_selection.Count + 1);
            foreach (Decoration decoration in _selection)
            {
                if (decoration != null &&
                    !decoration.IsDeleted &&
                    !selection.Contains(decoration))
                {
                    selection.Add(decoration);
                }
            }

            if (_selected != null &&
                !_selected.IsDeleted &&
                !selection.Contains(_selected))
            {
                selection.Add(_selected);
            }

            return selection.ToArray();
        }

        private bool TryApplyPaintSelection(
            AllConstruct selectionConstruct,
            IReadOnlyList<Decoration> decorations,
            IReadOnlyList<Block> blocks,
            int color,
            Decoration primary,
            IReadOnlyList<Decoration> historySelection,
            string label,
            out int changedDecorationCount,
            out int changedBlockCount)
        {
            color = Mathf.Clamp(color, 0, 31);
            changedDecorationCount = 0;
            changedBlockCount = 0;

            var changedDecorations = new List<Decoration>();
            var decorationBefore = new List<DecorationEditSnapshot>();
            var seenDecorations = new HashSet<Decoration>();
            if (decorations != null)
            {
                for (int index = 0; index < decorations.Count; index++)
                {
                    Decoration decoration = decorations[index];
                    if (decoration == null ||
                        decoration.IsDeleted ||
                        decoration.Color.Us == color ||
                        !seenDecorations.Add(decoration))
                    {
                        continue;
                    }

                    changedDecorations.Add(decoration);
                    decorationBefore.Add(new DecorationEditSnapshot(decoration));
                }
            }

            var changedBlocks = new List<Block>();
            var blockBefore = new List<int>();
            var seenBlocks = new HashSet<Block>();
            if (blocks != null)
            {
                for (int index = 0; index < blocks.Count; index++)
                {
                    Block block = blocks[index];
                    if (block == null ||
                        block.IsDeleted ||
                        !block.IsAlive ||
                        !block.OnPlayerTeam ||
                        block.color == color ||
                        !seenBlocks.Add(block))
                    {
                        continue;
                    }

                    changedBlocks.Add(block);
                    blockBefore.Add(block.color);
                }
            }

            changedDecorationCount = changedDecorations.Count;
            changedBlockCount = changedBlocks.Count;
            if (changedDecorationCount == 0 && changedBlockCount == 0)
                return true;

            DecorationEditSnapshot[] decorationAfter;
            PaintSelectionHistoryCommand historyCommand;
            try
            {
                for (int index = 0; index < changedDecorations.Count; index++)
                {
                    Decoration decoration = changedDecorations[index];
                    decoration.Color.Us = color;
                    decoration.Changed();
                }

                for (int index = 0; index < changedBlocks.Count; index++)
                {
                    if (!PaintBlock(changedBlocks[index], color))
                        throw new InvalidOperationException("FTD rejected a block paint operation.");
                }

                decorationAfter = new DecorationEditSnapshot[changedDecorations.Count];
                for (int index = 0; index < changedDecorations.Count; index++)
                    decorationAfter[index] = new DecorationEditSnapshot(changedDecorations[index]);

                Decoration[] savedSelection = historySelection == null
                    ? Array.Empty<Decoration>()
                    : historySelection
                        .Where(decoration => decoration != null && !decoration.IsDeleted)
                        .Distinct()
                        .ToArray();
                historyCommand = new PaintSelectionHistoryCommand(
                    label,
                    selectionConstruct,
                    changedDecorations.ToArray(),
                    decorationBefore.ToArray(),
                    decorationAfter,
                    changedBlocks.ToArray(),
                    blockBefore.ToArray(),
                    Enumerable.Repeat(color, changedBlocks.Count).ToArray(),
                    savedSelection,
                    primary);
            }
            catch (Exception exception)
            {
                bool rollbackOk = RollbackPaintSelectionMutation(
                    changedDecorations,
                    decorationBefore,
                    changedBlocks,
                    blockBefore);

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Atomic paint selection failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? label + " failed; every paint target was rolled back."
                    : label + " failed and rollback was incomplete; see the log.");
                changedDecorationCount = 0;
                changedBlockCount = 0;
                return false;
            }

            for (int index = 0; index < changedDecorations.Count; index++)
                _transactions.TrackEdit(changedDecorations[index], decorationBefore[index]);

            for (int index = 0; index < changedBlocks.Count; index++)
            {
                if (!_blockPaintOriginalColors.ContainsKey(changedBlocks[index]))
                    _blockPaintOriginalColors.Add(changedBlocks[index], blockBefore[index]);
            }

            _history.Record(historyCommand);

            _dirty = true;
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return true;
        }

        private static bool RollbackPaintSelectionMutation(
            IReadOnlyList<Decoration> decorations,
            IReadOnlyList<DecorationEditSnapshot> decorationSnapshots,
            IReadOnlyList<Block> blocks,
            IReadOnlyList<int> blockColors)
        {
            bool rollbackOk = true;
            for (int index = blocks.Count - 1; index >= 0; index--)
            {
                if (!PaintBlock(blocks[index], blockColors[index]))
                    rollbackOk = false;
            }

            for (int index = decorations.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (!decorationSnapshots[index].TryRestore(decorations[index]) ||
                        !decorationSnapshots[index].Matches(decorations[index]))
                    {
                        rollbackOk = false;
                    }
                }
                catch
                {
                    rollbackOk = false;
                }
            }

            return rollbackOk;
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

            if (!TryApplyPaintSelection(
                    _selectedConstruct,
                    Array.Empty<Decoration>(),
                    new[] { block },
                    color,
                    _selected,
                    CapturePaintHistorySelection(),
                    "Paint block",
                    out int _,
                    out int changedBlocks))
            {
                message = "Block paint failed and was rolled back.";
                return true;
            }

            message = changedBlocks > 0
                ? "Painted block color #" + color.ToString(CultureInfo.InvariantCulture) + "."
                : "Block is already color #" + color.ToString(CultureInfo.InvariantCulture) + ".";
            return true;
        }

        private static bool PaintBlock(Block block, int color)
        {
            if (block == null || block.IsDeleted)
                return false;

            int requested = Mathf.Clamp(color, 0, 31);
            if (block.color == requested)
                return true;

            int rollbackColor = block.color;
            try
            {
                block.SetColor(requested);
                if (block.color != requested)
                    throw new InvalidOperationException("FTD did not retain the requested block color.");
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint tool block mutation failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                return false;
            }

            if (SendPaintBlockRpc(block, requested))
                return true;

            try
            {
                block.SetColor(rollbackColor);
                SendPaintBlockRpc(block, rollbackColor);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint tool block RPC rollback failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }

            return false;
        }

        private static bool SendPaintBlockRpc(Block block, int color)
        {
            try
            {
                var constructable = block.GetConstructableOrSubConstructable();
                if (constructable?.NetworkIdentity == null)
                    return true;

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

                return true;
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint tool block RPC failed",
                    exception,
                    LogOptions._AlertDevInGame);
                return false;
            }
        }

        private bool HasPendingBlockPaintChanges()
        {
            foreach (KeyValuePair<Block, int> pair in _blockPaintOriginalColors)
            {
                Block block = pair.Key;
                if (block != null &&
                    !block.IsDeleted &&
                    block.color != pair.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private void RestoreOriginalBlockPaintsForCancel()
        {
            int failures = 0;
            foreach (KeyValuePair<Block, int> pair in _blockPaintOriginalColors.ToArray())
            {
                Block block = pair.Key;
                if (block == null || block.IsDeleted || block.color == pair.Value)
                    continue;
                if (!PaintBlock(block, pair.Value))
                    failures++;
            }

            _blockPaintOriginalColors.Clear();
            if (failures > 0)
            {
                InfoStore.Add(
                    "Decoration Edit cancel could not restore " +
                    failures.ToString("N0", CultureInfo.InvariantCulture) +
                    " painted block(s); see the log.");
            }
        }

        private bool TryRestorePaintSelectionHistory(
            AllConstruct selectionConstruct,
            Decoration[] decorations,
            DecorationEditSnapshot[] desiredDecorationSnapshots,
            Block[] blocks,
            int[] desiredBlockColors,
            Decoration[] selection,
            Decoration primary,
            string context)
        {
            decorations ??= Array.Empty<Decoration>();
            desiredDecorationSnapshots ??= Array.Empty<DecorationEditSnapshot>();
            blocks ??= Array.Empty<Block>();
            desiredBlockColors ??= Array.Empty<int>();
            if (decorations.Length != desiredDecorationSnapshots.Length ||
                blocks.Length != desiredBlockColors.Length ||
                decorations.Length + blocks.Length == 0)
            {
                InfoStore.Add(context + ": paint history is incomplete.");
                return false;
            }

            var decorationRollback = new DecorationEditSnapshot[decorations.Length];
            var blockRollback = new int[blocks.Length];
            Decoration[] selectionRollback = CapturePaintHistorySelection();
            Decoration primaryRollback = _selected;
            AllConstruct selectionConstructRollback = _selectedConstruct;
            try
            {
                for (int index = 0; index < decorations.Length; index++)
                {
                    if (decorations[index] == null ||
                        decorations[index].IsDeleted ||
                        desiredDecorationSnapshots[index] == null)
                    {
                        InfoStore.Add(context + ": a painted decoration no longer exists.");
                        return false;
                    }

                    decorationRollback[index] = new DecorationEditSnapshot(decorations[index]);
                }

                for (int index = 0; index < blocks.Length; index++)
                {
                    if (blocks[index] == null ||
                        blocks[index].IsDeleted ||
                        !blocks[index].IsAlive ||
                        !blocks[index].OnPlayerTeam)
                    {
                        InfoStore.Add(context + ": a painted block is no longer editable.");
                        return false;
                    }

                    blockRollback[index] = blocks[index].color;
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Paint history preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(context + ": current paint state could not be captured safely.");
                return false;
            }

            try
            {
                for (int index = 0; index < decorations.Length; index++)
                {
                    if (!desiredDecorationSnapshots[index].TryRestore(decorations[index]) ||
                        !desiredDecorationSnapshots[index].Matches(decorations[index]))
                    {
                        throw new InvalidOperationException(
                            "FTD rejected a decoration paint snapshot restore.");
                    }
                }

                for (int index = 0; index < blocks.Length; index++)
                {
                    if (!PaintBlock(blocks[index], desiredBlockColors[index]))
                        throw new InvalidOperationException("FTD rejected a block paint history restore.");
                }

                RestorePaintHistorySelection(selectionConstruct, selection, primary);
            }
            catch (Exception exception)
            {
                bool rollbackOk = true;
                for (int index = blocks.Length - 1; index >= 0; index--)
                {
                    if (blocks[index] != null &&
                        !blocks[index].IsDeleted &&
                        !PaintBlock(blocks[index], blockRollback[index]))
                    {
                        rollbackOk = false;
                    }
                }

                for (int index = decorations.Length - 1; index >= 0; index--)
                {
                    try
                    {
                        if (!decorationRollback[index].TryRestore(decorations[index]) ||
                            !decorationRollback[index].Matches(decorations[index]))
                        {
                            rollbackOk = false;
                        }
                    }
                    catch
                    {
                        rollbackOk = false;
                    }
                }

                try
                {
                    RestorePaintHistorySelection(
                        selectionConstructRollback,
                        selectionRollback,
                        primaryRollback);
                }
                catch
                {
                    rollbackOk = false;
                }

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Atomic paint history restore failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? context + ": restore failed; every paint target was rolled back."
                    : context + ": restore failed and rollback was incomplete; see the log.");
                return false;
            }

            return true;
        }

        private void RestorePaintHistorySelection(
            AllConstruct construct,
            Decoration[] selection,
            Decoration primary)
        {
            selection ??= Array.Empty<Decoration>();
            Decoration resolvedPrimary = primary != null && !primary.IsDeleted
                ? primary
                : selection.FirstOrDefault(decoration => decoration != null && !decoration.IsDeleted);
            if (resolvedPrimary == null || construct == null)
            {
                _selection.Clear();
                _selected = null;
                _selectedConstruct = null;
                _snapshot = null;
                _selectedCreatedInSession = false;
                return;
            }

            SetPrimarySelection(resolvedPrimary, construct);
            _selection.Clear();
            for (int index = 0; index < selection.Length; index++)
            {
                Decoration decoration = selection[index];
                if (decoration != null && !decoration.IsDeleted)
                    _selection.Add(decoration);
            }

            _selection.Add(resolvedPrimary);
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

            if (TryHandleSelectionFocusRequest(decoration, construct))
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
            _dirty = _transactions.HasChanges ||
                     HasPendingBlockPaintChanges() ||
                     _deletedDecorations.Count > 0;
            _dragAxis = DecorationEditAxis.None;
            _dragSnapshotStart = null;
            _dragSymmetryFollow = null;
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragUniformGroup = false;
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

            if (_selectionFocusLocked)
            {
                InfoStore.Add("Disable Focus deco before duplicating in place so the new result can be selected.");
                return;
            }

            if (!DecorationSelectionClipboardPayload.TryCreate(
                    _selectedConstruct,
                    _selected,
                    CurrentPrimarySelectionDecorations(),
                    out DecorationSelectionClipboardPayload payload,
                    out string failure))
            {
                InfoStore.Add("Duplicate selection was not started: " + failure);
                return;
            }

            DecorationEditSnapshot[] snapshots = payload.CopySnapshots();

            PasteDecorationSnapshotsInPlace(
                _selectedConstruct,
                snapshots,
                payload.PrimaryIndex,
                payload.Count == 1
                    ? "Duplicate in place"
                    : "Duplicate selection in place",
                payload.Count == 1
                    ? "Decoration duplicated in place."
                    : payload.Count.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations duplicated in place.");
        }

        private void CopyDecorationSettings()
        {
            if (!CanUseDecorationClipboard(
                    wholeSelection: false,
                    requireSelection: true,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            uint uniqueId = _selected.UniqueId;
            try
            {
                try
                {
                    _selected.Copy();
                }
                finally
                {
                    _selected.UniqueId = uniqueId;
                }
                InfoStore.Add("Copied decoration settings to the native FTD clipboard.");
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Native decoration settings copy failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Decoration settings copy failed; see the log.");
            }
        }

        private void PasteDecorationSettings()
        {
            if (!CanUseDecorationClipboard(
                    wholeSelection: false,
                    requireSelection: true,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            List<Decoration> targets = CurrentPrimarySelectionDecorations();
            if (targets.Count == 0)
            {
                InfoStore.Add("Select at least one decoration before pasting settings.");
                return;
            }

            var manager = _selectedConstruct?.Decorations as AllConstructDecorations;
            var before = new DecorationEditSnapshot[targets.Count];
            var uniqueIds = new uint[targets.Count];
            try
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    Decoration target = targets[index];
                    if (target == null ||
                        target.IsDeleted ||
                        manager == null ||
                        !ReferenceEquals(target.OurManager, manager) ||
                        !CopyPaster.ReadyToPaste(target))
                    {
                        InfoStore.Add(
                            "Settings paste was not started because a target is stale, on another manager, or incompatible with the native clipboard.");
                        return;
                    }

                    before[index] = new DecorationEditSnapshot(target);
                    uniqueIds[index] = target.UniqueId;
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Native decoration settings preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Settings paste was not started because native clipboard validation failed.");
                return;
            }

            try
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    Decoration target = targets[index];
                    Vector3i tether = target.TetherPoint.Us;
                    AllConstructDecorations targetManager = target.OurManager;
                    target.UniqueId = 0;
                    try
                    {
                        CopyPaster.Paste(target);
                    }
                    finally
                    {
                        target.UniqueId = uniqueIds[index];
                    }
                    var pasted = new DecorationEditSnapshot(target);
                    if (target.UniqueId != uniqueIds[index] ||
                        !ReferenceEquals(target.OurManager, targetManager) ||
                        !SameTether(target.TetherPoint.Us, tether) ||
                        !pasted.HasFiniteTransform ||
                        !IsValidScale(pasted.Scaling) ||
                        pasted.Color < 0 ||
                        pasted.Color > 31)
                    {
                        throw new InvalidOperationException(
                            "The native clipboard returned an invalid payload or changed protected identity data.");
                    }
                }
            }
            catch (Exception exception)
            {
                bool rollbackOk = RestoreSettingsPasteTargets(
                    targets,
                    before,
                    uniqueIds,
                    manager);
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Native decoration settings paste failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? "Settings paste failed; every target was restored."
                    : "Settings paste failed and rollback was incomplete; see the log.");
                return;
            }

            var changedDecorations = new List<Decoration>();
            var changedBefore = new List<DecorationEditSnapshot>();
            var changedAfter = new List<DecorationEditSnapshot>();
            int primaryIndex = 0;
            for (int index = 0; index < targets.Count; index++)
            {
                if (before[index].Matches(targets[index]))
                    continue;

                if (ReferenceEquals(targets[index], _selected))
                    primaryIndex = changedDecorations.Count;
                _transactions.TrackEdit(targets[index], before[index]);
                changedDecorations.Add(targets[index]);
                changedBefore.Add(before[index]);
                changedAfter.Add(new DecorationEditSnapshot(targets[index]));
            }

            if (changedDecorations.Count == 0)
            {
                InfoStore.Add("The native settings clipboard already matches the selection.");
                return;
            }

            if (changedDecorations.Count == 1)
            {
                _history.Record(new DecorationSnapshotCommand(
                    "Paste decoration settings",
                    _selectedConstruct,
                    changedDecorations[0],
                    changedBefore[0],
                    changedAfter[0]));
            }
            else
            {
                _history.Record(new DecorationSnapshotBatchCommand(
                    "Paste decoration settings",
                    _selectedConstruct,
                    changedDecorations.ToArray(),
                    changedBefore.ToArray(),
                    changedAfter.ToArray(),
                    primaryIndex));
            }

            UpdateDirtyFromSelection();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add(
                "Pasted native settings onto " +
                changedDecorations.Count.ToString("N0", CultureInfo.InvariantCulture) +
                (changedDecorations.Count == 1 ? " decoration." : " decorations."));
        }

        private bool RestoreSettingsPasteTargets(
            IReadOnlyList<Decoration> targets,
            IReadOnlyList<DecorationEditSnapshot> before,
            IReadOnlyList<uint> uniqueIds,
            AllConstructDecorations manager)
        {
            bool ok = true;
            for (int index = targets.Count - 1; index >= 0; index--)
            {
                Decoration target = targets[index];
                if (target == null || target.IsDeleted)
                {
                    ok = false;
                    continue;
                }

                try
                {
                    target.UniqueId = uniqueIds[index];
                    if (!before[index].TryRestore(target) ||
                        target.UniqueId != uniqueIds[index] ||
                        !ReferenceEquals(target.OurManager, manager) ||
                        !TryIsDecorationInManager(manager, target, out bool isMember) ||
                        !isMember ||
                        !before[index].Matches(target))
                    {
                        ok = false;
                    }
                }
                catch
                {
                    ok = false;
                }
            }

            return ok;
        }

        private void CopyDecorationSelection()
        {
            TryCopyDecorationSelection();
        }

        private bool TryCopyDecorationSelection()
        {
            if (!CanUseDecorationClipboard(
                    wholeSelection: true,
                    requireSelection: true,
                    out string reason))
            {
                InfoStore.Add(reason);
                return false;
            }

            try
            {
                bool copied = DecorationSelectionClipboard.TryCopy(
                    _selectedConstruct,
                    _selected,
                    CurrentPrimarySelectionDecorations(),
                    out string message);
                InfoStore.Add(message);
                return copied;
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration selection copy failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Copy selection failed safely because the selection changed or unloaded.");
                return false;
            }
        }

        private void PasteDecorationSelectionInPlace()
        {
            if (!CanUseDecorationClipboard(
                    wholeSelection: true,
                    requireSelection: false,
                    out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            AllConstruct construct = _selected != null &&
                                     !_selected.IsDeleted &&
                                     _selectedConstruct != null
                ? _selectedConstruct
                : FocusedConstruct();
            if (!DecorationSelectionClipboard.TryGetFor(
                    construct,
                    out DecorationSelectionClipboardPayload payload,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }

            PasteDecorationSnapshotsInPlace(
                construct,
                payload.CopySnapshots(),
                payload.PrimaryIndex,
                "Paste decoration selection",
                payload.Count == 1
                    ? "Pasted 1 decoration in place."
                    : "Pasted " + payload.Count.ToString("N0", CultureInfo.InvariantCulture) +
                      " decorations in place.");
        }

        private bool PasteDecorationSnapshotsInPlace(
            AllConstruct construct,
            DecorationEditSnapshot[] snapshots,
            int primaryIndex,
            string historyLabel,
            string successMessage)
        {
            var manager = construct?.Decorations as AllConstructDecorations;
            if (!TryPreflightDecorationSnapshotBatch(
                    construct,
                    manager,
                    snapshots,
                    historyLabel,
                    out string failure))
            {
                InfoStore.Add(failure);
                return false;
            }

            var created = new Decoration[snapshots.Length];
            var createdIndexes = new List<int>(snapshots.Length);
            try
            {
                for (int index = 0; index < snapshots.Length; index++)
                {
                    Decoration decoration = manager.NewDecoration(
                        snapshots[index].TetherPoint,
                        force: true,
                        playSound: false,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a copied decoration.");

                    created[index] = decoration;
                    createdIndexes.Add(index);
                    if (!snapshots[index].TryRestore(decoration) ||
                        !snapshots[index].Matches(decoration))
                    {
                        throw new InvalidOperationException("FTD rejected a copied decoration.");
                    }
                }
            }
            catch (Exception exception)
            {
                bool cleanupOk = CleanupCreatedDecorationBatch(
                    created,
                    createdIndexes,
                    historyLabel + " cleanup");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration paste-in-place failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(cleanupOk
                    ? historyLabel + " failed; no clones were kept."
                    : historyLabel + " failed and cleanup was incomplete; see the log.");
                return false;
            }

            for (int index = 0; index < created.Length; index++)
                _transactions.MarkCreated(created[index]);
            SelectDecorationGroup(created, construct, primaryIndex, createdInSession: true);
            _history.Record(new DecorationCreateBatchCommand(
                historyLabel,
                construct,
                created,
                snapshots,
                primaryIndex));
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add(successMessage);
            return true;
        }

        private bool CanUseDecorationClipboard(
            bool wholeSelection,
            bool requireSelection,
            out string reason)
        {
            if (IsSurfaceMode)
            {
                reason = "Decoration clipboard commands are unavailable in Surface mode.";
                return false;
            }

            if (_textInputFocused || DecoLimitLifter.EsuInputState.IsTextInputActive())
            {
                reason = "Leave the active text field before using decoration clipboard commands.";
                return false;
            }

            if (_unappliedClosePromptOpen ||
                _placingMesh != null ||
                _boxSelecting ||
                _dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _sharedAnchorDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active placement, drag, selection, or prompt first.";
                return false;
            }

            if (wholeSelection && _selectionFocusLocked)
            {
                reason = "Disable Focus deco before copying or pasting a whole selection.";
                return false;
            }

            if (requireSelection &&
                (_selected == null || _selected.IsDeleted || _selectedConstruct == null))
            {
                reason = "Select a live decoration first.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void DeleteSelectedDecoration()
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
            {
                InfoStore.Add("Select a decoration before deleting it.");
                return;
            }

            if (!TryBuildDecorationDeletePlans(out List<DecorationDeletePlan> plans))
                return;

            var deletedPlans = new List<DecorationDeletePlan>(plans.Count);
            for (int index = 0; index < plans.Count; index++)
            {
                DecorationDeletePlan plan = plans[index];
                try
                {
                    plan.Decoration.Delete();
                    deletedPlans.Add(plan);
                }
                catch (Exception exception)
                {
                    bool rollbackOk = RollbackFailedDecorationDelete(deletedPlans);
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Decoration context delete failed",
                        exception,
                        LogOptions._AlertDevAndCustomerInGame);
                    InfoStore.Add(rollbackOk
                        ? "Decoration delete failed; already deleted mirrors were restored."
                        : "Decoration delete failed and rollback had errors; see the log.");
                    return;
                }
            }

            for (int index = 0; index < deletedPlans.Count; index++)
            {
                DecorationDeletePlan plan = deletedPlans[index];
                if (plan.CreatedInSession)
                    _transactions.UnmarkCreated(plan.Decoration);
                else
                    TrackDeletedDecoration(plan.Construct, plan.Original, plan.Deleted, plan.Decoration);
                ClearDeletedSelection(plan.Decoration);
            }

            if (deletedPlans.Count == 1)
            {
                DecorationDeletePlan plan = deletedPlans[0];
                _history.Record(new DecorationDeleteCommand(
                    plan.Construct,
                    plan.Decoration,
                    plan.Deleted,
                    plan.Original,
                    plan.CreatedInSession));
            }
            else
            {
                _history.Record(new DecorationDeleteBatchCommand(
                    deletedPlans[0].Construct,
                    deletedPlans.Select(plan => plan.Decoration).ToArray(),
                    deletedPlans.Select(plan => plan.Deleted).ToArray(),
                    deletedPlans.Select(plan => plan.Original).ToArray(),
                    deletedPlans.Select(plan => plan.CreatedInSession).ToArray(),
                    primaryIndex: 0));
            }

            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            InfoStore.Add(deletedPlans.Count == 1
                ? "Decoration deleted."
                : deletedPlans.Count.ToString(CultureInfo.InvariantCulture) +
                  " selected or mirrored decorations deleted.");
        }

        private bool RollbackFailedDecorationDelete(IReadOnlyList<DecorationDeletePlan> deletedPlans)
        {
            if (deletedPlans == null || deletedPlans.Count == 0)
                return true;

            bool ok = true;
            for (int index = deletedPlans.Count - 1; index >= 0; index--)
            {
                DecorationDeletePlan plan = deletedPlans[index];
                if (TryUndoDeletedDecoration(
                        plan.Construct,
                        plan.Deleted,
                        plan.Original,
                        plan.CreatedInSession,
                        out _))
                {
                    continue;
                }

                ok = false;
            }

            UpdateDirtyFromSelection();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return ok;
        }

        private bool TryBuildDecorationDeletePlans(out List<DecorationDeletePlan> plans)
        {
            plans = new List<DecorationDeletePlan>();
            List<Decoration> explicitSelection = CurrentPrimarySelectionDecorations();
            var decorations = new List<Decoration>(explicitSelection.Count);
            var seen = new HashSet<Decoration>();
            var manager = _selectedConstruct?.Decorations as AllConstructDecorations;
            if (manager == null || explicitSelection.Count == 0)
            {
                InfoStore.Add("Decoration delete needs a live selection and decoration manager.");
                return false;
            }

            for (int index = 0; index < explicitSelection.Count; index++)
            {
                Decoration decoration = explicitSelection[index];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    !ReferenceEquals(decoration.OurManager, manager) ||
                    !TryIsDecorationInManager(manager, decoration, out bool isMember) ||
                    !isMember ||
                    !seen.Add(decoration))
                {
                    InfoStore.Add("Decoration delete was not started because the selection changed or contains another construct.");
                    return false;
                }

                decorations.Add(decoration);
            }

            if (DecoLimitLifter.EsuSymmetry.HasActivePlanes)
            {
                for (int selectionIndex = 0;
                     selectionIndex < explicitSelection.Count;
                     selectionIndex++)
                {
                    Decoration source = explicitSelection[selectionIndex];
                    var selectedSnapshot = new DecorationEditSnapshot(source);
                    if (!TryBuildSymmetryFollowContext(
                            source,
                            _selectedConstruct,
                            selectedSnapshot,
                            reportSkipped: true,
                            trackTargetEdits: false,
                            out SymmetryFollowContext symmetryFollow))
                    {
                        return false;
                    }

                    if (symmetryFollow == null || !symmetryFollow.IsActive)
                        continue;

                    for (int targetIndex = 0;
                         targetIndex < symmetryFollow.Targets.Length;
                         targetIndex++)
                    {
                        Decoration counterpart = symmetryFollow.Targets[targetIndex].Decoration;
                        if (counterpart != null &&
                            !counterpart.IsDeleted &&
                            ReferenceEquals(counterpart.OurManager, manager) &&
                            seen.Add(counterpart))
                        {
                            decorations.Add(counterpart);
                        }
                    }
                }
            }

            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null || decoration.IsDeleted)
                    continue;

                bool createdInSession = _transactions.IsCreated(decoration);
                var deleted = new DecorationEditSnapshot(decoration);
                DecorationEditSnapshot original = createdInSession
                    ? null
                    : _transactions.GetOriginal(decoration) ?? deleted;
                plans.Add(new DecorationDeletePlan(
                    _selectedConstruct,
                    decoration,
                    deleted,
                    original,
                    createdInSession));
            }

            if (plans.Count != decorations.Count)
            {
                InfoStore.Add("Decoration delete failed because a mirrored decoration disappeared.");
                return false;
            }

            return true;
        }

        private void ClearDeletedSelection(Decoration decoration)
        {
            if (decoration == null)
                return;

            if (ReferenceEquals(_selectionFocusDecoration, decoration))
                ClearSelectionFocusLock(notify: false);

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

            try
            {
                decoration = decorations.NewDecoration(
                    snapshot.TetherPoint,
                    force: true,
                    playSound: false,
                    forceEvenIfMaxReached: true);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] " + context + " creation failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(context + " failed because FTD threw while creating the replacement decoration.");
                return false;
            }
            if (decoration == null)
            {
                InfoStore.Add(context + " failed because FTD rejected the new decoration.");
                return false;
            }

            Exception restoreFailure = null;
            try
            {
                if (snapshot.TryRestore(decoration) && snapshot.Matches(decoration))
                    return true;
            }
            catch (Exception exception)
            {
                restoreFailure = exception;
            }

            var cleanup = new[] { decoration };
            bool cleanupOk = CleanupCreatedDecorationBatch(
                cleanup,
                new[] { 0 },
                context + " cleanup");

            decoration = cleanup[0];
            if (restoreFailure != null)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] " + context + " restore failed",
                    restoreFailure,
                    LogOptions._AlertDevAndCustomerInGame);
            }
            InfoStore.Add(cleanupOk
                ? context + " failed because FTD rejected the restored decoration state; the partial decoration was removed."
                : context + " failed and partial-decoration cleanup was incomplete; see the log.");
            return false;
        }

        private void ApplySelection(bool notify = true)
        {
            ClearApplyCancelAttention();
            CloseDecorationContextMenu();
            if (_selected != null && !_selected.IsDeleted)
                _snapshot = new DecorationEditSnapshot(_selected);
            _transactions.Apply();
            _blockPaintOriginalColors.Clear();
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
            RefreshSelectionFocusLock();
            if (notify)
                InfoStore.Add("Decoration edit session applied.");
        }

        private void CancelSelection(bool notify = true)
        {
            ClearApplyCancelAttention();
            CloseDecorationContextMenu();
            CancelPlacement();
            // Restore native blocks first so a later decoration/draft cleanup
            // failure cannot strand block colors outside the session rollback.
            RestoreOriginalBlockPaintsForCancel();
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
            RefreshSelectionFocusLock();
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

                    created.Add(decoration);
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = mesh.Guid;
                    decoration.Positioning.Us = plan.Positioning;
                    decoration.Scaling.Us = plan.Scaling;
                    decoration.Orientation.Us = plan.Orientation;
                    decoration.Color.Us = plan.Color;
                    decoration.HideOriginalMesh.Us = plan.HideOriginalMesh;
                    decoration.MaterialReplacement.Us = plan.MaterialReplacement;
                    decoration.Changed();
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
            out List<DecorationPlacementPlan> plans) =>
            TryBuildDecorationPlacementPlans(
                originalPositioning,
                reportMessages: true,
                out plans);

        private bool TryBuildDecorationPlacementPlans(
            Vector3 originalPositioning,
            bool reportMessages,
            out List<DecorationPlacementPlan> plans)
        {
            plans = new List<DecorationPlacementPlan>();
            Vector3 originalCenter = ToVector3(_placementAnchor) + originalPositioning;
            DecorationPlacementTemplate template = CurrentPlacementTemplate();
            var seen = new HashSet<string>();
            foreach (DecoLimitLifter.EsuSymmetry.SymmetryVariant variant in
                     DecoLimitLifter.EsuSymmetry.Variants())
            {
                Vector3i anchor = variant.Mirror(_placementAnchor);
                Vector3 center = variant.Mirror(originalCenter);
                Vector3 positioning = DecorationEditMath.Snap(center - ToVector3(anchor));
                Vector3 orientation = variant.MirrorEuler(template.Orientation);
                Vector3 scaling = variant.MirrorScale(template.Scaling);
                string key = PlacementKey(anchor, positioning);
                if (!seen.Add(key))
                    continue;

                if (!HasBlock(_placementConstruct, anchor))
                {
                    if (reportMessages)
                        InfoStore.Add("Symmetry placement rejected because a mirrored tether block is missing.");
                    return false;
                }

                if (!DecorationEditMath.IsFinite(positioning) ||
                    !DecorationEditMath.IsWithinPositionLimit(positioning))
                {
                    if (reportMessages)
                        InfoStore.Add("Symmetry placement rejected because a mirrored offset would exceed +/-10.");
                    return false;
                }

                if (!DecorationEditMath.IsFinite(orientation) || !IsValidScale(scaling))
                {
                    if (reportMessages)
                        InfoStore.Add("Symmetry placement rejected because a mirrored transform is invalid.");
                    return false;
                }

                plans.Add(new DecorationPlacementPlan(
                    anchor,
                    positioning,
                    orientation,
                    scaling,
                    template.Color,
                    template.HideOriginalMesh,
                    template.MaterialReplacement));
            }

            if (plans.Count == 0)
            {
                if (reportMessages)
                    InfoStore.Add("No valid decoration placement was generated.");
                return false;
            }

            return true;
        }

        private DecorationPlacementTemplate CurrentPlacementTemplate()
        {
            if (_selected == null || _selected.IsDeleted)
                return DecorationPlacementTemplate.Default;

            Vector3 orientation = DecorationEditMath.IsFinite(_selected.Orientation.Us)
                ? _selected.Orientation.Us
                : Vector3.zero;
            Vector3 scaling = IsValidScale(_selected.Scaling.Us)
                ? _selected.Scaling.Us
                : Vector3.one;
            return new DecorationPlacementTemplate(
                orientation,
                scaling,
                Mathf.Clamp(_selected.Color.Us, 0, 31),
                _selected.HideOriginalMesh.Us,
                _selected.MaterialReplacement.Us);
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

        private bool TryPreflightDecorationSnapshotBatch(
            AllConstruct construct,
            AllConstructDecorations manager,
            IReadOnlyList<DecorationEditSnapshot> snapshots,
            string context,
            out string failure)
        {
            if (construct == null || manager == null || snapshots == null)
            {
                failure = context + ": the decoration manager or copied batch is unavailable.";
                return false;
            }

            try
            {
                if (!ReferenceEquals(construct.Decorations, manager) || snapshots.Count == 0)
                {
                    failure = context + ": the decoration manager or copied batch is unavailable.";
                    return false;
                }

                if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                        manager,
                        snapshots.Count,
                        out string compatibilityMessage))
                {
                    failure = context + ": " + compatibilityMessage;
                    return false;
                }

                long requestedTotal = (long)manager.DecorationCount + snapshots.Count;
                if (requestedTotal > AllConstructDecorations._limitPerPacketManager)
                {
                    failure = context + ": the manager does not have capacity for " +
                              snapshots.Count.ToString("N0", CultureInfo.InvariantCulture) +
                              " decorations.";
                    return false;
                }

                for (int index = 0; index < snapshots.Count; index++)
                {
                    DecorationEditSnapshot snapshot = snapshots[index];
                    if (snapshot == null ||
                        !snapshot.HasFiniteTransform ||
                        !IsValidScale(snapshot.Scaling))
                    {
                        failure = context + ": copied decoration #" +
                                  (index + 1).ToString(CultureInfo.InvariantCulture) +
                                  " has an invalid transform.";
                        return false;
                    }

                    if (!HasBlock(construct, snapshot.TetherPoint))
                    {
                        failure = context + ": tether block for copied decoration #" +
                                  (index + 1).ToString(CultureInfo.InvariantCulture) +
                                  " is missing.";
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] " + context + " preflight failed",
                    exception,
                    LogOptions._AlertDevInGame);
                failure = context + ": the construct changed or became unavailable during preflight.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private bool CleanupCreatedDecorationBatch(
            Decoration[] decorations,
            IReadOnlyList<int> createdIndexes,
            string context)
        {
            bool ok = true;
            using (VanillaCompatibilityGuard.BeginSuppression(context))
            {
                for (int step = createdIndexes.Count - 1; step >= 0; step--)
                {
                    int index = createdIndexes[step];
                    Decoration decoration = decorations[index];
                    if (decoration == null)
                        continue;

                    AllConstructDecorations manager = null;
                    try { manager = decoration.OurManager; }
                    catch { }
                    try
                    {
                        decoration.Delete();
                        _transactions.UnmarkCreated(decoration);
                        decorations[index] = null;
                    }
                    catch (Exception exception)
                    {
                        bool detached = manager != null &&
                                        TryIsDecorationInManager(
                                            manager,
                                            decoration,
                                            out bool isMember) &&
                                        !isMember;
                        bool finalized = detached &&
                                         TryFinalizeDetachedDecoration(
                                             manager,
                                             decoration,
                                             context + " detached cleanup");
                        if (detached)
                        {
                            _transactions.UnmarkCreated(decoration);
                            decorations[index] = null;
                        }
                        if (!finalized)
                            ok = false;
                        AdvLogger.LogException(
                            "[EndlessShapes Unlimited] Decoration batch cleanup failed",
                            exception,
                            LogOptions._AlertDevInGame);
                    }
                }
            }

            return ok;
        }

        private static bool TryIsDecorationInManager(
            AllConstructDecorations manager,
            Decoration decoration,
            out bool isMember)
        {
            isMember = false;
            if (manager == null || decoration == null)
                return false;

            try
            {
                foreach (Decoration live in manager.DecorationList)
                {
                    if (!ReferenceEquals(live, decoration))
                        continue;
                    isMember = true;
                    break;
                }

                return true;
            }
            catch
            {
                isMember = false;
                return false;
            }
        }

        private static bool TryFinalizeDetachedDecoration(
            AllConstructDecorations manager,
            Decoration decoration,
            string context)
        {
            if (!TryIsDecorationInManager(manager, decoration, out bool isMember) || isMember)
                return false;

            try
            {
                if (decoration.IsDeleted)
                    return true;
            }
            catch
            {
            }

            try
            {
                using (VanillaCompatibilityGuard.BeginSuppression(context))
                    decoration.Delete();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] " + context + " failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }

            if (!TryIsDecorationInManager(manager, decoration, out isMember) || isMember)
                return false;
            try
            {
                return decoration.IsDeleted;
            }
            catch
            {
                return false;
            }
        }

        private bool TryRestoreAfterFailedDecorationDeletion(
            AllConstruct construct,
            AllConstructDecorations manager,
            Decoration removed,
            DecorationEditSnapshot rollback,
            DecorationEditSnapshot original,
            bool createdInSession,
            string context,
            out Decoration restored)
        {
            restored = removed;
            if (construct == null ||
                manager == null ||
                removed == null ||
                rollback == null ||
                !TryIsDecorationInManager(manager, removed, out bool isMember))
            {
                return false;
            }

            if (isMember)
            {
                try
                {
                    return rollback.TryRestore(removed) && rollback.Matches(removed);
                }
                catch
                {
                    return false;
                }
            }

            bool detachedFinalized = TryFinalizeDetachedDecoration(
                manager,
                removed,
                context + " finalize detached decoration");
            if (createdInSession)
                _transactions.UnmarkCreated(removed);

            if (!TryCreateDecorationFromSnapshot(
                    construct,
                    rollback,
                    context,
                    out Decoration replacement))
            {
                return false;
            }

            restored = replacement;
            if (createdInSession)
                _transactions.MarkCreated(replacement);
            else if (original != null)
                _transactions.TrackEdit(replacement, original);

            if (_selectionFocusLocked && ReferenceEquals(_selectionFocusDecoration, removed))
            {
                _selectionFocusDecoration = replacement;
                _selectionFocusConstruct = construct;
            }
            SelectFromHistory(replacement, construct, createdInSession);
            try
            {
                UpdateDirtyFromSelection();
                RefreshDecorationCache(force: true);
                RefreshForecast(force: true);
            }
            catch
            {
                detachedFinalized = false;
            }

            return detachedFinalized;
        }

        private void SelectDecorationGroup(
            IReadOnlyList<Decoration> decorations,
            AllConstruct construct,
            int primaryIndex,
            bool createdInSession)
        {
            if (decorations == null || decorations.Count == 0 || construct == null)
                return;

            int selectedIndex = Mathf.Clamp(primaryIndex, 0, decorations.Count - 1);
            Decoration primary = decorations[selectedIndex];
            if (primary == null || primary.IsDeleted)
                return;

            _selected = primary;
            _selectedConstruct = construct;
            _snapshot = _transactions.GetOriginal(primary) ?? new DecorationEditSnapshot(primary);
            _selectedCreatedInSession = createdInSession || _transactions.IsCreated(primary);
            _selection.Clear();
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration != null && !decoration.IsDeleted)
                    _selection.Add(decoration);
            }

            SetActiveTool(DecorationEditorTool.Move);
            _dragAxis = DecorationEditAxis.None;
            _rotateDragAxis = DecorationEditAxis.None;
            _scaleDragAxis = DecorationEditAxis.None;
            _anchorDragAxis = DecorationEditAxis.None;
            ClearMultiTransformState();
            UpdateDirtyFromSelection();
            ResetInspectorFields();
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
            TryBuildSymmetryFollowContext(
                selectedBefore,
                reportSkipped,
                trackTargetEdits: true,
                out SymmetryFollowContext symmetryFollow);
            return symmetryFollow;
        }

        private bool TryBuildSymmetryFollowContext(
            DecorationEditSnapshot selectedBefore,
            bool reportSkipped,
            bool trackTargetEdits,
            out SymmetryFollowContext symmetryFollow)
        {
            return TryBuildSymmetryFollowContext(
                _selected,
                _selectedConstruct,
                selectedBefore,
                reportSkipped,
                trackTargetEdits,
                out symmetryFollow);
        }

        private bool TryBuildSymmetryFollowContext(
            Decoration sourceDecoration,
            AllConstruct sourceConstruct,
            DecorationEditSnapshot selectedBefore,
            bool reportSkipped,
            bool trackTargetEdits,
            out SymmetryFollowContext symmetryFollow)
        {
            symmetryFollow = null;
            if (selectedBefore == null ||
                sourceDecoration == null ||
                sourceDecoration.IsDeleted ||
                sourceConstruct == null ||
                !DecoLimitLifter.EsuSymmetry.HasActivePlanes)
            {
                return true;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(sourceConstruct, out string constructReason))
            {
                ReportSymmetryFollowSkipped(constructReason, reportSkipped);
                return false;
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

                if (!HasBlock(sourceConstruct, expectedAnchor))
                {
                    ReportSymmetryFollowSkipped(
                        "mirrored tether block is missing",
                        reportSkipped);
                    return false;
                }

                if (!DecorationEditMath.IsFinite(expectedPosition) ||
                    !DecorationEditMath.IsWithinPositionLimit(expectedPosition))
                {
                    ReportSymmetryFollowSkipped(
                        "mirrored offset would exceed +/-10",
                        reportSkipped);
                    return false;
                }

                if (!TryFindSymmetryCounterpart(
                        sourceDecoration,
                        sourceConstruct,
                        expectedAnchor,
                        expectedCenter,
                        selectedBefore.MeshGuid,
                        seenDecorations,
                        out Decoration counterpart,
                        out string matchReason))
                {
                    ReportSymmetryFollowSkipped(matchReason, reportSkipped);
                    return false;
                }

                seenDecorations.Add(counterpart);
                targets.Add(new SymmetryFollowTarget(
                    variant,
                    counterpart,
                    new DecorationEditSnapshot(counterpart)));
            }

            if (targets.Count == 0)
                return true;

            if (trackTargetEdits)
            {
                for (int index = 0; index < targets.Count; index++)
                    _transactions.TrackEdit(targets[index].Decoration, targets[index].Before);
            }

            symmetryFollow = new SymmetryFollowContext(selectedBefore, targets.ToArray());
            return true;
        }

        private bool TryFindSymmetryCounterpart(
            Decoration sourceDecoration,
            AllConstruct sourceConstruct,
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
            foreach (Decoration decoration in SelectedAnchorDecorations(
                         sourceConstruct,
                         expectedAnchor))
            {
                if (decoration == null ||
                    decoration.IsDeleted ||
                    ReferenceEquals(decoration, sourceDecoration) ||
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
                Vector3 targetOrientation = target.Variant.MirrorEuler(_selected.Orientation.Us);
                Vector3 targetScale = target.Variant.MirrorScale(_selected.Scaling.Us);
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

                if (!DecorationEditMath.IsFinite(targetOrientation) ||
                    !IsValidScale(targetScale))
                {
                    return ReportSymmetryFollowInvalid(
                        symmetryFollow,
                        "mirrored transform is invalid",
                        reportInvalid);
                }

                applications[index] = new SymmetryFollowApplication(
                    target.Decoration,
                    targetAnchor,
                    targetPosition,
                    targetOrientation,
                    targetScale,
                    _selected.Color.Us);
            }

            DecorationEditSnapshot[] rollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            for (int index = 0; index < applications.Length; index++)
            {
                SymmetryFollowApplication application = applications[index];
                if (TryApplyDecorationSymmetryTransform(
                        application.Decoration,
                        application.Anchor,
                        application.Positioning,
                        application.Orientation,
                        application.Scaling,
                        application.Color,
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

        private bool TryApplyDecorationSymmetryTransform(
            Decoration decoration,
            Vector3i anchor,
            Vector3 positioning,
            Vector3 orientation,
            Vector3 scaling,
            int color,
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

            if (!DecorationEditMath.IsFinite(orientation) || !IsValidScale(scaling))
            {
                reason = "mirrored transform is invalid";
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
                decoration.Orientation.Us = orientation;
                DecorationScaleBounds.AllowExtendedScale(decoration);
                decoration.Scaling.Us = scaling;
                decoration.Color.Us = color;
                decoration.Changed();
                return true;
            }
            catch
            {
                reason = "FTD rejected the mirrored decoration update";
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
            _generatorDraft.ClearSelection();

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
            _surfaceDraft.ClearSelection();

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
                _generatorMessage = "Use Path, Tube, or a centered shape to place generator points.";
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
                : GeneratorToolDisplayName(_generatorDraft.Tool) + " point selected.";
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
                    ? (radial
                        ? "Shape center"
                        : GeneratorToolDisplayName(_generatorDraft.Tool) + " point")
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    _generatorDraft.Construct,
                    center,
                    DecorationEditMath.AxisVector,
                    style.MoveLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: true);
            axis = pick.Axis;
            return pick.IsHit;
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
            if (!TryProjectGizmo(_generatorDraft.Construct, center, out Vector2 origin))
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float radius = style.RotationRadius;
            float best = style.HitAreaPixels;
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
                    if (!TryProjectGizmo(_generatorDraft.Construct, local, out Vector2 projected))
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoProjections projections = new DecorationGizmoProjections(
                TryProjectNullable(_generatorDraft.Construct, center),
                TryProjectNullable(
                    _generatorDraft.Construct,
                    center + _generatorDraft.CircleTangentA * style.ScaleLength),
                null,
                null);
            return DecorationEditMath.PickGizmo(
                mouse,
                projections,
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false).IsHit;
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
            TryProjectGizmo(_generatorDraft.Construct, _generatorDraft.CircleCenter, out _generatorRotateDragCenterScreen);
            _generatorRotateDragStartVector = _lastMouseGui - _generatorRotateDragCenterScreen;
            _generatorRotateDragSensitivityScale = RotationDragSensitivityScale(
                _generatorDraft.Construct,
                _generatorDraft.CircleCenter,
                DecorationEditMath.AxisVector(axis),
                EsuGizmoSettings.Current.RotationRadius,
                EsuGizmoSettings.BaseRotationRadius,
                _lastMouseGui);
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
            _generatorScaleQuadWidthStart = _generatorSettings.QuadWidth;
            _generatorScaleQuadHeightStart = _generatorSettings.QuadHeight;
            TryProjectGizmo(_generatorDraft.Construct, _generatorDraft.CircleCenter, out _generatorRotateDragCenterScreen);
            _generatorRotateDragStartVector = _lastMouseGui - _generatorRotateDragCenterScreen;
            _generatorRotateDragSensitivityScale = 1f;
        }

        private void TryUpdateGeneratorRotate()
        {
            Vector2 current = _lastMouseGui - _generatorRotateDragCenterScreen;
            if (_generatorRotateDragStartVector.sqrMagnitude < 16f || current.sqrMagnitude < 16f)
                return;

            float degrees = DecorationEditMath.Snap(
                Vector2.SignedAngle(_generatorRotateDragStartVector, current) *
                _generatorRotateDragSensitivityScale,
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

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            Vector3 center = _generatorDraft.CircleCenter;
            if (!TryProjectGizmo(_generatorDraft.Construct, center, out Vector2 origin) ||
                !TryProjectGizmo(
                    _generatorDraft.Construct,
                    center + _generatorDraft.CircleTangentA * style.ScaleLength,
                    out Vector2 visualEnd) ||
                !TryProjectGizmo(
                    _generatorDraft.Construct,
                    center + _generatorDraft.CircleTangentA * EsuGizmoSettings.BaseScaleLength,
                    out Vector2 referenceEnd) ||
                !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    origin,
                    visualEnd,
                    referenceEnd,
                    EsuGizmoSettings.BaseScaleLength,
                    out float delta))
            {
                return;
            }

            if (_surfaceExtraTool == SurfaceExtraTool.Quad)
            {
                if (!TryResolveQuadScaleDimensions(
                        _generatorScaleQuadWidthStart,
                        _generatorScaleQuadHeightStart,
                        delta,
                        DecorationScaleSnap,
                        out float width,
                        out float height))
                {
                    return;
                }

                _generatorSettings.QuadWidth = width;
                _generatorSettings.QuadHeight = height;
                _generatorQuadWidthText = width.ToString("0.###", CultureInfo.InvariantCulture);
                _generatorQuadHeightText = height.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                float radius = Mathf.Max(
                    0.05f,
                    DecorationEditMath.Snap(_generatorScaleRadiusStart + delta, DecorationScaleSnap));
                _generatorSettings.CircleRadius = radius;
                _generatorRadiusText = radius.ToString("0.###", CultureInfo.InvariantCulture);
            }
            _generatorPlan = null;
            _generatorMessage = "Generator shape scaled.";
        }

        internal static bool TryResolveQuadScaleDimensions(
            float startWidth,
            float startHeight,
            float halfWidthDelta,
            float snap,
            out float width,
            out float height)
        {
            width = 0f;
            height = 0f;
            if (!DecorationEditMath.IsFinite(startWidth) ||
                !DecorationEditMath.IsFinite(startHeight) ||
                !DecorationEditMath.IsFinite(halfWidthDelta) ||
                !DecorationEditMath.IsFinite(snap) ||
                startWidth <= 0f ||
                startHeight <= 0f ||
                snap <= 0f)
            {
                return false;
            }

            float drivingWidth = DecorationEditMath.Snap(
                startWidth + halfWidthDelta * 2f,
                snap);
            if (!DecorationEditMath.IsFinite(drivingWidth))
                return false;

            float factor = drivingWidth / startWidth;
            float minimumFactor = Mathf.Max(
                0.05f / startWidth,
                0.05f / startHeight);
            factor = Mathf.Max(factor, minimumFactor);
            width = startWidth * factor;
            height = startHeight * factor;
            return DecorationEditMath.IsFinite(width) &&
                   DecorationEditMath.IsFinite(height) &&
                   width >= 0.05f &&
                   height >= 0.05f;
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
                if (!TryProjectGizmo(_generatorDraft.Construct, _generatorDragPointStart, out Vector2 origin))
                    return;

                float moveLength = EsuGizmoSettings.Current.MoveLength;
                Vector3 axisVector = DecorationEditMath.AxisVector(_generatorDragAxis);
                if (!TryProjectGizmo(_generatorDraft.Construct, _generatorDragPointStart + axisVector * moveLength, out Vector2 axisEnd))
                    return;
                if (!TryProjectGizmo(
                        _generatorDraft.Construct,
                        _generatorDragPointStart + axisVector * EsuGizmoSettings.BaseMoveLength,
                        out Vector2 referenceEnd) ||
                    !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                        mouseDelta,
                        origin,
                        axisEnd,
                        referenceEnd,
                        EsuGizmoSettings.BaseMoveLength,
                        out float axisDelta))
                {
                    return;
                }
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectSignedGizmoAxes(
                    construct,
                    anchor,
                    DecorationEditMath.AxisVector,
                    style.AnchorLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: false);
            axis = pick.Axis;
            sign = pick.Sign;
            return pick.IsHit;
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

            if (!TryProjectGizmo(construct, ToVector3(_sharedAnchorDragStart), out Vector2 origin))
                return;

            float anchorLength = EsuGizmoSettings.Current.AnchorLength;
            Vector3 axisVector = DecorationEditMath.AxisVector(_sharedAnchorDragAxis) * _sharedAnchorDragSign;
            Vector3 endLocal = ToVector3(_sharedAnchorDragStart) + axisVector * anchorLength;
            if (!TryProjectGizmo(construct, endLocal, out Vector2 axisEnd))
                return;
            if (!TryProjectGizmo(
                    construct,
                    ToVector3(_sharedAnchorDragStart) + axisVector * EsuGizmoSettings.BaseAnchorLength,
                    out Vector2 referenceEnd) ||
                !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                    mouseDelta,
                    origin,
                    axisEnd,
                    referenceEnd,
                    EsuGizmoSettings.BaseAnchorLength,
                    out float projected))
            {
                return;
            }
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
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationGizmoPick pick = DecorationEditMath.PickGizmo(
                mouse,
                ProjectPositiveGizmoAxes(
                    _surfaceDraft.Construct,
                    center,
                    DecorationEditMath.AxisVector,
                    style.MoveLength),
                style.HitAreaPixels,
                style.FreeMoveCorePixels,
                allowFree: true);
            axis = pick.Axis;
            return pick.IsHit;
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
                if (!TryProjectGizmo(_surfaceDraft.Construct, _surfaceDragPointStart, out Vector2 origin))
                    return;

                float moveLength = EsuGizmoSettings.Current.MoveLength;
                Vector3 axisVector = DecorationEditMath.AxisVector(_surfaceDragAxis);
                if (!TryProjectGizmo(_surfaceDraft.Construct, _surfaceDragPointStart + axisVector * moveLength, out Vector2 axisEnd))
                    return;
                if (!TryProjectGizmo(
                        _surfaceDraft.Construct,
                        _surfaceDragPointStart + axisVector * EsuGizmoSettings.BaseMoveLength,
                        out Vector2 referenceEnd) ||
                    !DecorationEditMath.TryProjectMouseDeltaToAxisInvariant(
                        mouseDelta,
                        origin,
                        axisEnd,
                        referenceEnd,
                        EsuGizmoSettings.BaseMoveLength,
                        out float axisDelta))
                {
                    return;
                }
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

            float best = EsuGizmoSettings.Current.HitAreaPixels;
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
            if (!TryBuildSurfacePlan(out SurfaceDecorationPlan plan, out string message))
            {
                _surfacePlan = null;
                _surfaceMessage = message;
                if (showMessage && !string.IsNullOrEmpty(message))
                    InfoStore.Add("Surface preview rejected: " + message);
                return false;
            }

            _surfacePlan = plan;
            _surfaceMessage = message;
            if (showMessage)
                InfoStore.Add("Surface preview: " + message);
            return true;
        }

        private bool TryBuildSurfacePlan(
            out SurfaceDecorationPlan plan,
            out string message) =>
            SurfaceDecorationPlanner.TryPlanWithSymmetry(
                _surfaceDraft,
                new ConstructSurfaceAnchorResolver(_surfaceDraft.Construct),
                out plan,
                out message);

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
            if (!TryBuildSurfacePlan(out SurfaceDecorationPlan plan, out string message) ||
                plan == null)
            {
                _surfaceMessage = message;
                if (!string.IsNullOrEmpty(message))
                    InfoStore.Add("Surface placement rejected: " + message);
                return;
            }

            _surfaceMessage = message;
            AllConstruct construct = plan.Construct;
            var decorations = construct?.Decorations as AllConstructDecorations;
            if (decorations == null)
            {
                InfoStore.Add("Surface placement rejected: decoration manager is unavailable.");
                return;
            }

            long requestedTotal = (long)decorations.DecorationCount + plan.DecorationCount;
            if (!VanillaCompatibilityGuard.TryAllowDecorationCreation(
                    decorations,
                    plan.DecorationCount,
                    out string compatibilityMessage))
            {
                InfoStore.Add(compatibilityMessage);
                return;
            }

            if (requestedTotal > AllConstructDecorations._limitPerPacketManager)
            {
                InfoStore.Add(
                    "Surface placement rejected: needs " +
                    plan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " decorations, but the manager has insufficient remaining capacity.");
                return;
            }

            var created = new List<Decoration>(plan.DecorationCount);
            try
            {
                for (int index = 0; index < plan.Placements.Count; index++)
                {
                    SurfaceDecorationPlacement placement = plan.Placements[index];
                    Decoration decoration = decorations.NewDecoration(
                        placement.Anchor,
                        force: true,
                        playSound: index == 0,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a generated surface decoration.");

                    created.Add(decoration);
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = placement.MeshGuid;
                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Orientation.Us = placement.Orientation;
                    decoration.Color.Us = placement.Color;
                    decoration.Changed();
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

                    created.Add(decoration);
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.MeshGuid.Us = placement.MeshGuid;
                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Orientation.Us = placement.Orientation;
                    decoration.Color.Us = placement.Color;
                    decoration.MaterialReplacement.Us = placement.MaterialReplacement;
                    decoration.Changed();
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
            _generatorMessage = "Generator draft cleared. Draw a path/tube or place a shape center.";
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
            SyncSurfaceCoordinateText(force: true);
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
            SyncSurfaceCoordinateText(force: true);
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

        private bool TryHandleFacingSymmetryShortcut()
        {
            if (!DecoLimitLifter.EsuInputState.IsFacingSymmetryShortcutDown())
                return false;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            if (!TryFacingSymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell))
            {
                InfoStore.Add("Point at the focused construct grid before toggling a symmetry mirror.");
                return true;
            }

            if (!DecoLimitLifter.EsuSymmetry.TryCameraFacingLocal(construct, out Vector3 localFacing))
            {
                InfoStore.Add("A camera direction is required before toggling a symmetry mirror.");
                return true;
            }

            DecoLimitLifter.EsuSymmetry.TryToggleFacingPlane(
                construct,
                cell,
                localFacing,
                out string message);
            InfoStore.Add(message);
            return true;
        }

        private bool TryFacingSymmetryPlaneCandidate(out AllConstruct construct, out Vector3i cell)
        {
            construct = null;
            cell = default;
            if (_pointerProbe.TryProbe(out DecorationPointerHit hit))
            {
                construct = hit.Construct;
                cell = hit.Anchor;
                return construct != null;
            }

            if (_placingMesh != null && _placementConstruct != null)
            {
                construct = _placementConstruct;
                cell = _placementAnchor;
                return true;
            }

            if (_selected != null && !_selected.IsDeleted && _selectedConstruct != null)
            {
                construct = _selectedConstruct;
                cell = _selected.TetherPoint.Us;
                return true;
            }

            construct = FocusedConstruct();
            if (construct == null)
                return false;

            cell = RoundToCell(FocusedConstructLocalCenter(construct));
            return true;
        }

        private void HandleEditorKeybinds()
        {
            if (_textInputFocused ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
                return;

            if (!IsSurfaceMode)
            {
                bool copySelection = ReadEditorKeyDown(
                    SerializationHudKeyInput.CopyDecorationSelection,
                    IsCopySelectionDefaultDown);
                bool pasteSelection = ReadEditorKeyDown(
                    SerializationHudKeyInput.PasteDecorationSelection,
                    IsPasteSelectionDefaultDown);
                if (copySelection)
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    CopyDecorationSelection();
                    return;
                }

                if (pasteSelection)
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    PasteDecorationSelectionInPlace();
                    return;
                }

                if (ReadNativeDecorationSettingsKeyDown(paste: false))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    CopyWithStandardDecorationShortcut();
                    return;
                }

                if (ReadNativeDecorationSettingsKeyDown(paste: true))
                {
                    DecorationEditorInputScope.ClaimBuildInputForFrames();
                    PasteWithStandardDecorationShortcut();
                    return;
                }
            }

            if (TryHandleEsuNumberShortcut())
                return;

            if (TryHandleFacingSymmetryShortcut())
                return;

            if (TryHandleBackspaceUtility())
                return;

            bool undo = ReadEditorKeyDown(
                SerializationHudKeyInput.UndoDecorationEdit,
                IsUndoDefaultDown);
            bool redo = ReadEditorKeyDown(
                SerializationHudKeyInput.RedoDecorationEdit,
                IsRedoDefaultDown);

            if (undo)
                UndoEdit();
            else if (redo)
                RedoEdit();
        }

        private static bool ReadEditorKeyDown(
            SerializationHudKeyInput input,
            Func<bool> fallback)
        {
            try
            {
                return SerializationHudKeyMap.Instance.Bool(
                    input,
                    KeyInputEventType.Down);
            }
            catch
            {
                // Direct keyboard fallback is only for early boot/profile failures.
                return fallback != null && fallback();
            }
        }

        private static bool IsUndoDefaultDown() =>
            IsControlHeld() &&
            !IsShiftHeld() &&
            Input.GetKeyDown(KeyCode.Z);

        private static bool IsRedoDefaultDown() =>
            IsControlHeld() &&
            (Input.GetKeyDown(KeyCode.Y) ||
             (IsShiftHeld() && Input.GetKeyDown(KeyCode.Z)));

        private static bool IsCopySelectionDefaultDown() =>
            IsControlHeld() &&
            IsShiftHeld() &&
            Input.GetKeyDown(KeyCode.C);

        private static bool IsPasteSelectionDefaultDown() =>
            IsControlHeld() &&
            IsShiftHeld() &&
            Input.GetKeyDown(KeyCode.V);

        private void CopyWithStandardDecorationShortcut()
        {
            if (CurrentPrimarySelectionDecorations().Count > 1)
            {
                s_standardClipboardRoutesToDecorationSelection =
                    TryCopyDecorationSelection();
                return;
            }

            s_standardClipboardRoutesToDecorationSelection = false;
            CopyDecorationSettings();
        }

        private void PasteWithStandardDecorationShortcut()
        {
            if (s_standardClipboardRoutesToDecorationSelection &&
                DecorationSelectionClipboard.HasValue)
            {
                PasteDecorationSelectionInPlace();
                return;
            }

            PasteDecorationSettings();
        }

        private static bool ReadNativeDecorationSettingsKeyDown(bool paste)
        {
            try
            {
                return FtdKeyMap.Instance.Bool(
                    paste
                        ? KeyInputsFtd.PasteInBuildMode
                        : KeyInputsFtd.CopyInBuildMode,
                    KeyInputEventType.Down);
            }
            catch
            {
                return IsControlHeld() &&
                       !IsShiftHeld() &&
                       Input.GetKeyDown(paste ? KeyCode.V : KeyCode.C);
            }
        }

        private bool TryHandleBackspaceUtility()
        {
            if (!Input.GetKeyDown(KeyCode.Backspace))
                return false;

            if (IsSurfaceMode)
            {
                if (_generatorDraft.HasActiveSelection)
                {
                    DeleteGeneratorSelection();
                    return true;
                }

                if (_surfaceDraft.HasActiveSelection)
                {
                    DeleteSurfaceSelection();
                    return true;
                }
            }

            if (_selected != null && !_selected.IsDeleted && _selectedConstruct != null)
            {
                DeleteSelectedDecoration();
                return true;
            }

            InfoStore.Add("Backspace: select a decoration or Surface Builder point before deleting.");
            return true;
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
                return CycleSurfaceCreateTool();

            if (_tool != DecorationEditorTool.Select)
            {
                SetActiveTool(DecorationEditorTool.Select);
                _selectionMode = DecorationSelectionMode.Single;
                CancelBoxSelection();
                InfoStore.Add("Selection: Single.");
                return true;
            }

            if (_selectionMode == DecorationSelectionMode.Single &&
                TryHandleSelectionFocusBulkRequest())
            {
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

        private bool CycleSurfaceCreateTool()
        {
            if (_surfaceBuilderTool != SurfaceBuilderTool.Draw &&
                _surfaceBuilderTool != SurfaceBuilderTool.Path &&
                _surfaceBuilderTool != SurfaceBuilderTool.Circle)
            {
                SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
                InfoStore.Add("Surface Builder tool: Draw.");
                return true;
            }

            if (_surfaceBuilderTool == SurfaceBuilderTool.Draw)
            {
                SetSurfaceExtraTool(SurfaceExtraTool.Path);
                InfoStore.Add("Surface Builder tool: Path.");
                return true;
            }

            SurfaceExtraTool current = _surfaceExtraTool == SurfaceExtraTool.None
                ? SurfaceExtraTool.Path
                : _surfaceExtraTool;
            int currentIndex = Array.IndexOf(SurfaceCreateToolCycle, current);
            if (currentIndex < 0 || currentIndex >= SurfaceCreateToolCycle.Length - 1)
            {
                SetSurfaceBuilderTool(SurfaceBuilderTool.Draw);
                InfoStore.Add("Surface Builder tool: Draw.");
                return true;
            }

            SurfaceExtraTool next = SurfaceCreateToolCycle[currentIndex + 1];
            SetSurfaceExtraTool(next);
            InfoStore.Add("Surface Builder tool: " + GeneratorToolDisplayName(next) + ".");
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

            DecorationEditSnapshot rollback = null;
            try
            {
                rollback = new DecorationEditSnapshot(decoration);
                if (!snapshot.TryRestore(decoration) || !snapshot.Matches(decoration))
                    throw new InvalidOperationException("FTD rejected a decoration snapshot restore.");
            }
            catch (Exception exception)
            {
                bool rollbackOk = false;
                try
                {
                    rollbackOk = rollback != null &&
                                 rollback.TryRestore(decoration) &&
                                 rollback.Matches(decoration);
                }
                catch
                {
                    rollbackOk = false;
                }

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration history restore failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? $"{context}: restore failed; the decoration was rolled back."
                    : $"{context}: restore failed and rollback was incomplete; see the log.");
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
            try
            {
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
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration batch history capture failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add($"{context}: current decoration state could not be captured safely.");
                return false;
            }

            try
            {
                for (int index = 0; index < decorations.Length; index++)
                {
                    if (!snapshots[index].TryRestore(decorations[index]) ||
                        !snapshots[index].Matches(decorations[index]))
                        throw new InvalidOperationException("FTD rejected a decoration snapshot restore.");
                }
            }
            catch (Exception exception)
            {
                bool rollbackOk = true;
                for (int rollbackIndex = decorations.Length - 1; rollbackIndex >= 0; rollbackIndex--)
                {
                    try
                    {
                        if (!rollback[rollbackIndex].TryRestore(decorations[rollbackIndex]) ||
                            !rollback[rollbackIndex].Matches(decorations[rollbackIndex]))
                            rollbackOk = false;
                    }
                    catch
                    {
                        rollbackOk = false;
                    }
                }

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration batch history restore failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? $"{context}: restore failed; every target was rolled back."
                    : $"{context}: restore failed and rollback was incomplete; see the log.");
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

            var manager = construct?.Decorations as AllConstructDecorations;
            Decoration removed = decoration;
            DecorationEditSnapshot rollback;
            try
            {
                if (manager == null ||
                    !ReferenceEquals(removed.OurManager, manager) ||
                    !TryIsDecorationInManager(manager, removed, out bool isMember) ||
                    !isMember)
                {
                    InfoStore.Add("Undo failed because the created decoration is no longer on its manager.");
                    return false;
                }

                rollback = new DecorationEditSnapshot(removed);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation undo preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Undo failed because the created decoration could not be captured safely.");
                return false;
            }

            try
            {
                removed.Delete();
                _transactions.UnmarkCreated(removed);
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
                bool rollbackOk = TryRestoreAfterFailedDecorationDeletion(
                    construct,
                    manager,
                    removed,
                    rollback,
                    original: null,
                    createdInSession: true,
                    "Creation undo rollback",
                    out Decoration restored);
                if (restored != null)
                    decoration = restored;
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation undo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? "Undo delete failed; the created decoration was restored."
                    : "Undo delete failed and restoration was incomplete; see the log.");
                return false;
            }
        }

        internal bool TryUndoCreatedDecorationBatch(
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] created,
            int primaryIndex,
            string context)
        {
            var manager = construct?.Decorations as AllConstructDecorations;
            if (manager == null ||
                decorations == null ||
                created == null ||
                decorations.Length == 0 ||
                decorations.Length != created.Length ||
                primaryIndex < 0 ||
                primaryIndex >= decorations.Length)
            {
                InfoStore.Add(context + ": creation history is incomplete.");
                return false;
            }

            var uniqueDecorations = new HashSet<Decoration>();
            try
            {
                for (int index = 0; index < decorations.Length; index++)
                {
                    Decoration decoration = decorations[index];
                    if (decoration == null ||
                        decoration.IsDeleted ||
                        created[index] == null ||
                        !ReferenceEquals(decoration.OurManager, manager) ||
                        !uniqueDecorations.Add(decoration))
                    {
                        InfoStore.Add(context + ": a created decoration is stale or belongs to another manager.");
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation batch preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(context + ": a created decoration became unavailable during preflight.");
                return false;
            }

            var originals = (Decoration[])decorations.Clone();
            try
            {
                for (int index = decorations.Length - 1; index >= 0; index--)
                    originals[index].Delete();
            }
            catch (Exception exception)
            {
                bool classified = TryFindDeletedDecorationIndexes(
                    manager,
                    originals,
                    out List<int> deletedIndexes);
                bool detachedFinalized = classified;
                for (int step = 0; classified && step < deletedIndexes.Count; step++)
                {
                    int index = deletedIndexes[step];
                    if (!TryFinalizeDetachedDecoration(
                            manager,
                            originals[index],
                            context + " finalize detached decoration"))
                    {
                        detachedFinalized = false;
                    }
                    _transactions.UnmarkCreated(originals[index]);
                    decorations[index] = null;
                }
                bool restored = classified &&
                                TryRestoreUndoneCreationBatch(
                                    construct,
                                    decorations,
                                    created,
                                    deletedIndexes,
                                    primaryIndex);
                bool rollbackOk = classified && detachedFinalized && restored;
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation batch undo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? context + ": delete failed; the batch was restored."
                    : context + ": delete failed and rollback was incomplete; see the log.");
                return false;
            }

            for (int index = 0; index < originals.Length; index++)
            {
                _transactions.UnmarkCreated(originals[index]);
                decorations[index] = null;
            }

            _selection.Clear();
            _selected = null;
            _selectedConstruct = null;
            _snapshot = null;
            _selectedCreatedInSession = false;
            ClearMultiTransformState();
            UpdateDirtyFromSelection();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return true;
        }

        private bool TryRestoreUndoneCreationBatch(
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] created,
            IReadOnlyList<int> deletedIndexes,
            int primaryIndex)
        {
            bool ok = true;
            for (int step = deletedIndexes.Count - 1; step >= 0; step--)
            {
                int index = deletedIndexes[step];
                try
                {
                    if (TryCreateDecorationFromSnapshot(
                            construct,
                            created[index],
                            "Creation undo rollback",
                            out Decoration restored))
                    {
                        decorations[index] = restored;
                        _transactions.MarkCreated(restored);
                    }
                    else
                    {
                        ok = false;
                    }
                }
                catch (Exception exception)
                {
                    ok = false;
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Decoration creation undo rollback failed",
                        exception,
                        LogOptions._AlertDevAndCustomerInGame);
                }
            }

            var live = new List<Decoration>(decorations.Length);
            int livePrimaryIndex = 0;
            for (int index = 0; index < decorations.Length; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null || decoration.IsDeleted)
                    continue;
                if (index == primaryIndex)
                    livePrimaryIndex = live.Count;
                live.Add(decoration);
            }
            if (live.Count > 0)
                SelectDecorationGroup(live, construct, livePrimaryIndex, createdInSession: true);
            return ok;
        }

        private static bool TryFindDeletedDecorationIndexes(
            AllConstructDecorations manager,
            IReadOnlyList<Decoration> decorations,
            out List<int> deleted)
        {
            deleted = new List<int>(decorations?.Count ?? 0);
            if (manager == null || decorations == null)
                return false;

            var liveDecorations = new List<Decoration>();
            try
            {
                foreach (Decoration live in manager.DecorationList)
                {
                    if (live != null)
                        liveDecorations.Add(live);
                }
            }
            catch
            {
                return false;
            }

            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                bool isMember = false;
                for (int liveIndex = 0; liveIndex < liveDecorations.Count; liveIndex++)
                {
                    if (!ReferenceEquals(liveDecorations[liveIndex], decoration))
                        continue;
                    isMember = true;
                    break;
                }

                if (!isMember)
                {
                    deleted.Add(index);
                    continue;
                }

                try
                {
                    if (decoration == null || decoration.IsDeleted)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        internal bool TryRedoCreatedDecorationBatch(
            AllConstruct construct,
            Decoration[] decorations,
            DecorationEditSnapshot[] created,
            int primaryIndex,
            string context)
        {
            var manager = construct?.Decorations as AllConstructDecorations;
            if (manager == null ||
                decorations == null ||
                created == null ||
                decorations.Length == 0 ||
                decorations.Length != created.Length ||
                primaryIndex < 0 ||
                primaryIndex >= decorations.Length)
            {
                InfoStore.Add(context + ": creation history is incomplete.");
                return false;
            }

            if (!TryPreflightDecorationSnapshotBatch(
                    construct,
                    manager,
                    created,
                    context,
                    out string failure))
            {
                InfoStore.Add(failure);
                return false;
            }

            var recreated = new List<int>(created.Length);
            try
            {
                for (int index = 0; index < created.Length; index++)
                {
                    Decoration decoration = manager.NewDecoration(
                        created[index].TetherPoint,
                        force: true,
                        playSound: false,
                        forceEvenIfMaxReached: true);
                    if (decoration == null)
                        throw new InvalidOperationException("FTD rejected a recreated decoration.");

                    decorations[index] = decoration;
                    recreated.Add(index);
                    if (!created[index].TryRestore(decoration) ||
                        !created[index].Matches(decoration))
                    {
                        throw new InvalidOperationException("FTD rejected a recreated decoration.");
                    }
                }
            }
            catch (Exception exception)
            {
                bool cleanupOk = CleanupCreatedDecorationBatch(decorations, recreated, context + " cleanup");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration creation batch redo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(cleanupOk
                    ? context + ": recreate failed; no decorations were kept."
                    : context + ": recreate failed and cleanup was incomplete; see the log.");
                return false;
            }

            for (int index = 0; index < decorations.Length; index++)
                _transactions.MarkCreated(decorations[index]);
            SelectDecorationGroup(decorations, construct, primaryIndex, createdInSession: true);
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return true;
        }

        internal bool TryRedoCreatedDecoration(
            AllConstruct construct,
            DecorationEditSnapshot snapshot,
            out Decoration decoration)
        {
            if (!TryCreateDecorationFromSnapshot(
                    construct,
                    snapshot,
                    "Creation redo",
                    out decoration))
                return false;

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

            var manager = construct?.Decorations as AllConstructDecorations;
            Decoration removed = decoration;
            DecorationEditSnapshot rollback;
            try
            {
                if (manager == null ||
                    !ReferenceEquals(removed.OurManager, manager) ||
                    !TryIsDecorationInManager(manager, removed, out bool isMember) ||
                    !isMember)
                {
                    InfoStore.Add("Delete redo failed because the decoration is no longer on its manager.");
                    return false;
                }

                rollback = new DecorationEditSnapshot(removed);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration delete redo preflight failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add("Delete redo failed because the decoration could not be captured safely.");
                return false;
            }

            try
            {
                removed.Delete();
            }
            catch (Exception exception)
            {
                bool rollbackOk = TryRestoreAfterFailedDecorationDeletion(
                    construct,
                    manager,
                    removed,
                    rollback,
                    original,
                    createdInSession,
                    "Delete redo rollback",
                    out Decoration restored);
                if (restored != null)
                    decoration = restored;
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration delete redo failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? "Delete redo failed; the decoration was restored."
                    : "Delete redo failed and restoration was incomplete; see the log.");
                return false;
            }

            if (createdInSession)
                _transactions.UnmarkCreated(removed);
            else
                TrackDeletedDecoration(construct, original, deleted, removed);

            ClearDeletedSelection(removed);
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

            if (TryHandleSelectionFocusRequest(decoration, construct))
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
            DrawSurfaceCoordinateHoverOverlay();
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
                DrawMultiSelectionBoundsGuide(selectedWidth);
                DecorationEditorOverlay.Circle(pivotWorld, 0.28f * selectedScale, new Color(0.1f, 0.95f, 1f, 1f), Vector3.up, selectedWidth, 24);
                DecorationEditorOverlay.Cross(pivotWorld, 0.34f * selectedScale, new Color(0.1f, 0.95f, 1f, 1f), selectedWidth);
            }

            if (_tool == DecorationEditorTool.Move)
            {
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.X, multiTransform);
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.Y, multiTransform);
                DrawAxis(gizmoCenterLocal, DecorationEditAxis.Z, multiTransform);
                DecorationEditAxis freePick = DecorationEditAxis.None;
                bool freeHovered = GizmoHoverAllowed() &&
                                   (multiTransform
                                       ? TryPickGroupMoveHandle(gizmoCenterLocal, _lastMouseGui, out freePick)
                                       : TryPickMoveHandle(_selected, _selectedConstruct, _lastMouseGui, out freePick)) &&
                                   freePick == DecorationEditAxis.Free;
                if (_dragAxis == DecorationEditAxis.Free || freeHovered)
                {
                    DecorationEditorOverlay.Circle(
                        _selectedConstruct.SafeLocalToGlobal(gizmoCenterLocal),
                        0.34f * selectedScale,
                        freeHovered ? Color.white : Color.yellow,
                        Vector3.up,
                        EsuGizmoSettings.Current.LineWidth(4f),
                        28);
                }
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

            DrawSelectedSetAnchorConnections();
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

        private void DrawSelectedSetAnchorConnections()
        {
            if (_selectedConstruct == null)
                return;

            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            if (decorations.Count <= 1)
                return;

            var groups = new Dictionary<string, List<Decoration>>();
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null || decoration.IsDeleted)
                    continue;

                string key = FormatTether(decoration.TetherPoint.Us);
                if (!groups.TryGetValue(key, out List<Decoration> group))
                {
                    group = new List<Decoration>();
                    groups[key] = group;
                }

                group.Add(decoration);
            }

            int drawn = 0;
            Color anchorColor = new Color(0.1f, 0.95f, 1f, 0.86f);
            Color lineColor = new Color(0.1f, 0.95f, 1f, 0.72f);
            foreach (List<Decoration> group in groups.Values)
            {
                if (group.Count <= 1)
                    continue;

                Vector3i tether = group[0].TetherPoint.Us;
                Vector3 anchorWorld = _selectedConstruct.SafeLocalToGlobal(ToVector3(tether));
                DecorationEditorOverlay.Circle(anchorWorld, 0.24f, anchorColor, Vector3.up, 2.4f, 16);
                DecorationEditorOverlay.Cross(anchorWorld, 0.29f, anchorColor, 2.2f);

                for (int index = 0; index < group.Count; index++)
                {
                    Decoration decoration = group[index];
                    if (decoration == null ||
                        decoration.IsDeleted ||
                        ReferenceEquals(decoration, _selected))
                    {
                        continue;
                    }

                    Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(GetDecorationLocalCenter(decoration));
                    DecorationEditorOverlay.Line(anchorWorld, centerWorld, lineColor, 2.1f);
                    DecorationEditorOverlay.Circle(centerWorld, 0.16f, lineColor, Vector3.up, 1.9f, 12);
                    drawn++;
                    if (drawn >= MaxWorldHintLines)
                        return;
                }
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
            DrawSymmetryPlacementGhostHints(color);
        }

        private void DrawSymmetryPlacementGhostHints(Color color)
        {
            if (!_placementValid ||
                !DecoLimitLifter.EsuSymmetry.HasActivePlanes ||
                !DecoLimitLifter.EsuSymmetry.CanUseWith(_placementConstruct, out string _))
            {
                return;
            }

            Vector3 originalPositioning = DecorationEditMath.Snap(
                _placementLocalPosition - ToVector3(_placementAnchor));
            if (!TryBuildDecorationPlacementPlans(
                    originalPositioning,
                    reportMessages: false,
                    out List<DecorationPlacementPlan> plans))
            {
                return;
            }

            for (int index = 0; index < plans.Count; index++)
            {
                DecorationPlacementPlan plan = plans[index];
                Vector3 centerLocal = ToVector3(plan.Anchor) + plan.Positioning;
                if ((centerLocal - _placementLocalPosition).sqrMagnitude <= 0.0001f &&
                    plan.Anchor.Equals(_placementAnchor))
                {
                    continue;
                }

                Vector3 anchorWorld = _placementConstruct.SafeLocalToGlobal(ToVector3(plan.Anchor));
                Vector3 centerWorld = _placementConstruct.SafeLocalToGlobal(centerLocal);
                DecorationEditorOverlay.Circle(anchorWorld, 0.35f, color, Vector3.up, 3f, 24);
                DecorationEditorOverlay.Cross(anchorWorld, 0.42f, color, 3f);
                DecorationEditorOverlay.Line(anchorWorld, centerWorld, color, 2f);
                DecorationEditorOverlay.Circle(centerWorld, 0.2f, color, Vector3.up, 3f, 18);
            }
        }

        private Color SurfaceDraftPaintColor(int colorIndex, float alpha)
        {
            Color color = PaintPreviewColor(Mathf.Clamp(colorIndex, 0, 31));
            if (!PaintColorApplies(color))
                color = new Color(0.42f, 0.48f, 0.56f, 1f);
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private bool DrawSurfacePlanMeshPreview(SurfaceDecorationPlan plan)
        {
            if (plan?.Construct == null ||
                plan.Placements == null ||
                plan.Placements.Count == 0)
            {
                return false;
            }

            bool drewAny = false;
            bool drewAll = true;
            for (int index = 0; index < plan.Placements.Count; index++)
            {
                SurfaceDecorationPlacement placement = plan.Placements[index];
                if (!_meshByGuid.TryGetValue(placement.MeshGuid, out DecorationMeshCatalogEntry entry))
                {
                    drewAll = false;
                    continue;
                }

                SurfacePreviewResource resource = SurfacePreviewResourceFor(
                    plan.Construct,
                    placement,
                    entry);
                if (resource?.Mesh == null || resource.Material == null)
                {
                    drewAll = false;
                    continue;
                }

                Graphics.DrawMesh(
                    resource.Mesh,
                    SurfacePlacementMatrix(plan.Construct, placement),
                    resource.Material,
                    0);
                drewAny = true;
            }

            // If admission was intentionally capped, keep the lightweight face
            // fill visible beneath the admitted meshes instead of pretending the
            // incomplete mesh batch represented the whole plan.
            return drewAny && drewAll;
        }

        private SurfacePreviewResource SurfacePreviewResourceFor(
            AllConstruct construct,
            SurfaceDecorationPlacement placement,
            DecorationMeshCatalogEntry entry)
        {
            Mesh sourceMesh = _previewRenderer?.GetMesh(entry);
            if (sourceMesh == null)
                return null;

            Color nativeColor = NativeDecorationPreviewColor(
                construct,
                placement.Anchor,
                placement.Color);
            object palette = null;
            try { palette = construct?.Main?.ColorsRestricted; }
            catch { }
            int paletteIdentity = palette == null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(palette);
            string key = SurfacePreviewResourceKey(
                placement,
                nativeColor,
                paletteIdentity);
            int currentFrame = Time.frameCount;
            if (_surfacePreviewResources.TryGetValue(key, out SurfacePreviewResource cached))
            {
                if (cached?.Mesh != null && cached.Material != null)
                {
                    cached.LastUsedFrame = currentFrame;
                    return cached;
                }

                ReleaseSurfacePreviewResource(cached);
                _surfacePreviewResources.Remove(key);
            }

            if (!TryReserveSurfacePreviewResourceSlot(currentFrame))
                return null;

            Material sourceMaterial = _previewRenderer?.GetMaterial(entry);
            Mesh previewMesh = null;
            bool ownsMesh = false;
            try
            {
                if (SurfacePreviewMeshCanAcceptVertexColors(sourceMesh))
                {
                    previewMesh = UnityEngine.Object.Instantiate(sourceMesh);
                    previewMesh.name = "ESU surface preview mesh " + key;
                    previewMesh.hideFlags = HideFlags.HideAndDontSave;
                    var vertexColors = new Color[previewMesh.vertexCount];
                    for (int index = 0; index < vertexColors.Length; index++)
                        vertexColors[index] = nativeColor;
                    previewMesh.colors = vertexColors;
                    ownsMesh = true;
                }
            }
            catch (Exception exception)
            {
                ReleasePlacementGhostObject(previewMesh);
                previewMesh = null;
                ownsMesh = false;
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Surface preview vertex colors unavailable",
                    exception,
                    LogOptions._AlertDevInGame);
            }

            bool ownsMaterial = false;
            Material material = null;
            if (previewMesh == null)
            {
                // Imported catalog meshes can be marked non-readable. Render the
                // original mesh through a private tinted material rather than
                // throwing every frame while attempting to replace vertex colors.
                previewMesh = sourceMesh;
                ownsMesh = false;
                try
                {
                    material = sourceMaterial != null
                        ? new Material(sourceMaterial)
                        : CreatePlacementGhostMaterial();
                    if (material != null)
                    {
                        material.name = "ESU unreadable surface preview material " + key;
                        material.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
                catch
                {
                    if (!ReferenceEquals(material, sourceMaterial))
                        ReleasePlacementGhostObject(material);
                    material = CreatePlacementGhostMaterial();
                }

                ownsMaterial = true;
                if (material != null)
                {
                    try
                    {
                        SetPlacementGhostColor(material, nativeColor);
                    }
                    catch
                    {
                        ReleasePlacementGhostObject(material);
                        material = null;
                    }
                }
            }
            else
            {
                ownsMaterial = sourceMaterial == null;
                material = sourceMaterial ?? CreatePlacementGhostMaterial();
            }

            if (material == null)
            {
                if (ownsMesh)
                    ReleasePlacementGhostObject(previewMesh);
                return null;
            }

            if (ownsMaterial && ownsMesh)
            {
                try
                {
                    SetPlacementGhostColor(
                        material,
                        SurfaceFallbackMaterialPreviewColor(
                            placement.StructureBlockType,
                            nativeColor));
                }
                catch
                {
                    ReleasePlacementGhostObject(material);
                    ReleasePlacementGhostObject(previewMesh);
                    return null;
                }
            }

            var resource = new SurfacePreviewResource(
                previewMesh,
                material,
                ownsMesh,
                ownsMaterial,
                currentFrame);
            _surfacePreviewResources[key] = resource;
            return resource;
        }

        internal static bool SurfacePreviewMeshCanAcceptVertexColors(Mesh mesh)
        {
            if (ReferenceEquals(mesh, null))
                return false;
            try
            {
                return mesh.isReadable;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReserveSurfacePreviewResourceSlot(int currentFrame)
        {
            if (_surfacePreviewResources.Count < MaxSurfacePreviewResources)
                return true;

            string oldestKey = null;
            SurfacePreviewResource oldest = null;
            foreach (KeyValuePair<string, SurfacePreviewResource> pair in _surfacePreviewResources)
            {
                if (pair.Value == null ||
                    pair.Value.Mesh == null ||
                    pair.Value.Material == null)
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                    break;
                }

                if (oldest == null ||
                    (pair.Value != null &&
                     pair.Value.LastUsedFrame < oldest.LastUsedFrame))
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
            }

            int oldestFrame = oldest?.LastUsedFrame ?? int.MinValue;
            if (!CanAdmitSurfacePreviewResource(
                    _surfacePreviewResources.Count,
                    MaxSurfacePreviewResources,
                    oldestFrame,
                    currentFrame))
            {
                return false;
            }

            if (oldestKey != null)
            {
                _surfacePreviewResources.Remove(oldestKey);
                ReleaseSurfacePreviewResource(oldest);
            }

            return _surfacePreviewResources.Count < MaxSurfacePreviewResources;
        }

        internal static bool CanAdmitSurfacePreviewResource(
            int currentCount,
            int maximumCount,
            int oldestLastUsedFrame,
            int currentFrame)
        {
            if (maximumCount <= 0 || currentCount < 0)
                return false;
            if (currentCount < maximumCount)
                return true;
            if (currentCount > maximumCount)
                return false;

            // Protect resources used in this or the immediately previous frame.
            // That makes an over-cap stable plan retain the same admitted subset
            // instead of evicting and rebuilding all meshes on every frame.
            return (long)oldestLastUsedFrame < (long)currentFrame - 1L;
        }

        internal static string SurfacePreviewResourceKey(
            SurfaceDecorationPlacement placement,
            Color nativeColor,
            int paletteIdentity)
        {
            Color32 rgba = nativeColor;
            return placement.MeshGuid.ToString("N") + "|" +
                   ((int)placement.StructureBlockType).ToString(CultureInfo.InvariantCulture) + "|" +
                   Mathf.Clamp(placement.Color, 0, 31).ToString(CultureInfo.InvariantCulture) + "|" +
                   paletteIdentity.ToString("X8", CultureInfo.InvariantCulture) + "|" +
                   rgba.r.ToString("X2", CultureInfo.InvariantCulture) +
                   rgba.g.ToString("X2", CultureInfo.InvariantCulture) +
                   rgba.b.ToString("X2", CultureInfo.InvariantCulture) +
                   rgba.a.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static Color SurfaceFallbackMaterialPreviewColor(
            StructureBlockType materialType,
            Color nativeColor)
        {
            Color materialColor;
            switch (materialType)
            {
                case StructureBlockType.Alloy:
                    materialColor = new Color(0.68f, 0.78f, 0.8f, 1f);
                    break;
                case StructureBlockType.Glass:
                    materialColor = new Color(0.58f, 0.9f, 1f, 1f);
                    break;
                case StructureBlockType.Lead:
                    materialColor = new Color(0.32f, 0.34f, 0.42f, 1f);
                    break;
                case StructureBlockType.HeavyArmour:
                    materialColor = new Color(0.18f, 0.2f, 0.24f, 1f);
                    break;
                case StructureBlockType.Rubber:
                    materialColor = new Color(0.04f, 0.05f, 0.05f, 1f);
                    break;
                case StructureBlockType.Stone:
                    materialColor = new Color(0.58f, 0.62f, 0.62f, 1f);
                    break;
                case StructureBlockType.Metal:
                    materialColor = new Color(0.42f, 0.48f, 0.56f, 1f);
                    break;
                default:
                    materialColor = new Color(0.62f, 0.42f, 0.24f, 1f);
                    break;
            }

            Color color = PaintColorApplies(nativeColor)
                ? nativeColor
                : materialColor;
            color.a = materialType == StructureBlockType.Glass ? 0.42f : 0.62f;
            return color;
        }

        private static Matrix4x4 SurfacePlacementMatrix(
            AllConstruct construct,
            SurfaceDecorationPlacement placement)
        {
            Vector3 centerLocal = ToVector3(placement.Anchor) + placement.Positioning;
            Quaternion localRotation = Quaternion.Euler(placement.Orientation);
            try
            {
                return Matrix4x4.TRS(
                    construct.SafeLocalToGlobal(centerLocal),
                    ConstructRotation(construct) * localRotation,
                    placement.Scaling);
            }
            catch
            {
            }

            Matrix4x4 localPlacement = Matrix4x4.TRS(
                centerLocal,
                localRotation,
                placement.Scaling);
            try
            {
                if (construct?.myTransform != null)
                    return construct.myTransform.localToWorldMatrix * localPlacement;
            }
            catch
            {
            }

            return localPlacement;
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
            bool drewPreviewMeshes = hasPreview && DrawSurfacePlanMeshPreview(_surfacePlan);
            bool drawDraftFill = !hasPreview || !drewPreviewMeshes;

            for (int index = 0; index < _surfaceDraft.Faces.Count; index++)
            {
                SurfaceFace face = _surfaceDraft.Faces[index];
                Vector3 a = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.A]);
                Vector3 b = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.B]);
                Vector3 c = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.C]);
                if (drawDraftFill)
                {
                    Color fill = SurfaceDraftPaintColor(
                        _surfaceDraft.FaceStyleAt(index).ColorIndex,
                        hasPreview ? 0.52f : 0.68f);
                    Color faceFill = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                                     _surfaceDraft.SelectedFace == index
                        ? Color.Lerp(fill, new Color(selected.r, selected.g, selected.b, 0.34f), 0.45f)
                        : fill;
                    DecorationEditorOverlay.Quad(a, b, c, c, faceFill);
                }

                DrawSurfaceEdge(face.A, face.B, edgeColor);
                DrawSurfaceEdge(face.B, face.C, edgeColor);
                DrawSurfaceEdge(face.C, face.A, edgeColor);
            }

            DrawMirroredSurfaceOverlay(
                construct,
                hasPreview,
                drawDraftFill,
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
                bool freeHovered = GizmoHoverAllowed() &&
                                   TryPickSurfaceHandle(_lastMouseGui, out DecorationEditAxis picked) &&
                                   picked == DecorationEditAxis.Free;
                if (_surfaceDragAxis == DecorationEditAxis.Free || freeHovered)
                {
                    DecorationEditorOverlay.Circle(
                        construct.SafeLocalToGlobal(center),
                        0.32f,
                        freeHovered ? Color.white : selected,
                        Vector3.up,
                        EsuGizmoSettings.Current.LineWidth(4f),
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
            bool drawFill,
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
                    if (drawFill)
                    {
                        Color fill = SurfaceDraftPaintColor(
                            mirrored.FaceStyleAt(index).ColorIndex,
                            hasPreview ? 0.4f : 0.52f);
                        DecorationEditorOverlay.Quad(a, b, c, c, fill);
                    }

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

            SurfaceDecorationPlan plan = _surfacePlan;
            if (plan == null &&
                !TryBuildSurfacePlan(out plan, out _))
            {
                return;
            }

            if (plan == null || plan.Placements.Count == 0)
                return;

            DrawSameAnchorPreview(
                plan.Construct,
                plan.Placements.Select(placement => new PlannedAnchorLine(
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
            bool hovered = GizmoHoverAllowed() &&
                           TryPickSharedAnchorHandle(
                               construct,
                               new Vector3i(
                                   Mathf.RoundToInt(anchorLocal.x),
                                   Mathf.RoundToInt(anchorLocal.y),
                                   Mathf.RoundToInt(anchorLocal.z)),
                               _lastMouseGui,
                               out DecorationEditAxis picked,
                               out int pickedSign) &&
                           picked == axis &&
                           pickedSign == sign;
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(
                dragging && _sharedAnchorDragAxis == axis && _sharedAnchorDragSign == sign || hovered ? 4f : 2.5f);
            Vector3 start = construct.SafeLocalToGlobal(anchorLocal);
            Vector3 end = construct.SafeLocalToGlobal(anchorLocal + axisVector * style.AnchorLength);
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
            IReadOnlyList<DecorationGeneratorSegment> baseSegments =
                Array.Empty<DecorationGeneratorSegment>();
            bool symmetryActive = DecoLimitLifter.EsuSymmetry.HasActivePlanes;
            bool canDrawSymmetry = symmetryActive &&
                                   DecoLimitLifter.EsuSymmetry.CanUseWith(construct, out string _);
            IReadOnlyList<DecorationGeneratorSegment> symmetrySegments =
                Array.Empty<DecorationGeneratorSegment>();
            bool wiresValid = symmetryActive
                ? canDrawSymmetry &&
                  DecorationGeneratorPlanner.TryBuildSymmetryPreviewSegments(
                      _generatorDraft,
                      _generatorSettings,
                      new ConstructSurfaceAnchorResolver(construct),
                      DecoLimitLifter.EsuSymmetry.Variants(),
                      out baseSegments,
                      out symmetrySegments,
                      out string _)
                : DecorationGeneratorPlanner.TryBuildPreviewSegments(
                      _generatorDraft,
                      _generatorSettings,
                      out baseSegments,
                      out string _);

            if (wiresValid && canDrawSymmetry)
            {
                DrawGeneratorSegments(
                    construct,
                    symmetrySegments,
                    SurfaceDraftPaintColor(
                        _generatorSettings.ColorIndex,
                        _generatorPlan == null ? 0.42f : 0.52f));
            }

            DrawGeneratorDraftOverlay(
                _generatorDraft,
                wiresValid ? baseSegments : Array.Empty<DecorationGeneratorSegment>(),
                lineColor,
                pointColor,
                selected,
                drawHandles: true);
            if (canDrawSymmetry)
                DrawMirroredGeneratorPointOverlay(construct);
            DrawGeneratorSameAnchorPreview();
        }

        private void DrawSurfaceCoordinateHoverOverlay()
        {
            if (_surfaceCoordinateHoverPointIndex < 0 ||
                !_showSurfaceCoordinates)
            {
                return;
            }

            AllConstruct construct;
            Vector3 point;
            if (_surfaceCoordinateHoverGenerator)
            {
                if (_generatorDraft.Construct == null ||
                    _surfaceCoordinateHoverPointIndex >= _generatorDraft.PointCount)
                {
                    return;
                }

                construct = _generatorDraft.Construct;
                point = _generatorDraft.PointAt(_surfaceCoordinateHoverPointIndex);
            }
            else
            {
                if (_surfaceDraft.Construct == null ||
                    _surfaceCoordinateHoverPointIndex >= _surfaceDraft.Points.Count)
                {
                    return;
                }

                construct = _surfaceDraft.Construct;
                point = _surfaceDraft.Points[_surfaceCoordinateHoverPointIndex];
            }

            if (!DecorationEditMath.IsFinite(point))
                return;

            Vector3 world = construct.SafeLocalToGlobal(point);
            Color highlight = new Color(1f, 0.68f, 0.08f, 1f);
            DecorationEditorOverlay.Circle(
                world,
                0.34f,
                highlight,
                Vector3.up,
                4.8f,
                28);
            DecorationEditorOverlay.Circle(
                world,
                0.43f,
                Color.white,
                Vector3.up,
                2.2f,
                28);
            DecorationEditorOverlay.Cross(world, 0.42f, highlight, 4.4f);
        }

        private void DrawGeneratorDraftOverlay(
            DecorationGeneratorDraft draft,
            IReadOnlyList<DecorationGeneratorSegment> segments,
            Color lineColor,
            Color pointColor,
            Color selected,
            bool drawHandles)
        {
            if (draft == null || draft.Construct == null)
                return;

            DrawGeneratorSegments(draft.Construct, segments, lineColor);

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
                bool freeHovered = GizmoHoverAllowed() &&
                                   TryPickGeneratorHandle(_lastMouseGui, out DecorationEditAxis picked) &&
                                   picked == DecorationEditAxis.Free;
                if (_generatorDragAxis == DecorationEditAxis.Free || freeHovered)
                {
                    DecorationEditorOverlay.Circle(
                        draft.Construct.SafeLocalToGlobal(center),
                        0.32f,
                        freeHovered ? Color.white : selected,
                        Vector3.up,
                        EsuGizmoSettings.Current.LineWidth(4f),
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

        private void DrawMirroredGeneratorPointOverlay(AllConstruct construct)
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
                Color color = SurfaceDraftPaintColor(
                    _generatorSettings.ColorIndex,
                    _generatorPlan == null ? 0.42f : 0.52f);
                DrawGeneratorDraftOverlay(
                    mirrored,
                    Array.Empty<DecorationGeneratorSegment>(),
                    color,
                    color,
                    color,
                    drawHandles: false);
            }
        }

        private void DrawGeneratorAxis(Vector3 centerLocal, DecorationEditAxis axis)
        {
            if (_generatorDraft.Construct == null)
                return;

            bool hovered = GizmoHoverAllowed() &&
                           TryPickGeneratorHandle(_lastMouseGui, out DecorationEditAxis picked) &&
                           picked == axis;
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(_generatorDragAxis == axis || hovered ? 4f : 2.5f);
            Vector3 start = _generatorDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _generatorDraft.Construct.SafeLocalToGlobal(
                centerLocal + DecorationEditMath.AxisVector(axis) * style.MoveLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
        }

        private void DrawGeneratorRotateGizmo(Vector3 centerLocal)
        {
            if (_generatorDraft.Construct == null)
                return;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            DecorationEditAxis hoverAxis = DecorationEditAxis.None;
            if (GizmoHoverAllowed())
                TryPickGeneratorRotateRing(_lastMouseGui, out hoverAxis);
            foreach (DecorationEditAxis axis in new[] { DecorationEditAxis.X, DecorationEditAxis.Y, DecorationEditAxis.Z })
            {
                bool hovered = hoverAxis == axis;
                Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
                float width = style.LineWidth(_generatorRotateDragAxis == axis || hovered ? 4.4f : 2.4f);
                Vector3 normalWorld = GeneratorNormalWorld(_generatorDraft.Construct, DecorationEditMath.AxisVector(axis));
                DecorationEditorOverlay.Circle(
                    _generatorDraft.Construct.SafeLocalToGlobal(centerLocal),
                    style.RotationRadius,
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

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            bool hovered = GizmoHoverAllowed() && TryPickGeneratorScaleHandle(_lastMouseGui);
            Color color = GizmoHoverColor(new Color(0.1f, 1f, 0.2f, 0.95f), hovered);
            Vector3 center = _generatorDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _generatorDraft.Construct.SafeLocalToGlobal(
                centerLocal + _generatorDraft.CircleTangentA * style.ScaleLength);
            DecorationEditorOverlay.Arrow(
                center,
                end,
                color,
                style.LineWidth(_generatorRotateDragAxis != DecorationEditAxis.None || hovered ? 4.4f : 2.8f),
                0.18f);
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

            if (!TryProjectGizmo(_selectedConstruct, center, out Vector2 origin))
                return false;

            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float best = style.HitAreaPixels;
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
                                    style.RotationRadius;
                    if (!TryProjectGizmo(_selectedConstruct, local, out Vector2 projected))
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

            bool hovered = GizmoHoverAllowed() &&
                           TryPickSurfaceHandle(_lastMouseGui, out DecorationEditAxis picked) &&
                           picked == axis;
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(_surfaceDragAxis == axis || hovered ? 4f : 2.5f);
            Vector3 start = _surfaceDraft.Construct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _surfaceDraft.Construct.SafeLocalToGlobal(
                centerLocal + DecorationEditMath.AxisVector(axis) * style.MoveLength);
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
            DecorationPlacementTemplate template = CurrentPlacementTemplate();
            ApplyPlacementGhostTransform(
                _placementGhost.transform,
                centerLocal,
                template.Orientation,
                template.Scaling);
            if (!_placementGhost.activeSelf)
                _placementGhost.SetActive(true);
            UpdateSymmetryPlacementGhosts(mesh, color, centerLocal);
        }

        private void UpdateSymmetryPlacementGhosts(
            Mesh mesh,
            Color color,
            Vector3 primaryCenterLocal)
        {
            if (mesh == null ||
                !_placementValid ||
                !DecoLimitLifter.EsuSymmetry.HasActivePlanes ||
                !DecoLimitLifter.EsuSymmetry.CanUseWith(_placementConstruct, out string _))
            {
                HideSymmetryPlacementGhosts();
                return;
            }

            Vector3 originalPositioning = DecorationEditMath.Snap(
                _placementLocalPosition - ToVector3(_placementAnchor));
            if (!TryBuildDecorationPlacementPlans(
                    originalPositioning,
                    reportMessages: false,
                    out List<DecorationPlacementPlan> plans))
            {
                HideSymmetryPlacementGhosts();
                return;
            }

            int visible = 0;
            for (int index = 0; index < plans.Count; index++)
            {
                DecorationPlacementPlan plan = plans[index];
                Vector3 centerLocal = ToVector3(plan.Anchor) + plan.Positioning;
                if ((centerLocal - primaryCenterLocal).sqrMagnitude <= 0.0001f &&
                    plan.Anchor.Equals(_placementAnchor))
                {
                    continue;
                }

                PlacementGhostInstance ghost = EnsureSymmetryPlacementGhost(visible);
                if (ghost == null ||
                    ghost.Filter == null ||
                    ghost.Renderer == null)
                {
                    continue;
                }

                if (!ReferenceEquals(ghost.Entry, _placingMesh))
                    ghost.Entry = _placingMesh;
                if (ghost.Filter.sharedMesh != mesh)
                    ghost.Filter.sharedMesh = mesh;
                SetPlacementGhostColor(ghost.Material, color);
                ApplyPlacementGhostTransform(
                    ghost.GameObject.transform,
                    centerLocal,
                    plan.Orientation,
                    plan.Scaling);
                if (!ghost.GameObject.activeSelf)
                    ghost.GameObject.SetActive(true);
                visible++;
            }

            HideSymmetryPlacementGhosts(startIndex: visible);
        }

        private void ApplyPlacementGhostTransform(
            Transform target,
            Vector3 centerLocal,
            Vector3 orientation,
            Vector3 scaling)
        {
            if (target == null)
                return;

            target.position = _placementConstruct.SafeLocalToGlobal(centerLocal);
            target.rotation = ConstructRotation(_placementConstruct) * Quaternion.Euler(orientation);
            target.localScale = IsValidScale(scaling) ? scaling : Vector3.one;
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

        private PlacementGhostInstance EnsureSymmetryPlacementGhost(int index)
        {
            while (_symmetryPlacementGhosts.Count <= index)
                _symmetryPlacementGhosts.Add(CreatePlacementGhostInstance("ESU Decoration Symmetry Placement Ghost"));
            return _symmetryPlacementGhosts[index];
        }

        private PlacementGhostInstance CreatePlacementGhostInstance(string name)
        {
            var gameObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var ghost = new PlacementGhostInstance
            {
                GameObject = gameObject,
                Filter = gameObject.AddComponent<MeshFilter>(),
                Renderer = gameObject.AddComponent<MeshRenderer>(),
                Material = CreatePlacementGhostMaterial()
            };
            ghost.Renderer.sharedMaterial = ghost.Material;
            ghost.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            ghost.Renderer.receiveShadows = false;
            gameObject.SetActive(false);
            return ghost;
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
            SetPlacementGhostColor(_placementGhostMaterial, color);
        }

        private static void SetPlacementGhostColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", color);
        }

        private void HidePlacementGhost()
        {
            if (_placementGhost != null && _placementGhost.activeSelf)
                _placementGhost.SetActive(false);
            _placementGhostEntry = null;
            HideSymmetryPlacementGhosts();
        }

        private void HideSymmetryPlacementGhosts(int startIndex = 0)
        {
            for (int index = Mathf.Max(0, startIndex); index < _symmetryPlacementGhosts.Count; index++)
            {
                PlacementGhostInstance ghost = _symmetryPlacementGhosts[index];
                if (ghost?.GameObject != null && ghost.GameObject.activeSelf)
                    ghost.GameObject.SetActive(false);
                if (ghost != null)
                    ghost.Entry = null;
            }
        }

        private void DestroyPlacementGhost()
        {
            ReleasePlacementGhostObject(_placementGhostMaterial);
            ReleasePlacementGhostObject(_placementGhost);
            for (int index = 0; index < _symmetryPlacementGhosts.Count; index++)
            {
                PlacementGhostInstance ghost = _symmetryPlacementGhosts[index];
                ReleasePlacementGhostObject(ghost?.Material);
                ReleasePlacementGhostObject(ghost?.GameObject);
            }

            _symmetryPlacementGhosts.Clear();
            ClearSurfacePreviewResources();
            _placementGhost = null;
            _placementGhostFilter = null;
            _placementGhostRenderer = null;
            _placementGhostMaterial = null;
            _placementGhostEntry = null;
        }

        private void ClearSurfacePreviewResources()
        {
            foreach (SurfacePreviewResource resource in _surfacePreviewResources.Values)
                ReleaseSurfacePreviewResource(resource);

            _surfacePreviewResources.Clear();
        }

        private static void ReleaseSurfacePreviewResource(SurfacePreviewResource resource)
        {
            if (resource?.OwnsMesh == true)
                ReleasePlacementGhostObject(resource.Mesh);
            if (resource?.OwnsMaterial == true)
                ReleasePlacementGhostObject(resource.Material);
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

        private bool GizmoHoverAllowed() =>
            !HasActiveGizmoDrag() &&
            _placingMesh == null &&
            !_boxSelecting &&
            !_unappliedClosePromptOpen &&
            !IsMouseOverEditorUi(_lastMouseGui);

        private static Color GizmoHoverColor(Color color, bool hovered) =>
            hovered ? Color.Lerp(color, Color.white, 0.62f) : color;

        private static float GizmoWorldRadiusForPixels(
            AllConstruct construct,
            Vector3 centerLocal,
            float pixels,
            float fallback)
        {
            Camera camera = Camera.main;
            if (construct == null ||
                camera == null ||
                !DecorationEditMath.IsFinite(pixels) ||
                pixels <= 0f ||
                !TryProjectGizmo(construct, centerLocal, out Vector2 origin))
            {
                return fallback;
            }

            try
            {
                Vector3 localRight = construct.myTransform
                    .InverseTransformDirection(camera.transform.right)
                    .normalized;
                if (!DecorationEditMath.IsFinite(localRight) ||
                    localRight.sqrMagnitude < 0.0001f ||
                    !TryProjectGizmo(construct, centerLocal + localRight, out Vector2 metreEnd))
                {
                    return fallback;
                }

                float pixelsPerMetre = Vector2.Distance(origin, metreEnd);
                if (!DecorationEditMath.IsFinite(pixelsPerMetre) || pixelsPerMetre < 1f)
                    return fallback;
                return Mathf.Clamp(pixels / pixelsPerMetre, 0.03f, 2f);
            }
            catch
            {
                return fallback;
            }
        }

        private void DrawAxis(Vector3 centerLocal, DecorationEditAxis axis, bool groupTransform)
        {
            Vector3 axisVector = groupTransform
                ? GroupTransformAxisVector(axis)
                : ActiveTransformAxisVector(axis);
            bool hovered = false;
            if (GizmoHoverAllowed())
            {
                DecorationEditAxis picked;
                bool hit = groupTransform
                    ? TryPickGroupMoveHandle(centerLocal, _lastMouseGui, out picked)
                    : TryPickMoveHandle(_selected, _selectedConstruct, _lastMouseGui, out picked);
                hovered = hit && picked == axis;
            }
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(_dragAxis == axis || hovered ? 4.5f : 3f);
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(centerLocal + axisVector * style.MoveLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.2f);
        }

        private void DrawRotateGizmo(Vector3 centerLocal, bool groupTransform)
        {
            DecorationEditAxis hoverAxis = DecorationEditAxis.None;
            if (GizmoHoverAllowed())
            {
                if (groupTransform)
                    TryPickGroupRotateRing(centerLocal, _lastMouseGui, out hoverAxis);
                else
                    TryPickRotateRing(_selected, _selectedConstruct, _lastMouseGui, out hoverAxis);
            }
            DrawRotationRing(centerLocal, DecorationEditAxis.X, groupTransform, hoverAxis);
            DrawRotationRing(centerLocal, DecorationEditAxis.Y, groupTransform, hoverAxis);
            DrawRotationRing(centerLocal, DecorationEditAxis.Z, groupTransform, hoverAxis);
        }

        private void DrawRotationRing(
            Vector3 centerLocal,
            DecorationEditAxis axis,
            bool groupTransform,
            DecorationEditAxis hoverAxis)
        {
            Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 normalWorld = _selectedConstruct.SafeLocalToGlobal(
                centerLocal + (groupTransform ? GroupTransformAxisVector(axis) : ActiveTransformAxisVector(axis))) - centerWorld;
            if (normalWorld.sqrMagnitude < 0.0001f)
                normalWorld = DecorationEditMath.AxisVector(axis);

            bool hovered = hoverAxis == axis;
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(_rotateDragAxis == axis || hovered ? 4f : 2.5f);
            DecorationEditorOverlay.Circle(centerWorld, style.RotationRadius, color, normalWorld.normalized, width, 48);
        }

        private void DrawScaleGizmo(Vector3 centerLocal, bool groupTransform)
        {
            if (groupTransform)
                DrawUniformGroupScaleHandle(centerLocal);
            DrawScaleAxis(centerLocal, DecorationEditAxis.X, groupTransform);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Y, groupTransform);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Z, groupTransform);
        }

        private void DrawUniformGroupScaleHandle(Vector3 centerLocal)
        {
            Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            bool hovered = GizmoHoverAllowed() &&
                           TryPickGroupUniformScaleHandle(centerLocal, _lastMouseGui);
            Color color = GizmoHoverColor(new Color(0.1f, 0.95f, 1f, 1f), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(
                _scaleDragUniformGroup || hovered ? 5f : 3.2f);
            float radius = GizmoWorldRadiusForPixels(
                _selectedConstruct,
                centerLocal,
                style.HitAreaPixels,
                fallback: 0.24f);
            DecorationEditorOverlay.Circle(centerWorld, radius, color, Vector3.up, width, 28);
            DecorationEditorOverlay.Cross(centerWorld, radius * 1.08f, color, width);
        }

        private void DrawScaleAxis(Vector3 centerLocal, DecorationEditAxis axis, bool groupTransform)
        {
            Vector3 axisVector = groupTransform
                ? GroupTransformAxisVector(axis)
                : ActiveTransformAxisVector(axis);
            bool hovered = false;
            if (GizmoHoverAllowed())
            {
                DecorationEditAxis picked;
                bool hit = groupTransform
                    ? TryPickGroupHandle(centerLocal, _lastMouseGui, out picked)
                    : TryPickHandle(_selected, _selectedConstruct, _lastMouseGui, out picked);
                hovered = hit && picked == axis;
            }
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(_scaleDragAxis == axis || hovered ? 4.5f : 3f);
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(centerLocal + axisVector * style.ScaleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.18f);
            DecorationEditorOverlay.Cross(end, 0.16f, color, width);
        }

        private void DrawAnchorGizmo(Vector3 anchorLocal)
        {
            DrawBlockWireframe(_selectedConstruct, _selected.TetherPoint.Us, Color.cyan, 2f);
            DrawSymmetryAnchorGizmoHints(anchorLocal);

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

        private void DrawSymmetryAnchorGizmoHints(Vector3 selectedAnchorLocal)
        {
            if (_selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null ||
                !DecoLimitLifter.EsuSymmetry.HasActivePlanes)
            {
                return;
            }

            SymmetryFollowContext symmetryFollow = _anchorDragSymmetryFollow;
            if (symmetryFollow == null || !symmetryFollow.IsActive)
            {
                TryBuildSymmetryFollowContext(
                    new DecorationEditSnapshot(_selected),
                    reportSkipped: false,
                    trackTargetEdits: false,
                    out symmetryFollow);
            }

            if (symmetryFollow == null || !symmetryFollow.IsActive)
                return;

            Color anchorColor = new Color(0.1f, 0.95f, 1f, 0.82f);
            for (int index = 0; index < symmetryFollow.Targets.Length; index++)
            {
                Decoration counterpart = symmetryFollow.Targets[index].Decoration;
                if (counterpart == null || counterpart.IsDeleted)
                    continue;

                Vector3i anchor = counterpart.TetherPoint.Us;
                Vector3 anchorLocal = ToVector3(anchor);
                Vector3 centerLocal = GetDecorationLocalCenter(counterpart);
                DrawBlockWireframe(_selectedConstruct, anchor, anchorColor, 2f);
                DecorationEditorOverlay.Line(
                    _selectedConstruct.SafeLocalToGlobal(anchorLocal),
                    _selectedConstruct.SafeLocalToGlobal(centerLocal),
                    anchorColor,
                    2f);
                DecorationEditorOverlay.Circle(
                    _selectedConstruct.SafeLocalToGlobal(centerLocal),
                    0.2f,
                    anchorColor,
                    Vector3.up,
                    2f,
                    18);
            }

            if (_anchorDragAxis == DecorationEditAxis.None)
                return;

            Color preview = _anchorPreviewValid
                ? new Color(0.25f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.2f, 0.15f, 0.9f);
            Vector3 selectedPreviewCenter = ToVector3(_anchorPreviewTether) + _anchorPreviewPosition;
            for (int index = 0; index < symmetryFollow.Targets.Length; index++)
            {
                DecoLimitLifter.EsuSymmetry.SymmetryVariant variant = symmetryFollow.Targets[index].Variant;
                Vector3i mirroredTether = variant.Mirror(_anchorPreviewTether);
                Vector3 mirroredAnchorLocal = ToVector3(mirroredTether);
                DrawBlockWireframe(_selectedConstruct, mirroredTether, preview, 3f);
                DecorationEditorOverlay.Line(
                    _selectedConstruct.SafeLocalToGlobal(variant.Mirror(selectedAnchorLocal)),
                    _selectedConstruct.SafeLocalToGlobal(mirroredAnchorLocal),
                    preview,
                    2f);
                if (_anchorPreviewValid)
                {
                    DecorationEditorOverlay.Circle(
                        _selectedConstruct.SafeLocalToGlobal(variant.Mirror(selectedPreviewCenter)),
                        0.22f,
                        preview,
                        Vector3.up,
                        2f,
                        18);
                }
            }
        }

        private void DrawGeneratorSegments(
            AllConstruct construct,
            IReadOnlyList<DecorationGeneratorSegment> segments,
            Color color)
        {
            if (construct == null || segments == null)
                return;

            for (int index = 0; index < segments.Count; index++)
            {
                DecorationEditorOverlay.Line(
                    construct.SafeLocalToGlobal(segments[index].Start),
                    construct.SafeLocalToGlobal(segments[index].End),
                    color,
                    _generatorPlan == null ? 2.2f : 3f);
            }
        }

        private void DrawAnchorAxis(Vector3 anchorLocal, DecorationEditAxis axis, int sign)
        {
            Vector3 axisVector = DecorationEditMath.AxisVector(axis) * sign;
            bool hovered = GizmoHoverAllowed() &&
                           TryPickAnchorHandle(
                               _selected,
                               _selectedConstruct,
                               _lastMouseGui,
                               out DecorationEditAxis picked,
                               out int pickedSign) &&
                           picked == axis &&
                           pickedSign == sign;
            Color color = GizmoHoverColor(DecorationEditMath.AxisColor(axis), hovered);
            EsuGizmoStyle style = EsuGizmoSettings.Current;
            float width = style.LineWidth(
                _anchorDragAxis == axis && _anchorDragSign == sign || hovered ? 4f : 2.5f);
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(anchorLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(anchorLocal + axisVector * style.AnchorLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.16f);
        }

        private void DrawMultiSelectionBoundsGuide(float width)
        {
            if (_selectedConstruct == null ||
                !TryGetMultiSelectionBounds(out Vector3 min, out Vector3 max))
            {
                return;
            }

            Vector3 padding = Vector3.one * 0.25f;
            DrawLocalBoundsWireframe(
                _selectedConstruct,
                min - padding,
                max + padding,
                new Color(0.1f, 0.95f, 1f, 0.72f),
                Mathf.Max(1.5f, width * 0.65f));
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

        private static void DrawLocalBoundsWireframe(
            AllConstruct construct,
            Vector3 min,
            Vector3 max,
            Color color,
            float width)
        {
            if (construct == null)
                return;

            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
            for (int index = 0; index < corners.Length; index++)
                corners[index] = construct.SafeLocalToGlobal(corners[index]);

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
            SetVectorText(_scaleText, HasMultiSelectionForTransform() ? Vector3.one : _selected.Scaling.Us);
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
            SetVectorText(_scaleText, HasMultiSelectionForTransform() ? Vector3.one : _selected.Scaling.Us);
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

        private AllConstruct ResolveBoxSelectionConstruct(Vector2 mouse, bool paint)
        {
            if (paint)
            {
                if (TryFindNearestDecoration(mouse, out DecorationHit decorationHit) &&
                    decorationHit?.Construct != null)
                {
                    return decorationHit.Construct;
                }

                try
                {
                    if (_pointerProbe.TryProbe(out DecorationPointerHit pointerHit) &&
                        pointerHit?.Construct != null)
                    {
                        return pointerHit.Construct;
                    }
                }
                catch
                {
                }

                return FocusedConstruct() ?? _selectedConstruct;
            }

            return _selectedConstruct ?? FocusedConstruct();
        }

        private void ApplyScaleFromInspector(Vector3 value, bool syncText)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            if (HasMultiSelectionForTransform())
            {
                ApplyMultiScaleFromInspector(value, syncText);
                return;
            }

            if (!IsValidScale(value))
            {
                InfoStore.Add("Scale must be finite; each axis must be 0 or at least 0.00001.");
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            SymmetryFollowContext symmetryFollow = BeginSymmetryFollow(before, reportSkipped: true);
            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            DecorationScaleBounds.AllowExtendedScale(_selected);
            _selected.Scaling.Us = value;
            if (!TryApplySymmetryFollow(symmetryFollow, reportInvalid: true))
            {
                RestoreSymmetryEditState(selectedRollback, symmetryFollow, targetRollback);
                if (syncText)
                    SetVectorText(_scaleText, _selected.Scaling.Us);
                RefreshDecorationCache(force: true);
                return;
            }

            if (syncText)
                SetVectorText(_scaleText, value);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set scale", before, symmetryFollow);
        }

        private void ApplyMultiScaleFromInspector(Vector3 factors, bool syncText)
        {
            if (!IsValidGroupScaleFactors(factors))
            {
                InfoStore.Add("Group scale factors must be finite, non-negative, and each axis must be 0 or at least 0.00001.");
                return;
            }

            if (!TryCaptureMultiTransformStart(out _))
            {
                InfoStore.Add("Select at least two decorations before applying group scale.");
                return;
            }

            bool uniform = GroupScaleFactorsAreUniform(factors);
            bool applied = uniform
                ? TryApplyMultiUniformScale(factors.x)
                : TryApplyMultiScaleFactors(factors);
            if (!applied)
            {
                ClearMultiTransformState();
                InfoStore.Add("Group scale rejected; resulting placement or scale would be invalid.");
                return;
            }

            RecordMultiTransformEdit(uniform ? "Uniform scale decorations" : "Scale decorations");
            if (syncText)
                SetVectorText(_scaleText, Vector3.one);
            InfoStore.Add(uniform
                ? "Group scale applied: " + FormatFloat(factors.x) + "x."
                : "Group scale applied: X " + FormatFloat(factors.x) + " Y " + FormatFloat(factors.y) + " Z " + FormatFloat(factors.z) + ".");
        }

        private static bool IsValidScale(Vector3 value) =>
            DecorationEditMath.IsFinite(value) &&
            IsValidScaleComponent(value.x) &&
            IsValidScaleComponent(value.y) &&
            IsValidScaleComponent(value.z);

        private static bool IsValidScaleComponent(float value) =>
            value == 0f || Mathf.Abs(value) >= MinimumNonZeroScale;

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
            SymmetryFollowContext symmetryFollow = BeginSymmetryFollow(before, reportSkipped: true);
            DecorationEditSnapshot selectedRollback = new DecorationEditSnapshot(_selected);
            DecorationEditSnapshot[] targetRollback = CaptureSymmetryTargetSnapshots(symmetryFollow);
            _selected.Orientation.Us = value;
            if (!TryApplySymmetryFollow(symmetryFollow, reportInvalid: true))
            {
                RestoreSymmetryEditState(selectedRollback, symmetryFollow, targetRollback);
                if (syncText)
                    SetVectorText(_orientationText, _selected.Orientation.Us);
                RefreshDecorationCache(force: true);
                return;
            }

            if (syncText)
                SetVectorText(_orientationText, value);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set rotation", before, symmetryFollow);
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

        private static Vector3i RoundToCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

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

        private static bool TryProjectGizmo(AllConstruct construct, Vector3 local, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (construct == null || !DecorationEditMath.IsFinite(local))
                return false;
            Camera camera = Camera.main;
            if (camera == null)
                return false;

            try
            {
                Vector3 projected = camera.WorldToScreenPoint(construct.SafeLocalToGlobal(local));
                if (!DecorationEditMath.IsFinite(projected) || projected.z <= 0f)
                    return false;
                screenPoint = new Vector2(projected.x, Screen.height - projected.y);
                return DecorationEditMath.IsFinite(screenPoint);
            }
            catch
            {
                return false;
            }
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
                Vector3 positioning,
                Vector3 orientation,
                Vector3 scaling,
                int color)
            {
                Decoration = decoration;
                Anchor = anchor;
                Positioning = positioning;
                Orientation = orientation;
                Scaling = scaling;
                Color = color;
            }

            internal Decoration Decoration { get; }

            internal Vector3i Anchor { get; }

            internal Vector3 Positioning { get; }

            internal Vector3 Orientation { get; }

            internal Vector3 Scaling { get; }

            internal int Color { get; }
        }

        private readonly struct DecorationPlacementPlan
        {
            internal DecorationPlacementPlan(
                Vector3i anchor,
                Vector3 positioning,
                Vector3 orientation,
                Vector3 scaling,
                int color,
                bool hideOriginalMesh,
                Guid materialReplacement)
            {
                Anchor = anchor;
                Positioning = positioning;
                Orientation = orientation;
                Scaling = scaling;
                Color = color;
                HideOriginalMesh = hideOriginalMesh;
                MaterialReplacement = materialReplacement;
            }

            internal Vector3i Anchor { get; }

            internal Vector3 Positioning { get; }

            internal Vector3 Orientation { get; }

            internal Vector3 Scaling { get; }

            internal int Color { get; }

            internal bool HideOriginalMesh { get; }

            internal Guid MaterialReplacement { get; }
        }

        private readonly struct DecorationPlacementTemplate
        {
            internal DecorationPlacementTemplate(
                Vector3 orientation,
                Vector3 scaling,
                int color,
                bool hideOriginalMesh,
                Guid materialReplacement)
            {
                Orientation = orientation;
                Scaling = scaling;
                Color = color;
                HideOriginalMesh = hideOriginalMesh;
                MaterialReplacement = materialReplacement;
            }

            internal static DecorationPlacementTemplate Default =>
                new DecorationPlacementTemplate(
                    Vector3.zero,
                    Vector3.one,
                    0,
                    false,
                    Guid.Empty);

            internal Vector3 Orientation { get; }

            internal Vector3 Scaling { get; }

            internal int Color { get; }

            internal bool HideOriginalMesh { get; }

            internal Guid MaterialReplacement { get; }
        }

        private readonly struct DecorationDeletePlan
        {
            internal DecorationDeletePlan(
                AllConstruct construct,
                Decoration decoration,
                DecorationEditSnapshot deleted,
                DecorationEditSnapshot original,
                bool createdInSession)
            {
                Construct = construct;
                Decoration = decoration;
                Deleted = deleted;
                Original = original;
                CreatedInSession = createdInSession;
            }

            internal AllConstruct Construct { get; }

            internal Decoration Decoration { get; }

            internal DecorationEditSnapshot Deleted { get; }

            internal DecorationEditSnapshot Original { get; }

            internal bool CreatedInSession { get; }
        }

        private sealed class PlacementGhostInstance
        {
            internal GameObject GameObject { get; set; }

            internal MeshFilter Filter { get; set; }

            internal MeshRenderer Renderer { get; set; }

            internal Material Material { get; set; }

            internal DecorationMeshCatalogEntry Entry { get; set; }
        }

        private sealed class PaintSelectionHistoryCommand : IDecorationEditCommand
        {
            private readonly AllConstruct _selectionConstruct;
            private readonly Decoration[] _decorations;
            private readonly DecorationEditSnapshot[] _beforeDecorations;
            private readonly DecorationEditSnapshot[] _afterDecorations;
            private readonly Block[] _blocks;
            private readonly int[] _beforeBlockColors;
            private readonly int[] _afterBlockColors;
            private readonly Decoration[] _selection;
            private readonly Decoration _primary;

            internal PaintSelectionHistoryCommand(
                string label,
                AllConstruct selectionConstruct,
                Decoration[] decorations,
                DecorationEditSnapshot[] beforeDecorations,
                DecorationEditSnapshot[] afterDecorations,
                Block[] blocks,
                int[] beforeBlockColors,
                int[] afterBlockColors,
                Decoration[] selection,
                Decoration primary)
            {
                Label = string.IsNullOrEmpty(label) ? "Paint selection" : label;
                _selectionConstruct = selectionConstruct;
                _decorations = decorations == null
                    ? Array.Empty<Decoration>()
                    : (Decoration[])decorations.Clone();
                _beforeDecorations = beforeDecorations == null
                    ? Array.Empty<DecorationEditSnapshot>()
                    : (DecorationEditSnapshot[])beforeDecorations.Clone();
                _afterDecorations = afterDecorations == null
                    ? Array.Empty<DecorationEditSnapshot>()
                    : (DecorationEditSnapshot[])afterDecorations.Clone();
                _blocks = blocks == null ? Array.Empty<Block>() : (Block[])blocks.Clone();
                _beforeBlockColors = beforeBlockColors == null
                    ? Array.Empty<int>()
                    : (int[])beforeBlockColors.Clone();
                _afterBlockColors = afterBlockColors == null
                    ? Array.Empty<int>()
                    : (int[])afterBlockColors.Clone();
                _selection = selection == null
                    ? Array.Empty<Decoration>()
                    : (Decoration[])selection.Clone();
                _primary = primary;
            }

            public string Label { get; }

            public bool Undo(DecorationEditSession session) =>
                session != null &&
                session.TryRestorePaintSelectionHistory(
                    _selectionConstruct,
                    _decorations,
                    _beforeDecorations,
                    _blocks,
                    _beforeBlockColors,
                    _selection,
                    _primary,
                    Label + " undo");

            public bool Redo(DecorationEditSession session) =>
                session != null &&
                session.TryRestorePaintSelectionHistory(
                    _selectionConstruct,
                    _decorations,
                    _afterDecorations,
                    _blocks,
                    _afterBlockColors,
                    _selection,
                    _primary,
                    Label + " redo");
        }

        private sealed class SurfacePreviewResource
        {
            internal SurfacePreviewResource(
                Mesh mesh,
                Material material,
                bool ownsMesh,
                bool ownsMaterial,
                int lastUsedFrame)
            {
                Mesh = mesh;
                Material = material;
                OwnsMesh = ownsMesh;
                OwnsMaterial = ownsMaterial;
                LastUsedFrame = lastUsedFrame;
            }

            internal Mesh Mesh { get; }

            internal Material Material { get; }

            internal bool OwnsMesh { get; }

            internal bool OwnsMaterial { get; }

            internal int LastUsedFrame { get; set; }
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
