using System;
using System.Collections.Generic;
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
        private const float ToolbarHeight = 54f;
        private const float StatusHeight = 30f;
        private const float LeftPanelWidth = 392f;
        private const float LeftPanelMinHeight = 292f;
        private const float HandleLength = 1.8f;
        private const float AxisPickThresholdPixels = 24f;

        private static Rect s_leftPanelRect = new Rect(18f, 110f, LeftPanelWidth, 390f);
        private static int s_layoutGeneration = -1;
        private static SmartBuildMaterial s_selectedMaterial = SmartBuildMaterial.Wood;

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
        private DecorationEditAxis _dragAxis = DecorationEditAxis.None;
        private SmartBuildTool _dragTool = SmartBuildTool.Draw;
        private Vector2 _dragMouseStart;
        private Vector3i _dragOriginStart;
        private Vector3i _dragSizeStart;
        private int _dragSign = 1;
        private bool _dragging;
        private bool _planDirty;
        private bool _dragStartedFromFace;
        private DecorationEditAxis _hoverFaceAxis = DecorationEditAxis.None;
        private int _hoverFaceSign = 1;
        private bool _closeRequested;
        private bool _resizingLeftPanel;
        private Rect _leftPanelResizeStart;
        private Vector2 _leftPanelResizeMouseStart;
        private int _layoutResetGeneration = s_layoutGeneration;
        private float _applyCancelAttentionUntil = -1f;

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
            RefreshSelection();
        }

        internal void End()
        {
            SmartBuildInputScope.End();
            DecorationEditorOverlay.Clear();
            Active = false;
            _draft = null;
            _plan = null;
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _planDirty = false;
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
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
            _leftPanelRect = EsuHudLayout.ClampPanel(
                _leftPanelRect,
                MinLeftPanelWidth(),
                MinLeftPanelHeight(),
                MaxLeftPanelWidth(),
                MaxLeftPanelHeight(),
                LeftPanelTopLimit(),
                StatusHeightScaled() + EsuHudLayout.Scale(12f));
            HandleLeftPanelResize();

            GUI.Window(_toolbarWindowId, _toolbarRect, DrawToolbar, GUIContent.none, GUIStyle.none);
            EsuHudNotifications.DrawExpandedPopup();
            _leftPanelRect = GUI.Window(_panelWindowId, _leftPanelRect, DrawLeftPanel, GUIContent.none, GUIStyle.none);
            GUI.Window(_statusWindowId, _statusRect, DrawStatusStrip, GUIContent.none, GUIStyle.none);
            EsuHudLayout.DrawResizeGrip(_leftPanelRect, leftEdge: false);

            s_leftPanelRect = _leftPanelRect;
            s_layoutGeneration = _layoutResetGeneration;
            bool overUi = ContainsMouse(_toolbarRect) ||
                          EsuHudNotifications.ContainsMouse(Event.current.mousePosition) ||
                          ContainsMouse(_leftPanelRect) ||
                          ContainsMouse(_statusRect);
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

        private float ToolbarHeightScaled() =>
            EsuHudNotifications.ToolbarHeightScaled(ToolbarHeight, Screen.width - EsuHudLayout.Scale(16f));

        private float StatusHeightScaled() => EsuHudLayout.Scale(StatusHeight);

        private float LeftPanelTopLimit() => ToolbarHeightScaled() + EsuHudLayout.Scale(14f);

        private float MinLeftPanelWidth() => EsuHudLayout.Scale(300f);

        private float MinLeftPanelHeight() => EsuHudLayout.Scale(LeftPanelMinHeight);

        private float MaxLeftPanelWidth() => Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.62f);

        private float MaxLeftPanelHeight() =>
            Mathf.Max(MinLeftPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f));

        private Rect DefaultLeftPanelRect()
        {
            float width = Mathf.Min(
                EsuHudLayout.Scale(LeftPanelWidth),
                Mathf.Max(MinLeftPanelWidth(), Screen.width * 0.28f));
            float height = Mathf.Min(
                EsuHudLayout.Scale(390f),
                Mathf.Max(MinLeftPanelHeight(), Screen.height - LeftPanelTopLimit() - StatusHeightScaled() - EsuHudLayout.Scale(16f)));
            return new Rect(EsuHudLayout.Scale(18f), LeftPanelTopLimit(), width, height);
        }

        private void ApplyLayoutResetIfNeeded()
        {
            if (_layoutResetGeneration == EsuHudLayout.ResetGeneration)
                return;

            _leftPanelRect = DefaultLeftPanelRect();
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
                    DrawDraftOutline();
                    DrawDraftFaceHighlight();
                    DrawDraftHandles();
                }

                DrawSymmetryOverlay();
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
                if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
                {
                    DecoLimitLifter.EsuSymmetry.CancelPending();
                    InfoStore.Add("Symmetry plane placement cancelled.");
                    return;
                }

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
                InfoStore.Add(_sourceReason ?? "Selected Smart Builder material is unavailable.");
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

            if (TryPickHandle(out DecorationEditAxis axis, out int sign))
                return BeginDrag(axis, sign, fromFace: false);

            if (_tool == SmartBuildTool.Scale &&
                TryPickFace(out axis, out sign))
            {
                return BeginDrag(axis, sign, fromFace: true);
            }

            return false;
        }

        private bool BeginDrag(DecorationEditAxis axis, int sign, bool fromFace)
        {
            _dragging = true;
            _dragTool = _tool;
            _dragAxis = axis;
            _dragSign = sign;
            _dragStartedFromFace = fromFace;
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

            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private void EndDrag(bool resetDraft)
        {
            if (resetDraft && _draft != null)
            {
                _draft.SetTransform(_dragOriginStart, _dragSizeStart);
            }

            if (_draft != null)
                RebuildPlan();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _dragStartedFromFace = false;
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
            if (_draft == null)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
                return;
            }

            SmartBuildVolume volume = _draft.ToVolume();
            if (_source == null)
            {
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = BuildPreviewVolumesOnly(volume);
                _plan = SmartBuildPlan.Failed(
                    volume,
                    _sourceReason ?? "Selected Smart Builder material is unavailable.");
                _planDirty = false;
                return;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_draft.Construct, out string reason))
            {
                _previewCells = volume.EnumerateCells().ToArray();
                _previewCellSets = new[] { _previewCells };
                _previewVolumes = new[] { volume };
                _plan = SmartBuildPlan.Failed(volume, reason);
                _planDirty = false;
                return;
            }

            BuildPreviewSymmetrySets(volume);
            _plan = SmartBuildPlanner.BuildPlanFromCells(
                volume,
                _previewCells,
                volume.GrainAxis,
                _source.Family,
                IsOccupied,
                new SmartBuildPlannerOptions
                {
                    SkipOccupiedCells = _occupancyMode == SmartBuildOccupancyMode.SkipOccupied
                });
            if (_plan.CanCommit && !EveryPreviewSetTouchesExistingConstruct())
            {
                _plan = SmartBuildPlan.Failed(
                    volume,
                    "Every symmetry preview must touch an existing block before Apply.",
                    _plan.SkippedCells,
                    _plan.Warnings);
            }

            _planDirty = false;
        }

        private void BuildPreviewSymmetrySets(SmartBuildVolume volume)
        {
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
            if (_draft == null)
            {
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                return;
            }

            SmartBuildVolume volume = _draft.ToVolume();
            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(_draft.Construct, out _))
            {
                _previewVolumes = new[] { volume };
                return;
            }

            _previewVolumes = BuildPreviewVolumesOnly(volume);
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

        private bool EveryPreviewSetTouchesExistingConstruct()
        {
            if (_draft?.Construct == null || _previewCellSets == null || _previewCellSets.Count == 0)
                return false;

            foreach (IReadOnlyList<Vector3i> rawSet in _previewCellSets)
            {
                var target = rawSet?
                    .Where(cell => !IsCellOccupied(_draft.Construct, cell))
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
                            IsCellOccupied(_draft.Construct, neighbor))
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

        private void CancelPreview()
        {
            ClearApplyCancelAttention();
            _draft = null;
            _plan = null;
            _previewCells = Array.Empty<Vector3i>();
            _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
            _previewVolumes = Array.Empty<SmartBuildVolume>();
            _dragging = false;
            _dragAxis = DecorationEditAxis.None;
            _planDirty = false;
            _dragStartedFromFace = false;
            _hoverFaceAxis = DecorationEditAxis.None;
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
            if (_draft == null)
            {
                _plan = null;
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = Array.Empty<SmartBuildVolume>();
                _planDirty = false;
            }
            else
            {
                SmartBuildVolume volume = _draft.ToVolume();
                _previewCells = Array.Empty<Vector3i>();
                _previewCellSets = Array.Empty<IReadOnlyList<Vector3i>>();
                _previewVolumes = BuildPreviewVolumesOnly(volume);
                _plan = SmartBuildPlan.Failed(volume, reason);
                _planDirty = false;
            }
        }

        private void DrawToolbar(int id)
        {
            GUI.Box(new Rect(0f, 0f, _toolbarRect.width, _toolbarRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.LocalPanelInnerRect(_toolbarRect.width, _toolbarRect.height);
            float leftRailWidth = EsuHudLayout.ToolbarLeftRailWidth(inner.width);
            float notificationWidth = EsuHudLayout.ToolbarNotificationWidth(inner.width);
            float rightRailWidth = EsuHudLayout.ToolbarRightControlsWidth(inner.width);
            float gap = EsuHudLayout.ToolbarGap;

            GUILayout.BeginArea(inner);
            GUILayout.BeginHorizontal();
            GUILayout.BeginHorizontal(GUILayout.Width(leftRailWidth));
            {
                ModeSwitchButton();
                ToolButton(SmartBuildTool.Draw, "create", "Draw", "Click the focused construct grid to create a 1x1x1 preview.");
                ToolButton(SmartBuildTool.Move, "move", "Move", "Drag RGB handles to move the preview by whole cells.");
                ToolButton(SmartBuildTool.Scale, "scale", "Scale", "Drag RGB handles to resize the preview by whole cells.");
                PlaneButton();
                OccupancyButton();
                MaterialButton();
                SymmetryButton(DecorationEditAxis.X);
                SymmetryButton(DecorationEditAxis.Y);
                SymmetryButton(DecorationEditAxis.Z);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(gap);
            EsuHudNotifications.DrawToolbarSlot(inner, notificationWidth, "ESU mode: Smart Builder.");
            GUILayout.Space(gap);
            GUILayout.BeginHorizontal(GUILayout.Width(rightRailWidth));
            GUILayout.FlexibleSpace();
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

        private void ModeSwitchButton()
        {
            if (IconButton(
                    "build",
                    "Build",
                    DecorationEditorTheme.ToolButton(true),
                    "Tab: switch to Decoration Edit Mode when Smart Builder is clean."))
                SwitchToDecorationEditRequested = true;
        }

        private void DrawLeftPanel(int id)
        {
            GUI.Box(new Rect(0f, 0f, _leftPanelRect.width, _leftPanelRect.height), GUIContent.none, DecorationEditorTheme.Panel);
            float inset = EsuHudLayout.Scale(8f);
            GUILayout.BeginArea(new Rect(inset, inset, _leftPanelRect.width - inset * 2f, _leftPanelRect.height - inset * 2f));
            DrawCompactIconHeader("Smart Block Builder", "build");
            DecorationEditorTheme.Separator();
            DrawMaterialSelector();
            if (!string.IsNullOrWhiteSpace(_sourceReason))
                LabelRow("Blocks", _sourceReason);
            LabelRow("Tool", _tool.ToString());
            LabelRow("Plane", _drawPlane.ToString());
            LabelRow("Occupancy", _occupancyMode == SmartBuildOccupancyMode.SkipOccupied ? "Skip occupied" : "Block on overlap");
            LabelRow("Symmetry", DecoLimitLifter.EsuSymmetry.FormatSummary());
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
                LabelRow("Cells", PreviewCellCount()
                    .ToString("N0", CultureInfo.InvariantCulture));
                DrawPlanSummary();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Middle mouse can show the FTD cursor without closing Smart Builder. Camera/WASD remain live unless a Smart Builder handle is being dragged.", DecorationEditorTheme.MiniWrap);
            GUI.DragWindow();
            GUILayout.EndArea();
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
            GUILayout.BeginArea(inner);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                _draft == null
                    ? "Smart Builder | runtime preview: none"
                    : StatusSummary(),
                DecorationEditorTheme.Status);
            GUILayout.FlexibleSpace();
            if (_planDirty)
                GUILayout.Label("Preview changed", DecorationEditorTheme.Warning);
            else if (_plan != null && !_plan.CanCommit)
                GUILayout.Label(_plan.FailureReason ?? "Plan blocked", DecorationEditorTheme.Error);
            else if (_plan != null && _plan.SkippedCells.Count > 0)
                GUILayout.Label($"Skipped {_plan.SkippedCells.Count:N0} occupied", DecorationEditorTheme.Warning);
            else
                GUILayout.Label("Ready", DecorationEditorTheme.Mini);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private string StatusSummary()
        {
            if (_draft == null)
                return "Smart Builder | runtime preview: none";

            if (_planDirty)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Smart Builder | {0} | preview | {1:N0} cells",
                    _draft.FormatDimensions(),
                    PreviewCellCount());
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Smart Builder | {0} | {1:N0} placements | {2:N0} cells",
                _draft.FormatDimensions(),
                _plan?.EstimatedBlockCount ?? 0,
                _plan?.CoveredCellCount ?? 0);
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

        private void MaterialButton()
        {
            if (IconButton(
                    "material",
                    MaterialShortName(_selectedMaterial),
                    DecorationEditorTheme.Button,
                    "Cycle Smart Builder material."))
            {
                CycleSelectedMaterial(1);
            }
        }

        private void DrawMaterialSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Material", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(72f)));
            if (GUILayout.Button(
                    "<",
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
                    ">",
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

        private static string MaterialShortName(SmartBuildMaterial material)
        {
            switch (material)
            {
                case SmartBuildMaterial.Stone:
                    return "Stone";
                case SmartBuildMaterial.Metal:
                    return "Metal";
                case SmartBuildMaterial.Alloy:
                    return "Alloy";
                case SmartBuildMaterial.Glass:
                    return "Glass";
                case SmartBuildMaterial.Lead:
                    return "Lead";
                case SmartBuildMaterial.HeavyArmour:
                    return "Heavy";
                case SmartBuildMaterial.Rubber:
                    return "Rubber";
                default:
                    return "Wood";
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
                RebuildPlan();
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
            if (_draft == null)
                return 0;
            if (_planDirty || _previewCells.Count == 0)
                return _draft.ToVolume().CellCount;
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
            if (_draft?.Construct == null)
                return;

            Color color = !_planDirty && _plan != null && !_plan.CanCommit
                ? new Color(1f, 0.2f, 0.15f, 1f)
                : new Color(0.1f, 0.95f, 1f, 1f);
            IReadOnlyList<SmartBuildVolume> volumes = _previewVolumes.Count > 0
                ? _previewVolumes
                : (IReadOnlyList<SmartBuildVolume>)new[] { _draft.ToVolume() };
            foreach (SmartBuildVolume volume in volumes)
                DrawWireEdges(volume.GetWorldCorners(), color, 3.2f);
        }

        private void DrawDraftFaceHighlight()
        {
            if (_draft?.Construct == null || _tool != SmartBuildTool.Scale)
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

            if (!TryGetFaceWorldCorners(axis, sign, out Vector3[] face))
                return;

            Color axisColor = DecorationEditMath.AxisColor(axis);
            Color faceColor = new Color(axisColor.r, axisColor.g, axisColor.b, active ? 0.20f : 0.12f);
            Color edgeColor = new Color(axisColor.r, axisColor.g, axisColor.b, active ? 1f : 0.86f);
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

        private bool TryGetFaceWorldCorners(
            DecorationEditAxis axis,
            int sign,
            out Vector3[] face)
        {
            face = null;
            if (_draft?.Construct == null)
                return false;

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
