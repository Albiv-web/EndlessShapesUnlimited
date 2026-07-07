using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;

namespace DecoLimitLifter.AutomationEditMode
{
    internal enum AutomationControllerKind
    {
        Breadboard,
        AiBreadboard,
        Acb,
        AcbController,
        MissileBreadboard
    }

    internal sealed class AutomationControllerDescriptor
    {
        internal AutomationControllerDescriptor(
            AutomationControllerKind kind,
            string label,
            string shortLabel,
            Guid itemGuid,
            string className,
            string description)
        {
            Kind = kind;
            Label = label;
            ShortLabel = shortLabel;
            ItemGuid = itemGuid;
            ClassName = className;
            Description = description;
        }

        internal AutomationControllerKind Kind { get; }

        internal string Label { get; }

        internal string ShortLabel { get; }

        internal Guid ItemGuid { get; }

        internal string ClassName { get; }

        internal string Description { get; }

        internal ItemDefinition ResolveItemDefinition() =>
            AutomationControllerCatalog.ResolveItemDefinition(ItemGuid);
    }

    internal static class AutomationControllerCatalog
    {
        private static readonly AutomationControllerDescriptor[] s_all =
        {
            new AutomationControllerDescriptor(
                AutomationControllerKind.Breadboard,
                "Bread Board",
                "Bread",
                new Guid("7fcfdaf0-2d2a-43be-842a-423e736ccdd0"),
                "BreadBoard",
                "Vehicle circuit block for non-AI automation."),
            new AutomationControllerDescriptor(
                AutomationControllerKind.AiBreadboard,
                "AI Breadboard",
                "AI",
                new Guid("5ef97d26-1196-4b1a-ba1d-fd539c26b684"),
                "AiBreadboard",
                "Mainframe-aware breadboard for AI and target data."),
            new AutomationControllerDescriptor(
                AutomationControllerKind.Acb,
                "Automated Control Block",
                "ACB",
                new Guid("a3d914e9-697d-425f-abda-a6b21b4de952"),
                "ControlBlock",
                "Condition/action automation block."),
            new AutomationControllerDescriptor(
                AutomationControllerKind.AcbController,
                "ACB Controller",
                "ACB Ctrl",
                new Guid("328006a1-123f-432a-b18b-601dd1284247"),
                "AcbController",
                "Button controller that can bridge ACBs and breadboards."),
            new AutomationControllerDescriptor(
                AutomationControllerKind.MissileBreadboard,
                "Missile Breadboard Controller",
                "Missile",
                new Guid("88ec2d62-1b2c-4f0c-b513-6e1a2d772b1f"),
                "MissileBreadboardBlock",
                "Launch vehicle controller for missile breadboard commands.")
        };

        internal static IReadOnlyList<AutomationControllerDescriptor> All => s_all;

        internal static bool TryClassify(Block block, out AutomationControllerDescriptor descriptor)
        {
            descriptor = null;
            if (block == null)
                return false;

            string typeName = RuntimeTypeText(block);
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            foreach (AutomationControllerDescriptor candidate in s_all)
            {
                if (typeName.IndexOf(candidate.ClassName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf(candidate.Kind.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    descriptor = candidate;
                    return true;
                }
            }

            return false;
        }

        internal static string BlockDisplayName(Block block)
        {
            if (block == null)
                return "Block";

            string vanillaName = VanillaBlockNameFilter(block);
            if (!string.IsNullOrWhiteSpace(vanillaName))
            {
                if (TryClassify(block, out AutomationControllerDescriptor namedController))
                    return vanillaName.Trim() + " (" + namedController.ShortLabel + ")";

                return vanillaName.Trim();
            }

            if (TryClassify(block, out AutomationControllerDescriptor controller))
                return controller.Label;

            Type type = block.GetType();
            return string.IsNullOrWhiteSpace(type?.Name) ? "Block" : type.Name;
        }

        internal static string VanillaBlockNameFilter(Block block)
        {
            object idSet = ReadMember(block, "IdSet");
            object name = ReadMember(idSet, "Name");
            object value = ReadUs(name);
            return value?.ToString() ?? string.Empty;
        }

        internal static string RuntimeTypeText(Block block)
        {
            if (block == null)
                return string.Empty;

            Type type = block.GetType();
            return (type.FullName ?? string.Empty) + " " + (type.Name ?? string.Empty);
        }

        internal static ItemDefinition ResolveItemDefinition(Guid guid)
        {
            if (guid == Guid.Empty)
                return null;

            try
            {
                ItemDefinition item = Configured.i
                    .Get<ModificationComponentContainerItem>()
                    .Find(guid, out bool found);
                return found ? item : null;
            }
            catch
            {
                return null;
            }
        }

        internal static IReadOnlyList<string> AvailabilitySummary()
        {
            return s_all
                .Select(descriptor =>
                    descriptor.Label + ": " +
                    (descriptor.ResolveItemDefinition() == null ? "missing" : "available"))
                .ToArray();
        }

        private static object ReadMember(object owner, string memberName)
        {
            if (owner == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(owner, null);
            }
            catch
            {
                // Try the matching field below.
            }

            try
            {
                FieldInfo field = type.GetField(memberName, flags);
                return field?.GetValue(owner);
            }
            catch
            {
                return null;
            }
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
    }
}
