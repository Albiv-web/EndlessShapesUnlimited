using System;
using System.Collections.Generic;
using System.Linq;
using DecoLimitLifter.DecorationEditMode;

internal static class DecorationAuditVerification
{
    internal static string Run()
    {
        Guid meshA = new Guid("10000000-0000-0000-0000-000000000001");
        Guid meshB = new Guid("20000000-0000-0000-0000-000000000002");
        var decorationA = new DecorationAuditDecorationSnapshot(
            "a",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(720d, -540d, 360d),
            meshA,
            1,
            storageKey: "storage-a");
        var decorationB = new DecorationAuditDecorationSnapshot(
            "b",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.MissingBlock,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            meshB,
            2,
            storageKey: "duplicate-storage");
        var decorationC = new DecorationAuditDecorationSnapshot(
            "c",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            meshB,
            2,
            storageKey: "duplicate-storage");
        var decorationD = new DecorationAuditDecorationSnapshot(
            "d",
            new DecorationAuditCell(int.MaxValue, 0, 0),
            DecorationAuditTetherState.OutsideConstruct,
            new DecorationAuditVector3(double.NaN, 0d, 0d),
            new DecorationAuditVector3(50_000d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            meshA,
            3);
        var decorationE = new DecorationAuditDecorationSnapshot(
            "e",
            new DecorationAuditCell(1, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            new Guid("40000000-0000-0000-0000-000000000004"),
            99,
            materialReplacement: new Guid("50000000-0000-0000-0000-000000000005"),
            meshMetadata: new DecorationAuditMeshMetadata(
                DecorationAuditReferenceState.Missing),
            materialReferenceState: DecorationAuditReferenceState.Missing,
            layerName: "Missing layer");
        var decorationF = new DecorationAuditDecorationSnapshot(
            "f",
            new DecorationAuditCell(2, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(2d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            new Guid("60000000-0000-0000-0000-000000000006"),
            4,
            meshMetadata: new DecorationAuditMeshMetadata(
                DecorationAuditReferenceState.Valid,
                metricsAvailable: true,
                localSize: new DecorationAuditVector3(600d, 2d, 2d),
                vertexCount: 1_000_001L,
                triangleCount: 2_000_001L),
            layerName: "Used layer");

        var serialization = new DecorationAuditSerializationSnapshot(
            available: true,
            exact: false,
            uncalibrated: true,
            wireFormat: "OverLimit",
            peakHeaderBytes: 1_100UL,
            peakDataBytes: 900UL,
            legacyHeaderMaximum: 100UL,
            legacyDataMaximum: 100UL,
            maximumHeaderBytes: 1_000UL,
            maximumDataBytes: 1_000UL,
            largestBlueprintStreamBytes: 2_000UL,
            maximumSaveBufferBytes: 1_500UL,
            requiresModBuffer: true);
        DecorationAuditDecorationSnapshot[] decorations =
        {
            decorationD,
            decorationB,
            decorationA,
            decorationC,
            decorationE,
            decorationF
        };
        var manager = new DecorationAuditManagerSnapshot(
            "manager",
            decorations,
            reportedDecorationCount: 6,
            capacityLimit: 6);
        var source = new DecorationAuditCraftSnapshot(
            "verification-craft",
            new[] { manager },
            serialization,
            layers: new[]
            {
                new DecorationAuditLayerSnapshot("Used layer"),
                new DecorationAuditLayerSnapshot("Unused layer")
            });
        var options = new DecorationAuditOptions(
            maximumAbsoluteTetherCoordinate: 100,
            maximumAbsolutePosition: 10d,
            minimumNonZeroScale: 0.001d,
            maximumAbsoluteScale: 10_000d,
            maximumAbsoluteOrientationDegrees: 360d,
            overlapTolerance: 0.001d,
            defaultManagerCapacity: 100,
            capacityWarningRatio: 0.5d,
            capacityErrorRatio: 0.75d,
            serializationWarningRatio: 0.8d,
            maximumRenderedMeshExtent: 1_000d,
            maximumMeshVertices: 1_000_000L,
            maximumMeshTriangles: 2_000_000L);

        DecorationAuditReport report = DecorationAuditEngine.Scan(source, options);
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.TetherMissingBlock),
            "Missing tethers are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.TetherCoordinateOutOfRange),
            "Implausible tether coordinates are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.PositionNonFinite),
            "Non-finite transforms are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.ScaleOutOfRange),
            "Out-of-range transforms are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.ColorOutOfRange) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.MeshReferenceMissing) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.MaterialReferenceMissing),
            "Invalid decoration properties and broken asset references are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.MeshRenderedExtentExceeded) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.MeshVertexCountExceeded) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.MeshTriangleCountExceeded),
            "Oversized rendered bounds and unusually complex meshes are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.DuplicateStorageKey),
            "Duplicate storage keys are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.ExactDuplicate),
            "Exact duplicate decorations are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SpatialOverlap),
            "Potential spatial overlaps are reported separately from exact duplicates.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SpatialOverlap &&
                finding.Detail.Contains("center-proximity fallback")),
            "Decorations with unavailable mesh bounds use the explicit conservative center fallback.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.LayerReferenceMissing) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.LayerUnused),
            "Broken layer assignments and unused current-craft layers are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.ManagerCapacityExceeded),
            "Per-manager capacity hotspots are reported.");
        Require(report.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SerializationCapacityExceeded) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.SerializationBufferExceeded) &&
                report.Findings.Any(finding =>
                    finding.Code == DecorationAuditCode.SerializationFormatOverLimit),
            "Serialization summary inputs produce hard-limit findings.");
        Require(IsDeterministicallyOrdered(report.Findings),
            "Findings have deterministic severity/category/target ordering.");
        DateTime exportTime = new DateTime(
            2026,
            7,
            18,
            12,
            34,
            56,
            DateTimeKind.Utc);
        string exported = DecorationAuditReportFormatter.Format(report, exportTime);
        Require(exported.Contains("generated_utc=2026-07-18T12:34:56.0000000Z") &&
                exported.Contains("snapshot_sha256=" + report.SnapshotFingerprint) &&
                exported.Contains("reference_findings=") &&
                exported.Contains("mesh_findings=") &&
                exported.Contains("layer_findings=") &&
                exported.Contains("MeshReferenceMissing") &&
                exported.Contains("MeshRenderedExtentExceeded") &&
                exported.Contains("LayerUnused") &&
                exported.Contains("Safe dry-run repair plan") &&
                exported.Contains("operations=1") &&
                exported == DecorationAuditReportFormatter.Format(report, exportTime),
            "Report export is deterministic for a supplied timestamp and includes every strict audit domain plus the dry-run plan.");

        var reversedManager = new DecorationAuditManagerSnapshot(
            "manager",
            decorations.Reverse(),
            reportedDecorationCount: 6,
            capacityLimit: 6);
        DecorationAuditReport reversed = DecorationAuditEngine.Scan(
            new DecorationAuditCraftSnapshot(
                "verification-craft",
                new[] { reversedManager },
                serialization,
                layers: new[]
                {
                    new DecorationAuditLayerSnapshot("Unused layer"),
                    new DecorationAuditLayerSnapshot("Used layer")
                }),
            options);
        Require(report.SnapshotFingerprint == reversed.SnapshotFingerprint &&
                report.Findings.Select(finding => finding.FindingId)
                    .SequenceEqual(reversed.Findings.Select(finding => finding.FindingId)),
            "Snapshot fingerprints and finding order do not depend on adapter enumeration order.");

        DecorationAuditRepairPlan safePlan = report.CreateRepairPlan();
        Require(safePlan.IsDryRun && safePlan.Operations.Count == 1 &&
                safePlan.Operations[0].Kind == DecorationAuditRepairKind.NormalizeOrientation &&
                safePlan.Operations[0].ReplacementValue.Equals(
                    new DecorationAuditVector3(0d, -180d, 0d)),
            "Safe-only dry runs include angle normalization and exclude review-required changes.");
        var staleAdapter = new RecordingRepairAdapter("stale");
        DecorationAuditRepairApplyResult stale = safePlan.ApplyExplicitly(staleAdapter);
        Require(stale.Status == DecorationAuditRepairApplyStatus.RejectedStaleSnapshot &&
                staleAdapter.ApplyCalls == 0,
            "A stale fingerprint prevents all mutation.");

        var matchingAdapter = new RecordingRepairAdapter(report.SnapshotFingerprint);
        Require(matchingAdapter.ApplyCalls == 0,
            "Creating a dry-run plan never invokes a repair adapter.");
        DecorationAuditRepairApplyResult applied = safePlan.ApplyExplicitly(matchingAdapter);
        Require(applied.Applied && matchingAdapter.ApplyCalls == 1 &&
                applied.AppliedOperationCount == safePlan.Operations.Count,
            "Repairs cross the mutation boundary only through explicit atomic apply.");

        DecorationAuditRepairPlan reviewPlan = report.CreateRepairPlan(
            DecorationAuditRepairInclusion.IncludeReviewRequired);
        Require(reviewPlan.Operations.Count > safePlan.Operations.Count &&
                reviewPlan.Operations.Any(operation =>
                    operation.Kind == DecorationAuditRepairKind.ResetPosition) &&
                reviewPlan.Operations.Any(operation =>
                    operation.Kind == DecorationAuditRepairKind.ClampScale),
            "Review-required repairs remain available but opt-in.");

        var incompleteManager = new DecorationAuditManagerSnapshot(
            "incomplete-manager",
            new[] { decorationA },
            captureComplete: false,
            captureDiagnostic: "Verification capture failure.");
        DecorationAuditRepairPlan incompletePlan = DecorationAuditEngine.Scan(
                new DecorationAuditCraftSnapshot(
                    "incomplete-verification-craft",
                    new[] { incompleteManager }))
            .CreateRepairPlan(DecorationAuditRepairInclusion.IncludeDestructive);
        Require(incompletePlan.Operations.Count == 0 &&
                incompletePlan.ExcludedSuggestionCount == 1,
            "Incomplete snapshots cannot produce executable repairs at any inclusion level.");

        var lockedDecoration = new DecorationAuditDecorationSnapshot(
            decorationA.DecorationId,
            decorationA.Tether,
            decorationA.TetherState,
            decorationA.Position,
            decorationA.Scale,
            decorationA.Orientation,
            decorationA.MeshGuid,
            decorationA.Color,
            storageKey: decorationA.StorageKey,
            workspaceLocked: true);
        DecorationAuditCraftSnapshot unlockedLockSource = new DecorationAuditCraftSnapshot(
            "workspace-lock-verification",
            new[]
            {
                new DecorationAuditManagerSnapshot("manager", new[] { decorationA })
            });
        DecorationAuditCraftSnapshot lockedLockSource = new DecorationAuditCraftSnapshot(
            "workspace-lock-verification",
            new[]
            {
                new DecorationAuditManagerSnapshot("manager", new[] { lockedDecoration })
            });
        DecorationAuditRepairPlan unlockedLockPlan =
            DecorationAuditEngine.Scan(unlockedLockSource).CreateRepairPlan();
        DecorationAuditReport lockedLockReport = DecorationAuditEngine.Scan(lockedLockSource);
        DecorationAuditRepairPlan lockedLockPlan = lockedLockReport.CreateRepairPlan();
        Require(unlockedLockPlan.Operations.Count == 1 &&
                lockedLockPlan.Operations.Count == 0 &&
                lockedLockPlan.ExcludedSuggestionCount == 1 &&
                DecorationAuditEngine.ComputeSnapshotFingerprint(unlockedLockSource) !=
                lockedLockReport.SnapshotFingerprint,
            "Per-object workspace locks participate in stale fingerprints and exclude every locked repair target.");

        var duplicateManagerA = new DecorationAuditManagerSnapshot(
            "ambiguous-manager",
            new[] { decorationA });
        var duplicateManagerB = new DecorationAuditManagerSnapshot(
            "ambiguous-manager",
            Array.Empty<DecorationAuditDecorationSnapshot>());
        DecorationAuditRepairPlan ambiguousPlan = DecorationAuditEngine.Scan(
                new DecorationAuditCraftSnapshot(
                    "ambiguous-verification-craft",
                    new[] { duplicateManagerA, duplicateManagerB }))
            .CreateRepairPlan(DecorationAuditRepairInclusion.IncludeDestructive);
        Require(ambiguousPlan.Operations.Count == 0,
            "Duplicate manager ids block every executable repair target.");
        string ambiguousForwardFingerprint = DecorationAuditEngine.ComputeSnapshotFingerprint(
            new DecorationAuditCraftSnapshot(
                "ambiguous-verification-craft",
                new[] { duplicateManagerA, duplicateManagerB }));
        string ambiguousReverseFingerprint = DecorationAuditEngine.ComputeSnapshotFingerprint(
            new DecorationAuditCraftSnapshot(
                "ambiguous-verification-craft",
                new[] { duplicateManagerB, duplicateManagerA }));
        Require(ambiguousForwardFingerprint == ambiguousReverseFingerprint,
            "Duplicate manager ids do not make snapshot fingerprints enumeration-dependent.");

        var differentMaterial = new DecorationAuditDecorationSnapshot(
            "material-b",
            decorationB.Tether,
            decorationB.TetherState,
            decorationB.Position,
            decorationB.Scale,
            decorationB.Orientation,
            decorationB.MeshGuid,
            decorationB.Color,
            materialReplacement: new Guid("30000000-0000-0000-0000-000000000003"));
        DecorationAuditReport materialReport = DecorationAuditEngine.Scan(
            new DecorationAuditCraftSnapshot(
                "material-verification-craft",
                new[]
                {
                    new DecorationAuditManagerSnapshot(
                        "material-manager",
                        new[] { decorationB, differentMaterial })
                }));
        Require(!materialReport.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.ExactDuplicate),
            "Different material overrides are not reported as exact duplicates.");

        VerifyTransformedMeshBoundsOverlap();
        VerifyOverlapFallbackAndBroadPhaseBound();

        return "PASS: Decoration audit domain verification completed.";
    }

    private static void VerifyTransformedMeshBoundsOverlap()
    {
        Guid elongatedMesh = new Guid("71000000-0000-0000-0000-000000000001");
        Guid cubeMesh = new Guid("72000000-0000-0000-0000-000000000002");
        var elongatedMetadata = new DecorationAuditMeshMetadata(
            DecorationAuditReferenceState.Valid,
            metricsAvailable: true,
            localSize: new DecorationAuditVector3(4d, 1d, 1d),
            localCenter: new DecorationAuditVector3(1d, 0d, 0d));
        var cubeMetadata = new DecorationAuditMeshMetadata(
            DecorationAuditReferenceState.Valid,
            metricsAvailable: true,
            localSize: new DecorationAuditVector3(1d, 1d, 1d));
        var rotatedScaledOffset = new DecorationAuditDecorationSnapshot(
            "rotated-scaled-offset",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 0d, 0d),
            new DecorationAuditVector3(2d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 90d),
            elongatedMesh,
            1,
            meshMetadata: elongatedMetadata);
        var offsetCenterIntersection = new DecorationAuditDecorationSnapshot(
            "offset-center-intersection",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 5.75d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            cubeMesh,
            2,
            meshMetadata: cubeMetadata);
        var separated = new DecorationAuditDecorationSnapshot(
            "separated",
            new DecorationAuditCell(0, 0, 0),
            DecorationAuditTetherState.Valid,
            new DecorationAuditVector3(0d, 7d, 0d),
            new DecorationAuditVector3(1d, 1d, 1d),
            new DecorationAuditVector3(0d, 0d, 0d),
            cubeMesh,
            3,
            meshMetadata: cubeMetadata);
        var source = new DecorationAuditCraftSnapshot(
            "transformed-overlap",
            new[]
            {
                new DecorationAuditManagerSnapshot(
                    "transformed-manager",
                    new[] { separated, offsetCenterIntersection, rotatedScaledOffset })
            });
        DecorationAuditReport report = DecorationAuditEngine.Scan(source);
        DecorationAuditFinding overlap = report.Findings.Single(finding =>
            finding.Code == DecorationAuditCode.SpatialOverlap);
        Require(
            overlap.RelatedDecorationIds.SequenceEqual(
                new[] { "offset-center-intersection", "rotated-scaled-offset" }) &&
            !overlap.RelatedDecorationIds.Contains("separated") &&
            overlap.Detail.Contains("transformed oriented mesh bounds intersect"),
            "Rotated, scaled, local-center-offset mesh bounds detect an intersection between offset decoration centers without including a nearby non-overlap.");

        var changedCenterMetadata = new DecorationAuditMeshMetadata(
            DecorationAuditReferenceState.Valid,
            metricsAvailable: true,
            localSize: new DecorationAuditVector3(4d, 1d, 1d),
            localCenter: new DecorationAuditVector3(0d, 0d, 0d));
        var changedCenter = new DecorationAuditDecorationSnapshot(
            rotatedScaledOffset.DecorationId,
            rotatedScaledOffset.Tether,
            rotatedScaledOffset.TetherState,
            rotatedScaledOffset.Position,
            rotatedScaledOffset.Scale,
            rotatedScaledOffset.Orientation,
            rotatedScaledOffset.MeshGuid,
            rotatedScaledOffset.Color,
            meshMetadata: changedCenterMetadata);
        var changedSource = new DecorationAuditCraftSnapshot(
            "transformed-overlap",
            new[]
            {
                new DecorationAuditManagerSnapshot(
                    "transformed-manager",
                    new[] { separated, offsetCenterIntersection, changedCenter })
            });
        Require(
            report.SnapshotFingerprint !=
            DecorationAuditEngine.ComputeSnapshotFingerprint(changedSource),
            "Mesh local bounds centers participate in the immutable audit fingerprint.");

        DecorationAuditReport reversed = DecorationAuditEngine.Scan(
            new DecorationAuditCraftSnapshot(
                "transformed-overlap",
                new[]
                {
                    new DecorationAuditManagerSnapshot(
                        "transformed-manager",
                        new[] { rotatedScaledOffset, offsetCenterIntersection, separated })
                }));
        Require(
            report.Findings.Select(finding => finding.FindingId)
                .SequenceEqual(reversed.Findings.Select(finding => finding.FindingId)),
            "Transformed-bounds overlap groups remain deterministic across adapter enumeration order.");
    }

    private static void VerifyOverlapFallbackAndBroadPhaseBound()
    {
        Guid mesh = new Guid("73000000-0000-0000-0000-000000000003");
        DecorationAuditDecorationSnapshot Fallback(string id, double x) =>
            new DecorationAuditDecorationSnapshot(
                id,
                new DecorationAuditCell(0, 0, 0),
                DecorationAuditTetherState.Valid,
                new DecorationAuditVector3(x, 0d, 0d),
                new DecorationAuditVector3(1d, 1d, 1d),
                new DecorationAuditVector3(0d, 0d, 0d),
                mesh,
                1);
        DecorationAuditReport fallbackReport = DecorationAuditEngine.Scan(
            new DecorationAuditCraftSnapshot(
                "fallback-overlap",
                new[]
                {
                    new DecorationAuditManagerSnapshot(
                        "fallback-manager",
                        new[]
                        {
                            Fallback("fallback-near-b", 0.0005d),
                            Fallback("fallback-far", 1d),
                            Fallback("fallback-near-a", 0d)
                        })
                }),
            new DecorationAuditOptions(overlapTolerance: 0.001d));
        DecorationAuditFinding fallbackOverlap = fallbackReport.Findings.Single(finding =>
            finding.Code == DecorationAuditCode.SpatialOverlap);
        Require(
            fallbackOverlap.RelatedDecorationIds.SequenceEqual(
                new[] { "fallback-near-a", "fallback-near-b" }) &&
            fallbackOverlap.Detail.Contains("center-proximity fallback"),
            "Unavailable mesh bounds fall back to bounded center proximity without pulling in a separated decoration.");

        var longThinMetadata = new DecorationAuditMeshMetadata(
            DecorationAuditReferenceState.Valid,
            metricsAvailable: true,
            localSize: new DecorationAuditVector3(100d, 1d, 1d));
        DecorationAuditDecorationSnapshot LongThin(string id, double y) =>
            new DecorationAuditDecorationSnapshot(
                id,
                new DecorationAuditCell(0, 0, 0),
                DecorationAuditTetherState.Valid,
                new DecorationAuditVector3(0d, y, 0d),
                new DecorationAuditVector3(1d, 1d, 1d),
                new DecorationAuditVector3(0d, 0d, 0d),
                mesh,
                1,
                meshMetadata: longThinMetadata);
        DecorationAuditReport bounded = DecorationAuditEngine.Scan(
            new DecorationAuditCraftSnapshot(
                "bounded-overlap",
                new[]
                {
                    new DecorationAuditManagerSnapshot(
                        "bounded-manager",
                        new[]
                        {
                            LongThin("long-a", 0d),
                            LongThin("long-b", 10d),
                            LongThin("long-c", 20d)
                        })
                }),
            new DecorationAuditOptions(maximumOverlapPairChecks: 1));
        Require(
            bounded.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SpatialOverlapScanLimit) &&
            !bounded.Findings.Any(finding =>
                finding.Code == DecorationAuditCode.SpatialOverlap),
            "The sweep broad phase counts even long-axis/non-overlap candidates against its strict pair-check budget and reports truncation without inventing overlaps.");
    }

    private static bool IsDeterministicallyOrdered(
        IReadOnlyList<DecorationAuditFinding> findings)
    {
        for (int index = 1; index < findings.Count; index++)
        {
            DecorationAuditFinding previous = findings[index - 1];
            DecorationAuditFinding current = findings[index];
            int comparison = current.Severity.CompareTo(previous.Severity);
            if (comparison > 0)
                return false;
            if (comparison < 0)
                continue;
            comparison = previous.Category.CompareTo(current.Category);
            if (comparison > 0)
                return false;
            if (comparison < 0)
                continue;
            comparison = previous.Code.CompareTo(current.Code);
            if (comparison > 0)
                return false;
            if (comparison < 0)
                continue;
            comparison = string.Compare(
                previous.ManagerId,
                current.ManagerId,
                StringComparison.Ordinal);
            if (comparison > 0)
                return false;
            if (comparison < 0)
                continue;
            if (string.Compare(
                    previous.DecorationId,
                    current.DecorationId,
                    StringComparison.Ordinal) > 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Decoration audit verification failed: " + message);
    }

    private sealed class RecordingRepairAdapter : IDecorationAuditRepairAdapter
    {
        private readonly string _fingerprint;

        internal RecordingRepairAdapter(string fingerprint)
        {
            _fingerprint = fingerprint;
        }

        internal int ApplyCalls { get; private set; }

        public string GetCurrentSnapshotFingerprint(string sourceId) => _fingerprint;

        public DecorationAuditRepairApplyResult ApplyAtomically(
            DecorationAuditRepairPlan plan)
        {
            ApplyCalls++;
            return new DecorationAuditRepairApplyResult(
                DecorationAuditRepairApplyStatus.Applied,
                plan.Operations.Count,
                "Applied by verification adapter.");
        }
    }
}
