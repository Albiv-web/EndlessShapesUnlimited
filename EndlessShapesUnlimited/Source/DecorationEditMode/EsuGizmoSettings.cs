using System;
using System.Globalization;
using BrilliantSkies.PlayerProfiles;
using DecoLimitLifter.SerializationHud;

namespace DecoLimitLifter.DecorationEditMode
{
    internal readonly struct EsuGizmoStyle
    {
        internal EsuGizmoStyle(
            float moveSize,
            float rotateSize,
            float scaleSize,
            float thickness,
            float hitAreaPixels)
        {
            MoveSize = moveSize;
            RotateSize = rotateSize;
            ScaleSize = scaleSize;
            Thickness = thickness;
            HitAreaPixels = hitAreaPixels;
        }

        internal float MoveSize { get; }

        internal float RotateSize { get; }

        internal float ScaleSize { get; }

        internal float Thickness { get; }

        internal float HitAreaPixels { get; }

        internal float FreeMoveCorePixels => EsuGizmoSettings.FreeMoveCorePixels;

        internal float MoveLength => EsuGizmoSettings.BaseMoveLength * MoveSize;

        internal float AnchorLength => EsuGizmoSettings.BaseAnchorLength * MoveSize;

        internal float RotationRadius => EsuGizmoSettings.BaseRotationRadius * RotateSize;

        internal float ScaleLength => EsuGizmoSettings.BaseScaleLength * ScaleSize;

        internal float LineWidth(float baseWidth)
        {
            if (!IsPositiveFinite(baseWidth))
                return 0f;

            float width = baseWidth * Thickness;
            return IsPositiveFinite(width) ? width : 0f;
        }

        private static bool IsPositiveFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }

    internal static class EsuGizmoSettings
    {
        internal const float DefaultMoveSize = 1f;
        internal const float DefaultRotateSize = 1f;
        internal const float DefaultScaleSize = 1f;
        internal const float DefaultThickness = 1f;
        internal const float DefaultHitAreaPixels = 18f;
        internal const float FreeMoveCorePixels = 7f;

        internal const float MinimumSize = 0.5f;
        internal const float MaximumSize = 3f;
        internal const float MinimumThickness = 0.5f;
        internal const float MaximumThickness = 3f;
        internal const float MinimumHitAreaPixels = 8f;
        internal const float MaximumHitAreaPixels = 40f;

        internal const float BaseMoveLength = 1.25f;
        internal const float BaseAnchorLength = 1.25f;
        internal const float BaseRotationRadius = 0.82f;
        internal const float BaseScaleLength = 1.25f;

        private static readonly SerializationHudProfile.ProfileData Fallback =
            new SerializationHudProfile.ProfileData();

        internal static EsuGizmoStyle Current
        {
            get
            {
                SerializationHudProfile.ProfileData data = Data;
                return Resolve(
                    data.DecorationGizmoMoveSize,
                    data.DecorationGizmoRotateSize,
                    data.DecorationGizmoScaleSize,
                    data.DecorationGizmoThickness,
                    data.DecorationGizmoHitAreaPixels);
            }
        }

        internal static EsuGizmoStyle Resolve(
            float moveSize,
            float rotateSize,
            float scaleSize,
            float thickness,
            float hitAreaPixels) =>
            new EsuGizmoStyle(
                ClampSize(moveSize, DefaultMoveSize),
                ClampSize(rotateSize, DefaultRotateSize),
                ClampSize(scaleSize, DefaultScaleSize),
                ClampThickness(thickness),
                ClampHitAreaPixels(hitAreaPixels));

        internal static bool Set(
            float moveSize,
            float rotateSize,
            float scaleSize,
            float thickness,
            float hitAreaPixels)
        {
            EsuGizmoStyle style = Resolve(
                moveSize,
                rotateSize,
                scaleSize,
                thickness,
                hitAreaPixels);
            SerializationHudProfile.ProfileData data = Data;
            data.DecorationGizmoMoveSize = style.MoveSize;
            data.DecorationGizmoRotateSize = style.RotateSize;
            data.DecorationGizmoScaleSize = style.ScaleSize;
            data.DecorationGizmoThickness = style.Thickness;
            data.DecorationGizmoHitAreaPixels = style.HitAreaPixels;
            return SaveProfileBestEffort();
        }

        internal static bool ResetDefaults() =>
            Set(
                DefaultMoveSize,
                DefaultRotateSize,
                DefaultScaleSize,
                DefaultThickness,
                DefaultHitAreaPixels);

        internal static string FormatMultiplier(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        internal static string FormatPixels(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture);

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

        private static float ClampSize(float value, float fallback) =>
            ClampFinite(value, fallback, MinimumSize, MaximumSize);

        private static float ClampThickness(float value) =>
            ClampFinite(value, DefaultThickness, MinimumThickness, MaximumThickness);

        private static float ClampHitAreaPixels(float value) =>
            ClampFinite(
                value,
                DefaultHitAreaPixels,
                MinimumHitAreaPixels,
                MaximumHitAreaPixels);

        private static float ClampFinite(
            float value,
            float fallback,
            float minimum,
            float maximum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                value = fallback;
            return Math.Min(maximum, Math.Max(minimum, value));
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
