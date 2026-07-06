using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class DecorationEditorOverlay
    {
        private const float MinimumLineWidth = 1f;
        private const float ScreenCullMarginPixels = 64f;
        private static readonly List<Segment> Segments = new List<Segment>(1024);
        private static readonly List<OverlayQuad> Quads = new List<OverlayQuad>(1024);
        private static Material _material;

        internal static void BeginFrame()
        {
            Segments.Clear();
            Quads.Clear();
        }

        internal static void Clear()
        {
            Segments.Clear();
            Quads.Clear();
        }

        internal static void Line(Vector3 start, Vector3 end, Color color, float width)
        {
            if (!IsFinite(start) || !IsFinite(end))
                return;
            Segments.Add(new Segment(start, end, color, Mathf.Max(MinimumLineWidth, width)));
        }

        internal static void Quad(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Color color)
        {
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c) || !IsFinite(d))
                return;
            Quads.Add(new OverlayQuad(a, b, c, d, color));
        }

        internal static void Arrow(Vector3 start, Vector3 end, Color color, float width, float headLength)
        {
            Line(start, end, color, width);
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length <= 0.0001f)
                return;

            direction /= length;
            Camera camera = Camera.main;
            Vector3 side = camera == null
                ? Vector3.Cross(direction, Vector3.up)
                : Vector3.Cross(direction, camera.transform.forward);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(direction, Vector3.right);
            if (side.sqrMagnitude < 0.0001f)
                return;

            side.Normalize();
            float head = Mathf.Min(Mathf.Max(0.04f, headLength), length * 0.42f);
            Vector3 basePoint = end - direction * head;
            Line(end, basePoint + side * head * 0.42f, color, width);
            Line(end, basePoint - side * head * 0.42f, color, width);
        }

        internal static void Circle(
            Vector3 center,
            float radius,
            Color color,
            Vector3 normal,
            float width,
            int segments)
        {
            if (radius <= 0f || segments < 3)
                return;
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            Vector3 tangentA = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f
                ? Vector3.forward
                : Vector3.up);
            if (tangentA.sqrMagnitude < 0.0001f)
                tangentA = Vector3.right;
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(normal, tangentA).normalized;

            Vector3 previous = center + tangentA * radius;
            for (int index = 1; index <= segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                Vector3 next = center +
                               (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) *
                               radius;
                Line(previous, next, color, width);
                previous = next;
            }
        }

        internal static void Cross(Vector3 center, float radius, Color color, float width)
        {
            Camera camera = Camera.main;
            Vector3 right = camera == null ? Vector3.right : camera.transform.right;
            Vector3 up = camera == null ? Vector3.up : camera.transform.up;
            Line(center - right * radius, center + right * radius, color, width);
            Line(center - up * radius, center + up * radius, color, width);
        }

        internal static void Render()
        {
            if (Segments.Count == 0 && Quads.Count == 0)
                return;
            Camera camera = Camera.main;
            if (camera == null)
                return;

            EnsureMaterial();
            if (_material == null || !_material.SetPass(0))
                return;

            GL.PushMatrix();
            try
            {
                GL.LoadProjectionMatrix(camera.projectionMatrix);
                GL.modelview = camera.worldToCameraMatrix;
                GL.Begin(GL.QUADS);
                foreach (OverlayQuad quad in Quads)
                    DrawWorldQuad(camera, quad);
                foreach (Segment segment in Segments)
                    DrawScreenSpaceLine(camera, segment);
                GL.End();
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        private static void DrawWorldQuad(Camera camera, OverlayQuad quad)
        {
            Vector3 a = camera.WorldToScreenPoint(quad.A);
            Vector3 b = camera.WorldToScreenPoint(quad.B);
            Vector3 c = camera.WorldToScreenPoint(quad.C);
            Vector3 d = camera.WorldToScreenPoint(quad.D);
            if (a.z <= camera.nearClipPlane ||
                b.z <= camera.nearClipPlane ||
                c.z <= camera.nearClipPlane ||
                d.z <= camera.nearClipPlane)
            {
                return;
            }

            if (IsScreenQuadOutside(a, b, c, d))
                return;

            GL.Color(quad.Color);
            GL.Vertex(quad.A);
            GL.Vertex(quad.B);
            GL.Vertex(quad.C);
            GL.Vertex(quad.D);
        }

        private static void EnsureMaterial()
        {
            if (_material != null)
                return;

            Shader shader = Shader.Find("Hidden/Internal-Colored") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
                return;

            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        private static void DrawScreenSpaceLine(Camera camera, Segment segment)
        {
            Vector3 start = camera.WorldToScreenPoint(segment.Start);
            Vector3 end = camera.WorldToScreenPoint(segment.End);
            if (start.z <= camera.nearClipPlane || end.z <= camera.nearClipPlane)
                return;

            Vector2 screenStart = new Vector2(start.x, start.y);
            Vector2 screenEnd = new Vector2(end.x, end.y);
            if (IsScreenSegmentOutside(screenStart, screenEnd))
                return;

            Vector2 direction = screenEnd - screenStart;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized *
                                    (segment.Width * 0.5f);
            Vector3 a = camera.ScreenToWorldPoint(new Vector3(start.x + perpendicular.x, start.y + perpendicular.y, start.z));
            Vector3 b = camera.ScreenToWorldPoint(new Vector3(start.x - perpendicular.x, start.y - perpendicular.y, start.z));
            Vector3 c = camera.ScreenToWorldPoint(new Vector3(end.x - perpendicular.x, end.y - perpendicular.y, end.z));
            Vector3 d = camera.ScreenToWorldPoint(new Vector3(end.x + perpendicular.x, end.y + perpendicular.y, end.z));

            GL.Color(segment.Color);
            GL.Vertex(a);
            GL.Vertex(b);
            GL.Vertex(c);
            GL.Vertex(d);
        }

        private static bool IsScreenSegmentOutside(Vector2 start, Vector2 end) =>
            (start.x < -ScreenCullMarginPixels && end.x < -ScreenCullMarginPixels) ||
            (start.x > Screen.width + ScreenCullMarginPixels && end.x > Screen.width + ScreenCullMarginPixels) ||
            (start.y < -ScreenCullMarginPixels && end.y < -ScreenCullMarginPixels) ||
            (start.y > Screen.height + ScreenCullMarginPixels && end.y > Screen.height + ScreenCullMarginPixels);

        private static bool IsScreenQuadOutside(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            (a.x < -ScreenCullMarginPixels &&
             b.x < -ScreenCullMarginPixels &&
             c.x < -ScreenCullMarginPixels &&
             d.x < -ScreenCullMarginPixels) ||
            (a.x > Screen.width + ScreenCullMarginPixels &&
             b.x > Screen.width + ScreenCullMarginPixels &&
             c.x > Screen.width + ScreenCullMarginPixels &&
             d.x > Screen.width + ScreenCullMarginPixels) ||
            (a.y < -ScreenCullMarginPixels &&
             b.y < -ScreenCullMarginPixels &&
             c.y < -ScreenCullMarginPixels &&
             d.y < -ScreenCullMarginPixels) ||
            (a.y > Screen.height + ScreenCullMarginPixels &&
             b.y > Screen.height + ScreenCullMarginPixels &&
             c.y > Screen.height + ScreenCullMarginPixels &&
             d.y > Screen.height + ScreenCullMarginPixels);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private readonly struct Segment
        {
            internal Segment(Vector3 start, Vector3 end, Color color, float width)
            {
                Start = start;
                End = end;
                Color = color;
                Width = width;
            }

            internal Vector3 Start { get; }
            internal Vector3 End { get; }
            internal Color Color { get; }
            internal float Width { get; }
        }

        private readonly struct OverlayQuad
        {
            internal OverlayQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                A = a;
                B = b;
                C = c;
                D = d;
                Color = color;
            }

            internal Vector3 A { get; }
            internal Vector3 B { get; }
            internal Vector3 C { get; }
            internal Vector3 D { get; }
            internal Color Color { get; }
        }
    }
}
