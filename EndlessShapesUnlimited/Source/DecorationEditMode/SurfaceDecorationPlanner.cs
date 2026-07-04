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

    internal struct SurfaceFaceStyle
    {
        internal SurfaceFaceStyle(int colorIndex)
        {
            ColorIndex = Mathf.Clamp(colorIndex, 0, 31);
        }

        internal int ColorIndex { get; }

        internal bool SameAs(SurfaceFaceStyle other) =>
            ColorIndex == other.ColorIndex;

        internal static SurfaceFaceStyle FromSettings(SurfaceDecorationSettings settings) =>
            new SurfaceFaceStyle(settings?.ColorIndex ?? 0);
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

    internal sealed class SurfaceDecorationSettingsSnapshot
    {
        internal SurfaceDecorationSettingsSnapshot(SurfaceDecorationSettings settings)
        {
            if (settings == null)
                return;

            StructureBlockType = settings.StructureBlockType;
            FaceThickness = settings.FaceThickness;
            ColorIndex = settings.ColorIndex;
            NormalReversal = settings.NormalReversal;
            NearestAnchor = settings.NearestAnchor;
        }

        internal StructureBlockType StructureBlockType { get; }

        internal float FaceThickness { get; }

        internal int ColorIndex { get; }

        internal bool NormalReversal { get; }

        internal bool NearestAnchor { get; }

        internal void Restore(SurfaceDecorationSettings settings)
        {
            if (settings == null)
                return;

            settings.StructureBlockType = StructureBlockType;
            settings.FaceThickness = FaceThickness;
            settings.ColorIndex = ColorIndex;
            settings.NormalReversal = NormalReversal;
            settings.NearestAnchor = NearestAnchor;
        }

        internal bool SameAs(SurfaceDecorationSettingsSnapshot other) =>
            other != null &&
            StructureBlockType == other.StructureBlockType &&
            Math.Abs(FaceThickness - other.FaceThickness) <= 0.0001f &&
            ColorIndex == other.ColorIndex &&
            NormalReversal == other.NormalReversal &&
            NearestAnchor == other.NearestAnchor;
    }

    internal sealed class SurfaceDraftSnapshot
    {
        internal SurfaceDraftSnapshot(
            AllConstruct construct,
            IReadOnlyList<Vector3> points,
            IReadOnlyList<SurfaceFace> faces,
            IReadOnlyList<SurfaceFaceStyle> faceStyles,
            IReadOnlyList<int> manualFaceSelection,
            IReadOnlyList<int> freeTriangleSelection,
            IReadOnlyList<SurfaceEdge> bridgeEdgeSelection,
            SurfaceSelectionKind selectionKind,
            int selectedPoint,
            int selectedFace,
            SurfaceEdge selectedEdge,
            bool hasSharedAnchor,
            Vector3i sharedAnchor,
            bool sharedAnchorSelected,
            SurfaceDecorationSettingsSnapshot settings)
        {
            Construct = construct;
            Points = (points ?? Array.Empty<Vector3>()).ToArray();
            Faces = (faces ?? Array.Empty<SurfaceFace>()).ToArray();
            FaceStyles = (faceStyles ?? Array.Empty<SurfaceFaceStyle>()).ToArray();
            ManualFaceSelection = (manualFaceSelection ?? Array.Empty<int>()).ToArray();
            FreeTriangleSelection = (freeTriangleSelection ?? Array.Empty<int>()).ToArray();
            BridgeEdgeSelection = (bridgeEdgeSelection ?? Array.Empty<SurfaceEdge>()).ToArray();
            SelectionKind = selectionKind;
            SelectedPoint = selectedPoint;
            SelectedFace = selectedFace;
            SelectedEdge = selectedEdge;
            HasSharedAnchor = hasSharedAnchor;
            SharedAnchor = sharedAnchor;
            SharedAnchorSelected = sharedAnchorSelected;
            Settings = settings;
        }

        internal AllConstruct Construct { get; }

        internal Vector3[] Points { get; }

        internal SurfaceFace[] Faces { get; }

        internal SurfaceFaceStyle[] FaceStyles { get; }

        internal int[] ManualFaceSelection { get; }

        internal int[] FreeTriangleSelection { get; }

        internal SurfaceEdge[] BridgeEdgeSelection { get; }

        internal SurfaceSelectionKind SelectionKind { get; }

        internal int SelectedPoint { get; }

        internal int SelectedFace { get; }

        internal SurfaceEdge SelectedEdge { get; }

        internal bool HasSharedAnchor { get; }

        internal Vector3i SharedAnchor { get; }

        internal bool SharedAnchorSelected { get; }

        internal SurfaceDecorationSettingsSnapshot Settings { get; }

        internal bool SameAs(SurfaceDraftSnapshot other)
        {
            if (other == null ||
                !ReferenceEquals(Construct, other.Construct) ||
                SelectionKind != other.SelectionKind ||
                SelectedPoint != other.SelectedPoint ||
                SelectedFace != other.SelectedFace ||
                !SelectedEdge.Matches(other.SelectedEdge.A, other.SelectedEdge.B) ||
                HasSharedAnchor != other.HasSharedAnchor ||
                !SameCell(SharedAnchor, other.SharedAnchor) ||
                SharedAnchorSelected != other.SharedAnchorSelected ||
                !(Settings == null ? other.Settings == null : Settings.SameAs(other.Settings)) ||
                Points.Length != other.Points.Length ||
                Faces.Length != other.Faces.Length ||
                FaceStyles.Length != other.FaceStyles.Length ||
                ManualFaceSelection.Length != other.ManualFaceSelection.Length ||
                FreeTriangleSelection.Length != other.FreeTriangleSelection.Length ||
                BridgeEdgeSelection.Length != other.BridgeEdgeSelection.Length)
            {
                return false;
            }

            for (int index = 0; index < Points.Length; index++)
            {
                if (!SameVector(Points[index], other.Points[index]))
                    return false;
            }

            for (int index = 0; index < Faces.Length; index++)
            {
                if (Faces[index].A != other.Faces[index].A ||
                    Faces[index].B != other.Faces[index].B ||
                    Faces[index].C != other.Faces[index].C)
                {
                    return false;
                }
            }

            for (int index = 0; index < FaceStyles.Length; index++)
            {
                if (!FaceStyles[index].SameAs(other.FaceStyles[index]))
                    return false;
            }

            for (int index = 0; index < ManualFaceSelection.Length; index++)
            {
                if (ManualFaceSelection[index] != other.ManualFaceSelection[index])
                    return false;
            }

            for (int index = 0; index < FreeTriangleSelection.Length; index++)
            {
                if (FreeTriangleSelection[index] != other.FreeTriangleSelection[index])
                    return false;
            }

            for (int index = 0; index < BridgeEdgeSelection.Length; index++)
            {
                if (!BridgeEdgeSelection[index].Matches(other.BridgeEdgeSelection[index].A, other.BridgeEdgeSelection[index].B))
                    return false;
            }

            return true;
        }

        private static bool SameVector(Vector3 left, Vector3 right) =>
            Math.Abs(left.x - right.x) <= 0.0001f &&
            Math.Abs(left.y - right.y) <= 0.0001f &&
            Math.Abs(left.z - right.z) <= 0.0001f;

        private static bool SameCell(Vector3i left, Vector3i right) =>
            left.x == right.x &&
            left.y == right.y &&
            left.z == right.z;
    }

    internal sealed class SurfaceDraft
    {
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<SurfaceFace> _faces = new List<SurfaceFace>();
        private readonly List<SurfaceFaceStyle> _faceStyles = new List<SurfaceFaceStyle>();
        private readonly List<int> _manualFaceSelection = new List<int>(3);
        private readonly List<int> _freeTriangleSelection = new List<int>(3);
        private readonly List<SurfaceEdge> _bridgeEdgeSelection = new List<SurfaceEdge>(2);

        internal AllConstruct Construct { get; private set; }

        internal IReadOnlyList<Vector3> Points => _points;

        internal IReadOnlyList<SurfaceFace> Faces => _faces;

        internal IReadOnlyList<SurfaceFaceStyle> FaceStyles => _faceStyles;

        internal SurfaceDecorationSettings Settings { get; } = new SurfaceDecorationSettings();

        internal SurfaceSelectionKind SelectionKind { get; private set; }

        internal int SelectedPoint { get; private set; } = -1;

        internal int SelectedFace { get; private set; } = -1;

        internal SurfaceEdge SelectedEdge { get; private set; } = new SurfaceEdge(-1, -1);

        internal bool HasSharedAnchor { get; private set; }

        internal Vector3i SharedAnchor { get; private set; }

        internal bool SharedAnchorSelected { get; private set; }

        internal IReadOnlyList<int> ManualFaceSelection => _manualFaceSelection;

        internal IReadOnlyList<SurfaceEdge> BridgeEdgeSelection => _bridgeEdgeSelection;

        internal int ManualFaceSelectionCount => _manualFaceSelection.Count;

        internal int FreeTriangleSelectionCount => _freeTriangleSelection.Count;

        internal bool HasDraft => _points.Count > 0 || _faces.Count > 0 || HasSharedAnchor;

        internal bool HasPlaceableFaces => _faces.Count > 0;

        internal bool HasActiveSelection =>
            SelectionKind != SurfaceSelectionKind.None ||
            _manualFaceSelection.Count > 0 ||
            _bridgeEdgeSelection.Count > 0 ||
            SharedAnchorSelected;

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
                mirrored._faceStyles.Add(FaceStyleAt(index));
            }

            if (HasSharedAnchor)
            {
                mirrored.SharedAnchor = variant.Mirror(SharedAnchor);
                mirrored.HasSharedAnchor = true;
                mirrored.SharedAnchorSelected = SharedAnchorSelected;
            }

            return mirrored;
        }

        internal void Clear()
        {
            Construct = null;
            _points.Clear();
            _faces.Clear();
            _faceStyles.Clear();
            _freeTriangleSelection.Clear();
            HasSharedAnchor = false;
            SharedAnchor = default;
            ClearSelection();
        }

        internal void ClearSelection()
        {
            SelectionKind = SurfaceSelectionKind.None;
            SelectedPoint = -1;
            SelectedFace = -1;
            SelectedEdge = new SurfaceEdge(-1, -1);
            SharedAnchorSelected = false;
            _manualFaceSelection.Clear();
            _bridgeEdgeSelection.Clear();
        }

        internal bool TrySetSharedAnchor(
            AllConstruct construct,
            Vector3i anchor,
            out string message)
        {
            message = null;
            if ((construct != null || Construct != null) &&
                !TryAcceptConstruct(construct, out message))
            {
                return false;
            }

            HasSharedAnchor = true;
            SharedAnchor = anchor;
            ClearSelection();
            SharedAnchorSelected = true;
            message = "Surface same anchor set to " + FormatCell(anchor) + ".";
            return true;
        }

        internal void ClearSharedAnchor()
        {
            HasSharedAnchor = false;
            SharedAnchor = default;
            SharedAnchorSelected = false;
        }

        internal bool TryMoveSharedAnchor(Vector3i anchor, out string message)
        {
            message = null;
            if (!HasSharedAnchor)
            {
                message = "Pick a shared surface anchor before moving it.";
                return false;
            }

            SharedAnchor = anchor;
            SharedAnchorSelected = true;
            return true;
        }

        internal void SelectSharedAnchor()
        {
            if (!HasSharedAnchor)
                return;

            ClearSelection();
            SharedAnchorSelected = true;
        }

        internal SurfaceDraftSnapshot CreateSnapshot() =>
            new SurfaceDraftSnapshot(
                Construct,
                _points,
                _faces,
                _faceStyles,
                _manualFaceSelection,
                _freeTriangleSelection,
                _bridgeEdgeSelection,
                SelectionKind,
                SelectedPoint,
                SelectedFace,
                SelectedEdge,
                HasSharedAnchor,
                SharedAnchor,
                SharedAnchorSelected,
                new SurfaceDecorationSettingsSnapshot(Settings));

        internal void Restore(SurfaceDraftSnapshot snapshot)
        {
            Clear();
            if (snapshot == null)
                return;

            snapshot.Settings?.Restore(Settings);
            Construct = snapshot.Construct;
            _points.AddRange(snapshot.Points ?? Array.Empty<Vector3>());
            _faces.AddRange(snapshot.Faces ?? Array.Empty<SurfaceFace>());
            _faceStyles.AddRange(snapshot.FaceStyles ?? Array.Empty<SurfaceFaceStyle>());
            NormalizeFaceStyleCount();
            _manualFaceSelection.AddRange(snapshot.ManualFaceSelection ?? Array.Empty<int>());
            _freeTriangleSelection.AddRange(snapshot.FreeTriangleSelection ?? Array.Empty<int>());
            _bridgeEdgeSelection.AddRange(snapshot.BridgeEdgeSelection ?? Array.Empty<SurfaceEdge>());
            SelectionKind = snapshot.SelectionKind;
            SelectedPoint = snapshot.SelectedPoint;
            SelectedFace = snapshot.SelectedFace;
            SelectedEdge = snapshot.SelectedEdge;
            HasSharedAnchor = snapshot.HasSharedAnchor;
            SharedAnchor = snapshot.SharedAnchor;
            SharedAnchorSelected = snapshot.SharedAnchorSelected;
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

            return TryAddAcceptedPoint(local, extendSelectedEdge, out message);
        }

        internal bool TryAddPointForTests(
            Vector3 local,
            bool extendSelectedEdge,
            out string message)
        {
            message = null;
            if (!DecorationEditMath.IsFinite(local))
            {
                message = "Surface point must be finite.";
                return false;
            }

            return TryAddAcceptedPoint(local, extendSelectedEdge, out message);
        }

        private bool TryAddAcceptedPoint(
            Vector3 local,
            bool extendSelectedEdge,
            out string message)
        {
            message = null;
            local = DecorationEditMath.Snap(local);
            int index = _points.Count;
            _points.Add(local);
            SelectionKind = SurfaceSelectionKind.Point;
            SelectedPoint = index;
            SelectedFace = -1;
            SharedAnchorSelected = false;
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

            SelectedEdge = new SurfaceEdge(-1, -1);
            _manualFaceSelection.Clear();
            _bridgeEdgeSelection.Clear();
            _freeTriangleSelection.Add(index);
            if (_freeTriangleSelection.Count == 3)
            {
                int a = _freeTriangleSelection[0];
                int b = _freeTriangleSelection[1];
                int c = _freeTriangleSelection[2];
                _freeTriangleSelection.Clear();
                bool baseTriangle = _faces.Count == 0;
                if (!TryAddFace(a, b, c, out message))
                    return false;
                message = baseTriangle
                    ? "Surface base triangle created."
                    : "Surface triangle created from free points.";
            }
            else
            {
                message = "Surface point " +
                          _freeTriangleSelection.Count.ToString(CultureInfo.InvariantCulture) +
                          "/3 placed for the next triangle.";
            }

            return true;
        }

        internal bool TryMovePoint(int index, Vector3 local, out string message) =>
            TryMovePoint(index, local, DecorationEditMath.MoveSnapMetres, out message);

        internal bool TryMovePoint(int index, Vector3 local, float snap, out string message)
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

            _points[index] = DecorationEditMath.Snap(local, snap);
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
            SharedAnchorSelected = false;
            _bridgeEdgeSelection.Clear();
        }

        internal void SelectEdge(int a, int b, bool preserveBridgeSelection = false)
        {
            if (a < 0 || b < 0 || a >= _points.Count || b >= _points.Count || a == b)
                return;
            SelectionKind = SurfaceSelectionKind.Edge;
            SelectedPoint = -1;
            SelectedFace = -1;
            SelectedEdge = new SurfaceEdge(a, b);
            SharedAnchorSelected = false;
            _manualFaceSelection.Clear();
            if (!preserveBridgeSelection)
                _bridgeEdgeSelection.Clear();
        }

        internal void SelectFace(int index)
        {
            if (index < 0 || index >= _faces.Count)
                return;
            SelectionKind = SurfaceSelectionKind.Face;
            SelectedPoint = -1;
            SelectedFace = index;
            SelectedEdge = new SurfaceEdge(-1, -1);
            SharedAnchorSelected = false;
            _manualFaceSelection.Clear();
            _bridgeEdgeSelection.Clear();
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
            {
                _manualFaceSelection.Remove(index);
            }
            else
            {
                if (_manualFaceSelection.Count >= 3)
                {
                    message = "Point selection already has 3/3 points. Right-click to create a face or clear the selection.";
                    return false;
                }

                _manualFaceSelection.Add(index);
            }

            SelectPoint(index);
            message = ManualFaceSelectionMessage();
            return true;
        }

        internal bool TrySelectManualPointEdge(out string message)
        {
            message = null;
            if (_manualFaceSelection.Count != 2)
            {
                message = "Select exactly two surface points before connecting them.";
                return false;
            }

            int a = _manualFaceSelection[0];
            int b = _manualFaceSelection[1];
            if (a < 0 || b < 0 || a >= _points.Count || b >= _points.Count || a == b)
            {
                message = "Selected surface points are no longer valid.";
                return false;
            }

            SelectEdge(a, b);
            message = "Surface point edge selected. Click a new point to create a face.";
            return true;
        }

        internal bool TryCreateFaceFromManualSelection(out string message)
        {
            message = null;
            if (_manualFaceSelection.Count != 3)
            {
                message = "Select exactly three surface points before creating a face.";
                return false;
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

        private string ManualFaceSelectionMessage()
        {
            switch (_manualFaceSelection.Count)
            {
                case 0:
                    return "Surface point selection cleared.";
                case 1:
                    return "Selected 1/3 points. Shift-click more points, then right-click for point actions.";
                case 2:
                    return "Selected 2/3 points. Right-click to connect them or Shift-click a third point.";
                default:
                    return "Selected 3/3 points. Right-click to create a face.";
            }
        }

        internal bool ToggleBridgeEdge(SurfaceEdge edge, out string message)
        {
            message = null;
            if (!edge.IsValid ||
                edge.A >= _points.Count ||
                edge.B >= _points.Count ||
                !FacesContainEdge(edge))
            {
                message = "Select an existing surface edge to bridge.";
                return false;
            }

            int existing = _bridgeEdgeSelection.FindIndex(candidate => candidate.Matches(edge.A, edge.B));
            if (existing >= 0)
            {
                _bridgeEdgeSelection.RemoveAt(existing);
                message = "Bridge edge removed. " +
                          _bridgeEdgeSelection.Count.ToString(CultureInfo.InvariantCulture) +
                          "/2 edges selected.";
                return true;
            }

            if (_bridgeEdgeSelection.Count >= 2)
            {
                message = "Bridge already has 2/2 edges selected. Press Bridge or Shift-click a selected bridge edge to remove it.";
                return false;
            }

            if (_bridgeEdgeSelection.Count == 0 &&
                SelectedEdge.IsValid &&
                !SelectedEdge.Matches(edge.A, edge.B) &&
                IsBridgeEdgeStillValid(SelectedEdge))
            {
                _bridgeEdgeSelection.Add(SelectedEdge);
            }

            _bridgeEdgeSelection.Add(edge);
            _manualFaceSelection.Clear();
            SelectionKind = SurfaceSelectionKind.Edge;
            SelectedPoint = -1;
            SelectedFace = -1;
            SelectedEdge = edge;
            SharedAnchorSelected = false;
            message = "Bridge edge " +
                      _bridgeEdgeSelection.Count.ToString(CultureInfo.InvariantCulture) +
                      "/2 selected.";
            return true;
        }

        internal bool IsBridgeEdgeSelected(int a, int b) =>
            _bridgeEdgeSelection.Any(edge => edge.Matches(a, b));

        internal bool TryBridgeSelectedEdges(out string message)
        {
            message = null;
            if (_bridgeEdgeSelection.Count != 2)
            {
                message = "Select two surface edges with Shift-click before bridging.";
                return false;
            }

            SurfaceEdge first = _bridgeEdgeSelection[0];
            SurfaceEdge second = _bridgeEdgeSelection[1];
            if (!IsBridgeEdgeStillValid(first) || !IsBridgeEdgeStillValid(second))
            {
                message = "Bridge edges are no longer valid.";
                return false;
            }

            List<List<SurfaceFace>> candidateSets = BuildBridgeCandidateSets(first, second, out message);
            if (candidateSets == null || candidateSets.Count == 0)
                return false;

            string lastMessage = message;
            for (int index = 0; index < candidateSets.Count; index++)
            {
                if (!TryAddBridgeCandidateSet(candidateSets[index], out lastMessage))
                    continue;

                _bridgeEdgeSelection.Clear();
                message = candidateSets[index].Count == 1
                    ? "Bridge created one surface face."
                    : "Bridge created two surface faces.";
                return true;
            }

            message = lastMessage ?? "Bridge faces could not be created.";
            return false;
        }

        internal bool TryDeleteSelection(out string message)
        {
            message = null;
            if (SharedAnchorSelected && HasSharedAnchor)
            {
                ClearSharedAnchor();
                message = "Surface shared anchor cleared.";
                return true;
            }

            if (SelectionKind == SurfaceSelectionKind.Face && SelectedFace >= 0 && SelectedFace < _faces.Count)
            {
                _faces.RemoveAt(SelectedFace);
                RemoveFaceStyleAt(SelectedFace);
                ClearSelection();
                message = "Surface face deleted.";
                return true;
            }

            if (SelectionKind == SurfaceSelectionKind.Point && SelectedPoint >= 0 && SelectedPoint < _points.Count)
            {
                int removed = SelectedPoint;
                for (int index = _faces.Count - 1; index >= 0; index--)
                {
                    if (!_faces[index].Contains(removed))
                        continue;

                    _faces.RemoveAt(index);
                    RemoveFaceStyleAt(index);
                }

                for (int index = 0; index < _faces.Count; index++)
                {
                    SurfaceFace face = _faces[index];
                    _faces[index] = new SurfaceFace(
                        RemapAfterPointDelete(face.A, removed),
                        RemapAfterPointDelete(face.B, removed),
                        RemapAfterPointDelete(face.C, removed));
                }

                _points.RemoveAt(removed);
                RemapFreeTriangleSelectionAfterPointDelete(removed);
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
            _faceStyles.Add(SurfaceFaceStyle.FromSettings(Settings));
            SelectFace(_faces.Count - 1);
            return true;
        }

        internal SurfaceFaceStyle FaceStyleAt(int index) =>
            index >= 0 && index < _faceStyles.Count
                ? _faceStyles[index]
                : SurfaceFaceStyle.FromSettings(Settings);

        internal bool TrySetFaceColor(int index, int color, out string message)
        {
            message = null;
            if (index < 0 || index >= _faces.Count)
            {
                message = "Select a surface face before assigning a face color.";
                return false;
            }

            NormalizeFaceStyleCount();
            _faceStyles[index] = new SurfaceFaceStyle(color);
            SelectFace(index);
            message = "Surface face color set to " + Mathf.Clamp(color, 0, 31).ToString(CultureInfo.InvariantCulture) + ".";
            return true;
        }

        internal void SetAllFaceColors(int color)
        {
            NormalizeFaceStyleCount();
            var style = new SurfaceFaceStyle(color);
            for (int index = 0; index < _faceStyles.Count; index++)
                _faceStyles[index] = style;
        }

        private bool TryOrientFace(SurfaceFace candidate, out SurfaceFace oriented, out string message) =>
            TryOrientFace(candidate, Array.Empty<SurfaceFace>(), out oriented, out message);

        private bool TryOrientFace(
            SurfaceFace candidate,
            IReadOnlyList<SurfaceFace> pendingFaces,
            out SurfaceFace oriented,
            out string message)
        {
            oriented = candidate;
            message = null;
            if (_faces.Count == 0)
            {
                if (pendingFaces == null || pendingFaces.Count == 0)
                    return true;
            }

            int sameDirectionEdges = 0;
            int oppositeDirectionEdges = 0;
            foreach (SurfaceEdge edge in candidate.Edges())
            {
                foreach (SurfaceFace existing in _faces)
                {
                    CountSharedEdgeDirection(existing, edge, ref sameDirectionEdges, ref oppositeDirectionEdges);
                }

                if (pendingFaces == null)
                    continue;

                foreach (SurfaceFace pending in pendingFaces)
                {
                    CountSharedEdgeDirection(pending, edge, ref sameDirectionEdges, ref oppositeDirectionEdges);
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

        private static void CountSharedEdgeDirection(
            SurfaceFace existing,
            SurfaceEdge edge,
            ref int sameDirectionEdges,
            ref int oppositeDirectionEdges)
        {
            if (!existing.ContainsEdge(edge.A, edge.B))
                return;

            if (existing.HasDirectedEdge(edge.A, edge.B))
                sameDirectionEdges++;
            else
                oppositeDirectionEdges++;
        }

        private bool FacesContainEdge(SurfaceEdge edge) =>
            _faces.Any(face => face.ContainsEdge(edge.A, edge.B));

        private bool IsBridgeEdgeStillValid(SurfaceEdge edge) =>
            edge.IsValid &&
            edge.A < _points.Count &&
            edge.B < _points.Count &&
            FacesContainEdge(edge);

        private bool TryAddBridgeCandidateSet(
            IReadOnlyList<SurfaceFace> candidates,
            out string message)
        {
            message = null;
            var orientedFaces = new List<SurfaceFace>(candidates.Count);

            for (int index = 0; index < candidates.Count; index++)
            {
                SurfaceFace candidate = candidates[index];
                if (!ArePointIndexesValid(candidate.A, candidate.B, candidate.C))
                {
                    message = "Surface face needs three different existing points.";
                    return false;
                }

                if (_faces.Any(face => SameFace(face, candidate.A, candidate.B, candidate.C)) ||
                    orientedFaces.Any(face => SameFace(face, candidate.A, candidate.B, candidate.C)))
                {
                    message = "Surface face already exists.";
                    return false;
                }

                if (!TryOrientBridgeFace(candidate, orientedFaces, out SurfaceFace oriented, out message))
                    return false;

                orientedFaces.Add(oriented);
            }

            for (int index = 0; index < orientedFaces.Count; index++)
            {
                _faces.Add(orientedFaces[index]);
                _faceStyles.Add(SurfaceFaceStyle.FromSettings(Settings));
            }

            SelectFace(_faces.Count - 1);
            return true;
        }

        private void RemoveFaceStyleAt(int index)
        {
            if (index >= 0 && index < _faceStyles.Count)
                _faceStyles.RemoveAt(index);
        }

        private void NormalizeFaceStyleCount()
        {
            while (_faceStyles.Count < _faces.Count)
                _faceStyles.Add(SurfaceFaceStyle.FromSettings(Settings));
            while (_faceStyles.Count > _faces.Count)
                _faceStyles.RemoveAt(_faceStyles.Count - 1);
        }

        private bool TryOrientBridgeFace(
            SurfaceFace candidate,
            IReadOnlyList<SurfaceFace> pendingFaces,
            out SurfaceFace oriented,
            out string message)
        {
            oriented = candidate;
            message = null;
            if (!TryGetFaceNormal(candidate, out Vector3 _))
            {
                message = "Surface bridge face has zero area.";
                return false;
            }

            return TryOrientFace(candidate, pendingFaces, out oriented, out message);
        }

        private List<List<SurfaceFace>> BuildBridgeCandidateSets(
            SurfaceEdge first,
            SurfaceEdge second,
            out string message)
        {
            message = null;
            int[] unique = { first.A, first.B, second.A, second.B };
            var points = unique.Distinct().ToList();
            if (points.Count < 3)
            {
                message = "Bridge needs two different edges.";
                return null;
            }

            if (points.Count == 3)
            {
                return PermuteFace(points[0], points[1], points[2])
                    .Select(face => new List<SurfaceFace> { face })
                    .ToList();
            }

            if (points.Count != 4)
            {
                message = "Bridge supports only two triangle or quad-like edges.";
                return null;
            }

            SurfaceFace firstA = new SurfaceFace(first.A, first.B, second.A);
            SurfaceFace firstB = new SurfaceFace(first.B, second.B, second.A);
            SurfaceFace secondA = new SurfaceFace(first.A, first.B, second.B);
            SurfaceFace secondB = new SurfaceFace(first.B, second.A, second.B);
            float firstPairing =
                Vector3.Distance(_points[first.A], _points[second.A]) +
                Vector3.Distance(_points[first.B], _points[second.B]);
            float secondPairing =
                Vector3.Distance(_points[first.A], _points[second.B]) +
                Vector3.Distance(_points[first.B], _points[second.A]);

            List<List<SurfaceFace>> preferred = firstPairing <= secondPairing
                ? PermuteFacePair(firstA, firstB)
                : PermuteFacePair(secondA, secondB);
            List<List<SurfaceFace>> alternate = firstPairing <= secondPairing
                ? PermuteFacePair(secondA, secondB)
                : PermuteFacePair(firstA, firstB);
            preferred.AddRange(alternate);
            return preferred;
        }

        private static List<List<SurfaceFace>> PermuteFacePair(SurfaceFace first, SurfaceFace second)
        {
            var result = new List<List<SurfaceFace>>();
            foreach (SurfaceFace left in PermuteFace(first.A, first.B, first.C))
            {
                foreach (SurfaceFace right in PermuteFace(second.A, second.B, second.C))
                {
                    result.Add(new List<SurfaceFace> { left, right });
                }
            }

            return result;
        }

        private static IEnumerable<SurfaceFace> PermuteFace(int a, int b, int c)
        {
            yield return new SurfaceFace(a, b, c);
            yield return new SurfaceFace(a, c, b);
            yield return new SurfaceFace(b, a, c);
            yield return new SurfaceFace(b, c, a);
            yield return new SurfaceFace(c, a, b);
            yield return new SurfaceFace(c, b, a);
        }

        private void RemapFreeTriangleSelectionAfterPointDelete(int removed)
        {
            for (int index = _freeTriangleSelection.Count - 1; index >= 0; index--)
            {
                int point = _freeTriangleSelection[index];
                if (point == removed)
                {
                    _freeTriangleSelection.RemoveAt(index);
                }
                else if (point > removed)
                {
                    _freeTriangleSelection[index] = point - 1;
                }
            }
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

        private static string FormatCell(Vector3i value) =>
            value.x.ToString(CultureInfo.InvariantCulture) + "," +
            value.y.ToString(CultureInfo.InvariantCulture) + "," +
            value.z.ToString(CultureInfo.InvariantCulture);

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
            StructureBlockType structureBlockType,
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
            StructureBlockType = structureBlockType;
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

        internal StructureBlockType StructureBlockType { get; }

        internal Vector3 ThicknessAxis { get; }

        internal Vector3 TransformThicknessAxis { get; }

        internal float TransformPlaneDistance { get; }
    }

    internal abstract class DecorationAnchorResolver
    {
        internal abstract bool TryResolveAnchor(Vector3 localCenter, out Vector3i anchor);
    }

    internal abstract class ISurfaceAnchorResolver : DecorationAnchorResolver
    {
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
            var anchorContext = new SurfaceAnchorContext(
                draft.Settings.NearestAnchor,
                anchorResolver,
                draft.HasSharedAnchor,
                draft.SharedAnchor);
            try
            {
                for (int faceIndex = 0; faceIndex < draft.Faces.Count; faceIndex++)
                {
                    SurfaceFace face = draft.Faces[faceIndex];
                    AddFacePlacements(
                        draft,
                        face,
                        faceIndex,
                        anchorContext,
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
            if (draft.HasSharedAnchor)
            {
                builder.Append("anchor:")
                    .Append(CellKey(draft.SharedAnchor))
                    .Append('|');
            }

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
            VectorKey(placement.Positioning) + "|" +
            VectorKey(placement.Scaling) + "|" +
            VectorKey(placement.Orientation) + "|" +
            placement.Color.ToString(CultureInfo.InvariantCulture);

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
            SurfaceAnchorContext anchorContext,
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
            SurfaceFaceStyle faceStyle = draft.FaceStyleAt(faceIndex);
            ValidateSurfaceFace(vertices, faceIndex);
            Vector3 parentNormal = IntendedFaceNormal(vertices, draft.Settings.NormalReversal);
            if (parentNormal == Vector3.zero)
                throw SurfaceGeometryError(faceIndex, "has no valid normal");

            List<PolygonData> polygons = BuildSurfacePolygons(vertices, faceIndex, parentNormal);

            foreach (PolygonData polygon in polygons)
            {
                Vector3 thicknessAxis = DecorationThicknessAxis(polygon, parentNormal);
                bool converterNormalReversal = ShouldReversePolygonForConverter(
                    polygon,
                    parentNormal,
                    draft.Settings.NormalReversal);
                var data = new MimicAndDecorationCommonData();
                MADCD_PolygonInput.Start(
                    data,
                    polygon,
                    new PolygonDecorationSettings(
                        converterNormalReversal,
                        draft.Settings.FaceThickness,
                        draft.Settings.FaceThickness,
                        draft.Settings.StructureBlockType),
                    Mathf.Clamp(faceStyle.ColorIndex, 0, 31));

                Guid meshGuid;
                Vector3 center;
                Vector3 scaling;
                Vector3 orientation;
                int color;
                if (!data.TryGetStandaloneData(out meshGuid, out center, out scaling, out orientation, out color))
                    throw new InvalidOperationException("Surface polygon conversion did not produce standalone decoration data.");

                orientation = CorrectOrientationForIntendedNormal(
                    polygon,
                    orientation,
                    parentNormal,
                    faceIndex);

                Vector3i anchor;
                string anchorMessage = "Surface anchor resolver is unavailable.";
                if (anchorContext == null ||
                    !anchorContext.TryResolveAnchor(center, faceIndex, out anchor, out anchorMessage))
                {
                    throw new InvalidOperationException(anchorMessage);
                }

                Vector3 positioning = RoundPlacementPosition(center - ToVector3(anchor));
                if (!DecorationEditMath.IsWithinPositionLimit(positioning))
                {
                    throw new InvalidOperationException(anchorContext.NearestAnchor
                        ? "Surface face " +
                          (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          " generated a decoration outside FTD's +/-10 positioning limit."
                        : "Surface same-anchor mode would exceed FTD's +/-10 positioning limit on face " +
                          (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                          ".");
                }

                Vector3 transformThicknessAxis = DecorationTransformThicknessAxis(polygon, orientation);
                transformThicknessAxis = AlignAxisWithIntendedNormal(
                    transformThicknessAxis,
                    parentNormal,
                    faceIndex,
                    "final decoration transform");
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
                    draft.Settings.StructureBlockType,
                    thicknessAxis,
                    transformThicknessAxis,
                    transformPlaneDistance));
            }
        }

        private static bool ShouldReversePolygonForConverter(
            PolygonData polygon,
            Vector3 intendedNormal,
            bool normalReversal)
        {
            if (!normalReversal ||
                polygon == null ||
                intendedNormal == Vector3.zero ||
                polygon.NormalVector == Vector3.zero)
            {
                return normalReversal;
            }

            return Vector3.Dot(polygon.NormalVector, intendedNormal) < 0f;
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

        private static Vector3 IntendedFaceNormal(
            IReadOnlyList<Vector3> vertices,
            bool normalReversal)
        {
            Vector3 axis = Vector3.Cross(
                vertices[1] - vertices[0],
                vertices[2] - vertices[0]);
            if (!DecorationEditMath.IsFinite(axis) ||
                axis.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                return Vector3.zero;
            }

            axis.Normalize();
            return normalReversal ? -axis : axis;
        }

        private static List<Vector3> AlignChildVerticesToParentNormal(
            IReadOnlyList<Vector3> vertices,
            Vector3 parentNormal)
        {
            var child = new List<Vector3>(vertices);
            Vector3 childNormal = IntendedFaceNormal(child, normalReversal: false);
            if (childNormal != Vector3.zero &&
                parentNormal != Vector3.zero &&
                Vector3.Dot(childNormal, parentNormal) < 0f)
            {
                Vector3 swap = child[1];
                child[1] = child[2];
                child[2] = swap;
            }

            return child;
        }

        private static List<PolygonData> BuildSurfacePolygons(
            List<Vector3> vertices,
            int faceIndex,
            Vector3 parentNormal)
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
                TryBuildCoPlanarScalenePolygons(vertices, sourceLine, parentNormal, out List<PolygonData> scalenePolygons))
            {
                return scalenePolygons;
            }

            return polygons;
        }

        private static bool TryBuildCoPlanarScalenePolygons(
            IReadOnlyList<Vector3> vertices,
            int sourceLine,
            Vector3 parentNormal,
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
                    AlignChildVerticesToParentNormal(
                        new[] { apex, start, foot },
                        parentNormal),
                    sourceLine),
                CreateRightTrianglePolygon(
                    AlignChildVerticesToParentNormal(
                        new[] { end, apex, foot },
                        parentNormal),
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

        private static Vector3 DecorationThicknessAxis(PolygonData polygon, Vector3 intendedNormal)
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
                    axis = polygon.NormalVector;
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
            if (intendedNormal != Vector3.zero && Vector3.Dot(axis, intendedNormal) < 0f)
                axis = -axis;
            return axis;
        }

        private static Vector3 AlignAxisWithIntendedNormal(
            Vector3 axis,
            Vector3 intendedNormal,
            int faceIndex,
            string context)
        {
            if (!DecorationEditMath.IsFinite(axis) ||
                axis.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                throw SurfaceGeometryError(faceIndex, context + " has no valid thickness axis");
            }

            axis.Normalize();
            if (intendedNormal != Vector3.zero &&
                Vector3.Dot(axis, intendedNormal) < 0.999f)
            {
                throw SurfaceGeometryError(
                    faceIndex,
                    context +
                    " normal did not match the source face normal (dot " +
                    Vector3.Dot(axis, intendedNormal).ToString("0.####", CultureInfo.InvariantCulture) +
                    ")");
            }

            return axis;
        }

        private static Vector3 DecorationTransformThicknessAxis(PolygonData polygon, Vector3 orientation)
        {
            Vector3 localAxis = DecorationTransformLocalThicknessAxis(polygon);
            Vector3 axis = RotateEuler(orientation, localAxis);
            if (!DecorationEditMath.IsFinite(axis) ||
                axis.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
            {
                return Vector3.zero;
            }

            axis.Normalize();
            return axis;
        }

        private static Vector3 CorrectOrientationForIntendedNormal(
            PolygonData polygon,
            Vector3 orientation,
            Vector3 intendedNormal,
            int faceIndex)
        {
            Vector3 axis = DecorationTransformThicknessAxis(polygon, orientation);
            float dot = axis == Vector3.zero ? 0f : Vector3.Dot(axis, intendedNormal);
            if (axis != Vector3.zero && dot >= 0.999f)
                return orientation;

            if (axis == Vector3.zero || dot >= 0f)
                return orientation;

            Vector3 localAxis = DecorationTransformLocalThicknessAxis(polygon);
            Vector3 forward = RotateEuler(orientation, Vector3.forward);
            Vector3 up = RotateEuler(orientation, Vector3.up);
            if (localAxis == Vector3.forward)
                forward = -forward;
            else
                up = -up;

            return ManagedLookRotationEuler(forward, up, faceIndex + 1);
        }

        private static Vector3 DecorationTransformLocalThicknessAxis(PolygonData polygon)
        {
            switch (polygon.PolyType)
            {
                case PolygonType.RightTriangle:
                case PolygonType.OtherTriangle_F:
                case PolygonType.OtherTriangle_B:
                case PolygonType.Rectangle:
                    return Vector3.right;
                case PolygonType.IsoscelesTriangle:
                    return Vector3.up;
                case PolygonType.Ellipse:
                    return Vector3.forward;
                default:
                    return Vector3.forward;
            }
        }

        private static Vector3 RotateEuler(Vector3 degrees, Vector3 vector)
        {
            try
            {
                return Quaternion.Euler(degrees) * vector;
            }
            catch (Exception exception) when (IsUnityECallUnavailable(exception))
            {
                return ManagedRotateUnityEuler(degrees, vector);
            }
        }

        private static Vector3 ManagedRotateUnityEuler(Vector3 degrees, Vector3 vector)
        {
            double x = degrees.x * Mathf.Deg2Rad;
            double y = degrees.y * Mathf.Deg2Rad;
            double z = degrees.z * Mathf.Deg2Rad;
            double sx = Math.Sin(x);
            double cx = Math.Cos(x);
            double sy = Math.Sin(y);
            double cy = Math.Cos(y);
            double sz = Math.Sin(z);
            double cz = Math.Cos(z);

            // Unity's Quaternion.Euler applies rotations in Z, then X, then Y order.
            double m00 = cy * cz + sy * sx * sz;
            double m01 = -cy * sz + sy * sx * cz;
            double m02 = sy * cx;
            double m10 = cx * sz;
            double m11 = cx * cz;
            double m12 = -sx;
            double m20 = -sy * cz + cy * sx * sz;
            double m21 = sy * sz + cy * sx * cz;
            double m22 = cy * cx;

            return new Vector3(
                (float)(m00 * vector.x + m01 * vector.y + m02 * vector.z),
                (float)(m10 * vector.x + m11 * vector.y + m12 * vector.z),
                (float)(m20 * vector.x + m21 * vector.y + m22 * vector.z));
        }

        private static Vector3 ManagedLookRotationEuler(Vector3 forward, Vector3 upwards, int sourceLine)
        {
            Vector3 z = NormalizeVector(forward, sourceLine, "forward vector");
            Vector3 x = Cross(upwards, z);
            if (x.sqrMagnitude <= SurfaceGeometryEpsilon * SurfaceGeometryEpsilon)
                throw SurfaceGeometryError(sourceLine - 1, "generated decoration up vector is parallel to forward");
            x = NormalizeVector(x, sourceLine, "right vector");
            Vector3 y = Cross(z, x);

            double m00 = x.x;
            double m01 = y.x;
            double m02 = z.x;
            double m10 = x.y;
            double m11 = y.y;
            double m12 = z.y;
            double m20 = x.z;
            double m21 = y.z;
            double m22 = z.z;

            return UnityEulerFromRotationMatrix(
                m00,
                m01,
                m02,
                m10,
                m11,
                m12,
                m20,
                m21,
                m22);
        }

        private static Vector3 UnityEulerFromRotationMatrix(
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
            const double epsilon = 0.000001d;
            double xRadians = Math.Asin(Math.Max(-1d, Math.Min(1d, -m12)));
            double cosX = Math.Cos(xRadians);
            double yRadians;
            double zRadians;
            if (Math.Abs(cosX) > epsilon)
            {
                zRadians = Math.Atan2(m10, m11);
                yRadians = Math.Atan2(m02, m22);
            }
            else
            {
                zRadians = 0d;
                yRadians = m12 < 0d
                    ? Math.Atan2(m01, m00)
                    : Math.Atan2(-m01, m00);
            }

            return new Vector3(
                NormalizeDegrees((float)(xRadians * Mathf.Rad2Deg)),
                NormalizeDegrees((float)(yRadians * Mathf.Rad2Deg)),
                NormalizeDegrees((float)(zRadians * Mathf.Rad2Deg)));
        }

        private static Vector3 NormalizeVector(Vector3 vector, int sourceLine, string name)
        {
            float magnitude = vector.magnitude;
            if (!IsFinite(magnitude) || magnitude <= SurfaceGeometryEpsilon)
                throw SurfaceGeometryError(sourceLine - 1, "generated decoration " + name + " has zero length");
            return vector / magnitude;
        }

        private static Vector3 Cross(Vector3 left, Vector3 right) =>
            new Vector3(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);

        private static float NormalizeDegrees(float value)
        {
            if (!IsFinite(value))
                return value;
            value %= 360f;
            if (value < 0f)
                value += 360f;
            return value;
        }

        private static bool IsUnityECallUnavailable(Exception exception) =>
            exception?.Message?.IndexOf("ECall", StringComparison.OrdinalIgnoreCase) >= 0;

        private static InvalidOperationException SurfaceGeometryError(int faceIndex, string message) =>
            new InvalidOperationException(
                "Surface face " +
                (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " " +
                message +
                ".");

        private sealed class SurfaceAnchorContext
        {
            private readonly ISurfaceAnchorResolver _resolver;
            private readonly bool _hasExplicitSharedAnchor;
            private readonly Vector3i _explicitSharedAnchor;
            private bool _hasSharedAnchor;
            private Vector3i _sharedAnchor;

            internal SurfaceAnchorContext(
                bool nearestAnchor,
                ISurfaceAnchorResolver resolver,
                bool hasExplicitSharedAnchor,
                Vector3i explicitSharedAnchor)
            {
                NearestAnchor = nearestAnchor;
                _resolver = resolver;
                _hasExplicitSharedAnchor = hasExplicitSharedAnchor;
                _explicitSharedAnchor = explicitSharedAnchor;
            }

            internal bool NearestAnchor { get; }

            internal bool TryResolveAnchor(
                Vector3 center,
                int faceIndex,
                out Vector3i anchor,
                out string message)
            {
                anchor = new Vector3i(0, 0, 0);
                message = null;
                if (_resolver == null)
                {
                    message = "Surface anchor resolver is unavailable.";
                    return false;
                }

                if (NearestAnchor)
                {
                    if (_resolver.TryResolveAnchor(center, out anchor))
                        return true;

                    message = "Surface face " +
                              (faceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                              " has no valid nearest anchor within +/-10m.";
                    return false;
                }

                if (!_hasSharedAnchor)
                {
                    if (_hasExplicitSharedAnchor)
                    {
                        if (!TryValidateExplicitSharedAnchor(out message))
                            return false;

                        _sharedAnchor = _explicitSharedAnchor;
                    }
                    else if (!_resolver.TryResolveAnchor(center, out _sharedAnchor))
                    {
                        message = "Surface same-anchor mode found no valid anchor within +/-10m.";
                        return false;
                    }

                    _hasSharedAnchor = true;
                }

                anchor = _sharedAnchor;
                return true;
            }

            private bool TryValidateExplicitSharedAnchor(out string message)
            {
                message = null;
                if (_resolver.TryResolveAnchor(ToVector3(_explicitSharedAnchor), out Vector3i resolved) &&
                    SameCell(resolved, _explicitSharedAnchor))
                {
                    return true;
                }

                message = "Surface shared anchor " + CellLabel(_explicitSharedAnchor) + " is not a valid craft block.";
                return false;
            }

            private static bool SameCell(Vector3i left, Vector3i right) =>
                left.x == right.x &&
                left.y == right.y &&
                left.z == right.z;

            private static string CellLabel(Vector3i value) =>
                value.x.ToString(CultureInfo.InvariantCulture) + "," +
                value.y.ToString(CultureInfo.InvariantCulture) + "," +
                value.z.ToString(CultureInfo.InvariantCulture);
        }

        private static Vector3 ToVector3(Vector3i value) =>
            new Vector3(value.x, value.y, value.z);
    }
}
