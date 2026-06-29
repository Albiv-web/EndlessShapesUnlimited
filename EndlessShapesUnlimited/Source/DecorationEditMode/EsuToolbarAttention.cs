using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuToolbarAttention
    {
        private const float PulseSeconds = 2.5f;

        internal static float RefreshUntil() =>
            Time.unscaledTime + PulseSeconds;

        internal static bool IsActive(float until) =>
            Time.unscaledTime <= until;

        internal static void DrawLastButtonPulse(bool active)
        {
            if (!active || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            Rect rect = GUILayoutUtility.GetLastRect();
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
            Color previous = GUI.color;
            try
            {
                Color amber = DecorationEditorTheme.WarningColor;
                Color cyan = DecorationEditorTheme.Cyan;
                Color color = Color.Lerp(amber, cyan, wave * 0.35f);
                color.a = 0.38f + wave * 0.42f;
                GUI.color = color;

                DrawBorder(
                    new Rect(
                        rect.x - EsuHudLayout.Scale(2f),
                        rect.y - EsuHudLayout.Scale(2f),
                        rect.width + EsuHudLayout.Scale(4f),
                        rect.height + EsuHudLayout.Scale(4f)),
                    EsuHudLayout.Scale(2f));

                GUI.color = new Color(color.r, color.g, color.b, color.a * 0.35f);
                DrawBorder(
                    new Rect(
                        rect.x - EsuHudLayout.Scale(4f),
                        rect.y - EsuHudLayout.Scale(4f),
                        rect.width + EsuHudLayout.Scale(8f),
                        rect.height + EsuHudLayout.Scale(8f)),
                    EsuHudLayout.Scale(1f));
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static void DrawBorder(Rect rect, float thickness)
        {
            thickness = Mathf.Max(1f, thickness);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        }
    }
}
