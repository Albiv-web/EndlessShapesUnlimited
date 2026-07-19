using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal readonly struct SmartBuildNodeProvenance
    {
        internal SmartBuildNodeProvenance(int nodeId, int patternInstanceId)
        {
            NodeId = nodeId;
            PatternInstanceId = patternInstanceId;
        }

        internal int NodeId { get; }

        internal int PatternInstanceId { get; }
    }

    internal enum SmartBuildCellDiagnosticState
    {
        Valid = 0,
        SkippedOccupied = 1,
        PreviewOverlap = 2,
        CraftCollision = 3,
        Disconnected = 4,
        Removal = 5,
        Replacement = 6,
        Unsupported = 7
    }

    internal readonly struct SmartBuildCellDiagnostic
    {
        internal SmartBuildCellDiagnostic(
            SmartBuildCellDiagnosticState state,
            Vector3i cell,
            bool hasCell,
            string message,
            int nodeId = -1,
            int patternInstanceId = -1)
        {
            State = state;
            Cell = cell;
            HasCell = hasCell;
            Message = message ?? string.Empty;
            NodeId = nodeId;
            PatternInstanceId = patternInstanceId;
        }

        internal SmartBuildCellDiagnosticState State { get; }

        internal Vector3i Cell { get; }

        internal bool HasCell { get; }

        internal string Message { get; }

        internal int NodeId { get; }

        internal int PatternInstanceId { get; }
    }

    internal static class SmartBuildDiagnosticBuilder
    {
        internal static IReadOnlyList<SmartBuildCellDiagnostic> FromPlan(SmartBuildPlan plan)
        {
            if (plan == null)
                return Array.Empty<SmartBuildCellDiagnostic>();

            var byCell = new Dictionary<string, SmartBuildCellDiagnostic>(StringComparer.Ordinal);
            foreach (SmartBuildPlacement placement in plan.Placements ?? Array.Empty<SmartBuildPlacement>())
            {
                foreach (Vector3i cell in placement.CoveredCells())
                {
                    if (!plan.TryGetCellProvenance(
                            cell,
                            out SmartBuildNodeProvenance provenance))
                    {
                        provenance = new SmartBuildNodeProvenance(
                            placement.NodeId,
                            placement.PatternInstanceId);
                    }
                    AddOrReplace(
                        byCell,
                        new SmartBuildCellDiagnostic(
                            SmartBuildCellDiagnosticState.Valid,
                            cell,
                            hasCell: true,
                            "Valid placement cell.",
                            provenance.NodeId,
                            provenance.PatternInstanceId));
                }
            }
            foreach (Vector3i cell in plan.SkippedCells ?? Array.Empty<Vector3i>())
            {
                if (!plan.TryGetCellProvenance(
                        cell,
                        out SmartBuildNodeProvenance provenance))
                    provenance = new SmartBuildNodeProvenance(-1, -1);
                AddOrReplace(
                    byCell,
                    new SmartBuildCellDiagnostic(
                        SmartBuildCellDiagnosticState.SkippedOccupied,
                        cell,
                        hasCell: true,
                        "Occupied cell skipped by the current occupancy mode.",
                        provenance.NodeId,
                        provenance.PatternInstanceId));
            }

            SmartBuildCellDiagnosticState destructiveState =
                plan.CommitOperation == SmartBuildCommitOperation.Replace
                    ? SmartBuildCellDiagnosticState.Replacement
                    : SmartBuildCellDiagnosticState.Removal;
            string destructiveMessage =
                plan.CommitOperation == SmartBuildCommitOperation.Replace
                    ? "Existing craft item footprint scheduled for replacement."
                    : "Existing craft item footprint scheduled for removal.";
            foreach (Vector3i cell in plan.RemovalFootprintCells ?? Array.Empty<Vector3i>())
            {
                if (!plan.TryGetCellProvenance(
                        cell,
                        out SmartBuildNodeProvenance provenance))
                    provenance = new SmartBuildNodeProvenance(-1, -1);
                AddOrReplace(
                    byCell,
                    new SmartBuildCellDiagnostic(
                        destructiveState,
                        cell,
                        hasCell: true,
                        destructiveMessage,
                        provenance.NodeId,
                        provenance.PatternInstanceId));
            }

            var diagnostics = byCell.Values
                .OrderBy(entry => entry.Cell.x)
                .ThenBy(entry => entry.Cell.y)
                .ThenBy(entry => entry.Cell.z)
                .ToList();
            if (!plan.CanCommit && !string.IsNullOrWhiteSpace(plan.FailureReason))
            {
                SmartBuildCellDiagnosticState state = FailureState(plan.FailureReason);
                Vector3i issueCell = plan.SkippedCells?.FirstOrDefault() ?? new Vector3i(0, 0, 0);
                bool hasCell = plan.SkippedCells?.Count > 0;
                if (!plan.TryGetCellProvenance(
                        issueCell,
                        out SmartBuildNodeProvenance provenance))
                    provenance = new SmartBuildNodeProvenance(-1, -1);
                diagnostics.Add(
                    new SmartBuildCellDiagnostic(
                        state,
                        issueCell,
                        hasCell,
                        plan.FailureReason,
                        provenance.NodeId,
                        provenance.PatternInstanceId));
            }
            return diagnostics;
        }

        private static void AddOrReplace(
            IDictionary<string, SmartBuildCellDiagnostic> diagnostics,
            SmartBuildCellDiagnostic diagnostic)
        {
            diagnostics[DecoLimitLifter.EsuSymmetry.CellKey(diagnostic.Cell)] = diagnostic;
        }

        private static SmartBuildCellDiagnosticState FailureState(string failure)
        {
            string normalized = (failure ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("overlap"))
                return SmartBuildCellDiagnosticState.PreviewOverlap;
            if (normalized.Contains("intersect") || normalized.Contains("occupied"))
                return SmartBuildCellDiagnosticState.CraftCollision;
            if (normalized.Contains("disconnect") || normalized.Contains("touch"))
                return SmartBuildCellDiagnosticState.Disconnected;
            return SmartBuildCellDiagnosticState.Unsupported;
        }
    }
}
