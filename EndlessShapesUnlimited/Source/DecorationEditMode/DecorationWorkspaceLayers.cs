using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.Presets;
using Newtonsoft.Json;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationLayerDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("visible")]
        public bool Visible { get; set; } = true;

        [JsonProperty("locked")]
        public bool Locked { get; set; }

        [JsonProperty("folder")]
        public string Folder { get; set; } = string.Empty;

        [JsonProperty("tags")]
        public string[] Tags { get; set; } = Array.Empty<string>();

        internal DecorationLayerDefinition Copy() =>
            new DecorationLayerDefinition
            {
                Name = Name,
                Visible = Visible,
                Locked = Locked,
                Folder = Folder ?? string.Empty,
                Tags = Tags == null ? Array.Empty<string>() : (string[])Tags.Clone()
            };
    }

    internal sealed class DecorationLayerAssignment
    {
        [JsonProperty("layer")]
        public string Layer { get; set; } = string.Empty;

        [JsonProperty("locked")]
        public bool Locked { get; set; }

        [JsonProperty("tags")]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    internal sealed class DecorationLayerDocument
    {
        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = DecorationLayerWorkspace.CurrentSchemaVersion;

        [JsonProperty("isolated_layer")]
        public string IsolatedLayer { get; set; } = string.Empty;

        [JsonProperty("layers")]
        public List<DecorationLayerDefinition> Layers { get; set; } =
            new List<DecorationLayerDefinition>();

        [JsonProperty("objects")]
        public Dictionary<string, DecorationLayerAssignment> Objects { get; set; } =
            new Dictionary<string, DecorationLayerAssignment>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Profile-persistent named layers, tags, visibility, and edit locks. Object keys are
    /// based on FTD's persisted decoration UniqueId plus a stable construct identity when
    /// available; the transform signature fallback is only used for legacy zero-ID objects.
    /// </summary>
    internal sealed class DecorationLayerWorkspace
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int MaximumLayers = 256;
        internal const int MaximumObjects = 250000;
        internal const int MaximumDocumentBytes = 32 * 1024 * 1024;
        private const int MaximumNameLength = 64;
        private const int MaximumTags = 16;
        private const int MaximumTagLength = 32;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object DefaultSync = new object();
        private static DecorationLayerWorkspace s_default;
        private readonly object _sync = new object();
        private DecorationLayerDocument _document;

        internal DecorationLayerWorkspace(string profileRoot)
        {
            if (string.IsNullOrWhiteSpace(profileRoot))
                throw new ArgumentException("A profile root is required.", nameof(profileRoot));
            DirectoryPath = Path.Combine(
                Path.GetFullPath(profileRoot),
                "EndlessShapesUnlimited",
                "Workspace");
            FilePath = Path.Combine(DirectoryPath, "decoration-layers.json");
            BackupPath = FilePath + ".backup";
        }

        internal string DirectoryPath { get; }

        internal string FilePath { get; }

        internal string BackupPath { get; }

        internal static DecorationLayerWorkspace Default
        {
            get
            {
                lock (DefaultSync)
                {
                    if (s_default != null)
                        return s_default;
                    string presets = EsuPresetLibrary.Default.DirectoryPath;
                    string profileRoot = Directory.GetParent(Directory.GetParent(presets).FullName).FullName;
                    return s_default = new DecorationLayerWorkspace(profileRoot);
                }
            }
        }

        internal IReadOnlyList<DecorationLayerDefinition> Layers
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return _document.Layers
                        .OrderBy(layer => layer.Folder, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(layer => layer.Copy())
                        .ToArray();
                }
            }
        }

        internal string IsolatedLayer
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return _document.IsolatedLayer ?? string.Empty;
                }
            }
        }

        internal bool TryCreateLayer(string name, out string message) =>
            TryCreateLayer(name, string.Empty, out message);

        internal bool TryCreateLayer(
            string name,
            string folder,
            out string message)
        {
            if (!TryNormalizeName(name, out string normalized, out message) ||
                !TryNormalizeOptionalFolder(folder, out string normalizedFolder, out message))
                return false;
            lock (_sync)
            {
                EnsureLoaded();
                if (_document.Layers.Any(layer =>
                        string.Equals(layer.Name, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    message = "Layer '" + normalized + "' already exists.";
                    return false;
                }
                if (_document.Layers.Count >= MaximumLayers)
                {
                    message = "The layer workspace has reached its 256-layer safety limit.";
                    return false;
                }

                _document.Layers.Add(
                    new DecorationLayerDefinition
                    {
                        Name = normalized,
                        Folder = normalizedFolder
                    });
                if (!TrySave(out message))
                {
                    _document.Layers.RemoveAt(_document.Layers.Count - 1);
                    return false;
                }
                message = "Created layer '" + normalized + "'" +
                          (normalizedFolder.Length == 0
                              ? "."
                              : " in folder '" + normalizedFolder + "'.");
                return true;
            }
        }

        internal bool TrySetLayerFolder(
            string layerName,
            string folder,
            out string message)
        {
            if (!TryNormalizeOptionalFolder(folder, out string normalizedFolder, out message))
                return false;

            lock (_sync)
            {
                EnsureLoaded();
                DecorationLayerDefinition layer = FindLayer(layerName);
                if (layer == null)
                {
                    message = "Layer was not found.";
                    return false;
                }

                if (string.Equals(layer.Folder, normalizedFolder, StringComparison.Ordinal))
                {
                    message = normalizedFolder.Length == 0
                        ? "Layer is already outside a folder."
                        : "Layer is already in folder '" + normalizedFolder + "'.";
                    return true;
                }

                string previous = layer.Folder ?? string.Empty;
                layer.Folder = normalizedFolder;
                if (!TrySave(out message))
                {
                    layer.Folder = previous;
                    return false;
                }

                message = normalizedFolder.Length == 0
                    ? "Removed layer '" + layer.Name + "' from its folder."
                    : "Moved layer '" + layer.Name + "' to folder '" + normalizedFolder + "'.";
                return true;
            }
        }

        internal bool TryDeleteLayer(string name, out string message)
        {
            lock (_sync)
            {
                EnsureLoaded();
                DecorationLayerDefinition layer = FindLayer(name);
                if (layer == null)
                {
                    message = "Layer was not found.";
                    return false;
                }

                var previous = _document;
                _document = CloneDocument(_document);
                _document.Layers.RemoveAll(candidate =>
                    string.Equals(candidate.Name, layer.Name, StringComparison.OrdinalIgnoreCase));
                foreach (string key in _document.Objects
                             .Where(pair => string.Equals(
                                 pair.Value?.Layer,
                                 layer.Name,
                                 StringComparison.OrdinalIgnoreCase))
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    DecorationLayerAssignment assignment = _document.Objects[key];
                    assignment.Layer = string.Empty;
                    if (!assignment.Locked &&
                        (assignment.Tags == null || assignment.Tags.Length == 0))
                    {
                        _document.Objects.Remove(key);
                    }
                }
                if (string.Equals(_document.IsolatedLayer, layer.Name, StringComparison.OrdinalIgnoreCase))
                    _document.IsolatedLayer = string.Empty;
                if (!TrySave(out message))
                {
                    _document = previous;
                    return false;
                }
                message = "Deleted layer '" + layer.Name +
                          "' and cleared its layer assignments; object locks and tags were preserved.";
                return true;
            }
        }

        internal bool TryAssign(
            IEnumerable<string> objectKeys,
            string layerName,
            out string message)
        {
            string[] keys = NormalizeObjectKeys(objectKeys);
            if (keys.Length == 0)
            {
                message = "Select at least one decoration to assign a layer.";
                return false;
            }
            lock (_sync)
            {
                EnsureLoaded();
                DecorationLayerDefinition layer = FindLayer(layerName);
                if (layer == null)
                {
                    message = "Create or select a valid layer first.";
                    return false;
                }
                int newCount = keys.Count(key => !_document.Objects.ContainsKey(key));
                if ((long)_document.Objects.Count + newCount > MaximumObjects)
                {
                    message = "Layer assignments would exceed the 250,000-object safety limit.";
                    return false;
                }

                DecorationLayerDocument previous = CloneDocument(_document);
                foreach (string key in keys)
                {
                    if (!_document.Objects.TryGetValue(key, out DecorationLayerAssignment assignment) ||
                        assignment == null)
                    {
                        assignment = new DecorationLayerAssignment();
                        _document.Objects[key] = assignment;
                    }
                    assignment.Layer = layer.Name;
                }
                if (!TrySave(out message))
                {
                    _document = previous;
                    return false;
                }
                message = "Assigned " + keys.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s) to '" + layer.Name + "'.";
                return true;
            }
        }

        internal bool TrySetObjectLock(
            IEnumerable<string> objectKeys,
            bool locked,
            out string message)
        {
            string[] keys = NormalizeObjectKeys(objectKeys);
            if (keys.Length == 0)
            {
                message = "Select at least one decoration to change its lock.";
                return false;
            }
            lock (_sync)
            {
                EnsureLoaded();
                int newCount = keys.Count(key => !_document.Objects.ContainsKey(key));
                if ((long)_document.Objects.Count + newCount > MaximumObjects)
                {
                    message = "Object locks would exceed the workspace safety limit.";
                    return false;
                }

                DecorationLayerDocument previous = CloneDocument(_document);
                foreach (string key in keys)
                {
                    if (!_document.Objects.TryGetValue(key, out DecorationLayerAssignment assignment) ||
                        assignment == null)
                    {
                        assignment = new DecorationLayerAssignment();
                        _document.Objects[key] = assignment;
                    }
                    assignment.Locked = locked;
                }
                if (!TrySave(out message))
                {
                    _document = previous;
                    return false;
                }
                message = (locked ? "Locked " : "Unlocked ") +
                          keys.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s).";
                return true;
            }
        }

        internal bool TrySetObjectTags(
            IEnumerable<string> objectKeys,
            IEnumerable<string> tags,
            out string message)
        {
            string[] keys = NormalizeObjectKeys(objectKeys);
            message = "Tags are invalid.";
            if (keys.Length == 0 || !TryNormalizeTags(tags, out string[] normalized, out message))
            {
                if (keys.Length == 0)
                    message = "Select at least one decoration to assign tags.";
                return false;
            }
            lock (_sync)
            {
                EnsureLoaded();
                DecorationLayerDocument previous = CloneDocument(_document);
                foreach (string key in keys)
                {
                    if (!_document.Objects.TryGetValue(key, out DecorationLayerAssignment assignment) ||
                        assignment == null)
                    {
                        assignment = new DecorationLayerAssignment();
                        _document.Objects[key] = assignment;
                    }
                    assignment.Tags = normalized;
                }
                if (!TrySave(out message))
                {
                    _document = previous;
                    return false;
                }
                message = "Updated tags on " + keys.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s).";
                return true;
            }
        }

        internal bool TrySetLayerState(
            string layerName,
            bool? visible,
            bool? locked,
            out string message)
        {
            lock (_sync)
            {
                EnsureLoaded();
                DecorationLayerDefinition layer = FindLayer(layerName);
                if (layer == null)
                {
                    message = "Layer was not found.";
                    return false;
                }
                DecorationLayerDocument previous = CloneDocument(_document);
                if (visible.HasValue)
                    layer.Visible = visible.Value;
                if (locked.HasValue)
                    layer.Locked = locked.Value;
                if (!TrySave(out message))
                {
                    _document = previous;
                    return false;
                }
                message = "Updated layer '" + layer.Name + "'.";
                return true;
            }
        }

        internal bool TrySetIsolatedLayer(string layerName, out string message)
        {
            lock (_sync)
            {
                EnsureLoaded();
                string normalized = string.Empty;
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    DecorationLayerDefinition layer = FindLayer(layerName);
                    if (layer == null)
                    {
                        message = "Layer was not found.";
                        return false;
                    }
                    normalized = layer.Name;
                }

                string previous = _document.IsolatedLayer;
                _document.IsolatedLayer = normalized;
                if (!TrySave(out message))
                {
                    _document.IsolatedLayer = previous;
                    return false;
                }
                message = normalized.Length == 0
                    ? "Layer isolation cleared."
                    : "Isolated layer '" + normalized + "'.";
                return true;
            }
        }

        internal bool IsLocked(string objectKey)
        {
            lock (_sync)
            {
                EnsureLoaded();
                if (!_document.Objects.TryGetValue(objectKey ?? string.Empty, out DecorationLayerAssignment assignment) ||
                    assignment == null)
                {
                    return false;
                }
                if (assignment.Locked)
                    return true;
                DecorationLayerDefinition layer = FindLayer(assignment.Layer);
                return layer?.Locked == true;
            }
        }

        internal bool IsVisible(string objectKey)
        {
            lock (_sync)
            {
                EnsureLoaded();
                _document.Objects.TryGetValue(objectKey ?? string.Empty, out DecorationLayerAssignment assignment);
                string layerName = assignment?.Layer ?? string.Empty;
                if (!string.IsNullOrEmpty(_document.IsolatedLayer) &&
                    !string.Equals(layerName, _document.IsolatedLayer, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                DecorationLayerDefinition layer = FindLayer(layerName);
                return layer == null || layer.Visible;
            }
        }

        internal string LayerFor(string objectKey)
        {
            lock (_sync)
            {
                EnsureLoaded();
                return _document.Objects.TryGetValue(objectKey ?? string.Empty, out DecorationLayerAssignment assignment)
                    ? assignment?.Layer ?? string.Empty
                    : string.Empty;
            }
        }

        internal string[] TagsFor(string objectKey)
        {
            lock (_sync)
            {
                EnsureLoaded();
                return _document.Objects.TryGetValue(objectKey ?? string.Empty, out DecorationLayerAssignment assignment)
                    ? (assignment?.Tags ?? Array.Empty<string>()).ToArray()
                    : Array.Empty<string>();
            }
        }

        private DecorationLayerDefinition FindLayer(string name) =>
            _document.Layers.FirstOrDefault(layer =>
                string.Equals(layer.Name, (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

        private void EnsureLoaded()
        {
            if (_document != null)
                return;
            if (TryRead(FilePath, out DecorationLayerDocument primary) ||
                TryRead(BackupPath, out primary))
            {
                _document = primary;
                return;
            }
            _document = new DecorationLayerDocument();
        }

        private bool TrySave(out string message)
        {
            if (!TryValidate(_document, out message))
                return false;
            string json = JsonConvert.SerializeObject(_document, Formatting.Indented);
            if (Utf8NoBom.GetByteCount(json) > MaximumDocumentBytes)
            {
                message = "Layer workspace exceeds its 32 MiB safety limit.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string pending = FilePath + ".pending-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(pending, json, Utf8NoBom);
                if (File.Exists(FilePath))
                {
                    try { File.Replace(pending, FilePath, BackupPath, ignoreMetadataErrors: true); }
                    catch
                    {
                        File.Copy(FilePath, BackupPath, overwrite: true);
                        File.Delete(FilePath);
                        File.Move(pending, FilePath);
                    }
                }
                else
                {
                    File.Move(pending, FilePath);
                    File.Copy(FilePath, BackupPath, overwrite: true);
                }
                message = "Layer workspace saved.";
                return true;
            }
            catch (Exception exception)
            {
                message = "Layer workspace could not be saved: " + exception.Message;
                return false;
            }
        }

        private static bool TryRead(string path, out DecorationLayerDocument document)
        {
            document = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumDocumentBytes)
                    return false;
                document = JsonConvert.DeserializeObject<DecorationLayerDocument>(
                    File.ReadAllText(path, Utf8NoBom));
                return TryValidate(document, out _);
            }
            catch
            {
                document = null;
                return false;
            }
        }

        private static bool TryValidate(DecorationLayerDocument document, out string message)
        {
            if (document == null || document.SchemaVersion != CurrentSchemaVersion)
            {
                message = "Layer workspace schema is invalid or unsupported.";
                return false;
            }
            document.Layers = document.Layers ?? new List<DecorationLayerDefinition>();
            document.Objects = document.Objects ??
                               new Dictionary<string, DecorationLayerAssignment>(StringComparer.Ordinal);
            document.IsolatedLayer = document.IsolatedLayer ?? string.Empty;
            if (document.Layers.Count > MaximumLayers || document.Objects.Count > MaximumObjects)
            {
                message = "Layer workspace exceeds its bounded layer or object limit.";
                return false;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DecorationLayerDefinition layer in document.Layers)
            {
                if (layer != null)
                    layer.Folder = layer.Folder ?? string.Empty;
                if (layer == null ||
                    !TryNormalizeName(layer.Name, out string normalized, out message) ||
                    !string.Equals(layer.Name, normalized, StringComparison.Ordinal) ||
                    !TryNormalizeOptionalFolder(layer.Folder, out string normalizedFolder, out message) ||
                    !string.Equals(layer.Folder, normalizedFolder, StringComparison.Ordinal) ||
                    !names.Add(layer.Name) ||
                    !TryNormalizeTags(layer.Tags, out string[] tags, out message))
                {
                    message = "Layer workspace contains an invalid layer.";
                    return false;
                }
                layer.Tags = tags;
            }
            if (document.IsolatedLayer.Length > 0 && !names.Contains(document.IsolatedLayer))
            {
                message = "Layer workspace isolates a missing layer.";
                return false;
            }
            foreach (KeyValuePair<string, DecorationLayerAssignment> pair in document.Objects)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
                    (pair.Value.Layer?.Length > 0 && !names.Contains(pair.Value.Layer)) ||
                    !TryNormalizeTags(pair.Value.Tags, out string[] tags, out message))
                {
                    message = "Layer workspace contains an invalid object assignment.";
                    return false;
                }
                pair.Value.Layer = pair.Value.Layer ?? string.Empty;
                pair.Value.Tags = tags;
            }
            message = "Layer workspace is valid.";
            return true;
        }

        private static bool TryNormalizeName(string name, out string normalized, out string message)
        {
            normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumNameLength ||
                normalized.Any(character => char.IsControl(character) || character == '/' || character == '\\'))
            {
                message = "Layer names must contain 1 through 64 safe characters.";
                return false;
            }
            message = "Layer name is valid.";
            return true;
        }

        private static bool TryNormalizeOptionalFolder(
            string folder,
            out string normalized,
            out string message)
        {
            normalized = (folder ?? string.Empty).Trim();
            if (normalized.Length > MaximumNameLength ||
                normalized.Any(character =>
                    char.IsControl(character) || character == '/' || character == '\\'))
            {
                message = "Folder names may be empty or contain up to 64 safe characters.";
                return false;
            }

            message = "Folder name is valid.";
            return true;
        }

        private static bool TryNormalizeTags(
            IEnumerable<string> tags,
            out string[] normalized,
            out string message)
        {
            normalized = (tags ?? Array.Empty<string>())
                .Select(tag => (tag ?? string.Empty).Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length > MaximumTags || normalized.Any(tag =>
                    tag.Length > MaximumTagLength ||
                    tag.Any(character => char.IsControl(character) || character == '/' || character == '\\')))
            {
                message = "Use at most 16 tags of 32 safe characters each.";
                return false;
            }
            message = "Tags are valid.";
            return true;
        }

        private static string[] NormalizeObjectKeys(IEnumerable<string> keys) =>
            (keys ?? Array.Empty<string>())
                .Select(key => (key ?? string.Empty).Trim())
                .Where(key => key.Length > 0 && key.Length <= 512)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static DecorationLayerDocument CloneDocument(DecorationLayerDocument source) =>
            JsonConvert.DeserializeObject<DecorationLayerDocument>(
                JsonConvert.SerializeObject(source, Formatting.None));
    }

    internal static class DecorationWorkspaceObjectIdentity
    {
        internal static string Key(AllConstruct construct, Decoration decoration)
        {
            if (decoration == null)
                return string.Empty;
            string scope = ConstructScope(construct);
            if (decoration.UniqueId != 0)
            {
                return scope + "|uid:" +
                       decoration.UniqueId.ToString(CultureInfo.InvariantCulture);
            }

            Vector3i tether = decoration.TetherPoint.Us;
            Vector3 position = decoration.Positioning.Us;
            return scope + "|legacy:" + decoration.MeshGuid.Us.ToString("N") + ":" +
                   tether.x + ":" + tether.y + ":" + tether.z + ":" +
                   Mathf.RoundToInt(position.x * 10000f) + ":" +
                   Mathf.RoundToInt(position.y * 10000f) + ":" +
                   Mathf.RoundToInt(position.z * 10000f);
        }

        internal static string ConstructScope(AllConstruct construct)
        {
            if (construct == null)
                return "construct:null";

            string craftScope = MainConstructScope(construct);
            try
            {
                // FTD uses -1 for the main construct and starts subconstructs at 0.
                // Zero is therefore a real persisted identity and must not be treated
                // as an unset/default value.
                return ComposeConstructScope(
                    craftScope,
                    construct.PersistentSubConstructIndex);
            }
            catch
            {
                return craftScope + "|construct-instance:" +
                       RuntimeHelpers.GetHashCode(construct).ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static string ComposeConstructScope(
            string craftScope,
            int persistentSubConstructIndex) =>
            (string.IsNullOrWhiteSpace(craftScope) ? "craft:unknown" : craftScope) +
            "|sub:" + persistentSubConstructIndex.ToString(CultureInfo.InvariantCulture);

        private static string MainConstructScope(AllConstruct construct)
        {
            MainConstruct main = null;
            try
            {
                main = construct as MainConstruct ?? construct.Main;
            }
            catch
            {
            }

            if (main == null)
            {
                return "craft-instance:" +
                       RuntimeHelpers.GetHashCode(construct).ToString(CultureInfo.InvariantCulture);
            }

            int savedForceId = 0;
            bool hasSavedForceId = false;
            try
            {
                BrilliantSkies.Core.Id.ObjectId savedId = main.ForceIdWeWereSavedWith;
                if (savedId.AppearsValid)
                {
                    savedForceId = savedId.Id;
                    hasSavedForceId = true;
                }
            }
            catch
            {
            }

            int mainUniqueId = 0;
            try
            {
                mainUniqueId = main.UniqueId;
            }
            catch
            {
            }

            if (mainUniqueId != 0)
            {
                return (hasSavedForceId
                           ? "craft-force:" + savedForceId.ToString(CultureInfo.InvariantCulture) + "|"
                           : string.Empty) +
                       "craft-main:" + mainUniqueId.ToString(CultureInfo.InvariantCulture);
            }

            // A force ID is a namespace shared by multiple constructs, never a craft
            // identity by itself. Runtime object identity keeps even zero-ID designer
            // craft and unusual partially loaded craft records separate.
            return (hasSavedForceId
                       ? "craft-force:" + savedForceId.ToString(CultureInfo.InvariantCulture) + "|"
                       : string.Empty) +
                   "craft-instance:" +
                   RuntimeHelpers.GetHashCode(main).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Best-effort renderer bridge for per-layer viewport visibility. FTD has changed
    /// the private decoration render member names across releases, so the bridge finds
    /// Renderer/GameObject members conservatively and never deactivates the decoration
    /// component itself. Hidden states are fully restored when a layer is shown.
    /// </summary>
    internal static class DecorationLayerVisibilityBridge
    {
        private sealed class RendererState
        {
            internal Renderer Renderer;
            internal bool Enabled;
        }

        private static readonly Dictionary<Decoration, List<RendererState>> Hidden =
            new Dictionary<Decoration, List<RendererState>>();

        internal static bool SetHidden(Decoration decoration, bool hidden)
        {
            if (decoration == null || decoration.IsDeleted)
                return false;
            if (!hidden)
            {
                if (!Hidden.TryGetValue(decoration, out List<RendererState> states))
                    return true;
                foreach (RendererState state in states)
                {
                    if (state?.Renderer != null)
                        state.Renderer.enabled = state.Enabled;
                }
                Hidden.Remove(decoration);
                return true;
            }
            if (Hidden.ContainsKey(decoration))
                return true;

            Renderer[] renderers = FindRenderers(decoration).Distinct().ToArray();
            if (renderers.Length == 0)
                return false;
            var captured = new List<RendererState>(renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                captured.Add(new RendererState { Renderer = renderer, Enabled = renderer.enabled });
                renderer.enabled = false;
            }
            Hidden[decoration] = captured;
            return captured.Count > 0;
        }

        internal static void RestoreAll()
        {
            foreach (Decoration decoration in Hidden.Keys.ToArray())
                SetHidden(decoration, hidden: false);
        }

        private static IEnumerable<Renderer> FindRenderers(object source)
        {
            if (source == null)
                yield break;
            Type type = source.GetType();
            foreach (MemberInfo member in type.GetMembers(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = null;
                try
                {
                    if (member is FieldInfo field && !field.IsStatic)
                        value = field.GetValue(source);
                    else if (member is PropertyInfo property &&
                             property.CanRead &&
                             property.GetIndexParameters().Length == 0 &&
                             (typeof(Renderer).IsAssignableFrom(property.PropertyType) ||
                              typeof(GameObject).IsAssignableFrom(property.PropertyType) ||
                              typeof(Component).IsAssignableFrom(property.PropertyType)))
                        value = property.GetValue(source, null);
                }
                catch
                {
                }

                if (value is Renderer renderer)
                {
                    yield return renderer;
                }
                else if (value is GameObject gameObject)
                {
                    foreach (Renderer child in gameObject.GetComponentsInChildren<Renderer>(true))
                        yield return child;
                }
                else if (value is Component component && component.gameObject != null)
                {
                    foreach (Renderer child in component.gameObject.GetComponentsInChildren<Renderer>(true))
                        yield return child;
                }
            }
        }
    }
}
