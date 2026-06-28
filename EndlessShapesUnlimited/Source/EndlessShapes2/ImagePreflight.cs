using System;
using System.IO;

namespace EndlessShapes2
{
    internal readonly struct ImageDimensions
    {
        internal ImageDimensions(int width, int height)
        {
            Width = width;
            Height = height;
        }

        internal int Width { get; }

        internal int Height { get; }
    }

    internal static class ImagePreflight
    {
        internal const int MaxDimension = 8_192;
        internal const long MaxPixels = 16_777_216L;

        internal static ImageDimensions ReadAndValidate(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ImageDimensions dimensions = ReadDimensions(stream);
                Validate(dimensions);
                return dimensions;
            }
        }

        internal static void Validate(ImageDimensions dimensions)
        {
            if (dimensions.Width <= 0 || dimensions.Height <= 0)
                throw new InvalidDataException("Texture dimensions must be positive.");
            if (dimensions.Width > MaxDimension || dimensions.Height > MaxDimension)
            {
                throw new InvalidDataException(
                    $"Texture is {dimensions.Width}x{dimensions.Height}; each dimension is limited to {MaxDimension:N0} pixels.");
            }
            long pixels = (long)dimensions.Width * dimensions.Height;
            if (pixels > MaxPixels)
            {
                throw new InvalidDataException(
                    $"Texture contains {pixels:N0} pixels; the limit is {MaxPixels:N0} pixels.");
            }
        }

        internal static ImageDimensions ReadDimensions(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("Texture stream must be readable.", nameof(stream));
            if (!stream.CanSeek)
                throw new ArgumentException("Texture stream must support seeking.", nameof(stream));

            int first = stream.ReadByte();
            int second = stream.ReadByte();
            if (first == 0x89 && second == 0x50)
                return ReadPng(stream);
            if (first == 0xff && second == 0xd8)
                return ReadJpeg(stream);
            throw new InvalidDataException("Texture must be a PNG or JPEG file.");
        }

        private static ImageDimensions ReadPng(Stream stream)
        {
            byte[] restOfSignatureAndHeader = ReadExact(stream, 22);
            byte[] expected = { 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            for (int index = 0; index < expected.Length; index++)
            {
                if (restOfSignatureAndHeader[index] != expected[index])
                    throw new InvalidDataException("Texture has an invalid PNG signature.");
            }

            if (restOfSignatureAndHeader[6] != 0 ||
                restOfSignatureAndHeader[7] != 0 ||
                restOfSignatureAndHeader[8] != 0 ||
                restOfSignatureAndHeader[9] != 13)
            {
                throw new InvalidDataException("PNG texture has an invalid IHDR length.");
            }

            if (restOfSignatureAndHeader[10] != 0x49 ||
                restOfSignatureAndHeader[11] != 0x48 ||
                restOfSignatureAndHeader[12] != 0x44 ||
                restOfSignatureAndHeader[13] != 0x52)
            {
                throw new InvalidDataException("PNG texture does not begin with an IHDR chunk.");
            }

            int width = ReadBigEndianInt32(restOfSignatureAndHeader, 14);
            int height = ReadBigEndianInt32(restOfSignatureAndHeader, 18);
            return new ImageDimensions(width, height);
        }

        private static ImageDimensions ReadJpeg(Stream stream)
        {
            while (stream.Position < stream.Length)
            {
                int prefix;
                do
                {
                    prefix = stream.ReadByte();
                } while (prefix != -1 && prefix != 0xff);

                if (prefix == -1)
                    break;

                int marker;
                do
                {
                    marker = stream.ReadByte();
                } while (marker == 0xff);

                if (marker == -1 || marker == 0xda || marker == 0xd9)
                    break;
                if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7)
                    continue;

                int length = ReadBigEndianUInt16(stream);
                if (length < 2)
                    throw new InvalidDataException("JPEG contains an invalid marker length.");

                if (IsStartOfFrame(marker))
                {
                    if (length < 7)
                        throw new InvalidDataException("JPEG start-of-frame marker is truncated.");
                    ReadRequiredByte(stream);
                    int height = ReadBigEndianUInt16(stream);
                    int width = ReadBigEndianUInt16(stream);
                    return new ImageDimensions(width, height);
                }

                long next = stream.Position + length - 2L;
                if (next > stream.Length)
                    throw new InvalidDataException("JPEG marker extends beyond the file.");
                stream.Position = next;
            }

            throw new InvalidDataException("JPEG texture has no supported start-of-frame marker.");
        }

        private static bool IsStartOfFrame(int marker)
        {
            return marker >= 0xc0 && marker <= 0xcf &&
                   marker != 0xc4 && marker != 0xc8 && marker != 0xcc;
        }

        private static int ReadBigEndianUInt16(Stream stream)
        {
            return ReadRequiredByte(stream) << 8 | ReadRequiredByte(stream);
        }

        private static int ReadRequiredByte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0)
                throw new EndOfStreamException("Texture header is truncated.");
            return value;
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException("Texture header is truncated.");
                offset += read;
            }
            return buffer;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            uint value = (uint)(bytes[offset] << 24 |
                                bytes[offset + 1] << 16 |
                                bytes[offset + 2] << 8 |
                                bytes[offset + 3]);
            if (value > int.MaxValue)
                throw new InvalidDataException("Texture dimension exceeds the supported integer range.");
            return (int)value;
        }
    }
}
