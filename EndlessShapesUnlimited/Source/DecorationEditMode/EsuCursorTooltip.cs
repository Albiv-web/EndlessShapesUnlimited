using System;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuCursorTooltip
    {
        internal const float HoverDelaySeconds = 1f;

        private const float MaxWidthBase = 320f;
        private const float MinWidthBase = 120f;
        private const float CursorOffsetXBase = 18f;
        private const float CursorOffsetYBase = 20f;
        private const float MouseMoveResetPixels = 4f;

        private static string _manualTooltip;
        private static string _hoverTooltip;
        private static float _hoverStartedAt = -1f;
        private static Vector2 _hoverMouse;
        private static Vector2 _screenMouse;
        private static bool _suppress;

        internal static void BeginFrame(Vector2 screenMouse, bool suppress = false)
        {
            _screenMouse = screenMouse;
            _manualTooltip = null;
            _suppress = suppress;
            if (Event.current != null &&
                Event.current.type == EventType.Repaint)
            {
                GUI.tooltip = string.Empty;
            }
        }

        internal static void RegisterLast(string tooltip) =>
            Register(GUILayoutUtility.GetLastRect(), tooltip);

        internal static void Register(Rect rect, string tooltip)
        {
            Event current = Event.current;
            if (current == null ||
                string.IsNullOrWhiteSpace(tooltip))
            {
                return;
            }

            if (rect.Contains(current.mousePosition))
                _manualTooltip = tooltip.Trim();
        }

        internal static void Draw()
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (ShouldResetImmediately(current))
            {
                ClearHover();
                return;
            }

            if (current.type != EventType.Repaint)
                return;

            string tooltip = !string.IsNullOrWhiteSpace(_manualTooltip)
                ? _manualTooltip
                : (GUI.tooltip ?? string.Empty).Trim();

            if (_suppress ||
                string.IsNullOrWhiteSpace(tooltip))
            {
                ClearHover();
                return;
            }

            if (!string.Equals(tooltip, _hoverTooltip, StringComparison.Ordinal) ||
                (_screenMouse - _hoverMouse).sqrMagnitude > MouseMoveResetPixels * MouseMoveResetPixels)
            {
                _hoverTooltip = tooltip;
                _hoverStartedAt = Time.unscaledTime;
                _hoverMouse = _screenMouse;
                return;
            }

            if (Time.unscaledTime - _hoverStartedAt < HoverDelaySeconds)
                return;

            DrawTooltip(tooltip);
        }

        private static bool ShouldResetImmediately(Event current)
        {
            if (current.type == EventType.MouseDown ||
                current.type == EventType.MouseDrag ||
                current.type == EventType.ScrollWheel)
            {
                return true;
            }

            return Input.GetMouseButton(0) ||
                   Input.GetMouseButton(1) ||
                   Input.GetMouseButton(2);
        }

        private static void ClearHover()
        {
            _hoverTooltip = null;
            _hoverStartedAt = -1f;
        }

        private static void DrawTooltip(string tooltip)
        {
            GUIStyle textStyle = TooltipTextStyle();
            var content = new GUIContent(tooltip);
            float maxWidth = EsuHudLayout.Scale(MaxWidthBase);
            float minWidth = EsuHudLayout.Scale(MinWidthBase);
            float paddingX = EsuHudLayout.Scale(11f);
            float paddingY = EsuHudLayout.Scale(8f);
            float naturalWidth = textStyle.CalcSize(content).x + paddingX * 2f;
            float width = Mathf.Clamp(naturalWidth, minWidth, maxWidth);
            float textWidth = Mathf.Max(1f, width - paddingX * 2f);
            float height = textStyle.CalcHeight(content, textWidth) + paddingY * 2f;
            float margin = EsuHudLayout.Scale(8f);
            Rect rect = new Rect(
                _screenMouse.x + EsuHudLayout.Scale(CursorOffsetXBase),
                _screenMouse.y + EsuHudLayout.Scale(CursorOffsetYBase),
                width,
                height);
            rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, Screen.width - rect.width - margin));
            rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, Screen.height - rect.height - margin));

            Color previous = GUI.color;
            try
            {
                GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
                GUI.color = DecorationEditorTheme.Cyan;
                GUI.DrawTexture(
                    new Rect(rect.x, rect.y, Mathf.Max(1f, EsuHudLayout.Scale(2f)), rect.height),
                    Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 1f);
                GUI.Label(
                    new Rect(rect.x + paddingX, rect.y + paddingY, textWidth, Mathf.Max(1f, rect.height - paddingY * 2f)),
                    tooltip,
                    textStyle);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static GUIStyle TooltipTextStyle()
        {
            var style = new GUIStyle(DecorationEditorTheme.BodyWrap)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = new Color(0.93f, 0.98f, 1f, 1f);
            return style;
        }
    }
}
