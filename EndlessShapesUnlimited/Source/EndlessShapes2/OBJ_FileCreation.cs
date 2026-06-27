using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BrilliantSkies.Common.CarriedObjects;
using BrilliantSkies.Common.ChunkCreators.Chunks;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Constructs.Modules.All.Chunks;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using UnityEngine;

namespace EndlessShapes2
{
    public static class OBJ_FileCreation
    {
        private const string MaterialFileName = "Materials.mtl";

        public static void Start(MainConstruct mainConstruct)
        {
            try
            {
                string outputPath = Export(mainConstruct);
                InfoStore.Add($"OBJ export completed: {outputPath}");
            }
            catch (Exception exception)
            {
                InfoStore.Add($"OBJ export failed: {exception.Message}");
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] OBJ export failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }
        }

        private static string Export(MainConstruct mainConstruct)
        {
            if (mainConstruct == null)
                throw new ArgumentNullException(nameof(mainConstruct));

            string profileRoot = Get.ProfilePaths.ProfileRootDir().ToString();
            string constructName = SanitizeFileName(mainConstruct.GetBlueprintName(), "Construct");
            string outputFolder = Path.Combine(
                profileRoot,
                $"{constructName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
            outputFolder = FindAvailableDirectory(outputFolder);
            string textureFolder = Path.Combine(outputFolder, "Textures");
            Directory.CreateDirectory(textureFolder);

            WriteMaterials(textureFolder, outputFolder);

            var constructs = new List<AllConstruct>();
            mainConstruct.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(constructs);
            foreach (AllConstruct construct in constructs)
                ExportConstruct(construct, outputFolder);

            return outputFolder;
        }

        private static void WriteMaterials(string textureFolder, string outputFolder)
        {
            IEnumerable<MaterialDefinition> materials = Configured.i.Materials.Components
                .Where(material => material != null)
                .GroupBy(material => material.ComponentId.Guid)
                .Select(group => group.First());

            using (var writer = CreateWriter(Path.Combine(outputFolder, MaterialFileName)))
            {
                foreach (MaterialDefinition material in materials)
                {
                    writer.WriteLine("newmtl " + MaterialName(material));
                    TextureDefinition texture = Configured.i.Textures.Find(
                        material.ColorTextureReference.Reference.Guid);
                    if (texture == null ||
                        (texture.Source != ModSource.File && texture.Source != ModSource.Resources))
                    {
                        writer.WriteLine();
                        continue;
                    }

                    try
                    {
                        string textureName = TextureName(texture);
                        byte[] encoded = ForcedEncodeToJpg(texture.Texture.GetTexture());
                        File.WriteAllBytes(
                            Path.Combine(textureFolder, textureName + ".jpg"),
                            encoded);
                        writer.WriteLine("map_Kd Textures/" + textureName + ".jpg");
                    }
                    catch (Exception exception)
                    {
                        AdvLogger.LogException(
                            $"[EndlessShapes Unlimited] Could not export texture '{texture.FilenameOrUrl}'",
                            exception,
                            LogOptions._AlertDevInGame);
                    }
                    writer.WriteLine();
                }
            }
        }

        private static void ExportConstruct(AllConstruct construct, string outputFolder)
        {
            var meshMerger = construct.Chunks as ConstructableMeshMerger;
            if (meshMerger == null)
                return;

            var meshesByMaterial = new Dictionary<MaterialDefinition, List<Mesh>>();
            var ownedSourceMeshes = new List<Mesh>();
            var outputMeshes = new List<Mesh>();

            try
            {
                foreach (ICarriedObjectReference carried in construct.CarriedObjects.Objects)
                {
                    MeshRenderer renderer = carried.ObjectItself.GetComponent<MeshRenderer>();
                    MeshFilter filter = carried.ObjectItself.GetComponent<MeshFilter>();
                    if (renderer == null || filter == null || filter.sharedMesh == null)
                        continue;

                    MaterialDefinition material = Configured.i.Materials.Components
                        .FirstOrDefault(candidate => candidate.Material == renderer.sharedMaterial);
                    if (material == null)
                        continue;

                    Mesh clone = UnityEngine.Object.Instantiate(filter.sharedMesh);
                    ownedSourceMeshes.Add(clone);
                    clone.SetVertices(clone.vertices
                        .Select(vertex => filter.transform.localToWorldMatrix.MultiplyPoint(vertex))
                        .Select(vertex => construct.myTransform.worldToLocalMatrix.MultiplyPoint(vertex))
                        .ToList());
                    AddMesh(meshesByMaterial, material, clone);
                }

                foreach (KeyValuePair<int, List<ChunkMesh>> chunks in meshMerger.D)
                {
                    MaterialDefinition material = Configured.i.Materials.FindUsingTheRuntimeId(
                        chunks.Key,
                        out bool found);
                    if (!found || chunks.Value.Count == 0 || chunks.Value.All(chunk => chunk.VertCount == 0))
                        continue;
                    foreach (ChunkMesh chunk in chunks.Value)
                        AddMesh(meshesByMaterial, material, chunk.GetMesh());
                }

                Vector3 subConstructPosition = Vector3.zero;
                Quaternion subConstructRotation = Quaternion.identity;
                bool isSubConstruct = construct.PersistentSubConstructIndex != -1;
                if (isSubConstruct)
                {
                    MainConstruct main = construct.Main;
                    subConstructPosition = main.SafeGlobalToLocal(construct.SafePosition);
                    subConstructRotation = main.SafeGlobalRotationToLocalRotation(construct.SafeRotation);
                }

                foreach (KeyValuePair<MaterialDefinition, List<Mesh>> group in meshesByMaterial)
                {
                    Mesh merged = MergeMeshes(
                        group.Key,
                        group.Value,
                        isSubConstruct,
                        subConstructPosition,
                        subConstructRotation);
                    if (merged != null)
                        outputMeshes.Add(merged);
                }

                string fileName = construct.PersistentSubConstructIndex == -1
                    ? "MainConstruct"
                    : $"SubConstruct_{construct.PersistentSubConstructIndex}";
                WriteObj(outputMeshes, Path.Combine(outputFolder, fileName + ".obj"));
            }
            finally
            {
                foreach (Mesh mesh in outputMeshes)
                    UnityEngine.Object.Destroy(mesh);
                foreach (Mesh mesh in ownedSourceMeshes)
                    UnityEngine.Object.Destroy(mesh);
            }
        }

        private static Mesh MergeMeshes(
            MaterialDefinition material,
            List<Mesh> sources,
            bool isSubConstruct,
            Vector3 subConstructPosition,
            Quaternion subConstructRotation)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var textureCoordinates = new List<Vector2>();
            var triangles = new List<int>();

            foreach (Mesh source in sources.Where(source => source != null))
            {
                int offset = vertices.Count;
                vertices.AddRange(source.vertices);
                if (source.normals.Length == source.vertexCount)
                    normals.AddRange(source.normals);
                if (source.uv.Length == source.vertexCount)
                    textureCoordinates.AddRange(source.uv);
                triangles.AddRange(source.triangles.Select(index => index + offset));
            }

            if (vertices.Count == 0 || triangles.Count == 0)
                return null;

            if (isSubConstruct)
            {
                for (int index = 0; index < vertices.Count; index++)
                    vertices[index] = subConstructRotation * vertices[index] + subConstructPosition;
            }

            var merged = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                name = MaterialName(material)
            };
            merged.SetVertices(vertices);
            if (normals.Count == vertices.Count)
                merged.SetNormals(normals);
            if (textureCoordinates.Count == vertices.Count)
                merged.SetUVs(0, textureCoordinates);
            merged.SetTriangles(triangles, 0);
            FlipHorizontal(merged);
            return merged;
        }

        private static void AddMesh(
            IDictionary<MaterialDefinition, List<Mesh>> target,
            MaterialDefinition material,
            Mesh mesh)
        {
            if (material == null || mesh == null)
                return;
            if (!target.TryGetValue(material, out List<Mesh> list))
            {
                list = new List<Mesh>();
                target.Add(material, list);
            }
            list.Add(mesh);
        }

        private static void FlipHorizontal(Mesh mesh)
        {
            mesh.SetVertices(mesh.vertices
                .Select(vertex => new Vector3(-vertex.x, vertex.y, vertex.z))
                .ToList());

            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int first = triangles[index];
                triangles[index] = triangles[index + 1];
                triangles[index + 1] = first;
            }
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            if (mesh.uv.Length == mesh.vertexCount)
                mesh.RecalculateTangents();
        }

        private static void WriteObj(IReadOnlyList<Mesh> meshes, string filename)
        {
            using (var writer = CreateWriter(filename))
            {
                writer.WriteLine("mtllib " + MaterialFileName);
                int vertexOffset = 1;
                foreach (Mesh mesh in meshes)
                {
                    writer.WriteLine();
                    writer.WriteLine("g " + mesh.name);
                    writer.WriteLine("usemtl " + mesh.name);

                    foreach (Vector3 vertex in mesh.vertices)
                        writer.WriteLine($"v {Number(vertex.x)} {Number(vertex.y)} {Number(vertex.z)}");

                    Vector2[] textureCoordinates = mesh.uv;
                    bool hasTextureCoordinates = textureCoordinates.Length == mesh.vertexCount;
                    if (hasTextureCoordinates)
                    {
                        foreach (Vector2 uv in textureCoordinates)
                            writer.WriteLine($"vt {Number(uv.x)} {Number(uv.y)}");
                    }

                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        int[] triangles = mesh.GetTriangles(subMesh);
                        for (int index = 0; index < triangles.Length; index += 3)
                        {
                            int a = triangles[index] + vertexOffset;
                            int b = triangles[index + 1] + vertexOffset;
                            int c = triangles[index + 2] + vertexOffset;
                            writer.WriteLine(hasTextureCoordinates
                                ? $"f {a}/{a} {b}/{b} {c}/{c}"
                                : $"f {a} {b} {c}");
                        }
                    }

                    vertexOffset += mesh.vertexCount;
                }
            }
        }

        private static byte[] ForcedEncodeToJpg(Texture2D source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                copy = new Texture2D(source.width, source.height);
                copy.ReadPixels(new Rect(0, 0, temporary.width, temporary.height), 0, 0);
                copy.Apply();
                return copy.EncodeToJPG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }

        private static StreamWriter CreateWriter(string path)
        {
            return new StreamWriter(path, append: false, new UTF8Encoding(false))
            {
                NewLine = "\n"
            };
        }

        private static string MaterialName(MaterialDefinition material)
        {
            TextureDefinition texture = Configured.i.Textures.Find(
                material.ColorTextureReference.Reference.Guid);
            string prefix = texture != null ? TextureName(texture) : "Material";
            return prefix + "-" + material.ComponentId.Guid.ToString("N").Substring(0, 8);
        }

        private static string TextureName(TextureDefinition texture)
        {
            string raw = $"{texture.Source}-{texture.FilenameOrUrl}";
            return SanitizeFileName(raw.Replace('\\', '-').Replace('/', '-'),
                "Texture-" + texture.ComponentId.Guid.ToString("N"));
        }

        private static string SanitizeFileName(string value, string fallback)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (!invalid.Contains(character) && !char.IsControl(character))
                    builder.Append(character);
            }

            string sanitized = builder.ToString().Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = fallback;
            return sanitized.Length <= 120 ? sanitized : sanitized.Substring(0, 120);
        }

        private static string FindAvailableDirectory(string preferred)
        {
            if (!Directory.Exists(preferred) && !File.Exists(preferred))
                return preferred;
            for (int index = 2; index < 10_000; index++)
            {
                string candidate = preferred + "-" + index.ToString(CultureInfo.InvariantCulture);
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }
            throw new IOException("Could not allocate a unique OBJ export folder.");
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
