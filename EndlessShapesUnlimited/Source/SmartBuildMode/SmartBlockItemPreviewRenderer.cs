using System;
using System.Collections.Generic;
using BrilliantSkies.Modding.Types;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed class SmartBlockItemPreviewRenderer : IDisposable
    {
        private const int PreviewLayer = 31;
        private const int CacheLimit = 64;
        private const int MaxWireEdgesPerPlacement = 240;
        private const float HardEdgeDotThreshold = 0.996f;

        private readonly Dictionary<string, PreviewEntry> _previews =
            new Dictionary<string, PreviewEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Edge[]> _edgesByMeshId =
            new Dictionary<int, Edge[]>();
        private readonly Dictionary<int, Material> _ghostMaterials =
            new Dictionary<int, Material>();

        private GameObject _root;
        private GameObject _meshObject;
        private Camera _camera;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Material _fallbackPreviewMaterial;
        private Material _fallbackGhostMaterial;

        internal Texture GetCachedPreview(SmartBlockCandidate candidate, int size)
        {
            string key = CandidateKey(candidate);
            if (key == null)
                return null;

            size = Mathf.Clamp(size, 48, 160);
            if (!_previews.TryGetValue(key, out PreviewEntry preview) ||
                preview.Texture == null ||
                preview.Texture.width != size ||
                preview.Texture.height != size)
            {
                return null;
            }

            preview.LastUse = Time.unscaledTime;
            return preview.Texture;
        }

        internal Texture GetPreview(SmartBlockCandidate candidate, int size, float spin)
        {
            Mesh mesh = GetMesh(candidate);
            string key = CandidateKey(candidate);
            if (mesh == null || key == null)
                return null;

            size = Mathf.Clamp(size, 48, 160);
            EnsureScene();
            if (!_previews.TryGetValue(key, out PreviewEntry preview) ||
                preview.Texture == null ||
                preview.Texture.width != size ||
                preview.Texture.height != size)
            {
                if (preview != null)
                    Release(preview.Texture);
                preview = new PreviewEntry
                {
                    Texture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                    {
                        name = "ESU smart block preview " + key,
                        hideFlags = HideFlags.DontUnloadUnusedAsset
                    }
                };
                preview.Texture.Create();
                _previews[key] = preview;
            }

            preview.LastUse = Time.unscaledTime;
            Render(mesh, candidate, preview.Texture, spin);
            TrimCache();
            return preview.Texture;
        }

        internal Mesh GetMesh(SmartBlockCandidate candidate)
        {
            try
            {
                return candidate?.Definition?.GetMesh();
            }
            catch
            {
                return null;
            }
        }

        internal void ClearCache()
        {
            foreach (PreviewEntry entry in _previews.Values)
                Release(entry.Texture);
            _previews.Clear();
            _edgesByMeshId.Clear();
            foreach (Material material in _ghostMaterials.Values)
                Release(material);
            _ghostMaterials.Clear();
        }

        internal void DrawPlacementMesh(
            SmartBuildPlacement placement,
            Matrix4x4 matrix,
            Color tint)
        {
            Mesh mesh = GetMesh(placement?.Candidate);
            if (mesh == null)
                return;

            Material material = GhostMaterial(placement.Candidate?.Definition, tint);
            if (material == null)
                return;

            Graphics.DrawMesh(mesh, matrix, material, 0);
        }

        internal bool DrawPlacementWire(
            SmartBuildPlacement placement,
            Matrix4x4 matrix,
            Color color,
            float width)
        {
            Mesh mesh = GetMesh(placement?.Candidate);
            if (mesh == null)
                return false;

            Vector3[] vertices = mesh.vertices;
            Edge[] edges = HardEdgesFor(mesh);
            if (vertices == null || vertices.Length == 0 || edges.Length == 0)
                return false;

            int stride = edges.Length > MaxWireEdgesPerPlacement
                ? Mathf.CeilToInt(edges.Length / (float)MaxWireEdgesPerPlacement)
                : 1;
            for (int index = 0; index < edges.Length; index += stride)
            {
                Edge edge = edges[index];
                if (edge.A < 0 || edge.B < 0 || edge.A >= vertices.Length || edge.B >= vertices.Length)
                    continue;

                DecorationEditorOverlay.Line(
                    matrix.MultiplyPoint3x4(vertices[edge.A]),
                    matrix.MultiplyPoint3x4(vertices[edge.B]),
                    color,
                    width);
            }

            return true;
        }

        public void Dispose()
        {
            ClearCache();
            Release(_fallbackPreviewMaterial);
            Release(_fallbackGhostMaterial);
            Release(_root);
            _root = null;
            _meshObject = null;
            _camera = null;
            _filter = null;
            _renderer = null;
            _fallbackPreviewMaterial = null;
            _fallbackGhostMaterial = null;
        }

        private void EnsureScene()
        {
            if (_root != null)
                return;

            _root = new GameObject("ESU Smart Block Preview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            _root.SetActive(false);

            GameObject cameraObject = new GameObject("Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            cameraObject.transform.SetParent(_root.transform, worldPositionStays: false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 0.9f;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 20f;
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.enabled = false;
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -4f);
            cameraObject.transform.localRotation = Quaternion.identity;

            _meshObject = new GameObject("Mesh")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            _meshObject.transform.SetParent(_root.transform, worldPositionStays: false);
            _filter = _meshObject.AddComponent<MeshFilter>();
            _renderer = _meshObject.AddComponent<MeshRenderer>();
            _fallbackPreviewMaterial = new Material(
                Shader.Find("Standard") ??
                Shader.Find("Diffuse") ??
                Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.85f, 1f, 1f, 1f)
            };
            _renderer.sharedMaterial = _fallbackPreviewMaterial;
        }

        private void Render(
            Mesh mesh,
            SmartBlockCandidate candidate,
            RenderTexture target,
            float spin)
        {
            if (mesh == null || target == null)
                return;

            _root.SetActive(true);
            _filter.sharedMesh = mesh;
            _renderer.sharedMaterial = PreviewMaterial(candidate?.Definition);

            Bounds bounds = mesh.bounds;
            float maxExtent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            float scale = maxExtent > 0.0001f ? 0.78f / maxExtent : 1f;
            _meshObject.transform.localScale = Vector3.one * scale;
            _meshObject.transform.localRotation = Quaternion.Euler(18f, spin, 0f);
            _meshObject.transform.localPosition = -bounds.center * scale;

            RenderTexture previous = RenderTexture.active;
            try
            {
                _camera.targetTexture = target;
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                RenderTexture.active = previous;
                _root.SetActive(false);
            }
        }

        private Material PreviewMaterial(ItemDefinition definition)
        {
            try
            {
                Material material = definition?.GetMaterial();
                if (material != null)
                    return material;
            }
            catch
            {
            }

            EnsureScene();
            return _fallbackPreviewMaterial;
        }

        private Material GhostMaterial(ItemDefinition definition, Color tint)
        {
            int key = definition == null ? 0 : definition.GetHashCode();
            if (!_ghostMaterials.TryGetValue(key, out Material material) || material == null)
            {
                Material source = null;
                try
                {
                    source = definition?.GetMaterial();
                }
                catch
                {
                }

                if (source != null)
                    material = new Material(source);
                else
                {
                    if (_fallbackGhostMaterial == null)
                    {
                        _fallbackGhostMaterial = new Material(
                            Shader.Find("Standard") ??
                            Shader.Find("Diffuse") ??
                            Shader.Find("Hidden/Internal-Colored"))
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }

                    material = new Material(_fallbackGhostMaterial);
                }

                material.hideFlags = HideFlags.HideAndDontSave;
                ConfigureTransparent(material);
                _ghostMaterials[key] = material;
            }

            material.color = tint;
            return material;
        }

        private static void ConfigureTransparent(Material material)
        {
            if (material == null)
                return;

            try
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
            catch
            {
                // Some FtD materials use custom shaders; tinting still works if blend setup is unsupported.
            }
        }

        private Edge[] HardEdgesFor(Mesh mesh)
        {
            int key = mesh.GetInstanceID();
            if (_edgesByMeshId.TryGetValue(key, out Edge[] edges))
                return edges;

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            if (triangles == null || triangles.Length < 3 || vertices == null || vertices.Length == 0)
                return Array.Empty<Edge>();

            var edgeNormals = new Dictionary<long, EdgeAccumulator>();
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                if (!ValidVertex(a, vertices.Length) ||
                    !ValidVertex(b, vertices.Length) ||
                    !ValidVertex(c, vertices.Length) ||
                    a == b ||
                    b == c ||
                    c == a)
                {
                    continue;
                }

                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (normal.sqrMagnitude <= 0.000001f)
                    continue;

                normal.Normalize();
                AddHardEdgeCandidate(a, b, normal, edgeNormals);
                AddHardEdgeCandidate(b, c, normal, edgeNormals);
                AddHardEdgeCandidate(c, a, normal, edgeNormals);
            }

            var result = new List<Edge>();
            foreach (EdgeAccumulator accumulator in edgeNormals.Values)
            {
                if (accumulator.ShouldDrawHardEdge())
                    result.Add(new Edge(accumulator.A, accumulator.B));
            }

            edges = result.ToArray();
            _edgesByMeshId[key] = edges;
            return edges;
        }

        private static bool ValidVertex(int index, int count) =>
            index >= 0 && index < count;

        private static void AddHardEdgeCandidate(
            int a,
            int b,
            Vector3 normal,
            Dictionary<long, EdgeAccumulator> edgeNormals)
        {
            int min = Math.Min(a, b);
            int max = Math.Max(a, b);
            long key = ((long)min << 32) | (uint)max;
            if (!edgeNormals.TryGetValue(key, out EdgeAccumulator accumulator))
            {
                accumulator = new EdgeAccumulator(min, max);
                edgeNormals[key] = accumulator;
            }

            accumulator.Add(normal);
        }

        private static string CandidateKey(SmartBlockCandidate candidate)
        {
            if (candidate == null)
                return null;

            try
            {
                Guid guid = candidate.Definition?.ComponentId?.Guid ?? Guid.Empty;
                if (guid != Guid.Empty)
                    return guid.ToString("N");
            }
            catch
            {
            }

            return (candidate.DisplayName ?? "shape") + "|" +
                   (candidate.GeometryName ?? string.Empty) + "|" +
                   candidate.Length.ToString();
        }

        private void TrimCache()
        {
            if (_previews.Count <= CacheLimit)
                return;

            string oldestKey = null;
            float oldest = float.MaxValue;
            foreach (KeyValuePair<string, PreviewEntry> pair in _previews)
            {
                if (pair.Value.LastUse < oldest)
                {
                    oldest = pair.Value.LastUse;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey == null)
                return;
            Release(_previews[oldestKey].Texture);
            _previews.Remove(oldestKey);
        }

        private static void Release(UnityEngine.Object instance)
        {
            if (instance == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
        }

        private sealed class PreviewEntry
        {
            internal RenderTexture Texture;
            internal float LastUse;
        }

        private readonly struct Edge
        {
            internal Edge(int a, int b)
            {
                A = a;
                B = b;
            }

            internal int A { get; }

            internal int B { get; }
        }

        private sealed class EdgeAccumulator
        {
            private readonly List<Vector3> _normals = new List<Vector3>(2);

            internal EdgeAccumulator(int a, int b)
            {
                A = a;
                B = b;
            }

            internal int A { get; }

            internal int B { get; }

            internal void Add(Vector3 normal) =>
                _normals.Add(normal);

            internal bool ShouldDrawHardEdge()
            {
                if (_normals.Count != 2)
                    return true;

                return Mathf.Abs(Vector3.Dot(_normals[0], _normals[1])) < HardEdgeDotThreshold;
            }
        }
    }
}
