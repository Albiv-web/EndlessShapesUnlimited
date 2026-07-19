using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;

internal static partial class Program
{
    // Kept as an independently callable verification entry so the shared Program.cs
    // runner can remain untouched while concurrent milestone work is in progress.
    private static void VerifySmartBuildPrecisionWorkflow()
    {
        VerifySmartBuildAbsoluteAndRelativeTranslation();
        VerifySmartBuildAlignmentPlans();
        VerifySmartBuildEqualGapDistribution();
        VerifySmartBuildPrecisionValidation();
        VerifySmartBuildPrecisionPivotsAndQuarterTurns();
        VerifySmartBuildPrimitiveDimensionEditing();
        VerifySmartBuildFaceSnappingAndMeasurements();
        VerifySmartBuildPrecisionRuntimeWiring();
    }

    private static void VerifySmartBuildAbsoluteAndRelativeTranslation()
    {
        SmartBuildPrecisionItem[] items =
        {
            PrecisionItem(1, 10, 2, -3, 10, 2, -3, 11, 2, -3),
            PrecisionItem(2, 14, 4, -1, 14, 4, -1, 16, 5, 0)
        };

        bool absolute = SmartBuildPrecisionTools.TryTranslate(
            items,
            primaryId: 1,
            new Vector3i(20, 7, -8),
            SmartBuildPrecisionPositionMode.AbsoluteCraftLocal,
            out SmartBuildPrecisionLayoutPlan absolutePlan,
            out string absoluteMessage);
        Assert(
            absolute &&
            absolutePlan.SelectionCount == 2 &&
            MoveFor(absolutePlan, 1).Equals(new Vector3i(10, 5, -5)) &&
            MoveFor(absolutePlan, 2).Equals(new Vector3i(10, 5, -5)),
            "Smart Builder absolute precision editing sets the primary craft-local origin while preserving every selected group offset. " +
            absoluteMessage);

        bool relative = SmartBuildPrecisionTools.TryTranslate(
            items,
            primaryId: 1,
            new Vector3i(-2, 3, 5),
            SmartBuildPrecisionPositionMode.RelativeCraftLocal,
            out SmartBuildPrecisionLayoutPlan relativePlan,
            out string relativeMessage);
        Assert(
            relative &&
            MoveFor(relativePlan, 1).Equals(new Vector3i(-2, 3, 5)) &&
            MoveFor(relativePlan, 2).Equals(new Vector3i(-2, 3, 5)),
            "Smart Builder relative precision editing applies one exact craft-local delta atomically to the selected group. " +
            relativeMessage);
    }

    private static void VerifySmartBuildAlignmentPlans()
    {
        SmartBuildPrecisionItem[] edgeItems =
        {
            PrecisionItem(10, 2, 0, 0, 2, 0, 0, 4, 0, 0),
            PrecisionItem(20, 9, 0, 0, 9, 0, 0, 10, 0, 0),
            PrecisionItem(30, -3, 0, 0, -3, 0, 0, -1, 0, 0)
        };

        bool minimum = SmartBuildPrecisionTools.TryAlign(
            edgeItems,
            primaryId: 10,
            SmartBuildAxis.X,
            SmartBuildPrecisionAlignment.Minimum,
            out SmartBuildPrecisionLayoutPlan minimumPlan,
            out string minimumMessage);
        Assert(
            minimum &&
            MoveFor(minimumPlan, 20).Equals(new Vector3i(-7, 0, 0)) &&
            MoveFor(minimumPlan, 30).Equals(new Vector3i(5, 0, 0)),
            "Smart Builder minimum-edge alignment uses the primary occupied bound in craft-local cells. " +
            minimumMessage);

        bool maximum = SmartBuildPrecisionTools.TryAlign(
            edgeItems,
            primaryId: 10,
            SmartBuildAxis.X,
            SmartBuildPrecisionAlignment.Maximum,
            out SmartBuildPrecisionLayoutPlan maximumPlan,
            out string maximumMessage);
        Assert(
            maximum &&
            MoveFor(maximumPlan, 20).Equals(new Vector3i(-6, 0, 0)) &&
            MoveFor(maximumPlan, 30).Equals(new Vector3i(5, 0, 0)),
            "Smart Builder maximum-edge alignment uses exact inclusive occupied bounds. " +
            maximumMessage);

        bool incompatibleCenters = SmartBuildPrecisionTools.TryAlign(
            edgeItems.Take(2).ToArray(),
            primaryId: 10,
            SmartBuildAxis.X,
            SmartBuildPrecisionAlignment.Center,
            out _,
            out string centerMessage);
        Assert(
            !incompatibleCenters && centerMessage.IndexOf("parity", StringComparison.OrdinalIgnoreCase) >= 0,
            "Center alignment fails closed when half-cell parity would make an exact whole-cell result impossible.");
    }

    private static void VerifySmartBuildEqualGapDistribution()
    {
        SmartBuildPrecisionItem[] exactItems =
        {
            PrecisionItem(1, 0, 0, 0, 0, 0, 0, 1, 0, 0),
            PrecisionItem(2, 3, 0, 0, 3, 0, 0, 3, 0, 0),
            PrecisionItem(3, 9, 0, 0, 9, 0, 0, 11, 0, 0)
        };
        bool exact = SmartBuildPrecisionTools.TryDistributeEqualGaps(
            exactItems,
            SmartBuildAxis.X,
            out SmartBuildPrecisionLayoutPlan exactPlan,
            out string exactMessage);
        Assert(
            exact &&
            !exactPlan.RoundedToWholeCells &&
            MoveFor(exactPlan, 2).Equals(new Vector3i(2, 0, 0)) &&
            MoveFor(exactPlan, 1).Equals(default) &&
            MoveFor(exactPlan, 3).Equals(default),
            "Equal-gap distribution preserves the outer pair and accounts for each occupied width. " +
            exactMessage);

        SmartBuildPrecisionItem[] roundedItems =
        {
            PrecisionItem(1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            PrecisionItem(2, 2, 0, 0, 2, 0, 0, 2, 0, 0),
            PrecisionItem(3, 6, 0, 0, 6, 0, 0, 6, 0, 0),
            PrecisionItem(4, 8, 0, 0, 8, 0, 0, 8, 0, 0)
        };
        bool rounded = SmartBuildPrecisionTools.TryDistributeEqualGaps(
            roundedItems,
            SmartBuildAxis.X,
            out SmartBuildPrecisionLayoutPlan roundedPlan,
            out string roundedMessage);
        Assert(
            rounded &&
            roundedPlan.RoundedToWholeCells &&
            MoveFor(roundedPlan, 2).Equals(new Vector3i(1, 0, 0)) &&
            MoveFor(roundedPlan, 3).Equals(new Vector3i(-1, 0, 0)),
            "Indivisible Smart Builder spans are apportioned deterministically with adjacent whole-cell gaps differing by at most one. " +
            roundedMessage);
    }

    private static void VerifySmartBuildPrecisionValidation()
    {
        bool parsed = SmartBuildPrecisionTools.TryParseCellVector(
            "-12",
            "+34",
            " 56 ",
            out Vector3i value,
            out string parseMessage);
        Assert(
            parsed && value.Equals(new Vector3i(-12, 34, 56)) &&
            !SmartBuildPrecisionTools.TryParseCellVector("1.5", "0", "0", out _, out _),
            "Precision coordinate fields accept signed whole craft-local cells and reject fractional input. " +
            parseMessage);

        SmartBuildPrecisionItem nearLimit = PrecisionItem(
            99,
            SmartBuildPrecisionTools.MaximumCoordinateMagnitude,
            0,
            0,
            SmartBuildPrecisionTools.MaximumCoordinateMagnitude,
            0,
            0,
            SmartBuildPrecisionTools.MaximumCoordinateMagnitude,
            0,
            0);
        bool unsafeMove = SmartBuildPrecisionTools.TryTranslate(
            new[] { nearLimit },
            primaryId: 99,
            new Vector3i(1, 0, 0),
            SmartBuildPrecisionPositionMode.RelativeCraftLocal,
            out _,
            out string unsafeMessage);
        Assert(
            !unsafeMove && unsafeMessage.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0,
            "Precision helpers reject the complete operation before any target bound leaves the safe craft-local range.");
    }

    private static void VerifySmartBuildPrecisionPivotsAndQuarterTurns()
    {
        SmartBuildPrecisionItem[] items =
        {
            PrecisionItem(10, 0, 0, 0, 0, 0, 0, 1, 1, 1),
            PrecisionItem(20, 5, 0, 0, 5, 0, 0, 6, 1, 1)
        };
        bool primaryPivot = SmartBuildPrecisionTools.TryResolvePivot(
            items,
            primaryId: 10,
            SmartBuildPrecisionPivotMode.Primary,
            default,
            out Vector3i primary,
            out string primaryMessage);
        bool boundsPivot = SmartBuildPrecisionTools.TryResolvePivot(
            items,
            primaryId: 10,
            SmartBuildPrecisionPivotMode.SelectionBoundsCenter,
            default,
            out Vector3i bounds,
            out string boundsMessage);
        Vector3i requestedCustom = new Vector3i(-4, 7, 9);
        bool customPivot = SmartBuildPrecisionTools.TryResolvePivot(
            items,
            primaryId: 10,
            SmartBuildPrecisionPivotMode.CustomCraftLocal,
            requestedCustom,
            out Vector3i custom,
            out string customMessage);
        Assert(
            primaryPivot && primary.Equals(new Vector3i(1, 1, 1)) &&
            boundsPivot && bounds.Equals(new Vector3i(3, 1, 1)) &&
            customPivot && custom.Equals(requestedCustom),
            "Precision rotation resolves deterministic primary, selection-bounds-center, and custom craft-local pivot cells. " +
            primaryMessage + " " + boundsMessage + " " + customMessage);

        SmartBuildPiece first = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(1, 0, 0),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece second = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(2, 0, 0),
            SmartBuildDrawPlane.Camera);
        bool rotatedGroup = SmartBuildPrecisionTools.TryRotatePieces(
            new[] { first, second },
            DecorationEditAxis.Z,
            quarterTurns: 1,
            new Vector3i(0, 0, 0),
            out IReadOnlyList<SmartBuildPiece> rotated,
            out string rotateMessage);
        bool invalidTurnsRejected = !SmartBuildPrecisionTools.TryRotatePieces(
            new[] { first },
            DecorationEditAxis.Z,
            quarterTurns: 2,
            new Vector3i(0, 0, 0),
            out _,
            out _);
        SmartBuildPiece xAxisProbe = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 1, 0),
            SmartBuildDrawPlane.Camera);
        bool rotatedX = SmartBuildPrecisionTools.TryRotatePieces(
            new[] { xAxisProbe },
            DecorationEditAxis.X,
            quarterTurns: 1,
            new Vector3i(0, 0, 0),
            out IReadOnlyList<SmartBuildPiece> aroundX,
            out _);
        bool rotatedY = SmartBuildPrecisionTools.TryRotatePieces(
            new[] { first },
            DecorationEditAxis.Y,
            quarterTurns: 1,
            new Vector3i(0, 0, 0),
            out IReadOnlyList<SmartBuildPiece> aroundY,
            out _);
        bool rotatedNegativeZ = SmartBuildPrecisionTools.TryRotatePieces(
            new[] { first },
            DecorationEditAxis.Z,
            quarterTurns: -1,
            new Vector3i(0, 0, 0),
            out IReadOnlyList<SmartBuildPiece> aroundNegativeZ,
            out _);
        Assert(
            rotatedGroup && rotated.Count == 2 &&
            rotated[0].Origin.Equals(new Vector3i(0, 1, 0)) &&
            rotated[1].Origin.Equals(new Vector3i(0, 2, 0)) &&
            rotatedX && aroundX.Count == 1 &&
            aroundX[0].Origin.Equals(new Vector3i(0, 0, 1)) &&
            rotatedY && aroundY.Count == 1 &&
            aroundY[0].Origin.Equals(new Vector3i(0, 0, -1)) &&
            rotatedNegativeZ && aroundNegativeZ.Count == 1 &&
            aroundNegativeZ[0].Origin.Equals(new Vector3i(0, -1, 0)) &&
            first.Origin.Equals(new Vector3i(1, 0, 0)) &&
            second.Origin.Equals(new Vector3i(2, 0, 0)) &&
            invalidTurnsRejected,
            "Precision group rotation applies one exact X/Y/Z-axis quarter turn to cloned primitives and leaves every input unchanged on preparation. " +
            rotateMessage);
    }

    private static void VerifySmartBuildPrimitiveDimensionEditing()
    {
        SmartBuildPiece original = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(7, -3, 11),
            new Vector3i(2, 3, 4),
            SmartBuildDrawPlane.Camera);
        SmartBuildBounds originalBounds = original.GroupScaleBounds;
        Vector3i requested = new Vector3i(4, 2, 1);
        bool resizedExactly = SmartBuildPrecisionTools.TryResizePrimitive(
            original,
            requested,
            out SmartBuildPiece resized,
            out string resizeMessage);
        bool invalidRejected = !SmartBuildPrecisionTools.TryResizePrimitive(
            original,
            new Vector3i(4, 0, 1),
            out _,
            out _);
        Assert(
            resizedExactly && resized != null &&
            resized.GroupScaleBounds.Min.Equals(originalBounds.Min) &&
            resized.GroupScaleBounds.Size.Equals(requested) &&
            original.GroupScaleBounds.Min.Equals(originalBounds.Min) &&
            original.GroupScaleBounds.Size.Equals(originalBounds.Size) &&
            SmartBuildPrecisionTools.PrimitiveDimensionLabel(original) == "Cuboid" &&
            invalidRejected,
            "Shape-specific primitive dimensions anchor the minimum craft-local faces, produce exact occupied bounds, and reject invalid input without mutating the source. " +
            resizeMessage);
    }

    private static void VerifySmartBuildFaceSnappingAndMeasurements()
    {
        SmartBuildPrecisionItem[] snapItems =
        {
            PrecisionItem(1, 2, 0, 0, 2, 0, 0, 3, 0, 0),
            PrecisionItem(2, 5, 2, 0, 5, 2, 0, 5, 2, 0)
        };
        bool positiveSnap = SmartBuildPrecisionTools.TrySnapSelectionToFace(
            snapItems,
            SmartBuildAxis.X,
            faceSign: 1,
            new Vector3i(10, 4, 2),
            out SmartBuildPrecisionLayoutPlan positivePlan,
            out string positiveMessage);
        bool negativeSnap = SmartBuildPrecisionTools.TrySnapSelectionToFace(
            snapItems,
            SmartBuildAxis.X,
            faceSign: -1,
            new Vector3i(-2, 4, 2),
            out SmartBuildPrecisionLayoutPlan negativePlan,
            out string negativeMessage);
        bool invalidFaceRejected = !SmartBuildPrecisionTools.TrySnapSelectionToFace(
            snapItems,
            SmartBuildAxis.X,
            faceSign: 0,
            new Vector3i(10, 4, 2),
            out _,
            out _);
        Assert(
            positiveSnap &&
            MoveFor(positivePlan, 1).Equals(new Vector3i(8, 0, 0)) &&
            MoveFor(positivePlan, 2).Equals(new Vector3i(8, 0, 0)) &&
            negativeSnap &&
            MoveFor(negativePlan, 1).Equals(new Vector3i(-7, 0, 0)) &&
            MoveFor(negativePlan, 2).Equals(new Vector3i(-7, 0, 0)) &&
            invalidFaceRejected,
            "Craft-face snapping moves the whole selected group uniformly and flushes the correct occupied bound into the picked outside cell layer. " +
            positiveMessage + " " + negativeMessage);

        SmartBuildPrecisionItem[] measuredItems =
        {
            PrecisionItem(10, 0, 0, 0, 0, 0, 0, 1, 0, 0),
            PrecisionItem(20, 5, 0, 0, 5, 0, 0, 6, 0, 0),
            PrecisionItem(30, -5, 0, 0, -5, 0, 0, -4, 0, 0)
        };
        bool measured = SmartBuildPrecisionTools.TryMeasureSelection(
            measuredItems,
            primaryId: 10,
            out SmartBuildPrecisionSelectionMetrics metrics,
            out string measureMessage);
        Assert(
            measured && metrics != null &&
            metrics.Bounds.Min.Equals(new Vector3i(-5, 0, 0)) &&
            metrics.Bounds.Max.Equals(new Vector3i(6, 0, 0)) &&
            metrics.Span.Equals(new Vector3i(12, 1, 1)) &&
            metrics.NearestPieceId == 20 &&
            metrics.MeasuredClearGapCells.Equals(new Vector3i(3, 0, 0)),
            "Selection measurement reports exact aggregate bounds/span and a deterministic primary-to-nearest clear cell gap. " +
            measureMessage);
    }

    private static void VerifySmartBuildPrecisionRuntimeWiring()
    {
        string root = FindPrecisionRepositoryRoot();
        string session = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.cs"));
        string precisionSession = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.PrecisionTools.cs"));
        string precisionTransformSession = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.PrecisionTransformTools.cs"));
        string precisionTools = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildPrecisionTools.cs"));
        string behaviour = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildModeBehaviour.cs"));

        Assert(
            session.Contains("TryHandlePrecisionKeyboardShortcuts()") &&
            session.Contains("TryHandlePrecisionPointerInput()") &&
            session.Contains("DrawPrecisionTransformLayoutControls()") &&
            session.Contains("ResetPrecisionSessionState()") &&
            precisionSession.Contains("SmartBuildPrecisionPositionMode.AbsoluteCraftLocal") &&
            precisionSession.Contains("SmartBuildPrecisionPositionMode.RelativeCraftLocal") &&
            precisionSession.Contains("ApplyPrecisionLayoutPlan(plan, message)") &&
            precisionSession.Contains("Input.GetKeyDown(KeyCode.A)") &&
            precisionSession.Contains("Input.GetKeyDown(KeyCode.C)") &&
            precisionSession.Contains("Input.GetKeyDown(KeyCode.D)") &&
            precisionSession.Contains("Input.GetKeyDown(KeyCode.V)") &&
            precisionSession.Contains("Input.GetKeyDown(KeyCode.Delete)") &&
            precisionSession.Contains("GUIUtility.keyboardControl != 0") &&
            precisionSession.Contains("EsuInputState.IsTextInputActive()") &&
            precisionTools.Contains("SmartBuildPrecisionPivotMode") &&
            precisionTools.Contains("TryRotatePieces(") &&
            precisionTools.Contains("TryResizePrimitive(") &&
            precisionTools.Contains("TrySnapSelectionToFace(") &&
            precisionTools.Contains("TryMeasureSelection(") &&
            precisionTransformSession.Contains("DrawPrecisionSelectionMeasurements()") &&
            precisionTransformSession.Contains("DrawPrecisionPrimitiveDimensionControls()") &&
            precisionTransformSession.Contains("DrawPrecisionPivotRotationControls()") &&
            precisionTransformSession.Contains("Snap group to craft face") &&
            precisionTransformSession.Contains("_scene.MarkGeometryChanged()") &&
            behaviour.Contains("TryHandlePrecisionEscapeShortcut()"),
            "Smart Builder wires compact craft-local transforms, measurements, shape dimensions, pivot quarter turns, craft-face snapping, session clipboard shortcuts, text-input guards, and Escape cancellation through precision partials.");
    }

    private static SmartBuildPrecisionItem PrecisionItem(
        int id,
        int originX,
        int originY,
        int originZ,
        int minimumX,
        int minimumY,
        int minimumZ,
        int maximumX,
        int maximumY,
        int maximumZ) =>
        new SmartBuildPrecisionItem(
            id,
            new Vector3i(originX, originY, originZ),
            new SmartBuildBounds(
                new Vector3i(minimumX, minimumY, minimumZ),
                new Vector3i(maximumX, maximumY, maximumZ)));

    private static string FindPrecisionRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EndlessShapesUnlimited", "plugin.json")))
                return current.FullName;
            current = current.Parent;
        }

        return FindRepositoryRoot();
    }

    private static Vector3i MoveFor(SmartBuildPrecisionLayoutPlan plan, int pieceId)
    {
        SmartBuildPrecisionMove[] matches = plan.Moves
            .Where(move => move.PieceId == pieceId)
            .ToArray();
        return matches.Length == 0 ? default : matches[0].Delta;
    }
}
