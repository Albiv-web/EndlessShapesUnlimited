using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using EndlessShapes2.Polygon;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum SurfaceSelectionKind
    {
        None,
        Point,
        Edge,
        Face
    }

    internal struct SurfaceFace
    {
        internal SurfaceFace(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        internal int A { get; }

        internal int B { get; }

        internal int C { get; }

        internal bool Contains(int index) => A == index || B == index || C == index;

        internal bool ContainsEdge(int a, int b) =>
            (A == a && B == b) ||
            (B == a && C == b) ||
            (C == a && A == b) ||
            (A == b && B == a) ||
            (B == b && C == a) ||
            (C == b && A == a);

        internal bool HasDirectedEdge(int a, int b) =>
            (A == a && B == b) ||
            (B == a && C == b) ||
            (C == a && A == b);

        internal SurfaceFace Flipped() =>
            new SurfaceFace(A, C, B);

        internal IEnumerable<SurfaceEdge> Edges()
        {
            yield return new SurfaceEdge(A, B);
            yield return new SurfaceEdge(B, C);
            yield return new SurfaceEdge(C, A);
        }

        internal IEnumerable<int> Points()
        {
            yield return A;
            yield return B;
            yield return C;
        }
    }

    internal struct SurfaceEdge
    {
        internal SurfaceEdge(int a, int b)
        {
            A = a;
            B = b;
        }

        internal int A { get; }

        internal int B { get; }

        internal bool IsValid => A >= 0 && B >= 0 && A != B;

        internal bool Matches(int a, int b) =>
            (A == a && B == b) || (A == b && B == a);
    }

    internal sealed class SurfaceDecorationSettings
    {
        internal SurfaceDecorationSettings()
        {
            StructureBlockType = StructureBlockType.Metal;
            FaceThickness = 0.05f;
            ColorIndex = 0;
            NearestAnchor = true;
        }

        internal StructureBlockType StructureBlockType { get; set; }

        internal float FaceThickness { get; set; }

        internal int ColorIndex { get; set; }

        internal bool NormalReversal { get; set; }

        internal bool NearestAnchor { get; set; }

        internal bool IsValid(out string reason)
        {
            reason = null;
            if (!DecorationEditMath.IsFinite(FaceThickness) || FaceThickness <= 0f)
            {
                reason = "Surface thickness must be finite and greater than zero.";
                return false;
            }

            if ((int)StructureBlockType < (int)StructureBlockType.Glass ||
                (int)StructureBlockType > (int)StructureBlockType.Rubber)
            {
                reason = "Surface material is outside the supported structure material range.";
                return false;
            }

            if (ColorIndex < 0 || ColorIndex > 31)
            {
                reason = "Surface color must be 0 through 31.";
                return false;
            }

            return true;
        }
    }

    internal sealed class SurfaceDraft
    {
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<SurfaceFace> _faces = new List<SurfaceFace>();
        private readonly List<int> _manualFaceSelection = new List<int>(3);

        internal AllConstruct Construct { get; private set; }

        internal IReadOnlyList<Vector3> Points => _points;

        internal IReadOnlyList<SurfaceFace> Faces => _faces;

        internal SurfaceDecorationSettings Settings { get; } = new SurfaceDecorationSettings();

        internal SurfaceSelectionKind SelectionKind { get; private set; }

        internal int SelectedPoint { get; private set; } = -1;

        internal int SelectedFace { get; private set; } = -1;

        internal SurfaceEdge SelectedEdge { get; private set; } = new SurfaceEdge(-1, -1);

        internal IReadOnlyList<int> ManualFaceSelection => _manualFaceSelection;

        internal bool HasDraft => _points.Count > 0 || _faces.Count > 0;

        internal bool HasPlaceableFaces => _faces.Count > 0;

        internal void SetConstructForTests(AllConstruct construct) =>
            Construct = construct;

        internal void AddPointForTests(Vector3 local) =>
            _points.Add(local);

        internal SurfaceDraft CreateMirroredForSymmetry(
            DecoLimitLifter.EsuSymmetry.SymmetryVariant variant)
        {
            var mirrored = new SurfaceDraft();
            mirrored.Construct = Construct;
            CopySettings(Settings, mirrored.Settings);
            for (int index = 0; index < _points.Count; index++)
                mirrored._points.Add(variant.Mirror(_points[index]));

            bool flipWinding = variant.AxisCount % 2 == 1;
            for (int index = 0; index < _faces.Count; index++)
            {
                SurfaceFace face = _faces[index];
                mirrored._faces.Add(flipWinding ? face.Flipped() : face);
            }

            return mirrored;
        }

        internal void Clear()
        {
            Construct = null;
            _points.Clear();
            _faces.Clear();
            ClearSelection();
        }

        internal void ClearSelection()
        {
            SelectionKind = SurfaceSelectionKind.None;
            SelectedPoint = -1;
            SelectedFace = -1;
            SelectedEdge = new SurfaceEdge(-1, -1);
            _manualFaceSelection.Clear();
        }

        internal bool TryAddPoint(
            AllConstruct construct,
            Vector3 local,
            bool extendSelectedEdge,
            out string message)
        {
            message = null;
            if (!TryAcceptConstruct(construct, out message))
                return false;
            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Surface point must be finite.";
                return false;
            }

            local = DecorationEditMath.Snap(local);
            int index = _points.Count;
            _points.Add(local);
            SelectionKind = SurfaceSelectionKind.Point;
            SelectedPoint = index;
            SelectedFace = -1;
            if (extendSelectedEdge && SelectedEdge.IsValid && index >= 3)
            {
                SurfaceEdge edge = SelectedEdge;
                if (!TryAddFace(edge.A, edge.B, index, out message))
                {
                    _points.RemoveAt(_points.Count - 1);
                    ClearSelection();
                    return false;
                }

                SelectionKind = SurfaceSelectionKind.Edge;
                SelectedEdge = new SurfaceEdge(edge.B, index);
                SelectedPoint = index;
                message = "Surface edge extended.";
                return true;
            }

            if (_points.Count == 3)
            {
                if (!TryAddFace(0, 1, 2, out message))
                    return false;
                message = "Surface base triangle created.";
            }
            else
            {
                message = _points.Count < 3
                    ? "Surface point " + _points.Count.ToString(CultureInfo.InvariantCulture) + "/3 placed."
                    : "Surface point placed. Select an edge before clicking to extend a face.";
            }

            return true;
        }

        internal bool TryMovePoint(int index, Vector3 local, out string message)
        {
            message = null;
            if (index < 0 || index >= _points.Count)
            {
                message = "Select a surface point before moving it.";
                return false;
            }

            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Surface point move rejected because the position is not finite.";
                return false;
            }

            _points[index] = DecorationEditMath.Snap(local);
            return true;
        }

        internal void SelectPoint(int index)
        {
            if (index < 0 || index >= _points.Count)
                return;
            SelectionKind = SurfaceSelectionKind.Point;
            SelectedPoint = index;
            SelectedFace = -1;
            SelectedEdge = new SurfaceEdge(-1, -1);
        }

        internal void SelectEdge(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _points.Count || b >= _points.Count || a == b)
                return;
            SelectionKind = SurfaceSelectionKind.Edge;
            SelectedPoint = -1;
            SelectedFace = -1;
            SelectedEdge = new SurfaceEdge(a, b);
            _manualFaceSelection.Clear();
        }

        internal void SelectFace(int index)
        {
            if (index < 0 || index >= _faces.Count)
                return;
            SelectionKind = SurfaceSelectionKind.Face;
            SelectedPoint = -1;
            SelectedFace = index;
            SelectedEdge = new SurfaceEdge(-1, -1);
            _manualFaceSelection.Clear();
        }

        internal bool ToggleManualFacePoint(int index, out string message)
        {
            message = null;
            if (index < 0 || index >= _points.Count)
            {
                message = "Point is not part of the current surface draft.";
                return false;
            }

            if (_manualFaceSelection.Contains(index))
                _manualFaceSelection.Remove(index);
            else
                _manualFaceSelection.Add(index);

            SelectPoint(index);
            if (_manualFaceSelection.Count < 3)
            {
                message = "Selected " + _manualFaceSelection.Count.ToString(CultureInfo.InvariantCulture) +
                          "/3 points for a face.";
                return true;
            }

            int a = _manualFaceSelection[0];
            int b = _manualFaceSelection[1];
            int c = _manualFaceSelection[2];
            _manualFaceSelection.Clear();
            bool created = TryAddFace(a, b, c, out message);
            if (created)
                message = "Surface face created from selected points.";
            return created;
        }

        internal bool TryDeleteSelection(out string message)
        {
            message = null;
            if (SelectionKind == SurfaceSelectionKind.Face && SelectedFace >= 0 && SelectedFace < _faces.Count)
            {
                _faces.RemoveAt(SelectedFace);
                ClearSelection();
                message = "Surface face deleted.";
                return true;
            }

            if (SelectionKind == SurfaceSelectionKind.Point && SelectedPoint >= 0 && SelectedPoint < _points.Count)
            {
                int removed = SelectedPoint;
                _faces.RemoveAll(face => face.Contains(removed));
                for (int index = 0; index < _faces.Count; index++)
                {
                    SurfaceFace face = _faces[index];
                    _faces[index] = new SurfaceFace(
                        RemapAfterPointDelete(face.A, removed),
                        RemapAfterPointDelete(face.B, removed),
                        RemapAfterPointDelete(face.C, removed));
                }

                _points.RemoveAt(removed);
                ClearSelection();
                if (_points.Count == 0)
                    Construct = null;
                message = "Surface point and attached faces deleted.";
                return true;
            }

            message = "Select a surface point or face to delete.";
            return false;
        }

        internal bool TryAddFace(int a, int b, int c, out string message)
        {
            message = null;
            if (!ArePointIndexesValid(a, b, c))
            {
                message = "Surface face needs three different existing points.";
                return false;
            }

            if (_faces.Any(face => SameFace(face, a, b, c)))
            {
                message = "Surface face already exists.";
                return false;
            }

            if (!TryOrientFace(new SurfaceFace(a, b, c), out SurfaceFace face, out message))
                return false;

            _faces.Add(face);
            SelectFace(_faces.Count - 1);
            return true;
        }

        private bool TryOrientFace(SurfaceFace candidate, out SurfaceFace oriented, out string message)
        {
            oriented = candidate;
            message = null;
            if (_faces.Count == 0)
                return true;

            int sameDirectionEdges = 0;
            int oppositeDirectionEdges = 0;
            foreach (SurfaceEdge edge in candidate.Edges())
            {
                foreach (SurfaceFace existing in _faces)
                {
                    if (!existing.ContainsEdge(edge.A, edge.B))
                        continue;

                    if (existing.HasDirectedEdge(edge.A, edge.B))
                        sameDirectionEdges++;
                    else
                        oppositeDirectionEdges++;
                }
            }

            if (sameDirectionEdges > 0 && oppositeDirectionEdges > 0)
            {
                message = "Surface face conflicts with the winding of connected faces.";
                return false;
            }

            if (sameDirectionEdges > 0)
            {
                oriented = candidate.Flipped();
                return true;
            }

            if (oppositeDirectionEdges > 0)
                return true;

            if (TryGetReferenceNormal(out Vector3 referenceNormal) &&
                TryGetFaceNormal(candidate, out Vector3 candidateNormal) &&
                Vector3.Dot(referenceNormal, candidateNormal) < 0f)
            {
                oriented = candidate.Flipped();
            }

            return true;
        }

        private bool TryGetReferenceNormal(out Vector3 normal)
        {
            normal = Vector3.zero;
            foreach (SurfaceFace face in _faces)
            {
                if (TryGetFaceNormal(face, out Vector3 faceNormal))
                    normal += faceNormal;
            }

            if (DecorationEditMath.IsFinite(normal) && normal.sqrMagnitude > 0.000000000001f)
            {
                normal.Normalize();
                return true;
            }

            foreach (SurfaceFace face in _faces)
            {
                if (TryGetFaceNormal(face, out normal))
                {
                    normal.Normalize();
                    return true;
                }
            }

            normal = Vector3.zero;
            return false;
        }

        private bool TryGetFaceNormal(SurfaceFace face, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (face.A < 0 || face.B < 0 || face.C < 0 ||
                face.A >= _points.Count || face.B >= _points.Count || face.C >= _points.Count)
            {
                return false;
            }

            Vector3 ab = _points[face.B] - _points[face.A];
            Vector3 ac = _points[face.C] - _points[face.A];
            normal = Vector3.Cross(ab, ac);
            return DecorationEditMath.IsFinite(normal) && normal.sqrMagnitude > 0.000000000001f;
        }

        private bool TryAcceptConstruct(AllConstruct construct, out string message)
        {
            message = null;
            if (construct == null)
            {
                message = "Point at a real craft block before placing a surface point.";
                return false;
            }

            if (Construct == null)
            {
                Construct = construct;
                return true;
            }

            if (!ReferenceEquals(Construct, construct))
            {
                message = "Surface drafts are scoped to one construct. Clear the draft to start on another construct.";
                return false;
            }

            return true;
        }

        private bool ArePointIndexesValid(int a, int b, int c) =>
            a >= 0 && b >= 0 && c >= 0 &&
            a < _points.Count && b < _points.Count && c < _points.Count &&
            a != b && b != c && a != c;

        private static bool SameFace(SurfaceFace face, int a, int b, int c) =>
            face.Contains(a) && face.Contains(b) && face.Contains(c);

        private static int RemapAfterPointDelete(int index, int removed) =>
            index > removed ? index - 1 : index;

        private static void CopySettings(
            SurfaceDecorationSettings source,
            SurfaceDecorationSettings destination)
        {
            destination.StructureBlockType = source.StructureBlockType;
            destination.FaceThickness = source.FaceThickness;
            destination.ColorIndex = source.ColorIndex;
            destination.NormalReversal = source.NormalReversal;
            destination.NearestAnchor = source.NearestAnchor;
        }
    }

    internal sealed class SurfaceDecorationPlan
    {
        internal SurfaceDecorationPlan(
            AllConstruct construct,
            IReadOnlyList<SurfaceDecorationPlacement> placements,
            IReadOnlyList<string> warnings)
        {
            Construct = construct;
            Placements = placements ?? Array.Empty<SurfaceDecorationPlacement>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        internal AllConstruct Construct { get; }

        internal IReadOnlyList<SurfaceDecorationPlacement> Placements { get; }

        internal IReadOnlyList<string> Warnings { get; }

        internal int DecorationCount => Placements.Count;
    }

    internal sealed class SurfaceDecorationPlacement
    {
        internal SurfaceDecorationPlacement(
            Vector3i anchor,
            Guid meshGuid,
            Vector3 positioning,
            Vector3 scaling,
            Vector3 orientation,
            int color,
            Vector3 thicknessAxis,
            Vector3 transformThicknessAxis,
            float transformPlaneDistance)
        {
            Anchor = anchor;
            MeshGuid = meshGuid;
            Positioning = positioning;
            Scaling = scaling;
            Orientation = orientation;
            Color = color;
            ThicknessAxis = thicknessAxis;
            TransformThicknessAxis = transformThicknessAxis;
            TransformPlaneDistance = transformPlaneDistance;
        }

        internal Vector3i Anchor { get; }

        internal Guid MeshGuid { get; }

        internal Vector3 Positioning { get; }

        internal Vector3 Scaling { get; }

        internal Vector3 Orientation { get; }

        internal int Color { get; }

        internal Vector3 ThicknessAxis { get; }

        internal Vector3 TransformThicknessAxis { get; }

        internal float TransformPlaneDistance { get; }
    }

    internal abstract class ISurfaceAnchorResolver
    {
        internal abstract bool TryResolveAnchor(Vector3 localCenter, out Vector3i anchor);
    }

    internal sealed class ConstructSurfaceAnchorResolver : ISurfaceAnchorResolver
    {
        private readonly AllConstruct _construct;
        private readonly int _radius;

        internal ConstructSurfaceAnchorResolver(AllConstruct construct, int radius = 10)
        {
            _construct = construct;
            _radius = Mathf.Clamp(radius, 1, 10);
        }

        internal override bool TryResolveAnchor(Vector3 localCenter, out Vector3i anchor)
        {
            anchor = default;
            if (_construct == null || !DecorationEditMath.IsFinite(localCenter))
                return false;

            Vector3i rounded = new Vector3i(
                Mathf.RoundToInt(localCenter.x),
                Mathf.RoundToInt(localCenter.y),
                Mathf.RoundToInt(localCenter.z));
            float bestDistance = float.MaxValue;
            Vector3i best = default;
            for (int radius = 0; radius <= _radius; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                        for (int z = -radius; z <= radius; z++)
                        {
                            if (Math.Abs(x) != radius &&
                                Math.Abs(y) != radius &&
                                Math.Abs(z) != radius)
                            {
                                continue;
                            }

                            var candidate = new Vector3i(rounded.x + x, rounded.y + y, rounded.z + z);
                            if (!HasBlock(candidate))
                                continue;

                            Vector3 positioning = localCenter - ToVector3(candidate);
                            if (!DecorationEditMath.IsWithinPositionLimit(positioning))
                                continue;

                            float distance = (localCenter - ToVector3(candidate)).sqrMagnitude;
                            if (distance >= bestDistance)
                                continue;

                            bestDistance = distance;
                            best = candidate;
                        }

                if (bestDistance < float.MaxValue)
                {
                    anchor = best;
                    return true;
                }
            }

            return false;
        }

        private bool HasBlock(Vector3i position)
        {
            try
            {
                return _construct?.AllBasics?.GetBlockViaLocalPosition(position) != null;
            }
            catch
            {
                return false;
            }
        }

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);
    }

    internal sealed class SetSurfaceAnchorResolver : ISurfaceAnchorResolver
    {
        private readonly HashSet<string> _blocks;
        private readonly int _radius;

        internal SetSurfaceAnchorResolver(IEnumerable<Vector3i> blocks, int radius = 10)
        {
            _blocks = new HashSet<string>((blocks ?? Enumerable.Empty<Vector3i>()).Select(Key));
            _radius = Mathf.Clamp(radius, 1, 10);
        }

        internal override bool TryResolveAnchor(Vector3 localCenter, out Vector3i anchor)
        {
            anchor = default;
            if (!DecorationEditMath.IsFinite(localCenter))
                return false;

            Vector3i rounded = new Vector3i(
                Mathf.RoundToInt(localCenter.x),
                Mathf.RoundToInt(localCenter.y),
                Mathf.RoundToInt(localCenter.z));
            float bestDistance = float.MaxValue;
            Vector3i best = default;
            for (int radius = 0; radius <= _radius; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                        for (int z = -radius; z <= radius; z++)
                        {
                            if (Math.Abs(x) != radius &&
                                Math.Abs(y) != radius &&
                                Math.Abs(z) != radius)
                            {
                                continue;
                            }

                            var candidate = new Vector3i(rounded.x + x, rounded.y + y, rounded.z + z);
                            if (!_blocks.Contains(Key(candidate)))
                                continue;

                            Vector3 positioning = localCenter - ToVector3(candidate);
                            if (!DecorationEditMath.IsWithinPositionLimit(positioning))
                                continue;

                            float distance = (localCenter - ToVector3(candidate)).sqrMagnitude;
                            if (distance >= bestDistance)
                                continue;

                            bestDistance = distance;
                            best = candidate;
                        }

                if (bestDistance < float.MaxValue)
                {
                    anchor = best;
                    return true;
                }
            }

            return false;
        }

        private static string Key(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + ":" +
            value.y.ToString(CultureInfo.InvariantCulture) + ":" +
            value.z.ToString(CultureInfo.InvariantCulture);

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);
    }

    internal static class SurfaceDecorationPlanner
    {
        private const float SurfaceGeometryEpsilon = 0.000001f;

        internal static bool TryPlan(
            SurfaceDraft draft,
            ISurfaceAnchorResolver anchorResolver,
            out SurfaceDecorationPlan plan,
            out string message)
        {
            plan = null;
            message = null;
            if (draft == null)
            {
                message = "Create a surface draft on a construct before previewing.";
                return false;
            }

            if (!draft.Settings.IsValid(out message))
                return false;

            if (draft.Faces.Count == 0)
            {
                message = "Surface draft has no faces.";
                return false;
            }

            var placements = new List<SurfaceDecorationPlacement>();
            var warnings = new List<string>();
            try
            {
                for (int faceIndex = 0; faceIndex < draft.Faces.Count; faceIndex++)
                {
                    SurfaceFace face = draft.Faces[faceIndex];
                    AddFacePlacements(
                        draft,
                        face,
                        faceIndex,
                        anchorResolver,
                        placements,
                        warnings);
                }
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }

            if (placements.Count == 0)
            {
                message = "Surface draft produced no decorations.";
                return false;
            }

            plan = new SurfaceDecorationPlan(draft.Construct, placements, warnings);
            message = placements.Count.ToString("N0", CultureInfo.InvariantCulture) + " decoration(s) ready.";
            return true;
        }

        internal static bool TryPlanWithSymmetry(
            SurfaceDraft draft,
            ISurfaceAnchorResolver anchorResolver,
            out SurfaceDecorationPlan plan,
            out string message)
        {
            plan = null;
            if (draft == null)
            {
                message = "Create a surface draft on a construct before previewing.";
                return false;
            }

            if (!DecoLimitLifter.EsuSymmetry.CanUseWith(draft.Construct, out message))
                return false;

            return TryPlanMirroredVariants(
                draft,
                anchorResolver,
                DecoLimitLifter.EsuSymmetry.Variants(),
                out plan,
                out message);
        }

        internal static bool TryPlanMirroredVariants(
            SurfaceDraft draft,
            ISurfaceAnchorResolver anchorResolver,
            IEnumerable<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variants,
            out SurfaceDecorationPlan plan,
            out string message)
        {
            plan = null;
            message = null;
            if (draft == null)
            {
                message = "Create a surface draft on a construct before previewing.";
                return false;
            }

            List<DecoLimitLifter.EsuSymmetry.SymmetryVariant> variantList =
                (variants ?? DecoLimitLifter.EsuSymmetry.Variants()).ToList();
            if (variantList.Count == 0)
                variantList.Add(new DecoLimitLifter.EsuSymmetry.SymmetryVariant(Array.Empty<DecorationEditAxis>()));

            var placements = new List<SurfaceDecorationPlacement>();
            var warnings = new List<string>();
            var placementKeys = new HashSet<string>();
            var geometryKeys = new HashSet<string>();
            int plannedVariants = 0;
            for (int index = 0; index < variantList.Count; index++)
            {
                DecoLimitLifter.EsuSymmetry.SymmetryVariant variant = variantList[index];
                SurfaceDraft variantDraft = variant.IsIdentity
                    ? draft
                    : draft.CreateMirroredForSymmetry(variant);
                if (!geometryKeys.Add(GeometryKey(variantDraft)))
                    continue;

                if (!TryPlan(variantDraft, anchorResolver, out SurfaceDecorationPlan variantPlan, out string variantMessage))
                {
                    message = variant.IsIdentity
                        ? variantMessage
                        : "Surface symmetry placement rejected: " + variantMessage;
                    return false;
                }

                plannedVariants++;
                foreach (SurfaceDecorationPlacement placement in variantPlan.Placements)
                {
                    if (placementKeys.Add(PlacementKey(placement)))
                        placements.Add(placement);
                }

                foreach (string warning in variantPlan.Warnings)
                {
                    if (!warnings.Contains(warning))
                        warnings.Add(warning);
                }
            }

            if (placements.Count == 0)
            {
                message = "Surface draft produced no decorations.";
                return false;
            }

            plan = new SurfaceDecorationPlan(draft.Construct, placements, warnings);
            message = plannedVariants > 1
                ? "Surface symmetry: " +
                  placements.Count.ToString("N0", CultureInfo.InvariantCulture) +
                  " decoration(s) ready across " +
                  plannedVariants.ToString("N0", CultureInfo.InvariantCulture) +
                  " variant(s)."
                : placements.Count.ToString("N0", CultureInfo.InvariantCulture) + " decoration(s) ready.";
            return true;
        }

        internal static bool SameGeometry(SurfaceDraft left, SurfaceDraft right) =>
            string.Equals(GeometryKey(left), GeometryKey(right), StringComparison.Ordinal);

        private static string GeometryKey(SurfaceDraft draft)
        {
            if (draft == null)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            for (int index = 0; index < draft.Points.Count; index++)
            {
                Vector3 point = draft.Points[index];
                builder.Append(FloatKey(point.x)).Append(',')
                    .Append(FloatKey(point.y)).Append(',')
                    .Append(FloatKey(point.z)).Append(';');
            }

            builder.Append('|');
            foreach (SurfaceFace face in draft.Faces)
            {
                int[] indexes = { face.A, face.B, face.C };
                Array.Sort(indexes);
                builder.Append(indexes[0]).Append(',')
                    .Append(indexes[1]).Append(',')
                    .Append(indexes[2]).Append(';');
            }

            return builder.ToString();
        }

        private static string PlacementKey(SurfaceDecorationPlacement placement) =>
            placement.MeshGuid.ToString("N") + "|" +
            CellKey(placement.Anchor) + "|" +
            VectorKey(placement.Positioning);

        private static string CellKey(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + ":" +
            value.y.ToString(CultureInfo.InvariantCulture) + ":" +
            value.z.ToString(CultureInfo.InvariantCulture);

        private static string VectorKey(Vector3 value) =>
            FloatKey(value.x) + ":" + FloatKey(value.y) + ":" + FloatKey(value.z);

        private static string FloatKey(float value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

        private static void AddFacePlacements(
            SurfaceDraft draft,
            SurfaceFace face,
            int faceIndex,
            ISurfaceAnchorResolver anchorResolver,
            List<SurfaceDecorationPlacement> placements,
            List<string> warnings)
        {
            if (face.A < 0 || face.B < 0 || face.C < 0 ||
                face.A >= draft.Points.Count ||
                face.B >= draft.Points.Count ||
                face.C >= draft.Points.Count)
            {
                throw SurfaceGeometryError(faceIndex, "references a missing surface point");
            }

            var vertices = new List<Vector3>
            {
                draft.Points[face.A],
                draft.Points[face.B],
                draft.Points[face.C]
            };
            ValidateSurfaceFace(vertices, faceIndex);

            List<PolygonData> polygons = BuildSurfacePolygons(vertices, faceIndex);

            foreach (PolygonData polygon in polygons)
            {
                Vector3 thicknessAxis = DecorationThicknessAxis(polygon, draft.Settings.NormalReversal);
                var data = new MimicAndDecorationCommonData();
                MADCD_PolygonInput.Start(
                    data,
                    polygon,
                    new PolygonDecorationSettings(
                        draft.Settings.NormalReversal,
                        draft.Settings.FaceThickness,
                        draft.Settings.FaceThickness,
                        draft.Settings.StructureBlockType),
                    Mathf.Clamp(draft.Settings.ColorIndex, 0, 31));

                Guid meshGuid;
                Vector3 center;
                Vector3 scaling;
                Vector3 orientation;
                int color;
                if (!data.TryGetStandaloneData(out meshGuid, out center, out scaling, out orientation, out color))
                    throw new InvalidOperationException("Surface polygon conversion did not produce standalone decoration data.");

                Vector3i anchor = new Vector3i(0, 0, 0);
                if (draft.Settings.NearestAnchor)
                {
                    if (anchorResolver == null ||
                        !anchorResolver.TryResolveAnchor(center, out anchor))
                    {
                        throw new InvalidOperationException(
                            "Surface face " +
                            (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " has no valid nearest anchor within +/-10m.");
                    }
                }

                Vector3 positioning = RoundPlacementPosition(center - ToVector3(anchor));
                if (!DecorationEditMath.IsWithinPositionLimit(positioning))
                {
                    throw new InvalidOperationException(
                        "Surface face " +
                        (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                        " generated a decoration outside FTD's +/-10 positioning limit.");
                }

                if (!draft.Settings.NearestAnchor)
                    warnings.Add("Nearest anchoring is off; generated offsets are relative to 0,0,0.");

                Vector3 transformThicknessAxis = DecorationTransformThicknessAxis(polygon, orientation);
                float transformPlaneDistance = Vector3.Dot(
                    transformThicknessAxis,
                    ToVector3(anchor) + positioning);

                placements.Add(new SurfaceDecorationPlacement(
                    anchor,
                    meshGuid,
                    positioning,
                    scaling,
                    orientation,
                    color,
                    thicknessAxis,
                    transformThicknessAxis,
                    transformPlaneDistance));
            }
        }

        private static void ValidateSurfaceFace(IReadOnlyList<Vector3> vertices, int faceIndex)
        {
            for (int index = 0; index < vertices.Count; index++)
            {
                if (!DecorationEditMath.IsFinite(vertices[index]))
                    throw SurfaceGeometryError(faceIndex, "contains a non-finite point");
            }

            Vector3 ab = vertices[1] - vertices[0];
            Vector3 bc = vertices[2] - vertices[1];
            Vector3 ca = vertices[0] - vertices[2];
            if (!DecorationEditMath.IsFinite(ab) ||
                !DecorationEditMath.IsFinite(bc) ||
                !DecorationEditMath.IsFinite(ca))
            {
                throw SurfaceGeometryError(faceIndex, "has a non-finite edge");
            }

            if (ab.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon ||
                bc.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon ||
                ca.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                throw SurfaceGeometryError(faceIndex, "contains a repeated or zero-length edge");
            }

            Vector3 normal = Vector3.Cross(ab, vertices[2] - vertices[0]);
            if (!DecorationEditMath.IsFinite(normal) ||
                normal.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                throw SurfaceGeometryError(faceIndex, "is a zero-area triangle");
            }
        }

        private static List<PolygonData> BuildSurfacePolygons(List<Vector3> vertices, int faceIndex)
        {
            var polygons = new List<PolygonData>();
            int sourceLine = faceIndex + 1;
            PolygonDataControl.PolygonClassify(
                polygons,
                new[] { new[] { new[] { 0 }, new[] { 1 }, new[] { 2 } } },
                Array.Empty<int[]>(),
                vertices,
                null,
                new[] { sourceLine },
                Array.Empty<int>(),
                3);

            if (polygons.Count == 2 &&
                polygons.All(polygon =>
                    polygon.PolyType == PolygonType.OtherTriangle_F ||
                    polygon.PolyType == PolygonType.OtherTriangle_B) &&
                TryBuildCoPlanarScalenePolygons(vertices, sourceLine, out List<PolygonData> scalenePolygons))
            {
                return scalenePolygons;
            }

            return polygons;
        }

        private static bool TryBuildCoPlanarScalenePolygons(
            IReadOnlyList<Vector3> vertices,
            int sourceLine,
            out List<PolygonData> polygons)
        {
            polygons = null;
            int longestSide = LongestTriangleSide(vertices);
            int startIndex = longestSide;
            int endIndex = (longestSide + 1) % 3;
            int apexIndex = (longestSide + 2) % 3;

            Vector3 start = vertices[startIndex];
            Vector3 end = vertices[endIndex];
            Vector3 apex = vertices[apexIndex];
            Vector3 baseVector = end - start;
            float baseLengthSquared = baseVector.sqrMagnitude;
            if (!DecorationEditMath.IsFinite(baseVector) ||
                !IsFinite(baseLengthSquared) ||
                baseLengthSquared <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                return false;
            }

            float t = Vector3.Dot(apex - start, baseVector) / baseLengthSquared;
            if (!IsFinite(t) ||
                t <= SurfaceGeometryEpsilon ||
                t >= 1f - SurfaceGeometryEpsilon)
            {
                return false;
            }

            Vector3 foot = start + baseVector * t;
            if (!DecorationEditMath.IsFinite(foot))
                return false;

            polygons = new List<PolygonData>
            {
                CreateRightTrianglePolygon(
                    new List<Vector3> { apex, start, foot },
                    sourceLine),
                CreateRightTrianglePolygon(
                    new List<Vector3> { end, apex, foot },
                    sourceLine)
            };
            return true;
        }

        private static PolygonData CreateRightTrianglePolygon(List<Vector3> vertices, int sourceLine)
        {
            return new PolygonData(
                PolygonType.RightTriangle,
                new[] { 0, 1, 2 },
                vertices,
                Vector2.zero,
                sourceLine);
        }

        private static int LongestTriangleSide(IReadOnlyList<Vector3> vertices)
        {
            int longest = 0;
            float longestSquared = -1f;
            for (int side = 0; side < 3; side++)
            {
                Vector3 edge = vertices[(side + 1) % 3] - vertices[side];
                float squared = edge.sqrMagnitude;
                if (squared > longestSquared)
                {
                    longestSquared = squared;
                    longest = side;
                }
            }

            return longest;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static Vector3 RoundPlacementPosition(Vector3 value) =>
            new Vector3(
                RoundPlacementComponent(value.x),
                RoundPlacementComponent(value.y),
                RoundPlacementComponent(value.z));

        private static float RoundPlacementComponent(float value)
        {
            if (!IsFinite(value))
                return value;
            return Mathf.Round(value * 10000f) / 10000f;
        }

        private static Vector3 DecorationThicknessAxis(PolygonData polygon, bool normalReversal)
        {
            SideData[] sides = polygon.Sides;
            Vector3 axis;
            switch (polygon.PolyType)
            {
                case PolygonType.RightTriangle:
                    axis = Vector3.Cross(sides[2].SideVector, -sides[1].SideVector);
                    break;
                case PolygonType.OtherTriangle_F:
                    axis = Vector3.Cross(sides[1].SideVector, sides[0].SideVector);
                    break;
                case PolygonType.OtherTriangle_B:
                    axis = Vector3.Cross(-sides[2].SideVector, -sides[0].SideVector);
                    break;
                case PolygonType.IsoscelesTriangle:
                    axis = polygon.NormalVector * (normalReversal ? -1f : 1f);
                    break;
                case PolygonType.Rectangle:
                    axis = Vector3.Cross(sides[1].SideVector, sides[0].SideVector);
                    break;
                default:
                    axis = polygon.NormalVector;
                    break;
            }

            if (!DecorationEditMath.IsFinite(axis) ||
                axis.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                return Vector3.zero;
            }

            axis.Normalize();
            return axis;
        }

        private static Vector3 DecorationTransformThicknessAxis(PolygonData polygon, Vector3 orientation)
        {
            Vector3 localAxis;
            switch (polygon.PolyType)
            {
                case PolygonType.RightTriangle:
                case PolygonType.OtherTriangle_F:
                case PolygonType.OtherTriangle_B:
                case PolygonType.Rectangle:
                    localAxis = Vector3.right;
                    break;
                case PolygonType.IsoscelesTriangle:
                    localAxis = Vector3.up;
                    break;
                case PolygonType.Ellipse:
                    localAxis = Vector3.forward;
                    break;
                default:
                    localAxis = Vector3.forward;
                    break;
            }

            Vector3 axis = RotateEuler(orientation, localAxis);
            if (!DecorationEditMath.IsFinite(axis) ||
                axis.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                return Vector3.zero;
            }

            axis.Normalize();
            return axis;
        }

        private static Vector3 RotateEuler(Vector3 degrees, Vector3 vector)
        {
            double halfX = degrees.x * Math.PI / 360d;
            double halfY = degrees.y * Math.PI / 360d;
            double halfZ = degrees.z * Math.PI / 360d;
            double cx = Math.Cos(halfX);
            double sx = Math.Sin(halfX);
            double cy = Math.Cos(halfY);
            double sy = Math.Sin(halfY);
            double cz = Math.Cos(halfZ);
            double sz = Math.Sin(halfZ);

            double qw = cx * cy * cz + sx * sy * sz;
            double qx = sx * cy * cz - cx * sy * sz;
            double qy = cx * sy * cz + sx * cy * sz;
            double qz = cx * cy * sz - sx * sy * cz;

            double tx = 2d * (qy * vector.z - qz * vector.y);
            double ty = 2d * (qz * vector.x - qx * vector.z);
            double tz = 2d * (qx * vector.y - qy * vector.x);

            return new Vector3(
                (float)(vector.x + qw * tx + qy * tz - qz * ty),
                (float)(vector.y + qw * ty + qz * tx - qx * tz),
                (float)(vector.z + qw * tz + qx * ty - qy * tx));
        }

        private static InvalidOperationException SurfaceGeometryError(int faceIndex, string message) =>
            new InvalidOperationException(
                "Surface face " +
                (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " " +
                message +
                ".");

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);
    }
}
