using System;
using System.Collections.Generic;
using System.Reflection;
using BrilliantSkies.Core.AssetReadWrite.Obj;
using BrilliantSkies.Modding.Types;
using UnityEngine;
using UnityEngine.Rendering;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationMeshPreviewRenderer : IDisposable
    {
        private const int PreviewLayer = 31;
        private const int CacheLimit = 48;

        private readonly Dictionary<Guid, PreviewEntry> _cache =
            new Dictionary<Guid, PreviewEntry>();

        private GameObject _root;
        private GameObject _meshObject;
        private Camera _camera;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Material _material;

        internal Texture GetCachedPreview(DecorationMeshCatalogEntry entry, int size)
        {
            if (entry == null)
                return null;

            size = Mathf.Clamp(size, 48, 160);
            if (!_cache.TryGetValue(entry.Guid, out PreviewEntry preview) ||
                preview.Texture == null ||
                preview.Texture.width != size ||
                preview.Texture.height != size)
                return null;

            preview.LastUse = Time.unscaledTime;
            return preview.Texture;
        }

        internal Texture GetPreview(DecorationMeshCatalogEntry entry, int size, float spin)
        {
            if (entry == null || !entry.TryGetMesh(out MeshDefinition definition))
                return null;

            size = Mathf.Clamp(size, 48, 160);
            EnsureScene();
            if (!_cache.TryGetValue(entry.Guid, out PreviewEntry preview) ||
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
                        name = "ESU mesh preview " + entry.Guid.ToString("N"),
                        hideFlags = HideFlags.DontUnloadUnusedAsset
                    }
                };
                preview.Texture.Create();
                _cache[entry.Guid] = preview;
            }

            preview.LastUse = Time.unscaledTime;
            Mesh unityMesh = TryGetUnityMesh(definition.SafeMesh, preview);
            if (unityMesh == null)
                return null;

            Render(unityMesh, preview.Texture, spin);
            TrimCache();
            return preview.Texture;
        }

        internal Mesh GetMesh(DecorationMeshCatalogEntry entry)
        {
            if (entry == null || !entry.TryGetMesh(out MeshDefinition definition))
                return null;

            if (!_cache.TryGetValue(entry.Guid, out PreviewEntry preview))
            {
                preview = new PreviewEntry();
                _cache[entry.Guid] = preview;
            }

            preview.LastUse = Time.unscaledTime;
            Mesh mesh = TryGetUnityMesh(definition.SafeMesh, preview);
            TrimCache();
            return mesh;
        }

        public void Dispose()
        {
            foreach (PreviewEntry entry in _cache.Values)
            {
                Release(entry.Texture);
                Release(entry.GeneratedMesh);
            }
            _cache.Clear();
            Release(_material);
            Release(_root);
            _root = null;
            _meshObject = null;
            _camera = null;
            _filter = null;
            _renderer = null;
            _material = null;
        }

        private void EnsureScene()
        {
            if (_root != null)
                return;

            _root = new GameObject("ESU Decoration Mesh Preview")
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
            _material = new Material(
                Shader.Find("Standard") ??
                Shader.Find("Diffuse") ??
                Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.85f, 1f, 1f, 1f)
            };
            _renderer.sharedMaterial = _material;
        }

        private void Render(Mesh mesh, RenderTexture target, float spin)
        {
            if (mesh == null || target == null)
                return;

            _root.SetActive(true);
            _filter.sharedMesh = mesh;

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

        private static Mesh TryGetUnityMesh(object safeMesh, PreviewEntry preview)
        {
            if (safeMesh == null)
                return null;

            Type type = safeMesh.GetType();
            try
            {
                foreach (string name in new[] { "OurMesh", "Mesh", "_mesh", "mesh", "UnityMesh" })
                {
                    FieldInfo direct = type.GetField(
                        name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (direct?.GetValue(safeMesh) is Mesh mesh && mesh != null)
                        return mesh;
                }

                foreach (string name in new[] { "_bakedMesh", "BakedMesh", "Mesh", "UnityMesh" })
                {
                    PropertyInfo baked = type.GetProperty(
                        name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (baked?.GetValue(safeMesh, null) is Mesh bakedMesh && bakedMesh != null)
                        return bakedMesh;
                }
            }
            catch
            {
                // Fall through to building a lightweight preview mesh from safe mesh data.
            }

            if (preview.GeneratedMesh != null)
                return preview.GeneratedMesh;

            preview.GeneratedMesh = BuildGeneratedMesh(safeMesh);
            return preview.GeneratedMesh;
        }

        private static Mesh BuildGeneratedMesh(object safeMesh)
        {
            if (safeMesh is SafeMeshBase baseMesh)
                return BuildGeneratedMesh(baseMesh.vertices, baseMesh.triangles, baseMesh.normals, baseMesh.uv);
            if (safeMesh is SafeMeshSkinned skinned)
                return BuildGeneratedMesh(skinned.vertices, skinned.triangles, skinned.normals, null);

            try
            {
                Type type = safeMesh.GetType();
                var vertices = GetList<Vector3>(type, safeMesh, "vertices");
                var triangles = GetList<int>(type, safeMesh, "triangles");
                var normals = GetList<Vector3>(type, safeMesh, "normals");
                var uv = GetList<Vector2>(type, safeMesh, "uv");
                return BuildGeneratedMesh(vertices, triangles, normals, uv);
            }
            catch
            {
                return null;
            }
        }

        private static Mesh BuildGeneratedMesh(
            IList<Vector3> vertices,
            IList<int> triangles,
            IList<Vector3> normals,
            IList<Vector2> uv)
        {
            if (vertices == null || triangles == null ||
                vertices.Count == 0 || triangles.Count < 3)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = "ESU generated mesh preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(new List<Vector3>(vertices));
            mesh.SetTriangles(new List<int>(triangles), 0, calculateBounds: true);
            if (normals != null && normals.Count == vertices.Count)
                mesh.SetNormals(new List<Vector3>(normals));
            else
                mesh.RecalculateNormals();
            if (uv != null && uv.Count == vertices.Count)
                mesh.SetUVs(0, new List<Vector2>(uv));
            mesh.RecalculateBounds();
            return mesh;
        }

        private static IList<T> GetList<T>(Type type, object instance, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(instance, null) is IList<T> propertyList)
                return propertyList;

            FieldInfo field = type.GetField(
                "<" + name + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(instance) is IList<T> fieldList)
                return fieldList;

            return null;
        }

        private void TrimCache()
        {
            if (_cache.Count <= CacheLimit)
                return;

            Guid oldestKey = Guid.Empty;
            float oldest = float.MaxValue;
            foreach (KeyValuePair<Guid, PreviewEntry> pair in _cache)
            {
                if (pair.Value.LastUse < oldest)
                {
                    oldest = pair.Value.LastUse;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey == Guid.Empty)
                return;
            Release(_cache[oldestKey].Texture);
            Release(_cache[oldestKey].GeneratedMesh);
            _cache.Remove(oldestKey);
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
            internal Mesh GeneratedMesh;
            internal float LastUse;
        }
    }
}
