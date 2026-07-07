using System;
using System.Collections.Generic;
using System.Linq;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationNativeGraphSnapshot
    {
        internal AutomationNativeGraphSnapshot(
            string controllerLabel,
            IReadOnlyList<AutomationNativeComponentSnapshot> components,
            IReadOnlyList<AutomationNativeWireSnapshot> wires)
        {
            ControllerLabel = string.IsNullOrWhiteSpace(controllerLabel)
                ? "Automation controller"
                : controllerLabel;
            Components = components ?? Array.Empty<AutomationNativeComponentSnapshot>();
            Wires = wires ?? Array.Empty<AutomationNativeWireSnapshot>();
        }

        internal string ControllerLabel { get; }

        internal IReadOnlyList<AutomationNativeComponentSnapshot> Components { get; }

        internal IReadOnlyList<AutomationNativeWireSnapshot> Wires { get; }

        internal bool HasNativeGraph => Components.Count > 0;
    }

    internal sealed class AutomationNativeComponentSnapshot
    {
        internal AutomationNativeComponentSnapshot(
            uint componentId,
            Guid componentTypeId,
            string typeName,
            string label,
            string description,
            string blockTypeName,
            string blockFilter,
            float x,
            float y,
            float width,
            float height,
            string settingsSummary,
            IReadOnlyList<AutomationNativePortSnapshot> inputs,
            IReadOnlyList<AutomationNativePortSnapshot> outputs)
        {
            ComponentId = componentId;
            ComponentTypeId = componentTypeId;
            TypeName = typeName ?? string.Empty;
            Label = string.IsNullOrWhiteSpace(label) ? "Native component" : label;
            Description = description ?? string.Empty;
            BlockTypeName = blockTypeName ?? string.Empty;
            BlockFilter = blockFilter ?? string.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            SettingsSummary = settingsSummary ?? string.Empty;
            Inputs = inputs ?? Array.Empty<AutomationNativePortSnapshot>();
            Outputs = outputs ?? Array.Empty<AutomationNativePortSnapshot>();
            Fingerprint = BuildFingerprint();
        }

        internal uint ComponentId { get; }

        internal Guid ComponentTypeId { get; }

        internal string TypeName { get; }

        internal string Label { get; }

        internal string Description { get; }

        internal string BlockTypeName { get; }

        internal string BlockFilter { get; }

        internal float X { get; }

        internal float Y { get; }

        internal float Width { get; }

        internal float Height { get; }

        internal string SettingsSummary { get; }

        internal IReadOnlyList<AutomationNativePortSnapshot> Inputs { get; }

        internal IReadOnlyList<AutomationNativePortSnapshot> Outputs { get; }

        internal string Fingerprint { get; }

        private string BuildFingerprint()
        {
            string inputShape = string.Join(",", Inputs.Select(port => port.Label ?? string.Empty).ToArray());
            string outputShape = string.Join(",", Outputs.Select(port => port.Label ?? string.Empty).ToArray());
            return string.Join(
                "|",
                new[]
                {
                    TypeName,
                    ComponentTypeId.ToString("D"),
                    Label,
                    BlockTypeName,
                    BlockFilter,
                    X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    inputShape,
                    outputShape
                });
        }
    }

    internal sealed class AutomationNativePortSnapshot
    {
        internal AutomationNativePortSnapshot(
            int index,
            string label,
            bool isOutput)
        {
            Index = index;
            Label = string.IsNullOrWhiteSpace(label) ? "Port " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : label;
            IsOutput = isOutput;
        }

        internal int Index { get; }

        internal string Label { get; }

        internal bool IsOutput { get; }
    }

    internal sealed class AutomationNativeWireSnapshot
    {
        internal AutomationNativeWireSnapshot(
            uint fromComponentId,
            int fromOutputIndex,
            uint toComponentId,
            int toInputIndex)
        {
            FromComponentId = fromComponentId;
            FromOutputIndex = fromOutputIndex;
            ToComponentId = toComponentId;
            ToInputIndex = toInputIndex;
        }

        internal uint FromComponentId { get; }

        internal int FromOutputIndex { get; }

        internal uint ToComponentId { get; }

        internal int ToInputIndex { get; }
    }
}
