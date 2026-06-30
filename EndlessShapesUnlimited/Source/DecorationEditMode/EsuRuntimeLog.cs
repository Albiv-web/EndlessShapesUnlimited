using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum EsuRuntimeLogSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class EsuRuntimeLogEntry
    {
        internal EsuRuntimeLogEntry(
            int sequence,
            DateTime timestamp,
            string source,
            EsuRuntimeLogSeverity severity,
            string message,
            string detail)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            Source = source ?? "ESU";
            Severity = severity;
            Message = message ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal int Sequence { get; }

        internal DateTime Timestamp { get; }

        internal string Source { get; }

        internal EsuRuntimeLogSeverity Severity { get; }

        internal string Message { get; }

        internal string Detail { get; }

        internal string FormatLine()
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:HH:mm:ss}] [{1}] [{2}] {3}",
                Timestamp,
                Severity,
                Source,
                Message);
            return string.IsNullOrWhiteSpace(Detail)
                ? line
                : line + Environment.NewLine + Detail;
        }
    }

    internal static class EsuRuntimeLog
    {
        internal const int Capacity = 25;

        private static readonly List<EsuRuntimeLogEntry> Entries =
            new List<EsuRuntimeLogEntry>(Capacity);

        private static int _nextSequence;

        internal static int Count => Entries.Count;

        internal static IReadOnlyList<EsuRuntimeLogEntry> Snapshot(bool newestFirst = false)
        {
            EsuRuntimeLogEntry[] snapshot = Entries.ToArray();
            if (newestFirst)
            {
                for (int left = 0, right = snapshot.Length - 1; left < right; left++, right--)
                {
                    EsuRuntimeLogEntry swap = snapshot[left];
                    snapshot[left] = snapshot[right];
                    snapshot[right] = swap;
                }
            }

            return snapshot;
        }

        internal static IReadOnlyList<EsuRuntimeLogEntry> Filtered(
            EsuRuntimeLogSeverity? severity,
            bool newestFirst = true)
        {
            IEnumerable<EsuRuntimeLogEntry> entries = Entries;
            if (severity.HasValue)
                entries = entries.Where(entry => entry.Severity == severity.Value);
            if (newestFirst)
                entries = entries.Reverse();
            return entries.ToArray();
        }

        internal static void Clear() =>
            Entries.Clear();

        internal static EsuRuntimeLogEntry Info(
            string source,
            string message,
            string detail = null) =>
            Add(source, EsuRuntimeLogSeverity.Info, message, detail);

        internal static EsuRuntimeLogEntry Warning(
            string source,
            string message,
            string detail = null) =>
            Add(source, EsuRuntimeLogSeverity.Warning, message, detail);

        internal static EsuRuntimeLogEntry Error(
            string source,
            string message,
            string detail = null) =>
            Add(source, EsuRuntimeLogSeverity.Error, message, detail);

        internal static EsuRuntimeLogEntry Exception(
            string source,
            Exception exception,
            string message = null)
        {
            if (exception == null)
                return Error(source, message ?? "Unknown exception.");

            string summary = string.IsNullOrWhiteSpace(message)
                ? exception.Message
                : message;
            return Error(source, summary, exception.ToString());
        }

        internal static EsuRuntimeLogEntry FromNotification(
            string source,
            EsuHudNotificationKind kind,
            string message,
            string detail = null) =>
            Add(source, ToSeverity(kind), message, detail);

        internal static string FormatForClipboard(
            IEnumerable<EsuRuntimeLogEntry> entries = null)
        {
            var builder = new StringBuilder();
            foreach (EsuRuntimeLogEntry entry in entries ?? Snapshot(newestFirst: true))
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine(entry.FormatLine());
            }

            return builder.ToString().TrimEnd(new[] { '\r', '\n' });
        }

        internal static EsuRuntimeLogSeverity ToSeverity(EsuHudNotificationKind kind)
        {
            switch (kind)
            {
                case EsuHudNotificationKind.Error:
                    return EsuRuntimeLogSeverity.Error;
                case EsuHudNotificationKind.Warning:
                    return EsuRuntimeLogSeverity.Warning;
                default:
                    return EsuRuntimeLogSeverity.Info;
            }
        }

        internal static EsuRuntimeLogEntry Add(
            string source,
            EsuRuntimeLogSeverity severity,
            string message,
            string detail = null)
        {
            message = (message ?? string.Empty).Trim();
            detail = (detail ?? string.Empty).Trim();
            if (message.Length == 0 && detail.Length == 0)
                return null;

            if (message.Length == 0)
                message = detail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "ESU log entry";

            var entry = new EsuRuntimeLogEntry(
                ++_nextSequence,
                DateTime.Now,
                string.IsNullOrWhiteSpace(source) ? "ESU" : source.Trim(),
                severity,
                message,
                detail);
            Entries.Add(entry);
            while (Entries.Count > Capacity)
                Entries.RemoveAt(0);
            return entry;
        }
    }
}
