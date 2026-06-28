namespace DecoLimitLifter
{
    /// Central limits used by the serializer and Harmony patches.
    internal static class DecoLimits
    {
        public const int VanillaDataSortedBytes = 2_000_000;
        public const int VanillaHeaderBytes = 70_000;
        public const int VanillaSaveBufferBytes = 10_000_000;

        public const int MaxDataSortedBytes = 64 * 1024 * 1024;
        public const int MaxHeaderBytes = 4 * 1024 * 1024;

        // FtD starts ByteStore at 10,000,000 bytes. The mod starts at
        // 20,000,000 and grows known output buffers from exact required sizes.
        public const int InitialSaveBufferBytes = 20_000_000;
        public const int MaxSaveBufferBytes = 256 * 1024 * 1024;

        // Mutable so advanced users can raise the initial allocation without
        // changing the hard safety ceiling.
        public static int SaveBufferBytes = InitialSaveBufferBytes;

        public const int MaxDecorations = 100_000;
    }
}
