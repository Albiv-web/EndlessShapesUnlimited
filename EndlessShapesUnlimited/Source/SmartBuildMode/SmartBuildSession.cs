using System;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed class SmartBuildSession
    {
        private const float ToolbarHeight = 48f;
        private const float StatusHeight = 30f;
        private const float LeftPanelWidth = 392f;
        private const float LeftPanelMinHeight = 292f;
        private const float HandleLength = 1.8f;
        private const float AxisPickThresholdPixels = 24f;
        private const int MaxPreviewCellsWithFaces = 768;

        private static Rect s_leftPanelRect = new Rect(18f, 110f, LeftPanelWidth, 390f);

        private readonly cBuild _build;
        private readonly DecorationPointerProbe _pointerProbe;
        private readonly int _toolbarWindowId = "EndlessShapesUnlimited.SmartBuild.Toolbar".GetHashCode();
        private readonly int _panelWindowId = "EndlessShapesUnlimited.SmartBuild.Panel".GetHashCode();
        private readonly int _statusWindowId = "EndlessShapesUnlimited.SmartBuild.Status".GetHashCode();

        private Rect _toolbarRect;
        private Rect _leftPanelRect = s_leftPanelRect;
        private Rect _statusRect;
        private SmartBuildSource _source;
        private string _sourceReason;
        private SmartBuildDraft _draft;
        private SmartBuildPlan _plan;
        private SmartBuildTool _tool = SmartBuildTool.Draw;
        private SmartBuildDrawPlane _drawPlane = SmartBuildDrawPlane.Camera;
        private SmartBuildOccupancyMode _occupancyMode = SmartBuildOccupancyMode.SkipOccupied;
        private DecorationEditAxis _dragAxis = DecorationEditAxis.None;
        private SmartBuildTool _dragTool = SmartBuildTool.Draw;
        private Vector2 _dragMouseStart;
        private Vector3i _dragOriginStart;
        private Vector3i _dragSizeStart;
        private int _dragSign = 1;
        private bool _dragging;
        private bool _closeRequested;

        internal SmartBuildSession(cBuild build)
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
            if (_dragging)
            {
                reason = "Finish the Smart Builder handle drag before switching modes.";
                return false;
            }

            if (_draft != null)
            {
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
            RefreshSelection();
        }

        internal void End()
        {
            SmartBuildInputScope.End();
            DecorationEditorOverlay.Clear();
            Active = false;
            _draft = null;
            _plan = null;
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            SwitchToDecorationEditRequested = false;
        }

        internal void Update()
        {
            if (!Active)
                return;

            RefreshSelection();
            HandleKeyboard();
            HandleMouse();
            DrawWorldPreview();
        }

        internal void OnGUI()
        {
            if (!Active)
                return;

            DecorationEditorTheme.Ensure();
            if (Event.current.type == EventType.Repaint)
                DecorationEditorOverlay.Render();

            _toolbarRect = new Rect(8f, 8f, Screen.width - 16f, ToolbarHeight);
            _statusRect = new Rect(8f, Screen.height - StatusHeight - 8f, Screen.width - 16f, StatusHeight);
            _leftPanelRect.width = LeftPanelWidth;
            _leftPanelRect.height = Mathf.Clamp(
                _leftPanelRect.height,
                LeftPanelMinHeight,
                Mathf.Max(LeftPanelMinHeight, Screen.height - ToolbarHeight - StatusHeight - 34f));
            _leftPanelRect.x = Mathf.Clamp(_leftPanelRect.x, 8f, Screen.width - LeftPanelWidth - 8f);
            _leftPanelRect.y = Mathf.Clamp(
                _leftPanelRect.y,
                ToolbarHeight + 14f,
                Screen.height - StatusHeight - _leftPanelRect.height - 12f);

            GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
            _leftPanelRect = GUI.Window(_panelWindowId, _leftPanelRect, DrawLeftPanel, GUIContent.none, GUIStyle.none);
            GUI.Window(_statusWindowId, _statusRect, DrawStatusStrip, GUIContent.none, GUIStyle.none);

            s_leftPanelRect = _leftPanelRect;
            bool overUi = ContainsMouse(_toolbarRect) || ContainsMouse(_leftPanelRect) || ContainsMouse(_statusRect);
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

        internal void DrawWorldPreview()
        {
            DecorationEditorOverlay.BeginFrame();
            if (!Active)
                return;

            try
            {
                if (_draft == null)
                    DrawDrawPlaneCursor();
                else
                {
                    DrawDraftCells();
                    DrawDraftOutline();
                    DrawDraftHandles();
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Smart Block Builder preview draw failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_dragging)
                {
                    EndDrag(resetDraft: true);
                    return;
                }

                if (_draft != null)
                {
                    CancelPreview();
                    return;
                }
            }

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                _draft != null)
            {
                ApplyPreview();
            }
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

            if (Input.GetMouseButtonDown(1))
            {
                if (_dragging)
                    EndDrag(resetDraft: true);
                else if (_draft != null)
                    CancelPreview();
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
                InfoStore.Add(_sourceReason ?? "Select a supported block before using Smart Block Builder.");
                return;
            }

            if (TrySeedFromPointedBlock(out AllConstruct hitConstruct, out Vector3i hitCell))
            {
                CreatePreviewAtSeed(hitConstruct, hitCell);
                return;
            }

            if (TrySeedFromDrawPlane(out AllConstruct construct, out Vector3i cell))
            {
                CreatePreviewAtSeed(construct, cell);
                return;
            }

            InfoStore.Add("Point at the focused construct grid to create a Smart Builder preview.");
        }

        private void CreatePreviewAtSeed(AllConstruct construct, Vector3i cell)
        {
            if (!CanPlaceDraftOrigin(construct, cell, out string reason))
            {
                InfoStore.Add(reason);
                return;
            }

            _draft = SmartBuildDraft.CreateSeed(construct, cell, _drawPlane);
            _tool = SmartBuildTool.Scale;
            RebuildPlan();
            InfoStore.Add("Smart Builder origin placed. Scale the preview, then Apply.");
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

        private bool TrySeedFromPointedBlock(out AllConstruct construct, out Vector3i cell)
        {
            construct = null;
            cell = default;
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit))
                return false;

            Vector3 localNormal = WorldNormalToLocal(hit);
            SmartBuildAxis axis = SmartBuildAxisHelper.FromLargestComponent(localNormal, out int sign);
            construct = hit.Construct;
            cell = hit.Anchor + SmartBuildAxisHelper.ToVector3i(axis, sign);
            return construct != null;
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

            if (!TryPickHandle(out DecorationEditAxis axis, out int sign))
                return false;

            _dragging = true;
            _dragTool = _tool;
            _dragAxis = axis;
            _dragSign = sign;
            _dragMouseStart = MouseGuiPosition();
            _dragOriginStart = _draft.Origin;
            _dragSizeStart = _draft.Size;
            return true;
        }

        private void UpdateHandleDrag()
        {
            if (_draft == null || _dragAxis == DecorationEditAxis.None)
                return;

            int cells = ProjectDragDeltaToCells(_dragAxis);
            _draft.SetTransform(_dragOriginStart, _dragSizeStart);
            if (_dragTool == SmartBuildTool.Move)
            {
                _draft.MoveBy(
                    SmartBuildAxisHelper.ToVector3i(
                        SmartBuildDraft.ToSmartAxis(_dragAxis),
                        cells));
            }
            else if (_dragTool == SmartBuildTool.Scale)
            {
                _draft.ResizeFromHandle(_dragAxis, _dragSign, cells);
            }

            RebuildPlan();
        }

        private void EndDrag(bool resetDraft)
        {
            if (resetDraft && _draft != null)
            {
                _draft.SetTransform(_dragOriginStart, _dragSizeStart);
                RebuildPlan();
            }

            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
        }

        private int ProjectDragDeltaToCells(DecorationEditAxis axis)
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
            return Mathf.RoundToInt(projected);
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

        private void RebuildPlan()
        {
            if (_draft == null || _source == null)
            {
                _plan = null;
                return;
            }

            SmartBuildVolume volume = _draft.ToVolume();
            _plan = SmartBuildPlanner.BuildPlan(
                volume,
                _source.Family,
                IsOccupied,
                new SmartBuildPlannerOptions
                {
                    SkipOccupiedCells = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied
                });
            if (_plan.CanCommit && !PlanTouchesExistingConstruct(_plan))
            {
                _plan = SmartBuildPlan.Failed(
                    volume,
                    "The Smart Builder preview must touch an existing block before Apply.",
                    _plan.SkippedCells,
                    _plan.Warnings);
            }
        }

        private bool IsOccupied(Vector3i cell)
        {
            return IsCellOccupied(_draft?.Construct, cell);
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
            if (plan?.Volume?.Construct == null)
                return false;

            foreach (SmartBuildPlacement placement in plan.Placements)
            {
                foreach (Vector3i cell in placement.CoveredCells())
                {
                    foreach (Vector3i neighbor in NeighborCells(cell))
                    {
                        if (IsCellOccupied(plan.Volume.Construct, neighbor))
                            return true;
                    }
                }
            }

            return false;
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

        private void CancelPreview()
        {
            _draft = null;
            _plan = null;
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
        }

        private void RefreshSelection()
        {
            if (SmartBuildSelectionResolver.TryResolve(
                    _build,
                    out SmartBuildSource source,
                    out string reason))
            {
                _source = source;
                _sourceReason = null;
                if (_draft != null)
                    RebuildPlan();
                return;
            }

            _source = null;
            _sourceReason = reason;
            _plan = _draft == null
                ? null
                : SmartBuildPlan.Failed(_draft.ToVolume(), reason);
        }

        private void DrawToolbar(int id)
        {
            GUILayout.BeginArea(new Rect(0f, 0f, _toolbarRect.width, _toolbarRect.height), DecorationEditorTheme.Panel);
            GUILayout.BeginHorizontal();
            if (IconButton("build", "Build", DecorationEditorTheme.ToolButton(true), "Smart Block Builder."))
                _tool = SmartBuildTool.Draw;
            ToolButton(SmartBuildTool.Draw, "create", "Draw", "Click the focused construct grid to create a 1x1x1 preview.");
            ToolButton(SmartBuildTool.Move, "move", "Move", "Drag RGB handles to move the preview by whole cells.");
            ToolButton(SmartBuildTool.Scale, "scale", "Scale", "Drag RGB handles to resize the preview by whole cells.");
            PlaneButton();
            OccupancyButton();
            GUILayout.FlexibleSpace();
            if (IconButton("save", "Apply", DecorationEditorTheme.Button, "Place the planned blocks.", _plan != null && _plan.CanCommit))
                ApplyPreview();
            if (IconButton("cancel", "Cancel", DecorationEditorTheme.Button, "Clear the runtime preview.", _draft != null))
                CancelPreview();
            if (IconButton("open", "Deco", DecorationEditorTheme.Button, "Switch to Decoration Edit Mode."))
                SwitchToDecorationEditRequested = true;
            if (IconButton("delete", "Close", DecorationEditorTheme.Button, "Close Smart Block Builder."))
                _closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLeftPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            GUILayout.BeginArea(new Rect(8f, 8f, _leftPanelRect.width - 16f, _leftPanelRect.height - 16f));
            GUILayout.Label(new GUIContent(" Smart Block Builder", DecorationEditorIconCatalog.Get("build")), DecorationEditorTheme.Header);
            DecorationEditorTheme.Separator();
            LabelRow("Selected", _source?.DisplayName ?? (_sourceReason ?? "No supported selected block"));
            LabelRow("Tool", _tool.ToString());
            LabelRow("Plane", _drawPlane.ToString());
            LabelRow("Occupancy", _occupancyMode == SmartBuildOccupancyMode.SkipOccupied ? "Skip occupied" : "Block on overlap");
            DecorationEditorTheme.Separator();
            if (_draft == null)
            {
                GUILayout.Label("No preview", DecorationEditorTheme.SubHeader);
                GUILayout.Label("Click in the focused construct grid to create a runtime-only 1x1x1 preview.", DecorationEditorTheme.MiniWrap);
            }
            else
            {
                GUILayout.Label("Preview", DecorationEditorTheme.SubHeader);
                LabelRow("Origin", FormatCell(_draft.Origin));
                LabelRow("Size", _draft.FormatDimensions());
                LabelRow("Cells", _draft.ToVolume().CellCount.ToString("N0", CultureInfo.InvariantCulture));
                DrawPlanSummary();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Middle mouse can show the FTD cursor without closing Smart Builder. Camera/WASD remain live unless a Smart Builder handle is being dragged.", DecorationEditorTheme.MiniWrap);
            GUI.DragWindow();
            GUILayout.EndArea();
        }

        private void DrawStatusStrip(int id)
        {
            GUILayout.BeginArea(new Rect(0f, 0f, _statusRect.width, _statusRect.height), DecorationEditorTheme.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                _draft == null
                    ? "Smart Builder | runtime preview: none"
                    : $"Smart Builder | {_draft.FormatDimensions()} | {_plan?.EstimatedBlockCount ?? 0:N0} placements | {_plan?.CoveredCellCount ?? 0:N0} cells",
                DecorationEditorTheme.Status);
            GUILayout.FlexibleSpace();
            if (_plan != null && !_plan.CanCommit)
                GUILayout.Label(_plan.FailureReason ?? "Plan blocked", DecorationEditorTheme.Error);
            else if (_plan != null && _plan.SkippedCells.Count > 0)
                GUILayout.Label($"Skipped {_plan.SkippedCells.Count:N0} occupied", DecorationEditorTheme.Warning);
            else
                GUILayout.Label("Ready", DecorationEditorTheme.Mini);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ToolButton(SmartBuildTool tool, string icon, string label, string tooltip)
        {
            if (IconButton(icon, label, DecorationEditorTheme.ToolButton(_tool == tool), tooltip))
                _tool = tool;
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
                GUILayout.Width(62f),
                GUILayout.Height(40f));
            GUI.enabled = previous;
            return clicked && enabled;
        }

        private void DrawPlanSummary()
        {
            if (_plan == null)
            {
                LabelRow("Plan", "--");
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

        private static void LabelRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, DecorationEditorTheme.Mini, GUILayout.Width(72f));
            GUILayout.Label(value ?? string.Empty, DecorationEditorTheme.BodyWrap);
            GUILayout.EndHorizontal();
        }

        private void DrawDraftCells()
        {
            if (_draft?.Construct == null)
                return;

            SmartBuildVolume volume = _draft.ToVolume();
            int drawn = 0;
            foreach (Vector3i cell in volume.EnumerateCells())
            {
                bool occupied = IsOccupied(cell);
                Color face = occupied
                    ? new Color(1f, 0.55f, 0.12f, 0.16f)
                    : new Color(0.05f, 0.9f, 1f, 0.13f);
                Color edge = occupied
                    ? new Color(1f, 0.55f, 0.12f, 0.72f)
                    : new Color(0.1f, 0.95f, 1f, 0.62f);
                DrawCell(cell, face, edge, 1.1f);
                drawn++;
                if (drawn >= MaxPreviewCellsWithFaces)
                    break;
            }
        }

        private void DrawDraftOutline()
        {
            if (_draft?.Construct == null)
                return;

            Vector3[] corners = _draft.ToVolume().GetWorldCorners();
            Color color = _plan != null && !_plan.CanCommit
                ? new Color(1f, 0.2f, 0.15f, 1f)
                : new Color(0.1f, 0.95f, 1f, 1f);
            DrawWireEdges(corners, color, 3.2f);
        }

        private void DrawDraftHandles()
        {
            if (_draft?.Construct == null)
                return;

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

        private void DrawDrawPlaneCursor()
        {
            if (!TrySeedFromDrawPlane(out AllConstruct construct, out Vector3i cell) || construct == null)
                return;

            Vector3 center = new Vector3(cell.x, cell.y, cell.z);
            Vector3 world = construct.SafeLocalToGlobal(center);
            DecorationEditorOverlay.Cross(world, 0.36f, new Color(0.1f, 0.95f, 1f, 0.92f), 2.2f);
        }

        private void DrawCell(Vector3i cell, Color face, Color edge, float edgeWidth)
        {
            Vector3 center = new Vector3(cell.x, cell.y, cell.z);
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
                corners[index] = _draft.Construct.SafeLocalToGlobal(center + corners[index]);

            DecorationEditorOverlay.Quad(corners[0], corners[1], corners[2], corners[3], face);
            DecorationEditorOverlay.Quad(corners[4], corners[7], corners[6], corners[5], face);
            DecorationEditorOverlay.Quad(corners[0], corners[4], corners[5], corners[1], face);
            DecorationEditorOverlay.Quad(corners[1], corners[5], corners[6], corners[2], face);
            DecorationEditorOverlay.Quad(corners[2], corners[6], corners[7], corners[3], face);
            DecorationEditorOverlay.Quad(corners[3], corners[7], corners[4], corners[0], face);
            DrawWireEdges(corners, edge, edgeWidth);
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

        private static Vector2 ScreenToGui(Vector3 screen) =>
            new Vector2(screen.x, Screen.height - screen.y);

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
    }
}
