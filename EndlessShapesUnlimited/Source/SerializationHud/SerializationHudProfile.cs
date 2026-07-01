using BrilliantSkies.Localisation;
using BrilliantSkies.PlayerProfiles;
using InControl;
using UnityEngine;

namespace DecoLimitLifter.SerializationHud
{
    public sealed class SerializationHudProfile :
        ProfileModule<SerializationHudProfile.ProfileData>
    {
        public sealed class ProfileData
        {
            public bool Enabled { get; set; }
            public bool EnforceVanillaCompatibility { get; set; } = true;
            public bool EsuEditorAutoScale { get; set; } = true;
            public float EsuEditorScale { get; set; } = 1f;
            public float DecorationMoveSnap { get; set; } = 0.05f;
            public float DecorationRotateSnapDegrees { get; set; } = 5f;
            public float DecorationScaleSnap { get; set; } = 0.05f;
            public int SmartBuildMoveStepCells { get; set; } = 1;
            public float SmartBuildRotateSnapDegrees { get; set; } = 90f;
            public int SmartBuildScaleStepCells { get; set; } = 1;
            public bool StreamLargeBlueprintJsonSaves { get; set; }
            public FastBlueprintLoadTier FastBlueprintLoadTier { get; set; }
            public bool FastBlueprintLoadDiagnostics { get; set; }
            public bool FastBlueprintLoadSmallBlueprintTesting { get; set; }
        }

        internal static ProfileData Data =>
            ProfileManager.Instance.GetModule<SerializationHudProfile>().GetInternalData();

        public override ModuleType ModuleType => ModuleType.Options;

        protected override string FilenameAndExtension =>
            "profile.endlessshapesunlimited";
    }

    public enum FastBlueprintLoadTier
    {
        Off,
        V1,
        V2,
        V3
    }

    public enum SerializationHudKeyInput
    {
        ToggleHud,
        MeasureUsage,
        ToggleDecorationEditMode,
        ToggleSmartBuildMode,
        SwitchEsuBuildMode,
        UndoDecorationEdit,
        RedoDecorationEdit,
        MaxId
    }

    public sealed class SerializationHudKeyMap : KeyMap<SerializationHudKeyInput>
    {
        internal static SerializationHudKeyMap Instance =>
            ProfileManager.Instance.GetModule<SerializationHudKeyMap>();

        public SerializationHudKeyMap()
            : base(SerializationHudKeyInput.MaxId)
        {
        }

        public override ModuleType ModuleType => ModuleType.Options;

        protected override string FilenameAndExtension =>
            "profile.keymappingendlessshapesunlimited";

        protected override void FillAllVolatileData()
        {
            var category = new KeyAndEng(string.Empty, "in game", string.Empty);
            SetVolatile(
                SerializationHudKeyInput.ToggleHud,
                "Toggle serialization HUD",
                "Show or hide EndlessShapes Unlimited serialization usage.",
                category,
                Q(Key.F8));
            SetVolatile(
                SerializationHudKeyInput.MeasureUsage,
                "Measure serialization usage",
                "Run an exact EndlessShapes Unlimited serialization usage measurement for the focused vehicle.",
                category,
                Q(Key.Shift, Key.F8));
            SetVolatile(
                SerializationHudKeyInput.ToggleDecorationEditMode,
                "Toggle Decoration Edit Mode",
                "Open or close the EndlessShapes Unlimited decoration editor.",
                category,
                Q(Key.Control, Key.D));
            SetVolatile(
                SerializationHudKeyInput.ToggleSmartBuildMode,
                "Toggle smart block builder",
                "Open or close the EndlessShapes Unlimited Smart Block Builder.",
                category,
                Q(Key.Control, Key.Shift, Key.B));
            SetVolatile(
                SerializationHudKeyInput.SwitchEsuBuildMode,
                "Switch ESU build mode",
                "Cycle EndlessShapes Unlimited modes: Decoration Edit, Surface Builder, Smart Builder.",
                category,
                Q(Key.Tab));
            SetVolatile(
                SerializationHudKeyInput.UndoDecorationEdit,
                "Undo Decoration Edit",
                "Undo the last un-applied EndlessShapes Unlimited decoration editor action.",
                category,
                Q(Key.Control, Key.Z));
            SetVolatile(
                SerializationHudKeyInput.RedoDecorationEdit,
                "Redo Decoration Edit",
                "Redo the last undone EndlessShapes Unlimited decoration editor action.",
                category,
                Q(Key.Control, Key.Y));
        }

        protected override int IdToInt(SerializationHudKeyInput id) => (int)id;

        public Vector3 GetMovementDirection(bool smoothDigitalInput = true) =>
            Vector3.zero;
    }
}
