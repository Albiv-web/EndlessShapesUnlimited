using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.Presets;
using DecoLimitLifter.SmartBuildMode;
using Newtonsoft.Json;
using UnityEngine;

internal static partial class Program
{
    private static void VerifySmartBuildAdvancedFeatures()
    {
        VerifySmartGroupSelectionAndArrays();
        VerifySmartSceneCapAndAtomicGroupConversion();
        VerifySmartGroupSpatialScalingAndCardinalConversion();
        VerifySmartFillConversionAndMaterials();
        VerifySmartAdvancedGenerators();
        VerifySmartCraftBlockSamplingAndBulkPredicates();
        VerifySmartScenePresetRoundTrip();
        VerifySurfacePresetTargetRelocation();
        VerifySmartDestructivePlanSemantics();
        VerifySmartAdvancedRuntimeWiring();
    }

    private static void VerifySmartGroupSpatialScalingAndCardinalConversion()
    {
        SmartBuildPiece primary = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(2, 2, 2),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece offset = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(4, 0, 0),
            new Vector3i(2, 2, 2),
            SmartBuildDrawPlane.Camera);
        Vector3 pivot = primary.GroupScaleBounds.Center;
        bool primaryScaled = primary.TryScaleAboutPivot(
            pivot,
            new Vector3(2f, 1f, 1f),
            out string primaryScaleReason);
        bool offsetScaled = offset.TryScaleAboutPivot(
            pivot,
            new Vector3(2f, 1f, 1f),
            out string offsetScaleReason);
        Assert(
            primaryScaled && offsetScaled &&
            primary.GroupScaleBounds.Min.Equals(new Vector3i(-1, 0, 0)) &&
            primary.GroupScaleBounds.Max.Equals(new Vector3i(2, 1, 1)) &&
            offset.GroupScaleBounds.Min.Equals(new Vector3i(7, 0, 0)) &&
            offset.GroupScaleBounds.Max.Equals(new Vector3i(10, 1, 1)) &&
            VectorApproximately(primary.GroupScaleBounds.Center, pivot, 0.00001f) &&
            VectorApproximately(offset.GroupScaleBounds.Center, new Vector3(8.5f, 0.5f, 0.5f), 0.00001f),
            "Smart Builder group scaling transforms both piece extents and inter-piece offsets exactly about the primary pivot. " +
            primaryScaleReason + " " + offsetScaleReason);

        SmartBuildPiece convertible = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(10, 20, 30),
            new Vector3i(2, 3, 4),
            SmartBuildDrawPlane.Camera);
        convertible.SetMaterialOverride(SmartBuildMaterial.Alloy);
        bool oriented = convertible.TrySetOrientationFromRotation(
            new Quaternion(0f, 0f, 0.70710677f, 0.70710677f));
        SmartBuildBounds expectedBounds = convertible.Bounds;
        SmartBuildAxis expectedForward = convertible.ForwardAxis;
        int expectedForwardSign = convertible.ForwardSign;
        SmartBuildAxis expectedRight = convertible.RightAxis;
        int expectedRightSign = convertible.RightSign;
        SmartBuildAxis expectedDrop = convertible.DropAxis;
        int expectedDropSign = convertible.DropSign;

        string[] structuralTargets =
        {
            SmartBuildShapeDescriptors.DownSlopeKey,
            "wedge",
            "triangle-corner-left"
        };
        var conversionMessages = new List<string>();
        bool allConversionsExact = oriented;
        foreach (string targetKey in structuralTargets)
        {
            SmartBuildShapeDescriptor target = SmartBuildShapeDescriptors.ByKey(targetKey);
            bool converted = convertible.TryConvertTo(target, selectedLength: 2, out string toReason);
            allConversionsExact &= converted &&
                                   SameSmartBounds(convertible.Bounds, expectedBounds) &&
                                   SameSmartBasis(
                                       convertible,
                                       expectedForward,
                                       expectedForwardSign,
                                       expectedRight,
                                       expectedRightSign,
                                       expectedDrop,
                                       expectedDropSign) &&
                                   convertible.MaterialOverride == SmartBuildMaterial.Alloy;
            conversionMessages.Add(toReason);

            bool returned = convertible.TryConvertTo(
                SmartBuildShapeDescriptors.Cuboid,
                selectedLength: 1,
                out string fromReason);
            allConversionsExact &= returned &&
                                   convertible.ShapeKind == SmartBuildShapeKind.Cuboid &&
                                   SameSmartBounds(convertible.Bounds, expectedBounds) &&
                                   SameSmartBasis(
                                       convertible,
                                       expectedForward,
                                       expectedForwardSign,
                                       expectedRight,
                                       expectedRightSign,
                                       expectedDrop,
                                       expectedDropSign) &&
                                   convertible.MaterialOverride == SmartBuildMaterial.Alloy;
            conversionMessages.Add(fromReason);
        }

        SmartBuildPiece incompatible = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(2, 3, 3),
            SmartBuildDrawPlane.Camera);
        incompatible.TrySetOrientationFromRotation(
            new Quaternion(0f, 0f, 0.70710677f, 0.70710677f));
        SmartBuildBounds incompatibleBefore = incompatible.Bounds;
        bool rejectedLossySlope = !incompatible.TryConvertTo(
            SmartBuildShapeDescriptors.ByKey(SmartBuildShapeDescriptors.DownSlopeKey),
            selectedLength: 2,
            out string incompatibleReason) &&
            incompatible.ShapeKind == SmartBuildShapeKind.Cuboid &&
            SameSmartBounds(incompatible.Bounds, incompatibleBefore);

        Assert(
            allConversionsExact && rejectedLossySlope,
            "Block <-> Slope/Wedge/Corner conversions preserve exact bounds, full cardinal basis, and per-piece material; unrepresentable lossy conversions are rejected atomically. " +
            string.Join(" ", conversionMessages.Where(message => !string.IsNullOrWhiteSpace(message))) + " " +
            incompatibleReason);
    }

    private static bool SameSmartBounds(SmartBuildBounds first, SmartBuildBounds second) =>
        first.Min.Equals(second.Min) && first.Max.Equals(second.Max);

    private static bool SameSmartBasis(
        SmartBuildPiece piece,
        SmartBuildAxis forward,
        int forwardSign,
        SmartBuildAxis right,
        int rightSign,
        SmartBuildAxis drop,
        int dropSign) =>
        piece.ForwardAxis == forward && piece.ForwardSign == forwardSign &&
        piece.RightAxis == right && piece.RightSign == rightSign &&
        piece.DropAxis == drop && piece.DropSign == dropSign;

    private static void VerifySmartGroupSelectionAndArrays()
    {
        var scene = new SmartBuildPieceScene(null);
        SmartBuildPiece first = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(2, 2, 2),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece second = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(5, 0, 0),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece third = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(10, 0, 0),
            SmartBuildDrawPlane.Camera);
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);
        scene.SetSelection(new[] { first.Id, second.Id }, second.Id);
        IReadOnlyList<SmartBuildPiece> copies = scene.DuplicateSelectionArray(
            new Vector3i(0, 0, 4),
            copyCount: 2);

        Assert(
            copies.Count == 4 &&
            scene.Count == 7 &&
            scene.SelectionCount == 6 &&
            scene.SelectedPieces.Select(piece => piece.Origin.z).OrderBy(value => value)
                .SequenceEqual(new[] { 0, 0, 4, 4, 8, 8 }),
            "Smart Builder multi-selection arrays duplicate the complete selected group across bounded layers and retain one group selection.");

        int deleted = scene.DeleteSelection();
        Assert(
            deleted == 6 && scene.Count == 1 && ReferenceEquals(scene.SelectedPiece, third),
            "Smart Builder group deletion removes the selected set atomically and keeps a deterministic live fallback selection.");
    }

    private static void VerifySmartSceneCapAndAtomicGroupConversion()
    {
        var capped = new SmartBuildPieceScene(null);
        bool filledExactly = true;
        for (int index = 0; index < SmartBuildPieceScene.MaximumScenePieces; index++)
        {
            filledExactly &= capped.Add(SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(index * 2, 0, 0),
                SmartBuildDrawPlane.Camera));
        }

        int cappedPrimary = capped.SelectedPiece.Id;
        bool addRejected = !capped.Add(SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(2000, 0, 0),
            SmartBuildDrawPlane.Camera));
        bool duplicateRejected = !capped.TryDuplicateSelection(
            new Vector3i(0, 1, 0),
            out IReadOnlyList<SmartBuildPiece> cappedCopies,
            out string duplicateReason);
        Assert(
            filledExactly && addRejected && duplicateRejected && cappedCopies.Count == 0 &&
            capped.Count == SmartBuildPieceScene.MaximumScenePieces &&
            capped.SelectedPiece.Id == cappedPrimary &&
            duplicateReason.IndexOf("scene cap", StringComparison.OrdinalIgnoreCase) >= 0,
            "Smart Builder Add and group Duplicate enforce the central 512-piece cap without changing count or selection on failure.");

        var almostFull = new SmartBuildPieceScene(null);
        var tail = new List<int>();
        for (int index = 0; index < SmartBuildPieceScene.MaximumScenePieces - 2; index++)
        {
            SmartBuildPiece piece = SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(index * 2, 10, 0),
                SmartBuildDrawPlane.Camera);
            almostFull.Add(piece);
            if (index >= SmartBuildPieceScene.MaximumScenePieces - 4)
                tail.Add(piece.Id);
        }
        almostFull.SetSelection(tail, tail[tail.Count - 1]);
        int[] selectionBefore = almostFull.SelectedPieceIds.ToArray();
        bool wholeArrayRejected = !almostFull.TryDuplicateSelectionArray(
            new Vector3i(0, 0, 2),
            copyCount: 2,
            out IReadOnlyList<SmartBuildPiece> arrayCopies,
            out string arrayReason);
        Assert(
            wholeArrayRejected && arrayCopies.Count == 0 &&
            almostFull.Count == SmartBuildPieceScene.MaximumScenePieces - 2 &&
            almostFull.SelectedPieceIds.SequenceEqual(selectionBefore) &&
            arrayReason.IndexOf("scene cap", StringComparison.OrdinalIgnoreCase) >= 0,
            "Basic arrays reject the complete requested copy count instead of silently truncating to the remaining capacity.");

        var conversionScene = new SmartBuildPieceScene(null);
        SmartBuildPiece convertible = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(2, 3, 4),
            SmartBuildDrawPlane.Camera);
        convertible.TrySetOrientationFromRotation(
            new Quaternion(0f, 0f, 0.70710677f, 0.70710677f));
        SmartBuildPiece incompatible = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(10, 0, 0),
            new Vector3i(2, 3, 3),
            SmartBuildDrawPlane.Camera);
        incompatible.TrySetOrientationFromRotation(
            new Quaternion(0f, 0f, 0.70710677f, 0.70710677f));
        conversionScene.Add(convertible);
        conversionScene.Add(incompatible);
        conversionScene.SetSelection(new[] { convertible.Id, incompatible.Id }, incompatible.Id);
        SmartBuildPiece convertibleBefore = convertible.Clone();
        SmartBuildPiece incompatibleBefore = incompatible.Clone();
        bool conversionRejected = !conversionScene.TryConvertSelectionTo(
            SmartBuildShapeDescriptors.ByKey(SmartBuildShapeDescriptors.DownSlopeKey),
            selectedLength: 2,
            out int convertedCount,
            out string conversionReason);
        Assert(
            conversionRejected && convertedCount == 0 &&
            convertible.ShapeKind == convertibleBefore.ShapeKind &&
            incompatible.ShapeKind == incompatibleBefore.ShapeKind &&
            SameSmartBounds(convertible.Bounds, convertibleBefore.Bounds) &&
            SameSmartBounds(incompatible.Bounds, incompatibleBefore.Bounds),
            "Group shape conversion stages every selected piece and leaves the whole group unchanged when any member is unrepresentable. " +
            conversionReason);
    }

    private static void VerifySmartFillConversionAndMaterials()
    {
        SmartBuildPiece cuboid = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(4, 4, 4),
            SmartBuildDrawPlane.Camera);
        int solidCells = cuboid.EnumeratePreviewCells().Count();
        cuboid.SetCuboidHollow(true);
        int hollowCells = cuboid.EnumeratePreviewCells().Count();
        cuboid.SetMaterialOverride(SmartBuildMaterial.Alloy);
        Vector3 centerBefore = cuboid.Bounds.Center;
        bool converted = cuboid.TryConvertTo(
            SmartBuildShapeDescriptors.ByKey("generated-tube"),
            selectedLength: 1,
            out string conversionReason);
        Vector3 centerAfter = cuboid.Bounds.Center;

        Assert(
            solidCells == 64 && hollowCells == 56 && converted &&
            cuboid.ShapeKind == SmartBuildShapeKind.GeneratedTube &&
            cuboid.MaterialOverride == SmartBuildMaterial.Alloy &&
            Vector3.Distance(centerBefore, centerAfter) <= 0.9f,
            "Smart Builder supports solid/hollow cuboids and shape conversion while preserving material and approximate center. " +
            conversionReason);

        AllConstruct construct = NewConstructIdentityForTests();
        var mixedScene = new SmartBuildPieceScene(construct);
        SmartBuildPiece wood = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(0, 0, 0),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece metal = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(4, 0, 0),
            SmartBuildDrawPlane.Camera);
        wood.SetMaterialOverride(SmartBuildMaterial.Wood);
        metal.SetMaterialOverride(SmartBuildMaterial.Metal);
        mixedScene.Add(wood);
        mixedScene.Add(metal);
        SmartBuildSource woodSource = SmartTestSource("wood fixture");
        SmartBuildSource metalSource = SmartTestSource("metal fixture");
        SmartBuildPlan mixedPlan = mixedScene.BuildPlanWithSources(
            piece => piece.MaterialOverride == SmartBuildMaterial.Metal ? metalSource : woodSource,
            _ => false,
            new SmartBuildPlannerOptions(),
            out SmartBuildPreviewSnapshot mixedPreview);
        Assert(
            mixedPlan.CanCommit && mixedPlan.Placements.Count == 2 && mixedPreview.Cells.Count == 2 &&
            mixedPlan.Placements.Select(placement => placement.Candidate.DisplayName)
                .OrderBy(value => value)
                .SequenceEqual(new[] { "metal fixture 1m", "wood fixture 1m" }),
            "Smart Builder plans one scene through per-piece material sources without flattening the group material.");
    }

    private static void VerifySmartAdvancedGenerators()
    {
        SmartBuildPiece arc = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(0, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-arc"),
            SmartBuildAxis.Z,
            1,
            width: 9,
            drawPlane: SmartBuildDrawPlane.Camera);
        arc.SetGeneratorArcDegrees(90);
        int quarterArcCells = arc.EnumeratePreviewCells().Count();
        arc.SetGeneratorArcDegrees(360);
        int fullArcCells = arc.EnumeratePreviewCells().Count();

        SmartBuildPiece tube = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(20, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-tube"),
            SmartBuildAxis.Z,
            1,
            width: 9,
            drawPlane: SmartBuildDrawPlane.Camera);
        int tubeShellCells = tube.EnumeratePreviewCells().Count();
        tube.SetGeneratorFillMode(SmartBuildGeneratorFillMode.Filled);
        int tubeFilledCells = tube.EnumeratePreviewCells().Count();

        SmartBuildPiece cone = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(40, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-cone"),
            SmartBuildAxis.Z,
            1,
            width: 9,
            drawPlane: SmartBuildDrawPlane.Camera);
        Vector3i[] coneCells = cone.EnumeratePreviewCells().ToArray();

        SmartBuildPiece frustum = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(60, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-frustum"),
            SmartBuildAxis.Z,
            1,
            width: 9,
            drawPlane: SmartBuildDrawPlane.Camera);
        frustum.SetGeneratorTopScalePercent(25);
        int narrowTopCells = frustum.EnumeratePreviewCells().Count();
        frustum.SetGeneratorTopScalePercent(75);
        int wideTopCells = frustum.EnumeratePreviewCells().Count();

        SmartBuildPiece oriented = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(80, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-tube"),
            SmartBuildAxis.Z,
            1,
            width: 7,
            drawPlane: SmartBuildDrawPlane.Camera);
        bool orientationCopied = oriented.TrySetOrientationFromRotation(
            SmartBuildPlacement.RotationForAxis(SmartBuildAxis.X, 1));

        Assert(
            quarterArcCells > 0 && fullArcCells > quarterArcCells &&
            tubeShellCells > 0 && tubeFilledCells > tubeShellCells &&
            coneCells.Length > 0 && coneCells.Select(cell => cell.y).Distinct().Count() >= 5 &&
            wideTopCells > narrowTopCells &&
            orientationCopied && oriented.ForwardAxis == SmartBuildAxis.X && oriented.ForwardSign == 1,
            "Smart Builder generates adjustable arcs, shell/filled tubes, layered cones/frustums, and accepts sampled cardinal block rotation.");
    }

    private static void VerifySmartCraftBlockSamplingAndBulkPredicates()
    {
        Guid sourceGuid = new Guid("f13ae2f0-7499-4b40-b57f-bf11481ab427");
        bool sampledTwo = SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            Guid.NewGuid(),
            SmartBuildMaterial.Wood,
            SmartBlockCandidate.ForTests(2),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            Quaternion.identity,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor acceptedTwo,
            out _);
        Quaternion cardinalX90 = new Quaternion(0.70710677f, 0f, 0f, 0.70710677f);
        bool sampledThree = SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            Guid.NewGuid(),
            SmartBuildMaterial.Metal,
            SmartBlockCandidate.ForTests(3),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            cardinalX90,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor acceptedThree,
            out _);
        Quaternion cardinalY90 = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);
        bool sampled = SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            sourceGuid,
            SmartBuildMaterial.Alloy,
            SmartBlockCandidate.ForTests(4),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            cardinalY90,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor accepted,
            out string sampleReason);
        SmartBuildPiece sampledBeam = sampled
            ? SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(10, 20, 30),
                accepted.OccupiedSize,
                SmartBuildDrawPlane.Camera)
            : null;
        bool beamOriented = sampledBeam?.TrySetOrientationFromRotation(accepted.LocalRotation) == true;
        Quaternion cardinalNegativeY90 = new Quaternion(0f, -0.70710677f, 0f, 0.70710677f);
        bool sampledNegative = SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            Guid.NewGuid(),
            SmartBuildMaterial.Metal,
            SmartBlockCandidate.ForTests(4),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            cardinalNegativeY90,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor acceptedNegative,
            out string negativeReason);
        Vector3i sampledAnchor = new Vector3i(10, 20, 30);
        SmartBuildPiece negativeBeam = sampledNegative
            ? SmartBuildPiece.CreateCuboid(
                null,
                sampledAnchor + acceptedNegative.OccupiedOriginOffset,
                acceptedNegative.OccupiedSize,
                SmartBuildDrawPlane.Camera)
            : null;
        bool negativeOriented = negativeBeam?.TrySetOrientationFromRotation(
            acceptedNegative.LocalRotation) == true;
        Vector3i[] negativeCells = negativeBeam?.EnumeratePreviewCells().ToArray() ??
                                   Array.Empty<Vector3i>();
        SmartBuildVolume negativeVolume = negativeCells.Length > 0
            ? SmartBuildVolume.FromBounds(
                null,
                new Vector3i(
                    negativeCells.Min(cell => cell.x),
                    negativeCells.Min(cell => cell.y),
                    negativeCells.Min(cell => cell.z)),
                new Vector3i(
                    negativeCells.Max(cell => cell.x),
                    negativeCells.Max(cell => cell.y),
                    negativeCells.Max(cell => cell.z)))
            : null;
        SmartBuildPlan negativePlan = negativeVolume == null
            ? null
            : SmartBuildPlanner.BuildPlanFromCells(
                negativeVolume,
                negativeCells,
                acceptedNegative.ForwardAxis,
                acceptedNegative.ForwardSign,
                SmartBlockFamily.ForTests(1, 2, 3, 4),
                _ => false);
        var negativeScene = new SmartBuildPieceScene(null);
        if (negativeBeam != null)
            negativeScene.Add(negativeBeam);
        var negativeFamily = SmartBlockFamily.ForTests(1, 2, 3, 4);
        var negativeSource = new SmartBuildSource(
            null,
            Guid.Empty,
            "negative beam test",
            new Vector3i(1, 1, 1),
            negativeFamily);
        SmartBuildPlan negativeScenePlan = negativeScene.BuildPlan(
            negativeSource,
            _ => false,
            new SmartBuildPlannerOptions { AllowNullConstructForVerification = true },
            out _);
        Assert(
            sampledTwo && acceptedTwo != null &&
            acceptedTwo.OccupiedSize.Equals(new Vector3i(1, 1, 2)) &&
            acceptedTwo.Length == 2 &&
            sampledThree && acceptedThree != null &&
            acceptedThree.OccupiedSize.Equals(new Vector3i(1, 3, 1)) &&
            acceptedThree.Length == 3 &&
            sampled && accepted != null &&
            accepted.DefinitionGuid == sourceGuid &&
            accepted.Material == SmartBuildMaterial.Alloy &&
            accepted.ShapeDescriptorKey == SmartBuildShapeDescriptors.CuboidKey &&
            accepted.Length == 4 &&
            accepted.OccupiedSize.Equals(new Vector3i(4, 1, 1)) &&
            accepted.ForwardAxis == SmartBuildAxis.X && accepted.ForwardSign == 1 &&
            beamOriented && sampledBeam.EnumeratePreviewCells().Count() == 4 &&
            sampledBeam.Bounds.Size.Equals(new Vector3i(4, 1, 1)) &&
            sampledNegative && acceptedNegative.ForwardAxis == SmartBuildAxis.X &&
            acceptedNegative.ForwardSign == -1 &&
            acceptedNegative.OccupiedOriginOffset.Equals(new Vector3i(-3, 0, 0)) &&
            negativeOriented && negativeCells.Select(cell => cell.x).OrderBy(value => value)
                .SequenceEqual(new[] { 7, 8, 9, 10 }) &&
            negativePlan?.CanCommit == true && negativePlan.Placements.Count == 1 &&
            negativePlan.Placements[0].Position.Equals(sampledAnchor) &&
            negativePlan.Placements[0].AxisSign == -1 &&
            negativePlan.Placements[0].CoveredCells().Select(cell => cell.x).OrderBy(value => value)
                .SequenceEqual(new[] { 7, 8, 9, 10 }) &&
            negativeScenePlan.CanCommit && negativeScenePlan.Placements.Count == 1 &&
            negativeScenePlan.Placements[0].Position.Equals(sampledAnchor) &&
            negativeScenePlan.Placements[0].AxisSign == -1,
            "Craft-block sampling preserves exact signed cardinal 2m/3m/4m beam footprints from preview through directional planning while retaining immutable source identity. " +
            sampleReason + " " + negativeReason);

        Quaternion nonCardinalY45 = new Quaternion(0f, 0.38268343f, 0f, 0.9238795f);
        SmartBuildCraftBlockSampleDescriptor previous = accepted;
        bool rejectedNonCardinal = !SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            Guid.NewGuid(),
            SmartBuildMaterial.Wood,
            SmartBlockCandidate.ForTests(2),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            nonCardinalY45,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor rejectedRotation,
            out string rotationReason);
        bool rejectedUnreadable = !SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            Guid.NewGuid(),
            SmartBuildMaterial.Metal,
            SmartBlockCandidate.ForTests(3),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: false,
            Quaternion.identity,
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor rejectedUnreadableSample,
            out string unreadableReason);
        Assert(
            rejectedNonCardinal && rejectedRotation == null &&
            rejectedUnreadable && rejectedUnreadableSample == null &&
            ReferenceEquals(previous, accepted) &&
            rotationReason.IndexOf("picker remains armed", StringComparison.OrdinalIgnoreCase) >= 0 &&
            unreadableReason.IndexOf("picker remains armed", StringComparison.OrdinalIgnoreCase) >= 0,
            "Unreadable and non-cardinal craft rotations produce no replacement descriptor, preserving the prior immutable sample for an atomic armed-picker retry.");

        SmartBuildPiece exactPrimary = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(3, 3, 3),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece exactMovedRotated = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(20, 0, 0),
            new Vector3i(3, 3, 3),
            SmartBuildDrawPlane.XY);
        exactMovedRotated.TrySetOrientationFromRotation(cardinalY90);
        SmartBuildPiece differentSize = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(4, 3, 3),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece differentFill = exactPrimary.Clone();
        differentFill.SetCuboidHollow(true);
        exactPrimary.SetMaterialOverride(SmartBuildMaterial.Wood);
        exactMovedRotated.SetMaterialOverride(SmartBuildMaterial.Wood);
        differentSize.SetMaterialOverride(SmartBuildMaterial.Metal);

        SmartBuildPiece generatorShell = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(0, 0, 0),
            SmartBuildShapeDescriptors.ByKey("generated-tube"),
            SmartBuildAxis.Z,
            1,
            7,
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece generatorFilled = generatorShell.Clone();
        generatorFilled.SetGeneratorFillMode(SmartBuildGeneratorFillMode.Filled);
        Func<SmartBuildPiece, SmartBuildMaterial> materialForPiece =
            piece => piece.MaterialOverride ?? SmartBuildMaterial.Alloy;
        Assert(
            SmartBuildSession.SameExactSmartPieceShape(exactPrimary, exactMovedRotated) &&
            !SmartBuildSession.SameExactSmartPieceShape(exactPrimary, differentSize) &&
            !SmartBuildSession.SameExactSmartPieceShape(exactPrimary, differentFill) &&
            !SmartBuildSession.SameExactSmartPieceShape(generatorShell, generatorFilled) &&
            SmartBuildSession.SameSmartPieceMaterial(
                exactPrimary,
                exactMovedRotated,
                materialForPiece) &&
            !SmartBuildSession.SameSmartPieceMaterial(
                exactPrimary,
                differentSize,
                materialForPiece),
            "Smart bulk predicates match exact geometry/fill variants and resolved materials while ignoring only position, orientation, and draw-plane presentation state.");
    }

    private static void VerifySmartScenePresetRoundTrip()
    {
        AllConstruct sourceConstruct = NewConstructIdentityForTests();
        AllConstruct targetConstruct = NewConstructIdentityForTests();
        var scene = new SmartBuildPieceScene(sourceConstruct);
        SmartBuildPiece arc = SmartBuildPiece.CreateGeneratedShape(
            sourceConstruct,
            new Vector3i(10, 4, 3),
            SmartBuildShapeDescriptors.ByKey("generated-arc"),
            SmartBuildAxis.Z,
            1,
            width: 7,
            drawPlane: SmartBuildDrawPlane.XY);
        arc.SetGeneratorArcDegrees(225);
        arc.SetMaterialOverride(SmartBuildMaterial.Glass);
        SmartBuildPiece frustum = SmartBuildPiece.CreateGeneratedShape(
            sourceConstruct,
            new Vector3i(30, 4, 3),
            SmartBuildShapeDescriptors.ByKey("generated-frustum"),
            SmartBuildAxis.X,
            -1,
            width: 7,
            drawPlane: SmartBuildDrawPlane.XY);
        frustum.SetGeneratorTopScalePercent(35);
        frustum.SetMaterialOverride(SmartBuildMaterial.HeavyArmour);
        scene.Add(arc);
        scene.Add(frustum);
        scene.SetSelection(new[] { arc.Id, frustum.Id }, frustum.Id);

        SmartBuildScenePresetPayload captured = SmartBuildScenePresetPayload.Capture(
            scene,
            SmartBuildMaterial.Metal,
            SmartBuildTool.Rotate,
            SmartBuildShapeKind.GeneratedFrustum,
            "generated-frustum",
            3,
            SmartBuildDrawPlane.XY,
            SmartBuildEditHandleMode.Corner,
            SmartBuildSlopeSupportMode.Step,
            SmartBuildPreviewMode.Material,
            SmartBuildOccupancyMode.ReplaceOccupied);
        string json = JsonConvert.SerializeObject(captured, Formatting.None);
        SmartBuildScenePresetPayload loaded = JsonConvert.DeserializeObject<SmartBuildScenePresetPayload>(json);
        IReadOnlyList<SmartBuildPiece> pieces = Array.Empty<SmartBuildPiece>();
        IReadOnlyList<int> selectedIds = Array.Empty<int>();
        int primaryId = -1;
        string restoreReason = "The serialized payload did not deserialize.";
        bool restored = loaded != null && loaded.TryRestore(
            targetConstruct,
            new Vector3i(100, 20, 30),
            out pieces,
            out selectedIds,
            out primaryId,
            out restoreReason);

        Assert(
            restored && pieces.Count == 2 && selectedIds.Count == 2 && primaryId == pieces[1].Id &&
            ReferenceEquals(pieces[0].Construct, targetConstruct) &&
            pieces[0].Origin.Equals(new Vector3i(100, 20, 30)) &&
            pieces[1].Origin.Equals(new Vector3i(120, 20, 30)) &&
            pieces[0].GeneratorArcDegrees == 225 &&
            pieces[1].GeneratorTopScalePercent == 35 &&
            pieces[0].MaterialOverride == SmartBuildMaterial.Glass &&
            pieces[1].MaterialOverride == SmartBuildMaterial.HeavyArmour &&
            loaded.OccupancyMode == SmartBuildOccupancyMode.ReplaceOccupied &&
            !json.Contains("$type"),
            "Smart Builder named/recovery payloads round-trip portable group selection, editor state, materials, and advanced generator settings. " +
            restoreReason);
    }

    private static void VerifySurfacePresetTargetRelocation()
    {
        AllConstruct sourceConstruct = NewConstructIdentityForTests();
        AllConstruct targetConstruct = NewConstructIdentityForTests();
        var draft = new SurfaceDraft();
        draft.SetConstructForTests(sourceConstruct);
        draft.AddPointForTests(new Vector3(2f, 3f, 4f));
        draft.AddPointForTests(new Vector3(4f, 3f, 4f));
        draft.AddPointForTests(new Vector3(2f, 5f, 4f));
        bool face = draft.TryAddFace(0, 1, 2, out string faceMessage);
        EsuSurfaceDraftPresetPayload payload =
            EsuSurfaceDraftPresetPayload.Capture(draft, preserveSelection: false);
        bool relocated = payload.TryCreateSnapshot(
            targetConstruct,
            new Vector3(50f, 60f, 70f),
            out SurfaceDraftSnapshot snapshot,
            out string relocationMessage);

        Assert(
            face && relocated && ReferenceEquals(snapshot.Construct, targetConstruct) &&
            snapshot.Points.Length == 3 &&
            VectorApproximately(snapshot.Points[0], new Vector3(50f, 60f, 70f), 0.00001f) &&
            VectorApproximately(snapshot.Points[1], new Vector3(52f, 60f, 70f), 0.00001f) &&
            snapshot.Faces.Length == 1,
            "Surface preset payloads relocate their full topology to an explicit selected/pointed target on another construct. " +
            faceMessage + " " + relocationMessage);
    }

    private static void VerifySmartDestructivePlanSemantics()
    {
        var volume = SmartBuildVolume.FromBounds(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(2, 0, 0));
        SmartBuildPlan replace = SmartBuildPlanner.BuildPlan(
            volume,
            SmartBlockFamily.ForTests(1),
            _ => true,
            new SmartBuildPlannerOptions { AllowOccupiedCells = true })
            .WithCommitOperation(
                SmartBuildCommitOperation.Replace,
                new[]
                {
                    new Vector3i(2, 0, 0),
                    new Vector3i(0, 0, 0),
                    new Vector3i(2, 0, 0)
                });
        var erase = new SmartBuildPlan(
                volume,
                Array.Empty<SmartBuildPlacement>(),
                Array.Empty<Vector3i>(),
                Array.Empty<string>(),
                canCommit: true,
                failureReason: null)
            .WithCommitOperation(
                SmartBuildCommitOperation.Erase,
                new[]
                {
                    new Vector3i(4, 0, 0),
                    new Vector3i(3, 0, 0),
                    new Vector3i(4, 0, 0)
                });

        Assert(
            replace.CanCommit && replace.Placements.Count == 3 &&
            replace.CommitOperation == SmartBuildCommitOperation.Replace &&
            replace.RemovalCells.Count == 2 &&
            erase.CanCommit && erase.CommitOperation == SmartBuildCommitOperation.Erase &&
            erase.Placements.Count == 0 && erase.RemovalCells.Count == 2 &&
            erase.EstimatedBlockCount == 2 && erase.CoveredCellCount == 2,
            "Smart Builder replace retains placements while erase is a bounded removal-only plan with deterministic deduped cells.");
    }

    private static void VerifySmartAdvancedRuntimeWiring()
    {
        string root = FindRepositoryRoot();
        string smartRoot = Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode");
        string session = File.ReadAllText(Path.Combine(smartRoot, "SmartBuildSession.cs"));
        string committer = File.ReadAllText(Path.Combine(smartRoot, "SmartBuildCommitter.cs"));
        string commandAbstractions = File.ReadAllText(Path.Combine(
            smartRoot,
            "SmartBuildCommitAbstractions.cs"));
        string catalog = File.ReadAllText(Path.Combine(smartRoot, "SmartBlockFamilyCatalog.cs"));
        string decorationSession = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.cs"));

        Assert(
            session.Contains("BeginViewportBoxSelection") &&
            session.Contains("SelectRange(") &&
            session.Contains("BulkSelectAllPieces") &&
            session.Contains("BulkInvertPieceSelection") &&
            session.Contains("BulkSelectPiecesByPrimaryShape") &&
            session.Contains("BulkSelectPiecesByPrimaryMaterial"),
            "Smart Builder wires viewport marquee/range multi-selection and explicit bulk all/invert/by-shape/by-material commands.");
        Assert(
            session.Contains("TryScaleSelectionFromDraggedHandles") &&
            session.Contains("piece.TryScaleAboutPivot(pivot, factors") &&
            session.Contains("RestoreSelectedPiecesFromSnapshot(_dragSceneStart)") &&
            session.Contains("Group scale rejected"),
            "Smart Builder's Scale gizmo derives one primary-pivot factor set, scales every selected piece spatially from the immutable drag snapshot, and rejects non-exact shape transforms atomically.");
        Assert(
            session.Contains("DrawSmartScenePresetTargetControls") &&
            session.Contains("TryCaptureSmartScenePresetTargetFromPointer") &&
            session.Contains("relocateToChosenTarget: true") &&
            session.Contains("Vector3i? anchorOverride = relocate") &&
            decorationSession.Contains("DrawSurfacePresetPlacementTarget") &&
            decorationSession.Contains("TryCaptureSurfacePresetPlacementTarget") &&
            decorationSession.Contains("TryResolveSurfacePresetPlacement") &&
            decorationSession.Contains("targetReference") &&
            decorationSession.Contains("if (_pendingSurfacePresetTargetPick)"),
            "Named Smart scenes and Surface/Generator drafts expose selected/pointed placement targets and feed those explicit anchors into their portable payload restore paths.");
        Assert(
            session.Contains("BeginBrushStroke") &&
            session.Contains("recordHistory: false") &&
            session.Contains("PushSceneSnapshot(before)") &&
            session.Contains("Undo removes the complete stroke"),
            "Smart Builder continuous brush painting records the complete stroke as one preview-scene undo transaction.");
        Assert(
            catalog.Contains("TryIdentifyBlock(") &&
            catalog.Contains("ComponentGuid(possible.Definition) == definitionGuid") &&
            session.Contains("TrySamplePointedCraftBlock") &&
            session.Contains("GetBlockViaLocalPosition(hit.Anchor)") &&
            session.Contains("definition = block?.item") &&
            session.Contains("localRotation = block.LocalRotation") &&
            session.Contains("TryCreateCraftBlockSampleDescriptor") &&
            session.Contains("TrySetOrientationFromRotation") &&
            session.Contains("_sampledCuboidSize = sample.IsCuboid") &&
            session.Contains("resolvedCuboidSize = _sampledCuboidSize.Value") &&
            session.Contains("_lastCraftBlockSample = sample") &&
            session.IndexOf("TryCreateCraftBlockSampleDescriptor(", StringComparison.Ordinal) <
            session.IndexOf("_selectedMaterial = sample.Material", StringComparison.Ordinal) &&
            session.IndexOf("_lastCraftBlockSample = sample", StringComparison.Ordinal) <
            session.IndexOf("_blockEyedropperArmed = false", session.IndexOf("_lastCraftBlockSample = sample", StringComparison.Ordinal), StringComparison.Ordinal) &&
            catalog.Contains("not a supported Smart Builder structural shape"),
            "Smart Builder's armed craft-block eyedropper validates exact item metadata, occupied size, and cardinal rotation before atomically accepting and disarming the sample.");
        Assert(
            commandAbstractions.Contains("new RemoveBlockCommand(") &&
            commandAbstractions.Contains("new PlaceBlockCommand(") &&
            committer.Contains("commandFactory.CreateRemoval(") &&
            committer.Contains("commandFactory.CreatePlacement(") &&
            committer.Contains("TryRollBackCommandJournal(") &&
            committer.Contains("new SmartBuildUndoCommand(commands.ToArray())") &&
            committer.IndexOf("foreach (SmartBuildRemovalItem removal in removalItems)", StringComparison.Ordinal) >= 0 &&
            committer.IndexOf("foreach (SmartBuildPlacement placement in ordered)", StringComparison.Ordinal) >= 0 &&
            committer.IndexOf("foreach (SmartBuildRemovalItem removal in removalItems)", StringComparison.Ordinal) <
            committer.IndexOf("foreach (SmartBuildPlacement placement in ordered)", StringComparison.Ordinal),
            "Smart Builder transactional replace/erase removes before placement, rolls the combined command journal back in reverse, and registers one undo command.");
    }

    private static SmartBuildSource SmartTestSource(string label)
    {
        var candidate = new SmartBlockCandidate(label + " 1m", 1, null);
        var family = new SmartBlockFamily(label, new[] { candidate });
        return new SmartBuildSource(
            null,
            Guid.Empty,
            label,
            new Vector3i(1, 1, 1),
            family);
    }
}
