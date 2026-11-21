namespace Importer.Readers;

/// <summary>
///		Reads Daggerfall's .COL palettes, such as ART_PAL.COL, which every TEXTURE.xxx image uses.
/// </summary>
/// <remarks>
///		Daggerfall images store one byte per pixel: an index into a 256-color palette.
///		A .COL file is 256 × (R, G, B) bytes, each 0-255, usually after an 8-byte header (776 bytes in all).
///		Some palettes have no header (768 bytes).
/// </remarks>
public static class PaletteReader
{
	private const int Colors = 256;
	private const int BodySize = Colors * 3;
	private const int HeaderSize = 8;

	/// <summary>
	///		Returns the palette as 256 packed RGBA colors (R in the lowest byte), ready to write into an RGBA image.
	///		Index 0 is transparent: Daggerfall uses it for "no pixel here".
	/// </summary>
	/// <exception cref="InvalidDataException">The file is neither 768 nor 776 bytes.</exception>
	public static uint[] Read(byte[] data)
	{
		var start = data.Length switch
		{
			BodySize => 0,
			BodySize + HeaderSize => HeaderSize,
			_ => throw new InvalidDataException($"A palette is {BodySize} or {BodySize + HeaderSize} bytes, not {data.Length}."),
		};

		var colors = new uint[Colors];
		for (var i = 0; i < Colors; i++)
		{
			var r = data[start + i * 3];
			var g = data[start + i * 3 + 1];
			var b = data[start + i * 3 + 2];
			var a = i == 0 ? 0u : 255u;
			colors[i] = r | (uint) g << 8 | (uint) b << 16 | a << 24;
		}

		return colors;
	}
}
