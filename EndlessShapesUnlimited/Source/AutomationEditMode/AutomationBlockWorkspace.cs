using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DecoLimitLifter.AutomationEditMode
{
    internal enum AutomationBlockKind
    {
        WhenIf,
        ReadTarget,
        Compare,
        MathScale,
        SetTarget,
        Constant,
        Delay,
        Comment,
        SystemBlock
    }

    internal enum AutomationBlockPortDirection
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

    internal sealed class AutomationBlockWorkspace
    {
        private readonly List<AutomationBlockNode> _nodes = new List<AutomationBlockNode>();
        private readonly List<AutomationBlockPort> _ports = new List<AutomationBlockPort>();
        private readonly List<AutomationBlockLink> _links = new List<AutomationBlockLink>();
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

        internal string SelectedNodeId { get; private set; }

        internal IReadOnlyList<AutomationBlockNode> Nodes => _nodes;

        internal IReadOnlyList<AutomationBlockPort> Ports => _ports;

        internal IReadOnlyList<AutomationBlockLink> Links => _links;

        internal IReadOnlyList<AutomationTarget> LinkedTargets => _linkedTargets;

        internal static AutomationBlockWorkspace CreateDefault(
            string controllerKey,
            string controllerLabel,
            IReadOnlyList<AutomationTarget> linkedTargets)
        {
            var workspace = new AutomationBlockWorkspace(controllerKey, controllerLabel);
            workspace.ReplaceTargets(linkedTargets);
            workspace.AddBlock(AutomationBlockKind.Comment);
            workspace.AddBlock(AutomationBlockKind.WhenIf);
            workspace.AddBlock(AutomationBlockKind.ReadTarget);
            workspace.AddBlock(AutomationBlockKind.Compare);
            workspace.AddBlock(AutomationBlockKind.MathScale);
            workspace.AddBlock(AutomationBlockKind.SetTarget);
            return workspace;
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

                AutomationTarget target = _linkedTargets.FirstOrDefault(item =>
                    string.Equals(item.StableKey, node.TargetKey, StringComparison.Ordinal));
                if (target != null)
                    node.SetTarget(target);
            }

            RebuildPortsAndLinks();
        }

        internal AutomationBlockNode AddBlock(AutomationBlockKind kind)
        {
            AutomationTarget target = DefaultTargetFor(kind);
            var node = new AutomationBlockNode(
                "esu-block-" + _nextNodeIndex.ToString(CultureInfo.InvariantCulture),
                kind,
                DefaultLabelFor(kind),
                DefaultIconFor(kind));
            _nextNodeIndex++;

            if (target != null)
                node.SetTarget(target);
            if (kind == AutomationBlockKind.Compare)
                node.NumericValue = 0.5f;
            if (kind == AutomationBlockKind.MathScale)
                node.NumericValue = 1f;
            if (kind == AutomationBlockKind.Constant)
                node.NumericValue = 1f;
            if (kind == AutomationBlockKind.Delay)
                node.NumericValue = 0.2f;
            if (kind == AutomationBlockKind.Comment)
                node.Comment = "Describe what this automation does.";

            _nodes.Add(node);
            Select(node.Id);
            RebuildPortsAndLinks();
            return node;
        }

        internal bool RemoveSelected()
        {
            if (string.IsNullOrWhiteSpace(SelectedNodeId))
                return false;

            int removed = _nodes.RemoveAll(node =>
                string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));
            if (removed == 0)
                return false;

            SelectedNodeId = _nodes.Count == 0 ? string.Empty : _nodes[Math.Max(0, _nodes.Count - 1)].Id;
            RebuildPortsAndLinks();
            return true;
        }

        internal bool MoveSelected(int delta)
        {
            if (delta == 0 || string.IsNullOrWhiteSpace(SelectedNodeId))
                return false;

            int index = _nodes.FindIndex(node =>
                string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));
            if (index < 0)
                return false;

            int next = Math.Max(0, Math.Min(_nodes.Count - 1, index + delta));
            if (next == index)
                return false;

            AutomationBlockNode nodeToMove = _nodes[index];
            _nodes.RemoveAt(index);
            _nodes.Insert(next, nodeToMove);
            RebuildPortsAndLinks();
            return true;
        }

        internal void Select(string nodeId)
        {
            SelectedNodeId = nodeId ?? string.Empty;
        }

        internal AutomationBlockNode SelectedNode() =>
            _nodes.FirstOrDefault(node => string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal));

        internal AutomationBlockNode FirstNode(AutomationBlockKind kind) =>
            _nodes.FirstOrDefault(node => node.Kind == kind);

        internal AutomationTarget TargetForNode(AutomationBlockNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.TargetKey))
                return null;

            return _linkedTargets.FirstOrDefault(target =>
                string.Equals(target.StableKey, node.TargetKey, StringComparison.Ordinal));
        }

        internal AutomationSystemBlockDefinition CollapseSelectionToSystemBlock(string name)
        {
            AutomationBlockNode[] selected = string.IsNullOrWhiteSpace(SelectedNodeId)
                ? _nodes.ToArray()
                : _nodes.Where(node => string.Equals(node.Id, SelectedNodeId, StringComparison.Ordinal)).ToArray();
            if (selected.Length == 0)
                selected = _nodes.ToArray();

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
            AutomationBlockNode read = FirstNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode set = FirstNode(AutomationBlockKind.SetTarget);
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
            AutomationBlockPort previousOutput = null;
            foreach (AutomationBlockNode node in _nodes)
            {
                AutomationBlockPort input = null;
                if (node.Kind != AutomationBlockKind.WhenIf && node.Kind != AutomationBlockKind.Comment)
                {
                    input = new AutomationBlockPort(
                        node.Id + ".in",
                        node.Id,
                        AutomationBlockPortDirection.Input,
                        "input",
                        node.TargetKey,
                        node.TargetLabel);
                    _ports.Add(input);
                }

                AutomationBlockPort output = null;
                if (node.Kind != AutomationBlockKind.SetTarget && node.Kind != AutomationBlockKind.Comment)
                {
                    output = new AutomationBlockPort(
                        node.Id + ".out",
                        node.Id,
                        AutomationBlockPortDirection.Output,
                        "output",
                        node.TargetKey,
                        node.TargetLabel);
                    _ports.Add(output);
                }

                if (previousOutput != null && input != null)
                {
                    _links.Add(new AutomationBlockLink(
                        previousOutput.NodeId,
                        previousOutput.Id,
                        input.NodeId,
                        input.Id));
                }

                if (output != null)
                    previousOutput = output;
            }
        }

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
                case AutomationBlockKind.SetTarget:
                    return "Set target";
                case AutomationBlockKind.Constant:
                    return "Constant";
                case AutomationBlockKind.Delay:
                    return "Delay";
                case AutomationBlockKind.SystemBlock:
                    return "System Block";
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
                case AutomationBlockKind.SetTarget:
                    return "anchor";
                case AutomationBlockKind.Constant:
                    return "cube";
                case AutomationBlockKind.Delay:
                    return "time";
                case AutomationBlockKind.SystemBlock:
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
            string iconKey)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Label = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "info" : iconKey;
            Operator = AutomationCompareOperator.GreaterThan;
            TargetLabel = "target";
        }

        internal string Id { get; }

        internal AutomationBlockKind Kind { get; }

        internal string Label { get; }

        internal string IconKey { get; }

        internal string TargetKey { get; private set; }

        internal string TargetLabel { get; private set; }

        internal AutomationCompareOperator Operator { get; set; }

        internal float NumericValue { get; set; }

        internal float SecondaryNumericValue { get; set; }

        internal string Comment { get; set; }

        internal void SetTarget(AutomationTarget target)
        {
            if (target == null)
                return;

            TargetKey = target.StableKey;
            TargetLabel = target.Label;
        }
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
            string toPortId)
        {
            FromNodeId = fromNodeId ?? string.Empty;
            FromPortId = fromPortId ?? string.Empty;
            ToNodeId = toNodeId ?? string.Empty;
            ToPortId = toPortId ?? string.Empty;
        }

        internal string FromNodeId { get; }

        internal string FromPortId { get; }

        internal string ToNodeId { get; }

        internal string ToPortId { get; }
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
            string readIdentifier,
            string conditionExpression,
            string passExpression,
            float failValue,
            IReadOnlyList<string> steps)
        {
            ControllerKey = controllerKey ?? string.Empty;
            ControllerLabel = controllerLabel ?? string.Empty;
            ReadTargetKey = readTargetKey ?? string.Empty;
            ReadTargetLabel = readTargetLabel ?? string.Empty;
            OutputTargetKey = outputTargetKey ?? string.Empty;
            OutputTargetLabel = outputTargetLabel ?? string.Empty;
            ReadIdentifier = string.IsNullOrWhiteSpace(readIdentifier) ? "target" : readIdentifier;
            ConditionExpression = conditionExpression ?? string.Empty;
            PassExpression = passExpression ?? string.Empty;
            FailValue = failValue;
            Steps = steps ?? Array.Empty<string>();
        }

        internal string ControllerKey { get; }

        internal string ControllerLabel { get; }

        internal string ReadTargetKey { get; }

        internal string ReadTargetLabel { get; }

        internal string OutputTargetKey { get; }

        internal string OutputTargetLabel { get; }

        internal string ReadIdentifier { get; }

        internal string ConditionExpression { get; }

        internal string PassExpression { get; }

        internal float FailValue { get; }

        internal IReadOnlyList<string> Steps { get; }

        internal string Summary =>
            "If " + ConditionExpression + " then set " + OutputTargetLabel + " from " + PassExpression + ".";

        internal string ToNativeCode()
        {
            return
                "if " + ConditionExpression + ":\n" +
                "    out = " + PassExpression + "\n" +
                "else:\n" +
                "    out = " + FailValue.ToString("0.###", CultureInfo.InvariantCulture) + "\n";
        }
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

            AutomationBlockNode readNode = workspace.FirstNode(AutomationBlockKind.ReadTarget);
            AutomationBlockNode compareNode = workspace.FirstNode(AutomationBlockKind.Compare);
            AutomationBlockNode scaleNode = workspace.FirstNode(AutomationBlockKind.MathScale);
            AutomationBlockNode setNode = workspace.FirstNode(AutomationBlockKind.SetTarget);
            if (readNode == null)
            {
                message = "Add a Read target block before checking ESU Blocks.";
                return false;
            }

            if (compareNode == null)
            {
                message = "Add a Compare block before checking ESU Blocks.";
                return false;
            }

            if (setNode == null)
            {
                message = "Add a Set target block before checking ESU Blocks.";
                return false;
            }

            AutomationTarget readTarget = workspace.TargetForNode(readNode);
            AutomationTarget setTarget = workspace.TargetForNode(setNode);
            if (readTarget == null)
            {
                message = "The Read target block is not connected to a live linked target.";
                return false;
            }

            if (setTarget == null)
            {
                message = "The Set target block is not connected to a live linked target.";
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

            string identifier = IdentifierForTarget(readTarget);
            string condition = identifier + " " + OperatorText(compareNode.Operator) + " " +
                               compareNode.NumericValue.ToString("0.###", CultureInfo.InvariantCulture);
            float scale = scaleNode == null ? 1f : scaleNode.NumericValue;
            string pass = Math.Abs(scale - 1f) < 0.0001f
                ? identifier
                : identifier + " * " + scale.ToString("0.###", CultureInfo.InvariantCulture);

            plan = new AutomationLoweringPlan(
                workspace.ControllerKey,
                workspace.ControllerLabel,
                readTarget.StableKey,
                readTarget.Label,
                setTarget.StableKey,
                setTarget.Label,
                identifier,
                condition,
                pass,
                0f,
                new[]
                {
                    "Create or reuse Generic Getter for " + readTarget.Label + ".",
                    "Create native Evaluator/Switch logic for " + condition + ".",
                    "Create or reuse Generic Setter for " + setTarget.Label + ".",
                    "Track generated native component ids for Revert."
                });
            message = "ESU Blocks check passed: " + plan.Summary + " No native data changed.";
            return true;
        }

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
