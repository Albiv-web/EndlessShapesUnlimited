using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DecoLimitLifter.SerializationHud;

namespace DecoLimitLifter.AutomationEditMode
{
    internal enum AutomationBlockKind
    {
        WhenIf,
        ReadTarget,
        Compare,
        MathScale,
        MathEvaluator,
        Switch,
        SetTarget,
        Constant,
        Delay,
        Comment,
        SystemBlock,
        NativeComponent
    }

    internal enum AutomationBlockWorkspaceMode
    {
        Semantic,
        NativeExact
    }

    internal enum AutomationBlockCategory
    {
        Output,
        Input,
        Control,
        Math,
        Variables,
        Timing,
        Organization,
        Advanced,
        Notation
    }

    internal enum AutomationBlockPortDirection
    {
        Input,
        Output
    }

    internal enum AutomationLinkDirection
    {
        Input,
        Output
    }

    internal enum AutomationCompareOperator
    {
        GreaterThan,
        LessThan,
        EqualOrGreater,
        EqualOrLess
    }

    internal enum AutomationBlockSettingKind
    {
        Target,
        Property,
        Number,
        Text,
        Operator,
        NativeComponentType
    }

    internal enum AutomationProxyPropertyBindingMode
    {
        Unset,
        Explicit
    }

    internal enum AutomationBlockAudience
    {
        Beginner,
        Advanced,
        NativeWrapper,
        MetadataOnly
    }

    internal enum AutomationBlockCompatibility
    {
        Native,
        NativeViaGetterSetter,
        LayoutTemplateOnly,
        NotLowerableYet
    }

    internal sealed class AutomationBlockPortDefinition
    {
        internal AutomationBlockPortDefinition(
            string name,
            AutomationBlockPortDirection direction)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "port" : name;
            Direction = direction;
        }

        internal string Name { get; }

        internal AutomationBlockPortDirection Direction { get; }
    }

    internal sealed class AutomationBlockSettingDefinition
    {
        internal AutomationBlockSettingDefinition(
            string name,
            AutomationBlockSettingKind kind,
            float defaultNumber = 0f)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Setting" : name;
            Kind = kind;
            DefaultNumber = defaultNumber;
        }

        internal string Name { get; }

        internal AutomationBlockSettingKind Kind { get; }

        internal float DefaultNumber { get; }
    }

    internal sealed class AutomationBlockDefinition
    {
        internal AutomationBlockDefinition(
            AutomationBlockKind kind,
            string templateId,
            string label,
            string iconKey,
            AutomationBlockCategory category,
            string description,
            IReadOnlyList<AutomationBlockPortDefinition> inputPorts,
            IReadOnlyList<AutomationBlockPortDefinition> outputPorts,
            IReadOnlyList<AutomationBlockSettingDefinition> settings,
            string nativeComponentTypeName = null,
            AutomationBlockAudience audience = AutomationBlockAudience.Beginner,
            AutomationBlockCompatibility compatibility = AutomationBlockCompatibility.NotLowerableYet,
            bool canLowerToNative = false)
        {
            Kind = kind;
            TemplateId = string.IsNullOrWhiteSpace(templateId)
                ? "esu-" + kind.ToString().ToLowerInvariant()
                : templateId;
            Label = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "info" : iconKey;
            Category = category;
            Description = description ?? string.Empty;
            InputPorts = inputPorts ?? Array.Empty<AutomationBlockPortDefinition>();
            OutputPorts = outputPorts ?? Array.Empty<AutomationBlockPortDefinition>();
            Settings = settings ?? Array.Empty<AutomationBlockSettingDefinition>();
            NativeComponentTypeName = nativeComponentTypeName ?? string.Empty;
            Audience = audience;
            Compatibility = compatibility;
            CanLowerToNative = canLowerToNative;
        }

        internal AutomationBlockKind Kind { get; }

        internal string TemplateId { get; }

        internal string Label { get; }

        internal string IconKey { get; }

        internal AutomationBlockCategory Category { get; }

        internal string Description { get; }

        internal IReadOnlyList<AutomationBlockPortDefinition> InputPorts { get; }

        internal IReadOnlyList<AutomationBlockPortDefinition> OutputPorts { get; }

        internal IReadOnlyList<AutomationBlockSettingDefinition> Settings { get; }

        internal string NativeComponentTypeName { get; }

        internal AutomationBlockAudience Audience { get; }

        internal AutomationBlockCompatibility Compatibility { get; }

        internal bool CanLowerToNative { get; }

        internal bool IsNativeWrapper => Kind == AutomationBlockKind.NativeComponent;
    }

    internal static class AutomationBlockCatalog
    {
        private static readonly AutomationBlockDefinition[] s_semanticDefinitions =
        {
            Definition(
                AutomationBlockKind.SetTarget,
                "set target to",
                "anchor",
                AutomationBlockCategory.Output,
                "Write the incoming value into a linked Generic Setter target.",
                inputs: new[] { "value" },
                outputs: Array.Empty<string>(),
                settings: new[]
                {
                    new AutomationBlockSettingDefinition("Writable target", AutomationBlockSettingKind.Target),
                    new AutomationBlockSettingDefinition("Setter property", AutomationBlockSettingKind.Property)
                },
                compatibility: AutomationBlockCompatibility.NativeViaGetterSetter,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.WhenIf,
                "if value then",
                "risk",
                AutomationBlockCategory.Control,
                "Gate the true value with an if/else switch.",
                inputs: new[] { "condition", "true" },
                outputs: new[] { "out" },
                settings: new[] { new AutomationBlockSettingDefinition("Else", AutomationBlockSettingKind.Number, 0f) },
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.Delay,
                "wait seconds",
                "time",
                AutomationBlockCategory.Timing,
                "Delay a signal in native Breadboard time.",
                inputs: new[] { "in" },
                outputs: new[] { "out" },
                settings: new[] { new AutomationBlockSettingDefinition("Seconds", AutomationBlockSettingKind.Number, 0.2f) },
                audience: AutomationBlockAudience.MetadataOnly,
                compatibility: AutomationBlockCompatibility.NotLowerableYet),
            Definition(
                AutomationBlockKind.ReadTarget,
                "read target",
                "visibility",
                AutomationBlockCategory.Input,
                "Read a value from a linked Generic Getter target.",
                inputs: Array.Empty<string>(),
                outputs: new[] { "value" },
                settings: new[]
                {
                    new AutomationBlockSettingDefinition("Readable target", AutomationBlockSettingKind.Target),
                    new AutomationBlockSettingDefinition("Getter property", AutomationBlockSettingKind.Property)
                },
                compatibility: AutomationBlockCompatibility.NativeViaGetterSetter,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.Compare,
                "compare value",
                "filter",
                AutomationBlockCategory.Control,
                "Compare the input against a threshold.",
                inputs: new[] { "value" },
                outputs: new[] { "condition" },
                settings: new[]
                {
                    new AutomationBlockSettingDefinition("Operator", AutomationBlockSettingKind.Operator),
                    new AutomationBlockSettingDefinition("Threshold", AutomationBlockSettingKind.Number, 0.5f)
                },
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.MathScale,
                "scale value",
                "settings",
                AutomationBlockCategory.Math,
                "Multiply the incoming value by a scale.",
                inputs: new[] { "value" },
                outputs: new[] { "value" },
                settings: new[] { new AutomationBlockSettingDefinition("Scale", AutomationBlockSettingKind.Number, 1f) },
                audience: AutomationBlockAudience.Advanced,
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.MathEvaluator,
                "math evaluator",
                "settings",
                AutomationBlockCategory.Math,
                "Advanced native Maths Evaluator expression block.",
                inputs: new[] { "in" },
                outputs: new[] { "out" },
                settings: new[] { new AutomationBlockSettingDefinition("Expression", AutomationBlockSettingKind.Text) },
                audience: AutomationBlockAudience.Advanced,
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.Switch,
                "switch",
                "filter",
                AutomationBlockCategory.Control,
                "Native Switch-style true/else gate.",
                inputs: new[] { "value", "threshold" },
                outputs: new[] { "out" },
                settings: new[] { new AutomationBlockSettingDefinition("Fail value", AutomationBlockSettingKind.Number, 0f) },
                audience: AutomationBlockAudience.Advanced,
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.Constant,
                "constant value",
                "cube",
                AutomationBlockCategory.Input,
                "Output a numeric constant.",
                inputs: Array.Empty<string>(),
                outputs: new[] { "value" },
                settings: new[] { new AutomationBlockSettingDefinition("Value", AutomationBlockSettingKind.Number, 1f) },
                compatibility: AutomationBlockCompatibility.Native,
                canLowerToNative: true),
            Definition(
                AutomationBlockKind.SystemBlock,
                "system block",
                "duplicate",
                AutomationBlockCategory.Organization,
                "Collapsed reusable ESU block metadata.",
                inputs: new[] { "in" },
                outputs: new[] { "out" },
                settings: Array.Empty<AutomationBlockSettingDefinition>(),
                audience: AutomationBlockAudience.MetadataOnly,
                compatibility: AutomationBlockCompatibility.LayoutTemplateOnly),
            Definition(
                AutomationBlockKind.Comment,
                "note",
                "info",
                AutomationBlockCategory.Organization,
                "Workspace note.",
                inputs: Array.Empty<string>(),
                outputs: Array.Empty<string>(),
                settings: new[] { new AutomationBlockSettingDefinition("Comment", AutomationBlockSettingKind.Text) },
                audience: AutomationBlockAudience.MetadataOnly,
                compatibility: AutomationBlockCompatibility.LayoutTemplateOnly)
        };

        private static readonly AutomationBlockPortDefinition[] s_nativeInputPorts =
        {
            new AutomationBlockPortDefinition("in", AutomationBlockPortDirection.Input)
        };

        private static readonly AutomationBlockPortDefinition[] s_nativeOutputPorts =
        {
            new AutomationBlockPortDefinition("out", AutomationBlockPortDirection.Output)
        };

        internal static IReadOnlyList<AutomationBlockDefinition> SemanticDefinitions => s_semanticDefinitions;

        internal static AutomationBlockDefinition DefinitionFor(AutomationBlockKind kind) =>
            s_semanticDefinitions.FirstOrDefault(definition => definition.Kind == kind) ??
            s_semanticDefinitions.First(definition => definition.Kind == AutomationBlockKind.Comment);

        internal static IReadOnlyList<AutomationBlockDefinition> DefinitionsForCategory(AutomationBlockCategory category) =>
            s_semanticDefinitions
                .Where(definition => definition.Category == category)
                .ToArray();

        internal static AutomationBlockDefinition NativeDefinition(AutomationBreadboardAvailableComponent component)
        {
            string label = string.IsNullOrWhiteSpace(component?.Label)
                ? component?.TypeName ?? "Native component"
                : component.Label;
            string typeName = string.IsNullOrWhiteSpace(component?.FullTypeName)
                ? component?.TypeName ?? string.Empty
                : component.FullTypeName;
            return new AutomationBlockDefinition(
                AutomationBlockKind.NativeComponent,
                "native-" + AutomationBlockLowering.IdentifierForLabel(label),
                label,
                "duplicate",
                AutomationBlockCategory.Advanced,
                component?.Description ?? "Native Breadboard component wrapper.",
                new[] { new AutomationBlockPortDefinition("in", AutomationBlockPortDirection.Input) },
                new[] { new AutomationBlockPortDefinition("out", AutomationBlockPortDirection.Output) },
                new[] { new AutomationBlockSettingDefinition("Native type", AutomationBlockSettingKind.NativeComponentType) },
                typeName,
                AutomationBlockAudience.NativeWrapper,
                AutomationBlockCompatibility.Native,
                true);
        }

        internal static IReadOnlyList<AutomationBlockPortDefinition> InputPortsForNode(AutomationBlockNode node) =>
            node?.Kind == AutomationBlockKind.NativeComponent
                ? NativePortsForNode(node, AutomationBlockPortDirection.Input)
                : DefinitionFor(node == null ? AutomationBlockKind.Comment : node.Kind).InputPorts;

        internal static IReadOnlyList<AutomationBlockPortDefinition> OutputPortsForNode(AutomationBlockNode node) =>
            node?.Kind == AutomationBlockKind.NativeComponent
                ? NativePortsForNode(node, AutomationBlockPortDirection.Output)
                : DefinitionFor(node == null ? AutomationBlockKind.Comment : node.Kind).OutputPorts;

        private static IReadOnlyList<AutomationBlockPortDefinition> NativePortsForNode(
            AutomationBlockNode node,
            AutomationBlockPortDirection direction)
        {
            IReadOnlyList<string> labels = direction == AutomationBlockPortDirection.Input
                ? node?.NativeInputPortLabels
                : node?.NativeOutputPortLabels;
            if (labels != null && labels.Count > 0)
            {
                return labels
                    .Select(label => new AutomationBlockPortDefinition(label, direction))
                    .ToArray();
            }

            if (node?.NativeImported == true)
                return Array.Empty<AutomationBlockPortDefinition>();

            return direction == AutomationBlockPortDirection.Input
                ? s_nativeInputPorts
                : s_nativeOutputPorts;
        }

        internal static AutomationBlockDefinition DefinitionForNode(AutomationBlockNode node)
        {
            if (node?.Kind != AutomationBlockKind.NativeComponent)
                return DefinitionFor(node == null ? AutomationBlockKind.Comment : node.Kind);

            return new AutomationBlockDefinition(
                AutomationBlockKind.NativeComponent,
                string.IsNullOrWhiteSpace(node.PaletteTemplateId)
                    ? "native-component"
                    : node.PaletteTemplateId,
                string.IsNullOrWhiteSpace(node.NativeComponentLabel)
                    ? node.Label
                    : node.NativeComponentLabel,
                node.IconKey,
                AutomationBlockCategory.Advanced,
                node.NativeComponentDescription,
                InputPortsForNode(node),
                OutputPortsForNode(node),
                new[] { new AutomationBlockSettingDefinition("Native type", AutomationBlockSettingKind.NativeComponentType) },
                node.NativeComponentTypeName,
                AutomationBlockAudience.NativeWrapper,
                AutomationBlockCompatibility.Native,
                !string.IsNullOrWhiteSpace(node.NativeComponentTypeName));
        }

        private static AutomationBlockDefinition Definition(
            AutomationBlockKind kind,
            string label,
            string iconKey,
            AutomationBlockCategory category,
            string description,
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs,
            IReadOnlyList<AutomationBlockSettingDefinition> settings,
            AutomationBlockAudience audience = AutomationBlockAudience.Beginner,
            AutomationBlockCompatibility compatibility = AutomationBlockCompatibility.NotLowerableYet,
            bool canLowerToNative = false)
        {
            return new AutomationBlockDefinition(
                kind,
                "esu-" + kind.ToString().ToLowerInvariant(),
                label,
                iconKey,
                category,
                description,
                (inputs ?? Array.Empty<string>())
                    .Select(name => new AutomationBlockPortDefinition(name, AutomationBlockPortDirection.Input))
                    .ToArray(),
                (outputs ?? Array.Empty<string>())
                    .Select(name => new AutomationBlockPortDefinition(name, AutomationBlockPortDirection.Output))
                    .ToArray(),
                settings,
                audience: audience,
                compatibility: compatibility,
                canLowerToNative: canLowerToNative);
        }
    }

    internal sealed class AutomationBlockWorkspace
    {
        private const float NativeExactLayoutCanvasWidth = 900f;
        private const float NativeExactLayoutCanvasHeight = 520f;
        private const float NativeExactLayoutPadding = 26f;
        private const float NativeExactMinimumExtentX = 220f;
        private const float NativeExactMinimumExtentY = 140f;

        private readonly List<AutomationBlockNode> _nodes = new List<AutomationBlockNode>();
        private readonly List<AutomationBlockPort> _ports = new List<AutomationBlockPort>();
        private readonly List<AutomationBlockLink> _links = new List<AutomationBlockLink>();
        private readonly List<AutomationBlockLink> _explicitLinks = new List<AutomationBlockLink>();
        private IReadOnlyList<AutomationTarget> _linkedTargets = Array.Empty<AutomationTarget>();
        private int _nextNodeIndex = 1;

        private AutomationBlockWorkspace(string controllerKey, string controllerLabel)
        {
            ControllerKey = controllerKey ?? string.Empty;
            ControllerLabel = string.IsNullOrWhiteSpace(controllerLabel)
                ? "Automation controller"
                : controllerLabel;
        }

        internal string ControllerKey { get; }

        internal string ControllerLabel { get; }

        internal AutomationBlockWorkspaceMode Mode { get; private set; }

        internal string NativeImportStatus { get; private set; } = string.Empty;

        internal string SelectedNodeId { get; private set; }

        internal IReadOnlyList<AutomationBlockNode> Nodes => _nodes;

        internal IReadOnlyList<AutomationBlockNode> ExecutableNodes =>
            _nodes
                .Where(node => node != null && node.SnappedToStack)
                .OrderBy(node => node.CanvasOrder)
                .ToArray();

        internal IReadOnlyList<AutomationBlockPort> Ports => _ports;

        internal IReadOnlyList<AutomationBlockLink> Links => _links;

        internal IReadOnlyList<AutomationTarget> LinkedTargets => _linkedTargets;

        internal AutomationBlockCanvasPosition CanvasPan { get; private set; }

        internal float CanvasZoom { get; private set; } = 1f;

        internal float NativeDisplayScale { get; private set; } = 1f;

        internal static AutomationBlockWorkspace CreateDefault(
            string controllerKey,
            string controllerLabel,
            IReadOnlyList<AutomationTarget> linkedTargets)
        {
            var workspace = new AutomationBlockWorkspace(controllerKey, controllerLabel);
            workspace.ReplaceTargets(linkedTargets);
            return workspace;
        }

        internal static AutomationBlockWorkspace FromNativeGraphSnapshot(
            string controllerKey,
            string controllerLabel,
            AutomationNativeGraphSnapshot snapshot)
        {
            var workspace = new AutomationBlockWorkspace(controllerKey, controllerLabel)
            {
                Mode = AutomationBlockWorkspaceMode.NativeExact,
                CanvasPan = new AutomationBlockCanvasPosition(0f, 0f),
                CanvasZoom = 1f,
                NativeImportStatus = snapshot == null
                    ? "Native exact import found no Breadboard graph."
                    : "Imported from vanilla Breadboard: " +
                      snapshot.Components.Count.ToString(CultureInfo.InvariantCulture) +
                      " component(s), " +
                      snapshot.Wires.Count.ToString(CultureInfo.InvariantCulture) +
                      " wire(s)."
            };

            if (snapshot == null)
                return workspace;

            workspace.NativeDisplayScale = NativeExactLayoutScale(snapshot.Components);
            int order = 0;
            foreach (AutomationNativeComponentSnapshot component in snapshot.Components)
            {
                if (component == null)
                    continue;

                string id = NativeNodeId(component);
                var node = new AutomationBlockNode(
                    id,
                    AutomationBlockKind.NativeComponent,
                    component.Label,
                    "duplicate",
                    AutomationBlockCategory.Advanced,
                    "native-imported-" + AutomationBlockLowering.IdentifierForLabel(component.Label));
                node.SnappedToStack = false;
                node.CanvasOrder = order++;
                node.CanvasPosition = NativeExactDisplayPosition(component, snapshot.Components, workspace.NativeDisplayScale);
                node.SetNativeComponent(
                    component.TypeName,
                    component.Label,
                    component.Description);
                node.SetNativeIdentity(
                    component.ComponentId,
                    component.ComponentTypeId.ToString("D"),
                    component.Fingerprint,
                    imported: true,
                    esuOwned: false,
                    component.X,
                    component.Y,
                    component.Width,
                    component.Height,
                    component.SettingsSummary,
                    component.Inputs.Select(port => port.Label).ToArray(),
                    component.Outputs.Select(port => port.Label).ToArray(),
                    component.BlockTypeName,
                    component.BlockFilter);
                workspace._nodes.Add(node);
            }

            Dictionary<uint, AutomationBlockNode> nodesByNativeId = workspace._nodes
                .Where(node => node.NativeComponentId != 0U)
                .GroupBy(node => node.NativeComponentId)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (AutomationNativeWireSnapshot wire in snapshot.Wires)
            {
                if (wire == null ||
                    !nodesByNativeId.TryGetValue(wire.FromComponentId, out AutomationBlockNode fromNode) ||
                    !nodesByNativeId.TryGetValue(wire.ToComponentId, out AutomationBlockNode toNode))
                {
                    continue;
                }

                workspace._explicitLinks.Add(new AutomationBlockLink(
                    fromNode.Id,
                    NativePortId(fromNode.Id, AutomationBlockPortDirection.Output, wire.FromOutputIndex),
                    toNode.Id,
                    NativePortId(toNode.Id, AutomationBlockPortDirection.Input, wire.ToInputIndex),
                    wire.FromComponentId,
                    wire.FromOutputIndex,
                    wire.ToComponentId,
                    wire.ToInputIndex));
            }

            workspace._nextNodeIndex = Math.Max(workspace._nodes.Count + 1, 1);
            workspace.Select(workspace._nodes.FirstOrDefault()?.Id ?? string.Empty);
            workspace.RebuildPortsAndLinks();
            return workspace;
        }

        internal static AutomationBlockWorkspace FromProfileData(
            string controllerKey,
            string controllerLabel,
            IReadOnlyList<AutomationTarget> linkedTargets,
            SerializationHudProfile.AutomationBlockWorkspaceData data)
        {
            if (data == null)
                return CreateDefault(controllerKey, controllerLabel, linkedTargets);

            var workspace = new AutomationBlockWorkspace(controllerKey, controllerLabel);
            workspace.Mode = Enum.IsDefined(typeof(AutomationBlockWorkspaceMode), data.Mode)
                ? (AutomationBlockWorkspaceMode)data.Mode
                : AutomationBlockWorkspaceMode.Semantic;
            workspace.NativeImportStatus = data.NativeImportStatus ?? string.Empty;
            workspace._linkedTargets = linkedTargets == null
                ? Array.Empty<AutomationTarget>()
                : linkedTargets.Where(target => target != null).ToArray();
            workspace.CanvasPan = new AutomationBlockCanvasPosition(data.CanvasPanX, data.CanvasPanY);
            workspace.CanvasZoom = Math.Max(0.45f, Math.Min(1.85f, data.CanvasZoom <= 0f ? 1f : data.CanvasZoom));
            workspace.NativeDisplayScale = Math.Max(0.05f, Math.Min(4f, data.NativeDisplayScale <= 0f ? 1f : data.NativeDisplayScale));

            int maxNodeIndex = 0;
            foreach (SerializationHudProfile.AutomationBlockNodeData nodeData in data.Nodes ?? new List<SerializationHudProfile.AutomationBlockNodeData>())
            {
                if (nodeData == null)
                    continue;

                if (!Enum.IsDefined(typeof(AutomationBlockKind), nodeData.Kind))
                    continue;

                var kind = (AutomationBlockKind)nodeData.Kind;
                AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionFor(kind);
                string id = string.IsNullOrWhiteSpace(nodeData.Id)
                    ? "esu-block-" + workspace._nextNodeIndex.ToString(CultureInfo.InvariantCulture)
                    : nodeData.Id;
                maxNodeIndex = Math.Max(maxNodeIndex, ExtractNodeIndex(id));
                var node = new AutomationBlockNode(
                    id,
                    kind,
                    string.IsNullOrWhiteSpace(nodeData.Label) ? definition.Label : nodeData.Label,
                    string.IsNullOrWhiteSpace(nodeData.IconKey) ? definition.IconKey : nodeData.IconKey,
                    Enum.IsDefined(typeof(AutomationBlockCategory), nodeData.Category)
                        ? (AutomationBlockCategory)nodeData.Category
                        : definition.Category,
                    string.IsNullOrWhiteSpace(nodeData.PaletteTemplateId)
                        ? definition.TemplateId
                        : nodeData.PaletteTemplateId);

                node.ParentNodeId = nodeData.ParentNodeId ?? string.Empty;
                node.CanvasOrder = nodeData.CanvasOrder;
                node.CanvasPosition = new AutomationBlockCanvasPosition(nodeData.CanvasX, nodeData.CanvasY);
                node.SnappedToStack = nodeData.SnappedToStack;
                node.LinkDirection = Enum.IsDefined(typeof(AutomationLinkDirection), nodeData.LinkDirection)
                    ? (AutomationLinkDirection)nodeData.LinkDirection
                    : AutomationLinkDirection.Output;
                node.Operator = Enum.IsDefined(typeof(AutomationCompareOperator), nodeData.Operator)
                    ? (AutomationCompareOperator)nodeData.Operator
                    : AutomationCompareOperator.GreaterThan;
                node.NumericValue = nodeData.NumericValue;
                node.SecondaryNumericValue = nodeData.SecondaryNumericValue;
                node.Comment = nodeData.Comment ?? string.Empty;
                node.Expression = nodeData.Expression ?? string.Empty;

                AutomationTarget target = ResolveStoredTarget(
                    workspace._linkedTargets,
                    nodeData.TargetKey,
                    nodeData.TargetPersistenceKey);
                if (target != null)
                    node.RebindTarget(target);
                else
                    node.RestoreTarget(
                        nodeData.TargetKey,
                        nodeData.TargetPersistenceKey,
                        nodeData.TargetLabel);

                AutomationProxyPropertySelection selection = ProfileToPropertySelection(nodeData.PropertySelection);
                if (selection != null)
                    node.SelectProperty(selection);

                if (kind == AutomationBlockKind.NativeComponent)
                {
                    node.SetNativeComponent(
                        nodeData.NativeComponentTypeName,
                        nodeData.NativeComponentLabel,
                        nodeData.NativeComponentDescription);
                    node.SetNativeIdentity(
                        nodeData.NativeComponentId,
                        nodeData.NativeComponentTypeId,
                        nodeData.NativeComponentFingerprint,
                        nodeData.NativeImported,
                        nodeData.NativeEsuOwned,
                        nodeData.NativeX,
                        nodeData.NativeY,
                        nodeData.NativeWidth,
                        nodeData.NativeHeight,
                        nodeData.NativeSettingsSummary,
                        nodeData.NativeInputPortLabels ?? new List<string>(),
                        nodeData.NativeOutputPortLabels ?? new List<string>(),
                        nodeData.NativeBlockTypeName,
                        nodeData.NativeBlockFilter);
                }

                workspace._nodes.Add(node);
            }

            foreach (SerializationHudProfile.AutomationBlockLinkData linkData in data.Links ?? new List<SerializationHudProfile.AutomationBlockLinkData>())
            {
                if (linkData == null)
                    continue;

                workspace._explicitLinks.Add(new AutomationBlockLink(
                    linkData.FromNodeId,
                    linkData.FromPortId,
                    linkData.ToNodeId,
                    linkData.ToPortId,
                    linkData.FromNativeComponentId,
                    linkData.FromNativePortIndex,
                    linkData.ToNativeComponentId,
                    linkData.ToNativePortIndex));
            }

            workspace._nextNodeIndex = Math.Max(maxNodeIndex + 1, workspace._nodes.Count + 1);
            workspace.Select(workspace._nodes.Any(node => string.Equals(node.Id, data.SelectedNodeId, StringComparison.Ordinal))
                ? data.SelectedNodeId
                : workspace._nodes.FirstOrDefault()?.Id ?? string.Empty);
            workspace.RebuildPortsAndLinks();
            return workspace;
        }

        internal SerializationHudProfile.AutomationBlockWorkspaceData ToProfileData()
        {
            return new SerializationHudProfile.AutomationBlockWorkspaceData
            {
                Mode = (int)Mode,
                CanvasPanX = CanvasPan.X,
                CanvasPanY = CanvasPan.Y,
                CanvasZoom = CanvasZoom,
                NativeDisplayScale = NativeDisplayScale,
                NativeImportStatus = NativeImportStatus,
                SelectedNodeId = SelectedNodeId,
                Nodes = _nodes
                    .Where(node => node != null)
                    .Select(NodeToProfileData)
                    .ToList(),
                Links = _explicitLinks
                    .Where(link => link != null)
                    .Select(LinkToProfileData)
                    .ToList()
            };
        }

        private static SerializationHudProfile.AutomationBlockNodeData NodeToProfileData(
            AutomationBlockNode node)
        {
            return new SerializationHudProfile.AutomationBlockNodeData
            {
                Id = node.Id,
                Kind = (int)node.Kind,
                Label = node.Label,
                IconKey = node.IconKey,
                Category = (int)node.Category,
                PaletteTemplateId = node.PaletteTemplateId,
                ParentNodeId = node.ParentNodeId,
                CanvasOrder = node.CanvasOrder,
                CanvasX = node.CanvasPosition.X,
                CanvasY = node.CanvasPosition.Y,
                SnappedToStack = node.SnappedToStack,
                TargetKey = node.TargetKey,
                TargetPersistenceKey = node.TargetPersistenceKey,
                TargetLabel = node.TargetLabel,
                LinkDirection = (int)node.LinkDirection,
                Operator = (int)node.Operator,
                NumericValue = node.NumericValue,
                SecondaryNumericValue = node.SecondaryNumericValue,
                Comment = node.Comment,
                Expression = node.Expression,
                PropertySelection = PropertySelectionToProfileData(node.PropertySelection),
                NativeComponentTypeName = node.NativeComponentTypeName,
                NativeComponentLabel = node.NativeComponentLabel,
                NativeComponentDescription = node.NativeComponentDescription,
                NativeBlockTypeName = node.NativeBlockTypeName,
                NativeBlockFilter = node.NativeBlockFilter,
                NativeComponentId = node.NativeComponentId,
                NativeComponentTypeId = node.NativeComponentTypeId,
                NativeComponentFingerprint = node.NativeComponentFingerprint,
                NativeImported = node.NativeImported,
                NativeEsuOwned = node.NativeEsuOwned,
                NativeX = node.NativeX,
                NativeY = node.NativeY,
                NativeWidth = node.NativeWidth,
                NativeHeight = node.NativeHeight,
                NativeSettingsSummary = node.NativeSettingsSummary,
                NativeInputPortLabels = node.NativeInputPortLabels.ToList(),
                NativeOutputPortLabels = node.NativeOutputPortLabels.ToList()
            };
        }

        private static SerializationHudProfile.AutomationBlockLinkData LinkToProfileData(
            AutomationBlockLink link)
        {
            return new SerializationHudProfile.AutomationBlockLinkData
            {
                FromNodeId = link.FromNodeId,
                FromPortId = link.FromPortId,
                ToNodeId = link.ToNodeId,
                ToPortId = link.ToPortId,
                FromNativeComponentId = link.FromNativeComponentId,
                FromNativePortIndex = link.FromNativePortIndex,
                ToNativeComponentId = link.ToNativeComponentId,
                ToNativePortIndex = link.ToNativePortIndex
            };
        }

        private static SerializationHudProfile.AutomationProxyPropertySelectionData PropertySelectionToProfileData(
            AutomationProxyPropertySelection selection)
        {
            if (selection == null)
                return null;

            return new SerializationHudProfile.AutomationProxyPropertySelectionData
            {
                Label = selection.Label,
                Tooltip = selection.Tooltip,
                IsGetter = selection.IsGetter,
                IsClear = selection.IsClear,
                IsGetterReadable = selection.IsGetterReadable,
                ReadableAttributeId = selection.ReadableAttributeId,
                BlockPropertyId = selection.BlockPropertyId,
                BlockSetId = selection.BlockSetId
            };
        }

        private static AutomationProxyPropertySelection ProfileToPropertySelection(
            SerializationHudProfile.AutomationProxyPropertySelectionData data)
        {
            return data == null
                ? null
                : new AutomationProxyPropertySelection(
                    data.Label,
                    data.Tooltip,
                    data.IsGetter,
                    data.IsClear,
                    data.IsGetterReadable,
                    data.ReadableAttributeId,
                    data.BlockPropertyId,
                    data.BlockSetId);
        }

        private static AutomationTarget ResolveStoredTarget(
            IReadOnlyList<AutomationTarget> targets,
            string stableKey,
            string persistenceKey)
        {
            if (targets == null || targets.Count == 0)
                return null;

            AutomationTarget target = targets.FirstOrDefault(item =>
                string.Equals(item.StableKey, stableKey, StringComparison.Ordinal));
            if (target != null || string.IsNullOrWhiteSpace(persistenceKey))
                return target;

            AutomationTarget[] matches = targets
                .Where(item => string.Equals(item.PersistenceKey, persistenceKey, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static int ExtractNodeIndex(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return 0;

            int index = id.Length - 1;
            while (index >= 0 && char.IsDigit(id[index]))
                index--;
            if (index == id.Length - 1)
                return 0;

            return int.TryParse(
                id.Substring(index + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
                ? parsed
                : 0;
        }

        private static string NativeNodeId(AutomationNativeComponentSnapshot component)
        {
            if (component == null)
                return "native-component";

            if (component.ComponentId != 0U)
                return "native-component-" + component.ComponentId.ToString(CultureInfo.InvariantCulture);

            return "native-component-" + AutomationBlockLowering.IdentifierForLabel(component.Fingerprint);
        }

        private static string NativePortId(
            string nodeId,
            AutomationBlockPortDirection direction,
            int index)
        {
            return (nodeId ?? string.Empty) +
                   "." +
                   (direction == AutomationBlockPortDirection.Output ? "out" : "in") +
                   "." +
                   Math.Max(0, index).ToString(CultureInfo.InvariantCulture);
        }

        private static AutomationBlockCanvasPosition NativeExactDisplayPosition(
            AutomationNativeComponentSnapshot component,
            IReadOnlyList<AutomationNativeComponentSnapshot> components,
            float scale)
        {
            if (component == null)
                return new AutomationBlockCanvasPosition(NativeExactLayoutPadding, NativeExactLayoutPadding);

            NativeExactLayoutBounds(
                components,
                out float minX,
                out float minY,
                out float extentX,
                out float extentY);
            scale = Math.Max(0.05f, scale <= 0f ? 1f : scale);
            float contentWidth = extentX * scale;
            float contentHeight = extentY * scale;
            float originX =
                NativeExactLayoutPadding +
                Math.Max(0f, NativeExactLayoutCanvasWidth - NativeExactLayoutPadding * 2f - contentWidth) * 0.5f -
                minX * scale;
            float originY =
                NativeExactLayoutPadding +
                Math.Max(0f, NativeExactLayoutCanvasHeight - NativeExactLayoutPadding * 2f - contentHeight) * 0.5f -
                minY * scale;
            return new AutomationBlockCanvasPosition(
                originX + component.X * scale,
                originY + component.Y * scale);
        }

        private static float NativeExactLayoutScale(
            IReadOnlyList<AutomationNativeComponentSnapshot> components)
        {
            NativeExactLayoutBounds(
                components,
                out float unusedMinX,
                out float unusedMinY,
                out float extentX,
                out float extentY);
            float scaleX = (NativeExactLayoutCanvasWidth - NativeExactLayoutPadding * 2f) / Math.Max(1f, extentX);
            float scaleY = (NativeExactLayoutCanvasHeight - NativeExactLayoutPadding * 2f) / Math.Max(1f, extentY);
            float scale = Math.Min(scaleX, scaleY);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.01f)
                scale = 1f;

            return Math.Max(0.55f, Math.Min(1.25f, scale));
        }

        private static void NativeExactLayoutBounds(
            IReadOnlyList<AutomationNativeComponentSnapshot> components,
            out float minX,
            out float minY,
            out float extentX,
            out float extentY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (AutomationNativeComponentSnapshot component in components ?? Array.Empty<AutomationNativeComponentSnapshot>())
            {
                if (component == null)
                    continue;

                float width = NativeExactDisplayWidth(component.Width);
                float height = NativeExactDisplayHeight(component.Height, component.Inputs.Count, component.Outputs.Count);
                minX = Math.Min(minX, component.X);
                minY = Math.Min(minY, component.Y);
                maxX = Math.Max(maxX, component.X + width);
                maxY = Math.Max(maxY, component.Y + height);
            }

            if (minX == float.MaxValue)
            {
                minX = 0f;
                minY = 0f;
                maxX = NativeExactMinimumExtentX;
                maxY = NativeExactMinimumExtentY;
            }

            extentX = Math.Max(NativeExactMinimumExtentX, maxX - minX);
            extentY = Math.Max(NativeExactMinimumExtentY, maxY - minY);
        }

        private static float NativeExactDisplayWidth(float nativeWidth) =>
            ClampFloat(nativeWidth > 20f ? nativeWidth : 150f, 130f, 260f);

        private static float NativeExactDisplayHeight(
            float nativeHeight,
            int inputCount,
            int outputCount)
        {
            float portRows = Math.Max(inputCount, outputCount);
            float fallback = Math.Max(78f, 48f + portRows * 13f);
            return ClampFloat(nativeHeight > 20f ? nativeHeight : fallback, 62f, 180f);
        }

        private static float ClampFloat(float value, float min, float max) =>
            Math.Max(min, Math.Min(max, value));

        internal bool TryApplyAmmoThresholdStarterTemplate(
            AutomationTarget inputTarget,
            AutomationTarget outputTarget)
        {
            if (inputTarget == null ||
                outputTarget == null ||
                inputTarget.Category != AutomationTargetCategory.TurretsWeapons ||
                outputTarget.Category != AutomationTargetCategory.Spinblocks)
            {
                return false;
            }

            AutomationBlockNode read = EnsureStarterNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode when = EnsureStarterNode(AutomationBlockKind.WhenIf);
            AutomationBlockNode compare = EnsureStarterNode(AutomationBlockKind.Compare);
            AutomationBlockNode constant = EnsureStarterNode(AutomationBlockKind.Constant);
            AutomationBlockNode set = EnsureStarterNode(AutomationBlockKind.SetTarget);
            AutomationBlockNode comment = EnsureStarterNode(AutomationBlockKind.Comment);

            SetNodeTarget(read.Id, inputTarget, AutomationLinkDirection.Input);
            SetNodeTarget(set.Id, outputTarget, AutomationLinkDirection.Output);
            when.SecondaryNumericValue = 0f;
            compare.Operator = AutomationCompareOperator.LessThan;
            compare.NumericValue = 10f;
            constant.NumericValue = 45f;
            if (comment != null)
                comment.Comment = "APS ammo < 10 sets spinblock angle to 45, else 0.";
            RebuildPortsAndLinks();
            return true;
        }

        internal void ReplaceTargets(IReadOnlyList<AutomationTarget> linkedTargets)
        {
            _linkedTargets = linkedTargets == null
                ? Array.Empty<AutomationTarget>()
                : linkedTargets.Where(target => target != null).ToArray();

            foreach (AutomationBlockNode node in _nodes)
            {
                if (string.IsNullOrWhiteSpace(node.TargetKey))
                    continue;

                AutomationTarget target = ResolveStoredTarget(
                    _linkedTargets,
                    node.TargetKey,
                    node.TargetPersistenceKey);
                if (target != null)
                    node.RebindTarget(target);
            }

            RebuildPortsAndLinks();
        }

        internal AutomationBlockNode AddBlock(AutomationBlockKind kind) =>
            AddBlock(kind, ExecutableNodes.Count);

        internal AutomationBlockNode AddBlock(AutomationBlockKind kind, int insertIndex)
        {
            AutomationBlockNode node = CreateConfiguredBlock(kind);
            InsertNodeIntoStack(node, insertIndex);
            Select(node.Id);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return node;
        }

        internal AutomationBlockNode AddBlockAt(
            AutomationBlockKind kind,
            AutomationBlockCanvasPosition canvasPosition,
            bool snappedToStack)
        {
            AutomationBlockNode node = CreateConfiguredBlock(kind);
            if (snappedToStack)
            {
                InsertNodeIntoStack(node, ExecutableNodes.Count);
            }
            else
            {
                node.SnappedToStack = false;
                node.CanvasPosition = canvasPosition;
                node.CanvasOrder = _nodes.Count;
                _nodes.Add(node);
            }
            Select(node.Id);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return node;
        }

        internal AutomationBlockNode AddNativeComponentBlock(
            AutomationBreadboardAvailableComponent component,
            int insertIndex)
        {
            AutomationBlockDefinition definition = AutomationBlockCatalog.NativeDefinition(component);
            var node = new AutomationBlockNode(
                "esu-block-" + _nextNodeIndex.ToString(CultureInfo.InvariantCulture),
                AutomationBlockKind.NativeComponent,
                definition.Label,
                definition.IconKey,
                definition.Category,
                definition.TemplateId);
            _nextNodeIndex++;
            node.SetNativeComponent(
                definition.NativeComponentTypeName,
                definition.Label,
                component?.Description ?? string.Empty);

            InsertNodeIntoStack(node, insertIndex);
            Select(node.Id);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return node;
        }

        internal AutomationBlockNode AddNativeComponentBlockAt(
            AutomationBreadboardAvailableComponent component,
            AutomationBlockCanvasPosition canvasPosition,
            bool snappedToStack)
        {
            AutomationBlockDefinition definition = AutomationBlockCatalog.NativeDefinition(component);
            var node = new AutomationBlockNode(
                "esu-block-" + _nextNodeIndex.ToString(CultureInfo.InvariantCulture),
                AutomationBlockKind.NativeComponent,
                definition.Label,
                definition.IconKey,
                definition.Category,
                definition.TemplateId);
            _nextNodeIndex++;
            node.SetNativeComponent(
                definition.NativeComponentTypeName,
                definition.Label,
                component?.Description ?? string.Empty);

            if (snappedToStack)
                InsertNodeIntoStack(node, ExecutableNodes.Count);
            else
            {
                node.SnappedToStack = false;
                node.CanvasPosition = canvasPosition;
                node.CanvasOrder = _nodes.Count;
                _nodes.Add(node);
            }

            Select(node.Id);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return node;
        }

        internal void SetCanvasPan(float x, float y)
        {
            CanvasPan = new AutomationBlockCanvasPosition(x, y);
        }

        internal void AddCanvasPan(float x, float y)
        {
            CanvasPan = new AutomationBlockCanvasPosition(CanvasPan.X + x, CanvasPan.Y + y);
        }

        internal void SetCanvasZoom(float zoom)
        {
            CanvasZoom = Math.Max(0.45f, Math.Min(1.85f, zoom));
        }

        internal bool RemoveSelected()
        {
            if (string.IsNullOrWhiteSpace(SelectedNodeId))
                return false;

            string removedNodeId = SelectedNodeId;
            int removed = _nodes.RemoveAll(node =>
                string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));
            if (removed == 0)
                return false;

            _explicitLinks.RemoveAll(link =>
                string.Equals(link.FromNodeId, removedNodeId, StringComparison.Ordinal) ||
                string.Equals(link.ToNodeId, removedNodeId, StringComparison.Ordinal));
            SelectedNodeId = _nodes.Count == 0 ? string.Empty : _nodes[Math.Max(0, _nodes.Count - 1)].Id;
            RebuildPortsAndLinks();
            return true;
        }

        internal bool MoveSelected(int delta)
        {
            if (delta == 0 || string.IsNullOrWhiteSpace(SelectedNodeId))
                return false;

            AutomationBlockNode selected = _nodes.FirstOrDefault(node =>
                string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));
            if (selected == null || !selected.SnappedToStack)
                return false;

            AutomationBlockNode[] executable = ExecutableNodes.ToArray();
            int index = Array.FindIndex(executable, node =>
                string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));
            if (index < 0)
                return false;
            int next = Math.Max(0, Math.Min(executable.Length - 1, index + delta));
            if (next == index)
                return false;

            return MoveNodeToIndex(SelectedNodeId, next);
        }

        internal bool MoveNodeToIndex(string nodeId, int insertIndex)
        {
            if (Mode == AutomationBlockWorkspaceMode.NativeExact)
                return false;

            if (string.IsNullOrWhiteSpace(nodeId))
                return false;

            int index = _nodes.FindIndex(node =>
                string.Equals(node.Id, nodeId, StringComparison.Ordinal));
            if (index < 0)
                return false;

            AutomationBlockNode nodeToMove = _nodes[index];
            _nodes.RemoveAt(index);
            InsertNodeIntoStack(nodeToMove, insertIndex);
            Select(nodeId);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return true;
        }

        internal bool MoveNodeToCanvas(
            string nodeId,
            AutomationBlockCanvasPosition canvasPosition)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return false;

            AutomationBlockNode node = _nodes.FirstOrDefault(item =>
                string.Equals(item.Id, nodeId, StringComparison.Ordinal));
            if (node == null)
                return false;

            node.SnappedToStack = false;
            node.CanvasPosition = canvasPosition;
            Select(nodeId);
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return true;
        }

        internal bool SetNodeExecutionStackState(
            string nodeId,
            bool snappedToStack)
        {
            if (Mode == AutomationBlockWorkspaceMode.NativeExact && snappedToStack)
                return false;

            if (string.IsNullOrWhiteSpace(nodeId))
                return false;

            AutomationBlockNode node = _nodes.FirstOrDefault(item =>
                string.Equals(item.Id, nodeId, StringComparison.Ordinal));
            if (node == null || node.SnappedToStack == snappedToStack)
                return false;

            if (snappedToStack)
                return MoveNodeToIndex(nodeId, ExecutableNodes.Count);

            node.SnappedToStack = false;
            RefreshCanvasOrder();
            RebuildPortsAndLinks();
            return true;
        }

        internal void Select(string nodeId)
        {
            SelectedNodeId = nodeId ?? string.Empty;
            foreach (AutomationBlockNode node in _nodes)
                node.IsSelected = string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal);
        }

        internal AutomationBlockNode SelectedNode() =>
            _nodes.FirstOrDefault(node => string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));

        internal AutomationBlockNode FirstNode(AutomationBlockKind kind) =>
            _nodes.FirstOrDefault(node => node.Kind == kind);

        internal AutomationBlockNode FirstExecutableNode(AutomationBlockKind kind) =>
            ExecutableNodes.FirstOrDefault(node => node.Kind == kind);

        internal bool SetNodeTarget(
            string nodeId,
            AutomationTarget target,
            AutomationLinkDirection direction)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || target == null)
                return false;

            AutomationBlockNode node = _nodes.FirstOrDefault(item =>
                string.Equals(item.Id, nodeId, StringComparison.Ordinal));
            if (node == null)
                return false;

            node.LinkDirection = direction;
            node.SetTarget(target);
            RebuildPortsAndLinks();
            return true;
        }

        internal bool RebindNodeTarget(
            string nodeId,
            AutomationTarget target)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || target == null)
                return false;

            AutomationBlockNode node = _nodes.FirstOrDefault(item =>
                string.Equals(item.Id, nodeId, StringComparison.Ordinal));
            if (node == null)
                return false;

            node.RebindTarget(target);
            RebuildPortsAndLinks();
            return true;
        }

        internal AutomationTarget TargetForNode(AutomationBlockNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.TargetKey))
                return null;

            return _linkedTargets.FirstOrDefault(target =>
                string.Equals(target.StableKey, node.TargetKey, StringComparison.Ordinal));
        }

        internal IReadOnlyList<AutomationBlockNode> NativeComponentNodes() =>
            ExecutableNodes
                .Where(node => node.Kind == AutomationBlockKind.NativeComponent &&
                               !string.IsNullOrWhiteSpace(node.NativeComponentTypeName))
                .ToArray();

        internal bool HasNativeComponentRequests =>
            NativeComponentNodes().Count > 0;

        internal bool HasConfiguredSemanticFlow
        {
            get
            {
                AutomationBlockNode read = FirstExecutableNode(AutomationBlockKind.ReadTarget);
                AutomationBlockNode compare = FirstExecutableNode(AutomationBlockKind.Compare);
                AutomationBlockNode set = FirstExecutableNode(AutomationBlockKind.SetTarget);
                return read != null &&
                       compare != null &&
                       set != null &&
                       !string.IsNullOrWhiteSpace(read.TargetKey) &&
                       !string.IsNullOrWhiteSpace(set.TargetKey);
            }
        }

        internal bool HasConfiguredDirectReadSetFlow
        {
            get
            {
                AutomationBlockNode read = FirstExecutableNode(AutomationBlockKind.ReadTarget);
                AutomationBlockNode set = FirstExecutableNode(AutomationBlockKind.SetTarget);
                return read != null &&
                       set != null &&
                       FirstExecutableNode(AutomationBlockKind.Compare) == null &&
                       FirstExecutableNode(AutomationBlockKind.WhenIf) == null &&
                       FirstExecutableNode(AutomationBlockKind.Switch) == null &&
                       FirstExecutableNode(AutomationBlockKind.MathScale) == null &&
                       FirstExecutableNode(AutomationBlockKind.MathEvaluator) == null &&
                       FirstExecutableNode(AutomationBlockKind.Constant) == null &&
                       !string.IsNullOrWhiteSpace(read.TargetKey) &&
                       !string.IsNullOrWhiteSpace(set.TargetKey) &&
                       HasLink(read, "value", set, "value");
            }
        }

        internal AutomationSystemBlockDefinition CollapseSelectionToSystemBlock(string name)
        {
            AutomationBlockNode[] executable = ExecutableNodes.ToArray();
            AutomationBlockNode[] selected = string.IsNullOrWhiteSpace(SelectedNodeId)
                ? executable
                : executable.Where(node => string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal)).ToArray();
            if (selected.Length == 0)
                selected = string.IsNullOrWhiteSpace(SelectedNodeId)
                    ? executable
                    : Array.Empty<AutomationBlockNode>();

            var nodeIds = selected.Select(node => node.Id).ToArray();
            var inputs = selected
                .Where(node => node.Kind == AutomationBlockKind.ReadTarget && !string.IsNullOrWhiteSpace(node.TargetKey))
                .Select(node => new AutomationBlockPort(
                    node.Id + ".in",
                    node.Id,
                    AutomationBlockPortDirection.Input,
                    AutomationBlockLowering.IdentifierForLabel(node.TargetLabel),
                    node.TargetKey,
                    node.TargetLabel))
                .ToArray();
            var outputs = selected
                .Where(node => node.Kind == AutomationBlockKind.SetTarget && !string.IsNullOrWhiteSpace(node.TargetKey))
                .Select(node => new AutomationBlockPort(
                    node.Id + ".out",
                    node.Id,
                    AutomationBlockPortDirection.Output,
                    AutomationBlockLowering.IdentifierForLabel(node.TargetLabel),
                    node.TargetKey,
                    node.TargetLabel))
                .ToArray();

            return new AutomationSystemBlockDefinition(
                string.IsNullOrWhiteSpace(name) ? "System Block" : name.Trim(),
                nodeIds,
                inputs,
                outputs,
                Summary());
        }

        internal string Summary()
        {
            AutomationBlockNode read = FirstExecutableNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode set = FirstExecutableNode(AutomationBlockKind.SetTarget);
            return (read == null ? "Read target" : "Read " + read.TargetLabel) +
                   " -> " +
                   (set == null ? "Set target" : "Set " + set.TargetLabel);
        }

        private AutomationTarget DefaultTargetFor(AutomationBlockKind kind)
        {
            if (_linkedTargets.Count == 0)
                return null;

            if (kind == AutomationBlockKind.SetTarget)
            {
                return _linkedTargets.FirstOrDefault(AutomationTargetCatalog.IsBreadboardWritableTarget) ??
                       _linkedTargets[0];
            }

            if (kind == AutomationBlockKind.ReadTarget)
            {
                return _linkedTargets.FirstOrDefault(AutomationTargetCatalog.IsBreadboardReadableTarget) ??
                       _linkedTargets[0];
            }

            return null;
        }

        private void RebuildPortsAndLinks()
        {
            _ports.Clear();
            _links.Clear();
            foreach (AutomationBlockNode node in _nodes)
                RebuildPortsForNode(node);

            if (Mode == AutomationBlockWorkspaceMode.NativeExact)
            {
                AddExplicitNativeLinks();
                return;
            }

            AddStarterRecipeLinks();
            AddLinearFallbackLinks();
        }

        private void RebuildPortsForNode(AutomationBlockNode node)
        {
            if (node == null)
                return;

            IReadOnlyList<AutomationBlockPortDefinition> inputPorts =
                AutomationBlockCatalog.InputPortsForNode(node);
            for (int index = 0; index < inputPorts.Count; index++)
                AddPortForNode(node, inputPorts[index], index);

            IReadOnlyList<AutomationBlockPortDefinition> outputPorts =
                AutomationBlockCatalog.OutputPortsForNode(node);
            for (int index = 0; index < outputPorts.Count; index++)
                AddPortForNode(node, outputPorts[index], index);
        }

        private void AddPortForNode(
            AutomationBlockNode node,
            AutomationBlockPortDefinition definition,
            int index)
        {
            if (node == null || definition == null)
                return;

            string direction = definition.Direction == AutomationBlockPortDirection.Input ? "in" : "out";
            string portName = string.IsNullOrWhiteSpace(definition.Name) ? direction : definition.Name;
            string portId = Mode == AutomationBlockWorkspaceMode.NativeExact &&
                            node.Kind == AutomationBlockKind.NativeComponent
                ? NativePortId(node.Id, definition.Direction, index)
                : node.Id +
                  "." +
                  direction +
                  "." +
                  AutomationBlockLowering.IdentifierForLabel(portName);
            if (_ports.Any(port => string.Equals(port.Id, portId, StringComparison.Ordinal)))
                portId += "." + index.ToString(CultureInfo.InvariantCulture);

            _ports.Add(new AutomationBlockPort(
                portId,
                node.Id,
                definition.Direction,
                portName,
                node.TargetKey,
                node.TargetLabel));
        }

        private void AddExplicitNativeLinks()
        {
            foreach (AutomationBlockLink link in _explicitLinks)
            {
                if (link == null)
                    continue;

                AutomationBlockPort from = _ports.FirstOrDefault(port =>
                    string.Equals(port.Id, link.FromPortId, StringComparison.Ordinal));
                AutomationBlockPort to = _ports.FirstOrDefault(port =>
                    string.Equals(port.Id, link.ToPortId, StringComparison.Ordinal));
                if (from == null ||
                    to == null ||
                    from.Direction != AutomationBlockPortDirection.Output ||
                    to.Direction != AutomationBlockPortDirection.Input)
                {
                    continue;
                }

                if (_links.Any(item =>
                        string.Equals(item.FromPortId, from.Id, StringComparison.Ordinal) &&
                        string.Equals(item.ToPortId, to.Id, StringComparison.Ordinal)))
                {
                    continue;
                }

                _links.Add(new AutomationBlockLink(
                    from.NodeId,
                    from.Id,
                    to.NodeId,
                    to.Id,
                    link.FromNativeComponentId,
                    link.FromNativePortIndex,
                    link.ToNativeComponentId,
                    link.ToNativePortIndex));
            }
        }

        private void AddStarterRecipeLinks()
        {
            AutomationBlockNode read = FirstExecutableNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode compare = FirstExecutableNode(AutomationBlockKind.Compare);
            AutomationBlockNode when = FirstExecutableNode(AutomationBlockKind.WhenIf);
            AutomationBlockNode constant = FirstExecutableNode(AutomationBlockKind.Constant);
            AutomationBlockNode scale = FirstExecutableNode(AutomationBlockKind.MathScale);
            AutomationBlockNode evaluator = FirstExecutableNode(AutomationBlockKind.MathEvaluator);
            AutomationBlockNode set = FirstExecutableNode(AutomationBlockKind.SetTarget);

            AddLink(read, "value", compare, "value");
            AddLink(compare, "condition", when, "condition");

            AutomationBlockNode trueSource = constant ?? scale ?? evaluator ?? read;
            AddLink(trueSource, FirstOutputPortName(trueSource), when, "true");

            if (!AddLink(when, "out", set, "value"))
                AddLink(trueSource, FirstOutputPortName(trueSource), set, "value");
        }

        private void AddLinearFallbackLinks()
        {
            AutomationBlockPort previousOutput = null;
            foreach (AutomationBlockNode node in ExecutableNodes)
            {
                AutomationBlockPort input = _ports.FirstOrDefault(port =>
                    string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                    port.Direction == AutomationBlockPortDirection.Input);

                if (previousOutput != null && input != null && !HasIncomingLink(input))
                    AddLink(previousOutput, input);

                AutomationBlockPort output = _ports.FirstOrDefault(port =>
                    string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                    port.Direction == AutomationBlockPortDirection.Output);
                if (output != null)
                    previousOutput = output;
            }
        }

        internal bool HasInputLink(AutomationBlockNode node, string portName)
        {
            AutomationBlockPort port = FindPort(node, AutomationBlockPortDirection.Input, portName);
            return port != null && HasIncomingLink(port);
        }

        internal bool HasOutputLink(AutomationBlockNode node, string portName)
        {
            AutomationBlockPort port = FindPort(node, AutomationBlockPortDirection.Output, portName);
            return port != null && _links.Any(link =>
                string.Equals(link.FromPortId, port.Id, StringComparison.Ordinal));
        }

        internal bool HasLink(
            AutomationBlockNode fromNode,
            string fromPortName,
            AutomationBlockNode toNode,
            string toPortName)
        {
            AutomationBlockPort from = FindPort(fromNode, AutomationBlockPortDirection.Output, fromPortName);
            AutomationBlockPort to = FindPort(toNode, AutomationBlockPortDirection.Input, toPortName);
            return from != null &&
                   to != null &&
                   _links.Any(link =>
                       string.Equals(link.FromPortId, from.Id, StringComparison.Ordinal) &&
                       string.Equals(link.ToPortId, to.Id, StringComparison.Ordinal));
        }

        private bool AddLink(
            AutomationBlockNode fromNode,
            string fromPortName,
            AutomationBlockNode toNode,
            string toPortName)
        {
            AutomationBlockPort from = FindPort(fromNode, AutomationBlockPortDirection.Output, fromPortName);
            AutomationBlockPort to = FindPort(toNode, AutomationBlockPortDirection.Input, toPortName);
            return AddLink(from, to);
        }

        private bool AddLink(AutomationBlockPort from, AutomationBlockPort to)
        {
            if (from == null ||
                to == null ||
                from.Direction != AutomationBlockPortDirection.Output ||
                to.Direction != AutomationBlockPortDirection.Input)
            {
                return false;
            }

            if (_links.Any(link =>
                    string.Equals(link.FromPortId, from.Id, StringComparison.Ordinal) &&
                    string.Equals(link.ToPortId, to.Id, StringComparison.Ordinal)))
            {
                return false;
            }

            _links.Add(new AutomationBlockLink(
                from.NodeId,
                from.Id,
                to.NodeId,
                to.Id));
            return true;
        }

        private AutomationBlockPort FindPort(
            AutomationBlockNode node,
            AutomationBlockPortDirection direction,
            string portName)
        {
            if (node == null)
                return null;

            return _ports.FirstOrDefault(port =>
                string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                port.Direction == direction &&
                string.Equals(port.Name, portName, StringComparison.OrdinalIgnoreCase));
        }

        private bool HasIncomingLink(AutomationBlockPort port)
        {
            if (port == null)
                return false;

            return _links.Any(link =>
                string.Equals(link.ToPortId, port.Id, StringComparison.Ordinal));
        }

        private static string FirstOutputPortName(AutomationBlockNode node)
        {
            IReadOnlyList<AutomationBlockPortDefinition> ports =
                AutomationBlockCatalog.OutputPortsForNode(node);
            return ports.Count == 0 ? string.Empty : ports[0].Name;
        }

        private void RefreshCanvasOrder()
        {
            AutomationBlockNode[] executable = ExecutableNodes.ToArray();
            for (int index = 0; index < executable.Length; index++)
            {
                AutomationBlockNode node = executable[index];
                node.CanvasOrder = index;
                node.CanvasPosition = new AutomationBlockCanvasPosition(0f, index * 68f);
            }

            int looseIndex = executable.Length;
            foreach (AutomationBlockNode node in _nodes.Where(item => item != null && !item.SnappedToStack))
                node.CanvasOrder = looseIndex++;
        }

        private AutomationBlockNode EnsureStarterNode(AutomationBlockKind kind)
        {
            AutomationBlockNode node = FirstExecutableNode(kind);
            if (node != null)
                return node;

            return AddBlock(kind, ExecutableNodes.Count);
        }

        private AutomationBlockNode CreateConfiguredBlock(AutomationBlockKind kind)
        {
            AutomationTarget target = DefaultTargetFor(kind);
            AutomationBlockDefinition definition = AutomationBlockCatalog.DefinitionFor(kind);
            var node = new AutomationBlockNode(
                "esu-block-" + _nextNodeIndex.ToString(CultureInfo.InvariantCulture),
                kind,
                definition.Label,
                definition.IconKey,
                definition.Category,
                definition.TemplateId);
            _nextNodeIndex++;

            if (target != null)
                node.SetTarget(target);
            if (kind == AutomationBlockKind.ReadTarget)
                node.LinkDirection = AutomationLinkDirection.Input;
            if (kind == AutomationBlockKind.SetTarget)
                node.LinkDirection = AutomationLinkDirection.Output;
            if (kind == AutomationBlockKind.WhenIf)
                node.SecondaryNumericValue = 0f;
            if (kind == AutomationBlockKind.Compare)
                node.NumericValue = 0.5f;
            if (kind == AutomationBlockKind.MathScale)
                node.NumericValue = 1f;
            if (kind == AutomationBlockKind.Constant)
                node.NumericValue = 1f;
            if (kind == AutomationBlockKind.Delay)
                node.NumericValue = 0.2f;
            if (kind == AutomationBlockKind.MathEvaluator)
                node.Expression = "input";
            if (kind == AutomationBlockKind.Switch)
                node.SecondaryNumericValue = 0f;
            if (kind == AutomationBlockKind.Comment)
                node.Comment = "Describe what this automation does.";

            return node;
        }

        private void InsertNodeIntoStack(
            AutomationBlockNode node,
            int insertIndex)
        {
            if (node == null)
                return;

            node.SnappedToStack = true;
            AutomationBlockNode[] executable = ExecutableNodes.ToArray();
            int clamped = Math.Max(0, Math.Min(executable.Length, insertIndex));
            if (clamped >= executable.Length)
            {
                _nodes.Add(node);
                return;
            }

            int absolute = _nodes.FindIndex(item =>
                string.Equals(item.Id, executable[clamped].Id, StringComparison.Ordinal));
            if (absolute < 0)
                _nodes.Add(node);
            else
                _nodes.Insert(absolute, node);
        }

        internal static AutomationBlockCategory CategoryFor(AutomationBlockKind kind)
        {
            switch (kind)
            {
                case AutomationBlockKind.SetTarget:
                    return AutomationBlockCategory.Output;
                case AutomationBlockKind.ReadTarget:
                    return AutomationBlockCategory.Input;
                case AutomationBlockKind.WhenIf:
                case AutomationBlockKind.Compare:
                case AutomationBlockKind.Switch:
                    return AutomationBlockCategory.Control;
                case AutomationBlockKind.MathScale:
                case AutomationBlockKind.MathEvaluator:
                    return AutomationBlockCategory.Math;
                case AutomationBlockKind.Constant:
                    return AutomationBlockCategory.Input;
                case AutomationBlockKind.Delay:
                    return AutomationBlockCategory.Timing;
                case AutomationBlockKind.SystemBlock:
                case AutomationBlockKind.Comment:
                    return AutomationBlockCategory.Organization;
                case AutomationBlockKind.NativeComponent:
                    return AutomationBlockCategory.Advanced;
                default:
                    return AutomationBlockCategory.Notation;
            }
        }

        private static string PaletteTemplateIdFor(AutomationBlockKind kind) =>
            "esu-" + kind.ToString().ToLowerInvariant();

        private static string DefaultLabelFor(AutomationBlockKind kind)
        {
            switch (kind)
            {
                case AutomationBlockKind.WhenIf:
                    return "When / If";
                case AutomationBlockKind.ReadTarget:
                    return "Read target";
                case AutomationBlockKind.Compare:
                    return "Compare";
                case AutomationBlockKind.MathScale:
                    return "Math / Scale";
                case AutomationBlockKind.MathEvaluator:
                    return "Math Evaluator";
                case AutomationBlockKind.Switch:
                    return "Switch";
                case AutomationBlockKind.SetTarget:
                    return "Set target";
                case AutomationBlockKind.Constant:
                    return "Constant";
                case AutomationBlockKind.Delay:
                    return "Delay";
                case AutomationBlockKind.SystemBlock:
                    return "System Block";
                case AutomationBlockKind.NativeComponent:
                    return "Native component";
                default:
                    return "Comment";
            }
        }

        private static string DefaultIconFor(AutomationBlockKind kind)
        {
            switch (kind)
            {
                case AutomationBlockKind.WhenIf:
                    return "risk";
                case AutomationBlockKind.ReadTarget:
                    return "visibility";
                case AutomationBlockKind.Compare:
                    return "filter";
                case AutomationBlockKind.MathScale:
                    return "settings";
                case AutomationBlockKind.MathEvaluator:
                    return "settings";
                case AutomationBlockKind.Switch:
                    return "filter";
                case AutomationBlockKind.SetTarget:
                    return "anchor";
                case AutomationBlockKind.Constant:
                    return "cube";
                case AutomationBlockKind.Delay:
                    return "time";
                case AutomationBlockKind.SystemBlock:
                    return "duplicate";
                case AutomationBlockKind.NativeComponent:
                    return "duplicate";
                default:
                    return "info";
            }
        }
    }

    internal sealed class AutomationBlockNode
    {
        internal AutomationBlockNode(
            string id,
            AutomationBlockKind kind,
            string label,
            string iconKey,
            AutomationBlockCategory category,
            string paletteTemplateId)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Label = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "info" : iconKey;
            Category = category;
            PaletteTemplateId = string.IsNullOrWhiteSpace(paletteTemplateId)
                ? "esu-" + Kind.ToString().ToLowerInvariant()
                : paletteTemplateId;
            Operator = AutomationCompareOperator.GreaterThan;
            TargetLabel = "target";
        }

        internal string Id { get; }

        internal AutomationBlockKind Kind { get; }

        internal string Label { get; }

        internal string IconKey { get; }

        internal AutomationBlockCategory Category { get; }

        internal string PaletteTemplateId { get; }

        internal string ParentNodeId { get; set; }

        internal int CanvasOrder { get; set; }

        internal AutomationBlockCanvasPosition CanvasPosition { get; set; }

        internal bool SnappedToStack { get; set; }

        internal bool IsSelected { get; set; }

        internal string TargetKey { get; private set; }

        internal string TargetPersistenceKey { get; private set; }

        internal string TargetLabel { get; private set; }

        internal AutomationLinkDirection LinkDirection { get; set; }

        internal AutomationProxyPropertySelection PropertySelection { get; private set; }

        internal AutomationCompareOperator Operator { get; set; }

        internal float NumericValue { get; set; }

        internal float SecondaryNumericValue { get; set; }

        internal string Comment { get; set; }

        internal string Expression { get; set; }

        internal AutomationProxyPropertyBindingMode PropertyBindingMode { get; private set; } =
            AutomationProxyPropertyBindingMode.Unset;

        internal string NativeComponentTypeName { get; private set; } = string.Empty;

        internal string NativeComponentLabel { get; private set; } = string.Empty;

        internal string NativeComponentDescription { get; private set; } = string.Empty;

        internal string NativeBlockTypeName { get; private set; } = string.Empty;

        internal string NativeBlockFilter { get; private set; } = string.Empty;

        internal uint NativeComponentId { get; private set; }

        internal string NativeComponentTypeId { get; private set; } = string.Empty;

        internal string NativeComponentFingerprint { get; private set; } = string.Empty;

        internal bool NativeImported { get; private set; }

        internal bool NativeEsuOwned { get; private set; }

        internal float NativeX { get; private set; }

        internal float NativeY { get; private set; }

        internal float NativeWidth { get; private set; }

        internal float NativeHeight { get; private set; }

        internal string NativeSettingsSummary { get; private set; } = string.Empty;

        internal IReadOnlyList<string> NativeInputPortLabels { get; private set; } =
            Array.Empty<string>();

        internal IReadOnlyList<string> NativeOutputPortLabels { get; private set; } =
            Array.Empty<string>();

        internal void SetTarget(AutomationTarget target)
        {
            if (target == null)
                return;

            bool targetChanged = !string.Equals(TargetKey, target.StableKey, StringComparison.Ordinal);
            TargetKey = target.StableKey;
            TargetPersistenceKey = target.PersistenceKey;
            TargetLabel = target.Label;
            if (targetChanged)
                ClearProperty();
        }

        internal void RebindTarget(AutomationTarget target)
        {
            if (target == null)
                return;

            TargetKey = target.StableKey;
            TargetPersistenceKey = target.PersistenceKey;
            TargetLabel = target.Label;
        }

        internal void RestoreTarget(
            string targetKey,
            string targetPersistenceKey,
            string targetLabel)
        {
            TargetKey = targetKey ?? string.Empty;
            TargetPersistenceKey = targetPersistenceKey ?? string.Empty;
            TargetLabel = string.IsNullOrWhiteSpace(targetLabel) ? "target" : targetLabel;
        }

        internal void SelectProperty(AutomationProxyPropertySelection selection)
        {
            PropertySelection = selection;
            PropertyBindingMode = selection == null || selection.IsClear
                ? AutomationProxyPropertyBindingMode.Unset
                : AutomationProxyPropertyBindingMode.Explicit;
        }

        internal void ClearProperty()
        {
            PropertySelection = null;
            PropertyBindingMode = AutomationProxyPropertyBindingMode.Unset;
        }

        internal void SetNativeComponent(
            string typeName,
            string label,
            string description)
        {
            NativeComponentTypeName = typeName ?? string.Empty;
            NativeComponentLabel = string.IsNullOrWhiteSpace(label) ? "Native component" : label;
            NativeComponentDescription = description ?? string.Empty;
        }

        internal void SetNativeIdentity(
            uint componentId,
            string componentTypeId,
            string fingerprint,
            bool imported,
            bool esuOwned,
            float x,
            float y,
            float width,
            float height,
            string settingsSummary,
            IReadOnlyList<string> inputPortLabels,
            IReadOnlyList<string> outputPortLabels,
            string blockTypeName = null,
            string blockFilter = null)
        {
            NativeComponentId = componentId;
            NativeComponentTypeId = componentTypeId ?? string.Empty;
            NativeComponentFingerprint = fingerprint ?? string.Empty;
            NativeImported = imported;
            NativeEsuOwned = esuOwned;
            NativeBlockTypeName = blockTypeName ?? string.Empty;
            NativeBlockFilter = blockFilter ?? string.Empty;
            NativeX = x;
            NativeY = y;
            NativeWidth = width;
            NativeHeight = height;
            NativeSettingsSummary = settingsSummary ?? string.Empty;
            NativeInputPortLabels = (inputPortLabels ?? Array.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray();
            NativeOutputPortLabels = (outputPortLabels ?? Array.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray();
        }
    }

    internal struct AutomationBlockCanvasPosition
    {
        internal AutomationBlockCanvasPosition(float x, float y)
        {
            X = x;
            Y = y;
        }

        internal float X { get; }

        internal float Y { get; }
    }

    internal sealed class AutomationBlockPort
    {
        internal AutomationBlockPort(
            string id,
            string nodeId,
            AutomationBlockPortDirection direction,
            string name,
            string targetKey,
            string targetLabel)
        {
            Id = id ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            Direction = direction;
            Name = string.IsNullOrWhiteSpace(name) ? "port" : name;
            TargetKey = targetKey ?? string.Empty;
            TargetLabel = string.IsNullOrWhiteSpace(targetLabel) ? Name : targetLabel;
        }

        internal string Id { get; }

        internal string NodeId { get; }

        internal AutomationBlockPortDirection Direction { get; }

        internal string Name { get; }

        internal string TargetKey { get; }

        internal string TargetLabel { get; }
    }

    internal sealed class AutomationBlockLink
    {
        internal AutomationBlockLink(
            string fromNodeId,
            string fromPortId,
            string toNodeId,
            string toPortId,
            uint fromNativeComponentId = 0U,
            int fromNativePortIndex = -1,
            uint toNativeComponentId = 0U,
            int toNativePortIndex = -1)
        {
            FromNodeId = fromNodeId ?? string.Empty;
            FromPortId = fromPortId ?? string.Empty;
            ToNodeId = toNodeId ?? string.Empty;
            ToPortId = toPortId ?? string.Empty;
            FromNativeComponentId = fromNativeComponentId;
            FromNativePortIndex = fromNativePortIndex;
            ToNativeComponentId = toNativeComponentId;
            ToNativePortIndex = toNativePortIndex;
        }

        internal string FromNodeId { get; }

        internal string FromPortId { get; }

        internal string ToNodeId { get; }

        internal string ToPortId { get; }

        internal uint FromNativeComponentId { get; }

        internal int FromNativePortIndex { get; }

        internal uint ToNativeComponentId { get; }

        internal int ToNativePortIndex { get; }
    }

    internal sealed class AutomationProxyPropertySelection
    {
        internal AutomationProxyPropertySelection(
            string label,
            string tooltip,
            bool isGetter,
            bool isClear,
            bool isGetterReadable,
            uint readableAttributeId,
            uint blockPropertyId,
            uint blockSetId)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Property" : label;
            Tooltip = tooltip ?? string.Empty;
            IsGetter = isGetter;
            IsClear = isClear;
            IsGetterReadable = isGetterReadable;
            ReadableAttributeId = readableAttributeId;
            BlockPropertyId = blockPropertyId;
            BlockSetId = blockSetId;
        }

        internal string Label { get; }

        internal string Tooltip { get; }

        internal bool IsGetter { get; }

        internal bool IsClear { get; }

        internal bool IsGetterReadable { get; }

        internal uint ReadableAttributeId { get; }

        internal uint BlockPropertyId { get; }

        internal uint BlockSetId { get; }

        internal bool Matches(AutomationBreadboardProxyOption option)
        {
            if (option == null)
                return false;

            if (IsClear || option.IsClear)
                return IsClear && option.IsClear;

            return option.IsGetterReadable == IsGetterReadable &&
                   option.ReadableAttributeId == ReadableAttributeId &&
                   option.BlockPropertyId == BlockPropertyId &&
                   option.BlockSetId == BlockSetId;
        }

        internal bool Matches(AutomationProxyPropertySelection other)
        {
            if (other == null)
                return false;

            if (IsClear || other.IsClear)
                return IsClear && other.IsClear && IsGetter == other.IsGetter;

            return IsGetter == other.IsGetter &&
                   IsGetterReadable == other.IsGetterReadable &&
                   ReadableAttributeId == other.ReadableAttributeId &&
                   BlockPropertyId == other.BlockPropertyId &&
                   BlockSetId == other.BlockSetId;
        }
    }

    internal sealed class AutomationTargetPropertyOption
    {
        internal AutomationTargetPropertyOption(
            AutomationProxyPropertySelection selection,
            string label,
            string tooltip)
        {
            Selection = selection;
            Label = string.IsNullOrWhiteSpace(label) ? selection?.Label ?? "Property" : label;
            Tooltip = tooltip ?? selection?.Tooltip ?? string.Empty;
        }

        internal AutomationProxyPropertySelection Selection { get; }

        internal string Label { get; }

        internal string Tooltip { get; }
    }

    internal sealed class AutomationSystemBlockDefinition
    {
        internal AutomationSystemBlockDefinition(
            string name,
            IReadOnlyList<string> nodeIds,
            IReadOnlyList<AutomationBlockPort> inputPorts,
            IReadOnlyList<AutomationBlockPort> outputPorts,
            string internalSummary)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "System Block" : name;
            NodeIds = nodeIds ?? Array.Empty<string>();
            InputPorts = inputPorts ?? Array.Empty<AutomationBlockPort>();
            OutputPorts = outputPorts ?? Array.Empty<AutomationBlockPort>();
            InternalSummary = internalSummary ?? string.Empty;
        }

        internal string Name { get; }

        internal IReadOnlyList<string> NodeIds { get; }

        internal IReadOnlyList<AutomationBlockPort> InputPorts { get; }

        internal IReadOnlyList<AutomationBlockPort> OutputPorts { get; }

        internal string InternalSummary { get; }
    }

    internal sealed class AutomationLoweringPlan
    {
        internal AutomationLoweringPlan(
            string controllerKey,
            string controllerLabel,
            string readTargetKey,
            string readTargetLabel,
            string outputTargetKey,
            string outputTargetLabel,
            AutomationProxyPropertySelection readProperty,
            AutomationProxyPropertySelection outputProperty,
            string readIdentifier,
            string conditionExpression,
            string passExpression,
            float failValue,
            bool directValueFlow,
            IReadOnlyList<AutomationNativeComponentRequest> nativeComponentRequests,
            IReadOnlyList<string> steps)
        {
            ControllerKey = controllerKey ?? string.Empty;
            ControllerLabel = controllerLabel ?? string.Empty;
            ReadTargetKey = readTargetKey ?? string.Empty;
            ReadTargetLabel = readTargetLabel ?? string.Empty;
            OutputTargetKey = outputTargetKey ?? string.Empty;
            OutputTargetLabel = outputTargetLabel ?? string.Empty;
            ReadProperty = readProperty;
            OutputProperty = outputProperty;
            ReadIdentifier = string.IsNullOrWhiteSpace(readIdentifier) ? "target" : readIdentifier;
            ConditionExpression = conditionExpression ?? string.Empty;
            PassExpression = passExpression ?? string.Empty;
            FailValue = failValue;
            DirectValueFlow = directValueFlow;
            NativeComponentRequests = nativeComponentRequests ?? Array.Empty<AutomationNativeComponentRequest>();
            Steps = steps ?? Array.Empty<string>();
        }

        internal string ControllerKey { get; }

        internal string ControllerLabel { get; }

        internal string ReadTargetKey { get; }

        internal string ReadTargetLabel { get; }

        internal string OutputTargetKey { get; }

        internal string OutputTargetLabel { get; }

        internal AutomationProxyPropertySelection ReadProperty { get; }

        internal AutomationProxyPropertySelection OutputProperty { get; }

        internal string ReadIdentifier { get; }

        internal string ConditionExpression { get; }

        internal string PassExpression { get; }

        internal float FailValue { get; }

        internal bool DirectValueFlow { get; }

        internal bool HasDirectValueFlow =>
            DirectValueFlow &&
            !string.IsNullOrWhiteSpace(ReadTargetKey) &&
            !string.IsNullOrWhiteSpace(OutputTargetKey) &&
            !string.IsNullOrWhiteSpace(PassExpression);

        internal IReadOnlyList<AutomationNativeComponentRequest> NativeComponentRequests { get; }

        internal bool HasSemanticFlow =>
            !string.IsNullOrWhiteSpace(ReadTargetKey) &&
            !string.IsNullOrWhiteSpace(OutputTargetKey) &&
            !string.IsNullOrWhiteSpace(ConditionExpression) &&
            !string.IsNullOrWhiteSpace(PassExpression);

        internal bool HasNativeComponentRequests =>
            NativeComponentRequests.Count > 0;

        internal IReadOnlyList<string> Steps { get; }

        internal string Summary =>
            HasDirectValueFlow
                ? "Copy " + ReadTargetLabel + " " + ReadIdentifier + " directly into " + OutputTargetLabel + "."
                :
            HasSemanticFlow
                ? "If " + ConditionExpression + " then set " + OutputTargetLabel + " to " +
                  PassExpression + " else " + FailValue.ToString("0.###", CultureInfo.InvariantCulture) + "."
                : "Create " + NativeComponentRequests.Count.ToString(CultureInfo.InvariantCulture) +
                  " native Breadboard component block(s).";

        internal string ToNativeCode()
        {
            if (HasDirectValueFlow)
                return "out = " + PassExpression + "\n";

            if (!HasSemanticFlow)
                return "out = 0\n";

            return
                "if " + ConditionExpression + ":\n" +
                "    out = " + PassExpression + "\n" +
                "else:\n" +
                "    out = " + FailValue.ToString("0.###", CultureInfo.InvariantCulture) + "\n";
        }
    }

    internal sealed class AutomationNativeComponentRequest
    {
        internal AutomationNativeComponentRequest(
            string typeName,
            string label)
        {
            TypeName = typeName ?? string.Empty;
            Label = string.IsNullOrWhiteSpace(label) ? TypeName : label;
        }

        internal string TypeName { get; }

        internal string Label { get; }
    }

    internal static class AutomationBlockLowering
    {
        internal static bool CheckBlocksToNative(
            AutomationBlockWorkspace workspace,
            out AutomationLoweringPlan plan,
            out string message)
        {
            plan = null;
            message = null;
            if (workspace == null)
            {
                message = "Open an ESU Blocks workspace before checking native lowering.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(workspace.ControllerKey))
            {
                message = "Select a live Breadboard controller before checking ESU Blocks.";
                return false;
            }

            AutomationBlockNode readNode = workspace.FirstExecutableNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode whenNode = workspace.FirstExecutableNode(AutomationBlockKind.WhenIf);
            AutomationBlockNode compareNode = workspace.FirstExecutableNode(AutomationBlockKind.Compare);
            AutomationBlockNode scaleNode = workspace.FirstExecutableNode(AutomationBlockKind.MathScale);
            AutomationBlockNode evaluatorNode = workspace.FirstExecutableNode(AutomationBlockKind.MathEvaluator);
            AutomationBlockNode switchNode = workspace.FirstExecutableNode(AutomationBlockKind.Switch);
            AutomationBlockNode constantNode = workspace.FirstExecutableNode(AutomationBlockKind.Constant);
            AutomationBlockNode setNode = workspace.FirstExecutableNode(AutomationBlockKind.SetTarget);
            AutomationNativeComponentRequest[] nativeRequests = workspace
                .NativeComponentNodes()
                .Select(node => new AutomationNativeComponentRequest(
                    node.NativeComponentTypeName,
                    string.IsNullOrWhiteSpace(node.NativeComponentLabel) ? node.Label : node.NativeComponentLabel))
                .ToArray();
            bool hasConfiguredSemanticFlow = workspace.HasConfiguredSemanticFlow;
            bool hasAnySemanticBlock =
                readNode != null ||
                compareNode != null ||
                setNode != null ||
                whenNode != null ||
                scaleNode != null ||
                evaluatorNode != null ||
                switchNode != null ||
                constantNode != null;

            if (!ValidateBlockSupportForCheck(workspace, out message))
                return false;

            if (!hasAnySemanticBlock && nativeRequests.Length == 0)
            {
                message = "No snapped ESU Blocks are ready to Check. Drag blocks near the forever stack to snap them into the executable flow.";
                return false;
            }

            if ((!hasAnySemanticBlock || !hasConfiguredSemanticFlow) && nativeRequests.Length > 0)
            {
                plan = new AutomationLoweringPlan(
                    workspace.ControllerKey,
                    workspace.ControllerLabel,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0f,
                    directValueFlow: false,
                    nativeRequests,
                    nativeRequests
                        .Select(request => "Create native Breadboard component " + request.Label + ".")
                        .ToArray());
                message =
                    "ESU Blocks check passed: " +
                    nativeRequests.Length.ToString(CultureInfo.InvariantCulture) +
                    " native Breadboard component block(s) will be created on Apply. No native data changed.";
                return true;
            }

            if (workspace.HasConfiguredDirectReadSetFlow)
            {
                AutomationTarget directReadTarget = workspace.TargetForNode(readNode);
                AutomationTarget directSetTarget = workspace.TargetForNode(setNode);
                if (directReadTarget == null)
                {
                    message = "Read Target needs a live linked Input target.";
                    return false;
                }

                if (directSetTarget == null)
                {
                    message = "Set Target needs a live linked Output target and value.";
                    return false;
                }

                if (!AutomationTargetCatalog.IsBreadboardReadableTarget(directReadTarget))
                {
                    message = directReadTarget.Label + " is not available as a readable Breadboard target.";
                    return false;
                }

                if (!AutomationTargetCatalog.IsBreadboardWritableTarget(directSetTarget))
                {
                    message = directSetTarget.Label + " is not available as a writable Breadboard target.";
                    return false;
                }

                if (!HasExplicitPropertySelection(readNode))
                {
                    message = "Read Target needs an explicit Getter property. Choose a property before checking ESU Blocks.";
                    return false;
                }

                if (!HasExplicitPropertySelection(setNode))
                {
                    message = "Set Target needs an explicit Setter property. Choose a property before checking ESU Blocks.";
                    return false;
                }

                string directIdentifier = IdentifierForReadSignal(directReadTarget, readNode.PropertySelection);
                string directReadProperty = PropertyPreview(readNode, "Choose Getter property");
                string directSetProperty = PropertyPreview(setNode, "Choose Setter property");
                var directSteps = new List<string>
                {
                    "Read " + directReadTarget.Label + " property: " + directReadProperty + ".",
                    "Create or reuse Generic Getter for " + directReadTarget.Label + ".",
                    "Write " + directSetTarget.Label + " property: " + directSetProperty + ".",
                    "Create or reuse Generic Setter for " + directSetTarget.Label + ".",
                    "Wire the Generic Getter output directly to the Generic Setter value input.",
                    "Track generated native proxy ids for Revert."
                };
                foreach (AutomationNativeComponentRequest request in nativeRequests)
                    directSteps.Add("Create native Breadboard component " + request.Label + ".");
                AddMetadataOnlyStep(workspace, directSteps);

                plan = new AutomationLoweringPlan(
                    workspace.ControllerKey,
                    workspace.ControllerLabel,
                    directReadTarget.StableKey,
                    directReadTarget.Label,
                    directSetTarget.StableKey,
                    directSetTarget.Label,
                    readNode.PropertySelection,
                    setNode.PropertySelection,
                    directIdentifier,
                    string.Empty,
                    directIdentifier,
                    0f,
                    directValueFlow: true,
                    nativeRequests,
                    directSteps);
                message = "ESU Blocks check passed: " + plan.Summary + " No native data changed.";
                return true;
            }

            if (readNode == null)
            {
                message = "Compare needs a left input. Add a Read Target block before checking ESU Blocks.";
                return false;
            }

            if (compareNode == null)
            {
                message = "Read Target needs a Compare block before this starter flow can lower to native behavior.";
                return false;
            }

            if (setNode == null)
            {
                message = "Set Target needs a target and value. Add a Set Target block before checking ESU Blocks.";
                return false;
            }

            if (!workspace.HasOutputLink(readNode, "value") ||
                !workspace.HasInputLink(compareNode, "value"))
            {
                message = "Compare needs a left input from Read Target.";
                return false;
            }

            if (whenNode != null &&
                (!workspace.HasInputLink(whenNode, "condition") ||
                 !workspace.HasInputLink(whenNode, "true")))
            {
                message = "If / Else needs a condition input and a true-value input before Check can preview native lowering.";
                return false;
            }

            if (!workspace.HasInputLink(setNode, "value"))
            {
                message = "Set Target needs a target and value before Check can preview native lowering.";
                return false;
            }

            AutomationTarget readTarget = workspace.TargetForNode(readNode);
            AutomationTarget setTarget = workspace.TargetForNode(setNode);
            if (readTarget == null)
            {
                message = "Read Target needs a live linked Input target.";
                return false;
            }

            if (setTarget == null)
            {
                message = "Set Target needs a live linked Output target and value.";
                return false;
            }

            if (!AutomationTargetCatalog.IsBreadboardReadableTarget(readTarget))
            {
                message = readTarget.Label + " is not available as a readable Breadboard target.";
                return false;
            }

            if (!AutomationTargetCatalog.IsBreadboardWritableTarget(setTarget))
            {
                message = setTarget.Label + " is not available as a writable Breadboard target.";
                return false;
            }

            if (!HasExplicitPropertySelection(readNode))
            {
                message = "Read Target needs an explicit Getter property. Choose a property before checking ESU Blocks.";
                return false;
            }

            if (!HasExplicitPropertySelection(setNode))
            {
                message = "Set Target needs an explicit Setter property. Choose a property before checking ESU Blocks.";
                return false;
            }

            string identifier = IdentifierForReadSignal(readTarget, readNode.PropertySelection);
            string condition = identifier + " " + OperatorText(compareNode.Operator) + " " +
                               compareNode.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);
            string pass = PassExpressionFor(identifier, constantNode, scaleNode, evaluatorNode);
            float failValue = switchNode != null
                ? switchNode.SecondaryNumericValue
                : whenNode == null ? 0f : whenNode.SecondaryNumericValue;
            string readProperty = PropertyPreview(readNode, "Choose Getter property");
            string setProperty = PropertyPreview(setNode, "Choose Setter property");
            var steps = new List<string>
            {
                "Read " + readTarget.Label + " property: " + readProperty + ".",
                "Create or reuse Generic Getter for " + readTarget.Label + ".",
                "Create native Evaluator/Switch logic for " + condition + ".",
                "Set " + setTarget.Label + " to " + pass + " when true; else " +
                failValue.ToString("0.###", CultureInfo.InvariantCulture) + ".",
                "Write " + setTarget.Label + " property: " + setProperty + ".",
                "Create or reuse Generic Setter for " + setTarget.Label + ".",
                "Track generated native component ids for Revert."
            };
            foreach (AutomationNativeComponentRequest request in nativeRequests)
                steps.Add("Create native Breadboard component " + request.Label + ".");
            AddMetadataOnlyStep(workspace, steps);

            plan = new AutomationLoweringPlan(
                workspace.ControllerKey,
                workspace.ControllerLabel,
                readTarget.StableKey,
                readTarget.Label,
                setTarget.StableKey,
                setTarget.Label,
                readNode.PropertySelection,
                setNode.PropertySelection,
                identifier,
                condition,
                pass,
                failValue,
                directValueFlow: false,
                nativeRequests,
                steps);
            message = "ESU Blocks check passed: " + plan.Summary + " No native data changed.";
            return true;
        }

        private static bool ValidateBlockSupportForCheck(
            AutomationBlockWorkspace workspace,
            out string message)
        {
            message = null;
            if (workspace == null)
                return true;

            foreach (AutomationBlockNode node in workspace.ExecutableNodes)
            {
                if (node == null)
                    continue;

                if (node.Kind == AutomationBlockKind.Delay)
                {
                    message = "Delay is not lowerable yet. Remove it or use an Advanced native component before Apply.";
                    return false;
                }

                if (node.Kind == AutomationBlockKind.SystemBlock)
                {
                    message = "This System Block has no exposed output ports yet. Open Systems to define ports; ESU Blocks Check does not lower nested System Blocks tonight.";
                    return false;
                }

                if (node.Kind == AutomationBlockKind.NativeComponent &&
                    string.IsNullOrWhiteSpace(node.NativeComponentTypeName))
                {
                    message = "This native wrapper can be placed, but its advertised FtD component type is missing.";
                    return false;
                }
            }

            return true;
        }

        private static void AddMetadataOnlyStep(
            AutomationBlockWorkspace workspace,
            List<string> steps)
        {
            if (workspace == null || steps == null)
                return;

            int comments = workspace.ExecutableNodes.Count(node => node.Kind == AutomationBlockKind.Comment);
            if (comments > 0)
            {
                steps.Add(
                    comments.ToString(CultureInfo.InvariantCulture) +
                    " Comment block(s) stay as ESU layout metadata; Apply will not create native nodes for them.");
            }
        }

        private static string PropertyPreview(
            AutomationBlockNode node,
            string fallback)
        {
            if (node == null)
                return fallback;

            if (node.PropertySelection != null &&
                !node.PropertySelection.IsClear &&
                !string.IsNullOrWhiteSpace(node.PropertySelection.Label))
            {
                return node.PropertySelection.Label;
            }

            return fallback;
        }

        private static bool HasExplicitPropertySelection(AutomationBlockNode node) =>
            node != null &&
            node.PropertyBindingMode == AutomationProxyPropertyBindingMode.Explicit &&
            node.PropertySelection != null &&
            !node.PropertySelection.IsClear;

        internal static bool ApplyLoweringPlan(
            AutomationBreadboardInspector inspector,
            AutomationLoweringPlan plan,
            out AutomationBreadboardCompileResult result,
            out string message)
        {
            result = null;
            message = null;
            if (inspector == null)
            {
                message = "Selected controller does not expose a native Breadboard.";
                return false;
            }

            if (plan == null)
            {
                message = "Check ESU Blocks before applying native lowering.";
                return false;
            }

            if (!plan.HasSemanticFlow)
            {
                result = new AutomationBreadboardCompileResult("native component blocks", Array.Empty<uint>());
                message = "No semantic if/else block flow was present; native component blocks are applied by the ESU Blocks session.";
                return true;
            }

            return inspector.TryCreateIfElseSwitch(
                plan.ConditionExpression,
                string.Empty,
                "AND",
                plan.PassExpression,
                plan.FailValue,
                string.Empty,
                out result,
                out message);
        }

        internal static int RevertLowering(
            AutomationBreadboardInspector inspector,
            IEnumerable<uint> componentIds,
            out int missing,
            out int failed)
        {
            missing = 0;
            failed = 0;
            if (inspector == null || componentIds == null)
                return 0;

            int deleted = 0;
            IReadOnlyList<AutomationBreadboardComponentSummary> components = inspector.Components;
            foreach (uint componentId in componentIds.Distinct().Reverse())
            {
                if (componentId == 0U)
                    continue;

                AutomationBreadboardComponentSummary component = components.FirstOrDefault(item =>
                    item != null && item.UniqueId == componentId);
                if (component == null)
                {
                    missing++;
                    continue;
                }

                if (inspector.TryDeleteComponent(component, out string message))
                {
                    deleted++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(message) &&
                    message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    missing++;
                }
                else
                {
                    failed++;
                }
            }

            return deleted;
        }

        internal static string IdentifierForTarget(AutomationTarget target)
        {
            if (target == null)
                return "target";

            switch (target.Category)
            {
                case AutomationTargetCategory.Spinblocks:
                    return "spinblock";
                case AutomationTargetCategory.TurretsWeapons:
                    return "weapon";
                case AutomationTargetCategory.Propulsion:
                    return "propulsion";
                case AutomationTargetCategory.Pistons:
                    return "piston";
                case AutomationTargetCategory.Pumps:
                    return "pump";
                case AutomationTargetCategory.ControlSurfaces:
                    return "control_surface";
                case AutomationTargetCategory.Ai:
                    return "ai_target";
                case AutomationTargetCategory.Missiles:
                    return "missile";
                case AutomationTargetCategory.Lights:
                    return "light";
                case AutomationTargetCategory.ShieldsDefence:
                    return "shield";
                case AutomationTargetCategory.Detection:
                    return "detector";
                case AutomationTargetCategory.DoorsDocking:
                    return "door";
                case AutomationTargetCategory.SoundDisplay:
                    return "media";
                case AutomationTargetCategory.ResourcePower:
                    return "resource";
                default:
                    return IdentifierForLabel(target.Label);
            }
        }

        private static string IdentifierForReadSignal(
            AutomationTarget target,
            AutomationProxyPropertySelection property)
        {
            if (property != null &&
                !property.IsClear &&
                !string.IsNullOrWhiteSpace(property.Label))
            {
                string propertyIdentifier = IdentifierForLabel(property.Label);
                if (!string.Equals(propertyIdentifier, "target", StringComparison.Ordinal))
                    return propertyIdentifier;
            }

            return IdentifierForTarget(target);
        }

        private static string PassExpressionFor(
            string identifier,
            AutomationBlockNode constantNode,
            AutomationBlockNode scaleNode,
            AutomationBlockNode evaluatorNode)
        {
            if (evaluatorNode != null && !string.IsNullOrWhiteSpace(evaluatorNode.Expression))
            {
                string expression = evaluatorNode.Expression.Trim();
                return expression
                    .Replace("input", identifier)
                    .Replace("value", identifier);
            }

            if (constantNode != null)
                return constantNode.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);

            float scale = scaleNode == null ? 1f : scaleNode.NumericValue;
            return Math.Abs(scale - 1f) < 0.0001f
                ? identifier
                : identifier + " * " + scale.ToString("0.###", CultureInfo.InvariantCulture);
        }

        internal static string IdentifierForLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "target";

            var chars = new List<char>();
            bool lastWasUnderscore = false;
            foreach (char raw in label.Trim())
            {
                char character = char.ToLowerInvariant(raw);
                bool valid = char.IsLetterOrDigit(character) || character == '_';
                char next = valid ? character : '_';
                if (next == '_' && lastWasUnderscore)
                    continue;

                chars.Add(next);
                lastWasUnderscore = next == '_';
            }

            while (chars.Count > 0 && chars[chars.Count - 1] == '_')
                chars.RemoveAt(chars.Count - 1);

            if (chars.Count == 0)
                return "target";
            if (char.IsDigit(chars[0]))
                chars.Insert(0, '_');

            return new string(chars.ToArray());
        }

        private static string OperatorText(AutomationCompareOperator compareOperator)
        {
            switch (compareOperator)
            {
                case AutomationCompareOperator.LessThan:
                    return "<";
                case AutomationCompareOperator.EqualOrGreater:
                    return ">=";
                case AutomationCompareOperator.EqualOrLess:
                    return "<=";
                default:
                    return ">";
            }
        }
    }
}
