using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BrilliantSkies.Blocks;
using BrilliantSkies.Blocks.BreadBoards;
using BrilliantSkies.Blocks.BreadBoards.GenericGetter;
using BrilliantSkies.Common.Circuits;
using BrilliantSkies.Common.Circuits.ComponentTypes;
using BrilliantSkies.Common.Circuits.ComponentTypes.Inputs;
using BrilliantSkies.Common.Circuits.Ui.UndoRedo;
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
        private const string AutoNamePrefix = "EsuAutomation";
        private const string LegacyAutoNamePrefix = "ESU_AB_";
        private const string NativeOwnerMarkerPrefix = "ESU_AB_OWNER|";

        private static readonly Dictionary<string, Type> s_typeByNameCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> s_blockTypeByNameCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, List<NativeReadableCandidate>> s_readableCandidatesByType =
            new Dictionary<Type, List<NativeReadableCandidate>>();
        private static readonly Dictionary<string, List<NativeVariableCandidate>> s_nativeVariablesByType =
            new Dictionary<string, List<NativeVariableCandidate>>(StringComparer.Ordinal);

        private NativeBreadboardSnapshot _nativeSnapshot;
        private string _nativeAutomationCacheKey;

        private void RefreshNativeAutomationCache(bool force = false)
        {
            string selectedKey = _selectedBreadboard?.StableKey;
            if (!string.Equals(_nativeAutomationCacheKey, selectedKey, StringComparison.Ordinal))
            {
                force = true;
                _nativeAutomationCacheKey = selectedKey;
                _nativeSnapshot = null;
            }

            float now = Time.unscaledTime;
            if (!force &&
                !_nativeAutomationCacheDirty &&
                now < _nextNativeAutomationRefreshTime)
            {
                return;
            }

            _nextNativeAutomationRefreshTime = now + NativeRefreshIntervalSeconds;
            _nativeAutomationCacheDirty = false;
            List<AutomationLink> stagedLinks = _links
                .Where(link => link?.NativeComponent == null)
                .ToList();
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                bool hadNative = _nativeSnapshot != null || _links.Any(link => link?.NativeComponent != null);
                _nativeSnapshot = null;
                _links.Clear();
                _links.AddRange(stagedLinks);
                _selectedLink = null;
                if (hadNative)
                {
                    _nativeAutomationCacheVersion++;
                    InvalidateAutomationDisplayCache();
                }

                return;
            }

            NativeBreadboardSnapshot snapshot = NativeBreadboardSnapshot.Create(breadboard);
            bool nativeChanged = _nativeSnapshot == null ||
                                 !ReferenceEquals(_nativeSnapshot.Breadboard, breadboard) ||
                                 _nativeSnapshot.Signature != snapshot.Signature;
            _nativeSnapshot = snapshot;
            object selectedNative = _selectedLink?.NativeComponent;
            List<AutomationLink> rebuilt = BuildNativeLinks(snapshot);
            bool linksChanged = NativeLinksChanged(rebuilt, stagedLinks);
            _links.Clear();
            _links.AddRange(rebuilt);
            foreach (AutomationLink stagedLink in stagedLinks)
            {
                if (stagedLink != null &&
                    !_links.Any(link => LinksMatch(link, stagedLink)))
                {
                    _links.Add(stagedLink);
                }
            }

            _nextLinkId = Math.Max(_nextLinkId, _links.Count + 1);
            if (_selectedLink?.NativeComponent == null &&
                _selectedLink != null)
            {
                _selectedLink = _links.FirstOrDefault(link => ReferenceEquals(link, _selectedLink)) ??
                                _links.FirstOrDefault(link => LinksMatch(link, _selectedLink));
            }
            else if (selectedNative != null)
            {
                _selectedLink = _links.FirstOrDefault(link => ReferenceEquals(link.NativeComponent, selectedNative));
            }

            if (_selectedLink == null && selectedNative != null)
                _selectedBlock = _selectedBreadboard;

            if (nativeChanged || linksChanged)
            {
                _nativeAutomationCacheVersion++;
                InvalidateAutomationDisplayCache();
                SyncSelectedGraphFromNativeIfLoaded(force: true);
            }
            else
            {
                SyncSelectedGraphFromNativeIfLoaded(force: false);
            }
        }

        private bool NativeLinksChanged(
            IReadOnlyList<AutomationLink> rebuiltNativeLinks,
            IReadOnlyList<AutomationLink> stagedLinks)
        {
            int currentNativeCount = _links.Count(link => link?.NativeComponent != null);
            if (currentNativeCount != (rebuiltNativeLinks?.Count ?? 0))
                return true;

            foreach (AutomationLink link in rebuiltNativeLinks ?? Array.Empty<AutomationLink>())
            {
                if (!_links.Any(existing =>
                        existing?.NativeComponent != null &&
                        ReferenceEquals(existing.NativeComponent, link.NativeComponent) &&
                        LinksMatch(existing, link) &&
                        string.Equals(existing.NativeStatus, link.NativeStatus, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            foreach (AutomationLink stagedLink in stagedLinks ?? Array.Empty<AutomationLink>())
            {
                if (stagedLink != null && !_links.Any(link => ReferenceEquals(link, stagedLink)))
                    return true;
            }

            return false;
        }

        private void SyncSelectedGraphFromNativeIfLoaded(bool force)
        {
            if (_selectedBreadboard == null)
                return;

            if (!_graphs.TryGetValue(_selectedBreadboard.StableKey, out AutomationGraph graph))
                return;

            if (force || graph.NativeSyncVersion != _nativeAutomationCacheVersion)
                SyncGraphFromNativeBreadboard(_selectedBreadboard, graph);
        }

        private bool RemoveNativeLink(AutomationLink link)
        {
            if (link?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out _))
            {
                if (IsEsuOwnedNativeComponent(component))
                {
                    _pendingNativeNodeRemovals.Add(component.UniqueId);
                    return true;
                }

                NotifyNativeApply(
                    "Imported native links are read-only in Automation Builder.",
                    EsuHudNotificationKind.Warning,
                    "Remove imported native links in the native breadboard editor if needed.");
                return false;
            }

            return false;
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
                if (!graph.ConnectionsInitialized)
                    graph.RebuildConnections(Array.Empty<AutomationGraphConnection>());
                graph.NativeSyncVersion = _nativeAutomationCacheVersion;
                return;
            }

            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            List<AutomationGraphNode> nodes = snapshot.Components
                .Where(IsSupportedAutomationNativeComponent)
                .Where(component => !IsPendingNativeRemoval(component))
                .OrderBy(component => component.Y.Us)
                .ThenBy(component => component.X.Us)
                .Select(component => NativeComponentToNode(breadboardRef, snapshot, component))
                .ToList();
            ApplyPendingNativeNodeDrafts(nodes, breadboard);
            graph.RebuildNativeNodes(nodes);
            if (!graph.ConnectionsInitialized)
                graph.RebuildConnections(Array.Empty<AutomationGraphConnection>());
            graph.NativeSyncVersion = _nativeAutomationCacheVersion;
            RefreshImportedNativeConnections(graph);
            InvalidateAutomationDisplayCache();
        }

        private void SyncGraphFromNativeBreadboardIfNeeded(
            AutomationBlockRef breadboardRef,
            AutomationGraph graph,
            bool force)
        {
            if (graph == null)
                return;

            RefreshNativeAutomationCache(force);
            if (!force && graph.NativeSyncVersion == _nativeAutomationCacheVersion)
                return;

            SyncGraphFromNativeBreadboard(breadboardRef, graph);
        }

        private void SyncNativeNodeRect(AutomationGraphNode node)
        {
            if (!(node?.NativeComponent is CircuitComponent component))
                return;

            if (IsEsuOwnedNativeComponent(component))
            {
                StorePendingNativeNodeDraft(node);
                return;
            }

            ApplyNativeNodeRect(component, node.Rect);
        }

        private void ApplyPendingNativeNodeDrafts(
            IEnumerable<AutomationGraphNode> nodes,
            NativeBreadBoard breadboard)
        {
            if (nodes == null || _pendingNativeNodeDrafts.Count == 0)
                return;

            var ownedIds = new HashSet<uint>(
                OwnerRecordsFor(breadboard)
                .Where(record => record?.Target != null)
                .Select(record => record.ComponentId));
            foreach (AutomationGraphNode node in nodes)
            {
                if (!TryGetNativeComponentId(node, out uint componentId))
                    continue;

                if (!ownedIds.Contains(componentId))
                {
                    _pendingNativeNodeDrafts.Remove(componentId);
                    continue;
                }

                if (_pendingNativeNodeDrafts.TryGetValue(componentId, out AutomationGraphNodeDraft draft))
                    draft.ApplyTo(node);
            }
        }

        private bool IsEsuOwnedNativeComponent(CircuitComponent component)
        {
            return component != null &&
                   TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard) &&
                   OwnerRecordsFor(breadboard).Any(record =>
                       record?.Target != null &&
                       record.ComponentId == component.UniqueId);
        }

        private bool IsEsuOwnedNativeNode(AutomationGraphNode node)
        {
            return node?.NativeComponent is CircuitComponent component &&
                   IsEsuOwnedNativeComponent(component);
        }

        private static bool TryGetNativeComponentId(
            AutomationGraphNode node,
            out uint componentId)
        {
            componentId = 0u;
            if (node?.NativeComponent is CircuitComponent component)
            {
                componentId = component.UniqueId;
                return componentId != 0u;
            }

            return false;
        }

        private bool HasPendingNativeNodeDraft(AutomationGraphNode node)
        {
            return TryGetNativeComponentId(node, out uint componentId) &&
                   _pendingNativeNodeDrafts.ContainsKey(componentId);
        }

        private bool IsPendingNativeRemoval(CircuitComponent component)
        {
            return component != null &&
                   _pendingNativeNodeRemovals.Contains(component.UniqueId);
        }

        private bool IsPendingNativeRemoval(AutomationGraphNode node)
        {
            return TryGetNativeComponentId(node, out uint componentId) &&
                   _pendingNativeNodeRemovals.Contains(componentId);
        }

        private void StorePendingNativeNodeDraft(AutomationGraphNode node)
        {
            if (!TryGetNativeComponentId(node, out uint componentId))
                return;

            if (_pendingNativeNodeRemovals.Contains(componentId))
                return;

            _pendingNativeNodeDrafts[componentId] = new AutomationGraphNodeDraft(node);
        }

        private void ClearPendingNativeNodeDraft(AutomationGraphNode node)
        {
            if (TryGetNativeComponentId(node, out uint componentId))
                _pendingNativeNodeDrafts.Remove(componentId);
        }

        private int ApplyPendingNativeNodeDraftsToNative(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            if (graph == null || breadboard == null || _pendingNativeNodeDrafts.Count == 0)
                return 0;

            int applied = 0;
            HashSet<CircuitComponent> ownedComponents = EsuOwnedNativeComponentsFor(breadboard);
            foreach (AutomationGraphNode node in graph.Nodes
                         .Where(node => node?.NativeComponent is CircuitComponent component &&
                                        ownedComponents.Contains(component))
                         .ToList())
            {
                if (!TryGetNativeComponentId(node, out uint componentId) ||
                    !_pendingNativeNodeDrafts.TryGetValue(componentId, out AutomationGraphNodeDraft draft))
                {
                    continue;
                }

                draft.ApplyTo(node);
                ApplyNativeNodeToNativeComponent(node);
                ApplyNativeNodeRect((CircuitComponent)node.NativeComponent, node.Rect);
                _pendingNativeNodeDrafts.Remove(componentId);
                applied++;
            }

            return applied;
        }

        private void ApplyNativeNodeToNativeComponent(AutomationGraphNode node)
        {
            if (!(node?.NativeComponent is CircuitComponent component))
                return;

            if (component is GenericBlockGetter getter)
            {
                if (TryGetBoundTargetBlock(node, out Block target))
                {
                    var context = new NativeBuildContext(_selectedBreadboard, _links);
                    string filter = TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
                        ? EnsureExactBlockFilterName(breadboard, target, context)
                        : CurrentCustomName(target);
                    ConfigureGetterTarget(getter, target, filter);
                    TryConfigureGetterProperty(getter, target.GetType(), node.Property);
                    return;
                }

                Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
                if (blockType != null)
                    TryConfigureGetterProperty(getter, blockType, node.Property);
            }
            else if (component is GenericBlockSetter setter)
            {
                if (TryGetBoundTargetBlock(node, out Block target))
                {
                    var context = new NativeBuildContext(_selectedBreadboard, _links);
                    string filter = TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard)
                        ? EnsureExactBlockFilterName(breadboard, target, context)
                        : CurrentCustomName(target);
                    ConfigureSetterTarget(setter, target, filter);
                    TryConfigureSetterProperty(setter, target.GetType(), node.Property);
                    return;
                }

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

        private bool RemoveNativeGraphNode(AutomationGraphNode node)
        {
            ClearPendingNativeNodeDraft(node);
            if (node?.NativeComponent is CircuitComponent component &&
                TryGetSelectedNativeBreadboard(out _))
            {
                if (IsEsuOwnedNativeComponent(component))
                {
                    _pendingNativeNodeRemovals.Add(component.UniqueId);
                    return true;
                }

                NotifyNativeApply(
                    "Imported native graph blocks are read-only in Automation Builder.",
                    EsuHudNotificationKind.Warning,
                    "Remove imported native blocks in the native breadboard editor if needed.");
                return false;
            }

            return false;
        }

        private static bool RemoveNativeComponentPackage(
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            if (breadboard == null || component == null)
                return false;

            bool present = breadboard.Packages.Contains(component) ||
                           breadboard.Components.Contains(component);
            if (!present)
                return false;

            component.Delete();
            breadboard.RemovePackage(component);
            return true;
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
            node.Property = NormalizeNodeProperty(node.Kind, property);
            node.SetBindingProperty(node.Property);
            SyncStagedLinkPropertyForNode(node);
            node.ValueText = value ?? string.Empty;
            InvalidateAutomationDisplayCache();
            if (!(node.NativeComponent is CircuitComponent))
                return;

            if (IsEsuOwnedNativeNode(node))
            {
                StorePendingNativeNodeDraft(node);
                return;
            }

            ApplyNativeNodeToNativeComponent(node);
            InvalidateNativeAutomationCache();
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

            if (node.IsStaged)
                return TryBindStagedLinkTarget(node, link, out message);

            if (IsEsuOwnedNativeNode(node))
            {
                bool bound = TryBindStagedLinkTarget(node, link, out message);
                if (bound)
                    StorePendingNativeNodeDraft(node);
                return bound;
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

        private void ClearStagedLinkTargetBinding(AutomationGraphNode node)
        {
            if (node == null || !IsEsuOwnedNativeNode(node))
                return;

            node.BindTarget((AutomationGraphTargetBinding)null);
            node.Label = DefaultNodeLabel(node.Kind);
            node.Property = NormalizeNodeProperty(node.Kind, string.Empty);
            node.ValueText = DefaultValue(node.Kind);
            StorePendingNativeNodeDraft(node);
        }

        private bool TryBindStagedLinkTarget(
            AutomationGraphNode node,
            AutomationLink link,
            out string message)
        {
            message = null;
            AutomationBlockRef targetRef;
            if (node.Kind == AutomationNodeKind.InputGetter &&
                link.Kind == AutomationLinkKind.InputToBreadboard)
            {
                targetRef = link.Source;
            }
            else if (node.Kind == AutomationNodeKind.OutputSetter &&
                     link.Kind == AutomationLinkKind.BreadboardToOutput)
            {
                targetRef = link.Target;
            }
            else
            {
                message = "That linked target does not match the selected block direction.";
                return false;
            }

            if (targetRef == null ||
                !targetRef.TryGetBlock(out Block target))
            {
                message = "The selected linked target is no longer available.";
                return false;
            }

            string property = TryResolveNativePropertyLabel(
                node.Kind,
                target.GetType(),
                link.Property,
                out string resolvedProperty)
                ? resolvedProperty
                : string.Empty;
            node.BindTarget(link, property);
            node.Label = targetRef.Name;
            node.Property = property;
            node.ValueText = node.Kind == AutomationNodeKind.InputGetter
                ? "staged signal"
                : "incoming signal";
            if (link.IsStaged && !string.IsNullOrWhiteSpace(property))
                link.SetProperty(property);
            message = node.Kind == AutomationNodeKind.InputGetter
                ? "Automation staged input block now reads " + targetRef.Name + "."
                : "Automation staged output block now sets " + targetRef.Name + ".";
            return true;
        }

        private void SyncStagedLinkPropertyForNode(AutomationGraphNode node)
        {
            if (node?.TargetBinding == null ||
                string.IsNullOrWhiteSpace(node.Property))
            {
                return;
            }

            foreach (AutomationLink link in _links.Where(link =>
                         link?.IsStaged == true &&
                         NodeBindingTargetsMatchLink(node, link)))
            {
                link.SetProperty(node.Property);
            }
        }

        private IEnumerable<string> NativePropertyOptionsForNode(AutomationGraphNode node)
        {
            if (TryGetBoundTargetBlock(node, out Block targetBlock))
            {
                if (node.Kind == AutomationNodeKind.InputGetter)
                    return EnumerateGetterPropertyLabels(targetBlock.GetType());
                if (node.Kind == AutomationNodeKind.OutputSetter)
                    return EnumerateSetterPropertyLabels(targetBlock.GetType());
            }

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

        private static bool IsResolvedNativeProperty(
            AutomationNodeKind kind,
            Type blockType,
            string property)
        {
            return TryResolveNativePropertyLabel(kind, blockType, property, out _);
        }

        private static bool TryResolveNativePropertyLabel(
            AutomationNodeKind kind,
            Type blockType,
            string property,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            if (kind == AutomationNodeKind.InputGetter)
            {
                if (TryMatchGetterReadable(blockType, property, out uint readableId) &&
                    TryGetReadableLabel(blockType, readableId, out label))
                {
                    return true;
                }

                if (TryMatchGetterVariable(blockType, property, out uint setId, out uint propertyId) &&
                    TryGetVariableLabel(blockType, editableOnly: false, setId, propertyId, out label))
                {
                    return true;
                }

                return false;
            }

            if (kind == AutomationNodeKind.OutputSetter)
            {
                if (TryMatchSetterVariable(blockType, property, out uint setId, out uint propertyId) &&
                    TryGetVariableLabel(blockType, editableOnly: true, setId, propertyId, out label))
                {
                    return true;
                }

                return false;
            }

            label = NormalizeNodeProperty(kind, property);
            return true;
        }

        private static bool StagedNodeHasResolvedProperty(AutomationGraphNode node)
        {
            return TryGetBoundTargetBlock(node, out Block targetBlock) &&
                   IsResolvedNativeProperty(node.Kind, targetBlock.GetType(), node.Property);
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
                RefreshNativeAutomationCache(force: true);
                return true;
            }

            return false;
        }

        private void CheckNativeGraphPlan()
        {
            RefreshNativeAutomationCache(force: true);
            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            NativePlan plan = BuildSelectedNativePlan(graph);
            NotifyNativeApply(
                plan.HasErrors ? "Check found blocking automation plan errors." : "Check produced a native automation plan.",
                plan.HasErrors ? EsuHudNotificationKind.Warning : EsuHudNotificationKind.Info,
                plan.Detail);
        }

        private bool RevertEsuOwnedNativeGraph()
        {
            if (!TryGetSelectedNativeBreadboard(out NativeBreadBoard breadboard))
            {
                NotifyNativeApply("Select a live breadboard before reverting ESU-owned native automation.", EsuHudNotificationKind.Warning);
                return false;
            }

            List<NativeOwnerRecord> records = OwnerRecordsFor(breadboard).ToList();
            if (records.Count == 0)
            {
                NotifyNativeApply("No ESU-owned generated native automation was found on this breadboard.", EsuHudNotificationKind.Info);
                return true;
            }

            int componentsRemoved = 0;
            int markersRemoved = 0;
            HashSet<CircuitComponent> ownedComponents = EsuOwnedNativeComponentsFor(breadboard);
            int wiresRemoved = RemoveNativeInputConnectionsFromSources(breadboard, ownedComponents);
            foreach (NativeOwnerRecord record in records)
            {
                _pendingNativeNodeDrafts.Remove(record.ComponentId);
                _pendingNativeNodeRemovals.Remove(record.ComponentId);
                if (record.Target != null &&
                    !ReferenceEquals(record.Target, record.Marker) &&
                    RemoveNativeComponentPackage(breadboard, record.Target))
                {
                    componentsRemoved++;
                }

                if (RemoveNativeComponentPackage(breadboard, record.Marker))
                    markersRemoved++;
            }

            RefreshNativeAutomationCache(force: true);
            if (_selectedBreadboard != null)
                SyncedGraphFor(_selectedBreadboard, force: true);
            MarkAutomationDirty();
            NotifyNativeApply(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Revert removed {0:N0} ESU-owned native component{1}, {2:N0} marker{3}, and {4:N0} native wire{5}.",
                    componentsRemoved,
                    componentsRemoved == 1 ? string.Empty : "s",
                    markersRemoved,
                    markersRemoved == 1 ? string.Empty : "s",
                    wiresRemoved,
                    wiresRemoved == 1 ? string.Empty : "s"),
                EsuHudNotificationKind.Info);
            return true;
        }

        private NativePlan BuildSelectedNativePlan(AutomationGraph graph)
        {
            NativeBreadBoard breadboard = null;
            string breadboardMessage = null;
            bool hasBreadboard = TryResolveNativeBreadboard(
                _selectedBreadboard,
                out breadboard,
                out breadboardMessage);
            NativePlan plan = BuildNativePlan(graph, hasBreadboard ? breadboard : null);
            if (!hasBreadboard)
                plan.AddError(breadboardMessage ?? "Select a live breadboard before checking an automation graph.");
            return plan;
        }

        private NativePlan BuildNativePlan(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            var plan = new NativePlan();
            if (graph == null || graph.Nodes.Count == 0)
            {
                plan.AddWarning("No ESU graph nodes are staged for native lowering.");
            }
            else
            {
                foreach (string issue in NativeGraphReadinessIssues(graph))
                    plan.AddError(issue);

                foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
                {
                    if (node == null)
                        continue;

                    if (node.IsStaged)
                        plan.AddCreate(NativePlanCreateLine(node));
                    else if (HasPendingNativeNodeDraft(node))
                        plan.AddUpdate("Apply pending ESU edits to " + NativePlanSentence(node) + ".");
                    else if (IsEsuOwnedNativeNode(node))
                        plan.AddUpdate(NativePlanUpdateLine(node));
                    else
                        plan.AddUpdate(NativePlanImportedLine(node));
                }

                foreach (AutomationLink link in _links.Where(link => link?.IsStaged == true))
                    plan.AddUpdate(NativePlanLinkBindingLine(graph, link));

                foreach (string line in NativeConnectionPreviewLines(graph, true))
                {
                    string trimmed = (line ?? string.Empty).Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("(", StringComparison.Ordinal))
                        continue;

                    plan.AddUpdate("Connect/update " + trimmed);
                }
            }

            if (breadboard != null)
            {
                int supportedNativeCount = breadboard.Components.Count(IsSupportedAutomationNativeComponent);
                if (supportedNativeCount == 0 &&
                    graph?.Nodes.Any(node => node?.IsStaged == true) != true)
                {
                    plan.AddWarning("The selected native breadboard has no supported automation components.");
                }

                foreach (NativeOwnerRecord record in OwnerRecordsFor(breadboard))
                {
                    if (_pendingNativeNodeRemovals.Contains(record.ComponentId))
                        plan.AddRemove("Apply will remove ESU-owned native " + NativeComponentName(record.Kind) + " #" + record.ComponentId.ToString(CultureInfo.InvariantCulture) + ".");

                    plan.AddRemove(NativePlanRemoveLine(record));
                }

                int ownedInputConnections = CountEsuOwnedNativeInputConnections(breadboard);
                if (ownedInputConnections > 0)
                {
                    plan.AddRemove(
                        "Apply will clear and rebuild " +
                        ownedInputConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " ESU-owned native input connection" +
                        (ownedInputConnections == 1 ? string.Empty : "s") +
                        ".");
                }

                int esuSourcedConnections = CountNativeInputConnectionsFromSources(
                    breadboard,
                    EsuOwnedNativeComponentsFor(breadboard));
                if (esuSourcedConnections > 0)
                {
                    plan.AddRemove(
                        "Revert can clear " +
                        esuSourcedConnections.ToString("N0", CultureInfo.InvariantCulture) +
                        " native input connection" +
                        (esuSourcedConnections == 1 ? string.Empty : "s") +
                        " sourced from ESU-owned components.");
                }
            }

            if (plan.Creates.Count == 0)
                plan.AddWarning("No staged ESU nodes need native creation; Check is only validating/importing existing native graph state.");

            return plan;
        }

        private static string NativePlanCreateLine(AutomationGraphNode node)
        {
            return "Create native " + NativeComponentName(node.Kind) +
                   " for " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanUpdateLine(AutomationGraphNode node)
        {
            return "Update existing native " + NativeComponentName(node.Kind) +
                   " #" + node.Id.ToString(CultureInfo.InvariantCulture) +
                   " from " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanImportedLine(AutomationGraphNode node)
        {
            return "Import read-only native " + NativeComponentName(node.Kind) +
                   " #" + node.Id.ToString(CultureInfo.InvariantCulture) +
                   " from " + NativePlanSentence(node) +
                   " at " + FormatGraphPoint(node.Rect.position) + ".";
        }

        private static string NativePlanLinkBindingLine(
            AutomationGraph graph,
            AutomationLink link)
        {
            string direction = link.Kind == AutomationLinkKind.InputToBreadboard
                ? "input"
                : "output";
            string source = link.Source?.Name ?? "missing source";
            string target = link.Target?.Name ?? "missing target";
            AutomationGraphNode boundNode = graph?.Nodes.FirstOrDefault(node =>
                NodeBindingTargetsMatchLink(node, link));
            string propertyText = boundNode?.TargetBinding?.Property;
            if (string.IsNullOrWhiteSpace(propertyText))
                propertyText = boundNode?.Property;
            if (string.IsNullOrWhiteSpace(propertyText))
                propertyText = link.Property;
            string property = string.IsNullOrWhiteSpace(propertyText)
                ? "unresolved property"
                : propertyText;
            return "Bind staged " + direction + " target " + source + " -> " + target + " | " + property + ".";
        }

        private static string NativePlanRemoveLine(NativeOwnerRecord record)
        {
            if (record?.Target == null)
            {
                return "Revert stale ESU ownership marker for missing native component #" +
                       (record == null ? "unknown" : record.ComponentId.ToString(CultureInfo.InvariantCulture)) +
                       ".";
            }

            return "Revert can remove ESU-owned native " +
                   NativeComponentName(NativeKind(record.Target)) +
                   " #" +
                   record.ComponentId.ToString(CultureInfo.InvariantCulture) +
                   ".";
        }

        private static string FormatGraphPoint(Vector2 point)
        {
            return "(" +
                   point.x.ToString("0.#", CultureInfo.InvariantCulture) +
                   ", " +
                   point.y.ToString("0.#", CultureInfo.InvariantCulture) +
                   ")";
        }

        private static HashSet<CircuitComponent> EsuOwnedNativeComponents(NativeBreadBoard breadboard)
        {
            return new HashSet<CircuitComponent>(
                NativeOwnerRecords(breadboard)
                .Where(record => record?.Target != null &&
                                 !ReferenceEquals(record.Target, record.Marker))
                .Select(record => record.Target));
        }

        private IReadOnlyList<NativeOwnerRecord> OwnerRecordsFor(NativeBreadBoard breadboard)
        {
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            return snapshot == null
                ? Array.Empty<NativeOwnerRecord>()
                : (IReadOnlyList<NativeOwnerRecord>)snapshot.OwnerRecords;
        }

        private HashSet<CircuitComponent> EsuOwnedNativeComponentsFor(NativeBreadBoard breadboard)
        {
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            return snapshot == null
                ? new HashSet<CircuitComponent>()
                : new HashSet<CircuitComponent>(snapshot.OwnedComponents);
        }

        private static bool IsEsuOwnedNativeComponent(
            ISet<CircuitComponent> ownedComponents,
            CircuitComponent component)
        {
            return component != null &&
                   ownedComponents != null &&
                   ownedComponents.Contains(component);
        }

        private static int CountEsuOwnedNativeInputConnections(NativeBreadBoard breadboard)
        {
            HashSet<CircuitComponent> ownedComponents = EsuOwnedNativeComponents(breadboard);
            int count = 0;
            foreach (CircuitComponent component in ownedComponents)
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched == true)
                        count++;
                }
            }

            return count;
        }

        private static int RemoveEsuOwnedNativeInputConnections(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> ownedComponents)
        {
            if (breadboard == null || ownedComponents == null || ownedComponents.Count == 0)
                return 0;

            int removed = 0;
            foreach (CircuitComponent component in ownedComponents.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us.ToList())
                {
                    if (RemoveNativeInputConnection(breadboard, input))
                        removed++;
                }
            }

            return removed;
        }

        private static int CountNativeInputConnectionsFromSources(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> sourceComponents)
        {
            if (breadboard == null || sourceComponents == null || sourceComponents.Count == 0)
                return 0;

            int count = 0;
            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us)
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        IsEsuOwnedNativeComponent(sourceComponents, input.OurOutput.Them?.OurComponent))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int RemoveNativeInputConnectionsFromSources(
            NativeBreadBoard breadboard,
            ISet<CircuitComponent> sourceComponents)
        {
            if (breadboard == null || sourceComponents == null || sourceComponents.Count == 0)
                return 0;

            int removed = 0;
            foreach (CircuitComponent component in breadboard.Components.ToList())
            {
                if (component?.BInputs == null)
                    continue;

                foreach (BInput input in component.BInputs.Us.ToList())
                {
                    if (input?.OurOutput?.IsLatched == true &&
                        IsEsuOwnedNativeComponent(sourceComponents, input.OurOutput.Them?.OurComponent) &&
                        RemoveNativeInputConnection(breadboard, input))
                    {
                        removed++;
                    }
                }
            }

            return removed;
        }

        private int ApplyPendingNativeNodeRemovalsToNative(NativeBreadBoard breadboard)
        {
            if (breadboard == null || _pendingNativeNodeRemovals.Count == 0)
                return 0;

            int removed = 0;
            foreach (uint componentId in _pendingNativeNodeRemovals.ToList())
            {
                NativeOwnerRecord record = OwnerRecordsFor(breadboard)
                    .FirstOrDefault(candidate => candidate.ComponentId == componentId);
                if (record == null)
                {
                    _pendingNativeNodeDrafts.Remove(componentId);
                    _pendingNativeNodeRemovals.Remove(componentId);
                    continue;
                }

                if (record.Target != null &&
                    !ReferenceEquals(record.Target, record.Marker) &&
                    RemoveNativeComponentPackage(breadboard, record.Target))
                {
                    removed++;
                }

                RemoveNativeComponentPackage(breadboard, record.Marker);
                _pendingNativeNodeDrafts.Remove(componentId);
                _pendingNativeNodeRemovals.Remove(componentId);
            }

            return removed;
        }

        private static bool RemoveNativeInputConnection(
            NativeBreadBoard breadboard,
            BInput input)
        {
            if (breadboard == null ||
                input?.OurOutput?.IsLatched != true)
            {
                return false;
            }

            try
            {
                new DeleteConnectionCommand(breadboard, input).Execute();
            }
            catch
            {
                // Fall through to the direct latch API below.
            }

            if (input.OurOutput?.IsLatched == true)
            {
                try
                {
                    input.OurOutput.Unlatch();
                }
                catch
                {
                    return false;
                }
            }

            return input.OurOutput?.IsLatched != true;
        }

        private static CircuitComponent NativeValueForGraphSlot(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind)
        {
            AutomationGraphConnection connection = GraphValueConnection(graph, host, slotKind);
            return TryGetNativeComponent(
                connection?.From,
                out CircuitComponent component)
                ? component
                : null;
        }

        private NativeApplyResult ValidateAndConnectNativeGraph(NativeBreadBoard breadboard)
        {
            AutomationGraph graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            NativePlan plan = BuildNativePlan(graph, breadboard);
            if (plan.Errors.Count > 0)
            {
                return NativeApplyResult.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Apply blocked: fix {0:N0} automation readiness issue{1} first.",
                        plan.Errors.Count,
                        plan.Errors.Count == 1 ? string.Empty : "s"),
                    plan.Detail);
            }

            NativeLoweringResult lowering = LowerStagedGraphNodesToNative(graph, breadboard);
            if (lowering.HasErrors)
            {
                return NativeApplyResult.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Apply blocked: {0:N0} staged block{1} could not be lowered.",
                        lowering.Errors.Count,
                        lowering.Errors.Count == 1 ? string.Empty : "s"),
                    lowering.Detail);
            }

            if (lowering.CreatedCount > 0)
            {
                RefreshNativeAutomationCache(force: true);
                graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            }

            int removedNodes = ApplyPendingNativeNodeRemovalsToNative(breadboard);
            int appliedNodeEdits = ApplyPendingNativeNodeDraftsToNative(graph, breadboard);
            if (removedNodes > 0 || appliedNodeEdits > 0)
            {
                RefreshNativeAutomationCache(force: true);
                graph = _selectedBreadboard == null ? null : SyncedGraphFor(_selectedBreadboard, force: true);
            }

            List<AutomationGraphNode> nativeNodes = graph?.Nodes
                .Where(node => node?.NativeComponent is CircuitComponent component &&
                               IsSupportedAutomationNativeComponent(component))
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            if (nativeNodes == null || nativeNodes.Count == 0)
            {
                if (removedNodes > 0 || appliedNodeEdits > 0 || lowering.CreatedCount > 0)
                {
                    return new NativeApplyResult(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Native graph applied: {0:N0} staged block{1} lowered, {2:N0} owned edit{3} applied, {4:N0} owned node{5} removed.",
                            lowering.CreatedCount,
                            lowering.CreatedCount == 1 ? string.Empty : "s",
                            appliedNodeEdits,
                            appliedNodeEdits == 1 ? string.Empty : "s",
                            removedNodes,
                            removedNodes == 1 ? string.Empty : "s"),
                        EsuHudNotificationKind.Info,
                        lowering.Detail);
                }

                return NativeApplyResult.Warning("The native breadboard graph has no components to connect.");
            }

            HashSet<CircuitComponent> esuOwnedComponents = EsuOwnedNativeComponentsFor(breadboard);
            int removedConnections = RemoveEsuOwnedNativeInputConnections(breadboard, esuOwnedComponents);
            int connected = 0;
            int alreadyConnected = 0;
            int valueConnections = 0;
            IReadOnlyList<AutomationGraphConnection> stackConnections = GraphConnections(graph, AutomationGraphConnectionKind.Stack);
            foreach (AutomationGraphConnection connection in stackConnections)
            {
                if (!TryGetNativeComponent(connection?.From, out CircuitComponent from) ||
                    !TryGetNativeComponent(connection.To, out CircuitComponent to))
                {
                    continue;
                }

                if (!IsEsuOwnedNativeComponent(esuOwnedComponents, to))
                    continue;

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

            foreach (AutomationGraphNode hostNode in nativeNodes.Where(node =>
                         TryGetNativeComponent(node, out CircuitComponent component) &&
                         IsEsuOwnedNativeComponent(esuOwnedComponents, component) &&
                         AcceptsValueSlot(node.Kind)))
            {
                if (!TryGetNativeComponent(hostNode, out CircuitComponent host))
                    continue;

                if (host is NativeSwitch switchComponent)
                {
                    AutomationNodeKind switchKind = hostNode.Kind;
                    if (switchKind == AutomationNodeKind.IfCondition)
                    {
                        switchComponent.Threshold.Us = 0.5f;
                    }
                    else
                    {
                        CircuitComponent thresholdValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Threshold);
                        if (TryApplySwitchThresholdValue(switchComponent, thresholdValue))
                            valueConnections++;
                    }

                    CircuitComponent passValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInput(breadboard, passValue, switchComponent.Pass, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent elseValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Else);
                    if (TryApplySwitchElseValue(switchComponent, elseValue))
                        valueConnections++;
                    continue;
                }

                if (host is Evaluator evaluator &&
                    IsMathEvaluatorKind(hostNode.Kind))
                {
                    AutomationNodeKind evaluatorKind = hostNode.Kind;
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, evaluator, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent operandValue = NativeValueForGraphSlot(graph, hostNode, MathOperandSlotKind(evaluatorKind));
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
                    IsLogicGateKind(hostNode.Kind))
                {
                    AutomationNodeKind logicKind = hostNode.Kind;
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, logicGate, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    if (logicKind != AutomationNodeKind.LogicNot)
                    {
                        CircuitComponent secondValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.LogicB);
                        if (TryConnectComponentToInputAt(breadboard, secondValue, logicGate, 1, ref connected, ref alreadyConnected))
                            valueConnections++;
                    }

                    continue;
                }

                if (host is FuzzyThreshold fuzzyThreshold &&
                    IsFuzzyThresholdKind(hostNode.Kind))
                {
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent thresholdValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Threshold);
                    if (TryApplyFuzzyThresholdValue(fuzzyThreshold, thresholdValue))
                        valueConnections++;

                    continue;
                }

                if (host is MaxMin maxMin &&
                    IsMaxMinKind(hostNode.Kind))
                {
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToInputAt(breadboard, sourceValue, maxMin, 0, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent secondValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.MathB);
                    if (TryConnectComponentToInputAt(breadboard, secondValue, maxMin, 1, ref connected, ref alreadyConnected))
                        valueConnections++;

                    continue;
                }

                if (host is Clamp clamp)
                {
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent minValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Min);
                    if (TryApplyClampMinimum(clamp, minValue))
                        valueConnections++;

                    CircuitComponent maxValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Max);
                    if (TryApplyClampMaximum(clamp, maxValue))
                        valueConnections++;
                    continue;
                }

                if (host is Delay delay)
                {
                    CircuitComponent sourceValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                    if (TryConnectComponentToHost(breadboard, sourceValue, host, ref connected, ref alreadyConnected))
                        valueConnections++;

                    CircuitComponent secondsValue = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Seconds);
                    if (TryApplyDelaySeconds(delay, secondsValue))
                        valueConnections++;
                    continue;
                }

                CircuitComponent value = NativeValueForGraphSlot(graph, hostNode, AutomationValueSlotKind.Pass);
                if (TryConnectComponentToHost(breadboard, value, host, ref connected, ref alreadyConnected))
                    valueConnections++;
            }

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Native graph applied: {0:N0} staged block{1} lowered, {2:N0} owned edit{3} applied, {4:N0} owned node{5} removed, {6:N0} stale ESU connection{7} cleared, {8:N0} new connection{9}, {10:N0} already connected, {11:N0} value slot{12}.",
                lowering.CreatedCount,
                lowering.CreatedCount == 1 ? string.Empty : "s",
                appliedNodeEdits,
                appliedNodeEdits == 1 ? string.Empty : "s",
                removedNodes,
                removedNodes == 1 ? string.Empty : "s",
                removedConnections,
                removedConnections == 1 ? string.Empty : "s",
                connected,
                connected == 1 ? string.Empty : "s",
                alreadyConnected,
                valueConnections,
                valueConnections == 1 ? string.Empty : "s");
            return new NativeApplyResult(
                message,
                EsuHudNotificationKind.Info,
                CombineDetail(
                    lowering.Detail,
                    "Apply is idempotent: it lowers staged ESU blocks into ESU-owned native components, then connects visual ESU stack chains top-to-bottom and visual socketed value blocks into native inputs without appending duplicates."));
        }

        private NativeLoweringResult LowerStagedGraphNodesToNative(
            AutomationGraph graph,
            NativeBreadBoard breadboard)
        {
            var result = new NativeLoweringResult();
            if (graph == null || breadboard == null)
                return result;

            List<AutomationGraphNode> stagedNodes = graph.Nodes
                .Where(node => node?.IsStaged == true)
                .OrderBy(node => node.Rect.y)
                .ThenBy(node => node.Rect.x)
                .ToList();
            if (stagedNodes.Count == 0)
                return result;

            foreach (AutomationGraphNode node in stagedNodes)
            {
                if (!CanLowerStagedNode(node, out string issue))
                    result.AddError(issue);
            }

            if (result.HasErrors)
                return result;

            var loweredIds = new HashSet<int>();
            var loweredIdMap = new Dictionary<int, int>();
            foreach (AutomationGraphNode node in stagedNodes)
            {
                if (TryCreateNativeComponentFromStagedNode(
                        breadboard,
                        node,
                        out CircuitComponent component,
                        out string message,
                        out string warningDetail))
                {
                    loweredIds.Add(node.Id);
                    loweredIdMap[node.Id] = unchecked((int)component.UniqueId);
                    result.AddCreated(
                        "Lowered " +
                        NativePlanSentence(node) +
                        " into native " +
                        NativeComponentName(NativeKind(component)) +
                        " #" +
                        component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                        ".");
                    result.AddWarning(warningDetail);
                }
                else
                {
                    result.AddError(message ?? "Failed to lower staged " + NodeTitle(node.Kind) + ".");
                }
            }

            if (loweredIds.Count > 0)
            {
                graph.RetargetConnectionNodeIds(loweredIdMap);
                graph.Nodes.RemoveAll(node => node != null && loweredIds.Contains(node.Id));
                if (loweredIds.Contains(graph.SelectedNodeId))
                    graph.SelectedNodeId = 0;
            }

            return result;
        }

        private bool CanLowerStagedNode(
            AutomationGraphNode node,
            out string issue)
        {
            issue = null;
            if (node == null)
            {
                issue = "Missing staged graph node.";
                return false;
            }

            if (!CanLowerStagedNodeKind(node.Kind))
            {
                issue = ShortText(BlockSentenceTitle(node), 38) +
                        " cannot be lowered into a supported native breadboard component yet.";
                return false;
            }

            if (node.Kind == AutomationNodeKind.InputGetter ||
                node.Kind == AutomationNodeKind.OutputSetter)
            {
                if (!TryGetBoundTargetBlock(node, out Block targetBlock))
                {
                    issue = ShortText(BlockSentenceTitle(node), 38) + " has no staged target binding.";
                    return false;
                }

                if (!IsResolvedNativeProperty(node.Kind, targetBlock.GetType(), node.Property))
                {
                    issue = ShortText(BlockSentenceTitle(node), 38) + " has no resolved native property.";
                    return false;
                }
            }

            return true;
        }

        private bool TryCreateNativeComponentFromStagedNode(
            NativeBreadBoard breadboard,
            AutomationGraphNode node,
            out CircuitComponent component,
            out string message,
            out string warningDetail)
        {
            component = null;
            message = null;
            warningDetail = null;
            if (breadboard == null || node == null)
            {
                message = "Missing native breadboard or staged graph node.";
                return false;
            }

            component = CreateLooseNativeComponent(node.Kind);
            if (component == null)
            {
                message = "Automation Builder does not know how to lower staged " + NodeTitle(node.Kind) + " yet.";
                return false;
            }

            if (!TryConfigureNativeComponentTargetFromStagedNode(
                    breadboard,
                    node,
                    component,
                    out message,
                    out warningDetail))
            {
                component = null;
                return false;
            }

            component.OutlineColor.Us = NodeColor(node.Kind);
            ApplyNativeNodeRect(component, node.Rect);
            var nativeNode = new AutomationGraphNode(
                unchecked((int)component.UniqueId),
                node.Kind,
                node.Rect,
                node.Label,
                node.Property,
                node.ValueText,
                component);
            ApplyNativeNodeEdits(nativeNode, node.Label, node.Property, node.ValueText);
            breadboard.NewPackage(component);
            AddNativeOwnerMarker(breadboard, component, node.Kind);
            return true;
        }

        private bool TryConfigureNativeComponentTargetFromStagedNode(
            NativeBreadBoard breadboard,
            AutomationGraphNode node,
            CircuitComponent component,
            out string message,
            out string warningDetail)
        {
            message = null;
            warningDetail = null;
            if (node.Kind != AutomationNodeKind.InputGetter &&
                node.Kind != AutomationNodeKind.OutputSetter)
            {
                return true;
            }

            if (!TryGetBoundTargetBlock(node, out Block targetBlock))
            {
                message = ShortText(BlockSentenceTitle(node), 38) + " has no live staged target block.";
                return false;
            }

            var context = new NativeBuildContext(_selectedBreadboard, _links);
            string filter = EnsureExactBlockFilterName(breadboard, targetBlock, context);
            if (component is GenericBlockGetter getter &&
                node.Kind == AutomationNodeKind.InputGetter)
            {
                ConfigureGetterTarget(getter, targetBlock, filter);
                if (!TryConfigureGetterProperty(getter, targetBlock.GetType(), node.Property))
                {
                    message = ShortText(BlockSentenceTitle(node), 38) + " has no readable native property selected.";
                    return false;
                }

                warningDetail = context.HasWarnings ? context.Detail : null;
                return true;
            }

            if (component is GenericBlockSetter setter &&
                node.Kind == AutomationNodeKind.OutputSetter)
            {
                ConfigureSetterTarget(setter, targetBlock, filter);
                if (!TryConfigureSetterProperty(setter, targetBlock.GetType(), node.Property))
                {
                    message = ShortText(BlockSentenceTitle(node), 38) + " has no writable native property selected.";
                    return false;
                }

                warningDetail = context.HasWarnings ? context.Detail : null;
                return true;
            }

            message = "Staged " + NodeTitle(node.Kind) + " did not create the expected native component type.";
            return false;
        }

        private static bool CanLowerStagedNodeKind(AutomationNodeKind kind)
        {
            switch (kind)
            {
                case AutomationNodeKind.InputGetter:
                case AutomationNodeKind.OutputSetter:
                case AutomationNodeKind.IfCondition:
                case AutomationNodeKind.IfLessThan:
                case AutomationNodeKind.Constant:
                case AutomationNodeKind.Random:
                case AutomationNodeKind.LogicNot:
                case AutomationNodeKind.LogicAnd:
                case AutomationNodeKind.LogicOr:
                case AutomationNodeKind.LogicXor:
                case AutomationNodeKind.LogicNand:
                case AutomationNodeKind.LogicNor:
                case AutomationNodeKind.LogicXnor:
                case AutomationNodeKind.CompareAboveThreshold:
                case AutomationNodeKind.CompareBelowThreshold:
                case AutomationNodeKind.MathAdd:
                case AutomationNodeKind.MathSubtract:
                case AutomationNodeKind.MathMultiply:
                case AutomationNodeKind.MathMax:
                case AutomationNodeKind.MathMin:
                case AutomationNodeKind.Clamp:
                case AutomationNodeKind.Smooth:
                case AutomationNodeKind.Forever:
                case AutomationNodeKind.Comment:
                    return true;
                default:
                    return false;
            }
        }

        private static string CombineDetail(
            params string[] details)
        {
            return string.Join(
                "\n",
                (details ?? Array.Empty<string>())
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .Select(detail => detail.Trim())
                .Distinct()
                .ToArray());
        }

        private IEnumerable<string> NativeGraphReadinessIssues(AutomationGraph graph)
        {
            if (graph == null || graph.Nodes.Count == 0)
                yield break;

            foreach (string issue in ValueSocketConflictIssues(graph))
                yield return issue;

            foreach (AutomationGraphNode node in graph.Nodes.OrderBy(node => node.Rect.y).ThenBy(node => node.Rect.x))
            {
                if (node == null)
                    continue;

                foreach (string issue in NativeNodeReadinessIssues(graph, node))
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
            AutomationGraphNode node)
        {
            string nodeName = ShortText(BlockSentenceTitle(node), 38);
            if (node.IsStaged)
            {
                foreach (string issue in StagedNodeReadinessIssues(graph, node, nodeName))
                    yield return issue;
                yield break;
            }

            if (node.NativeComponent is GenericBlockGetter getter)
            {
                if (IsEsuOwnedNativeNode(node))
                {
                    if (!TryGetBoundTargetBlock(node, out _))
                        yield return nodeName + " has no linked input target; click its target slot or link an input block.";
                    if (!StagedNodeHasResolvedProperty(node))
                        yield return nodeName + " has no readable native property selected.";
                }
                else
                {
                    if (!HasNativeBlockTarget(getter.BlockTypeName.Us))
                        yield return nodeName + " has no linked input target; click its target slot or link an input block.";
                    if (!GetterHasNativeProperty(getter))
                        yield return nodeName + " has no readable native property selected.";
                }

                yield break;
            }

            if (node.NativeComponent is GenericBlockSetter setter)
            {
                bool hasIncomingSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedValue = GraphValueNode(graph, node) != null;
                if (IsEsuOwnedNativeNode(node))
                {
                    if (!TryGetBoundTargetBlock(node, out _))
                        yield return nodeName + " has no linked output target; click its target slot or link an output block.";
                    if (!StagedNodeHasResolvedProperty(node))
                        yield return nodeName + " has no writable native property selected.";
                }
                else
                {
                    if (!HasNativeBlockTarget(setter.BlockTypeName.Us))
                        yield return nodeName + " has no linked output target; click its target slot or link an output block.";
                    if (!SetterHasNativeProperty(setter))
                        yield return nodeName + " has no writable native property selected.";
                }

                if (!hasIncomingSignal && !hasSnappedValue)
                    yield return nodeName + " has no incoming signal or visual socket value block.";
                if (hasIncomingSignal && hasSnappedValue)
                    yield return nodeName + " has both a stack signal and a value socket input; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is NativeSwitch)
            {
                if (PreviousFlowNode(graph, node) == null)
                    yield return nodeName + " has no incoming signal to compare; place a read/value-producing block above it.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.Pass) == null)
                    yield return nodeName + " has no then value in the switch pass socket.";
                if (!HasGraphBodyChildren(graph, node) && NextFlowNode(graph, node) == null)
                    yield return nodeName + " switch output does not drive any action block yet; place a Set block in its body or below it.";
                yield break;
            }

            if (node.NativeComponent is LogicGate &&
                IsLogicGateKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first boolean input; place a read/value-producing block above it or snap/wire one into its " + LogicFirstInputLabel(node.Kind) + " socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and a " + LogicFirstInputLabel(node.Kind) + " socket input; remove one input path for clearer lowering.";
                if (node.Kind != AutomationNodeKind.LogicNot &&
                    GraphValueNode(graph, node, AutomationValueSlotKind.LogicB) == null)
                {
                    yield return nodeName + " has no second boolean input in the b socket.";
                }
                yield break;
            }

            if (node.NativeComponent is FuzzyThreshold &&
                IsFuzzyThresholdKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no signal to compare; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is MaxMin &&
                IsMaxMinKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first numeric input; place a read/value-producing block above it or snap/wire one into its a socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an A socket input; remove one input path for clearer lowering.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.MathB) == null)
                    yield return nodeName + " has no second numeric input in the b socket.";
                yield break;
            }

            if (node.NativeComponent is Evaluator &&
                IsMathEvaluatorKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Clamp)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.NativeComponent is Delay &&
                node.Kind == AutomationNodeKind.Smooth)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.Forever &&
                !HasGraphBodyChildren(graph, node))
            {
                yield return nodeName + " has no action blocks inside its body.";
            }
        }

        private IEnumerable<string> StagedNodeReadinessIssues(
            AutomationGraph graph,
            AutomationGraphNode node,
            string nodeName)
        {
            if (!CanLowerStagedNodeKind(node.Kind))
            {
                yield return nodeName + " cannot be lowered into a supported native breadboard component yet.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.InputGetter)
            {
                if (!TryGetBoundTargetBlock(node, out _))
                    yield return nodeName + " has no staged input target binding.";
                if (!StagedNodeHasResolvedProperty(node))
                    yield return nodeName + " has no readable native property selected.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.OutputSetter)
            {
                bool hasIncomingSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedValue = GraphValueNode(graph, node) != null;
                if (!TryGetBoundTargetBlock(node, out _))
                    yield return nodeName + " has no staged output target binding.";
                if (!StagedNodeHasResolvedProperty(node))
                    yield return nodeName + " has no writable native property selected.";
                if (!hasIncomingSignal && !hasSnappedValue)
                    yield return nodeName + " has no incoming signal or visual socket value block.";
                if (hasIncomingSignal && hasSnappedValue)
                    yield return nodeName + " has both a stack signal and a value socket input; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.IfCondition ||
                node.Kind == AutomationNodeKind.IfLessThan)
            {
                if (PreviousFlowNode(graph, node) == null)
                    yield return nodeName + " has no incoming signal to compare; place a read/value-producing block above it.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.Pass) == null)
                    yield return nodeName + " has no then value in the switch pass socket.";
                if (!HasGraphBodyChildren(graph, node) && NextFlowNode(graph, node) == null)
                    yield return nodeName + " switch output does not drive any action block yet; place a Set block in its body or below it.";
                yield break;
            }

            if (IsLogicGateKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first boolean input; place a read/value-producing block above it or snap/wire one into its " + LogicFirstInputLabel(node.Kind) + " socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and a " + LogicFirstInputLabel(node.Kind) + " socket input; remove one input path for clearer lowering.";
                if (node.Kind != AutomationNodeKind.LogicNot &&
                    GraphValueNode(graph, node, AutomationValueSlotKind.LogicB) == null)
                {
                    yield return nodeName + " has no second boolean input in the b socket.";
                }
                yield break;
            }

            if (IsFuzzyThresholdKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no signal to compare; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (IsMaxMinKind(node.Kind))
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no first numeric input; place a read/value-producing block above it or snap/wire one into its a socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an A socket input; remove one input path for clearer lowering.";
                if (GraphValueNode(graph, node, AutomationValueSlotKind.MathB) == null)
                    yield return nodeName + " has no second numeric input in the b socket.";
                yield break;
            }

            if (IsMathEvaluatorKind(node.Kind) ||
                node.Kind == AutomationNodeKind.Clamp ||
                node.Kind == AutomationNodeKind.Smooth)
            {
                bool hasStackSignal = PreviousFlowNode(graph, node) != null;
                bool hasSnappedSource = GraphValueNode(graph, node, AutomationValueSlotKind.Pass) != null;
                if (!hasStackSignal && !hasSnappedSource)
                    yield return nodeName + " has no input signal; place a read/value-producing block above it or snap/wire one into its input socket.";
                if (hasStackSignal && hasSnappedSource)
                    yield return nodeName + " has both a stack signal and an input socket signal; remove one input path for clearer lowering.";
                yield break;
            }

            if (node.Kind == AutomationNodeKind.Forever &&
                !HasGraphBodyChildren(graph, node))
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
            if (getter == null)
                return false;

            Type blockType = getter.BlockType ?? FindBlockTypeByName(getter.BlockTypeName.Us);
            if (getter.ReadableAttributeId.Us != NativeUnselectedCode)
                return TryGetReadableLabel(blockType, getter.ReadableAttributeId.Us, out _);

            return getter.BlockPropertyId.Us != NativeUnselectedCode &&
                   TryGetVariableLabel(blockType, editableOnly: false, getter.BlockSetId.Us, getter.BlockPropertyId.Us, out _);
        }

        private static bool SetterHasNativeProperty(GenericBlockSetter setter)
        {
            if (setter == null)
                return false;

            Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
            return setter.BlockPropertyId.Us != NativeUnselectedCode &&
                   TryGetVariableLabel(blockType, editableOnly: true, setter.BlockSetId.Us, setter.BlockPropertyId.Us, out _);
        }

        private static AutomationGraphNode PreviousFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null)
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Stack)
                .FirstOrDefault(connection => ReferenceEquals(connection.To, node))
                ?.From;
        }

        private static AutomationGraphNode NextFlowNode(
            AutomationGraph graph,
            AutomationGraphNode node)
        {
            if (graph == null || node == null)
                return null;

            return GraphConnections(graph, AutomationGraphConnectionKind.Stack)
                .FirstOrDefault(connection => ReferenceEquals(connection.From, node))
                ?.To;
        }

        private static AutomationGraphNode GraphValueNode(
            AutomationGraph graph,
            AutomationGraphNode host,
            AutomationValueSlotKind slotKind = AutomationValueSlotKind.Pass)
        {
            return GraphValueConnection(graph, host, slotKind)?.From;
        }

        private static bool HasGraphBodyChildren(
            AutomationGraph graph,
            AutomationGraphNode host)
        {
            if (graph == null || host == null)
                return false;

            return GraphConnections(graph, AutomationGraphConnectionKind.Body)
                .Any(connection => ReferenceEquals(connection.From, host));
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
            return BuildNativeLinks(SnapshotFor(breadboard));
        }

        private List<AutomationLink> BuildNativeLinks(NativeBreadboardSnapshot snapshot)
        {
            var links = new List<AutomationLink>();
            if (snapshot?.Breadboard == null || _selectedBreadboard == null)
                return links;

            foreach (CircuitComponent component in snapshot.Components)
            {
                if (IsPendingNativeRemoval(component))
                    continue;

                if (component is GenericBlockGetter getter)
                {
                    AutomationBlockRef target = ResolveNativeTargetBlock(snapshot, getter, getter.BlockTypeName.Us, getter.BlockFilter.Us, out string status);
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
                    AutomationBlockRef target = ResolveNativeTargetBlock(snapshot, setter, setter.BlockTypeName.Us, setter.BlockFilter.Us, out string status);
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
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            return NativeComponentToNode(breadboardRef, SnapshotFor(breadboard), component);
        }

        private AutomationGraphNode NativeComponentToNode(
            AutomationBlockRef breadboardRef,
            NativeBreadboardSnapshot snapshot,
            CircuitComponent component)
        {
            AutomationNodeKind kind = NativeKind(component);
            Rect rect = NativeComponentRect(component);
            var node = new AutomationGraphNode(
                (int)component.UniqueId,
                kind,
                rect,
                NativeNodeLabel(component),
                NativeNodeProperty(component),
                NativeNodeValue(component),
                component);
            if (component is GenericBlockGetter getter)
            {
                AutomationBlockRef target = ResolveNativeTargetBlock(
                    snapshot,
                    getter,
                    getter.BlockTypeName.Us,
                    getter.BlockFilter.Us,
                    out _);
                if (target != null)
                    node.BindTarget(new AutomationLink(0, target, breadboardRef, AutomationLinkKind.InputToBreadboard, NativePropertyLabel(getter), Color.white, component));
            }
            else if (component is GenericBlockSetter setter)
            {
                AutomationBlockRef target = ResolveNativeTargetBlock(
                    snapshot,
                    setter,
                    setter.BlockTypeName.Us,
                    setter.BlockFilter.Us,
                    out _);
                if (target != null)
                    node.BindTarget(new AutomationLink(0, breadboardRef, target, AutomationLinkKind.BreadboardToOutput, NativePropertyLabel(setter), Color.white, component));
            }

            return node;
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

        private void AddNativeOwnerMarker(
            NativeBreadBoard breadboard,
            CircuitComponent component,
            AutomationNodeKind kind)
        {
            if (breadboard == null ||
                component == null ||
                OwnerRecordsFor(breadboard).Any(record => record.ComponentId == component.UniqueId))
            {
                return;
            }

            var marker = (NativeComment)CreateNativeComment(NativeOwnerMarkerText(component, kind));
            marker.ClipText.Us = true;
            marker.ScaleWithZoom.Us = false;
            marker.OutlineColor.Us = new Color(0f, 0f, 0f, 0f);
            ApplyNativeNodeRect(marker, NativeOwnerMarkerRect(component));
            breadboard.NewPackage(marker);
        }

        private static Rect NativeOwnerMarkerRect(CircuitComponent component)
        {
            float x = component?.X.Us ?? 0f;
            float y = component?.Y.Us ?? 0f;
            return new Rect(x - 0.5f, y - 0.5f, 1f, 1f);
        }

        private static string NativeOwnerMarkerText(
            CircuitComponent component,
            AutomationNodeKind kind)
        {
            return NativeOwnerMarkerPrefix +
                   "v1|component=" +
                   component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                   "|kind=" +
                   kind;
        }

        private static bool IsNativeOwnerMarker(CircuitComponent component) =>
            component is NativeComment comment &&
            (comment.InputValue.Us ?? string.Empty).StartsWith(NativeOwnerMarkerPrefix, StringComparison.Ordinal);

        private static IEnumerable<NativeOwnerRecord> NativeOwnerRecords(NativeBreadBoard breadboard)
        {
            if (breadboard == null)
                yield break;

            foreach (NativeOwnerRecord record in BuildNativeOwnerRecords(breadboard.Components.ToList()))
                yield return record;
        }

        private static List<NativeOwnerRecord> BuildNativeOwnerRecords(
            IReadOnlyList<CircuitComponent> components)
        {
            var records = new List<NativeOwnerRecord>();
            if (components == null || components.Count == 0)
                return records;

            Dictionary<uint, CircuitComponent> componentsById = components
                .Where(component => component != null)
                .GroupBy(component => component.UniqueId)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (CircuitComponent marker in components.Where(IsNativeOwnerMarker))
            {
                if (!TryParseNativeOwnerMarker(marker, out uint componentId, out AutomationNodeKind kind))
                    continue;

                componentsById.TryGetValue(componentId, out CircuitComponent target);
                if (ReferenceEquals(target, marker))
                    target = null;

                records.Add(new NativeOwnerRecord(componentId, kind, target, marker));
            }

            return records;
        }

        private static int RemoveNativeOwnerMarkersForComponent(
            NativeBreadBoard breadboard,
            CircuitComponent component)
        {
            if (breadboard == null || component == null)
                return 0;

            int removed = 0;
            foreach (NativeOwnerRecord record in NativeOwnerRecords(breadboard)
                         .Where(record => record.ComponentId == component.UniqueId)
                         .ToList())
            {
                if (RemoveNativeComponentPackage(breadboard, record.Marker))
                    removed++;
            }

            return removed;
        }

        private static bool TryParseNativeOwnerMarker(
            CircuitComponent marker,
            out uint componentId,
            out AutomationNodeKind kind)
        {
            componentId = 0u;
            kind = AutomationNodeKind.Comment;
            if (!(marker is NativeComment comment))
                return false;

            string text = comment.InputValue.Us ?? string.Empty;
            if (!text.StartsWith(NativeOwnerMarkerPrefix, StringComparison.Ordinal))
                return false;

            string[] parts = text.Substring(NativeOwnerMarkerPrefix.Length)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (part.StartsWith("component=", StringComparison.OrdinalIgnoreCase))
                {
                    uint.TryParse(
                        part.Substring("component=".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out componentId);
                }
                else if (part.StartsWith("kind=", StringComparison.OrdinalIgnoreCase))
                {
                    Enum.TryParse(part.Substring("kind=".Length), out kind);
                }
            }

            return componentId != 0u;
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
            NativeBreadboardSnapshot snapshot = SnapshotFor(breadboard);
            string existing = CurrentCustomName(target);
            if (!string.IsNullOrWhiteSpace(existing) &&
                IsSafeTextOnlyBlockFilterName(existing) &&
                !IsLegacyAutoGeneratedBlockName(existing) &&
                IsUniqueBlockName(snapshot, target, existing))
            {
                return existing;
            }

            string generated = GenerateStableBlockName(target);
            int suffix = 0;
            string candidate = generated;
            while (!IsUniqueBlockName(snapshot, target, candidate))
                candidate = generated + AlphabeticSuffix(suffix++);

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
            string hashInput =
                (target.GetType().FullName ?? target.GetType().Name) + "|" +
                ConstructIdentityHash(target).ToString(CultureInfo.InvariantCulture) + "|" +
                cell.x.ToString(CultureInfo.InvariantCulture) + "|" +
                cell.y.ToString(CultureInfo.InvariantCulture) + "|" +
                cell.z.ToString(CultureInfo.InvariantCulture);
            return AutoNamePrefix +
                   LettersOnly(target.GetType().Name) +
                   AlphabeticToken(StableNameHash(hashInput), 8);
        }

        private static int ConstructIdentityHash(Block target)
        {
            try
            {
                object construct = target?.GetConstructableOrSubConstructable();
                return construct == null ? 0 : RuntimeHelpers.GetHashCode(construct);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsSafeTextOnlyBlockFilterName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Trim().All(character =>
                       character >= 'A' && character <= 'Z' ||
                       character >= 'a' && character <= 'z');
        }

        private static bool IsLegacyAutoGeneratedBlockName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Trim().StartsWith(LegacyAutoNamePrefix, StringComparison.Ordinal);
        }

        private static string LettersOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Block";

            string letters = new string(text.Where(character =>
                character >= 'A' && character <= 'Z' ||
                character >= 'a' && character <= 'z').ToArray());
            return string.IsNullOrWhiteSpace(letters) ? "Block" : letters;
        }

        private static uint StableNameHash(string text)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;
            foreach (char character in text ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash == 0u ? offset : hash;
        }

        private static string AlphabeticToken(
            uint value,
            int length)
        {
            int count = Mathf.Max(1, length);
            var chars = new char[count];
            uint state = value == 0u ? 2166136261u : value;
            for (int index = 0; index < count; index++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                chars[index] = (char)('A' + state % 26u);
            }

            return new string(chars);
        }

        private static string AlphabeticSuffix(int index)
        {
            string[] suffixes =
            {
                "Alpha",
                "Beta",
                "Gamma",
                "Delta",
                "Epsilon",
                "Zeta",
                "Eta",
                "Theta",
                "Iota",
                "Kappa",
                "Lambda",
                "Mu",
                "Nu",
                "Xi",
                "Omicron",
                "Pi",
                "Rho",
                "Sigma",
                "Tau",
                "Upsilon",
                "Phi",
                "Chi",
                "Psi",
                "Omega"
            };

            if (index >= 0 && index < suffixes.Length)
                return suffixes[index];

            return AlphabeticToken((uint)Mathf.Max(0, index), 6);
        }

        private static bool IsUniqueBlockName(
            NativeBreadBoard breadboard,
            Block target,
            string name)
        {
            return IsUniqueBlockName(
                NativeBreadboardSnapshot.Create(breadboard),
                target,
                name);
        }

        private static bool IsUniqueBlockName(
            NativeBreadboardSnapshot snapshot,
            Block target,
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (snapshot == null || target == null)
                return false;

            snapshot.BlocksByTypeName.TryGetValue(target.GetType().Name, out List<Block> blocks);
            int matches = (blocks ?? new List<Block>())
                .Count(block =>
                    block != null &&
                    !block.IsDeleted &&
                    !ReferenceEquals(block, target) &&
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

        private NativeBreadboardSnapshot SnapshotFor(NativeBreadBoard breadboard)
        {
            if (breadboard == null)
                return null;

            return _nativeSnapshot != null &&
                   ReferenceEquals(_nativeSnapshot.Breadboard, breadboard)
                ? _nativeSnapshot
                : NativeBreadboardSnapshot.Create(breadboard);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockGetter getter,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), getter, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
            GenericBlockGetter getter,
            string blockTypeName,
            string filter,
            out string status)
        {
            Block live = getter.PotentiallyAffectedBlocks.FirstOrDefault(block => block != null && !block.IsDeleted);
            if (live != null)
            {
                status = ExactnessStatus(snapshot, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(snapshot, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            GenericBlockSetter setter,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), setter, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
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
                status = ExactnessStatus(snapshot, live, filter);
                return BlockRefFromBlock(live);
            }

            return ResolveNativeTargetBlock(snapshot, blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadBoard breadboard,
            string blockTypeName,
            string filter,
            out string status)
        {
            return ResolveNativeTargetBlock(SnapshotFor(breadboard), blockTypeName, filter, out status);
        }

        private AutomationBlockRef ResolveNativeTargetBlock(
            NativeBreadboardSnapshot snapshot,
            string blockTypeName,
            string filter,
            out string status)
        {
            List<Block> candidates = (snapshot?.BlocksByTypeName.TryGetValue(blockTypeName ?? string.Empty, out List<Block> indexed) == true
                    ? indexed
                    : new List<Block>())
                .Where(block =>
                    block != null &&
                    !block.IsDeleted &&
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
            return ExactnessStatus(
                NativeBreadboardSnapshot.Create(breadboard),
                target,
                filter);
        }

        private static string ExactnessStatus(
            NativeBreadboardSnapshot snapshot,
            Block target,
            string filter)
        {
            return !string.IsNullOrWhiteSpace(filter) && IsUniqueBlockName(snapshot, target, filter)
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
            if (IsNativeOwnerMarker(component))
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
            return CachedLiveValueText(node, "getter", ComputeInputGetterCurrentValueText);
        }

        private string OutputSetterCurrentValueText(AutomationGraphNode node)
        {
            return CachedLiveValueText(node, "setter", ComputeOutputSetterCurrentValueText);
        }

        private string CachedLiveValueText(
            AutomationGraphNode node,
            string prefix,
            Func<AutomationGraphNode, string> compute)
        {
            if (node == null || compute == null)
                return string.Empty;

            float now = Time.unscaledTime;
            if (_livePreviewNativeVersion != _nativeAutomationCacheVersion ||
                now >= _nextLivePreviewRefreshTime)
            {
                InvalidateLivePreviewCache();
                _livePreviewNativeVersion = _nativeAutomationCacheVersion;
                _nextLivePreviewRefreshTime = now + LivePreviewIntervalSeconds;
            }

            string key = prefix + "|" + node.Id.ToString(CultureInfo.InvariantCulture);
            if (_liveValuePreviewCache.TryGetValue(key, out string cached))
                return cached;

            string value = compute(node);
            _liveValuePreviewCache[key] = value;
            return value;
        }

        private string ComputeInputGetterCurrentValueText(AutomationGraphNode node)
        {
            if (TryGetNativeGetterPreview(node, out string preview))
                return preview;

            return string.IsNullOrWhiteSpace(node?.ValueText)
                ? "native signal"
                : node.ValueText;
        }

        private string ComputeOutputSetterCurrentValueText(AutomationGraphNode node) =>
            TryGetNativeSetterPreview(node, out string preview)
                ? preview
                : "target value";

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

            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (candidate.Id != readableId || candidate.Property == null)
                    continue;

                readableProperty = candidate.Property;
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
            foreach (NativeVariableCandidate candidate in EnumerateNativeVariables(blockType, editableOnly: false))
            {
                if (candidate.SetId != setId ||
                    candidate.PropertyId != propertyId ||
                    candidate.PackageProperty == null ||
                    candidate.VariableProperty == null)
                {
                    continue;
                }

                packageProperty = candidate.PackageProperty;
                variableProperty = candidate.VariableProperty;
                return true;
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

                return "unresolved property";
            }

            if (getter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: false, getter.BlockSetId.Us, getter.BlockPropertyId.Us, out string label))
                    return label;

                return "unresolved property";
            }

            return "unresolved property";
        }

        private static string NativePropertyLabel(GenericBlockSetter setter)
        {
            Type blockType = setter.BlockType ?? FindBlockTypeByName(setter.BlockTypeName.Us);
            if (setter.BlockPropertyId.Us != NativeUnselectedCode)
            {
                if (TryGetVariableLabel(blockType, editableOnly: true, setter.BlockSetId.Us, setter.BlockPropertyId.Us, out string label))
                    return label;

                return "unresolved property";
            }

            return "unresolved property";
        }

        private static bool TryGetReadableLabel(
            Type blockType,
            uint readableId,
            out string label)
        {
            label = null;
            if (blockType == null)
                return false;

            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (candidate.Id != readableId)
                    continue;

                label = candidate.Label;
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
            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Label))
                    yield return candidate.Label;
            }
        }

        private static bool TryMatchGetterReadable(
            Type blockType,
            string query,
            out uint readableId)
        {
            readableId = NativeUnselectedCode;
            foreach (NativeReadableCandidate candidate in EnumerateReadableCandidates(blockType))
            {
                if (!MatchesQuery(query, candidate.CandidateText))
                    continue;

                readableId = candidate.Id;
                return readableId != NativeUnselectedCode;
            }

            return false;
        }

        private static IEnumerable<NativeReadableCandidate> EnumerateReadableCandidates(Type blockType)
        {
            if (blockType == null)
                return Enumerable.Empty<NativeReadableCandidate>();

            if (s_readableCandidatesByType.TryGetValue(blockType, out List<NativeReadableCandidate> cached))
                return cached;

            var candidates = new List<NativeReadableCandidate>();
            Type finder = FindType("Ftd.Blocks.BreadBoards.GenericGetter.GetterSourceFinder");
            MethodInfo method = finder?.GetMethod(
                "GetReadablesForBlockType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            IEnumerable items = method?.Invoke(null, new object[] { blockType }) as IEnumerable;
            if (items != null)
            {
                foreach (object item in items)
                {
                    PropertyInfo property = GetMemberValue(item, "property", "Item1") as PropertyInfo;
                    object attribute = GetMemberValue(item, "attribute", "Item2");
                    if (!TryGetUInt(attribute, "Index", out uint id))
                        continue;

                    candidates.Add(new NativeReadableCandidate(
                        id,
                        CandidateText(property?.Name, attribute),
                        CandidateLabel(property?.Name, attribute),
                        property));
                }
            }

            s_readableCandidatesByType[blockType] = candidates;
            return candidates;
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

            if (IsDefaultPropertyQuery(query) &&
                TryGetSingleEditableNativeVariable(blockType, out NativeVariableCandidate single))
            {
                setId = single.SetId;
                propertyId = single.PropertyId;
                return true;
            }

            setId = NativeUnselectedCode;
            propertyId = NativeUnselectedCode;
            return false;
        }

        private static bool IsDefaultPropertyQuery(string query)
        {
            string trimmed = query?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ||
                   string.Equals(trimmed, "value", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetSingleEditableNativeVariable(
            Type blockType,
            out NativeVariableCandidate single)
        {
            single = default;
            List<NativeVariableCandidate> candidates = EnumerateNativeVariables(blockType, editableOnly: true)
                .GroupBy(candidate => candidate.SetId.ToString(CultureInfo.InvariantCulture) + "|" + candidate.PropertyId.ToString(CultureInfo.InvariantCulture))
                .Select(group => group.First())
                .Take(2)
                .ToList();
            if (candidates.Count != 1)
                return false;

            single = candidates[0];
            return true;
        }

        private static IEnumerable<NativeVariableCandidate> EnumerateNativeVariables(
            Type blockType,
            bool editableOnly)
        {
            if (blockType == null)
                return Enumerable.Empty<NativeVariableCandidate>();

            string cacheKey = blockType.AssemblyQualifiedName + "|" + (editableOnly ? "1" : "0");
            if (s_nativeVariablesByType.TryGetValue(cacheKey, out List<NativeVariableCandidate> cached))
                return cached;

            var candidates = new List<NativeVariableCandidate>();
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
            {
                s_nativeVariablesByType[cacheKey] = candidates;
                return candidates;
            }

            object record = packagesMethod.Invoke(null, new object[] { blockType });
            IEnumerable dataPackages = GetMemberValue(record, "OurDataPackageProperties") as IEnumerable;
            if (dataPackages == null)
            {
                s_nativeVariablesByType[cacheKey] = candidates;
                return candidates;
            }

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
                    PropertyInfo variableProperty = GetMemberValue(variable, "PropertyInfo") as PropertyInfo;
                    if (variableProperty == null && !string.IsNullOrWhiteSpace(propertyName))
                    {
                        variableProperty = packageType.GetProperty(
                            propertyName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    candidates.Add(new NativeVariableCandidate(
                        setId,
                        propertyId,
                        CandidateText(packageProperty.Name, attribute, propertyName),
                        CandidateLabel(packageProperty.Name, attribute, propertyName),
                        packageProperty,
                        variableProperty));
                }
            }

            s_nativeVariablesByType[cacheKey] = candidates;
            return candidates;
        }

        private static Type FindBlockTypeByName(string blockTypeName)
        {
            if (string.IsNullOrWhiteSpace(blockTypeName))
                return null;

            if (s_blockTypeByNameCache.TryGetValue(blockTypeName, out Type cached))
                return cached;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type match = assembly.GetTypesSafe()
                    .FirstOrDefault(type =>
                        typeof(Block).IsAssignableFrom(type) &&
                        string.Equals(type.Name, blockTypeName, StringComparison.Ordinal));
                if (match != null)
                {
                    s_blockTypeByNameCache[blockTypeName] = match;
                    return match;
                }
            }

            s_blockTypeByNameCache[blockTypeName] = null;
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
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            if (s_typeByNameCache.TryGetValue(fullName, out Type cached))
                return cached;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    s_typeByNameCache[fullName] = type;
                    return type;
                }
            }

            Type resolved = Type.GetType(fullName, throwOnError: false);
            s_typeByNameCache[fullName] = resolved;
            return resolved;
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
                string label,
                PropertyInfo packageProperty = null,
                PropertyInfo variableProperty = null)
            {
                SetId = setId;
                PropertyId = propertyId;
                CandidateText = candidateText ?? string.Empty;
                Label = label ?? string.Empty;
                PackageProperty = packageProperty;
                VariableProperty = variableProperty;
            }

            internal uint SetId { get; }

            internal uint PropertyId { get; }

            internal string CandidateText { get; }

            internal string Label { get; }

            internal PropertyInfo PackageProperty { get; }

            internal PropertyInfo VariableProperty { get; }
        }

        private readonly struct NativeReadableCandidate
        {
            internal NativeReadableCandidate(
                uint id,
                string candidateText,
                string label,
                PropertyInfo property)
            {
                Id = id;
                CandidateText = candidateText ?? string.Empty;
                Label = label ?? string.Empty;
                Property = property;
            }

            internal uint Id { get; }

            internal string CandidateText { get; }

            internal string Label { get; }

            internal PropertyInfo Property { get; }
        }

        private sealed class NativeBreadboardSnapshot
        {
            private NativeBreadboardSnapshot(
                NativeBreadBoard breadboard,
                List<CircuitComponent> components,
                List<NativeOwnerRecord> ownerRecords,
                List<Block> constructBlocks,
                int signature)
            {
                Breadboard = breadboard;
                Components = components ?? new List<CircuitComponent>();
                ComponentsById = Components
                    .Where(component => component != null)
                    .GroupBy(component => component.UniqueId)
                    .ToDictionary(group => group.Key, group => group.First());
                OwnerRecords = ownerRecords ?? new List<NativeOwnerRecord>();
                OwnedComponents = new HashSet<CircuitComponent>(
                    OwnerRecords
                    .Where(record => record?.Target != null &&
                                     !ReferenceEquals(record.Target, record.Marker))
                    .Select(record => record.Target));
                _constructBlocks = constructBlocks;
                Signature = signature;
            }

            private List<Block> _constructBlocks;
            private Dictionary<string, List<Block>> _blocksByTypeName;

            internal NativeBreadBoard Breadboard { get; }

            internal List<CircuitComponent> Components { get; }

            internal Dictionary<uint, CircuitComponent> ComponentsById { get; }

            internal List<NativeOwnerRecord> OwnerRecords { get; }

            internal HashSet<CircuitComponent> OwnedComponents { get; }

            internal List<Block> ConstructBlocks =>
                _constructBlocks ?? (_constructBlocks = EnumerateConstructBlocks(Breadboard)
                    .Where(block => block != null)
                    .ToList());

            internal Dictionary<string, List<Block>> BlocksByTypeName =>
                _blocksByTypeName ?? (_blocksByTypeName = ConstructBlocks
                    .Where(block => block != null)
                    .GroupBy(block => block.GetType().Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal));

            internal int Signature { get; }

            internal static NativeBreadboardSnapshot Create(NativeBreadBoard breadboard)
            {
                if (breadboard == null)
                    return null;

                List<CircuitComponent> components = breadboard.Components.ToList();
                List<NativeOwnerRecord> ownerRecords = BuildNativeOwnerRecords(components);
                int signature = ComputeSignature(components, ownerRecords);
                return new NativeBreadboardSnapshot(
                    breadboard,
                    components,
                    ownerRecords,
                    constructBlocks: null,
                    signature);
            }

            private static int ComputeSignature(
                IReadOnlyList<CircuitComponent> components,
                IReadOnlyList<NativeOwnerRecord> ownerRecords)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (CircuitComponent component in components ?? Array.Empty<CircuitComponent>())
                    {
                        if (component == null)
                            continue;

                        hash = hash * 31 + component.UniqueId.GetHashCode();
                        hash = hash * 31 + (component.GetType().FullName ?? component.GetType().Name).GetHashCode();
                        hash = hash * 31 + Mathf.RoundToInt(component.X.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Y.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Width.Us * 10f);
                        hash = hash * 31 + Mathf.RoundToInt(component.Height.Us * 10f);
                        hash = hash * 31 + NativeComponentSignature(component);
                    }

                    foreach (NativeOwnerRecord record in ownerRecords ?? Array.Empty<NativeOwnerRecord>())
                    {
                        if (record == null)
                            continue;

                        hash = hash * 31 + record.ComponentId.GetHashCode();
                        hash = hash * 31 + record.Kind.GetHashCode();
                    }

                    return hash;
                }
            }

            private static int NativeComponentSignature(CircuitComponent component)
            {
                unchecked
                {
                    int hash = 23;
                    if (component is GenericBlockGetter getter)
                    {
                        hash = hash * 31 + (getter.BlockTypeName.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + (getter.BlockFilter.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + getter.ReadableAttributeId.Us.GetHashCode();
                        hash = hash * 31 + getter.BlockSetId.Us.GetHashCode();
                        hash = hash * 31 + getter.BlockPropertyId.Us.GetHashCode();
                    }
                    else if (component is GenericBlockSetter setter)
                    {
                        hash = hash * 31 + (setter.BlockTypeName.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + (setter.BlockFilter.Us ?? string.Empty).GetHashCode();
                        hash = hash * 31 + setter.BlockSetId.Us.GetHashCode();
                        hash = hash * 31 + setter.BlockPropertyId.Us.GetHashCode();
                    }
                    else if (component is ConstantInput constant)
                    {
                        hash = hash * 31 + constant.InputValue.Us.GetHashCode();
                    }
                    else if (component is RandomInput random)
                    {
                        hash = hash * 31 + random.RandomLimits.Lower.GetHashCode();
                        hash = hash * 31 + random.RandomLimits.Upper.GetHashCode();
                    }
                    else if (component is Clamp clamp)
                    {
                        hash = hash * 31 + clamp.MinMax.Lower.GetHashCode();
                        hash = hash * 31 + clamp.MinMax.Upper.GetHashCode();
                    }
                    else if (component is Delay delay)
                    {
                        hash = hash * 31 + delay.DelayTime.Us.GetHashCode();
                    }
                    else if (component is NativeComment comment)
                    {
                        hash = hash * 31 + (comment.InputValue.Us ?? string.Empty).GetHashCode();
                    }

                    try
                    {
                        if (component.BInputs != null)
                        {
                            foreach (BInput input in component.BInputs.Us)
                            {
                                if (input?.OurOutput?.IsLatched == true)
                                    hash = hash * 31 + (input.OurOutput.Them?.OurComponent?.UniqueId ?? 0u).GetHashCode();
                            }
                        }
                    }
                    catch
                    {
                        // Signature is an invalidation hint; missing a native input is not fatal.
                    }

                    return hash;
                }
            }
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

        private sealed class NativeOwnerRecord
        {
            internal NativeOwnerRecord(
                uint componentId,
                AutomationNodeKind kind,
                CircuitComponent target,
                CircuitComponent marker)
            {
                ComponentId = componentId;
                Kind = kind;
                Target = target;
                Marker = marker;
            }

            internal uint ComponentId { get; }

            internal AutomationNodeKind Kind { get; }

            internal CircuitComponent Target { get; }

            internal CircuitComponent Marker { get; }
        }

        private sealed class NativeLoweringResult
        {
            internal List<string> Errors { get; } = new List<string>();

            internal List<string> Warnings { get; } = new List<string>();

            internal List<string> Created { get; } = new List<string>();

            internal int CreatedCount => Created.Count;

            internal bool HasErrors => Errors.Count > 0;

            internal string Detail => string.Join("\n", DetailLines().ToArray());

            internal void AddError(string text) => AddUnique(Errors, text);

            internal void AddWarning(string text) => AddUnique(Warnings, text);

            internal void AddCreated(string text) => AddUnique(Created, text);

            private IEnumerable<string> DetailLines()
            {
                foreach (string line in SectionLines("Lowered", Created))
                    yield return line;
                foreach (string line in SectionLines("Lowering warnings", Warnings))
                    yield return line;
                foreach (string line in SectionLines("Lowering errors", Errors))
                    yield return line;
            }

            private static IEnumerable<string> SectionLines(
                string title,
                IReadOnlyList<string> lines)
            {
                if (lines == null || lines.Count == 0)
                    yield break;

                yield return title;
                foreach (string line in lines)
                    yield return "  - " + line;
            }

            private static void AddUnique(
                ICollection<string> lines,
                string text)
            {
                if (lines == null || string.IsNullOrWhiteSpace(text))
                    return;

                string trimmed = text.Trim();
                if (!lines.Contains(trimmed))
                    lines.Add(trimmed);
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

        private sealed class NativePlan
        {
            internal List<string> Errors { get; } = new List<string>();

            internal List<string> Warnings { get; } = new List<string>();

            internal List<string> Creates { get; } = new List<string>();

            internal List<string> Updates { get; } = new List<string>();

            internal List<string> Removes { get; } = new List<string>();

            internal bool HasErrors => Errors.Count > 0;

            internal string Summary =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:N0} error{1}, {2:N0} warning{3}, {4:N0} create, {5:N0} update, {6:N0} remove.",
                    Errors.Count,
                    Errors.Count == 1 ? string.Empty : "s",
                    Warnings.Count,
                    Warnings.Count == 1 ? string.Empty : "s",
                    Creates.Count,
                    Updates.Count,
                    Removes.Count);

            internal string Detail => string.Join("\n", DetailLines().ToArray());

            internal void AddError(string text) => AddUnique(Errors, text);

            internal void AddWarning(string text) => AddUnique(Warnings, text);

            internal void AddCreate(string text) => AddUnique(Creates, text);

            internal void AddUpdate(string text) => AddUnique(Updates, text);

            internal void AddRemove(string text) => AddUnique(Removes, text);

            internal IEnumerable<string> DetailLines()
            {
                yield return "Errors";
                foreach (string line in SectionLines(Errors))
                    yield return line;
                yield return "Warnings";
                foreach (string line in SectionLines(Warnings))
                    yield return line;
                yield return "Create";
                foreach (string line in SectionLines(Creates))
                    yield return line;
                yield return "Update";
                foreach (string line in SectionLines(Updates))
                    yield return line;
                yield return "Remove";
                foreach (string line in SectionLines(Removes))
                    yield return line;
            }

            private static IEnumerable<string> SectionLines(IReadOnlyList<string> lines)
            {
                if (lines == null || lines.Count == 0)
                {
                    yield return "  (none)";
                    yield break;
                }

                foreach (string line in lines)
                    yield return "  - " + line;
            }

            private static void AddUnique(
                ICollection<string> lines,
                string text)
            {
                if (lines == null || string.IsNullOrWhiteSpace(text))
                    return;

                string trimmed = text.Trim();
                if (!lines.Contains(trimmed))
                    lines.Add(trimmed);
            }
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
