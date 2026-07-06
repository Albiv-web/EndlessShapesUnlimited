using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal static class AutomationRuntimeDiagnostics
    {
        internal static AutomationRuntimeDiagnosticResult Run(
            AutomationTarget controller,
            IReadOnlyList<AutomationTarget> linkedTargets)
        {
            var builder = new AutomationRuntimeDiagnosticBuilder();
            try
            {
                if (controller == null)
                {
                    builder.Fail("No Automation controller is selected.");
                    return builder.ToResult();
                }

                if (controller.Block == null)
                    builder.Fail("Selected controller has no live FtD Block instance.");
                else
                    builder.Pass("Selected controller has a live FtD Block instance.");

                if (controller.Controller == null)
                {
                    builder.Fail("Selected target is not classified as a Breadboard, ACB, ACB Controller, or Missile Breadboard.");
                    return builder.ToResult();
                }

                builder.Pass(
                    "Controller classified as " +
                    controller.Controller.Label +
                    " at cell " +
                    controller.LocalPosition.x.ToString(CultureInfo.InvariantCulture) +
                    "," +
                    controller.LocalPosition.y.ToString(CultureInfo.InvariantCulture) +
                    "," +
                    controller.LocalPosition.z.ToString(CultureInfo.InvariantCulture) +
                    ".");

                switch (controller.Controller.Kind)
                {
                    case AutomationControllerKind.Breadboard:
                    case AutomationControllerKind.AiBreadboard:
                    case AutomationControllerKind.MissileBreadboard:
                        ProbeBreadboard(controller, linkedTargets, builder);
                        break;
                    case AutomationControllerKind.Acb:
                        ProbeAcb(controller, builder);
                        break;
                    case AutomationControllerKind.AcbController:
                        ProbeAcbController(controller, builder);
                        break;
                    default:
                        builder.Warn("No runtime probe is defined for this Automation controller kind.");
                        break;
                }

                ProbeLinkedTargets(linkedTargets, builder);
            }
            catch (Exception exception)
            {
                builder.Fail(
                    "Runtime diagnostic threw " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message);
            }

            return builder.ToResult();
        }

        private static void ProbeBreadboard(
            AutomationTarget controller,
            IReadOnlyList<AutomationTarget> linkedTargets,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            if (!AutomationBreadboardInspector.TryCreate(
                    controller.Block,
                    controller.Controller,
                    out AutomationBreadboardInspector inspector,
                    out string reason))
            {
                builder.Fail(reason ?? "Native Breadboard graph was not found.");
                return;
            }

            builder.Pass("Native Breadboard board discovered: " + inspector.BoardTypeName + ".");
            int packageCount = inspector.PackageCount;
            int componentCount = inspector.Components.Count;
            int availableCount = inspector.AvailableComponents.Count;
            builder.Pass(
                "Board reports " +
                packageCount.ToString(CultureInfo.InvariantCulture) +
                " package(s), " +
                componentCount.ToString(CultureInfo.InvariantCulture) +
                " visible component(s), and " +
                availableCount.ToString(CultureInfo.InvariantCulture) +
                " available component type(s).");

            if (availableCount == 0)
                builder.Fail("Breadboard AvailableComponentTypes is empty, so ESU cannot add native graph nodes.");

            if (inspector.TryRewriteCurrentSettings(out string writeMessage))
                builder.Pass(writeMessage);
            else
                builder.Fail(writeMessage);

            ProbeRequiredBreadboardComponents(inspector, builder);
            ProbeExpandedBreadboardComponents(inspector, builder);
            if (controller.Controller.Kind == AutomationControllerKind.MissileBreadboard)
                ProbeMissileBreadboardComponents(inspector, builder);

            ProbeBreadboardProxyPrerequisites(inspector, linkedTargets, builder);
            ProbeExistingProxyPropertyPickers(inspector, builder);
            ProbeSwitchFailExpressionReadiness(inspector, builder);
            ProbeNativeBreadboardPersistenceSnapshot(controller, inspector, linkedTargets, builder);
        }

        private static void ProbeRequiredBreadboardComponents(
            AutomationBreadboardInspector inspector,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            string[] required =
            {
                "GenericBlockGetter",
                "GenericBlockSetter",
                "Evaluator",
                "Switch",
                "LogicGate",
                "ConstantInput"
            };
            string[] missing = required
                .Where(component => !inspector.CanAddComponent(component))
                .ToArray();
            if (missing.Length == 0)
            {
                builder.Pass("Common Breadboard components are advertised for proxy, graph, and code editing.");
                return;
            }

            builder.Warn("Breadboard is missing advertised component(s): " + string.Join(", ", missing) + ".");
        }

        private static void ProbeExpandedBreadboardComponents(
            AutomationBreadboardInspector inspector,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            var families = new[]
            {
                new BreadboardComponentFamily("PID", "pid"),
                new BreadboardComponentFamily("Threshold", "threshold"),
                new BreadboardComponentFamily("Clamp", "clamp"),
                new BreadboardComponentFamily("Delay", "delay"),
                new BreadboardComponentFamily("Sum", "sum"),
                new BreadboardComponentFamily("Multiply", "multiply"),
                new BreadboardComponentFamily("Variable Reader", "variable", "reader"),
                new BreadboardComponentFamily("Variable Writer", "variable", "writer")
            };
            string[] missing = families
                .Where(family => !inspector.CanAddComponentMatching(family.SearchTerms))
                .Select(family => family.Label)
                .ToArray();
            int advertised = families.Length - missing.Length;
            if (missing.Length == 0)
            {
                builder.Pass("Extended Breadboard component palette is fully advertised.");
                return;
            }

            builder.Warn(
                "Extended Breadboard component palette advertised " +
                advertised.ToString(CultureInfo.InvariantCulture) +
                "/" +
                families.Length.ToString(CultureInfo.InvariantCulture) +
                " family/families; missing " +
                string.Join(", ", missing) +
                ".");
        }

        private static void ProbeMissileBreadboardComponents(
            AutomationBreadboardInspector inspector,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            int matches = 0;
            if (inspector.CanAddComponentMatching("thrust"))
                matches++;
            if (inspector.CanAddComponentMatching("detonate"))
                matches++;
            if (inspector.CanAddComponentMatching("guidance"))
                matches++;
            if (inspector.CanAddComponentMatching("target"))
                matches++;
            if (inspector.CanAddComponentMatching("proximity", "fuse"))
                matches++;

            if (matches > 0)
            {
                builder.Pass(
                    "Missile Breadboard advertised " +
                    matches.ToString(CultureInfo.InvariantCulture) +
                    " expected missile-specific component family/families.");
                return;
            }

            builder.Warn("Missile Breadboard did not advertise expected missile output/fuse component names.");
        }

        private static void ProbeBreadboardProxyPrerequisites(
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationTarget> linkedTargets,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            int count = linkedTargets?.Count ?? 0;
            if (count == 0)
            {
                builder.Warn("No linked world targets are present, so Generic Getter/Setter proxy picking was not exercised.");
                return;
            }

            bool canGetter = inspector.CanAddComponent("GenericBlockGetter");
            bool canSetter = inspector.CanAddComponent("GenericBlockSetter");
            if (canGetter || canSetter)
                builder.Pass("Linked target proxy prerequisites are available on this board.");
            else
                builder.Fail("Linked targets exist, but this board does not advertise Generic Getter or Generic Setter.");
        }

        private static void ProbeExistingProxyPropertyPickers(
            AutomationBreadboardInspector inspector,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            AutomationBreadboardComponentSummary[] proxies = inspector.Components
                .Where(component => component != null && component.IsGenericProxy)
                .Take(4)
                .ToArray();
            if (proxies.Length == 0)
            {
                builder.Warn("No existing Generic Getter/Setter proxy nodes are present, so property-picker enumeration was not exercised.");
                return;
            }

            int selectable = 0;
            int empty = 0;
            foreach (AutomationBreadboardComponentSummary proxy in proxies)
            {
                IReadOnlyList<AutomationBreadboardProxyOption> options =
                    inspector.ProxyPropertyOptions(proxy, string.Empty, 8);
                int count = options.Count(option => option != null && !option.IsClear);
                selectable += count;
                if (count == 0)
                    empty++;
            }

            if (empty == 0)
            {
                builder.Pass(
                    "Generic Getter/Setter property-picker enumeration exposed " +
                    selectable.ToString(CultureInfo.InvariantCulture) +
                    " selectable option(s) across " +
                    proxies.Length.ToString(CultureInfo.InvariantCulture) +
                    " proxy node(s).");
                return;
            }

            builder.Warn(
                "Generic Getter/Setter property-picker enumeration found " +
                empty.ToString(CultureInfo.InvariantCulture) +
                "/" +
                proxies.Length.ToString(CultureInfo.InvariantCulture) +
                " proxy node(s) without selectable properties.");
        }

        private static void ProbeSwitchFailExpressionReadiness(
            AutomationBreadboardInspector inspector,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            AutomationBreadboardComponentSummary[] switches = inspector.Components
                .Where(component => component != null && component.IsSwitch)
                .Take(8)
                .ToArray();
            if (switches.Length == 0)
            {
                builder.SetSwitchFailExpressionReadiness(probeRan: true, ready: false, maxVisibleInputs: 0);
                builder.Warn("No existing Switch node is present; compile an expression-else if/else recipe and rerun checks to validate fail-expression wiring.");
                return;
            }

            int maxInputs = switches
                .Select(component => inspector.InputPorts(component, 8).Count)
                .DefaultIfEmpty(0)
                .Max();
            builder.SetSwitchFailExpressionReadiness(probeRan: true, ready: maxInputs >= 3, maxVisibleInputs: maxInputs);
            if (maxInputs >= 3)
            {
                builder.Pass(
                    "Existing Switch node exposes input 2 for expression-else fail wiring; max visible Switch inputs: " +
                    maxInputs.ToString(CultureInfo.InvariantCulture) +
                    ".");
                return;
            }

            builder.Warn(
                "Existing Switch node did not expose input 2; max visible Switch inputs: " +
                maxInputs.ToString(CultureInfo.InvariantCulture) +
                ".");
        }

        private static void ProbeNativeBreadboardPersistenceSnapshot(
            AutomationTarget controller,
            AutomationBreadboardInspector inspector,
            IReadOnlyList<AutomationTarget> linkedTargets,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            AutomationBreadboardComponentSummary[] components = inspector.Components
                .Where(component => component != null)
                .OrderBy(component => component.UniqueId)
                .ThenBy(component => component.TypeName, StringComparer.Ordinal)
                .Take(64)
                .ToArray();

            string componentFingerprint = string.Join("|", components
                .Select(ComponentFingerprint)
                .ToArray());
            string wireFingerprint = string.Join("|", components
                .SelectMany(component => WireFingerprints(inspector, component))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
            string linkedFingerprint = string.Join("|", (linkedTargets ?? Array.Empty<AutomationTarget>())
                .Where(target => target != null)
                .OrderBy(target => target.StableKey, StringComparer.Ordinal)
                .Select(LinkedTargetFingerprint)
                .ToArray());
            string boardFingerprint =
                ControllerFingerprint(controller) +
                "|board=" + CleanSegment(inspector.BoardTypeName) +
                "|name=" + CleanSegment(inspector.ControllerName) +
                "|packages=" + inspector.PackageCount.ToString(CultureInfo.InvariantCulture) +
                "|components=" + components.Length.ToString(CultureInfo.InvariantCulture) +
                "|available=" + inspector.AvailableComponents.Count.ToString(CultureInfo.InvariantCulture) +
                "|grid=" + BoolBit(inspector.GridEnabled) +
                "|help=" + BoolBit(inspector.HelpLabels) +
                "|blur=" + BoolBit(inspector.Blur);
            string combined = boardFingerprint +
                              "|C:" + componentFingerprint +
                              "|W:" + wireFingerprint +
                              "|L:" + linkedFingerprint;
            string hash = StableHash64(combined);
            builder.SetNativePersistenceFingerprint(hash);

            builder.Pass(
                "Native persistence fingerprint " +
                hash +
                " covers " +
                components.Length.ToString(CultureInfo.InvariantCulture) +
                " component(s), " +
                CountWireFingerprints(inspector, components).ToString(CultureInfo.InvariantCulture) +
                " wire(s), and " +
                ((linkedTargets?.Count) ?? 0).ToString(CultureInfo.InvariantCulture) +
                " linked target(s); compare this line before save and after reload.");

            AutomationBreadboardComponentSummary[] proxies = components
                .Where(component => component.IsGenericProxy)
                .Take(8)
                .ToArray();
            if (proxies.Length > 0)
            {
                string proxyHash = StableHash64(string.Join("|", proxies.Select(ProxyFingerprint).ToArray()));
                builder.SetGenericProxyFingerprint(proxyHash);
                builder.Pass(
                    "Generic proxy fingerprint: " +
                    proxyHash +
                    " across " +
                    proxies.Length.ToString(CultureInfo.InvariantCulture) +
                    " proxy node(s).");
            }
            else
            {
                builder.Warn("No Generic Getter/Setter proxy fingerprint is available until proxy nodes are created.");
            }

            if (componentFingerprint.Length == 0)
                builder.Warn("Native persistence snapshot has no visible components yet.");
        }

        private static string ControllerFingerprint(AutomationTarget controller)
        {
            if (controller == null)
                return "controller=<none>";

            return
                "controller=" + CleanSegment(controller.Label) +
                "|kind=" + CleanSegment(controller.Controller?.Kind.ToString() ?? string.Empty) +
                "|type=" + CleanSegment(controller.RuntimeType) +
                "|cell=" +
                controller.LocalPosition.x.ToString(CultureInfo.InvariantCulture) +
                "," +
                controller.LocalPosition.y.ToString(CultureInfo.InvariantCulture) +
                "," +
                controller.LocalPosition.z.ToString(CultureInfo.InvariantCulture);
        }

        private static string ComponentFingerprint(AutomationBreadboardComponentSummary component)
        {
            return
                component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                ":" +
                CleanSegment(component.TypeName) +
                ":" +
                CleanSegment(component.Label) +
                ":" +
                component.InputCount.ToString(CultureInfo.InvariantCulture) +
                "/" +
                component.OutputCount.ToString(CultureInfo.InvariantCulture) +
                ":" +
                RoundForFingerprint(component.X) +
                "," +
                RoundForFingerprint(component.Y) +
                ":" +
                ProxyFingerprint(component);
        }

        private static string ProxyFingerprint(AutomationBreadboardComponentSummary component)
        {
            if (component == null || !component.IsGenericProxy)
                return string.Empty;

            return
                CleanSegment(component.BlockTypeName) +
                "," +
                CleanSegment(component.BlockFilter) +
                "," +
                component.ReadableAttributeId.ToString(CultureInfo.InvariantCulture) +
                "," +
                component.BlockPropertyId.ToString(CultureInfo.InvariantCulture) +
                "," +
                component.BlockSetId.ToString(CultureInfo.InvariantCulture);
        }

        private static IEnumerable<string> WireFingerprints(
            AutomationBreadboardInspector inspector,
            AutomationBreadboardComponentSummary component)
        {
            foreach (AutomationBreadboardPortSummary input in inspector.InputPorts(component, 12))
            {
                if (input == null || !input.IsConnected)
                    continue;

                yield return
                    input.ConnectedFromComponentId.ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    input.ConnectedFromOutputIndex.ToString(CultureInfo.InvariantCulture) +
                    ">" +
                    component.UniqueId.ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    input.Index.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static int CountWireFingerprints(
            AutomationBreadboardInspector inspector,
            IEnumerable<AutomationBreadboardComponentSummary> components) =>
            components.Sum(component => WireFingerprints(inspector, component).Count());

        private static string LinkedTargetFingerprint(AutomationTarget target)
        {
            return
                CleanSegment(target.Label) +
                ":" +
                CleanSegment(target.Category.ToString()) +
                ":" +
                CleanSegment(target.RuntimeType) +
                ":" +
                target.LocalPosition.x.ToString(CultureInfo.InvariantCulture) +
                "," +
                target.LocalPosition.y.ToString(CultureInfo.InvariantCulture) +
                "," +
                target.LocalPosition.z.ToString(CultureInfo.InvariantCulture);
        }

        private static string StableHash64(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (char character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }

        private static string CleanSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("|", "/")
                .Replace(":", ";")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static string RoundForFingerprint(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string BoolBit(bool value) =>
            value ? "1" : "0";

        private static void ProbeAcb(
            AutomationTarget controller,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            if (!AutomationAcbInspector.TryCreate(
                    controller.Block,
                    out AutomationAcbInspector inspector,
                    out string reason))
            {
                builder.Fail(reason ?? "ACB ControlBlockData package was not found.");
                return;
            }

            builder.Pass("ACB ControlBlockData discovered: " + inspector.DataTypeName + ".");
            if (inspector.HasRuleData)
                builder.Pass("ACB action and/or condition rule packages were discovered.");
            else
                builder.Warn("ACB action/condition rule packages were not found on this instance.");

            if (inspector.TryRewriteCurrentSettings(out string writeMessage))
                builder.Pass(writeMessage);
            else
                builder.Fail(writeMessage);
        }

        private static void ProbeAcbController(
            AutomationTarget controller,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            if (!AutomationAcbControllerInspector.TryCreate(
                    controller.Block,
                    out AutomationAcbControllerInspector inspector,
                    out string reason))
            {
                builder.Fail(reason ?? "ACB Controller button data was not found.");
                return;
            }

            builder.Pass("ACB Controller data discovered: " + inspector.ControllerTypeName + ".");
            builder.Pass(
                "ACB Controller exposes " +
                inspector.Buttons.Count.ToString(CultureInfo.InvariantCulture) +
                " button data item(s).");

            AutomationAcbControllerButtonSummary first = inspector.Buttons.FirstOrDefault();
            if (first == null)
            {
                builder.Fail("ACB Controller has no button to probe.");
                return;
            }

            if (inspector.TryRewriteCurrentButtonValues(first, out string writeMessage))
                builder.Pass(writeMessage);
            else
                builder.Fail(writeMessage);

            if (inspector.Buttons.Any(button => button != null && button.IsUsedForBreadboard))
                builder.Pass("At least one ACB Controller button is marked for Breadboard output.");
            else
                builder.Warn("No ACB Controller button is currently marked for Breadboard output.");
        }

        private static void ProbeLinkedTargets(
            IReadOnlyList<AutomationTarget> linkedTargets,
            AutomationRuntimeDiagnosticBuilder builder)
        {
            int count = linkedTargets?.Count ?? 0;
            if (count == 0)
                return;

            builder.Pass(
                "Automation link list contains " +
                count.ToString(CultureInfo.InvariantCulture) +
                " target(s).");
            int stale = linkedTargets.Count(target => target?.Block == null);
            if (stale > 0)
            {
                builder.Warn(
                    stale.ToString(CultureInfo.InvariantCulture) +
                    " linked target(s) no longer have a live Block instance.");
            }

            foreach (AutomationTarget target in linkedTargets.Where(target => target?.Block != null).Take(12))
            {
                if (target.Controller?.Kind == AutomationControllerKind.Acb)
                {
                    if (AutomationAcbInspector.TryCreate(target.Block, out _, out string reason))
                        builder.Pass("Linked ACB target exposes ControlBlockData: " + target.Label + ".");
                    else
                        builder.Warn("Linked ACB target cannot expose ControlBlockData: " + (reason ?? target.Label) + ".");
                }
                else if (target.Controller?.Kind == AutomationControllerKind.AcbController)
                {
                    if (AutomationAcbControllerInspector.TryCreate(target.Block, out AutomationAcbControllerInspector inspector, out string reason))
                    {
                        bool hasOutput = inspector.Buttons.Any(button => button != null && button.IsUsedForBreadboard);
                        if (hasOutput)
                            builder.Pass("Linked ACB Controller has a Breadboard-output button: " + target.Label + ".");
                        else
                            builder.Warn("Linked ACB Controller has no Breadboard-output button yet: " + target.Label + ".");
                    }
                    else
                    {
                        builder.Warn("Linked ACB Controller cannot expose button data: " + (reason ?? target.Label) + ".");
                    }
                }
            }
        }

        private sealed class BreadboardComponentFamily
        {
            internal BreadboardComponentFamily(string label, params string[] searchTerms)
            {
                Label = label ?? string.Empty;
                SearchTerms = searchTerms ?? Array.Empty<string>();
            }

            internal string Label { get; }

            internal string[] SearchTerms { get; }
        }
    }

    internal sealed class AutomationRuntimeDiagnosticResult
    {
        internal static readonly AutomationRuntimeDiagnosticResult Empty =
            new AutomationRuntimeDiagnosticResult(
                0,
                0,
                0,
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                switchProbeRan: false,
                switchFailExpressionReady: false,
                switchMaxVisibleInputs: 0);

        internal AutomationRuntimeDiagnosticResult(
            int passed,
            int warnings,
            int failures,
            IReadOnlyList<string> lines,
            string nativePersistenceFingerprint,
            string genericProxyFingerprint,
            bool switchProbeRan,
            bool switchFailExpressionReady,
            int switchMaxVisibleInputs)
        {
            Passed = Math.Max(0, passed);
            Warnings = Math.Max(0, warnings);
            Failures = Math.Max(0, failures);
            Lines = lines ?? Array.Empty<string>();
            NativePersistenceFingerprint = nativePersistenceFingerprint ?? string.Empty;
            GenericProxyFingerprint = genericProxyFingerprint ?? string.Empty;
            SwitchProbeRan = switchProbeRan;
            SwitchFailExpressionReady = switchFailExpressionReady;
            SwitchMaxVisibleInputs = Math.Max(0, switchMaxVisibleInputs);
        }

        internal int Passed { get; }

        internal int Warnings { get; }

        internal int Failures { get; }

        internal IReadOnlyList<string> Lines { get; }

        internal string NativePersistenceFingerprint { get; }

        internal string GenericProxyFingerprint { get; }

        internal bool HasNativePersistenceFingerprint =>
            !string.IsNullOrWhiteSpace(NativePersistenceFingerprint);

        internal bool HasGenericProxyFingerprint =>
            !string.IsNullOrWhiteSpace(GenericProxyFingerprint);

        internal bool HasCompleteValidationEvidence =>
            HasNativePersistenceFingerprint &&
            HasGenericProxyFingerprint &&
            SwitchProbeRan &&
            SwitchFailExpressionReady;

        internal bool SwitchProbeRan { get; }

        internal bool SwitchFailExpressionReady { get; }

        internal int SwitchMaxVisibleInputs { get; }

        internal bool IsEmpty => Passed == 0 && Warnings == 0 && Failures == 0 && Lines.Count == 0;

        internal bool HasFailures => Failures > 0;

        internal bool HasWarnings => Warnings > 0;

        internal string Summary
        {
            get
            {
                if (IsEmpty)
                    return "Runtime checks have not been run.";

                return
                    "Runtime check: " +
                    Passed.ToString(CultureInfo.InvariantCulture) +
                    " pass, " +
                    Warnings.ToString(CultureInfo.InvariantCulture) +
                    " warn, " +
                    Failures.ToString(CultureInfo.InvariantCulture) +
                    " fail.";
            }
        }
    }

    internal sealed class AutomationRuntimeDiagnosticBuilder
    {
        private readonly List<string> _lines = new List<string>();
        private int _passed;
        private int _warnings;
        private int _failures;
        private string _nativePersistenceFingerprint = string.Empty;
        private string _genericProxyFingerprint = string.Empty;
        private bool _switchProbeRan;
        private bool _switchFailExpressionReady;
        private int _switchMaxVisibleInputs;

        internal void Pass(string message)
        {
            _passed++;
            _lines.Add("OK: " + Clean(message));
        }

        internal void Warn(string message)
        {
            _warnings++;
            _lines.Add("WARN: " + Clean(message));
        }

        internal void Fail(string message)
        {
            _failures++;
            _lines.Add("FAIL: " + Clean(message));
        }

        internal void SetNativePersistenceFingerprint(string fingerprint) =>
            _nativePersistenceFingerprint = fingerprint ?? string.Empty;

        internal void SetGenericProxyFingerprint(string fingerprint) =>
            _genericProxyFingerprint = fingerprint ?? string.Empty;

        internal void SetSwitchFailExpressionReadiness(
            bool probeRan,
            bool ready,
            int maxVisibleInputs)
        {
            _switchProbeRan = probeRan;
            _switchFailExpressionReady = ready;
            _switchMaxVisibleInputs = Math.Max(0, maxVisibleInputs);
        }

        internal AutomationRuntimeDiagnosticResult ToResult() =>
            new AutomationRuntimeDiagnosticResult(
                _passed,
                _warnings,
                _failures,
                _lines.ToArray(),
                _nativePersistenceFingerprint,
                _genericProxyFingerprint,
                _switchProbeRan,
                _switchFailExpressionReady,
                _switchMaxVisibleInputs);

        private static string Clean(string message) =>
            string.IsNullOrWhiteSpace(message) ? "No detail." : message.Trim();
    }
}
