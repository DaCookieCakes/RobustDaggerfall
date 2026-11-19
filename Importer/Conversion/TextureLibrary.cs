using Importer.Readers;

namespace Importer.Conversion;

/// <summary>
///		A texture as colors, ready for a PNG or the GPU.
/// </summary>
/// <param name="Pixels">Width × Height colors, rows top to bottom, R in the lowest byte, A in the highest.</param>
/// <param name="HasTransparency">Whether any pixel is see-through, so a renderer knows whether to cut holes.</param>
public sealed record RgbaImage(int Width, int Height, uint[] Pixels, bool HasTransparency);

/// <summary>
///		Every Daggerfall model texture, looked up by (archive, record) and turned into colors.
/// </summary>
/// <remarks>
///		Loads each TEXTURE.xxx from the pack the first time it's asked for. Archives 0 and 1 are generated instead,
///		see <see cref="TextureReader.IsSolidColor"/>. Only the first frame of animated textures is used for now.
/// </remarks>
public sealed class TextureLibrary
{
	private readonly Pack _pack;
	private readonly uint[] _palette;

	/// <summary>Decoded TEXTURE files by archive number. Null if the file doesn't exist or failed to decode.</summary>
	private readonly Dictionary<int, DfTexture?[]?> _archives = new();

	public TextureLibrary(Pack pack)
	{
		_pack = pack;
		_palette = PaletteReader.Read(pack.Read("ART_PAL.COL")
		                              ?? throw new FileNotFoundException("ART_PAL.COL is not in the pack."));
	}

	/// <summary>
	///		The texture's size in pixels, or null if it doesn't exist. <see cref="MeshConverter"/> needs it for UVs.
	/// </summary>
	public (int Width, int Height)? Size(int archive, int record)
	{
		return Find(archive, record) is { } t ? (t.Width, t.Height) : null;
	}

	/// <summary>
	///		The texture's first frame as colors, or null if it doesn't exist.
	/// </summary>
	public RgbaImage? Rgba(int archive, int record)
	{
		if (Find(archive, record) is not { } texture)
			return null;

		// Palette index 0 means "no pixel here" and comes out transparent, except in solid colors:
		// a face set to "solid color 0" is meant to be black (palette 0's color), not a hole.
		var opaque = TextureReader.IsSolidColor(archive) ? 0xFF000000u : 0u;
		var frame = texture.Frames[0];
		var pixels = new uint[frame.Length];
		var hasTransparency = false;

		for (var i = 0; i < frame.Length; i++)
		{
			pixels[i] = _palette[frame[i]] | opaque;
			hasTransparency |= pixels[i] >> 24 == 0;
		}

		return new RgbaImage(texture.Width, texture.Height, pixels, hasTransparency);
	}

	private DfTexture? Find(int archive, int record)
	{
		if (TextureReader.IsSolidColor(archive))
			return TextureReader.SolidColor(archive, record);

		if (!_archives.TryGetValue(archive, out var textures))
		{
			try
			{
				textures = _pack.Read($"TEXTURE.{archive:D3}") is { } data ? TextureReader.Read(data) : null;
			}
			catch (InvalidDataException e)
			{
				Console.WriteLine($"TEXTURE.{archive:D3}: {e.Message}");
				textures = null;
			}

			_archives[archive] = textures;
		}

		return textures is not null && record < textures.Length ? textures[record] : null;
	}
}
