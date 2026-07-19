using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;

internal static partial class Program
{
    private static void VerifySmartBuildToolPlanner()
    {
        VerifySmartToolPlannerLinearAndGrid();
        VerifySmartToolPlannerCardinalRadial();
        VerifySmartToolPlannerPaths();
        VerifySmartToolPlannerRectangles();
        VerifySmartToolPlannerFloodFill();
        VerifySmartToolPlannerAtomicSceneCopies();
        VerifySmartToolPlannerRuntimeIntegration();
    }

    private static void VerifySmartToolPlannerLinearAndGrid()
    {
        bool linear = SmartBuildAdvancedToolPlanner.TryPlanLinear(
            new Vector3i(2, -1, 3),
            3,
            out IReadOnlyList<SmartBuildGroupTransform> linearCopies,
            out string linearReason);
        Assert(
            linear && linearCopies.Count == 3 &&
            ToolPlannerCell(linearCopies[0].Offset, 2, -1, 3) &&
            ToolPlannerCell(linearCopies[1].Offset, 4, -2, 6) &&
            ToolPlannerCell(linearCopies[2].Offset, 6, -3, 9) &&
            linearCopies.All(copy => copy.QuarterTurns == 0),
            "Smart advanced planner creates exact bounded linear copy-only transforms. " +
            linearReason);

        bool zeroLinear = SmartBuildAdvancedToolPlanner.TryPlanLinear(
            new Vector3i(0, 0, 0),
            2,
            out IReadOnlyList<SmartBuildGroupTransform> zeroLinearCopies,
            out string zeroLinearReason);
        bool cappedLinear = SmartBuildAdvancedToolPlanner.TryPlanLinear(
            new Vector3i(1, 0, 0),
            SmartBuildAdvancedToolPlanner.MaximumPatternCopies + 1,
            out IReadOnlyList<SmartBuildGroupTransform> cappedLinearCopies,
            out string cappedLinearReason);
        Assert(
            !zeroLinear && zeroLinearCopies.Count == 0 &&
            ToolPlannerReasonContains(zeroLinearReason, "spacing") &&
            !cappedLinear && cappedLinearCopies.Count == 0 &&
            ToolPlannerReasonContains(cappedLinearReason, "safe limit"),
            "Smart linear planning rejects identity spacing and over-cap requests without a partial plan.");

        bool grid = SmartBuildAdvancedToolPlanner.TryPlanGrid(
            new Vector3i(2, 0, 0),
            3,
            new Vector3i(0, 0, 3),
            2,
            out IReadOnlyList<SmartBuildGroupTransform> gridCopies,
            out string gridReason);
        Assert(
            grid && gridCopies.Count == 5 &&
            ToolPlannerCell(gridCopies[0].Offset, 2, 0, 0) &&
            ToolPlannerCell(gridCopies[1].Offset, 4, 0, 0) &&
            ToolPlannerCell(gridCopies[2].Offset, 0, 0, 3) &&
            ToolPlannerCell(gridCopies[3].Offset, 2, 0, 3) &&
            ToolPlannerCell(gridCopies[4].Offset, 4, 0, 3),
            "Smart advanced planner creates a deterministic grid with the original identity excluded. " +
            gridReason);

        bool parallelGrid = SmartBuildAdvancedToolPlanner.TryPlanGrid(
            new Vector3i(1, 0, 0),
            2,
            new Vector3i(-2, 0, 0),
            2,
            out IReadOnlyList<SmartBuildGroupTransform> parallelGridCopies,
            out string parallelGridReason);
        bool cappedGrid = SmartBuildAdvancedToolPlanner.TryPlanGrid(
            new Vector3i(1, 0, 0),
            23,
            new Vector3i(0, 1, 0),
            23,
            out IReadOnlyList<SmartBuildGroupTransform> cappedGridCopies,
            out string cappedGridReason);
        Assert(
            !parallelGrid && parallelGridCopies.Count == 0 &&
            ToolPlannerReasonContains(parallelGridReason, "independent") &&
            !cappedGrid && cappedGridCopies.Count == 0 &&
            ToolPlannerReasonContains(cappedGridReason, "safe limit"),
            "Smart grid planning rejects collapsed directions and cap overflow atomically.");
    }

    private static void VerifySmartToolPlannerCardinalRadial()
    {
        bool radialY = SmartBuildAdvancedToolPlanner.TryPlanCardinalRadial(
            new Vector3i(4, 0, 0),
            DecorationEditAxis.Y,
            3,
            rotateCopies: true,
            out IReadOnlyList<SmartBuildGroupTransform> yCopies,
            out string yReason);
        bool radialX = SmartBuildAdvancedToolPlanner.TryPlanCardinalRadial(
            new Vector3i(0, 2, 0),
            DecorationEditAxis.X,
            1,
            rotateCopies: false,
            out IReadOnlyList<SmartBuildGroupTransform> xCopies,
            out string xReason);
        bool radialZ = SmartBuildAdvancedToolPlanner.TryPlanCardinalRadial(
            new Vector3i(2, 0, 0),
            DecorationEditAxis.Z,
            1,
            rotateCopies: true,
            out IReadOnlyList<SmartBuildGroupTransform> zCopies,
            out string zReason);
        Assert(
            radialY && yCopies.Count == 3 &&
            ToolPlannerCell(yCopies[0].Offset, -4, 0, -4) &&
            ToolPlannerCell(yCopies[1].Offset, -8, 0, 0) &&
            ToolPlannerCell(yCopies[2].Offset, -4, 0, 4) &&
            yCopies.Select(copy => copy.QuarterTurns).SequenceEqual(new[] { 1, 2, 3 }) &&
            yCopies.All(copy => copy.RotationAxis == DecorationEditAxis.Y) &&
            radialX && xCopies.Count == 1 &&
            ToolPlannerCell(xCopies[0].Offset, 0, -2, 2) &&
            xCopies[0].QuarterTurns == 0 &&
            radialZ && zCopies.Count == 1 &&
            ToolPlannerCell(zCopies[0].Offset, -2, 2, 0) &&
            zCopies[0].RotationAxis == DecorationEditAxis.Z &&
            zCopies[0].QuarterTurns == 1,
            "Smart radial planning covers X/Y/Z cardinal orbits and optional copy orientation. " +
            yReason + xReason + zReason);

        bool axialArm = SmartBuildAdvancedToolPlanner.TryPlanCardinalRadial(
            new Vector3i(0, 2, 0),
            DecorationEditAxis.Y,
            1,
            rotateCopies: true,
            out IReadOnlyList<SmartBuildGroupTransform> axialCopies,
            out string axialReason);
        bool wrappedRadial = SmartBuildAdvancedToolPlanner.TryPlanCardinalRadial(
            new Vector3i(2, 0, 0),
            DecorationEditAxis.Y,
            4,
            rotateCopies: true,
            out IReadOnlyList<SmartBuildGroupTransform> wrappedCopies,
            out string wrappedReason);
        Assert(
            !axialArm && axialCopies.Count == 0 &&
            ToolPlannerReasonContains(axialReason, "perpendicular") &&
            !wrappedRadial && wrappedCopies.Count == 0 &&
            ToolPlannerReasonContains(wrappedReason, "one to three"),
            "Smart radial planning rejects axial arms and identity-wrapping fourth copies.");
    }

    private static void VerifySmartToolPlannerPaths()
    {
        Vector3i[] controls =
        {
            new Vector3i(10, 5, -2),
            new Vector3i(13, 5, -2),
            new Vector3i(13, 7, -2),
            new Vector3i(13, 7, -2)
        };
        bool vertices = SmartBuildAdvancedToolPlanner.TryPlanPath(
            controls,
            SmartBuildPathArrayMode.PolylineVertices,
            out IReadOnlyList<SmartBuildGroupTransform> vertexCopies,
            out string vertexReason);
        Assert(
            vertices && vertexCopies.Count == 2 &&
            ToolPlannerCell(vertexCopies[0].Offset, 3, 0, 0) &&
            ToolPlannerCell(vertexCopies[1].Offset, 3, 2, 0),
            "Smart path planning uses unique polyline vertices and excludes the original. " +
            vertexReason);

        bool stepped = SmartBuildAdvancedToolPlanner.TryPlanPath(
            new[]
            {
                new Vector3i(0, 0, 0),
                new Vector3i(3, 2, 0),
                new Vector3i(3, 2, 2)
            },
            SmartBuildPathArrayMode.SteppedCells,
            out IReadOnlyList<SmartBuildGroupTransform> steppedCopies,
            out string steppedReason);
        Vector3i[] expectedSteps =
        {
            new Vector3i(1, 1, 0),
            new Vector3i(2, 1, 0),
            new Vector3i(3, 2, 0),
            new Vector3i(3, 2, 1),
            new Vector3i(3, 2, 2)
        };
        Assert(
            stepped && steppedCopies.Count == expectedSteps.Length &&
            steppedCopies.Select(copy => copy.Offset)
                .Select(ToolPlannerCellKey)
                .SequenceEqual(expectedSteps.Select(ToolPlannerCellKey)),
            "Smart stepped path planning rasterizes each polyline segment deterministically. " +
            steppedReason);

        bool shortPath = SmartBuildAdvancedToolPlanner.TryPlanPath(
            new[] { new Vector3i(0, 0, 0) },
            SmartBuildPathArrayMode.SteppedCells,
            out IReadOnlyList<SmartBuildGroupTransform> shortCopies,
            out string shortReason);
        bool cappedPath = SmartBuildAdvancedToolPlanner.TryPlanPath(
            new[] { new Vector3i(0, 0, 0), new Vector3i(512, 0, 0) },
            SmartBuildPathArrayMode.SteppedCells,
            out IReadOnlyList<SmartBuildGroupTransform> cappedCopies,
            out string cappedReason);
        Assert(
            !shortPath && shortCopies.Count == 0 &&
            ToolPlannerReasonContains(shortReason, "at least two") &&
            !cappedPath && cappedCopies.Count == 0 &&
            ToolPlannerReasonContains(cappedReason, "safe limit"),
            "Smart path planning rejects missing control geometry and over-cap rasterization without partial output.");
    }

    private static void VerifySmartToolPlannerRectangles()
    {
        Vector3i origin = new Vector3i(10, 20, 30);
        bool plane = SmartBuildAdvancedToolPlanner.TryPlanRectanglePlane(
            origin,
            new Vector3i(2, 0, 0),
            3,
            new Vector3i(0, 0, -3),
            2,
            out IReadOnlyList<Vector3i> planeCells,
            out string planeReason);
        Assert(
            plane && planeCells.Count == 6 &&
            ToolPlannerCell(planeCells[0], 10, 20, 30) &&
            ToolPlannerCell(planeCells[1], 12, 20, 30) &&
            ToolPlannerCell(planeCells[2], 14, 20, 30) &&
            ToolPlannerCell(planeCells[3], 10, 20, 27) &&
            ToolPlannerCell(planeCells[5], 14, 20, 27),
            "Smart rectangle plane planning returns exact absolute cells including its origin. " +
            planeReason);

        bool wall = SmartBuildAdvancedToolPlanner.TryPlanRectangleWall(
            origin,
            new Vector3i(1, 0, 0),
            4,
            new Vector3i(0, 1, 0),
            3,
            out IReadOnlyList<Vector3i> wallCells,
            out string wallReason);
        Assert(
            wall && wallCells.Count == 10 &&
            wallCells.Any(cell => cell.Equals(origin)) &&
            !wallCells.Any(cell => cell.Equals(new Vector3i(11, 21, 30))) &&
            !wallCells.Any(cell => cell.Equals(new Vector3i(12, 21, 30))) &&
            wallCells.Any(cell => cell.Equals(new Vector3i(13, 22, 30))),
            "Smart rectangle wall planning returns the absolute perimeter while omitting interior cells. " +
            wallReason);

        bool diagonalRectangle = SmartBuildAdvancedToolPlanner.TryPlanRectanglePlane(
            origin,
            new Vector3i(1, 1, 0),
            2,
            new Vector3i(0, 0, 1),
            2,
            out IReadOnlyList<Vector3i> diagonalCells,
            out string diagonalReason);
        bool cappedRectangle = SmartBuildAdvancedToolPlanner.TryPlanRectanglePlane(
            origin,
            new Vector3i(1, 0, 0),
            65,
            new Vector3i(0, 1, 0),
            65,
            out IReadOnlyList<Vector3i> cappedCells,
            out string cappedReason);
        Assert(
            !diagonalRectangle && diagonalCells.Count == 0 &&
            ToolPlannerReasonContains(diagonalReason, "cardinal") &&
            !cappedRectangle && cappedCells.Count == 0 &&
            ToolPlannerReasonContains(cappedReason, "safe limit"),
            "Smart rectangle planning rejects non-cardinal brushes and cell-cap overflow atomically.");
    }

    private static void VerifySmartToolPlannerFloodFill()
    {
        var bounds = new SmartBuildBounds(
            new Vector3i(0, 0, 0),
            new Vector3i(6, 0, 6));
        Func<Vector3i, bool> closedBoundary = cell =>
            cell.y == 0 &&
            (((cell.x == 1 || cell.x == 5) && cell.z >= 1 && cell.z <= 5) ||
             ((cell.z == 1 || cell.z == 5) && cell.x >= 1 && cell.x <= 5));
        bool filled = SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
            new Vector3i(3, 0, 3),
            SmartBuildAxis.Y,
            bounds,
            closedBoundary,
            16,
            out IReadOnlyList<Vector3i> filledCells,
            out string filledReason);
        Assert(
            filled && filledCells.Count == 9 &&
            filledCells.Any(cell => cell.Equals(new Vector3i(3, 0, 3))) &&
            filledCells.All(cell => cell.x >= 2 && cell.x <= 4 && cell.z >= 2 && cell.z <= 4),
            "Smart planar flood fill accepts a conservative enclosed Y-normal region and includes its seed. " +
            filledReason);

        Func<Vector3i, bool> openBoundary = cell =>
            !cell.Equals(new Vector3i(1, 0, 3)) && closedBoundary(cell);
        bool open = SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
            new Vector3i(3, 0, 3),
            SmartBuildAxis.Y,
            bounds,
            openBoundary,
            64,
            out IReadOnlyList<Vector3i> openCells,
            out string openReason);
        bool capped = SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
            new Vector3i(3, 0, 3),
            SmartBuildAxis.Y,
            bounds,
            closedBoundary,
            8,
            out IReadOnlyList<Vector3i> cappedCells,
            out string cappedReason);
        Assert(
            !open && openCells.Count == 0 && ToolPlannerReasonContains(openReason, "open") &&
            !capped && cappedCells.Count == 0 && ToolPlannerReasonContains(cappedReason, "cap"),
            "Smart planar flood fill rejects an open region and a cap breach without returning partial cells.");

        bool xNormal = SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
            new Vector3i(7, 1, 1),
            SmartBuildAxis.X,
            new SmartBuildBounds(new Vector3i(7, 0, 0), new Vector3i(7, 2, 2)),
            cell => cell.y == 0 || cell.y == 2 || cell.z == 0 || cell.z == 2,
            4,
            out IReadOnlyList<Vector3i> xCells,
            out string xReason);
        bool zNormal = SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
            new Vector3i(1, 1, 7),
            SmartBuildAxis.Z,
            new SmartBuildBounds(new Vector3i(0, 0, 7), new Vector3i(2, 2, 7)),
            cell => cell.x == 0 || cell.x == 2 || cell.y == 0 || cell.y == 2,
            4,
            out IReadOnlyList<Vector3i> zCells,
            out string zReason);
        Assert(
            xNormal && xCells.Count == 1 && xCells[0].Equals(new Vector3i(7, 1, 1)) &&
            zNormal && zCells.Count == 1 && zCells[0].Equals(new Vector3i(1, 1, 7)),
            "Smart enclosed flood planning supports all three plane normals. " + xReason + zReason);
    }

    private static void VerifySmartToolPlannerAtomicSceneCopies()
    {
        var scene = new SmartBuildPieceScene(null);
        SmartBuildPiece first = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(0, 0, 0),
            SmartBuildDrawPlane.Camera);
        SmartBuildPiece second = SmartBuildPiece.CreateCuboid(
            null,
            new Vector3i(2, 0, 0),
            SmartBuildDrawPlane.Camera);
        scene.Add(first);
        scene.Add(second);
        scene.SetSelection(new[] { first.Id, second.Id }, second.Id);
        var transforms = new[]
        {
            new SmartBuildGroupTransform(
                new Vector3i(0, 0, 5),
                DecorationEditAxis.Y,
                1),
            new SmartBuildGroupTransform(new Vector3i(10, 0, 0))
        };
        bool copied = scene.TryDuplicateSelectionTransforms(
            transforms,
            out IReadOnlyList<SmartBuildPiece> copies,
            out string copyReason);
        Assert(
            copied && copies.Count == 4 && scene.Count == 6 && scene.SelectionCount == 6 &&
            ToolPlannerCell(first.Origin, 0, 0, 0) &&
            ToolPlannerCell(second.Origin, 2, 0, 0) &&
            ToolPlannerCell(copies[0].Origin, 1, 0, 6) &&
            ToolPlannerCell(copies[1].Origin, 1, 0, 4) &&
            ToolPlannerCell(copies[2].Origin, 10, 0, 0) &&
            ToolPlannerCell(copies[3].Origin, 12, 0, 0) &&
            ReferenceEquals(scene.SelectedPiece, copies[3]),
            "Smart scene transform duplication rotates about one group pivot, moves afterward, and selects originals plus copies. " +
            copyReason);

        int stableCount = scene.Count;
        int[] stableSelection = scene.SelectedPieceIds.ToArray();
        bool identityCopy = scene.TryDuplicateSelectionTransforms(
            new[] { new SmartBuildGroupTransform(new Vector3i(0, 0, 0)) },
            out IReadOnlyList<SmartBuildPiece> identityCopies,
            out string identityReason);
        Assert(
            !identityCopy && identityCopies.Count == 0 &&
            scene.Count == stableCount &&
            scene.SelectedPieceIds.SequenceEqual(stableSelection) &&
            ToolPlannerReasonContains(identityReason, "identity"),
            "Smart scene transform duplication leaves scene and selection untouched after invalid-transform rejection.");

        var cappedScene = new SmartBuildPieceScene(null);
        var cappedPieces = new List<SmartBuildPiece>();
        for (int index = 0; index < SmartBuildPieceScene.MaximumScenePieces - 1; index++)
        {
            SmartBuildPiece piece = SmartBuildPiece.CreateCuboid(
                null,
                new Vector3i(index, 0, 0),
                SmartBuildDrawPlane.Camera);
            cappedScene.Add(piece);
            cappedPieces.Add(piece);
        }

        cappedScene.SetSelection(
            new[] { cappedPieces[0].Id, cappedPieces[1].Id },
            cappedPieces[1].Id);
        Vector3i firstBefore = cappedPieces[0].Origin;
        Vector3i secondBefore = cappedPieces[1].Origin;
        bool overCap = cappedScene.TryDuplicateSelectionTransforms(
            new[] { new SmartBuildGroupTransform(new Vector3i(0, 1, 0)) },
            out IReadOnlyList<SmartBuildPiece> overCapCopies,
            out string overCapReason);
        Assert(
            !overCap && overCapCopies.Count == 0 &&
            cappedScene.Count == SmartBuildPieceScene.MaximumScenePieces - 1 &&
            cappedScene.SelectionCount == 2 &&
            cappedPieces[0].Origin.Equals(firstBefore) &&
            cappedPieces[1].Origin.Equals(secondBefore) &&
            ToolPlannerReasonContains(overCapReason, "512"),
            "Smart scene transform duplication preflights the 512-piece cap and performs no partial copy or transform.");
    }

    private static void VerifySmartToolPlannerRuntimeIntegration()
    {
        string root = FindRepositoryRoot();
        string smartRoot = Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode");
        string session = File.ReadAllText(Path.Combine(smartRoot, "SmartBuildSession.cs"));
        string advanced = File.ReadAllText(Path.Combine(
            smartRoot,
            "SmartBuildSession.AdvancedTools.cs"));
        string sceneTools = File.ReadAllText(Path.Combine(
            smartRoot,
            "SmartBuildPieceScene.AdvancedTools.cs"));

        Assert(
            session.Contains("DrawAdvancedArrayBrushControls();") &&
            advanced.Contains("private void DrawAdvancedArrayBrushControls()") &&
            advanced.Contains("Advanced patterns") &&
            advanced.Contains("new GUIContent(\"Grid\"") &&
            advanced.Contains("CreateAdvancedGridArray();") &&
            advanced.Contains("Kind = SmartBuildEditablePatternKind.Grid") &&
            advanced.Contains("ApplyEditablePattern(definition, \"grid pattern\")"),
            "Smart Builder's live array/brush panel creates one bounded editable Grid node through the atomic scene path.");

        Assert(
            advanced.Contains("DrawAdvancedRadialButton(DecorationEditAxis.X, \"Radial X\")") &&
            advanced.Contains("DrawAdvancedRadialButton(DecorationEditAxis.Y, \"Radial Y\")") &&
            advanced.Contains("DrawAdvancedRadialButton(DecorationEditAxis.Z, \"Radial Z\")") &&
            advanced.Contains("CreateAdvancedRadialArray(axis);") &&
            advanced.Contains("Kind = SmartBuildEditablePatternKind.Radial") &&
            advanced.Contains("RadialAngleStepDegrees = 90f") &&
            advanced.Contains("_advancedRadialRotateCopies"),
            "Smart Builder exposes reachable editable Radial X/Y/Z actions with optional copy orientation.");

        Assert(
            advanced.Contains("new GUIContent(\"Path verts\"") &&
            advanced.Contains("new GUIContent(\"Path cells\"") &&
            advanced.Contains("CreateAdvancedPathArray(SmartBuildPathArrayMode.PolylineVertices)") &&
            advanced.Contains("CreateAdvancedPathArray(SmartBuildPathArrayMode.SteppedCells)") &&
            advanced.Contains("Kind = SmartBuildEditablePatternKind.Polyline") &&
            advanced.Contains("polyline pattern") &&
            advanced.Contains("stepped path pattern"),
            "Smart Builder exposes both reachable editable vertex-polyline and stepped-cell path variants.");

        Assert(
            advanced.Contains("new GUIContent(\"Wall fill\"") &&
            advanced.Contains("new GUIContent(\"Wall brush\"") &&
            advanced.Contains("new GUIContent(\"Plane brush\"") &&
            advanced.Contains("AdvancedRectangleAction.WallFill") &&
            advanced.Contains("AdvancedRectangleAction.WallPerimeter") &&
            advanced.Contains("AdvancedRectangleAction.ActivePlaneFill") &&
            advanced.Contains("TryPlanRectanglePlane(") &&
            advanced.Contains("TryPlanRectangleWall(") &&
            advanced.Contains("ApplyAdvancedCellPattern("),
            "Smart Builder exposes reachable filled wall, perimeter wall brush, and active-plane brush cell planners.");

        Assert(
            advanced.Contains("Flood fill enclosed plane") &&
            advanced.Contains("CreateAdvancedFloodFill();") &&
            advanced.Contains("TryPlanEnclosedPlanarFloodFill(") &&
            advanced.Contains("IsCellOccupied(_scene.Construct, cell)") &&
            advanced.Contains("previewBoundary.Contains") &&
            advanced.Contains("enclosed planar flood fill"),
            "Smart Builder's reachable Flood fill action uses craft/preview boundaries and the conservative enclosed-plane planner.");

        Assert(
            advanced.Contains("_scene.TryCreatePatternFromSelection(") &&
            advanced.Contains("_scene.TryCreateRegionFromSelection(") &&
            advanced.Contains("SmartBuildSceneSnapshot before = CaptureSceneSnapshot()") &&
            advanced.Contains("PushSceneSnapshot(before)") &&
            advanced.Contains("_scene.TryBakeSelectedNode(") &&
            File.Exists(Path.Combine(smartRoot, "SmartBuildPieceScene.Nodes.cs")) &&
            sceneTools.Contains("TryDuplicateSelectionTransforms("),
            "Advanced tools reach atomic editable-node creation, Bake/Dissolve, and one undo snapshot while retaining the legacy transform API for compatibility.");
    }

    private static bool ToolPlannerCell(Vector3i cell, int x, int y, int z) =>
        cell.x == x && cell.y == y && cell.z == z;

    private static string ToolPlannerCellKey(Vector3i cell) =>
        cell.x + ":" + cell.y + ":" + cell.z;

    private static bool ToolPlannerReasonContains(string reason, string expected) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
}
