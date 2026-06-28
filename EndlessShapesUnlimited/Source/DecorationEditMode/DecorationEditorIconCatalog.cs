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
            new DecorationEditorIconDefinition("select", "Select", "crosshair", new Guid("f417ee2c-2aa4-4fb2-ab9f-de4c59b94e45"), "◎"),
            new DecorationEditorIconDefinition("move", "Move", "move", new Guid("68419445-57e1-41ac-89c9-7683976ddcff"), "↔"),
            new DecorationEditorIconDefinition("axis", "Axis", "axis", new Guid("2ba07384-f67f-48a9-897c-901a51c690f3"), "X"),
            new DecorationEditorIconDefinition("threeAxis", "Three axis", "threeAxis", new Guid("2754a2f9-2523-4837-a67f-a3a247aede97"), "3"),
            new DecorationEditorIconDefinition("rotate", "Rotate", "spin", new Guid("c74e78ba-0d66-4b7c-932f-47cf9bc48bc3"), "⟳"),
            new DecorationEditorIconDefinition("scale", "Scale", "scales", new Guid("a243302c-f2ea-4f82-b937-393ac3036009"), "S"),
            new DecorationEditorIconDefinition("anchor", "Anchor", "pin", new Guid("6d2363fd-760c-4b3f-b4b6-a1803d1ede2c"), "⌖"),
            new DecorationEditorIconDefinition("paint", "Paint", "paint", new Guid("d1d370f1-f5c8-47cc-9471-15274e4c474c"), "P"),
            new DecorationEditorIconDefinition("brush", "Brush", "brush", new Guid("2349b49b-74ba-4ba1-986d-ea0ce6db383e"), "✎"),
            new DecorationEditorIconDefinition("material", "Material", "materialfull", new Guid("bfed1af7-09ac-441e-9352-2c9346dcde2d"), "M"),
            new DecorationEditorIconDefinition("materialEmpty", "No material", "materialEmpty", new Guid("e5bdbd61-0a61-4f8d-ac3b-bb30b0314600"), "m"),
            new DecorationEditorIconDefinition("visibility", "Visibility", "standardEye", new Guid("c403c4cc-ed2a-4041-a100-1a32151d5d24"), "◉"),
            new DecorationEditorIconDefinition("focus", "Focus", "focus", new Guid("2616f6ef-894b-4075-b49a-d6309fb57c7c"), "F"),
            new DecorationEditorIconDefinition("focusCamera", "Focus camera", "Focus camera", new Guid("2c10cd31-99ec-44cb-b25a-3eaaf8d8d55f"), "▣"),
            new DecorationEditorIconDefinition("duplicate", "Duplicate", "duplicate", new Guid("3aef4997-5045-48b2-909b-006a0b0c0713"), "⧉"),
            new DecorationEditorIconDefinition("delete", "Delete", "delete", new Guid("157d2a08-9ec3-4ca7-8fb0-21ca7cd780e9"), "×"),
            new DecorationEditorIconDefinition("cancel", "Cancel", "cancel", new Guid("f71a9e09-53e0-4e2d-bdae-512705b4e72c"), "↶"),
            new DecorationEditorIconDefinition("save", "Save/apply", "save", new Guid("62b709ba-5c66-452e-a613-0d7d7292881b"), "✓"),
            new DecorationEditorIconDefinition("settings", "Settings", "cogs", new Guid("ee0feae4-f36b-451e-b30d-b159aca123a2"), "⚙"),
            new DecorationEditorIconDefinition("gear", "Gear", "gear", new Guid("a13af71c-7120-4588-b908-abbd16b78e13"), "⚙"),
            new DecorationEditorIconDefinition("camera", "Camera", "camera", new Guid("ef38a3c1-69d3-427e-b3bc-cfebc0d5ed3b"), "▣"),
            new DecorationEditorIconDefinition("mirror", "Mirror", "mirror", new Guid("582c17b5-9372-4247-933b-9b4568242c38"), "⇋"),
            new DecorationEditorIconDefinition("chevron1", "Chevron", "Chevron1", new Guid("45cc3ab4-2ae0-4103-bb57-101428306377"), "›"),
            new DecorationEditorIconDefinition("chevron2", "Chevron", "Chevron2", new Guid("8a970688-b2ce-4d8c-ac7e-cdd3e64e385b"), "»"),
            new DecorationEditorIconDefinition("chevron3", "Chevron", "Chevron3", new Guid("55f85f3a-333d-4992-be06-611912522128"), "≫"),

            new DecorationEditorIconDefinition("undo", "Undo", "cancel", new Guid("f71a9e09-53e0-4e2d-bdae-512705b4e72c"), "U"),
            new DecorationEditorIconDefinition("redo", "Redo", "save", new Guid("62b709ba-5c66-452e-a613-0d7d7292881b"), "R"),

            // ESU-owned in-memory fallbacks for editor-only concepts.
            new DecorationEditorIconDefinition("outliner", "Outliner", "esuOutliner", Guid.Empty, "☰"),
            new DecorationEditorIconDefinition("filter", "Filter", "esuFilter", Guid.Empty, "⌕"),
            new DecorationEditorIconDefinition("dirty", "Dirty", "esuDirty", Guid.Empty, "!"),
            new DecorationEditorIconDefinition("risk", "Serializer risk", "esuRisk", Guid.Empty, "△"),
            new DecorationEditorIconDefinition("lock", "Lock", "esuLock", Guid.Empty, "L"),
            new DecorationEditorIconDefinition("unlock", "Unlock", "esuUnlock", Guid.Empty, "U"),
            new DecorationEditorIconDefinition("count", "Decoration count", "esuCount", Guid.Empty, "#"),
            new DecorationEditorIconDefinition("mesh", "Mesh", "esuMesh", Guid.Empty, "◆"),
        };

        private static readonly Dictionary<string, Texture2D> _icons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _runtimeIcons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        internal static IReadOnlyList<DecorationEditorIconDefinition> Definitions => _definitions;

        internal static Texture2D Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = "settings";

            if (_icons.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            DecorationEditorIconDefinition definition = _definitions.FirstOrDefault(
                item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)) ??
                _definitions[0];

            Texture2D texture = TryFindRuntimeTexture(definition) ??
                                CreateFallback(definition);
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
            if (definition.Guid == Guid.Empty && definition.FtdName.StartsWith("esu", StringComparison.OrdinalIgnoreCase))
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
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color border = new Color(0.1f, 0.95f, 1f, 0.95f);
            Color fill = new Color(0f, 0.18f, 0.22f, 0.86f);
            Color accent = PickAccent(definition.Key);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    bool inner = x > 2 && y > 2 && x < size - 3 && y < size - 3;
                    texture.SetPixel(x, y, edge ? border : inner ? fill : clear);
                }
            }

            DrawGlyph(texture, definition.FallbackGlyph, accent);
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
                    return new Color(0.25f, 0.55f, 1f, 1f);
                case "anchor":
                case "dirty":
                case "risk":
                    return new Color(1f, 0.72f, 0.2f, 1f);
                case "delete":
                case "cancel":
                    return new Color(1f, 0.25f, 0.2f, 1f);
                case "save":
                case "create":
                case "build":
                    return new Color(0.35f, 1f, 0.35f, 1f);
                default:
                    return Color.white;
            }
        }

        private static void DrawGlyph(Texture2D texture, string glyph, Color color)
        {
            // Small, deterministic pixel glyphs. These are not FTD assets.
            switch (glyph)
            {
                case "+":
                    DrawLine(texture, 16, 8, 16, 24, color);
                    DrawLine(texture, 8, 16, 24, 16, color);
                    break;
                case "×":
                    DrawLine(texture, 9, 9, 23, 23, color);
                    DrawLine(texture, 23, 9, 9, 23, color);
                    break;
                case "↔":
                    DrawLine(texture, 7, 16, 25, 16, color);
                    DrawLine(texture, 7, 16, 12, 11, color);
                    DrawLine(texture, 7, 16, 12, 21, color);
                    DrawLine(texture, 25, 16, 20, 11, color);
                    DrawLine(texture, 25, 16, 20, 21, color);
                    break;
                case "◎":
                    DrawCircle(texture, 16, 16, 9, color);
                    DrawCircle(texture, 16, 16, 3, color);
                    break;
                case "⌖":
                    DrawCircle(texture, 16, 16, 8, color);
                    DrawLine(texture, 16, 6, 16, 26, color);
                    DrawLine(texture, 6, 16, 26, 16, color);
                    break;
                case "☰":
                    DrawLine(texture, 8, 10, 24, 10, color);
                    DrawLine(texture, 8, 16, 24, 16, color);
                    DrawLine(texture, 8, 22, 24, 22, color);
                    break;
                case "△":
                    DrawLine(texture, 16, 7, 25, 24, color);
                    DrawLine(texture, 25, 24, 7, 24, color);
                    DrawLine(texture, 7, 24, 16, 7, color);
                    DrawLine(texture, 16, 13, 16, 18, color);
                    texture.SetPixel(16, 22, color);
                    break;
                case "◆":
                    DrawLine(texture, 16, 6, 26, 16, color);
                    DrawLine(texture, 26, 16, 16, 26, color);
                    DrawLine(texture, 16, 26, 6, 16, color);
                    DrawLine(texture, 6, 16, 16, 6, color);
                    break;
                default:
                    DrawBoxLetter(texture, glyph, color);
                    break;
            }
        }

        private static void DrawBoxLetter(Texture2D texture, string glyph, Color color)
        {
            DrawLine(texture, 10, 8, 22, 8, color);
            DrawLine(texture, 10, 24, 22, 24, color);
            DrawLine(texture, 10, 8, 10, 24, color);
            DrawLine(texture, 22, 8, 22, 24, color);
            if (!string.IsNullOrEmpty(glyph) && (glyph[0] == 'S' || glyph[0] == 'E' || glyph[0] == 'F'))
                DrawLine(texture, 10, 16, 20, 16, color);
            if (!string.IsNullOrEmpty(glyph) && (glyph[0] == 'M' || glyph[0] == 'W'))
            {
                DrawLine(texture, 10, 24, 16, 10, color);
                DrawLine(texture, 16, 10, 22, 24, color);
            }
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
