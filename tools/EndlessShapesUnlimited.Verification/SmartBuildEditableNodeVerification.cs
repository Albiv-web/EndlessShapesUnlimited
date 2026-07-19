using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using EndlessShapes2;

internal static class SmartBuildEditableNodeVerification
{
    internal static string Run()
    {
        VerifyPatternViewportHandleGestures();
        VerifyLinearGridAndPolylineExpansion();
        VerifyRadialRoundingAndOrientationModes();
        VerifyEmbeddedPatternSourceEditing();
        VerifyPatternDissolveBakeAndDirectApplyEquivalence();
        VerifyRegionSpansAndBakeEquivalence();
        VerifyBakeCapAndNestedPatternGuard();
        VerifyCompactSceneDeltas();
        VerifyUnchangedNodeExpansionCacheSurvivesOtherMutations();
        VerifySceneControllerAndConstructToken();
        VerifyPlacementProvenanceAndDiagnostics();
        VerifyVerticalPlacementMirroring();
        return "PASS: Smart Builder editable-node verification completed.";
    }

    private static void VerifyPatternViewportHandleGestures()
    {
        Vector3i host = new Vector3i(10, 2, 3);
        var linear = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Linear,
            PrimaryStep = new Vector3i(2, 0, 0),
            PrimaryAfter = 2
        };
        Require(
            SmartBuildPatternViewportEditor.TryApplyCellHandle(
                linear,
                new SmartBuildPatternViewportHandle(
                    SmartBuildPatternViewportHandleKind.PrimaryStep),
                host,
                new Vector3i(14, 5, 3),
                out SmartBuildPatternDefinition linearEdited,
                out string reason) &&
            linearEdited.PrimaryStep.Equals(new Vector3i(4, 3, 0)) &&
            linearEdited.TryValidate(1, out _, out reason),
            "linear viewport step handle did not produce a valid craft-local step: " + reason);

        var grid = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Grid,
            PrimaryStep = new Vector3i(2, 0, 0),
            PrimaryAfter = 1,
            SecondaryStep = new Vector3i(0, 0, 2),
            SecondaryAfter = 1
        };
        Require(
            SmartBuildPatternViewportEditor.TryApplyCellHandle(
                grid,
                new SmartBuildPatternViewportHandle(
                    SmartBuildPatternViewportHandleKind.SecondaryStep),
                host,
                new Vector3i(10, 4, 8),
                out SmartBuildPatternDefinition gridEdited,
                out reason) &&
            gridEdited.SecondaryStep.Equals(new Vector3i(0, 2, 5)) &&
            gridEdited.TryValidate(1, out _, out reason),
            "grid viewport V-step handle did not remain independent: " + reason);

        var radial = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Radial,
            RadialPivot = new Vector3i(7, 2, 3),
            RadialAxis = DecorationEditAxis.Y,
            RadialAngleStepDegrees = 90f,
            RadialOrientation = SmartBuildRadialOrientationMode.RotateCardinal,
            PrimaryAfter = 2
        };
        Require(
            SmartBuildPatternViewportEditor.TryApplyCellHandle(
                radial,
                new SmartBuildPatternViewportHandle(
                    SmartBuildPatternViewportHandleKind.RadialPivot),
                host,
                new Vector3i(6, 1, 4),
                out SmartBuildPatternDefinition pivotEdited,
                out reason) &&
            pivotEdited.RadialPivot.Equals(new Vector3i(6, 1, 4)) &&
            SmartBuildPatternViewportEditor.TryApplyRadialAngle(
                pivotEdited,
                136f,
                out SmartBuildPatternDefinition cardinalAngleEdited,
                out reason) &&
            Math.Abs(cardinalAngleEdited.RadialAngleStepDegrees - 180f) <= 0.0001f,
            "radial pivot/angle viewport handles did not preserve cardinal snapping: " + reason);
        radial.RadialOrientation = SmartBuildRadialOrientationMode.Keep;
        Require(
            SmartBuildPatternViewportEditor.TryApplyRadialAngle(
                radial,
                37f,
                out SmartBuildPatternDefinition freeAngleEdited,
                out reason) &&
            Math.Abs(freeAngleEdited.RadialAngleStepDegrees - 35f) <= 0.0001f,
            "keep-orientation radial viewport angle did not use fine snapping: " + reason);

        var polyline = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Polyline,
            PathPoints = new[]
            {
                new Vector3i(0, 0, 0),
                new Vector3i(3, 0, 0),
                new Vector3i(3, 0, 3)
            }
        };
        Require(
            SmartBuildPatternViewportEditor.TryApplyCellHandle(
                polyline,
                new SmartBuildPatternViewportHandle(
                    SmartBuildPatternViewportHandleKind.PolylinePoint,
                    1),
                host,
                new Vector3i(14, 4, 3),
                out SmartBuildPatternDefinition pathEdited,
                out reason) &&
            pathEdited.PathPoints[1].Equals(new Vector3i(4, 2, 0)) &&
            pathEdited.TryValidate(1, out _, out reason) &&
            !SmartBuildPatternViewportEditor.TryApplyCellHandle(
                polyline,
                new SmartBuildPatternViewportHandle(
                    SmartBuildPatternViewportHandleKind.PolylinePoint,
                    0),
                host,
                host,
                out _,
                out _),
            "polyline viewport handles did not reshape points in the source-anchor frame: " + reason);

        int historyDeltas = 0;
        var committed = new SmartBuildPatternViewportGesture(41, linear);
        committed.Accept(linearEdited);
        Require(
            committed.TryCommit(out bool recordHistory) && recordHistory,
            "Enter-style viewport commit did not request one changed history delta");
        if (recordHistory)
            historyDeltas++;
        if (committed.TryCommit(out recordHistory) && recordHistory)
            historyDeltas++;
        Require(
            historyDeltas == 1,
            "one viewport gesture could publish more than one history delta");

        var cancelled = new SmartBuildPatternViewportGesture(42, linear);
        cancelled.Accept(linearEdited);
        Require(
            cancelled.TryCancel(out SmartBuildPatternDefinition restored) &&
            SmartBuildPatternViewportEditor.DefinitionsEqual(linear, restored) &&
            !cancelled.TryCommit(out _),
            "Escape-style viewport cancellation did not restore the exact starting parameters");

        var unchanged = new SmartBuildPatternViewportGesture(43, linear);
        Require(
            unchanged.TryCommit(out recordHistory) && !recordHistory,
            "an unchanged viewport gesture requested an empty history delta");
    }

    private static void VerifyLinearGridAndPolylineExpansion()
    {
        var linear = new SmartBuildPieceScene(null);
        SmartBuildPiece first = Unit(new Vector3i(0, 0, 0));
        SmartBuildPiece second = Unit(new Vector3i(0, 1, 0));
        Require(linear.Add(first) && linear.Add(second), "linear sources could not be added");
        linear.SetSelection(new[] { first.Id, second.Id }, second.Id);
        Require(
            linear.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(3, 0, 0),
                    PrimaryBefore = 1,
                    PrimaryAfter = 2
                },
                out SmartBuildPatternNode linearNode,
                out string linearReason),
            "linear node creation failed: " + linearReason);
        Require(
            linear.Count == 1 && linear.EditableNodeCount == 1 &&
            linear.TryExpandSceneNodes(
                out IReadOnlyList<SmartBuildPiece> linearPieces,
                out IReadOnlyDictionary<int, SmartBuildNodeProvenance> linearProvenance,
                out _,
                out linearReason) &&
            linearPieces.Count == 8 &&
            linearPieces.Select(piece => piece.Origin.x).Distinct().OrderBy(value => value)
                .SequenceEqual(new[] { -3, 0, 3, 6 }) &&
            linearProvenance.Values.Select(value => value.PatternInstanceId).Distinct().Count() == 4 &&
            linearProvenance.Values.All(value => value.NodeId == linearNode.Id),
            "linear expansion did not preserve the source group or before/after instances: " + linearReason);

        var grid = new SmartBuildPieceScene(null);
        Require(grid.Add(Unit(new Vector3i(0, 0, 0))), "grid host could not be added");
        Require(
            grid.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Grid,
                    PrimaryStep = new Vector3i(2, 0, 0),
                    SecondaryStep = new Vector3i(0, 0, 3),
                    PrimaryBefore = 1,
                    PrimaryAfter = 1,
                    SecondaryAfter = 1
                },
                out _,
                out string gridReason) &&
            grid.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> gridPieces, out _, out gridReason) &&
            gridPieces.Count == 6 &&
            gridPieces.Select(piece => piece.Origin.x).Distinct().OrderBy(value => value)
                .SequenceEqual(new[] { -2, 0, 2 }) &&
            gridPieces.Select(piece => piece.Origin.z).Distinct().OrderBy(value => value)
                .SequenceEqual(new[] { 0, 3 }),
            "grid expansion did not produce the independent U/V copy ranges: " + gridReason);

        var polyline = new SmartBuildPieceScene(null);
        SmartBuildPiece polylineHost = Unit(new Vector3i(0, 0, 0));
        Require(polyline.Add(polylineHost), "polyline host could not be added");
        Require(
            polyline.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Polyline,
                    PathPoints = new[]
                    {
                        new Vector3i(0, 0, 0),
                        new Vector3i(4, 0, 0),
                        new Vector3i(4, 0, 3)
                    },
                    PathMode = SmartBuildPathArrayMode.SteppedCells,
                    PathOrientation = SmartBuildPolylineOrientationMode.CardinalTangent,
                    PathSpacingCells = 2
                },
                out _,
                out string pathReason) &&
            polyline.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> pathPieces, out _, out pathReason) &&
            pathPieces.Count >= 3 && pathPieces.Count <= 5 &&
            pathPieces[0].Origin.Equals(new Vector3i(0, 0, 0)) &&
            pathPieces[0].ForwardAxis == polylineHost.ForwardAxis &&
            pathPieces[0].ForwardSign == polylineHost.ForwardSign &&
            pathPieces.Single(piece => piece.Origin.Equals(new Vector3i(2, 0, 0))).ForwardAxis ==
                SmartBuildAxis.X &&
            pathPieces.Single(piece => piece.Origin.Equals(new Vector3i(2, 0, 0))).ForwardSign == 1 &&
            pathPieces.Single(piece => piece.Origin.Equals(new Vector3i(4, 0, 0))).ForwardAxis ==
                SmartBuildAxis.Z &&
            pathPieces.Single(piece => piece.Origin.Equals(new Vector3i(4, 0, 0))).ForwardSign == 1 &&
            pathPieces.All(piece => piece.Origin.x >= 0 && piece.Origin.x <= 4 &&
                                    piece.Origin.z >= 0 && piece.Origin.z <= 3),
            "polyline spacing, cardinal tangent orientation, or bounded path expansion changed: " +
            pathReason);
    }

    private static void VerifyRadialRoundingAndOrientationModes()
    {
        var radial = new SmartBuildPieceScene(null);
        SmartBuildPiece host = Unit(new Vector3i(1, 0, 0));
        Require(radial.Add(host), "radial host could not be added");
        Require(
            radial.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Radial,
                    RadialPivot = new Vector3i(0, 0, 0),
                    RadialAxis = DecorationEditAxis.Y,
                    RadialAngleStepDegrees = 90f,
                    RadialOrientation = SmartBuildRadialOrientationMode.Keep,
                    PrimaryAfter = 3
                },
                out _,
                out string radialReason) &&
            radial.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> radialPieces, out _, out radialReason) &&
            radialPieces.Count == 4 &&
            radialPieces.Select(piece => Cell(piece.Origin)).Distinct().Count() == 4 &&
            radialPieces.All(piece => piece.ForwardAxis == host.ForwardAxis &&
                                      piece.ForwardSign == host.ForwardSign),
            "radial keep-orientation expansion did not rotate only craft-local positions: " + radialReason);

        var collapsed = new SmartBuildPieceScene(null);
        Require(collapsed.Add(Unit(new Vector3i(0, 0, 0))), "collapsed radial host could not be added");
        Require(
            collapsed.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Radial,
                    RadialPivot = new Vector3i(0, 0, 0),
                    RadialAxis = DecorationEditAxis.Y,
                    RadialAngleStepDegrees = 45f,
                    RadialOrientation = SmartBuildRadialOrientationMode.Keep,
                    PrimaryAfter = 7
                },
                out _,
                out string collapsedReason) &&
            collapsed.TryExpandSceneNodes(
                out IReadOnlyList<SmartBuildPiece> collapsedPieces,
                out IReadOnlyList<string> warnings,
                out collapsedReason) &&
            collapsedPieces.Count == 1 &&
            warnings.Any(warning => warning.IndexOf("collapsed", StringComparison.OrdinalIgnoreCase) >= 0),
            "radial rounding did not suppress and report collapsed instances: " + collapsedReason);

        var invalidOrientation = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Radial,
            RadialAxis = DecorationEditAxis.Y,
            RadialAngleStepDegrees = 45f,
            RadialOrientation = SmartBuildRadialOrientationMode.RotateCardinal,
            PrimaryAfter = 1
        };
        Require(
            !invalidOrientation.TryValidate(1, out _, out string invalidReason) &&
            invalidReason.IndexOf("90-degree", StringComparison.OrdinalIgnoreCase) >= 0,
            "orientation-follow radial mode accepted a non-quarter-turn step");
    }

    private static void VerifyEmbeddedPatternSourceEditing()
    {
        var scene = new SmartBuildPieceScene(null);
        SmartBuildPiece first = Unit(new Vector3i(0, 0, 0));
        SmartBuildPiece host = Unit(new Vector3i(0, 1, 0));
        Require(scene.Add(first) && scene.Add(host), "source-edit fixtures could not be added");
        scene.SetSelection(new[] { first.Id, host.Id }, host.Id);
        Require(
            scene.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(3, 0, 0),
                    PrimaryAfter = 1
                },
                out SmartBuildPatternNode pattern,
                out string reason),
            "source-edit pattern could not be created: " + reason);

        IReadOnlyList<SmartBuildPiece> sources = pattern.SourcePieces;
        int firstIndex = sources.ToList().FindIndex(piece => piece.Id == first.Id);
        SmartBuildPiece editedFirst = sources[firstIndex].Clone();
        editedFirst.SetMaterialOverride(SmartBuildMaterial.Metal);
        Require(
            editedFirst.TryConvertTo(
                SmartBuildShapeDescriptors.ByKey("wedge"),
                1,
                out reason) &&
            scene.TryUpdateSelectedPatternSource(firstIndex, editedFirst, out reason) &&
            scene.TryExpandSceneNodes(
                out IReadOnlyList<SmartBuildPiece> expanded,
                out _,
                out reason) &&
            expanded.Count(piece => piece.ShapeKind == SmartBuildShapeKind.Wedge) == 2 &&
            expanded.Where(piece => piece.ShapeKind == SmartBuildShapeKind.Wedge)
                .All(piece => piece.MaterialOverride == SmartBuildMaterial.Metal),
            "Edit Source did not update the embedded primitive shape/material across pattern instances: " + reason);

        int hostIndex = pattern.SourcePieces.ToList().FindIndex(piece => piece.Id == host.Id);
        SmartBuildPiece editedHost = pattern.SourcePieces[hostIndex].Clone();
        editedHost.SetMaterialOverride(SmartBuildMaterial.Glass);
        Require(
            scene.TryUpdateSelectedPatternSource(hostIndex, editedHost, out reason) &&
            pattern.HostPiece.MaterialOverride == SmartBuildMaterial.Glass,
            "editing the embedded primary source did not synchronize the visible host: " + reason);

        SmartBuildPiece wrongIdentity = Unit(new Vector3i(99, 99, 99));
        SmartBuildSceneState beforeInvalid = scene.CaptureState();
        Require(
            !scene.TryUpdateSelectedPatternSource(firstIndex, wrongIdentity, out reason) &&
            !SmartBuildSceneStateDelta.Create(beforeInvalid, scene.CaptureState()).HasSceneChanges,
            "an invalid embedded-source edit changed the pattern instead of failing atomically");
    }

    private static void VerifyPatternDissolveBakeAndDirectApplyEquivalence()
    {
        SmartBuildPieceScene direct = LinearScene(copyAfter: 3, step: 2);
        SmartBuildSource source = TestSource();
        SmartBuildPlan directPlan = Plan(direct, source);
        Require(directPlan.CanCommit, "direct editable-pattern plan failed: " + directPlan.FailureReason);
        string[] directCells = PlanCells(directPlan);
        Require(
            direct.TryBakeSelectedNode(out IReadOnlyList<SmartBuildPiece> baked, out string bakeReason) &&
            baked.Count == 4 && direct.Count == 4 && direct.EditableNodeCount == 0,
            "pattern Bake did not atomically create independent primitive nodes: " + bakeReason);
        SmartBuildPlan bakedPlan = Plan(direct, source);
        Require(
            bakedPlan.CanCommit && directCells.SequenceEqual(PlanCells(bakedPlan)),
            "direct Apply and baked-pattern placement footprints differ");

        SmartBuildPieceScene dissolve = LinearScene(copyAfter: 4, step: 3);
        Vector3i sourceOrigin = dissolve.SelectedNode.SourcePieces.Single().Origin;
        Require(
            dissolve.TryDissolveSelectedNode(
                out IReadOnlyList<SmartBuildPiece> sources,
                out string dissolveReason) &&
            sources.Count == 1 && dissolve.Count == 1 && dissolve.EditableNodeCount == 0 &&
            dissolve.Pieces.Single().Origin.Equals(sourceOrigin),
            "Dissolve did not restore only the original source group: " + dissolveReason);
    }

    private static void VerifyRegionSpansAndBakeEquivalence()
    {
        var scene = new SmartBuildPieceScene(null);
        SmartBuildPiece host = Unit(new Vector3i(10, 2, 3));
        Require(scene.Add(host), "region host could not be added");
        Vector3i[] cells =
        {
            new Vector3i(10, 2, 3),
            new Vector3i(11, 2, 3),
            new Vector3i(12, 2, 3),
            new Vector3i(10, 3, 3),
            new Vector3i(11, 3, 3),
            new Vector3i(20, 2, 4)
        };
        Require(
            scene.TryCreateRegionFromSelection(
                SmartBuildRegionKind.Brush,
                cells,
                out SmartBuildRegionNode region,
                out string regionReason) &&
            region.Spans.Count == 3 &&
            region.Spans.Sum(span => span.Length) == cells.Length &&
            scene.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> expanded, out _, out regionReason) &&
            new HashSet<string>(expanded.Select(piece => Cell(piece.Origin)))
                .SetEquals(cells.Select(Cell)),
            "region run-length spans did not preserve the exact cell mask: " + regionReason);

        SmartBuildSource source = TestSource();
        SmartBuildPlan direct = Plan(scene, source);
        string[] directCells = PlanCells(direct);
        string bakeReason = null;
        Require(
            direct.CanCommit &&
            scene.TryBakeSelectedNode(out IReadOnlyList<SmartBuildPiece> baked, out bakeReason) &&
            baked.Count == cells.Length &&
            Plan(scene, source).CanCommit &&
            directCells.SequenceEqual(PlanCells(Plan(scene, source))),
            "region direct Apply and Bake footprints differ: " + bakeReason);
    }

    private static void VerifyBakeCapAndNestedPatternGuard()
    {
        SmartBuildPieceScene oversized = LinearScene(copyAfter: 512, step: 2);
        string bakeReason = null;
        Require(
            oversized.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> expanded, out _, out string expandReason) &&
            expanded.Count == 513 &&
            !oversized.TryBakeSelectedNode(out IReadOnlyList<SmartBuildPiece> baked, out bakeReason) &&
            baked.Count == 0 && oversized.Count == 1 && oversized.EditableNodeCount == 1 &&
            (bakeReason.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
             bakeReason.IndexOf("exceed", StringComparison.OrdinalIgnoreCase) >= 0 ||
             bakeReason.IndexOf("more than", StringComparison.OrdinalIgnoreCase) >= 0),
            "over-cap Bake was not rejected atomically: " + expandReason + " " + bakeReason);

        Require(
            !oversized.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(0, 1, 0),
                    PrimaryAfter = 1
                },
                out _,
                out string nestedReason) &&
            nestedReason.IndexOf("Nested", StringComparison.OrdinalIgnoreCase) >= 0,
            "nested pattern creation was not rejected with an actionable message");
    }

    private static void VerifyCompactSceneDeltas()
    {
        var scene = new SmartBuildPieceScene(null);
        for (int index = 0; index < 100; index++)
            Require(scene.Add(Unit(new Vector3i(index * 2, 0, 0))), "delta fixture fill failed");
        SmartBuildSceneState before = scene.CaptureState();
        scene.Pieces[40].MoveBy(new Vector3i(0, 3, 0));
        scene.MarkGeometryChanged();
        SmartBuildSceneState after = scene.CaptureState();
        SmartBuildSceneStateDelta delta = SmartBuildSceneStateDelta.Create(before, after);
        Require(
            delta.BeforeNodes.Count == 1 && delta.AfterNodes.Count == 1 &&
            delta.HasSceneChanges &&
            after.Apply(delta, forward: false).Nodes[40].HostPiece.Origin.Equals(
                before.Nodes[40].HostPiece.Origin) &&
            before.Apply(delta, forward: true).Nodes[40].HostPiece.Origin.Equals(
                after.Nodes[40].HostPiece.Origin),
            "scene history delta retained more than the single changed node or failed round-trip");

        SmartBuildSceneState selectionBefore = scene.CaptureState();
        scene.SetSelection(new[] { scene.Pieces[1].Id, scene.Pieces[2].Id }, scene.Pieces[2].Id);
        SmartBuildSceneStateDelta selectionDelta = SmartBuildSceneStateDelta.Create(
            selectionBefore,
            scene.CaptureState());
        Require(
            selectionDelta.BeforeNodes.Count == 0 && selectionDelta.AfterNodes.Count == 0 &&
            selectionDelta.HasSceneChanges,
            "selection-only history stored geometry clones instead of selection state");
    }

    private static void VerifyUnchangedNodeExpansionCacheSurvivesOtherMutations()
    {
        var scene = new SmartBuildPieceScene(null);
        SmartBuildPiece firstHost = Unit(new Vector3i(0, 0, 0));
        Require(scene.Add(firstHost), "first cache fixture host could not be added");
        Require(
            scene.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(2, 0, 0),
                    PrimaryAfter = 1
                },
                out _,
                out string firstReason),
            "first cache fixture pattern failed: " + firstReason);

        SmartBuildPiece secondHost = Unit(new Vector3i(0, 10, 0));
        Require(scene.Add(secondHost), "second cache fixture host could not be added");
        Require(
            scene.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(3, 0, 0),
                    PrimaryAfter = 1
                },
                out SmartBuildPatternNode secondPattern,
                out string secondReason),
            "second cache fixture pattern failed: " + secondReason);
        Require(
            scene.TryExpandSceneNodes(out IReadOnlyList<SmartBuildPiece> before, out _, out string expandReason),
            "initial cached-node expansion failed: " + expandReason);
        SmartBuildPiece cachedFirstInstance = before.First(piece => piece.Origin.Equals(new Vector3i(0, 0, 0)));

        SmartBuildPatternDefinition changed = secondPattern.Definition;
        changed.PrimaryAfter = 2;
        IReadOnlyList<SmartBuildPiece> after = Array.Empty<SmartBuildPiece>();
        Require(
            scene.TryUpdateSelectedPattern(changed, out string updateReason) &&
            scene.TryExpandSceneNodes(out after, out _, out expandReason),
            "mutating the second cached node failed: " + updateReason + " " + expandReason);
        SmartBuildPiece reusedFirstInstance = after.First(piece => piece.Origin.Equals(new Vector3i(0, 0, 0)));
        Require(
            ReferenceEquals(cachedFirstInstance, reusedFirstInstance) &&
            after.Count == 5,
            "changing one editable node discarded or re-enumerated an unchanged node expansion cache");
    }

    private static void VerifyPlacementProvenanceAndDiagnostics()
    {
        SmartBuildPieceScene scene = LinearScene(copyAfter: 2, step: 2);
        int nodeId = scene.SelectedNode.Id;
        SmartBuildPlan plan = Plan(scene, TestSource());
        Require(
            plan.CanCommit && plan.Placements.Count == 3 &&
            plan.Placements.All(placement => placement.NodeId == nodeId) &&
            plan.Placements.Select(placement => placement.PatternInstanceId).Distinct().Count() == 3 &&
            plan.Diagnostics.Where(item => item.State == SmartBuildCellDiagnosticState.Valid)
                .All(item => item.NodeId == nodeId && item.PatternInstanceId >= 0),
            "plan placements and per-cell diagnostics lost node/instance provenance");
    }

    private static void VerifySceneControllerAndConstructToken()
    {
        AllConstruct construct = (AllConstruct)FormatterServices.GetUninitializedObject(
            typeof(TelescopicPistonSubConstructable));
        var scene = new SmartBuildPieceScene(construct);
        SmartBuildPiece piece = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(4, 5, 6),
            SmartBuildDrawPlane.Camera);
        Require(scene.Add(piece), "scene-controller fixture could not be added");
        var controller = new SmartBuildSceneController(scene);
        bool rejected = !controller.TryMutate(
            candidate =>
            {
                candidate.SelectedPiece.MoveBy(new Vector3i(10, 0, 0));
                candidate.MarkGeometryChanged();
                return false;
            },
            out SmartBuildSceneStateDelta rejectedDelta,
            out string rejectedReason);
        Require(
            rejected && rejectedDelta == null &&
            controller.Scene.SelectedPiece.Origin.Equals(new Vector3i(4, 5, 6)) &&
            rejectedReason.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0,
            "scene controller did not restore a rejected mutation atomically");

        Require(
            controller.TryMutate(
                candidate =>
                {
                    candidate.SelectedPiece.MoveBy(new Vector3i(0, 2, 0));
                    candidate.MarkGeometryChanged();
                    return true;
                },
                out SmartBuildSceneStateDelta acceptedDelta,
                out string acceptedReason) &&
            acceptedDelta?.AfterNodes.Count == 1 && controller.Revision == 1 &&
            controller.Scene.SelectedPiece.Origin.Equals(new Vector3i(4, 7, 6)) &&
            controller.TryApplyDelta(acceptedDelta, forward: false, out acceptedReason) &&
            controller.Scene.SelectedPiece.Origin.Equals(new Vector3i(4, 5, 6)),
            "scene controller did not publish and reverse one atomic node delta: " + acceptedReason);

        SmartBuildConstructToken token = SmartBuildConstructToken.Capture(
            construct,
            occupancyRevision: 7);
        AllConstruct other = (AllConstruct)FormatterServices.GetUninitializedObject(
            typeof(TelescopicPistonSubConstructable));
        Require(
            token.OccupancyRevision == 7 &&
            token.Matches(construct, construct, out string tokenReason) &&
            !token.Matches(construct, other, out tokenReason),
            "construct token did not bind the exact construct/root identity");
    }

    private static void VerifyVerticalPlacementMirroring()
    {
        SmartBlockCandidate candidate = SmartBlockCandidate.ForTests(1);
        var original = new SmartBuildPlacement(
            new Vector3i(2, 3, 4),
            candidate,
            SmartBuildAxis.Z,
            1,
            SmartBuildPlacement.RotationForAxis(SmartBuildAxis.Z),
            new[] { new Vector3i(2, 3, 4), new Vector3i(2, 2, 5) },
            "down slope");
        DecoLimitLifter.EsuSymmetry.Clear();
        DecoLimitLifter.EsuSymmetry.SetPlaneForTests(DecorationEditAxis.Y, 0);
        var vertical = new DecoLimitLifter.EsuSymmetry.SymmetryVariant(
            new[] { DecorationEditAxis.Y });
        SmartBuildPlacement mirrored = SmartBuildPieceScene.MirrorPlacement(
            original,
            vertical,
            source: null);
        Require(
            mirrored != null &&
            mirrored.Position.Equals(vertical.Mirror(original.Position)) &&
            new HashSet<string>(mirrored.CoveredCells().Select(Cell))
                .SetEquals(original.CoveredCells().Select(vertical.Mirror).Select(Cell)) &&
            Approximately(
                mirrored.Rotation * UnityEngine.Vector3.forward,
                MirrorY(original.Rotation * UnityEngine.Vector3.forward)) &&
            Approximately(
                mirrored.Rotation * UnityEngine.Vector3.up,
                MirrorY(original.Rotation * UnityEngine.Vector3.up)),
            "vertical symmetry did not remap placement anchor, footprint, and orientation");
        DecoLimitLifter.EsuSymmetry.Clear();
    }

    private static SmartBuildPieceScene LinearScene(int copyAfter, int step)
    {
        var scene = new SmartBuildPieceScene(null);
        Require(scene.Add(Unit(new Vector3i(0, 0, 0))), "linear fixture host could not be added");
        Require(
            scene.TryCreatePatternFromSelection(
                new SmartBuildPatternDefinition
                {
                    Kind = SmartBuildEditablePatternKind.Linear,
                    PrimaryStep = new Vector3i(step, 0, 0),
                    PrimaryAfter = copyAfter
                },
                out _,
                out string reason),
            "linear fixture pattern could not be created: " + reason);
        return scene;
    }

    private static SmartBuildPiece Unit(Vector3i origin) =>
        SmartBuildPiece.CreateCuboid(null, origin, SmartBuildDrawPlane.Camera);

    private static SmartBuildSource TestSource()
    {
        SmartBlockFamily family = SmartBlockFamily.ForTests(1);
        return new SmartBuildSource(
            null,
            Guid.Empty,
            "test",
            new Vector3i(1, 1, 1),
            family,
            family);
    }

    private static SmartBuildPlan Plan(SmartBuildPieceScene scene, SmartBuildSource source) =>
        scene.BuildPlanWithSources(
            _ => source,
            _ => false,
            new SmartBuildPlannerOptions
            {
                AllowNullConstructForVerification = true
            },
            out _);

    private static string[] PlanCells(SmartBuildPlan plan) =>
        plan.Placements
            .SelectMany(placement => placement.CoveredCells())
            .Select(Cell)
            .Distinct()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static string Cell(Vector3i cell) =>
        cell.x + ":" + cell.y + ":" + cell.z;

    private static UnityEngine.Vector3 MirrorY(UnityEngine.Vector3 value) =>
        new UnityEngine.Vector3(value.x, -value.y, value.z);

    private static bool Approximately(UnityEngine.Vector3 left, UnityEngine.Vector3 right) =>
        UnityEngine.Vector3.Distance(left.normalized, right.normalized) <= 0.0001f;

    private static void Require(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidOperationException(
                "Smart Builder editable-node verification failed: " + reason);
    }
}
