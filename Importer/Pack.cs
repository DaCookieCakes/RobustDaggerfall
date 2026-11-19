using System.IO.Compression;

namespace Importer;

/// <summary>
///		Read access to daggerfall.pack, the zip of ARENA2 the <see cref="Packer"/> makes.
/// </summary>
public sealed class Pack : IDisposable
{
	private readonly ZipArchive _zip;

	public Pack(string path)
	{
		_zip = ZipFile.OpenRead(path);
	}

	/// <summary>
	///		The pack in its usual place.
	/// </summary>
	public static string DefaultPath => Path.Combine(Packer.PackFolder, Packer.PackName);

	/// <summary>
	///		Every file name in the pack. Names are uppercase, as the Packer writes them.
	/// </summary>
	public IEnumerable<string> Names => _zip.Entries.Select(e => e.FullName);

	/// <summary>
	///		Reads a whole file into memory, or returns null if the pack doesn't have it.
	/// </summary>
	/// <remarks>
	///		Zip entries can only be read front to back, while formats like BSA keep their directory at the end,
	///		so readers get the whole file as bytes.
	/// </remarks>
	public byte[]? Read(string name)
	{
		var entry = _zip.GetEntry(name.ToUpperInvariant());
		if (entry is null)
			return null;

		using var stream = entry.Open();
		var memory = new MemoryStream((int) entry.Length);
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	public void Dispose() => _zip.Dispose();
}
