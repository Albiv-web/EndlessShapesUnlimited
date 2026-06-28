using System;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal static class SmartBuildSelectionResolver
    {
        internal static bool TryResolve(
            cBuild build,
            out SmartBuildSource source,
            out string reason)
        {
            source = null;
            reason = null;
            if (build == null)
            {
                reason = "Build mode is not active.";
                return false;
            }

            ItemDefinition item;
            try
            {
                item = build.BuildingWith?.Item;
            }
            catch (Exception exception)
            {
                reason = "Could not read the selected build item: " + exception.Message;
                return false;
            }

            if (item == null)
            {
                reason = "Select a block before using Smart Block Builder.";
                return false;
            }

            try
            {
                if (item.GetBlockClassType() == null)
                {
                    reason = "The selected item is not a placeable block.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "Could not validate the selected block: " + exception.Message;
                return false;
            }

            Guid guid;
            Vector3i dimensions;
            try
            {
                guid = item.ComponentId.Guid;
                dimensions = item.SizeInfo?.Dimensions ?? new Vector3i(1, 1, 1);
                if (dimensions.x < 1 || dimensions.y < 1 || dimensions.z < 1)
                    dimensions = new Vector3i(1, 1, 1);
            }
            catch (Exception exception)
            {
                reason = "Could not read selected block metadata: " + exception.Message;
                return false;
            }

            string displayName = SmartBlockFamilyCatalog.ItemName(item);
            SmartBlockFamily family = SmartBlockFamilyCatalog.FromSelected(
                item,
                guid,
                displayName,
                dimensions);
            source = new SmartBuildSource(item, guid, displayName, dimensions, family);
            if (!family.IsSupported)
            {
                reason = family.UnsupportedReason;
                return false;
            }

            return true;
        }
    }
}
