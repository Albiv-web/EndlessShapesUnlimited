namespace DecoLimitLifter.SerializationHud
{
    internal static class EsuHudPreferences
    {
        internal static bool FadeHudBehindModalPopups
        {
            get
            {
                try { return SerializationHudProfile.Data?.FadeHudBehindModalPopups == true; }
                catch { return false; }
            }
        }

        internal static bool ResponsivePaintPalettes
        {
            get
            {
                try { return SerializationHudProfile.Data?.ResponsivePaintPalettes != false; }
                catch { return true; }
            }
        }
    }
}
