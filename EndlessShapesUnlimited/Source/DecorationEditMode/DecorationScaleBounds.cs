using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class DecorationScaleBounds
    {
        internal static void AllowExtendedScale(Decoration decoration)
        {
            if (decoration?.Scaling == null)
                return;

            decoration.Scaling.MaxElementValue = float.PositiveInfinity;
            decoration.Scaling.MinElementValue = float.NegativeInfinity;
            decoration.Scaling.MaximumMagnitude = float.PositiveInfinity;
        }
    }
}
