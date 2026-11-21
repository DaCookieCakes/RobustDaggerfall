using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Importer.Conversion;

/// <summary>
///		Encodes RGBA pixels as a PNG file. Just enough of the format for textures: 8-bit RGBA, no filtering.
/// </summary>
/// <remarks>
///		A PNG is an 8-byte signature followed by chunks. Each chunk is
///		<c>[UInt32 length][4-letter type][data][UInt32 CRC of type + data]</c>, numbers big-endian.
///		Three chunks are enough:
///		<list type="bullet">
///			<item>IHDR: width, height and pixel format.</item>
///			<item>IDAT: the pixels, zlib-compressed. Each row starts with a filter byte; 0 means "stored as is".</item>
///			<item>IEND: marks the end.</item>
///		</list>
/// </remarks>
public static class PngWriter
{
	private static readonly byte[] Signature = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];

	private const byte BitDepth = 8;
	private const byte ColorTypeRgba = 6;

	/// <param name="pixels">Width × Height colors, rows top to bottom, packed as R in the lowest byte, A in the highest.</param>
	public static byte[] Encode(int width, int height, uint[] pixels)
	{
		if (pixels.Length != width * height)
			throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.");

		using var png = new MemoryStream();
		png.Write(Signature);

		// IHDR: width, height, bit depth, color type, then compression, filter and interlace methods (all 0 = the default).
		var header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
		header[8] = BitDepth;
		header[9] = ColorTypeRgba;
		WriteChunk(png, "IHDR", header);

		// IDAT: each row is a 0 filter byte, then its pixels as R, G, B, A bytes.
		using (var compressed = new MemoryStream())
		{
			using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
			{
				var row = new byte[1 + width * 4];
				for (var y = 0; y < height; y++)
				{
					for (var x = 0; x < width; x++)
						BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(1 + x * 4), pixels[y * width + x]);
					zlib.Write(row);
				}
			}

			WriteChunk(png, "IDAT", compressed.ToArray());
		}

		WriteChunk(png, "IEND", []);
		return png.ToArray();
	}

	private static void WriteChunk(Stream stream, string type, byte[] data)
	{
		Span<byte> number = stackalloc byte[4];
		var typeBytes = Encoding.ASCII.GetBytes(type);

		BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
		stream.Write(number);
		stream.Write(typeBytes);
		stream.Write(data);

		// The checksum covers the type and the data, not the length.
		var crc = Crc32.Update(Crc32.Start, typeBytes);
		crc = Crc32.Update(crc, data);
		BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Finish(crc));
		stream.Write(number);
	}

	/// <summary>
	///		The CRC-32 checksum PNG uses (the same one zip uses). Table-driven: one lookup per byte.
	/// </summary>
	private static class Crc32
	{
		public const uint Start = 0xFFFFFFFF;

		private static readonly uint[] Table = BuildTable();

		public static uint Update(uint crc, ReadOnlySpan<byte> bytes)
		{
			foreach (var b in bytes)
				crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
			return crc;
		}

		public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;

		private static uint[] BuildTable()
		{
			// 0xEDB88320 is the standard CRC-32 polynomial, bit-reversed.
			var table = new uint[256];
			for (uint n = 0; n < 256; n++)
			{
				var c = n;
				for (var k = 0; k < 8; k++)
					c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
				table[n] = c;
			}

			return table;
		}
	}
}
