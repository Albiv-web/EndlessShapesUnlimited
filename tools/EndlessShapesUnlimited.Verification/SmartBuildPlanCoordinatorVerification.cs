using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

internal static partial class Program
{
    private static void VerifySmartBuildPlanCoordinator()
    {
        var coordinator = new SmartBuildPlanCoordinator();
        var craft = new object();
        var result = new SmartBuildCoordinatedPlan(
            plan: null,
            cells: new[] { new Vector3i(1, 2, 3), new Vector3i(2, 2, 3) },
            cellSets: Array.Empty<IReadOnlyList<Vector3i>>(),
            volumes: Array.Empty<SmartBuildVolume>());

        coordinator.ObserveCraftIdentity(craft);
        coordinator.RegisterRevision(SmartBuildPlanRevisionKind.Geometry);
        bool firstRequestRequiresPlan = !coordinator.TryReuseNormalPlan(out _);
        coordinator.RecordNormalPlan(
            result,
            TimeSpan.FromMilliseconds(2.75),
            nodeCount: 4,
            cellCount: 12,
            placementCount: 7);

        bool unchangedReuses =
            coordinator.TryReuseNormalPlan(out SmartBuildCoordinatedPlan unchanged) &&
            ReferenceEquals(result, unchanged);
        coordinator.RegisterRevision(SmartBuildPlanRevisionKind.Selection);
        bool selectionReuses = coordinator.TryReuseNormalPlan(out _);
        coordinator.RegisterRevision(SmartBuildPlanRevisionKind.Presentation);
        bool presentationReuses = coordinator.TryReuseNormalPlan(out _);

        bool allPlanInputsInvalidate = true;
        SmartBuildPlanRevisionKind[] inputs =
        {
            SmartBuildPlanRevisionKind.Geometry,
            SmartBuildPlanRevisionKind.Material,
            SmartBuildPlanRevisionKind.Symmetry,
            SmartBuildPlanRevisionKind.Occupancy,
            SmartBuildPlanRevisionKind.Craft
        };
        foreach (SmartBuildPlanRevisionKind input in inputs)
        {
            coordinator.RegisterRevision(input);
            allPlanInputsInvalidate &= !coordinator.TryReuseNormalPlan(out _);
            coordinator.RecordNormalPlan(
                result,
                TimeSpan.FromMilliseconds(1),
                nodeCount: 4,
                cellCount: 12,
                placementCount: 7);
        }

        bool sameCraftReuses = true;
        coordinator.ObserveCraftIdentity(craft);
        sameCraftReuses &= coordinator.TryReuseNormalPlan(out _);
        coordinator.ObserveCraftIdentity(new object());
        bool replacementCraftInvalidates = !coordinator.TryReuseNormalPlan(out _);

        SmartBuildPlanCoordinatorDiagnostics diagnostics = coordinator.Diagnostics;
        bool diagnosticsRecorded =
            diagnostics.PlanningPassCount == 6 &&
            diagnostics.PlanReuseCount == 4 &&
            Math.Abs(diagnostics.LastPlanningMilliseconds - 1d) < 0.001d &&
            diagnostics.NodeCount == 4 &&
            diagnostics.CellCount == 12 &&
            diagnostics.PlacementCount == 7 &&
            diagnostics.SelectionRevision == 1 &&
            diagnostics.PresentationRevision == 1 &&
            !diagnostics.NormalPlanIsCurrent;

        Assert(
            firstRequestRequiresPlan &&
            unchangedReuses &&
            selectionReuses &&
            presentationReuses &&
            allPlanInputsInvalidate &&
            sameCraftReuses &&
            replacementCraftInvalidates &&
            diagnosticsRecorded,
            "Smart Builder plan coordination caches plan/preview atomically, ignores selection and presentation revisions, invalidates every planning-input domain, observes craft identity, and records timing/count diagnostics.");

        Vector3i cell = new Vector3i(7, -2, 11);
        long firstDefinition = SmartBuildSession.MixCraftCellFingerprint(
            1469598103934665603L,
            cell,
            occupied: true,
            definitionIdentity: 101,
            itemOrigin: cell,
            rotation: Quaternion.identity,
            footprintCellCount: 1);
        long repeatedDefinition = SmartBuildSession.MixCraftCellFingerprint(
            1469598103934665603L,
            cell,
            occupied: true,
            definitionIdentity: 101,
            itemOrigin: cell,
            rotation: Quaternion.identity,
            footprintCellCount: 1);
        long replacementDefinition = SmartBuildSession.MixCraftCellFingerprint(
            1469598103934665603L,
            cell,
            occupied: true,
            definitionIdentity: 202,
            itemOrigin: cell,
            rotation: Quaternion.identity,
            footprintCellCount: 1);
        long replacementRotation = SmartBuildSession.MixCraftCellFingerprint(
            1469598103934665603L,
            cell,
            occupied: true,
            definitionIdentity: 101,
            itemOrigin: cell,
            rotation: new Quaternion(0f, 0.70710677f, 0f, 0.70710677f),
            footprintCellCount: 1);
        long empty = SmartBuildSession.MixCraftCellFingerprint(
            1469598103934665603L,
            cell,
            occupied: false,
            definitionIdentity: 0,
            itemOrigin: cell,
            rotation: Quaternion.identity,
            footprintCellCount: 0);
        Assert(
            firstDefinition == repeatedDefinition &&
            firstDefinition != replacementDefinition &&
            firstDefinition != replacementRotation &&
            firstDefinition != empty,
            "Smart Builder craft revisions detect definition, footprint/origin, orientation, and occupancy changes even when the same craft-local cell remains occupied.");
    }
}
