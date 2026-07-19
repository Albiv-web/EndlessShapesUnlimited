using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum DecorationAuditSeverity
    {
        Information,
        Warning,
        Error,
        Critical
    }

    internal enum DecorationAuditCategory
    {
        Snapshot,
        Tether,
        Transform,
        Reference,
        Mesh,
        Duplicate,
        Layer,
        Capacity,
        Serialization
    }

    internal enum DecorationAuditCode
    {
        SnapshotIncomplete,
        DuplicateManagerId,
        DuplicateDecorationId,
        DuplicateStorageKey,
        TetherUnknown,
        TetherMissingBlock,
        TetherOutsideConstruct,
        TetherManagerMismatch,
        TetherUnreadable,
        TetherCoordinateOutOfRange,
        PositionNonFinite,
        PositionOutOfRange,
        ScaleNonFinite,
        ScaleNearZero,
        ScaleOutOfRange,
        OrientationNonFinite,
        OrientationOutOfRange,
        ColorOutOfRange,
        MeshReferenceMissing,
        MeshReferenceUnreadable,
        MaterialReferenceMissing,
        MaterialReferenceUnreadable,
        MeshBoundsInvalid,
        MeshRenderedExtentExceeded,
        MeshVertexCountExceeded,
        MeshTriangleCountExceeded,
        ExactDuplicate,
        SpatialOverlap,
        SpatialOverlapScanLimit,
        LayerReferenceMissing,
        LayerUnused,
        ManagerCapacityWarning,
        ManagerCapacityError,
        ManagerCapacityExceeded,
        SerializationEstimateUncalibrated,
        SerializationLegacyBoundaryExceeded,
        SerializationCapacityWarning,
        SerializationCapacityExceeded,
        SerializationRequiresModBuffer,
        SerializationBufferExceeded,
        SerializationFormatOverLimit
    }

    internal enum DecorationAuditTetherState
    {
        Unknown,
        Valid,
        MissingBlock,
        OutsideConstruct,
        ManagerMismatch,
        Unreadable
    }

    internal enum DecorationAuditReferenceState
    {
        Unknown,
        Valid,
        Missing,
        Unreadable,
        NotApplicable
    }

    internal enum DecorationAuditRepairSafety
    {
        Safe,
        ReviewRequired,
        Destructive
    }

    internal enum DecorationAuditRepairKind
    {
        NormalizeOrientation,
        ClampPosition,
        ResetPosition,
        ClampScale,
        ResetScale,
        ResetOrientation
    }

    internal enum DecorationAuditRepairInclusion
    {
        SafeOnly,
        IncludeReviewRequired,
        IncludeDestructive
    }

    internal enum DecorationAuditRepairApplyStatus
    {
        NothingToApply,
        Applied,
        RejectedStaleSnapshot,
        Rejected,
        Failed
    }

    internal readonly struct DecorationAuditCell : IEquatable<DecorationAuditCell>
    {
        internal DecorationAuditCell(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal int X { get; }

        internal int Y { get; }

        internal int Z { get; }

        public bool Equals(DecorationAuditCell other) =>
            X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) =>
            obj is DecorationAuditCell other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                return (hash * 397) ^ Z;
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }

    internal readonly struct DecorationAuditVector3 : IEquatable<DecorationAuditVector3>
    {
        internal DecorationAuditVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal double X { get; }

        internal double Y { get; }

        internal double Z { get; }

        internal bool IsFinite =>
            IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

        internal double MaximumAbsoluteComponent =>
            Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));

        internal DecorationAuditVector3 Clamp(double minimum, double maximum) =>
            new DecorationAuditVector3(
                ClampValue(X, minimum, maximum),
                ClampValue(Y, minimum, maximum),
                ClampValue(Z, minimum, maximum));

        internal DecorationAuditVector3 NormalizeDegrees() =>
            new DecorationAuditVector3(
                NormalizeDegrees(X),
                NormalizeDegrees(Y),
                NormalizeDegrees(Z));

        public bool Equals(DecorationAuditVector3 other) =>
            SameBits(X, other.X) && SameBits(Y, other.Y) && SameBits(Z, other.Z);

        public override bool Equals(object obj) =>
            obj is DecorationAuditVector3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BitConverter.DoubleToInt64Bits(X).GetHashCode();
                hash = (hash * 397) ^ BitConverter.DoubleToInt64Bits(Y).GetHashCode();
                return (hash * 397) ^ BitConverter.DoubleToInt64Bits(Z).GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(
                CultureInfo.InvariantCulture,
                "({0:R}, {1:R}, {2:R})",
                X,
                Y,
                Z);

        private static bool IsFiniteValue(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static double ClampValue(double value, double minimum, double maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;

        private static double NormalizeDegrees(double value)
        {
            if (!IsFiniteValue(value))
                return value;

            double normalized = value % 360d;
            if (normalized >= 180d)
                normalized -= 360d;
            else if (normalized < -180d)
                normalized += 360d;
            return normalized == 0d ? 0d : normalized;
        }

        private static bool SameBits(double left, double right) =>
            BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
    }

    internal sealed class DecorationAuditMeshMetadata
    {
        internal DecorationAuditMeshMetadata(
            DecorationAuditReferenceState referenceState,
            bool metricsAvailable = false,
            DecorationAuditVector3 localSize = default(DecorationAuditVector3),
            long vertexCount = -1L,
            long triangleCount = -1L,
            string diagnostic = null,
            DecorationAuditVector3 localCenter = default(DecorationAuditVector3))
        {
            ReferenceState = referenceState;
            MetricsAvailable = metricsAvailable;
            LocalSize = localSize;
            LocalCenter = localCenter;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            Diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? string.Empty
                : diagnostic.Trim();
        }

        internal DecorationAuditReferenceState ReferenceState { get; }

        internal bool MetricsAvailable { get; }

        internal DecorationAuditVector3 LocalSize { get; }

        internal DecorationAuditVector3 LocalCenter { get; }

        internal long VertexCount { get; }

        internal long TriangleCount { get; }

        internal string Diagnostic { get; }

        internal static DecorationAuditMeshMetadata Unknown { get; } =
            new DecorationAuditMeshMetadata(DecorationAuditReferenceState.Unknown);
    }

    internal sealed class DecorationAuditLayerSnapshot
    {
        internal DecorationAuditLayerSnapshot(
            string name,
            bool visible = true,
            bool locked = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A layer name is required.", nameof(name));
            Name = name.Trim();
            Visible = visible;
            Locked = locked;
        }

        internal string Name { get; }

        internal bool Visible { get; }

        internal bool Locked { get; }
    }

    internal sealed class DecorationAuditDecorationSnapshot
    {
        internal DecorationAuditDecorationSnapshot(
            string decorationId,
            DecorationAuditCell tether,
            DecorationAuditTetherState tetherState,
            DecorationAuditVector3 position,
            DecorationAuditVector3 scale,
            DecorationAuditVector3 orientation,
            Guid meshGuid,
            int color,
            string displayName = null,
            string storageKey = null,
            string overlapKey = null,
            Guid materialReplacement = default(Guid),
            bool hideOriginalMesh = false,
            DecorationAuditMeshMetadata meshMetadata = null,
            DecorationAuditReferenceState materialReferenceState =
                DecorationAuditReferenceState.Unknown,
            string layerName = null,
            bool workspaceLocked = false)
        {
            if (string.IsNullOrWhiteSpace(decorationId))
                throw new ArgumentException("A stable decoration id is required.", nameof(decorationId));

            DecorationId = decorationId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? DecorationId
                : displayName.Trim();
            StorageKey = NormalizeOptionalKey(storageKey);
            OverlapKey = NormalizeOptionalKey(overlapKey);
            Tether = tether;
            TetherState = tetherState;
            Position = position;
            Scale = scale;
            Orientation = orientation;
            MeshGuid = meshGuid;
            Color = color;
            MaterialReplacement = materialReplacement;
            HideOriginalMesh = hideOriginalMesh;
            MeshMetadata = meshMetadata ?? DecorationAuditMeshMetadata.Unknown;
            MaterialReferenceState = materialReferenceState;
            LayerName = NormalizeOptionalKey(layerName);
            WorkspaceLocked = workspaceLocked;
        }

        internal string DecorationId { get; }

        internal string DisplayName { get; }

        internal string StorageKey { get; }

        internal string OverlapKey { get; }

        internal DecorationAuditCell Tether { get; }

        internal DecorationAuditTetherState TetherState { get; }

        internal DecorationAuditVector3 Position { get; }

        internal DecorationAuditVector3 Scale { get; }

        internal DecorationAuditVector3 Orientation { get; }

        internal Guid MeshGuid { get; }

        internal int Color { get; }

        internal Guid MaterialReplacement { get; }

        internal bool HideOriginalMesh { get; }

        internal DecorationAuditMeshMetadata MeshMetadata { get; }

        internal DecorationAuditReferenceState MaterialReferenceState { get; }

        internal string LayerName { get; }

        internal bool WorkspaceLocked { get; }

        private static string NormalizeOptionalKey(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    internal sealed class DecorationAuditManagerSnapshot
    {
        private readonly ReadOnlyCollection<DecorationAuditDecorationSnapshot> _decorations;

        internal DecorationAuditManagerSnapshot(
            string managerId,
            IEnumerable<DecorationAuditDecorationSnapshot> decorations,
            string displayName = null,
            int reportedDecorationCount = -1,
            int capacityLimit = 0,
            bool captureComplete = true,
            string captureDiagnostic = null)
        {
            if (string.IsNullOrWhiteSpace(managerId))
                throw new ArgumentException("A stable manager id is required.", nameof(managerId));
            if (decorations == null)
                throw new ArgumentNullException(nameof(decorations));

            ManagerId = managerId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? ManagerId
                : displayName.Trim();
            DecorationAuditDecorationSnapshot[] copied = decorations.ToArray();
            if (copied.Any(decoration => decoration == null))
                throw new ArgumentException("Decoration snapshots cannot contain null entries.", nameof(decorations));
            _decorations = Array.AsReadOnly(copied);
            ReportedDecorationCount = reportedDecorationCount;
            CapacityLimit = capacityLimit;
            CaptureComplete = captureComplete;
            CaptureDiagnostic = string.IsNullOrWhiteSpace(captureDiagnostic)
                ? string.Empty
                : captureDiagnostic.Trim();
        }

        internal string ManagerId { get; }

        internal string DisplayName { get; }

        internal IReadOnlyList<DecorationAuditDecorationSnapshot> Decorations => _decorations;

        internal int ReportedDecorationCount { get; }

        internal int EffectiveDecorationCount =>
            Math.Max(_decorations.Count, Math.Max(0, ReportedDecorationCount));

        internal int CapacityLimit { get; }

        internal bool CaptureComplete { get; }

        internal string CaptureDiagnostic { get; }
    }

    internal sealed class DecorationAuditSerializationSnapshot
    {
        internal DecorationAuditSerializationSnapshot(
            bool available,
            bool exact,
            bool uncalibrated,
            string wireFormat,
            ulong peakHeaderBytes,
            ulong peakDataBytes,
            ulong legacyHeaderMaximum,
            ulong legacyDataMaximum,
            ulong maximumHeaderBytes,
            ulong maximumDataBytes,
            ulong largestBlueprintStreamBytes,
            ulong maximumSaveBufferBytes,
            bool requiresModBuffer)
        {
            Available = available;
            Exact = exact;
            Uncalibrated = uncalibrated;
            WireFormat = string.IsNullOrWhiteSpace(wireFormat) ? "Unknown" : wireFormat.Trim();
            PeakHeaderBytes = peakHeaderBytes;
            PeakDataBytes = peakDataBytes;
            LegacyHeaderMaximum = legacyHeaderMaximum;
            LegacyDataMaximum = legacyDataMaximum;
            MaximumHeaderBytes = maximumHeaderBytes;
            MaximumDataBytes = maximumDataBytes;
            LargestBlueprintStreamBytes = largestBlueprintStreamBytes;
            MaximumSaveBufferBytes = maximumSaveBufferBytes;
            RequiresModBuffer = requiresModBuffer;
        }

        internal bool Available { get; }

        internal bool Exact { get; }

        internal bool Uncalibrated { get; }

        internal string WireFormat { get; }

        internal ulong PeakHeaderBytes { get; }

        internal ulong PeakDataBytes { get; }

        internal ulong LegacyHeaderMaximum { get; }

        internal ulong LegacyDataMaximum { get; }

        internal ulong MaximumHeaderBytes { get; }

        internal ulong MaximumDataBytes { get; }

        internal ulong LargestBlueprintStreamBytes { get; }

        internal ulong MaximumSaveBufferBytes { get; }

        internal bool RequiresModBuffer { get; }

        internal static DecorationAuditSerializationSnapshot Unavailable { get; } =
            new DecorationAuditSerializationSnapshot(
                available: false,
                exact: false,
                uncalibrated: true,
                wireFormat: "Unknown",
                peakHeaderBytes: 0UL,
                peakDataBytes: 0UL,
                legacyHeaderMaximum: 0UL,
                legacyDataMaximum: 0UL,
                maximumHeaderBytes: 0UL,
                maximumDataBytes: 0UL,
                largestBlueprintStreamBytes: 0UL,
                maximumSaveBufferBytes: 0UL,
                requiresModBuffer: false);
    }

    internal sealed class DecorationAuditCraftSnapshot
    {
        private readonly ReadOnlyCollection<DecorationAuditManagerSnapshot> _managers;
        private readonly ReadOnlyCollection<DecorationAuditLayerSnapshot> _layers;

        internal DecorationAuditCraftSnapshot(
            string sourceId,
            IEnumerable<DecorationAuditManagerSnapshot> managers,
            DecorationAuditSerializationSnapshot serialization = null,
            string displayName = null,
            IEnumerable<DecorationAuditLayerSnapshot> layers = null)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("A stable audit source id is required.", nameof(sourceId));
            if (managers == null)
                throw new ArgumentNullException(nameof(managers));

            SourceId = sourceId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? SourceId
                : displayName.Trim();
            DecorationAuditManagerSnapshot[] copied = managers.ToArray();
            if (copied.Any(manager => manager == null))
                throw new ArgumentException("Manager snapshots cannot contain null entries.", nameof(managers));
            _managers = Array.AsReadOnly(copied);
            DecorationAuditLayerSnapshot[] copiedLayers =
                (layers ?? Enumerable.Empty<DecorationAuditLayerSnapshot>()).ToArray();
            if (copiedLayers.Any(layer => layer == null))
                throw new ArgumentException("Layer snapshots cannot contain null entries.", nameof(layers));
            _layers = Array.AsReadOnly(copiedLayers);
            Serialization = serialization ?? DecorationAuditSerializationSnapshot.Unavailable;
        }

        internal string SourceId { get; }

        internal string DisplayName { get; }

        internal IReadOnlyList<DecorationAuditManagerSnapshot> Managers => _managers;

        internal IReadOnlyList<DecorationAuditLayerSnapshot> Layers => _layers;

        internal DecorationAuditSerializationSnapshot Serialization { get; }
    }

    internal sealed class DecorationAuditOptions
    {
        internal DecorationAuditOptions(
            int maximumAbsoluteTetherCoordinate = 1_000_000,
            double maximumAbsolutePosition = 10d,
            double minimumNonZeroScale = 0.0001d,
            double maximumAbsoluteScale = 10_000d,
            double maximumAbsoluteOrientationDegrees = 360d,
            double overlapTolerance = 0.001d,
            int defaultManagerCapacity = 100_000,
            double capacityWarningRatio = 0.80d,
            double capacityErrorRatio = 0.95d,
            double serializationWarningRatio = 0.85d,
            bool reportUnknownTethers = false,
            double maximumRenderedMeshExtent = 1_000d,
            long maximumMeshVertices = 1_000_000L,
            long maximumMeshTriangles = 2_000_000L,
            int maximumOverlapPairChecks = 2_000_000)
        {
            if (maximumAbsoluteTetherCoordinate < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumAbsoluteTetherCoordinate));
            RequireFinitePositive(maximumAbsolutePosition, nameof(maximumAbsolutePosition));
            RequireFinitePositive(minimumNonZeroScale, nameof(minimumNonZeroScale));
            RequireFinitePositive(maximumAbsoluteScale, nameof(maximumAbsoluteScale));
            RequireFinitePositive(maximumAbsoluteOrientationDegrees, nameof(maximumAbsoluteOrientationDegrees));
            RequireFinitePositive(overlapTolerance, nameof(overlapTolerance));
            if (minimumNonZeroScale > maximumAbsoluteScale)
                throw new ArgumentException("The minimum scale cannot exceed the maximum scale.");
            if (defaultManagerCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(defaultManagerCapacity));
            RequireRatio(capacityWarningRatio, nameof(capacityWarningRatio));
            RequireRatio(capacityErrorRatio, nameof(capacityErrorRatio));
            RequireRatio(serializationWarningRatio, nameof(serializationWarningRatio));
            if (capacityWarningRatio >= capacityErrorRatio)
                throw new ArgumentException("The capacity warning ratio must be below the error ratio.");
            RequireFinitePositive(maximumRenderedMeshExtent, nameof(maximumRenderedMeshExtent));
            if (maximumMeshVertices < 1L)
                throw new ArgumentOutOfRangeException(nameof(maximumMeshVertices));
            if (maximumMeshTriangles < 1L)
                throw new ArgumentOutOfRangeException(nameof(maximumMeshTriangles));
            if (maximumOverlapPairChecks < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumOverlapPairChecks));

            MaximumAbsoluteTetherCoordinate = maximumAbsoluteTetherCoordinate;
            MaximumAbsolutePosition = maximumAbsolutePosition;
            MinimumNonZeroScale = minimumNonZeroScale;
            MaximumAbsoluteScale = maximumAbsoluteScale;
            MaximumAbsoluteOrientationDegrees = maximumAbsoluteOrientationDegrees;
            OverlapTolerance = overlapTolerance;
            DefaultManagerCapacity = defaultManagerCapacity;
            CapacityWarningRatio = capacityWarningRatio;
            CapacityErrorRatio = capacityErrorRatio;
            SerializationWarningRatio = serializationWarningRatio;
            ReportUnknownTethers = reportUnknownTethers;
            MaximumRenderedMeshExtent = maximumRenderedMeshExtent;
            MaximumMeshVertices = maximumMeshVertices;
            MaximumMeshTriangles = maximumMeshTriangles;
            MaximumOverlapPairChecks = maximumOverlapPairChecks;
        }

        internal int MaximumAbsoluteTetherCoordinate { get; }

        internal double MaximumAbsolutePosition { get; }

        internal double MinimumNonZeroScale { get; }

        internal double MaximumAbsoluteScale { get; }

        internal double MaximumAbsoluteOrientationDegrees { get; }

        internal double OverlapTolerance { get; }

        internal int DefaultManagerCapacity { get; }

        internal double CapacityWarningRatio { get; }

        internal double CapacityErrorRatio { get; }

        internal double SerializationWarningRatio { get; }

        internal bool ReportUnknownTethers { get; }

        internal double MaximumRenderedMeshExtent { get; }

        internal long MaximumMeshVertices { get; }

        internal long MaximumMeshTriangles { get; }

        internal int MaximumOverlapPairChecks { get; }

        internal static DecorationAuditOptions Default { get; } = new DecorationAuditOptions();

        private static void RequireFinitePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireRatio(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d || value >= 1d)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    internal sealed class DecorationAuditRepairOperation
    {
        internal DecorationAuditRepairOperation(
            DecorationAuditRepairKind kind,
            DecorationAuditRepairSafety safety,
            string managerId,
            string decorationId,
            DecorationAuditVector3 expectedValue,
            DecorationAuditVector3 replacementValue,
            string rationale)
        {
            Kind = kind;
            Safety = safety;
            ManagerId = managerId ?? string.Empty;
            DecorationId = decorationId ?? string.Empty;
            ExpectedValue = expectedValue;
            ReplacementValue = replacementValue;
            Rationale = rationale ?? string.Empty;
            OperationId = string.Join(
                ":",
                kind.ToString(),
                ManagerId,
                DecorationId);
        }

        internal string OperationId { get; }

        internal DecorationAuditRepairKind Kind { get; }

        internal DecorationAuditRepairSafety Safety { get; }

        internal string ManagerId { get; }

        internal string DecorationId { get; }

        internal DecorationAuditVector3 ExpectedValue { get; }

        internal DecorationAuditVector3 ReplacementValue { get; }

        internal string Rationale { get; }
    }

    internal sealed class DecorationAuditRepairSuggestion
    {
        internal DecorationAuditRepairSuggestion(
            DecorationAuditRepairSafety safety,
            string description,
            DecorationAuditRepairOperation operation = null)
        {
            Safety = safety;
            Description = description ?? string.Empty;
            Operation = operation;
        }

        internal DecorationAuditRepairSafety Safety { get; }

        internal string Description { get; }

        internal DecorationAuditRepairOperation Operation { get; }
    }

    internal sealed class DecorationAuditFinding
    {
        private readonly ReadOnlyCollection<string> _relatedDecorationIds;

        internal DecorationAuditFinding(
            DecorationAuditSeverity severity,
            DecorationAuditCategory category,
            DecorationAuditCode code,
            string title,
            string detail,
            string managerId = null,
            string decorationId = null,
            IEnumerable<string> relatedDecorationIds = null,
            DecorationAuditRepairSuggestion suggestedRepair = null)
        {
            Severity = severity;
            Category = category;
            Code = code;
            Title = title ?? code.ToString();
            Detail = detail ?? string.Empty;
            ManagerId = managerId ?? string.Empty;
            DecorationId = decorationId ?? string.Empty;
            string[] related = (relatedDecorationIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            _relatedDecorationIds = Array.AsReadOnly(related);
            SuggestedRepair = suggestedRepair;
            FindingId = BuildFindingId(code, ManagerId, DecorationId, related);
        }

        internal string FindingId { get; }

        internal DecorationAuditSeverity Severity { get; }

        internal DecorationAuditCategory Category { get; }

        internal DecorationAuditCode Code { get; }

        internal string Title { get; }

        internal string Detail { get; }

        internal string ManagerId { get; }

        internal string DecorationId { get; }

        internal IReadOnlyList<string> RelatedDecorationIds => _relatedDecorationIds;

        internal DecorationAuditRepairSuggestion SuggestedRepair { get; }

        private static string BuildFindingId(
            DecorationAuditCode code,
            string managerId,
            string decorationId,
            IEnumerable<string> related)
        {
            string target = string.IsNullOrEmpty(decorationId)
                ? string.Join(",", related)
                : decorationId;
            return code + ":" + managerId + ":" + target;
        }
    }

    internal sealed class DecorationAuditSummary
    {
        internal DecorationAuditSummary(
            int managerCount,
            long decorationCount,
            int informationCount,
            int warningCount,
            int errorCount,
            int criticalCount,
            int suspiciousTetherCount,
            int transformFindingCount,
            int referenceFindingCount,
            int meshFindingCount,
            int duplicateGroupCount,
            int layerFindingCount,
            int capacityHotspotCount,
            int serializationFindingCount)
        {
            ManagerCount = managerCount;
            DecorationCount = decorationCount;
            InformationCount = informationCount;
            WarningCount = warningCount;
            ErrorCount = errorCount;
            CriticalCount = criticalCount;
            SuspiciousTetherCount = suspiciousTetherCount;
            TransformFindingCount = transformFindingCount;
            ReferenceFindingCount = referenceFindingCount;
            MeshFindingCount = meshFindingCount;
            DuplicateGroupCount = duplicateGroupCount;
            LayerFindingCount = layerFindingCount;
            CapacityHotspotCount = capacityHotspotCount;
            SerializationFindingCount = serializationFindingCount;
        }

        internal int ManagerCount { get; }

        internal long DecorationCount { get; }

        internal int InformationCount { get; }

        internal int WarningCount { get; }

        internal int ErrorCount { get; }

        internal int CriticalCount { get; }

        internal int SuspiciousTetherCount { get; }

        internal int TransformFindingCount { get; }

        internal int ReferenceFindingCount { get; }

        internal int MeshFindingCount { get; }

        internal int DuplicateGroupCount { get; }

        internal int LayerFindingCount { get; }

        internal int CapacityHotspotCount { get; }

        internal int SerializationFindingCount { get; }

        internal bool HasErrors => ErrorCount > 0 || CriticalCount > 0;
    }

    internal sealed class DecorationAuditReport
    {
        private readonly ReadOnlyCollection<DecorationAuditFinding> _findings;

        internal DecorationAuditReport(
            DecorationAuditCraftSnapshot source,
            string snapshotFingerprint,
            IEnumerable<DecorationAuditFinding> findings,
            DecorationAuditSummary summary)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            SnapshotFingerprint = snapshotFingerprint ?? string.Empty;
            _findings = Array.AsReadOnly((findings ?? Enumerable.Empty<DecorationAuditFinding>()).ToArray());
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        }

        internal DecorationAuditCraftSnapshot Source { get; }

        internal string SnapshotFingerprint { get; }

        internal IReadOnlyList<DecorationAuditFinding> Findings => _findings;

        internal DecorationAuditSummary Summary { get; }

        internal DecorationAuditRepairPlan CreateRepairPlan(
            DecorationAuditRepairInclusion inclusion = DecorationAuditRepairInclusion.SafeOnly) =>
            DecorationAuditRepairPlanner.Create(this, inclusion);
    }

    internal sealed class DecorationAuditRepairPlan
    {
        private readonly ReadOnlyCollection<DecorationAuditRepairOperation> _operations;

        internal DecorationAuditRepairPlan(
            string sourceId,
            string snapshotFingerprint,
            DecorationAuditRepairInclusion inclusion,
            IEnumerable<DecorationAuditRepairOperation> operations,
            int excludedSuggestionCount)
        {
            SourceId = sourceId ?? string.Empty;
            SnapshotFingerprint = snapshotFingerprint ?? string.Empty;
            Inclusion = inclusion;
            _operations = Array.AsReadOnly((operations ?? Enumerable.Empty<DecorationAuditRepairOperation>()).ToArray());
            ExcludedSuggestionCount = Math.Max(0, excludedSuggestionCount);
        }

        internal string SourceId { get; }

        internal string SnapshotFingerprint { get; }

        internal DecorationAuditRepairInclusion Inclusion { get; }

        internal IReadOnlyList<DecorationAuditRepairOperation> Operations => _operations;

        internal int ExcludedSuggestionCount { get; }

        internal bool IsDryRun => true;

        internal DecorationAuditRepairApplyResult ApplyExplicitly(
            IDecorationAuditRepairAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (_operations.Count == 0)
            {
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.NothingToApply,
                    0,
                    "The dry-run plan contains no approved operations.");
            }

            string currentFingerprint = adapter.GetCurrentSnapshotFingerprint(SourceId);
            if (!string.Equals(
                    currentFingerprint,
                    SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.RejectedStaleSnapshot,
                    0,
                    "The craft changed after the audit; capture a fresh audit before applying repairs.");
            }

            return adapter.ApplyAtomically(this) ??
                   new DecorationAuditRepairApplyResult(
                       DecorationAuditRepairApplyStatus.Failed,
                       0,
                       "The repair adapter returned no result.");
        }
    }

    internal interface IDecorationAuditRepairAdapter
    {
        // This method must be read-only. It protects a plan from being applied to a
        // craft that changed after the dry-run was created.
        string GetCurrentSnapshotFingerprint(string sourceId);

        // This is the sole mutation boundary. Implementations must validate all
        // expected values and either commit every operation or leave the craft
        // unchanged.
        DecorationAuditRepairApplyResult ApplyAtomically(DecorationAuditRepairPlan plan);
    }

    internal sealed class DecorationAuditRepairApplyResult
    {
        internal DecorationAuditRepairApplyResult(
            DecorationAuditRepairApplyStatus status,
            int appliedOperationCount,
            string message)
        {
            Status = status;
            AppliedOperationCount = Math.Max(0, appliedOperationCount);
            Message = message ?? string.Empty;
        }

        internal DecorationAuditRepairApplyStatus Status { get; }

        internal int AppliedOperationCount { get; }

        internal string Message { get; }

        internal bool Applied => Status == DecorationAuditRepairApplyStatus.Applied;
    }

    internal static class DecorationAuditEngine
    {
        private sealed class IndexedManager
        {
            internal IndexedManager(DecorationAuditManagerSnapshot snapshot, int index)
            {
                Snapshot = snapshot;
                Index = index;
            }

            internal DecorationAuditManagerSnapshot Snapshot { get; }

            internal int Index { get; }
        }

        private sealed class IndexedDecoration
        {
            internal IndexedDecoration(DecorationAuditDecorationSnapshot snapshot, int index)
            {
                Snapshot = snapshot;
                Index = index;
            }

            internal DecorationAuditDecorationSnapshot Snapshot { get; }

            internal int Index { get; }
        }

        private sealed class OverlapProxy
        {
            internal OverlapProxy(
                DecorationAuditDecorationSnapshot snapshot,
                int index,
                string exactKey,
                DecorationAuditVector3 center,
                DecorationAuditVector3 halfExtents,
                AuditMatrix3 rotation,
                DecorationAuditVector3 minimum,
                DecorationAuditVector3 maximum,
                bool hasExactMeshBounds)
            {
                Snapshot = snapshot;
                Index = index;
                ExactKey = exactKey;
                Center = center;
                HalfExtents = halfExtents;
                Rotation = rotation;
                Minimum = minimum;
                Maximum = maximum;
                HasExactMeshBounds = hasExactMeshBounds;
            }

            internal DecorationAuditDecorationSnapshot Snapshot { get; }

            internal int Index { get; }

            internal string ExactKey { get; }

            internal DecorationAuditVector3 Center { get; }

            internal DecorationAuditVector3 HalfExtents { get; }

            internal AuditMatrix3 Rotation { get; }

            internal DecorationAuditVector3 Minimum { get; }

            internal DecorationAuditVector3 Maximum { get; }

            internal bool HasExactMeshBounds { get; }
        }

        private readonly struct AuditMatrix3
        {
            internal AuditMatrix3(
                double m00,
                double m01,
                double m02,
                double m10,
                double m11,
                double m12,
                double m20,
                double m21,
                double m22)
            {
                M00 = m00;
                M01 = m01;
                M02 = m02;
                M10 = m10;
                M11 = m11;
                M12 = m12;
                M20 = m20;
                M21 = m21;
                M22 = m22;
            }

            internal double M00 { get; }
            internal double M01 { get; }
            internal double M02 { get; }
            internal double M10 { get; }
            internal double M11 { get; }
            internal double M12 { get; }
            internal double M20 { get; }
            internal double M21 { get; }
            internal double M22 { get; }

            internal static AuditMatrix3 Identity { get; } =
                new AuditMatrix3(1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d);

            internal DecorationAuditVector3 Transform(DecorationAuditVector3 value) =>
                new DecorationAuditVector3(
                    M00 * value.X + M01 * value.Y + M02 * value.Z,
                    M10 * value.X + M11 * value.Y + M12 * value.Z,
                    M20 * value.X + M21 * value.Y + M22 * value.Z);

            internal DecorationAuditVector3 Axis(int index)
            {
                switch (index)
                {
                    case 0:
                        return new DecorationAuditVector3(M00, M10, M20);
                    case 1:
                        return new DecorationAuditVector3(M01, M11, M21);
                    default:
                        return new DecorationAuditVector3(M02, M12, M22);
                }
            }

            internal static AuditMatrix3 Multiply(AuditMatrix3 left, AuditMatrix3 right) =>
                new AuditMatrix3(
                    left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
                    left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
                    left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
                    left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
                    left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
                    left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
                    left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
                    left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
                    left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22);
        }

        private sealed class OverlapDisjointSet
        {
            private readonly int[] _parent;
            private readonly byte[] _rank;

            internal OverlapDisjointSet(int count)
            {
                _parent = Enumerable.Range(0, count).ToArray();
                _rank = new byte[count];
            }

            internal int Find(int value)
            {
                int root = value;
                while (_parent[root] != root)
                    root = _parent[root];
                while (_parent[value] != value)
                {
                    int next = _parent[value];
                    _parent[value] = root;
                    value = next;
                }
                return root;
            }

            internal void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot == rightRoot)
                    return;
                if (_rank[leftRoot] < _rank[rightRoot])
                {
                    _parent[leftRoot] = rightRoot;
                    return;
                }
                _parent[rightRoot] = leftRoot;
                if (_rank[leftRoot] == _rank[rightRoot])
                    _rank[leftRoot]++;
            }
        }

        internal static DecorationAuditReport Scan(
            DecorationAuditCraftSnapshot source,
            DecorationAuditOptions options = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            options = options ?? DecorationAuditOptions.Default;

            var findings = new List<DecorationAuditFinding>();
            IndexedManager[] managers = source.Managers
                .Select((manager, index) => new IndexedManager(manager, index))
                .OrderBy(item => item.Snapshot.ManagerId, StringComparer.Ordinal)
                .ThenBy(item => item.Index)
                .ToArray();

            AddDuplicateManagerFindings(managers, findings);
            foreach (IndexedManager indexedManager in managers)
                ScanManager(indexedManager.Snapshot, options, findings);
            ScanLayers(source, findings);
            ScanSerialization(source.Serialization, options, findings);

            DecorationAuditFinding[] ordered = findings
                .OrderByDescending(finding => finding.Severity)
                .ThenBy(finding => finding.Category)
                .ThenBy(finding => finding.Code)
                .ThenBy(finding => finding.ManagerId, StringComparer.Ordinal)
                .ThenBy(finding => finding.DecorationId, StringComparer.Ordinal)
                .ThenBy(finding => finding.FindingId, StringComparer.Ordinal)
                .ToArray();
            return new DecorationAuditReport(
                source,
                ComputeSnapshotFingerprint(source),
                ordered,
                BuildSummary(source, ordered));
        }

        internal static string ComputeSnapshotFingerprint(DecorationAuditCraftSnapshot source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var canonical = new StringBuilder();
            AppendPart(canonical, source.SourceId);
            IEnumerable<DecorationAuditManagerSnapshot> orderedManagers =
                source.Managers
                    .GroupBy(item => item.ManagerId, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .SelectMany(group => group.Count() == 1
                        ? (IEnumerable<DecorationAuditManagerSnapshot>)group
                        : group.OrderBy(
                            ComputeManagerOrderingDigest,
                            StringComparer.Ordinal));
            foreach (DecorationAuditManagerSnapshot manager in orderedManagers)
            {
                AppendManagerFingerprint(canonical, manager);
            }

            DecorationAuditSerializationSnapshot serialization = source.Serialization;
            AppendPart(canonical, serialization.Available ? "1" : "0");
            AppendPart(canonical, serialization.Exact ? "1" : "0");
            AppendPart(canonical, serialization.Uncalibrated ? "1" : "0");
            AppendPart(canonical, serialization.WireFormat);
            AppendPart(canonical, serialization.PeakHeaderBytes.ToString(CultureInfo.InvariantCulture));
            AppendPart(canonical, serialization.PeakDataBytes.ToString(CultureInfo.InvariantCulture));
            AppendPart(canonical, serialization.LargestBlueprintStreamBytes.ToString(CultureInfo.InvariantCulture));
            foreach (DecorationAuditLayerSnapshot layer in source.Layers
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                AppendPart(canonical, layer.Name);
                AppendPart(canonical, layer.Visible ? "1" : "0");
                AppendPart(canonical, layer.Locked ? "1" : "0");
            }

            return ComputeCanonicalDigest(canonical);
        }

        private static void AddDuplicateManagerFindings(
            IEnumerable<IndexedManager> managers,
            ICollection<DecorationAuditFinding> findings)
        {
            foreach (IGrouping<string, IndexedManager> group in managers
                         .GroupBy(item => item.Snapshot.ManagerId, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Snapshot,
                    DecorationAuditCode.DuplicateManagerId,
                    "Duplicate manager id",
                    "The snapshot contains multiple managers named '" + group.Key +
                    "'. Repair targets would be ambiguous.",
                    managerId: group.Key,
                    suggestedRepair: ReviewSuggestion(
                        "Recapture with an adapter that assigns a unique, stable id to every manager.")));
            }
        }

        private static void ScanManager(
            DecorationAuditManagerSnapshot manager,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (!manager.CaptureComplete)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Snapshot,
                    DecorationAuditCode.SnapshotIncomplete,
                    "Manager snapshot is incomplete",
                    string.IsNullOrEmpty(manager.CaptureDiagnostic)
                        ? "Some decorations could not be captured, so duplicate and transform results may be incomplete."
                        : manager.CaptureDiagnostic,
                    manager.ManagerId,
                    suggestedRepair: ReviewSuggestion(
                        "Close active editors, recapture the craft, and do not repair from this partial snapshot.")));
            }

            IndexedDecoration[] decorations = manager.Decorations
                .Select((decoration, index) => new IndexedDecoration(decoration, index))
                .OrderBy(item => item.Snapshot.DecorationId, StringComparer.Ordinal)
                .ThenBy(item => item.Index)
                .ToArray();
            HashSet<string> ambiguousIds = DuplicateDecorationIds(manager, decorations, findings);
            var reportedMeshComplexity = new HashSet<Guid>();

            foreach (IndexedDecoration indexedDecoration in decorations)
            {
                ScanDecoration(
                    manager.ManagerId,
                    indexedDecoration.Snapshot,
                    !ambiguousIds.Contains(indexedDecoration.Snapshot.DecorationId),
                    reportedMeshComplexity.Add(indexedDecoration.Snapshot.MeshGuid),
                    options,
                    findings);
            }

            ScanStorageKeys(manager.ManagerId, decorations, findings);
            ScanExactDuplicates(manager.ManagerId, decorations, findings);
            ScanOverlaps(manager.ManagerId, decorations, options, findings);
            ScanCapacity(manager, options, findings);
        }

        private static HashSet<string> DuplicateDecorationIds(
            DecorationAuditManagerSnapshot manager,
            IEnumerable<IndexedDecoration> decorations,
            ICollection<DecorationAuditFinding> findings)
        {
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (IGrouping<string, IndexedDecoration> group in decorations
                         .GroupBy(item => item.Snapshot.DecorationId, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                duplicates.Add(group.Key);
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Duplicate,
                    DecorationAuditCode.DuplicateDecorationId,
                    "Duplicate decoration id",
                    "Manager '" + manager.DisplayName + "' exposes " +
                    group.Count().ToString(CultureInfo.InvariantCulture) +
                    " decorations with id '" + group.Key + "'.",
                    manager.ManagerId,
                    group.Key,
                    group.Select(item => item.Snapshot.DecorationId),
                    ReviewSuggestion(
                        "Recapture with unique ids before attempting any automated repair.")));
            }
            return duplicates;
        }

        private static void ScanDecoration(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            bool repairTargetUnique,
            bool reportMeshComplexity,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            ScanTether(managerId, decoration, options, findings);
            ScanPosition(managerId, decoration, repairTargetUnique, options, findings);
            ScanScale(managerId, decoration, repairTargetUnique, options, findings);
            ScanOrientation(managerId, decoration, repairTargetUnique, options, findings);
            ScanReferences(managerId, decoration, findings);
            ScanMesh(managerId, decoration, reportMeshComplexity, options, findings);
        }

        private static void ScanTether(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            DecorationAuditSeverity severity;
            DecorationAuditCode code;
            string title;
            string detail;
            switch (decoration.TetherState)
            {
                case DecorationAuditTetherState.Valid:
                    break;
                case DecorationAuditTetherState.MissingBlock:
                    severity = DecorationAuditSeverity.Error;
                    code = DecorationAuditCode.TetherMissingBlock;
                    title = "Decoration tether block is missing";
                    detail = "The decoration points at " + decoration.Tether +
                             ", but the adapter could not find a live block there.";
                    AddTetherFinding(managerId, decoration, severity, code, title, detail, findings);
                    break;
                case DecorationAuditTetherState.OutsideConstruct:
                    severity = DecorationAuditSeverity.Error;
                    code = DecorationAuditCode.TetherOutsideConstruct;
                    title = "Decoration tether is outside its construct";
                    detail = "The tether " + decoration.Tether +
                             " resolves outside the decoration's owning construct.";
                    AddTetherFinding(managerId, decoration, severity, code, title, detail, findings);
                    break;
                case DecorationAuditTetherState.ManagerMismatch:
                    severity = DecorationAuditSeverity.Critical;
                    code = DecorationAuditCode.TetherManagerMismatch;
                    title = "Decoration manager and tether disagree";
                    detail = "The decoration is indexed by one manager but reports another owner.";
                    AddTetherFinding(managerId, decoration, severity, code, title, detail, findings);
                    break;
                case DecorationAuditTetherState.Unreadable:
                    severity = DecorationAuditSeverity.Warning;
                    code = DecorationAuditCode.TetherUnreadable;
                    title = "Decoration tether could not be validated";
                    detail = "The adapter failed while resolving tether " + decoration.Tether + ".";
                    AddTetherFinding(managerId, decoration, severity, code, title, detail, findings);
                    break;
                default:
                    if (options.ReportUnknownTethers)
                    {
                        AddTetherFinding(
                            managerId,
                            decoration,
                            DecorationAuditSeverity.Information,
                            DecorationAuditCode.TetherUnknown,
                            "Decoration tether was not checked",
                            "The snapshot adapter did not provide tether validity.",
                            findings);
                    }
                    break;
            }

            if (AbsoluteExceeds(decoration.Tether.X, options.MaximumAbsoluteTetherCoordinate) ||
                AbsoluteExceeds(decoration.Tether.Y, options.MaximumAbsoluteTetherCoordinate) ||
                AbsoluteExceeds(decoration.Tether.Z, options.MaximumAbsoluteTetherCoordinate))
            {
                AddTetherFinding(
                    managerId,
                    decoration,
                    DecorationAuditSeverity.Error,
                    DecorationAuditCode.TetherCoordinateOutOfRange,
                    "Decoration tether coordinate is implausibly large",
                    "Tether " + decoration.Tether + " exceeds the configured +/-" +
                    options.MaximumAbsoluteTetherCoordinate.ToString(CultureInfo.InvariantCulture) +
                    " audit boundary.",
                    findings);
            }
        }

        private static void AddTetherFinding(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            DecorationAuditSeverity severity,
            DecorationAuditCode code,
            string title,
            string detail,
            ICollection<DecorationAuditFinding> findings)
        {
            findings.Add(new DecorationAuditFinding(
                severity,
                DecorationAuditCategory.Tether,
                code,
                title,
                detail,
                managerId,
                decoration.DecorationId,
                suggestedRepair: ReviewSuggestion(
                    "Retether the decoration to a verified block in Decoration Edit Mode; if no valid target exists, remove it manually after making a backup.")));
        }

        private static void ScanPosition(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            bool repairTargetUnique,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (!decoration.Position.IsFinite)
            {
                findings.Add(TransformFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCode.PositionNonFinite,
                    "Decoration position contains NaN or Infinity",
                    "Position " + decoration.Position + " is not serializable safely.",
                    managerId,
                    decoration,
                    repairTargetUnique
                        ? RepairSuggestion(
                            DecorationAuditRepairKind.ResetPosition,
                            DecorationAuditRepairSafety.ReviewRequired,
                            managerId,
                            decoration,
                            decoration.Position,
                            new DecorationAuditVector3(0d, 0d, 0d),
                            "Reset the corrupt local position to zero, then reposition it manually.")
                        : ReviewSuggestion("Resolve duplicate decoration ids, then reset and reposition this value.")));
                return;
            }

            if (decoration.Position.MaximumAbsoluteComponent > options.MaximumAbsolutePosition)
            {
                double limit = options.MaximumAbsolutePosition;
                findings.Add(TransformFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCode.PositionOutOfRange,
                    "Decoration position exceeds the safe local range",
                    "Position " + decoration.Position + " exceeds +/-" +
                    limit.ToString("R", CultureInfo.InvariantCulture) + ".",
                    managerId,
                    decoration,
                    repairTargetUnique
                        ? RepairSuggestion(
                            DecorationAuditRepairKind.ClampPosition,
                            DecorationAuditRepairSafety.ReviewRequired,
                            managerId,
                            decoration,
                            decoration.Position,
                            decoration.Position.Clamp(-limit, limit),
                            "Clamp the local position only after checking the intended visual placement.")
                        : ReviewSuggestion("Resolve duplicate decoration ids, then clamp or retether manually.")));
            }
        }

        private static void ScanScale(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            bool repairTargetUnique,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (!decoration.Scale.IsFinite)
            {
                findings.Add(TransformFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCode.ScaleNonFinite,
                    "Decoration scale contains NaN or Infinity",
                    "Scale " + decoration.Scale + " is not serializable safely.",
                    managerId,
                    decoration,
                    repairTargetUnique
                        ? RepairSuggestion(
                            DecorationAuditRepairKind.ResetScale,
                            DecorationAuditRepairSafety.ReviewRequired,
                            managerId,
                            decoration,
                            decoration.Scale,
                            new DecorationAuditVector3(1d, 1d, 1d),
                            "Reset the corrupt scale to one, then restore the intended size manually.")
                        : ReviewSuggestion("Resolve duplicate decoration ids, then reset the scale manually.")));
                return;
            }

            bool nearZero = IsNearZero(decoration.Scale.X, options.MinimumNonZeroScale) ||
                            IsNearZero(decoration.Scale.Y, options.MinimumNonZeroScale) ||
                            IsNearZero(decoration.Scale.Z, options.MinimumNonZeroScale);
            bool tooLarge = decoration.Scale.MaximumAbsoluteComponent > options.MaximumAbsoluteScale;
            if (!nearZero && !tooLarge)
                return;

            DecorationAuditVector3 replacement = ClampScale(decoration.Scale, options);
            findings.Add(TransformFinding(
                tooLarge ? DecorationAuditSeverity.Error : DecorationAuditSeverity.Warning,
                tooLarge ? DecorationAuditCode.ScaleOutOfRange : DecorationAuditCode.ScaleNearZero,
                tooLarge
                    ? "Decoration scale is implausibly large"
                    : "Decoration scale has a zero or near-zero axis",
                "Scale " + decoration.Scale + " is outside the configured audit range [" +
                options.MinimumNonZeroScale.ToString("R", CultureInfo.InvariantCulture) + ", " +
                options.MaximumAbsoluteScale.ToString("R", CultureInfo.InvariantCulture) + "] by magnitude.",
                managerId,
                decoration,
                repairTargetUnique
                    ? RepairSuggestion(
                        DecorationAuditRepairKind.ClampScale,
                        DecorationAuditRepairSafety.ReviewRequired,
                        managerId,
                        decoration,
                        decoration.Scale,
                        replacement,
                        "Clamp only after checking whether the extreme or flat scale was intentional.")
                    : ReviewSuggestion("Resolve duplicate decoration ids, then inspect the scale manually.")));
        }

        private static void ScanOrientation(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            bool repairTargetUnique,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (!decoration.Orientation.IsFinite)
            {
                findings.Add(TransformFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCode.OrientationNonFinite,
                    "Decoration orientation contains NaN or Infinity",
                    "Orientation " + decoration.Orientation + " is not serializable safely.",
                    managerId,
                    decoration,
                    repairTargetUnique
                        ? RepairSuggestion(
                            DecorationAuditRepairKind.ResetOrientation,
                            DecorationAuditRepairSafety.ReviewRequired,
                            managerId,
                            decoration,
                            decoration.Orientation,
                            new DecorationAuditVector3(0d, 0d, 0d),
                            "Reset the corrupt orientation to zero, then restore it manually.")
                        : ReviewSuggestion("Resolve duplicate decoration ids, then reset the orientation manually.")));
                return;
            }

            if (decoration.Orientation.MaximumAbsoluteComponent <=
                options.MaximumAbsoluteOrientationDegrees)
            {
                return;
            }

            findings.Add(TransformFinding(
                DecorationAuditSeverity.Warning,
                DecorationAuditCode.OrientationOutOfRange,
                "Decoration orientation is outside the canonical range",
                "Orientation " + decoration.Orientation +
                " can be reduced modulo 360 without changing the represented rotation.",
                managerId,
                decoration,
                repairTargetUnique
                    ? RepairSuggestion(
                        DecorationAuditRepairKind.NormalizeOrientation,
                        DecorationAuditRepairSafety.Safe,
                        managerId,
                        decoration,
                        decoration.Orientation,
                        decoration.Orientation.NormalizeDegrees(),
                        "Normalize finite Euler angles modulo 360.")
                    : ReviewSuggestion("Resolve duplicate decoration ids before normalizing this orientation.")));
        }

        private static void ScanReferences(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            ICollection<DecorationAuditFinding> findings)
        {
            if (decoration.Color < 0 || decoration.Color > 31)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Reference,
                    DecorationAuditCode.ColorOutOfRange,
                    "Decoration color index is invalid",
                    "Color index " + decoration.Color.ToString(CultureInfo.InvariantCulture) +
                    " is outside FtD's supported 0 through 31 palette range.",
                    managerId,
                    decoration.DecorationId,
                    suggestedRepair: ReviewSuggestion(
                        "Choose the intended palette color manually; clamping could change appearance.")));
            }

            DecorationAuditReferenceState meshState = decoration.MeshMetadata.ReferenceState;
            if (decoration.MeshGuid == Guid.Empty ||
                meshState == DecorationAuditReferenceState.Missing)
            {
                findings.Add(ReferenceFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCode.MeshReferenceMissing,
                    "Decoration mesh reference is missing",
                    decoration.MeshGuid == Guid.Empty
                        ? "The decoration stores an empty mesh GUID."
                        : "Mesh " + decoration.MeshGuid.ToString("D") +
                          " is not present in the active component catalog.",
                    managerId,
                    decoration,
                    "Choose a known mesh manually or restore the mod/content pack that owns this GUID."));
            }
            else if (meshState == DecorationAuditReferenceState.Unreadable)
            {
                findings.Add(ReferenceFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCode.MeshReferenceUnreadable,
                    "Decoration mesh reference could not be read",
                    string.IsNullOrEmpty(decoration.MeshMetadata.Diagnostic)
                        ? "The active mesh catalog could not validate this reference."
                        : decoration.MeshMetadata.Diagnostic,
                    managerId,
                    decoration,
                    "Reload the owning content pack and audit again before replacing the mesh."));
            }

            if (decoration.MaterialReplacement == Guid.Empty)
                return;
            if (decoration.MaterialReferenceState == DecorationAuditReferenceState.Missing)
            {
                findings.Add(ReferenceFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCode.MaterialReferenceMissing,
                    "Decoration material override is missing",
                    "Material " + decoration.MaterialReplacement.ToString("D") +
                    " is not present in the active material catalog.",
                    managerId,
                    decoration,
                    "Clear or replace the material override manually after checking the intended finish."));
            }
            else if (decoration.MaterialReferenceState ==
                     DecorationAuditReferenceState.Unreadable)
            {
                findings.Add(ReferenceFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCode.MaterialReferenceUnreadable,
                    "Decoration material override could not be read",
                    "The active material catalog could not validate " +
                    decoration.MaterialReplacement.ToString("D") + ".",
                    managerId,
                    decoration,
                    "Reload the owning content pack and audit again before replacing the material."));
            }
        }

        private static void ScanMesh(
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            bool reportComplexity,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            DecorationAuditMeshMetadata metadata = decoration.MeshMetadata;
            if (metadata.ReferenceState != DecorationAuditReferenceState.Valid ||
                !metadata.MetricsAvailable)
            {
                return;
            }

            DecorationAuditVector3 size = metadata.LocalSize;
            bool boundsValid = size.IsFinite &&
                               metadata.LocalCenter.IsFinite &&
                               size.X >= 0d &&
                               size.Y >= 0d &&
                               size.Z >= 0d &&
                               size.MaximumAbsoluteComponent > 0d;
            if (!boundsValid)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Mesh,
                    DecorationAuditCode.MeshBoundsInvalid,
                    "Decoration mesh bounds are invalid",
                    "Mesh " + decoration.MeshGuid.ToString("D") +
                    " reports local center " + metadata.LocalCenter +
                    " and size " + size + ".",
                    managerId,
                    decoration.DecorationId,
                    suggestedRepair: ReviewSuggestion(
                        "Replace or repair the source mesh; transform-only repair cannot make invalid asset bounds safe.")));
            }
            else if (decoration.Scale.IsFinite)
            {
                var renderedSize = new DecorationAuditVector3(
                    Math.Abs(size.X * decoration.Scale.X),
                    Math.Abs(size.Y * decoration.Scale.Y),
                    Math.Abs(size.Z * decoration.Scale.Z));
                if (!renderedSize.IsFinite)
                {
                    findings.Add(new DecorationAuditFinding(
                        DecorationAuditSeverity.Critical,
                        DecorationAuditCategory.Mesh,
                        DecorationAuditCode.MeshBoundsInvalid,
                        "Decoration rendered bounds overflow",
                        "Mesh size and scale overflow while estimating rendered bounds.",
                        managerId,
                        decoration.DecorationId,
                        suggestedRepair: ReviewSuggestion(
                            "Reduce the scale manually after backing up the craft.")));
                }
                else if (renderedSize.MaximumAbsoluteComponent >
                         options.MaximumRenderedMeshExtent)
                {
                    findings.Add(new DecorationAuditFinding(
                        DecorationAuditSeverity.Error,
                        DecorationAuditCategory.Mesh,
                        DecorationAuditCode.MeshRenderedExtentExceeded,
                        "Decoration mesh is oversized after scaling",
                        "Estimated rendered size " + renderedSize + " exceeds the configured " +
                        options.MaximumRenderedMeshExtent.ToString("N0", CultureInfo.InvariantCulture) +
                        " metre single-axis audit limit.",
                        managerId,
                        decoration.DecorationId,
                        suggestedRepair: ReviewSuggestion(
                            "Inspect the intended coverage and reduce scale manually; oversized geometry is never clamped automatically.")));
                }
            }

            if (!reportComplexity)
                return;
            if (metadata.VertexCount > options.MaximumMeshVertices)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Mesh,
                    DecorationAuditCode.MeshVertexCountExceeded,
                    "Decoration mesh has an unusually high vertex count",
                    metadata.VertexCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " vertices exceed the configured " +
                    options.MaximumMeshVertices.ToString("N0", CultureInfo.InvariantCulture) +
                    " audit threshold.",
                    managerId,
                    decoration.DecorationId,
                    suggestedRepair: ReviewSuggestion(
                        "Use a lower-complexity mesh or confirm the performance cost in a backed-up craft.")));
            }
            if (metadata.TriangleCount > options.MaximumMeshTriangles)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Mesh,
                    DecorationAuditCode.MeshTriangleCountExceeded,
                    "Decoration mesh has an unusually high triangle count",
                    metadata.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) +
                    " triangles exceed the configured " +
                    options.MaximumMeshTriangles.ToString("N0", CultureInfo.InvariantCulture) +
                    " audit threshold.",
                    managerId,
                    decoration.DecorationId,
                    suggestedRepair: ReviewSuggestion(
                        "Use a lower-complexity mesh or confirm the performance cost in a backed-up craft.")));
            }
        }

        private static DecorationAuditFinding ReferenceFinding(
            DecorationAuditSeverity severity,
            DecorationAuditCode code,
            string title,
            string detail,
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            string repair) =>
            new DecorationAuditFinding(
                severity,
                DecorationAuditCategory.Reference,
                code,
                title,
                detail,
                managerId,
                decoration.DecorationId,
                suggestedRepair: ReviewSuggestion(repair));

        private static DecorationAuditFinding TransformFinding(
            DecorationAuditSeverity severity,
            DecorationAuditCode code,
            string title,
            string detail,
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            DecorationAuditRepairSuggestion suggestion) =>
            new DecorationAuditFinding(
                severity,
                DecorationAuditCategory.Transform,
                code,
                title,
                detail,
                managerId,
                decoration.DecorationId,
                suggestedRepair: suggestion);

        private static void ScanStorageKeys(
            string managerId,
            IEnumerable<IndexedDecoration> decorations,
            ICollection<DecorationAuditFinding> findings)
        {
            foreach (IGrouping<string, IndexedDecoration> group in decorations
                         .Where(item => !string.IsNullOrEmpty(item.Snapshot.StorageKey))
                         .GroupBy(item => item.Snapshot.StorageKey, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string[] ids = RelatedIds(group);
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Duplicate,
                    DecorationAuditCode.DuplicateStorageKey,
                    "Duplicate decoration storage key",
                    "Storage key '" + group.Key + "' is shared by " +
                    group.Count().ToString(CultureInfo.InvariantCulture) + " decorations.",
                    managerId,
                    ids.FirstOrDefault(),
                    ids,
                    ReviewSuggestion(
                        "Do not delete automatically. Back up the craft, inspect the duplicates, and recreate only confirmed corrupt entries.")));
            }
        }

        private static void ScanExactDuplicates(
            string managerId,
            IEnumerable<IndexedDecoration> decorations,
            ICollection<DecorationAuditFinding> findings)
        {
            foreach (IGrouping<string, IndexedDecoration> group in decorations
                         .Where(item => HasFiniteTransform(item.Snapshot))
                         .GroupBy(item => ExactKey(item.Snapshot), StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string[] ids = RelatedIds(group);
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Duplicate,
                    DecorationAuditCode.ExactDuplicate,
                    "Exact duplicate decorations",
                    group.Count().ToString(CultureInfo.InvariantCulture) +
                    " entries share the same tether, transform, mesh, color, material override, and anchor-mesh visibility.",
                    managerId,
                    ids.FirstOrDefault(),
                    ids,
                    ReviewSuggestion(
                        "Keep all entries during the dry run. Delete only duplicates confirmed visually after saving a backup.")));
            }
        }

        private static void ScanOverlaps(
            string managerId,
            IEnumerable<IndexedDecoration> decorations,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            OverlapProxy[] proxies = decorations
                .Where(item => HasFiniteTransform(item.Snapshot))
                .OrderBy(item => item.Snapshot.DecorationId, StringComparer.Ordinal)
                .ThenBy(item => ExactKey(item.Snapshot), StringComparer.Ordinal)
                .ThenBy(item => item.Index)
                .Select((item, index) => BuildOverlapProxy(
                    item,
                    index,
                    options.OverlapTolerance))
                .ToArray();
            if (proxies.Length < 2)
                return;

            var components = new OverlapDisjointSet(proxies.Length);
            foreach (IGrouping<string, OverlapProxy> explicitGroup in proxies
                         .Where(proxy => !string.IsNullOrEmpty(proxy.Snapshot.OverlapKey))
                         .GroupBy(proxy => proxy.Snapshot.OverlapKey, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                OverlapProxy[] keyed = explicitGroup
                    .OrderBy(proxy => proxy.Snapshot.DecorationId, StringComparer.Ordinal)
                    .ThenBy(proxy => proxy.ExactKey, StringComparer.Ordinal)
                    .ToArray();
                if (keyed.Select(proxy => proxy.ExactKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1)
                {
                    for (int index = 1; index < keyed.Length; index++)
                        components.Union(keyed[0].Index, keyed[index].Index);
                }
            }

            OverlapProxy[] sweep = proxies
                .OrderBy(proxy => proxy.Minimum.X)
                .ThenBy(proxy => proxy.Minimum.Y)
                .ThenBy(proxy => proxy.Minimum.Z)
                .ThenBy(proxy => proxy.Snapshot.DecorationId, StringComparer.Ordinal)
                .ThenBy(proxy => proxy.ExactKey, StringComparer.Ordinal)
                .ToArray();
            var active = new List<OverlapProxy>();
            long pairChecks = 0L;
            bool scanLimitReached = false;
            for (int currentIndex = 0;
                 currentIndex < sweep.Length && !scanLimitReached;
                 currentIndex++)
            {
                OverlapProxy current = sweep[currentIndex];
                for (int activeIndex = active.Count - 1; activeIndex >= 0; activeIndex--)
                {
                    if (active[activeIndex].Maximum.X + options.OverlapTolerance <
                        current.Minimum.X)
                    {
                        active.RemoveAt(activeIndex);
                    }
                }

                foreach (OverlapProxy candidate in active)
                {
                    pairChecks++;
                    if (pairChecks > options.MaximumOverlapPairChecks)
                    {
                        scanLimitReached = true;
                        break;
                    }

                    if (components.Find(candidate.Index) == components.Find(current.Index) ||
                        !AabbAxesOverlap(candidate, current, options.OverlapTolerance))
                    {
                        continue;
                    }

                    if (string.Equals(candidate.ExactKey, current.ExactKey, StringComparison.Ordinal))
                        continue;
                    if (StrictBoundsOverlap(candidate, current, options.OverlapTolerance))
                        components.Union(candidate.Index, current.Index);
                }

                active.Add(current);
            }

            var overlapGroups = proxies
                .GroupBy(proxy => components.Find(proxy.Index))
                .Where(group => group.Count() > 1)
                .Select(group => group
                    .OrderBy(proxy => proxy.Snapshot.DecorationId, StringComparer.Ordinal)
                    .ThenBy(proxy => proxy.ExactKey, StringComparer.Ordinal)
                    .ToArray())
                .OrderBy(
                    group => string.Join(
                        "|",
                        group.Select(proxy => proxy.Snapshot.DecorationId).ToArray()),
                    StringComparer.Ordinal)
                .ToArray();
            foreach (OverlapProxy[] group in overlapGroups)
            {
                string[] ids = group
                    .Select(proxy => proxy.Snapshot.DecorationId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                int fallbackCount = group.Count(proxy => !proxy.HasExactMeshBounds);
                string boundsDetail = fallbackCount == 0
                    ? "Their transformed oriented mesh bounds intersect"
                    : fallbackCount.ToString(CultureInfo.InvariantCulture) +
                      " decoration(s) lacked valid mesh bounds and used the conservative center-proximity fallback";

                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Duplicate,
                    DecorationAuditCode.SpatialOverlap,
                    "Decoration bounds overlap",
                    group.Count().ToString(CultureInfo.InvariantCulture) +
                    " decorations form an intersecting bounds group. " +
                    boundsDetail + " within " +
                    options.OverlapTolerance.ToString("R", CultureInfo.InvariantCulture) +
                    " units. Layered decorations may be intentional.",
                    managerId,
                    ids.FirstOrDefault(),
                    ids,
                    ReviewSuggestion(
                        "Inspect the group in the viewport; leave intentional layers unchanged and remove only confirmed accidental copies.")));
            }

            if (scanLimitReached)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Duplicate,
                    DecorationAuditCode.SpatialOverlapScanLimit,
                    "Decoration overlap scan reached its safety limit",
                    "The deterministic broad-phase performed " +
                    options.MaximumOverlapPairChecks.ToString("N0", CultureInfo.InvariantCulture) +
                    " transformed-bounds pair checks and stopped. Reported overlaps are valid, but additional overlap groups may remain unreported.",
                    managerId,
                    suggestedRepair: ReviewSuggestion(
                        "Isolate smaller layer or craft regions and scan again; the audit never mutates decorations while resolving this hotspot.")));
            }
        }

        private static OverlapProxy BuildOverlapProxy(
            IndexedDecoration indexed,
            int index,
            double tolerance)
        {
            DecorationAuditDecorationSnapshot decoration = indexed.Snapshot;
            var decorationCenter = new DecorationAuditVector3(
                decoration.Tether.X + decoration.Position.X,
                decoration.Tether.Y + decoration.Position.Y,
                decoration.Tether.Z + decoration.Position.Z);
            DecorationAuditMeshMetadata metadata = decoration.MeshMetadata;
            if (metadata.ReferenceState == DecorationAuditReferenceState.Valid &&
                metadata.MetricsAvailable &&
                TryBuildExactOverlapBounds(
                    decoration,
                    decorationCenter,
                    out DecorationAuditVector3 center,
                    out DecorationAuditVector3 halfExtents,
                    out AuditMatrix3 rotation,
                    out DecorationAuditVector3 minimum,
                    out DecorationAuditVector3 maximum))
            {
                return new OverlapProxy(
                    decoration,
                    index,
                    ExactKey(decoration),
                    center,
                    halfExtents,
                    rotation,
                    minimum,
                    maximum,
                    hasExactMeshBounds: true);
            }

            // Bounds are unavailable or unsafe to evaluate. Retain the old
            // center-proximity behavior as a conservative, deterministic point
            // fallback rather than inventing an asset extent.
            double fallbackHalf = tolerance * 0.5d;
            var fallbackExtents = new DecorationAuditVector3(
                fallbackHalf,
                fallbackHalf,
                fallbackHalf);
            return new OverlapProxy(
                decoration,
                index,
                ExactKey(decoration),
                decorationCenter,
                new DecorationAuditVector3(0d, 0d, 0d),
                AuditMatrix3.Identity,
                Subtract(decorationCenter, fallbackExtents),
                Add(decorationCenter, fallbackExtents),
                hasExactMeshBounds: false);
        }

        private static bool TryBuildExactOverlapBounds(
            DecorationAuditDecorationSnapshot decoration,
            DecorationAuditVector3 decorationCenter,
            out DecorationAuditVector3 center,
            out DecorationAuditVector3 halfExtents,
            out AuditMatrix3 rotation,
            out DecorationAuditVector3 minimum,
            out DecorationAuditVector3 maximum)
        {
            center = default(DecorationAuditVector3);
            halfExtents = default(DecorationAuditVector3);
            rotation = AuditMatrix3.Identity;
            minimum = default(DecorationAuditVector3);
            maximum = default(DecorationAuditVector3);
            DecorationAuditMeshMetadata metadata = decoration.MeshMetadata;
            DecorationAuditVector3 size = metadata.LocalSize;
            DecorationAuditVector3 localCenter = metadata.LocalCenter;
            if (!decorationCenter.IsFinite ||
                !size.IsFinite || !localCenter.IsFinite ||
                size.X < 0d || size.Y < 0d || size.Z < 0d ||
                size.MaximumAbsoluteComponent <= 0d)
            {
                return false;
            }

            rotation = RotationForEulerDegrees(decoration.Orientation);
            var scaledLocalCenter = new DecorationAuditVector3(
                localCenter.X * decoration.Scale.X,
                localCenter.Y * decoration.Scale.Y,
                localCenter.Z * decoration.Scale.Z);
            center = Add(decorationCenter, rotation.Transform(scaledLocalCenter));
            halfExtents = new DecorationAuditVector3(
                Math.Abs(size.X * decoration.Scale.X) * 0.5d,
                Math.Abs(size.Y * decoration.Scale.Y) * 0.5d,
                Math.Abs(size.Z * decoration.Scale.Z) * 0.5d);
            if (!center.IsFinite || !halfExtents.IsFinite)
                return false;

            var aabbHalf = new DecorationAuditVector3(
                Math.Abs(rotation.M00) * halfExtents.X +
                Math.Abs(rotation.M01) * halfExtents.Y +
                Math.Abs(rotation.M02) * halfExtents.Z,
                Math.Abs(rotation.M10) * halfExtents.X +
                Math.Abs(rotation.M11) * halfExtents.Y +
                Math.Abs(rotation.M12) * halfExtents.Z,
                Math.Abs(rotation.M20) * halfExtents.X +
                Math.Abs(rotation.M21) * halfExtents.Y +
                Math.Abs(rotation.M22) * halfExtents.Z);
            if (!aabbHalf.IsFinite)
                return false;

            minimum = Subtract(center, aabbHalf);
            maximum = Add(center, aabbHalf);
            return minimum.IsFinite && maximum.IsFinite;
        }

        private static AuditMatrix3 RotationForEulerDegrees(
            DecorationAuditVector3 orientation)
        {
            const double degreesToRadians = Math.PI / 180d;
            double x = (orientation.X % 360d) * degreesToRadians;
            double y = (orientation.Y % 360d) * degreesToRadians;
            double z = (orientation.Z % 360d) * degreesToRadians;
            double sinX = Math.Sin(x);
            double cosX = Math.Cos(x);
            double sinY = Math.Sin(y);
            double cosY = Math.Cos(y);
            double sinZ = Math.Sin(z);
            double cosZ = Math.Cos(z);
            var rotateX = new AuditMatrix3(
                1d, 0d, 0d,
                0d, cosX, -sinX,
                0d, sinX, cosX);
            var rotateY = new AuditMatrix3(
                cosY, 0d, sinY,
                0d, 1d, 0d,
                -sinY, 0d, cosY);
            var rotateZ = new AuditMatrix3(
                cosZ, -sinZ, 0d,
                sinZ, cosZ, 0d,
                0d, 0d, 1d);

            // Unity Quaternion.Euler applies Z, then X, then Y.
            return AuditMatrix3.Multiply(
                rotateY,
                AuditMatrix3.Multiply(rotateX, rotateZ));
        }

        private static bool AabbAxesOverlap(
            OverlapProxy left,
            OverlapProxy right,
            double tolerance) =>
            left.Maximum.X + tolerance >= right.Minimum.X &&
            right.Maximum.X + tolerance >= left.Minimum.X &&
            left.Maximum.Y + tolerance >= right.Minimum.Y &&
            right.Maximum.Y + tolerance >= left.Minimum.Y &&
            left.Maximum.Z + tolerance >= right.Minimum.Z &&
            right.Maximum.Z + tolerance >= left.Minimum.Z;

        private static bool StrictBoundsOverlap(
            OverlapProxy left,
            OverlapProxy right,
            double tolerance)
        {
            if (!string.IsNullOrEmpty(left.Snapshot.OverlapKey) &&
                string.Equals(
                    left.Snapshot.OverlapKey,
                    right.Snapshot.OverlapKey,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (!left.HasExactMeshBounds || !right.HasExactMeshBounds)
            {
                return Math.Abs(left.Center.X - right.Center.X) <= tolerance &&
                       Math.Abs(left.Center.Y - right.Center.Y) <= tolerance &&
                       Math.Abs(left.Center.Z - right.Center.Z) <= tolerance;
            }

            return OrientedBoundsOverlap(left, right, tolerance);
        }

        private static bool OrientedBoundsOverlap(
            OverlapProxy left,
            OverlapProxy right,
            double tolerance)
        {
            DecorationAuditVector3 leftAxis0 = left.Rotation.Axis(0);
            DecorationAuditVector3 leftAxis1 = left.Rotation.Axis(1);
            DecorationAuditVector3 leftAxis2 = left.Rotation.Axis(2);
            DecorationAuditVector3 rightAxis0 = right.Rotation.Axis(0);
            DecorationAuditVector3 rightAxis1 = right.Rotation.Axis(1);
            DecorationAuditVector3 rightAxis2 = right.Rotation.Axis(2);
            var relative = new AuditMatrix3(
                Dot(leftAxis0, rightAxis0),
                Dot(leftAxis0, rightAxis1),
                Dot(leftAxis0, rightAxis2),
                Dot(leftAxis1, rightAxis0),
                Dot(leftAxis1, rightAxis1),
                Dot(leftAxis1, rightAxis2),
                Dot(leftAxis2, rightAxis0),
                Dot(leftAxis2, rightAxis1),
                Dot(leftAxis2, rightAxis2));
            const double parallelEpsilon = 1e-12d;
            var absolute = new AuditMatrix3(
                Math.Abs(relative.M00) + parallelEpsilon,
                Math.Abs(relative.M01) + parallelEpsilon,
                Math.Abs(relative.M02) + parallelEpsilon,
                Math.Abs(relative.M10) + parallelEpsilon,
                Math.Abs(relative.M11) + parallelEpsilon,
                Math.Abs(relative.M12) + parallelEpsilon,
                Math.Abs(relative.M20) + parallelEpsilon,
                Math.Abs(relative.M21) + parallelEpsilon,
                Math.Abs(relative.M22) + parallelEpsilon);
            DecorationAuditVector3 worldDelta = Subtract(right.Center, left.Center);
            var delta = new DecorationAuditVector3(
                Dot(worldDelta, leftAxis0),
                Dot(worldDelta, leftAxis1),
                Dot(worldDelta, leftAxis2));

            for (int leftAxis = 0; leftAxis < 3; leftAxis++)
            {
                double leftRadius = Component(left.HalfExtents, leftAxis);
                double rightRadius = 0d;
                for (int rightAxis = 0; rightAxis < 3; rightAxis++)
                {
                    rightRadius += Component(right.HalfExtents, rightAxis) *
                                   MatrixComponent(absolute, leftAxis, rightAxis);
                }
                if (Math.Abs(Component(delta, leftAxis)) >
                    leftRadius + rightRadius + tolerance)
                {
                    return false;
                }
            }

            for (int rightAxis = 0; rightAxis < 3; rightAxis++)
            {
                double leftRadius = 0d;
                for (int leftAxis = 0; leftAxis < 3; leftAxis++)
                {
                    leftRadius += Component(left.HalfExtents, leftAxis) *
                                  MatrixComponent(absolute, leftAxis, rightAxis);
                }
                double rightRadius = Component(right.HalfExtents, rightAxis);
                double projection = Math.Abs(
                    Component(delta, 0) * MatrixComponent(relative, 0, rightAxis) +
                    Component(delta, 1) * MatrixComponent(relative, 1, rightAxis) +
                    Component(delta, 2) * MatrixComponent(relative, 2, rightAxis));
                if (projection > leftRadius + rightRadius + tolerance)
                    return false;
            }

            for (int leftAxis = 0; leftAxis < 3; leftAxis++)
            {
                int leftNext = (leftAxis + 1) % 3;
                int leftOther = (leftAxis + 2) % 3;
                for (int rightAxis = 0; rightAxis < 3; rightAxis++)
                {
                    int rightNext = (rightAxis + 1) % 3;
                    int rightOther = (rightAxis + 2) % 3;
                    double leftRadius =
                        Component(left.HalfExtents, leftNext) *
                        MatrixComponent(absolute, leftOther, rightAxis) +
                        Component(left.HalfExtents, leftOther) *
                        MatrixComponent(absolute, leftNext, rightAxis);
                    double rightRadius =
                        Component(right.HalfExtents, rightNext) *
                        MatrixComponent(absolute, leftAxis, rightOther) +
                        Component(right.HalfExtents, rightOther) *
                        MatrixComponent(absolute, leftAxis, rightNext);
                    double projection = Math.Abs(
                        Component(delta, leftOther) *
                        MatrixComponent(relative, leftNext, rightAxis) -
                        Component(delta, leftNext) *
                        MatrixComponent(relative, leftOther, rightAxis));
                    if (projection > leftRadius + rightRadius + tolerance)
                        return false;
                }
            }

            return true;
        }

        private static double MatrixComponent(AuditMatrix3 value, int row, int column)
        {
            if (row == 0)
                return column == 0 ? value.M00 : column == 1 ? value.M01 : value.M02;
            if (row == 1)
                return column == 0 ? value.M10 : column == 1 ? value.M11 : value.M12;
            return column == 0 ? value.M20 : column == 1 ? value.M21 : value.M22;
        }

        private static double Component(DecorationAuditVector3 value, int axis) =>
            axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

        private static double Dot(
            DecorationAuditVector3 left,
            DecorationAuditVector3 right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        private static DecorationAuditVector3 Add(
            DecorationAuditVector3 left,
            DecorationAuditVector3 right) =>
            new DecorationAuditVector3(
                left.X + right.X,
                left.Y + right.Y,
                left.Z + right.Z);

        private static DecorationAuditVector3 Subtract(
            DecorationAuditVector3 left,
            DecorationAuditVector3 right) =>
            new DecorationAuditVector3(
                left.X - right.X,
                left.Y - right.Y,
                left.Z - right.Z);

        private static void ScanLayers(
            DecorationAuditCraftSnapshot source,
            ICollection<DecorationAuditFinding> findings)
        {
            var defined = new HashSet<string>(
                source.Layers.Select(layer => layer.Name),
                StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DecorationAuditManagerSnapshot manager in source.Managers)
            {
                foreach (DecorationAuditDecorationSnapshot decoration in manager.Decorations)
                {
                    if (string.IsNullOrEmpty(decoration.LayerName))
                        continue;
                    used.Add(decoration.LayerName);
                    if (defined.Contains(decoration.LayerName))
                        continue;
                    findings.Add(new DecorationAuditFinding(
                        DecorationAuditSeverity.Warning,
                        DecorationAuditCategory.Layer,
                        DecorationAuditCode.LayerReferenceMissing,
                        "Decoration references a missing workspace layer",
                        "Layer '" + decoration.LayerName +
                        "' is assigned to the decoration but is not defined in this workspace snapshot.",
                        manager.ManagerId,
                        decoration.DecorationId,
                        suggestedRepair: ReviewSuggestion(
                            "Recreate the intended layer or reassign this decoration; layer assignments are not rewritten automatically.")));
                }
            }

            foreach (DecorationAuditLayerSnapshot layer in source.Layers
                         .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First())
                         .Where(layer => !used.Contains(layer.Name))
                         .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(layer => layer.Name, StringComparer.Ordinal))
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Information,
                    DecorationAuditCategory.Layer,
                    DecorationAuditCode.LayerUnused,
                    "Workspace layer is unused by this craft snapshot",
                    "Layer '" + layer.Name + "' has no live decoration assignment in this audit. " +
                    "It may still be used by another craft because layers are profile-persistent.",
                    managerId: "layer:" + layer.Name,
                    suggestedRepair: ReviewSuggestion(
                        "Delete it from Workspace tools only after confirming it is not needed by another craft.")));
            }
        }

        private static void ScanCapacity(
            DecorationAuditManagerSnapshot manager,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            int capacity = manager.CapacityLimit > 0
                ? manager.CapacityLimit
                : options.DefaultManagerCapacity;
            int count = manager.EffectiveDecorationCount;
            double ratio = count / (double)capacity;
            if (ratio < options.CapacityWarningRatio)
                return;

            DecorationAuditSeverity severity;
            DecorationAuditCode code;
            string title;
            if (ratio >= 1d)
            {
                severity = DecorationAuditSeverity.Critical;
                code = DecorationAuditCode.ManagerCapacityExceeded;
                title = "Decoration manager is at or above capacity";
            }
            else if (ratio >= options.CapacityErrorRatio)
            {
                severity = DecorationAuditSeverity.Error;
                code = DecorationAuditCode.ManagerCapacityError;
                title = "Decoration manager is close to capacity";
            }
            else
            {
                severity = DecorationAuditSeverity.Warning;
                code = DecorationAuditCode.ManagerCapacityWarning;
                title = "Decoration manager is a capacity hotspot";
            }

            findings.Add(new DecorationAuditFinding(
                severity,
                DecorationAuditCategory.Capacity,
                code,
                title,
                manager.DisplayName + " contains " +
                count.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                capacity.ToString("N0", CultureInfo.InvariantCulture) +
                " configured decorations (" +
                (ratio * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%).",
                manager.ManagerId,
                suggestedRepair: ReviewSuggestion(
                    "Reduce accidental duplicates or distribute new decoration work across subconstruct managers; do not bulk-delete automatically.")));
        }

        private static void ScanSerialization(
            DecorationAuditSerializationSnapshot serialization,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (serialization == null || !serialization.Available)
                return;

            if (!serialization.Exact || serialization.Uncalibrated)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationEstimateUncalibrated,
                    "Serialization risk is estimated",
                    "Capture a measured save/load snapshot before relying on exact byte margins.",
                    suggestedRepair: ReviewSuggestion(
                        "Run a non-destructive serialization measurement and audit the craft again.")));
            }

            AddSerializationByteFinding(
                "header",
                serialization.PeakHeaderBytes,
                serialization.LegacyHeaderMaximum,
                serialization.MaximumHeaderBytes,
                options,
                findings);
            AddSerializationByteFinding(
                "data",
                serialization.PeakDataBytes,
                serialization.LegacyDataMaximum,
                serialization.MaximumDataBytes,
                options,
                findings);

            if (serialization.RequiresModBuffer)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Warning,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationRequiresModBuffer,
                    "Craft requires ESU serialization buffers",
                    "At least one blueprint stream exceeds the vanilla save buffer.",
                    suggestedRepair: ReviewSuggestion(
                        "Keep ESU enabled for every save/load participant and retain a backup before compatibility testing.")));
            }

            if (serialization.MaximumSaveBufferBytes > 0UL &&
                serialization.LargestBlueprintStreamBytes > serialization.MaximumSaveBufferBytes)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationBufferExceeded,
                    "Blueprint stream exceeds the hard save-buffer ceiling",
                    ByteMarginDetail(
                        serialization.LargestBlueprintStreamBytes,
                        serialization.MaximumSaveBufferBytes),
                    suggestedRepair: ReviewSuggestion(
                        "Do not overwrite the only copy. Reduce craft payload or raise limits only after a separate compatibility review.")));
            }

            if (string.Equals(
                    serialization.WireFormat,
                    "OverLimit",
                    StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationFormatOverLimit,
                    "Serialization forecast is over limit",
                    "The supplied serialization summary reports an over-limit wire format.",
                    suggestedRepair: ReviewSuggestion(
                        "Do not save over the only craft copy. Reduce the flagged manager or stream and measure again.")));
            }
        }

        private static void AddSerializationByteFinding(
            string label,
            ulong value,
            ulong legacyMaximum,
            ulong hardMaximum,
            DecorationAuditOptions options,
            ICollection<DecorationAuditFinding> findings)
        {
            if (hardMaximum > 0UL && value > hardMaximum)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Critical,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationCapacityExceeded,
                    "Serialization " + label + " bytes exceed the hard ceiling",
                    ByteMarginDetail(value, hardMaximum),
                    suggestedRepair: ReviewSuggestion(
                        "Do not overwrite the only craft copy. Reduce serialization load and measure again.")));
                return;
            }

            if (hardMaximum > 0UL &&
                value >= (ulong)Math.Ceiling(hardMaximum * options.SerializationWarningRatio))
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Error,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationCapacityWarning,
                    "Serialization " + label + " bytes are close to the hard ceiling",
                    ByteMarginDetail(value, hardMaximum),
                    suggestedRepair: ReviewSuggestion(
                        "Pause bulk decoration growth and measure after reducing confirmed duplicates or splitting work across managers.")));
                return;
            }

            if (legacyMaximum > 0UL && value > legacyMaximum)
            {
                findings.Add(new DecorationAuditFinding(
                    DecorationAuditSeverity.Information,
                    DecorationAuditCategory.Serialization,
                    DecorationAuditCode.SerializationLegacyBoundaryExceeded,
                    "Serialization " + label + " bytes require sentinel format",
                    ByteMarginDetail(value, legacyMaximum),
                    suggestedRepair: ReviewSuggestion(
                        "No automatic repair is needed; keep ESU enabled and test a backup in the intended multiplayer/mod environment.")));
            }
        }

        private static DecorationAuditSummary BuildSummary(
            DecorationAuditCraftSnapshot source,
            IReadOnlyCollection<DecorationAuditFinding> findings)
        {
            long decorationCount = 0L;
            foreach (DecorationAuditManagerSnapshot manager in source.Managers)
            {
                int count = manager.EffectiveDecorationCount;
                decorationCount = long.MaxValue - decorationCount < count
                    ? long.MaxValue
                    : decorationCount + count;
            }

            return new DecorationAuditSummary(
                source.Managers.Count,
                decorationCount,
                findings.Count(item => item.Severity == DecorationAuditSeverity.Information),
                findings.Count(item => item.Severity == DecorationAuditSeverity.Warning),
                findings.Count(item => item.Severity == DecorationAuditSeverity.Error),
                findings.Count(item => item.Severity == DecorationAuditSeverity.Critical),
                findings.Count(item => item.Category == DecorationAuditCategory.Tether),
                findings.Count(item => item.Category == DecorationAuditCategory.Transform),
                findings.Count(item => item.Category == DecorationAuditCategory.Reference),
                findings.Count(item => item.Category == DecorationAuditCategory.Mesh),
                findings.Count(item => item.Category == DecorationAuditCategory.Duplicate),
                findings.Count(item => item.Category == DecorationAuditCategory.Layer),
                findings.Count(item => item.Category == DecorationAuditCategory.Capacity),
                findings.Count(item => item.Category == DecorationAuditCategory.Serialization));
        }

        private static DecorationAuditRepairSuggestion RepairSuggestion(
            DecorationAuditRepairKind kind,
            DecorationAuditRepairSafety safety,
            string managerId,
            DecorationAuditDecorationSnapshot decoration,
            DecorationAuditVector3 expected,
            DecorationAuditVector3 replacement,
            string description)
        {
            return new DecorationAuditRepairSuggestion(
                safety,
                description,
                new DecorationAuditRepairOperation(
                    kind,
                    safety,
                    managerId,
                    decoration.DecorationId,
                    expected,
                    replacement,
                    description));
        }

        private static DecorationAuditRepairSuggestion ReviewSuggestion(string description) =>
            new DecorationAuditRepairSuggestion(
                DecorationAuditRepairSafety.ReviewRequired,
                description);

        private static DecorationAuditVector3 ClampScale(
            DecorationAuditVector3 value,
            DecorationAuditOptions options) =>
            new DecorationAuditVector3(
                ClampScaleComponent(value.X, options),
                ClampScaleComponent(value.Y, options),
                ClampScaleComponent(value.Z, options));

        private static double ClampScaleComponent(double value, DecorationAuditOptions options)
        {
            double sign = value < 0d ? -1d : 1d;
            double magnitude = Math.Abs(value);
            magnitude = Math.Max(options.MinimumNonZeroScale, magnitude);
            magnitude = Math.Min(options.MaximumAbsoluteScale, magnitude);
            return sign * magnitude;
        }

        private static bool IsNearZero(double value, double threshold) =>
            Math.Abs(value) < threshold;

        private static bool AbsoluteExceeds(int value, int limit) =>
            value == int.MinValue || Math.Abs(value) > limit;

        private static bool HasFiniteTransform(DecorationAuditDecorationSnapshot decoration) =>
            decoration.Position.IsFinite &&
            decoration.Scale.IsFinite &&
            decoration.Orientation.IsFinite;

        private static string[] RelatedIds(IEnumerable<IndexedDecoration> decorations) =>
            decorations
                .Select(item => item.Snapshot.DecorationId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        private static string ExactKey(DecorationAuditDecorationSnapshot decoration) =>
            string.Join(
                "|",
                CellKey(decoration.Tether),
                VectorKey(decoration.Position),
                VectorKey(decoration.Scale),
                VectorKey(decoration.Orientation),
                decoration.MeshGuid.ToString("N"),
                decoration.Color.ToString(CultureInfo.InvariantCulture),
                decoration.MaterialReplacement.ToString("N"),
                decoration.HideOriginalMesh ? "1" : "0");

        private static string CellKey(DecorationAuditCell value) =>
            string.Join(
                ",",
                value.X.ToString(CultureInfo.InvariantCulture),
                value.Y.ToString(CultureInfo.InvariantCulture),
                value.Z.ToString(CultureInfo.InvariantCulture));

        private static string VectorKey(DecorationAuditVector3 value) =>
            string.Join(
                ",",
                BitConverter.DoubleToInt64Bits(value.X).ToString("X16", CultureInfo.InvariantCulture),
                BitConverter.DoubleToInt64Bits(value.Y).ToString("X16", CultureInfo.InvariantCulture),
                BitConverter.DoubleToInt64Bits(value.Z).ToString("X16", CultureInfo.InvariantCulture));

        private static string ByteMarginDetail(ulong value, ulong limit)
        {
            string relation = value > limit ? "over" : "of";
            return value.ToString("N0", CultureInfo.InvariantCulture) + " bytes " +
                   relation + " a " +
                   limit.ToString("N0", CultureInfo.InvariantCulture) + "-byte boundary.";
        }

        private static void AppendPart(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private static void AppendManagerFingerprint(
            StringBuilder canonical,
            DecorationAuditManagerSnapshot manager)
        {
            AppendPart(canonical, manager.ManagerId);
            AppendPart(
                canonical,
                manager.ReportedDecorationCount.ToString(CultureInfo.InvariantCulture));
            AppendPart(
                canonical,
                manager.CapacityLimit.ToString(CultureInfo.InvariantCulture));
            AppendPart(canonical, manager.CaptureComplete ? "1" : "0");
            foreach (DecorationAuditDecorationSnapshot decoration in manager.Decorations
                         .OrderBy(item => item.DecorationId, StringComparer.Ordinal)
                         .ThenBy(item => ExactKey(item), StringComparer.Ordinal)
                         .ThenBy(item => item.StorageKey, StringComparer.Ordinal)
                         .ThenBy(item => item.OverlapKey, StringComparer.Ordinal)
                         .ThenBy(item => item.TetherState)
                         .ThenBy(item => item.LayerName, StringComparer.Ordinal)
                         .ThenBy(item => item.WorkspaceLocked)
                         .ThenBy(item => item.MaterialReferenceState)
                         .ThenBy(item => item.MeshMetadata.ReferenceState)
                         .ThenBy(item => item.MeshMetadata.MetricsAvailable)
                         .ThenBy(item => VectorKey(item.MeshMetadata.LocalCenter), StringComparer.Ordinal)
                         .ThenBy(item => VectorKey(item.MeshMetadata.LocalSize), StringComparer.Ordinal)
                         .ThenBy(item => item.MeshMetadata.VertexCount)
                         .ThenBy(item => item.MeshMetadata.TriangleCount))
            {
                AppendPart(canonical, decoration.DecorationId);
                AppendPart(canonical, decoration.StorageKey);
                AppendPart(canonical, decoration.OverlapKey);
                AppendPart(canonical, decoration.TetherState.ToString());
                AppendPart(canonical, CellKey(decoration.Tether));
                AppendPart(canonical, VectorKey(decoration.Position));
                AppendPart(canonical, VectorKey(decoration.Scale));
                AppendPart(canonical, VectorKey(decoration.Orientation));
                AppendPart(canonical, decoration.MeshGuid.ToString("N"));
                AppendPart(
                    canonical,
                    decoration.Color.ToString(CultureInfo.InvariantCulture));
                AppendPart(canonical, decoration.MaterialReplacement.ToString("N"));
                AppendPart(canonical, decoration.HideOriginalMesh ? "1" : "0");
                AppendPart(canonical, decoration.MeshMetadata.ReferenceState.ToString());
                AppendPart(canonical, decoration.MeshMetadata.MetricsAvailable ? "1" : "0");
                AppendPart(canonical, VectorKey(decoration.MeshMetadata.LocalCenter));
                AppendPart(canonical, VectorKey(decoration.MeshMetadata.LocalSize));
                AppendPart(
                    canonical,
                    decoration.MeshMetadata.VertexCount.ToString(CultureInfo.InvariantCulture));
                AppendPart(
                    canonical,
                    decoration.MeshMetadata.TriangleCount.ToString(CultureInfo.InvariantCulture));
                AppendPart(canonical, decoration.MaterialReferenceState.ToString());
                AppendPart(canonical, decoration.LayerName);
                AppendPart(canonical, decoration.WorkspaceLocked ? "1" : "0");
            }
        }

        private static string ComputeManagerOrderingDigest(
            DecorationAuditManagerSnapshot manager)
        {
            var canonical = new StringBuilder();
            AppendManagerFingerprint(canonical, manager);
            return ComputeCanonicalDigest(canonical);
        }

        private static string ComputeCanonicalDigest(StringBuilder canonical)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }

    internal static class DecorationAuditReportFormatter
    {
        internal static string Format(
            DecorationAuditReport report,
            DateTime generatedUtc)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var builder = new StringBuilder();
            DecorationAuditSummary summary = report.Summary;
            builder.AppendLine("Endless Shapes Unlimited craft and decoration audit");
            builder.AppendLine(
                "generated_utc=" + generatedUtc.ToUniversalTime()
                    .ToString("o", CultureInfo.InvariantCulture));
            builder.AppendLine("source=" + report.Source.SourceId);
            builder.AppendLine("snapshot_sha256=" + report.SnapshotFingerprint);
            builder.AppendLine("managers=" + summary.ManagerCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("decorations=" + summary.DecorationCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("critical=" + summary.CriticalCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("errors=" + summary.ErrorCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("warnings=" + summary.WarningCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("information=" + summary.InformationCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("tether_findings=" + summary.SuspiciousTetherCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("transform_findings=" + summary.TransformFindingCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("reference_findings=" + summary.ReferenceFindingCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("mesh_findings=" + summary.MeshFindingCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("duplicate_findings=" + summary.DuplicateGroupCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("layer_findings=" + summary.LayerFindingCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("capacity_findings=" + summary.CapacityHotspotCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("serialization_findings=" + summary.SerializationFindingCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("Serialization inputs");
            DecorationAuditSerializationSnapshot serialization = report.Source.Serialization;
            builder.AppendLine("available=" + serialization.Available.ToString().ToLowerInvariant());
            builder.AppendLine("exact=" + serialization.Exact.ToString().ToLowerInvariant());
            builder.AppendLine("uncalibrated=" + serialization.Uncalibrated.ToString().ToLowerInvariant());
            builder.AppendLine("wire_format=" + serialization.WireFormat);
            builder.AppendLine("peak_header_bytes=" + serialization.PeakHeaderBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("peak_data_bytes=" + serialization.PeakDataBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("largest_blueprint_stream_bytes=" + serialization.LargestBlueprintStreamBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("requires_mod_buffer=" + serialization.RequiresModBuffer.ToString().ToLowerInvariant());
            builder.AppendLine();
            builder.AppendLine("Findings (deterministic order)");
            for (int index = 0; index < report.Findings.Count; index++)
            {
                DecorationAuditFinding finding = report.Findings[index];
                builder.AppendLine();
                builder.AppendLine("[" + (index + 1).ToString(CultureInfo.InvariantCulture) + "] " +
                                   finding.Severity + " / " + finding.Category + " / " + finding.Code);
                builder.AppendLine("id=" + finding.FindingId);
                builder.AppendLine("manager=" + finding.ManagerId);
                builder.AppendLine("decoration=" + finding.DecorationId);
                if (finding.RelatedDecorationIds.Count > 0)
                    builder.AppendLine("related=" + string.Join(",", finding.RelatedDecorationIds));
                builder.AppendLine("title=" + finding.Title);
                builder.AppendLine("detail=" + SingleLine(finding.Detail));
                if (finding.SuggestedRepair != null)
                {
                    builder.AppendLine("repair_safety=" + finding.SuggestedRepair.Safety);
                    builder.AppendLine("repair=" + SingleLine(finding.SuggestedRepair.Description));
                }
            }

            DecorationAuditRepairPlan safe = report.CreateRepairPlan();
            builder.AppendLine();
            builder.AppendLine("Safe dry-run repair plan");
            builder.AppendLine("operations=" + safe.Operations.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("excluded_review_or_destructive=" +
                               safe.ExcludedSuggestionCount.ToString(CultureInfo.InvariantCulture));
            foreach (DecorationAuditRepairOperation operation in safe.Operations)
            {
                builder.AppendLine(
                    operation.OperationId + " expected=" + operation.ExpectedValue +
                    " replacement=" + operation.ReplacementValue);
            }
            return builder.ToString().TrimEnd('\r', '\n');
        }

        private static string SingleLine(string value) =>
            (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
    }

    internal static class DecorationAuditRepairPlanner
    {
        internal static DecorationAuditRepairPlan Create(
            DecorationAuditReport report,
            DecorationAuditRepairInclusion inclusion)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var operations = new Dictionary<string, DecorationAuditRepairOperation>(
                StringComparer.Ordinal);
            var lockedTargets = new HashSet<string>(
                report.Source.Managers.SelectMany(manager =>
                    manager.Decorations
                        .Where(decoration => decoration.WorkspaceLocked)
                        .Select(decoration => RepairTargetKey(
                            manager.ManagerId,
                            decoration.DecorationId))),
                StringComparer.Ordinal);
            int excluded = 0;
            bool snapshotBlocksRepair = report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SnapshotIncomplete ||
                finding.Code == DecorationAuditCode.DuplicateManagerId);
            foreach (DecorationAuditFinding finding in report.Findings)
            {
                DecorationAuditRepairSuggestion suggestion = finding.SuggestedRepair;
                DecorationAuditRepairOperation operation = suggestion?.Operation;
                if (operation == null)
                    continue;
                if (snapshotBlocksRepair ||
                    lockedTargets.Contains(RepairTargetKey(
                        operation.ManagerId,
                        operation.DecorationId)) ||
                    !Includes(inclusion, operation.Safety))
                {
                    excluded++;
                    continue;
                }

                if (!operations.ContainsKey(operation.OperationId))
                    operations.Add(operation.OperationId, operation);
            }

            DecorationAuditRepairOperation[] ordered = operations.Values
                .OrderBy(operation => operation.ManagerId, StringComparer.Ordinal)
                .ThenBy(operation => operation.DecorationId, StringComparer.Ordinal)
                .ThenBy(operation => operation.Kind)
                .ThenBy(operation => operation.OperationId, StringComparer.Ordinal)
                .ToArray();
            return new DecorationAuditRepairPlan(
                report.Source.SourceId,
                report.SnapshotFingerprint,
                inclusion,
                ordered,
                excluded);
        }

        private static string RepairTargetKey(string managerId, string decorationId) =>
            (managerId ?? string.Empty) + "\n" + (decorationId ?? string.Empty);

        private static bool Includes(
            DecorationAuditRepairInclusion inclusion,
            DecorationAuditRepairSafety safety)
        {
            switch (inclusion)
            {
                case DecorationAuditRepairInclusion.IncludeDestructive:
                    return true;
                case DecorationAuditRepairInclusion.IncludeReviewRequired:
                    return safety != DecorationAuditRepairSafety.Destructive;
                default:
                    return safety == DecorationAuditRepairSafety.Safe;
            }
        }
    }
}
