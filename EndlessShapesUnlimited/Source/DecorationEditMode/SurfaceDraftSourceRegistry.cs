using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.Presets;
using Newtonsoft.Json;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class SurfaceDraftSourceDocument
    {
        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = SurfaceDraftSourceStore.CurrentSchemaVersion;

        [JsonProperty("decoration_sources")]
        public Dictionary<string, string> DecorationSources { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        [JsonProperty("sources")]
        public Dictionary<string, EsuSurfaceDraftPresetPayload> Sources { get; set; } =
            new Dictionary<string, EsuSurfaceDraftPresetPayload>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Transactional, profile-scoped sidecar for placed Surface Builder source.
    /// One topology payload is shared by all decorations produced by the placement,
    /// avoiding payload multiplication for large generated surfaces.
    /// </summary>
    internal sealed class SurfaceDraftSourceStore
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int MaximumAssociations = 250000;
        internal const int MaximumSources = 25000;
        internal const int MaximumDocumentBytes = 64 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private SurfaceDraftSourceDocument _document;

        internal SurfaceDraftSourceStore(string profileRoot)
        {
            if (string.IsNullOrWhiteSpace(profileRoot))
                throw new ArgumentException("A profile root is required.", nameof(profileRoot));
            DirectoryPath = Path.Combine(
                Path.GetFullPath(profileRoot),
                "EndlessShapesUnlimited",
                "Workspace");
            FilePath = Path.Combine(DirectoryPath, "placed-surface-sources.json");
            BackupPath = FilePath + ".backup";
        }

        internal string DirectoryPath { get; }

        internal string FilePath { get; }

        internal string BackupPath { get; }

        internal bool TryRegister(
            IEnumerable<string> decorationKeys,
            EsuSurfaceDraftPresetPayload payload,
            out string message)
        {
            message = null;
            string[] keys = NormalizeKeys(decorationKeys);
            if (keys.Length == 0)
            {
                message = "Placed surface source requires at least one stable decoration key.";
                return false;
            }
            if (payload == null || !payload.TryValidate(out message))
            {
                message = "Placed surface source payload is invalid. " + (message ?? string.Empty);
                return false;
            }

            lock (_sync)
            {
                EnsureLoaded();
                SurfaceDraftSourceDocument candidate = Clone(_document);
                string sourceId = Guid.NewGuid().ToString("N");
                candidate.Sources[sourceId] = payload;
                for (int index = 0; index < keys.Length; index++)
                    candidate.DecorationSources[keys[index]] = sourceId;
                PruneOrphanSources(candidate);
                if (!TryValidate(candidate, out message))
                    return false;
                if (!TrySave(candidate, out message))
                    return false;
                _document = candidate;
                message = "Placed surface source metadata saved for " +
                          keys.Length.ToString("N0") + " decoration(s).";
                return true;
            }
        }

        internal bool TryGet(
            string decorationKey,
            out EsuSurfaceDraftPresetPayload payload,
            out string message)
        {
            payload = null;
            string key = (decorationKey ?? string.Empty).Trim();
            if (key.Length == 0 || key.Length > 512)
            {
                message = "Placed surface source key is invalid.";
                return false;
            }

            lock (_sync)
            {
                EnsureLoaded();
                if (!_document.DecorationSources.TryGetValue(key, out string sourceId) ||
                    string.IsNullOrEmpty(sourceId) ||
                    !_document.Sources.TryGetValue(sourceId, out EsuSurfaceDraftPresetPayload found) ||
                    found == null ||
                    !found.TryValidate(out message))
                {
                    message = "No persistent Surface Builder source is registered for this decoration.";
                    return false;
                }

                payload = found;
                message = "Persistent Surface Builder source loaded.";
                return true;
            }
        }

        private void EnsureLoaded()
        {
            if (_document != null)
                return;
            if (TryRead(FilePath, out SurfaceDraftSourceDocument primary) ||
                TryRead(BackupPath, out primary))
            {
                _document = primary;
                return;
            }
            _document = new SurfaceDraftSourceDocument();
        }

        private bool TrySave(SurfaceDraftSourceDocument document, out string message)
        {
            string pending = null;
            try
            {
                string json = JsonConvert.SerializeObject(document, Formatting.Indented);
                if (Utf8NoBom.GetByteCount(json) > MaximumDocumentBytes)
                {
                    message = "Placed surface source metadata exceeds its 64 MiB safety limit.";
                    return false;
                }

                Directory.CreateDirectory(DirectoryPath);
                pending = FilePath + ".pending-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(pending, json, Utf8NoBom);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(pending, FilePath, BackupPath, ignoreMetadataErrors: true);
                    }
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
                message = "Placed surface source metadata saved.";
                return true;
            }
            catch (Exception exception)
            {
                message = "Placed surface source metadata could not be saved: " + exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(pending) && File.Exists(pending))
                {
                    try { File.Delete(pending); }
                    catch { }
                }
            }
        }

        private static bool TryRead(string path, out SurfaceDraftSourceDocument document)
        {
            document = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumDocumentBytes)
                    return false;
                document = JsonConvert.DeserializeObject<SurfaceDraftSourceDocument>(
                    File.ReadAllText(path, Utf8NoBom));
                return TryValidate(document, out _);
            }
            catch
            {
                document = null;
                return false;
            }
        }

        private static bool TryValidate(
            SurfaceDraftSourceDocument document,
            out string message)
        {
            if (document == null || document.SchemaVersion != CurrentSchemaVersion)
            {
                message = "Placed surface source metadata schema is invalid or unsupported.";
                return false;
            }
            document.DecorationSources = document.DecorationSources ??
                new Dictionary<string, string>(StringComparer.Ordinal);
            document.Sources = document.Sources ??
                new Dictionary<string, EsuSurfaceDraftPresetPayload>(StringComparer.Ordinal);
            if (document.DecorationSources.Count > MaximumAssociations ||
                document.Sources.Count > MaximumSources)
            {
                message = "Placed surface source metadata exceeds its bounded record limit.";
                return false;
            }
            foreach (KeyValuePair<string, string> pair in document.DecorationSources)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 512 ||
                    string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 64 ||
                    !document.Sources.ContainsKey(pair.Value))
                {
                    message = "Placed surface source metadata contains an invalid association.";
                    return false;
                }
            }
            foreach (KeyValuePair<string, EsuSurfaceDraftPresetPayload> pair in document.Sources)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64 ||
                    pair.Value == null || !pair.Value.TryValidate(out _))
                {
                    message = "Placed surface source metadata contains an invalid topology payload.";
                    return false;
                }
            }

            message = "Placed surface source metadata is valid.";
            return true;
        }

        private static string[] NormalizeKeys(IEnumerable<string> keys) =>
            (keys ?? Array.Empty<string>())
                .Select(key => (key ?? string.Empty).Trim())
                .Where(key => key.Length > 0 && key.Length <= 512)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static void PruneOrphanSources(SurfaceDraftSourceDocument document)
        {
            var used = new HashSet<string>(
                document.DecorationSources.Values,
                StringComparer.Ordinal);
            foreach (string sourceId in document.Sources.Keys
                         .Where(sourceId => !used.Contains(sourceId))
                         .ToArray())
            {
                document.Sources.Remove(sourceId);
            }
        }

        private static SurfaceDraftSourceDocument Clone(SurfaceDraftSourceDocument source) =>
            JsonConvert.DeserializeObject<SurfaceDraftSourceDocument>(
                JsonConvert.SerializeObject(source, Formatting.None));
    }

    /// <summary>
    /// Reopens placed Surface Builder sources from a fast process-local cache first,
    /// then from the profile sidecar keyed by FTD's persisted decoration identity.
    /// </summary>
    internal static class SurfaceDraftSourceRegistry
    {
        private static readonly ConditionalWeakTable<Decoration, SourceRecord> Sources =
            new ConditionalWeakTable<Decoration, SourceRecord>();
        private static readonly object StoreSync = new object();
        private static SurfaceDraftSourceStore s_store;

        internal static void Register(
            AllConstruct construct,
            IReadOnlyList<Decoration> decorations,
            SurfaceDraftSnapshot source)
        {
            if (construct == null || decorations == null || source == null)
                return;

            SurfaceDraftSnapshot stored = Copy(source);
            var keys = new List<string>(decorations.Count);
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                if (decoration == null || decoration.IsDeleted)
                    continue;

                Sources.Remove(decoration);
                Sources.Add(decoration, new SourceRecord(construct, stored));
                string key = DecorationWorkspaceObjectIdentity.Key(construct, decoration);
                if (!string.IsNullOrWhiteSpace(key))
                    keys.Add(key);
            }

            if (keys.Count == 0)
                return;
            try
            {
                var draft = new SurfaceDraft();
                draft.Restore(stored);
                EsuSurfaceDraftPresetPayload payload =
                    EsuSurfaceDraftPresetPayload.Capture(draft, preserveSelection: true);
                Store.TryRegister(keys, payload, out _);
            }
            catch
            {
                // The in-memory source remains usable for this session. Placement is
                // never rolled back merely because optional reopen metadata failed.
            }
        }

        internal static bool TryGet(
            Decoration decoration,
            AllConstruct construct,
            out SurfaceDraftSnapshot source)
        {
            source = null;
            if (decoration == null || decoration.IsDeleted || construct == null)
                return false;

            if (Sources.TryGetValue(decoration, out SourceRecord record) &&
                ReferenceEquals(record.Construct, construct))
            {
                source = Copy(record.Source);
                return true;
            }

            try
            {
                string key = DecorationWorkspaceObjectIdentity.Key(construct, decoration);
                if (!Store.TryGet(key, out EsuSurfaceDraftPresetPayload payload, out _) ||
                    !payload.TryCreateSnapshot(
                        construct,
                        payload.Reference.ToVector3(),
                        out SurfaceDraftSnapshot restored,
                        out _))
                {
                    return false;
                }

                Sources.Remove(decoration);
                Sources.Add(decoration, new SourceRecord(construct, restored));
                source = Copy(restored);
                return true;
            }
            catch
            {
                source = null;
                return false;
            }
        }

        private static SurfaceDraftSourceStore Store
        {
            get
            {
                lock (StoreSync)
                {
                    if (s_store != null)
                        return s_store;
                    string presets = EsuPresetLibrary.Default.DirectoryPath;
                    string profileRoot = Directory.GetParent(
                        Directory.GetParent(presets).FullName).FullName;
                    return s_store = new SurfaceDraftSourceStore(profileRoot);
                }
            }
        }

        private static SurfaceDraftSnapshot Copy(SurfaceDraftSnapshot source) =>
            new SurfaceDraftSnapshot(
                source.Construct,
                source.Points,
                source.Faces,
                source.FaceStyles,
                source.ManualFaceSelection,
                source.FreeTriangleSelection,
                source.BridgeEdgeSelection,
                source.SelectionKind,
                source.SelectedPoint,
                source.SelectedFace,
                source.SelectedEdge,
                source.HasSharedAnchor,
                source.SharedAnchor,
                source.SharedAnchorSelected,
                source.Settings);

        private sealed class SourceRecord
        {
            internal SourceRecord(AllConstruct construct, SurfaceDraftSnapshot source)
            {
                Construct = construct;
                Source = source;
            }

            internal AllConstruct Construct { get; }

            internal SurfaceDraftSnapshot Source { get; }
        }
    }
}
