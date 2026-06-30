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
                Color color = DecorationEditorTheme.ErrorColor;
                color.a = 0.32f + wave * 0.48f;
                GUI.color = color;

                Rect inner = Inset(rect, EsuHudLayout.Scale(2f));
                GUI.DrawTexture(inner, Texture2D.whiteTexture);

                GUI.color = new Color(color.r, color.g, color.b, color.a * 0.35f);
                DrawBorderInside(inner, EsuHudLayout.Scale(2f));

                GUI.color = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a + 0.2f));
                DrawBorderInside(Inset(inner, EsuHudLayout.Scale(2f)), EsuHudLayout.Scale(1f));
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static Rect Inset(Rect rect, float amount)
        {
            amount = Mathf.Max(0f, amount);
            return new Rect(
                rect.x + amount,
                rect.y + amount,
                Mathf.Max(1f, rect.width - amount * 2f),
                Mathf.Max(1f, rect.height - amount * 2f));
        }

        private static void DrawBorderInside(Rect rect, float thickness)
        {
            thickness = Mathf.Max(1f, thickness);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        }
    }
}
