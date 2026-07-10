using System;
using System.Collections.Generic;
using System.Globalization;
using BrilliantSkies.PlayerProfiles;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal readonly struct SurfaceCoordinateSliderRange
    {
        internal SurfaceCoordinateSliderRange(Vector3 minimum, Vector3 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        internal Vector3 Minimum { get; }

        internal Vector3 Maximum { get; }

        internal float MinimumFor(int component) =>
            component == 0 ? Minimum.x : component == 1 ? Minimum.y : Minimum.z;

        internal float MaximumFor(int component) =>
            component == 0 ? Maximum.x : component == 1 ? Maximum.y : Maximum.z;
    }

    internal static class SurfaceCoordinateSliderSettings
    {
        internal const float DefaultMinimum = -10f;
        internal const float DefaultMaximum = 10f;
        internal const float DefaultStep = 0.1f;
        internal const float MaximumStep = 1000f;
        internal const float ResolutionMetres = 0.001f;
        internal const float ExpansionMarginMetres = 1f;
        internal const float ExpansionMarginFraction = 0.05f;

        private static readonly SerializationHudProfile.ProfileData Fallback =
            new SerializationHudProfile.ProfileData();

        internal static SurfaceCoordinateSliderRange Current
        {
            get
            {
                SerializationHudProfile.ProfileData data = Data;
                SurfaceCoordinateSliderRange range = ResolveProfileData(data, out bool changed);
                if (changed)
                    SaveProfileBestEffort();
                return range;
            }
        }

        internal static SurfaceCoordinateSliderRange ResolveProfileData(
            SerializationHudProfile.ProfileData data,
            out bool changed)
        {
            data = data ?? Fallback;
            changed = ResolveStoredAxis(
                data.SurfaceCoordinateSliderMinX,
                data.SurfaceCoordinateSliderMaxX,
                out float minX,
                out float maxX);
            changed |= ResolveStoredAxis(
                data.SurfaceCoordinateSliderMinY,
                data.SurfaceCoordinateSliderMaxY,
                out float minY,
                out float maxY);
            changed |= ResolveStoredAxis(
                data.SurfaceCoordinateSliderMinZ,
                data.SurfaceCoordinateSliderMaxZ,
                out float minZ,
                out float maxZ);
            changed |= ResolveStoredStep(data.SurfaceCoordinateStepX, out float stepX);
            changed |= ResolveStoredStep(data.SurfaceCoordinateStepY, out float stepY);
            changed |= ResolveStoredStep(data.SurfaceCoordinateStepZ, out float stepZ);

            if (changed)
            {
                data.SurfaceCoordinateSliderMinX = minX;
                data.SurfaceCoordinateSliderMaxX = maxX;
                data.SurfaceCoordinateSliderMinY = minY;
                data.SurfaceCoordinateSliderMaxY = maxY;
                data.SurfaceCoordinateSliderMinZ = minZ;
                data.SurfaceCoordinateSliderMaxZ = maxZ;
                data.SurfaceCoordinateStepX = stepX;
                data.SurfaceCoordinateStepY = stepY;
                data.SurfaceCoordinateStepZ = stepZ;
            }

            return new SurfaceCoordinateSliderRange(
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, maxY, maxZ));
        }

        internal static float CurrentStep(int component)
        {
            if (component < 0 || component > 2)
                return DefaultStep;

            SerializationHudProfile.ProfileData data = Data;
            ResolveProfileData(data, out bool changed);
            if (changed)
                SaveProfileBestEffort();
            return component == 0
                ? data.SurfaceCoordinateStepX
                : component == 1
                    ? data.SurfaceCoordinateStepY
                    : data.SurfaceCoordinateStepZ;
        }

        internal static bool TrySetStep(
            int component,
            float value,
            out bool saved,
            out string message)
        {
            saved = false;
            if (component < 0 || component > 2)
            {
                message = "Surface coordinate step axis is invalid.";
                return false;
            }

            if (!TryNormalizeStep(value, out float normalized))
            {
                message = "Surface coordinate step must be finite and between 0.001 m and 1000 m.";
                return false;
            }

            SerializationHudProfile.ProfileData data = Data;
            if (component == 0)
                data.SurfaceCoordinateStepX = normalized;
            else if (component == 1)
                data.SurfaceCoordinateStepY = normalized;
            else
                data.SurfaceCoordinateStepZ = normalized;
            saved = SaveProfileBestEffort();
            message = saved
                ? "Surface coordinate step saved."
                : "Surface coordinate step applied for this session, but the profile could not be saved.";
            return true;
        }

        internal static bool ResetStep(int component, out bool saved, out string message) =>
            TrySetStep(component, DefaultStep, out saved, out message);

        internal static bool TrySet(
            Vector3 minimum,
            Vector3 maximum,
            out bool saved,
            out string message)
        {
            saved = false;
            if (!TryResolveUserRange(minimum, maximum, out SurfaceCoordinateSliderRange range, out message))
                return false;

            SerializationHudProfile.ProfileData data = Data;
            data.SurfaceCoordinateSliderMinX = range.Minimum.x;
            data.SurfaceCoordinateSliderMaxX = range.Maximum.x;
            data.SurfaceCoordinateSliderMinY = range.Minimum.y;
            data.SurfaceCoordinateSliderMaxY = range.Maximum.y;
            data.SurfaceCoordinateSliderMinZ = range.Minimum.z;
            data.SurfaceCoordinateSliderMaxZ = range.Maximum.z;
            saved = SaveProfileBestEffort();
            message = saved
                ? "Surface coordinate slider ranges saved."
                : "Surface coordinate slider ranges applied for this session, but the profile could not be saved.";
            return true;
        }

        internal static bool ResetDefaults()
        {
            Vector3 minimum = Vector3.one * DefaultMinimum;
            Vector3 maximum = Vector3.one * DefaultMaximum;
            return TrySet(minimum, maximum, out bool saved, out string _) && saved;
        }

        internal static bool TryResolveUserRange(
            Vector3 minimum,
            Vector3 maximum,
            out SurfaceCoordinateSliderRange range,
            out string message)
        {
            range = default;
            if (!TryNormalizeAxis(minimum.x, maximum.x, out float minX, out float maxX))
            {
                message = "X slider range needs finite Min and Max values at least 0.001 m apart.";
                return false;
            }

            if (!TryNormalizeAxis(minimum.y, maximum.y, out float minY, out float maxY))
            {
                message = "Y slider range needs finite Min and Max values at least 0.001 m apart.";
                return false;
            }

            if (!TryNormalizeAxis(minimum.z, maximum.z, out float minZ, out float maxZ))
            {
                message = "Z slider range needs finite Min and Max values at least 0.001 m apart.";
                return false;
            }

            range = new SurfaceCoordinateSliderRange(
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, maxY, maxZ));
            message = string.Empty;
            return true;
        }

        internal static bool TryExpandDisplayRange(
            float configuredMinimum,
            float configuredMaximum,
            IEnumerable<float> stagedValues,
            out float displayMinimum,
            out float displayMaximum)
        {
            displayMinimum = configuredMinimum;
            displayMaximum = configuredMaximum;
            float initialSpan = displayMaximum - displayMinimum;
            if (!DecorationEditMath.IsFinite(displayMinimum) ||
                !DecorationEditMath.IsFinite(displayMaximum) ||
                !DecorationEditMath.IsFinite(initialSpan) ||
                displayMinimum >= displayMaximum ||
                initialSpan < ResolutionMetres)
            {
                return false;
            }

            if (stagedValues == null)
                return true;

            float observedMinimum = displayMinimum;
            float observedMaximum = displayMaximum;
            foreach (float value in stagedValues)
            {
                if (!DecorationEditMath.IsFinite(value))
                    continue;
                observedMinimum = Math.Min(observedMinimum, value);
                observedMaximum = Math.Max(observedMaximum, value);
            }

            float configuredSpan = displayMaximum - displayMinimum;
            if (!DecorationEditMath.IsFinite(configuredSpan) || configuredSpan < ResolutionMetres)
                return false;

            float margin = Math.Max(
                ExpansionMarginMetres,
                configuredSpan * ExpansionMarginFraction);
            if (!DecorationEditMath.IsFinite(margin))
                return false;

            if (observedMinimum < displayMinimum)
            {
                float expanded = observedMinimum - margin;
                displayMinimum = DecorationEditMath.IsFinite(expanded)
                    ? expanded
                    : observedMinimum;
            }

            if (observedMaximum > displayMaximum)
            {
                float expanded = observedMaximum + margin;
                displayMaximum = DecorationEditMath.IsFinite(expanded)
                    ? expanded
                    : observedMaximum;
            }

            float displaySpan = displayMaximum - displayMinimum;
            return DecorationEditMath.IsFinite(displayMinimum) &&
                   DecorationEditMath.IsFinite(displayMaximum) &&
                   DecorationEditMath.IsFinite(displaySpan) &&
                   displaySpan >= ResolutionMetres;
        }

        internal static string Format(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static SerializationHudProfile.ProfileData Data
        {
            get
            {
                try
                {
                    return SerializationHudProfile.Data ?? Fallback;
                }
                catch
                {
                    return Fallback;
                }
            }
        }

        private static bool ResolveStoredAxis(
            float minimum,
            float maximum,
            out float resolvedMinimum,
            out float resolvedMaximum)
        {
            if (TryNormalizeAxis(minimum, maximum, out resolvedMinimum, out resolvedMaximum))
            {
                return resolvedMinimum != minimum || resolvedMaximum != maximum;
            }

            resolvedMinimum = DefaultMinimum;
            resolvedMaximum = DefaultMaximum;
            return true;
        }

        private static bool ResolveStoredStep(float value, out float resolved)
        {
            if (TryNormalizeStep(value, out resolved))
                return resolved != value;

            resolved = DefaultStep;
            return true;
        }

        internal static bool TryNormalizeStep(float value, out float normalized)
        {
            normalized = 0f;
            if (!DecorationEditMath.IsFinite(value) ||
                value < ResolutionMetres ||
                value > MaximumStep)
            {
                return false;
            }

            normalized = DecorationEditMath.Snap(value, ResolutionMetres);
            if (normalized > MaximumStep)
                normalized = MaximumStep;
            return DecorationEditMath.IsFinite(normalized) &&
                   normalized >= ResolutionMetres &&
                   normalized <= MaximumStep;
        }

        private static bool TryNormalizeAxis(
            float minimum,
            float maximum,
            out float normalizedMinimum,
            out float normalizedMaximum)
        {
            normalizedMinimum = 0f;
            normalizedMaximum = 0f;
            if (!DecorationEditMath.IsFinite(minimum) ||
                !DecorationEditMath.IsFinite(maximum))
            {
                return false;
            }

            normalizedMinimum = DecorationEditMath.Snap(minimum, ResolutionMetres);
            normalizedMaximum = DecorationEditMath.Snap(maximum, ResolutionMetres);
            float span = normalizedMaximum - normalizedMinimum;
            return DecorationEditMath.IsFinite(normalizedMinimum) &&
                   DecorationEditMath.IsFinite(normalizedMaximum) &&
                   DecorationEditMath.IsFinite(span) &&
                   normalizedMinimum < normalizedMaximum &&
                   span >= ResolutionMetres;
        }

        private static bool SaveProfileBestEffort()
        {
            try
            {
                ProfileManager.Instance.Save(module => module is SerializationHudProfile);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
