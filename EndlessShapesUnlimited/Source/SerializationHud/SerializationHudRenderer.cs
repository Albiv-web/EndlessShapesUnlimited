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
        private const int MinimumHudFontSize = 7;
        private const int MaximumHudFontSize = 14;
        private const int MaximumHudHeaderFontSize = 15;
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
                    $"Wire bytes: {FormatName(forecast.Format)} ({exactLabel})",
                    forecast.Format == SerializationWireFormat.OverLimit
                        ? ErrorColor
                        : forecast.Format == SerializationWireFormat.Sentinel
                            ? WarningColor
                            : Color.white);
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "risk",
                    VanillaCompatibilityGuard.FormatVanillaLoadStatus(forecast),
                    VanillaCompatibilityGuard.VanillaLoadStatusColor(
                        forecast,
                        WarningColor,
                        ErrorColor));
                DrawRow(
                    styles.TextWithPadding,
                    R,
                    "count",
                    VanillaCompatibilityGuard.FormatVanillaEditorStatus(forecast),
                    VanillaCompatibilityGuard.VanillaEditorStatusColor(
                        forecast,
                        WarningColor,
                        ErrorColor));
                DrawHeader(styles.InfoHeader, R, "EndlessShapes save");
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
            Rect row = rects.GetRectAndMove();
            DrawHudBackground(style, row);

            Texture2D texture = DecorationEditorIconCatalog.GetRuntimeIcon(icon);
            if (texture != null)
            {
                float size = Mathf.Clamp(row.height * 0.68f, 10f, 18f);
                GUI.DrawTexture(
                    new Rect(row.x + 4f, row.y + (row.height - size) * 0.5f, size, size),
                    texture,
                    ScaleMode.ScaleToFit,
                    alphaBlend: true);
                row.xMin += size + 8f;
            }

            DrawFittedHudText(
                style,
                new Rect(
                    row.x + 2f,
                    row.y + 1f,
                    Mathf.Max(1f, row.width - 4f),
                    Mathf.Max(1f, row.height - 2f)),
                text,
                color,
                header: false);
        }

        private static void DrawHeader(StylePlus style, Rectum rects, string text)
        {
            Rect row = rects.GetRectAndMove();
            DrawHudBackground(style, row);
            DrawFittedHudText(
                style,
                new Rect(
                    row.x + 4f,
                    row.y + 1f,
                    Mathf.Max(1f, row.width - 8f),
                    Mathf.Max(1f, row.height - 2f)),
                text,
                Color.white,
                header: true);
        }

        private static void DrawHudBackground(StylePlus style, Rect row)
        {
            GUIStyle background = style?.Style != null
                ? new GUIStyle(style.Style)
                : new GUIStyle(GUI.skin.box);
            background.normal.textColor = Color.clear;
            background.hover.textColor = Color.clear;
            background.active.textColor = Color.clear;
            GUI.Label(row, GUIContent.none, background);
        }

        private static void DrawFittedHudText(
            StylePlus source,
            Rect rect,
            string text,
            Color color,
            bool header)
        {
            GUIStyle textStyle = source?.Style != null
                ? new GUIStyle(source.Style)
                : new GUIStyle(GUI.skin.label);
            textStyle.wordWrap = false;
            textStyle.clipping = TextClipping.Clip;
            textStyle.alignment = header ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            textStyle.fontStyle = header ? FontStyle.BoldAndItalic : FontStyle.Normal;
            textStyle.padding = new RectOffset(0, 0, 0, 0);
            textStyle.normal.textColor = color;
            textStyle.hover.textColor = color;
            textStyle.active.textColor = color;
            textStyle.fontSize = FittedHudFontSize(textStyle, text ?? string.Empty, rect, header);
            GUI.Label(rect, text, textStyle);
        }

        private static int FittedHudFontSize(
            GUIStyle style,
            string text,
            Rect rect,
            bool header)
        {
            int minimum = MinimumHudFontSize;
            int size = HudBaseFontSize(rect.height, header);
            var content = new GUIContent(text);
            for (; size > minimum; size--)
            {
                style.fontSize = size;
                Vector2 measured = style.CalcSize(content);
                if (measured.x <= rect.width &&
                    measured.y <= rect.height + 1f)
                {
                    return size;
                }
            }

            return minimum;
        }

        private static int HudBaseFontSize(float rowHeight, bool header)
        {
            float screenScale = 1f;
            if (Screen.width > 0 && Screen.height > 0)
            {
                screenScale = Mathf.Clamp(
                    Mathf.Min(Screen.width / 1920f, Screen.height / 1080f),
                    0.55f,
                    1f);
            }

            float maximum = header ? MaximumHudHeaderFontSize : MaximumHudFontSize;
            float rowLimited = Mathf.Max(MinimumHudFontSize, rowHeight - 6f);
            return Mathf.RoundToInt(Mathf.Clamp(maximum * screenScale, MinimumHudFontSize, rowLimited));
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
