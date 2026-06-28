using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Choices;
using BrilliantSkies.Ui.Tips;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using Ui.Consoles.Examples;

namespace DecoLimitLifter.SerializationHud
{
    public sealed class SerializationHudOptionsScreen :
        KeyMappingUi<SerializationHudKeyInput>
    {
        public SerializationHudOptionsScreen(ConsoleWindow window)
            : base(window, ProfileManager.Instance.GetModule<SerializationHudKeyMap>())
        {
        }

        public override Content Name => new Content(
            "Endless Shapes Unlimited",
            "Decoration and serialization HUD settings");

        protected override void BuildInitialOptionalInfo()
        {
            CreateHeader(
                "Serialization HUD",
                new ToolTip("Display decoration counts and blueprint serializer usage."));
            SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
            var settings = CreateTableSegment(2, 2);
            settings.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Show serialization HUD",
                    new ToolTip(
                        "Append EndlessShapes Unlimited usage to the vehicle HUD."),
                    (profile, value) => profile.Enabled = value,
                    profile => profile.Enabled));

            CreateHeader(
                "Decoration Edit Mode",
                new ToolTip(
                    "Modal decoration editor for selecting, moving, retethering, and assigning meshes in build mode."));
            var editor = CreateTableSegment(1, 1);
            editor.AddInterpretter(
                BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons.SubjectiveButton<byte>.Quick(
                    0,
                    "Toggle Decoration Edit Mode",
                    new ToolTip(
                        "Can only open while building on a craft. The keybind defaults to Ctrl+D."),
                    _ => DecorationEditModeRegistration.ToggleFromUi()));

            CreateHeader(
                "Smart Block Builder",
                new ToolTip(
                    "Modal rectangular block builder that previews a legal FTD grid volume before placing real blocks."));
            var smartBuilder = CreateTableSegment(1, 1);
            smartBuilder.AddInterpretter(
                BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons.SubjectiveButton<byte>.Quick(
                    0,
                    "Toggle Smart Block Builder",
                    new ToolTip(
                        "Can only open while building on a craft. The keybind defaults to Ctrl+Shift+B."),
                    _ => SmartBuildModeRegistration.ToggleFromUi()));
        }
    }
}
