namespace Importer.Readers;

/// <summary>
///		One image from a TEXTURE.xxx file, as palette indices. Animated textures have several frames.
/// </summary>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
/// <param name="Frames">One Width × Height array of palette indices per frame, rows top to bottom.</param>
public sealed record DfTexture(int Width, int Height, byte[][] Frames);

/// <summary>
///		Reads Daggerfall's TEXTURE.xxx files. Each file is one "archive" of related images, such as one wall style.
/// </summary>
/// <remarks>
///		Layout, from DFU's TextureFile.cs, checked by decoding samples of every storage type:
///		<code>
///			[Int16 record count][24-byte name]
///			[directory at byte 26: record count × 20 bytes]
///			    Int16 unknown, Int32 record position (from file start), Int16 + Int32 + Int64 unknown
///			[each record, at its position P, starts with a 28-byte header]
///			     0 Int16 x offset, 2 Int16 y offset, 4 Int16 width, 6 Int16 height
///			     8 UInt16 compression
///			    10 UInt32 record size (not reliable)
///			    14 UInt32 data offset (from P)
///			    18 UInt16 unknown, 20 UInt16 frame count, 22-27 unknown and scale
///		</code>
///		Pixel data is stored one of three ways:
///		<list type="bullet">
///			<item>
///				Row-table RLE (compression 0x0108 or 0x1108): at P + data offset, per frame, a table of
///				height × (Int16 row offset from P, UInt16 encoding). Encoding 0x8000 means run-length encoded, 0 means raw.
///			</item>
///			<item>Animated, not RLE (frame count &gt; 1): a table of Int32 frame offsets, each frame a sparse format.</item>
///			<item>Anything else: plain bytes, but every row starts 256 bytes after the last, whatever the width.</item>
///		</list>
/// </remarks>
public static class TextureReader
{
	private const int DirectoryStart = 26;
	private const int DirectoryEntrySize = 20;
	private const int RecordHeaderSize = 28;
	private const int UncompressedRowStride = 256;

	private const ushort ImageRle = 0x0108;
	private const ushort RecordRle = 0x1108;
	private const ushort RleRow = 0x8000;

	/// <summary>
	///		Size of a generated solid-color texture. Any size would do; 32 is what DFU uses.
	/// </summary>
	private const int SolidColorSize = 32;

	/// <summary>
	///		TEXTURE.000 and TEXTURE.001 aren't images. A face using them is one flat palette color, picked by the
	///		record number: record N of archive 0 is palette entry N, record N of archive 1 is entry 128 + N.
	///		Their files hold nothing useful (mostly empty slots), so they're never read. From DFU.
	/// </summary>
	public static bool IsSolidColor(int archive) => archive is 0 or 1;

	/// <summary>
	///		Makes the flat-colored texture a face in archive 0 or 1 stands for. See <see cref="IsSolidColor"/>.
	/// </summary>
	public static DfTexture SolidColor(int archive, int record)
	{
		var paletteIndex = (byte) (archive * 128 + record);
		var pixels = new byte[SolidColorSize * SolidColorSize];
		Array.Fill(pixels, paletteIndex);
		return new DfTexture(SolidColorSize, SolidColorSize, [pixels]);
	}

	/// <summary>
	///		Reads every image in a TEXTURE file. Empty directory slots come back as null, so indices still match records.
	/// </summary>
	/// <exception cref="InvalidDataException">The file or one of its images is damaged.</exception>
	public static DfTexture?[] Read(byte[] data)
	{
		if (data.Length < DirectoryStart)
			throw new InvalidDataException("File is too small to be a TEXTURE file.");

		var count = BitConverter.ToInt16(data, 0);
		if (count < 0 || DirectoryStart + count * DirectoryEntrySize > data.Length)
			throw new InvalidDataException($"TEXTURE file claims {count} records, which don't fit.");

		var textures = new DfTexture?[count];
		for (var i = 0; i < count; i++)
		{
			var position = BitConverter.ToInt32(data, DirectoryStart + i * DirectoryEntrySize + 2);

			// Unused slots point at 0, or (twice in the whole game) somewhere inside the directory.
			if (position < DirectoryStart + count * DirectoryEntrySize || position + RecordHeaderSize > data.Length)
				continue;

			try
			{
				textures[i] = ReadRecord(data, position);
			}
			catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
			{
				// Bad offsets inside the image show up as out-of-range reads. Report them as damaged data instead.
				throw new InvalidDataException($"Record {i} runs past the end of the file.", e);
			}
		}

		return textures;
	}

	private static DfTexture? ReadRecord(byte[] data, int p)
	{
		var width = BitConverter.ToInt16(data, p + 4);
		var height = BitConverter.ToInt16(data, p + 6);
		var compression = BitConverter.ToUInt16(data, p + 8);
		var dataStart = p + (int) BitConverter.ToUInt32(data, p + 14);
		var frameCount = BitConverter.ToUInt16(data, p + 20);

		// A handful of records have no size or no frames: nothing to draw.
		if (width <= 0 || height <= 0 || frameCount == 0)
			return null;

		var frames = new byte[frameCount][];
		for (var f = 0; f < frameCount; f++)
		{
			frames[f] = compression is ImageRle or RecordRle
				? ReadRleFrame(data, p, dataStart + height * 4 * f, width, height)
				: frameCount > 1
					? ReadSparseFrame(data, dataStart, f, width, height)
					: ReadUncompressed(data, dataStart, width, height);
		}

		return new DfTexture(width, height, frames);
	}

	/// <summary>
	///		Plain pixels, but rows are 256 bytes apart: a 64-pixel row is followed by 192 unused bytes.
	/// </summary>
	private static byte[] ReadUncompressed(byte[] data, int start, int width, int height)
	{
		var pixels = new byte[width * height];
		for (var y = 0; y < height; y++)
			Array.Copy(data, start + y * UncompressedRowStride, pixels, y * width, width);

		return pixels;
	}

	/// <summary>
	///		One frame of a row-table RLE image. Each row is either raw bytes, or a run-length encoded list of:
	///		negative Int16 n then one byte (repeat it -n times), or positive Int16 n then n literal bytes.
	/// </summary>
	/// <param name="p">The record start; row offsets count from here.</param>
	/// <param name="table">Where this frame's row table starts.</param>
	private static byte[] ReadRleFrame(byte[] data, int p, int table, int width, int height)
	{
		var pixels = new byte[width * height];

		for (var y = 0; y < height; y++)
		{
			var pos = p + BitConverter.ToInt16(data, table + y * 4);
			var encoding = BitConverter.ToUInt16(data, table + y * 4 + 2);
			var row = y * width;

			if (encoding != RleRow)
			{
				Array.Copy(data, pos, pixels, row, width);
				continue;
			}

			// The row starts with how many pixels it decodes to.
			var rowWidth = Math.Min(BitConverter.ToUInt16(data, pos), width);
			pos += 2;

			var x = 0;
			while (x < rowWidth)
			{
				var n = BitConverter.ToInt16(data, pos);
				pos += 2;

				if (n < 0)
				{
					// A run: one byte, repeated. Clamped so a bad count can't spill into the next row.
					var run = Math.Min(-n, rowWidth - x);
					Array.Fill(pixels, data[pos++], row + x, run);
					x += run;
				}
				else if (n > 0)
				{
					var run = Math.Min(n, rowWidth - x);
					Array.Copy(data, pos, pixels, row + x, run);
					pos += n;
					x += run;
				}
				else
				{
					// A zero count makes no progress. DFU loops on it; stopping the row is safer.
					break;
				}
			}
		}

		return pixels;
	}

	/// <summary>
	///		One frame of an animated, non-RLE texture. A table of Int32 frame offsets (from <paramref name="start"/>)
	///		leads to each frame: Int16 width, Int16 height, then per row pairs of
	///		(byte transparent pixels to skip, byte count, count literal bytes) until the row is full.
	/// </summary>
	private static byte[] ReadSparseFrame(byte[] data, int start, int frame, int width, int height)
	{
		var pixels = new byte[width * height];
		var pos = start + BitConverter.ToInt32(data, start + frame * 4);

		// The frame repeats its own size; the record's is what the rest of the game uses.
		pos += 4;

		for (var y = 0; y < height; y++)
		{
			var row = y * width;
			var x = 0;
			while (x < width)
			{
				// Skipped pixels stay 0, which is transparent.
				x += data[pos++];

				var count = data[pos++];
				var copy = Math.Clamp(width - x, 0, count);
				Array.Copy(data, pos, pixels, row + Math.Min(x, width), copy);
				pos += count;
				x += count;
			}
		}

		return pixels;
	}
}
