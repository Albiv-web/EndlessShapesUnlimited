using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EndlessShapes2.Polygon
{
    public static class PolygonDataControl
    {
        private const float GeometryEpsilon = 0.000001f;

        private static readonly float[] CircleComponents =
            { 0f, 0.382683f, 0.707107f, 0.923880f, 1f };

        private static readonly Vector3[] CircleValues =
        {
            new Vector3(0, CircleComponents[0], CircleComponents[4]),
            new Vector3(0, CircleComponents[1], CircleComponents[3]),
            new Vector3(0, CircleComponents[2], CircleComponents[2]),
            new Vector3(0, CircleComponents[3], CircleComponents[1]),
            new Vector3(0, CircleComponents[4], -CircleComponents[0]),
            new Vector3(0, CircleComponents[3], -CircleComponents[1]),
            new Vector3(0, CircleComponents[2], -CircleComponents[2]),
            new Vector3(0, CircleComponents[1], -CircleComponents[3]),
            new Vector3(0, -CircleComponents[0], -CircleComponents[4]),
            new Vector3(0, -CircleComponents[1], -CircleComponents[3]),
            new Vector3(0, -CircleComponents[2], -CircleComponents[2]),
            new Vector3(0, -CircleComponents[3], -CircleComponents[1]),
            new Vector3(0, -CircleComponents[4], CircleComponents[0]),
            new Vector3(0, -CircleComponents[3], CircleComponents[1]),
            new Vector3(0, -CircleComponents[2], CircleComponents[2]),
            new Vector3(0, -CircleComponents[1], CircleComponents[3])
        };

        public static float AllowableError_length { get; set; } = 0.001f;

        [Obsolete("UV availability is determined independently per face; this compatibility property has no effect.")]
        public static bool UV_Load
        {
            get => true;
            set { }
        }

        public static void PolygonClassify(
            List<PolygonData> polygonDataList,
            List<int[][]> faceDatas,
            List<int[]> lineDatas,
            List<Vector3> vertices,
            List<Vector2> uvs = null)
        {
            PolygonClassify(
                polygonDataList,
                faceDatas,
                lineDatas,
                vertices,
                uvs,
                null,
                null,
                int.MaxValue);
        }

        internal static void PolygonClassify(
            List<PolygonData> polygonDataList,
            IReadOnlyList<int[][]> faceDatas,
            IReadOnlyList<int[]> lineDatas,
            List<Vector3> vertices,
            List<Vector2> uvs,
            IReadOnlyList<int> faceSourceLines,
            IReadOnlyList<int> lineSourceLines,
            int maximumDecorations)
        {
            if (polygonDataList == null)
                throw new ArgumentNullException(nameof(polygonDataList));
            if (faceDatas == null)
                throw new ArgumentNullException(nameof(faceDatas));
            if (lineDatas == null)
                throw new ArgumentNullException(nameof(lineDatas));
            if (vertices == null)
                throw new ArgumentNullException(nameof(vertices));
            if (maximumDecorations < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumDecorations));

            var comparer = Comparer<int>.Create((left, right) => right.CompareTo(left));
            var workBySize = new SortedDictionary<int, FaceBucket>(comparer);
            int pendingFaces = 0;

            for (int index = 0; index < faceDatas.Count; index++)
            {
                int sourceLine = SourceLine(faceSourceLines, index);
                EnqueueFace(
                    workBySize,
                    new FaceWork(faceDatas[index], sourceLine),
                    polygonDataList.Count,
                    ref pendingFaces,
                    maximumDecorations);
            }

            while (pendingFaces > 0)
            {
                KeyValuePair<int, FaceBucket> bucket = workBySize.First();
                FaceWork work = bucket.Value.Dequeue();
                pendingFaces--;
                if (!bucket.Value.HasItems)
                    workBySize.Remove(bucket.Key);

                ValidateFaceGeometry(work.Face, vertices, work.SourceLine);
                switch (work.Face.Length)
                {
                    case 3:
                        TriangleClassify(
                            polygonDataList,
                            work.Face,
                            vertices,
                            uvs,
                            work.SourceLine,
                            maximumDecorations);
                        break;
                    case 4:
                        if (!RectangleClassify(
                                polygonDataList,
                                work.Face,
                                vertices,
                                uvs,
                                work.SourceLine,
                                maximumDecorations))
                        {
                            EnqueueFan(
                                workBySize,
                                work,
                                polygonDataList.Count,
                                ref pendingFaces,
                                maximumDecorations);
                        }
                        break;
                    case 16:
                        if (!EllipseClassify(
                                polygonDataList,
                                work.Face,
                                vertices,
                                uvs,
                                work.SourceLine,
                                maximumDecorations))
                        {
                            EnqueueFan(
                                workBySize,
                                work,
                                polygonDataList.Count,
                                ref pendingFaces,
                                maximumDecorations);
                        }
                        break;
                    default:
                        EnqueueFan(
                            workBySize,
                            work,
                            polygonDataList.Count,
                            ref pendingFaces,
                            maximumDecorations);
                        break;
                }
            }

            for (int lineIndex = 0; lineIndex < lineDatas.Count; lineIndex++)
            {
                int[] line = lineDatas[lineIndex];
                int sourceLine = SourceLine(lineSourceLines, lineIndex);
                for (int index = 1; index < line.Length; index++)
                {
                    int[] segment = { line[index - 1], line[index] };
                    ValidateLineGeometry(segment, vertices, sourceLine);
                    AddChecked(
                        polygonDataList,
                        new PolygonData(PolygonType.Line, segment, vertices, Vector2.zero, sourceLine),
                        maximumDecorations,
                        sourceLine);
                }
            }
        }

        public static void TriangleClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs)
        {
            TriangleClassify(
                polygonDataList,
                faceData,
                vertices,
                uvs,
                0,
                int.MaxValue);
        }

        private static void TriangleClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs,
            int sourceLine,
            int maximumDecorations)
        {
            Vector2 uvMidpoint = UvMidpoint(faceData, uvs, sourceLine);
            int[] vertexIndexes = faceData.Select(point => point[0]).ToArray();
            SideData[] sides = GenerateSides(vertexIndexes, vertices);
            SideData[] sorted = sides.OrderByDescending(side => side.Length).ToArray();
            int[] next = { 1, 2, 0 };

            float angleError = Mathf.Abs(Vector3.Angle(sorted[1].SideVector, sorted[2].SideVector) - 90f);
            bool right = sorted[0].Length * Mathf.Sin(angleError * Mathf.Deg2Rad) < AllowableError_length;
            if (right)
            {
                int start = sorted[0].SideIndex;
                int[] reordered =
                    { vertexIndexes[start], vertexIndexes[next[start]], vertexIndexes[next[next[start]]] };
                AddChecked(
                    polygonDataList,
                    new PolygonData(PolygonType.RightTriangle, reordered, vertices, uvMidpoint, sourceLine),
                    maximumDecorations,
                    sourceLine);
                return;
            }

            bool shortestPairEqual = Mathf.Abs(sorted[1].Length - sorted[2].Length) < AllowableError_length;
            bool longestPairEqual = Mathf.Abs(sorted[0].Length - sorted[1].Length) < AllowableError_length;
            int startingSide = sorted[shortestPairEqual ? 0 : longestPairEqual ? 2 : 0].SideIndex;
            int[] indexes =
            {
                vertexIndexes[startingSide],
                vertexIndexes[next[startingSide]],
                vertexIndexes[next[next[startingSide]]]
            };

            if (shortestPairEqual || longestPairEqual)
            {
                AddChecked(
                    polygonDataList,
                    new PolygonData(PolygonType.IsoscelesTriangle, indexes, vertices, uvMidpoint, sourceLine),
                    maximumDecorations,
                    sourceLine);
                return;
            }

            AddChecked(
                polygonDataList,
                new PolygonData(PolygonType.OtherTriangle_F, indexes, vertices, uvMidpoint, sourceLine),
                maximumDecorations,
                sourceLine);
            AddChecked(
                polygonDataList,
                new PolygonData(PolygonType.OtherTriangle_B, indexes, vertices, uvMidpoint, sourceLine),
                maximumDecorations,
                sourceLine);
        }

        public static bool RectangleClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs)
        {
            return RectangleClassify(
                polygonDataList,
                faceData,
                vertices,
                uvs,
                0,
                int.MaxValue);
        }

        private static bool RectangleClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs,
            int sourceLine,
            int maximumDecorations)
        {
            int[] vertexIndexes = faceData.Select(point => point[0]).ToArray();
            SideData[] sides = GenerateSides(vertexIndexes, vertices);
            float longest = sides.Max(side => side.Length);
            for (int index = 0; index < sides.Length; index++)
            {
                float error = Mathf.Abs(
                    Vector3.Angle(sides[index].SideVector, sides[(index + 1) % sides.Length].SideVector) - 90f);
                if (longest * Mathf.Sin(error * Mathf.Deg2Rad) >= AllowableError_length)
                    return false;
            }

            AddChecked(
                polygonDataList,
                new PolygonData(
                    PolygonType.Rectangle,
                    vertexIndexes,
                    vertices,
                    UvMidpoint(faceData, uvs, sourceLine),
                    sourceLine),
                maximumDecorations,
                sourceLine);
            return true;
        }

        public static bool EllipseClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs)
        {
            return EllipseClassify(
                polygonDataList,
                faceData,
                vertices,
                uvs,
                0,
                int.MaxValue);
        }

        private static bool EllipseClassify(
            List<PolygonData> polygonDataList,
            int[][] faceData,
            List<Vector3> vertices,
            List<Vector2> uvs,
            int sourceLine,
            int maximumDecorations)
        {
            int[] vertexIndexes = faceData.Select(point => point[0]).ToArray();
            SideData[] sides = GenerateSides(vertexIndexes, vertices);
            Vector3 center = Vector3.zero;
            foreach (SideData side in sides)
                center += side.OriginPosition;
            center /= 16f;

            SideData[] distanceSorted = sides
                .OrderByDescending(side => (side.OriginPosition - center).sqrMagnitude)
                .ToArray();
            int mainIndex = distanceSorted[0].SideIndex;
            SideData secondMain = sides[(mainIndex + 4) % 16];
            Vector3 forward = distanceSorted[0].OriginPosition - center;
            Vector3 up = secondMain.OriginPosition - center;
            if (forward.sqrMagnitude <= GeometryEpsilon || up.sqrMagnitude <= GeometryEpsilon)
                return false;

            Vector3 forwardAxis = forward.normalized;
            Vector3 upAxis = up.normalized;
            if (Mathf.Abs(Vector3.Dot(forwardAxis, upAxis)) >= 0.0001f)
                return false;
            float forwardRadius = forward.magnitude;
            float upRadius = up.magnitude;

            for (int index = 0; index < 16; index++)
            {
                int sideIndex = (mainIndex + index) % 16;
                Vector3 template = CircleValues[index];
                Vector3 expected = center +
                                   upAxis * (template.y * upRadius) +
                                   forwardAxis * (template.z * forwardRadius);
                if ((sides[sideIndex].OriginPosition - expected).magnitude >= AllowableError_length)
                {
                    return false;
                }
            }

            int[] reordered = Enumerable.Range(0, 16)
                .Select(index => vertexIndexes[(mainIndex + index) % 16])
                .ToArray();
            AddChecked(
                polygonDataList,
                new PolygonData(
                    PolygonType.Ellipse,
                    reordered,
                    vertices,
                    UvMidpoint(faceData, uvs, sourceLine),
                    sourceLine),
                maximumDecorations,
                sourceLine);
            return true;
        }

        public static void LineClassify(
            List<PolygonData> polygonDataList,
            int[] indexes,
            List<Vector3> vertices)
        {
            ValidateLineGeometry(indexes, vertices, 0);
            polygonDataList.Add(new PolygonData(PolygonType.Line, indexes, vertices, Vector2.zero));
        }

        private static Vector2 UvMidpoint(int[][] faceData, List<Vector2> uvs, int sourceLine)
        {
            if (uvs == null ||
                faceData.Any(point => point.Length < 2 || point[1] < 0 || point[1] >= uvs.Count))
            {
                return Vector2.zero;
            }

            double totalX = 0d;
            double totalY = 0d;
            foreach (int[] point in faceData)
            {
                Vector2 uv = uvs[point[1]];
                if (!IsFinite(uv.x) || !IsFinite(uv.y))
                    throw GeometryError(sourceLine, "face contains a non-finite texture coordinate");
                totalX += uv.x;
                totalY += uv.y;
            }
            double averageX = totalX / faceData.Length;
            double averageY = totalY / faceData.Length;
            if (double.IsNaN(averageX) || double.IsInfinity(averageX) ||
                double.IsNaN(averageY) || double.IsInfinity(averageY) ||
                Math.Abs(averageX) > float.MaxValue || Math.Abs(averageY) > float.MaxValue)
            {
                throw GeometryError(sourceLine, "face texture-coordinate average is not finite");
            }
            return new Vector2((float)averageX, (float)averageY);
        }

        public static SideData[] GenerateSides(int[] indexes, List<Vector3> vertices)
        {
            var sides = new SideData[indexes.Length];
            for (int index = 0; index < indexes.Length; index++)
            {
                sides[index] = new SideData(
                    index,
                    indexes[index],
                    indexes[(index + 1) % indexes.Length],
                    vertices);
            }
            return sides;
        }

        public static void NgonDisassembly(List<int[][]> faces, int[][] face)
        {
            for (int index = 0; index < face.Length - 2; index++)
                faces.Add(new[] { face[0], face[1 + index], face[2 + index] });
        }

        private static void EnqueueFan(
            SortedDictionary<int, FaceBucket> workBySize,
            FaceWork work,
            int outputCount,
            ref int pendingFaces,
            int maximumDecorations)
        {
            int triangles = work.Face.Length - 2;
            if ((long)outputCount + pendingFaces + triangles > maximumDecorations)
                throw GeometryError(work.SourceLine, $"expanded plan exceeds {maximumDecorations:N0} decorations");

            for (int index = 0; index < triangles; index++)
            {
                EnqueueFace(
                    workBySize,
                    new FaceWork(
                        new[] { work.Face[0], work.Face[index + 1], work.Face[index + 2] },
                        work.SourceLine),
                    outputCount,
                    ref pendingFaces,
                    maximumDecorations);
            }
        }

        private static void EnqueueFace(
            IDictionary<int, FaceBucket> workBySize,
            FaceWork work,
            int outputCount,
            ref int pendingFaces,
            int maximumDecorations)
        {
            if ((long)outputCount + pendingFaces + 1 > maximumDecorations)
                throw GeometryError(work.SourceLine, $"expanded plan exceeds {maximumDecorations:N0} decorations");
            if (!workBySize.TryGetValue(work.Face.Length, out FaceBucket queue))
            {
                queue = new FaceBucket();
                workBySize.Add(work.Face.Length, queue);
            }
            queue.Enqueue(work);
            pendingFaces++;
        }

        private static void AddChecked(
            ICollection<PolygonData> target,
            PolygonData polygon,
            int maximumDecorations,
            int sourceLine)
        {
            if (target.Count >= maximumDecorations)
                throw GeometryError(sourceLine, $"expanded plan exceeds {maximumDecorations:N0} decorations");
            target.Add(polygon);
        }

        private static void ValidateLineGeometry(int[] indexes, IReadOnlyList<Vector3> vertices, int sourceLine)
        {
            if (indexes == null || indexes.Length != 2)
                throw GeometryError(sourceLine, "line segment must contain exactly two points");
            if (indexes[0] < 0 || indexes[0] >= vertices.Count ||
                indexes[1] < 0 || indexes[1] >= vertices.Count)
            {
                throw GeometryError(sourceLine, "line segment references a vertex outside the available range");
            }
            Vector3 first = vertices[indexes[0]];
            Vector3 second = vertices[indexes[1]];
            EnsureFinite(first, sourceLine);
            EnsureFinite(second, sourceLine);
            Vector3 difference = second - first;
            EnsureFinite(difference, sourceLine);
            float squaredLength = difference.sqrMagnitude;
            if (!IsFinite(squaredLength))
                throw GeometryError(sourceLine, "line segment length is not finite after transformation");
            if (squaredLength <= GeometryEpsilon * GeometryEpsilon)
                throw GeometryError(sourceLine, "line segment has zero length after transformation");
        }

        private static void ValidateFaceGeometry(int[][] face, IReadOnlyList<Vector3> vertices, int sourceLine)
        {
            if (face == null || face.Length < 3)
                throw GeometryError(sourceLine, "face must contain at least three points");

            var points = new Vector3[face.Length];
            var seen = new HashSet<int>();
            for (int index = 0; index < face.Length; index++)
            {
                if (face[index] == null || face[index].Length == 0)
                    throw GeometryError(sourceLine, "face contains a malformed point reference");
                int vertexIndex = face[index][0];
                if (vertexIndex < 0 || vertexIndex >= vertices.Count)
                    throw GeometryError(sourceLine, "face references a vertex outside the available range");
                if (!seen.Add(vertexIndex))
                    throw GeometryError(sourceLine, "face contains a repeated vertex index");
                points[index] = vertices[vertexIndex];
                EnsureFinite(points[index], sourceLine);
            }

            for (int index = 0; index < points.Length; index++)
            {
                Vector3 edge = points[(index + 1) % points.Length] - points[index];
                EnsureFinite(edge, sourceLine);
                float squaredLength = edge.sqrMagnitude;
                if (!IsFinite(squaredLength))
                    throw GeometryError(sourceLine, "face edge length is not finite after transformation");
                if (squaredLength <= GeometryEpsilon * GeometryEpsilon)
                    throw GeometryError(sourceLine, "face contains a zero-length edge after transformation");
            }

            Vector3 normal = Vector3.zero;
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 current = points[index];
                Vector3 next = points[(index + 1) % points.Length];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }
            EnsureFinite(normal, sourceLine);
            float normalMagnitude = normal.sqrMagnitude;
            if (!IsFinite(normalMagnitude))
                throw GeometryError(sourceLine, "face normal is not finite after transformation");
            if (normalMagnitude <= GeometryEpsilon * GeometryEpsilon)
                throw GeometryError(sourceLine, "face has zero area or is collinear after transformation");
            normal.Normalize();

            float extent = 0f;
            foreach (Vector3 point in points)
            {
                Vector3 difference = point - points[0];
                EnsureFinite(difference, sourceLine);
                float distance = difference.magnitude;
                if (!IsFinite(distance))
                    throw GeometryError(sourceLine, "face extent is not finite after transformation");
                extent = Math.Max(extent, distance);
            }
            float tolerance = Math.Max(AllowableError_length, extent * 0.00001f);
            foreach (Vector3 point in points)
            {
                float planeDistance = Vector3.Dot(point - points[0], normal);
                if (!IsFinite(planeDistance))
                    throw GeometryError(sourceLine, "face planarity is not finite after transformation");
                if (Mathf.Abs(planeDistance) > tolerance)
                    throw GeometryError(sourceLine, "face is not planar after transformation");
            }

            float windingSign = 0f;
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 previous = points[(index + points.Length - 1) % points.Length];
                Vector3 current = points[index];
                Vector3 next = points[(index + 1) % points.Length];
                float turn = Vector3.Dot(Vector3.Cross(current - previous, next - current), normal);
                if (!IsFinite(turn))
                    throw GeometryError(sourceLine, "face corner orientation is not finite after transformation");
                if (Mathf.Abs(turn) <= GeometryEpsilon)
                    throw GeometryError(sourceLine, "face contains a collinear corner");
                float sign = Mathf.Sign(turn);
                if (windingSign == 0f)
                    windingSign = sign;
                else if (sign != windingSign)
                    throw GeometryError(sourceLine, "face is concave or self-intersecting");
            }
        }

        private static void EnsureFinite(Vector3 value, int sourceLine)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                throw GeometryError(sourceLine, "transformed geometry contains a non-finite coordinate");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int SourceLine(IReadOnlyList<int> sourceLines, int index)
        {
            return sourceLines != null && index < sourceLines.Count ? sourceLines[index] : 0;
        }

        private static InvalidDataException GeometryError(int sourceLine, string message)
        {
            string prefix = sourceLine > 0 ? $"OBJ line {sourceLine}" : "OBJ geometry";
            return new InvalidDataException($"{prefix}: {message}.");
        }

        private sealed class FaceWork
        {
            internal FaceWork(int[][] face, int sourceLine)
            {
                Face = face ?? throw new ArgumentNullException(nameof(face));
                SourceLine = sourceLine;
            }

            internal int[][] Face { get; }

            internal int SourceLine { get; }
        }

        private sealed class FaceBucket
        {
            private readonly List<FaceWork> _items = new List<FaceWork>();
            private int _next;

            internal bool HasItems => _next < _items.Count;

            internal void Enqueue(FaceWork work) => _items.Add(work);

            internal FaceWork Dequeue()
            {
                if (!HasItems)
                    throw new InvalidOperationException("The face work bucket is empty.");
                return _items[_next++];
            }
        }
    }
}
