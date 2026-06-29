using System;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum EsuHudNotificationKind
    {
        Info,
        Warning,
        Error
    }

    internal static class EsuHudNotifications
    {
        private const float DisplaySeconds = 6f;
        private const float FadeSeconds = 0.75f;
        private const float SlotHeight = 40f;
        private const float ExpandedMinWidth = 280f;
        private const float ExpandedMaxWidth = 520f;
        private const float ExpandedMaxHeight = 190f;
        private const float SlotMinWidth = 120f;
        private const float AccentWidth = 3f;
        private const float TextPaddingX = 10f;
        private const float TextPaddingY = 6f;
        private const float DetailsButtonWidth = 58f;
        private const float DetailsButtonGap = 6f;

        private static string _message;
        private static float _expiresAt = -1f;
        private static EsuHudNotificationKind _kind;
        private static bool _expanded;
        private static bool _lastMessageOverflow;
        private static Rect _lastSlotScreenRect;
        private static Rect _expandedScreenRect;
        private static Vector2 _expandedScroll;

        internal static bool HasMessage =>
            !string.IsNullOrWhiteSpace(_message) &&
            (_expanded || Time.unscaledTime <= _expiresAt);

        internal static void Show(string message)
        {
            message = (message ?? string.Empty).Trim();
            if (message.Length == 0)
                return;

            _message = message;
            _kind = Classify(message);
            _expiresAt = Time.unscaledTime + DisplaySeconds;
            _expanded = false;
            _expandedScroll = Vector2.zero;
        }

        internal static bool TryCaptureInfoStore(object[] arguments)
        {
            if (!DecorationEditorInputScope.Active &&
                !SmartBuildInputScope.Active)
            {
                return false;
            }

            string message = ExtractMessage(arguments);
            if (string.IsNullOrWhiteSpace(message))
                return false;

            Show(message);
            return true;
        }

        internal static float ToolbarHeightScaled(float baseHeight, float toolbarWidth)
        {
            return EsuHudLayout.Scale(baseHeight);
        }

        internal static void DrawToolbarSlot(Rect toolbarRect)
        {
            DrawToolbarSlot(toolbarRect, EsuHudLayout.ToolbarNotificationWidth(toolbarRect.width));
        }

        internal static void DrawToolbarSlot(Rect toolbarRect, float width)
        {
            DrawToolbarSlot(toolbarRect, width, null);
        }

        internal static void DrawToolbarSlot(Rect toolbarRect, float width, string fallbackMessage)
        {
            float height = EsuHudLayout.Scale(SlotHeight);
            Rect rect = GUILayoutUtility.GetRect(
                Mathf.Max(EsuHudLayout.Scale(SlotMinWidth), width),
                height,
                GUILayout.Width(Mathf.Max(EsuHudLayout.Scale(SlotMinWidth), width)),
                GUILayout.Height(height));
            Vector2 screenPoint = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.y));
            _lastSlotScreenRect = new Rect(
                screenPoint.x,
                screenPoint.y,
                rect.width,
                rect.height);
            bool hasTransientMessage = HasMessage;
            string message = hasTransientMessage
                ? _message
                : (fallbackMessage ?? string.Empty).Trim();
            if (!hasTransientMessage && string.IsNullOrWhiteSpace(message))
            {
                _expanded = false;
                _lastMessageOverflow = false;
                return;
            }

            if (!hasTransientMessage)
            {
                _expanded = false;
                _lastMessageOverflow = false;
            }

            EsuHudNotificationKind kind = hasTransientMessage
                ? _kind
                : EsuHudNotificationKind.Info;
            float remaining = Mathf.Max(0f, _expiresAt - Time.unscaledTime);
            float alpha = hasTransientMessage
                ? (_expanded ? 1f : Mathf.Clamp01(remaining / FadeSeconds))
                : 1f;
            Color accent = AccentColor(kind);
            Color oldColor = GUI.color;
            try
            {
                GUI.color = new Color(0f, 0.08f, 0.1f, 0.78f * alpha);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);

                GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f * alpha);
                GUI.DrawTexture(
                    new Rect(rect.x, rect.y, EsuHudLayout.Scale(3f), rect.height),
                    Texture2D.whiteTexture);

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUIStyle style = MessageStyle(kind);
                Rect textRect = new Rect(
                    rect.x + EsuHudLayout.Scale(AccentWidth + TextPaddingX),
                    rect.y + EsuHudLayout.Scale(TextPaddingY),
                    rect.width - EsuHudLayout.Scale(AccentWidth + TextPaddingX * 2f),
                    rect.height - EsuHudLayout.Scale(TextPaddingY * 2f));
                _lastMessageOverflow = hasTransientMessage && MessageOverflows(message, style, textRect);
                if (_lastMessageOverflow)
                {
                    DrawCollapsedOverflow(rect, textRect, style, kind, alpha);
                }
                else
                {
                    if (hasTransientMessage)
                        _expanded = false;
                    GUI.Label(textRect, message, style);
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        internal static void DrawExpandedPopup()
        {
            if (!HasMessage || !_expanded || !_lastMessageOverflow)
                return;

            GUIStyle messageStyle = MessageStyle(_kind);
            GUIStyle titleStyle = new GUIStyle(messageStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            float margin = EsuHudLayout.Scale(8f);
            float width = Mathf.Clamp(
                _lastSlotScreenRect.width,
                EsuHudLayout.Scale(ExpandedMinWidth),
                EsuHudLayout.Scale(ExpandedMaxWidth));
            float x = Mathf.Clamp(
                _lastSlotScreenRect.x,
                margin,
                Mathf.Max(margin, Screen.width - width - margin));
            float textWidth = Mathf.Max(EsuHudLayout.Scale(80f), width - EsuHudLayout.Scale(26f));
            float textHeight = messageStyle.CalcHeight(new GUIContent(_message), textWidth);
            float headerHeight = EsuHudLayout.Scale(28f);
            float height = Mathf.Min(
                EsuHudLayout.Scale(ExpandedMaxHeight),
                Mathf.Max(
                    EsuHudLayout.Scale(74f),
                    textHeight + headerHeight + EsuHudLayout.Scale(22f)));
            float y = Mathf.Min(
                _lastSlotScreenRect.yMax + EsuHudLayout.Scale(4f),
                Mathf.Max(margin, Screen.height - height - margin));
            _expandedScreenRect = new Rect(x, y, width, height);

            Color oldColor = GUI.color;
            try
            {
                GUI.Box(_expandedScreenRect, GUIContent.none, DecorationEditorTheme.Panel);
                Color accent = AccentColor(_kind);
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f);
                GUI.DrawTexture(
                    new Rect(_expandedScreenRect.x, _expandedScreenRect.y, EsuHudLayout.Scale(3f), _expandedScreenRect.height),
                    Texture2D.whiteTexture);

                GUI.color = Color.white;
                Rect titleRect = new Rect(
                    _expandedScreenRect.x + EsuHudLayout.Scale(12f),
                    _expandedScreenRect.y + EsuHudLayout.Scale(6f),
                    _expandedScreenRect.width - EsuHudLayout.Scale(82f),
                    headerHeight);
                GUI.Label(titleRect, KindLabel(_kind) + " details", titleStyle);

                Rect hideRect = new Rect(
                    _expandedScreenRect.xMax - EsuHudLayout.Scale(64f),
                    _expandedScreenRect.y + EsuHudLayout.Scale(7f),
                    EsuHudLayout.Scale(54f),
                    EsuHudLayout.Scale(24f));
                if (GUI.Button(hideRect, "Hide", DecorationEditorTheme.Button))
                {
                    _expanded = false;
                    return;
                }

                Rect viewport = new Rect(
                    _expandedScreenRect.x + EsuHudLayout.Scale(12f),
                    _expandedScreenRect.y + headerHeight + EsuHudLayout.Scale(8f),
                    _expandedScreenRect.width - EsuHudLayout.Scale(24f),
                    _expandedScreenRect.height - headerHeight - EsuHudLayout.Scale(18f));
                Rect content = new Rect(0f, 0f, viewport.width - EsuHudLayout.Scale(18f), Mathf.Max(viewport.height, textHeight));
                _expandedScroll = GUI.BeginScrollView(viewport, _expandedScroll, content, false, textHeight > viewport.height);
                GUI.Label(new Rect(0f, 0f, content.width, textHeight), _message, messageStyle);
                GUI.EndScrollView();
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        internal static bool ContainsMouse(Vector2 mouse) =>
            _expanded &&
            HasMessage &&
            _expandedScreenRect.Contains(mouse);

        private static void DrawCollapsedOverflow(
            Rect slotRect,
            Rect textRect,
            GUIStyle style,
            EsuHudNotificationKind kind,
            float alpha)
        {
            float buttonWidth = EsuHudLayout.Scale(DetailsButtonWidth);
            float buttonGap = EsuHudLayout.Scale(DetailsButtonGap);
            Rect buttonRect = new Rect(
                slotRect.xMax - buttonWidth - EsuHudLayout.Scale(7f),
                slotRect.y + EsuHudLayout.Scale(7f),
                buttonWidth,
                slotRect.height - EsuHudLayout.Scale(14f));
            Rect labelRect = new Rect(
                textRect.x,
                textRect.y,
                Mathf.Max(EsuHudLayout.Scale(42f), buttonRect.x - textRect.x - buttonGap),
                textRect.height);

            GUI.Label(labelRect, KindLabel(kind), style);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (GUI.Button(buttonRect, _expanded ? "Hide" : "Details", DecorationEditorTheme.Button))
            {
                _expanded = !_expanded;
                if (_expanded)
                    _expiresAt = Time.unscaledTime + DisplaySeconds;
            }
            GUI.color = previous;
        }

        private static bool MessageOverflows(string message, GUIStyle style, Rect textRect)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            float textWidth = Mathf.Max(
                EsuHudLayout.Scale(48f),
                textRect.width);
            float textHeight = style.CalcHeight(new GUIContent(message), textWidth);
            return textHeight > textRect.height + EsuHudLayout.Scale(1f);
        }

        private static GUIStyle MessageStyle(EsuHudNotificationKind kind)
        {
            return new GUIStyle(DecorationEditorTheme.BodyWrap)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontStyle = kind == EsuHudNotificationKind.Info
                    ? FontStyle.Normal
                    : FontStyle.Bold,
                normal =
                {
                    textColor = kind == EsuHudNotificationKind.Error
                        ? DecorationEditorTheme.ErrorColor
                        : kind == EsuHudNotificationKind.Warning
                            ? DecorationEditorTheme.WarningColor
                            : Color.white
                }
            };
        }

        private static string ExtractMessage(object[] arguments)
        {
            if (arguments == null)
                return null;

            for (int index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] is string text &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return null;
        }

        private static EsuHudNotificationKind Classify(string message)
        {
            if (ContainsAny(
                    message,
                    "failed",
                    "failure",
                    "rejected",
                    "invalid",
                    "unavailable",
                    "could not",
                    "cannot",
                    "must",
                    "no valid",
                    "out of range",
                    "exceed",
                    "before switching"))
            {
                return EsuHudNotificationKind.Error;
            }

            if (ContainsAny(
                    message,
                    "cancel",
                    "skipped",
                    "blocked",
                    "warning",
                    "dirty",
                    "restore"))
            {
                return EsuHudNotificationKind.Warning;
            }

            return EsuHudNotificationKind.Info;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int index = 0; index < needles.Length; index++)
            {
                if (text.IndexOf(needles[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static Color AccentColor(EsuHudNotificationKind kind)
        {
            switch (kind)
            {
                case EsuHudNotificationKind.Error:
                    return DecorationEditorTheme.ErrorColor;
                case EsuHudNotificationKind.Warning:
                    return DecorationEditorTheme.WarningColor;
                default:
                    return DecorationEditorTheme.Cyan;
            }
        }

        private static string KindLabel(EsuHudNotificationKind kind)
        {
            switch (kind)
            {
                case EsuHudNotificationKind.Error:
                    return "Error";
                case EsuHudNotificationKind.Warning:
                    return "Warning";
                default:
                    return "Info";
            }
        }
    }
}
