using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.Constants;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DecoLimitLifter.Presets
{
    internal enum EsuPresetKind
    {
        DecorationSelection = 1,
        SurfaceDraft = 2,
        GeneratorDraft = 3,
        SmartScene = 4,
        EditorCheckpoint = 5
    }

    internal sealed class EsuPresetEntry
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("name")]
        public string Name { get; private set; }

        [JsonProperty("kind")]
        public EsuPresetKind Kind { get; private set; }

        [JsonProperty("description")]
        public string Description { get; private set; }

        [JsonProperty("tags")]
        public string[] Tags { get; private set; }

        [JsonProperty("created_utc")]
        public string CreatedUtc { get; private set; }

        [JsonProperty("updated_utc")]
        public string UpdatedUtc { get; private set; }

        [JsonProperty("payload_json")]
        public string PayloadJson { get; private set; }

        [JsonProperty("payload_sha256")]
        public string PayloadSha256 { get; private set; }

        [JsonProperty("is_recovery")]
        public bool IsRecovery { get; private set; }

        [JsonProperty("recovery_slot")]
        public string RecoverySlot { get; private set; }

        [JsonConstructor]
        private EsuPresetEntry()
        {
        }

        private EsuPresetEntry(
            string id,
            string name,
            EsuPresetKind kind,
            string description,
            string[] tags,
            string createdUtc,
            string updatedUtc,
            string payloadJson,
            string payloadSha256,
            bool isRecovery,
            string recoverySlot)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Description = description;
            Tags = tags ?? Array.Empty<string>();
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            PayloadJson = payloadJson;
            PayloadSha256 = payloadSha256;
            IsRecovery = isRecovery;
            RecoverySlot = recoverySlot;
        }

        internal static EsuPresetEntry Create(
            string name,
            EsuPresetKind kind,
            string description,
            IEnumerable<string> tags,
            string payloadJson,
            bool isRecovery,
            string recoverySlot,
            DateTime utcNow,
            EsuPresetEntry previous = null)
        {
            string timestamp = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return new EsuPresetEntry(
                previous?.Id ?? Guid.NewGuid().ToString("D"),
                name,
                kind,
                description ?? string.Empty,
                NormalizeTags(tags),
                previous?.CreatedUtc ?? timestamp,
                timestamp,
                payloadJson,
                EsuPresetLibrary.HashPayload(payloadJson),
                isRecovery,
                recoverySlot ?? string.Empty);
        }

        internal EsuPresetEntry Renamed(string name, DateTime utcNow) =>
            Create(
                name,
                Kind,
                Description,
                Tags,
                PayloadJson,
                IsRecovery,
                RecoverySlot,
                utcNow,
                this);

        internal EsuPresetEntry Copy() =>
            new EsuPresetEntry(
                Id,
                Name,
                Kind,
                Description,
                Tags == null ? Array.Empty<string>() : (string[])Tags.Clone(),
                CreatedUtc,
                UpdatedUtc,
                PayloadJson,
                PayloadSha256,
                IsRecovery,
                RecoverySlot);

        private static string[] NormalizeTags(IEnumerable<string> tags) =>
            (tags ?? Array.Empty<string>())
                .Select(tag => (tag ?? string.Empty).Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .Take(EsuPresetLibrary.MaximumTagsPerPreset)
                .ToArray();
    }

    internal sealed class EsuPresetLibraryDocument
    {
        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = EsuPresetLibrary.CurrentSchemaVersion;

        [JsonProperty("entries")]
        public List<EsuPresetEntry> Entries { get; set; } = new List<EsuPresetEntry>();
    }

    /// <summary>
    /// Transactional, profile-scoped storage for reusable editor presets and one recovery slot
    /// per editor mode. Payloads are normalized JSON rather than CLR type metadata so a future
    /// version can migrate them without loading arbitrary runtime types.
    /// </summary>
    internal sealed class EsuPresetLibrary
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int MaximumEntries = 512;
        internal const int MaximumNameLength = 80;
        internal const int MaximumDescriptionLength = 512;
        internal const int MaximumTagLength = 32;
        internal const int MaximumTagsPerPreset = 16;
        internal const int MaximumPayloadBytes = 4 * 1024 * 1024;
        internal const int MaximumDocumentBytes = 32 * 1024 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object DefaultSync = new object();
        private static EsuPresetLibrary s_default;
        private static string s_defaultRoot;
        private static bool s_defaultUsesProfileRoot;

        private readonly object _sync = new object();
        private readonly Func<DateTime> _utcNow;

        internal EsuPresetLibrary(string profileRoot, Func<DateTime> utcNow = null)
        {
            if (string.IsNullOrWhiteSpace(profileRoot))
                throw new ArgumentException("A profile root is required.", nameof(profileRoot));

            string fullRoot = Path.GetFullPath(profileRoot);
            DirectoryPath = Path.Combine(fullRoot, "EndlessShapesUnlimited", "Presets");
            FilePath = Path.Combine(DirectoryPath, "preset-library.json");
            BackupPath = FilePath + ".backup";
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal string DirectoryPath { get; }

        internal string FilePath { get; }

        internal string BackupPath { get; }

        internal static EsuPresetLibrary Default
        {
            get
            {
                lock (DefaultSync)
                {
                    bool profileAvailable = TryResolveProfileRoot(out string root);
                    if (s_default == null ||
                        (profileAvailable &&
                         (!s_defaultUsesProfileRoot ||
                          !string.Equals(s_defaultRoot, root, StringComparison.OrdinalIgnoreCase))))
                    {
                        s_default = new EsuPresetLibrary(root);
                        s_defaultRoot = Path.GetFullPath(root);
                        s_defaultUsesProfileRoot = profileAvailable;
                    }
                    return s_default;
                }
            }
        }

        internal bool TryList(
            out IReadOnlyList<EsuPresetEntry> entries,
            out string message,
            bool includeRecovery = false)
        {
            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out string readMessage))
                {
                    entries = Array.Empty<EsuPresetEntry>();
                    message = readMessage;
                    return false;
                }

                entries = document.Entries
                    .Where(entry => includeRecovery || !entry.IsRecovery)
                    .OrderBy(entry => entry.Kind)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => entry.Copy())
                    .ToArray();
                string countMessage = entries.Count.ToString("N0", CultureInfo.InvariantCulture) +
                                      (includeRecovery
                                          ? " preset/recovery entries loaded."
                                          : " presets loaded.");
                message = readMessage.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0
                    ? readMessage + " " + countMessage
                    : countMessage;
                return true;
            }
        }

        internal bool TrySave<T>(
            string name,
            EsuPresetKind kind,
            T payload,
            bool overwrite,
            out EsuPresetEntry saved,
            out string message,
            string description = "",
            IEnumerable<string> tags = null)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(payload, Formatting.None);
            }
            catch (Exception exception)
            {
                saved = null;
                message = "Preset payload could not be serialized: " + exception.Message;
                return false;
            }

            return TrySaveJson(
                name,
                kind,
                json,
                overwrite,
                out saved,
                out message,
                description,
                tags);
        }

        internal bool TrySaveJson(
            string name,
            EsuPresetKind kind,
            string payloadJson,
            bool overwrite,
            out EsuPresetEntry saved,
            out string message,
            string description = "",
            IEnumerable<string> tags = null)
        {
            saved = null;
            if (!TryNormalizeName(name, out string normalizedName, out message) ||
                !TryValidateKind(kind, out message) ||
                !TryNormalizeDescription(description, out string normalizedDescription, out message) ||
                !TryNormalizeTagInput(tags, out string[] normalizedTags, out message) ||
                !TryNormalizePayload(payloadJson, out string normalizedPayload, out message))
            {
                return false;
            }

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry previous = document.Entries.FirstOrDefault(entry =>
                    !entry.IsRecovery &&
                    entry.Kind == kind &&
                    string.Equals(entry.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
                if (previous != null && !overwrite)
                {
                    message = "A " + kind + " preset named '" + normalizedName + "' already exists.";
                    return false;
                }

                if (previous == null && document.Entries.Count >= MaximumEntries)
                {
                    message = "The preset library has reached its " +
                              MaximumEntries.ToString(CultureInfo.InvariantCulture) +
                              " entry limit.";
                    return false;
                }

                if (previous != null)
                    document.Entries.Remove(previous);
                saved = EsuPresetEntry.Create(
                    normalizedName,
                    kind,
                    normalizedDescription,
                    normalizedTags,
                    normalizedPayload,
                    isRecovery: false,
                    recoverySlot: string.Empty,
                    _utcNow(),
                    previous);
                document.Entries.Add(saved);
                if (!TryWriteDocument(document, out message))
                {
                    saved = null;
                    return false;
                }

                saved = saved.Copy();
                message = (previous == null ? "Saved" : "Updated") +
                          " preset '" + normalizedName + "'.";
                return true;
            }
        }

        internal bool TryRead<T>(string id, out T payload, out EsuPresetEntry entry, out string message)
        {
            payload = default;
            entry = null;
            if (!TryNormalizeId(id, out string normalizedId, out message))
                return false;

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry found = document.Entries.FirstOrDefault(candidate =>
                    !candidate.IsRecovery &&
                    string.Equals(candidate.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    message = "Preset '" + normalizedId + "' was not found.";
                    return false;
                }

                try
                {
                    payload = JsonConvert.DeserializeObject<T>(found.PayloadJson);
                }
                catch (Exception exception)
                {
                    message = "Preset payload could not be read: " + exception.Message;
                    return false;
                }

                entry = found.Copy();
                message = "Loaded preset '" + found.Name + "'.";
                return true;
            }
        }

        internal bool TryDelete(string id, out string message)
        {
            if (!TryNormalizeId(id, out string normalizedId, out message))
                return false;

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry found = document.Entries.FirstOrDefault(entry =>
                    !entry.IsRecovery &&
                    string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    message = "Preset '" + normalizedId + "' was not found.";
                    return false;
                }

                document.Entries.Remove(found);
                if (!TryWriteDocument(document, out message))
                    return false;

                message = "Deleted preset '" + found.Name + "'.";
                return true;
            }
        }

        internal bool TryRename(string id, string newName, out EsuPresetEntry renamed, out string message)
        {
            renamed = null;
            if (!TryNormalizeId(id, out string normalizedId, out message) ||
                !TryNormalizeName(newName, out string normalizedName, out message))
            {
                return false;
            }

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry found = document.Entries.FirstOrDefault(entry =>
                    !entry.IsRecovery &&
                    string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    message = "Preset '" + normalizedId + "' was not found.";
                    return false;
                }

                if (document.Entries.Any(entry =>
                        !entry.IsRecovery &&
                        !string.Equals(entry.Id, found.Id, StringComparison.OrdinalIgnoreCase) &&
                        entry.Kind == found.Kind &&
                        string.Equals(entry.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                {
                    message = "A " + found.Kind + " preset named '" + normalizedName + "' already exists.";
                    return false;
                }

                document.Entries.Remove(found);
                renamed = found.Renamed(normalizedName, _utcNow());
                document.Entries.Add(renamed);
                if (!TryWriteDocument(document, out message))
                {
                    renamed = null;
                    return false;
                }

                renamed = renamed.Copy();
                message = "Renamed preset to '" + normalizedName + "'.";
                return true;
            }
        }

        internal bool TrySaveRecovery<T>(
            string slot,
            EsuPresetKind kind,
            T payload,
            out string message)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(payload, Formatting.None);
            }
            catch (Exception exception)
            {
                message = "Recovery payload could not be serialized: " + exception.Message;
                return false;
            }

            return TrySaveRecoveryJson(slot, kind, json, out message);
        }

        internal bool TrySaveRecoveryJson(
            string slot,
            EsuPresetKind kind,
            string payloadJson,
            out string message)
        {
            if (!TryNormalizeRecoverySlot(slot, out string normalizedSlot, out message) ||
                !TryValidateKind(kind, out message) ||
                !TryNormalizePayload(payloadJson, out string normalizedPayload, out message))
            {
                return false;
            }

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry previous = document.Entries.FirstOrDefault(entry =>
                    entry.IsRecovery &&
                    string.Equals(entry.RecoverySlot, normalizedSlot, StringComparison.OrdinalIgnoreCase));
                if (previous == null && document.Entries.Count >= MaximumEntries)
                {
                    message = "The preset library has reached its entry limit; recovery was not written.";
                    return false;
                }

                if (previous != null)
                    document.Entries.Remove(previous);
                EsuPresetEntry recovery = EsuPresetEntry.Create(
                    "Recovery - " + normalizedSlot,
                    kind,
                    "Automatic editor recovery snapshot.",
                    Array.Empty<string>(),
                    normalizedPayload,
                    isRecovery: true,
                    recoverySlot: normalizedSlot,
                    _utcNow(),
                    previous);
                document.Entries.Add(recovery);
                if (!TryWriteDocument(document, out message))
                    return false;

                message = "Saved recovery slot '" + normalizedSlot + "'.";
                return true;
            }
        }

        internal bool TryReadRecovery<T>(
            string slot,
            out T payload,
            out EsuPresetEntry entry,
            out string message)
        {
            payload = default;
            entry = null;
            if (!TryNormalizeRecoverySlot(slot, out string normalizedSlot, out message))
                return false;

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                EsuPresetEntry found = document.Entries.FirstOrDefault(candidate =>
                    candidate.IsRecovery &&
                    string.Equals(candidate.RecoverySlot, normalizedSlot, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    message = "Recovery slot '" + normalizedSlot + "' is empty.";
                    return false;
                }

                try
                {
                    payload = JsonConvert.DeserializeObject<T>(found.PayloadJson);
                }
                catch (Exception exception)
                {
                    message = "Recovery payload could not be read: " + exception.Message;
                    return false;
                }

                entry = found.Copy();
                message = "Loaded recovery slot '" + normalizedSlot + "'.";
                return true;
            }
        }

        internal bool TryClearRecovery(string slot, out string message)
        {
            if (!TryNormalizeRecoverySlot(slot, out string normalizedSlot, out message))
                return false;

            lock (_sync)
            {
                if (!TryReadDocument(out EsuPresetLibraryDocument document, out message))
                    return false;

                int removed = document.Entries.RemoveAll(entry =>
                    entry.IsRecovery &&
                    string.Equals(entry.RecoverySlot, normalizedSlot, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    message = "Recovery slot '" + normalizedSlot + "' was already empty.";
                    return true;
                }

                if (!TryWriteDocument(document, out message))
                    return false;

                message = "Cleared recovery slot '" + normalizedSlot + "'.";
                return true;
            }
        }

        internal static string HashPayload(string payloadJson)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Utf8NoBom.GetBytes(payloadJson ?? string.Empty));
                return string.Concat(hash.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        private bool TryReadDocument(out EsuPresetLibraryDocument document, out string message)
        {
            if (!File.Exists(FilePath))
            {
                document = NewDocument();
                message = "Preset library is empty.";
                return true;
            }

            if (TryReadDocumentFile(FilePath, out document, out message))
                return true;

            string primaryError = message;
            if (File.Exists(BackupPath) &&
                TryReadDocumentFile(BackupPath, out document, out string backupMessage))
            {
                message = "The primary preset library was invalid; the last known-good backup was loaded. " +
                          primaryError;
                return true;
            }

            document = null;
            message = "Preset library and backup could not be read. " + primaryError;
            return false;
        }

        private static bool TryReadDocumentFile(
            string path,
            out EsuPresetLibraryDocument document,
            out string message)
        {
            document = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaximumDocumentBytes)
                {
                    message = "Preset library exceeds the " +
                              MaximumDocumentBytes.ToString("N0", CultureInfo.InvariantCulture) +
                              " byte safety limit.";
                    return false;
                }

                string json = File.ReadAllText(path, Utf8NoBom);
                document = JsonConvert.DeserializeObject<EsuPresetLibraryDocument>(json);
                if (!TryValidateDocument(document, out message))
                {
                    document = null;
                    return false;
                }

                message = "Preset library loaded.";
                return true;
            }
            catch (Exception exception)
            {
                message = "Preset library read failed: " + exception.Message;
                document = null;
                return false;
            }
        }

        private bool TryWriteDocument(EsuPresetLibraryDocument document, out string message)
        {
            if (!TryValidateDocument(document, out message))
                return false;

            document.Entries = document.Entries
                .OrderBy(entry => entry.IsRecovery)
                .ThenBy(entry => entry.Kind)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string json = JsonConvert.SerializeObject(document, Formatting.Indented);
            int byteCount = Utf8NoBom.GetByteCount(json);
            if (byteCount > MaximumDocumentBytes)
            {
                message = "Preset library would exceed its " +
                          MaximumDocumentBytes.ToString("N0", CultureInfo.InvariantCulture) +
                          " byte safety limit.";
                return false;
            }

            Directory.CreateDirectory(DirectoryPath);
            string pending = FilePath + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           pending,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024,
                           FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush();
                }

                if (File.Exists(FilePath))
                    File.Replace(pending, FilePath, BackupPath, ignoreMetadataErrors: true);
                else
                    File.Move(pending, FilePath);

                message = "Preset library saved.";
                return true;
            }
            catch (Exception exception)
            {
                message = "Preset library save failed: " + exception.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(pending))
                        File.Delete(pending);
                }
                catch
                {
                }
            }
        }

        private static bool TryValidateDocument(EsuPresetLibraryDocument document, out string message)
        {
            if (document == null)
            {
                message = "Preset library JSON did not contain a document.";
                return false;
            }
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                message = "Preset library schema " +
                          document.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                          " is not supported by this ESU version.";
                return false;
            }

            document.Entries = document.Entries ?? new List<EsuPresetEntry>();
            if (document.Entries.Count > MaximumEntries)
            {
                message = "Preset library contains too many entries.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recoverySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EsuPresetEntry entry in document.Entries)
            {
                if (!TryValidateEntry(entry, out message))
                    return false;
                if (!ids.Add(entry.Id))
                {
                    message = "Preset library contains duplicate entry ID '" + entry.Id + "'.";
                    return false;
                }

                if (entry.IsRecovery)
                {
                    if (!recoverySlots.Add(entry.RecoverySlot))
                    {
                        message = "Preset library contains duplicate recovery slot '" +
                                  entry.RecoverySlot + "'.";
                        return false;
                    }
                }
                else
                {
                    string key = ((int)entry.Kind).ToString(CultureInfo.InvariantCulture) + "\n" + entry.Name;
                    if (!names.Add(key))
                    {
                        message = "Preset library contains duplicate preset name '" + entry.Name + "'.";
                        return false;
                    }
                }
            }

            message = "Preset library is valid.";
            return true;
        }

        private static bool TryValidateEntry(EsuPresetEntry entry, out string message)
        {
            if (entry == null ||
                !Guid.TryParse(entry.Id, out _) ||
                !TryValidateKind(entry.Kind, out message))
            {
                message = entry == null
                    ? "Preset library contains a null entry."
                    : "Preset library contains an invalid entry ID or kind.";
                return false;
            }
            if (!TryNormalizeName(entry.Name, out string normalizedName, out message) ||
                !string.Equals(entry.Name, normalizedName, StringComparison.Ordinal))
            {
                message = "Preset entry has an invalid or non-normalized name.";
                return false;
            }
            if (!TryNormalizeDescription(entry.Description, out string normalizedDescription, out message) ||
                !string.Equals(entry.Description ?? string.Empty, normalizedDescription, StringComparison.Ordinal))
            {
                message = "Preset entry has an invalid description.";
                return false;
            }
            if (!TryNormalizeTagInput(entry.Tags, out string[] normalizedTags, out message) ||
                !(entry.Tags ?? Array.Empty<string>()).SequenceEqual(normalizedTags, StringComparer.Ordinal))
            {
                message = "Preset entry has invalid or non-normalized tags.";
                return false;
            }
            if (entry.IsRecovery)
            {
                if (!TryNormalizeRecoverySlot(entry.RecoverySlot, out string slot, out message) ||
                    !string.Equals(entry.RecoverySlot, slot, StringComparison.Ordinal))
                {
                    message = "Preset entry has an invalid recovery slot.";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(entry.RecoverySlot))
            {
                message = "Normal presets cannot carry a recovery slot.";
                return false;
            }

            if (!DateTime.TryParse(
                    entry.CreatedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _) ||
                !DateTime.TryParse(
                    entry.UpdatedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                message = "Preset entry has invalid timestamps.";
                return false;
            }
            if (!TryNormalizePayload(entry.PayloadJson, out string normalizedPayload, out message) ||
                !string.Equals(entry.PayloadJson, normalizedPayload, StringComparison.Ordinal))
            {
                message = "Preset entry payload is invalid or non-normalized.";
                return false;
            }
            if (!string.Equals(
                    entry.PayloadSha256,
                    HashPayload(entry.PayloadJson),
                    StringComparison.OrdinalIgnoreCase))
            {
                message = "Preset entry payload hash does not match its JSON.";
                return false;
            }

            message = "Preset entry is valid.";
            return true;
        }

        private static bool TryNormalizePayload(string json, out string normalized, out string message)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                message = "Preset payload cannot be empty.";
                return false;
            }

            try
            {
                JToken token = JToken.Parse(json);
                normalized = token.ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                message = "Preset payload is not valid JSON: " + exception.Message;
                return false;
            }

            int bytes = Utf8NoBom.GetByteCount(normalized);
            if (bytes > MaximumPayloadBytes)
            {
                message = "Preset payload exceeds the " +
                          MaximumPayloadBytes.ToString("N0", CultureInfo.InvariantCulture) +
                          " byte safety limit.";
                normalized = null;
                return false;
            }

            message = "Preset payload is valid.";
            return true;
        }

        private static bool TryNormalizeName(string name, out string normalized, out string message)
        {
            normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumNameLength)
            {
                message = "Preset name must contain 1 through " +
                          MaximumNameLength.ToString(CultureInfo.InvariantCulture) +
                          " characters.";
                return false;
            }
            if (normalized.Any(character =>
                    char.IsControl(character) ||
                    character == '/' ||
                    character == '\\' ||
                    character == ':'))
            {
                message = "Preset names cannot contain control characters or path separators.";
                return false;
            }

            message = "Preset name is valid.";
            return true;
        }

        private static bool TryNormalizeDescription(
            string description,
            out string normalized,
            out string message)
        {
            normalized = (description ?? string.Empty).Trim();
            if (normalized.Length > MaximumDescriptionLength ||
                normalized.Any(character => char.IsControl(character) && character != '\n' && character != '\t'))
            {
                message = "Preset description is too long or contains unsupported control characters.";
                return false;
            }

            message = "Preset description is valid.";
            return true;
        }

        private static bool TryNormalizeTagInput(
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
            if (normalized.Length > MaximumTagsPerPreset ||
                normalized.Any(tag =>
                    tag.Length > MaximumTagLength ||
                    tag.Any(character => char.IsControl(character) || character == '/' || character == '\\')))
            {
                message = "Preset tags exceed their count/length limit or contain path separators.";
                normalized = Array.Empty<string>();
                return false;
            }

            message = "Preset tags are valid.";
            return true;
        }

        private static bool TryNormalizeRecoverySlot(string slot, out string normalized, out string message)
        {
            normalized = (slot ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0 ||
                normalized.Length > 48 ||
                normalized.Any(character =>
                    !(char.IsLetterOrDigit(character) || character == '-' || character == '_')))
            {
                message = "Recovery slot must contain 1 through 48 letters, digits, '-' or '_' characters.";
                return false;
            }

            message = "Recovery slot is valid.";
            return true;
        }

        private static bool TryNormalizeId(string id, out string normalized, out string message)
        {
            if (!Guid.TryParse((id ?? string.Empty).Trim(), out Guid guid))
            {
                normalized = null;
                message = "Preset ID is not a valid GUID.";
                return false;
            }

            normalized = guid.ToString("D");
            message = "Preset ID is valid.";
            return true;
        }

        private static bool TryValidateKind(EsuPresetKind kind, out string message)
        {
            if (!Enum.IsDefined(typeof(EsuPresetKind), kind))
            {
                message = "Preset kind is not supported.";
                return false;
            }

            message = "Preset kind is valid.";
            return true;
        }

        private static EsuPresetLibraryDocument NewDocument() =>
            new EsuPresetLibraryDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Entries = new List<EsuPresetEntry>()
            };

        private static bool TryResolveProfileRoot(out string root)
        {
            try
            {
                root = Get.ProfilePaths?.ProfileRootDir()?.ToString();
                if (!string.IsNullOrWhiteSpace(root))
                    return true;
            }
            catch
            {
            }

            root = Path.GetTempPath();
            return false;
        }
    }
}
