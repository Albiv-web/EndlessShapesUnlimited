using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal enum AutomationBreadboardVariant
    {
        Ai,
        Basic
    }

    internal static class AutomationBreadboardCatalog
    {
        private static readonly string[] BreadboardNameTokens =
        {
            "breadboard",
            "bread board",
            "generic block setter",
            "generic block getter"
        };

        private static ItemDefinition _cachedAiBreadboard;
        private static ItemDefinition _cachedBasicBreadboard;
        private static ItemDefinition _cachedFallbackBreadboard;
        private static string _cachedAiResolveMessage;
        private static string _cachedBasicResolveMessage;
        private static int _cacheFrame = -1;

        internal static bool TryResolveBreadboard(
            out ItemDefinition definition,
            out string message) =>
            TryResolveBreadboard(AutomationBreadboardVariant.Ai, out definition, out message);

        internal static bool TryResolveBreadboard(
            AutomationBreadboardVariant variant,
            out ItemDefinition definition,
            out string message)
        {
            RefreshBreadboardCache();
            definition = variant == AutomationBreadboardVariant.Basic
                ? _cachedBasicBreadboard
                : _cachedAiBreadboard;
            message = variant == AutomationBreadboardVariant.Basic
                ? _cachedBasicResolveMessage
                : _cachedAiResolveMessage;
            return definition != null;
        }

        private static void RefreshBreadboardCache()
        {
            if (_cacheFrame == UnityEngine.Time.frameCount)
                return;

            _cacheFrame = UnityEngine.Time.frameCount;
            _cachedAiBreadboard = null;
            _cachedBasicBreadboard = null;
            _cachedFallbackBreadboard = null;
            _cachedAiResolveMessage = null;
            _cachedBasicResolveMessage = null;

            List<ItemDefinition> breadboards = LoadedItemDefinitions()
                .Where(IsBreadboardDefinition)
                .ToList();
            _cachedFallbackBreadboard = breadboards.FirstOrDefault();
            _cachedAiBreadboard =
                breadboards.FirstOrDefault(IsAiBreadboardDefinition) ??
                _cachedFallbackBreadboard;
            _cachedBasicBreadboard =
                breadboards.FirstOrDefault(item => !IsAiBreadboardDefinition(item)) ??
                _cachedFallbackBreadboard;

            _cachedAiResolveMessage = ResolveMessage(AutomationBreadboardVariant.Ai, _cachedAiBreadboard);
            _cachedBasicResolveMessage = ResolveMessage(AutomationBreadboardVariant.Basic, _cachedBasicBreadboard);
        }

        private static string ResolveMessage(
            AutomationBreadboardVariant variant,
            ItemDefinition item)
        {
            string label = VariantLabel(variant);
            if (item == null)
                return label + " was not found in the loaded item definitions.";

            string suffix = variant == AutomationBreadboardVariant.Basic &&
                            IsAiBreadboardDefinition(item)
                ? " (AI fallback)"
                : string.Empty;
            return label + " resolved: " + ItemName(item) + suffix + ".";
        }

        private static string VariantLabel(AutomationBreadboardVariant variant) =>
            variant == AutomationBreadboardVariant.Basic
                ? "Basic breadboard"
                : "AI breadboard";

        private static IEnumerable<ItemDefinition> CachedBreadboards()
        {
            RefreshBreadboardCache();
            var seen = new HashSet<Guid>();
            foreach (ItemDefinition item in new[]
                     {
                         _cachedAiBreadboard,
                         _cachedBasicBreadboard,
                         _cachedFallbackBreadboard
                     })
            {
                if (item == null)
                    continue;

                Guid guid = ComponentGuid(item);
                if (guid != Guid.Empty && !seen.Add(guid))
                    continue;

                yield return item;
            }
        }

        internal static bool IsBreadboardBlock(Block block)
        {
            if (block == null || block.IsDeleted)
                return false;

            try
            {
                if (block.item != null &&
                    CachedBreadboards().Any(definition =>
                        definition != null &&
                        block.item.ComponentId.Guid == definition.ComponentId.Guid))
                {
                    return true;
                }
            }
            catch
            {
                // Name fallback below handles unusual item definitions.
            }

            string text = (BlockName(block) + " " + BlockComponentText(block))
                .Replace("_", " ")
                .Replace("-", " ")
                .ToLowerInvariant();
            return BreadboardNameTokens.Any(token => text.Contains(token));
        }

        internal static string ItemName(ItemDefinition item)
        {
            if (item == null)
                return "Breadboard";

            try
            {
                string name = item.GetInventoryNameConsideringVariants();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Fall through to the next source.
            }

            try
            {
                string name = item.GetInventoryName();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Fall through to the component id.
            }

            try
            {
                return item.ComponentId.Guid.ToString();
            }
            catch
            {
                return "Breadboard";
            }
        }

        internal static string BlockName(Block block)
        {
            if (block == null)
                return "Block";

            try
            {
                string customName = block.IdSet?.Name;
                if (!string.IsNullOrWhiteSpace(customName))
                    return customName;
            }
            catch
            {
                // Fall through to the item display name.
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(block.Name))
                    return block.Name;
            }
            catch
            {
                // Fall through to the item name.
            }

            return ItemName(block.item);
        }

        private static bool IsBreadboardDefinition(ItemDefinition item)
        {
            if (item == null)
                return false;

            string text = DefinitionSearchText(item);
            return BreadboardNameTokens.Any(token => text.Contains(token));
        }

        private static bool IsAiBreadboardDefinition(ItemDefinition item)
        {
            string text = " " + DefinitionSearchText(item) + " ";
            return text.Contains(" ai breadboard ") ||
                   text.Contains(" ai bread board ") ||
                   text.Contains(" artificial intelligence ") ||
                   (text.Contains(" breadboard ") && text.Contains(" ai "));
        }

        private static string DefinitionSearchText(ItemDefinition item) =>
            (ItemName(item) + " " + ComponentText(item))
            .Replace("_", " ")
            .Replace("-", " ")
            .ToLowerInvariant();

        private static IEnumerable<ItemDefinition> LoadedItemDefinitions()
        {
            var results = new List<ItemDefinition>();
            var seen = new HashSet<Guid>();
            try
            {
                ModificationComponentContainerItem container = Configured.i
                    .Get<ModificationComponentContainerItem>();
                AddItemDefinitions(container?.Components, results, seen);
                FieldInfo field = typeof(ModificationComponentContainerItem)
                    .GetField(
                        "m_AllCorrespondingItems",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(container) is IEnumerable<ItemDefinition> items)
                    AddItemDefinitions(items, results, seen);
            }
            catch
            {
                // The caller reports a friendly resolver failure.
            }

            return results.ToArray();
        }

        private static void AddItemDefinitions(
            IEnumerable<ItemDefinition> items,
            ICollection<ItemDefinition> results,
            ISet<Guid> seen)
        {
            if (items == null)
                return;

            foreach (ItemDefinition item in items)
            {
                if (item == null)
                    continue;

                Guid guid = ComponentGuid(item);
                if (guid != Guid.Empty && !seen.Add(guid))
                    continue;

                results.Add(item);
            }
        }

        private static Guid ComponentGuid(ItemDefinition item)
        {
            try
            {
                return item?.ComponentId.Guid ?? Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        private static string ComponentText(ItemDefinition item)
        {
            try
            {
                object componentId = item?.ComponentId;
                if (componentId == null)
                    return string.Empty;

                string reflectedName = componentId
                    .GetType()
                    .GetProperty(
                        "Name",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(componentId, null) as string;
                if (!string.IsNullOrWhiteSpace(reflectedName))
                    return reflectedName;

                return item.ComponentId.Guid.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BlockComponentText(Block block)
        {
            try
            {
                object componentId = block?.item?.ComponentId;
                if (componentId == null)
                    return string.Empty;

                string reflectedName = componentId
                    .GetType()
                    .GetProperty(
                        "Name",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(componentId, null) as string;
                if (!string.IsNullOrWhiteSpace(reflectedName))
                    return reflectedName;

                return block.item.ComponentId.Guid.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
