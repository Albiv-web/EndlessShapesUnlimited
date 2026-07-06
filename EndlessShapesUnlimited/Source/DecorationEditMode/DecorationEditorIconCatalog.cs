using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditorIconDefinition
    {
        internal DecorationEditorIconDefinition(
            string key,
            string displayName,
            string ftdName,
            Guid guid,
            string fallbackGlyph)
        {
            Key = key;
            DisplayName = displayName;
            FtdName = ftdName;
            Guid = guid;
            FallbackGlyph = fallbackGlyph;
        }

        internal string Key { get; }
        internal string DisplayName { get; }
        internal string FtdName { get; }
        internal Guid Guid { get; }
        internal string FallbackGlyph { get; }
    }

    internal static class DecorationEditorIconCatalog
    {
        private static readonly DecorationEditorIconDefinition[] _definitions =
        {
            new DecorationEditorIconDefinition("open", "Open editor", "editButton", new Guid("8d3109e4-19bc-454b-9691-1432a54c99f4"), "E"),
            new DecorationEditorIconDefinition("build", "Build", "buildIcon", new Guid("dfbc8eb5-8b53-4fb6-a7da-7b93ec46ec2c"), "B"),
            new DecorationEditorIconDefinition("create", "Create", "create", new Guid("e70fd358-3df2-4fea-9cf9-af1e310df610"), "+"),
            new DecorationEditorIconDefinition("select", "Select", "crosshair", new Guid("f417ee2c-2aa4-4fb2-ab9f-de4c59b94e45"), "O"),
            new DecorationEditorIconDefinition("boxSelect", "Box select", "esuBoxSelect", Guid.Empty, "B"),
            new DecorationEditorIconDefinition("move", "Move", "move", new Guid("68419445-57e1-41ac-89c9-7683976ddcff"), "M"),
            new DecorationEditorIconDefinition("axis", "Axis", "axis", new Guid("2ba07384-f67f-48a9-897c-901a51c690f3"), "X"),
            new DecorationEditorIconDefinition("threeAxis", "Three axis", "threeAxis", new Guid("2754a2f9-2523-4837-a67f-a3a247aede97"), "3"),
            new DecorationEditorIconDefinition("rotate", "Rotate", "spin", new Guid("c74e78ba-0d66-4b7c-932f-47cf9bc48bc3"), "R"),
            new DecorationEditorIconDefinition("scale", "Scale", "scales", new Guid("a243302c-f2ea-4f82-b937-393ac3036009"), "S"),
            new DecorationEditorIconDefinition("cube", "Cube", "esuCube", Guid.Empty, "C"),
            new DecorationEditorIconDefinition("draw", "Draw surface", "esuDraw", Guid.Empty, "D"),
            new DecorationEditorIconDefinition("path", "Path", "esuPath", Guid.Empty, "P"),
            new DecorationEditorIconDefinition("circle", "Circle", "esuCircle", Guid.Empty, "O"),
            new DecorationEditorIconDefinition("arc", "Arc", "esuArc", Guid.Empty, "A"),
            new DecorationEditorIconDefinition("cone2d", "2D cone", "esuCone2d", Guid.Empty, "2"),
            new DecorationEditorIconDefinition("sphere", "Sphere", "esuSphere", Guid.Empty, "S"),
            new DecorationEditorIconDefinition("partialSphere", "Partial sphere", "esuPartialSphere", Guid.Empty, "P"),
            new DecorationEditorIconDefinition("cone", "Cone", "esuCone", Guid.Empty, "C"),
            new DecorationEditorIconDefinition("frustum", "Frustum", "esuFrustum", Guid.Empty, "F"),
            new DecorationEditorIconDefinition("anchor", "Anchor", "pin", new Guid("6d2363fd-760c-4b3f-b4b6-a1803d1ede2c"), "A"),
            new DecorationEditorIconDefinition("paint", "Paint", "paint", new Guid("d1d370f1-f5c8-47cc-9471-15274e4c474c"), "P"),
            new DecorationEditorIconDefinition("brush", "Brush", "brush", new Guid("2349b49b-74ba-4ba1-986d-ea0ce6db383e"), "B"),
            new DecorationEditorIconDefinition("material", "Material", "materialfull", new Guid("bfed1af7-09ac-441e-9352-2c9346dcde2d"), "M"),
            new DecorationEditorIconDefinition("materialEmpty", "No material", "materialEmpty", new Guid("e5bdbd61-0a61-4f8d-ac3b-bb30b0314600"), "m"),
            new DecorationEditorIconDefinition("visibility", "Visibility", "standardEye", new Guid("c403c4cc-ed2a-4041-a100-1a32151d5d24"), "V"),
            new DecorationEditorIconDefinition("focus", "Focus", "focus", new Guid("2616f6ef-894b-4075-b49a-d6309fb57c7c"), "F"),
            new DecorationEditorIconDefinition("focusCamera", "Focus camera", "Focus camera", new Guid("2c10cd31-99ec-44cb-b25a-3eaaf8d8d55f"), "F"),
            new DecorationEditorIconDefinition("duplicate", "Duplicate", "duplicate", new Guid("3aef4997-5045-48b2-909b-006a0b0c0713"), "D"),
            new DecorationEditorIconDefinition("delete", "Delete", "delete", new Guid("157d2a08-9ec3-4ca7-8fb0-21ca7cd780e9"), "X"),
            new DecorationEditorIconDefinition("cancel", "Cancel", "cancel", new Guid("f71a9e09-53e0-4e2d-bdae-512705b4e72c"), "X"),
            new DecorationEditorIconDefinition("close", "Close", "esuClose", Guid.Empty, "X"),
            new DecorationEditorIconDefinition("save", "Save/apply", "save", new Guid("62b709ba-5c66-452e-a613-0d7d7292881b"), "S"),
            new DecorationEditorIconDefinition("settings", "Settings", "cogs", new Guid("ee0feae4-f36b-451e-b30d-b159aca123a2"), "G"),
            new DecorationEditorIconDefinition("gear", "Gear", "gear", new Guid("a13af71c-7120-4588-b908-abbd16b78e13"), "G"),
            new DecorationEditorIconDefinition("camera", "Camera", "camera", new Guid("ef38a3c1-69d3-427e-b3bc-cfebc0d5ed3b"), "C"),
            new DecorationEditorIconDefinition("mirror", "Mirror", "mirror", new Guid("582c17b5-9372-4247-933b-9b4568242c38"), "M"),
            new DecorationEditorIconDefinition("chevron1", "Chevron", "Chevron1", new Guid("45cc3ab4-2ae0-4103-bb57-101428306377"), ">"),
            new DecorationEditorIconDefinition("chevron2", "Chevron", "Chevron2", new Guid("8a970688-b2ce-4d8c-ac7e-cdd3e64e385b"), ">"),
            new DecorationEditorIconDefinition("chevron3", "Chevron", "Chevron3", new Guid("55f85f3a-333d-4992-be06-611912522128"), ">"),

            new DecorationEditorIconDefinition("undo", "Undo", "cancel", new Guid("f71a9e09-53e0-4e2d-bdae-512705b4e72c"), "U"),
            new DecorationEditorIconDefinition("redo", "Redo", "save", new Guid("62b709ba-5c66-452e-a613-0d7d7292881b"), "R"),
            new DecorationEditorIconDefinition("symmetryX", "X symmetry", "esuSymmetryX", Guid.Empty, "X"),
            new DecorationEditorIconDefinition("symmetryY", "Y symmetry", "esuSymmetryY", Guid.Empty, "Y"),
            new DecorationEditorIconDefinition("symmetryZ", "Z symmetry", "esuSymmetryZ", Guid.Empty, "Z"),

            // ESU-owned in-memory fallbacks for editor-only concepts.
            new DecorationEditorIconDefinition("outliner", "Outliner", "esuOutliner", Guid.Empty, "L"),
            new DecorationEditorIconDefinition("filter", "Filter", "esuFilter", Guid.Empty, "F"),
            new DecorationEditorIconDefinition("dirty", "Dirty", "esuDirty", Guid.Empty, "!"),
            new DecorationEditorIconDefinition("risk", "Serializer risk", "esuRisk", Guid.Empty, "!"),
            new DecorationEditorIconDefinition("lock", "Lock", "esuLock", Guid.Empty, "L"),
            new DecorationEditorIconDefinition("unlock", "Unlock", "esuUnlock", Guid.Empty, "U"),
            new DecorationEditorIconDefinition("count", "Decoration count", "esuCount", Guid.Empty, "#"),
            new DecorationEditorIconDefinition("mesh", "Mesh", "esuMesh", Guid.Empty, "D"),
        };

        private static readonly Dictionary<string, Texture2D> _icons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _runtimeIcons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        internal static IReadOnlyList<DecorationEditorIconDefinition> Definitions => _definitions;

        internal static Texture2D Get(string key)
        {
            key = NormalizeKey(key);

            Texture2D runtime = GetRuntimeIcon(key);
            if (runtime != null)
                return runtime;

            if (_icons.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            DecorationEditorIconDefinition definition = ResolveDefinition(key);
            Texture2D texture = CreateFallback(definition);
            _icons[key] = texture;
            return texture;
        }

        internal static Texture2D GetRuntimeIcon(string key)
        {
            key = NormalizeKey(key);
            if (_runtimeIcons.TryGetValue(key, out Texture2D cached))
                return cached;

            DecorationEditorIconDefinition definition = ResolveDefinition(key);
            Texture2D texture = TryFindRuntimeTexture(definition);
            _runtimeIcons[key] = texture;
            return texture;
        }

        private static string NormalizeKey(string key) =>
            string.IsNullOrEmpty(key) ? "settings" : key;

        private static DecorationEditorIconDefinition ResolveDefinition(string key) =>
            _definitions.FirstOrDefault(
                item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)) ??
            _definitions[0];

        private static Texture2D TryFindRuntimeTexture(DecorationEditorIconDefinition definition)
        {
            if (definition.Guid == Guid.Empty &&
                definition.FtdName.StartsWith("esu", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                string guid = definition.Guid == Guid.Empty
                    ? string.Empty
                    : definition.Guid.ToString("N");
                string guidPrefix = guid.Length >= 7 ? guid.Substring(0, 7) : guid;
                foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (texture == null || string.IsNullOrEmpty(texture.name))
                        continue;

                    string name = texture.name;
                    if (name.IndexOf(definition.FtdName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (!string.IsNullOrEmpty(guidPrefix) &&
                         name.IndexOf(guidPrefix, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return texture;
                    }
                }
            }
            catch
            {
                // Icon lookup must never block the editor. The generated fallback is stable.
            }

            return null;
        }

        private static Texture2D CreateFallback(DecorationEditorIconDefinition definition)
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ESU icon " + definition.Key,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color accent = PickAccent(definition.Key);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, clear);
            }

            DrawIcon(texture, definition.Key, definition.FallbackGlyph, accent);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static Color PickAccent(string key)
        {
            switch (key)
            {
                case "move":
                case "axis":
                case "threeAxis":
                case "cube":
                case "path":
                case "circle":
                case "arc":
                case "cone2d":
                case "sphere":
                case "partialSphere":
                case "cone":
                case "frustum":
                    return new Color(0.25f, 0.55f, 1f, 1f);
                case "paint":
                case "draw":
                    return new Color(0.15f, 0.95f, 1f, 1f);
                case "symmetryX":
                    return new Color(1f, 0.25f, 0.2f, 1f);
                case "symmetryY":
                    return new Color(0.25f, 1f, 0.35f, 1f);
                case "symmetryZ":
                    return new Color(0.3f, 0.65f, 1f, 1f);
                case "anchor":
                case "dirty":
                case "risk":
                    return new Color(1f, 0.72f, 0.2f, 1f);
                case "delete":
                case "cancel":
                case "close":
                    return new Color(1f, 0.25f, 0.2f, 1f);
                case "save":
                case "create":
                case "build":
                    return new Color(0.35f, 1f, 0.35f, 1f);
                default:
                    return new Color(0.92f, 1f, 1f, 1f);
            }
        }

        private static void DrawIcon(Texture2D texture, string key, string fallbackGlyph, Color color)
        {
            // Small, deterministic ESU-owned pixel icons. These are not FTD assets.
            switch (key)
            {
                case "open":
                    DrawDiamond(texture, 16, 16, 10, color);
                    DrawLine(texture, 13, 16, 19, 16, color);
                    break;
                case "build":
                    DrawHammer(texture, color);
                    break;
                case "create":
                    DrawPlus(texture, 16, 16, 8, color);
                    break;
                case "select":
                    DrawCircle(texture, 16, 16, 9, color);
                    DrawCircle(texture, 16, 16, 3, color);
                    DrawLine(texture, 16, 4, 16, 8, color);
                    DrawLine(texture, 16, 24, 16, 28, color);
                    DrawLine(texture, 4, 16, 8, 16, color);
                    DrawLine(texture, 24, 16, 28, 16, color);
                    break;
                case "boxSelect":
                    DrawBoxSelect(texture, color);
                    break;
                case "move":
                    DrawDoubleArrow(texture, 7, 16, 25, 16, color);
                    break;
                case "axis":
                case "threeAxis":
                    DrawAxes(texture);
                    break;
                case "rotate":
                    DrawRotateGizmoIcon(texture);
                    break;
                case "scale":
                    DrawRect(texture, 9, 9, 13, 13, color);
                    DrawLine(texture, 19, 19, 26, 26, color);
                    DrawLine(texture, 26, 26, 20, 26, color);
                    DrawLine(texture, 26, 26, 26, 20, color);
                    break;
                case "cube":
                    DrawCube(texture, color);
                    break;
                case "draw":
                    DrawSurfaceTriangle(texture, color);
                    break;
                case "path":
                    DrawPathIcon(texture, color);
                    break;
                case "circle":
                    DrawCircleIcon(texture, color);
                    break;
                case "arc":
                    DrawArcIcon(texture, color);
                    break;
                case "cone2d":
                    DrawCone2DIcon(texture, color);
                    break;
                case "sphere":
                    DrawSphereIcon(texture, color);
                    break;
                case "partialSphere":
                    DrawPartialSphereIcon(texture, color);
                    break;
                case "cone":
                    DrawConeIcon(texture, color);
                    break;
                case "frustum":
                    DrawFrustumIcon(texture, color);
                    break;
                case "anchor":
                    DrawCircle(texture, 16, 16, 8, color);
                    DrawLine(texture, 16, 5, 16, 27, color);
                    DrawLine(texture, 5, 16, 27, 16, color);
                    DrawLine(texture, 16, 16, 22, 10, color);
                    break;
                case "paint":
                    DrawPaintBucket(texture, color);
                    break;
                case "brush":
                    DrawBrush(texture, color);
                    break;
                case "material":
                    DrawCube(texture, color);
                    DrawFilledRect(texture, 12, 15, 10, 6, new Color(color.r, color.g, color.b, 0.45f));
                    break;
                case "materialEmpty":
                    DrawCube(texture, color);
                    DrawLine(texture, 9, 24, 24, 9, color);
                    break;
                case "visibility":
                    DrawEye(texture, color);
                    break;
                case "focus":
                case "focusCamera":
                    DrawFocus(texture, color);
                    break;
                case "duplicate":
                    DrawRect(texture, 8, 11, 13, 13, color);
                    DrawRect(texture, 12, 7, 13, 13, color);
                    break;
                case "delete":
                    DrawTrash(texture, color);
                    break;
                case "cancel":
                    DrawTrash(texture, color);
                    break;
                case "close":
                    DrawCross(texture, color);
                    break;
                case "save":
                    DrawFloppy(texture, color);
                    break;
                case "settings":
                case "gear":
                    DrawGear(texture, color);
                    break;
                case "camera":
                    DrawCamera(texture, color);
                    break;
                case "mirror":
                    DrawMirror(texture, color);
                    break;
                case "chevron1":
                case "chevron2":
                case "chevron3":
                    DrawChevron(texture, color);
                    break;
                case "undo":
                    DrawCurvedArrow(texture, true, color);
                    break;
                case "redo":
                    DrawCurvedArrow(texture, false, color);
                    break;
                case "symmetryX":
                    DrawSymmetryPlaneIcon(texture, DecorationEditAxis.X);
                    break;
                case "symmetryY":
                    DrawSymmetryPlaneIcon(texture, DecorationEditAxis.Y);
                    break;
                case "symmetryZ":
                    DrawSymmetryPlaneIcon(texture, DecorationEditAxis.Z);
                    break;
                case "outliner":
                    DrawList(texture, color);
                    break;
                case "filter":
                    DrawFunnel(texture, color);
                    break;
                case "dirty":
                case "risk":
                    DrawWarning(texture, color);
                    break;
                case "lock":
                    DrawLock(texture, false, color);
                    break;
                case "unlock":
                    DrawLock(texture, true, color);
                    break;
                case "count":
                    DrawHash(texture, color);
                    break;
                case "mesh":
                    DrawDiamond(texture, 16, 16, 10, color);
                    break;
                default:
                    DrawFallbackBadge(texture, fallbackGlyph, color);
                    break;
            }
        }

        private static void DrawPlus(Texture2D texture, int cx, int cy, int radius, Color color)
        {
            DrawLine(texture, cx, cy - radius, cx, cy + radius, color);
            DrawLine(texture, cx - radius, cy, cx + radius, cy, color);
        }

        private static void DrawCross(Texture2D texture, Color color)
        {
            DrawLine(texture, 9, 9, 23, 23, color);
            DrawLine(texture, 23, 9, 9, 23, color);
        }

        private static void DrawBoxSelect(Texture2D texture, Color color)
        {
            for (int x = 7; x <= 25; x += 4)
            {
                DrawLine(texture, x, 7, Mathf.Min(25, x + 2), 7, color);
                DrawLine(texture, x, 25, Mathf.Min(25, x + 2), 25, color);
            }

            for (int y = 7; y <= 25; y += 4)
            {
                DrawLine(texture, 7, y, 7, Mathf.Min(25, y + 2), color);
                DrawLine(texture, 25, y, 25, Mathf.Min(25, y + 2), color);
            }

            DrawLine(texture, 17, 17, 27, 27, color);
            DrawLine(texture, 27, 27, 21, 27, color);
            DrawLine(texture, 27, 27, 27, 21, color);
        }

        private static void DrawDoubleArrow(Texture2D texture, int x0, int y0, int x1, int y1, Color color)
        {
            DrawLine(texture, x0, y0, x1, y1, color);
            DrawLine(texture, x0, y0, x0 + 5, y0 - 5, color);
            DrawLine(texture, x0, y0, x0 + 5, y0 + 5, color);
            DrawLine(texture, x1, y1, x1 - 5, y1 - 5, color);
            DrawLine(texture, x1, y1, x1 - 5, y1 + 5, color);
        }

        private static void DrawAxes(Texture2D texture)
        {
            Color x = new Color(1f, 0.25f, 0.2f, 1f);
            Color y = new Color(0.3f, 1f, 0.35f, 1f);
            Color z = new Color(0.35f, 0.65f, 1f, 1f);
            DrawLine(texture, 15, 20, 26, 20, x);
            DrawLine(texture, 26, 20, 22, 16, x);
            DrawLine(texture, 26, 20, 22, 24, x);
            DrawLine(texture, 15, 20, 15, 7, y);
            DrawLine(texture, 15, 7, 11, 11, y);
            DrawLine(texture, 15, 7, 19, 11, y);
            DrawLine(texture, 15, 20, 7, 25, z);
            DrawLine(texture, 7, 25, 9, 20, z);
            DrawLine(texture, 7, 25, 13, 25, z);
        }

        private static void DrawRotateGizmoIcon(Texture2D texture)
        {
            Color x = new Color(1f, 0.25f, 0.2f, 1f);
            Color y = new Color(0.3f, 1f, 0.35f, 1f);
            Color z = new Color(0.35f, 0.65f, 1f, 1f);
            Color center = new Color(1f, 0.82f, 0.12f, 1f);
            DrawRotatedEllipse(texture, 16, 16, 11f, 4f, 0f, x);
            DrawRotatedEllipse(texture, 16, 16, 5f, 11f, 0f, z);
            DrawRotatedEllipse(texture, 16, 16, 10f, 5f, 38f, y);
            DrawCircle(texture, 16, 16, 3, center);
            DrawLine(texture, 16, 7, 16, 25, center);
            DrawLine(texture, 8, 16, 24, 16, center);
        }

        private static void DrawCube(Texture2D texture, Color color)
        {
            DrawRect(texture, 8, 12, 12, 12, color);
            DrawRect(texture, 13, 7, 12, 12, color);
            DrawLine(texture, 8, 12, 13, 7, color);
            DrawLine(texture, 20, 12, 25, 7, color);
            DrawLine(texture, 20, 24, 25, 19, color);
        }

        private static void DrawDiamond(Texture2D texture, int cx, int cy, int radius, Color color)
        {
            DrawLine(texture, cx, cy - radius, cx + radius, cy, color);
            DrawLine(texture, cx + radius, cy, cx, cy + radius, color);
            DrawLine(texture, cx, cy + radius, cx - radius, cy, color);
            DrawLine(texture, cx - radius, cy, cx, cy - radius, color);
        }

        private static void DrawSurfaceTriangle(Texture2D texture, Color color)
        {
            Vector2 a = new Vector2(8, 23);
            Vector2 b = new Vector2(16, 7);
            Vector2 c = new Vector2(26, 22);
            DrawLine(texture, (int)a.x, (int)a.y, (int)b.x, (int)b.y, color);
            DrawLine(texture, (int)b.x, (int)b.y, (int)c.x, (int)c.y, color);
            DrawLine(texture, (int)c.x, (int)c.y, (int)a.x, (int)a.y, color);
            DrawFilledCircle(texture, (int)a.x, (int)a.y, 2, color);
            DrawFilledCircle(texture, (int)b.x, (int)b.y, 2, color);
            DrawFilledCircle(texture, (int)c.x, (int)c.y, 2, color);
        }

        private static void DrawPathIcon(Texture2D texture, Color color)
        {
            DrawLine(texture, 6, 22, 13, 11, color);
            DrawLine(texture, 13, 11, 21, 17, color);
            DrawLine(texture, 21, 17, 27, 8, color);
            DrawFilledCircle(texture, 6, 22, 2, color);
            DrawFilledCircle(texture, 13, 11, 2, color);
            DrawFilledCircle(texture, 21, 17, 2, color);
            DrawFilledCircle(texture, 27, 8, 2, color);
        }

        private static void DrawCircleIcon(Texture2D texture, Color color)
        {
            DrawCircle(texture, 16, 16, 10, color);
            DrawFilledCircle(texture, 16, 16, 2, color);
        }

        private static void DrawArcIcon(Texture2D texture, Color color)
        {
            DrawArc(texture, 16, 17, 10, 205f, 25f, color);
            DrawLine(texture, 7, 20, 11, 25, color);
            DrawLine(texture, 25, 20, 21, 25, color);
        }

        private static void DrawCone2DIcon(Texture2D texture, Color color)
        {
            DrawLine(texture, 16, 22, 8, 9, color);
            DrawLine(texture, 16, 22, 25, 10, color);
            DrawArc(texture, 16, 22, 14, 122f, 58f, color);
            DrawFilledCircle(texture, 16, 22, 2, color);
        }

        private static void DrawSphereIcon(Texture2D texture, Color color)
        {
            DrawCircle(texture, 16, 16, 10, color);
            DrawRotatedEllipse(texture, 16, 16, 10f, 4f, 0f, color);
            DrawRotatedEllipse(texture, 16, 16, 4f, 10f, 0f, color);
        }

        private static void DrawPartialSphereIcon(Texture2D texture, Color color)
        {
            DrawArc(texture, 16, 16, 10, 200f, -20f, color);
            DrawArc(texture, 16, 16, 7, 200f, -20f, color);
            DrawLine(texture, 7, 19, 10, 23, color);
            DrawLine(texture, 25, 19, 22, 23, color);
        }

        private static void DrawConeIcon(Texture2D texture, Color color)
        {
            DrawRotatedEllipse(texture, 16, 23, 9f, 3f, 0f, color);
            DrawLine(texture, 7, 23, 16, 6, color);
            DrawLine(texture, 25, 23, 16, 6, color);
        }

        private static void DrawFrustumIcon(Texture2D texture, Color color)
        {
            DrawRotatedEllipse(texture, 16, 23, 9f, 3f, 0f, color);
            DrawRotatedEllipse(texture, 16, 9, 5f, 2f, 0f, color);
            DrawLine(texture, 7, 23, 11, 9, color);
            DrawLine(texture, 25, 23, 21, 9, color);
        }

        private static void DrawPaintBucket(Texture2D texture, Color color)
        {
            Color fill = new Color(0.15f, 0.95f, 1f, 0.65f);
            DrawLine(texture, 9, 12, 18, 7, color);
            DrawLine(texture, 18, 7, 25, 15, color);
            DrawLine(texture, 25, 15, 15, 24, color);
            DrawLine(texture, 15, 24, 7, 17, color);
            DrawLine(texture, 7, 17, 9, 12, color);
            DrawFilledRect(texture, 13, 14, 7, 5, fill);
            DrawLine(texture, 22, 19, 27, 25, color);
            DrawLine(texture, 27, 25, 29, 21, color);
            DrawFilledCircle(texture, 27, 25, 2, fill);
        }

        private static void DrawBrush(Texture2D texture, Color color)
        {
            DrawLine(texture, 9, 23, 21, 11, color);
            DrawLine(texture, 12, 26, 24, 14, color);
            DrawLine(texture, 21, 11, 24, 14, color);
            DrawLine(texture, 7, 25, 12, 26, color);
            DrawLine(texture, 7, 25, 9, 20, color);
        }

        private static void DrawEye(Texture2D texture, Color color)
        {
            DrawLine(texture, 5, 16, 11, 10, color);
            DrawLine(texture, 11, 10, 21, 10, color);
            DrawLine(texture, 21, 10, 27, 16, color);
            DrawLine(texture, 27, 16, 21, 22, color);
            DrawLine(texture, 21, 22, 11, 22, color);
            DrawLine(texture, 11, 22, 5, 16, color);
            DrawFilledCircle(texture, 16, 16, 3, color);
        }

        private static void DrawFocus(Texture2D texture, Color color)
        {
            DrawLine(texture, 7, 7, 14, 7, color);
            DrawLine(texture, 7, 7, 7, 14, color);
            DrawLine(texture, 25, 7, 18, 7, color);
            DrawLine(texture, 25, 7, 25, 14, color);
            DrawLine(texture, 7, 25, 14, 25, color);
            DrawLine(texture, 7, 25, 7, 18, color);
            DrawLine(texture, 25, 25, 18, 25, color);
            DrawLine(texture, 25, 25, 25, 18, color);
            DrawCircle(texture, 16, 16, 3, color);
        }

        private static void DrawTrash(Texture2D texture, Color color)
        {
            DrawLine(texture, 10, 11, 24, 11, color);
            DrawLine(texture, 13, 8, 21, 8, color);
            DrawLine(texture, 15, 6, 19, 6, color);
            DrawLine(texture, 12, 12, 14, 26, color);
            DrawLine(texture, 22, 12, 20, 26, color);
            DrawLine(texture, 14, 26, 20, 26, color);
            DrawLine(texture, 16, 14, 16, 24, color);
            DrawLine(texture, 19, 14, 19, 24, color);
        }

        private static void DrawFloppy(Texture2D texture, Color color)
        {
            DrawRect(texture, 8, 7, 17, 19, color);
            DrawLine(texture, 11, 7, 11, 14, color);
            DrawLine(texture, 11, 14, 21, 14, color);
            DrawLine(texture, 21, 7, 21, 14, color);
            DrawRect(texture, 12, 19, 9, 7, color);
            DrawLine(texture, 24, 7, 25, 8, color);
            DrawLine(texture, 25, 8, 25, 26, color);
        }

        private static void DrawGear(Texture2D texture, Color color)
        {
            DrawCircle(texture, 16, 16, 7, color);
            DrawCircle(texture, 16, 16, 3, color);
            DrawLine(texture, 16, 5, 16, 9, color);
            DrawLine(texture, 16, 23, 16, 27, color);
            DrawLine(texture, 5, 16, 9, 16, color);
            DrawLine(texture, 23, 16, 27, 16, color);
            DrawLine(texture, 8, 8, 11, 11, color);
            DrawLine(texture, 24, 8, 21, 11, color);
            DrawLine(texture, 8, 24, 11, 21, color);
            DrawLine(texture, 24, 24, 21, 21, color);
        }

        private static void DrawCamera(Texture2D texture, Color color)
        {
            DrawRect(texture, 7, 12, 18, 11, color);
            DrawRect(texture, 11, 9, 7, 4, color);
            DrawCircle(texture, 16, 17, 4, color);
            DrawLine(texture, 24, 15, 28, 13, color);
            DrawLine(texture, 24, 20, 28, 22, color);
            DrawLine(texture, 28, 13, 28, 22, color);
        }

        private static void DrawMirror(Texture2D texture, Color color)
        {
            DrawLine(texture, 16, 5, 16, 27, color);
            DrawLine(texture, 6, 16, 13, 10, color);
            DrawLine(texture, 6, 16, 13, 22, color);
            DrawLine(texture, 26, 16, 19, 10, color);
            DrawLine(texture, 26, 16, 19, 22, color);
        }

        private static void DrawChevron(Texture2D texture, Color color)
        {
            DrawLine(texture, 12, 8, 21, 16, color);
            DrawLine(texture, 21, 16, 12, 24, color);
        }

        private static void DrawCurvedArrow(Texture2D texture, bool left, Color color)
        {
            DrawArc(texture, 16, 17, 8, left ? 35f : 145f, left ? 305f : -125f, color);
            if (left)
            {
                DrawLine(texture, 7, 14, 13, 9, color);
                DrawLine(texture, 7, 14, 13, 18, color);
            }
            else
            {
                DrawLine(texture, 25, 14, 19, 9, color);
                DrawLine(texture, 25, 14, 19, 18, color);
            }
        }

        private static void DrawSymmetryPlaneIcon(Texture2D texture, DecorationEditAxis axis)
        {
            Color plane = new Color(0.85f, 1f, 1f, 1f);
            Color axisColor = AxisIconColor(axis);
            DrawLine(texture, 7, 11, 19, 6, plane);
            DrawLine(texture, 19, 6, 26, 18, plane);
            DrawLine(texture, 26, 18, 13, 25, plane);
            DrawLine(texture, 13, 25, 7, 11, plane);
            DrawLine(texture, 13, 9, 20, 21, plane);
            switch (axis)
            {
                case DecorationEditAxis.X:
                    DrawLine(texture, 7, 18, 25, 18, axisColor);
                    DrawLine(texture, 25, 18, 21, 14, axisColor);
                    DrawLine(texture, 25, 18, 21, 22, axisColor);
                    break;
                case DecorationEditAxis.Y:
                    DrawLine(texture, 16, 25, 16, 6, axisColor);
                    DrawLine(texture, 16, 6, 12, 10, axisColor);
                    DrawLine(texture, 16, 6, 20, 10, axisColor);
                    break;
                default:
                    DrawLine(texture, 7, 24, 23, 8, axisColor);
                    DrawLine(texture, 23, 8, 17, 9, axisColor);
                    DrawLine(texture, 23, 8, 22, 14, axisColor);
                    break;
            }
        }

        private static Color AxisIconColor(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return new Color(1f, 0.25f, 0.2f, 1f);
                case DecorationEditAxis.Y:
                    return new Color(0.3f, 1f, 0.35f, 1f);
                default:
                    return new Color(0.35f, 0.65f, 1f, 1f);
            }
        }

        private static void DrawList(Texture2D texture, Color color)
        {
            for (int y = 9; y <= 23; y += 7)
            {
                DrawFilledRect(texture, 7, y, 3, 3, color);
                DrawLine(texture, 13, y + 1, 25, y + 1, color);
            }
        }

        private static void DrawFunnel(Texture2D texture, Color color)
        {
            DrawLine(texture, 7, 8, 25, 8, color);
            DrawLine(texture, 7, 8, 14, 17, color);
            DrawLine(texture, 25, 8, 18, 17, color);
            DrawLine(texture, 14, 17, 14, 25, color);
            DrawLine(texture, 18, 17, 18, 22, color);
            DrawLine(texture, 14, 25, 18, 22, color);
        }

        private static void DrawWarning(Texture2D texture, Color color)
        {
            DrawLine(texture, 16, 6, 26, 25, color);
            DrawLine(texture, 26, 25, 6, 25, color);
            DrawLine(texture, 6, 25, 16, 6, color);
            DrawLine(texture, 16, 13, 16, 19, color);
            DrawFilledRect(texture, 15, 22, 3, 3, color);
        }

        private static void DrawLock(Texture2D texture, bool open, Color color)
        {
            DrawRect(texture, 9, 14, 14, 11, color);
            DrawLine(texture, 12, 14, 12, 10, color);
            DrawLine(texture, 12, 10, 16, 7, color);
            if (open)
            {
                DrawLine(texture, 16, 7, 21, 9, color);
                DrawLine(texture, 21, 9, 22, 12, color);
            }
            else
            {
                DrawLine(texture, 16, 7, 20, 10, color);
                DrawLine(texture, 20, 10, 20, 14, color);
            }
            DrawLine(texture, 16, 18, 16, 22, color);
        }

        private static void DrawHash(Texture2D texture, Color color)
        {
            DrawLine(texture, 12, 8, 10, 24, color);
            DrawLine(texture, 22, 8, 20, 24, color);
            DrawLine(texture, 8, 13, 24, 13, color);
            DrawLine(texture, 7, 20, 23, 20, color);
        }

        private static void DrawHammer(Texture2D texture, Color color)
        {
            DrawLine(texture, 9, 10, 18, 5, color);
            DrawLine(texture, 18, 5, 24, 11, color);
            DrawLine(texture, 20, 13, 25, 8, color);
            DrawLine(texture, 14, 14, 24, 24, color);
            DrawLine(texture, 11, 17, 21, 27, color);
        }

        private static void DrawFallbackBadge(Texture2D texture, string glyph, Color color)
        {
            DrawRect(texture, 9, 8, 14, 17, color);
            if (!string.IsNullOrEmpty(glyph))
                DrawLine(texture, 13, 17, 19, 17, color);
        }

        private static void DrawCircle(Texture2D texture, int cx, int cy, int radius, Color color)
        {
            int radiusSquared = radius * radius;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    int dx = x - cx;
                    int dy = y - cy;
                    int distance = dx * dx + dy * dy;
                    if (Math.Abs(distance - radiusSquared) <= radius)
                        Set(texture, x, y, color);
                }
            }
        }

        private static void DrawFilledCircle(Texture2D texture, int cx, int cy, int radius, Color color)
        {
            int radiusSquared = radius * radius;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    int dx = x - cx;
                    int dy = y - cy;
                    if (dx * dx + dy * dy <= radiusSquared)
                        Set(texture, x, y, color);
                }
            }
        }

        private static void DrawArc(
            Texture2D texture,
            int cx,
            int cy,
            int radius,
            float startDegrees,
            float endDegrees,
            Color color)
        {
            float delta = endDegrees >= startDegrees ? 1f : -1f;
            int previousX = cx + Mathf.RoundToInt(Mathf.Cos(startDegrees * Mathf.Deg2Rad) * radius);
            int previousY = cy + Mathf.RoundToInt(Mathf.Sin(startDegrees * Mathf.Deg2Rad) * radius);
            for (float degrees = startDegrees + delta * 6f;
                 delta > 0f ? degrees <= endDegrees : degrees >= endDegrees;
                 degrees += delta * 6f)
            {
                int x = cx + Mathf.RoundToInt(Mathf.Cos(degrees * Mathf.Deg2Rad) * radius);
                int y = cy + Mathf.RoundToInt(Mathf.Sin(degrees * Mathf.Deg2Rad) * radius);
                DrawLine(texture, previousX, previousY, x, y, color);
                previousX = x;
                previousY = y;
            }
        }

        private static void DrawRotatedEllipse(
            Texture2D texture,
            int cx,
            int cy,
            float radiusX,
            float radiusY,
            float degrees,
            Color color)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 previous = EllipsePoint(cx, cy, radiusX, radiusY, cos, sin, 0f);
            for (int index = 1; index <= 40; index++)
            {
                float angle = index * Mathf.PI * 2f / 40f;
                Vector2 next = EllipsePoint(cx, cy, radiusX, radiusY, cos, sin, angle);
                DrawLine(
                    texture,
                    Mathf.RoundToInt(previous.x),
                    Mathf.RoundToInt(previous.y),
                    Mathf.RoundToInt(next.x),
                    Mathf.RoundToInt(next.y),
                    color);
                previous = next;
            }
        }

        private static Vector2 EllipsePoint(
            int cx,
            int cy,
            float radiusX,
            float radiusY,
            float cos,
            float sin,
            float angle)
        {
            float x = Mathf.Cos(angle) * radiusX;
            float y = Mathf.Sin(angle) * radiusY;
            return new Vector2(
                cx + x * cos - y * sin,
                cy + x * sin + y * cos);
        }

        private static void DrawRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            DrawLine(texture, x, y, x + width, y, color);
            DrawLine(texture, x + width, y, x + width, y + height, color);
            DrawLine(texture, x + width, y + height, x, y + height, color);
            DrawLine(texture, x, y + height, x, y, color);
        }

        private static void DrawFilledRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                    Set(texture, xx, yy, color);
            }
        }

        private static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                Set(texture, x0, y0, color);
                Set(texture, x0 + 1, y0, color);
                Set(texture, x0, y0 + 1, color);
                if (x0 == x1 && y0 == y1)
                    break;
                int e2 = 2 * error;
                if (e2 >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static void Set(Texture2D texture, int x, int y, Color color)
        {
            if (x >= 0 && y >= 0 && x < texture.width && y < texture.height)
                texture.SetPixel(x, y, color);
        }
    }
}
