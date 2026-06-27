using System.Globalization;

namespace EndlessShapes2
{
    internal static class FlexibleFloatParser
    {
        internal static bool TryParse(string text, out float value)
        {
            string candidate = text?.Trim();
            if (string.IsNullOrEmpty(candidate) || HasIncompleteSuffix(candidate))
            {
                value = 0f;
                return false;
            }

            const NumberStyles styles = NumberStyles.Float;
            if (float.TryParse(candidate, styles, CultureInfo.CurrentCulture, out value) && IsFinite(value))
                return true;
            if (float.TryParse(candidate, styles, CultureInfo.InvariantCulture, out value) && IsFinite(value))
                return true;
            value = 0f;
            return false;
        }

        private static bool HasIncompleteSuffix(string value)
        {
            if (value == "+" || value == "-")
                return true;
            char last = value[value.Length - 1];
            return last == '.' || last == ',' || last == 'e' || last == 'E' ||
                   last == '+' || last == '-';
        }

        internal static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
