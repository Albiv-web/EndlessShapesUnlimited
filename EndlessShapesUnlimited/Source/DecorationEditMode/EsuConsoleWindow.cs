using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal enum EsuConsoleFilter
    {
        All,
        Info,
        Warnings,
        Errors
    }

    internal static class EsuConsoleWindow
    {
        private const float DefaultWidth = 640f;
        private const float DefaultHeight = 390f;
        private const float MinWidth = 500f;
        private const float MinHeight = 220f;
        private const float MaxScreenWidthFactor = 0.88f;
        private const float MaxScreenHeightFactor = 0.78f;
        private static readonly int ForegroundWindowId =
            "EndlessShapesUnlimited.EsuConsoleWindow.Foreground".GetHashCode();

        private static Rect _rect;
        private static Vector2 _scroll;
        private static bool _open;
        private static bool _dragging;
        private static Vector2 _dragOffset;
        private static bool _resizing;
        private static Rect _resizeStart;
        private static Vector2 _resizeMouseStart;
        private static EsuConsoleFilter _filter = EsuConsoleFilter.All;

        internal static bool IsOpen => _open;

        internal static void Toggle()
        {
            if (_open)
                Close();
            else
                _open = true;
        }

        internal static void Open() =>
            _open = true;

        internal static void Close()
        {
            _open = false;
            _dragging = false;
            _resizing = false;
        }

        internal static bool ContainsMouse(Vector2 mouse) =>
            _open && _rect.Contains(mouse);

        internal static void Draw()
        {
            if (!_open)
                return;

            EnsureRect();
            HandleDrag();
            HandleResize();
            DrawPanel(_rect);
            _rect = ClampRect(_rect);
            EsuHudLayout.DrawResizeGrip(_rect, leftEdge: false);
            EsuCursorTooltip.Register(HeaderDragRect(), "Drag to move the ESU console.");
            EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_rect, leftEdge: false), "Drag to resize the ESU console.");
        }

        internal static void DrawForegroundWindow()
        {
            if (!_open)
                return;

            EnsureRect();
            HandleDrag();
            HandleResize();
            int previousDepth = GUI.depth;
            GUI.depth = Math.Min(previousDepth, -10000);
            try
            {
                _rect = GUI.Window(
                    ForegroundWindowId,
                    _rect,
                    DrawForegroundWindowContents,
                    GUIContent.none,
                    GUIStyle.none);
                GUI.BringWindowToFront(ForegroundWindowId);
            }
            finally
            {
                GUI.depth = previousDepth;
            }

            _rect = ClampRect(_rect);
            EsuCursorTooltip.Register(HeaderDragRect(), "Drag to move the ESU console.");
            EsuCursorTooltip.Register(EsuHudLayout.ResizeGripRect(_rect, leftEdge: false), "Drag to resize the ESU console.");
        }

        private static void EnsureRect()
        {
            if (_rect.width >= 1f && _rect.height >= 1f)
            {
                _rect = ClampRect(_rect);
                return;
            }

            float width = EsuHudLayout.Scale(DefaultWidth);
            float height = EsuHudLayout.Scale(DefaultHeight);
            _rect = ClampRect(new Rect(
                Mathf.Max(EsuHudLayout.Scale(8f), Screen.width - width - EsuHudLayout.Scale(28f)),
                EsuHudLayout.Scale(86f),
                width,
                height));
        }

        private static Rect ClampRect(Rect rect)
        {
            return EsuHudLayout.ClampPanel(
                rect,
                EsuHudLayout.Scale(MinWidth),
                EsuHudLayout.Scale(MinHeight),
                Mathf.Max(EsuHudLayout.Scale(MinWidth), Screen.width * MaxScreenWidthFactor),
                Mathf.Max(EsuHudLayout.Scale(MinHeight), Screen.height * MaxScreenHeightFactor),
                EsuHudLayout.Scale(8f),
                EsuHudLayout.Scale(8f));
        }

        private static void HandleResize()
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (_dragging)
                return;

            Rect grip = EsuHudLayout.ResizeGripRect(_rect, leftEdge: false);
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                grip.Contains(current.mousePosition))
            {
                _resizing = true;
                _resizeStart = _rect;
                _resizeMouseStart = current.mousePosition;
                current.Use();
                return;
            }

            if (_resizing && current.type == EventType.MouseDrag)
            {
                Vector2 delta = current.mousePosition - _resizeMouseStart;
                _rect = ClampRect(new Rect(
                    _resizeStart.x,
                    _resizeStart.y,
                    _resizeStart.width + delta.x,
                    _resizeStart.height + delta.y));
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                _resizing = false;
        }

        private static void HandleDrag()
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (_resizing)
                return;

            Rect drag = HeaderDragRect();
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                drag.Contains(current.mousePosition))
            {
                _dragging = true;
                _dragOffset = current.mousePosition - new Vector2(_rect.x, _rect.y);
                current.Use();
                return;
            }

            if (_dragging && current.type == EventType.MouseDrag)
            {
                _rect.x = current.mousePosition.x - _dragOffset.x;
                _rect.y = current.mousePosition.y - _dragOffset.y;
                _rect = ClampRect(_rect);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp)
                _dragging = false;
        }

        private static Rect HeaderDragRect()
        {
            float controlsWidth = EsuHudLayout.Scale(282f);
            return new Rect(
                _rect.x,
                _rect.y,
                Mathf.Max(EsuHudLayout.Scale(120f), _rect.width - controlsWidth),
                EsuHudLayout.Scale(34f));
        }

        private static void DrawForegroundWindowContents(int id)
        {
            Rect localRect = new Rect(0f, 0f, Mathf.Max(1f, _rect.width), Mathf.Max(1f, _rect.height));
            DrawPanel(localRect);
            EsuHudLayout.DrawResizeGrip(localRect, leftEdge: false);
        }

        private static void DrawPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            Rect inner = EsuHudLayout.PanelInnerRect(rect);
            GUILayout.BeginArea(inner);
            DrawHeader(inner.width);
            DecorationEditorTheme.Separator();
            DrawFilterRow();
            DecorationEditorTheme.Separator();
            DrawEntries(inner.width, inner.height - EsuHudLayout.Scale(82f));
            GUILayout.EndArea();
        }

        private static void DrawHeader(float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "ESU Console",
                HeaderTitleStyle(),
                GUILayout.Width(EsuHudLayout.Scale(132f)),
                GUILayout.Height(EsuHudLayout.Scale(26f)));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                EsuRuntimeLog.Count.ToString("N0", CultureInfo.InvariantCulture) + " entries",
                HeaderCountStyle(),
                GUILayout.Width(EsuHudLayout.Scale(78f)),
                GUILayout.Height(EsuHudLayout.Scale(24f)));
            if (GUILayout.Button(
                    new GUIContent("Clear", "Clear the ESU runtime log."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                EsuRuntimeLog.Clear();
                _scroll = Vector2.zero;
            }

            if (GUILayout.Button(
                    new GUIContent("Copy", "Copy visible log entries."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(52f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                GUIUtility.systemCopyBuffer = EsuRuntimeLog.FormatForClipboard(FilteredEntries());
            }

            if (GUILayout.Button(
                    new GUIContent("Close", "Close the ESU console."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                Close();
            }
            GUILayout.EndHorizontal();
        }

        private static GUIStyle HeaderTitleStyle() =>
            new GUIStyle(DecorationEditorTheme.SubHeader)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };

        private static GUIStyle HeaderCountStyle() =>
            new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };

        private static void DrawFilterRow()
        {
            GUILayout.BeginHorizontal();
            FilterButton(EsuConsoleFilter.All, "All");
            FilterButton(EsuConsoleFilter.Info, "Info");
            FilterButton(EsuConsoleFilter.Warnings, "Warnings");
            FilterButton(EsuConsoleFilter.Errors, "Errors");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void FilterButton(EsuConsoleFilter filter, string label)
        {
            if (GUILayout.Button(
                    new GUIContent(label, "Show " + label.ToLowerInvariant() + " log entries."),
                    DecorationEditorTheme.ToolButton(_filter == filter),
                    GUILayout.Width(EsuHudLayout.Scale(filter == EsuConsoleFilter.All ? 48f : 78f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                _filter = filter;
                _scroll = Vector2.zero;
            }
        }

        private static IReadOnlyList<EsuRuntimeLogEntry> FilteredEntries()
        {
            switch (_filter)
            {
                case EsuConsoleFilter.Info:
                    return EsuRuntimeLog.Filtered(EsuRuntimeLogSeverity.Info);
                case EsuConsoleFilter.Warnings:
                    return EsuRuntimeLog.Filtered(EsuRuntimeLogSeverity.Warning);
                case EsuConsoleFilter.Errors:
                    return EsuRuntimeLog.Filtered(EsuRuntimeLogSeverity.Error);
                default:
                    return EsuRuntimeLog.Filtered(null);
            }
        }

        private static void DrawEntries(float width, float height)
        {
            IReadOnlyList<EsuRuntimeLogEntry> entries = FilteredEntries();
            GUIStyle header = EntryHeaderStyle();
            GUIStyle body = EntryBodyStyle();
            float viewportWidth = Mathf.Max(1f, width - EsuHudLayout.Scale(2f));
            float contentWidth = Mathf.Max(1f, viewportWidth - EsuHudLayout.Scale(22f));
            float y = 0f;
            float gap = EsuHudLayout.Scale(7f);
            float rowPadding = EsuHudLayout.Scale(7f);
            var rowHeights = new float[entries.Count];
            for (int index = 0; index < entries.Count; index++)
            {
                EsuRuntimeLogEntry entry = entries[index];
                float messageHeight = body.CalcHeight(
                    new GUIContent(EntryBodyText(entry)),
                    contentWidth - rowPadding * 2f);
                rowHeights[index] = EsuHudLayout.Scale(22f) + messageHeight + rowPadding * 2f;
                y += rowHeights[index] + gap;
            }

            Rect viewport = GUILayoutUtility.GetRect(
                viewportWidth,
                Mathf.Max(EsuHudLayout.Scale(80f), height),
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            Rect content = new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, y));
            _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, content.height > viewport.height);
            float rowY = 0f;
            for (int index = 0; index < entries.Count; index++)
            {
                EsuRuntimeLogEntry entry = entries[index];
                float rowHeight = rowHeights[index];
                Rect row = new Rect(0f, rowY, content.width, rowHeight);
                DrawEntryRow(row, entry, header, body, rowPadding);
                rowY += rowHeight + gap;
            }

            if (entries.Count == 0)
            {
                GUI.Label(
                    new Rect(0f, 0f, content.width, EsuHudLayout.Scale(26f)),
                    "No log entries for this filter.",
                    DecorationEditorTheme.BodyWrap);
            }
            GUI.EndScrollView();
        }

        private static void DrawEntryRow(
            Rect row,
            EsuRuntimeLogEntry entry,
            GUIStyle header,
            GUIStyle body,
            float padding)
        {
            Color previous = GUI.color;
            Color accent = SeverityColor(entry.Severity);
            GUI.color = new Color(0f, 0.08f, 0.1f, 0.76f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f);
            GUI.DrawTexture(new Rect(row.x, row.y, EsuHudLayout.Scale(3f), row.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Rect headerRect = new Rect(
                row.x + padding,
                row.y + EsuHudLayout.Scale(4f),
                row.width - padding * 2f,
                EsuHudLayout.Scale(20f));
            Rect bodyRect = new Rect(
                row.x + padding,
                headerRect.yMax + EsuHudLayout.Scale(2f),
                row.width - padding * 2f,
                row.height - EsuHudLayout.Scale(24f) - padding);
            GUI.Label(headerRect, EntryHeaderText(entry), header);
            GUI.Label(bodyRect, EntryBodyText(entry), body);
            GUI.color = previous;
        }

        private static string EntryHeaderText(EsuRuntimeLogEntry entry) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss}  {1}  {2}",
                entry.Timestamp,
                entry.Severity,
                entry.Source);

        private static string EntryBodyText(EsuRuntimeLogEntry entry) =>
            string.IsNullOrWhiteSpace(entry.Detail)
                ? entry.Message
                : entry.Message + Environment.NewLine + entry.Detail;

        private static GUIStyle EntryHeaderStyle() =>
            new GUIStyle(DecorationEditorTheme.Mini)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip
            };

        private static GUIStyle EntryBodyStyle() =>
            new GUIStyle(DecorationEditorTheme.BodyWrap)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip
            };

        private static Color SeverityColor(EsuRuntimeLogSeverity severity)
        {
            switch (severity)
            {
                case EsuRuntimeLogSeverity.Error:
                    return DecorationEditorTheme.ErrorColor;
                case EsuRuntimeLogSeverity.Warning:
                    return DecorationEditorTheme.WarningColor;
                default:
                    return DecorationEditorTheme.Cyan;
            }
        }
    }
}
