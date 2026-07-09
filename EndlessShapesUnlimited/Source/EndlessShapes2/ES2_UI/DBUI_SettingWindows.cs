using BrilliantSkies.Ui.Consoles;

namespace EndlessShapes2.UI
{
    public class DBUI_SettingWindows : ConsoleUi<DecorationBuilder>
    {
        public DBUI_SettingWindows(DecorationBuilder focus) : base(focus)
        {
        }

        protected override ConsoleWindow BuildInterface(string suggestedName = "")
        {
            ConsoleWindow basicWindow = NewWindow(0, "Basic Setting", new ScaledRectangle(200f, 100f, 400f, 600f));
            basicWindow.DisplayTextPrompt = false;
            basicWindow.SetMultipleTabs(new DBUI_BasicSettingTab(basicWindow, _focus, this));

            ConsoleWindow advancedWindow = NewWindow(1, "Advanced Setting", new ScaledRectangle(680f, 100f, 400f, 600f));
            advancedWindow.DisplayTextPrompt = false;
            advancedWindow.SetMultipleTabs(new DBUIAdvancedSettingTab(advancedWindow, _focus));

            return basicWindow;
        }
    }
}
