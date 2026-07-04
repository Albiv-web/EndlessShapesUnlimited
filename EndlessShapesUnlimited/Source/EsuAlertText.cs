namespace DecoLimitLifter
{
    internal static class EsuAlertText
    {
        // Matches DecorationEditorTheme.Cyan without initializing GUI theme state during startup.
        internal const string HudCyan = "#0DE6FF";

        internal static string HudColorize(string text) =>
            "<color=" + HudCyan + ">" + (text ?? string.Empty) + "</color>";
    }
}
