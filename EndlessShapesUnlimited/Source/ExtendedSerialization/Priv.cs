using System;
using System.Reflection;
using BrilliantSkies.DataManagement.Serialisation;

namespace DecoLimitLifter.ExtendedSerialization
{
    internal static class Priv
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly PropertyInfo HeaderCountProperty =
            RequireProperty(typeof(SuperBase), "HeaderCount");
        private static readonly FieldInfo ReaderLengthField =
            RequireField(typeof(SuperLoader), "_readerLengthOfSortedSegment");
        private static readonly FieldInfo TotalDataLengthField =
            RequireField(typeof(SuperLoader), "_totalDataLengthSorted");
        private static readonly FieldInfo DataCursorField =
            RequireField(typeof(SuperLoader), "_datasWrittenSorted");

        internal static uint GetHeaderCount(SuperLoader loader) =>
            (uint)HeaderCountProperty.GetValue(loader, null);

        internal static void SetHeaderCount(SuperLoader loader, uint value) =>
            HeaderCountProperty.SetValue(loader, value, null);

        internal static uint GetReaderLength(SuperLoader loader) =>
            (uint)ReaderLengthField.GetValue(loader);

        internal static void SetReaderLength(SuperLoader loader, uint value) =>
            ReaderLengthField.SetValue(loader, value);

        internal static uint GetTotalDataLength(SuperLoader loader) =>
            (uint)TotalDataLengthField.GetValue(loader);

        internal static void SetTotalDataLength(SuperLoader loader, uint value) =>
            TotalDataLengthField.SetValue(loader, value);

        internal static uint GetDataCursor(SuperLoader loader) =>
            (uint)DataCursorField.GetValue(loader);

        internal static void SetDataCursor(SuperLoader loader, uint value) =>
            DataCursorField.SetValue(loader, value);

        private static PropertyInfo RequireProperty(Type type, string name) =>
            type.GetProperty(name, InstanceFlags) ??
            throw new MissingMemberException(type.FullName, name);

        private static FieldInfo RequireField(Type type, string name) =>
            type.GetField(name, InstanceFlags) ??
            throw new MissingFieldException(type.FullName, name);
    }
}
