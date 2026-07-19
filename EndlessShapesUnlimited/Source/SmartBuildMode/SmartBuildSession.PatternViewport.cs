using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildSession
    {
        private const float PatternViewportPickThresholdPixels = 22f;
        private const float PatternViewportHandleRadius = 0.2f;

        private SmartBuildPatternViewportGesture _patternViewportGesture;
        private SmartBuildSceneSnapshot _patternViewportSceneStart;
        private SmartBuildPatternViewportHandle _patternViewportDragHandle;
        private bool _patternViewportDragging;
        private Vector2 _patternViewportDragMouseStart;
        private Vector3i _patternViewportDragCellStart;
        private float _patternViewportRadialAngleStart;
        private float _patternViewportRadialMouseAngleStart;
        private string _patternViewportLastIssue;

        private void DrawEditablePatternViewportHandles()
        {
            if (!TryGetSelectedViewportPattern(out SmartBuildPatternNode pattern) ||
                pattern.HostPiece.Construct == null)
            {
                return;
            }

            SmartBuildPatternDefinition definition = pattern.Definition;
            Vector3 anchor = CellVector(pattern.HostPiece.Origin);
            IReadOnlyList<PatternViewportHandleVisual> visuals =
                BuildPatternViewportHandleVisuals(pattern, definition);
            SmartBuildPatternViewportHandle hovered = default;
            bool hasHover = !SmartBuildInputScope.MouseOverUi &&
                            TryPickEditablePatternViewportHandle(
                                pattern,
                                visuals,
                                out hovered,
                                out _);

            switch (definition.Kind)
            {
                case SmartBuildEditablePatternKind.Linear:
                case SmartBuildEditablePatternKind.Grid:
                    DrawPatternStepArrow(
                        pattern,
                        anchor,
                        anchor + CellVector(definition.PrimaryStep),
                        PatternViewportColor(
                            new SmartBuildPatternViewportHandle(
                                SmartBuildPatternViewportHandleKind.PrimaryStep),
                            hasHover,
                            hovered));
                    if (definition.Kind == SmartBuildEditablePatternKind.Grid)
                    {
                        DrawPatternStepArrow(
                            pattern,
                            anchor,
                            anchor + CellVector(definition.SecondaryStep),
                            PatternViewportColor(
                                new SmartBuildPatternViewportHandle(
                                    SmartBuildPatternViewportHandleKind.SecondaryStep),
                                hasHover,
                                hovered));
                    }
                    break;

                case SmartBuildEditablePatternKind.Radial:
                    DrawRadialPatternGuide(pattern, definition, hasHover, hovered);
                    break;

                case SmartBuildEditablePatternKind.Polyline:
                    DrawPolylinePatternGuide(pattern, visuals);
                    break;
            }

            foreach (PatternViewportHandleVisual visual in visuals)
            {
                Vector3 world = pattern.HostPiece.Construct.SafeLocalToGlobal(
                    visual.LocalPosition);
                bool active = _patternViewportDragging &&
                              visual.Handle == _patternViewportDragHandle;
                bool hot = hasHover && visual.Handle == hovered;
                Color color = PatternViewportColor(
                    visual.Handle,
                    hasHover,
                    hovered,
                    visual.Editable);
                DecorationEditorOverlay.Cross(
                    world,
                    active || hot
                        ? PatternViewportHandleRadius * 1.35f
                        : PatternViewportHandleRadius,
                    color,
                    active || hot ? 5f : 3.4f);
            }
        }

        private void DrawPatternStepArrow(
            SmartBuildPatternNode pattern,
            Vector3 localStart,
            Vector3 localEnd,
            Color color)
        {
            Vector3 start = pattern.HostPiece.Construct.SafeLocalToGlobal(localStart);
            Vector3 end = pattern.HostPiece.Construct.SafeLocalToGlobal(localEnd);
            DecorationEditorOverlay.Arrow(start, end, color, 3.2f, 0.22f);
        }

        private void DrawRadialPatternGuide(
            SmartBuildPatternNode pattern,
            SmartBuildPatternDefinition definition,
            bool hasHover,
            SmartBuildPatternViewportHandle hovered)
        {
            Vector3 pivot = CellVector(definition.RadialPivot);
            Vector3 angle = RadialAngleHandleLocal(pattern, definition);
            Vector3 pivotWorld = pattern.HostPiece.Construct.SafeLocalToGlobal(pivot);
            Vector3 angleWorld = pattern.HostPiece.Construct.SafeLocalToGlobal(angle);
            Vector3 axis = RadialAxisVector(definition.RadialAxis);
            Vector3 axisWorld =
                pattern.HostPiece.Construct.SafeLocalToGlobal(pivot + axis) -
                pivotWorld;
            float radius = Vector3.Distance(pivotWorld, angleWorld);
            Color guide = new Color(1f, 0.58f, 0.12f, 0.78f);
            DecorationEditorOverlay.Circle(
                pivotWorld,
                Mathf.Max(0.35f, radius),
                guide,
                axisWorld,
                2.2f,
                48);
            DecorationEditorOverlay.Arrow(
                pivotWorld,
                angleWorld,
                PatternViewportColor(
                    new SmartBuildPatternViewportHandle(
                        SmartBuildPatternViewportHandleKind.RadialAngle),
                    hasHover,
                    hovered),
                3.2f,
                0.22f);
        }

        private void DrawPolylinePatternGuide(
            SmartBuildPatternNode pattern,
            IReadOnlyList<PatternViewportHandleVisual> visuals)
        {
            for (int index = 1; index < visuals.Count; index++)
            {
                Vector3 start = pattern.HostPiece.Construct.SafeLocalToGlobal(
                    visuals[index - 1].LocalPosition);
                Vector3 end = pattern.HostPiece.Construct.SafeLocalToGlobal(
                    visuals[index].LocalPosition);
                DecorationEditorOverlay.Line(
                    start,
                    end,
                    new Color(0.25f, 1f, 0.55f, 0.82f),
                    2.8f);
            }
        }

        private bool HandleEditablePatternViewportMouse()
        {
            if (_patternViewportDragging)
            {
                ClaimPatternViewportInput();
                if (Input.GetMouseButtonDown(1))
                {
                    CancelEditablePatternViewportGesture(notify: true);
                    return true;
                }
                if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                {
                    EndEditablePatternViewportDrag();
                    return true;
                }

                UpdateEditablePatternViewportDrag();
                return true;
            }

            if (_patternViewportGesture != null &&
                (!TryGetSelectedViewportPattern(out SmartBuildPatternNode selected) ||
                 selected.Id != _patternViewportGesture.NodeId))
            {
                CancelEditablePatternViewportGesture(notify: false);
            }

            if (_patternViewportGesture != null && Input.GetMouseButtonDown(1))
            {
                ClaimPatternViewportInput();
                CancelEditablePatternViewportGesture(notify: true);
                return true;
            }

            if (!Input.GetMouseButtonDown(0) ||
                !TryGetSelectedViewportPattern(out SmartBuildPatternNode pattern))
            {
                return false;
            }

            IReadOnlyList<PatternViewportHandleVisual> visuals =
                BuildPatternViewportHandleVisuals(pattern, pattern.Definition);
            if (TryPickEditablePatternViewportHandle(
                    pattern,
                    visuals,
                    out SmartBuildPatternViewportHandle handle,
                    out Vector3 handleLocal))
            {
                BeginEditablePatternViewportDrag(pattern, handle, handleLocal);
                ClaimPatternViewportInput();
                return true;
            }

            if (_patternViewportGesture == null)
                return false;

            ClaimPatternViewportInput();
            InfoStore.Add("Finish the editable pattern with Enter, or restore it with Escape.");
            return true;
        }

        private void BeginEditablePatternViewportDrag(
            SmartBuildPatternNode pattern,
            SmartBuildPatternViewportHandle handle,
            Vector3 handleLocal)
        {
            if (_patternViewportGesture == null ||
                _patternViewportGesture.NodeId != pattern.Id)
            {
                FinalizePendingSceneHistory();
                _patternViewportSceneStart = CaptureSceneSnapshot();
                _patternViewportGesture = new SmartBuildPatternViewportGesture(
                    pattern.Id,
                    pattern.Definition);
                _patternViewportLastIssue = null;
                InfoStore.Add(
                    "Editable pattern gesture started. Drag handles, then press Enter to commit or Escape to restore.");
            }

            _patternViewportDragging = true;
            _patternViewportDragHandle = handle;
            _patternViewportDragMouseStart = MouseGuiPosition();
            _patternViewportDragCellStart = RoundCell(handleLocal);

            if (handle.Kind == SmartBuildPatternViewportHandleKind.RadialAngle)
            {
                SmartBuildPatternDefinition definition = pattern.Definition;
                _patternViewportRadialAngleStart = definition.RadialAngleStepDegrees;
                if (TryProjectSmartGizmo(
                        pattern.HostPiece.Construct,
                        CellVector(definition.RadialPivot),
                        out Vector2 pivotScreen))
                {
                    _patternViewportRadialMouseAngleStart =
                        ScreenAngleDegrees(_patternViewportDragMouseStart - pivotScreen);
                }
            }
        }

        private void UpdateEditablePatternViewportDrag()
        {
            if (!TryGetSelectedViewportPattern(out SmartBuildPatternNode pattern) ||
                _patternViewportGesture == null ||
                pattern.Id != _patternViewportGesture.NodeId)
            {
                CancelEditablePatternViewportGesture(notify: false);
                return;
            }

            SmartBuildPatternDefinition current = pattern.Definition;
            SmartBuildPatternDefinition updated;
            string reason;
            if (_patternViewportDragHandle.Kind ==
                SmartBuildPatternViewportHandleKind.RadialAngle)
            {
                if (!TryProjectSmartGizmo(
                        pattern.HostPiece.Construct,
                        CellVector(current.RadialPivot),
                        out Vector2 pivotScreen))
                {
                    return;
                }

                float currentMouseAngle = ScreenAngleDegrees(
                    MouseGuiPosition() - pivotScreen);
                float candidate = _patternViewportRadialAngleStart +
                                  Mathf.DeltaAngle(
                                      _patternViewportRadialMouseAngleStart,
                                      currentMouseAngle);
                if (!SmartBuildPatternViewportEditor.TryApplyRadialAngle(
                        current,
                        candidate,
                        out updated,
                        out reason))
                {
                    ReportPatternViewportIssue(reason);
                    return;
                }
            }
            else
            {
                if (!TryProjectPatternViewportCellDrag(
                        pattern.HostPiece.Construct,
                        out Vector3i targetCell))
                {
                    return;
                }
                if (!SmartBuildPatternViewportEditor.TryApplyCellHandle(
                        current,
                        _patternViewportDragHandle,
                        pattern.HostPiece.Origin,
                        targetCell,
                        out updated,
                        out reason))
                {
                    ReportPatternViewportIssue(reason);
                    return;
                }
            }

            if (SmartBuildPatternViewportEditor.DefinitionsEqual(current, updated))
                return;
            if (!_scene.TryUpdateSelectedPattern(updated, out reason))
            {
                ReportPatternViewportIssue(reason);
                return;
            }

            _patternViewportGesture.Accept(updated);
            _patternViewportLastIssue = null;
            SyncEditablePatternFields(pattern);
            _planDirty = true;
            RefreshPreviewVolumesOnly();
        }

        private void EndEditablePatternViewportDrag()
        {
            _patternViewportDragging = false;
            _patternViewportLastIssue = null;
            if (_planDirty)
                RebuildPlan();
            InfoStore.Add("Editable pattern staged. Press Enter to commit or Escape to restore.");
        }

        private bool TryCommitEditablePatternViewportGesture()
        {
            if (_patternViewportGesture == null)
                return false;

            SmartBuildPatternViewportGesture gesture = _patternViewportGesture;
            SmartBuildSceneSnapshot before = _patternViewportSceneStart;
            if (!gesture.TryCommit(out bool recordHistory))
            {
                ClearEditablePatternViewportGestureState();
                return false;
            }

            ClearEditablePatternViewportGestureState();
            if (_planDirty)
                RebuildPlan();
            if (recordHistory)
            {
                // This is the transaction's only history publication. Every live
                // handle update before Enter deliberately bypasses scene history.
                PushSceneSnapshot(before);
                InfoStore.Add("Editable pattern handle edit committed.");
            }
            else
            {
                InfoStore.Add("Editable pattern was unchanged.");
            }

            ClaimPatternViewportInput();
            return true;
        }

        internal bool TryHandleEditablePatternGestureEscape()
        {
            if (!Active || _patternViewportGesture == null)
                return false;

            CancelEditablePatternViewportGesture(notify: true);
            ClaimPatternViewportInput();
            DecoLimitLifter.EsuEscapeCloseGuard.Arm();
            return true;
        }

        private bool CancelEditablePatternViewportGesture(bool notify)
        {
            if (_patternViewportGesture == null)
                return false;

            SmartBuildPatternViewportGesture gesture = _patternViewportGesture;
            SmartBuildSceneSnapshot startingScene = _patternViewportSceneStart;
            bool canRestore = gesture.TryCancel(
                out SmartBuildPatternDefinition startingDefinition);
            ClearEditablePatternViewportGestureState();

            SmartBuildPatternNode pattern = null;
            bool restored = false;
            if (canRestore &&
                TryGetSelectedViewportPattern(out pattern) &&
                pattern.Id == gesture.NodeId)
            {
                restored = _scene.TryUpdateSelectedPattern(
                    startingDefinition,
                    out _);
            }
            if (!restored && startingScene != null)
            {
                // The immutable scene snapshot is the final safety net if selection
                // or a concurrently refreshed node made direct restoration impossible.
                RestoreSceneSnapshot(startingScene);
            }
            else if (restored)
            {
                SyncEditablePatternFields(pattern);
                RebuildPlan();
            }

            if (notify)
                InfoStore.Add("Editable pattern gesture cancelled; starting parameters restored.");
            return true;
        }

        private void ClearEditablePatternViewportGestureState()
        {
            _patternViewportGesture = null;
            _patternViewportSceneStart = null;
            _patternViewportDragging = false;
            _patternViewportDragHandle = default;
            _patternViewportLastIssue = null;
        }

        private bool TryProjectPatternViewportCellDrag(
            AllConstruct construct,
            out Vector3i targetCell)
        {
            targetCell = _patternViewportDragCellStart;
            Vector3 start = CellVector(_patternViewportDragCellStart);
            if (!TryProjectSmartGizmo(construct, start, out Vector2 projectedStart))
                return false;

            Vector3[] localAxes = { Vector3.right, Vector3.up, Vector3.forward };
            var screenAxes = new Vector2[3];
            var valid = new bool[3];
            for (int axis = 0; axis < localAxes.Length; axis++)
            {
                if (!TryProjectSmartGizmo(
                        construct,
                        start + localAxes[axis],
                        out Vector2 projectedAxis))
                {
                    continue;
                }
                screenAxes[axis] = projectedAxis - projectedStart;
                valid[axis] = screenAxes[axis].sqrMagnitude > 0.0001f;
            }

            int first = -1;
            int second = -1;
            float bestDeterminant = 0f;
            for (int left = 0; left < 3; left++)
            {
                if (!valid[left])
                    continue;
                for (int right = left + 1; right < 3; right++)
                {
                    if (!valid[right])
                        continue;
                    float determinant = Math.Abs(
                        screenAxes[left].x * screenAxes[right].y -
                        screenAxes[left].y * screenAxes[right].x);
                    if (determinant <= bestDeterminant)
                        continue;
                    bestDeterminant = determinant;
                    first = left;
                    second = right;
                }
            }

            Vector2 mouseDelta = MouseGuiPosition() - _patternViewportDragMouseStart;
            var deltaCells = new int[3];
            int snap = Math.Max(1, SmartMoveStepCells);
            if (first >= 0 && second >= 0 && bestDeterminant > 0.001f)
            {
                Vector2 a = screenAxes[first];
                Vector2 b = screenAxes[second];
                float determinant = a.x * b.y - a.y * b.x;
                float firstCells =
                    (mouseDelta.x * b.y - mouseDelta.y * b.x) / determinant;
                float secondCells =
                    (a.x * mouseDelta.y - a.y * mouseDelta.x) / determinant;
                deltaCells[first] = RoundPatternCells(firstCells, snap);
                deltaCells[second] = RoundPatternCells(secondCells, snap);
            }
            else
            {
                int axis = -1;
                float bestLength = 0f;
                for (int index = 0; index < 3; index++)
                {
                    float length = valid[index] ? screenAxes[index].sqrMagnitude : 0f;
                    if (length <= bestLength)
                        continue;
                    bestLength = length;
                    axis = index;
                }
                if (axis < 0 || bestLength <= 0.0001f)
                    return false;
                float cells = Vector2.Dot(mouseDelta, screenAxes[axis]) / bestLength;
                deltaCells[axis] = RoundPatternCells(cells, snap);
            }

            targetCell = new Vector3i(
                _patternViewportDragCellStart.x + deltaCells[0],
                _patternViewportDragCellStart.y + deltaCells[1],
                _patternViewportDragCellStart.z + deltaCells[2]);
            return true;
        }

        private bool TryPickEditablePatternViewportHandle(
            SmartBuildPatternNode pattern,
            IReadOnlyList<PatternViewportHandleVisual> visuals,
            out SmartBuildPatternViewportHandle handle,
            out Vector3 localPosition)
        {
            handle = default;
            localPosition = Vector3.zero;
            if (pattern?.HostPiece?.Construct == null ||
                visuals == null ||
                SmartBuildInputScope.MouseOverUi)
            {
                return false;
            }

            Vector2 mouse = MouseGuiPosition();
            float best = PatternViewportPickThresholdPixels;
            bool found = false;
            foreach (PatternViewportHandleVisual visual in visuals)
            {
                if (!visual.Editable ||
                    !TryProjectSmartGizmo(
                        pattern.HostPiece.Construct,
                        visual.LocalPosition,
                        out Vector2 screen))
                {
                    continue;
                }

                float distance = Vector2.Distance(mouse, screen);
                if (distance >= best)
                    continue;
                best = distance;
                found = true;
                handle = visual.Handle;
                localPosition = visual.LocalPosition;
            }
            return found;
        }

        private static IReadOnlyList<PatternViewportHandleVisual>
            BuildPatternViewportHandleVisuals(
                SmartBuildPatternNode pattern,
                SmartBuildPatternDefinition definition)
        {
            var visuals = new List<PatternViewportHandleVisual>();
            Vector3 anchor = CellVector(pattern.HostPiece.Origin);
            switch (definition.Kind)
            {
                case SmartBuildEditablePatternKind.Linear:
                case SmartBuildEditablePatternKind.Grid:
                    visuals.Add(new PatternViewportHandleVisual(
                        new SmartBuildPatternViewportHandle(
                            SmartBuildPatternViewportHandleKind.PrimaryStep),
                        anchor + CellVector(definition.PrimaryStep),
                        editable: true));
                    if (definition.Kind == SmartBuildEditablePatternKind.Grid)
                    {
                        visuals.Add(new PatternViewportHandleVisual(
                            new SmartBuildPatternViewportHandle(
                                SmartBuildPatternViewportHandleKind.SecondaryStep),
                            anchor + CellVector(definition.SecondaryStep),
                            editable: true));
                    }
                    break;

                case SmartBuildEditablePatternKind.Radial:
                    visuals.Add(new PatternViewportHandleVisual(
                        new SmartBuildPatternViewportHandle(
                            SmartBuildPatternViewportHandleKind.RadialPivot),
                        CellVector(definition.RadialPivot),
                        editable: true));
                    visuals.Add(new PatternViewportHandleVisual(
                        new SmartBuildPatternViewportHandle(
                            SmartBuildPatternViewportHandleKind.RadialAngle),
                        RadialAngleHandleLocal(pattern, definition),
                        editable: true));
                    break;

                case SmartBuildEditablePatternKind.Polyline:
                    IReadOnlyList<Vector3i> points =
                        definition.PathPoints ?? Array.Empty<Vector3i>();
                    if (points.Count == 0)
                        break;
                    Vector3i first = points[0];
                    for (int index = 0; index < points.Count; index++)
                    {
                        visuals.Add(new PatternViewportHandleVisual(
                            new SmartBuildPatternViewportHandle(
                                SmartBuildPatternViewportHandleKind.PolylinePoint,
                                index),
                            anchor + CellVector(points[index] - first),
                            editable: index > 0));
                    }
                    break;
            }
            return visuals;
        }

        private static Vector3 RadialAngleHandleLocal(
            SmartBuildPatternNode pattern,
            SmartBuildPatternDefinition definition)
        {
            Vector3 pivot = CellVector(definition.RadialPivot);
            Vector3 arm = CellVector(pattern.HostPiece.Origin) - pivot;
            if (arm.sqrMagnitude < 0.25f)
                arm = RadialFallbackArm(definition.RadialAxis) * 2f;
            return pivot +
                   Quaternion.AngleAxis(
                       definition.RadialAngleStepDegrees,
                       RadialAxisVector(definition.RadialAxis)) * arm;
        }

        private static Vector3 RadialAxisVector(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return Vector3.right;
                case DecorationEditAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.up;
            }
        }

        private static Vector3 RadialFallbackArm(DecorationEditAxis axis) =>
            axis == DecorationEditAxis.X ? Vector3.up : Vector3.right;

        private Color PatternViewportColor(
            SmartBuildPatternViewportHandle handle,
            bool hasHover,
            SmartBuildPatternViewportHandle hovered,
            bool editable = true)
        {
            Color baseColor;
            switch (handle.Kind)
            {
                case SmartBuildPatternViewportHandleKind.PrimaryStep:
                    baseColor = new Color(0.15f, 0.9f, 1f, 0.98f);
                    break;
                case SmartBuildPatternViewportHandleKind.SecondaryStep:
                    baseColor = new Color(1f, 0.25f, 0.9f, 0.98f);
                    break;
                case SmartBuildPatternViewportHandleKind.RadialPivot:
                    baseColor = new Color(1f, 0.9f, 0.18f, 0.98f);
                    break;
                case SmartBuildPatternViewportHandleKind.RadialAngle:
                    baseColor = new Color(1f, 0.48f, 0.1f, 0.98f);
                    break;
                default:
                    baseColor = editable
                        ? new Color(0.2f, 1f, 0.48f, 0.98f)
                        : new Color(0.72f, 0.78f, 0.82f, 0.9f);
                    break;
            }

            bool active = _patternViewportDragging &&
                          handle == _patternViewportDragHandle;
            bool hot = hasHover && handle == hovered;
            return active || hot
                ? Color.Lerp(baseColor, Color.white, 0.48f)
                : baseColor;
        }

        private bool TryGetSelectedViewportPattern(out SmartBuildPatternNode pattern)
        {
            pattern = _scene?.SelectedNode as SmartBuildPatternNode;
            return pattern != null;
        }

        private void ReportPatternViewportIssue(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) ||
                string.Equals(reason, _patternViewportLastIssue, StringComparison.Ordinal))
            {
                return;
            }
            _patternViewportLastIssue = reason;
            InfoStore.Add("Editable pattern handle: " + reason);
        }

        private static void ClaimPatternViewportInput()
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            SmartBuildInputScope.ClaimCameraInputForFrames();
        }

        private static Vector3 CellVector(Vector3i cell) =>
            new Vector3(cell.x, cell.y, cell.z);

        private static Vector3i RoundCell(Vector3 value) =>
            new Vector3i(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.z));

        private static int RoundPatternCells(float cells, int snap)
        {
            float bounded = Mathf.Clamp(cells, -100000f, 100000f);
            return Mathf.RoundToInt(bounded / snap) * snap;
        }

        private static float ScreenAngleDegrees(Vector2 direction) =>
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        private readonly struct PatternViewportHandleVisual
        {
            internal PatternViewportHandleVisual(
                SmartBuildPatternViewportHandle handle,
                Vector3 localPosition,
                bool editable)
            {
                Handle = handle;
                LocalPosition = localPosition;
                Editable = editable;
            }

            internal SmartBuildPatternViewportHandle Handle { get; }

            internal Vector3 LocalPosition { get; }

            internal bool Editable { get; }
        }
    }
}
