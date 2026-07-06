using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationAcbControllerInspector
    {
        private const int MaxSearchObjects = 180;
        private const int MaxSearchDepth = 5;

        private readonly object _controller;
        private readonly IReadOnlyList<AutomationAcbControllerButtonSummary> _buttons;

        private AutomationAcbControllerInspector(
            object controller,
            IReadOnlyList<AutomationAcbControllerButtonSummary> buttons)
        {
            _controller = controller;
            _buttons = buttons ?? Array.Empty<AutomationAcbControllerButtonSummary>();
        }

        internal string ControllerTypeName => _controller?.GetType().FullName ?? string.Empty;

        internal IReadOnlyList<AutomationAcbControllerButtonSummary> Buttons => _buttons;

        internal bool TryRewriteCurrentButtonValues(
            AutomationAcbControllerButtonSummary button,
            out string message)
        {
            message = null;
            if (button?.NativeButton == null)
            {
                message = "Select an ACB Controller button first.";
                return false;
            }

            var failures = new List<string>();
            if (!WriteValue(button.NativeButton, "ButtonName", button.ButtonName ?? string.Empty))
                failures.Add("ButtonName");
            if (!WriteValue(button.NativeButton, "Keyword", button.Keyword ?? string.Empty))
                failures.Add("Keyword");
            if (!WriteValue(button.NativeButton, "ShapeId", Math.Max(0, button.ShapeId)))
                failures.Add("ShapeId");
            if (!WriteValue(button.NativeButton, "IsUsedForBreadboard", button.IsUsedForBreadboard))
                failures.Add("IsUsedForBreadboard");
            if (!WriteValue(button.NativeButton, "ButtonColor", button.ButtonColor))
                failures.Add("ButtonColor");

            if (failures.Count == 0)
            {
                message = "ACB Controller button writes accepted same-value probes.";
                return true;
            }

            message = "ACB Controller button writes failed for " + string.Join(", ", failures.ToArray()) + ".";
            return false;
        }

        internal bool TrySetButtonName(
            AutomationAcbControllerButtonSummary button,
            string value,
            out string message) =>
            TryWriteButtonText(button, "ButtonName", value, "button name", out message);

        internal bool TrySetKeyword(
            AutomationAcbControllerButtonSummary button,
            string value,
            out string message) =>
            TryWriteButtonText(button, "Keyword", value, "keyword", out message);

        internal bool TrySetShapeId(
            AutomationAcbControllerButtonSummary button,
            int value,
            out string message)
        {
            message = null;
            if (button?.NativeButton == null)
            {
                message = "Select an ACB Controller button first.";
                return false;
            }

            int shape = Math.Max(0, value);
            if (!WriteValue(button.NativeButton, "ShapeId", shape))
            {
                message = "FtD rejected the ACB Controller shape write.";
                return false;
            }

            message = "ACB Controller button shape updated.";
            return true;
        }

        internal bool TrySetBreadboardOutput(
            AutomationAcbControllerButtonSummary button,
            bool value,
            out string message)
        {
            message = null;
            if (button?.NativeButton == null)
            {
                message = "Select an ACB Controller button first.";
                return false;
            }

            if (!WriteValue(button.NativeButton, "IsUsedForBreadboard", value))
            {
                message = "FtD rejected the Breadboard output flag write.";
                return false;
            }

            message = "ACB Controller Breadboard output " + (value ? "enabled" : "disabled") + ".";
            return true;
        }

        internal bool TrySetButtonColor(
            AutomationAcbControllerButtonSummary button,
            Color value,
            out string message)
        {
            message = null;
            if (button?.NativeButton == null)
            {
                message = "Select an ACB Controller button first.";
                return false;
            }

            Color color = new Color(
                Mathf.Clamp01(value.r),
                Mathf.Clamp01(value.g),
                Mathf.Clamp01(value.b),
                Mathf.Clamp01(value.a <= 0f ? 1f : value.a));
            if (!WriteValue(button.NativeButton, "ButtonColor", color))
            {
                message = "FtD rejected the ACB Controller color write.";
                return false;
            }

            message = "ACB Controller button color updated.";
            return true;
        }

        internal static bool TryCreate(
            Block block,
            out AutomationAcbControllerInspector inspector,
            out string reason)
        {
            inspector = null;
            reason = null;
            if (block == null)
            {
                reason = "No selected ACB Controller block.";
                return false;
            }

            IReadOnlyList<object> nativeButtons = FindAllByTypeName(block, "AcbControllerButtonData", 16);
            if (nativeButtons.Count == 0)
            {
                reason = "AcbControllerButtonData was not found on this ACB Controller instance.";
                return false;
            }

            var summaries = new List<AutomationAcbControllerButtonSummary>(nativeButtons.Count);
            for (int index = 0; index < nativeButtons.Count; index++)
            {
                object native = nativeButtons[index];
                summaries.Add(new AutomationAcbControllerButtonSummary(
                    native,
                    index,
                    ReadString(native, "ButtonName", "Button " + (index + 1).ToString(CultureInfo.InvariantCulture)),
                    ReadString(native, "Keyword", string.Empty),
                    ReadInt(native, "ShapeId", 0),
                    ReadBool(native, "IsUsedForBreadboard", false),
                    ReadColor(native, "ButtonColor", Color.cyan),
                    native?.GetType().FullName ?? string.Empty));
            }

            inspector = new AutomationAcbControllerInspector(block, summaries);
            return true;
        }

        private static bool TryWriteButtonText(
            AutomationAcbControllerButtonSummary button,
            string fieldName,
            string value,
            string label,
            out string message)
        {
            message = null;
            if (button?.NativeButton == null)
            {
                message = "Select an ACB Controller button first.";
                return false;
            }

            string text = value ?? string.Empty;
            if (text.Length > 80)
            {
                message = "ACB Controller " + label + " is limited to 80 characters.";
                return false;
            }

            if (!WriteValue(button.NativeButton, fieldName, text))
            {
                message = "FtD rejected the ACB Controller " + label + " write.";
                return false;
            }

            message = "ACB Controller " + label + " updated.";
            return true;
        }

        private static string ReadString(object owner, string propertyName, string fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            return value == null ? fallback : value.ToString();
        }

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

        private static int ReadInt(object owner, string propertyName, int fallback)
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

        private static Color ReadColor(object owner, string propertyName, Color fallback)
        {
            object value = ReadUs(ReadMember(owner, propertyName));
            if (TryConvertColor(value, out Color color))
                return color;

            return fallback;
        }

        private static bool TryConvertColor(object value, out Color color)
        {
            color = Color.white;
            if (value == null)
                return false;
            if (value is Color direct)
            {
                color = direct;
                return true;
            }
            if (value is Color32 color32)
            {
                color = color32;
                return true;
            }

            try
            {
                color = new Color(
                    Convert.ToSingle(ReadMember(value, "r"), CultureInfo.InvariantCulture),
                    Convert.ToSingle(ReadMember(value, "g"), CultureInfo.InvariantCulture),
                    Convert.ToSingle(ReadMember(value, "b"), CultureInfo.InvariantCulture),
                    Convert.ToSingle(ReadMember(value, "a"), CultureInfo.InvariantCulture));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool WriteValue(object owner, string propertyName, object value)
        {
            object variable = ReadMember(owner, propertyName);
            return variable != null && WriteUs(variable, value);
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
            if (underlying == typeof(Color32) && value is Color color)
                return (Color32)color;

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

        private static IReadOnlyList<object> FindAllByTypeName(
            object root,
            string typeName,
            int limit)
        {
            var results = new List<object>();
            if (root == null)
                return results;

            var queue = new Queue<SearchNode>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            queue.Enqueue(new SearchNode(root, 0));
            int inspected = 0;
            int max = Math.Max(1, limit);
            while (queue.Count > 0 && inspected < MaxSearchObjects && results.Count < max)
            {
                SearchNode node = queue.Dequeue();
                object current = node.Value;
                if (current == null || !visited.Add(current))
                    continue;

                inspected++;
                Type type = current.GetType();
                if (string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                {
                    results.Add(current);
                    continue;
                }

                if (node.Depth >= MaxSearchDepth || ShouldNotRecurse(type))
                    continue;

                foreach (object child in EnumerateChildren(current))
                    queue.Enqueue(new SearchNode(child, node.Depth + 1));
            }

            return results;
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
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value != null)
                        yield return entry.Value;
                }
            }
            else if (value is IEnumerable enumerable)
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (item != null)
                        yield return item;
                    count++;
                    if (count >= 64)
                        yield break;
                }
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
                    fields.AddRange(current.GetFields(flags | BindingFlags.DeclaredOnly));

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
                    properties.AddRange(current.GetProperties(flags | BindingFlags.DeclaredOnly));

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

    internal sealed class AutomationAcbControllerButtonSummary
    {
        internal AutomationAcbControllerButtonSummary(
            object nativeButton,
            int index,
            string buttonName,
            string keyword,
            int shapeId,
            bool isUsedForBreadboard,
            Color buttonColor,
            string dataTypeName)
        {
            NativeButton = nativeButton;
            Index = index;
            ButtonName = buttonName ?? string.Empty;
            Keyword = keyword ?? string.Empty;
            ShapeId = shapeId;
            IsUsedForBreadboard = isUsedForBreadboard;
            ButtonColor = buttonColor;
            DataTypeName = dataTypeName ?? string.Empty;
        }

        internal object NativeButton { get; }

        internal int Index { get; }

        internal string ButtonName { get; }

        internal string Keyword { get; }

        internal int ShapeId { get; }

        internal bool IsUsedForBreadboard { get; }

        internal Color ButtonColor { get; }

        internal string DataTypeName { get; }
    }
}
