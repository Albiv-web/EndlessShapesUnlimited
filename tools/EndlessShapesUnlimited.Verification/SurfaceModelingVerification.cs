using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.Presets;
using EndlessShapes2;
using Newtonsoft.Json;
using UnityEngine;

public static class SurfaceModelingVerification
{
    public static string Run()
    {
        VerifyFaceExtrusionAndSnapshots();
        VerifyBoundaryEdgeExtrusion();
        VerifyFaceInset();
        VerifyConformingSubdivision();
        VerifySelectionSmoothing();
        VerifyPointWeldAndMerge();
        VerifyBoundaryFill();
        VerifyFaceNormalFlip();
        VerifySmoothBezierGeneratorPath();
        VerifyPersistentSurfaceSourceStore();
        return "Surface modeling verification passed.";
    }

    private static void VerifyFaceExtrusionAndSnapshots()
    {
        SurfaceDraft draft = Triangle();
        draft.TrySetFaceColor(0, 7, out string message);
        SurfaceDraftSnapshot before = draft.CreateSnapshot();
        Assert(!draft.TryExtrudeSelectedFace(0f, out message) && before.SameAs(draft.CreateSnapshot()),
            "zero-distance face extrusion must be atomic");

        Assert(draft.TryExtrudeSelectedFace(1f, out message), message);
        Assert(draft.Points.Count == 6 && draft.Faces.Count == 7,
            "face extrusion should create a cap and six side triangles");
        Assert(draft.SelectionKind == SurfaceSelectionKind.Face && draft.SelectedFace == 0,
            "face extrusion should select its cap");
        Assert(draft.FaceStyles.All(style => style.ColorIndex == 7),
            "face extrusion should preserve face style");

        SurfaceDraftSnapshot after = draft.CreateSnapshot();
        draft.Restore(before);
        Assert(before.SameAs(draft.CreateSnapshot()),
            "face extrusion must restore through the existing snapshot path");
        draft.Restore(after);
        Assert(after.SameAs(draft.CreateSnapshot()),
            "face extrusion redo snapshot must restore exactly");
    }

    private static void VerifyBoundaryEdgeExtrusion()
    {
        SurfaceDraft draft = Triangle();
        draft.SelectEdge(0, 1);
        Assert(draft.TryExtrudeSelectedEdge(1f, out string message), message);
        Assert(draft.Points.Count == 5 && draft.Faces.Count == 3,
            "boundary edge extrusion should add two points and two faces");
        Assert(draft.SelectionKind == SurfaceSelectionKind.Edge && draft.SelectedEdge.IsValid,
            "boundary edge extrusion should select the new edge");

        SurfaceDraft joined = Quad();
        joined.SelectEdge(1, 2);
        SurfaceDraftSnapshot baseline = joined.CreateSnapshot();
        Assert(!joined.TryExtrudeSelectedEdge(1f, out message) && baseline.SameAs(joined.CreateSnapshot()),
            "an internal edge extrusion must fail without partial mutation");
    }

    private static void VerifyFaceInset()
    {
        SurfaceDraft draft = Triangle();
        SurfaceDraftSnapshot baseline = draft.CreateSnapshot();
        Assert(!draft.TryInsetSelectedFace(1f, out string message) && baseline.SameAs(draft.CreateSnapshot()),
            "invalid inset fraction must be atomic");
        Assert(draft.TryInsetSelectedFace(0.25f, out message), message);
        Assert(draft.Points.Count == 6 && draft.Faces.Count == 7,
            "triangular inset should create an inner face and six ring faces");
        Assert(draft.SelectedFace == 0, "inset should select the inner face");
    }

    private static void VerifyConformingSubdivision()
    {
        SurfaceDraft single = Triangle();
        Assert(single.TrySubdivideSelectedFace(out string message), message);
        Assert(single.Points.Count == 6 && single.Faces.Count == 4,
            "one triangle should subdivide into four triangles");

        SurfaceDraft joined = Quad();
        joined.SelectFace(0);
        Assert(joined.TrySubdivideSelectedFace(out message), message);
        Assert(joined.Points.Count == 7 && joined.Faces.Count == 6,
            "subdivision should split the face across a shared edge too");
        int sharedMidpoint = 5;
        Assert(joined.Faces.Count(face => face.Contains(sharedMidpoint)) >= 4,
            "the neighboring face should reference the shared-edge midpoint");
    }

    private static void VerifySelectionSmoothing()
    {
        SurfaceDraft draft = Triangle();
        draft.SelectPoint(0);
        SurfaceDraftSnapshot baseline = draft.CreateSnapshot();
        Assert(!draft.TrySmoothSelectedPoints(0f, out string message) &&
               baseline.SameAs(draft.CreateSnapshot()),
            "invalid smoothing strength must be atomic");
        Vector3 before = draft.Points[0];
        Assert(draft.TrySmoothSelectedPoints(0.25f, out message), message);
        Assert(draft.Points[0] != before,
            "selected point smoothing should move the point toward its neighbors");
        Assert(draft.SelectionKind == SurfaceSelectionKind.Point && draft.SelectedPoint == 0,
            "smoothing should retain the primary selection");
    }

    private static void VerifyPointWeldAndMerge()
    {
        SurfaceDraft draft = TwoDetachedTriangles();
        SurfaceDraftSnapshot baseline = draft.CreateSnapshot();
        Assert(!draft.TryWeldPoints(new[] { 0, 99 }, Vector3.zero, out string message) &&
               baseline.SameAs(draft.CreateSnapshot()),
            "invalid point weld must be atomic");

        Assert(draft.ToggleManualFacePoint(0, out message), message);
        Assert(draft.ToggleManualFacePoint(3, out message), message);
        Assert(draft.TryWeldSelectedPoints(out message), message);
        Assert(draft.Points.Count == 5 && draft.Faces.Count == 2,
            "welding detached vertices should merge one point without losing valid faces");
        Assert(draft.SelectionKind == SurfaceSelectionKind.Point,
            "weld should select its surviving point");

        SurfaceDraft collapsed = Triangle();
        Assert(collapsed.TryMergePointsTo(new[] { 0, 1 }, 0, out message), message);
        Assert(collapsed.Points.Count == 2 && collapsed.Faces.Count == 0,
            "merging an edge should remove its collapsed triangle");
    }

    private static void VerifyBoundaryFill()
    {
        var draft = new SurfaceDraft();
        draft.AddPointForTests(new Vector3(0f, 0f, 0f));
        draft.AddPointForTests(new Vector3(1f, 0f, 0f));
        draft.AddPointForTests(new Vector3(0f, 1f, 0f));
        draft.AddPointForTests(new Vector3(0f, 0f, 1f));
        Assert(draft.TryAddFace(0, 1, 3, out string message), message);
        Assert(draft.TryAddFace(1, 2, 3, out message), message);
        Assert(draft.TryAddFace(2, 0, 3, out message), message);
        draft.SelectEdge(0, 1);
        Assert(draft.TryFillSelectedBoundary(out message), message);
        Assert(draft.Faces.Count == 4 && draft.SelectionKind == SurfaceSelectionKind.Face,
            "the missing tetrahedron face should fill from one selected boundary edge");

        SurfaceDraft open = Triangle();
        open.SelectEdge(0, 1);
        SurfaceDraftSnapshot baseline = open.CreateSnapshot();
        Assert(!open.TryFillSelectedBoundary(out message) && baseline.SameAs(open.CreateSnapshot()),
            "filling an already-covered triangular outer boundary must reject duplicate geometry atomically");
    }

    private static void VerifyFaceNormalFlip()
    {
        SurfaceDraft draft = Triangle();
        SurfaceDraftSnapshot before = draft.CreateSnapshot();
        SurfaceFace original = draft.Faces[0];
        Assert(draft.TryFlipSelectedFaceNormal(out string message), message);
        Assert(draft.Faces[0].A == original.A &&
               draft.Faces[0].B == original.C &&
               draft.Faces[0].C == original.B,
            "face normal flip should reverse winding");
        Assert(draft.TryFlipSelectedFaceNormal(out message), message);
        Assert(before.SameAs(draft.CreateSnapshot()),
            "flipping twice should restore the exact draft snapshot");
    }

    private static void VerifySmoothBezierGeneratorPath()
    {
        var construct = (AllConstruct)FormatterServices.GetUninitializedObject(
            typeof(TelescopicPistonSubConstructable));
        var draft = new DecorationGeneratorDraft();
        draft.SetTool(SurfaceExtraTool.Path);
        Assert(
            draft.TryAddPathPoint(construct, new Vector3(0f, 0f, 0f), out string message) &&
            draft.TryAddPathPoint(construct, new Vector3(1f, 1f, 0f), out message) &&
            draft.TryAddPathPoint(construct, new Vector3(2f, 0f, 0f), out message),
            message);

        DecorationGeneratorDraftSnapshot linear = draft.CreateSnapshot();
        Assert(
            !draft.TrySetSmoothBezierPath(true, 1, 1f, out message) &&
            linear.SameAs(draft.CreateSnapshot()),
            "invalid Bezier settings must leave the draft byte-for-byte unchanged");
        Assert(
            draft.TrySetSmoothBezierPath(
                true,
                DecorationGeneratorDraft.DefaultBezierSubdivisions,
                DecorationGeneratorDraft.DefaultBezierTension,
                out message),
            message);
        Assert(
            draft.TryGetEffectivePathPoints(out var sampled, out message) &&
            sampled.Count == 17 && sampled[0] == draft.PathPoints[0] &&
            sampled[sampled.Count - 1] == draft.PathPoints[draft.PathPoints.Count - 1] &&
            sampled[4].y > 0f && sampled[12].y > 0f,
            "automatic cubic Bezier sampling must retain endpoints and curve through the editable knots");

        Assert(
            SurfaceBezierPath.TrySampleAutomatic(
                draft.PathPoints,
                SurfaceBezierPath.MaximumSubdivisions,
                DecorationGeneratorDraft.DefaultBezierTension,
                out IReadOnlyList<Vector3> dense,
                out message),
            message);
        Vector3 incoming = dense[64] - dense[63];
        Vector3 outgoing = dense[65] - dense[64];
        float continuity = Dot(incoming, outgoing) /
                           ((float)Math.Sqrt(Dot(incoming, incoming)) *
                            (float)Math.Sqrt(Dot(outgoing, outgoing)));
        Assert(continuity > 0.995f,
            "dense smooth Bezier samples must converge on a continuous tangent at interior knots");

        var settings = new DecorationGeneratorSettings();
        Assert(
            DecorationGeneratorPlanner.TryBuildPreviewSegments(
                draft,
                settings,
                out var preview,
                out message) && preview.Count == 16,
            "Path preview must consume the smooth Bezier samples instead of the linear knot polyline. " + message);

        DecorationGeneratorDraftSnapshot smooth = draft.CreateSnapshot();
        var restored = new DecorationGeneratorDraft();
        restored.Restore(smooth);
        Assert(
            restored.SmoothBezierPath && smooth.SameAs(restored.CreateSnapshot()),
            "undo/redo snapshots must preserve smooth Bezier mode, settings, knots, and selection");

        EsuGeneratorDraftPresetPayload payload =
            EsuGeneratorDraftPresetPayload.Capture(draft, settings, preserveSelection: true);
        string json = JsonConvert.SerializeObject(payload, Formatting.None);
        EsuGeneratorDraftPresetPayload loaded =
            JsonConvert.DeserializeObject<EsuGeneratorDraftPresetPayload>(json);
        DecorationGeneratorEditSnapshot portable = null;
        Assert(
            loaded != null && loaded.SmoothBezierPath &&
            loaded.TryCreateSnapshot(
                construct,
                new Vector3(10f, 20f, 30f),
                out portable,
                out message),
            "generator preset/recovery JSON must persist smooth Bezier semantics. " + message);
        var portableDraft = new DecorationGeneratorDraft();
        portableDraft.Restore(portable.Draft);
        Assert(
            portableDraft.SmoothBezierPath &&
            portableDraft.PathPoints.Count == 3 &&
            portableDraft.PathPoints[0] == new Vector3(10f, 20f, 30f) &&
            portableDraft.PathPoints[1] == new Vector3(11f, 21f, 30f),
            "portable smooth Bezier presets must retain editable knots while translating their reference");

        draft.SelectPoint(1);
        Assert(
            draft.TrySetSelectedPointCoordinate(new Vector3(1f, 2f, 0f), out message) &&
            draft.SmoothBezierPath &&
            draft.TryGetEffectivePathPoints(out var edited, out message) &&
            edited[4].y > sampled[4].y,
            "moving a reopened Bezier knot must rebuild the curve without flattening it to sampled points");
    }

    private static void VerifyPersistentSurfaceSourceStore()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "esu-surface-source-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var construct = (AllConstruct)FormatterServices.GetUninitializedObject(
                typeof(TelescopicPistonSubConstructable));
            SurfaceDraft draft = Triangle();
            draft.SetConstructForTests(construct);
            draft.TrySetFaceColor(0, 9, out string message);
            EsuSurfaceDraftPresetPayload payload =
                EsuSurfaceDraftPresetPayload.Capture(draft, preserveSelection: true);
            var store = new SurfaceDraftSourceStore(root);
            Assert(
                store.TryRegister(
                    new[] { "craft|uid:101", "craft|uid:102" },
                    payload,
                    out message),
                message);

            var reloaded = new SurfaceDraftSourceStore(root);
            SurfaceDraftSnapshot snapshot = null;
            Assert(
                reloaded.TryGet("craft|uid:102", out EsuSurfaceDraftPresetPayload loaded, out message) &&
                loaded.TryCreateSnapshot(
                    construct,
                    loaded.Reference.ToVector3(),
                    out snapshot,
                    out message),
                "persistent surface source must reconstruct after a fresh store instance. " + message);
            var reopened = new SurfaceDraft();
            reopened.Restore(snapshot);
            Assert(
                reopened.Points.Count == 3 && reopened.Faces.Count == 1 &&
                reopened.FaceStyles[0].ColorIndex == 9 &&
                draft.CreateSnapshot().SameAs(reopened.CreateSnapshot()),
                "placed-source roundtrip must preserve topology, styles, selection, settings, and construct-local coordinates");

            SurfaceDraftSourceDocument document =
                JsonConvert.DeserializeObject<SurfaceDraftSourceDocument>(
                    File.ReadAllText(store.FilePath));
            Assert(
                document != null && document.DecorationSources.Count == 2 &&
                document.Sources.Count == 1,
                "one placed source payload must be shared by every generated decoration association");

            File.WriteAllText(store.FilePath, "{corrupt");
            var fallback = new SurfaceDraftSourceStore(root);
            Assert(
                fallback.TryGet("craft|uid:101", out EsuSurfaceDraftPresetPayload _, out message),
                "placed-source store must recover from its transactional backup. " + message);
            Assert(
                Directory.GetFiles(store.DirectoryPath, "*.pending-*", SearchOption.TopDirectoryOnly).Length == 0,
                "placed-source transactions must not leave completed pending files");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static SurfaceDraft Triangle()
    {
        var draft = new SurfaceDraft();
        draft.AddPointForTests(Vector3.zero);
        draft.AddPointForTests(Vector3.right);
        draft.AddPointForTests(Vector3.up);
        Assert(draft.TryAddFace(0, 1, 2, out string message), message);
        return draft;
    }

    private static SurfaceDraft Quad()
    {
        var draft = new SurfaceDraft();
        draft.AddPointForTests(new Vector3(0f, 0f, 0f));
        draft.AddPointForTests(new Vector3(1f, 0f, 0f));
        draft.AddPointForTests(new Vector3(0f, 1f, 0f));
        draft.AddPointForTests(new Vector3(1f, 1f, 0f));
        Assert(draft.TryAddFace(0, 1, 2, out string message), message);
        Assert(draft.TryAddFace(1, 3, 2, out message), message);
        return draft;
    }

    private static SurfaceDraft TwoDetachedTriangles()
    {
        var draft = new SurfaceDraft();
        draft.AddPointForTests(new Vector3(0f, 0f, 0f));
        draft.AddPointForTests(new Vector3(1f, 0f, 0f));
        draft.AddPointForTests(new Vector3(0f, 1f, 0f));
        draft.AddPointForTests(new Vector3(2f, 2f, 0f));
        draft.AddPointForTests(new Vector3(3f, 2f, 0f));
        draft.AddPointForTests(new Vector3(2f, 3f, 0f));
        Assert(draft.TryAddFace(0, 1, 2, out string message), message);
        Assert(draft.TryAddFace(3, 4, 5, out message), message);
        return draft;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Surface modeling verification failed: " + message);
    }

    private static float Dot(Vector3 left, Vector3 right) =>
        left.x * right.x + left.y * right.y + left.z * right.z;
}
