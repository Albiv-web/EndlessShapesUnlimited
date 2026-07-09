using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons;
using BrilliantSkies.Ui.Consoles.Segments;
using BrilliantSkies.Ui.Displayer;
using System;
using UnityEngine;

namespace EndlessShapes2.UI
{
    public class DBUI_FinalConfirmationTab : SuperScreen<DecorationBuilder>
    {
        private readonly DBUI_FinalConfirmationWindows _mainUI;

        public DBUI_FinalConfirmationTab(ConsoleWindow window, DecorationBuilder focus, DBUI_FinalConfirmationWindows mainUI) : base(window, focus)
        {
            _mainUI = mainUI;
        }

        public override void Build()
        {
            ScreenSegmentTable summarySegment = CreateTableSegment(2, 4);
            summarySegment.SpaceAbove = 20f;
            summarySegment.SpaceBelow = 20f;
            summarySegment.SetConditionalDisplay(() => _focus.SelectMesh != null);

            SubjectiveDisplay<DecorationBuilder> AddDisplay(Func<DecorationBuilder, string> text) =>
                summarySegment.AddInterpretter(SubjectiveDisplay<DecorationBuilder>.Quick(_focus, M.m(text)));

            AddDisplay(I => "Name").Justify = TextAnchor.UpperRight;
            AddDisplay(I => I.SelectMesh.Name).Justify = TextAnchor.UpperLeft;
            AddDisplay(I => "Face count").Justify = TextAnchor.UpperRight;
            AddDisplay(I => I.SelectMesh.FaceDatas.Count.ToString()).Justify = TextAnchor.UpperLeft;
            AddDisplay(I => "Line count").Justify = TextAnchor.UpperRight;
            AddDisplay(I => I.SelectMesh.LineDatas.Count.ToString()).Justify = TextAnchor.UpperLeft;
            AddDisplay(I => "Number of decorations to generate").Justify = TextAnchor.UpperRight;
            AddDisplay(I => I.PolygonDataListCount.ToString()).Justify = TextAnchor.UpperLeft;

            ScreenSegmentStandard actionsSegment = CreateStandardSegment();
            actionsSegment.SpaceAbove = 20f;
            actionsSegment.SpaceBelow = 20f;

            actionsSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(_focus, "Return to settings menu", null,
                (DecorationBuilder I) =>
                {
                    new DBUI_SettingWindows(_focus).ActivateGui(GuiActivateType.Stack);
                    _mainUI.DeactivateGui(GuiDeactivateType.Standard);
                }));
            actionsSegment.AddInterpretter(new Empty());
            actionsSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(_focus, "Build", null,
                (DecorationBuilder I) =>
                {
                    I.Start();

                    new DBUI_SettingWindows(_focus).ActivateGui(GuiActivateType.Stack);
                    _mainUI.DeactivateGui(GuiDeactivateType.Standard);
                }));
        }
    }
}
