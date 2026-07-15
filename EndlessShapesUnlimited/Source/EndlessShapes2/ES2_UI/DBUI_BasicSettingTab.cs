using AdvancedMimicUi;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters;
using BrilliantSkies.Ui.Consoles.Interpretters.Simple;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Numbers;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Texts;
using BrilliantSkies.Ui.Consoles.Segments;
using BrilliantSkies.Ui.Displayer;
using BrilliantSkies.Ui.Layouts.DropDowns;
using DecoLimitLifter.DecorationEditMode;
using EndlessShapes2.Polygon;
using UnityEngine;

namespace EndlessShapes2.UI
{
    public class DBUI_BasicSettingTab : SuperScreen<DecorationBuilder>
    {
        private static readonly DropDownMenuAlt<StructureBlockType> StructureBlockDropDownMenu =
            CreateStructureBlockDropDownMenu();

        private static DropDownMenuAlt<StructureBlockType> CreateStructureBlockDropDownMenu()
        {
            DropDownMenuAlt<StructureBlockType> dropDownMenuAlt = new DropDownMenuAlt<StructureBlockType>();

            dropDownMenuAlt.SetItems(new DropDownMenuAltItem<StructureBlockType>[]
            {
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Wood",
                    ObjectForAction = StructureBlockType.Wood
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Stone",
                    ObjectForAction = StructureBlockType.Stone
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Metal",
                    ObjectForAction = StructureBlockType.Metal
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Alloy",
                    ObjectForAction = StructureBlockType.Alloy
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Glass",
                    ObjectForAction = StructureBlockType.Glass
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Lead",
                    ObjectForAction = StructureBlockType.Lead
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Heavy armour",
                    ObjectForAction = StructureBlockType.HeavyArmour
                },
                new DropDownMenuAltItem<StructureBlockType>
                {
                    Name = "Rubber",
                    ObjectForAction = StructureBlockType.Rubber
                }
            });

            return dropDownMenuAlt;
        }

        private readonly DBUI_SettingWindows _mainUI;

        public DBUI_BasicSettingTab(ConsoleWindow window, DecorationBuilder focus, DBUI_SettingWindows mainUI) : base(window, focus)
        {
            _mainUI = mainUI;
        }

        public override void Build()
        {
            ScreenSegmentStandard settingsSegment = CreateStandardSegment();
            settingsSegment.SpaceAbove = 20f;
            settingsSegment.SpaceBelow = 20f;

            settingsSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.FaceThickness.Us), null, (I, f) => I.Data.FaceThickness.Us = f, M.m<DecorationBuilder>(I => "FaceThickness : ")));
            settingsSegment.AddInterpretter(SimpleFloatInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.LineThickness.Us), null, (I, f) => I.Data.LineThickness.Us = f, M.m<DecorationBuilder>(I => "LineThickness : ")));
            settingsSegment.AddInterpretter(new DropDown<DecorationBuilder, StructureBlockType>(_focus, StructureBlockDropDownMenu, (I, s) => I.Data.SBType.Us.Equals(s), (I, s) => I.Data.SBType.Us = s));
            settingsSegment.AddInterpretter(SubjectiveFloatClampedWithBar<DecorationBuilder>.Quick(_focus, 0f, 31f, 1f, M.m<DecorationBuilder>(I => I.Data.DefaultColorIndex.Us), "Color {0}", (I, f) => I.Data.DefaultColorIndex.Us = Mathf.RoundToInt(f), null));
            settingsSegment.AddInterpretter(new Blank(20f));
            settingsSegment.AddInterpretter(TextInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.TexturePath.Us), "TexturePath", null, (I, s) => I.Data.TexturePath.Us = s));
            settingsSegment.AddInterpretter(new Blank(5f));
            settingsSegment.AddInterpretter(TextInput<DecorationBuilder>.Quick(_focus, M.m<DecorationBuilder>(I => I.Data.OBJ_FilePath.Us), "FilePath", null, (I, s) => I.Data.OBJ_FilePath.Us = s));
            settingsSegment.AddInterpretter(new Blank(5f));
            settingsSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(_focus, "Load", null, I => { I.Load(); TriggerScreenRebuild(); }));
            settingsSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(
                _focus,
                "Decoration Edit Mode",
                null,
                I => DecorationEditModeRegistration.ToggleFromUi()));
            settingsSegment.AddInterpretter(new Blank(20f));

            foreach (OBJ_Mesh meshes in _focus.Meshes)
            {
                settingsSegment.AddInterpretter(SubjectiveButton<DecorationBuilder>.Quick(_focus, meshes.Name, null,
                    (DecorationBuilder I) =>
                    {
                        I.SetSelectMesh(meshes);

                        new DBUI_FinalConfirmationWindows(_focus).ActivateGui(GuiActivateType.Stack);
                        _mainUI.DeactivateGui(GuiDeactivateType.Standard);
                    }));
            }
        }
    }
}
