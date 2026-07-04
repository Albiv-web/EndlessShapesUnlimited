using System;
using System.Globalization;
using DecoLimitLifter.SerializationHud;
using EndlessShapes2;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuTransformSnapSettings
    {
        internal const float DefaultDecorationMoveSnap = 0.05f;
        internal const float DefaultDecorationRotateSnapDegrees = 5f;
        internal const float DefaultDecorationScaleSnap = 0.05f;
        internal const int DefaultSmartMoveStepCells = 1;
        internal const float DefaultSmartRotateSnapDegrees = 90f;
        internal const int DefaultSmartScaleStepCells = 1;

        private const float DecorationMoveMinimum = 0.001f;
        private const float DecorationMoveMaximum = 10f;
        private const float DecorationRotateMinimum = 0.1f;
        private const float DecorationRotateMaximum = 180f;
        private const float DecorationScaleMinimum = 0.00001f;
        private static readonly float DecorationScaleMaximum = float.PositiveInfinity;
        private const int SmartStepMinimum = 1;
        private const int SmartStepMaximum = 20;
        private const float SmartRotateMinimum = 90f;
        private const float SmartRotateMaximum = 270f;

        private static readonly SerializationHudProfile.ProfileData Fallback =
            new SerializationHudProfile.ProfileData();

        internal static float DecorationMoveSnap =>
            ClampDecorationMove(Data.DecorationMoveSnap);

        internal static float DecorationRotateSnapDegrees =>
            ClampDecorationRotate(Data.DecorationRotateSnapDegrees);

        internal static float DecorationScaleSnap =>
            ClampDecorationScale(Data.DecorationScaleSnap);

        internal static int SmartMoveStepCells =>
            ClampSmartStep(Data.SmartBuildMoveStepCells);

        internal static float SmartRotateSnapDegrees =>
            ClampSmartRotate(Data.SmartBuildRotateSnapDegrees);

        internal static int SmartScaleStepCells =>
            ClampSmartStep(Data.SmartBuildScaleStepCells);

        internal static int SmartRotateQuarterTurns =>
            Mathf.Clamp(Mathf.RoundToInt(SmartRotateSnapDegrees / 90f), 1, 3);

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

        internal static void SetDecoration(float moveSnap, float rotateSnapDegrees, float scaleSnap)
        {
            SerializationHudProfile.ProfileData data = Data;
            data.DecorationMoveSnap = ClampDecorationMove(moveSnap);
            data.DecorationRotateSnapDegrees = ClampDecorationRotate(rotateSnapDegrees);
            data.DecorationScaleSnap = ClampDecorationScale(scaleSnap);
        }

        internal static void SetSmart(float moveStepCells, float rotateSnapDegrees, float scaleStepCells)
        {
            SerializationHudProfile.ProfileData data = Data;
            data.SmartBuildMoveStepCells = ClampSmartStep(Mathf.RoundToInt(moveStepCells));
            data.SmartBuildRotateSnapDegrees = ClampSmartRotate(rotateSnapDegrees);
            data.SmartBuildScaleStepCells = ClampSmartStep(Mathf.RoundToInt(scaleStepCells));
        }

        internal static bool TryParseSnapText(string text, out float value)
        {
            if (FlexibleFloatParser.TryParse(text, out value))
                return true;

            value = 0f;
            return false;
        }

        internal static string Format(float value) =>
            value.ToString("0.#####", CultureInfo.InvariantCulture);

        internal static string Format(int value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static float ClampDecorationMove(float value) =>
            ClampFinite(value, DefaultDecorationMoveSnap, DecorationMoveMinimum, DecorationMoveMaximum);

        private static float ClampDecorationRotate(float value) =>
            ClampFinite(value, DefaultDecorationRotateSnapDegrees, DecorationRotateMinimum, DecorationRotateMaximum);

        private static float ClampDecorationScale(float value) =>
            ClampFinite(value, DefaultDecorationScaleSnap, DecorationScaleMinimum, DecorationScaleMaximum);

        private static int ClampSmartStep(int value) =>
            Mathf.Clamp(value, SmartStepMinimum, SmartStepMaximum);

        private static float ClampSmartRotate(float value)
        {
            float finite = ClampFinite(value, DefaultSmartRotateSnapDegrees, SmartRotateMinimum, SmartRotateMaximum);
            int quarterTurns = Mathf.Clamp(Mathf.RoundToInt(finite / 90f), 1, 3);
            return quarterTurns * 90f;
        }

        private static float ClampFinite(float value, float fallback, float minimum, float maximum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                value = fallback;
            return Mathf.Clamp(value, minimum, maximum);
        }
    }

    internal static class EsuTransformSnapHud
    {
        internal static bool DrawSnapControls(
            Rect rect,
            string title,
            ref string moveText,
            ref string rotateText,
            ref string scaleText,
            string moveTip,
            string rotateTip,
            string scaleTip,
            out bool commitRequested)
        {
            string previousMove = moveText;
            string previousRotate = rotateText;
            string previousScale = scaleText;
            float buttonHeight = Mathf.Min(rect.height, EsuHudLayout.Scale(24f));
            float y = rect.y + Mathf.Max(0f, (rect.height - buttonHeight) * 0.5f);
            float gap = EsuHudLayout.Scale(4f);
            float titleWidth = Mathf.Min(EsuHudLayout.Scale(74f), rect.width * 0.18f);
            float labelWidth = EsuHudLayout.Scale(30f);
            float setWidth = EsuHudLayout.Scale(42f);
            float minimumFieldWidth = EsuHudLayout.Scale(34f);
            float available = Mathf.Max(
                minimumFieldWidth * 3f,
                rect.width - titleWidth - setWidth - gap * 9f - labelWidth * 3f);
            float fieldWidth = Mathf.Max(minimumFieldWidth, available / 3f);
            float x = rect.x;

            GUI.Label(new Rect(x, y, titleWidth, buttonHeight), title, DecorationEditorTheme.SubHeader);
            x += titleWidth + gap;
            DrawField("Move", ref moveText, moveTip, ref x, y, labelWidth, fieldWidth, buttonHeight, gap);
            DrawField("Rot", ref rotateText, rotateTip, ref x, y, labelWidth, fieldWidth, buttonHeight, gap);
            DrawField("Scale", ref scaleText, scaleTip, ref x, y, labelWidth, fieldWidth, buttonHeight, gap);

            Rect set = new Rect(Mathf.Min(x, rect.xMax - setWidth), y, setWidth, buttonHeight);
            commitRequested = GUI.Button(set, new GUIContent("Set", "Apply transform snap settings."), DecorationEditorTheme.Button);
            EsuCursorTooltip.Register(set, "Apply transform snap settings.");
            return commitRequested ||
                   !string.Equals(previousMove, moveText, StringComparison.Ordinal) ||
                   !string.Equals(previousRotate, rotateText, StringComparison.Ordinal) ||
                   !string.Equals(previousScale, scaleText, StringComparison.Ordinal);
        }

        private static void DrawField(
            string label,
            ref string text,
            string tooltip,
            ref float x,
            float y,
            float labelWidth,
            float fieldWidth,
            float height,
            float gap)
        {
            GUI.Label(new Rect(x, y, labelWidth, height), label, DecorationEditorTheme.Mini);
            x += labelWidth + gap;
            Rect field = new Rect(x, y, fieldWidth, height);
            text = GUI.TextField(field, text ?? string.Empty, DecorationEditorTheme.TextField);
            EsuCursorTooltip.Register(field, tooltip);
            x += fieldWidth + gap;
        }
    }
}
