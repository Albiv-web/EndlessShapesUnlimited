using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildPatternViewportHandleKind
    {
        PrimaryStep = 0,
        SecondaryStep = 1,
        RadialPivot = 2,
        RadialAngle = 3,
        PolylinePoint = 4
    }

    internal readonly struct SmartBuildPatternViewportHandle
    {
        internal SmartBuildPatternViewportHandle(
            SmartBuildPatternViewportHandleKind kind,
            int pointIndex = -1)
        {
            Kind = kind;
            PointIndex = pointIndex;
        }

        internal SmartBuildPatternViewportHandleKind Kind { get; }

        internal int PointIndex { get; }

        public override bool Equals(object obj) =>
            obj is SmartBuildPatternViewportHandle other &&
            Kind == other.Kind &&
            PointIndex == other.PointIndex;

        public override int GetHashCode() =>
            ((int)Kind * 397) ^ PointIndex;

        public static bool operator ==(
            SmartBuildPatternViewportHandle left,
            SmartBuildPatternViewportHandle right) =>
            left.Equals(right);

        public static bool operator !=(
            SmartBuildPatternViewportHandle left,
            SmartBuildPatternViewportHandle right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Pure pattern-parameter edits shared by the viewport and verification suite.
    /// The source/host cell is the visual origin for offset-based handles.
    /// </summary>
    internal static class SmartBuildPatternViewportEditor
    {
        internal static bool TryApplyCellHandle(
            SmartBuildPatternDefinition definition,
            SmartBuildPatternViewportHandle handle,
            Vector3i hostCell,
            Vector3i targetCell,
            out SmartBuildPatternDefinition updated,
            out string reason)
        {
            updated = null;
            if (definition == null)
            {
                reason = "No editable pattern definition is available.";
                return false;
            }

            updated = definition.Clone();
            switch (handle.Kind)
            {
                case SmartBuildPatternViewportHandleKind.PrimaryStep:
                    if (definition.Kind != SmartBuildEditablePatternKind.Linear &&
                        definition.Kind != SmartBuildEditablePatternKind.Grid)
                    {
                        reason = "The primary-step handle only belongs to linear and grid patterns.";
                        updated = null;
                        return false;
                    }
                    updated.PrimaryStep = targetCell - hostCell;
                    break;

                case SmartBuildPatternViewportHandleKind.SecondaryStep:
                    if (definition.Kind != SmartBuildEditablePatternKind.Grid)
                    {
                        reason = "The secondary-step handle only belongs to grid patterns.";
                        updated = null;
                        return false;
                    }
                    updated.SecondaryStep = targetCell - hostCell;
                    break;

                case SmartBuildPatternViewportHandleKind.RadialPivot:
                    if (definition.Kind != SmartBuildEditablePatternKind.Radial)
                    {
                        reason = "The pivot handle only belongs to radial patterns.";
                        updated = null;
                        return false;
                    }
                    updated.RadialPivot = targetCell;
                    break;

                case SmartBuildPatternViewportHandleKind.PolylinePoint:
                    if (definition.Kind != SmartBuildEditablePatternKind.Polyline)
                    {
                        reason = "Point handles only belong to polyline patterns.";
                        updated = null;
                        return false;
                    }

                    Vector3i[] points = (definition.PathPoints ?? Array.Empty<Vector3i>())
                        .ToArray();
                    if (handle.PointIndex <= 0 || handle.PointIndex >= points.Length)
                    {
                        reason = handle.PointIndex == 0
                            ? "The first polyline point is the source anchor; drag another point to reshape the path."
                            : "The selected polyline point no longer exists.";
                        updated = null;
                        return false;
                    }

                    // Path planning is offset-based: point zero maps to the source.
                    // Convert the craft-local viewport cell back into that path frame.
                    points[handle.PointIndex] =
                        points[0] + (targetCell - hostCell);
                    updated.PathPoints = points;
                    break;

                default:
                    reason = "This pattern handle does not edit a cell position.";
                    updated = null;
                    return false;
            }

            reason = null;
            return true;
        }

        internal static bool TryApplyRadialAngle(
            SmartBuildPatternDefinition definition,
            float candidateDegrees,
            out SmartBuildPatternDefinition updated,
            out string reason)
        {
            updated = null;
            if (definition == null || definition.Kind != SmartBuildEditablePatternKind.Radial)
            {
                reason = "The angle handle only belongs to radial patterns.";
                return false;
            }
            if (float.IsNaN(candidateDegrees) || float.IsInfinity(candidateDegrees))
            {
                reason = "The radial angle must be finite.";
                return false;
            }

            float snap = definition.RadialOrientation ==
                         SmartBuildRadialOrientationMode.RotateCardinal
                ? 90f
                : 5f;
            float snapped = (float)Math.Round(
                candidateDegrees / snap,
                MidpointRounding.AwayFromZero) * snap;
            if (Math.Abs(snapped) < 0.0001f)
            {
                float direction = candidateDegrees < 0f ||
                                  Math.Abs(candidateDegrees) < 0.0001f &&
                                  definition.RadialAngleStepDegrees < 0f
                    ? -1f
                    : 1f;
                snapped = direction * snap;
            }

            updated = definition.Clone();
            updated.RadialAngleStepDegrees = snapped;
            reason = null;
            return true;
        }

        internal static bool DefinitionsEqual(
            SmartBuildPatternDefinition left,
            SmartBuildPatternDefinition right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null ||
                left.Kind != right.Kind ||
                !SameCell(left.PrimaryStep, right.PrimaryStep) ||
                !SameCell(left.SecondaryStep, right.SecondaryStep) ||
                left.PrimaryBefore != right.PrimaryBefore ||
                left.PrimaryAfter != right.PrimaryAfter ||
                left.SecondaryBefore != right.SecondaryBefore ||
                left.SecondaryAfter != right.SecondaryAfter ||
                !SameCell(left.RadialPivot, right.RadialPivot) ||
                left.RadialAxis != right.RadialAxis ||
                Math.Abs(left.RadialAngleStepDegrees - right.RadialAngleStepDegrees) > 0.0001f ||
                left.RadialOrientation != right.RadialOrientation ||
                left.PathMode != right.PathMode ||
                left.PathSpacingCells != right.PathSpacingCells)
            {
                return false;
            }

            IReadOnlyList<Vector3i> leftPoints =
                left.PathPoints ?? Array.Empty<Vector3i>();
            IReadOnlyList<Vector3i> rightPoints =
                right.PathPoints ?? Array.Empty<Vector3i>();
            if (leftPoints.Count != rightPoints.Count)
                return false;
            for (int index = 0; index < leftPoints.Count; index++)
            {
                if (!SameCell(leftPoints[index], rightPoints[index]))
                    return false;
            }
            return true;
        }

        private static bool SameCell(Vector3i left, Vector3i right) =>
            left.x == right.x && left.y == right.y && left.z == right.z;
    }

    /// <summary>
    /// A viewport edit remains staged across any number of handle drags. The caller
    /// records history only when TryCommit reports a changed transaction.
    /// </summary>
    internal sealed class SmartBuildPatternViewportGesture
    {
        private readonly SmartBuildPatternDefinition _startingDefinition;
        private SmartBuildPatternDefinition _currentDefinition;

        internal SmartBuildPatternViewportGesture(
            int nodeId,
            SmartBuildPatternDefinition startingDefinition)
        {
            if (startingDefinition == null)
                throw new ArgumentNullException(nameof(startingDefinition));
            NodeId = nodeId;
            _startingDefinition = startingDefinition.Clone();
            _currentDefinition = startingDefinition.Clone();
            IsActive = true;
        }

        internal int NodeId { get; }

        internal bool IsActive { get; private set; }

        internal bool HasChanges =>
            IsActive &&
            !SmartBuildPatternViewportEditor.DefinitionsEqual(
                _startingDefinition,
                _currentDefinition);

        internal SmartBuildPatternDefinition CurrentDefinition =>
            _currentDefinition.Clone();

        internal void Accept(SmartBuildPatternDefinition definition)
        {
            if (!IsActive)
                throw new InvalidOperationException("The pattern viewport gesture is no longer active.");
            _currentDefinition = (definition ?? throw new ArgumentNullException(nameof(definition)))
                .Clone();
        }

        internal bool TryCommit(out bool recordHistory)
        {
            recordHistory = false;
            if (!IsActive)
                return false;

            recordHistory = HasChanges;
            IsActive = false;
            return true;
        }

        internal bool TryCancel(out SmartBuildPatternDefinition restoreDefinition)
        {
            restoreDefinition = null;
            if (!IsActive)
                return false;

            restoreDefinition = _startingDefinition.Clone();
            IsActive = false;
            return true;
        }
    }
}
