using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BrilliantSkies.Core.Types;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationAcbInspector
    {
        private const int MaxSearchObjects = 160;
        private const int MaxSearchDepth = 5;

        private readonly object _data;
        private readonly object _actionValue;
        private readonly object _conditionValue;

        private AutomationAcbInspector(
            object data,
            object actionValue,
            object conditionValue)
        {
            _data = data;
            _actionValue = actionValue;
            _conditionValue = conditionValue;
        }

        internal bool IsAvailable => _data != null;

        internal string DataTypeName => _data?.GetType().FullName ?? string.Empty;

        internal bool HasRuleData => _actionValue != null || _conditionValue != null;

        internal int ActionTypeId
        {
            get => ReadInt(_actionValue, "DataTypeId", 0);
            set => WriteValue(_actionValue, "DataTypeId", value);
        }

        internal string ActionTypeLabel => EnumLabel("ACB_ActionType, Ftd", ActionTypeId);

        internal string ActionDescription => ReadDescription(_actionValue);

        internal double ActionVariable
        {
            get => ReadDouble(ReadMember(_actionValue, "Variable"), 0d);
            set => WriteUs(ReadMember(_actionValue, "Variable"), value);
        }

        internal int ConditionTypeId
        {
            get => ReadInt(_conditionValue, "DataTypeId", 0);
            set => WriteValue(_conditionValue, "DataTypeId", value);
        }

        internal string ConditionTypeLabel => EnumLabel("ACB_ConditionType, Ftd", ConditionTypeId);

        internal string ConditionDescription => ReadDescription(_conditionValue);

        internal bool ConditionInverted
        {
            get => ReadBool(_conditionValue, "IsInverted", false);
            set => WriteValue(_conditionValue, "IsInverted", value);
        }

        internal double ConditionMinVariable
        {
            get => ReadDouble(ReadMember(_conditionValue, "MinVariable"), 0d);
            set => WriteUs(ReadMember(_conditionValue, "MinVariable"), value);
        }

        internal double ConditionMaxVariable
        {
            get => ReadDouble(ReadMember(_conditionValue, "MaxVariable"), 0d);
            set => WriteUs(ReadMember(_conditionValue, "MaxVariable"), value);
        }

        internal bool IsEnabled
        {
            get => ReadBool("IsEnabled", true);
            set => WriteValue("IsEnabled", value);
        }

        internal int Priority
        {
            get => ReadInt("Priority", 0);
            set => WriteValue("Priority", value);
        }

        internal float AffectDelay
        {
            get => ReadFloat("AffectDelay", 0f);
            set => WriteValue("AffectDelay", value);
        }

        internal int AffectRange
        {
            get => ReadInt("AffectRange", 0);
            set => WriteValue("AffectRange", value);
        }

        internal float MinActivationInterval
        {
            get => ReadFloat("MinActivationInterval", 0f);
            set => WriteValue("MinActivationInterval", value);
        }

        internal string SearchPattern
        {
            get => ReadString("SearchPattern", string.Empty);
            set => WriteValue("SearchPattern", value ?? string.Empty);
        }

        internal bool TryRewriteCurrentSettings(out string message)
        {
            var failures = new List<string>();
            if (!WriteValue("IsEnabled", IsEnabled))
                failures.Add("IsEnabled");
            if (!WriteValue("Priority", Priority))
                failures.Add("Priority");
            if (!WriteValue("AffectDelay", AffectDelay))
                failures.Add("AffectDelay");
            if (!WriteValue("AffectRange", AffectRange))
                failures.Add("AffectRange");
            if (!WriteValue("MinActivationInterval", MinActivationInterval))
                failures.Add("MinActivationInterval");
            if (!WriteValue("SearchPattern", SearchPattern ?? string.Empty))
                failures.Add("SearchPattern");

            if (_actionValue != null)
            {
                if (!WriteValue(_actionValue, "DataTypeId", ActionTypeId))
                    failures.Add("ActionType");
                if (!WriteUs(ReadMember(_actionValue, "Variable"), ActionVariable))
                    failures.Add("ActionVariable");
            }

            if (_conditionValue != null)
            {
                if (!WriteValue(_conditionValue, "DataTypeId", ConditionTypeId))
                    failures.Add("ConditionType");
                if (!WriteValue(_conditionValue, "IsInverted", ConditionInverted))
                    failures.Add("ConditionInverted");
                if (!WriteUs(ReadMember(_conditionValue, "MinVariable"), ConditionMinVariable))
                    failures.Add("ConditionMinVariable");
                if (!WriteUs(ReadMember(_conditionValue, "MaxVariable"), ConditionMaxVariable))
                    failures.Add("ConditionMaxVariable");
            }

            if (failures.Count == 0)
            {
                message = "ACB ControlBlockData and rule writes accepted same-value probes.";
                return true;
            }

            message = "ACB same-value writes failed for " + string.Join(", ", failures.ToArray()) + ".";
            return false;
        }

        internal bool TriggerTestNow()
        {
            object variable = GetVariable("TriggerATestNow");
            if (variable == null)
                return false;

            object current = ReadUs(variable);
            uint next = current == null
                ? 1U
                : Convert.ToUInt32(current, CultureInfo.InvariantCulture) + 1U;
            return WriteUs(variable, next);
        }

        internal bool CycleActionType(int delta)
        {
            if (!TryCycleEnumValue("ACB_ActionType, Ftd", ActionTypeId, delta, out int next))
                return false;

            ActionTypeId = next;
            return true;
        }

        internal bool CycleConditionType(int delta)
        {
            if (!TryCycleEnumValue("ACB_ConditionType, Ftd", ConditionTypeId, delta, out int next))
                return false;

            ConditionTypeId = next;
            return true;
        }

        internal static bool TryCreate(Block block, out AutomationAcbInspector inspector, out string reason)
        {
            inspector = null;
            reason = null;
            if (block == null)
            {
                reason = "No selected ACB block.";
                return false;
            }

            object data = FindControlBlockData(block);
            if (data == null)
            {
                reason = "ControlBlockData package was not found on this ACB instance.";
                return false;
            }

            object actionValue = FindFirstByTypeName(block, "ACB_ActionValue");
            object conditionValue = FindFirstByTypeName(block, "ACB_ConditionValue");
            inspector = new AutomationAcbInspector(data, actionValue, conditionValue);
            return true;
        }

        private bool ReadBool(string propertyName, bool fallback)
        {
            object value = ReadUs(GetVariable(propertyName));
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

        private int ReadInt(string propertyName, int fallback)
        {
            object value = ReadUs(GetVariable(propertyName));
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

        private float ReadFloat(string propertyName, float fallback)
        {
            object value = ReadUs(GetVariable(propertyName));
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

        private string ReadString(string propertyName, string fallback)
        {
            object value = ReadUs(GetVariable(propertyName));
            return value == null ? fallback : value.ToString();
        }

        private bool WriteValue(string propertyName, object value)
        {
            object variable = GetVariable(propertyName);
            return variable != null && WriteUs(variable, value);
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

        private static double ReadDouble(object variable, double fallback)
        {
            object value = ReadUs(variable);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
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

        private object GetVariable(string propertyName)
        {
            if (_data == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            return ReadMember(_data, propertyName);
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

        private static string ReadDescription(object owner)
        {
            string custom = ReadPlainString(owner, "CustomDescription", string.Empty);
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            return ReadPlainString(owner, "Description", string.Empty);
        }

        private static string ReadPlainString(object owner, string memberName, string fallback)
        {
            object value = ReadMember(owner, memberName);
            return value == null ? fallback : value.ToString();
        }

        private static string EnumLabel(string enumAssemblyName, int value)
        {
            Type type = Type.GetType(enumAssemblyName);
            if (type == null || !type.IsEnum)
                return value.ToString(CultureInfo.InvariantCulture);

            string name = Enum.GetName(type, value);
            return string.IsNullOrWhiteSpace(name)
                ? value.ToString(CultureInfo.InvariantCulture)
                : name.Replace('_', ' ');
        }

        private static bool TryCycleEnumValue(
            string enumAssemblyName,
            int current,
            int delta,
            out int next)
        {
            next = current;
            Type type = Type.GetType(enumAssemblyName);
            if (type == null || !type.IsEnum)
                return false;

            var values = new List<int>();
            foreach (object value in Enum.GetValues(type))
            {
                try
                {
                    values.Add(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                }
                catch
                {
                    // Ignore values that do not fit in an int.
                }
            }

            if (values.Count == 0)
                return false;

            values.Sort();
            int index = values.IndexOf(current);
            if (index < 0)
                index = 0;

            int offset = delta < 0 ? -1 : 1;
            int nextIndex = (index + offset + values.Count) % values.Count;
            next = values[nextIndex];
            return true;
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

        private static object FindControlBlockData(object root)
        {
            return FindFirstByTypeName(root, "ControlBlockData");
        }

        private static object FindFirstByTypeName(object root, string typeName)
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
                if (string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                {
                    return current;
                }

                if (node.Depth >= MaxSearchDepth || ShouldNotRecurse(type))
                    continue;

                foreach (object child in EnumerateChildren(current))
                    queue.Enqueue(new SearchNode(child, node.Depth + 1));
            }

            return null;
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
}
