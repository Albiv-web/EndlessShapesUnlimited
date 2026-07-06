using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationBreadboardInspector
    {
        private const int MaxSearchObjects = 240;
        private const int MaxSearchDepth = 6;
        private const int MaxExpandedEnumerableItems = 96;
        private const uint UnselectedCode = 999999U;

        private readonly object _board;

        private AutomationBreadboardInspector(object board)
        {
            _board = board;
        }

        internal string BoardTypeName => _board?.GetType().FullName ?? string.Empty;

        internal string ControllerName
        {
            get => ReadString("ControllerName", string.Empty);
            set => WriteValue("ControllerName", value ?? string.Empty);
        }

        internal bool GridEnabled
        {
            get => ReadBool("GridEnabled", true);
            set => WriteValue("GridEnabled", value);
        }

        internal float GridSize
        {
            get => ReadFloat("GridSize", 50f);
            set => WriteValue("GridSize", value);
        }

        internal bool Blur
        {
            get => ReadBool("Blur", false);
            set => WriteValue("Blur", value);
        }

        internal bool HelpLabels
        {
            get => ReadBool("HelpLabels", true);
            set => WriteValue("HelpLabels", value);
        }

        internal int PackageCount => ReadPlainInt(_board, "PackageCount", Components.Count);

        internal IReadOnlyList<AutomationBreadboardComponentSummary> Components =>
            ReadComponents(64);

        internal IReadOnlyList<AutomationBreadboardAvailableComponent> AvailableComponents =>
            ReadAvailableComponents();

        internal bool TryRewriteCurrentSettings(out string message)
        {
            var failures = new List<string>();
            if (!WriteValue("ControllerName", ControllerName ?? string.Empty))
                failures.Add("ControllerName");
            if (!WriteValue("GridEnabled", GridEnabled))
                failures.Add("GridEnabled");
            if (!WriteValue("GridSize", GridSize))
                failures.Add("GridSize");
            if (!WriteValue("HelpLabels", HelpLabels))
                failures.Add("HelpLabels");
            if (!WriteValue("Blur", Blur))
                failures.Add("Blur");

            if (failures.Count == 0)
            {
                message = "Breadboard Var.Us setting writes accepted same-value probes.";
                return true;
            }

            message = "Breadboard setting writes failed for " + string.Join(", ", failures.ToArray()) + ".";
            return false;
        }

        internal bool CanAddComponent(string typeName) =>
            FindAvailableComponent(typeName) != null;

        internal bool CanAddComponentMatching(params string[] searchTerms) =>
            FindAvailableComponentMatching(searchTerms) != null;

        internal bool TryAddComponent(string typeName, out string message)
        {
            if (!TryCreateNativeComponent(typeName, out object component, out message))
                return false;

            message = "Added " + ComponentDisplayName(component) + " to the native board.";
            return true;
        }

        internal bool TryAddFirstAvailableComponent(
            string displayLabel,
            string[] searchTerms,
            out string message)
        {
            AutomationBreadboardAvailableComponent available = FindAvailableComponentMatching(searchTerms);
            if (available == null)
            {
                message = "This board does not advertise " + (displayLabel ?? "that component") + ".";
                return false;
            }

            string typeName = string.IsNullOrWhiteSpace(available.FullTypeName)
                ? available.TypeName
                : available.FullTypeName;
            if (!TryCreateNativeComponent(typeName, out object component, out message))
                return false;

            message = "Added " + ComponentDisplayName(component) + " to the native board.";
            return true;
        }

        internal bool TryAddFirstAvailableComponents(
            string displayLabel,
            IReadOnlyList<string[]> searchTermSets,
            out string message)
        {
            message = null;
            if (searchTermSets == null || searchTermSets.Count == 0)
            {
                message = "No component searches were supplied.";
                return false;
            }

            var available = new List<AutomationBreadboardAvailableComponent>();
            foreach (string[] searchTerms in searchTermSets)
            {
                AutomationBreadboardAvailableComponent component = FindAvailableComponentMatching(searchTerms);
                if (component == null)
                {
                    message = "This board does not advertise every component needed for " + (displayLabel ?? "that bridge") + ".";
                    return false;
                }

                available.Add(component);
            }

            var createdComponents = new List<object>();
            foreach (AutomationBreadboardAvailableComponent component in available)
            {
                string typeName = string.IsNullOrWhiteSpace(component.FullTypeName)
                    ? component.TypeName
                    : component.FullTypeName;
                if (TryCreateNativeComponentTracked(typeName, createdComponents, out _, out message))
                    continue;

                DeleteCreatedComponents(createdComponents);
                message = message ?? "Could not create every component needed for " + (displayLabel ?? "that bridge") + ".";
                return false;
            }

            message =
                "Added " +
                createdComponents.Count.ToString(CultureInfo.InvariantCulture) +
                " native component(s) for " +
                (displayLabel ?? "the bridge") +
                ".";
            return true;
        }

        internal bool TryCreateTargetProxy(
            AutomationTarget target,
            bool getter,
            bool setter,
            out string message)
        {
            AutomationBreadboardCompileResult ignored;
            return TryCreateTargetProxy(
                target,
                getter,
                setter,
                out ignored,
                out message);
        }

        internal bool TryCreateTargetProxy(
            AutomationTarget target,
            bool getter,
            bool setter,
            out AutomationBreadboardCompileResult result,
            out string message)
        {
            result = null;
            message = null;
            if (target?.Block == null)
            {
                message = "No linked target block was selected.";
                return false;
            }

            if (!getter && !setter)
            {
                message = "Choose a Getter and/or Setter proxy.";
                return false;
            }

            int created = 0;
            int autoSelected = 0;
            string lastFailure = null;
            var createdComponents = new List<object>();
            if (getter)
            {
                if (TryCreateNativeComponent("GenericBlockGetter", out object component, out string getterMessage))
                {
                    createdComponents.Add(component);
                    ConfigureGenericProxy(component, target, "Getter");
                    if (TryAutoSelectTargetProxyProperty(component, target, getter: true))
                        autoSelected++;
                    created++;
                }
                else
                {
                    lastFailure = getterMessage;
                }
            }

            if (setter)
            {
                if (TryCreateNativeComponent("GenericBlockSetter", out object component, out string setterMessage))
                {
                    createdComponents.Add(component);
                    ConfigureGenericProxy(component, target, "Setter");
                    if (TryAutoSelectTargetProxyProperty(component, target, getter: false))
                        autoSelected++;
                    created++;
                }
                else
                {
                    lastFailure = setterMessage;
                }
            }

            if (created == 0)
            {
                message = lastFailure ?? "No native proxy components were created.";
                return false;
            }

            result = CompileResultFromComponents(createdComponents, "target proxy");
            message =
                "Created " +
                created.ToString(CultureInfo.InvariantCulture) +
                " native proxy component(s) for " +
                target.Label +
                (autoSelected > 0
                    ? ". Auto-selected " +
                      autoSelected.ToString(CultureInfo.InvariantCulture) +
                      " likely target propert" +
                      (autoSelected == 1 ? "y" : "ies") +
                      "; use the property picker to adjust."
                    : target.Controller?.Kind == AutomationControllerKind.AcbController
                    ? ". ACB Controller button getters try to select a matching keyword automatically."
                    : ". No likely property was auto-selected; select each proxy's exact property in the graph next.");
            return true;
        }

        internal bool TryCreateEvaluatorExpression(string expression, out string message)
        {
            AutomationBreadboardCompileResult ignored;
            return TryCreateEvaluatorExpression(expression, out ignored, out message);
        }

        internal bool TryCreateEvaluatorExpression(
            string expression,
            out AutomationBreadboardCompileResult result,
            out string message)
        {
            result = null;
            message = null;
            string normalized = (expression ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                message = "Enter a Maths Evaluator expression first.";
                return false;
            }

            if (normalized.Length > 1000)
            {
                message = "Maths Evaluator expressions are limited to 1000 characters.";
                return false;
            }

            var createdComponents = new List<object>();
            if (!TryCreateNativeComponentTracked("Evaluator", createdComponents, out object component, out message))
                return false;

            if (!WriteValue(component, "Expression", normalized))
            {
                DeleteCreatedComponents(createdComponents);
                message = "Created an Evaluator, but FtD rejected the expression write.";
                return false;
            }

            result = CompileResultFromComponents(createdComponents, "expression");
            message = "Compiled expression into a native Maths Evaluator node.";
            return true;
        }

        internal bool TryCreateIfElseSwitch(
            string conditionExpression,
            string secondaryConditionExpression,
            string logicGate,
            string passExpression,
            float failValue,
            out string message)
        {
            AutomationBreadboardCompileResult ignored;
            return TryCreateIfElseSwitch(
                conditionExpression,
                secondaryConditionExpression,
                logicGate,
                passExpression,
                failValue,
                null,
                out ignored,
                out message);
        }

        internal bool TryCreateIfElseSwitch(
            string conditionExpression,
            string secondaryConditionExpression,
            string logicGate,
            string passExpression,
            float failValue,
            string failExpression,
            out AutomationBreadboardCompileResult result,
            out string message)
        {
            result = null;
            message = null;
            string condition = (conditionExpression ?? string.Empty).Trim();
            string pass = (passExpression ?? string.Empty).Trim();
            string fail = (failExpression ?? string.Empty).Trim();
            string secondaryCondition = (secondaryConditionExpression ?? string.Empty).Trim();
            string gate = (logicGate ?? string.Empty).Trim();
            bool useLogicGate = !string.IsNullOrWhiteSpace(secondaryCondition);
            bool useFailExpression = !string.IsNullOrWhiteSpace(fail);
            if (string.IsNullOrWhiteSpace(condition) || string.IsNullOrWhiteSpace(pass))
            {
                message = "If/else lowering requires a condition and a pass expression.";
                return false;
            }

            if (condition.Length > 1000 ||
                secondaryCondition.Length > 1000 ||
                pass.Length > 1000 ||
                fail.Length > 1000)
            {
                message = "Maths Evaluator expressions are limited to 1000 characters.";
                return false;
            }

            var createdComponents = new List<object>();
            if (!TryCreateEvaluatorNode(condition, createdComponents, out object conditionEvaluator, "condition", out message))
            {
                DeleteCreatedComponents(createdComponents);
                return false;
            }

            object switchSignalComponent = conditionEvaluator;
            if (useLogicGate)
            {
                if (!TryCreateEvaluatorNode(secondaryCondition, createdComponents, out object secondaryEvaluator, "secondary condition", out message))
                {
                    DeleteCreatedComponents(createdComponents);
                    return false;
                }
                if (!TryCreateNativeComponentTracked("LogicGate", createdComponents, out object logicGateComponent, out message))
                {
                    DeleteCreatedComponents(createdComponents);
                    return false;
                }

                int gateValue = string.Equals(gate, "or", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                WriteValue(logicGateComponent, "SelectedGate", gateValue);
                WriteValue(logicGateComponent, "TrueLogic", 1);
                if (!TryConnectNativePorts(conditionEvaluator, 0, logicGateComponent, 0, out message) ||
                    !TryConnectNativePorts(secondaryEvaluator, 0, logicGateComponent, 1, out message))
                {
                    DeleteCreatedComponents(createdComponents);
                    message = message ?? "Created Logic Gate condition nodes, but native wiring failed.";
                    return false;
                }

                switchSignalComponent = logicGateComponent;
            }

            if (!TryCreateEvaluatorNode(pass, createdComponents, out object passEvaluator, "pass", out message))
            {
                DeleteCreatedComponents(createdComponents);
                return false;
            }
            object failEvaluator = null;
            if (useFailExpression &&
                !TryCreateEvaluatorNode(fail, createdComponents, out failEvaluator, "fail", out message))
            {
                DeleteCreatedComponents(createdComponents);
                return false;
            }
            if (!TryCreateNativeComponentTracked("Switch", createdComponents, out object switchComponent, out message))
            {
                DeleteCreatedComponents(createdComponents);
                return false;
            }
            WriteValue(switchComponent, "Threshold", 0.5f);
            WriteValue(switchComponent, "FailValue", failValue);

            if (!TryConnectNativePorts(passEvaluator, 0, switchComponent, 0, out message) ||
                !TryConnectNativePorts(switchSignalComponent, 0, switchComponent, 1, out message))
            {
                DeleteCreatedComponents(createdComponents);
                message = message ?? "Created if/else nodes, but native wiring failed.";
                return false;
            }

            if (useFailExpression &&
                !TryConnectNativePorts(failEvaluator, 0, switchComponent, 2, out message))
            {
                DeleteCreatedComponents(createdComponents);
                message = message ?? "Created if/else nodes, but the native Switch did not accept a fail-expression input.";
                return false;
            }

            result = CompileResultFromComponents(createdComponents, useLogicGate || useFailExpression ? "if/else logic" : "if/else");
            message =
                "Compiled if/else into native " +
                (useLogicGate || useFailExpression ? "Evaluator + Logic Gate/Switch" : "Evaluator + Switch") +
                " nodes. Else " +
                (useFailExpression
                    ? "expression: " + fail
                    : "value: " + failValue.ToString("0.###", CultureInfo.InvariantCulture)) +
                ".";
            return true;
        }

        private bool TryCreateEvaluatorNode(
            string expression,
            List<object> createdComponents,
            out object component,
            string role,
            out string message)
        {
            component = null;
            message = null;
            if (!TryCreateNativeComponentTracked("Evaluator", createdComponents, out component, out message))
                return false;
            if (WriteValue(component, "Expression", expression))
                return true;

            message = "Created a " + role + " Evaluator, but FtD rejected the expression write.";
            return false;
        }

        internal IReadOnlyList<AutomationBreadboardProxyOption> ProxyPropertyOptions(
            AutomationBreadboardComponentSummary component,
            string filter,
            int limit)
        {
            var results = new List<AutomationBreadboardProxyOption>
            {
                AutomationBreadboardProxyOption.Clear(component?.IsGenericGetter == true)
            };

            if (component?.NativeComponent == null || !component.IsGenericProxy)
                return results;

            InvokeInstance(component.NativeComponent, "ExtractCorrectInputs");
            object picker = InvokeInstance(component.NativeComponent, "PropertyPicker");
            object items = InvokeInstance(picker, "EnumerateItems");
            string normalizedFilter = filter ?? string.Empty;
            int max = Math.Max(1, limit);
            foreach (object item in EnumerateCollection(items, 512))
            {
                object nativeOption = ReadMember(item, "ObjectForAction");
                if (nativeOption == null)
                    continue;

                string label = ReadPlainString(item, "Name", "Property");
                string tooltip = ReadPlainString(item, "ToolTip", string.Empty);
                if (!MatchesFilter(label, tooltip, normalizedFilter))
                    continue;

                AutomationBreadboardProxyOption option = CreateProxyOption(
                    component.IsGenericGetter,
                    nativeOption,
                    label,
                    tooltip);
                if (option == null)
                    continue;

                results.Add(option);
                if (results.Count >= max + 1)
                    break;
            }

            return results;
        }

        internal bool TrySelectProxyProperty(
            AutomationBreadboardComponentSummary component,
            AutomationBreadboardProxyOption option,
            out string message)
        {
            message = null;
            if (component?.NativeComponent == null || !component.IsGenericProxy)
            {
                message = "Select a Generic Getter or Generic Setter component.";
                return false;
            }

            if (option == null)
            {
                message = "No proxy property was selected.";
                return false;
            }

            if (component.IsGenericGetter)
                ApplyGetterOption(component.NativeComponent, option);
            else
                ApplySetterOption(component.NativeComponent, option);

            message = option.IsClear
                ? "Cleared " + component.Label + " property selection."
                : "Selected " + option.Label + " for " + component.Label + ".";
            return true;
        }

        internal AutomationBreadboardComponentSettings SettingsFor(
            AutomationBreadboardComponentSummary component)
        {
            object native = component?.NativeComponent;
            if (native == null)
                return null;

            if (component.IsEvaluator)
            {
                return AutomationBreadboardComponentSettings.ForEvaluator(
                    ReadPlainString(ReadUs(ReadMember(native, "Expression")), null, string.Empty));
            }

            if (component.IsSwitch)
            {
                return AutomationBreadboardComponentSettings.ForSwitch(
                    ReadFloat(native, "Threshold", 0.5f),
                    ReadFloat(native, "FailValue", 0f));
            }

            if (component.IsLogicGate)
            {
                return AutomationBreadboardComponentSettings.ForLogicGate(
                    ReadVarInt(native, "SelectedGate", 1),
                    ReadVarInt(native, "TrueLogic", 1));
            }

            if (component.IsConstantInput)
            {
                return AutomationBreadboardComponentSettings.ForConstantInput(
                    ReadVarInt(native, "Type", 0),
                    ReadFloat(native, "InputValue", 1f),
                    ReadPlainString(ReadUs(ReadMember(native, "InputValueString")), null, string.Empty),
                    ReadVarLong(native, "InputValueLong", 0L));
            }

            return null;
        }

        internal bool TryUpdateComponentSetting(
            AutomationBreadboardComponentSummary component,
            string settingKey,
            string value,
            out string message)
        {
            message = null;
            object native = component?.NativeComponent;
            string key = settingKey ?? string.Empty;
            string text = value ?? string.Empty;
            if (native == null)
            {
                message = "Select a native component setting first.";
                return false;
            }

            if (string.Equals(key, "Expression", StringComparison.OrdinalIgnoreCase))
            {
                string expression = text.Trim();
                if (expression.Length > 1000)
                {
                    message = "Maths Evaluator expressions are limited to 1000 characters.";
                    return false;
                }

                if (!WriteValue(native, "Expression", expression))
                {
                    message = "FtD rejected the Evaluator expression write.";
                    return false;
                }

                message = "Evaluator expression updated.";
                return true;
            }

            if (string.Equals(key, "Threshold", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "FailValue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "InputValue", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    message = "Enter a valid numeric value.";
                    return false;
                }

                if (!WriteValue(native, key, parsed))
                {
                    message = "FtD rejected the numeric setting write.";
                    return false;
                }

                message = key + " updated.";
                return true;
            }

            if (string.Equals(key, "InputValueLong", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    message = "Enter a valid integer value.";
                    return false;
                }

                bool wrote = WriteValue(native, "InputValueLong", parsed);
                WriteValue(native, "InputValueLongAsString", parsed.ToString(CultureInfo.InvariantCulture));
                if (!wrote)
                {
                    message = "FtD rejected the integer setting write.";
                    return false;
                }

                message = "Constant integer updated.";
                return true;
            }

            if (string.Equals(key, "InputValueString", StringComparison.OrdinalIgnoreCase))
            {
                if (!WriteValue(native, "InputValueString", text))
                {
                    message = "FtD rejected the text setting write.";
                    return false;
                }

                message = "Constant text updated.";
                return true;
            }

            message = "Unsupported native component setting.";
            return false;
        }

        internal bool TryCycleComponentSetting(
            AutomationBreadboardComponentSummary component,
            string settingKey,
            int delta,
            out string message)
        {
            message = null;
            object native = component?.NativeComponent;
            if (native == null)
            {
                message = "Select a native component setting first.";
                return false;
            }

            string key = settingKey ?? string.Empty;
            int count;
            string propertyName;
            if (string.Equals(key, "SelectedGate", StringComparison.OrdinalIgnoreCase))
            {
                propertyName = "SelectedGate";
                count = AutomationBreadboardComponentSettings.LogicGateLabels.Length;
            }
            else if (string.Equals(key, "TrueLogic", StringComparison.OrdinalIgnoreCase))
            {
                propertyName = "TrueLogic";
                count = AutomationBreadboardComponentSettings.TrueLogicLabels.Length;
            }
            else if (string.Equals(key, "ConstantType", StringComparison.OrdinalIgnoreCase))
            {
                propertyName = "Type";
                count = AutomationBreadboardComponentSettings.ConstantTypeLabels.Length;
            }
            else
            {
                message = "Unsupported native component setting.";
                return false;
            }

            int current = ReadVarInt(native, propertyName, 0);
            int next = PositiveModulo(current + delta, count);
            if (!WriteValue(native, propertyName, next))
            {
                message = "FtD rejected the enum setting write.";
                return false;
            }

            message = propertyName + " set to " + next.ToString(CultureInfo.InvariantCulture) + ".";
            return true;
        }

        internal IReadOnlyList<AutomationBreadboardPortSummary> OutputPorts(
            AutomationBreadboardComponentSummary component,
            int limit) =>
            ReadPorts(component, outputs: true, limit: limit);

        internal IReadOnlyList<AutomationBreadboardPortSummary> InputPorts(
            AutomationBreadboardComponentSummary component,
            int limit) =>
            ReadPorts(component, outputs: false, limit: limit);

        internal bool TryConnectPorts(
            AutomationBreadboardComponentSummary source,
            int outputIndex,
            AutomationBreadboardComponentSummary target,
            int inputIndex,
            out string message)
        {
            message = null;
            if (!TryConnectNativePorts(source?.NativeComponent, outputIndex, target?.NativeComponent, inputIndex, out message))
                return false;

            message =
                "Connected " +
                source.Label +
                " output " +
                outputIndex.ToString(CultureInfo.InvariantCulture) +
                " to " +
                target.Label +
                " input " +
                inputIndex.ToString(CultureInfo.InvariantCulture) +
                ".";
            return true;
        }

        private bool TryConnectNativePorts(
            object sourceComponent,
            int outputIndex,
            object targetComponent,
            int inputIndex,
            out string message)
        {
            message = null;
            object output = GetPort(sourceComponent, true, outputIndex);
            object input = GetPort(targetComponent, false, inputIndex);
            if (output == null || input == null)
            {
                message = "The selected native ports were not found.";
                return false;
            }

            if (IsInputConnected(input))
                ExecuteBoardCommand("DeleteConnectionCommand", input);

            if (!ExecuteBoardCommand("CreateConnectionCommand", output, input))
            {
                message = "FtD rejected the native wire connection.";
                return false;
            }

            return true;
        }

        internal bool TryClearInput(
            AutomationBreadboardComponentSummary target,
            int inputIndex,
            out string message)
        {
            message = null;
            object input = GetPort(target?.NativeComponent, false, inputIndex);
            if (input == null)
            {
                message = "The selected native input was not found.";
                return false;
            }

            if (!IsInputConnected(input))
            {
                message = "The selected input is already clear.";
                return false;
            }

            if (!ExecuteBoardCommand("DeleteConnectionCommand", input))
            {
                message = "FtD rejected the native wire delete.";
                return false;
            }

            message =
                "Cleared " +
                target.Label +
                " input " +
                inputIndex.ToString(CultureInfo.InvariantCulture) +
                ".";
            return true;
        }

        internal bool TryMoveComponent(
            AutomationBreadboardComponentSummary component,
            float deltaX,
            float deltaY,
            out string message)
        {
            message = null;
            object native = component?.NativeComponent;
            if (native == null)
            {
                message = "The selected native component was not found.";
                return false;
            }

            float oldX = ReadFloat(native, "X", component.X);
            float oldY = ReadFloat(native, "Y", component.Y);
            float newX = oldX + deltaX;
            float newY = oldY + deltaY;
            if (!ExecuteBoardCommand("MoveComponentCommand", native, oldX, oldY, newX, newY))
            {
                message = "FtD rejected the native component move.";
                return false;
            }

            message =
                "Moved " +
                component.Label +
                " to " +
                newX.ToString("0.#", CultureInfo.InvariantCulture) +
                ", " +
                newY.ToString("0.#", CultureInfo.InvariantCulture) +
                ".";
            return true;
        }

        internal bool TryDeleteComponent(
            AutomationBreadboardComponentSummary component,
            out string message)
        {
            message = null;
            object native = component?.NativeComponent;
            if (native == null)
            {
                message = "The selected native component was not found.";
                return false;
            }

            if (!ExecuteBoardCommand("DeleteComponentCommand", native))
            {
                message = "FtD rejected the native component delete.";
                return false;
            }

            message = "Deleted " + component.Label + " from the native breadboard graph.";
            return true;
        }

        internal static bool TryCreate(
            Block block,
            AutomationControllerDescriptor descriptor,
            out AutomationBreadboardInspector inspector,
            out string reason)
        {
            inspector = null;
            reason = null;
            if (block == null)
            {
                reason = "No selected breadboard block.";
                return false;
            }

            if (!IsSupportedController(descriptor))
            {
                reason = "The selected controller does not own a breadboard graph.";
                return false;
            }

            object board = FindBoard(block);
            if (board == null)
            {
                reason = "Native IBoard/Board package was not found from this controller instance.";
                return false;
            }

            inspector = new AutomationBreadboardInspector(board);
            return true;
        }

        private static bool IsSupportedController(AutomationControllerDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            return descriptor.Kind == AutomationControllerKind.Breadboard ||
                   descriptor.Kind == AutomationControllerKind.AiBreadboard ||
                   descriptor.Kind == AutomationControllerKind.MissileBreadboard;
        }

        private bool TryCreateNativeComponent(
            string typeName,
            out object component,
            out string message)
        {
            component = null;
            message = null;
            if (_board == null)
            {
                message = "No native board was found.";
                return false;
            }

            object available = FindAvailableComponent(typeName);
            if (available == null)
            {
                message = "This board does not advertise " + typeName + ".";
                return false;
            }

            Guid guid = ReadGuid(available, "GuidOfComponent");
            if (guid == Guid.Empty)
            {
                message = "The component type does not expose a stable GUID.";
                return false;
            }

            component = InvokeInstance(_board, "MakeSystem", guid);
            if (component == null)
            {
                message = "FtD did not create the requested component.";
                return false;
            }

            PlaceNewComponent(component);
            if (!ExecuteAddComponentCommand(component) &&
                !InvokeNewPackage(component))
            {
                component = null;
                message = "FtD rejected the component add command.";
                return false;
            }

            return true;
        }

        private bool TryCreateNativeComponentTracked(
            string typeName,
            ICollection<object> createdComponents,
            out object component,
            out string message)
        {
            if (!TryCreateNativeComponent(typeName, out component, out message))
                return false;

            createdComponents?.Add(component);
            return true;
        }

        private void DeleteCreatedComponents(IEnumerable<object> createdComponents)
        {
            if (createdComponents == null)
                return;

            foreach (object component in createdComponents.Where(item => item != null).Reverse())
                ExecuteBoardCommand("DeleteComponentCommand", component);
        }

        private static AutomationBreadboardCompileResult CompileResultFromComponents(
            IEnumerable<object> components,
            string label)
        {
            uint[] ids = (components ?? Enumerable.Empty<object>())
                .Select(component => ReadPlainUInt(component, "UniqueId", 0U))
                .Where(id => id != 0U)
                .Distinct()
                .ToArray();
            return new AutomationBreadboardCompileResult(label, ids);
        }

        private static AutomationBreadboardProxyOption CreateProxyOption(
            bool getter,
            object nativeOption,
            string label,
            string tooltip)
        {
            if (nativeOption == null)
                return null;

            if (getter)
            {
                object readableAttribute = ReadMember(nativeOption, "Attribute");
                if (readableAttribute != null)
                {
                    return AutomationBreadboardProxyOption.GetterReadable(
                        nativeOption,
                        label,
                        tooltip,
                        ReadPlainUInt(readableAttribute, "Index", UnselectedCode));
                }

                object variableAttribute = ReadMember(nativeOption, "VariableAttribute");
                object set = ReadMember(nativeOption, "Set");
                if (variableAttribute != null && set != null)
                {
                    return AutomationBreadboardProxyOption.GetterVariable(
                        nativeOption,
                        label,
                        tooltip,
                        ReadPlainUInt(variableAttribute, "SaveIndex", UnselectedCode),
                        ReadPlainUInt(set, "Index", UnselectedCode));
                }

                return null;
            }

            object prop = ReadMember(nativeOption, "Prop");
            object attrib = ReadMember(nativeOption, "Attrib");
            if (prop == null || attrib == null)
                return null;

            return AutomationBreadboardProxyOption.Setter(
                nativeOption,
                label,
                tooltip,
                ReadPlainUInt(attrib, "SaveIndex", UnselectedCode),
                ReadPlainUInt(prop, "Index", UnselectedCode));
        }

        private static void ApplyGetterOption(
            object component,
            AutomationBreadboardProxyOption option)
        {
            if (option.IsClear)
            {
                WriteValue(component, "ReadableAttributeId", UnselectedCode);
                WriteValue(component, "BlockPropertyId", UnselectedCode);
                WriteValue(component, "BlockSetId", UnselectedCode);
                WriteDirectMember(component, "_computer", null);
                return;
            }

            if (option.IsGetterReadable)
            {
                WriteValue(component, "ReadableAttributeId", option.ReadableAttributeId);
                WriteValue(component, "BlockPropertyId", UnselectedCode);
                WriteValue(component, "BlockSetId", UnselectedCode);
                WriteDirectMember(component, "_computer", option.NativeOption);
                return;
            }

            WriteValue(component, "ReadableAttributeId", UnselectedCode);
            WriteValue(component, "BlockPropertyId", option.BlockPropertyId);
            WriteValue(component, "BlockSetId", option.BlockSetId);
            WriteDirectMember(component, "_computer", option.NativeOption);
        }

        private static void ApplySetterOption(
            object component,
            AutomationBreadboardProxyOption option)
        {
            if (option.IsClear)
            {
                WriteValue(component, "BlockPropertyId", UnselectedCode);
                WriteValue(component, "BlockSetId", UnselectedCode);
                WriteDirectMember(component, "_moduleAttributePair", null);
                return;
            }

            WriteValue(component, "BlockPropertyId", option.BlockPropertyId);
            WriteValue(component, "BlockSetId", option.BlockSetId);
            WriteDirectMember(component, "_moduleAttributePair", option.NativeOption);
        }

        private static bool TryAutoSelectAcbControllerGetter(
            object component,
            AutomationTarget target)
        {
            if (component == null ||
                target?.Controller?.Kind != AutomationControllerKind.AcbController ||
                !AutomationAcbControllerInspector.TryCreate(
                    target.Block,
                    out AutomationAcbControllerInspector inspector,
                    out _))
            {
                return false;
            }

            string[] terms = inspector.Buttons
                .Where(button => button != null && button.IsUsedForBreadboard)
                .SelectMany(button => new[] { button.Keyword, button.ButtonName })
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (terms.Length == 0)
                return false;

            AutomationBreadboardProxyOption option = FindGetterOption(component, terms);
            if (option == null)
                return false;

            ApplyGetterOption(component, option);
            return true;
        }

        private static bool TryAutoSelectTargetProxyProperty(
            object component,
            AutomationTarget target,
            bool getter)
        {
            if (component == null || target == null)
                return false;

            if (getter && TryAutoSelectAcbControllerGetter(component, target))
                return true;

            IReadOnlyList<string> terms = TargetProxySearchTerms(target, getter);
            if (terms.Count == 0)
                return false;

            AutomationBreadboardProxyOption option = FindProxyOption(component, getter, terms);
            if (option == null)
                return false;

            if (getter)
                ApplyGetterOption(component, option);
            else
                ApplySetterOption(component, option);
            return true;
        }

        private static AutomationBreadboardProxyOption FindGetterOption(
            object component,
            IReadOnlyList<string> searchTerms)
        {
            return FindProxyOption(component, getter: true, searchTerms: searchTerms);
        }

        private static AutomationBreadboardProxyOption FindProxyOption(
            object component,
            bool getter,
            IReadOnlyList<string> searchTerms)
        {
            InvokeInstance(component, "ExtractCorrectInputs");
            object picker = InvokeInstance(component, "PropertyPicker");
            object items = InvokeInstance(picker, "EnumerateItems");
            foreach (object item in EnumerateCollection(items, 512))
            {
                object nativeOption = ReadMember(item, "ObjectForAction");
                if (nativeOption == null)
                    continue;

                string label = ReadPlainString(item, "Name", "Property");
                string tooltip = ReadPlainString(item, "ToolTip", string.Empty);
                if (!MatchesAnyTerm(label, tooltip, searchTerms))
                    continue;

                AutomationBreadboardProxyOption option = CreateProxyOption(
                    getter,
                    nativeOption,
                    label,
                    tooltip);
                if (option != null && !option.IsClear)
                    return option;
            }

            return null;
        }

        internal static IReadOnlyList<string> TargetProxySearchTerms(
            AutomationTarget target,
            bool getter)
        {
            var terms = new List<string>();
            if (target == null)
                return terms;

            switch (target.Controller?.Kind)
            {
                case AutomationControllerKind.Acb:
                    AddTargetProxySearchTerms(
                        terms,
                        "enabled",
                        "priority",
                        "condition",
                        "action",
                        "range",
                        "delay",
                        "interval",
                        "search",
                        "pattern");
                    if (!getter)
                        AddTargetProxySearchTerms(terms, "trigger", "activate");
                    break;
            }

            switch (target.Category)
            {
                case AutomationTargetCategory.Spinblocks:
                    AddTargetProxySearchTerms(terms, "angle", "speed", "velocity", "rotation", "azimuth", "elevation");
                    break;
                case AutomationTargetCategory.TurretsWeapons:
                    AddTargetProxySearchTerms(terms, "fire", "firing", "aim", "yaw", "pitch", "azimuth", "elevation");
                    break;
                case AutomationTargetCategory.Propulsion:
                    AddTargetProxySearchTerms(terms, "drive", "thrust", "throttle", "power", "rpm");
                    break;
                case AutomationTargetCategory.Pistons:
                    AddTargetProxySearchTerms(terms, "extension", "position", "velocity", "speed");
                    break;
                case AutomationTargetCategory.Pumps:
                    AddTargetProxySearchTerms(terms, "pump", "air", "water", "pressure");
                    break;
                case AutomationTargetCategory.ControlSurfaces:
                    AddTargetProxySearchTerms(terms, "angle", "pitch", "roll", "yaw");
                    break;
                case AutomationTargetCategory.Ai:
                    AddTargetProxySearchTerms(terms, "target", "range", "bearing", "aim", "yaw", "pitch");
                    break;
                case AutomationTargetCategory.Missiles:
                    AddTargetProxySearchTerms(terms, "launch", "fire", "missile", "target", "guidance", "fuse");
                    break;
                case AutomationTargetCategory.Lights:
                    AddTargetProxySearchTerms(terms, "enabled", "on", "intensity", "colour", "color");
                    break;
                case AutomationTargetCategory.ShieldsDefence:
                    AddTargetProxySearchTerms(terms, "shield", "strength", "smoke", "defence", "defense");
                    break;
                case AutomationTargetCategory.Detection:
                    AddTargetProxySearchTerms(terms, "range", "bearing", "target", "signal", "detected");
                    break;
                case AutomationTargetCategory.DoorsDocking:
                    AddTargetProxySearchTerms(terms, "open", "door", "dock", "docking", "clamp");
                    break;
                case AutomationTargetCategory.SoundDisplay:
                    AddTargetProxySearchTerms(terms, "enabled", "volume", "sound", "display", "text");
                    break;
                case AutomationTargetCategory.ResourcePower:
                    AddTargetProxySearchTerms(terms, "power", "fuel", "ammo", "material", "battery", "charge");
                    break;
            }

            return terms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddTargetProxySearchTerms(
            ICollection<string> terms,
            params string[] values)
        {
            if (terms == null || values == null)
                return;

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    terms.Add(value);
            }
        }

        private static bool MatchesAnyTerm(
            string label,
            string tooltip,
            IReadOnlyList<string> searchTerms)
        {
            if (searchTerms == null || searchTerms.Count == 0)
                return false;

            string haystack = (label ?? string.Empty) + " " + (tooltip ?? string.Empty);
            return searchTerms.Any(term =>
                !string.IsNullOrWhiteSpace(term) &&
                haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool MatchesFilter(string label, string tooltip, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return (label ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (tooltip ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IReadOnlyList<AutomationBreadboardPortSummary> ReadPorts(
            AutomationBreadboardComponentSummary component,
            bool outputs,
            int limit)
        {
            var results = new List<AutomationBreadboardPortSummary>();
            object native = component?.NativeComponent;
            if (native == null)
                return results;

            object list = ReadMember(native, outputs ? "AOutputs" : "BInputs");
            int count = ReadPlainInt(list, "Count", 0);
            int shown = Math.Min(Math.Max(0, count), Math.Max(0, limit));
            for (int index = 0; index < shown; index++)
            {
                object port = GetPort(native, outputs, index);
                if (port == null)
                    continue;

                bool connected = !outputs && IsInputConnected(port);
                uint connectedFromComponentId = 0U;
                int connectedFromOutputIndex = -1;
                string connectedFrom = connected
                    ? ConnectedOutputLabel(port, out connectedFromComponentId, out connectedFromOutputIndex)
                    : string.Empty;
                string label = DescribePort(native, outputs, index);
                results.Add(new AutomationBreadboardPortSummary(
                    index,
                    string.IsNullOrWhiteSpace(label) ? (outputs ? "Out " : "In ") + index.ToString(CultureInfo.InvariantCulture) : label,
                    outputs,
                    connected,
                    connectedFrom,
                    connectedFromComponentId,
                    connectedFromOutputIndex));
            }

            return results;
        }

        private static object GetPort(object component, bool outputs, int index)
        {
            if (component == null || index < 0)
                return null;

            object list = ReadMember(component, outputs ? "AOutputs" : "BInputs");
            if (list == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo indexer = list.GetType().GetProperty("Item", flags);
                if (indexer != null)
                    return indexer.GetValue(list, new object[] { index });
            }
            catch
            {
                // Try backing list enumeration below.
            }

            return EnumerateCollection(ReadUs(list), index + 1).Skip(index).FirstOrDefault();
        }

        private static string DescribePort(object component, bool outputs, int index)
        {
            string methodName = outputs ? "DescribeOutput" : "DescribeInput";
            object value = InvokeInstance(component, methodName, index);
            return value?.ToString() ?? string.Empty;
        }

        private static bool IsInputConnected(object input)
        {
            object latch = ReadMember(input, "OurOutput");
            return ReadPlainBool(latch, "IsLatched", false);
        }

        private static string ConnectedOutputLabel(
            object input,
            out uint componentId,
            out int outputIndex)
        {
            componentId = 0U;
            outputIndex = -1;
            object latch = ReadMember(input, "OurOutput");
            object output = ReadMember(latch, "Them");
            object component = ReadMember(output, "OurComponent");
            if (component == null)
                return string.Empty;

            string type = component.GetType().Name;
            componentId = ReadPlainUInt(component, "UniqueId", 0U);
            outputIndex = IndexOfPort(component, true, output);
            return string.IsNullOrWhiteSpace(type)
                ? componentId.ToString(CultureInfo.InvariantCulture)
                : type + " " + componentId.ToString(CultureInfo.InvariantCulture);
        }

        private static int IndexOfPort(object component, bool outputs, object port)
        {
            if (component == null || port == null)
                return -1;

            object list = ReadMember(component, outputs ? "AOutputs" : "BInputs");
            int count = Math.Max(0, ReadPlainInt(list, "Count", 0));
            for (int index = 0; index < count; index++)
            {
                object candidate = GetPort(component, outputs, index);
                if (ReferenceEquals(candidate, port) || Equals(candidate, port))
                    return index;
            }

            return -1;
        }

        private bool ExecuteBoardCommand(string commandName, params object[] args)
        {
            try
            {
                Type commandType = Type.GetType(
                    "BrilliantSkies.Common.Circuits.Ui.UndoRedo." + commandName + ", Breadboards");
                if (commandType == null)
                    return false;

                object[] parameters = new object[args.Length + 1];
                parameters[0] = _board;
                Array.Copy(args, 0, parameters, 1, args.Length);
                object command = Activator.CreateInstance(commandType, parameters);
                MethodInfo execute = commandType.GetMethod(
                    "Execute",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                execute?.Invoke(command, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IReadOnlyList<AutomationBreadboardComponentSummary> ReadComponents(int limit)
        {
            object collection = ReadMember(_board, "Components");
            if (collection == null)
                collection = ReadMember(_board, "Packages");

            var results = new List<AutomationBreadboardComponentSummary>();
            foreach (object component in EnumerateCollection(collection, limit))
            {
                if (component == null)
                    continue;

                results.Add(new AutomationBreadboardComponentSummary(
                    component,
                    ComponentDisplayName(component),
                    component.GetType().FullName ?? component.GetType().Name,
                    ReadPlainUInt(component, "UniqueId", 0U),
                    ReadGuid(component, "ComponentTypeId"),
                    ReadFloat(component, "X", 0f),
                    ReadFloat(component, "Y", 0f),
                    ReadFloat(component, "Width", ReadPlainFloat(component, "WidthToUse", 0f)),
                    ReadFloat(component, "Height", ReadPlainFloat(component, "HeightToUse", 0f)),
                    CountLinks(ReadMember(component, "BInputs")),
                    CountLinks(ReadMember(component, "AOutputs")),
                    ReadPlainString(component, "Description", string.Empty),
                    ReadPlainString(ReadUs(ReadMember(component, "BlockTypeName")), null, string.Empty),
                    ReadPlainString(ReadUs(ReadMember(component, "BlockFilter")), null, string.Empty),
                    ReadPlainUInt(ReadMember(component, "ReadableAttributeId"), "Us", 999999U),
                    ReadPlainUInt(ReadMember(component, "BlockPropertyId"), "Us", 999999U),
                    ReadPlainUInt(ReadMember(component, "BlockSetId"), "Us", 999999U)));
            }

            return results;
        }

        private IReadOnlyList<AutomationBreadboardAvailableComponent> ReadAvailableComponents()
        {
            object collection = ReadMember(_board, "AvailableComponentTypes");
            var results = new List<AutomationBreadboardAvailableComponent>();
            foreach (object entry in EnumerateCollection(collection, 256))
            {
                if (entry == null)
                    continue;

                Type type = ReadMember(entry, "Type") as Type;
                Guid guid = ReadGuid(entry, "GuidOfComponent");
                object attribute = ReadMember(entry, "BoardAttribute");
                string name = ReadPlainString(attribute, "Name", null);
                string description = ReadPlainString(attribute, "Description", null);
                results.Add(new AutomationBreadboardAvailableComponent(
                    string.IsNullOrWhiteSpace(name) ? type?.Name ?? "Component" : name,
                    type?.Name ?? string.Empty,
                    type?.FullName ?? string.Empty,
                    guid,
                    description ?? string.Empty));
            }

            return results
                .OrderBy(component => component.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private object FindAvailableComponent(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            object collection = ReadMember(_board, "AvailableComponentTypes");
            foreach (object entry in EnumerateCollection(collection, 256))
            {
                Type type = ReadMember(entry, "Type") as Type;
                if (type == null)
                    continue;

                if (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private AutomationBreadboardAvailableComponent FindAvailableComponentMatching(
            IReadOnlyList<string> searchTerms)
        {
            if (searchTerms == null || searchTerms.Count == 0)
                return null;

            string[] terms = searchTerms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .ToArray();
            if (terms.Length == 0)
                return null;

            return AvailableComponents.FirstOrDefault(component =>
            {
                string haystack =
                    (component.Label ?? string.Empty) + " " +
                    (component.TypeName ?? string.Empty) + " " +
                    (component.FullTypeName ?? string.Empty) + " " +
                    (component.Description ?? string.Empty);
                return terms.All(term =>
                    haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        private bool ExecuteAddComponentCommand(object component)
        {
            try
            {
                Type commandType = Type.GetType(
                    "BrilliantSkies.Common.Circuits.Ui.UndoRedo.AddComponentCommand, Breadboards");
                if (commandType == null)
                    return false;

                object command = Activator.CreateInstance(commandType, _board, component);
                MethodInfo execute = commandType.GetMethod(
                    "Execute",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                execute?.Invoke(command, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool InvokeNewPackage(object component)
        {
            try
            {
                MethodInfo method = _board.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                        candidate.Name == "NewPackage" &&
                        candidate.IsGenericMethodDefinition &&
                        candidate.GetParameters().Length == 1);
                if (method == null)
                    return false;

                method
                    .MakeGenericMethod(component.GetType())
                    .Invoke(_board, new[] { component });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void PlaceNewComponent(object component)
        {
            IReadOnlyList<AutomationBreadboardComponentSummary> components = Components;
            float x = 80f;
            float y = 80f;
            if (components.Count > 0)
            {
                float maxX = components.Max(item => item.X);
                float maxY = components.Max(item => item.Y);
                x = maxX + 180f;
                y = maxY;
            }

            WriteValue(component, "X", x);
            WriteValue(component, "Y", y);
        }

        private static void ConfigureGenericProxy(
            object component,
            AutomationTarget target,
            string role)
        {
            if (component == null || target?.Block == null)
                return;

            Type blockType = target.Block.GetType();
            string blockTypeName = string.IsNullOrWhiteSpace(blockType.Name)
                ? blockType.FullName ?? string.Empty
                : blockType.Name;
            WriteValue(component, "BlockTypeName", blockTypeName);
            WriteDirectMember(component, "BlockType", blockType);
            WriteValue(component, "BlockFilter", SuggestedBlockFilter(target));
            WriteDirectMember(component, "_blocksDirty", true);
            if (string.Equals(role, "Getter", StringComparison.OrdinalIgnoreCase))
            {
                WriteValue(component, "ReadableAttributeId", 0U);
            }
            else if (string.Equals(role, "Setter", StringComparison.OrdinalIgnoreCase))
            {
                WriteValue(component, "HideLocked", true);
            }

            InvokeInstance(component, "SetBlockType");
        }

        private static string SuggestedBlockFilter(AutomationTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Label))
                return string.Empty;

            string runtimeType = target.RuntimeType ?? string.Empty;
            if (string.Equals(target.Label, runtimeType, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (target.Controller != null)
                return string.Empty;

            return target.Label;
        }

        private bool ReadBool(string propertyName, bool fallback) =>
            ReadBool(_board, propertyName, fallback);

        private float ReadFloat(string propertyName, float fallback) =>
            ReadFloat(_board, propertyName, fallback);

        private string ReadString(string propertyName, string fallback) =>
            ReadPlainString(ReadUs(ReadMember(_board, propertyName)), null, fallback);

        private bool WriteValue(string propertyName, object value) =>
            WriteValue(_board, propertyName, value);

        private static bool ReadBool(object owner, string propertyName, bool fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static float ReadFloat(object owner, string propertyName, float fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool WriteValue(object owner, string propertyName, object value)
        {
            object variable = ReadMember(owner, propertyName);
            return variable != null && WriteUs(variable, value);
        }

        private static bool WriteDirectMember(object owner, string memberName, object value)
        {
            if (owner == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            try
            {
                PropertyInfo property = FindProperty(type, memberName, flags);
                if (property?.SetMethod != null)
                {
                    property.SetValue(owner, ConvertFor(value, property.PropertyType), null);
                    return true;
                }
            }
            catch
            {
                // Try the matching field below.
            }

            try
            {
                FieldInfo field = FindField(type, memberName, flags);
                if (field == null)
                    return false;

                field.SetValue(owner, ConvertFor(value, field.FieldType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object InvokeInstance(object owner, string methodName, params object[] parameters)
        {
            if (owner == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                MethodInfo method = owner.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method?.Invoke(owner, parameters);
            }
            catch
            {
                return null;
            }
        }

        private static object ReadMember(object owner, string memberName)
        {
            if (owner == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            try
            {
                PropertyInfo property = FindProperty(type, memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(owner, null);
            }
            catch
            {
                // Try the matching field below.
            }

            try
            {
                FieldInfo field = FindField(type, memberName, flags);
                return field?.GetValue(owner);
            }
            catch
            {
                return null;
            }
        }

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(
                    name,
                    flags | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static object ReadUs(object variable)
        {
            if (variable == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo property = variable.GetType().GetProperty("Us", flags);
                return property?.GetValue(variable, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool WriteUs(object variable, object value)
        {
            if (variable == null)
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo property = variable.GetType().GetProperty("Us", flags);
                if (property?.SetMethod == null)
                    return false;

                object converted = ConvertFor(value, property.PropertyType);
                property.SetValue(variable, converted, null);
                TryForceChanged(variable);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (targetType == null || value == null)
                return value;

            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsInstanceOfType(value))
                return value;
            if (underlying.IsEnum)
                return Enum.ToObject(underlying, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            if (underlying == typeof(string))
                return value.ToString();

            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }

        private static void TryForceChanged(object variable)
        {
            try
            {
                MethodInfo method = variable.GetType().GetMethod(
                    "ForceChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                method?.Invoke(variable, null);
            }
            catch
            {
                // Setting Us already follows FtD's normal change path.
            }
        }

        private static int CountLinks(object linkList)
        {
            int count = ReadPlainInt(linkList, "Count", -1);
            if (count >= 0)
                return count;

            return EnumerateCollection(ReadUs(linkList), 128).Count();
        }

        private static int ReadVarInt(object owner, string propertyName, int fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static long ReadVarLong(object owner, string propertyName, long fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int PositiveModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;

            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static int ReadPlainInt(object owner, string memberName, int fallback)
        {
            object value = ReadMember(owner, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static uint ReadPlainUInt(object owner, string memberName, uint fallback)
        {
            object value = ReadMember(owner, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ReadPlainBool(object owner, string memberName, bool fallback)
        {
            object value = ReadMember(owner, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static float ReadPlainFloat(object owner, string memberName, float fallback)
        {
            object value = ReadMember(owner, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static Guid ReadGuid(object owner, string memberName)
        {
            object value = ReadMember(owner, memberName);
            if (value is Guid guid)
                return guid;

            return Guid.TryParse(value?.ToString(), out Guid parsed) ? parsed : Guid.Empty;
        }

        private static string ReadPlainString(object owner, string memberName, string fallback)
        {
            object value = string.IsNullOrWhiteSpace(memberName)
                ? owner
                : ReadMember(owner, memberName);
            return value == null ? fallback : value.ToString();
        }

        private static string ComponentDisplayName(object component)
        {
            if (component == null)
                return "Component";

            string typeName = component.GetType().Name;
            string description = ReadPlainString(component, "Description", string.Empty);
            if (!string.IsNullOrWhiteSpace(description) &&
                description.Length <= 44 &&
                description.IndexOf('\n') < 0)
            {
                return description;
            }

            return string.IsNullOrWhiteSpace(typeName) ? "Component" : typeName;
        }

        private static object FindBoard(object root)
        {
            if (root == null)
                return null;

            var queue = new Queue<SearchNode>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            queue.Enqueue(new SearchNode(root, 0));
            int inspected = 0;
            while (queue.Count > 0 && inspected < MaxSearchObjects)
            {
                SearchNode node = queue.Dequeue();
                object current = node.Value;
                if (current == null || !visited.Add(current))
                    continue;

                inspected++;
                Type type = current.GetType();
                if (IsBoardType(type))
                    return current;

                if (node.Depth >= MaxSearchDepth || ShouldNotRecurse(type))
                    continue;

                foreach (object child in EnumerateChildren(current))
                    queue.Enqueue(new SearchNode(child, node.Depth + 1));
            }

            return null;
        }

        private static bool IsBoardType(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == "BrilliantSkies.Common.Circuits.Board" ||
                    current.FullName == "BrilliantSkies.Blocks.FtdBoard" ||
                    current.FullName == "BrilliantSkies.Ftd.Missiles.Breadboard.MissileBreadboardBoard")
                {
                    return true;
                }
            }

            try
            {
                return type.GetInterfaces().Any(
                    face => face.FullName == "BrilliantSkies.Common.Circuits.IBoard");
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<object> EnumerateChildren(object source)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = source.GetType();
            foreach (FieldInfo field in SafeFields(type, flags))
            {
                object value = SafeGet(() => field.GetValue(source));
                foreach (object child in ExpandValue(value))
                    yield return child;
            }

            foreach (PropertyInfo property in SafeProperties(type, flags))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                object value = SafeGet(() => property.GetValue(source, null));
                foreach (object child in ExpandValue(value))
                    yield return child;
            }
        }

        private static IEnumerable<object> ExpandValue(object value)
        {
            if (value == null)
                yield break;

            Type type = value.GetType();
            if (ShouldNotRecurse(type))
                yield break;

            yield return value;
            foreach (object item in EnumerateCollection(value, MaxExpandedEnumerableItems))
            {
                if (item != null)
                    yield return item;
            }
        }

        private static IEnumerable<object> EnumerateCollection(object value, int limit)
        {
            if (value == null)
                yield break;

            if (value is IDictionary dictionary)
            {
                int count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value != null)
                        yield return entry.Value;
                    count++;
                    if (count >= limit)
                        yield break;
                }

                yield break;
            }

            if (!(value is IEnumerable enumerable) || value is string)
                yield break;

            int index = 0;
            foreach (object item in enumerable)
            {
                if (item != null)
                    yield return item;
                index++;
                if (index >= limit)
                    yield break;
            }
        }

        private static bool ShouldNotRecurse(Type type)
        {
            if (type == null)
                return true;

            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type.FullName != null && type.FullName.StartsWith("UnityEngine.", StringComparison.Ordinal);
        }

        private static IEnumerable<FieldInfo> SafeFields(Type type, BindingFlags flags)
        {
            try
            {
                var fields = new List<FieldInfo>();
                for (Type current = type; current != null; current = current.BaseType)
                {
                    fields.AddRange(current.GetFields(flags | BindingFlags.DeclaredOnly));
                }

                return fields;
            }
            catch
            {
                return Array.Empty<FieldInfo>();
            }
        }

        private static IEnumerable<PropertyInfo> SafeProperties(Type type, BindingFlags flags)
        {
            try
            {
                var properties = new List<PropertyInfo>();
                for (Type current = type; current != null; current = current.BaseType)
                {
                    properties.AddRange(current.GetProperties(flags | BindingFlags.DeclaredOnly));
                }

                return properties;
            }
            catch
            {
                return Array.Empty<PropertyInfo>();
            }
        }

        private static object SafeGet(Func<object> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private readonly struct SearchNode
        {
            internal SearchNode(object value, int depth)
            {
                Value = value;
                Depth = depth;
            }

            internal object Value { get; }

            internal int Depth { get; }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance =
                new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal sealed class AutomationBreadboardComponentSummary
    {
        internal AutomationBreadboardComponentSummary(
            object nativeComponent,
            string label,
            string typeName,
            uint uniqueId,
            Guid componentTypeId,
            float x,
            float y,
            float width,
            float height,
            int inputCount,
            int outputCount,
            string description,
            string blockTypeName,
            string blockFilter,
            uint readableAttributeId,
            uint blockPropertyId,
            uint blockSetId)
        {
            NativeComponent = nativeComponent;
            Label = string.IsNullOrWhiteSpace(label) ? "Component" : label;
            TypeName = typeName ?? string.Empty;
            UniqueId = uniqueId;
            ComponentTypeId = componentTypeId;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            InputCount = inputCount;
            OutputCount = outputCount;
            Description = description ?? string.Empty;
            BlockTypeName = blockTypeName ?? string.Empty;
            BlockFilter = blockFilter ?? string.Empty;
            ReadableAttributeId = readableAttributeId;
            BlockPropertyId = blockPropertyId;
            BlockSetId = blockSetId;
        }

        internal object NativeComponent { get; }

        internal string Label { get; }

        internal string TypeName { get; }

        internal uint UniqueId { get; }

        internal Guid ComponentTypeId { get; }

        internal float X { get; }

        internal float Y { get; }

        internal float Width { get; }

        internal float Height { get; }

        internal int InputCount { get; }

        internal int OutputCount { get; }

        internal string Description { get; }

        internal string BlockTypeName { get; }

        internal string BlockFilter { get; }

        internal uint ReadableAttributeId { get; }

        internal uint BlockPropertyId { get; }

        internal uint BlockSetId { get; }

        internal bool IsEvaluator =>
            TypeName.EndsWith(".Evaluator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TypeName, "Evaluator", StringComparison.OrdinalIgnoreCase);

        internal bool IsSwitch =>
            TypeName.EndsWith(".Switch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TypeName, "Switch", StringComparison.OrdinalIgnoreCase);

        internal bool IsLogicGate =>
            TypeName.EndsWith(".LogicGate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TypeName, "LogicGate", StringComparison.OrdinalIgnoreCase);

        internal bool IsConstantInput =>
            TypeName.EndsWith(".ConstantInput", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TypeName, "ConstantInput", StringComparison.OrdinalIgnoreCase);

        internal bool IsGenericGetter =>
            TypeName.IndexOf("GenericBlockGetter", StringComparison.OrdinalIgnoreCase) >= 0;

        internal bool IsGenericSetter =>
            TypeName.IndexOf("GenericBlockSetter", StringComparison.OrdinalIgnoreCase) >= 0;

        internal bool IsGenericProxy => IsGenericGetter || IsGenericSetter;
    }

    internal sealed class AutomationBreadboardCompileResult
    {
        internal AutomationBreadboardCompileResult(
            string label,
            IReadOnlyList<uint> componentIds)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "compile" : label;
            ComponentIds = componentIds ?? Array.Empty<uint>();
        }

        internal string Label { get; }

        internal IReadOnlyList<uint> ComponentIds { get; }

        internal int ComponentCount => ComponentIds.Count;
    }

    internal sealed class AutomationBreadboardComponentSettings
    {
        internal static readonly string[] LogicGateLabels =
        {
            "NOT",
            "AND",
            "OR",
            "XOR",
            "NAND",
            "NOR",
            "XNOR"
        };

        internal static readonly string[] TrueLogicLabels =
        {
            "Non-zero",
            "Greater than 0",
            "At least 1"
        };

        internal static readonly string[] ConstantTypeLabels =
        {
            "Float",
            "Text",
            "Vector",
            "Quaternion",
            "Integer"
        };

        private AutomationBreadboardComponentSettings(string kind)
        {
            Kind = kind ?? string.Empty;
        }

        internal string Kind { get; }

        internal string Expression { get; private set; }

        internal float Threshold { get; private set; }

        internal float FailValue { get; private set; }

        internal int LogicGate { get; private set; }

        internal int TrueLogic { get; private set; }

        internal int ConstantType { get; private set; }

        internal float ConstantFloat { get; private set; }

        internal string ConstantString { get; private set; }

        internal long ConstantLong { get; private set; }

        internal string LogicGateLabel => LabelAt(LogicGateLabels, LogicGate);

        internal string TrueLogicLabel => LabelAt(TrueLogicLabels, TrueLogic);

        internal string ConstantTypeLabel => LabelAt(ConstantTypeLabels, ConstantType);

        internal static AutomationBreadboardComponentSettings ForEvaluator(string expression) =>
            new AutomationBreadboardComponentSettings("Evaluator")
            {
                Expression = expression ?? string.Empty
            };

        internal static AutomationBreadboardComponentSettings ForSwitch(float threshold, float failValue) =>
            new AutomationBreadboardComponentSettings("Switch")
            {
                Threshold = threshold,
                FailValue = failValue
            };

        internal static AutomationBreadboardComponentSettings ForLogicGate(int logicGate, int trueLogic) =>
            new AutomationBreadboardComponentSettings("LogicGate")
            {
                LogicGate = ClampIndex(logicGate, LogicGateLabels.Length),
                TrueLogic = ClampIndex(trueLogic, TrueLogicLabels.Length)
            };

        internal static AutomationBreadboardComponentSettings ForConstantInput(
            int constantType,
            float constantFloat,
            string constantString,
            long constantLong) =>
            new AutomationBreadboardComponentSettings("ConstantInput")
            {
                ConstantType = ClampIndex(constantType, ConstantTypeLabels.Length),
                ConstantFloat = constantFloat,
                ConstantString = constantString ?? string.Empty,
                ConstantLong = constantLong
            };

        private static int ClampIndex(int value, int count)
        {
            if (count <= 0)
                return 0;

            return Math.Max(0, Math.Min(count - 1, value));
        }

        private static string LabelAt(string[] labels, int index)
        {
            if (labels == null || labels.Length == 0)
                return string.Empty;

            int clamped = ClampIndex(index, labels.Length);
            return labels[clamped] ?? string.Empty;
        }
    }

    internal sealed class AutomationBreadboardAvailableComponent
    {
        internal AutomationBreadboardAvailableComponent(
            string label,
            string typeName,
            string fullTypeName,
            Guid componentTypeId,
            string description)
        {
            Label = string.IsNullOrWhiteSpace(label) ? typeName : label;
            TypeName = typeName ?? string.Empty;
            FullTypeName = fullTypeName ?? string.Empty;
            ComponentTypeId = componentTypeId;
            Description = description ?? string.Empty;
        }

        internal string Label { get; }

        internal string TypeName { get; }

        internal string FullTypeName { get; }

        internal Guid ComponentTypeId { get; }

        internal string Description { get; }
    }

    internal sealed class AutomationBreadboardPortSummary
    {
        internal AutomationBreadboardPortSummary(
            int index,
            string label,
            bool isOutput,
            bool isConnected,
            string connectedFrom,
            uint connectedFromComponentId,
            int connectedFromOutputIndex)
        {
            Index = index;
            Label = string.IsNullOrWhiteSpace(label) ? "Port " + index.ToString(CultureInfo.InvariantCulture) : label;
            IsOutput = isOutput;
            IsConnected = isConnected;
            ConnectedFrom = connectedFrom ?? string.Empty;
            ConnectedFromComponentId = connectedFromComponentId;
            ConnectedFromOutputIndex = connectedFromOutputIndex;
        }

        internal int Index { get; }

        internal string Label { get; }

        internal bool IsOutput { get; }

        internal bool IsConnected { get; }

        internal string ConnectedFrom { get; }

        internal uint ConnectedFromComponentId { get; }

        internal int ConnectedFromOutputIndex { get; }
    }

    internal sealed class AutomationBreadboardProxyOption
    {
        private const uint UnselectedCode = 999999U;

        private AutomationBreadboardProxyOption(
            object nativeOption,
            string label,
            string tooltip,
            bool isClear,
            bool isGetterReadable,
            uint readableAttributeId,
            uint blockPropertyId,
            uint blockSetId)
        {
            NativeOption = nativeOption;
            Label = string.IsNullOrWhiteSpace(label) ? "Property" : label;
            Tooltip = tooltip ?? string.Empty;
            IsClear = isClear;
            IsGetterReadable = isGetterReadable;
            ReadableAttributeId = readableAttributeId;
            BlockPropertyId = blockPropertyId;
            BlockSetId = blockSetId;
        }

        internal object NativeOption { get; }

        internal string Label { get; }

        internal string Tooltip { get; }

        internal bool IsClear { get; }

        internal bool IsGetterReadable { get; }

        internal uint ReadableAttributeId { get; }

        internal uint BlockPropertyId { get; }

        internal uint BlockSetId { get; }

        internal static AutomationBreadboardProxyOption Clear(bool getter) =>
            new AutomationBreadboardProxyOption(
                null,
                "Unselected",
                string.Empty,
                isClear: true,
                isGetterReadable: getter,
                readableAttributeId: UnselectedCode,
                blockPropertyId: UnselectedCode,
                blockSetId: UnselectedCode);

        internal static AutomationBreadboardProxyOption GetterReadable(
            object nativeOption,
            string label,
            string tooltip,
            uint readableAttributeId) =>
            new AutomationBreadboardProxyOption(
                nativeOption,
                label,
                tooltip,
                isClear: false,
                isGetterReadable: true,
                readableAttributeId: readableAttributeId,
                blockPropertyId: UnselectedCode,
                blockSetId: UnselectedCode);

        internal static AutomationBreadboardProxyOption GetterVariable(
            object nativeOption,
            string label,
            string tooltip,
            uint blockPropertyId,
            uint blockSetId) =>
            new AutomationBreadboardProxyOption(
                nativeOption,
                label,
                tooltip,
                isClear: false,
                isGetterReadable: false,
                readableAttributeId: UnselectedCode,
                blockPropertyId: blockPropertyId,
                blockSetId: blockSetId);

        internal static AutomationBreadboardProxyOption Setter(
            object nativeOption,
            string label,
            string tooltip,
            uint blockPropertyId,
            uint blockSetId) =>
            new AutomationBreadboardProxyOption(
                nativeOption,
                label,
                tooltip,
                isClear: false,
                isGetterReadable: false,
                readableAttributeId: UnselectedCode,
                blockPropertyId: blockPropertyId,
                blockSetId: blockSetId);

        internal bool IsSelectedBy(AutomationBreadboardComponentSummary component)
        {
            if (component == null)
                return false;

            if (IsClear)
            {
                return component.ReadableAttributeId == UnselectedCode &&
                       component.BlockPropertyId == UnselectedCode &&
                       component.BlockSetId == UnselectedCode;
            }

            if (component.IsGenericGetter && IsGetterReadable)
                return component.ReadableAttributeId == ReadableAttributeId;

            return component.BlockPropertyId == BlockPropertyId &&
                   component.BlockSetId == BlockSetId;
        }
    }
}
