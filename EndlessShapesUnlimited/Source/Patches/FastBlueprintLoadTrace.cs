using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using DecoLimitLifter.SerializationHud;

namespace DecoLimitLifter.Patches
{
    internal sealed class FastBlueprintLoadTrace
    {
        internal static readonly TimeSpan FileHeartbeatInterval = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan AdvLoggerHeartbeatInterval = TimeSpan.FromMinutes(5);

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly DateTime _startedUtc;
        private readonly string _sessionId;
        private readonly string _blueprintFileName;
        private readonly long _fileBytes;
        private readonly FastBlueprintLoadTier _tier;
        private readonly string _logPath;
        private readonly bool _fileEnabled;

        private TimeSpan _lastFileHeartbeat;
        private TimeSpan _lastAdvLoggerHeartbeat;
        private bool _completed;

        private FastBlueprintLoadTrace(
            string sessionId,
            string blueprintFileName,
            long fileBytes,
            FastBlueprintLoadTier tier,
            string logPath,
            bool fileEnabled,
            DateTime startedUtc)
        {
            _sessionId = sessionId;
            _blueprintFileName = string.IsNullOrWhiteSpace(blueprintFileName)
                ? "unknown-blueprint"
                : blueprintFileName;
            _fileBytes = fileBytes;
            _tier = tier;
            _logPath = logPath;
            _fileEnabled = fileEnabled;
            _startedUtc = startedUtc;
        }

        internal string SessionId => _sessionId;

        internal string LogPath => _logPath;

        internal static FastBlueprintLoadTrace TryStart(
            string blueprintPath,
            long fileBytes,
            FastBlueprintLoadTier tier)
        {
            string fileName = string.IsNullOrWhiteSpace(blueprintPath)
                ? "standalone"
                : Path.GetFileName(blueprintPath);
            return TryStartWithName(fileName, fileBytes, tier);
        }

        internal static FastBlueprintLoadTrace TryStartStandalone(
            string name,
            long fileBytes,
            FastBlueprintLoadTier tier) =>
            TryStartWithName(
                string.IsNullOrWhiteSpace(name) ? "standalone" : name,
                fileBytes,
                tier);

        internal static string BuildLogPathForVerification(
            string root,
            string blueprintName,
            DateTime utcTimestamp,
            string sessionId)
        {
            string directory = Path.Combine(
                string.IsNullOrWhiteSpace(root) ? "." : root,
                "EndlessShapesUnlimited",
                "Logs");
            string safeName = SanitizeFileName(
                string.IsNullOrWhiteSpace(blueprintName) ? "standalone" : blueprintName);
            string safeSession = SanitizeFileName(
                string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);
            return Path.Combine(
                directory,
                "FastBlueprintLoad-" +
                utcTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" +
                safeName +
                "-" +
                safeSession +
                ".log");
        }

        internal static string SanitizeFileNameForVerification(string name) =>
            SanitizeFileName(name);

        internal static string FormatRouteDecisionForVerification(
            FastBlueprintLoadTier tier,
            bool diagnostics,
            bool smallBlueprintTesting,
            long fileBytes,
            long thresholdBytes,
            bool routed,
            string reason) =>
            FormatKeyValues(
                new[]
                {
                    Pair("event", "route-decision"),
                    Pair("tier", tier.ToString()),
                    Pair("diagnostics", diagnostics),
                    Pair("small_testing", smallBlueprintTesting),
                    Pair("file_bytes", fileBytes),
                    Pair("threshold_bytes", thresholdBytes),
                    Pair("routed", routed),
                    Pair("reason", reason)
                });

        internal void RouteDecision(
            bool diagnostics,
            bool smallBlueprintTesting,
            long thresholdBytes,
            bool routed,
            string reason)
        {
            Event(
                "route-decision",
                "route",
                advLogger: true,
                Pair("diagnostics", diagnostics),
                Pair("small_testing", smallBlueprintTesting),
                Pair("threshold_bytes", thresholdBytes),
                Pair("routed", routed),
                Pair("reason", reason));
        }

        internal void PhaseStart(string phase, params KeyValuePair<string, object>[] fields) =>
            Event("phase-start", phase, advLogger: true, fields);

        internal void PhaseEnd(
            string phase,
            Stopwatch phaseTimer,
            params KeyValuePair<string, object>[] fields)
        {
            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>());
            if (phaseTimer != null)
                merged.Add(Pair("phase_elapsed_ms", phaseTimer.Elapsed.TotalMilliseconds));
            Event("phase-end", phase, advLogger: true, merged.ToArray());
        }

        internal void Heartbeat(
            string phase,
            long current,
            long total,
            string unit,
            params KeyValuePair<string, object>[] fields)
        {
            TimeSpan elapsed = _elapsed.Elapsed;
            bool fileHeartbeat = elapsed - _lastFileHeartbeat >= FileHeartbeatInterval;
            bool advHeartbeat = elapsed - _lastAdvLoggerHeartbeat >= AdvLoggerHeartbeatInterval;
            if (!fileHeartbeat && !advHeartbeat)
                return;

            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>())
            {
                Pair("current", current),
                Pair("total", total),
                Pair("unit", unit),
                Pair("percent", Percent(current, total))
            };

            Event("heartbeat", phase, advHeartbeat, merged.ToArray());
            if (fileHeartbeat)
                _lastFileHeartbeat = elapsed;
            if (advHeartbeat)
                _lastAdvLoggerHeartbeat = elapsed;
        }

        internal void BlueprintMetadata(BlueprintFileModel model)
        {
            Blueprint blueprint = model?.Blueprint;
            Event(
                "blueprint-metadata",
                "v1-json",
                advLogger: true,
                Pair("name", model?.Name),
                Pair("version", model?.Version ?? 0),
                Pair("saved_total_blocks", model?.SavedTotalBlockCount ?? 0),
                Pair("saved_material_cost", model?.SavedMaterialCost ?? 0f),
                Pair("contained_material_cost", model?.ContainedMaterialCost ?? 0f),
                Pair("item_dictionary_count", model?.ItemDictionary?.Count ?? 0),
                Pair("block_ids_count", blueprint?.BlockIds?.Length ?? 0),
                Pair("block_data_bytes", blueprint?.BlockData?.LongLength ?? 0L),
                Pair("vehicle_data_bytes", blueprint?.VehicleData?.LongLength ?? 0L),
                Pair("subconstruct_count", blueprint?.SCs?.Count ?? 0));
        }

        internal void CraftLoadStart(params KeyValuePair<string, object>[] fields) =>
            Event(
                "craft-load-start",
                "craft-load",
                advLogger: true,
                WithTotalElapsed(fields));

        internal void CraftLoadComplete(params KeyValuePair<string, object>[] fields) =>
            Event(
                "craft-load-complete",
                "craft-load",
                advLogger: true,
                WithTotalElapsed(fields));

        internal void CraftLoadFailed(
            Exception exception,
            params KeyValuePair<string, object>[] fields)
        {
            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>())
            {
                Pair("exception_type", exception?.GetType().FullName),
                Pair("message", exception?.Message)
            };
            Event(
                "craft-load-failed",
                "craft-load",
                advLogger: true,
                WithTotalElapsed(merged.ToArray()));
        }

        internal void Complete(string reason)
        {
            if (_completed)
                return;
            _completed = true;
            Event("trace-complete", "complete", advLogger: true, Pair("reason", reason));
        }

        internal void Exception(string phase, Exception exception)
        {
            Event(
                "exception",
                phase,
                advLogger: true,
                Pair("exception_type", exception?.GetType().FullName),
                Pair("message", exception?.Message));
        }

        internal void Event(
            string eventName,
            string phase,
            bool advLogger,
            params KeyValuePair<string, object>[] fields)
        {
            string line = BuildLine(eventName, phase, fields);
            WriteFile(line);
            if (advLogger)
                WriteAdvLogger(eventName, phase, fields);
        }

        internal static KeyValuePair<string, object> Pair(string key, object value) =>
            new KeyValuePair<string, object>(key, value);

        private static FastBlueprintLoadTrace TryStartWithName(
            string blueprintFileName,
            long fileBytes,
            FastBlueprintLoadTier tier)
        {
            DateTime now = DateTime.UtcNow;
            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string logPath = null;
            bool fileEnabled = false;
            try
            {
                string root = ResolveProfileRoot();
                logPath = BuildLogPathForVerification(root, blueprintFileName, now, sessionId);
                string directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                using (new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                }
                fileEnabled = true;
            }
            catch
            {
                fileEnabled = false;
            }

            var trace = new FastBlueprintLoadTrace(
                sessionId,
                blueprintFileName,
                fileBytes,
                tier,
                logPath,
                fileEnabled,
                now);
            trace.Event(
                "trace-start",
                "start",
                advLogger: true,
                Pair("log_file", fileEnabled ? Path.GetFileName(logPath) : "unavailable"));
            return trace;
        }

        private string BuildLine(
            string eventName,
            string phase,
            params KeyValuePair<string, object>[] fields)
        {
            var pairs = new List<KeyValuePair<string, object>>
            {
                Pair("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                Pair("elapsed_ms", _elapsed.Elapsed.TotalMilliseconds),
                Pair("session", _sessionId),
                Pair("tier", _tier.ToString()),
                Pair("event", eventName),
                Pair("phase", phase),
                Pair("file", _blueprintFileName),
                Pair("file_bytes", _fileBytes),
                Pair("gc_memory_bytes", GC.GetTotalMemory(false))
            };

            try
            {
                pairs.Add(Pair(
                    "process_private_bytes",
                    Process.GetCurrentProcess().PrivateMemorySize64));
            }
            catch
            {
            }

            if (fields != null)
                pairs.AddRange(fields);
            return FormatKeyValues(pairs);
        }

        private static string FormatKeyValues(IEnumerable<KeyValuePair<string, object>> pairs)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, object> pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(pair.Key.Trim());
                builder.Append('=');
                builder.Append(FormatValue(pair.Value));
            }
            return builder.ToString();
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "\"\"";
            switch (value)
            {
                case bool boolean:
                    return boolean ? "true" : "false";
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                case float single:
                    return single.ToString("0.###", CultureInfo.InvariantCulture);
                case double dbl:
                    return dbl.ToString("0.###", CultureInfo.InvariantCulture);
                case decimal dec:
                    return dec.ToString("0.###", CultureInfo.InvariantCulture);
                default:
                    return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static string Quote(string value)
        {
            value = (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
            return "\"" + value + "\"";
        }

        private void WriteFile(string line)
        {
            if (!_fileEnabled || string.IsNullOrWhiteSpace(_logPath))
                return;
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine, Utf8NoBom);
                }
            }
            catch
            {
            }
        }

        private void WriteAdvLogger(
            string eventName,
            string phase,
            KeyValuePair<string, object>[] fields)
        {
            try
            {
                string summary = "[EndlessShapes Unlimited] [fast blueprint load] " +
                    "session=" + _sessionId +
                    " event=" + eventName +
                    " phase=" + phase +
                    " tier=" + _tier +
                    " file=" + _blueprintFileName;
                string percent = FieldValue(fields, "percent");
                if (!string.IsNullOrWhiteSpace(percent))
                    summary += " percent=" + percent;
                string reason = FieldValue(fields, "reason");
                if (!string.IsNullOrWhiteSpace(reason))
                    summary += " reason=" + reason;
                string totalElapsed = FieldValue(fields, "total_elapsed_ms");
                if (!string.IsNullOrWhiteSpace(totalElapsed))
                    summary += " total_elapsed_ms=" + totalElapsed;
                string constructLoaded = FieldValue(fields, "construct_loaded");
                if (!string.IsNullOrWhiteSpace(constructLoaded))
                    summary += " construct_loaded=" + constructLoaded;
                string aliveDeadBlocks = FieldValue(fields, "alive_dead_blocks");
                if (!string.IsNullOrWhiteSpace(aliveDeadBlocks))
                    summary += " alive_dead_blocks=" + aliveDeadBlocks;
                string logFile = FieldValue(fields, "log_file");
                if (!string.IsNullOrWhiteSpace(logFile))
                    summary += " log_file=" + logFile;
                AdvLogger.LogInfo(summary);
            }
            catch
            {
            }
        }

        private static string FieldValue(
            IEnumerable<KeyValuePair<string, object>> fields,
            string key)
        {
            if (fields == null)
                return null;
            foreach (KeyValuePair<string, object> field in fields)
            {
                if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToString(field.Value, CultureInfo.InvariantCulture);
            }
            return null;
        }

        private KeyValuePair<string, object>[] WithTotalElapsed(
            KeyValuePair<string, object>[] fields)
        {
            var merged = new List<KeyValuePair<string, object>>(fields ?? Array.Empty<KeyValuePair<string, object>>())
            {
                Pair("total_elapsed_ms", _elapsed.Elapsed.TotalMilliseconds)
            };
            return merged.ToArray();
        }

        private static string ResolveProfileRoot()
        {
            try
            {
                string root = Get.ProfilePaths?.ProfileRootDir()?.ToString();
                if (!string.IsNullOrWhiteSpace(root))
                    return root;
            }
            catch
            {
            }
            return Path.GetTempPath();
        }

        private static string SanitizeFileName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "standalone" : name.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            name = name.Replace(':', '_')
                .Replace('\\', '_')
                .Replace('/', '_');
            while (name.Contains(".."))
                name = name.Replace("..", ".");
            if (name.Length > 80)
                name = name.Substring(0, 80);
            return string.IsNullOrWhiteSpace(name) ? "standalone" : name;
        }

        private static double Percent(long current, long total)
        {
            if (total <= 0L)
                return 0d;
            if (current <= 0L)
                return 0d;
            return Math.Min(100d, current * 100d / total);
        }
    }

    internal sealed class FastBlueprintLoadTraceScope : IDisposable
    {
        private readonly FastBlueprintLoadTrace _trace;
        private readonly string _phase;
        private readonly Stopwatch _timer;
        private readonly Action _onDispose;
        private readonly KeyValuePair<string, object>[] _fields;
        private bool _completed;
        private bool _disposed;

        internal FastBlueprintLoadTraceScope(
            FastBlueprintLoadTrace trace,
            string phase,
            Action onDispose,
            params KeyValuePair<string, object>[] fields)
        {
            _trace = trace;
            _phase = phase;
            _onDispose = onDispose;
            _fields = fields ?? Array.Empty<KeyValuePair<string, object>>();
            _timer = Stopwatch.StartNew();
            _trace?.PhaseStart(phase, _fields);
        }

        internal FastBlueprintLoadTrace Trace => _trace;

        internal KeyValuePair<string, object>[] Fields => _fields;

        internal void Complete(params KeyValuePair<string, object>[] fields)
        {
            if (_completed)
                return;
            _completed = true;
            _trace?.PhaseEnd(_phase, _timer, fields);
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_completed)
                _trace?.PhaseEnd(_phase, _timer, FastBlueprintLoadTrace.Pair("completed", false));
            try { _onDispose?.Invoke(); }
            catch { }
        }
    }

    internal sealed class FastBlueprintLoadProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly FastBlueprintLoadTrace _trace;
        private readonly string _phase;
        private readonly long _total;
        private long _read;

        internal FastBlueprintLoadProgressStream(
            Stream inner,
            FastBlueprintLoadTrace trace,
            string phase,
            long total)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _trace = trace;
            _phase = phase;
            _total = total;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _read += read;
                _trace?.Heartbeat(_phase, _read, _total, "bytes");
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
