using System.Collections.Generic;
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
            public bool DecorationEditPromptBeforeHotkeyClose { get; set; } = true;
            public float DecorationMoveSnap { get; set; } = 0.001f;
            public float DecorationRotateSnapDegrees { get; set; } = 0.001f;
            public float DecorationScaleSnap { get; set; } = 0.001f;
            public bool DecorationSmoothSnapDefaultsMigrated { get; set; }
            public int SmartBuildMoveStepCells { get; set; } = 1;
            public float SmartBuildRotateSnapDegrees { get; set; } = 90f;
            public int SmartBuildScaleStepCells { get; set; } = 1;
            public bool StreamLargeBlueprintJsonSaves { get; set; }
            public FastBlueprintLoadTier FastBlueprintLoadTier { get; set; }
            public bool FastBlueprintLoadDiagnostics { get; set; }
            public bool FastBlueprintLoadSmallBlueprintTesting { get; set; }
            public bool FastBlueprintLoadForceV2BlockData { get; set; }
            public bool FastBlueprintLoadBlockCountRouting { get; set; }
            public FastBlueprintLoadUnsafeProbeMode FastBlueprintLoadUnsafeProbeMode { get; set; }
            public List<AutomationSystemBlockTemplateData> AutomationSystemBlockTemplates { get; set; } =
                new List<AutomationSystemBlockTemplateData>();
            public List<AutomationWorkspaceData> AutomationWorkspaces { get; set; } =
                new List<AutomationWorkspaceData>();
        }

        public sealed class AutomationSystemBlockTemplateData
        {
            public string Name { get; set; }
            public List<string> InputPorts { get; set; } = new List<string>();
            public List<string> OutputPorts { get; set; } = new List<string>();
            public string Comment { get; set; }
            public string InternalGraph { get; set; }
        }

        public sealed class AutomationWorkspaceData
        {
            public string ControllerKey { get; set; }
            public string ControllerPersistenceKey { get; set; }
            public string ControllerLabel { get; set; }
            public List<AutomationLinkData> Links { get; set; } = new List<AutomationLinkData>();
            public AutomationBlockWorkspaceData Blocks { get; set; }
        }

        public sealed class AutomationLinkData
        {
            public string TargetKey { get; set; }
            public string TargetPersistenceKey { get; set; }
            public string TargetLabel { get; set; }
            public int Direction { get; set; }
        }

        public sealed class AutomationBlockWorkspaceData
        {
            public int Mode { get; set; }
            public float CanvasPanX { get; set; }
            public float CanvasPanY { get; set; }
            public float CanvasZoom { get; set; } = 1f;
            public float NativeDisplayScale { get; set; } = 1f;
            public string NativeImportStatus { get; set; }
            public string SelectedNodeId { get; set; }
            public List<AutomationBlockNodeData> Nodes { get; set; } =
                new List<AutomationBlockNodeData>();
            public List<AutomationBlockLinkData> Links { get; set; } =
                new List<AutomationBlockLinkData>();
        }

        public sealed class AutomationBlockNodeData
        {
            public string Id { get; set; }
            public int Kind { get; set; }
            public string Label { get; set; }
            public string IconKey { get; set; }
            public int Category { get; set; }
            public string PaletteTemplateId { get; set; }
            public string ParentNodeId { get; set; }
            public int CanvasOrder { get; set; }
            public float CanvasX { get; set; }
            public float CanvasY { get; set; }
            public bool SnappedToStack { get; set; }
            public string TargetKey { get; set; }
            public string TargetPersistenceKey { get; set; }
            public string TargetLabel { get; set; }
            public int LinkDirection { get; set; }
            public int Operator { get; set; }
            public float NumericValue { get; set; }
            public float SecondaryNumericValue { get; set; }
            public string Comment { get; set; }
            public string Expression { get; set; }
            public AutomationProxyPropertySelectionData PropertySelection { get; set; }
            public string NativeComponentTypeName { get; set; }
            public string NativeComponentLabel { get; set; }
            public string NativeComponentDescription { get; set; }
            public string NativeBlockTypeName { get; set; }
            public string NativeBlockFilter { get; set; }
            public uint NativeComponentId { get; set; }
            public string NativeComponentTypeId { get; set; }
            public string NativeComponentFingerprint { get; set; }
            public bool NativeImported { get; set; }
            public bool NativeEsuOwned { get; set; }
            public float NativeX { get; set; }
            public float NativeY { get; set; }
            public float NativeWidth { get; set; }
            public float NativeHeight { get; set; }
            public string NativeSettingsSummary { get; set; }
            public List<string> NativeInputPortLabels { get; set; } =
                new List<string>();
            public List<string> NativeOutputPortLabels { get; set; } =
                new List<string>();
        }

        public sealed class AutomationBlockLinkData
        {
            public string FromNodeId { get; set; }
            public string FromPortId { get; set; }
            public string ToNodeId { get; set; }
            public string ToPortId { get; set; }
            public uint FromNativeComponentId { get; set; }
            public int FromNativePortIndex { get; set; } = -1;
            public uint ToNativeComponentId { get; set; }
            public int ToNativePortIndex { get; set; } = -1;
        }

        public sealed class AutomationProxyPropertySelectionData
        {
            public string Label { get; set; }
            public string Tooltip { get; set; }
            public bool IsGetter { get; set; }
            public bool IsClear { get; set; }
            public bool IsGetterReadable { get; set; }
            public uint ReadableAttributeId { get; set; }
            public uint BlockPropertyId { get; set; }
            public uint BlockSetId { get; set; }
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

    public enum FastBlueprintLoadUnsafeProbeMode
    {
        Off,
        SkipV3SyncRegistration,
        SkipV3StatusRegistration,
        SkipStage2ModuleExternalLinkup,
        SkipV3ColliderLinkup,
        SkipV3ShellLinkup,
        SkipV3SkinCalc
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
        ToggleAutomationEditMode,
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
                "Cycle EndlessShapes Unlimited modes: Decoration Edit, Surface Builder, Smart Builder, Automation.",
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
            SetVolatile(
                SerializationHudKeyInput.ToggleAutomationEditMode,
                "Toggle Automation Editor",
                "Open or close the EndlessShapes Unlimited Automation Editor.",
                category,
                Q(Key.Control, Key.Shift, Key.A));
        }

        protected override int IdToInt(SerializationHudKeyInput id) => (int)id;

        public Vector3 GetMovementDirection(bool smoothDigitalInput = true) =>
            Vector3.zero;
    }
}
