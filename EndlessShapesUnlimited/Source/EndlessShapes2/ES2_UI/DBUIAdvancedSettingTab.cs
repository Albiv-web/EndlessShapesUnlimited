using AdvancedMimicUi;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters;
using BrilliantSkies.Ui.Consoles.Interpretters.Simple;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Choices;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Texts;
using BrilliantSkies.Ui.Consoles.Segments;
using BrilliantSkies.Ui.Tips;
using UnityEngine;

namespace EndlessShapes2.UI
{
    public class DBUIAdvancedSettingTab : SuperScreen<DecorationBuilder>
    {
        public DBUIAdvancedSettingTab(ConsoleWindow window, DecorationBuilder focus) : base(window, focus)
        {
        }

        public override void Build()
        {
            CreateHeader("Offset", null);

            ScreenSegmentTable offsetSegment = CreateTableSegment(4, 5);
            offsetSegment.SpaceAbove = 10f;
            offsetSegment.SpaceBelow = 10f;
            offsetSegment.SqueezeTable = false;

            offsetSegment.AddInterpretter(StringDisplay.Quick("Add position", "Add a value for each parameter")).Justify = TextAnchor.UpperRight;
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Positioning.x), new ToolTip("Add a value to 'Left right positioning'"), (I, i) => I.Data.Positioning.x = i, M.m<DecorationBuilder>(I => "X ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Positioning.y), new ToolTip("Add a value to 'Up down positioning'"), (I, i) => I.Data.Positioning.y = i, M.m<DecorationBuilder>(I => "Y ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Positioning.z), new ToolTip("Add a value to 'Forward backward positioning'"), (I, i) => I.Data.Positioning.z = i, M.m<DecorationBuilder>(I => "Z ")));

            offsetSegment.AddInterpretter(new Blank(6f));
            offsetSegment.AddInterpretter(new Blank(9f));
            offsetSegment.AddInterpretter(new Blank(9f));
            offsetSegment.AddInterpretter(new Blank(9f));

            offsetSegment.AddInterpretter(StringDisplay.Quick("Add scale", "Add a value for each parameter")).Justify = TextAnchor.UpperRight;
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Scaling.x), new ToolTip("Add a value to 'Left right scaling'"), (I, i) => I.Data.Scaling.x = i, M.m<DecorationBuilder>(I => "X ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Scaling.y), new ToolTip("Add a value to 'Up down scaling'"), (I, i) => I.Data.Scaling.y = i, M.m<DecorationBuilder>(I => "Y ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Scaling.z), new ToolTip("Add a value to 'Forward backward scaling'"), (I, i) => I.Data.Scaling.z = i, M.m<DecorationBuilder>(I => "Z ")));

            offsetSegment.AddInterpretter(new Blank(6f));
            offsetSegment.AddInterpretter(new Blank(9f));
            offsetSegment.AddInterpretter(new Blank(9f));
            offsetSegment.AddInterpretter(new Blank(9f));

            offsetSegment.AddInterpretter(StringDisplay.Quick("Add angle", "Add a value for each parameter")).Justify = TextAnchor.UpperRight;
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Orientation.x), new ToolTip("Add a value to 'Pitch'"), (I, i) => I.Data.Orientation.x = i, M.m<DecorationBuilder>(I => "X ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Orientation.y), new ToolTip("Add a value to 'Yaw'"), (I, i) => I.Data.Orientation.y = i, M.m<DecorationBuilder>(I => "Y ")));
            offsetSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.Orientation.z), new ToolTip("Add a value to 'Roll'"), (I, i) => I.Data.Orientation.z = i, M.m<DecorationBuilder>(I => "Z ")));

            CreateHeader("Tether point", null);

            ScreenSegmentStandard tetherSegment = CreateStandardSegment();
            tetherSegment.SpaceAbove = 10f;
            tetherSegment.SpaceBelow = 10f;

            tetherSegment.AddInterpretter(SubjectiveToggle<DecorationBuilder>.Quick(_focus, "Auto tether point", null, (I, b) => { I.Data.TP_AutoTetherPoint.Us = b; TriggerScreenRebuild(); }, I => I.Data.TP_AutoTetherPoint.Us));

            if (_focus.Data.TP_AutoTetherPoint.Us)
            {
                tetherSegment.AddInterpretter(SubjectiveToggle<DecorationBuilder>.Quick(_focus, "Normal offset", null, (I, b) => { I.Data.TP_NormalOffset.Us = b; TriggerScreenRebuild(); }, I => I.Data.TP_NormalOffset.Us));

                if (_focus.Data.TP_NormalOffset.Us)
                {
                    tetherSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.TP_DistanceToShift.Us), null, (I, i) => I.Data.TP_DistanceToShift.Us = i, M.m<DecorationBuilder>(I => "Distance to shift ")));
                }

                tetherSegment.AddInterpretter(SubjectiveToggle<DecorationBuilder>.Quick(_focus, "Block placement", null, (I, b) => { I.Data.TP_BlockPlacement.Us = b; TriggerScreenRebuild(); }, I => I.Data.TP_BlockPlacement.Us));

                if (_focus.Data.TP_BlockPlacement.Us)
                {
                    tetherSegment.AddInterpretter(TextInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.TP_BlockGUID.Us), "Block GUID", null, (I, s) => I.Data.TP_BlockGUID.Us = s));
                }
            }

            CreateHeader("Others", null);

            ScreenSegmentStandard otherSegment = CreateStandardSegment();
            otherSegment.SpaceAbove = 10f;
            otherSegment.SpaceBelow = 10f;

            if (_focus.IsGenerationActive)
            {
                otherSegment.AddInterpretter(SubjectiveDisplay<DecorationBuilder>.Quick(
                    _focus,
                    M.m<DecorationBuilder>(I => $"Build progress: {I.GenerationProgress:P1}")));
                otherSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(
                    _focus,
                    "Cancel and roll back build",
                    null,
                    I =>
                    {
                        I.CancelGeneration();
                        TriggerScreenRebuild();
                    }));
            }
            else
            {
                otherSegment.AddInterpretter(SubjectiveToggle<DecorationBuilder>.Quick(
                    _focus,
                    "Build animation",
                    null,
                    (I, b) =>
                    {
                        I.Data.BuildAnimation.Us = b;
                        TriggerScreenRebuild();
                    },
                    I => I.Data.BuildAnimation.Us));

                if (_focus.Data.BuildAnimation.Us)
                {
                    otherSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(
                        _focus,
                        M.m<DecorationBuilder>(I => I.Data.BA_Speed.Us),
                        null,
                        (I, i) => I.Data.BA_Speed.Us = i,
                        M.m<DecorationBuilder>(I => "Build animation speed ")));
                }
            }

            otherSegment.AddInterpretter(new Empty());

            otherSegment.AddInterpretter(SubjectiveToggle<DecorationBuilder>.Quick(
                _focus,
                "Local origin projection",
                new ToolTip("Generates a 3D model relative to the construct local origin."),
                (I, b) => I.Data.LocalOrigin.Us = b,
                I => I.Data.LocalOrigin.Us));
        }
    }
}
