using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EndlessShapes2
{
    internal readonly struct ObjVector2
    {
        internal ObjVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        internal float X { get; }
        internal float Y { get; }
    }

    internal readonly struct ObjVector3
    {
        internal ObjVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
    }

    internal sealed class ObjParseResult
    {
        internal List<OBJ_Mesh> Meshes { get; } = new List<OBJ_Mesh>();
        internal List<ObjVector3> Vertices { get; } = new List<ObjVector3>();
        internal List<ObjVector2> TextureCoordinates { get; } = new List<ObjVector2>();
    }

    internal static class ObjParser
    {
        internal const long MaxFileBytes = 256L * 1024L * 1024L;
        internal const int MaxVertices = 2_000_000;
        internal const int MaxTextureCoordinates = 2_000_000;
        internal const int MaxPrimitives = 100_000;
        private const int MaxLineCharacters = 1_048_576;

        internal static ObjParseResult ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Select an OBJ file before loading.", nameof(path));

            var file = new FileInfo(path);
            if (!file.Exists)
                throw new FileNotFoundException("The selected OBJ file does not exist.", path);
            if (file.Length > MaxFileBytes)
                throw new InvalidDataException(
                    $"OBJ file is {file.Length:N0} bytes; the limit is {MaxFileBytes:N0} bytes.");

            using (var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true))
                return Parse(reader);
        }

        internal static ObjParseResult Parse(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            var result = new ObjParseResult();
            OBJ_Mesh currentMesh = null;
            int primitiveCount = 0;
            int lineNumber = 0;
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length > MaxLineCharacters)
                    throw Error(lineNumber, $"line exceeds {MaxLineCharacters:N0} characters");

                int comment = line.IndexOf('#');
                if (comment >= 0)
                    line = line.Substring(0, comment);

                string[] parts = line.Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                switch (parts[0])
                {
                    case "o":
                    case "g":
                        currentMesh = new OBJ_Mesh
                        {
                            Name = parts.Length > 1
                                ? string.Join(" ", parts, 1, parts.Length - 1)
                                : $"Mesh {result.Meshes.Count + 1}"
                        };
                        result.Meshes.Add(currentMesh);
                        break;

                    case "v":
                        Require(parts.Length >= 4, lineNumber, "vertex requires x, y, and z");
                        if (result.Vertices.Count >= MaxVertices)
                            throw Error(lineNumber, $"vertex count exceeds {MaxVertices:N0}");
                        result.Vertices.Add(new ObjVector3(
                            ParseFloat(parts[1], lineNumber),
                            ParseFloat(parts[2], lineNumber),
                            ParseFloat(parts[3], lineNumber)));
                        break;

                    case "vt":
                        Require(parts.Length >= 2, lineNumber, "texture coordinate requires u");
                        if (result.TextureCoordinates.Count >= MaxTextureCoordinates)
                            throw Error(
                                lineNumber,
                                $"texture coordinate count exceeds {MaxTextureCoordinates:N0}");
                        result.TextureCoordinates.Add(new ObjVector2(
                            ParseFloat(parts[1], lineNumber),
                            parts.Length >= 3 ? ParseFloat(parts[2], lineNumber) : 0f));
                        break;

                    case "f":
                        Require(parts.Length >= 4, lineNumber, "face requires at least three vertices");
                        CountPrimitive(ref primitiveCount, lineNumber);
                        currentMesh = EnsureMesh(result, currentMesh);
                        var face = new int[parts.Length - 1][];
                        for (int index = 1; index < parts.Length; index++)
                            face[parts.Length - 1 - index] = ParseFacePoint(parts[index], result, lineNumber);
                        Require(
                            HasUniqueFaceVertices(face),
                            lineNumber,
                            "face contains a repeated vertex index");
                        currentMesh.FaceDatas.Add(face);
                        currentMesh.FaceSourceLines.Add(lineNumber);
                        break;

                    case "l":
                        Require(parts.Length >= 3, lineNumber, "line requires at least two vertices");
                        CountPrimitive(ref primitiveCount, lineNumber);
                        currentMesh = EnsureMesh(result, currentMesh);
                        var indices = new int[parts.Length - 1];
                        for (int index = 1; index < parts.Length; index++)
                        {
                            string vertexToken = parts[index].Split(
                                new[] { '/' },
                                StringSplitOptions.None)[0];
                            indices[index - 1] = ResolveIndex(
                                ParseInteger(vertexToken, lineNumber),
                                result.Vertices.Count,
                                lineNumber,
                                "vertex");
                            if (index > 1 && indices[index - 1] == indices[index - 2])
                                throw Error(lineNumber, "line contains a zero-length segment");
                        }
                        currentMesh.LineDatas.Add(indices);
                        currentMesh.LineSourceLines.Add(lineNumber);
                        break;
                }
            }

            if (result.Vertices.Count == 0)
                throw new InvalidDataException("OBJ file contains no vertices.");
            if (result.Meshes.Count == 0 || primitiveCount == 0)
                throw new InvalidDataException("OBJ file contains no faces or lines.");

            return result;
        }

        private static OBJ_Mesh EnsureMesh(ObjParseResult result, OBJ_Mesh currentMesh)
        {
            if (currentMesh != null)
                return currentMesh;

            currentMesh = new OBJ_Mesh { Name = "Default" };
            result.Meshes.Add(currentMesh);
            return currentMesh;
        }

        private static int[] ParseFacePoint(string token, ObjParseResult result, int lineNumber)
        {
            string[] indices = token.Split(
                new[] { '/' },
                StringSplitOptions.None);
            Require(indices.Length >= 1 && indices.Length <= 3 && indices[0].Length > 0,
                lineNumber,
                $"invalid face point '{token}'");

            int vertex = ResolveIndex(
                ParseInteger(indices[0], lineNumber),
                result.Vertices.Count,
                lineNumber,
                "vertex");
            int texture = -1;
            if (indices.Length >= 2 && indices[1].Length > 0)
            {
                texture = ResolveIndex(
                    ParseInteger(indices[1], lineNumber),
                    result.TextureCoordinates.Count,
                    lineNumber,
                    "texture coordinate");
            }

            return new[] { vertex, texture };
        }

        private static bool HasUniqueFaceVertices(int[][] face)
        {
            var seen = new HashSet<int>();
            for (int index = 0; index < face.Length; index++)
            {
                if (!seen.Add(face[index][0]))
                    return false;
            }
            return true;
        }

        private static int ResolveIndex(int value, int count, int lineNumber, string kind)
        {
            if (value == 0)
                throw Error(lineNumber, $"OBJ {kind} index cannot be zero");

            int resolved = value > 0 ? value - 1 : count + value;
            if (resolved < 0 || resolved >= count)
                throw Error(lineNumber, $"{kind} index {value} is outside the available range");
            return resolved;
        }

        private static float ParseFloat(string value, int lineNumber)
        {
            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float parsed) ||
                float.IsNaN(parsed) ||
                float.IsInfinity(parsed))
            {
                throw Error(lineNumber, $"'{value}' is not a finite invariant-culture number");
            }

            return parsed;
        }

        private static int ParseInteger(string value, int lineNumber)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw Error(lineNumber, $"'{value}' is not a valid index");
            return parsed;
        }

        private static void CountPrimitive(ref int primitiveCount, int lineNumber)
        {
            primitiveCount++;
            if (primitiveCount > MaxPrimitives)
                throw Error(lineNumber, $"face/line count exceeds {MaxPrimitives:N0}");
        }

        private static void Require(bool condition, int lineNumber, string message)
        {
            if (!condition)
                throw Error(lineNumber, message);
        }

        private static InvalidDataException Error(int lineNumber, string message)
        {
            return new InvalidDataException($"OBJ line {lineNumber}: {message}.");
        }
    }
}
