using System;

namespace DecoLimitLifter.Patches
{
    internal static class BufferGrowth
    {
        internal static byte[] GrowPreserving(
            byte[] current,
            int required,
            int maximum,
            int bytesToPreserve,
            string bufferName,
            bool exact = false)
        {
            if (required < 0)
                throw new ArgumentOutOfRangeException(nameof(required));
            if (required > maximum)
                throw new InvalidOperationException(
                    $"{bufferName} requires {required} bytes, above the configured maximum of {maximum} bytes.");

            int currentLength = current?.Length ?? 0;
            if (bytesToPreserve < 0 || bytesToPreserve > currentLength)
                throw new InvalidOperationException(
                    $"{bufferName} state is inconsistent: asked to preserve {bytesToPreserve} bytes from a {currentLength}-byte array.");

            if (currentLength >= required)
                return current;

            int target = exact ? required : CapacityFor(required, maximum);
            var grown = new byte[target];
            if (bytesToPreserve > 0)
                Buffer.BlockCopy(current, 0, grown, 0, bytesToPreserve);
            return grown;
        }

        internal static int CapacityFor(int required, int maximum)
        {
            if (required < 0)
                throw new ArgumentOutOfRangeException(nameof(required));
            if (required > maximum)
                throw new InvalidOperationException(
                    $"Required capacity {required} exceeds the configured maximum of {maximum} bytes.");
            if (required <= 1)
                return 1;

            int capacity = 1;
            while (capacity < required)
            {
                if (capacity > maximum / 2)
                {
                    capacity = maximum;
                    break;
                }
                capacity <<= 1;
            }

            if (capacity < required)
                throw new InvalidOperationException(
                    $"Could not allocate {required} bytes within the configured maximum of {maximum} bytes.");
            return capacity;
        }
    }
}
