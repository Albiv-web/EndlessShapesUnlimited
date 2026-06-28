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
        private const int MaxMeshGridPreviewRendersPerFrame = 4;
        private const float OutlinerRowHeight = 22f;
        private const float MeshPaletteRowHeight = 24f;
        private const float MeshPreviewGridRowHeight = 90f;
        private const int MeshPreviewGridColumns = 4;
        private const float MeshPreviewGridCardWidth = 112f;
        private const float MeshPreviewGridCardHeight = 86f;
        private const int MeshPreviewGridTexturePixels = 96;
        private const float RotateSnapDegrees = 5f;
        private const float RotateGizmoRadius = 0.82f;
        private const float ScaleSnap = 0.05f;
        private const float MinimumScale = 0.01f;
        private const float AnchorFollowMinimumDistance = 1f;
        private const float AnchorFollowMaximumDistance = 10f;
        private const int AnchorFollowMaximumSearchRadius = 8;
        private static Rect s_meshPaletteRect = new Rect(18f, 110f, 480f, 1050f);
        private static bool s_showMeshPalette = true;
        private static bool s_showOutlinerPanel = true;
        private static bool s_meshPreviewGrid;
        private static DecorationTransformOrientation s_transformOrientation = DecorationTransformOrientation.Global;
        private static bool s_anchorMenuOpen;
        private static bool s_anchorFollowDecoration;
        private static float s_anchorFollowDistance = AnchorFollowMinimumDistance;

        private readonly cBuild _build;
        private readonly int _modalWindowId = "EndlessShapesUnlimited.DecorationEditMode.Modal".GetHashCode();
        private Rect _meshPaletteRect = s_meshPaletteRect;
        private Vector2 _meshScroll;
        private Vector2 _outlinerScroll;
        private Vector2 _inspectorScroll;
        private Vector2 _anchorListScroll;
        private bool _mouseOverWindow;
        private bool _draggingMeshPalette;
        private Vector2 _meshPaletteDragOffset;
        private bool _meshPreviewGrid = s_meshPreviewGrid;
        private bool _textInputFocused;
        private bool _viewModeMenuOpen;
        private bool _anchorMenuOpen = s_anchorMenuOpen;
        private bool _showMeshPalette = s_showMeshPalette;
        private bool _showOutlinerPanel = s_showOutlinerPanel;
        private bool _showTetherPins;
        private bool _anchorFollowDecoration = s_anchorFollowDecoration;
        private float _anchorFollowDistance = s_anchorFollowDistance;
        private string _anchorFollowDistanceText =
            s_anchorFollowDistance.ToString("0.###", CultureInfo.InvariantCulture);
        private readonly HashSet<string> _collapsedConstructs = new HashSet<string>();

        private MBuild_Ftd _buildOptions;
        private bool _previousWireframe;
        private bool _focusApplied;
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
        private DecorationEditorViewMode _viewMode = DecorationEditorViewMode.Mixed;
        private DecorationEditorTool _tool = DecorationEditorTool.Select;
        private DecorationTransformOrientation _transformOrientation = s_transformOrientation;
        private DecorationEditAxis _dragAxis;
        private Vector2 _dragMouseStart;
        private Vector3 _dragPositionStart;
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
        private readonly string[] _positionText = new string[3];
        private readonly string[] _scaleText = new string[3];
        private readonly string[] _orientationText = new string[3];
        private string _colorText = string.Empty;
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

        internal bool HasUnappliedChanges =>
            _dirty ||
            _transactions.HasChanges ||
            _placingMesh != null ||
            _dragAxis != DecorationEditAxis.None ||
            _rotateDragAxis != DecorationEditAxis.None ||
            _scaleDragAxis != DecorationEditAxis.None ||
            _anchorDragAxis != DecorationEditAxis.None;

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

            if (_dragAxis != DecorationEditAxis.None ||
                _rotateDragAxis != DecorationEditAxis.None ||
                _scaleDragAxis != DecorationEditAxis.None ||
                _anchorDragAxis != DecorationEditAxis.None)
            {
                reason = "Finish the active Decoration Edit drag before switching modes.";
                return false;
            }

            if (_dirty || _transactions.HasChanges)
            {
                reason = "Apply or Cancel Decoration Edit changes before switching to Smart Builder.";
                return false;
            }

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
                _rotateDragAxis = DecorationEditAxis.None;
                _rotateDragSnapshotStart = null;
                _scaleDragAxis = DecorationEditAxis.None;
                _scaleDragSnapshotStart = null;
                _anchorDragAxis = DecorationEditAxis.None;
                _anchorDragSnapshotStart = null;
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
                _anchorDragAxis != DecorationEditAxis.None)
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
                _anchorDragAxis != DecorationEditAxis.None)
                return current.type != EventType.ScrollWheel;

            if (_placingMesh != null)
                return current.type == EventType.MouseDown &&
                       (current.button == 0 || current.button == 1);

            return current.button == 0 &&
                   (_tool == DecorationEditorTool.Select ||
                    _tool == DecorationEditorTool.Move ||
                    _tool == DecorationEditorTool.Rotate ||
                    _tool == DecorationEditorTool.Scale ||
                    _tool == DecorationEditorTool.Anchor);
        }

        private void DrawEditorShell()
        {
            Rect toolbarRect = ToolbarRect();
            Rect rightRect = RightPanelRect();
            Rect bottomRect = StatusRect(rightRect);
            Rect meshRect = MeshPaletteRect();
            Vector2 mouse = Event.current.mousePosition;
            _mouseOverWindow = toolbarRect.Contains(mouse) ||
                               (_viewModeMenuOpen && ViewModeMenuRect(toolbarRect).Contains(mouse)) ||
                               (_anchorMenuOpen && AnchorMenuRect(toolbarRect).Contains(mouse)) ||
                               (_showOutlinerPanel && rightRect.Contains(mouse)) ||
                               bottomRect.Contains(mouse) ||
                               (_showMeshPalette && meshRect.Contains(mouse));
            DecorationEditorInputScope.SetMouseOverEditorUi(_mouseOverWindow);

            if (_showMeshPalette)
                HandleMeshPaletteDrag(meshRect);

            GUILayout.BeginArea(toolbarRect, DecorationEditorTheme.Panel);
            DrawTopToolbar();
            GUILayout.EndArea();

            DrawViewModeMenu(toolbarRect);
            DrawAnchorMenu(toolbarRect);

            if (_showMeshPalette)
                DrawMeshPalette(meshRect);

            if (_showOutlinerPanel)
            {
                GUILayout.BeginArea(rightRect, DecorationEditorTheme.Panel);
                DrawRightPanel(rightRect);
                GUILayout.EndArea();
            }

            GUI.Box(bottomRect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(bottomRect);
            DrawBottomPanel(bottomRect.height - 12f);
            GUILayout.EndArea();

            DrawMeshPreviewCard();
            PersistPanelState();
        }

        private Rect ToolbarRect()
        {
            float width = Mathf.Min(1040f, Mathf.Max(720f, Screen.width - 16f));
            float x = Mathf.Max(8f, (Screen.width - width) * 0.5f);
            return new Rect(x, 8f, width, 54f);
        }

        private Rect RightPanelRect()
        {
            float width = Mathf.Min(390f, Mathf.Max(330f, Screen.width * 0.22f));
            float height = Mathf.Max(260f, Screen.height - BottomPanelHeight() - 104f);
            return new Rect(Screen.width - width - 10f, 74f, width, height);
        }

        private Rect StatusRect(Rect rightRect)
        {
            float height = BottomPanelHeight();
            return new Rect(10f, Screen.height - height - 10f, Screen.width - 20f, height);
        }

        private Rect MeshPaletteRect()
        {
            float width = Mathf.Min(520f, Mathf.Max(480f, Screen.width * 0.24f));
            const float topLimit = 44f;
            float bottomLimit = BottomPanelHeight() + 10f;
            float height = Mathf.Min(
                1170f,
                Mathf.Max(600f, Screen.height - topLimit - bottomLimit));
            _meshPaletteRect.width = width;
            _meshPaletteRect.height = height;
            _meshPaletteRect.x = Mathf.Clamp(_meshPaletteRect.x, 8f, Screen.width - width - 8f);
            float maxY = Mathf.Max(topLimit, Screen.height - bottomLimit - height);
            _meshPaletteRect.y = Mathf.Clamp(_meshPaletteRect.y, topLimit, maxY);
            s_meshPaletteRect = _meshPaletteRect;
            return _meshPaletteRect;
        }

        private float BottomPanelHeight()
        {
            return Mathf.Clamp(Screen.height * 0.105f, 88f, 112f);
        }

        private bool IsMouseOverEditorUi(Vector2 mouse) =>
            ToolbarRect().Contains(mouse) ||
            (_viewModeMenuOpen && ViewModeMenuRect(ToolbarRect()).Contains(mouse)) ||
            (_anchorMenuOpen && AnchorMenuRect(ToolbarRect()).Contains(mouse)) ||
            (_showOutlinerPanel && RightPanelRect().Contains(mouse)) ||
            StatusRect(RightPanelRect()).Contains(mouse) ||
            (_showMeshPalette && MeshPaletteRect().Contains(mouse));

        private void HandleMeshPaletteDrag(Rect meshRect)
        {
            Event current = Event.current;
            if (current == null)
                return;

            Rect title = new Rect(meshRect.x, meshRect.y, meshRect.width, 28f);
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
                s_meshPaletteRect = _meshPaletteRect;
                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                _draggingMeshPalette = false;
            }
        }

        private void PersistPanelState()
        {
            s_meshPaletteRect = _meshPaletteRect;
            s_showMeshPalette = _showMeshPalette;
            s_showOutlinerPanel = _showOutlinerPanel;
            s_meshPreviewGrid = _meshPreviewGrid;
            s_transformOrientation = _transformOrientation;
            s_anchorMenuOpen = _anchorMenuOpen;
            s_anchorFollowDecoration = _anchorFollowDecoration;
            s_anchorFollowDistance = _anchorFollowDistance;
        }

        private void DrawTopToolbar()
        {
            GUILayout.BeginHorizontal();
            ModeSwitchButton();
            ToolButton(DecorationEditorTool.Select, "select", "Select", "Click decoration centers or outliner rows.");
            ToolButton(DecorationEditorTool.Move, "move", "Move", "Drag XYZ handles. Snaps to 0.05m.");
            ToolButton(DecorationEditorTool.Rotate, "rotate", "Rotate", "Drag RGB rotation rings. Snaps to 5 degrees.");
            ToolButton(DecorationEditorTool.Scale, "scale", "Scale", "Drag RGB scale handles. Snaps to 0.05.");
            OrientationToggleButton();
            ToolButton(DecorationEditorTool.Anchor, "anchor", "Anchor", "Move tether point by whole blocks.");
            ToolButton(DecorationEditorTool.Paint, "paint", "Paint", "Edit color and material replacement.");
            ToolButton(DecorationEditorTool.Focus, "visibility", "View", "Current view: " + ViewModeShortName());
            GUILayout.FlexibleSpace();
            PanelToggle("mesh", "Pal", ref _showMeshPalette, "Show or hide the mesh palette.");
            PanelToggle("outliner", "Out", ref _showOutlinerPanel, "Show or hide the decoration outliner.");
            InspectorPanelButton("settings", "Insp", "Show selected decoration fields in the mesh palette.");
            AnchorPanelButton("anchor", "Anch", "Show decorations sharing the selected anchor in the right panel.");
            if (IconButton("undo", $"U{_history.UndoCount}", DecorationEditorTheme.Button, "Ctrl+Z: undo the last editor action.", _history.CanUndo))
                UndoEdit();
            if (IconButton("redo", $"R{_history.RedoCount}", DecorationEditorTheme.Button, "Ctrl+Y/Ctrl+Shift+Z: redo the last editor action.", _history.CanRedo))
                RedoEdit();
            if (IconButton("save", "Apply", DecorationEditorTheme.Button, "Commit preview changes."))
                ApplySelection();
            if (IconButton("cancel", "Cancel", DecorationEditorTheme.Button, "Restore the current preview."))
                CancelSelection();
            if (IconButton("delete", "Close", DecorationEditorTheme.Button, "Close and restore un-applied changes."))
            {
                CloseRequested = true;
                CloseApplies = false;
            }
            GUILayout.EndHorizontal();
        }

        private void ModeSwitchButton()
        {
            if (GUILayout.Button(
                    new GUIContent("Deco", DecorationEditorIconCatalog.Get("build"), "Tab: switch to Smart Builder when Decoration Edit is clean."),
                    DecorationEditorTheme.ToolButton(true),
                    GUILayout.Width(54f),
                    GUILayout.Height(40f)))
            {
                SwitchToSmartBuildRequested = true;
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
                    GUILayout.Width(58f),
                    GUILayout.Height(40f)))
            {
                _transformOrientation = local
                    ? DecorationTransformOrientation.Global
                    : DecorationTransformOrientation.Local;
                s_transformOrientation = _transformOrientation;
            }
        }

        private void PanelToggle(string icon, string label, ref bool state, string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    DecorationEditorTheme.ToolButton(state),
                    GUILayout.Width(44f),
                    GUILayout.Height(40f)))
            {
                state = !state;
            }
        }

        private void InspectorPanelButton(string icon, string label, string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    DecorationEditorTheme.ToolButton(_showMeshPalette),
                    GUILayout.Width(46f),
                    GUILayout.Height(40f)))
            {
                _showMeshPalette = true;
            }
        }

        private void AnchorPanelButton(string icon, string label, string tooltip)
        {
            if (GUILayout.Button(
                    new GUIContent(label, DecorationEditorIconCatalog.Get(icon), tooltip),
                    DecorationEditorTheme.ToolButton(_showOutlinerPanel),
                    GUILayout.Width(46f),
                    GUILayout.Height(40f)))
            {
                _showOutlinerPanel = true;
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
            if (GUILayout.Button(content, style, GUILayout.Width(58f), GUILayout.Height(40f)) && enabled)
            {
                if (tool == DecorationEditorTool.Focus)
                {
                    _viewModeMenuOpen = !_viewModeMenuOpen;
                }
                else if (tool == DecorationEditorTool.Anchor)
                {
                    _tool = tool;
                    _anchorMenuOpen = !_anchorMenuOpen;
                    _viewModeMenuOpen = false;
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
            GUILayout.BeginArea(rect, DecorationEditorTheme.Panel);
            GUILayout.BeginHorizontal();
            ViewModeButton(DecorationEditorViewMode.Mixed, "Mixed");
            ViewModeButton(DecorationEditorViewMode.Wireframe, "Wire");
            ViewModeButton(DecorationEditorViewMode.DecorationOnly, "Deco");
            ViewModeButton(DecorationEditorViewMode.Mass, "Mass");
            ViewModeButton(DecorationEditorViewMode.Drag, "Drag");
            ViewModeButton(DecorationEditorViewMode.Cost, "Cost");
            ViewModeButton(DecorationEditorViewMode.Surface, "Surface");
            ViewModeButton(DecorationEditorViewMode.Important, "Important");
            ViewModeButton(DecorationEditorViewMode.Normal, "Normal");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private Rect ViewModeMenuRect(Rect toolbarRect)
        {
            float width = Mathf.Min(540f, Screen.width - 24f);
            float x = Mathf.Clamp(toolbarRect.xMax - width, 8f, Screen.width - width - 8f);
            return new Rect(x, toolbarRect.yMax + 4f, width, 42f);
        }

        private Rect AnchorMenuRect(Rect toolbarRect)
        {
            float width = Mathf.Min(420f, Screen.width - 24f);
            float x = Mathf.Clamp(toolbarRect.x + 344f, 8f, Screen.width - width - 8f);
            return new Rect(x, toolbarRect.yMax + 4f, width, 88f);
        }

        private void DrawAnchorMenu(Rect toolbarRect)
        {
            if (!_anchorMenuOpen)
                return;

            GUILayout.BeginArea(AnchorMenuRect(toolbarRect), DecorationEditorTheme.Panel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    _anchorFollowDecoration ? "Follow: on" : "Follow: off",
                    DecorationEditorTheme.ToolButton(_anchorFollowDecoration),
                    GUILayout.Width(86f),
                    GUILayout.Height(26f)))
            {
                _anchorFollowDecoration = !_anchorFollowDecoration;
                s_anchorFollowDecoration = _anchorFollowDecoration;
                InfoStore.Add("Anchor follow " + (_anchorFollowDecoration ? "enabled." : "disabled."));
            }

            GUILayout.Label("Range", DecorationEditorTheme.Mini, GUILayout.Width(42f));
            _anchorFollowDistanceText = GUILayout.TextField(
                _anchorFollowDistanceText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Width(52f),
                GUILayout.Height(22f));
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(42f), GUILayout.Height(24f)))
                ApplyAnchorFollowDistanceText();

            AnchorFollowDistanceButton(1f, "1m");
            AnchorFollowDistanceButton(2f, "2m");
            AnchorFollowDistanceButton(3f, "3m");
            AnchorFollowDistanceButton(5f, "5m");
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "When enabled, moving a decoration retethers its anchor to the nearest valid block once the center is this far from the current anchor.",
                DecorationEditorTheme.MiniWrap);
            GUILayout.EndArea();
        }

        private void AnchorFollowDistanceButton(float distance, string label)
        {
            if (GUILayout.Button(
                    label,
                    DecorationEditorTheme.ToolButton(Math.Abs(_anchorFollowDistance - distance) < 0.001f),
                    GUILayout.Width(42f),
                    GUILayout.Height(24f)))
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

        private void ViewModeButton(DecorationEditorViewMode mode, string label)
        {
            if (GUILayout.Button(
                    label,
                    DecorationEditorTheme.ToolButton(_viewMode == mode),
                    GUILayout.Width(54f),
                    GUILayout.Height(26f)))
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
                GUILayout.Width(54f),
                GUILayout.Height(40f));

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
                    GUILayout.Width(54f),
                    GUILayout.Height(40f));
            }
            finally
            {
                GUI.enabled = previous;
            }
        }

        private void DrawRightPanel(Rect rightRect)
        {
            Rect inner = new Rect(8f, 8f, rightRect.width - 16f, rightRect.height - 16f);
            float anchorHeight = Mathf.Clamp(inner.height * 0.38f, 190f, 310f);
            float outlinerHeight = Mathf.Max(160f, inner.height - anchorHeight - 8f);
            Rect outlinerRect = new Rect(inner.x, inner.y, inner.width, outlinerHeight);
            Rect anchorRect = new Rect(inner.x, outlinerRect.yMax + 8f, inner.width, inner.yMax - outlinerRect.yMax - 8f);

            GUILayout.BeginArea(outlinerRect);
            DrawOutliner(outlinerRect.height);
            GUILayout.EndArea();

            Rect separator = new Rect(inner.x, outlinerRect.yMax + 3f, inner.width, 1f);
            GUI.DrawTexture(separator, DecorationEditorTheme.DimTexture);

            GUILayout.BeginArea(anchorRect);
            DrawAnchorContext(anchorRect.height);
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
                GUILayout.Height(Mathf.Max(80f, height - (_tool == DecorationEditorTool.Anchor ? 84f : 52f))));
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
                        GUILayout.Height(OutlinerRowHeight)))
                {
                    Select(decoration, _selectedConstruct);
                }
            }
            GUILayout.EndScrollView();
        }

        private static void DrawCompactAnchorHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            GUI.Label(rect, "      Selected anchor", DecorationEditorTheme.SubHeader);

            Texture icon = DecorationEditorIconCatalog.Get("anchor");
            if (icon == null)
                return;

            var iconRect = new Rect(rect.x + 5f, rect.y + 3f, 16f, 16f);
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
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(" Outliner", DecorationEditorIconCatalog.Get("outliner")), DecorationEditorTheme.SubHeader);
            if (GUILayout.Button(
                    _showTetherPins ? "Pins on" : "Pins off",
                    DecorationEditorTheme.ToolButton(_showTetherPins),
                    GUILayout.Width(76f),
                    GUILayout.Height(24f)))
            {
                _showTetherPins = !_showTetherPins;
                RefreshDecorationCache(force: true);
            }
            if (GUILayout.Button(new GUIContent("Refresh", DecorationEditorIconCatalog.Get("create")), DecorationEditorTheme.Button, GUILayout.Width(86f), GUILayout.Height(24f)))
                RefreshDecorationCache(force: true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")), GUILayout.Width(24f), GUILayout.Height(22f));
            _outlinerFilter = GUILayout.TextField(_outlinerFilter ?? string.Empty, DecorationEditorTheme.TextField);
            GUILayout.EndHorizontal();

            List<OutlinerRow> rows = FilterOutlinerRows();
            GUILayout.Label(
                $"{rows.Count:N0} rows | {_hits.Count:N0} visible/projected decorations",
                DecorationEditorTheme.Mini);

            _outlinerScroll = GUILayout.BeginScrollView(_outlinerScroll, GUILayout.Height(Mathf.Max(96f, height - 76f)));
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_outlinerScroll.y / OutlinerRowHeight) - 4);
            int last = Mathf.Min(total, first + MaxOutlinerDrawRows);
            if (first > 0)
                GUILayout.Space(first * OutlinerRowHeight);
            for (int i = first; i < last; i++)
                DrawOutlinerRow(rows[i]);
            if (last < total)
                GUILayout.Space((total - last) * OutlinerRowHeight);
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
            GUILayout.Space(row.Depth * 14f);
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
            if (GUILayout.Button(new GUIContent(label, DecorationEditorIconCatalog.Get(icon)), style, GUILayout.Height(OutlinerRowHeight)))
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inspector", DecorationEditorTheme.SubHeader, GUILayout.Width(96f));
            if (_selected != null && !_selected.IsDeleted)
                GUILayout.Label(CompactText(MeshName(_selected.MeshGuid.Us), 38), DecorationEditorTheme.MiniWrap);
            GUILayout.EndHorizontal();
            _inspectorScroll = GUILayout.BeginScrollView(
                _inspectorScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(Mathf.Max(58f, height - 26f)));
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
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(72f));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.BodyWrap);
            GUILayout.EndHorizontal();
        }

        private void DrawVectorEditor(string label, string[] values, Action<Vector3> apply)
        {
            GUILayout.Label(label, DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", DecorationEditorTheme.Mini, GUILayout.Width(12f));
            values[0] = GUILayout.TextField(values[0] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(72f));
            GUILayout.Label("Y", DecorationEditorTheme.Mini, GUILayout.Width(12f));
            values[1] = GUILayout.TextField(values[1] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(72f));
            GUILayout.Label("Z", DecorationEditorTheme.Mini, GUILayout.Width(12f));
            values[2] = GUILayout.TextField(values[2] ?? string.Empty, DecorationEditorTheme.TextField, GUILayout.Width(72f));
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(42f)))
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
                            GUILayout.Width(36f),
                            GUILayout.Height(24f)))
                    {
                        SetSelectedColor(color);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Color", DecorationEditorTheme.Mini, GUILayout.Width(72f));
            _colorText = GUILayout.TextField(_colorText ?? string.Empty, DecorationEditorTheme.TextField);
            if (GUILayout.Button("0-31", DecorationEditorTheme.Button, GUILayout.Width(52f)))
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

            DecorationEditSnapshot before = new DecorationEditSnapshot(_selected);
            _selected.Color.Us = Mathf.Clamp(color, 0, 31);
            _colorText = _selected.Color.Us.ToString(CultureInfo.InvariantCulture);
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
                    GUILayout.Height(24f));
            if (hasMaterialOverride &&
                GUILayout.Button(
                    "Clear",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(54f),
                    GUILayout.Height(24f)))
            {
                SetSelectedMaterial(Guid.Empty);
            }

            if (GUILayout.Button(
                    _showMaterialPicker ? "Hide list" : "Show list",
                    DecorationEditorTheme.Button,
                    GUILayout.Width(76f),
                    GUILayout.Height(24f)))
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
                GUILayout.Label(new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")), GUILayout.Width(24f), GUILayout.Height(22f));
                _materialFilter = GUILayout.TextField(_materialFilter ?? string.Empty, DecorationEditorTheme.TextField);
                GUILayout.EndHorizontal();

                List<MaterialCatalogEntry> materials = FilterMaterials().ToList();
                GUILayout.Label(MaterialCountText(materials.Count), DecorationEditorTheme.Mini);
                float height = Mathf.Min(180f, Mathf.Max(70f, materials.Count * 22f + 8f));
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
                            GUILayout.Height(22f)))
                    {
                        SetSelectedMaterial(material.Guid);
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("GUID", DecorationEditorTheme.Mini, GUILayout.Width(72f));
            _materialText = GUILayout.TextField(_materialText ?? string.Empty, DecorationEditorTheme.TextField);
            if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(42f)))
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
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect inner = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            float headerHeight = 116f;
            const float countHeight = 18f;
            float placingHeight = _placingMesh == null ? 0f : 24f;
            const float detailsHeight = 342f;
            float listHeight = Mathf.Max(
                210f,
                inner.height - headerHeight - countHeight - placingHeight - detailsHeight - 12f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            Rect countRect = new Rect(inner.x, headerRect.yMax, inner.width, countHeight);
            Rect listRect = new Rect(inner.x, countRect.yMax, inner.width, listHeight);
            Rect placingRect = new Rect(inner.x, listRect.yMax + 4f, inner.width, placingHeight);
            Rect detailsRect = new Rect(
                inner.x,
                listRect.yMax + placingHeight + 10f,
                inner.width,
                Mathf.Max(160f, inner.yMax - listRect.yMax - placingHeight - 10f));
            bool mouseInListViewport = listRect.Contains(Event.current.mousePosition);

            GUILayout.BeginArea(headerRect);
            GUILayout.BeginVertical();
            GUILayout.Label(new GUIContent(" Mesh Palette", DecorationEditorIconCatalog.Get("mesh")), DecorationEditorTheme.Header);
            GUILayout.Label("Drag this panel. Click a mesh to place it; Set swaps the selected decoration.", DecorationEditorTheme.Mini);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("List", DecorationEditorTheme.ToolButton(!_meshPreviewGrid), GUILayout.Width(62f)))
                _meshPreviewGrid = false;
            if (GUILayout.Button("3D grid", DecorationEditorTheme.ToolButton(_meshPreviewGrid), GUILayout.Width(76f)))
                _meshPreviewGrid = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Hide", DecorationEditorTheme.Button, GUILayout.Width(58f)))
                _showMeshPalette = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", DecorationEditorTheme.ToolButton(_meshKindFilter == "all"), GUILayout.Width(48f)))
                _meshKindFilter = "all";
            if (GUILayout.Button("Items", DecorationEditorTheme.ToolButton(_meshKindFilter == "item"), GUILayout.Width(58f)))
                _meshKindFilter = "item";
            if (GUILayout.Button("Objects", DecorationEditorTheme.ToolButton(_meshKindFilter == "object"), GUILayout.Width(68f)))
                _meshKindFilter = "object";
            if (GUILayout.Button("Recent", DecorationEditorTheme.ToolButton(_meshKindFilter == "recent"), GUILayout.Width(68f)))
                _meshKindFilter = "recent";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(string.Empty, DecorationEditorIconCatalog.Get("filter")), GUILayout.Width(24f), GUILayout.Height(22f));
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
                DrawMeshPreviewGrid(rows, listRect.height, mouseInListViewport);
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

            DrawPaletteDetails(detailsRect);
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

        private void DrawPaletteDetails(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inspector", DecorationEditorTheme.SubHeader, GUILayout.Width(112f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                _selected == null || _selected.IsDeleted ? "No selection" : MeshName(_selected.MeshGuid.Us),
                DecorationEditorTheme.Mini,
                GUILayout.Width(132f));
            GUILayout.EndHorizontal();

            float contentHeight = Mathf.Max(120f, rect.height - 40f);
            DrawInspector(contentHeight);
            GUILayout.EndArea();
        }

        private void DrawMeshListRows(
            List<DecorationMeshCatalogEntry> rows,
            float viewportHeight,
            bool mouseInListViewport)
        {
            int total = rows.Count;
            int first = Mathf.Max(0, Mathf.FloorToInt(_meshScroll.y / MeshPaletteRowHeight) - 4);
            int visible = Mathf.CeilToInt(viewportHeight / MeshPaletteRowHeight) + 8;
            int last = Mathf.Min(total, first + visible);
            if (first > 0)
                GUILayout.Space(first * MeshPaletteRowHeight);
            for (int index = first; index < last; index++)
            {
                DecorationMeshCatalogEntry entry = rows[index];
                bool active = ReferenceEquals(entry, _selectedMesh) || ReferenceEquals(entry, _placingMesh);
                GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"[{entry.Kind}] {entry.Name}", style, GUILayout.Height(MeshPaletteRowHeight)))
                    StartMeshPlacement(entry);

                bool previous = GUI.enabled;
                GUI.enabled = previous && _selected != null && !_selected.IsDeleted;
                if (GUILayout.Button("Set", DecorationEditorTheme.Button, GUILayout.Width(44f), GUILayout.Height(MeshPaletteRowHeight)))
                    AssignMeshToSelected(entry);
                GUI.enabled = previous;
                GUILayout.EndHorizontal();

                Rect row = GUILayoutUtility.GetLastRect();
                if (mouseInListViewport && row.Contains(Event.current.mousePosition))
                    _hoveredMesh = entry;
            }
            if (last < total)
                GUILayout.Space((total - last) * MeshPaletteRowHeight);
        }

        private void DrawMeshPreviewGrid(
            List<DecorationMeshCatalogEntry> rows,
            float viewportHeight,
            bool mouseInListViewport)
        {
            int totalRows = Mathf.CeilToInt(rows.Count / (float)MeshPreviewGridColumns);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_meshScroll.y / MeshPreviewGridRowHeight));
            int lastRow = Mathf.Min(
                totalRows,
                Mathf.CeilToInt((_meshScroll.y + viewportHeight) / MeshPreviewGridRowHeight));
            bool canRenderMissingPreview = Event.current != null && Event.current.type == EventType.Repaint;
            int renderedPreviews = 0;
            if (firstRow > 0)
                GUILayout.Space(firstRow * MeshPreviewGridRowHeight);
            for (int gridRow = firstRow; gridRow < lastRow; gridRow++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < MeshPreviewGridColumns; column++)
                {
                    int rowIndex = gridRow * MeshPreviewGridColumns + column;
                    if (rowIndex >= rows.Count)
                    {
                        GUILayout.Space(MeshPreviewGridCardWidth);
                        continue;
                    }

                    DecorationMeshCatalogEntry entry = rows[rowIndex];
                    bool active = ReferenceEquals(entry, _selectedMesh) || ReferenceEquals(entry, _placingMesh);
                    GUIStyle style = active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row;
                    string label = CompactText(entry.Name, 18);
                    Rect card = GUILayoutUtility.GetRect(
                        MeshPreviewGridCardWidth,
                        MeshPreviewGridCardHeight,
                        GUILayout.Width(MeshPreviewGridCardWidth),
                        GUILayout.Height(MeshPreviewGridCardHeight));
                    if (GUI.Button(card, GUIContent.none, style))
                        StartMeshPlacement(entry);
                    bool hovered = mouseInListViewport && card.Contains(Event.current.mousePosition);
                    if (hovered)
                        _hoveredMesh = entry;

                    Texture preview = _previewRenderer?.GetCachedPreview(entry, MeshPreviewGridTexturePixels);
                    bool shouldRenderPreview =
                        preview == null &&
                        canRenderMissingPreview &&
                        (renderedPreviews < MaxMeshGridPreviewRendersPerFrame || hovered || active);
                    if (shouldRenderPreview)
                    {
                        preview = _previewRenderer?.GetPreview(entry, MeshPreviewGridTexturePixels, _previewSpin);
                        renderedPreviews++;
                    }

                    Rect previewRect = new Rect(card.x + 6f, card.y + 5f, card.width - 12f, 54f);
                    if (preview != null)
                        GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
                    else
                        GUI.Label(previewRect, "loading", DecorationEditorTheme.Mini);
                    GUI.Label(new Rect(card.x + 4f, card.yMax - 26f, card.width - 8f, 22f), label, DecorationEditorTheme.Mini);
                }
                GUILayout.EndHorizontal();
            }
            if (lastRow < totalRows)
                GUILayout.Space((totalRows - lastRow) * MeshPreviewGridRowHeight);
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Decoration Edit Mode", DecorationEditorTheme.SubHeader, GUILayout.Width(168f));
            GUILayout.Label("Mode: Deco | Tab to Build when clean", DecorationEditorTheme.Body);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                HasUnappliedChanges ? "Dirty preview" : "Clean",
                HasUnappliedChanges ? DecorationEditorTheme.Warning : DecorationEditorTheme.Mini,
                GUILayout.Width(92f));
            GUILayout.EndHorizontal();
            DecorationEditorTheme.Separator();
            DrawStatusStrip();
            DrawBottomTransformEditors();
            GUILayout.Label(
                "Tab switches ESU modes when clean | Ctrl+D/Esc closes and restores un-applied edits | Select rows or viewport centers | Move snaps 0.05m",
                DecorationEditorTheme.Mini);
        }

        private void DrawStatusStrip()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(StatusLine(), DecorationEditorTheme.Status);
            GUILayout.EndHorizontal();
        }

        private void DrawBottomTransformEditors()
        {
            Rect row = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
            const float gap = 8f;
            const float width = 386f;
            float totalWidth = width * 3f + gap * 2f;
            float x = row.xMax - totalWidth;
            if (x < row.x)
                x = row.x;

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
            GUI.Label(
                new Rect(rect.x, rect.y + 2f, 76f, 22f),
                label,
                hasSelection ? DecorationEditorTheme.SubHeader : DecorationEditorTheme.DisabledButton);

            bool previous = GUI.enabled;
            GUI.enabled = previous && hasSelection;
            const float setWidth = 36f;
            const float axisWidth = 10f;
            const float fieldWidth = 72f;
            const float gap = 5f;
            float x = rect.x + 84f;
            DrawBottomVectorComponent("X", values, 0, rect.y, ref x, axisWidth, fieldWidth, gap);
            DrawBottomVectorComponent("Y", values, 1, rect.y, ref x, axisWidth, fieldWidth, gap);
            DrawBottomVectorComponent("Z", values, 2, rect.y, ref x, axisWidth, fieldWidth, gap);
            if (GUI.Button(new Rect(x + gap, rect.y + 2f, setWidth, 22f), "Set", DecorationEditorTheme.Button))
            {
                if (TryParseVector(values, out Vector3 parsed))
                    apply(parsed);
                else
                    InfoStore.Add($"{label} contains incomplete, invalid, NaN, or infinity input.");
            }
            GUI.enabled = previous;
        }

        private static void DrawBottomVectorComponent(
            string axis,
            string[] values,
            int index,
            float y,
            ref float x,
            float axisWidth,
            float fieldWidth,
            float gap)
        {
            GUI.Label(new Rect(x, y + 6f, axisWidth, 16f), axis, DecorationEditorTheme.Mini);
            x += axisWidth + gap;
            values[index] = GUI.TextField(
                new Rect(x, y + 2f, fieldWidth, 22f),
                values[index] ?? string.Empty,
                DecorationEditorTheme.TextField);
            x += fieldWidth + gap;
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
            return $"View: {ViewModeDisplayName(_viewMode)} | Tool: {_tool}{placing} | Selected: {selected} | Dirty: {(_dirty ? "yes" : "no")} | Undo {_history.UndoCount}/Redo {_history.RedoCount} | Save: {format} | {counts}";
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

            Rect rect = new Rect(
                Mathf.Clamp(Event.current.mousePosition.x + 24f, 8f, Screen.width - 356f),
                Mathf.Clamp(Event.current.mousePosition.y + 24f, 8f, Screen.height - 170f),
                348f,
                160f);
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect previewRect = new Rect(rect.x + 10f, rect.y + 34f, 112f, 112f);
            Texture preview = _previewRenderer?.GetPreview(_hoveredMesh, 128, _previewSpin);
            if (preview != null)
                GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, alphaBlend: true);
            else
                GUI.Label(previewRect, "no preview", DecorationEditorTheme.Mini);

            GUI.BeginGroup(rect);
            GUI.Label(
                new Rect(8f, 6f, rect.width - 16f, 24f),
                new GUIContent(" Mesh preview", DecorationEditorIconCatalog.Get("mesh")),
                DecorationEditorTheme.SubHeader);
            GUI.Label(new Rect(132f, 38f, rect.width - 142f, 24f), $"[{_hoveredMesh.Kind}] {_hoveredMesh.Name}", DecorationEditorTheme.Body);
            GUI.Label(new Rect(132f, 64f, rect.width - 142f, 38f), _hoveredMesh.Guid.ToString("D"), DecorationEditorTheme.Mini);
            GUI.Label(new Rect(132f, 106f, rect.width - 142f, 42f), "Click: place on pointer. Set: assign to selected.", DecorationEditorTheme.Mini);
            GUI.EndGroup();
        }

        /*
                string prefix = ReferenceEquals(entry, _selectedMesh) ? "● " : string.Empty;
        */
        private void HandleSceneInput()
        {
            if (IsMouseOverEditorUi(_lastMouseGui))
                return;

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

                _selected.Positioning.Us = freeNext;
                _selected.Changed();
                _dirty = true;
                if (TryAutoFollowAnchor(out Vector3i freeShift))
                    _dragPositionStart -= ToVector3(freeShift);
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

            _selected.Positioning.Us = axisNext;
            _selected.Changed();
            _dirty = true;
            if (TryAutoFollowAnchor(out Vector3i axisShift))
                _dragPositionStart -= ToVector3(axisShift);
        }

        private void CommitDragEdit()
        {
            DecorationEditSnapshot before = _dragSnapshotStart;
            _dragSnapshotStart = null;
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            RecordSnapshotEdit("Move decoration", before);
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
            ApplyAnchorShift(shift, before);
        }

        private void RecordSnapshotEdit(string label, DecorationEditSnapshot before)
        {
            if (before == null || _selected == null || _selected.IsDeleted)
                return;

            if (before.Matches(_selected))
            {
                UpdateDirtyFromSelection();
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

        private void SelectNearest(Vector2 mouse)
        {
            RefreshDecorationCache(force: true);
            DecorationHit best = null;
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

            if (best == null)
            {
                InfoStore.Add("No decoration center near the cursor.");
                return;
            }

            Select(best.Decoration, best.Construct);
        }

        private void Select(Decoration decoration, AllConstruct construct)
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
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
            _anchorPreviewValid = false;
            _selection.Clear();
            _selection.Add(decoration);
            DecorationMeshCatalogEntry matching = _meshCatalog.FirstOrDefault(
                entry => entry.Guid == decoration.MeshGuid.Us);
            if (matching != null)
                _selectedMesh = matching;
            if (_tool != DecorationEditorTool.Anchor)
                _tool = DecorationEditorTool.Move;
            ResetInspectorFields();
            InfoStore.Add("Decoration selected.");
        }

        private void ApplySelection(bool notify = true)
        {
            if (_selected != null && !_selected.IsDeleted)
                _snapshot = new DecorationEditSnapshot(_selected);
            _transactions.Apply();
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
            CancelPlacement();
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
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
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

        private bool TryCreateDecorationAtCursor(
            DecorationMeshCatalogEntry mesh,
            out AllConstruct construct,
            out Decoration decoration)
        {
            construct = _placementConstruct;
            decoration = null;
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

            decoration = decorations.NewDecoration(
                _placementAnchor,
                force: true,
                playSound: true,
                forceEvenIfMaxReached: true);
            if (decoration == null)
            {
                InfoStore.Add("FTD rejected the new decoration at the pointed block.");
                return false;
            }

            decoration.MeshGuid.Us = mesh.Guid;
            decoration.Positioning.Us = localPosition;
            decoration.Scaling.Us = Vector3.one;
            decoration.Orientation.Us = Vector3.zero;
            decoration.Changed();
            return true;
        }

        private void FinishCreatedDecoration(
            AllConstruct construct,
            Decoration decoration,
            DecorationMeshCatalogEntry mesh,
            string message)
        {
            Select(decoration, construct);
            _transactions.MarkCreated(decoration);
            _selectedCreatedInSession = true;
            _dirty = true;
            _history.Record(new DecorationCreateCommand(
                construct,
                decoration,
                new DecorationEditSnapshot(decoration)));
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

        private void ApplyAnchorShift(Vector3i shift, DecorationEditSnapshot before)
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
                MarkSelectedDirty();
                ResetInspectorFields();
                RecordSnapshotEdit("Move anchor", before);
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

            if (!TryCreateDecorationAtCursor(mesh, out AllConstruct construct, out Decoration decoration))
                return;

            _placingMesh = null;
            HidePlacementGhost();
            FinishCreatedDecoration(
                construct,
                decoration,
                mesh,
                "Decoration placed on the pointed block.");
            _tool = DecorationEditorTool.Move;
        }

        internal bool HandleEscape()
        {
            if (_placingMesh == null)
                return false;

            CancelPlacement();
            return true;
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
            _rotateDragAxis = DecorationEditAxis.None;
            _rotateDragSnapshotStart = null;
            _scaleDragAxis = DecorationEditAxis.None;
            _scaleDragSnapshotStart = null;
            _anchorDragAxis = DecorationEditAxis.None;
            _anchorDragSnapshotStart = null;
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

            try
            {
                Get.UserInput.SetGameControlOptions(
                    gameShowMouseCursor: true,
                    gameDisableKeys: false);
                _focusApplied = true;
            }
            catch
            {
                _focusApplied = false;
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

            if (_focusApplied)
            {
                try
                {
                    Get.UserInput.SetGameControlOptions(
                        gameShowMouseCursor: false,
                        gameDisableKeys: false);
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(failure, exception);
                }
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
            _selected.Positioning.Us = value;
            TryAutoFollowAnchor(out _);
            SetVectorText(_positionText, _selected.Positioning.Us);
            MarkSelectedDirty();
            RecordSnapshotEdit("Set position", before);
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
