using System;
using System.Reflection;
using BrilliantSkies.Ftd.Constructs.UI;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons;
using BrilliantSkies.Ui.Consoles.Segments;
using BrilliantSkies.Ui.Consoles.Styles;
using HarmonyLib;

namespace EndlessShapes2
{
    [HarmonyPatch]
    internal static class EndlessShapes2Patch
    {
        internal static MethodBase ResolveTarget()
        {
            return AccessTools.Method(typeof(GeneralTab), "Mesh") ??
                   throw new MissingMethodException(typeof(GeneralTab).FullName, "Mesh");
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => ResolveTarget();

        [HarmonyPostfix]
        private static void Postfix(GeneralTab __instance)
        {
            ScreenSegmentStandard segment =
                __instance.CreateStandardSegment(InsertPosition.OnCursor);
            segment.SpaceAbove = 30f;
            segment.BackgroundStyleWhereApplicable = ConsoleStyles.Instance
                .Styles.Segments.OptionalSegmentDarkBackgroundWithHeader.Style;
            segment.NameWhereApplicable = "OBJ file creator";
            segment.AddInterpretter(SubjectiveButton<ConstructInfo>.Quick(
                __instance._focus,
                "Create an OBJ file of this vehicle",
                null,
                info => OBJ_FileCreation.Start(info.Construct)));
        }
    }
}
