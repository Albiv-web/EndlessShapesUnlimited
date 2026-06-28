using System;
using System.Globalization;
using BrilliantSkies.Ftd.Avatar.HUD;
using BrilliantSkies.Ui.Consoles.Styles;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter.SerializationHud
{
    [HarmonyPatch(
        typeof(DrawExtraVehicleInfo),
        nameof(DrawExtraVehicleInfo.DrawRHSTextDisplay),
        new[] { typeof(MainConstruct), typeof(Rectum) })]
    internal static class SerializationHudRenderer
    {
        private const float RefreshIntervalSeconds = 0.25f;
        private const string VanillaBufferLabel = "10 MB";
        private const string LegacyHeaderLimitLabel = "64 KiB legacy";
        private const string LegacyDataLimitLabel = "6.25 MiB legacy";
        private const string EsuHeaderLimitLabel = "4 MiB ESU";
        private const string EsuDataLimitLabel = "64 MiB ESU";
        private static readonly Color WarningColor = new Color(1f, 0.72f, 0.2f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.25f, 0.2f, 1f);
        private static MainConstruct _cachedConstruct;
        private static SerializationForecast _cachedForecast;
        private static float _nextRefresh;

        internal static void Postfix(MainConstruct C, Rectum R)
        {
            if (C != null)
                _cachedConstruct = C;

            if (!SerializationHudRegistration.Enabled ||
                DecorationEditorInputScope.Active ||
                SmartBuildInputScope.Active ||
                C == null ||
                R == null)
            {
                return;
            }

            try
            {
                RefreshIfNeeded(C);
                SerializationForecast forecast = _cachedForecast;
                if (forecast == null)
                    return;

                var styles = ConsoleStyles.Instance.Styles.Hud;
                string exactLabel = forecast.Exact ? "exact" : "estimated";

                // FTD's RHS rect stack grows upward. Draw the last visual row first so
                // the HUD reads naturally from title to details when rendered in-game.
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "save",
                    $"Last saved: {FormatName(forecast.SavedFormat)}",
                    Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "open",
                    $"Last loaded: {FormatName(forecast.LoadedFormat)}",
                    Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    $"Save buffer: {FormatSaveBuffer(forecast.BlueprintUsage)}",
                    BufferColor(forecast.BlueprintUsage));
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    $"Largest stream: {FormatLargestStream(forecast.BlueprintUsage)} / {VanillaBufferLabel}",
                    BufferColor(forecast.BlueprintUsage));
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    $"Payload total: {FormatPayloadTotal(forecast.BlueprintUsage)}",
                    Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    $"Peak data: {FormatBytes(forecast.PeakDataBytes)} / {FormatDataLimit(forecast.PeakDataBytes)}",
                    UsageColor(
                        forecast.PeakDataBytes,
                        SerializationForecastCalculator.LegacyDataMaximum,
                        (ulong)DecoLimits.MaxDataSortedBytes,
                        forecast.Format));
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    $"Peak header: {FormatBytes(forecast.PeakHeaderBytes)} / {FormatHeaderLimit(forecast.PeakHeaderBytes)}",
                    UsageColor(
                        forecast.PeakHeaderBytes,
                        SerializationForecastCalculator.LegacyHeaderMaximum,
                        (ulong)DecoLimits.MaxHeaderBytes,
                        forecast.Format));
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "count",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Manager max: {0:N0} / 100k",
                        forecast.Decorations.PeakManagerDecorations),
                    Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "mesh",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Decorations: {0:N0} total",
                        forecast.Decorations.TotalDecorations),
                    Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    forecast.Format == SerializationWireFormat.OverLimit ? "risk" : "save",
                    $"Wire format: {FormatName(forecast.Format)} ({exactLabel})",
                    forecast.Format == SerializationWireFormat.OverLimit
                        ? ErrorColor
                        : forecast.Format == SerializationWireFormat.Sentinel
                            ? WarningColor
                            : Color.white);
                styles.InfoHeader.Rect(R.GetRectAndMove(), "EndlessShapes save", null);
            }
            catch (Exception exception)
            {
                BlueprintConverter_SaveTelemetry_Patch.LogTelemetryFailure(
                    "draw the serialization HUD",
                    exception);
            }
        }

        internal static MainConstruct CachedConstruct => _cachedConstruct;

        internal static void Invalidate(MainConstruct construct)
        {
            if (construct != null)
                _cachedConstruct = construct;
            _cachedForecast = null;
            _nextRefresh = 0f;
        }

        internal static string FormatName(SerializationWireFormat format)
        {
            switch (format)
            {
                case SerializationWireFormat.Legacy:
                    return "LEGACY WIRE";
                case SerializationWireFormat.Sentinel:
                    return "SENTINEL";
                case SerializationWireFormat.OverLimit:
                    return "OVER LIMIT";
                default:
                    return "--";
            }
        }

        internal static string FormatBytes(ulong bytes)
        {
            const double kibibyte = 1024d;
            const double mebibyte = 1024d * 1024d;
            if (bytes >= (ulong)mebibyte)
                return (bytes / mebibyte).ToString("0.# MiB", CultureInfo.InvariantCulture);
            if (bytes >= 1024UL)
                return (bytes / kibibyte).ToString("0.# KiB", CultureInfo.InvariantCulture);
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        internal static string FormatSaveBuffer(BlueprintSerializationUsage usage)
        {
            if (usage == null || !usage.HasData)
                return "unknown";
            return usage.RequiresModBuffer ? "ESU required" : "Vanilla OK";
        }

        internal static string FormatPayloadTotal(BlueprintSerializationUsage usage) =>
            usage == null || !usage.HasData
                ? "--"
                : FormatBytes(usage.PayloadBytes);

        internal static string FormatLargestStream(BlueprintSerializationUsage usage) =>
            usage == null || !usage.HasData
                ? "--"
                : FormatBytes(usage.LargestStreamBytes);

        internal static string FormatHeaderLimit(ulong bytes) =>
            bytes <= SerializationForecastCalculator.LegacyHeaderMaximum
                ? LegacyHeaderLimitLabel
                : EsuHeaderLimitLabel;

        internal static string FormatDataLimit(ulong bytes) =>
            bytes <= SerializationForecastCalculator.LegacyDataMaximum
                ? LegacyDataLimitLabel
                : EsuDataLimitLabel;

        private static void RefreshIfNeeded(MainConstruct construct)
        {
            float now = Time.unscaledTime;
            if (ReferenceEquals(_cachedConstruct, construct) &&
                _cachedForecast != null &&
                now < _nextRefresh)
            {
                return;
            }

            DecorationUsageSnapshot current = DecorationUsageSnapshot.Capture(construct);
            SerializationTelemetry.TryGetHistory(
                construct,
                out CraftSerializationSnapshot loaded,
                out CraftSerializationSnapshot saved,
                out CraftSerializationSnapshot measured);
            _cachedForecast = SerializationForecastCalculator.Calculate(
                construct,
                current,
                loaded,
                saved,
                measured);
            _nextRefresh = now + RefreshIntervalSeconds;
        }

        private static void DrawRow(
            StylePlus style,
            Rectum rects,
            string icon,
            string text,
            Color color)
        {
            style.TextTintOnceOff = color;
            Rect row = rects.GetRectAndMove();
            Texture2D texture = DecorationEditorIconCatalog.GetRuntimeIcon(icon);
            if (texture != null)
            {
                float size = Mathf.Max(14f, Mathf.Min(row.height - 4f, 22f));
                GUI.DrawTexture(
                    new Rect(row.x + 4f, row.y + (row.height - size) * 0.5f, size, size),
                    texture,
                    ScaleMode.ScaleToFit,
                    alphaBlend: true);
                row.xMin += size + 8f;
            }
            style.Rect(row, text, null);
        }

        private static Color BufferColor(BlueprintSerializationUsage usage)
        {
            if (usage == null || !usage.HasData)
                return Color.white;
            return usage.RequiresModBuffer ? WarningColor : Color.white;
        }

        private static Color UsageColor(
            ulong value,
            ulong legacyMaximum,
            ulong hardMaximum,
            SerializationWireFormat format)
        {
            if (format == SerializationWireFormat.OverLimit || value > hardMaximum)
                return ErrorColor;
            if (value >= legacyMaximum * 4UL / 5UL)
                return WarningColor;
            return Color.white;
        }
    }
}
