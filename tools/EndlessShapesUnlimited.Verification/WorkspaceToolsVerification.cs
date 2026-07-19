using System;
using System.IO;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.Presets;
using UnityEngine;

internal static partial class Program
{
    private static void VerifyWorkspaceTools()
    {
        Assert(
            DecorationAuditVerification.Run() == "PASS: Decoration audit domain verification completed.",
            "Craft audit detects risk categories, produces dry-run repairs, and protects the atomic apply boundary.");
        VerifyDecorationLayoutTools();
        VerifyDecorationPrecisionSnapTools();
        VerifyDecorationBulkTools();
        VerifyDecorationLayerWorkspace();
        VerifyWorkspaceSessionIntegration();
        VerifyDecorationGroupPresetPayload();
        Assert(
            SurfaceModelingVerification.Run() == "Surface modeling verification passed.",
            "Advanced Surface modeling operations pass isolated topology and atomicity verification.");
    }

    private static void VerifyDecorationLayoutTools()
    {
        var items = new[]
        {
            new DecorationLayoutItem("a", new Vector3(0f, 0f, 0f), Vector3.one, Vector3.zero, Vector3.one),
            new DecorationLayoutItem("b", new Vector3(5f, 2f, 0f), Vector3.one, new Vector3(0f, 10f, 0f), new Vector3(2f, 2f, 2f)),
            new DecorationLayoutItem("c", new Vector3(20f, 4f, 0f), Vector3.one, new Vector3(0f, 20f, 0f), new Vector3(3f, 3f, 3f))
        };

        bool aligned = DecorationLayoutTools.TryAlign(
            items,
            "a",
            DecorationLayoutAxis.Y,
            DecorationAlignmentMode.Center,
            out DecorationLayoutPlan alignPlan,
            out string alignMessage);
        Assert(
            aligned && alignPlan.IsValid && alignPlan.ChangedCount == 2 &&
            alignPlan.After.All(item => Math.Abs(item.Center.y) < 0.0001f),
            "Precision layout aligns selected centers to the primary reference. " + alignMessage);

        bool distributed = DecorationLayoutTools.TryDistribute(
            items,
            DecorationLayoutAxis.X,
            useEdges: false,
            out DecorationLayoutPlan distributePlan,
            out string distributeMessage);
        Assert(
            distributed && distributePlan.IsValid &&
            Math.Abs(distributePlan.After[1].Center.x - 10f) < 0.0001f,
            "Precision layout distributes centers evenly and preserves the outer endpoints. " + distributeMessage);

        bool matched = DecorationLayoutTools.TryMatchTransform(
            items,
            "a",
            matchRotation: true,
            matchScale: true,
            out DecorationLayoutPlan matchPlan,
            out string matchMessage);
        Assert(
            matched && matchPlan.ChangedCount == 2 &&
            matchPlan.After.Skip(1).All(item => item.Rotation == Vector3.zero && item.Scale == Vector3.one),
            "Precision layout matches rotation and scale to the primary object. " + matchMessage);

        bool linear = DecorationLayoutTools.TryCreateLinearArray(
            items.Take(2).ToArray(),
            4,
            new Vector3(2f, 0f, 0f),
            out var linearCopies,
            out string linearMessage);
        bool radial = DecorationLayoutTools.TryCreateRadialArray(
            items.Take(1).ToArray(),
            4,
            Vector3.zero,
            Vector3.up,
            360f,
            rotateCopies: false,
            out var radialCopies,
            out string radialMessage);
        Assert(
            linear && linearCopies.Count == 4 && linearCopies[3][0].Center.x == 6f &&
            radial && radialCopies.Count == 4 &&
            !DecorationLayoutTools.TryCreateLinearArray(
                items,
                DecorationLayoutTools.MaximumArrayOutput,
                Vector3.one,
                out _,
                out _),
            "Linear/radial array planners generate bounded finite transforms and reject oversized output. " +
            linearMessage + " " + radialMessage);

        var locked = new[]
        {
            items[0],
            new DecorationLayoutItem("editable", new Vector3(2f, 3f, 0f), Vector3.one, Vector3.zero, Vector3.one),
            new DecorationLayoutItem("locked", new Vector3(3f, 1f, 0f), Vector3.one, Vector3.zero, Vector3.one, editable: false)
        };
        Assert(
            DecorationLayoutTools.TryAlign(
                locked,
                "a",
                DecorationLayoutAxis.Y,
                DecorationAlignmentMode.Center,
                out DecorationLayoutPlan lockedPlan,
                out _) &&
            lockedPlan.SkippedLocked == 1 && lockedPlan.After[2].Center.y == 1f &&
            lockedPlan.After[1].Center.y == 0f,
            "Layout plans preserve locked decorations while reporting the skipped count.");
    }

    private static void VerifyDecorationLayerWorkspace()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "esu-layer-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new DecorationLayerWorkspace(root);
            Assert(
                workspace.TryCreateLayer(" Hull ", " Structure ", out string createMessage) &&
                workspace.TryCreateLayer("Details", "Detail work", out _) &&
                !workspace.TryCreateLayer("hull", out _),
                "Layer workspace creates normalized unique named layers inside named folders. " + createMessage);
            string folderMessage = string.Empty;
            Assert(
                workspace.Layers.Single(layer => layer.Name == "Hull").Folder == "Structure" &&
                workspace.TrySetLayerFolder("Hull", "Armour", out folderMessage) &&
                workspace.Layers.Single(layer => layer.Name == "Hull").Folder == "Armour" &&
                !workspace.TrySetLayerFolder("Hull", "bad/folder", out _),
                "Named layer folders are normalized, editable, bounded, and reject unsafe paths. " + folderMessage);
            Assert(
                workspace.TryAssign(new[] { "craft|uid:1", "craft|uid:2" }, "Hull", out string assignMessage) &&
                workspace.LayerFor("craft|uid:2") == "Hull",
                "Layer workspace persists multi-object assignments. " + assignMessage);
            Assert(
                workspace.TrySetObjectLock(new[] { "craft|uid:1" }, true, out _) &&
                workspace.IsLocked("craft|uid:1") &&
                !workspace.IsLocked("craft|uid:2") &&
                workspace.TrySetLayerState("Hull", null, true, out _) &&
                workspace.IsLocked("craft|uid:2"),
                "Independent and layer-wide edit locks compose without mutating decoration data.");
            Assert(
                workspace.TrySetObjectTags(
                    new[] { "craft|uid:1" },
                    new[] { " armour ", "Hull", "armour" },
                    out _) &&
                workspace.TagsFor("craft|uid:1").SequenceEqual(new[] { "armour", "Hull" }),
                "Layer workspace normalizes and persists per-object tags.");
            Assert(
                workspace.TrySetLayerState("Hull", visible: false, locked: null, out _) &&
                !workspace.IsVisible("craft|uid:1") &&
                workspace.TrySetLayerState("Hull", visible: true, locked: null, out _) &&
                workspace.TrySetIsolatedLayer("Details", out _) &&
                !workspace.IsVisible("craft|uid:1") &&
                workspace.TrySetIsolatedLayer(string.Empty, out _) &&
                workspace.IsVisible("craft|uid:1"),
                "Layer visibility and isolate mode resolve deterministically.");

            var reloaded = new DecorationLayerWorkspace(root);
            Assert(
                reloaded.LayerFor("craft|uid:1") == "Hull" &&
                reloaded.Layers.Single(layer => layer.Name == "Hull").Folder == "Armour" &&
                reloaded.IsLocked("craft|uid:1") &&
                reloaded.TagsFor("craft|uid:1").Contains("armour"),
                "Layer assignments, folders, tags, and real edit locks survive a workspace reload.");

            Assert(
                reloaded.TryDeleteLayer("Hull", out string deleteMessage) &&
                reloaded.LayerFor("craft|uid:1") == string.Empty &&
                reloaded.IsLocked("craft|uid:1") &&
                reloaded.TagsFor("craft|uid:1").SequenceEqual(new[] { "armour", "Hull" }) &&
                !reloaded.IsLocked("craft|uid:2"),
                "Deleting a layer clears only layer membership while preserving independent object locks and tags. " +
                deleteMessage);
            var deletedLayerReload = new DecorationLayerWorkspace(root);
            Assert(
                deletedLayerReload.LayerFor("craft|uid:1") == string.Empty &&
                deletedLayerReload.IsLocked("craft|uid:1") &&
                deletedLayerReload.TagsFor("craft|uid:1").Contains("armour"),
                "Object locks and tags preserved during layer deletion survive a workspace reload.");

            string craftMain = DecorationWorkspaceObjectIdentity.ComposeConstructScope(
                "craft-force:41|craft-main:7",
                -1);
            string firstSubconstruct = DecorationWorkspaceObjectIdentity.ComposeConstructScope(
                "craft-force:41|craft-main:7",
                0);
            string secondCraftFirstSubconstruct = DecorationWorkspaceObjectIdentity.ComposeConstructScope(
                "craft-force:41|craft-main:8",
                0);
            Assert(
                craftMain != firstSubconstruct &&
                firstSubconstruct != secondCraftFirstSubconstruct &&
                firstSubconstruct.EndsWith("|sub:0", StringComparison.Ordinal),
                "Workspace identity treats subconstruct index zero as valid and scopes identical subconstruct indices to their owning main craft.");

            File.WriteAllText(deletedLayerReload.FilePath, "{corrupt");
            var fallback = new DecorationLayerWorkspace(root);
            Assert(
                fallback.Layers.Count >= 1,
                "Layer workspace falls back to its last transactional backup when the primary sidecar is corrupt.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void VerifyDecorationPrecisionSnapTools()
    {
        var items = new[]
        {
            new DecorationLayoutItem(
                "primary",
                new Vector3(2f, 3f, 4f),
                new Vector3(1f, 2f, 0.5f),
                Vector3.zero,
                Vector3.one),
            new DecorationLayoutItem(
                "member",
                new Vector3(4f, 6f, 4f),
                Vector3.one,
                Vector3.zero,
                Vector3.one),
            new DecorationLayoutItem(
                "locked",
                new Vector3(9f, 9f, 9f),
                Vector3.one,
                Vector3.zero,
                Vector3.one,
                editable: false)
        };

        bool surface = DecorationPrecisionSnapTools.TrySnapToSurface(
            items,
            "primary",
            Vector3.zero,
            Vector3.up,
            out DecorationLayoutPlan surfacePlan,
            out string surfaceMessage);
        Assert(
            surface && surfacePlan.IsValid && surfacePlan.SkippedLocked == 1 &&
            surfacePlan.After[0].Center == new Vector3(0f, 2f, 0f) &&
            surfacePlan.After[1].Center == new Vector3(2f, 5f, 0f) &&
            surfacePlan.After[2].Center == items[2].Center,
            "Surface snap places the primary bounds support on the picked plane and preserves editable group offsets. " +
            surfaceMessage);

        bool anchor = DecorationPrecisionSnapTools.TrySnapToAnchor(
            items,
            "primary",
            new Vector3(10f, 20f, 30f),
            out DecorationLayoutPlan anchorPlan,
            out string anchorMessage);
        Assert(
            anchor && anchorPlan.After[0].Center == new Vector3(10f, 20f, 30f) &&
            anchorPlan.After[1].Center == new Vector3(12f, 23f, 30f),
            "Anchor snap translates the editable selection so the primary center lands exactly on the anchor. " +
            anchorMessage);

        bool axis = DecorationPrecisionSnapTools.TrySnapToAxis(
            items,
            "primary",
            Vector3.zero,
            Vector3.up,
            out DecorationLayoutPlan axisPlan,
            out string axisMessage);
        Assert(
            axis && axisPlan.After[0].Center == new Vector3(0f, 3f, 0f) &&
            axisPlan.After[1].Center == new Vector3(2f, 6f, 0f),
            "Axis snap projects the primary center onto an arbitrary finite axis while preserving group layout. " +
            axisMessage);

        Assert(
            !DecorationPrecisionSnapTools.TrySnapToSurface(
                items,
                "primary",
                Vector3.zero,
                Vector3.zero,
                out DecorationLayoutPlan invalidPlan,
                out _) && invalidPlan == null,
            "Precision snap rejects an invalid surface normal without producing a partial plan.");

        var first = new DecorationLayoutItem(
            "first", Vector3.zero, Vector3.one, Vector3.zero, Vector3.one);
        var second = new DecorationLayoutItem(
            "second", new Vector3(5f, 4f, 0f), Vector3.one, Vector3.zero, Vector3.one);
        Assert(
            DecorationPrecisionSnapTools.TryMeasure(
                first,
                second,
                out DecorationPrecisionMeasurement measurement,
                out string measureMessage) &&
            Math.Abs(measurement.CenterDistance - (float)Math.Sqrt(41d)) < 0.00001f &&
            measurement.ClearanceByAxis == new Vector3(3f, 2f, 0f) &&
            Math.Abs(measurement.BoundsClearance - (float)Math.Sqrt(13d)) < 0.00001f,
            "Ruler math reports exact center deltas and axis-aligned bounds clearance. " + measureMessage);
    }

    private static void VerifyDecorationBulkTools()
    {
        Guid mesh = new Guid("901b236a-0dc0-4e41-bbbe-99f7de31b3a3");
        Guid otherMesh = new Guid("4e01b49e-af55-4692-ae1a-722ed7c115a0");
        Guid material = new Guid("38f25fb1-1eab-438f-9ecb-4efe7295c3e2");
        Assert(
            DecorationWorkspaceBulkTools.MatchesMesh(mesh, mesh) &&
            !DecorationWorkspaceBulkTools.MatchesMesh(mesh, otherMesh) &&
            DecorationWorkspaceBulkTools.MatchesColor(17, 17) &&
            !DecorationWorkspaceBulkTools.MatchesColor(17, 18) &&
            DecorationWorkspaceBulkTools.MatchesMaterial(material, material) &&
            !DecorationWorkspaceBulkTools.MatchesMaterial(material, Guid.Empty) &&
            DecorationWorkspaceBulkTools.MatchesAnchor(
                new Vector3i(2, -3, 4),
                new Vector3i(2, -3, 4)) &&
            !DecorationWorkspaceBulkTools.MatchesAnchor(
                new Vector3i(2, -3, 4),
                new Vector3i(2, -3, 5)),
            "Decoration bulk predicates perform exact mesh/color/material/anchor matching without display-name approximations.");

        string outlinerSearch =
            "Subconstruct 3 8,2,-1 alloy beam " + mesh.ToString("D") +
            " 17 " + material.ToString("D") + " Details armour";
        Assert(
            DecorationWorkspaceBulkTools.SearchMatches(outlinerSearch, "subconstruct 3") &&
            DecorationWorkspaceBulkTools.SearchMatches(outlinerSearch, "ARMOUR") &&
            !DecorationWorkspaceBulkTools.SearchMatches(outlinerSearch, "main construct") &&
            !DecorationWorkspaceBulkTools.SearchMatches(outlinerSearch, "   "),
            "Filter selection uses the same case-insensitive searchable construct-label/tag semantics as visible Outliner rows.");

        var frameA = new object();
        var frameB = new object();
        Assert(
            DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                new[] { frameA, frameA },
                out object resolvedFrame) &&
            ReferenceEquals(resolvedFrame, frameA) &&
            !DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                new[] { frameA, frameB },
                out _) &&
            !DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                new object[] { frameA, null },
                out _),
            "Construct-local spatial operations accept one exact frame and fail closed for mixed or unresolved frames.");

        bool transformed = DecorationWorkspaceBulkTools.TryTransformMeshBounds(
            new Bounds(Vector3.zero, new Vector3(4f, 2f, 2f)),
            new Vector3(10f, 0f, 0f),
            new Vector3(0f, 0f, 90f),
            Vector3.one,
            out DecorationWorkspaceBounds rotated);
        var longSelected = new DecorationWorkspaceBounds(
            new Vector3(-2f, -0.5f, -0.5f),
            new Vector3(2f, 0.5f, 0.5f));
        var nearLongNeighbor = new DecorationWorkspaceBounds(
            new Vector3(2.05f, -0.5f, -0.5f),
            new Vector3(6.05f, 0.5f, 0.5f));
        var separated = new DecorationWorkspaceBounds(
            new Vector3(2.2f, -0.5f, -0.5f),
            new Vector3(6.2f, 0.5f, 0.5f));
        DecorationWorkspaceBounds fallback = DecorationWorkspaceBulkTools.FallbackBounds(
            Vector3.zero,
            new Vector3(100f, 100f, 100f));
        Assert(
            transformed && rotated.IsValid &&
            VectorApproximately(rotated.Min, new Vector3(9f, -2f, -1f), 0.0001f) &&
            VectorApproximately(rotated.Max, new Vector3(11f, 2f, 1f), 0.0001f) &&
            DecorationWorkspaceBulkTools.BoundsTouchOrNear(
                longSelected,
                nearLongNeighbor,
                DecorationWorkspaceBulkTools.GrowAdjacencyTolerance) &&
            !DecorationWorkspaceBulkTools.BoundsTouchOrNear(
                longSelected,
                separated,
                DecorationWorkspaceBulkTools.GrowAdjacencyTolerance) &&
            fallback.IsValid &&
            fallback.Min == new Vector3(-2f, -2f, -2f) &&
            fallback.Max == new Vector3(2f, 2f, 2f),
            "Decoration Grow uses rotated/scaled mesh AABB adjacency, including long meshes with distant centers, and a strictly bounded fallback only when mesh bounds are unavailable.");

        bool offsetTransformed = DecorationWorkspaceBulkTools.TryTransformMeshBounds(
            new Bounds(
                new Vector3(1f, 0.5f, 0f),
                new Vector3(2f, 4f, 2f)),
            new Vector3(10f, 0f, 0f),
            new Vector3(0f, 0f, 90f),
            new Vector3(1f, 2f, 1f),
            out DecorationWorkspaceBounds offsetBounds);
        var secondBounds = new DecorationWorkspaceBounds(
            new Vector3(14f, 0f, -1f),
            new Vector3(16f, 2f, 1f));
        var exactBoundsItems = new[]
        {
            new DecorationLayoutItem(
                "offset-primary",
                offsetBounds.Center,
                offsetBounds.Extents,
                Vector3.zero,
                Vector3.one),
            new DecorationLayoutItem(
                "offset-secondary",
                secondBounds.Center,
                secondBounds.Extents,
                Vector3.zero,
                Vector3.one)
        };
        DecorationPrecisionMeasurement exactMeasurement = null;
        string exactMeasureMessage = string.Empty;
        bool exactMeasured = offsetTransformed &&
                             DecorationPrecisionSnapTools.TryMeasure(
                                 exactBoundsItems[0],
                                 exactBoundsItems[1],
                                 out exactMeasurement,
                                 out exactMeasureMessage);
        DecorationLayoutPlan exactAlignPlan = null;
        string exactAlignMessage = string.Empty;
        bool exactAligned = offsetTransformed &&
                            DecorationLayoutTools.TryAlign(
                                exactBoundsItems,
                                "offset-primary",
                                DecorationLayoutAxis.X,
                                DecorationAlignmentMode.MinimumEdge,
                                out exactAlignPlan,
                                out exactAlignMessage);
        Assert(
            offsetTransformed && offsetBounds.IsValid &&
            VectorApproximately(offsetBounds.Center, new Vector3(9f, 1f, 0f), 0.0001f) &&
            VectorApproximately(offsetBounds.Extents, new Vector3(4f, 1f, 1f), 0.0001f) &&
            VectorApproximately(offsetBounds.Min, new Vector3(5f, 0f, -1f), 0.0001f) &&
            VectorApproximately(offsetBounds.Max, new Vector3(13f, 2f, 1f), 0.0001f) &&
            exactMeasured &&
            VectorApproximately(exactMeasurement.Delta, new Vector3(6f, 0f, 0f), 0.0001f) &&
            Math.Abs(exactMeasurement.CenterDistance - 6f) < 0.0001f &&
            VectorApproximately(exactMeasurement.ClearanceByAxis, new Vector3(1f, 0f, 0f), 0.0001f) &&
            Math.Abs(exactMeasurement.BoundsClearance - 1f) < 0.0001f &&
            exactAligned &&
            Math.Abs(exactAlignPlan.After[1].Center.x - 6f) < 0.0001f,
            "Rotated, scaled, offset mesh AABBs drive exact ruler clearance and edge alignment instead of abs(scale)/2 extents. " +
            exactMeasureMessage + " " + exactAlignMessage);

        bool originTranslated =
            DecorationWorkspaceBulkTools.TryTranslateDecorationOriginForBoundsCenterMove(
                new Vector3(10f, 0f, 0f),
                new Vector3(9f, 1f, 0f),
                new Vector3(5f, 1f, 0f),
                out Vector3 translatedOrigin);
        Assert(
            originTranslated &&
            VectorApproximately(translatedOrigin, new Vector3(6f, 0f, 0f), 0.0001f) &&
            !DecorationWorkspaceBulkTools.TryTranslateDecorationOriginForBoundsCenterMove(
                new Vector3(float.NaN, 0f, 0f),
                Vector3.zero,
                Vector3.one,
                out _),
            "Layout applies the planned bounds-center delta to the decoration origin, preserving deliberate mesh-center offsets and rejecting non-finite moves.");

        bool sampleValid = DecorationEditSnapshot.TryCreatePortable(
            new Vector3i(1, 1, 1),
            Vector3.zero,
            new Vector3(-2f, 3f, 4f),
            new Vector3(10f, 20f, 30f),
            mesh,
            17,
            true,
            material,
            out DecorationEditSnapshot sample,
            out string sampleMessage);
        bool targetValid = DecorationEditSnapshot.TryCreatePortable(
            new Vector3i(9, 8, 7),
            new Vector3(0.25f, -0.5f, 0.75f),
            Vector3.one,
            Vector3.zero,
            otherMesh,
            2,
            false,
            Guid.Empty,
            out DecorationEditSnapshot target,
            out string targetMessage);
        DecorationEditSnapshot result = null;
        string applyMessage = "The source or target snapshot was invalid.";
        bool applied = sampleValid && targetValid &&
                       DecorationWorkspaceBulkTools.TryBuildEyedropperResult(
                           sample,
                           target,
                           out result,
                           out applyMessage);
        Assert(
            applied &&
            result.TetherPoint.Equals(target.TetherPoint) &&
            result.Positioning == target.Positioning &&
            result.MeshGuid == sample.MeshGuid &&
            result.Orientation == sample.Orientation &&
            result.Scaling == sample.Scaling &&
            result.Color == sample.Color &&
            result.HideOriginalMesh == sample.HideOriginalMesh &&
            result.MaterialReplacement == sample.MaterialReplacement,
            "Decoration eyedropper copies exact settings while retaining each target's anchor and positioning. " +
            sampleMessage + " " + targetMessage + " " + applyMessage);
    }

    private static void VerifyDecorationGroupPresetPayload()
    {
        Guid mesh = new Guid("12345678-1234-1234-1234-1234567890ab");
        Guid material = new Guid("abcdefab-cdef-cdef-cdef-abcdefabcdef");
        var payload = new EsuDecorationGroupPresetPayload
        {
            PrimaryIndex = 0,
            Decorations = new[]
            {
                new EsuDecorationPresetItem
                {
                    RelativeAnchor = new EsuPresetCell(0, 0, 0),
                    Positioning = new EsuPresetVector3(0.25f, 0f, 0f),
                    Scaling = new EsuPresetVector3(1f, 2f, 3f),
                    Orientation = new EsuPresetVector3(0f, 90f, 0f),
                    MeshGuid = mesh.ToString("D"),
                    Color = 7,
                    MaterialReplacement = material.ToString("D")
                },
                new EsuDecorationPresetItem
                {
                    RelativeAnchor = new EsuPresetCell(2, -1, 3),
                    Positioning = new EsuPresetVector3(-0.5f, 0.125f, 0f),
                    Scaling = new EsuPresetVector3(-1f, 1f, 1f),
                    Orientation = new EsuPresetVector3(10f, 20f, 30f),
                    MeshGuid = mesh.ToString("D"),
                    Color = 31,
                    HideOriginalMesh = true,
                    MaterialReplacement = Guid.Empty.ToString("D")
                }
            }
        };

        bool restored = payload.TryCreateSnapshots(
            new Vector3i(10, 20, 30),
            out DecorationEditSnapshot[] snapshots,
            out string restoreMessage);
        Assert(
            restored && snapshots.Length == 2 &&
            snapshots[0].TetherPoint.Equals(new Vector3i(10, 20, 30)) &&
            snapshots[1].TetherPoint.Equals(new Vector3i(12, 19, 33)) &&
            snapshots[1].HideOriginalMesh && snapshots[1].Scaling.x == -1f,
            "Decoration group presets preserve relative anchors and exact portable settings across constructs. " +
            restoreMessage);

        Assert(
            DecorationEditSnapshot.TryCreatePortable(
                new Vector3i(1, 2, 3),
                Vector3.zero,
                Vector3.one,
                Vector3.zero,
                mesh,
                4,
                false,
                material,
                out DecorationEditSnapshot portable,
                out _) &&
            portable.MeshGuid == mesh && portable.MaterialReplacement == material &&
            !DecorationEditSnapshot.TryCreatePortable(
                new Vector3i(),
                new Vector3(float.NaN, 0f, 0f),
                Vector3.one,
                Vector3.zero,
                mesh,
                4,
                false,
                material,
                out _,
                out _),
            "Portable decoration snapshots validate identity-free preset/array data before placement.");
    }

    private static void VerifyWorkspaceSessionIntegration()
    {
        string root = FindRepositoryRoot();
        string session = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.cs"));
        string workspaceLayout = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.Workspace.Layout.cs"));
        string workspaceBulk = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.Workspace.cs"));
        string history = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditHistory.cs"));
        string workspaceLayers = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationWorkspaceLayers.cs"));

        Assert(
            workspaceLayout.Contains("DrawWorkspacePrecisionSnapButton") &&
            workspaceLayout.Contains("TryHandleWorkspacePrecisionSnapInput") &&
            workspaceLayout.Contains("DecorationPrecisionSnapTools.TrySnapToSurface") &&
            workspaceLayout.Contains("DecorationPrecisionSnapTools.TrySnapToAnchor") &&
            workspaceLayout.Contains("DecorationPrecisionSnapTools.TrySnapToAxis") &&
            workspaceLayout.Contains("LocalNormalFromHit(hit)") &&
            workspaceLayout.Contains("WorkspaceAxisVector(_workspaceLayoutAxis)") &&
            workspaceLayout.Contains("ApplyWorkspaceLayoutPlan(decorations, plan, message)") &&
            session.Contains("if (TryHandleWorkspacePrecisionSnapInput())"),
            "Precision surface/anchor/axis snap buttons arm an owned viewport pick and apply the pure plan through undoable layout history.");

        Assert(
            workspaceLayout.Contains("Layers, folders, tags, visibility, and edit locks") &&
            workspaceLayout.Contains("Set folder") &&
            workspaceLayout.Contains("TrySetLayerFolder") &&
            workspaceLayout.Contains("Hide/show") &&
            workspaceLayout.Contains("Isolate") &&
            workspaceLayout.Contains("Layer lock") &&
            workspaceLayout.Contains("Set tags") &&
            workspaceLayout.Contains("targets.Any(WorkspaceDecorationLocked)") &&
            CountToken(session, "WorkspaceHasLockedSelection()") >= 5 &&
            session.Contains("DecorationLayerVisibilityBridge.RestoreAll()") &&
            session.Contains("RefreshWorkspaceLayerVisibility(force: false)") &&
            session.Contains("if (!WorkspaceDecorationVisible(decoration, construct))"),
            "Layer UI is wired to viewport visibility/isolate and persistent lock guards across transform, paste, duplicate, delete, and batch paths.");

        Assert(
            session.Contains("Enable smooth Bezier") &&
            session.Contains("ToggleGeneratorBezierPath") &&
            session.Contains("Enable smooth Bezier generator path") &&
            session.Contains("SurfaceDraftSourceRegistry.Register") &&
            session.Contains("persistent ESU surface source"),
            "Surface UI exposes editable smooth Bezier mode and persistent placed-source reopen wiring.");

        Assert(
            workspaceBulk.Contains("OutlinerRow[] matches = _outlinerRows") &&
            workspaceBulk.Contains("DecorationWorkspaceBulkTools.SearchMatches(row.SearchText, filter)") &&
            workspaceBulk.Contains("visible Outliner decoration result(s) across the craft") &&
            workspaceBulk.Contains("DecorationWorkspaceBulkTools.TryTransformMeshBounds") &&
            workspaceBulk.Contains("DecorationWorkspaceBulkTools.BoundsTouchOrNear") &&
            workspaceBulk.Contains("DecorationWorkspaceBulkTools.TryBuildEyedropperResult") &&
            workspaceLayout.Contains("WorkspaceConstructFor(decoration)"),
            "Workspace bulk UI consumes exact visible Outliner row semantics across constructs, grows by transformed mesh bounds, preserves cross-construct lock identity, and applies the pure exact eyedropper result atomically.");

        Assert(
            workspaceLayout.Contains("TryGetSingleConstructWorkspaceSelection") &&
            workspaceLayout.Contains("\"precision layout\"") &&
            workspaceLayout.Contains("\"radial array\" : \"linear array\"") &&
            workspaceLayout.Contains("\"live ruler\"") &&
            workspaceBulk.Contains("\"Grow selection\"") &&
            workspaceBulk.Contains("\"decoration group preset capture\"") &&
            workspaceLayout.Contains("WorkspaceSelectionSpansConstructs") &&
            workspaceBulk.Contains("TryResolveSingleFrame") &&
            workspaceLayout.Contains("WorkspaceBoundsFor(decoration, out _)") &&
            workspaceLayout.Contains("bounds.Center") &&
            workspaceLayout.Contains("bounds.Extents") &&
            workspaceLayout.Contains("DecorationPrecisionSnapTools.TryMeasure(") &&
            workspaceLayout.Contains("measurement.BoundsClearance") &&
            workspaceLayout.Contains("TryTranslateDecorationOriginForBoundsCenterMove") &&
            workspaceLayout.Contains("targetDecorationOrigin") &&
            !workspaceLayout.Contains("AbsVector(_selected.Scaling.Us) * 0.5f"),
            "Spatial layout, arrays, Grow, group presets, and ruler fail closed across construct frames; exact transformed mesh AABBs drive layout and measurement while field-only bulk selection remains cross-construct capable.");

        Assert(
            session.Contains("RejectCrossConstructSpatialTransform") &&
            session.Contains("WorkspaceSelectionSpansConstructs())") &&
            session.Contains("Inspector position") &&
            session.Contains("Inspector rotation") &&
            session.Contains("Inspector scale") &&
            history.Contains("AllConstruct[] _constructs") &&
            history.Contains("session.TryRestoreHistorySnapshots(") &&
            workspaceLayout.Contains("changedConstructs.Add(WorkspaceConstructFor(decoration))") &&
            session.Contains("resolvedConstructs[selectedIndex]") &&
            session.Contains("HistoryConstructOwnsDecoration") &&
            workspaceLayers.Contains("construct.PersistentSubConstructIndex") &&
            workspaceLayers.Contains("main.ForceIdWeWereSavedWith") &&
            workspaceLayers.Contains("main.UniqueId"),
            "Standard spatial transforms fail closed for cross-construct selections, history retains each target frame, and persistent object keys combine main-craft and real subconstruct identity.");

        int surfaceRestore = session.IndexOf(
            "internal bool TryRestoreSurfaceDraftHistory",
            StringComparison.Ordinal);
        int generatorRestore = session.IndexOf(
            "internal bool TryRestoreGeneratorDraftHistory",
            StringComparison.Ordinal);
        int styleRestore = session.IndexOf(
            "internal bool TryRestoreSurfaceBuilderStyleHistory",
            StringComparison.Ordinal);
        int restoreEnd = session.IndexOf(
            "private void SyncSurfaceTextFromSettings",
            StringComparison.Ordinal);
        Assert(
            surfaceRestore >= 0 && generatorRestore > surfaceRestore &&
            styleRestore > generatorRestore && restoreEnd > styleRestore &&
            session.Substring(surfaceRestore, generatorRestore - surfaceRestore)
                .Contains("SaveDraftRecovery(SurfaceDraftActionTarget.Surface, notify: false)") &&
            session.Substring(generatorRestore, styleRestore - generatorRestore)
                .Contains("SaveDraftRecovery(SurfaceDraftActionTarget.Generator, notify: false)") &&
            session.Substring(styleRestore, restoreEnd - styleRestore)
                .Contains("SaveDraftRecovery(SurfaceDraftActionTarget.Surface, notify: false)") &&
            session.Substring(styleRestore, restoreEnd - styleRestore)
                .Contains("SaveDraftRecovery(SurfaceDraftActionTarget.Generator, notify: false)"),
            "Surface and generator undo/redo restores immediately refresh their crash-recovery slots.");
    }

    private static int CountToken(string source, string token)
    {
        int count = 0;
        int offset = 0;
        while (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(token) &&
               (offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }
}
