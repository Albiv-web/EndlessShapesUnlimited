using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

internal static class SmartBuildSafetyVerification
{
    internal static string Run()
    {
        VerifyCentralLimitContract();
        VerifyPlannerStopsAtCapPlusOne();
        VerifySceneNodesRejectBeforeGeometryEnumeration();
        VerifyPathControlsStopAtCapPlusOne();
        VerifyConditionalReplacementBounds();
        VerifyPersistedSceneAggregateBounds();
        return "PASS: Smart Builder safety verification completed.";
    }

    private static void VerifyCentralLimitContract()
    {
        var planner = new SmartBuildPlannerOptions();
        var conditional = new SmartBuildConditionalReplacementLimits();
        Require(
            SmartBuildLimits.MaximumSceneNodes == 512 &&
            SmartBuildLimits.WarningPlacementCount == 1_000 &&
            SmartBuildLimits.HardPlacementCount == 10_000 &&
            SmartBuildLimits.MaximumPlannerInputCells == 40_000 &&
            SmartBuildLimits.MaximumFloodFillCells == 4_096 &&
            SmartBuildLimits.MaximumPathControlPoints == 256 &&
            SmartBuildLimits.MaximumCoordinateMagnitude == 1_000_000 &&
            SmartBuildLimits.MaximumConditionalScopeCells == 100_000 &&
            SmartBuildLimits.MaximumConditionalItems == 4_096 &&
            SmartBuildLimits.MaximumConditionalFootprintCells == 65_536 &&
            planner.WarningPlacementCap == SmartBuildLimits.WarningPlacementCount &&
            planner.HardPlacementCap == SmartBuildLimits.HardPlacementCount &&
            conditional.HardScopeCellCap == SmartBuildLimits.MaximumConditionalScopeCells &&
            conditional.HardItemCap == SmartBuildLimits.MaximumConditionalItems &&
            conditional.HardFootprintCellCap == SmartBuildLimits.MaximumConditionalFootprintCells &&
            SmartBuildAdvancedToolPlanner.MaximumFloodFillCells ==
                SmartBuildLimits.MaximumFloodFillCells &&
            SmartBuildScenePresetPayload.MaximumPieces ==
                SmartBuildLimits.MaximumSceneNodes,
            "central limits and planner defaults diverged");
    }

    private static void VerifyPlannerStopsAtCapPlusOne()
    {
        int enumerated = 0;
        IEnumerable<Vector3i> OversizedCells()
        {
            for (int index = 0;
                 index < SmartBuildLimits.MaximumPlannerInputCells + 100;
                 index++)
            {
                enumerated++;
                yield return new Vector3i(index, 0, 0);
            }
        }

        SmartBuildVolume reference = OneCellVolume();
        SmartBuildPlan oversized = SmartBuildPlanner.BuildPlanFromCells(
            reference,
            OversizedCells(),
            SmartBuildAxis.X,
            SmartBlockFamily.ForTests(1),
            _ => false,
            new SmartBuildPlannerOptions());
        Require(
            !oversized.CanCommit &&
            oversized.Placements.Count == 0 &&
            enumerated == SmartBuildLimits.MaximumPlannerInputCells + 1 &&
            Contains(oversized.FailureReason, "bounded planning limit"),
            "planner did not reject an oversized source at cap plus one");

        var tinyPlacementLimit = new SmartBuildPlannerOptions
        {
            HardPlacementCap = 3,
            WarningPlacementCap = 2
        };
        SmartBuildPlan tooManyPlacements = SmartBuildPlanner.BuildPlanFromCells(
            reference,
            new[]
            {
                new Vector3i(0, 0, 0),
                new Vector3i(1, 0, 0),
                new Vector3i(2, 0, 0),
                new Vector3i(3, 0, 0),
                new Vector3i(4, 0, 0)
            },
            SmartBuildAxis.X,
            SmartBlockFamily.ForTests(1),
            _ => false,
            tinyPlacementLimit);
        Require(
            !tooManyPlacements.CanCommit &&
            tooManyPlacements.Placements.Count == 4 &&
            Contains(tooManyPlacements.FailureReason, "more than 3"),
            "placement packing did not stop at the configured hard cap plus one");

        var oversizedVolume = new SmartBuildVolume(
            null,
            new Vector3i(0, 0, 0),
            SmartBuildAxis.X,
            SmartBuildAxis.Y,
            SmartBuildAxis.Z,
            SmartBuildLimits.MaximumPlannerInputCells + 1,
            1,
            1);
        SmartBuildPlan knownOversized = SmartBuildPlanner.BuildPlan(
            oversizedVolume,
            SmartBlockFamily.ForTests(1),
            _ => false,
            new SmartBuildPlannerOptions());
        Require(
            !knownOversized.CanCommit &&
            knownOversized.Placements.Count == 0 &&
            Contains(knownOversized.FailureReason, "bounded planning limit"),
            "known volume dimensions were not rejected before cell enumeration");
    }

    private static void VerifySceneNodesRejectBeforeGeometryEnumeration()
    {
        var oversized = new SmartBuildPieceScene(null);
        Require(
            oversized.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(0, 0, 0),
                new Vector3i(SmartBuildLimits.MaximumPlannerInputCells + 1, 1, 1),
                SmartBuildDrawPlane.Camera)),
            "oversized safety fixture could not be staged");
        Require(
            !oversized.TryExpandSceneNodes(out _, out _, out string oversizedReason) &&
            Contains(oversizedReason, "before geometry enumeration"),
            "an oversized scene primitive reached cell enumeration");

        var exact = new SmartBuildPieceScene(null);
        string exactReason = null;
        IReadOnlyList<SmartBuildPiece> exactPieces = Array.Empty<SmartBuildPiece>();
        Require(
            exact.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(0, 0, 0),
                new Vector3i(SmartBuildLimits.MaximumPlannerInputCells, 1, 1),
                SmartBuildDrawPlane.Camera)) &&
            exact.TryExpandSceneNodes(out exactPieces, out _, out exactReason) &&
            exactPieces.Count == 1,
            "a scene exactly at the preview-enumeration cap was rejected: " + exactReason);

        var aggregate = new SmartBuildPieceScene(null);
        Require(
            aggregate.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(0, 0, 0),
                new Vector3i(SmartBuildLimits.MaximumPlannerInputCells, 1, 1),
                SmartBuildDrawPlane.Camera)) &&
            aggregate.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(-10, 1, 0),
                new Vector3i(1, 1, 1),
                SmartBuildDrawPlane.Camera)) &&
            !aggregate.TryExpandSceneNodes(out _, out _, out string aggregateReason) &&
            Contains(aggregateReason, "before geometry enumeration"),
            "aggregate scene bounds did not reject the cap-plus-one cell before enumeration");

        var outOfRange = new SmartBuildPieceScene(null);
        Require(
            outOfRange.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(SmartBuildLimits.MaximumCoordinateMagnitude, 0, 0),
                new Vector3i(2, 1, 1),
                SmartBuildDrawPlane.Camera)) &&
            !outOfRange.TryExpandSceneNodes(out _, out _, out string rangeReason) &&
            Contains(rangeReason, "coordinate limit"),
            "a node extending beyond the portable coordinate bound was not rejected before enumeration");
    }

    private static void VerifyPathControlsStopAtCapPlusOne()
    {
        int enumerated = 0;
        IEnumerable<Vector3i> OversizedPath()
        {
            for (int index = 0;
                 index < SmartBuildLimits.MaximumPathControlPoints + 10;
                 index++)
            {
                enumerated++;
                yield return new Vector3i(index, 0, 0);
            }
        }

        bool planned = SmartBuildAdvancedToolPlanner.TryPlanPath(
            OversizedPath(),
            SmartBuildPathArrayMode.PolylineVertices,
            out IReadOnlyList<SmartBuildGroupTransform> transforms,
            out string reason);
        Require(
            !planned &&
            transforms.Count == 0 &&
            enumerated == SmartBuildLimits.MaximumPathControlPoints + 1 &&
            Contains(reason, "at most 256"),
            "path controls were materialized beyond their cap plus one");
    }

    private static void VerifyConditionalReplacementBounds()
    {
        SmartBuildVolume reference = OneCellVolume();
        SmartBuildExistingItemMatch match =
            SmartBuildExistingItemMatch.ForMaterial(SmartBuildMaterial.Wood);
        int enumerated = 0;
        int resolved = 0;
        IEnumerable<Vector3i> OversizedScope()
        {
            for (int index = 0; index < 10; index++)
            {
                enumerated++;
                yield return new Vector3i(index, 0, 0);
            }
        }

        bool scopePlanned = SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            reference,
            OversizedScope(),
            cell =>
            {
                resolved++;
                return Snapshot(cell, 1, new[] { cell });
            },
            _ => false,
            match,
            _ => SmartBlockCandidate.ForTests(1),
            new SmartBuildConditionalReplacementLimits
            {
                HardScopeCellCap = 3,
                HardItemCap = 3,
                HardFootprintCellCap = 3
            },
            out SmartBuildConditionalReplacementPlan scopePlan,
            out string scopeReason);
        Require(
            !scopePlanned && scopePlan == null &&
            enumerated == 4 && resolved == 0 &&
            Contains(scopeReason, "more than 3"),
            "conditional scope was scanned or resolved beyond cap plus one");

        int itemResolveCalls = 0;
        bool itemPlanned = SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            reference,
            new[]
            {
                new Vector3i(0, 0, 0),
                new Vector3i(1, 0, 0),
                new Vector3i(2, 0, 0),
                new Vector3i(3, 0, 0)
            },
            cell =>
            {
                itemResolveCalls++;
                return Snapshot(cell, 1, new[] { cell });
            },
            _ => false,
            match,
            _ => SmartBlockCandidate.ForTests(1),
            new SmartBuildConditionalReplacementLimits
            {
                HardScopeCellCap = 10,
                HardItemCap = 2,
                HardFootprintCellCap = 10
            },
            out SmartBuildConditionalReplacementPlan itemPlan,
            out string itemReason);
        Require(
            !itemPlanned && itemPlan == null &&
            itemResolveCalls == 3 &&
            Contains(itemReason, "more than 2"),
            "conditional item matching did not stop at item cap plus one");

        int replacementCalls = 0;
        Vector3i first = new Vector3i(0, 0, 0);
        Vector3i second = new Vector3i(10, 0, 0);
        bool footprintPlanned = SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            reference,
            new[] { first, second },
            cell => cell.Equals(first)
                ? Snapshot(
                    first,
                    3,
                    new[] { first, new Vector3i(1, 0, 0), new Vector3i(2, 0, 0) })
                : Snapshot(
                    second,
                    3,
                    new[] { second, new Vector3i(11, 0, 0), new Vector3i(12, 0, 0) }),
            _ => false,
            match,
            _ =>
            {
                replacementCalls++;
                return SmartBlockCandidate.ForTests(3);
            },
            new SmartBuildConditionalReplacementLimits
            {
                HardScopeCellCap = 10,
                HardItemCap = 10,
                HardFootprintCellCap = 4
            },
            out SmartBuildConditionalReplacementPlan footprintPlan,
            out string footprintReason);
        Require(
            !footprintPlanned && footprintPlan == null &&
            replacementCalls == 0 &&
            Contains(footprintReason, "more than 4"),
            "aggregate removal footprints were materialized before their checked limit");
    }

    private static void VerifyPersistedSceneAggregateBounds()
    {
        Require(
            !SmartBuildLimits.TryProductWithinLimit(
                SmartBuildLimits.MaximumPresetExtent,
                SmartBuildLimits.MaximumPresetExtent,
                SmartBuildLimits.MaximumPresetExtent,
                SmartBuildLimits.MaximumPersistedSceneEnumerationCells,
                out _),
            "checked persisted-scene multiplication accepted a 2048-cubed volume");

        SmartBuildScenePresetPayload huge = CaptureScene(
            new Vector3i(
                SmartBuildLimits.MaximumPresetExtent,
                SmartBuildLimits.MaximumPresetExtent,
                SmartBuildLimits.MaximumPresetExtent));
        Require(
            !huge.TryValidate(out string hugeReason) &&
            Contains(hugeReason, "estimated cell enumeration"),
            "a pathological but individually valid 2048-cubed payload passed validation");

        SmartBuildScenePresetPayload exactLimit = CaptureScene(
            new Vector3i(50, 50, 20),
            new Vector3i(50, 50, 20));
        Require(
            exactLimit.TryValidate(out string exactReason),
            "a persisted scene exactly at the aggregate limit was rejected: " + exactReason);

        SmartBuildScenePresetPayload overLimit = CaptureScene(
            new Vector3i(50, 50, 20),
            new Vector3i(50, 50, 20),
            new Vector3i(1, 1, 1));
        Require(
            !overLimit.TryValidate(out string aggregateReason) &&
            Contains(aggregateReason, "100,000-cell safety limit"),
            "persisted scene aggregate validation did not reject cap plus one");
    }

    private static SmartBuildScenePresetPayload CaptureScene(params Vector3i[] sizes)
    {
        var scene = new SmartBuildPieceScene(null);
        int offset = 0;
        foreach (Vector3i size in sizes ?? Array.Empty<Vector3i>())
        {
            Require(
                scene.Add(SmartBuildPiece.CreateCuboid(
                    null,
                    new Vector3i(offset, 0, 0),
                    size,
                    SmartBuildDrawPlane.Camera)),
                "test scene could not add a cuboid");
            offset += 100;
        }

        return SmartBuildScenePresetPayload.Capture(
            scene,
            SmartBuildMaterial.Wood,
            SmartBuildTool.Rotate,
            SmartBuildShapeKind.Cuboid,
            SmartBuildShapeDescriptors.CuboidKey,
            1,
            SmartBuildDrawPlane.Camera,
            SmartBuildEditHandleMode.Corner,
            SmartBuildSlopeSupportMode.Full,
            SmartBuildPreviewMode.Material,
            SmartBuildOccupancyMode.SkipOccupied);
    }

    private static SmartBuildExistingItemSnapshot Snapshot(
        Vector3i origin,
        int length,
        IEnumerable<Vector3i> footprint) =>
        new SmartBuildExistingItemSnapshot(
            origin,
            Quaternion.identity,
            null,
            Guid.Empty,
            SmartBuildMaterial.Wood,
            SmartBuildShapeDescriptors.CuboidKey,
            length,
            footprint);

    private static SmartBuildVolume OneCellVolume() =>
        SmartBuildVolume.FromBounds(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(0, 0, 0));

    private static bool Contains(string value, string expected) =>
        value?.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Smart Builder safety verification failed: " + message);
    }
}
