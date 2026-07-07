using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal sealed class AutomationTargetPreviewRenderer : IDisposable
    {
        private readonly SmartBlockItemPreviewRenderer _renderer = new SmartBlockItemPreviewRenderer();
        private readonly Dictionary<Type, ItemDefinition> _definitionsByBlockType =
            new Dictionary<Type, ItemDefinition>();
        private bool _scannedDefinitions;

        internal Texture GetPreview(AutomationTarget target, int size, float spin)
        {
            ItemDefinition definition = ResolveDefinition(target);
            if (definition == null)
                return null;

            var candidate = new SmartBlockCandidate(target.Label, 1, definition);
            return _renderer.GetPreview(candidate, size, spin);
        }

        public void Dispose()
        {
            _renderer.Dispose();
            _definitionsByBlockType.Clear();
        }

        private ItemDefinition ResolveDefinition(AutomationTarget target)
        {
            if (target?.Controller != null)
                return target.Controller.ResolveItemDefinition();

            Type blockType = target?.Block?.GetType();
            if (blockType == null)
                return null;

            EnsureDefinitionScan();
            _definitionsByBlockType.TryGetValue(blockType, out ItemDefinition definition);
            return definition;
        }

        private void EnsureDefinitionScan()
        {
            if (_scannedDefinitions)
                return;

            _scannedDefinitions = true;
            foreach (ItemDefinition item in LoadedItemDefinitions())
            {
                Type blockType = null;
                try
                {
                    blockType = item?.GetBlockClassType();
                }
                catch
                {
                    blockType = null;
                }

                if (blockType == null || _definitionsByBlockType.ContainsKey(blockType))
                    continue;

                _definitionsByBlockType[blockType] = item;
            }
        }

        private static IEnumerable<ItemDefinition> LoadedItemDefinitions()
        {
            object container = null;
            try
            {
                container = Configured.i.Get<ModificationComponentContainerItem>();
            }
            catch
            {
                yield break;
            }

            foreach (ItemDefinition item in EnumerateItemDefinitions(ReadMember(container, "Components")))
                yield return item;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo field in container.GetType().GetFields(flags))
            {
                foreach (ItemDefinition item in EnumerateItemDefinitions(field.GetValue(container)))
                    yield return item;
            }

            foreach (PropertyInfo property in container.GetType().GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                object value = null;
                try
                {
                    value = property.GetValue(container, null);
                }
                catch
                {
                    value = null;
                }

                foreach (ItemDefinition item in EnumerateItemDefinitions(value))
                    yield return item;
            }
        }

        private static IEnumerable<ItemDefinition> EnumerateItemDefinitions(object value)
        {
            if (value is ItemDefinition item)
            {
                yield return item;
                yield break;
            }

            if (!(value is IEnumerable enumerable))
                yield break;

            foreach (object entry in enumerable)
            {
                if (entry is ItemDefinition definition)
                    yield return definition;
            }
        }

        private static object ReadMember(object owner, string memberName)
        {
            if (owner == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = owner.GetType();
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(owner, null);
                }
                catch
                {
                    return null;
                }
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field == null)
                return null;

            try
            {
                return field.GetValue(owner);
            }
            catch
            {
                return null;
            }
        }
    }
}
