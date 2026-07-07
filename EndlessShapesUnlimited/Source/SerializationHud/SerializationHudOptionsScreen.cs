using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Getters;
using BrilliantSkies.Ui.Consoles.Interpretters;
using BrilliantSkies.Ui.Consoles.Interpretters.Simple;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Choices;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Numbers;
using BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Texts;
using BrilliantSkies.Ui.Tips;
using DecoLimitLifter.AutomationEditMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using Ui.Consoles.Examples;

namespace DecoLimitLifter.SerializationHud
{
    public sealed class SerializationHudOptionsScreen :
        KeyMappingUi<SerializationHudKeyInput>
    {
        private static readonly FastBlueprintLoadUnsafeProbeMode[] UnsafeProbeCycle =
        {
            FastBlueprintLoadUnsafeProbeMode.Off,
            FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration,
            FastBlueprintLoadUnsafeProbeMode.SkipV3StatusRegistration,
            FastBlueprintLoadUnsafeProbeMode.SkipStage2ModuleExternalLinkup,
            FastBlueprintLoadUnsafeProbeMode.SkipV3ColliderLinkup,
            FastBlueprintLoadUnsafeProbeMode.SkipV3ShellLinkup,
            FastBlueprintLoadUnsafeProbeMode.SkipV3SkinCalc
        };

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
                "Vanilla compatibility",
                new ToolTip(
                    "Prevent ESU-only decoration saves while building for unmodded From The Depths."));
            var compatibility = CreateTableSegment(2, 2);
            compatibility.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Vanilla compatibility mode",
                    new ToolTip(
                        "When enabled, ESU blocks decoration creation and saves that would require ESU extended decoration data. Keeps saves vanilla-compatible when the craft stays under vanilla limits."),
                    (profile, value) => profile.EnforceVanillaCompatibility = value,
                    profile => profile.EnforceVanillaCompatibility));

            CreateHeader(
                "Blueprint saving",
                new ToolTip(
                    "Optional streamed saving for very large blueprint JSON files."));
            var blueprintSaving = CreateTableSegment(2, 2);
            blueprintSaving.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Stream large blueprint JSON saves",
                    new ToolTip(
                        "When enabled, ESU streams blueprint JSON saves estimated at 64 MiB or larger without changing the file format."),
                    (profile, value) => profile.StreamLargeBlueprintJsonSaves = value,
                    profile => profile.StreamLargeBlueprintJsonSaves));

            CreateHeader(
                "Blueprint loading",
                new ToolTip(
                    "Optional load acceleration for very large or block-count-heavy blueprints. The saved blueprint schema is not changed."));
            var blueprintLoading = CreateTableSegment(2, 4);
            blueprintLoading.AddInterpretter(
                SubjectiveButton<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Fast load tier",
                    new ToolTip(
                        "Cycles Off, V1, V2, and V3. Off keeps the vanilla load path. V3 is recommended for huge block-count craft and falls back if ESU cannot prove the hooks are safe."),
                    profile =>
                    {
                        CycleFastBlueprintLoadTier(profile);
                        TriggerScreenRebuild();
                    }));
            blueprintLoading.AddInterpretter(
                SubjectiveDisplay<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    M.m<SerializationHudProfile.ProfileData>(
                        profile => FastBlueprintLoadTierStatus(profile))));
            blueprintLoading.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Fast load diagnostics",
                    new ToolTip(
                        "Log passive ESU blueprint load timings to FtD and write a shareable EndlessShapesUnlimited/Logs fast-load trace file. This does not enable fast loading by itself."),
                    (profile, value) => profile.FastBlueprintLoadDiagnostics = value,
                    profile => profile.FastBlueprintLoadDiagnostics));
            blueprintLoading.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Apply fast load to small blueprints",
                    new ToolTip(
                        "Testing override. When disabled, ESU only routes large blueprint loads through the selected fast-load tier."),
                    (profile, value) => profile.FastBlueprintLoadSmallBlueprintTesting = value,
                    profile => profile.FastBlueprintLoadSmallBlueprintTesting));
            blueprintLoading.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Force V2 block-data fast path",
                    new ToolTip(
                        "Diagnostics override. When enabled, ESU runs V2 block-data predecode even when the BlockData payload is small."),
                    (profile, value) => profile.FastBlueprintLoadForceV2BlockData = value,
                    profile => profile.FastBlueprintLoadForceV2BlockData));
            blueprintLoading.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Apply V3 to block-count-heavy blueprints",
                    new ToolTip(
                        "Explicit V3 opt-in. Allows below-64 MiB blueprints with very high saved block counts to use V3. Leave off for normal testing unless you are measuring huge block-count craft."),
                    (profile, value) => profile.FastBlueprintLoadBlockCountRouting = value,
                    profile => profile.FastBlueprintLoadBlockCountRouting));
            blueprintLoading.AddInterpretter(
                SubjectiveButton<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Unsafe probe mode",
                    new ToolTip(
                        "Diagnostics only. Cycles one unsafe V3 timing probe at a time. These can create invalid loaded constructs, log do_not_save=true, and must not be used for normal play, saving, or correctness testing."),
                    profile =>
                    {
                        CycleFastBlueprintLoadUnsafeProbeMode(profile);
                        TriggerScreenRebuild();
                    }));
            blueprintLoading.AddInterpretter(
                SubjectiveDisplay<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    M.m<SerializationHudProfile.ProfileData>(
                        profile => FastBlueprintLoadUnsafeProbeStatus(profile))));

            CreateHeader(
                "ESU editor HUD",
                new ToolTip(
                    "Scale and reset the Decoration Edit, Surface Builder, Smart Builder, and Automation overlays."));
            var editorHud = CreateTableSegment(2, 2);
            editorHud.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Automatic editor scaling",
                    new ToolTip(
                        "Shrink ESU editor panels automatically on smaller game windows."),
                    (profile, value) => profile.EsuEditorAutoScale = value,
                    profile => profile.EsuEditorAutoScale));
            editorHud.AddInterpretter(
                SubjectiveFloatClampedWithBar<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    EsuHudLayout.MinManualScale,
                    EsuHudLayout.MaxManualScale,
                    0.05f,
                    M.m<SerializationHudProfile.ProfileData>(
                        profile => EsuHudLayout.ClampManualScale(profile.EsuEditorScale)),
                    "Editor scale {0:0.00}x",
                    (profile, value) => profile.EsuEditorScale = EsuHudLayout.ClampManualScale(value),
                    new ToolTip(
                        "Multiplier applied after automatic ESU editor scaling.")));
            editorHud.AddInterpretter(
                SubjectiveButton<byte>.Quick(
                    0,
                    "Reset ESU editor layout",
                    new ToolTip(
                        "Move and resize ESU editor panels back to their responsive defaults."),
                    _ => EsuHudLayout.RequestReset()));
            editorHud.AddInterpretter(
                StringDisplay.Quick(
                    "Current ESU editor scale",
                    EsuHudLayout.ScaleSummary()));

            CreateHeader(
                "Decoration Edit Mode",
                new ToolTip(
                    "Modal decoration editor for selecting, moving, retethering, and assigning meshes in build mode."));
            var editor = CreateTableSegment(2, 1);
            editor.AddInterpretter(
                SubjectiveToggle<SerializationHudProfile.ProfileData>.Quick(
                    data,
                    "Warn before Ctrl+D auto-applies",
                    new ToolTip(
                        "When Decoration Edit Mode has unapplied changes, pressing Ctrl+D asks before applying the preview and closing."),
                    (profile, value) => profile.DecorationEditPromptBeforeHotkeyClose = value,
                    profile => profile.DecorationEditPromptBeforeHotkeyClose));
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

            CreateHeader(
                "Automation Editor",
                new ToolTip(
                    "Modal Breadboard/ACB editor for placing automation controllers, selecting targets, and preparing graph/code links."));
            var automationEditor = CreateTableSegment(1, 1);
            automationEditor.AddInterpretter(
                BrilliantSkies.Ui.Consoles.Interpretters.Subjective.Buttons.SubjectiveButton<byte>.Quick(
                    0,
                    "Toggle Automation Editor",
                    new ToolTip(
                        "Can only open while building on a craft. The keybind defaults to Ctrl+Shift+A."),
                    _ => AutomationEditModeRegistration.ToggleFromUi()));
        }

        private static void CycleFastBlueprintLoadTier(
            SerializationHudProfile.ProfileData profile)
        {
            if (profile == null)
                return;

            profile.FastBlueprintLoadTier = profile.FastBlueprintLoadTier == FastBlueprintLoadTier.V3
                ? FastBlueprintLoadTier.Off
                : profile.FastBlueprintLoadTier + 1;
        }

        internal static string FastBlueprintLoadTierLabel(FastBlueprintLoadTier tier)
        {
            switch (tier)
            {
                case FastBlueprintLoadTier.V1:
                    return "V1 - streamed JSON";
                case FastBlueprintLoadTier.V2:
                    return "V2 - parallel predecode";
                case FastBlueprintLoadTier.V3:
                    return "V3 - huge-craft bulk";
                default:
                    return "Off - vanilla";
            }
        }

        internal static string FastBlueprintLoadTierStatus(
            SerializationHudProfile.ProfileData profile) =>
            "Current fast load tier: " +
            FastBlueprintLoadTierLabel(profile?.FastBlueprintLoadTier ?? FastBlueprintLoadTier.Off);

        private static void CycleFastBlueprintLoadUnsafeProbeMode(
            SerializationHudProfile.ProfileData profile)
        {
            if (profile == null)
                return;

            int index = System.Array.IndexOf(
                UnsafeProbeCycle,
                profile.FastBlueprintLoadUnsafeProbeMode);
            if (index < 0)
                index = 0;
            profile.FastBlueprintLoadUnsafeProbeMode =
                UnsafeProbeCycle[(index + 1) % UnsafeProbeCycle.Length];
        }

        internal static string FastBlueprintLoadUnsafeProbeLabel(
            FastBlueprintLoadUnsafeProbeMode mode)
        {
            switch (mode)
            {
                case FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration:
                    return "Skip V3 sync registration";
                case FastBlueprintLoadUnsafeProbeMode.SkipV3StatusRegistration:
                    return "Skip V3 status registration";
                case FastBlueprintLoadUnsafeProbeMode.SkipStage2ModuleExternalLinkup:
                    return "Skip Stage2 module linkup";
                case FastBlueprintLoadUnsafeProbeMode.SkipV3ColliderLinkup:
                    return "Skip V3 collider linkup";
                case FastBlueprintLoadUnsafeProbeMode.SkipV3ShellLinkup:
                    return "Skip V3 shell linkup";
                case FastBlueprintLoadUnsafeProbeMode.SkipV3SkinCalc:
                    return "Skip V3 skin calculation";
                default:
                    return "Off";
            }
        }

        internal static string FastBlueprintLoadUnsafeProbeStatus(
            SerializationHudProfile.ProfileData profile) =>
            "Unsafe probe: " +
            FastBlueprintLoadUnsafeProbeLabel(
                profile?.FastBlueprintLoadUnsafeProbeMode ?? FastBlueprintLoadUnsafeProbeMode.Off);
    }
}
