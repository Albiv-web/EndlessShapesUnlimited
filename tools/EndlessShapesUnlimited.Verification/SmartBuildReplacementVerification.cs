using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Widgets;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using DecoLimitLifter.SmartBuildMode;
using NetInfrastructure;
using UnityEngine;

internal static partial class Program
{
    private static void VerifySmartBuildReplacementTransactions()
    {
        VerifyCompleteRemovalFootprintsAndConditionalDedupe();
        VerifyConditionalReplacementFailsAtomicPreflight();
        VerifyConditionalReplacementPreviewInputGuard();
        VerifyDestructiveUndoRegistrationFailsClosed();
        VerifyConditionalReplacementRuntimeIntegration();
    }

    private static void VerifyCompleteRemovalFootprintsAndConditionalDedupe()
    {
        Quaternion alongX = SmartBuildPlacement.RotationForAxis(SmartBuildAxis.X);
        Vector3i[] metalFootprint =
        {
            new Vector3i(0, 0, 0),
            new Vector3i(1, 0, 0),
            new Vector3i(2, 0, 0),
            new Vector3i(3, 0, 0)
        };
        var metal = new SmartBuildExistingItemSnapshot(
            metalFootprint[0],
            alongX,
            null,
            new Guid("10000000-0000-0000-0000-000000000001"),
            SmartBuildMaterial.Metal,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            metalFootprint);
        var wood = new SmartBuildExistingItemSnapshot(
            new Vector3i(8, 0, 0),
            Quaternion.identity,
            null,
            new Guid("20000000-0000-0000-0000-000000000002"),
            SmartBuildMaterial.Wood,
            SmartBuildShapeDescriptors.CuboidKey,
            1,
            new[] { new Vector3i(8, 0, 0) });
        Dictionary<string, SmartBuildExistingItemSnapshot> craft = SnapshotCraft(metal, wood);
        var volume = SmartBuildVolume.FromBounds(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(8, 0, 0));
        SmartBuildExistingItemMatch match =
            SmartBuildExistingItemMatch.ForMaterialAndShape(
                SmartBuildMaterial.Metal,
                SmartBuildShapeDescriptors.CuboidKey);

        bool planned = SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            volume,
            new[]
            {
                new Vector3i(1, 0, 0),
                new Vector3i(2, 0, 0),
                new Vector3i(2, 0, 0),
                new Vector3i(8, 0, 0)
            },
            cell => craft.TryGetValue(CellKey(cell), out SmartBuildExistingItemSnapshot item)
                ? item
                : null,
            cell => craft.ContainsKey(CellKey(cell)),
            match,
            _ => SmartBlockCandidate.ForTests(4),
            new SmartBuildConditionalReplacementLimits(),
            out SmartBuildConditionalReplacementPlan conditional,
            out string reason);
        SmartBuildPlan commitPlan = conditional?.ToSmartBuildPlan(null, volume);

        Assert(
            planned && conditional != null && conditional.MatchedItemCount == 1 &&
            conditional.Removals[0].TouchedCells.Count == 2 &&
            conditional.RemovedFootprintCells.Count == 4 &&
            conditional.ReplacementFootprintCells.Count == 4 &&
            conditional.Placements.Count == 1 &&
            conditional.Placements[0].Position.Equals(metal.Origin) &&
            Math.Abs(Quaternion.Dot(conditional.Placements[0].Rotation, alongX)) > 0.99999f &&
            commitPlan != null && commitPlan.CanCommit &&
            commitPlan.RemovalItems.Count == 1 && commitPlan.RemovalCells.Count == 1 &&
            commitPlan.RemovalTouchedCells.Count == 2 &&
            commitPlan.RemovalFootprintCells.Count == 4 &&
            SmartBuildExistingItemMatch.ForMaterial(SmartBuildMaterial.Metal).Matches(metal) &&
            SmartBuildExistingItemMatch.ForShape(SmartBuildShapeDescriptors.CuboidKey).Matches(metal) &&
            !match.Matches(wood),
            "Conditional replacement deduplicates touched cells to one complete multi-cell item, preserves its origin/orientation, and exposes exact touched/removal/replacement footprints. " +
            reason);
    }

    private static void VerifyConditionalReplacementFailsAtomicPreflight()
    {
        Quaternion alongX = SmartBuildPlacement.RotationForAxis(SmartBuildAxis.X);
        var first = new SmartBuildExistingItemSnapshot(
            new Vector3i(0, 0, 0),
            alongX,
            null,
            Guid.Empty,
            SmartBuildMaterial.Metal,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            new[] { new Vector3i(0, 0, 0) });
        var outside = new SmartBuildExistingItemSnapshot(
            new Vector3i(2, 0, 0),
            Quaternion.identity,
            null,
            Guid.Empty,
            SmartBuildMaterial.Wood,
            SmartBuildShapeDescriptors.CuboidKey,
            1,
            new[] { new Vector3i(2, 0, 0) });
        Dictionary<string, SmartBuildExistingItemSnapshot> craft = SnapshotCraft(first, outside);
        var volume = SmartBuildVolume.FromBounds(
            null,
            new Vector3i(0, 0, 0),
            new Vector3i(0, 0, 0));

        bool collisionRejected = !SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            volume,
            new[] { new Vector3i(0, 0, 0) },
            cell => craft.TryGetValue(CellKey(cell), out SmartBuildExistingItemSnapshot item)
                ? item
                : null,
            cell => craft.ContainsKey(CellKey(cell)),
            SmartBuildExistingItemMatch.ForMaterial(SmartBuildMaterial.Metal),
            _ => SmartBlockCandidate.ForTests(4),
            new SmartBuildConditionalReplacementLimits(),
            out SmartBuildConditionalReplacementPlan collisionPlan,
            out string collisionReason);

        var second = new SmartBuildExistingItemSnapshot(
            new Vector3i(10, 0, 0),
            alongX,
            null,
            Guid.Empty,
            SmartBuildMaterial.Metal,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            new[] { new Vector3i(10, 0, 0) });
        craft = SnapshotCraft(first, second);
        bool capRejected = !SmartBuildConditionalReplacementPlanner.TryPlanSnapshots(
            volume,
            new[] { new Vector3i(0, 0, 0), new Vector3i(10, 0, 0) },
            cell => craft.TryGetValue(CellKey(cell), out SmartBuildExistingItemSnapshot item)
                ? item
                : null,
            cell => craft.ContainsKey(CellKey(cell)),
            SmartBuildExistingItemMatch.ForMaterial(SmartBuildMaterial.Metal),
            _ => SmartBlockCandidate.ForTests(4),
            new SmartBuildConditionalReplacementLimits { HardItemCap = 1 },
            out SmartBuildConditionalReplacementPlan capPlan,
            out string capReason);

        Assert(
            collisionRejected && collisionPlan == null &&
            collisionReason.Contains("outside the matched removal set") &&
            capRejected && capPlan == null && capReason.Contains("hard cap"),
            "Conditional replacement rejects an outside-item footprint collision and an over-cap match before returning any partial plan.");
    }

    private static void VerifyDestructiveUndoRegistrationFailsClosed()
    {
        var undoOrder = new List<int>();
        ICommand[] destructive =
        {
            new JournalCommandForTests(1, undoOrder),
            new JournalCommandForTests(2, undoOrder),
            new JournalCommandForTests(3, undoOrder)
        };
        bool destructiveFinalized = SmartBuildCommitter.TryFinalizeCommandJournal(
            destructive,
            undoRequired: true,
            _ => throw new InvalidOperationException("test registrar failure"),
            out string destructiveFailure);

        var placementUndo = new List<int>();
        bool placementFinalized = SmartBuildCommitter.TryFinalizeCommandJournal(
            new ICommand[] { new JournalCommandForTests(4, placementUndo) },
            undoRequired: false,
            _ => throw new InvalidOperationException("test registrar failure"),
            out string placementWarning);

        var incompleteUndoOrder = new List<int>();
        ICommand[] incompleteRollback =
        {
            new JournalCommandForTests(10, incompleteUndoOrder),
            new JournalCommandForTests(11, incompleteUndoOrder, throwOnUndo: true),
            new JournalCommandForTests(12, incompleteUndoOrder)
        };
        bool incompleteFinalized = SmartBuildCommitter.TryFinalizeCommandJournal(
            incompleteRollback,
            undoRequired: true,
            _ => throw new InvalidOperationException("second registrar failure"),
            out string incompleteFailure);

        Assert(
            !destructiveFinalized &&
            undoOrder.SequenceEqual(new[] { 3, 2, 1 }) &&
            destructiveFailure.Contains("complete command journal was reversed") &&
            placementFinalized && placementUndo.Count == 0 &&
            placementWarning.Contains("Native undo registration was unavailable") &&
            !incompleteFinalized &&
            incompleteUndoOrder.SequenceEqual(new[] { 12, 11, 10 }) &&
            incompleteFailure.IndexOf("rollback was incomplete", StringComparison.OrdinalIgnoreCase) >= 0 &&
            incompleteFailure.IndexOf("partially modified", StringComparison.OrdinalIgnoreCase) >= 0,
            "Replace/erase fail closed, attempt the entire reverse journal, and truthfully distinguish complete reversal from an exception-caused partial rollback; ordinary placement preserves its existing non-destructive behavior.");
    }

    private static void VerifyConditionalReplacementPreviewInputGuard()
    {
        bool sampled = SmartBuildSession.TryCreateCraftBlockSampleDescriptor(
            new Guid("85ad137b-796a-4869-b685-1a6fc99c9936"),
            SmartBuildMaterial.Metal,
            SmartBlockCandidate.ForTests(4),
            SmartBuildShapeDescriptors.Cuboid,
            rotationReadable: true,
            SmartBuildPlacement.RotationForAxis(SmartBuildAxis.X, -1),
            SmartBuildDrawPlane.Camera,
            out SmartBuildCraftBlockSampleDescriptor sample,
            out string sampleReason);
        Vector3i[] scope =
        {
            new Vector3i(1, 2, 3),
            new Vector3i(2, 2, 3),
            new Vector3i(1, 2, 3)
        };
        SmartBuildConditionalReplacementInputSnapshot captured =
            SmartBuildConditionalReplacementInputSnapshot.Capture(
                SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
                sample,
                SmartBuildMaterial.Alloy,
                SmartBuildShapeDescriptors.CuboidKey,
                4,
                null,
                scope);
        bool unchanged = captured.Matches(
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
            sample,
            SmartBuildMaterial.Alloy,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            null,
            scope.Reverse());
        bool modeChanged = !captured.Matches(
            SmartBuildConditionalReplacementMatchMode.Material,
            sample,
            SmartBuildMaterial.Alloy,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            null,
            scope);
        bool materialChanged = !captured.Matches(
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
            sample,
            SmartBuildMaterial.Wood,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            null,
            scope);
        bool shapeChanged = !captured.Matches(
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
            sample,
            SmartBuildMaterial.Alloy,
            SmartBuildShapeDescriptors.DownSlopeKey,
            4,
            null,
            scope);
        bool lengthChanged = !captured.Matches(
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
            sample,
            SmartBuildMaterial.Alloy,
            SmartBuildShapeDescriptors.CuboidKey,
            3,
            null,
            scope);
        bool scopeChanged = !captured.Matches(
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape,
            sample,
            SmartBuildMaterial.Alloy,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            null,
            scope.Concat(new[] { new Vector3i(9, 9, 9) }));

        Assert(
            sampled && captured != null && unchanged && modeChanged && materialChanged &&
            shapeChanged && lengthChanged && scopeChanged,
            "Conditional replacement preview guards are order-independent but invalidate on match mode, target material/shape/length, or selected scan-footprint changes. " +
            sampleReason);
    }

    private static void VerifyConditionalReplacementRuntimeIntegration()
    {
        string root = FindRepositoryRoot();
        string smartRoot = System.IO.Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode");
        string tools = System.IO.File.ReadAllText(System.IO.Path.Combine(
            smartRoot,
            "SmartBuildSession.ReplacementTools.cs"));
        string session = System.IO.File.ReadAllText(System.IO.Path.Combine(
            smartRoot,
            "SmartBuildSession.cs"));
        string committer = System.IO.File.ReadAllText(System.IO.Path.Combine(
            smartRoot,
            "SmartBuildCommitter.cs"));

        Assert(
            tools.Contains("ConditionalReplacementMatchMode.Material") &&
            tools.Contains("ConditionalReplacementMatchMode.Shape") &&
            tools.Contains("ConditionalReplacementMatchMode.MaterialAndShape") &&
            tools.Contains("ConditionalReplacementMatchMode.ExactDefinition") &&
            tools.Contains("SmartBuildExistingItemMatch.ForMaterial(") &&
            tools.Contains("SmartBuildExistingItemMatch.ForShape(") &&
            tools.Contains("SmartBuildExistingItemMatch.ForMaterialAndShape(") &&
            tools.Contains("SmartBuildExistingItemMatch.ForExactDefinition("),
            "Conditional replacement exposes reachable Material, Shape, Both, and Exact-item source-match choices from the immutable craft-block sample.");
        Assert(
            tools.Contains("SmartBuildConditionalReplacementPlanner.TryBuildCraftPlan(") &&
            tools.Contains("new SmartBuildReplacementTarget(targetSource, targetShape)") &&
            tools.Contains("HardScopeCellCap = SmartBuildLimits.MaximumConditionalScopeCells") &&
            tools.Contains("HardItemCap = SmartBuildLimits.MaximumConditionalItems") &&
            tools.Contains("HardFootprintCellCap = SmartBuildLimits.MaximumConditionalFootprintCells") &&
            tools.Contains("new SmartBuildPieceScene(_scene.Construct)") &&
            tools.Contains("_scene.SelectedPieces.Select(piece => piece.Clone())") &&
            tools.Contains("scopeScene.BuildPreviewWithSources(SourceForPiece)") &&
            tools.Contains("_plan = plan") &&
            tools.Contains("Apply commits one undoable transaction"),
            "The reachable preview action bounds its symmetry-aware scan to cloned selected preview pieces, builds one bounded conditional craft plan from the sampled source and current material/shape target, then hands it to the normal transactional Apply path.");
        Assert(
            tools.Contains("private void DrawDestructivePlanFootprintOverlay()") &&
            tools.Contains("_plan.RemovalTouchedCells") &&
            tools.Contains("_plan.RemovalFootprintCells") &&
            tools.Contains("_conditionalReplacementDetails.ReplacementFootprintCells") &&
            session.Contains("DrawDestructivePlanFootprintOverlay();") &&
            session.Contains("DrawConditionalReplacementControls();") &&
            session.Contains("ClearConditionalReplacementPreview(rebuildNormalPlan: false);") &&
            session.Contains("_plan.RemovalItems.Count") &&
            session.Contains("_plan.RemovalFootprintCells.Count"),
            "Smart Builder draws exact full-item removal and replacement footprints, exposes the conditional controls, clears stale state, and summarizes complete item/cell counts.");
        Assert(
            committer.Contains("undoRequired: true") &&
            committer.Contains("removedItems > 0") &&
            committer.Contains("build.IsUndoRedoEnabled()") &&
            committer.Contains("container.Register(command") &&
            committer.Contains("TryRollBackCommandJournal(") &&
            committer.Contains("ROLLBACK INCOMPLETE") &&
            committer.Contains("Smart Builder replacement failed") &&
            committer.Contains("Smart Builder erase failed"),
            "Runtime replace/erase with actual removals require a live native FtD undo container and report complete versus incomplete rollback truthfully, while empty-overlap Replace remains an ordinary placement.");
    }

    private static Dictionary<string, SmartBuildExistingItemSnapshot> SnapshotCraft(
        params SmartBuildExistingItemSnapshot[] items)
    {
        var craft = new Dictionary<string, SmartBuildExistingItemSnapshot>(StringComparer.Ordinal);
        foreach (SmartBuildExistingItemSnapshot item in items ?? Array.Empty<SmartBuildExistingItemSnapshot>())
        {
            foreach (Vector3i cell in item.FootprintCells)
                craft[CellKey(cell)] = item;
        }

        return craft;
    }

    private static string CellKey(Vector3i cell) =>
        cell.x + ":" + cell.y + ":" + cell.z;

    private sealed class JournalCommandForTests : ICommand
    {
        private readonly int _id;
        private readonly List<int> _undoOrder;
        private readonly bool _throwOnUndo;

        internal JournalCommandForTests(
            int id,
            List<int> undoOrder,
            bool throwOnUndo = false)
        {
            _id = id;
            _undoOrder = undoOrder;
            _throwOnUndo = throwOnUndo;
        }

        public string Name => "Journal verification";

        public IConnectionData Owner { get; set; }

        public GameTime StartTime { get; set; }

        public bool IsFirstExecute { get; set; }

        public ICommand Next { get; set; }

        public void Execute() => Apply();

        public void Apply()
        {
        }

        public void Undo()
        {
            _undoOrder.Add(_id);
            if (_throwOnUndo)
                throw new InvalidOperationException("test undo failure " + _id);
        }

        public string GetDescription() => "Journal verification";

        public ICommand GetLast() => Next == null ? this : Next.GetLast();
    }
}
