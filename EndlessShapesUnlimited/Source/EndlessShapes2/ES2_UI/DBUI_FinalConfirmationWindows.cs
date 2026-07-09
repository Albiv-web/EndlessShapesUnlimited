using BrilliantSkies.Ui.Consoles;

namespace EndlessShapes2.UI
{
    public class DBUI_FinalConfirmationWindows : ConsoleUi<DecorationBuilder>
    {
        public DBUI_FinalConfirmationWindows(DecorationBuilder focus) : base(focus)
        {
        }

        protected override ConsoleWindow BuildInterface(string suggestedName = "")
        {
            ConsoleWindow buildWindow = NewWindow(0, "Build", new ScaledRectangle(350f, 100f, 580f, 600f));
            buildWindow.DisplayTextPrompt = false;
            buildWindow.SetMultipleTabs(new DBUI_FinalConfirmationTab(buildWindow, _focus, this));

            return buildWindow;
        }
    }
}
