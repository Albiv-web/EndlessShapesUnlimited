using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Blocks;
using BrilliantSkies.Blocks.BreadBoards;
using BrilliantSkies.Blocks.BreadBoards.GenericGetter;
using BrilliantSkies.Common.Circuits;
using BrilliantSkies.Common.Circuits.ComponentTypes;
using BrilliantSkies.Common.Circuits.ComponentTypes.Inputs;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;
using CircuitComponent = BrilliantSkies.Common.Circuits.Component;
using NativeAiBreadBoardBlock = BrilliantSkies.Blocks.BreadBoards.AiBreadboard;
using NativeBasicBreadBoardBlock = BrilliantSkies.Blocks.BreadBoard;
using NativeBreadBoard = BrilliantSkies.Blocks.FtdBoard;
using NativeComment = BrilliantSkies.Common.Circuits.ComponentTypes.Inputs.Comment;
using NativeSwitch = BrilliantSkies.Common.Circuits.ComponentTypes.Switch;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed partial class AutomationBuilderSession
    {
        private const uint NativeUnselectedCode = 999999u;
        private const string AutoNamePrefix = "ESU_AB_";

        private void RefreshNativeAutomationCache()
        {
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                _links.Clear();
                _selectedLink = null;
                return;
            }

            object selectedNative = _selectedLink?.NativeComponent;
            List<AutomationLink> rebuilt = BuildNativeLinks(breadboard);
            _links.Clear();
            _links.AddRange(rebuilt);
            _nextLinkId = Math.Max(_nextLinkId, _links.Count + 1);
            if (selectedNative != null)
                _selectedLink = _links.FirstOrDefault(link => ReferenceEquals(link.NativeComponent, selectedNative));
            if (_selectedLink == null && selectedNative != null)
                _selectedBlock = _selectedBreadboard;
        }

        private bool TryCreateNativeLink(
            AutomationBlockRef source,
            AutomationBlockRef target,
            AutomationLinkKind kind,
            string property,
            out AutomationLink nativeLink,
            out string message)
        {
            nativeLink = null;
            message = null;
            AutomationBlockRef breadboardRef = kind == AutomationLinkKind.InputToBreadboard
                ? target
                : source;
            AutomationBlockRef targetRef = kind == AutomationLinkKind.InputToBreadboard
                ? source
                : target;
            if (!TryResolveNativeBreadboard(
                    breadboardRef,
                    out NativeBreadBoard breadboard,
                    out message))
            {
                return false;
            }

            if (targetRef == null ||
                !targetRef.TryGetBlock(out Block targetBlock))
            {
                message = "The linked target block is no longer available.";
                return false;
            }

            string propertyText = string.IsNullOrWhiteSpace(property)
                ? "value"
                : property.Trim();
            var context = new NativeBuildContext(breadboardRef, _links);
            string filter = EnsureExactBlockFilterName(breadboard, targetBlock, context);
            Color color = LinkColor(_nextLinkId++);
            CircuitComponent component;
            bool propertyConfigured;
            if (kind == AutomationLinkKind.InputToBreadboard)
            {
                var getter = new GenericBlockGetter();
                ConfigureGetterTarget(getter, targetBlock, filter);
                propertyConfigured = TryConfigureGetterProperty(getter, targetBlock.GetType(), propertyText);
                component = getter;
            }
            else
            {
                var setter = new GenericBlockSetter();
                ConfigureSetterTarget(setter, targetBlock, filter);
                propertyConfigured = TryConfigureSetterProperty(setter, targetBlock.GetType(), propertyText);
                component = setter;
            }

            component.OutlineColor.Us = color;
            ApplyNativeNodeRect(component, NativeAppendRect(breadboard));
            breadboard.NewPackage(component);
            nativeLink = new AutomationLink(
                unchecked((int)component.UniqueId),
                source,
                target,
                kind,
                propertyText,
                color,
                component,
                context.HasWarnings ? "native warning" : "native exact");
            if (context.HasWarnings)
                message = context.Detail;
            if (!propertyConfigured)
                message = "Automation link created, but property '" + propertyText + "' was not found on " + targetRef.Name + ". Pick a native property in the graph inspector.";
            return true;
        }

        private void RemoveNativeLink(AutomationLink link)
        {
            if (link?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard) &&
                breadboard?.Packages.Contains(component) == true)
            {
                component.Delete();
                breadboard.RemovePackage(component);
            }
        }

        private bool TryAddNativeGraphComponent(
            AutomationNodeKind kind,
            out int nativeId,
            out string message) =>
            TryAddNativeGraphComponent(kind, hasPreferredRect: false, preferredRect: Rect.zero, out nativeId, out message);

        private bool TryAddNativeGraphComponent(
            AutomationNodeKind kind,
            bool hasPreferredRect,
            Rect preferredRect,
            out int nativeId,
            out string message)
        {
            nativeId = 0;
            message = null;
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                message = "Select a live breadboard before adding graph components.";
                return false;
            }

            CircuitComponent component = CreateLooseNativeComponent(kind);
            if (component == null)
            {
                message = "Automation Builder does not know how to create that native component yet.";
                return false;
            }

            TryAutoBindNewNativeGraphComponent(kind, breadboard, component, out string autoBindMessage);
            Rect rect = hasPreferredRect ? preferredRect : NativeAppendRect(breadboard);
            if (!hasPreferredRect)
            {
                rect.width = GraphNodeWidthForKind(kind);
                rect.height = GraphNodeHeightForKind(kind);
            }
            ApplyNativeNodeRect(component, rect);
            breadboard.NewPackage(component);
            nativeId = (int)component.UniqueId;
            message = autoBindMessage;
            return true;
        }

        private bool TryAutoBindNewNativeGraphComponent(
            AutomationNodeKind kind,
            NativeBreadBoard breadboard,
            CircuitComponent component,
            out string message)
        {
            message = null;
            if (breadboard == null ||
                component == null ||
                kind != AutomationNodeKind.InputGetter &&
                kind != AutomationNodeKind.OutputSetter)
            {
                return false;
            }

            List<AutomationLink> choices = AutoBindNativeLinkChoices(breadboard, kind).ToList();
            if (choices.Count != 1)
                return false;

            AutomationLink link = choices[0];
            AutomationBlockRef targetRef = kind == AutomationNodeKind.InputGetter
                ? link.Source
                : link.Target;
            if (targetRef == null || !targetRef.TryGetBlock(out Block target))
                return false;

            var context = new NativeBuildContext(_selectedBreadboard, _links);
            string filter = EnsureExactBlockFilterName(breadboard, target, context);
            if (component is GenericBlockGetter getter)
            {
                ConfigureGetterTarget(getter, target, filter);
                TryConfigureGetterProperty(getter, target.GetType(), link.Property);
                component.OutlineColor.Us = link.Color;
                message = context.HasWarnings
                    ? context.Detail
                    : "Read block auto-filled from linked input " + targetRef.Name + ".";
                return true;
            }

            if (component is GenericBlockSetter setter)
            {
                ConfigureSetterTarget(setter, target, filter);
                TryConfigureSetterProperty(setter, target.GetType(), link.Property);
                component.OutlineColor.Us = link.Color;
                message = context.HasWarnings
                    ? context.Detail
                    : "Set block auto-filled from linked output " + targetRef.Name + ".";
                return true;
            }

            return false;
        }

        private IEnumerable<AutomationLink> AutoBindNativeLinkChoices(
            NativeBreadBoard breadboard,
            AutomationNodeKind kind)
        {
            AutomationLinkKind linkKind = kind == AutomationNodeKind.InputGetter
                ? AutomationLinkKind.InputToBreadboard
                : AutomationLinkKind.BreadboardToOutput;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationLink link in BuildNativeLinks(breadboard))
            {
                if (link == null || link.Kind != linkKind)
                    continue;

                string key = AutoBindLinkChoiceKey(link, kind);
                if (seen.Add(key))
                    yield return link;
            }
        }

        private static string AutoBindLinkChoiceKey(
            AutomationLink link,
            AutomationNodeKind kind)
        {
            AutomationBlockRef target = kind == AutomationNodeKind.InputGetter
                ? link?.Source
                : link?.Target;
            return (target?.StableKey ?? string.Empty) + "|" + (link?.Property ?? string.Empty);
        }

        private bool TryAddNativeStarterFlow(out string message)
        {
            message = null;
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                message = "Select a live breadboard before creating a starter flow.";
                return false;
            }

            List<AutomationLink> inputChoices = StarterNativeLinkChoices(breadboard, AutomationLinkKind.InputToBreadboard).ToList();
            List<AutomationLink> outputChoices = StarterNativeLinkChoices(breadboard, AutomationLinkKind.BreadboardToOutput).ToList();
            if (inputChoices.Count != 1 || outputChoices.Count != 1)
            {
                message = "Starter Flow needs exactly one linked input and one linked output. Use the block dropdowns when there are multiple choices.";
                return false;
            }

            AutomationLink inputLink = inputChoices[0];
            AutomationLink outputLink = outputChoices[0];
            if (!(inputLink.NativeComponent is GenericBlockGetter getter) ||
                !(outputLink.NativeComponent is GenericBlockSetter setter))
            {
                message = "Starter Flow could not resolve the linked native Read/Set components.";
                return false;
            }

            if (breadboard.Components.Any(component =>
                    component != null &&
                    !ReferenceEquals(component, getter) &&
                    !ReferenceEquals(component, setter)))
            {
                message = "Starter Flow starts from a clean linked graph. Remove extra breadboard components or assemble the flow manually.";
                return false;
            }

            if (inputLink.Source == null ||
                outputLink.Target == null ||
                !inputLink.Source.TryGetBlock(out Block inputTarget) ||
                !outputLink.Target.TryGetBlock(out Block outputTarget))
            {
                message = "Starter Flow could not resolve the linked input/output blocks.";
                return false;
            }

            var context = new NativeBuildContext(_selectedBreadboard, _links);
            string inputFilter = EnsureExactBlockFilterName(breadboard, inputTarget, context);
            ConfigureGetterTarget(getter, inputTarget, inputFilter);
            TryConfigureGetterProperty(getter, inputTarget.GetType(), inputLink.Property);
            getter.OutlineColor.Us = inputLink.Color;

            var thresholdComponent = (FuzzyThreshold)CreateNativeFuzzyThreshold(
                AutomationNodeKind.CompareBelowThreshold,
                "threshold 10");
            thresholdComponent.OutlineColor.Us = NodeColor(AutomationNodeKind.CompareBelowThreshold);

            var switchComponent = (NativeSwitch)CreateNativeSwitch(DefaultValue(AutomationNodeKind.IfCondition));
            switchComponent.OutlineColor.Us = NodeColor(AutomationNodeKind.IfCondition);

            var thresholdValue = (ConstantInput)CreateNativeConstant("10");
            thresholdValue.OutlineColor.Us = NodeColor(AutomationNodeKind.Constant);

            var thenValue = (ConstantInput)CreateNativeConstant("45");
            thenValue.OutlineColor.Us = NodeColor(AutomationNodeKind.Constant);

            string outputFilter = EnsureExactBlockFilterName(breadboard, outputTarget, context);
            ConfigureSetterTarget(setter, outputTarget, outputFilter);
            TryConfigureSetterProperty(setter, outputTarget.GetType(), outputLink.Property);
            setter.OutlineColor.Us = outputLink.Color;

            Rect baseRect = NativeAppendRect(breadboard);
            Rect getterRect = new Rect(baseRect.x, baseRect.y, GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.InputGetter));
            Rect thresholdComponentRect = new Rect(getterRect.x, getterRect.yMax - 2f, GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.CompareBelowThreshold));
            Rect switchRect = new Rect(thresholdComponentRect.x, thresholdComponentRect.yMax - 2f, GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.IfCondition));
            Rect setterRect = new Rect(switchRect.x, switchRect.yMax - 2f, GraphNodeWidth, GraphNodeHeightForKind(AutomationNodeKind.OutputSetter));
            Rect thresholdRect = ValueSlotRect(thresholdComponentRect, AutomationNodeKind.CompareBelowThreshold, AutomationValueSlotKind.Threshold);
            Rect constantRect = ValueSlotRect(switchRect, AutomationNodeKind.IfCondition, AutomationValueSlotKind.Pass);

            ApplyNativeNodeRect(getter, getterRect);
            ApplyNativeNodeRect(thresholdComponent, thresholdComponentRect);
            ApplyNativeNodeRect(switchComponent, switchRect);
            ApplyNativeNodeRect(thresholdValue, thresholdRect);
            ApplyNativeNodeRect(thenValue, constantRect);
            ApplyNativeNodeRect(setter, setterRect);

            breadboard.NewPackage(thresholdComponent);
            breadboard.NewPackage(switchComponent);
            breadboard.NewPackage(thresholdValue);
            breadboard.NewPackage(thenValue);

            message = context.HasWarnings
                ? context.Detail
                : "Starter Flow created: read " + inputLink.Source.Name + ", below 10, if true, set " + outputLink.Target.Name + " to 45 else 0. Press Apply to write connections.";
            return true;
        }

        private IEnumerable<AutomationLink> StarterNativeLinkChoices(
            NativeBreadBoard breadboard,
            AutomationLinkKind linkKind)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationLink link in BuildNativeLinks(breadboard))
            {
                if (link == null || link.Kind != linkKind)
                    continue;

                AutomationBlockRef target = linkKind == AutomationLinkKind.InputToBreadboard
                    ? link.Source
                    : link.Target;
                string key = (target?.StableKey ?? string.Empty) + "|" + (link.Property ?? string.Empty);
                if (seen.Add(key))
                    yield return link;
            }
        }

        private void SyncGraphFromNativeBreadboard(
            AutomationBlockRef breadboardRef,
            AutomationGraph graph)
        {
            if (graph == null)
                return;

            if (!TryResolveNativeBreadboard(
                    breadboardRef,
                    out NativeBreadBoard breadboard,
                    out _))
            {
                graph.RebuildNativeNodes(Array.Empty<AutomationGraphNode>());
                return;
            }

            List<AutomationGraphNode> nodes = breadboard.Components
                .Where(IsSupportedAutomationNativeComponent)
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .Select(component => NativeComponentToNode(breadboardRef, component))
                .ToList();
            graph.RebuildNativeNodes(nodes);
        }

        private void SyncNativeNodeRect(AutomationGraphNode node)
        {
            if (node?.NativeComponent is CircuitComponent component)
                ApplyNativeNodeRect(component, node.Rect);
        }

        private void RemoveNativeGraphNode(AutomationGraphNode node)
        {
            if (node?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard) &&
                breadboard?.Packages.Contains(component) == true)
            {
                component.Delete();
                breadboard.RemovePackage(component);
            }
        }

        private void ApplyNativeNodeEdits(
            AutomationGraphNode node,
            string label,
            string property,
            string value)
        {
            if (node == null)
                return;

            node.Label = label ?? string.Empty;
            node.Property = string.IsNullOrWhiteSpace(property) ? "value" : property;
            node.ValueText = value ?? string.Empty;
            if (!(node.NativeComponent is CircuitComponent component))
                return;

            if (component is GenericBlockGetter getter)
            {
                Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
                if (blockType != null)
                    TryConfigureGetterProperty(getter, blockType, node.Property);
            }
            else if (component is GenericBlockSetter setter)
            {
                Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
                if (blockType != null)
                    TryConfigureSetterProperty(setter, blockType, node.Property);
            }
            else if (component is Evaluator evaluator)
            {
                evaluator.Expression.Us = string.IsNullOrWhiteSpace(node.ValueText)
                    ? node.Label
                    : node.ValueText;
            }
            else if (component is NativeSwitch switchComponent)
            {
                if (node.Kind == AutomationNodeKind.IfCondition)
                {
                    ParseIfSwitchValue(
                        node.ValueText,
                        0.5f,
                        switchComponent.FailValue.Us,
                        out _,
                        out float failValue);
                    switchComponent.Threshold.Us = 0.5f;
                    switchComponent.FailValue.Us = failValue;
                }
                else
                {
                    ParseIfSwitchValue(
                        node.ValueText,
                        switchComponent.Threshold.Us,
                        switchComponent.FailValue.Us,
                        out float threshold,
                        out float failValue);
                    switchComponent.Threshold.Us = threshold;
                    switchComponent.FailValue.Us = failValue;
                }
            }
            else if (component is LogicGate logicGate &&
                     IsLogicGateKind(node.Kind))
            {
                logicGate.SelectedGate.Us = LogicGateType(node.Kind);
                logicGate.TrueLogic.Us = TrueType.GreaterZero;
            }
            else if (component is FuzzyThreshold fuzzyThreshold &&
                     IsFuzzyThresholdKind(node.Kind))
            {
                ApplyFuzzyThresholdEdits(fuzzyThreshold, node.Kind, node.ValueText);
            }
            else if (component is MaxMin maxMin &&
                     IsMaxMinKind(node.Kind))
            {
                maxMin.SelectedOp.Us = MaxMinOpType(node.Kind);
            }
            else if (component is ConstantInput constant)
            {
                constant.Type.Us = ConstantInput.ConstantType.ItsAFloat;
                constant.InputValue.Us = ParseFloat(node.ValueText, constant.InputValue.Us);
            }
            else if (component is RandomInput random)
            {
                ParseRange(node.ValueText, random.RandomLimits.Lower, random.RandomLimits.Upper, out float lower, out float upper);
                random.RandomLimits.Lower = lower;
                random.RandomLimits.Upper = upper;
            }
            else if (component is Clamp clamp)
            {
                ParseRange(node.ValueText, clamp.MinMax.Lower, clamp.MinMax.Upper, out float lower, out float upper);
                clamp.MinMax.Lower = lower;
                clamp.MinMax.Upper = upper;
            }
            else if (component is Delay delay)
            {
                delay.DelayTime.Us = ParseSeconds(node.ValueText, delay.DelayTime.Us);
            }
            else if (component is NativeComment comment)
            {
                string commentText = string.IsNullOrWhiteSpace(node.ValueText)
                    ? node.Label
                    : node.ValueText;
                comment.InputValue.Us = node.Kind == AutomationNodeKind.Forever && !IsForeverComment(commentText)
                    ? "Forever: " + commentText
                    : commentText;
            }
        }

        private bool TryApplyNativeLinkTarget(
            AutomationGraphNode node,
            AutomationLink link,
            out string message)
        {
            message = null;
            if (node == null || link == null)
            {
                message = "Select a native graph block and linked target first.";
                return false;
            }

            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                message = "Select a live breadboard before assigning a linked target.";
                return false;
            }

            if (node.NativeComponent is GenericBlockGetter getter &&
                link.Kind == AutomationLinkKind.InputToBreadboard)
            {
                if (link.Source == null || !link.Source.TryGetBlock(out Block target))
                {
                    message = "The selected input target is no longer available.";
                    return false;
                }

                var context = new NativeBuildContext(_selectedBreadboard, _links);
                string filter = EnsureExactBlockFilterName(breadboard, target, context);
                ConfigureGetterTarget(getter, target, filter);
                TryConfigureGetterProperty(getter, target.GetType(), link.Property);
                node.Label = NativeNodeLabel(getter);
                node.Property = NativeNodeProperty(getter);
                node.ValueText = NativeNodeValue(getter);
                message = context.HasWarnings
                    ? context.Detail
                    : "Automation input block now reads " + link.Source.Name + ".";
                return true;
            }

            if (node.NativeComponent is GenericBlockSetter setter &&
                link.Kind == AutomationLinkKind.BreadboardToOutput)
            {
                if (link.Target == null || !link.Target.TryGetBlock(out Block target))
                {
                    message = "The selected output target is no longer available.";
                    return false;
                }

                var context = new NativeBuildContext(_selectedBreadboard, _links);
                string filter = EnsureExactBlockFilterName(breadboard, target, context);
                ConfigureSetterTarget(setter, target, filter);
                TryConfigureSetterProperty(setter, target.GetType(), link.Property);
                node.Label = NativeNodeLabel(setter);
                node.Property = NativeNodeProperty(setter);
                node.ValueText = NativeNodeValue(setter);
                message = context.HasWarnings
                    ? context.Detail
                    : "Automation output block now sets " + link.Target.Name + ".";
                return true;
            }

            message = "That linked target does not match the selected block direction.";
            return false;
        }

        private IEnumerable<string> NativePropertyOptionsForNode(AutomationGraphNode node)
        {
            if (node?.NativeComponent is GenericBlockGetter getter)
            {
                Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
                return blockType == null
                    ? Enumerable.Empty<string>()
                    : EnumerateGetterPropertyLabels(blockType);
            }

            if (node?.NativeComponent is GenericBlockSetter setter)
            {
                Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
                return blockType == null
                    ? Enumerable.Empty<string>()
                    : EnumerateSetterPropertyLabels(blockType);
            }

            return Enumerable.Empty<string>();
        }

        private bool ApplyGraphToNativeBoard()
        {
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                NotifyNativeApply("Select a live breadboard before applying an automation graph.", EsuHudNotificationKind.Warning);
                return false;
            }

            NativeApplyResult result = ValidateAndConnectNativeGraph(breadboard);
            NotifyNativeApply(result.Message, result.Kind, result.Detail);
            if (result.Kind == EsuHudNotificationKind.Info)
            {
                ClearAutomationDirty();
                RefreshNativeAutomationCache();
                return true;
            }

            return false;
        }

        private NativeApplyResult ValidateAndConnectNativeGraph(NativeBreadBoard breadboard)
        {
            AutomationGraph graph = _selectedBreadboard == null ? null : GraphFor(_selectedBreadboard);
            List<string> readinessIssues = NativeGraphReadinessIssues(graph).ToList();
            if (readinessIssues.Count > 0)
            {
                return NativeApplyResult.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Apply blocked: fix {0:N0} automation readiness issue{1} first.",
                        readinessIssues.Count,
                        readinessIssues.Count == 1 ? string.Empty : "s"),
                    string.Join("\n", readinessIssues.Take(12).ToArray()));
            }

            List<CircuitComponent> ordered = breadboard.Components
                .Where(IsSupportedAutomationNativeComponent)
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .ToList();
            if (ordered.Count == 0)
                return NativeApplyResult.Warning("The native breadboard graph has no components to connect.");

            int connected = 0;
            int alreadyConnected = 0;
            int valueConnections = 0;
            List<List<CircuitComponent>> flowChains = OrderedNativeFlowChains(ordered);
            foreach (List<CircuitComponent> flow in flowChains)
            {
                for (int index = 0; index < flow.Count - 1; index++)
                {
                    CircuitComponent from = flow[index];
                    CircuitComponent to = flow[index + 1];

                    if (to is NativeSwitch switchTo)
                    {
                        TryConnectComponentToInputAt(breadboard, from, switchTo, 1, ref connected, ref alreadyConnected);
                        continue;
                    }

                    if (IsNativeConnected(from, to))
                    {
                        alreadyConnected++;
                        continue;
                    }

                    if (breadboard.Connect(from, to))
                        connected++;
                }
            }

            foreach (CircuitComponent host in ordered.Where(component => AcceptsValueSlot(NativeKind(component))))
            {
                if (host is NativeSwitch switchComponent)
                {
                    AutomationNodeKind switchKind = NativeKind(host);
                    if (switchKind == AutomationNodeKind.IfCondition)
                    {
                        switchComponent.Threshold.Us = 0.5f;
                    }
                    else
                    {
                        CircuitComponent thresholdValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Threshold);
                        if (TryApplySwitchThresholdValue(switchComponent, thresholdValue))
                            valueConnections++;
                    }

                    CircuitComponent passValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInput(breadboard, passValue, switchComponent.Pass, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent elseValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Else);
                    if (TryApplySwitchElseValue(switchComponent, elseValue))
                        valueConnections++;
                    continue;
                }

                if (host is Evaluator evaluator &&
                    IsMathEvaluatorKind(NativeKind(host)))
                {
                    AutomationNodeKind evaluatorKind = NativeKind(host);
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, evaluator, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent operandValue = FindSnappedValueComponent(host, ordered, MathOperandSlotKind(evaluatorKind));
                    if (operandValue == null)
                    {
                        if (TryApplyEvaluatorMathOperand(evaluator, operandValue, evaluatorKind))
                            valueConnections++;
                    }
                    else
                    {
                        evaluator.Expression.Us = MathExpressionInputText(evaluatorKind);
                        if (TryConnectComponentToInputAt(breadboard, operandValue, evaluator, 1, ref connected, ref alreadyConnected))
                            valueConnections++;
                    }

                    continue;
                }

                if (host is LogicGate logicGate &&
                    IsLogicGateKind(NativeKind(host)))
                {
                    AutomationNodeKind logicKind = NativeKind(host);
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, logicGate, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    if (logicKind != AutomationNodeKind.LogicNot)
                    {
                        CircuitComponent secondValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.LogicB);
                        if (TryConnectComponentToInputAt(breadboard, secondValue, logicGate, 1, ref connected, ref alreadyConnected))
                            valueConnections++;
                    }

                    continue;
                }

                if (host is FuzzyThreshold fuzzyThreshold &&
                    IsFuzzyThresholdKind(NativeKind(host)))
                {
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent thresholdValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Threshold);
                    if (TryApplyFuzzyThresholdValue(fuzzyThreshold, thresholdValue))
                        valueConnections++;

                    continue;
                }

                if (host is MaxMin maxMin &&
                    IsMaxMinKind(NativeKind(host)))
                {
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, maxMin, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent secondValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.MathB);
                    if (TryConnectComponentToInputAt(breadboard, secondValue, maxMin, 1, ref connected, ref alreadyConnected))
                        valueConnections++;

                    continue;
                }

                if (host is Clamp clamp)
                {
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent minValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Min);
                    if (TryApplyClampMinimum(clamp, minValue))
                        valueConnections++;

                    CircuitComponent maxValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Max);
                    if (TryApplyClampMaximum(clamp, maxValue))
                        valueConnections++;
                    continue;
                }

                if (host is Delay delay)
                {
                    CircuitComponent sourceValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent secondsValue = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Seconds);
                    if (TryApplyDelaySeconds(delay, secondsValue))
                        valueConnections++;
                    continue;
                }

                CircuitComponent value = FindSnappedValueComponent(host, ordered, AutomationValueSlotKind.Pass);
                if (TryConnectComponentToHost(breadboard, value, host, ref connected, ref alreadyConnected))
                    valueConnections++;
            }

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Native graph checked: {0:N0} new connection{1}, {2:N0} already connected, {3:N0} value slot{4}.",
                connected,
                connected == 1 ? string.Empty : "s",
                alreadyConnected,
                valueConnections,
                valueConnections == 1 ? string.Empty : "s");
            return new NativeApplyResult(
                message,
                EsuHudNotificationKind.Info,
                "Apply is idempotent: it connects visible snapped/native-wired stack chains top-to-bottom and snapped/native-wired value blocks into native inputs without appending duplicates.");
        }

        private IEnumerable<string> NativeGraphReadinessIssues(AutomationGraph graph)
        {
            if (graph == null || graph.Nodes.Count == 0)
                yield break;

            List<List<AutomationGraphNode>> flowChains = OrderedGraphFlowChains(graph);
            foreach (string issue in ValueSocketConflictIssues(graph))
                yield return issue;

            foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
            {
                if (node == null)
                    continue;

                foreach (string issue in NativeNodeReadinessIssues(graph, flowChains, node))
                    yield return issue;
            }
        }

        private static IEnumerable<string> ValueSocketConflictIssues(AutomationGraph graph)
        {
            if (graph == null)
                yield break;

            foreach (AutomationGraphNode host in graph.Nodes.Where(node => node != null && AcceptsValueSlot(node.Kind)))
            {
                foreach (AutomationValueSlotKind slotKind in ValueSlotKinds(host.Kind))
                {
                    List<AutomationGraphNode> candidates = ValueSlotCandidateNodes(graph, host, slotKind);
                    if (candidates.Count > 1)
                    {
                        yield return ShortText(BlockSentenceTitle(host), 38) + " " +
                                     ValueSlotLabel(host.Kind, slotKind) +
                                     " socket has multiple nearby value blocks; move extra value blocks away.";
                    }
                }
            }

            foreach (IGrouping<AutomationGraphNode, Tuple<AutomationGraphNode, AutomationValueSlotKind, AutomationGraphNode>> claim in
                     ValueSlotAssignments(graph).GroupBy(tuple => tuple.Item3))
            {
                if (claim.Count() <= 1)
                    continue;

                yield return ShortText(BlockSentenceTitle(claim.Key), 38) +
                             " is close enough to feed multiple value sockets; move it so only one socket owns it.";
            }
        }

        private IEnumerable<string> NativeNodeReadinessIssues(
            AutomationGraph graph,
            IReadOnlyList<List<AutomationGraphNode>> flowChains,
            AutomationGraphNode node)
        {
            string nodeName = ShortText(BlockSentenceTitle(node), 38);
            if (node.NativeComponent is GenericBlockGetter getter)
            {
                if (!HasNativeBlockTarget(getter.BlockTypeName.Us))
                    yield return nodeName + " has no linked input target; click its target slot or link an input block.";
                if (!GetterHasNativeProperty(getter))
                    yield return nodeName + " has no readable native property selected.";
                yield break;
            }

            if (node.NativeComponent is GenericBlockSetter setter)
            {
                bool hasIncomingSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedValue = SnappedValueForHost(graph, node) != null;
                if (!HasNativeBlockTarget(setter.BlockTypeName.Us))
                    yield return nodeName + " has no linked output target; click its target slot or link an output block.";
                if (!SetterHasNativeProperty(setter))
                    yield return nodeName + " has no writable native property selected.";
                if (!hasIncomingSignal && !hasSnappedValue)
                    yield return nodeName + " has no incoming signal or snapped/native-wired value block.";
                if (hasIncomingSignal && hasSnappedValue)
                    yield return nodeName + " has both a stack signal and a value socket input; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is NativeSwitch)
            {
                if (PreviousFlowNode(flowChains, node) == null)
                    yield return nodeName + " has no incoming signal to compare; place a read/value-producing block above it.";
                if (SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) == null)
                    yield return nodeName + " has no then value in the switch pass socket.";
                if (!BodyChildrenForHost(graph, node).Any() && NextFlowNode(flowChains, node) == null)
                    yield return nodeName + " switch output does not drive any action block yet; place a Set block in its body or below it.";
                yield break;
            }

            if (node.NativeComponent is LogicGate &&
                IsLogicGateKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first boolean input; place a read/value-producing block above it or snap/wire one into its " + LogicFirstInputLabel(node.Kind) + " socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and a " + LogicFirstInputLabel(node.Kind) + " socket input; remove one input path for clearer lowering.";
                if (node.Kind != AutomationNodeKind.LogicNot &&
                    SnappedValueForHost(graph, node, AutomationValueSlotKind.LogicB) == null)
                {
                    yield return nodeName + " has no second boolean input in the b socket.";
                }
                yield break;
            }

            if (node.NativeComponent is FuzzyThreshold &&
                IsFuzzyThresholdKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no signal to compare; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is MaxMin &&
                IsMaxMinKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first numeric input; place a read/value-producing block above it or snap/wire one into its a socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an A socket input; remove one input path for clearer lowering.";
                if (SnappedValueForHost(graph, node, AutomationValueSlotKind.MathB) == null)
                    yield return nodeName + " has no second numeric input in the b socket.";
                yield break;
            }

            if (node.NativeComponent is Evaluator &&
                IsMathEvaluatorKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Clamp)
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Delay &&
                node.Kind == AutomationNodeKind.Smooth)
            {
                bool hasStackSignal = PreviousFlowNode(flowChains, node) != null;
                bool hasSnappedSource = SnappedValueForHost(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.Forever &&
                !BodyChildrenForHost(graph, node).Any())
            {
                yield return nodeName + " has no action blocks inside its body.";
            }
        }

        private static bool HasNativeBlockTarget(string blockTypeName)
        {
            return !string.IsNullOrWhiteSpace(blockTypeName);
        }

        private static bool GetterHasNativeProperty(GenericBlockGetter getter)
        {
            return getter != null &&
                   (getter.ReadableAttributeId.Us != NativeUnselectedCode ||
                    getter.BlockPropertyId.Us != NativeUnselectedCode);
        }

        private static bool SetterHasNativeProperty(GenericBlockSetter setter)
        {
            return setter != null &&
                   setter.BlockPropertyId.Us != NativeUnselectedCode;
        }

        private static AutomationGraphNode PreviousFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            return PreviousFlowNode(OrderedGraphFlowChains(graph), node);
        }

        private static AutomationGraphNode PreviousFlowNode(
            IReadOnlyList<List<AutomationGraphNode>> flowChains,
            AutomationGraphNode node)
        {
            if (flowChains == null || node == null)
                return null;

            foreach (List<AutomationGraphNode> flow in flowChains)
            {
                int index = FlowNodeIndex(flow, node);
                if (index > 0)
                    return flow[index - 1];
            }

            return null;
        }

        private static AutomationGraphNode NextFlowNode(
            IReadOnlyList<List<AutomationGraphNode>> flowChains,
            AutomationGraphNode node)
        {
            if (flowChains == null || node == null)
                return null;

            foreach (List<AutomationGraphNode> flow in flowChains)
            {
                int index = FlowNodeIndex(flow, node);
                if (index >= 0 && index < flow.Count - 1)
                    return flow[index + 1];
            }

            return null;
        }

        private static int FlowNodeIndex(
            IReadOnlyList<AutomationGraphNode> flow,
            AutomationGraphNode node)
        {
            if (flow == null || node == null)
                return -1;

            for (int index = 0; index < flow.Count; index++)
            {
                if (ReferenceEquals(flow[index], node))
                    return index;
            }

            return -1;
        }

        private void NotifyNativeApply(
            string message,
            EsuHudNotificationKind kind,
            string detail = null)
        {
            EsuHudNotifications.ShowSystem(
                "Automation Builder",
                message,
                kind,
                detail);
        }

        private List<AutomationLink> BuildNativeLinks(NativeBreadBoard breadboard)
        {
            var links = new List<AutomationLink>();
            if (breadboard == null || _selectedBreadboard == null)
                return links;

            foreach (CircuitComponent component in breadboard.Components.ToArray())
            {
                if (component is GenericBlockGetter getter)
                {
                    AutomationBlockRef target = ResolveNativeTargetBlock(breadboard, getter, getter.BlockTypeName.Us, getter.BlockFilter.Us, out string status);
                    if (target == null)
                        continue;

                    links.Add(new AutomationLink(
                        unchecked((int)component.UniqueId),
                        target,
                        _selectedBreadboard,
                        AutomationLinkKind.InputToBreadboard,
                        NativePropertyLabel(getter),
                        NativeComponentColor(component, AutomationNodeKind.InputGetter),
                        component,
                        status));
                }
                else if (component is GenericBlockSetter setter)
                {
                    AutomationBlockRef target = ResolveNativeTargetBlock(breadboard, setter, setter.BlockTypeName.Us, setter.BlockFilter.Us, out string status);
                    if (target == null)
                        continue;

                    links.Add(new AutomationLink(
                        unchecked((int)component.UniqueId),
                        _selectedBreadboard,
                        target,
                        AutomationLinkKind.BreadboardToOutput,
                        NativePropertyLabel(setter),
                        NativeComponentColor(component, AutomationNodeKind.OutputSetter),
                        component,
                        status));
                }
            }

            return links;
        }

        private AutomationGraphNode NativeComponentToNode(
            AutomationBlockRef breadboardRef,
            CircuitComponent component)
        {
            AutomationNodeKind kind = NativeKind(component);
            Rect rect = NativeComponentRect(component);
            return new AutomationGraphNode(
                (int)component.UniqueId,
                kind,
                rect,
                NativeNodeLabel(component),
                NativeNodeProperty(component),
                NativeNodeValue(component),
                component);
        }

        private CircuitComponent CreateLooseNativeComponent(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                    return new GenericBlockGetter();
                case AutomationNodeKind.OutputSetter:
                    return new GenericBlockSetter();
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                    return CreateNativeSwitch(DefaultValue(kind));
                case AutomationNodeKind.Constant:
                    return CreateNativeConstant(_constantText);
                case AutomationNodeKind.Random:
                    return CreateNativeRandom("0..1");
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                    return CreateNativeLogicGate(kind);
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                    return CreateNativeFuzzyThreshold(kind, DefaultValue(kind));
                case AutomationNodeKind.MathAdd:
                    return CreateNativeEvaluator("a + 0");
                case AutomationNodeKind.MathSubtract:
                    return CreateNativeEvaluator("a - 0");
                case AutomationNodeKind.MathMultiply:
                    return CreateNativeEvaluator("a * 1");
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                    return CreateNativeMaxMin(kind);
                case AutomationNodeKind.Clamp:
                    return CreateNativeClamp("0..100");
                case AutomationNodeKind.Smooth:
                    return CreateNativeDelay("0.25s");
                case AutomationNodeKind.Forever:
                    return CreateNativeComment("Forever: native breadboard evaluates continuously");
                case AutomationNodeKind.Comment:
                    return CreateNativeComment("Note");
                default:
                    return null;
            }
        }

        private static CircuitComponent CreateNativeEvaluator(string expression)
        {
            var evaluator = new Evaluator();
            evaluator.Expression.Us = expression;
            return evaluator;
        }

        private static CircuitComponent CreateNativeSwitch(string value)
        {
            var switchComponent = new NativeSwitch();
            ParseIfSwitchValue(value, 10f, 0f, out float threshold, out float failValue);
            switchComponent.Threshold.Us = threshold;
            switchComponent.FailValue.Us = failValue;
            return switchComponent;
        }

        private static CircuitComponent CreateNativeConstant(string value)
        {
            var constant = new ConstantInput();
            constant.Type.Us = ConstantInput.ConstantType.ItsAFloat;
            constant.InputValue.Us = ParseFloat(value, 0f);
            return constant;
        }

        private static CircuitComponent CreateNativeRandom(string range)
        {
            var random = new RandomInput();
            ParseRange(range, 0f, 1f, out float lower, out float upper);
            random.RandomLimits.Lower = lower;
            random.RandomLimits.Upper = upper;
            return random;
        }

        private static CircuitComponent CreateNativeLogicGate(AutomationNodeKind kind)
        {
            var logicGate = new LogicGate();
            logicGate.SelectedGate.Us = LogicGateType(kind);
            logicGate.TrueLogic.Us = TrueType.GreaterZero;
            return logicGate;
        }

        private static CircuitComponent CreateNativeFuzzyThreshold(
            AutomationNodeKind kind,
            string value)
        {
            var fuzzyThreshold = new FuzzyThreshold();
            ApplyFuzzyThresholdEdits(fuzzyThreshold, kind, value);
            return fuzzyThreshold;
        }

        private static void ApplyFuzzyThresholdEdits(
            FuzzyThreshold fuzzyThreshold,
            AutomationNodeKind kind,
            string value)
        {
            if (fuzzyThreshold == null)
                return;

            float threshold = ParseThresholdValue(value, 10f);
            fuzzyThreshold.Above.Us = kind != AutomationNodeKind.CompareBelowThreshold;
            fuzzyThreshold.ThresholdLimits.Us = new Vector2(threshold, threshold);
        }

        private static float ParseThresholdValue(
            string value,
            float fallback)
        {
            List<float> numbers = ExtractFloatTokens(value).ToList();
            return numbers.Count > 0
                ? numbers[0]
                : fallback;
        }

        private static float FuzzyThresholdValue(FuzzyThreshold fuzzyThreshold)
        {
            if (fuzzyThreshold == null)
                return 10f;

            return (fuzzyThreshold.ThresholdLimits.Lower + fuzzyThreshold.ThresholdLimits.Upper) * 0.5f;
        }

        private static CircuitComponent CreateNativeMaxMin(AutomationNodeKind kind)
        {
            var maxMin = new MaxMin();
            maxMin.SelectedOp.Us = MaxMinOpType(kind);
            return maxMin;
        }

        private static CircuitComponent CreateNativeClamp(string range)
        {
            var clamp = new Clamp();
            ParseRange(range, 0f, 100f, out float lower, out float upper);
            clamp.MinMax.Lower = lower;
            clamp.MinMax.Upper = upper;
            return clamp;
        }

        private static CircuitComponent CreateNativeDelay(string seconds)
        {
            var delay = new Delay();
            delay.DelayTime.Us = ParseSeconds(seconds, 0.25f);
            return delay;
        }

        private static CircuitComponent CreateNativeComment(string text)
        {
            var comment = new NativeComment();
            comment.InputValue.Us = text;
            return comment;
        }

        private bool TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
        {
            return TryResolveNativeBreadboard(
                _selectedBreadboard,
                out breadboard,
                out _);
        }

        private bool TryResolveNativeBreadboard(
            AutomationBlockRef breadboardRef,
            out NativeBreadBoard breadboard,
            out string message)
        {
            breadboard = null;
            message = null;
            if (breadboardRef == null)
            {
                message = "Select a breadboard before using native automation.";
                return false;
            }

            if (!breadboardRef.IsBreadboard)
            {
                message = "The selected block is not a breadboard.";
                return false;
            }

            if (!breadboardRef.TryGetBlock(out Block block))
            {
                message = "The selected breadboard block is no longer available.";
                return false;
            }

            if (!TryResolveNativeBreadboardBoard(block, out breadboard))
            {
                message = breadboardRef.Name + " does not expose an editable native breadboard board.";
                return false;
            }

            return true;
        }

        private static bool TryResolveNativeBreadboardBoard(
            Block block,
            out NativeBreadBoard breadboard)
        {
            breadboard = null;
            if (block == null || block.IsDeleted)
                return false;

            if (block is NativeBasicBreadBoardBlock basicBreadboard)
            {
                breadboard = basicBreadboard.Board;
                return breadboard != null;
            }

            if (block is NativeAiBreadBoardBlock aiBreadboard)
            {
                breadboard = aiBreadboard.Board;
                return breadboard != null;
            }

            try
            {
                PropertyInfo boardProperty = block.GetType().GetProperty(
                    "Board",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (boardProperty?.GetValue(block, null) is NativeBreadBoard reflectedBreadboard)
                {
                    breadboard = reflectedBreadboard;
                    return true;
                }
            }
            catch
            {
                // Unknown block types are treated as non-editable breadboards.
            }

            return false;
        }

        private static void ConfigureGetterTarget(
            GenericBlockGetter getter,
            Block target,
            string filter)
        {
            getter.BlockType = target.GetType();
            getter.BlockTypeName.Us = target.GetType().Name;
            getter.BlockFilter.Us = filter ?? string.Empty;
            getter.PotentiallyAffectedBlocks.Clear();
            getter.PotentiallyAffectedBlocks.Add(target);
        }

        private static void ConfigureSetterTarget(
            GenericBlockSetter setter,
            Block target,
            string filter)
        {
            setter.BlockType = target.GetType();
            setter.BlockTypeName.Us = target.GetType().Name;
            setter.BlockFilter.Us = filter ?? string.Empty;
            setter.PotentiallyAffectedBlocks.Clear();
            setter.PotentiallyAffectedBlocks.Add(new BlockStub(target));
        }

        private string EnsureExactBlockFilterName(
            NativeBreadBoard breadboard,
            Block target,
            NativeBuildContext context)
        {
            string existing = CurrentCustomName(target);
            if (!string.IsNullOrWhiteSpace(existing) &&
                IsUniqueBlockName(breadboard, target, existing))
            {
                return existing;
            }

            string generated = GenerateStableBlockName(target);
            int suffix = 2;
            string candidate = generated;
            while (!IsUniqueBlockName(breadboard, target, candidate))
                candidate = generated + "_" + suffix++.ToString(CultureInfo.InvariantCulture);

            try
            {
                target.IdSet.Name.Us = candidate;
            }
            catch (Exception exception)
            {
                context.Warn("Could not auto-name linked block for exact vanilla filtering: " + exception.Message);
                return existing ?? string.Empty;
            }

            context.Warn("Auto-named linked block '" + AutomationBreadboardCatalog.BlockName(target) + "' as " + candidate + " for exact vanilla filtering.");
            return candidate;
        }

        private static string CurrentCustomName(Block block)
        {
            try
            {
                string name = block?.IdSet?.Name;
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GenerateStableBlockName(Block target)
        {
            Vector3i cell = target.LocalPosition;
            return AutoNamePrefix +
                   target.GetType().Name + "_" +
                   cell.x.ToString(CultureInfo.InvariantCulture) + "_" +
                   cell.y.ToString(CultureInfo.InvariantCulture) + "_" +
                   cell.z.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsUniqueBlockName(
            NativeBreadBoard breadboard,
            Block target,
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            int matches = EnumerateConstructBlocks(breadboard)
                .Count(block =>
                    block != null &&
                    !block.IsDeleted &&
                    !ReferenceEquals(block, target) &&
                    block.GetType() == target.GetType() &&
                    string.Equals(CurrentCustomName(block), name, StringComparison.Ordinal));
            return matches == 0;
        }

        private static IEnumerable<Block> EnumerateConstructBlocks(NativeBreadBoard breadboard)
        {
            if (breadboard?.Construct == null)
                yield break;

            foreach (Block block in breadboard.Construct.AllBasics.AliveAndDead.Blocks)
                yield return block;
            foreach (SubConstruct subConstruct in breadboard.Construct.AllBasics.AllSubconstructsBelowUs)
            {
                foreach (Block block in subConstruct.AllBasics.AliveAndDead.Blocks)
                    yield return block;
            }
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockGetter getter,
            string blockTypeName,
            string filter,
            out string status)
        {
            Block live = getter.PotentiallyAffectedBlocks.FirstOrDefault(block => block != null && !block.IsDeleted);
            if (live != null)
            {
                status = ExactnessStatus(breadboard, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(breadboard, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockSetter setter,
            string blockTypeName,
            string filter,
            out string status)
        {
            Block live = setter.PotentiallyAffectedBlocks
                .Select(stub => stub?.Block)
                .FirstOrDefault(block => block != null && !block.IsDeleted);
            if (live != null)
            {
                status = ExactnessStatus(breadboard, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(breadboard, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            string blockTypeName,
            string filter,
            out string status)
        {
            List<Block> candidates = EnumerateConstructBlocks(breadboard)
                .Where(block =>
                    block != null &&
                    !block.IsDeleted &&
                    string.Equals(block.GetType().Name, blockTypeName, StringComparison.Ordinal) &&
                    MatchesNativeFilter(block, filter))
                .ToList();
            status = candidates.Count == 1 ? "native exact" : "native broad";
            Block target = candidates.FirstOrDefault();
            return target == null ? null : BlockRefFromBlock(target);
        }

        private static bool MatchesNativeFilter(Block block, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string customName = CurrentCustomName(block);
            if (string.Equals(customName, filter, StringComparison.Ordinal))
                return true;

            try
            {
                return (block.IdSet?.Name?.ToString() ?? string.Empty).Contains(filter);
            }
            catch
            {
                return false;
            }
        }

        private static string ExactnessStatus(
            NativeBreadBoard breadboard,
            Block target,
            string filter)
        {
            return !string.IsNullOrWhiteSpace(filter) && IsUniqueBlockName(breadboard, target, filter)
                ? "native exact"
                : "native broad";
        }

        private AutomationBlockRef BlockRefFromBlock(Block block)
        {
            AllConstruct construct = null;
            try
            {
                construct = block.GetConstructableOrSubConstructable() as AllConstruct;
            }
            catch
            {
                construct = null;
            }

            construct = construct ?? _selectedBreadboard?.Construct;
            return new AutomationBlockRef(
                construct,
                block.LocalPosition,
                AutomationBreadboardCatalog.BlockName(block),
                AutomationBreadboardCatalog.IsBreadboardBlock(block));
        }

        private static Rect NativeAppendRect(NativeBreadBoard breadboard)
        {
            int count = breadboard?.Components.Count ?? 0;
            int index = Math.Max(0, count);
            return new Rect(
                80f + (index % 2) * (GraphNodeWidth + 46f),
                48f + index * 128f,
                GraphNodeWidth,
                GraphNodeHeight);
        }

        private static Rect NativeComponentRect(CircuitComponent component)
        {
            AutomationNodeKind kind = NativeKind(component);
            bool valueFootprint = NativeComponentUsesValueFootprint(component);
            float minWidth = valueFootprint
                ? GraphValueNodeWidth
                : GraphNodeWidthForKind(kind);
            float minHeight = valueFootprint
                ? BlockShape(kind) == AutomationBlockShape.Value
                    ? GraphNodeHeightForKind(kind)
                    : GraphNodeHeightForKind(AutomationNodeKind.Constant)
                : GraphNodeHeightForKind(kind);
            float width = Mathf.Max(minWidth, component.Width.Us);
            float height = Mathf.Max(minHeight, component.Height.Us);
            return new Rect(
                component.X.Us - width * 0.5f,
                component.Y.Us - height * 0.5f,
                width,
                height);
        }

        private static void ApplyNativeNodeRect(
            CircuitComponent component,
            Rect rect)
        {
            component.X.Us = rect.center.x;
            component.Y.Us = rect.center.y;
            component.Width.Us = rect.width;
            component.Height.Us = rect.height;
        }

        private static AutomationNodeKind NativeKind(CircuitComponent component)
        {
            if (component is GenericBlockGetter)
                return AutomationNodeKind.InputGetter;
            if (component is GenericBlockSetter)
                return AutomationNodeKind.OutputSetter;
            if (component is NativeSwitch switchComponent)
                return NativeSwitchKind(switchComponent);
            if (component is LogicGate logicGate)
                return NativeLogicGateKind(logicGate);
            if (component is FuzzyThreshold fuzzyThreshold)
                return NativeFuzzyThresholdKind(fuzzyThreshold);
            if (component is MaxMin maxMin)
                return NativeMaxMinKind(maxMin);
            if (component is ConstantInput)
                return AutomationNodeKind.Constant;
            if (component is RandomInput)
                return AutomationNodeKind.Random;
            if (component is Clamp)
                return AutomationNodeKind.Clamp;
            if (component is Delay)
                return AutomationNodeKind.Smooth;
            if (component is NativeComment comment)
                return IsForeverComment(comment.InputValue.Us)
                    ? AutomationNodeKind.Forever
                    : AutomationNodeKind.Comment;
            if (component is Evaluator evaluator)
                return NativeEvaluatorKind(evaluator);
            return AutomationNodeKind.Comment;
        }

        private static bool IsSupportedAutomationNativeComponent(CircuitComponent component)
        {
            if (component == null)
                return false;

            if (component is GenericBlockGetter ||
                component is GenericBlockSetter ||
                component is NativeSwitch ||
                component is LogicGate ||
                component is FuzzyThreshold ||
                component is MaxMin ||
                component is ConstantInput ||
                component is RandomInput ||
                component is Clamp ||
                component is Delay ||
                component is NativeComment)
            {
                return true;
            }

            return component is Evaluator evaluator &&
                   IsSupportedNativeEvaluator(evaluator);
        }

        private static AutomationNodeKind NativeLogicGateKind(LogicGate logicGate)
        {
            if (logicGate == null)
                return AutomationNodeKind.LogicAnd;

            switch (logicGate.SelectedGate.Us)
            {
                case GateType.NOT:
                    return AutomationNodeKind.LogicNot;
                case GateType.OR:
                    return AutomationNodeKind.LogicOr;
                case GateType.XOR:
                    return AutomationNodeKind.LogicXor;
                case GateType.NAND:
                    return AutomationNodeKind.LogicNand;
                case GateType.NOR:
                    return AutomationNodeKind.LogicNor;
                case GateType.XNOR:
                    return AutomationNodeKind.LogicXnor;
                default:
                    return AutomationNodeKind.LogicAnd;
            }
        }

        private static AutomationNodeKind NativeSwitchKind(NativeSwitch switchComponent)
        {
            return switchComponent != null &&
                   Mathf.Abs(switchComponent.Threshold.Us - 0.5f) <= 0.001f
                ? AutomationNodeKind.IfCondition
                : AutomationNodeKind.IfLessThan;
        }

        private static GateType LogicGateType(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.LogicNot:
                    return GateType.NOT;
                case AutomationNodeKind.LogicOr:
                    return GateType.OR;
                case AutomationNodeKind.LogicXor:
                    return GateType.XOR;
                case AutomationNodeKind.LogicNand:
                    return GateType.NAND;
                case AutomationNodeKind.LogicNor:
                    return GateType.NOR;
                case AutomationNodeKind.LogicXnor:
                    return GateType.XNOR;
                default:
                    return GateType.AND;
            }
        }

        private static AutomationNodeKind NativeFuzzyThresholdKind(FuzzyThreshold fuzzyThreshold)
        {
            return fuzzyThreshold != null && !fuzzyThreshold.Above.Us
                ? AutomationNodeKind.CompareBelowThreshold
                : AutomationNodeKind.CompareAboveThreshold;
        }

        private static AutomationNodeKind NativeMaxMinKind(MaxMin maxMin)
        {
            return maxMin != null && maxMin.SelectedOp.Us == OpType.Min
                ? AutomationNodeKind.MathMin
                : AutomationNodeKind.MathMax;
        }

        private static OpType MaxMinOpType(AutomationNodeKind kind)
        {
            return kind == AutomationNodeKind.MathMin
                ? OpType.Min
                : OpType.Max;
        }

        private static AutomationNodeKind NativeEvaluatorKind(Evaluator evaluator)
        {
            string expression = CompactNativeExpression(evaluator?.Expression.Us);
            if (IsSupportedNativeMathExpression(expression, "*"))
                return AutomationNodeKind.MathMultiply;

            if (IsSupportedNativeMathExpression(expression, "-"))
                return AutomationNodeKind.MathSubtract;

            return IsSupportedNativeMathExpression(expression, "+")
                ? AutomationNodeKind.MathAdd
                : AutomationNodeKind.Comment;
        }

        private static bool IsSupportedNativeEvaluator(Evaluator evaluator)
        {
            string expression = CompactNativeExpression(evaluator?.Expression.Us);
            return IsSupportedNativeMathExpression(expression, "+") ||
                   IsSupportedNativeMathExpression(expression, "-") ||
                   IsSupportedNativeMathExpression(expression, "*");
        }

        private static bool IsSupportedNativeMathExpression(
            string expression,
            string operation)
        {
            if (string.IsNullOrWhiteSpace(expression) ||
                string.IsNullOrWhiteSpace(operation))
            {
                return false;
            }

            string prefix = "a" + operation;
            if (!expression.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string operand = expression.Substring(prefix.Length);
            return string.Equals(operand, "b", StringComparison.Ordinal) ||
                   float.TryParse(
                       operand,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out _);
        }

        private static string CompactNativeExpression(string expression)
        {
            return new string((expression ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .ToArray());
        }

        private static string NativeNodeLabel(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return "Read " + SafeFilterName(getter.BlockFilter.Us, getter.BlockTypeName.Us);
            if (component is GenericBlockSetter setter)
                return "Set " + SafeFilterName(setter.BlockFilter.Us, setter.BlockTypeName.Us);
            if (component is NativeSwitch switchComponent)
            {
                if (NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition)
                    return "If true";

                return "Switch > " + switchComponent.Threshold.Us.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (component is LogicGate logicGate)
                return "Logic " + logicGate.SelectedGate.Us;
            if (component is FuzzyThreshold fuzzyThreshold)
                return (fuzzyThreshold.Above.Us ? "Above " : "Below ") +
                       FuzzyThresholdValue(fuzzyThreshold).ToString("0.###", CultureInfo.InvariantCulture);
            if (component is MaxMin maxMin)
                return MaxMinName(NativeMaxMinKind(maxMin));
            if (component is Evaluator evaluator)
                return evaluator.Expression.Us;
            if (component is ConstantInput constant)
                return "Constant " + constant.InputValue.Us.ToString("0.###", CultureInfo.InvariantCulture);
            if (component is RandomInput random)
                return "Random " +
                       random.RandomLimits.Lower.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                       random.RandomLimits.Upper.ToString("0.###", CultureInfo.InvariantCulture);
            if (component is NativeComment comment)
            {
                if (IsForeverComment(comment.InputValue.Us))
                    return "Forever";
                return comment.InputValue.Us;
            }
            return component.GetType().Name;
        }

        private static string NativeNodeProperty(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return NativePropertyLabel(getter);
            if (component is GenericBlockSetter setter)
                return NativePropertyLabel(setter);
            if (component is NativeSwitch switchComponent)
                return NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition
                    ? "condition"
                    : "threshold";
            if (component is LogicGate)
                return "logic";
            if (component is FuzzyThreshold)
                return "threshold";
            if (component is MaxMin)
                return "operation";
            if (component is Evaluator)
                return "expression";
            if (component is ConstantInput)
                return "number";
            if (component is RandomInput)
                return "range";
            if (component is Clamp)
                return "range";
            if (component is Delay)
                return "seconds";
            if (component is NativeComment comment)
            {
                if (IsForeverComment(comment.InputValue.Us))
                    return "body";
                return "note";
            }
            return "value";
        }

        private static string NativeNodeValue(CircuitComponent component)
        {
            if (component is GenericBlockGetter getter)
                return "native signal: " + NativePropertyLabel(getter);
            if (component is GenericBlockSetter)
                return "incoming signal";
            if (component is NativeSwitch switchComponent)
            {
                if (NativeSwitchKind(switchComponent) == AutomationNodeKind.IfCondition)
                    return SwitchValueText(0.5f, switchComponent.FailValue.Us);

                return "threshold " +
                       switchComponent.Threshold.Us.ToString("0.###", CultureInfo.InvariantCulture) +
                       " else " +
                       switchComponent.FailValue.Us.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (component is LogicGate logicGate)
                return logicGate.SelectedGate.Us.ToString();
            if (component is FuzzyThreshold fuzzyThreshold)
                return ThresholdValueText(FuzzyThresholdValue(fuzzyThreshold));
            if (component is MaxMin maxMin)
                return MaxMinName(NativeMaxMinKind(maxMin));
            if (component is Evaluator evaluator)
                return evaluator.Expression.Us;
            if (component is ConstantInput constant)
                return constant.InputValue.Us.ToString("0.###", CultureInfo.InvariantCulture);
            if (component is RandomInput random)
                return random.RandomLimits.Lower.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                       random.RandomLimits.Upper.ToString("0.###", CultureInfo.InvariantCulture);
            if (component is Clamp clamp)
                return clamp.MinMax.Lower.ToString("0.###", CultureInfo.InvariantCulture) + ".." +
                       clamp.MinMax.Upper.ToString("0.###", CultureInfo.InvariantCulture);
            if (component is Delay delay)
                return delay.DelayTime.Us.ToString("0.###", CultureInfo.InvariantCulture) + "s";
            if (component is NativeComment comment)
                return comment.InputValue.Us;
            return string.Empty;
        }

        private string InputGetterCurrentValueText(AutomationGraphNode node)
        {
            if (TryGetNativeGetterPreview(node, out string preview))
                return preview;

            return string.IsNullOrWhiteSpace(node?.ValueText)
                ? "native signal"
                : node.ValueText;
        }

        private string OutputSetterCurrentValueText(AutomationGraphNode node)
        {
            if (TryGetNativeSetterPreview(node, out string preview))
                return preview;

            return "target value";
        }

        private bool TryGetNativeGetterPreview(
            AutomationGraphNode node,
            out string preview)
        {
            preview = null;
            if (!(node?.NativeComponent is GenericBlockGetter getter) ||
                !TryResolveGetterPreviewTarget(getter, out Block block) ||
                !TryReadGetterLiveValue(getter, block, out object value))
            {
                return false;
            }

            preview = "live " + FormatNativePreviewValue(value);
            return true;
        }

        private bool TryGetNativeSetterPreview(
            AutomationGraphNode node,
            out string preview)
        {
            preview = null;
            if (!(node?.NativeComponent is GenericBlockSetter setter) ||
                !TryResolveSetterPreviewTarget(setter, out Block block) ||
                !TryReadSetterLiveValue(setter, block, out object value))
            {
                return false;
            }

            preview = "live " + FormatNativePreviewValue(value);
            return true;
        }

        private bool TryResolveGetterPreviewTarget(
            GenericBlockGetter getter,
            out Block block)
        {
            block = getter?.PotentiallyAffectedBlocks
                .FirstOrDefault(candidate => candidate != null && !candidate.IsDeleted);
            if (block != null)
                return true;

            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                return false;
            }

            AutomationBlockRef target = ResolveNativeTargetBlock(
                breadboard,
                getter,
                getter.BlockTypeName.Us,
                getter.BlockFilter.Us,
                out _);
            return target != null && target.TryGetBlock(out block);
        }

        private bool TryResolveSetterPreviewTarget(
            GenericBlockSetter setter,
            out Block block)
        {
            block = setter?.PotentiallyAffectedBlocks
                .Select(stub => stub?.Block)
                .FirstOrDefault(candidate => candidate != null && !candidate.IsDeleted);
            if (block != null)
                return true;

            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                return false;
            }

            AutomationBlockRef target = ResolveNativeTargetBlock(
                breadboard,
                setter,
                setter.BlockTypeName.Us,
                setter.BlockFilter.Us,
                out _);
            return target != null && target.TryGetBlock(out block);
        }

        private static bool TryReadGetterLiveValue(
            GenericBlockGetter getter,
            Block block,
            out object value)
        {
            value = null;
            if (getter == null || block == null)
                return false;

            Type blockType = block.GetType();
            try
            {
                if (getter.ReadableAttributeId.Us != NativeUnselectedCode &&
                    TryGetReadablePropertyInfo(blockType, getter.ReadableAttributeId.Us, out PropertyInfo readableProperty))
                {
                    value = readableProperty.GetValue(block, null);
                    return true;
                }

                if (getter.BlockPropertyId.Us != NativeUnselectedCode &&
                    TryGetNativeVariablePropertyInfo(
                        blockType,
                        getter.BlockSetId.Us,
                        getter.BlockPropertyId.Us,
                        out PropertyInfo packageProperty,
                        out PropertyInfo variableProperty))
                {
                    object package = packageProperty.GetValue(block, null);
                    if (package == null)
                        return false;

                    value = variableProperty.GetValue(package, null);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryReadSetterLiveValue(
            GenericBlockSetter setter,
            Block block,
            out object value)
        {
            value = null;
            if (setter == null || block == null ||
                setter.BlockPropertyId.Us == NativeUnselectedCode)
            {
                return false;
            }

            try
            {
                if (!TryGetNativeVariablePropertyInfo(
                        block.GetType(),
                        setter.BlockSetId.Us,
                        setter.BlockPropertyId.Us,
                        out PropertyInfo packageProperty,
                        out PropertyInfo variableProperty))
                {
                    return false;
                }

                object package = packageProperty.GetValue(block, null);
                if (package == null)
                    return false;

                value = variableProperty.GetValue(package, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetReadablePropertyInfo(
            Type blockType,
            uint readableId,
            out PropertyInfo readableProperty)
        {
            readableProperty = null;
            if (blockType == null)
                return false;

            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            IEnumerable items = method.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items == null)
                return false;

            foreach (object item in items)
            {
                PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                object attribute = GetMemberValue(item, "attribute", "Item2");
                if (property == null ||
                    !TryGetUInt(attribute, "Index", out uint id) ||
                    id != readableId)
                {
                    continue;
                }

                readableProperty = property;
                return true;
            }

            return false;
        }

        private static bool TryGetNativeVariablePropertyInfo(
            Type blockType,
            uint setId,
            uint propertyId,
            out PropertyInfo packageProperty,
            out PropertyInfo variableProperty)
        {
            packageProperty = null;
            variableProperty = null;
            Type setFinder = FindType("BrilliantSkies.DataManagement.Finders.SetFinder");
            Type saveAndLoad = FindType("BrilliantSkies.DataManagement.Saving.DataPackageSaveAndLoad");
            MethodInfo packagesMethod = setFinder?.GetMethod(
                "GetAllPackagesFromType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo gatherMethod = saveAndLoad?.GetMethod(
                "Gather",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Type) },
                null);
            if (blockType == null || packagesMethod == null || gatherMethod == null)
                return false;

            object record = packagesMethod.Invoke(null, new object[] { blockType });
            IEnumerable dataPackages = GetMemberValue(record, "OurDataPackageProperties") as IEnumerable;
            if (dataPackages == null)
                return false;

            foreach (object dataPackage in dataPackages)
            {
                if (!TryGetUInt(dataPackage, "Index", out uint candidateSetId) ||
                    candidateSetId != setId)
                {
                    continue;
                }

                PropertyInfo candidatePackageProperty = GetMemberValue(dataPackage, "PropertyInfo") as PropertyInfo;
                Type packageType = candidatePackageProperty?.PropertyType;
                if (packageType == null)
                    continue;

                object varSet = gatherMethod.Invoke(null, new object[] { packageType });
                IEnumerable attributes = GetMemberValue(varSet, "Attributes") as IEnumerable;
                if (attributes == null)
                    continue;

                foreach (object variable in attributes)
                {
                    if (!TryGetUInt(variable, "SaveIndex", out uint candidatePropertyId) ||
                        candidatePropertyId != propertyId)
                    {
                        continue;
                    }

                    PropertyInfo candidateVariableProperty = GetMemberValue(variable, "PropertyInfo") as PropertyInfo;
                    if (candidateVariableProperty == null)
                    {
                        string propertyName = GetMemberString(variable, "PropertyName");
                        candidateVariableProperty = string.IsNullOrWhiteSpace(propertyName)
                            ? null
                            : packageType.GetProperty(
                                propertyName,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    if (candidateVariableProperty == null)
                        continue;

                    packageProperty = candidatePackageProperty;
                    variableProperty = candidateVariableProperty;
                    return true;
                }
            }

            return false;
        }

        private static string FormatNativePreviewValue(object value)
        {
            if (value == null)
                return "null";

            if (value is float floatValue)
                return floatValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double doubleValue)
                return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is decimal decimalValue)
                return decimalValue.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text)
                ? value.GetType().Name
                : ShortText(text, 32);
        }

        private static string SafeFilterName(string filter, string typeName)
        {
            if (!string.IsNullOrWhiteSpace(filter))
                return filter;
            return string.IsNullOrWhiteSpace(typeName) ? "block" : typeName;
        }

        private static bool IsForeverComment(string text)
        {
            string normalized = NormalizeTokenText(text);
            return normalized.StartsWith("forever", StringComparison.Ordinal) ||
                   normalized.Contains("nativebreadboardevaluatescontinuously");
        }

        private static string NativePropertyLabel(GenericBlockGetter getter)
        {
            Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
            if (getter.ReadableAttributeId.Us != NativeUnselectedCode)
            {
                if (TryGetReadableLabel(blockType, getter.ReadableAttributeId.Us, out string label))
                    return label;

                return "readable #" + getter.ReadableAttributeId.Us.ToString(CultureInfo.InvariantCulture);
            }

            if (getter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: false, getter.BlockSetId.Us, getter.BlockPropertyId.Us, out string label))
                    return label;

                return "property #" + getter.BlockSetId.Us.ToString(CultureInfo.InvariantCulture) + ":" +
                       getter.BlockPropertyId.Us.ToString(CultureInfo.InvariantCulture);
            }

            return "value";
        }

        private static string NativePropertyLabel(GenericBlockSetter setter)
        {
            Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
            if (setter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: true, setter.BlockSetId.Us, setter.BlockPropertyId.Us, out string label))
                    return label;

                return "property #" + setter.BlockSetId.Us.ToString(CultureInfo.InvariantCulture) + ":" +
                       setter.BlockPropertyId.Us.ToString(CultureInfo.InvariantCulture);
            }

            return "value";
        }

        private static bool TryGetReadableLabel(
            Type blockType,
            uint readableId,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            IEnumerable items = method.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items == null)
                return false;

            foreach (object item in items)
            {
                PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                object attribute = GetMemberValue(item, "attribute", "Item2");
                if (!TryGetUInt(attribute, "Index", out uint id) || id != readableId)
                    continue;

                label = CandidateLabel(property?.Name, attribute);
                return !string.IsNullOrWhiteSpace(label);
            }

            return false;
        }

        private static bool TryGetVariableLabel(
            Type blockType,
            bool editableOnly,
            uint setId,
            uint propertyId,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly))
            {
                if (candidate.SetId != setId || candidate.PropertyId != propertyId)
                    continue;

                label = candidate.Label;
                return !string.IsNullOrWhiteSpace(label);
            }

            return false;
        }

        private static Color NativeComponentColor(
            CircuitComponent component,
            AutomationNodeKind fallbackKind)
        {
            try
            {
                Color color = component.OutlineColor.Us;
                if (color.a > 0.001f)
                    return color;
            }
            catch
            {
                // Use kind color below.
            }

            return NodeColor(fallbackKind);
        }

        private static bool ComponentAcceptsInput(CircuitComponent component) =>
            component != null && component.MaxInputs > 0;

        private static bool ComponentProducesOutput(CircuitComponent component) =>
            component != null && component.MaxOutputs > 0;

        private static bool NativeComponentUsesValueFootprint(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return BlockShape(kind) == AutomationBlockShape.Value ||
                   component.Width.Us > 1f &&
                   CanProduceValueBlock(kind) &&
                   IsValueFootprint(new Rect(0f, 0f, component.Width.Us, component.Height.Us));
        }

        private static bool CanNativeComponentFeedValueSlot(
            CircuitComponent component,
            AutomationNodeKind hostKind,
            AutomationValueSlotKind slotKind)
        {
            if (component == null ||
                !CanProduceValueBlock(NativeKind(component)) ||
                !ComponentProducesOutput(component))
            {
                return false;
            }

            if (CanFeedSignalValueSlot(hostKind, slotKind))
                return true;

            return component is ConstantInput &&
                   IsConstantOnlyValueSlot(hostKind, slotKind);
        }

        private static bool IsStackFlowComponent(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return !NativeComponentUsesValueFootprint(component) &&
                   kind != AutomationNodeKind.Comment &&
                   kind != AutomationNodeKind.Forever &&
                   (ComponentProducesOutput(component) || ComponentAcceptsInput(component));
        }

        private static IEnumerable<CircuitComponent> OrderedNativeFlowComponents(
            IReadOnlyList<CircuitComponent> components)
        {
            return OrderedNativeFlowChains(components).SelectMany(chain => chain);
        }

        private static List<List<CircuitComponent>> OrderedNativeFlowChains(
            IReadOnlyList<CircuitComponent> components)
        {
            if (components == null)
                return new List<List<CircuitComponent>>();

            var result = new List<List<CircuitComponent>>();
            List<CircuitComponent> topLevel = components
                .Where(component => component != null)
                .Where(component => NativeBodyParent(components, component) == null)
                .Where(component => NativeValueHost(components, component) == null)
                .Where(component => IsStackFlowComponent(component) || AcceptsControlBody(NativeKind(component)))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .ToList();

            var visited = new HashSet<CircuitComponent>();
            List<CircuitComponent> roots = topLevel
                .Where(component => PreviousSnappedNativeStackComponent(components, topLevel, component) == null)
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .ToList();
            foreach (CircuitComponent component in roots)
                AppendNativeFlowChain(components, topLevel, component, visited, result);
            foreach (CircuitComponent component in topLevel)
                AppendNativeFlowChain(components, topLevel, component, visited, result);

            return result;
        }

        private static void AppendNativeFlowChain(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent start,
            HashSet<CircuitComponent> visited,
            List<List<CircuitComponent>> result)
        {
            if (start == null || visited == null || result == null || visited.Contains(start))
                return;

            var chain = new List<CircuitComponent>();
            CircuitComponent current = start;
            while (current != null && visited.Add(current))
            {
                AppendNativeFlowComponent(components, current, chain);
                current = NextSnappedNativeStackComponent(components, candidates, current, visited);
            }

            if (chain.Count > 0)
                result.Add(chain);
        }

        private static void AppendNativeFlowComponent(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent component,
            List<CircuitComponent> result)
        {
            if (component == null || result == null)
                return;

            if (IsStackFlowComponent(component))
                result.Add(component);

            foreach (CircuitComponent child in NativeBodyChildrenForHost(components, component))
                AppendNativeFlowComponent(components, child, result);
        }

        private static CircuitComponent PreviousSnappedNativeStackComponent(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent component)
        {
            if (components == null || candidates == null || component == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, component))
                .Where(candidate => AreNativeStackComponentsSnapped(components, candidate, component))
                .OrderBy(candidate => Mathf.Abs(NativeComponentRect(component).y - (NativeComponentRect(candidate).yMax - 2f)))
                .ThenBy(candidate => Mathf.Abs(NativeComponentRect(component).x - NativeComponentRect(candidate).x))
                .FirstOrDefault();
        }

        private static CircuitComponent NextSnappedNativeStackComponent(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<CircuitComponent> candidates,
            CircuitComponent component,
            HashSet<CircuitComponent> visited)
        {
            if (components == null || candidates == null || component == null)
                return null;

            return candidates
                .Where(candidate => candidate != null && !ReferenceEquals(candidate, component))
                .Where(candidate => visited == null || !visited.Contains(candidate))
                .Where(candidate => AreNativeStackComponentsSnapped(components, component, candidate))
                .OrderBy(candidate => Mathf.Abs(NativeComponentRect(candidate).y - (NativeComponentRect(component).yMax - 2f)))
                .ThenBy(candidate => Mathf.Abs(NativeComponentRect(candidate).x - NativeComponentRect(component).x))
                .FirstOrDefault();
        }

        private static bool AreNativeStackComponentsSnapped(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent from,
            CircuitComponent to)
        {
            if (components == null ||
                from == null ||
                to == null ||
                !IsStackFlowComponent(from) && !AcceptsControlBody(NativeKind(from)) ||
                !IsStackFlowComponent(to) && !AcceptsControlBody(NativeKind(to)) ||
                !ReferenceEquals(NativeBodyParent(components, from), NativeBodyParent(components, to)))
            {
                return false;
            }

            Rect fromRect = NativeComponentRect(from);
            Rect toRect = NativeComponentRect(to);
            if (IsNativeConnected(from, to))
                return true;

            float expectedY = fromRect.yMax - 2f;
            return Mathf.Abs(toRect.x - fromRect.x) <= 14f &&
                   Mathf.Abs(toRect.y - expectedY) <= 14f;
        }

        private static CircuitComponent NativeValueHost(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent valueComponent)
        {
            if (components == null || valueComponent == null)
                return null;

            foreach (CircuitComponent host in components)
            {
                if (host == null ||
                    ReferenceEquals(host, valueComponent) ||
                    !AcceptsValueSlot(NativeKind(host)))
                {
                    continue;
                }

                foreach (AutomationValueSlotKind slotKind in ValueSlotKinds(NativeKind(host)))
                {
                    if (ReferenceEquals(FindSnappedValueComponent(host, components, slotKind), valueComponent))
                        return host;
                }
            }

            return null;
        }

        private static IEnumerable<CircuitComponent> NativeBodyChildrenForHost(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent host)
        {
            if (components == null ||
                host == null ||
                !AcceptsControlBody(NativeKind(host)))
            {
                return Enumerable.Empty<CircuitComponent>();
            }

            Rect body = ControlBodyRect(NativeComponentRect(host), NativeKind(host));
            return components
                .Where(component => component != null && !ReferenceEquals(component, host) && IsBodyFlowComponent(component))
                .Where(component => body.Contains(NativeComponentRect(component).center))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us);
        }

        private static CircuitComponent NativeBodyParent(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent component)
        {
            if (components == null || component == null || !IsBodyFlowComponent(component))
                return null;

            foreach (CircuitComponent host in components)
            {
                if (host == null ||
                    ReferenceEquals(host, component) ||
                    !AcceptsControlBody(NativeKind(host)))
                {
                    continue;
                }

                if (ControlBodyRect(NativeComponentRect(host), NativeKind(host)).Contains(NativeComponentRect(component).center))
                    return host;
            }

            return null;
        }

        private static bool IsBodyFlowComponent(CircuitComponent component)
        {
            if (component == null)
                return false;

            AutomationNodeKind kind = NativeKind(component);
            return !NativeComponentUsesValueFootprint(component) &&
                   kind != AutomationNodeKind.Comment &&
                   kind != AutomationNodeKind.Forever &&
                   (ComponentProducesOutput(component) || ComponentAcceptsInput(component));
        }

        private static CircuitComponent FindSnappedValueComponent(
            CircuitComponent host,
            IReadOnlyList<CircuitComponent> components)
        {
            return FindSnappedValueComponent(host, components, AutomationValueSlotKind.Pass);
        }

        private static CircuitComponent FindSnappedValueComponent(
            CircuitComponent host,
            IReadOnlyList<CircuitComponent> components,
            AutomationValueSlotKind slotKind)
        {
            if (host == null || components == null)
                return null;

            AutomationNodeKind hostKind = NativeKind(host);
            if (!AcceptsValueSlot(hostKind))
                return null;

            Rect slot = ValueSlotRect(NativeComponentRect(host), hostKind, slotKind);
            float threshold = 52f;
            CircuitComponent best = null;
            float bestDistance = threshold;
            foreach (CircuitComponent candidate in components)
            {
                if (candidate == null ||
                    ReferenceEquals(candidate, host) ||
                    !CanNativeComponentFeedValueSlot(candidate, hostKind, slotKind))
                {
                    continue;
                }

                float distance = Vector2.Distance(NativeComponentRect(candidate).center, slot.center);
                if (distance >= bestDistance)
                    continue;

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private static bool TryApplySwitchElseValue(
            NativeSwitch switchComponent,
            CircuitComponent valueComponent)
        {
            if (switchComponent == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            switchComponent.FailValue.Us = value;
            return true;
        }

        private static bool TryApplySwitchThresholdValue(
            NativeSwitch switchComponent,
            CircuitComponent valueComponent)
        {
            if (switchComponent == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            switchComponent.Threshold.Us = value;
            return true;
        }

        private static bool TryApplyFuzzyThresholdValue(
            FuzzyThreshold fuzzyThreshold,
            CircuitComponent valueComponent)
        {
            if (fuzzyThreshold == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            fuzzyThreshold.ThresholdLimits.Us = new Vector2(value, value);
            return true;
        }

        private static bool TryApplyEvaluatorAddAmount(
            Evaluator evaluator,
            CircuitComponent valueComponent)
        {
            return TryApplyEvaluatorMathOperand(evaluator, valueComponent, AutomationNodeKind.MathAdd);
        }

        private static bool TryApplyEvaluatorMathOperand(
            Evaluator evaluator,
            CircuitComponent valueComponent,
            AutomationNodeKind kind)
        {
            if (evaluator == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            evaluator.Expression.Us = MathExpressionText(kind, FormatGraphFloat(value));
            return true;
        }

        private static bool TryApplyClampMinimum(
            Clamp clamp,
            CircuitComponent valueComponent)
        {
            if (clamp == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            float upper = Mathf.Max(value, clamp.MinMax.Upper);
            clamp.MinMax.Lower = Mathf.Min(value, upper);
            clamp.MinMax.Upper = upper;
            return true;
        }

        private static bool TryApplyClampMaximum(
            Clamp clamp,
            CircuitComponent valueComponent)
        {
            if (clamp == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            float lower = Mathf.Min(clamp.MinMax.Lower, value);
            clamp.MinMax.Lower = lower;
            clamp.MinMax.Upper = Mathf.Max(value, lower);
            return true;
        }

        private static bool TryApplyDelaySeconds(
            Delay delay,
            CircuitComponent valueComponent)
        {
            if (delay == null ||
                valueComponent == null ||
                !TryReadValueComponentFloat(valueComponent, out float value))
            {
                return false;
            }

            delay.DelayTime.Us = Mathf.Max(0f, value);
            return true;
        }

        private static bool TryReadValueComponentFloat(
            CircuitComponent component,
            out float value)
        {
            value = 0f;
            if (component is ConstantInput constant)
            {
                value = constant.InputValue.Us;
                return true;
            }

            return false;
        }

        private static bool TryGetNativeComponent(
            AutomationGraphNode node,
            out CircuitComponent component)
        {
            component = node?.NativeComponent as CircuitComponent;
            return component != null;
        }

        private static bool NativeStackConnectionExists(
            AutomationGraphNode from,
            AutomationGraphNode to)
        {
            if (!TryGetNativeComponent(from, out CircuitComponent fromComponent) ||
                !TryGetNativeComponent(to, out CircuitComponent toComponent) ||
                !ComponentProducesOutput(fromComponent) ||
                !ComponentAcceptsInput(toComponent))
            {
                return false;
            }

            return IsNativeConnected(fromComponent, toComponent);
        }

        private static bool NativeValueConnectionExists(
            AutomationGraphNode value,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (!TryGetNativeComponent(value, out CircuitComponent valueComponent) ||
                !TryGetNativeComponent(host, out CircuitComponent hostComponent))
            {
                return false;
            }

            AutomationNodeKind hostKind = NativeKind(hostComponent);
            if (!CanNativeComponentFeedValueSlot(valueComponent, hostKind, slotKind))
                return false;

            if (hostComponent is NativeSwitch switchComponent)
                return slotKind == AutomationValueSlotKind.Pass &&
                       IsNativeConnectedToInput(valueComponent, switchComponent.Pass);

            if (hostComponent is Evaluator &&
                IsMathEvaluatorKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostComponent is LogicGate &&
                IsLogicGateKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostComponent is MaxMin &&
                IsMaxMinKind(hostKind))
            {
                return IsNativeConnectedToInputAt(
                    valueComponent,
                    hostComponent,
                    slotKind == AutomationValueSlotKind.Pass ? 0 : 1);
            }

            if (hostKind == AutomationNodeKind.OutputSetter ||
                IsFuzzyThresholdKind(hostKind) ||
                hostComponent is Clamp ||
                hostComponent is Delay)
            {
                return slotKind == AutomationValueSlotKind.Pass &&
                       IsNativeConnected(valueComponent, hostComponent);
            }

            return slotKind == AutomationValueSlotKind.Pass &&
                   IsNativeConnected(valueComponent, hostComponent);
        }

        private static bool NativePropertyValueMatchesSocket(
            AutomationGraphNode valueNode,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            if (!TryGetNativeComponent(valueNode, out CircuitComponent valueComponent) ||
                !TryGetNativeComponent(host, out CircuitComponent hostComponent) ||
                !(valueComponent is ConstantInput) ||
                !TryReadValueComponentFloat(valueComponent, out float constantValue))
            {
                return false;
            }

            return TryGetNativePropertySocketValue(hostComponent, slotKind, out float socketValue) &&
                   NearlyEqual(constantValue, socketValue);
        }

        private static bool TryGetNativePropertySocketValue(
            CircuitComponent hostComponent,
            AutomationValueSlotKind slotKind,
            out float value)
        {
            value = 0f;
            if (hostComponent is NativeSwitch switchComponent)
            {
                AutomationNodeKind kind = NativeKind(hostComponent);
                if (kind == AutomationNodeKind.IfLessThan &&
                    slotKind == AutomationValueSlotKind.Threshold)
                {
                    value = switchComponent.Threshold.Us;
                    return true;
                }

                if ((kind == AutomationNodeKind.IfLessThan ||
                     kind == AutomationNodeKind.IfCondition) &&
                    slotKind == AutomationValueSlotKind.Else)
                {
                    value = switchComponent.FailValue.Us;
                    return true;
                }
            }

            if (hostComponent is FuzzyThreshold fuzzyThreshold &&
                slotKind == AutomationValueSlotKind.Threshold)
            {
                value = FuzzyThresholdValue(fuzzyThreshold);
                return true;
            }

            if (hostComponent is Clamp clamp)
            {
                if (slotKind == AutomationValueSlotKind.Min)
                {
                    value = clamp.MinMax.Lower;
                    return true;
                }

                if (slotKind == AutomationValueSlotKind.Max)
                {
                    value = clamp.MinMax.Upper;
                    return true;
                }
            }

            if (hostComponent is Delay delay &&
                slotKind == AutomationValueSlotKind.Seconds)
            {
                value = delay.DelayTime.Us;
                return true;
            }

            return false;
        }

        private static bool NearlyEqual(float left, float right) =>
            Mathf.Abs(left - right) <= 0.0005f;

        private static bool TryConnectComponentToHost(
            Board board,
            CircuitComponent from,
            CircuitComponent to,
            ref int connected,
            ref int alreadyConnected)
        {
            if (board == null ||
                from == null ||
                to == null ||
                !ComponentProducesOutput(from) ||
                !ComponentAcceptsInput(to))
            {
                return false;
            }

            if (IsNativeConnected(from, to))
            {
                alreadyConnected++;
                return true;
            }

            if (board.Connect(from, to))
            {
                connected++;
                return true;
            }

            return false;
        }

        private static bool TryConnectComponentToInput(
            Board board,
            CircuitComponent from,
            BInput input,
            ref int connected,
            ref int alreadyConnected)
        {
            if (board == null ||
                from == null ||
                input == null ||
                !ComponentProducesOutput(from))
            {
                return false;
            }

            if (IsNativeConnectedToInput(from, input))
            {
                alreadyConnected++;
                return true;
            }

            board.ConnectComponentToInput(input, from);
            if (IsNativeConnectedToInput(from, input))
            {
                connected++;
                return true;
            }

            return false;
        }

        private static bool TryConnectComponentToInputAt(
            Board board,
            CircuitComponent from,
            CircuitComponent to,
            int inputIndex,
            ref int connected,
            ref int alreadyConnected)
        {
            EnsureNativeInputCount(to, inputIndex + 1);
            if (to == null ||
                inputIndex < 0 ||
                to.BInputs == null ||
                to.BInputs.Count <= inputIndex)
            {
                return false;
            }

            return TryConnectComponentToInput(
                board,
                from,
                to.BInputs[inputIndex],
                ref connected,
                ref alreadyConnected);
        }

        private static void EnsureNativeInputCount(
            CircuitComponent component,
            int count)
        {
            if (component == null || count <= 0 || component.BInputs == null)
                return;

            while (component.BInputs.Count < count &&
                   component.BInputs.Count < component.MaxInputs)
            {
                component.CreateInput(sync: true);
            }
        }

        private static bool IsNativeConnected(
            CircuitComponent from,
            CircuitComponent to)
        {
            if (from == null || to == null)
                return false;

            foreach (BInput input in to.BInputs.Us)
            {
                if (input?.OurOutput?.IsLatched == true &&
                    ReferenceEquals(input.OurOutput.Them?.OurComponent, from))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNativeConnectedToInput(
            CircuitComponent from,
            BInput input)
        {
            return from != null &&
                   input?.OurOutput?.IsLatched == true &&
                   ReferenceEquals(input.OurOutput.Them?.OurComponent, from);
        }

        private static bool IsNativeConnectedToInputAt(
            CircuitComponent from,
            CircuitComponent to,
            int inputIndex)
        {
            if (from == null ||
                to == null ||
                inputIndex < 0 ||
                to.BInputs == null ||
                to.BInputs.Count <= inputIndex)
            {
                return false;
            }

            return IsNativeConnectedToInput(from, to.BInputs[inputIndex]);
        }

        private static bool TryConfigureGetterProperty(
            GenericBlockGetter getter,
            Type blockType,
            string query)
        {
            if (TryMatchGetterReadable(blockType, query, out uint readableId))
            {
                getter.ReadableAttributeId.Us = readableId;
                getter.BlockPropertyId.Us = NativeUnselectedCode;
                getter.BlockSetId.Us = NativeUnselectedCode;
                return true;
            }

            if (TryMatchGetterVariable(blockType, query, out uint setId, out uint propertyId))
            {
                getter.ReadableAttributeId.Us = NativeUnselectedCode;
                getter.BlockSetId.Us = setId;
                getter.BlockPropertyId.Us = propertyId;
                return true;
            }

            return false;
        }

        private static bool TryConfigureSetterProperty(
            GenericBlockSetter setter,
            Type blockType,
            string query)
        {
            if (!TryMatchSetterVariable(blockType, query, out uint setId, out uint propertyId))
                return false;

            setter.BlockSetId.Us = setId;
            setter.BlockPropertyId.Us = propertyId;
            return true;
        }

        private static IEnumerable<string> EnumerateGetterPropertyLabels(Type blockType)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string label in EnumerateReadableLabels(blockType))
            {
                if (!string.IsNullOrWhiteSpace(label) && seen.Add(label))
                    yield return label;
            }

            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label) && seen.Add(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static IEnumerable<string> EnumerateSetterPropertyLabels(Type blockType)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: true))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label) && seen.Add(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static IEnumerable<string> EnumerateReadableLabels(Type blockType)
        {
            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                yield break;

            IEnumerable items = method.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items == null)
                yield break;

            foreach (object item in items)
            {
                PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                object attribute = GetMemberValue(item, "attribute", "Item2");
                string label = CandidateLabel(property?.Name, attribute);
                if (!string.IsNullOrWhiteSpace(label))
                    yield return label;
            }
        }

        private static bool TryMatchGetterReadable(
            Type blockType,
            string query,
            out uint readableId)
        {
            readableId = NativeUnselectedCode;
            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            IEnumerable items = method.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items == null)
                return false;

            foreach (object item in items)
            {
                PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                object attribute = GetMemberValue(item, "attribute", "Item2");
                if (!MatchesQuery(query, CandidateText(property?.Name, attribute)))
                    continue;

                if (TryGetUInt(attribute, "Index", out readableId))
                    return true;
            }

            return false;
        }

        private static bool TryMatchGetterVariable(
            Type blockType,
            string query,
            out uint setId,
            out uint propertyId)
        {
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                setId = candidate.SetId;
                propertyId = candidate.PropertyId;
                return true;
            }

            setId = NativeUnselectedCode;
            propertyId = NativeUnselectedCode;
            return false;
        }

        private static bool TryMatchSetterVariable(
            Type blockType,
            string query,
            out uint setId,
            out uint propertyId)
        {
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: true))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                setId = candidate.SetId;
                propertyId = candidate.PropertyId;
                return true;
            }

            setId = NativeUnselectedCode;
            propertyId = NativeUnselectedCode;
            return false;
        }

        private static IEnumerable<NativeVariableCandidate> EnumerateNativeVariables(
            Type blockType,
            bool editableOnly)
        {
            Type setFinder = FindType("BrilliantSkies.DataManagement.Finders.SetFinder");
            Type saveAndLoad = FindType("BrilliantSkies.DataManagement.Saving.DataPackageSaveAndLoad");
            MethodInfo packagesMethod = setFinder?.GetMethod(
                "GetAllPackagesFromType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo gatherMethod = saveAndLoad?.GetMethod(
                "Gather",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Type) },
                null);
            if (packagesMethod == null || gatherMethod == null)
                yield break;

            object record = packagesMethod.Invoke(null, new object[] { blockType });
            IEnumerable dataPackages = GetMemberValue(record, "OurDataPackageProperties") as IEnumerable;
            if (dataPackages == null)
                yield break;

            foreach (object dataPackage in dataPackages)
            {
                if (!TryGetUInt(dataPackage, "Index", out uint setId))
                    continue;

                PropertyInfo packageProperty = GetMemberValue(dataPackage, "PropertyInfo") as PropertyInfo;
                Type packageType = packageProperty?.PropertyType;
                if (packageType == null)
                    continue;

                object varSet = gatherMethod.Invoke(null, new object[] { packageType });
                IEnumerable attributes = GetMemberValue(varSet, "Attributes") as IEnumerable;
                if (attributes == null)
                    continue;

                foreach (object variable in attributes)
                {
                    if (editableOnly && GetMemberValue(variable, "EditableAttribute") == null)
                        continue;
                    if (!TryGetUInt(variable, "SaveIndex", out uint propertyId))
                        continue;

                    object attribute = GetMemberValue(variable, "Attribute");
                    string propertyName = GetMemberString(variable, "PropertyName");
                    yield return new NativeVariableCandidate(
                        setId,
                        propertyId,
                        CandidateText(packageProperty.Name, attribute, propertyName),
                        CandidateLabel(packageProperty.Name, attribute, propertyName));
                }
            }
        }

        private static Type FindBlockTypeByName(string blockTypeName)
        {
            if (string.IsNullOrWhiteSpace(blockTypeName))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type match = assembly.GetTypesSafe()
                    .FirstOrDefault(type =>
                        typeof(Block).IsAssignableFrom(type) &&
                        string.Equals(type.Name, blockTypeName, StringComparison.Ordinal));
                if (match != null)
                    return match;
            }

            return null;
        }

        private static string CandidateText(
            string primary,
            object attribute,
            string secondary = null)
        {
            return string.Join(
                " ",
                new[]
                {
                    primary,
                    secondary,
                    GetMemberString(attribute, "Name"),
                    GetMemberString(attribute, "Description"),
                    attribute?.ToString()
                }.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray());
        }

        private static string CandidateLabel(
            string primary,
            object attribute,
            string secondary = null)
        {
            string name = GetMemberString(attribute, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (!string.IsNullOrWhiteSpace(secondary))
                return secondary.Trim();

            if (!string.IsNullOrWhiteSpace(primary))
                return primary.Trim();

            string fallback = attribute?.ToString();
            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private static bool MatchesQuery(string query, string candidate)
        {
            string normalizedQuery = NormalizeTokenText(query);
            string normalizedCandidate = NormalizeTokenText(candidate);
            if (string.IsNullOrEmpty(normalizedQuery) ||
                string.IsNullOrEmpty(normalizedCandidate))
            {
                return false;
            }

            if (normalizedCandidate.Contains(normalizedQuery) ||
                normalizedQuery.Contains(normalizedCandidate))
            {
                return true;
            }

            string[] words = (query ?? string.Empty)
                .Split(new[] { ' ', '_', '-', '.', '/', '\\', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeTokenText)
                .Where(word => word.Length > 1)
                .ToArray();
            return words.Length > 0 && words.All(word => normalizedCandidate.Contains(word));
        }

        private static string NormalizeTokenText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            char[] chars = text
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray();
            return new string(chars);
        }

        private static object GetMemberValue(
            object instance,
            params string[] names)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null)
                    return property.GetValue(instance, null);

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(instance);
            }

            return null;
        }

        private static string GetMemberString(
            object instance,
            string name)
        {
            object value = GetMemberValue(instance, name);
            return value == null ? string.Empty : value.ToString();
        }

        private static bool TryGetUInt(
            object instance,
            string name,
            out uint value)
        {
            value = 0u;
            object raw = GetMemberValue(instance, name);
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return Type.GetType(fullName, throwOnError: false);
        }

        private static float ParseFloat(
            string text,
            float fallback)
        {
            if (float.TryParse(
                    (text ?? string.Empty).Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value))
            {
                return value;
            }

            return fallback;
        }

        private static float ParseSeconds(
            string text,
            float fallback)
        {
            string cleaned = (text ?? string.Empty).Trim().TrimEnd('s', 'S');
            return ParseFloat(cleaned, fallback);
        }

        private static void ParseRange(
            string text,
            float fallbackLower,
            float fallbackUpper,
            out float lower,
            out float upper)
        {
            lower = fallbackLower;
            upper = fallbackUpper;
            string value = text ?? string.Empty;
            string[] parts = value.Split(new[] { "..", ",", ";" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                lower = ParseFloat(parts[0], fallbackLower);
                upper = ParseFloat(parts[1], fallbackUpper);
                if (lower > upper)
                {
                    float swap = lower;
                    lower = upper;
                    upper = swap;
                }
            }
        }

        private static void ParseIfSwitchValue(
            string text,
            float fallbackThreshold,
            float fallbackFailValue,
            out float threshold,
            out float failValue)
        {
            threshold = fallbackThreshold;
            failValue = fallbackFailValue;
            List<float> numbers = ExtractFloatTokens(text).ToList();
            if (numbers.Count > 0)
                threshold = numbers[0];
            if (numbers.Count > 2)
                failValue = numbers[2];
            else if ((text ?? string.Empty).IndexOf("else", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     numbers.Count > 1)
                failValue = numbers[1];
        }

        private static IEnumerable<float> ExtractFloatTokens(string text)
        {
            string value = text ?? string.Empty;
            var token = new List<char>();
            for (int index = 0; index <= value.Length; index++)
            {
                char c = index < value.Length ? value[index] : ' ';
                bool numeric = char.IsDigit(c) || c == '-' || c == '+' || c == '.';
                if (numeric)
                {
                    token.Add(c);
                    continue;
                }

                if (token.Count == 0)
                    continue;

                string candidate = new string(token.ToArray());
                token.Clear();
                if (float.TryParse(
                        candidate,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed))
                {
                    yield return parsed;
                }
            }
        }

        private readonly struct NativeVariableCandidate
        {
            internal NativeVariableCandidate(
                uint setId,
                uint propertyId,
                string candidateText,
                string label)
            {
                SetId = setId;
                PropertyId = propertyId;
                CandidateText = candidateText ?? string.Empty;
                Label = label ?? string.Empty;
            }

            internal uint SetId { get; }

            internal uint PropertyId { get; }

            internal string CandidateText { get; }

            internal string Label { get; }
        }

        private sealed class NativeBuildContext
        {
            private readonly List<string> _warnings = new List<string>();

            internal NativeBuildContext(
                AutomationBlockRef breadboard,
                IReadOnlyList<AutomationLink> links)
            {
            }

            internal bool HasWarnings => _warnings.Count > 0;

            internal string Detail => string.Join("\n", _warnings.Distinct().ToArray());

            internal void Warn(string warning)
            {
                if (!string.IsNullOrWhiteSpace(warning))
                    _warnings.Add(warning.Trim());
            }
        }

        private readonly struct NativeApplyResult
        {
            internal NativeApplyResult(
                string message,
                EsuHudNotificationKind kind,
                string detail = null)
            {
                Message = message;
                Kind = kind;
                Detail = detail;
            }

            internal string Message { get; }

            internal EsuHudNotificationKind Kind { get; }

            internal string Detail { get; }

            internal static NativeApplyResult Warning(
                string message,
                string detail = null) =>
                new NativeApplyResult(message, EsuHudNotificationKind.Warning, detail);
        }
    }

    internal static class AutomationBuilderReflectionExtensions
    {
        internal static IEnumerable<Type> GetTypesSafe(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
