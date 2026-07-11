using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuHudChrome
    {
        internal static void DrawPanel(Rect rect, bool accent = false)
        {
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);
            if (!accent || rect.width <= 1f || rect.height <= 1f)
                return;

            Color previous = GUI.color;
            Color cyan = DecorationEditorTheme.Cyan;
            float topHeight = Mathf.Max(1f, EsuHudLayout.Scale(1.5f));
            float railWidth = Mathf.Max(1f, EsuHudLayout.Scale(1f));
            GUI.color = new Color(cyan.r, cyan.g, cyan.b, 0.72f);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width, topHeight),
                Texture2D.whiteTexture);
            GUI.color = new Color(cyan.r, cyan.g, cyan.b, 0.38f);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y + topHeight, railWidth, Mathf.Max(0f, rect.height - topHeight)),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        internal static void DrawCompactIconHeader(string text, string iconKey)
        {
            DrawCompactIconHeader(text, iconKey, GUILayout.ExpandWidth(true));
        }

        internal static void DrawCompactIconHeader(
            string text,
            string iconKey,
            params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                EsuHudLayout.Scale(EsuHudLayout.CompactHeaderHeightBase),
                options);
            DrawCompactIconHeader(rect, text, iconKey);
        }

        internal static void DrawCompactIconHeader(Rect rect, string text, string iconKey)
        {
            GUI.Label(rect, GUIContent.none, DecorationEditorTheme.SubHeader);

            float inset = EsuHudLayout.Scale(5f);
            float iconSize = Mathf.Min(rect.height - EsuHudLayout.Scale(6f), EsuHudLayout.Scale(16f));
            Rect iconRect = new Rect(
                rect.x + inset,
                rect.y + (rect.height - iconSize) * 0.5f,
                Mathf.Max(0f, iconSize),
                Mathf.Max(0f, iconSize));
            Texture icon = DecorationEditorIconCatalog.Get(iconKey);
            if (icon != null && iconRect.width > 0f)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

            float textX = icon == null
                ? rect.x + inset
                : iconRect.xMax + EsuHudLayout.Scale(6f);
            Rect textRect = new Rect(
                textX,
                rect.y,
                Mathf.Max(0f, rect.xMax - textX - EsuHudLayout.Scale(7f)),
                rect.height);
            if (textRect.width > 1f)
                GUI.Label(textRect, text ?? string.Empty, DecorationEditorTheme.CompactHeaderText);
        }

        internal static void DrawPanelHeader(
            string text,
            string iconKey,
            ref bool panelVisible,
            string tooltip)
        {
            GUILayout.BeginHorizontal();
            DrawCompactIconHeader(text, iconKey);
            if (GUILayout.Button(
                    new GUIContent("Hide", tooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(EsuHudLayout.CompactHeaderHeightBase))))
            {
                panelVisible = false;
            }

            GUILayout.EndHorizontal();
        }

        internal static bool DrawSectionHeader(
            string text,
            ref bool sectionVisible,
            string tooltip)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                text ?? string.Empty,
                DecorationEditorTheme.SectionHeader,
                GUILayout.Height(EsuHudLayout.Scale(EsuHudLayout.SectionHeaderHeightBase)),
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    new GUIContent(sectionVisible ? "Hide" : "Show", tooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(EsuHudLayout.SectionHeaderHeightBase))))
            {
                sectionVisible = !sectionVisible;
            }

            GUILayout.EndHorizontal();
            return sectionVisible;
        }
    }
}
