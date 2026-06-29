using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
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
        private const float HandleLength = 1.25f;
        private const float SelectionRadiusPixels = 28f;
        private const float HandleRadiusPixels = 14f;
        private const int MaxOutlinerDrawRows = 180;
        private const int MaxWorldHintLines = 650;
        private const float OutlinerRowHeight = 22f;
        private const float MeshPaletteRowHeight = 24f;
        private const float MeshPreviewGridRowHeight = 90f;
        private const int MeshPreviewGridColumns = 4;
        private const float MeshPreviewGridCardWidth = 112f;
        private const float MeshPreviewGridCardHeight = 86f;
        private const int MeshPreviewGridTexturePixels = 96;
        private const int ViewModeMenuButtonCount = 9;
        private const float RotateSnapDegrees = 5f;
        private const float RotateGizmoRadius = 0.82f;
        private const float ScaleSnap = 0.05f;
        private const float MinimumScale = 0.01f;
        private const float AnchorFollowMinimumDistance = 1f;
        private const float AnchorFollowMaximumDistance = 10f;
        private const int AnchorFollowMaximumSearchRadius = 8;
        private const float SymmetryCounterpartCenterTolerance = 0.075f;
        private static Rect s_meshPaletteRect = new Rect(18f, 110f, 480f, 1050f);
        private static Rect s_rightPanelRect = Rect.zero;
        private static int s_layoutGeneration = -1;
        private static bool s_showMeshPalette = true;
        private static bool s_showInspectorPanel = true;
        private static bool s_showOutlinerPanel = true;
        private static bool s_showAnchorPanel = true;
        private static bool s_meshPreviewGrid;
        private static DecorationTransformOrientation s_transformOrientation = DecorationTransformOrientation.Global;
        private static bool s_anchorMenuOpen;
        private static bool s_anchorFollowDecoration;
        private static float s_anchorFollowDistance = AnchorFollowMinimumDistance;

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
        private int _layoutResetGeneration = s_layoutGeneration;
        private bool _meshPreviewGrid = s_meshPreviewGrid;
        private bool _textInputFocused;
        private bool _viewModeMenuOpen;
        private bool _anchorMenuOpen = s_anchorMenuOpen;
        private bool _showMeshPalette = s_showMeshPalette;
        private bool _showInspectorPanel = s_showInspectorPanel;
        private bool _showOutlinerPanel = s_showOutlinerPanel;
        private bool _showAnchorPanel = s_showAnchorPanel;
        private bool _showTetherPins;
        private bool _anchorFollowDecoration = s_anchorFollowDecoration;
        private float _anchorFollowDistance = s_anchorFollowDistance;
        private string _anchorFollowDistanceText =
            s_anchorFollowDistance.ToString("0.###", CultureInfo.InvariantCulture);
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
        private DecorationTransformOrientation _transformOrientation = s_transformOrientation;
        private DecorationEditAxis _dragAxis;
        private Vector2 _dragMouseStart;
        private Vector3 _dragPositionStart;
        private SymmetryFollowContext _dragSymmetryFollow;
        private Vector3 _freeDragCameraRight;
        private Vector3 _freeDragCameraUp;
        private float _freeDragMetresPerPixel;
        private DecorationEditSnapshot _dragSnapshotStart;
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
        private Vector3 _surfaceFreeDragCameraRight;
        private Vector3 _surfaceFreeDragCameraUp;
        private float _surfaceFreeDragMetresPerPixel = 0.01f;
        private readonly string[] _positionText = new string[3];
        private readonly string[] _scaleText = new string[3];
        private readonly string[] _orientationText = new string[3];
        private string _colorText = string.Empty;
        private Vector2 _paintScroll;
        private int _paintColor;
        private string _materialText = string.Empty;
        private string _materialFilter = string.Empty;
        private bool _showMaterialPicker;
        private Vector2 _materialScroll;
        private List<MaterialCatalogEntry> _materialCatalog;

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
            _placingMesh != null ||
            _dragAxis != DecorationEditAxis.None ||
            _rotateDragAxis != DecorationEditAxis.None ||
            _scaleDragAxis != DecorationEditAxis.None ||
            _anchorDragAxis != DecorationEditAxis.None ||
            _surfaceDragAxis != DecorationEditAxis.None ||
            _surfaceDraft.HasDraft;

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

            if (_surfaceDraft.HasDraft)
            {
                reason = "Place or clear the Surface draft before switching to Smart Builder.";
                return false;
            }

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None ||
                _surfaceDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty || _transactions.HasChanges)
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
                _surfaceDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty || _transactions.HasChanges)
            {
                RefreshApplyCancelAttention();
                reason = "Apply or Cancel Decoration Edit changes before switching to Surface Builder.";
                return false;
            }

            _tool = DecorationEditorTool.Surface;
            _viewModeMenuOpen = false;
            _anchorMenuOpen = false;
            reason = null;
            return true;
        }

        internal void End(bool apply, bool notify = true)
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
                DecorationEditorOverlay.Clear();
                _viewModeMenuOpen = false;
                _anchorMenuOpen = false;
                _placingMesh = null;
                _placementConstruct = null;
                _placementLocalPosition = Vector3.zero;
                _placementWorldPosition = Vector3.zero;
                _placementWorldNormal = Vector3.zero;
                _placementValid = false;
                ClearSurfaceDraft(notify: false);
                _surfaceDragAxis = DecorationEditAxis.None;
                DestroyPlacementGhost();
                _history.Clear();
                _transactions.Apply();
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
                _surfaceDragAxis != DecorationEditAxis.None)
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
                _surfaceDragAxis != DecorationEditAxis.None)
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
            ApplyLayoutResetIfNeeded();
            Rect toolbarRect = ToolbarRect();
            Rect rightRect = RightPanelRect();
            Rect bottomRect = StatusRect(rightRect);
            Rect meshRect = MeshPaletteRect();
            bool leftStackVisible = IsLeftPanelStackVisible();
            bool rightStackVisible = IsRightPanelStackVisible();
            Vector2 mouse = Event.current.mousePosition;
            _mouseOverWindow = toolbarRect.Contains(mouse) ||
                               EsuHudNotifications.ContainsMouse(mouse) ||
                               (_viewModeMenuOpen && ViewModeMenuRect(toolbarRect).Contains(mouse)) ||
                               (_anchorMenuOpen && AnchorMenuRect(toolbarRect).Contains(mouse)) ||
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
            DrawTopToolbar(toolbarInner);
            GUILayout.EndArea();
            EsuHudNotifications.DrawExpandedPopup();

            DrawViewModeMenu(toolbarRect);
            DrawAnchorMenu(toolbarRect);

            if (leftStackVisible)
            {
                DrawLeftPanelStack(meshRect);
                EsuHudLayout.DrawResizeGrip(meshRect, leftEdge: false);
            }

            if (rightStackVisible)
            {
                DrawRightPanel(rightRect);
                EsuHudLayout.DrawResizeGrip(rightRect, leftEdge: true);
            }

            GUI.Box(bottomRect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect bottomInner = EsuHudLayout.PanelInnerRect(bottomRect);
            GUILayout.BeginArea(bottomInner);
            DrawBottomPanel(bottomInner.height);
            GUILayout.EndArea();

            DrawMeshPreviewCard();
            PersistPanelState();
        }

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
            return Mathf.Clamp(Screen.height * 0.105f, EsuHudLayout.Scale(88f), EsuHudLayout.Scale(112f));
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
            _layoutResetGeneration = EsuHudLayout.ResetGeneration;
            s_layoutGeneration = _layoutResetGeneration;
        }

        private bool IsLeftPanelStackVisible() =>
            IsSurfaceMode || _tool == DecorationEditorTool.Paint || _showMeshPalette || _showInspectorPanel;

        private bool IsRightPanelStackVisible() =>
            _showOutlinerPanel || _showAnchorPanel;

        private bool IsMouseOverEditorUi(Vector2 mouse) =>
            ToolbarRect().Contains(mouse) ||
            (_viewModeMenuOpen && ViewModeMenuRect(ToolbarRect()).Contains(mouse)) ||
            (_anchorMenuOpen && AnchorMenuRect(ToolbarRect()).Contains(mouse)) ||
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
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                title.Contains(current.mousePosition))
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
            s_showMeshPalette = _showMeshPalette;
            s_showInspectorPanel = _showInspectorPanel;
            s_showOutlinerPanel = _showOutlinerPanel;
            s_showAnchorPanel = _showAnchorPanel;
            s_meshPreviewGrid = _meshPreviewGrid;
            s_transformOrientation = _transformOrientation;
            s_anchorMenuOpen = _anchorMenuOpen;
            s_anchorFollowDecoration = _anchorFollowDecoration;
            s_anchorFollowDistance = _anchorFollowDistance;
        }

        private void DrawTopToolbar(Rect toolbarRect)
        {
            float leftRailWidth = EsuHudLayout.ToolbarLeftRailWidth(toolbarRect.width);
            float notificationWidth = EsuHudLayout.ToolbarNotificationWidth(toolbarRect.width);
            float rightRailWidth = EsuHudLayout.ToolbarRightControlsWidth(toolbarRect.width);
            float gap = EsuHudLayout.ToolbarGap;

            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(leftRailWidth));
            {
                ModeSwitchButton();
                if (!IsSurfaceMode)
                {
                    ToolButton(DecorationEditorTool.Select, "select", "Select", "Click decoration centers or outliner rows.");
                    ToolButton(DecorationEditorTool.Move, "move", "Move", "Drag XYZ handles. Snaps to 0.05m.");
                    ToolButton(DecorationEditorTool.Rotate, "rotate", "Rotate", "Drag RGB rotation rings. Snaps to 5 degrees.");
                    ToolButton(DecorationEditorTool.Scale, "scale", "Scale", "Drag RGB scale handles. Snaps to 0.05.");
                    OrientationToggleButton();
                    SymmetryButton(DecorationEditAxis.X);
                    SymmetryButton(DecorationEditAxis.Y);
                    SymmetryButton(DecorationEditAxis.Z);
                    AnchorMenuButton();
                    ToolButton(DecorationEditorTool.Paint, "paint", "Paint", "Edit color and material replacement.");
                    ToolButton(DecorationEditorTool.Focus, "visibility", "View", "Current view: " + ViewModeShortName());
                }

                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(gap);
            EsuHudNotifications.DrawToolbarSlot(toolbarRect, notificationWidth, CurrentModeToolbarLabel());
            GUILayout.Space(gap);
            GUILayout.BeginHorizontal(GUILayout.Width(rightRailWidth));
            PanelToggle("mesh", "Pal", ref _showMeshPalette, "Show or hide the mesh palette.");
            PanelToggle("outliner", "Out", ref _showOutlinerPanel, "Show or hide the decoration outliner.");
            PanelToggle("settings", "Insp", ref _showInspectorPanel, "Show or hide the selected decoration inspector.");
            PanelToggle("anchor", "Anch", ref _showAnchorPanel, "Show or hide decorations sharing the selected anchor.");
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
        }

        private string CurrentModeToolbarLabel() =>
            IsSurfaceMode
                ? "ESU mode: Surface Builder."
                : "ESU mode: Decoration Edit.";

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
                    DecorationEditorTheme.Button,
                    "Open anchor retethering options. Anchor follow is also available in the bottom status panel."))
            {
                _tool = DecorationEditorTool.Anchor;
                _anchorMenuOpen = !_anchorMenuOpen;
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
                    _tool = tool;
                }
            }
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

        private Rect AnchorMenuRect(Rect toolbarRect)
        {
            float margin = EsuHudLayout.Scale(8f);
            float maxWidth = Mathf.Max(1f, Screen.width - margin * 2f);
            float minWidth = Mathf.Min(EsuHudLayout.Scale(300f), maxWidth);
            float width = Mathf.Clamp(AnchorMenuPreferredWidth(), minWidth, maxWidth);
            bool wrapsPresets = AnchorMenuNeedsPresetWrap(width);
            float height = EsuHudLayout.Scale(wrapsPresets ? 122f : 88f);
            float x = Mathf.Clamp(
                toolbarRect.x + EsuHudLayout.Scale(344f),
                margin,
                Screen.width - width - margin);
            float y = Mathf.Clamp(
                toolbarRect.yMax + EsuHudLayout.Scale(4f),
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

        private void DrawAnchorMenu(Rect toolbarRect)
        {
            if (!_anchorMenuOpen)
                return;

            Rect rect = AnchorMenuRect(toolbarRect);
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
                    _anchorFollowDecoration ? "Follow: on" : "Follow: off",
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
            _anchorFollowDistanceText = GUI.TextField(
                new Rect(x, y + EsuHudLayout.Scale(2f), EsuHudLayout.Scale(52f), EsuHudLayout.Scale(22f)),
                _anchorFollowDistanceText ?? string.Empty,
                DecorationEditorTheme.TextField);
            x += EsuHudLayout.Scale(52f) + compactGap;
            if (GUI.Button(
                    new Rect(x, y + EsuHudLayout.Scale(1f), EsuHudLayout.Scale(42f), EsuHudLayout.Scale(24f)),
                    "Set",
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
                    label,
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
                    label,
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
            bool showBuilderPanel = IsSurfaceMode || _tool == DecorationEditorTool.Paint || _showMeshPalette;
            if (!showBuilderPanel && !_showInspectorPanel)
                return;

            Rect meshRect;
            Rect inspectorRect;
            SplitVerticalStack(
                stackRect,
                showBuilderPanel,
                _showInspectorPanel,
                Mathf.Clamp(stackRect.height * 0.34f, EsuHudLayout.Scale(125f), EsuHudLayout.Scale(342f)),
                EsuHudLayout.Scale(8f),
                out meshRect,
                out inspectorRect);

            if (showBuilderPanel)
                DrawMeshPalette(meshRect);
            if (_showInspectorPanel)
                DrawInspectorPanel(inspectorRect);
        }

        private void DrawRightPanel(Rect rightRect)
        {
            if (!_showOutlinerPanel && !_showAnchorPanel)
                return;

            Rect outlinerRect;
            Rect anchorRect;
            SplitVerticalStack(
                rightRect,
                _showOutlinerPanel,
                _showAnchorPanel,
                Mathf.Clamp(rightRect.height * 0.38f, EsuHudLayout.Scale(170f), EsuHudLayout.Scale(310f)),
                EsuHudLayout.Scale(8f),
                out outlinerRect,
                out anchorRect);

            if (_showOutlinerPanel)
                DrawOutlinerPanel(outlinerRect);
            if (_showAnchorPanel)
                DrawAnchorPanel(anchorRect);
        }

        private static void SplitVerticalStack(
            Rect stackRect,
            bool showTop,
            bool showBottom,
            float preferredBottomHeight,
            float gap,
            out Rect topRect,
            out Rect bottomRect)
        {
            topRect = Rect.zero;
            bottomRect = Rect.zero;
            if (showTop && showBottom)
            {
                float bottomHeight = Mathf.Clamp(
                    preferredBottomHeight,
                    Mathf.Min(EsuHudLayout.Scale(96f), stackRect.height * 0.5f),
                    Mathf.Max(EsuHudLayout.Scale(96f), stackRect.height - gap - EsuHudLayout.Scale(120f)));
                float topHeight = Mathf.Max(EsuHudLayout.Scale(96f), stackRect.height - bottomHeight - gap);
                topRect = new Rect(stackRect.x, stackRect.y, stackRect.width, topHeight);
                bottomRect = new Rect(stackRect.x, topRect.yMax + gap, stackRect.width, stackRect.yMax - topRect.yMax - gap);
                return;
            }

            if (showTop)
                topRect = stackRect;
            if (showBottom)
                bottomRect = stackRect;
        }

        private void DrawOutlinerPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
            GUILayout.BeginArea(inner);
            DrawOutliner(inner.height);
            GUILayout.EndArea();
        }

        private void DrawAnchorPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
            GUILayout.BeginArea(inner);
            DrawAnchorContext(inner.height);
            GUILayout.EndArea();
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
                    EsuHudLayout.Scale(72f),
                    height - EsuHudLayout.Scale(_tool == DecorationEditorTool.Anchor ? 84f : 52f))));
            foreach (Decoration decoration in rows)
            {
                GUIStyle style = ReferenceEquals(decoration, _selected)
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row;
                string meshName = CompactText(MeshName(decoration.MeshGuid.Us), 36);
                string detail = $"c{decoration.Color.Us} | {decoration.MeshGuid.Us.ToString("N").Substring(0, 8)}";
                if (GUILayout.Button(
                        new GUIContent(meshName + "  " + detail, DecorationEditorIconCatalog.Get("mesh")),
                        style,
                        GUILayout.Height(EsuHudLayout.Scale(OutlinerRowHeight))))
                {
                    Select(decoration, _selectedConstruct);
                }
            }
            GUILayout.EndScrollView();
        }

        private static void DrawCompactAnchorHeader()
        {
            DrawCompactIconHeader("Selected anchor", "anchor");
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

        private void DrawOutliner(float height)
        {
            Rect headerRow = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(24f), GUILayout.ExpandWidth(true));
            float gap = EsuHudLayout.Scale(6f);
            float pinsWidth = EsuHudLayout.Scale(76f);
            float refreshWidth = EsuHudLayout.Scale(86f);
            float actionWidth = pinsWidth + refreshWidth + gap * 2f;
            float titleWidth = Mathf.Clamp(
                headerRow.width - actionWidth,
                EsuHudLayout.Scale(80f),
                Mathf.Max(EsuHudLayout.Scale(80f), headerRow.width - actionWidth));
            Rect titleRect = new Rect(headerRow.x, headerRow.y, titleWidth, headerRow.height);
            DrawCompactIconHeader(titleRect, "Outliner", "outliner");
            float buttonX = titleRect.xMax + gap;
            if (GUI.Button(
                    new Rect(buttonX, headerRow.y, pinsWidth, headerRow.height),
                    _showTetherPins ? "Pins on" : "Pins off",
                    DecorationEditorTheme.ToolButton(_showTetherPins)))
            {
                _showTetherPins = !_showTetherPins;
                RefreshDecorationCache(force: true);
            }
            buttonX += pinsWidth + gap;
            if (GUI.Button(
                    new Rect(buttonX, headerRow.y, refreshWidth, headerRow.height),
                    new GUIContent("Refresh", DecorationEditorIconCatalog.Get("create")),
                    DecorationEditorTheme.Button))
                RefreshDecorationCache(force: true);

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            _outlinerFilter = GUILayout.TextField(_outlinerFilter ?? string.Empty, DecorationEditorTheme.TextField);
            GUILayout.EndHorizontal();

            List<OutlinerRow> rows = FilterOutlinerRows();
            GUILayout.Label(
                $"{rows.Count:N0} rows | {_hits.Count:N0} visible/projected decorations",
                DecorationEditorTheme.Mini);

            float rowHeight = EsuHudLayout.Scale(OutlinerRowHeight);
            _outlinerScroll = GUILayout.BeginScrollView(
                _outlinerScroll,
                GUILayout.Height(Mathf.Max(EsuHudLayout.Scale(80f), height - EsuHudLayout.Scale(76f))));
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_outlinerScroll.y / rowHeight) - 4);
            int last = Mathf.Min(total, first + MaxOutlinerDrawRows);
            if (first > 0)
                GUILayout.Space(first * rowHeight);
            for (int i = first; i < last; i++)
                DrawOutlinerRow(rows[i]);
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

        private void DrawOutlinerRow(OutlinerRow row)
        {
            GUILayout.BeginHorizontal();
            float rowHeight = EsuHudLayout.Scale(OutlinerRowHeight);
            GUILayout.Space(row.Depth * EsuHudLayout.Scale(14f));
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
            string label = row.Label + dirty + (string.IsNullOrEmpty(row.Detail) ? string.Empty : "  " + row.Detail);
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon)),
                    style,
                    GUILayout.Height(rowHeight)))
            {
                if (row.Kind == DecorationOutlinerRowKind.Construct)
                {
                    ToggleConstructCollapse(row.CollapseKey);
                    RefreshDecorationCache(force: true);
                }
                else if (row.Kind == DecorationOutlinerRowKind.Decoration && row.Decoration != null)
                {
                    Select(row.Decoration, row.Construct);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawInspector(float height)
        {
            _inspectorScroll = GUILayout.BeginScrollView(
                _inspectorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(EsuHudLayout.Scale(58f), height)));
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
            GUILayout.Label("Y", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(12f)));
            values[1] = GUILayout.TextField(values[1] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(72f)));
            GUILayout.Label("Z", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(12f)));
            values[2] = GUILayout.TextField(values[2] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(42f))))
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
            GUILayout.Label("Paint color", DecorationEditorTheme.SubHeader);
            for (int row = 0; row < 4; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < 8; column++)
                {
                    int color = row * 8 + column;
                    GUIStyle style = _selected.Color.Us == color
                        ? DecorationEditorTheme.ActiveButton
                        : DecorationEditorTheme.Button;
                    if (GUILayout.Button(
                            color.ToString(CultureInfo.InvariantCulture),
                            style,
                            GUILayout.Width(EsuHudLayout.Scale(36f)),
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        SetSelectedColor(color);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _colorText = GUILayout.TextField(_colorText ?? string.Empty, DecorationEditorTheme.TextField);
            if (GUILayout.Button("0-31", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(52f))))
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
                    "Clear",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                SetSelectedMaterial(Guid.Empty);
            }

            if (GUILayout.Button(
                    _showMaterialPicker ? "Hide list" : "Show list",
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
                            material.DisplayName,
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
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(42f))))
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
            if (GUILayout.Button(label, DecorationEditorTheme.Button))
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

            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
            float headerHeight = EsuHudLayout.Scale(116f);
            float countHeight = EsuHudLayout.Scale(18f);
            float placingHeight = _placingMesh == null ? 0f : EsuHudLayout.Scale(24f);
            float listHeight = Mathf.Max(
                EsuHudLayout.Scale(120f),
                inner.height - headerHeight - countHeight - placingHeight - EsuHudLayout.Scale(8f));
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect countRect = new Rect(inner.x, headerRect.yMax, inner.width, countHeight);
            Rect listRect = new Rect(inner.x, countRect.yMax, inner.width, listHeight);
            Rect placingRect = new Rect(inner.x, listRect.yMax + EsuHudLayout.Scale(4f), inner.width, placingHeight);
            bool mouseInListViewport = listRect.Contains(Event.current.mousePosition);

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginVertical();
            DrawCompactIconHeader("Mesh Palette", "mesh");
            GUILayout.Label("Drag this panel. Click a mesh to place it; Set swaps the selected decoration.", DecorationEditorTheme.Mini);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("List", DecorationEditorTheme.ToolButton(!_meshPreviewGrid), GUILayout.Width(EsuHudLayout.Scale(62f))))
                _meshPreviewGrid = false;
            if (GUILayout.Button("3D grid", DecorationEditorTheme.ToolButton(_meshPreviewGrid), GUILayout.Width(EsuHudLayout.Scale(76f))))
                _meshPreviewGrid = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Hide", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(58f))))
                _showMeshPalette = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", DecorationEditorTheme.ToolButton(_meshKindFilter == "all"), GUILayout.Width(EsuHudLayout.Scale(48f))))
                _meshKindFilter = "all";
            if (GUILayout.Button("Items", DecorationEditorTheme.ToolButton(_meshKindFilter == "item"), GUILayout.Width(EsuHudLayout.Scale(58f))))
                _meshKindFilter = "item";
            if (GUILayout.Button("Objects", DecorationEditorTheme.ToolButton(_meshKindFilter == "object"), GUILayout.Width(EsuHudLayout.Scale(68f))))
                _meshKindFilter = "object";
            if (GUILayout.Button("Recent", DecorationEditorTheme.ToolButton(_meshKindFilter == "recent"), GUILayout.Width(EsuHudLayout.Scale(68f))))
                _meshKindFilter = "recent";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")),
                GUILayout.Width(EsuHudLayout.Scale(24f)),
                GUILayout.Height(EsuHudLayout.Scale(22f)));
            _meshFilter = GUILayout.TextField(_meshFilter ?? string.Empty, DecorationEditorTheme.TextField);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            List<DecorationMeshCatalogEntry> rows = FilterMeshCatalog().ToList();
            GUI.Label(countRect, MeshCountText(rows.Count), DecorationEditorTheme.Mini);

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

            if (_placingMesh != null)
            {
                GUILayout.BeginArea(placingRect);
                GUILayout.Label($"Placing: {_placingMesh.Name} | click a valid block, right-click/Esc cancels.", DecorationEditorTheme.Warning);
                GUILayout.EndArea();
            }
        }

        private void DrawPaintPalette(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            Rect inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
            float headerHeight = EsuHudLayout.Scale(106f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect listRect = new Rect(
                inner.x,
                headerRect.yMax + EsuHudLayout.Scale(4f),
                inner.width,
                Mathf.Max(EsuHudLayout.Scale(120f), inner.yMax - headerRect.yMax - EsuHudLayout.Scale(4f)));

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginVertical();
            DrawCompactIconHeader("Paint Palette", "paint");
            GUILayout.Label("Pick a color, then click decoration centers in the viewport to paint them.", DecorationEditorTheme.MiniWrap);
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
            GUILayout.BeginArea(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f));
            DrawCompactIconHeader("Surface Builder", "build");
            GUILayout.Label("Click three craft-surface points to seed a triangle. Select an edge, then click a new point to extend.", DecorationEditorTheme.MiniWrap);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(82f))))
                RebuildSurfacePreview(showMessage: true);
            bool canPlace = _surfacePlan != null && _surfacePlan.DecorationCount > 0;
            bool previous = GUI.enabled;
            GUI.enabled = previous && canPlace;
            if (GUILayout.Button("Place", DecorationEditorTheme.ToolButton(false, canPlace), GUILayout.Width(EsuHudLayout.Scale(72f))))
                PlaceSurfacePlan();
            GUI.enabled = previous;
            if (GUILayout.Button("Clear", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(64f))))
                ClearSurfaceDraft(notify: true);
            if (GUILayout.Button("Delete", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(72f))))
                DeleteSurfaceSelection();
            GUILayout.EndHorizontal();

            DecorationEditorTheme.Separator();
            DrawSurfaceSettings();
            DecorationEditorTheme.Separator();

            GUILayout.Label(SurfaceSummary(), DecorationEditorTheme.BodyWrap);
            if (!string.IsNullOrEmpty(_surfaceMessage))
                GUILayout.Label(_surfaceMessage, SurfaceMessageStyle());
            if (_surfacePlan != null && _surfacePlan.Warnings.Count > 0)
                GUILayout.Label(_surfacePlan.Warnings[0], DecorationEditorTheme.Warning);

            GUILayout.Label("Draft", DecorationEditorTheme.SubHeader);
            _surfaceScroll = GUILayout.BeginScrollView(
                _surfaceScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(EsuHudLayout.Scale(110f), rect.height - EsuHudLayout.Scale(332f))));
            for (int index = 0; index < _surfaceDraft.Points.Count; index++)
            {
                GUIStyle style = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Point &&
                                 _surfaceDraft.SelectedPoint == index
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row;
                if (GUILayout.Button(
                        PointLabel(index, _surfaceDraft.Points[index]),
                        style,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                    _surfaceDraft.SelectPoint(index);
            }

            for (int index = 0; index < _surfaceDraft.Faces.Count; index++)
            {
                SurfaceFace face = _surfaceDraft.Faces[index];
                GUIStyle style = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                                 _surfaceDraft.SelectedFace == index
                    ? DecorationEditorTheme.RowSelected
                    : DecorationEditorTheme.Row;
                if (GUILayout.Button(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Face {0}: {1}, {2}, {3}",
                            index + 1,
                            face.A + 1,
                            face.B + 1,
                            face.C + 1),
                        style,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                    _surfaceDraft.SelectFace(index);
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Ctrl-click existing points to create a face from any three points.", DecorationEditorTheme.MiniWrap);
            GUILayout.EndArea();
        }

        private void DrawSurfaceSettings()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (GUILayout.Button("<", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                StepSurfaceMaterial(-1);
            GUILayout.Label(_surfaceDraft.Settings.StructureBlockType.ToString(), DecorationEditorTheme.Body, GUILayout.Width(EsuHudLayout.Scale(112f)));
            if (GUILayout.Button(">", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                StepSurfaceMaterial(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Thickness", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _surfaceThicknessText = GUILayout.TextField(_surfaceThicknessText ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(64f)));
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(42f))))
                ApplySurfaceThicknessText();
            SurfaceThicknessButton(0.025f, "0.025");
            SurfaceThicknessButton(0.05f, "0.05");
            SurfaceThicknessButton(0.1f, "0.1");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            _surfaceColorText = GUILayout.TextField(_surfaceColorText ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(EsuHudLayout.Scale(44f)));
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(42f))))
                ApplySurfaceColorText();
            if (GUILayout.Button("-", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                SetSurfaceColor(_surfaceDraft.Settings.ColorIndex - 1);
            if (GUILayout.Button("+", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(28f))))
                SetSurfaceColor(_surfaceDraft.Settings.ColorIndex + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    _surfaceDraft.Settings.NormalReversal ? "Normal flip: on" : "Normal flip: off",
                    DecorationEditorTheme.ToolButton(_surfaceDraft.Settings.NormalReversal),
                    GUILayout.Width(EsuHudLayout.Scale(126f))))
            {
                _surfaceDraft.Settings.NormalReversal = !_surfaceDraft.Settings.NormalReversal;
                InvalidateSurfacePlan("Surface normal direction changed.");
            }
            if (GUILayout.Button(
                    _surfaceDraft.Settings.NearestAnchor ? "Nearest anchor: on" : "Nearest anchor: off",
                    DecorationEditorTheme.ToolButton(_surfaceDraft.Settings.NearestAnchor),
                    GUILayout.Width(EsuHudLayout.Scale(148f))))
            {
                _surfaceDraft.Settings.NearestAnchor = !_surfaceDraft.Settings.NearestAnchor;
                InvalidateSurfacePlan("Surface anchor mode changed.");
            }
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
            int count = Enum.GetValues(typeof(StructureBlockType)).Length;
            int next = ((int)_surfaceDraft.Settings.StructureBlockType + delta + count) % count;
            _surfaceDraft.Settings.StructureBlockType = (StructureBlockType)next;
            InvalidateSurfacePlan("Surface material changed.");
        }

        private void SurfaceThicknessButton(float value, string label)
        {
            if (GUILayout.Button(label, DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(48f))))
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

            _surfaceDraft.Settings.FaceThickness = Mathf.Clamp(value, 0.001f, 10f);
            _surfaceThicknessText = FormatFloat(_surfaceDraft.Settings.FaceThickness);
            InvalidateSurfacePlan("Surface thickness changed.");
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
            _surfaceDraft.Settings.ColorIndex = Mathf.Clamp(value, 0, 31);
            _surfaceColorText = _surfaceDraft.Settings.ColorIndex.ToString(CultureInfo.InvariantCulture);
            InvalidateSurfacePlan("Surface color changed.");
        }

        private string SurfaceSummary()
        {
            string count = _surfacePlan == null
                ? "no preview"
                : _surfacePlan.DecorationCount.ToString("N0", CultureInfo.InvariantCulture) + " decorations";
            string selection = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Point
                ? "point " + (_surfaceDraft.SelectedPoint + 1).ToString(CultureInfo.InvariantCulture)
                : _surfaceDraft.SelectionKind == SurfaceSelectionKind.Edge
                    ? "edge " + (_surfaceDraft.SelectedEdge.A + 1).ToString(CultureInfo.InvariantCulture) +
                      "-" + (_surfaceDraft.SelectedEdge.B + 1).ToString(CultureInfo.InvariantCulture)
                    : _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face
                        ? "face " + (_surfaceDraft.SelectedFace + 1).ToString(CultureInfo.InvariantCulture)
                        : "none";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0} points | {1:N0} faces | {2} | selected {3}",
                _surfaceDraft.Points.Count,
                _surfaceDraft.Faces.Count,
                count,
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

        private void DrawInspectorPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f));
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader("Inspector", "settings", GUILayout.Width(EsuHudLayout.Scale(126f)));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                _selected == null || _selected.IsDeleted ? "No selection" : MeshName(_selected.MeshGuid.Us),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(132f)));
            if (GUILayout.Button("Hide", DecorationEditorTheme.Button, GUILayout.Width(EsuHudLayout.Scale(58f))))
                _showInspectorPanel = false;
            GUILayout.EndHorizontal();

            float contentHeight = Mathf.Max(EsuHudLayout.Scale(120f), rect.height - EsuHudLayout.Scale(38f));
            DrawInspector(contentHeight);
            GUILayout.EndArea();
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

                bool previous = GUI.enabled;
                GUI.enabled = previous && _selected != null && !_selected.IsDeleted;
                if (GUILayout.Button(
                        "Set",
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
            float rowHeight = EsuHudLayout.Scale(MeshPreviewGridRowHeight);
            float cardWidth = EsuHudLayout.Scale(MeshPreviewGridCardWidth);
            float cardHeight = EsuHudLayout.Scale(MeshPreviewGridCardHeight);
            int columns = Mathf.Max(
                1,
                Mathf.Min(MeshPreviewGridColumns, Mathf.FloorToInt((viewportWidth - EsuHudLayout.Scale(20f)) / cardWidth)));
            int totalRows = Mathf.CeilToInt(rows.Count / (float)columns);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_meshScroll.y / rowHeight));
            int lastRow = Mathf.Min(
                totalRows,
                Mathf.CeilToInt((_meshScroll.y + viewportHeight) / rowHeight));
            bool canRenderVisiblePreview = Event.current != null && Event.current.type == EventType.Repaint;
            if (firstRow > 0)
                GUILayout.Space(firstRow * rowHeight);
            for (int gridRow = firstRow; gridRow < lastRow; gridRow++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int rowIndex = gridRow * columns + column;
                    if (rowIndex >= rows.Count)
                    {
                        GUILayout.Space(cardWidth);
                        continue;
                    }

                    DecorationMeshCatalogEntry entry = rows[rowIndex];
                    bool active = ReferenceEquals(entry, _selectedMesh) || ReferenceEquals(entry, _placingMesh);
                    GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                    string label = CompactText(entry.Name, Mathf.Max(10, Mathf.FloorToInt(cardWidth / 7f)));
                    Rect card = GUILayoutUtility.GetRect(
                        cardWidth,
                        cardHeight,
                        GUILayout.Width(cardWidth),
                        GUILayout.Height(cardHeight));
                    if (GUI.Button(card, GUIContent.none, style))
                        StartMeshPlacement(entry);
                    bool hovered = mouseInListViewport && card.Contains(Event.current.mousePosition);
                    if (hovered)
                        _hoveredMesh = entry;

                    int previewPixels = Mathf.Clamp(
                        Mathf.RoundToInt(MeshPreviewGridTexturePixels * EsuHudLayout.CurrentScale),
                        64,
                        128);
                    Texture preview = canRenderVisiblePreview
                        ? _previewRenderer?.GetPreview(entry, previewPixels, _previewSpin)
                        : _previewRenderer?.GetCachedPreview(entry, previewPixels);

                    Rect previewRect = new Rect(
                        card.x + EsuHudLayout.Scale(6f),
                        card.y + EsuHudLayout.Scale(5f),
                        card.width - EsuHudLayout.Scale(12f),
                        EsuHudLayout.Scale(54f));
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
                        label,
                        DecorationEditorTheme.Mini);
                }
                GUILayout.EndHorizontal();
            }
            if (lastRow < totalRows)
                GUILayout.Space((totalRows - lastRow) * rowHeight);
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

        private void DrawBottomPanel(float height)
        {
            bool surface = IsSurfaceMode;
            GUILayout.BeginHorizontal();
            GUILayout.Label(surface ? "Surface Builder" : "Decoration Edit Mode", DecorationEditorTheme.SubHeader, GUILayout.Width(EsuHudLayout.Scale(168f)));
            GUILayout.Label(surface ? "Mode: Surface | Tab to Build when clean" : "Mode: Deco | Tab to Surface when clean", DecorationEditorTheme.Body);
            if (!surface)
                DrawBottomAnchorFollowToggle();
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                HasUnappliedChanges ? "Dirty preview" : "Clean",
                HasUnappliedChanges ? DecorationEditorTheme.Warning : DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(92f)));
            GUILayout.EndHorizontal();
            DecorationEditorTheme.Separator();
            DrawStatusStrip();
            DrawBottomTransformEditors();
            GUILayout.Label(
                "Tab switches ESU modes when clean | Ctrl+D/Esc closes and restores un-applied edits | Select rows or viewport centers | Move snaps 0.05m",
                DecorationEditorTheme.Mini);
        }

        private void DrawBottomAnchorFollowToggle()
        {
            GUILayout.Space(EsuHudLayout.Scale(8f));
            if (GUILayout.Button(
                    new GUIContent(
                        _anchorFollowDecoration ? "Anchor follow: on" : "Anchor follow: off",
                        "When enabled, moving a decoration retethers its anchor to the nearest valid block once the center is outside the follow range."),
                    DecorationEditorTheme.ToolButton(_anchorFollowDecoration),
                    GUILayout.Width(EsuHudLayout.Scale(132f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                ToggleAnchorFollow();
            }
        }

        private void DrawStatusStrip()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(StatusLine(), DecorationEditorTheme.Status);
            GUILayout.EndHorizontal();
        }

        private void DrawBottomTransformEditors()
        {
            Rect row = GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(30f), GUILayout.ExpandWidth(true));
            float edgePadding = EsuHudLayout.Scale(4f);
            float gap = EsuHudLayout.Scale(8f);
            float availableWidth = Mathf.Max(1f, row.width - edgePadding * 2f);
            float width = Mathf.Max(1f, Mathf.Min(EsuHudLayout.Scale(386f), (availableWidth - gap * 2f) / 3f));
            float totalWidth = width * 3f + gap * 2f;
            float x = row.xMax - edgePadding - totalWidth;
            if (x < row.x + edgePadding)
                x = row.x + edgePadding;

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

        private void DrawBottomVectorEditor(
            Rect rect,
            string label,
            string[] values,
            Action<Vector3> apply)
        {
            bool hasSelection = _selected != null && !_selected.IsDeleted;
            float fieldHeight = EsuHudLayout.Scale(24f);
            float verticalOffset = EsuHudLayout.Scale(3f);
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
            float setX = Mathf.Min(x + gap, rect.xMax - setWidth);
            if (GUI.Button(
                    new Rect(
                        setX,
                        rect.y + verticalOffset,
                        setWidth,
                        fieldHeight),
                    "Set",
                    hasSelection ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton) &&
                hasSelection)
            {
                if (TryParseVector(values, out Vector3 parsed))
                    apply(parsed);
                else
                    InfoStore.Add($"{label} contains incomplete, invalid, NaN, or infinity input.");
            }
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
            GUI.Label(
                new Rect(x, y + EsuHudLayout.Scale(4f), axisWidth, fieldHeight),
                axis.ToString(),
                BottomVectorAxisStyle(axis));
            x += axisWidth + gap;
            Rect fieldRect = new Rect(x, y + EsuHudLayout.Scale(3f), fieldWidth, fieldHeight);
            if (hasSelection)
            {
                values[index] = GUI.TextField(
                    fieldRect,
                    values[index] ?? string.Empty,
                    DecorationEditorTheme.TextField);
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
            return $"View: {ViewModeDisplayName(_viewMode)} | Tool: {_tool}{placing}{surface} | Selected: {selected} | Dirty: {(_dirty ? "yes" : "no")} | Undo {_history.UndoCount}/Redo {_history.RedoCount} | Save: {format} | {counts}";
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
                "Click: place on pointer. Set: assign to selected.",
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
                    _surfaceDragAxis = DecorationEditAxis.None;
                    RebuildSurfacePreview(showMessage: false);
                    return;
                }

                TryUpdateSurfaceDrag(_lastMouseGui - _surfaceDragMouseStart);
                return;
            }

            if (_tool == DecorationEditorTool.Surface)
            {
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

            if (!Input.GetMouseButtonDown(0))
                return;

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
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            if (_dragAxis == DecorationEditAxis.Free)
            {
                Vector3 freeDelta = _freeDragCameraRight * (mouseDelta.x * _freeDragMetresPerPixel) -
                                    _freeDragCameraUp * (mouseDelta.y * _freeDragMetresPerPixel);
                Vector3 freeNext = DecorationEditMath.Snap(_dragPositionStart + freeDelta);
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
            axisDelta = DecorationEditMath.Snap(axisDelta);
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
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
                return;

            Vector2 current = _lastMouseGui - _rotateDragCenterScreen;
            if (_rotateDragStartVector.sqrMagnitude < 16f || current.sqrMagnitude < 16f)
                return;

            Vector3 axisVector = DecorationEditMath.AxisVector(_rotateDragAxis);
            float degrees = DecorationEditMath.Snap(
                Vector2.SignedAngle(_rotateDragStartVector, current),
                RotateSnapDegrees);
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
            DecorationEditSnapshot before = _rotateDragSnapshotStart;
            _rotateDragSnapshotStart = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Rotate decoration", before);
        }

        private void TryUpdateScale(Vector2 mouseDelta)
        {
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
            delta = DecorationEditMath.Snap(delta, ScaleSnap);
            Vector3 next = _scaleStart + DecorationEditMath.AxisVector(_scaleDragAxis) * delta;
            if (!IsValidScale(next))
                return;

            _selected.Scaling.Us = next;
            _selected.Changed();
            _dirty = true;
        }

        private void CommitScaleEdit()
        {
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
                _dirty = _transactions.HasChanges;
                return;
            }

            _dirty = _transactions.HasChanges;
        }

        private Vector3 ActiveTransformAxisVector(DecorationEditAxis axis) =>
            ActiveTransformAxisVector(_selected, axis);

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
                InfoStore.Add("No decoration center near the cursor.");
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
            _selection.Clear();
            _selection.Add(decoration);
            DecorationMeshCatalogEntry matching = _meshCatalog.FirstOrDefault(
                entry => entry.Guid == decoration.MeshGuid.Us);
            if (matching != null)
                _selectedMesh = matching;
            if (_tool != DecorationEditorTool.Anchor &&
                _tool != DecorationEditorTool.Paint)
            {
                _tool = DecorationEditorTool.Move;
            }
            ResetInspectorFields();
            if (notify)
                InfoStore.Add("Decoration selected.");
        }

        private void ApplySelection(bool notify = true)
        {
            ClearApplyCancelAttention();
            if (_selected != null && !_selected.IsDeleted)
                _snapshot = new DecorationEditSnapshot(_selected);
            _transactions.Apply();
            ClearSurfaceDraft(notify: false);
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
            CancelPlacement();
            ClearSurfaceDraft(notify: false);
            _history.Clear();
            _transactions.Cancel();
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
                for (int index = created.Count - 1; index >= 0; index--)
                {
                    try { created[index]?.Delete(); }
                    catch { /* The creation failure is the actionable result. */ }
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
            target = current;
            if (_selectedConstruct == null)
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
                        if (!HasBlock(_selectedConstruct, candidate))
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

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
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
                if (_surfaceDraft.SelectionKind != SurfaceSelectionKind.None)
                {
                    _surfaceDraft.ClearSelection();
                    _surfaceMessage = "Surface selection cleared.";
                }
                else
                {
                    ClearSurfaceDraft(notify: true);
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();

            if (TryPickSurfaceHandle(_lastMouseGui, out DecorationEditAxis axis))
            {
                BeginSurfacePointDrag(axis);
                return;
            }

            if (TryPickSurfacePoint(_lastMouseGui, out int pointIndex))
            {
                if (IsControlHeld())
                {
                    if (!_surfaceDraft.ToggleManualFacePoint(pointIndex, out _surfaceMessage))
                        InfoStore.Add(_surfaceMessage);
                    else
                        InvalidateSurfacePlan(_surfaceMessage);
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
                _surfaceDraft.SelectEdge(edge.A, edge.B);
                _surfaceMessage = "Surface edge selected. Click a new point to extend.";
                return;
            }

            if (TryPickSurfaceFace(_lastMouseGui, out int faceIndex))
            {
                _surfaceDraft.SelectFace(faceIndex);
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
            if (!_surfaceDraft.TryAddPoint(hit.Construct, hit.LocalHit, extend, out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            InvalidateSurfacePlan(_surfaceMessage);
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
                next = _surfaceDragPointStart + freeDelta;
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
                next = _surfaceDragPointStart + axisVector * DecorationEditMath.Snap(axisDelta);
            }

            if (_surfaceDraft.TryMovePoint(_surfaceDraft.SelectedPoint, next, out string message))
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
            if (!SurfaceDecorationPlanner.TryPlan(
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
                for (int index = created.Count - 1; index >= 0; index--)
                {
                    try { created[index]?.Delete(); }
                    catch { }
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

        private void ClearSurfaceDraft(bool notify)
        {
            _surfaceDraft.Clear();
            _surfacePlan = null;
            _surfaceDragAxis = DecorationEditAxis.None;
            _surfaceMessage = "Click three block-surface points to seed a freeform surface.";
            if (notify)
                InfoStore.Add("Surface draft cleared.");
        }

        private void DeleteSurfaceSelection()
        {
            if (!_surfaceDraft.TryDeleteSelection(out _surfaceMessage))
            {
                InfoStore.Add(_surfaceMessage);
                return;
            }

            _surfacePlan = null;
            InfoStore.Add(_surfaceMessage);
        }

        private void InvalidateSurfacePlan(string message)
        {
            _surfacePlan = null;
            if (!string.IsNullOrEmpty(message))
                _surfaceMessage = message;
        }

        private static bool IsControlHeld() =>
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        internal bool HandleEscape()
        {
            if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
            {
                DecoLimitLifter.EsuSymmetry.CancelPending();
                InfoStore.Add("Symmetry plane placement cancelled.");
                return true;
            }

            if (_surfaceDragAxis != DecorationEditAxis.None)
            {
                _surfaceDragAxis = DecorationEditAxis.None;
                _surfaceMessage = "Surface point move cancelled.";
                return true;
            }

            if (_tool == DecorationEditorTool.Surface && _surfaceDraft.HasDraft)
            {
                if (_surfaceDraft.SelectionKind != SurfaceSelectionKind.None)
                {
                    _surfaceDraft.ClearSelection();
                    _surfaceMessage = "Surface selection cleared.";
                }
                else
                {
                    ClearSurfaceDraft(notify: true);
                }
                return true;
            }

            if (_placingMesh == null)
                return false;

            CancelPlacement();
            return true;
        }

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
            if (_textInputFocused)
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

        private void UndoEdit()
        {
            CancelPlacement();
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
                try { decoration.Delete(); }
                catch { /* The restore failure is the actionable result. */ }
                InfoStore.Add("Redo failed because FTD rejected the recreated decoration.");
                return false;
            }

            _transactions.MarkCreated(decoration);
            SelectFromHistory(decoration, construct, createdInSession: true);
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
            DrawSymmetryOverlay();
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

            if (_tool == DecorationEditorTool.Move)
            {
                DrawAxis(centerLocal, DecorationEditAxis.X);
                DrawAxis(centerLocal, DecorationEditAxis.Y);
                DrawAxis(centerLocal, DecorationEditAxis.Z);
                if (_dragAxis == DecorationEditAxis.Free)
                    DecorationEditorOverlay.Circle(centerWorld, 0.34f * selectedScale, Color.yellow, Vector3.up, 4f, 28);
            }
            else if (_tool == DecorationEditorTool.Rotate)
            {
                DrawRotateGizmo(centerLocal);
            }
            else if (_tool == DecorationEditorTool.Scale)
            {
                DrawScaleGizmo(centerLocal);
            }
            else if (_tool == DecorationEditorTool.Anchor)
            {
                DrawAnchorGizmo(anchorLocal);
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

        private void DrawSurfaceOverlay()
        {
            if (!_surfaceDraft.HasDraft || _surfaceDraft.Construct == null)
                return;

            AllConstruct construct = _surfaceDraft.Construct;
            Color fill = _surfacePlan == null
                ? new Color(0.1f, 0.75f, 1f, 0.18f)
                : new Color(0.2f, 1f, 0.55f, 0.22f);
            Color edgeColor = new Color(0.1f, 0.95f, 1f, 0.92f);
            Color selected = new Color(1f, 0.9f, 0.2f, 1f);
            Color manual = new Color(1f, 0.45f, 0.95f, 1f);

            for (int index = 0; index < _surfaceDraft.Faces.Count; index++)
            {
                SurfaceFace face = _surfaceDraft.Faces[index];
                Vector3 a = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.A]);
                Vector3 b = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.B]);
                Vector3 c = construct.SafeLocalToGlobal(_surfaceDraft.Points[face.C]);
                Color faceFill = _surfaceDraft.SelectionKind == SurfaceSelectionKind.Face &&
                                 _surfaceDraft.SelectedFace == index
                    ? new Color(selected.r, selected.g, selected.b, 0.26f)
                    : fill;
                DecorationEditorOverlay.Quad(a, b, c, c, faceFill);
                DrawSurfaceEdge(face.A, face.B, edgeColor);
                DrawSurfaceEdge(face.B, face.C, edgeColor);
                DrawSurfaceEdge(face.C, face.A, edgeColor);
            }

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
            Color color = active ? new Color(1f, 0.9f, 0.2f, 1f) : defaultColor;
            float width = active ? 4f : 2.2f;
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

        private void DrawAxis(Vector3 centerLocal, DecorationEditAxis axis)
        {
            Vector3 axisVector = ActiveTransformAxisVector(axis);
            Color color = DecorationEditMath.AxisColor(axis);
            float width = _dragAxis == axis ? 4.5f : 3f;
            Vector3 start = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 end = _selectedConstruct.SafeLocalToGlobal(centerLocal + axisVector * HandleLength);
            DecorationEditorOverlay.Arrow(start, end, color, width, 0.2f);
        }

        private void DrawRotateGizmo(Vector3 centerLocal)
        {
            DrawRotationRing(centerLocal, DecorationEditAxis.X);
            DrawRotationRing(centerLocal, DecorationEditAxis.Y);
            DrawRotationRing(centerLocal, DecorationEditAxis.Z);
        }

        private void DrawRotationRing(Vector3 centerLocal, DecorationEditAxis axis)
        {
            Vector3 centerWorld = _selectedConstruct.SafeLocalToGlobal(centerLocal);
            Vector3 normalWorld = _selectedConstruct.SafeLocalToGlobal(
                centerLocal + ActiveTransformAxisVector(axis)) - centerWorld;
            if (normalWorld.sqrMagnitude < 0.0001f)
                normalWorld = DecorationEditMath.AxisVector(axis);

            Color color = DecorationEditMath.AxisColor(axis);
            float width = _rotateDragAxis == axis ? 4f : 2.5f;
            DecorationEditorOverlay.Circle(centerWorld, RotateGizmoRadius, color, normalWorld.normalized, width, 48);
        }

        private void DrawScaleGizmo(Vector3 centerLocal)
        {
            DrawScaleAxis(centerLocal, DecorationEditAxis.X);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Y);
            DrawScaleAxis(centerLocal, DecorationEditAxis.Z);
        }

        private void DrawScaleAxis(Vector3 centerLocal, DecorationEditAxis axis)
        {
            Vector3 axisVector = ActiveTransformAxisVector(axis);
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

        private void ApplyPositionFromInspector(Vector3 value)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            value = DecorationEditMath.Snap(value);
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
                SetVectorText(_positionText, _selected.Positioning.Us);
                RefreshDecorationCache(force: true);
                return;
            }

            SetVectorText(_positionText, _selected.Positioning.Us);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set position", before, symmetryFollow);
            RefreshDecorationCache(force: true);
        }

        private void ApplyScaleFromInspector(Vector3 value)
        {
            if (_selected == null || _selected.IsDeleted)
                return;
            if (!IsValidScale(value))
            {
                InfoStore.Add("Scale must be finite and non-collapsed on every axis.");
                return;
            }

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.Scaling.Us = value;
            SetVectorText(_scaleText, value);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set scale", before);
        }

        private static bool IsValidScale(Vector3 value) =>
            DecorationEditMath.IsFinite(value) &&
            Mathf.Abs(value.x) >= MinimumScale &&
            Mathf.Abs(value.y) >= MinimumScale &&
            Mathf.Abs(value.z) >= MinimumScale;

        private void ApplyOrientationFromInspector(Vector3 value)
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
            value.ToString("0.###", CultureInfo.InvariantCulture);

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
