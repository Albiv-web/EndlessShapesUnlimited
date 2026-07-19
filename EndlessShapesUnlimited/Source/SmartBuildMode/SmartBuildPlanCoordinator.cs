using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    [Flags]
    internal enum SmartBuildPlanRevisionKind
    {
        None = 0,
        Geometry = 1 << 0,
        Craft = 1 << 1,
        Material = 1 << 2,
        Symmetry = 1 << 3,
        Occupancy = 1 << 4,
        Selection = 1 << 5,
        Presentation = 1 << 6,
        PlanningInputs = Geometry | Craft | Material | Symmetry | Occupancy,
        All = PlanningInputs | Selection | Presentation
    }

    /// <summary>
    /// The normal (non-conditional) plan and its matching preview are published and
    /// cached as one value so a cache hit cannot mix revisions from different builds.
    /// </summary>
    internal sealed class SmartBuildCoordinatedPlan
    {
        internal SmartBuildCoordinatedPlan(
            SmartBuildPlan plan,
            IReadOnlyList<Vector3i> cells,
            IReadOnlyList<IReadOnlyList<Vector3i>> cellSets,
            IReadOnlyList<SmartBuildVolume> volumes)
        {
            Plan = plan;
            Cells = cells ?? Array.Empty<Vector3i>();
            CellSets = cellSets ?? Array.Empty<IReadOnlyList<Vector3i>>();
            Volumes = volumes ?? Array.Empty<SmartBuildVolume>();
        }

        internal SmartBuildPlan Plan { get; }

        internal IReadOnlyList<Vector3i> Cells { get; }

        internal IReadOnlyList<IReadOnlyList<Vector3i>> CellSets { get; }

        internal IReadOnlyList<SmartBuildVolume> Volumes { get; }
    }

    internal sealed class SmartBuildPlanCoordinatorDiagnostics
    {
        internal SmartBuildPlanCoordinatorDiagnostics(
            long geometryRevision,
            long craftRevision,
            long materialRevision,
            long symmetryRevision,
            long occupancyRevision,
            long selectionRevision,
            long presentationRevision,
            long planningPassCount,
            long planReuseCount,
            TimeSpan lastPlanningDuration,
            int nodeCount,
            int cellCount,
            int placementCount,
            SmartBuildPlanRevisionKind lastRevisionKind,
            bool lastRequestReusedPlan,
            bool normalPlanIsCurrent)
        {
            GeometryRevision = geometryRevision;
            CraftRevision = craftRevision;
            MaterialRevision = materialRevision;
            SymmetryRevision = symmetryRevision;
            OccupancyRevision = occupancyRevision;
            SelectionRevision = selectionRevision;
            PresentationRevision = presentationRevision;
            PlanningPassCount = planningPassCount;
            PlanReuseCount = planReuseCount;
            LastPlanningDuration = lastPlanningDuration;
            NodeCount = nodeCount;
            CellCount = cellCount;
            PlacementCount = placementCount;
            LastRevisionKind = lastRevisionKind;
            LastRequestReusedPlan = lastRequestReusedPlan;
            NormalPlanIsCurrent = normalPlanIsCurrent;
        }

        internal long GeometryRevision { get; }

        internal long CraftRevision { get; }

        internal long MaterialRevision { get; }

        internal long SymmetryRevision { get; }

        internal long OccupancyRevision { get; }

        internal long SelectionRevision { get; }

        internal long PresentationRevision { get; }

        internal long PlanningPassCount { get; }

        internal long PlanReuseCount { get; }

        internal TimeSpan LastPlanningDuration { get; }

        internal double LastPlanningMilliseconds => LastPlanningDuration.TotalMilliseconds;

        internal int NodeCount { get; }

        internal int CellCount { get; }

        internal int PlacementCount { get; }

        internal SmartBuildPlanRevisionKind LastRevisionKind { get; }

        internal bool LastRequestReusedPlan { get; }

        internal bool NormalPlanIsCurrent { get; }
    }

    /// <summary>
    /// Owns the normal-plan cache and independent revision domains. Selection and
    /// presentation revisions remain observable without invalidating geometry work.
    /// </summary>
    internal sealed class SmartBuildPlanCoordinator
    {
        private long _geometryRevision;
        private long _craftRevision;
        private long _materialRevision;
        private long _symmetryRevision;
        private long _occupancyRevision;
        private long _selectionRevision;
        private long _presentationRevision;

        private long _plannedGeometryRevision = -1L;
        private long _plannedCraftRevision = -1L;
        private long _plannedMaterialRevision = -1L;
        private long _plannedSymmetryRevision = -1L;
        private long _plannedOccupancyRevision = -1L;

        private SmartBuildCoordinatedPlan _normalPlan;
        private bool _hasNormalPlan;
        private object _observedCraftIdentity;
        private bool _hasObservedCraftIdentity;
        private long _planningPassCount;
        private long _planReuseCount;
        private TimeSpan _lastPlanningDuration;
        private int _nodeCount;
        private int _cellCount;
        private int _placementCount;
        private SmartBuildPlanRevisionKind _lastRevisionKind;
        private bool _lastRequestReusedPlan;

        internal SmartBuildPlanCoordinatorDiagnostics Diagnostics =>
            new SmartBuildPlanCoordinatorDiagnostics(
                _geometryRevision,
                _craftRevision,
                _materialRevision,
                _symmetryRevision,
                _occupancyRevision,
                _selectionRevision,
                _presentationRevision,
                _planningPassCount,
                _planReuseCount,
                _lastPlanningDuration,
                _nodeCount,
                _cellCount,
                _placementCount,
                _lastRevisionKind,
                _lastRequestReusedPlan,
                NormalPlanIsCurrent);

        internal bool NormalPlanIsCurrent =>
            _hasNormalPlan &&
            _plannedGeometryRevision == _geometryRevision &&
            _plannedCraftRevision == _craftRevision &&
            _plannedMaterialRevision == _materialRevision &&
            _plannedSymmetryRevision == _symmetryRevision &&
            _plannedOccupancyRevision == _occupancyRevision;

        internal void RegisterRevision(SmartBuildPlanRevisionKind revisions)
        {
            revisions &= SmartBuildPlanRevisionKind.All;
            if (revisions == SmartBuildPlanRevisionKind.None)
                return;

            if ((revisions & SmartBuildPlanRevisionKind.Geometry) != 0)
                Advance(ref _geometryRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Craft) != 0)
                Advance(ref _craftRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Material) != 0)
                Advance(ref _materialRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Symmetry) != 0)
                Advance(ref _symmetryRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Occupancy) != 0)
                Advance(ref _occupancyRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Selection) != 0)
                Advance(ref _selectionRevision);
            if ((revisions & SmartBuildPlanRevisionKind.Presentation) != 0)
                Advance(ref _presentationRevision);

            _lastRevisionKind = revisions;
            _lastRequestReusedPlan = false;
        }

        internal void ObserveCraftIdentity(object craftIdentity)
        {
            if (_hasObservedCraftIdentity &&
                ReferenceEquals(_observedCraftIdentity, craftIdentity))
            {
                return;
            }

            _observedCraftIdentity = craftIdentity;
            _hasObservedCraftIdentity = true;
            RegisterRevision(SmartBuildPlanRevisionKind.Craft);
        }

        internal bool TryReuseNormalPlan(out SmartBuildCoordinatedPlan plan)
        {
            if (!NormalPlanIsCurrent)
            {
                _lastRequestReusedPlan = false;
                plan = null;
                return false;
            }

            plan = _normalPlan;
            _lastRequestReusedPlan = true;
            Advance(ref _planReuseCount);
            return true;
        }

        internal void RecordNormalPlan(
            SmartBuildCoordinatedPlan plan,
            TimeSpan planningDuration,
            int nodeCount,
            int cellCount,
            int placementCount)
        {
            _normalPlan = plan ?? throw new ArgumentNullException(nameof(plan));
            _hasNormalPlan = true;
            _plannedGeometryRevision = _geometryRevision;
            _plannedCraftRevision = _craftRevision;
            _plannedMaterialRevision = _materialRevision;
            _plannedSymmetryRevision = _symmetryRevision;
            _plannedOccupancyRevision = _occupancyRevision;
            _lastPlanningDuration = planningDuration < TimeSpan.Zero
                ? TimeSpan.Zero
                : planningDuration;
            _nodeCount = Math.Max(0, nodeCount);
            _cellCount = Math.Max(0, cellCount);
            _placementCount = Math.Max(0, placementCount);
            _lastRequestReusedPlan = false;
            Advance(ref _planningPassCount);
        }

        internal void Reset()
        {
            _normalPlan = null;
            _hasNormalPlan = false;
            _plannedGeometryRevision = -1L;
            _plannedCraftRevision = -1L;
            _plannedMaterialRevision = -1L;
            _plannedSymmetryRevision = -1L;
            _plannedOccupancyRevision = -1L;
            _observedCraftIdentity = null;
            _hasObservedCraftIdentity = false;
            _lastRequestReusedPlan = false;
        }

        private static void Advance(ref long revision)
        {
            unchecked
            {
                revision++;
                if (revision < 0L)
                    revision = 0L;
            }
        }
    }
}
