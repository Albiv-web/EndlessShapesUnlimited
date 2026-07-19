using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed partial class SurfaceDraft
    {
        private const int MaxModelingBoundaryPoints = 2048;

        /// <summary>
        /// Extrudes the selected face along its current normal. The selected face
        /// becomes the cap and six side triangles connect it to the old boundary.
        /// </summary>
        internal bool TryExtrudeSelectedFace(float distance, out string message)
        {
            message = null;
            if (!TryGetSelectedModelingFace(out SurfaceFace face, out SurfaceFaceStyle style, out message))
                return false;
            if (!IsFiniteModelingValue(distance) || Math.Abs(distance) <= TypedGeometryEpsilon)
            {
                message = "Surface face extrusion distance must be finite and non-zero.";
                return false;
            }

            if (!TryGetFaceNormal(face, out Vector3 normal))
            {
                message = "Selected surface face has no usable normal.";
                return false;
            }

            normal.Normalize();
            return TryExtrudeSelectedFace(normal * distance, style, out message);
        }

        /// <summary>
        /// Extrudes the selected face by an explicit construct-local offset.
        /// This overload is intended for a later interactive gizmo workflow.
        /// </summary>
        internal bool TryExtrudeSelectedFace(Vector3 offset, out string message)
        {
            message = null;
            if (!TryGetSelectedModelingFace(out SurfaceFace _, out SurfaceFaceStyle style, out message))
                return false;

            return TryExtrudeSelectedFace(offset, style, out message);
        }

        private bool TryExtrudeSelectedFace(
            Vector3 offset,
            SurfaceFaceStyle style,
            out string message)
        {
            message = null;
            if (!TryValidateModelingOffset(offset, "Surface face extrusion", out message))
                return false;

            int selectedFace = SelectedFace;
            SurfaceFace source = _faces[selectedFace];
            var points = new List<Vector3>(_points);
            var faces = new List<SurfaceFace>(_faces);
            var styles = ModelingFaceStyles();

            int a2 = points.Count;
            int b2 = a2 + 1;
            int c2 = a2 + 2;
            points.Add(_points[source.A] + offset);
            points.Add(_points[source.B] + offset);
            points.Add(_points[source.C] + offset);

            faces[selectedFace] = new SurfaceFace(a2, b2, c2);
            styles[selectedFace] = style;
            AddExtrusionSide(faces, styles, source.A, source.B, a2, b2, style);
            AddExtrusionSide(faces, styles, source.B, source.C, b2, c2, style);
            AddExtrusionSide(faces, styles, source.C, source.A, c2, a2, style);

            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Face,
                    -1,
                    selectedFace,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = "Surface face extruded; the new cap is selected.";
            return true;
        }

        /// <summary>
        /// Extrudes the selected boundary edge outward in the plane of its one
        /// incident face. A negative distance deliberately extrudes inward.
        /// </summary>
        internal bool TryExtrudeSelectedEdge(float distance, out string message)
        {
            message = null;
            if (!TryGetSelectedBoundaryEdge(
                    out SurfaceEdge edge,
                    out int incidentFace,
                    out SurfaceFaceStyle _,
                    out message))
            {
                return false;
            }

            if (!IsFiniteModelingValue(distance) || Math.Abs(distance) <= TypedGeometryEpsilon)
            {
                message = "Surface edge extrusion distance must be finite and non-zero.";
                return false;
            }

            SurfaceFace face = _faces[incidentFace];
            if (!TryGetFaceNormal(face, out Vector3 normal))
            {
                message = "The face beside the selected edge has no usable normal.";
                return false;
            }

            GetDirectedFaceEdge(face, edge, out int from, out int to);
            Vector3 direction = _points[to] - _points[from];
            if (!TryGetFiniteSquaredMagnitude(direction, out float edgeSquared) ||
                edgeSquared <= TypedEdgeLengthSquaredEpsilon)
            {
                message = "Selected surface edge has zero length.";
                return false;
            }

            direction.Normalize();
            normal.Normalize();
            Vector3 outward = Vector3.Cross(direction, normal);
            if (!TryGetFiniteSquaredMagnitude(outward, out float outwardSquared) ||
                outwardSquared <= TypedEdgeLengthSquaredEpsilon)
            {
                message = "Selected surface edge has no usable outward direction.";
                return false;
            }

            outward.Normalize();
            return TryExtrudeSelectedEdge(outward * distance, out message);
        }

        /// <summary>
        /// Extrudes the selected boundary edge by an explicit construct-local
        /// offset and selects the newly-created boundary edge.
        /// </summary>
        internal bool TryExtrudeSelectedEdge(Vector3 offset, out string message)
        {
            message = null;
            if (!TryGetSelectedBoundaryEdge(
                    out SurfaceEdge edge,
                    out int incidentFace,
                    out SurfaceFaceStyle style,
                    out message))
            {
                return false;
            }

            if (!TryValidateModelingOffset(offset, "Surface edge extrusion", out message))
                return false;

            SurfaceFace face = _faces[incidentFace];
            GetDirectedFaceEdge(face, edge, out int from, out int to);
            var points = new List<Vector3>(_points);
            var faces = new List<SurfaceFace>(_faces);
            var styles = ModelingFaceStyles();

            int from2 = points.Count;
            int to2 = from2 + 1;
            points.Add(_points[from] + offset);
            points.Add(_points[to] + offset);

            // The incident face travels from -> to, so the new side must use the
            // original edge in the opposite direction.
            faces.Add(new SurfaceFace(to, from, from2));
            styles.Add(style);
            faces.Add(new SurfaceFace(to, from2, to2));
            styles.Add(style);

            var newEdge = new SurfaceEdge(from2, to2);
            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Edge,
                    -1,
                    -1,
                    newEdge,
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = "Surface boundary edge extruded; the new edge is selected.";
            return true;
        }

        /// <summary>
        /// Insets the selected triangular face. insetFraction is the fraction of
        /// each corner-to-centroid vector consumed, and must be strictly between
        /// zero and one.
        /// </summary>
        internal bool TryInsetSelectedFace(float insetFraction, out string message)
        {
            message = null;
            if (!TryGetSelectedModelingFace(out SurfaceFace source, out SurfaceFaceStyle style, out message))
                return false;
            if (!IsFiniteModelingValue(insetFraction) ||
                insetFraction <= 0f ||
                insetFraction >= 1f)
            {
                message = "Surface face inset must be greater than zero and less than one.";
                return false;
            }

            Vector3 centroid = (_points[source.A] + _points[source.B] + _points[source.C]) / 3f;
            if (!DecorationEditMath.IsFinite(centroid))
            {
                message = "Selected surface face is outside the supported numeric range.";
                return false;
            }

            var points = new List<Vector3>(_points);
            var faces = new List<SurfaceFace>(_faces);
            var styles = ModelingFaceStyles();
            int a2 = points.Count;
            int b2 = a2 + 1;
            int c2 = a2 + 2;
            points.Add(Vector3.Lerp(_points[source.A], centroid, insetFraction));
            points.Add(Vector3.Lerp(_points[source.B], centroid, insetFraction));
            points.Add(Vector3.Lerp(_points[source.C], centroid, insetFraction));

            int selectedFace = SelectedFace;
            faces[selectedFace] = new SurfaceFace(a2, b2, c2);
            styles[selectedFace] = style;
            AddInsetRingSide(faces, styles, source.A, source.B, a2, b2, style);
            AddInsetRingSide(faces, styles, source.B, source.C, b2, c2, style);
            AddInsetRingSide(faces, styles, source.C, source.A, c2, a2, style);

            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Face,
                    -1,
                    selectedFace,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = "Surface face inset created; the inner face is selected.";
            return true;
        }

        /// <summary>
        /// Subdivides the selected triangle into four triangles. Faces sharing
        /// one of its edges are split too, preventing T-junctions.
        /// </summary>
        internal bool TrySubdivideSelectedFace(out string message)
        {
            message = null;
            if (!TryGetSelectedModelingFace(out SurfaceFace source, out SurfaceFaceStyle style, out message))
                return false;

            var points = new List<Vector3>(_points);
            int ab = AddMidpoint(points, source.A, source.B);
            int bc = AddMidpoint(points, source.B, source.C);
            int ca = AddMidpoint(points, source.C, source.A);
            var faces = new List<SurfaceFace>(_faces);
            var styles = ModelingFaceStyles();
            int selectedFace = SelectedFace;

            // Keep the center triangle at the old face index so history and a
            // later UI can retain a stable selected-face slot.
            faces[selectedFace] = new SurfaceFace(ab, bc, ca);
            styles[selectedFace] = style;
            faces.Add(new SurfaceFace(source.A, ab, ca));
            styles.Add(style);
            faces.Add(new SurfaceFace(ab, source.B, bc));
            styles.Add(style);
            faces.Add(new SurfaceFace(ca, bc, source.C));
            styles.Add(style);

            int originalFaceCount = _faces.Count;
            SplitFacesAcrossSubdividedEdge(faces, styles, originalFaceCount, selectedFace, source.A, source.B, ab);
            SplitFacesAcrossSubdividedEdge(faces, styles, originalFaceCount, selectedFace, source.B, source.C, bc);
            SplitFacesAcrossSubdividedEdge(faces, styles, originalFaceCount, selectedFace, source.C, source.A, ca);

            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Face,
                    -1,
                    selectedFace,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = "Surface face subdivided into four triangles; connected faces were split to match.";
            return true;
        }

        /// <summary>
        /// Applies one bounded Laplacian smoothing pass to the selected point,
        /// edge endpoints, face corners, or manually-selected point set. All
        /// target positions are calculated from the same pre-edit snapshot.
        /// </summary>
        internal bool TrySmoothSelectedPoints(float strength, out string message)
        {
            message = null;
            if (!IsFiniteModelingValue(strength) || strength <= 0f || strength > 1f)
            {
                message = "Surface smoothing strength must be greater than zero and no more than one.";
                return false;
            }

            int[] selected = SelectedSmoothingPoints();
            if (selected.Length == 0)
            {
                message = "Select one or more connected surface points, an edge, or a face before smoothing.";
                return false;
            }

            var neighbors = new Dictionary<int, HashSet<int>>();
            for (int index = 0; index < selected.Length; index++)
                neighbors[selected[index]] = new HashSet<int>();
            for (int faceIndex = 0; faceIndex < _faces.Count; faceIndex++)
            {
                SurfaceFace face = _faces[faceIndex];
                AddSmoothingNeighbors(neighbors, face.A, face.B, face.C);
                AddSmoothingNeighbors(neighbors, face.B, face.C, face.A);
                AddSmoothingNeighbors(neighbors, face.C, face.A, face.B);
            }

            var points = new List<Vector3>(_points);
            bool changed = false;
            for (int index = 0; index < selected.Length; index++)
            {
                int pointIndex = selected[index];
                HashSet<int> adjacent = neighbors[pointIndex];
                if (adjacent.Count == 0)
                {
                    message = "Every smoothed surface point must belong to at least one face.";
                    return false;
                }

                Vector3 average = Vector3.zero;
                foreach (int neighbor in adjacent)
                    average += _points[neighbor];
                average /= adjacent.Count;
                Vector3 smoothed = NormalizeTypedCoordinate(
                    _points[pointIndex] + (average - _points[pointIndex]) * strength);
                if (!DecorationEditMath.IsFinite(smoothed))
                {
                    message = "Surface smoothing produced a non-finite coordinate.";
                    return false;
                }

                points[pointIndex] = smoothed;
                if ((smoothed - _points[pointIndex]).sqrMagnitude > TypedGeometryEpsilon)
                    changed = true;
            }

            if (!changed)
            {
                message = "Selected surface points are already locally smooth at this precision.";
                return false;
            }

            var selectedSet = new HashSet<int>(selected);
            for (int faceIndex = 0; faceIndex < _faces.Count; faceIndex++)
            {
                SurfaceFace face = _faces[faceIndex];
                if (!selectedSet.Contains(face.A) &&
                    !selectedSet.Contains(face.B) &&
                    !selectedSet.Contains(face.C))
                {
                    continue;
                }

                Vector3 oldNormal = Vector3.Cross(
                    _points[face.B] - _points[face.A],
                    _points[face.C] - _points[face.A]);
                Vector3 newNormal = Vector3.Cross(
                    points[face.B] - points[face.A],
                    points[face.C] - points[face.A]);
                if (!DecorationEditMath.IsFinite(oldNormal) ||
                    !DecorationEditMath.IsFinite(newNormal) ||
                    Vector3.Dot(oldNormal, newNormal) <= 0f)
                {
                    message = "Surface smoothing would collapse or invert a connected face.";
                    return false;
                }
            }

            int[] manualSelection = _manualFaceSelection
                .Where(index => index >= 0 && index < _points.Count)
                .Distinct()
                .ToArray();
            SurfaceSelectionKind selectionKind = SelectionKind;
            int selectedPoint = SelectedPoint;
            int selectedFace = SelectedFace;
            SurfaceEdge selectedEdge = SelectedEdge;
            if (!TryCommitModelingEdit(
                    points,
                    new List<SurfaceFace>(_faces),
                    ModelingFaceStyles(),
                    selectionKind,
                    selectedPoint,
                    selectedFace,
                    selectedEdge,
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            // TryCommitModelingEdit intentionally clears transient selection
            // sets; restore a manual point set only after the validated commit.
            if (manualSelection.Length > 0)
                _manualFaceSelection.AddRange(manualSelection);
            message = selected.Length.ToString(CultureInfo.InvariantCulture) +
                      (selected.Length == 1
                          ? " surface point smoothed."
                          : " surface points smoothed.");
            return true;
        }

        /// <summary>
        /// Welds the manually-selected points, or the two endpoints of the
        /// selected edge, to their average construct-local position.
        /// </summary>
        internal bool TryWeldSelectedPoints(out string message)
        {
            int[] selected = SelectedModelingPoints();
            if (selected.Length < 2)
            {
                message = "Select at least two surface points, or select an edge, before welding.";
                return false;
            }

            Vector3 average = Vector3.zero;
            for (int index = 0; index < selected.Length; index++)
                average += _points[selected[index]];
            average /= selected.Length;
            return TryWeldPoints(selected, average, out message);
        }

        /// <summary>
        /// Merges the supplied points into one deterministic survivor at an
        /// explicit construct-local position. Degenerate and duplicate faces
        /// created by the merge are removed atomically.
        /// </summary>
        internal bool TryWeldPoints(
            IReadOnlyList<int> pointIndexes,
            Vector3 weldPosition,
            out string message)
        {
            message = null;
            int[] selected = (pointIndexes ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            if (selected.Length < 2)
            {
                message = "Weld needs at least two different surface points.";
                return false;
            }
            if (selected.Any(index => index < 0 || index >= _points.Count))
            {
                message = "A surface point selected for welding is no longer available.";
                return false;
            }
            if (!DecorationEditMath.IsFinite(weldPosition))
            {
                message = "Surface weld position must be finite.";
                return false;
            }

            weldPosition = NormalizeTypedCoordinate(weldPosition);
            if (!TryGetFiniteSquaredMagnitude(weldPosition, out float _))
            {
                message = "Surface weld position is outside the supported numeric range.";
                return false;
            }

            var selectedSet = new HashSet<int>(selected);
            int survivor = selected[0];
            int[] remap = new int[_points.Count];
            var points = new List<Vector3>(_points.Count - selected.Length + 1);
            int survivorNew = -1;
            for (int oldIndex = 0; oldIndex < _points.Count; oldIndex++)
            {
                if (selectedSet.Contains(oldIndex) && oldIndex != survivor)
                    continue;

                int newIndex = points.Count;
                remap[oldIndex] = newIndex;
                if (oldIndex == survivor)
                {
                    survivorNew = newIndex;
                    points.Add(weldPosition);
                }
                else
                {
                    points.Add(_points[oldIndex]);
                }
            }
            for (int index = 0; index < selected.Length; index++)
                remap[selected[index]] = survivorNew;

            var faces = new List<SurfaceFace>(_faces.Count);
            var styles = new List<SurfaceFaceStyle>(_faces.Count);
            var faceKeys = new HashSet<string>(StringComparer.Ordinal);
            int removedFaces = 0;
            for (int faceIndex = 0; faceIndex < _faces.Count; faceIndex++)
            {
                SurfaceFace old = _faces[faceIndex];
                var mapped = new SurfaceFace(remap[old.A], remap[old.B], remap[old.C]);
                if (mapped.A == mapped.B || mapped.B == mapped.C || mapped.A == mapped.C)
                {
                    removedFaces++;
                    continue;
                }

                string key = ModelingFaceKey(mapped);
                if (!faceKeys.Add(key))
                {
                    removedFaces++;
                    continue;
                }

                faces.Add(mapped);
                styles.Add(FaceStyleAt(faceIndex));
            }

            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Point,
                    survivorNew,
                    -1,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = selected.Length.ToString(CultureInfo.InvariantCulture) +
                      " surface points welded" +
                      (removedFaces > 0
                          ? "; " + removedFaces.ToString(CultureInfo.InvariantCulture) + " collapsed face(s) removed."
                          : ".");
            return true;
        }

        /// <summary>
        /// Merges supplied points onto one selected survivor without moving it.
        /// </summary>
        internal bool TryMergePointsTo(
            IReadOnlyList<int> pointIndexes,
            int targetPoint,
            out string message)
        {
            message = null;
            if (targetPoint < 0 || targetPoint >= _points.Count)
            {
                message = "Choose an existing surface point as the merge target.";
                return false;
            }

            int[] selected = (pointIndexes ?? Array.Empty<int>())
                .Concat(new[] { targetPoint })
                .Distinct()
                .ToArray();
            return TryWeldPoints(selected, _points[targetPoint], out message);
        }

        /// <summary>
        /// Finds and fills the closed boundary loop containing the selected edge.
        /// </summary>
        internal bool TryFillSelectedBoundary(out string message)
        {
            message = null;
            if (!SelectedEdge.IsValid)
            {
                message = "Select an open surface boundary edge before filling a hole.";
                return false;
            }

            if (!TryTraceBoundaryLoop(SelectedEdge, out List<int> boundary, out message))
                return false;

            SurfaceFaceStyle style = SurfaceFaceStyle.FromSettings(Settings);
            int incident = FindIncidentFaces(SelectedEdge).DefaultIfEmpty(-1).First();
            if (incident >= 0)
                style = FaceStyleAt(incident);
            return TryFillBoundary(boundary, style, out message);
        }

        /// <summary>
        /// Fills an explicitly ordered, closed boundary loop. Every consecutive
        /// pair, including last-to-first, must currently be an open mesh edge.
        /// </summary>
        internal bool TryFillBoundary(IReadOnlyList<int> boundary, out string message) =>
            TryFillBoundary(boundary, SurfaceFaceStyle.FromSettings(Settings), out message);

        private bool TryFillBoundary(
            IReadOnlyList<int> boundary,
            SurfaceFaceStyle style,
            out string message)
        {
            message = null;
            int[] loop = NormalizeBoundary(boundary, out message);
            if (loop == null)
                return false;
            if (!TryOrientBoundaryForFill(loop, out message))
                return false;
            if (!TryTriangulateBoundary(loop, out List<SurfaceFace> fillFaces, out message))
                return false;

            var points = new List<Vector3>(_points);
            var faces = new List<SurfaceFace>(_faces);
            var styles = ModelingFaceStyles();
            int firstFillFace = faces.Count;
            for (int index = 0; index < fillFaces.Count; index++)
            {
                if (faces.Any(face => SameFace(
                        face,
                        fillFaces[index].A,
                        fillFaces[index].B,
                        fillFaces[index].C)))
                {
                    message = "Boundary fill would duplicate an existing surface face.";
                    return false;
                }
                faces.Add(fillFaces[index]);
                styles.Add(style);
            }

            if (!TryCommitModelingEdit(
                    points,
                    faces,
                    styles,
                    SurfaceSelectionKind.Face,
                    -1,
                    firstFillFace,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: false,
                    out message))
            {
                return false;
            }

            message = fillFaces.Count.ToString(CultureInfo.InvariantCulture) +
                      (fillFaces.Count == 1
                          ? " surface face filled the selected boundary."
                          : " surface faces filled the selected boundary.");
            return true;
        }

        /// <summary>
        /// Reverses the selected face winding. Winding conflicts with connected
        /// faces are intentional for this operation and are therefore permitted.
        /// </summary>
        internal bool TryFlipSelectedFaceNormal(out string message)
        {
            message = null;
            if (!TryGetSelectedModelingFace(out SurfaceFace face, out SurfaceFaceStyle _, out message))
                return false;

            var faces = new List<SurfaceFace>(_faces);
            faces[SelectedFace] = face.Flipped();
            if (!TryCommitModelingEdit(
                    new List<Vector3>(_points),
                    faces,
                    ModelingFaceStyles(),
                    SurfaceSelectionKind.Face,
                    -1,
                    SelectedFace,
                    new SurfaceEdge(-1, -1),
                    allowWindingConflicts: true,
                    out message))
            {
                return false;
            }

            message = "Selected surface face normal flipped.";
            return true;
        }

        private bool TryGetSelectedModelingFace(
            out SurfaceFace face,
            out SurfaceFaceStyle style,
            out string message)
        {
            face = default;
            style = default;
            message = null;
            if (SelectionKind != SurfaceSelectionKind.Face ||
                SelectedFace < 0 ||
                SelectedFace >= _faces.Count)
            {
                message = "Select a surface face before using this modeling operation.";
                return false;
            }

            face = _faces[SelectedFace];
            style = FaceStyleAt(SelectedFace);
            if (!TryGetFaceNormal(face, out Vector3 _))
            {
                message = "Selected surface face has zero area.";
                return false;
            }
            return true;
        }

        private bool TryGetSelectedBoundaryEdge(
            out SurfaceEdge edge,
            out int incidentFace,
            out SurfaceFaceStyle style,
            out string message)
        {
            edge = SelectedEdge;
            incidentFace = -1;
            style = default;
            message = null;
            if (SelectionKind != SurfaceSelectionKind.Edge ||
                !edge.IsValid ||
                edge.A >= _points.Count ||
                edge.B >= _points.Count)
            {
                message = "Select a surface boundary edge before extruding it.";
                return false;
            }

            int[] incident = FindIncidentFaces(edge).ToArray();
            if (incident.Length != 1)
            {
                message = incident.Length == 0
                    ? "Selected edge is not part of a surface face."
                    : "Only an open boundary edge can be extruded; the selected edge has faces on both sides.";
                return false;
            }

            incidentFace = incident[0];
            style = FaceStyleAt(incidentFace);
            return true;
        }

        private IEnumerable<int> FindIncidentFaces(SurfaceEdge edge)
        {
            for (int index = 0; index < _faces.Count; index++)
            {
                if (_faces[index].ContainsEdge(edge.A, edge.B))
                    yield return index;
            }
        }

        private static void AddExtrusionSide(
            ICollection<SurfaceFace> faces,
            ICollection<SurfaceFaceStyle> styles,
            int from,
            int to,
            int from2,
            int to2,
            SurfaceFaceStyle style)
        {
            faces.Add(new SurfaceFace(from, to, to2));
            styles.Add(style);
            faces.Add(new SurfaceFace(from, to2, from2));
            styles.Add(style);
        }

        private static void AddInsetRingSide(
            ICollection<SurfaceFace> faces,
            ICollection<SurfaceFaceStyle> styles,
            int from,
            int to,
            int from2,
            int to2,
            SurfaceFaceStyle style) =>
            AddExtrusionSide(faces, styles, from, to, from2, to2, style);

        private int AddMidpoint(ICollection<Vector3> points, int a, int b)
        {
            int index = points.Count;
            points.Add((_points[a] + _points[b]) * 0.5f);
            return index;
        }

        private static void SplitFacesAcrossSubdividedEdge(
            IList<SurfaceFace> faces,
            IList<SurfaceFaceStyle> styles,
            int originalFaceCount,
            int selectedFace,
            int a,
            int b,
            int midpoint)
        {
            for (int faceIndex = 0; faceIndex < originalFaceCount; faceIndex++)
            {
                if (faceIndex == selectedFace || !faces[faceIndex].ContainsEdge(a, b))
                    continue;

                SurfaceFace face = faces[faceIndex];
                SurfaceFaceStyle style = styles[faceIndex];
                GetDirectedFaceEdge(face, new SurfaceEdge(a, b), out int from, out int to);
                int third = face.Points().First(point => point != from && point != to);
                faces[faceIndex] = new SurfaceFace(from, midpoint, third);
                faces.Add(new SurfaceFace(midpoint, to, third));
                styles.Add(style);
            }
        }

        private static void GetDirectedFaceEdge(
            SurfaceFace face,
            SurfaceEdge edge,
            out int from,
            out int to)
        {
            if (face.HasDirectedEdge(edge.A, edge.B))
            {
                from = edge.A;
                to = edge.B;
            }
            else
            {
                from = edge.B;
                to = edge.A;
            }
        }

        private int[] SelectedModelingPoints()
        {
            int[] manual = _manualFaceSelection
                .Where(index => index >= 0 && index < _points.Count)
                .Distinct()
                .ToArray();
            if (manual.Length >= 2)
                return manual;
            if (SelectionKind == SurfaceSelectionKind.Edge &&
                SelectedEdge.IsValid &&
                SelectedEdge.A < _points.Count &&
                SelectedEdge.B < _points.Count)
            {
                return new[] { SelectedEdge.A, SelectedEdge.B };
            }
            return manual;
        }

        private int[] SelectedSmoothingPoints()
        {
            int[] manual = _manualFaceSelection
                .Where(index => index >= 0 && index < _points.Count)
                .Distinct()
                .ToArray();
            if (manual.Length > 0)
                return manual;

            if (SelectionKind == SurfaceSelectionKind.Point &&
                SelectedPoint >= 0 &&
                SelectedPoint < _points.Count)
            {
                return new[] { SelectedPoint };
            }
            if (SelectionKind == SurfaceSelectionKind.Edge &&
                SelectedEdge.IsValid &&
                SelectedEdge.A < _points.Count &&
                SelectedEdge.B < _points.Count)
            {
                return new[] { SelectedEdge.A, SelectedEdge.B };
            }
            if (SelectionKind == SurfaceSelectionKind.Face &&
                SelectedFace >= 0 &&
                SelectedFace < _faces.Count)
            {
                SurfaceFace face = _faces[SelectedFace];
                return new[] { face.A, face.B, face.C };
            }
            return Array.Empty<int>();
        }

        private static void AddSmoothingNeighbors(
            IReadOnlyDictionary<int, HashSet<int>> neighbors,
            int point,
            int first,
            int second)
        {
            if (!neighbors.TryGetValue(point, out HashSet<int> adjacent))
                return;
            adjacent.Add(first);
            adjacent.Add(second);
        }

        private bool TryTraceBoundaryLoop(
            SurfaceEdge seed,
            out List<int> boundary,
            out string message)
        {
            boundary = null;
            message = null;
            var incidence = BuildModelingEdgeIncidence(_faces);
            string seedKey = ModelingEdgeKey(seed.A, seed.B);
            if (!incidence.TryGetValue(seedKey, out ModelingEdgeIncidence seedIncidence) ||
                seedIncidence.Count != 1)
            {
                message = "Selected edge is not an open surface boundary edge.";
                return false;
            }

            var neighbors = new Dictionary<int, List<int>>();
            foreach (ModelingEdgeIncidence edge in incidence.Values)
            {
                if (edge.Count != 1)
                    continue;
                AddBoundaryNeighbor(neighbors, edge.A, edge.B);
                AddBoundaryNeighbor(neighbors, edge.B, edge.A);
            }

            if (!neighbors.TryGetValue(seed.A, out List<int> startNeighbors) ||
                !neighbors.TryGetValue(seed.B, out List<int> endNeighbors) ||
                startNeighbors.Count != 2 ||
                endNeighbors.Count != 2)
            {
                message = "Selected boundary is open or branched and cannot be filled as one hole.";
                return false;
            }

            var loop = new List<int> { seed.A, seed.B };
            var visited = new HashSet<int> { seed.A, seed.B };
            int previous = seed.A;
            int current = seed.B;
            while (loop.Count <= MaxModelingBoundaryPoints)
            {
                if (!neighbors.TryGetValue(current, out List<int> currentNeighbors) ||
                    currentNeighbors.Count != 2)
                {
                    message = "Selected boundary is open or branched and cannot be filled as one hole.";
                    return false;
                }

                int next = currentNeighbors[0] == previous
                    ? currentNeighbors[1]
                    : currentNeighbors[0];
                if (next == seed.A)
                {
                    if (loop.Count < 3)
                    {
                        message = "A fill boundary needs at least three points.";
                        return false;
                    }
                    boundary = loop;
                    return true;
                }
                if (!visited.Add(next))
                {
                    message = "Selected boundary crosses or revisits itself.";
                    return false;
                }

                loop.Add(next);
                previous = current;
                current = next;
            }

            message = "Selected boundary is too large to fill safely in one operation.";
            return false;
        }

        private static void AddBoundaryNeighbor(
            IDictionary<int, List<int>> neighbors,
            int point,
            int neighbor)
        {
            if (!neighbors.TryGetValue(point, out List<int> values))
            {
                values = new List<int>(2);
                neighbors.Add(point, values);
            }
            if (!values.Contains(neighbor))
                values.Add(neighbor);
        }

        private int[] NormalizeBoundary(IReadOnlyList<int> boundary, out string message)
        {
            message = null;
            if (boundary == null)
            {
                message = "Choose a closed surface boundary before filling it.";
                return null;
            }

            var loop = boundary.ToList();
            if (loop.Count > 1 && loop[0] == loop[loop.Count - 1])
                loop.RemoveAt(loop.Count - 1);
            if (loop.Count < 3)
            {
                message = "A fill boundary needs at least three different points.";
                return null;
            }
            if (loop.Count > MaxModelingBoundaryPoints)
            {
                message = "Surface boundary exceeds the safe " +
                          MaxModelingBoundaryPoints.ToString(CultureInfo.InvariantCulture) +
                          "-point fill limit.";
                return null;
            }
            if (loop.Any(index => index < 0 || index >= _points.Count) ||
                loop.Distinct().Count() != loop.Count)
            {
                message = "Surface fill boundary contains a missing or repeated point.";
                return null;
            }

            var incidence = BuildModelingEdgeIncidence(_faces);
            for (int index = 0; index < loop.Count; index++)
            {
                int next = (index + 1) % loop.Count;
                if (!incidence.TryGetValue(
                        ModelingEdgeKey(loop[index], loop[next]),
                        out ModelingEdgeIncidence edge) ||
                    edge.Count != 1)
                {
                    message = "Every fill-boundary segment must be an open surface edge.";
                    return null;
                }
            }
            return loop.ToArray();
        }

        private bool TryOrientBoundaryForFill(int[] loop, out string message)
        {
            message = null;
            int same = 0;
            int opposite = 0;
            for (int index = 0; index < loop.Length; index++)
            {
                int from = loop[index];
                int to = loop[(index + 1) % loop.Length];
                SurfaceFace adjacent = _faces.First(face => face.ContainsEdge(from, to));
                if (adjacent.HasDirectedEdge(from, to))
                    same++;
                else
                    opposite++;
            }

            if (same > 0 && opposite > 0)
            {
                message = "Boundary faces have conflicting winding and cannot be filled atomically.";
                return false;
            }

            // Fill faces must run opposite to their adjacent faces along every
            // boundary edge.
            if (same > 0)
                // Use an explicit in-place reversal instead of Array.Reverse<T>.
                // The mod targets netstandard2.1 while the standalone FtD verifier
                // hosts it on .NET Framework, whose mscorlib lacks that emitted
                // generic overload.
                for (int left = 0, right = loop.Length - 1; left < right; left++, right--)
                {
                    int swap = loop[left];
                    loop[left] = loop[right];
                    loop[right] = swap;
                }
            return true;
        }

        private bool TryTriangulateBoundary(
            IReadOnlyList<int> boundary,
            out List<SurfaceFace> triangles,
            out string message)
        {
            triangles = null;
            message = null;
            if (!TryBoundaryProjection(boundary, out Vector2[] projected, out message))
                return false;
            if (!TryValidateSimplePolygon(projected, out message))
                return false;

            float area = SignedArea(projected);
            if (!IsFiniteModelingValue(area) || Math.Abs(area) <= TypedAreaSquaredEpsilon)
            {
                message = "Surface fill boundary has no usable area.";
                return false;
            }

            float winding = area > 0f ? 1f : -1f;
            var remaining = Enumerable.Range(0, boundary.Count).ToList();
            var result = new List<SurfaceFace>(Math.Max(1, boundary.Count - 2));
            int guard = boundary.Count * boundary.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int candidate = 0; candidate < remaining.Count; candidate++)
                {
                    int previous = remaining[(candidate + remaining.Count - 1) % remaining.Count];
                    int current = remaining[candidate];
                    int next = remaining[(candidate + 1) % remaining.Count];
                    float corner = Cross2(projected[current] - projected[previous], projected[next] - projected[current]);
                    if (corner * winding <= TypedGeometryEpsilon)
                        continue;

                    bool contains = false;
                    for (int testIndex = 0; testIndex < remaining.Count; testIndex++)
                    {
                        int test = remaining[testIndex];
                        if (test == previous || test == current || test == next)
                            continue;
                        if (PointInTriangle(
                                projected[test],
                                projected[previous],
                                projected[current],
                                projected[next],
                                winding))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains)
                        continue;

                    result.Add(new SurfaceFace(
                        boundary[previous],
                        boundary[current],
                        boundary[next]));
                    remaining.RemoveAt(candidate);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    message = "Surface fill boundary is concave, collinear, or self-crossing in a way that cannot be triangulated safely.";
                    return false;
                }
            }

            if (remaining.Count != 3)
            {
                message = "Surface fill boundary could not be triangulated safely.";
                return false;
            }
            result.Add(new SurfaceFace(
                boundary[remaining[0]],
                boundary[remaining[1]],
                boundary[remaining[2]]));
            triangles = result;
            return true;
        }

        private bool TryBoundaryProjection(
            IReadOnlyList<int> boundary,
            out Vector2[] projected,
            out string message)
        {
            projected = null;
            message = null;
            Vector3 normal = Vector3.zero;
            for (int index = 0; index < boundary.Count; index++)
            {
                Vector3 current = _points[boundary[index]];
                Vector3 next = _points[boundary[(index + 1) % boundary.Count]];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }
            if (!TryGetFiniteSquaredMagnitude(normal, out float normalSquared) ||
                normalSquared <= TypedAreaSquaredEpsilon)
            {
                message = "Surface fill boundary has no stable plane.";
                return false;
            }
            normal.Normalize();

            Vector3 origin = _points[boundary[0]];
            const float planarTolerance = 0.0015f;
            for (int index = 1; index < boundary.Count; index++)
            {
                float distance = Math.Abs(Vector3.Dot(_points[boundary[index]] - origin, normal));
                if (!IsFiniteModelingValue(distance) || distance > planarTolerance)
                {
                    message = "Surface fill boundary must be planar within 0.0015 metres.";
                    return false;
                }
            }

            float x = Math.Abs(normal.x);
            float y = Math.Abs(normal.y);
            float z = Math.Abs(normal.z);
            projected = new Vector2[boundary.Count];
            for (int index = 0; index < boundary.Count; index++)
            {
                Vector3 point = _points[boundary[index]];
                projected[index] = x >= y && x >= z
                    ? new Vector2(point.y, point.z)
                    : y >= z
                        ? new Vector2(point.x, point.z)
                        : new Vector2(point.x, point.y);
            }
            return true;
        }

        private static bool TryValidateSimplePolygon(Vector2[] points, out string message)
        {
            message = null;
            for (int first = 0; first < points.Length; first++)
            {
                int firstNext = (first + 1) % points.Length;
                if ((points[firstNext] - points[first]).sqrMagnitude <= TypedEdgeLengthSquaredEpsilon)
                {
                    message = "Surface fill boundary contains a zero-length edge.";
                    return false;
                }

                for (int second = first + 1; second < points.Length; second++)
                {
                    int secondNext = (second + 1) % points.Length;
                    if (first == second ||
                        firstNext == second ||
                        secondNext == first)
                    {
                        continue;
                    }
                    if (SegmentsIntersect(points[first], points[firstNext], points[second], points[secondNext]))
                    {
                        message = "Surface fill boundary crosses itself.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross2(b - a, c - a);
            float abD = Cross2(b - a, d - a);
            float cdA = Cross2(d - c, a - c);
            float cdB = Cross2(d - c, b - c);
            return ((abC > TypedGeometryEpsilon && abD < -TypedGeometryEpsilon) ||
                    (abC < -TypedGeometryEpsilon && abD > TypedGeometryEpsilon)) &&
                   ((cdA > TypedGeometryEpsilon && cdB < -TypedGeometryEpsilon) ||
                    (cdA < -TypedGeometryEpsilon && cdB > TypedGeometryEpsilon));
        }

        private static bool PointInTriangle(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            float winding)
        {
            float ab = Cross2(b - a, point - a) * winding;
            float bc = Cross2(c - b, point - b) * winding;
            float ca = Cross2(a - c, point - c) * winding;
            return ab >= -TypedGeometryEpsilon &&
                   bc >= -TypedGeometryEpsilon &&
                   ca >= -TypedGeometryEpsilon;
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            double area = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                Vector2 current = points[index];
                Vector2 next = points[(index + 1) % points.Count];
                area += (double)current.x * next.y - (double)next.x * current.y;
            }
            return (float)(area * 0.5d);
        }

        private static float Cross2(Vector2 left, Vector2 right) =>
            left.x * right.y - left.y * right.x;

        private List<SurfaceFaceStyle> ModelingFaceStyles()
        {
            var styles = new List<SurfaceFaceStyle>(_faces.Count);
            for (int index = 0; index < _faces.Count; index++)
                styles.Add(FaceStyleAt(index));
            return styles;
        }

        private bool TryValidateModelingOffset(
            Vector3 offset,
            string operation,
            out string message)
        {
            message = null;
            if (!DecorationEditMath.IsFinite(offset) ||
                !TryGetFiniteSquaredMagnitude(offset, out float squared) ||
                squared <= TypedEdgeLengthSquaredEpsilon)
            {
                message = operation + " offset must be finite and non-zero.";
                return false;
            }
            return true;
        }

        private bool TryCommitModelingEdit(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<SurfaceFace> faces,
            IReadOnlyList<SurfaceFaceStyle> styles,
            SurfaceSelectionKind selectionKind,
            int selectedPoint,
            int selectedFace,
            SurfaceEdge selectedEdge,
            bool allowWindingConflicts,
            out string message)
        {
            if (!TryValidateModelingState(
                    points,
                    faces,
                    styles,
                    allowWindingConflicts,
                    out message))
            {
                return false;
            }

            if (selectionKind == SurfaceSelectionKind.Point &&
                (selectedPoint < 0 || selectedPoint >= points.Count))
            {
                message = "Modeling operation produced an invalid point selection.";
                return false;
            }
            if (selectionKind == SurfaceSelectionKind.Face &&
                (selectedFace < 0 || selectedFace >= faces.Count))
            {
                message = "Modeling operation produced an invalid face selection.";
                return false;
            }
            if (selectionKind == SurfaceSelectionKind.Edge &&
                (!selectedEdge.IsValid ||
                 selectedEdge.A >= points.Count ||
                 selectedEdge.B >= points.Count))
            {
                message = "Modeling operation produced an invalid edge selection.";
                return false;
            }

            _points.Clear();
            _points.AddRange(points);
            _faces.Clear();
            _faces.AddRange(faces);
            _faceStyles.Clear();
            _faceStyles.AddRange(styles);
            _manualFaceSelection.Clear();
            _freeTriangleSelection.Clear();
            _bridgeEdgeSelection.Clear();
            SelectionKind = selectionKind;
            SelectedPoint = selectedPoint;
            SelectedFace = selectedFace;
            SelectedEdge = selectedEdge;
            SharedAnchorSelected = false;
            return true;
        }

        private static bool TryValidateModelingState(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<SurfaceFace> faces,
            IReadOnlyList<SurfaceFaceStyle> styles,
            bool allowWindingConflicts,
            out string message)
        {
            message = null;
            if (points == null || faces == null || styles == null || styles.Count != faces.Count)
            {
                message = "Modeling operation produced inconsistent surface data.";
                return false;
            }
            for (int index = 0; index < points.Count; index++)
            {
                if (!DecorationEditMath.IsFinite(points[index]) ||
                    !TryGetFiniteSquaredMagnitude(points[index], out float _))
                {
                    message = "Modeling operation produced a point outside the supported numeric range.";
                    return false;
                }
            }

            var faceKeys = new HashSet<string>(StringComparer.Ordinal);
            var incidence = new Dictionary<string, ModelingEdgeIncidence>(StringComparer.Ordinal);
            for (int index = 0; index < faces.Count; index++)
            {
                SurfaceFace face = faces[index];
                if (face.A < 0 || face.B < 0 || face.C < 0 ||
                    face.A >= points.Count || face.B >= points.Count || face.C >= points.Count ||
                    face.A == face.B || face.B == face.C || face.A == face.C)
                {
                    message = "Modeling operation produced a face with invalid point indexes.";
                    return false;
                }
                if (!TryValidateTypedFaceGeometry(
                        face,
                        points,
                        "Modeled surface face " + (index + 1).ToString(CultureInfo.InvariantCulture),
                        out message))
                {
                    return false;
                }
                if (!faceKeys.Add(ModelingFaceKey(face)))
                {
                    message = "Modeling operation would create duplicate surface faces.";
                    return false;
                }

                foreach (SurfaceEdge edge in face.Edges())
                {
                    string key = ModelingEdgeKey(edge.A, edge.B);
                    if (!incidence.TryGetValue(key, out ModelingEdgeIncidence value))
                    {
                        value = new ModelingEdgeIncidence(edge.A, edge.B);
                        incidence.Add(key, value);
                    }
                    value.Add(face.HasDirectedEdge(value.A, value.B));
                    if (value.Count > 2)
                    {
                        message = "Modeling operation would create a non-manifold edge shared by more than two faces.";
                        return false;
                    }
                }
            }

            if (!allowWindingConflicts && incidence.Values.Any(edge => edge.Count == 2 && edge.SameDirectionCount != 1))
            {
                message = "Modeling operation would create conflicting face winding along a shared edge.";
                return false;
            }
            return true;
        }

        private static Dictionary<string, ModelingEdgeIncidence> BuildModelingEdgeIncidence(
            IReadOnlyList<SurfaceFace> faces)
        {
            var result = new Dictionary<string, ModelingEdgeIncidence>(StringComparer.Ordinal);
            foreach (SurfaceFace face in faces ?? Array.Empty<SurfaceFace>())
            {
                foreach (SurfaceEdge edge in face.Edges())
                {
                    string key = ModelingEdgeKey(edge.A, edge.B);
                    if (!result.TryGetValue(key, out ModelingEdgeIncidence value))
                    {
                        value = new ModelingEdgeIncidence(edge.A, edge.B);
                        result.Add(key, value);
                    }
                    value.Add(face.HasDirectedEdge(value.A, value.B));
                }
            }
            return result;
        }

        private static string ModelingFaceKey(SurfaceFace face)
        {
            int[] values = { face.A, face.B, face.C };
            Array.Sort(values);
            return values[0].ToString(CultureInfo.InvariantCulture) + ":" +
                   values[1].ToString(CultureInfo.InvariantCulture) + ":" +
                   values[2].ToString(CultureInfo.InvariantCulture);
        }

        private static string ModelingEdgeKey(int a, int b)
        {
            int lower = Math.Min(a, b);
            int upper = Math.Max(a, b);
            return lower.ToString(CultureInfo.InvariantCulture) + ":" +
                   upper.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsFiniteModelingValue(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class ModelingEdgeIncidence
        {
            internal ModelingEdgeIncidence(int a, int b)
            {
                A = Math.Min(a, b);
                B = Math.Max(a, b);
            }

            internal int A { get; }

            internal int B { get; }

            internal int Count { get; private set; }

            internal int SameDirectionCount { get; private set; }

            internal void Add(bool sameAsSuppliedDirection)
            {
                Count++;
                if (sameAsSuppliedDirection)
                    SameDirectionCount++;
            }
        }
    }
}
