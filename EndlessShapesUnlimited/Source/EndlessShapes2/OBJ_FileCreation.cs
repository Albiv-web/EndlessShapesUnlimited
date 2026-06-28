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
            string finalFolder = FindAvailableDirectory(Path.Combine(
                profileRoot,
                $"{constructName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"));
            CommitStagedDirectory(finalFolder, stagingFolder =>
            {
                string textureFolder = Path.Combine(stagingFolder, "Textures");
                Directory.CreateDirectory(textureFolder);
                Dictionary<Material, MaterialDefinition> materialLookup = BuildMaterialLookup();
                var usedMaterials = new Dictionary<Guid, MaterialDefinition>();

                var constructs = new List<AllConstruct>();
                mainConstruct.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(constructs);
                foreach (AllConstruct construct in constructs)
                {
                    ExportConstruct(
                        construct,
                        stagingFolder,
                        materialLookup,
                        usedMaterials);
                }

                WriteMaterials(
                    usedMaterials.Values.OrderBy(MaterialName),
                    textureFolder,
                    stagingFolder);
            });
            return finalFolder;
        }

        private static Dictionary<Material, MaterialDefinition> BuildMaterialLookup()
        {
            var result = new Dictionary<Material, MaterialDefinition>();
            foreach (MaterialDefinition definition in Configured.i.Materials.Components)
            {
                if (definition?.Material != null && !result.ContainsKey(definition.Material))
                    result.Add(definition.Material, definition);
            }
            return result;
        }

        private static void WriteMaterials(
            IEnumerable<MaterialDefinition> materials,
            string textureFolder,
            string outputFolder)
        {
            var exportedTextures = new Dictionary<Guid, string>();
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
                        string textureName = GetOrAddTextureName(
                            exportedTextures,
                            texture.ComponentId.Guid,
                            () => TextureName(texture),
                            out bool writeTexture);
                        if (writeTexture)
                        {
                            byte[] encoded = ForcedEncodeToJpg(texture.Texture.GetTexture());
                            File.WriteAllBytes(
                                Path.Combine(textureFolder, textureName + ".jpg"),
                                encoded);
                        }
                        writer.WriteLine("map_Kd Textures/" + textureName + ".jpg");
                    }
                    catch (Exception exception)
                    {
                        throw new IOException(
                            $"Could not export texture '{texture.FilenameOrUrl}'.",
                            exception);
                    }
                    writer.WriteLine();
                }
            }
        }

        private static void ExportConstruct(
            AllConstruct construct,
            string outputFolder,
            IReadOnlyDictionary<Material, MaterialDefinition> materialLookup,
            IDictionary<Guid, MaterialDefinition> usedMaterials)
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
                    Mesh shared = filter?.sharedMesh;
                    if (renderer == null || shared == null)
                        continue;

                    Material[] materials = renderer.sharedMaterials;
                    List<Vector3> transformedVertices = shared.vertices
                        .Select(vertex => filter.transform.localToWorldMatrix.MultiplyPoint(vertex))
                        .Select(vertex => construct.myTransform.worldToLocalMatrix.MultiplyPoint(vertex))
                        .ToList();
                    Vector2[] textureCoordinates = shared.uv;

                    foreach (SubMeshBinding<Material> binding in
                             BindSubMeshes(shared.subMeshCount, materials))
                    {
                        Material runtimeMaterial = binding.Material;
                        if (runtimeMaterial == null ||
                            !materialLookup.TryGetValue(runtimeMaterial, out MaterialDefinition definition))
                        {
                            continue;
                        }

                        Mesh extracted = CreateSubMesh(
                            shared,
                            binding.SubMesh,
                            transformedVertices,
                            textureCoordinates);
                        ownedSourceMeshes.Add(extracted);
                        AddMesh(meshesByMaterial, definition, extracted);
                    }
                }

                foreach (KeyValuePair<int, List<ChunkMesh>> chunks in meshMerger.D)
                {
                    MaterialDefinition material = Configured.i.Materials.FindUsingTheRuntimeId(
                        chunks.Key,
                        out bool found);
                    if (!found || material == null || chunks.Value.Count == 0 ||
                        chunks.Value.All(chunk => chunk.VertCount == 0))
                    {
                        continue;
                    }

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
                    {
                        outputMeshes.Add(merged);
                        TrackIfEmitted(
                            usedMaterials,
                            group.Key.ComponentId.Guid,
                            group.Key,
                            emitted: true);
                    }
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

        private static Mesh CreateSubMesh(
            Mesh source,
            int subMesh,
            List<Vector3> transformedVertices,
            Vector2[] textureCoordinates)
        {
            var extracted = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            extracted.SetVertices(transformedVertices);
            if (textureCoordinates.Length == transformedVertices.Count)
                extracted.SetUVs(0, textureCoordinates.ToList());
            extracted.SetTriangles(source.GetTriangles(subMesh), 0);
            return extracted;
        }

        private static Mesh MergeMeshes(
            MaterialDefinition material,
            IEnumerable<Mesh> sources,
            bool isSubConstruct,
            Vector3 subConstructPosition,
            Quaternion subConstructRotation)
        {
            var vertices = new List<Vector3>();
            var textureCoordinates = new List<Vector2>();
            var triangles = new List<int>();

            foreach (Mesh source in sources.Where(source => source != null))
            {
                int offset = vertices.Count;
                vertices.AddRange(source.vertices);
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

                    int[] triangles = mesh.triangles;
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        int a = triangles[index] + vertexOffset;
                        int b = triangles[index + 1] + vertexOffset;
                        int c = triangles[index + 2] + vertexOffset;
                        writer.WriteLine(hasTextureCoordinates
                            ? $"f {a}/{a} {b}/{b} {c}/{c}"
                            : $"f {a} {b} {c}");
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

        internal static string MaterialName(MaterialDefinition material)
        {
            TextureDefinition texture = Configured.i.Textures.Find(
                material.ColorTextureReference.Reference.Guid);
            string prefix = texture != null ? TextureName(texture) : "Material";
            return prefix + "-" + material.ComponentId.Guid.ToString("N").Substring(0, 8);
        }

        internal static string TextureName(TextureDefinition texture)
        {
            string raw = $"{texture.Source}-{texture.FilenameOrUrl}";
            return AssetNameWithGuid(raw.Replace('\\', '-').Replace('/', '-'), texture.ComponentId.Guid, "Texture");
        }

        internal static string AssetNameWithGuid(string raw, Guid guid, string fallback)
        {
            string prefix = SanitizeFileName(raw, fallback);
            if (prefix.Length > 67)
                prefix = prefix.Substring(0, 67);
            return prefix + "-" + guid.ToString("N");
        }

        internal static string GetOrAddTextureName(
            IDictionary<Guid, string> registry,
            Guid textureGuid,
            Func<string> createName,
            out bool added)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (createName == null)
                throw new ArgumentNullException(nameof(createName));
            if (registry.TryGetValue(textureGuid, out string existing))
            {
                added = false;
                return existing;
            }

            string created = createName();
            registry.Add(textureGuid, created);
            added = true;
            return created;
        }

        internal static IReadOnlyList<SubMeshBinding<TMaterial>> BindSubMeshes<TMaterial>(
            int subMeshCount,
            IReadOnlyList<TMaterial> materials)
        {
            if (subMeshCount < 0)
                throw new ArgumentOutOfRangeException(nameof(subMeshCount));
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));
            int count = Math.Min(subMeshCount, materials.Count);
            var bindings = new List<SubMeshBinding<TMaterial>>(count);
            for (int index = 0; index < count; index++)
                bindings.Add(new SubMeshBinding<TMaterial>(index, materials[index]));
            return bindings;
        }

        internal static void TrackIfEmitted<TKey, TValue>(
            IDictionary<TKey, TValue> used,
            TKey key,
            TValue value,
            bool emitted)
        {
            if (used == null)
                throw new ArgumentNullException(nameof(used));
            if (emitted)
                used[key] = value;
        }

        internal static string SanitizeFileName(string value, string fallback)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (!invalid.Contains(character) && !char.IsControl(character))
                    builder.Append(character);
            }

            string sanitized = builder.ToString().Trim().TrimEnd(new[] { '.' });
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = fallback;
            return sanitized.Length <= 100 ? sanitized : sanitized.Substring(0, 100);
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

        internal static void CommitStagedDirectory(string finalFolder, Action<string> populate)
        {
            if (string.IsNullOrWhiteSpace(finalFolder))
                throw new ArgumentException("The final export folder is required.", nameof(finalFolder));
            if (populate == null)
                throw new ArgumentNullException(nameof(populate));

            string stagingFolder = finalFolder + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(stagingFolder);
                populate(stagingFolder);
                Directory.Move(stagingFolder, finalFolder);
            }
            catch
            {
                TryDeleteDirectory(stagingFolder);
                throw;
            }
        }

        internal static string Number(float value)
        {
            if (!FlexibleFloatParser.IsFinite(value))
                throw new InvalidDataException("OBJ export encountered a non-finite number.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    $"[EndlessShapes Unlimited] Could not remove partial export '{path}'",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }
    }

    internal readonly struct SubMeshBinding<TMaterial>
    {
        internal SubMeshBinding(int subMesh, TMaterial material)
        {
            SubMesh = subMesh;
            Material = material;
        }

        internal int SubMesh { get; }

        internal TMaterial Material { get; }
    }
}
