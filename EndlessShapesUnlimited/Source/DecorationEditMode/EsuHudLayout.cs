using System;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuHudLayout
    {
        internal const float ReferenceWidth = 1920f;
        internal const float ReferenceHeight = 1080f;
        internal const float MinAutoScale = 0.72f;
        internal const float MinManualScale = 0.10f;
        internal const float MaxManualScale = 2f;
        internal const float MinEffectiveScale = 0.10f;
        internal const float MaxEffectiveScale = 2f;
        internal const float PanelInsetBase = 6f;
        internal const float ToolbarGapBase = 6f;
        internal const float ToolbarLeftRailBaseWidth = 650f;
        internal const float ToolbarNotificationBaseWidth = 250f;
        internal const float ToolbarRightControlsBaseWidth = 504f;
        internal const float EditorSideMarginBase = 18f;
        internal const float EditorBottomMarginBase = 10f;
        internal const float EditorPanelTopGapBase = 8f;
        internal const float EditorBottomPanelGapBase = 12f;
        internal const float BottomStripScreenRatio = 0.13f;
        internal const float BottomStripMinHeightBase = 118f;
        internal const float BottomStripMaxHeightBase = 146f;
        internal const float CompactHeaderHeightBase = 22f;
        internal const float SectionHeaderHeightBase = 24f;

        private static int _resetGeneration;

        internal struct ToolbarBudget
        {
            internal ToolbarBudget(
                float leftRailWidth,
                float notificationWidth,
                float rightControlsWidth,
                float gap)
            {
                LeftRailWidth = leftRailWidth;
                NotificationWidth = notificationWidth;
                RightControlsWidth = rightControlsWidth;
                Gap = gap;
            }

            internal float LeftRailWidth { get; }

            internal float NotificationWidth { get; }

            internal float RightControlsWidth { get; }

            internal float Gap { get; }

            internal float TotalWidth =>
                LeftRailWidth + NotificationWidth + RightControlsWidth + Gap * 2f;
        }

        internal struct CenteredToolbarFrame
        {
            internal CenteredToolbarFrame(Rect rect, ToolbarBudget budget)
            {
                Rect = rect;
                Budget = budget;
            }

            internal Rect Rect { get; }

            internal ToolbarBudget Budget { get; }
        }

        internal static int ResetGeneration => _resetGeneration;

        internal static float CurrentScale => ScaleForScreen(Screen.width, Screen.height);

        internal static void RequestReset()
        {
            _resetGeneration++;
        }

        internal static float ScaleForScreen(int width, int height)
        {
            bool autoScale = true;
            float manualScale = 1f;
            try
            {
                SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
                autoScale = data.EsuEditorAutoScale;
                manualScale = data.EsuEditorScale;
            }
            catch
            {
                // Defaults keep the in-game editor usable before the profile module is available.
            }

            return ScaleForScreen(width, height, autoScale, manualScale);
        }

        internal static float ScaleForScreen(
            int width,
            int height,
            bool autoScale,
            float manualScale)
        {
            float manual = ClampManualScale(manualScale);
            float automatic = 1f;
            if (autoScale && width > 0 && height > 0)
            {
                automatic = Mathf.Min(width / ReferenceWidth, height / ReferenceHeight);
                automatic = Mathf.Clamp(automatic, MinAutoScale, 1f);
            }

            return Mathf.Clamp(automatic * manual, MinEffectiveScale, MaxEffectiveScale);
        }

        internal static float ClampManualScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                return 1f;
            return Mathf.Clamp(scale, MinManualScale, MaxManualScale);
        }

        internal static float Scale(float value) => value * CurrentScale;

        internal static int Pixel(float value) =>
            Mathf.Max(1, Mathf.RoundToInt(Scale(value)));

        internal static int FontSize(int baseSize)
        {
            float fontScale = Mathf.Clamp(CurrentScale, MinEffectiveScale, MaxEffectiveScale);
            return Mathf.Max(1, Mathf.RoundToInt(baseSize * fontScale));
        }

        internal static RectOffset Offset(int left, int right, int top, int bottom) =>
            new RectOffset(Pixel(left), Pixel(right), Pixel(top), Pixel(bottom));

        internal static Rect PanelInnerRect(Rect rect)
        {
            return PanelInnerRect(rect, PanelInsetBase);
        }

        internal static Rect PanelInnerRect(Rect rect, float insetBase)
        {
            float inset = Scale(insetBase);
            return new Rect(
                rect.x + inset,
                rect.y + inset,
                Mathf.Max(1f, rect.width - inset * 2f),
                Mathf.Max(1f, rect.height - inset * 2f));
        }

        internal static Rect LocalPanelInnerRect(float width, float height) =>
            PanelInnerRect(new Rect(0f, 0f, width, height));

        internal static Rect LocalPanelInnerRect(float width, float height, float insetBase) =>
            PanelInnerRect(new Rect(0f, 0f, width, height), insetBase);

        internal static Rect ToolbarRect(float baseHeight)
        {
            float margin = Scale(8f);
            float width = Mathf.Max(1f, Screen.width - margin * 2f);
            return new Rect(margin, margin, width, Scale(baseHeight));
        }

        internal static float ToolbarGap => Scale(ToolbarGapBase);

        internal static float EditorSideMargin => Scale(EditorSideMarginBase);

        internal static float EditorBottomMargin => Scale(EditorBottomMarginBase);

        internal static float EditorPanelTopLimit(float toolbarBaseHeight) =>
            ToolbarRect(toolbarBaseHeight).yMax + Scale(EditorPanelTopGapBase);

        internal static float BottomStripHeight() =>
            Mathf.Clamp(
                Screen.height * BottomStripScreenRatio,
                Scale(BottomStripMinHeightBase),
                Scale(BottomStripMaxHeightBase));

        internal static Rect BottomStripRect(float height)
        {
            float margin = EditorBottomMargin;
            return new Rect(
                margin,
                Screen.height - height - margin,
                Screen.width - margin * 2f,
                height);
        }

        internal static float BottomPanelLimit(float bottomPanelHeight) =>
            bottomPanelHeight + Scale(EditorBottomPanelGapBase);

        internal static ToolbarBudget CalculateToolbarBudget(float toolbarWidth) =>
            CalculateToolbarBudget(toolbarWidth, CurrentScale);

        internal static CenteredToolbarFrame CalculateCenteredToolbarFrame(Rect innerRect) =>
            CalculateCenteredToolbarFrame(innerRect, CurrentScale);

        internal static CenteredToolbarFrame CalculateCenteredToolbarFrame(Rect innerRect, float scale)
        {
            float width = Mathf.Max(1f, innerRect.width);
            ToolbarBudget budget = CalculateToolbarBudget(width, scale);
            float contentWidth = Mathf.Min(width, Mathf.Max(1f, budget.TotalWidth));
            float x = innerRect.x + Mathf.Max(0f, (width - contentWidth) * 0.5f);
            return new CenteredToolbarFrame(
                new Rect(x, innerRect.y, contentWidth, innerRect.height),
                budget);
        }

        internal static ToolbarBudget CalculateToolbarBudget(float toolbarWidth, float scale)
        {
            float width = Mathf.Max(1f, toolbarWidth);
            float resolvedScale = ResolveLayoutScale(scale);
            float gap = ToolbarGapBase * resolvedScale;
            float left = Mathf.Min(ToolbarLeftRailBaseWidth * resolvedScale, width * 0.48f);
            float notification = ToolbarNotificationBaseWidth * resolvedScale;
            float right = Mathf.Min(ToolbarRightControlsBaseWidth * resolvedScale, width * 0.38f);
            float leftFloor = Mathf.Min(54f * resolvedScale, left);
            float rightFloor = Mathf.Min(168f * resolvedScale, right);

            float overflow = left + notification + right + gap * 2f - width;
            if (overflow > 0f)
                Reduce(ref notification, 0f, ref overflow);
            if (overflow > 0f)
                Reduce(ref left, leftFloor, ref overflow);
            if (overflow > 0f)
                Reduce(ref right, rightFloor, ref overflow);
            if (overflow > 0f)
            {
                float gapFloor = 0f;
                float pairedGap = gap * 2f;
                float reduction = Mathf.Min(pairedGap, overflow);
                gap = Mathf.Max(gapFloor, gap - reduction * 0.5f);
                overflow -= reduction;
            }

            float total = left + notification + right + gap * 2f;
            if (total > width)
            {
                float ratio = width / total;
                left *= ratio;
                notification *= ratio;
                right *= ratio;
                gap *= ratio;
            }

            return new ToolbarBudget(
                Mathf.Max(0f, left),
                Mathf.Max(0f, notification),
                Mathf.Max(0f, right),
                Mathf.Max(0f, gap));
        }

        internal static float ToolbarLeftRailWidth(float toolbarWidth)
        {
            return CalculateToolbarBudget(toolbarWidth).LeftRailWidth;
        }

        internal static float ToolbarNotificationWidth(float toolbarWidth)
        {
            return CalculateToolbarBudget(toolbarWidth).NotificationWidth;
        }

        internal static float ToolbarRightControlsWidth(float toolbarWidth)
        {
            return CalculateToolbarBudget(toolbarWidth).RightControlsWidth;
        }

        internal static float ToolbarRightControlsWidth() =>
            ToolbarRightControlsWidth(Screen.width - Scale(16f));

        internal static Rect ClampPanel(
            Rect rect,
            float minWidth,
            float minHeight,
            float maxWidth,
            float maxHeight,
            float topLimit,
            float bottomLimit)
        {
            float margin = Scale(8f);
            float widthLimit = Mathf.Max(minWidth, Mathf.Min(maxWidth, Screen.width - margin * 2f));
            float heightLimit = Mathf.Max(minHeight, Mathf.Min(maxHeight, Screen.height - topLimit - bottomLimit));
            rect.width = Mathf.Clamp(rect.width, minWidth, widthLimit);
            rect.height = Mathf.Clamp(rect.height, minHeight, heightLimit);

            float minX = margin;
            float maxX = Mathf.Max(minX, Screen.width - rect.width - margin);
            rect.x = Mathf.Clamp(rect.x, minX, maxX);
            float minY = topLimit;
            float maxY = Mathf.Max(minY, Screen.height - bottomLimit - rect.height);
            rect.y = Mathf.Clamp(rect.y, minY, maxY);
            return rect;
        }

        internal static Rect ResizeGripRect(Rect rect, bool leftEdge)
        {
            float size = Scale(16f);
            return new Rect(
                leftEdge ? rect.x : rect.xMax - size,
                rect.yMax - size,
                size,
                size);
        }

        internal static void DrawResizeGrip(Rect rect, bool leftEdge)
        {
            Rect grip = ResizeGripRect(rect, leftEdge);
            Color previous = GUI.color;
            GUI.color = new Color(DecorationEditorTheme.Cyan.r, DecorationEditorTheme.Cyan.g, DecorationEditorTheme.Cyan.b, 0.9f);
            float line = Mathf.Max(1f, Scale(1.5f));
            float step = Mathf.Max(4f, Scale(4f));
            float inset = Mathf.Max(3f, Scale(3f));
            for (int index = 0; index < 3; index++)
            {
                float offset = inset + index * step;
                Rect lineRect = leftEdge
                    ? new Rect(grip.x + inset, grip.yMax - offset, grip.width - offset, line)
                    : new Rect(grip.x + offset, grip.yMax - offset, grip.width - offset, line);
                GUI.DrawTexture(lineRect, Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        internal static void DrawStackDividerGrip(Rect rect, bool active)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            Color previous = GUI.color;
            float alpha = active ? 0.85f : 0.42f;
            GUI.color = new Color(
                DecorationEditorTheme.Cyan.r,
                DecorationEditorTheme.Cyan.g,
                DecorationEditorTheme.Cyan.b,
                alpha);
            float line = Mathf.Max(1f, Scale(1.25f));
            float centerY = rect.y + rect.height * 0.5f - line * 0.5f;
            float inset = Mathf.Max(18f, Scale(18f));
            float width = Mathf.Max(12f, rect.width - inset * 2f);
            GUI.DrawTexture(
                new Rect(rect.x + (rect.width - width) * 0.5f, centerY, width, line),
                Texture2D.whiteTexture);

            if (active)
            {
                float handleWidth = Mathf.Min(width, Scale(56f));
                GUI.DrawTexture(
                    new Rect(rect.x + (rect.width - handleWidth) * 0.5f, centerY - line * 2f, handleWidth, line),
                    Texture2D.whiteTexture);
                GUI.DrawTexture(
                    new Rect(rect.x + (rect.width - handleWidth) * 0.5f, centerY + line * 2f, handleWidth, line),
                    Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        internal static string ScaleSummary()
        {
            try
            {
                SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0} x{1:0.00}",
                    data.EsuEditorAutoScale ? "auto" : "manual",
                    CurrentScale);
            }
            catch (Exception)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "auto x{0:0.00}",
                    CurrentScale);
            }
        }

        private static float ResolveLayoutScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                return 1f;
            return Mathf.Clamp(scale, MinEffectiveScale, MaxEffectiveScale);
        }

        private static void Reduce(ref float value, float floor, ref float overflow)
        {
            float reduction = Mathf.Min(Mathf.Max(0f, value - floor), overflow);
            value -= reduction;
            overflow -= reduction;
        }
    }
}
