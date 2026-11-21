using System.IO.Compression;

namespace Importer;

/// <summary>
///		Read access to daggerfall.pack, the zip of converted game files the <see cref="Packer"/> makes.
/// </summary>
public sealed class Pack : IDisposable
{
	private readonly ZipArchive _zip;

	public Pack(string path)
	{
		_zip = ZipFile.OpenRead(path);
	}

	/// <summary>
	///		Starts a path that points into the pack, as opposed to a file in Resources.
	///		The folder isn't written: each field knows its own. A mesh's <c>$62305.glb</c> is the pack entry
	///		<c>models/62305.glb</c>, and a texture's <c>$056/2.png</c> is <c>textures/056/2.png</c>.
	/// </summary>
	public const string PathPrefix = "$";

	/// <summary>The pack folder that mesh paths are relative to.</summary>
	public const string ModelFolder = "models";

	/// <summary>The pack folder that texture paths are relative to.</summary>
	public const string TextureFolder = "textures";

	/// <summary>A converted model's name inside <see cref="ModelFolder"/>, such as 62305.glb.</summary>
	public static string ModelName(uint modelId) => $"{modelId}.glb";

	/// <summary>A converted texture's name inside <see cref="TextureFolder"/>, such as 056/2.png for record 2 of TEXTURE.056.</summary>
	public static string TextureName(int archive, int record) => $"{archive:D3}/{record}.png";

	/// <summary>The full pack entry of a model, such as models/62305.glb.</summary>
	public static string ModelEntry(uint modelId) => $"{ModelFolder}/{ModelName(modelId)}";

	/// <summary>The full pack entry of a texture, such as textures/056/2.png.</summary>
	public static string TextureEntry(int archive, int record) => $"{TextureFolder}/{TextureName(archive, record)}";

	/// <summary>
	///		A name as it's written in prototypes: 62305.glb → $62305.glb.
	/// </summary>
	public static string ToPath(string name) => PathPrefix + name;

	/// <summary>
	///		The pack in its usual place.
	/// </summary>
	public static string DefaultPath => Path.Combine(Packer.PackFolder, Packer.PackName);

	/// <summary>
	///		Every file in the pack, such as models/62305.glb or textures/056/2.png.
	/// </summary>
	public IEnumerable<string> Names => _zip.Entries.Select(e => e.FullName);

	/// <summary>
	///		Reads a whole file into memory, or returns null if the pack doesn't have it.
	///		The name must match exactly, as written in prototypes: models/62305.glb.
	/// </summary>
	public byte[]? Read(string name)
	{
		var entry = _zip.GetEntry(name);
		if (entry is null)
			return null;

		using var stream = entry.Open();
		var memory = new MemoryStream((int) entry.Length);
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	public void Dispose() => _zip.Dispose();
}
