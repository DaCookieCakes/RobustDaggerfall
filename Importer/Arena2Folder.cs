namespace Importer;

/// <summary>
///		Read access to the original files in a Daggerfall ARENA2 folder, which the Importer converts from.
/// </summary>
/// <remarks>
///		Installs differ in the case of their file names (ARCH3D.BSA, arch3d.bsa...), and Linux file systems care,
///		so files are looked up by their name in upper case.
/// </remarks>
public sealed class Arena2Folder
{
	/// <summary>Full paths by upper-case path inside ARENA2, with '/' between folders.</summary>
	private readonly Dictionary<string, string> _files;

	public Arena2Folder(string path)
	{
		Path = path;
		_files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
			.ToDictionary(f => System.IO.Path.GetRelativePath(path, f).Replace('\\', '/').ToUpperInvariant());
	}

	/// <summary>The ARENA2 folder itself.</summary>
	public string Path { get; }

	/// <summary>
	///		Every file in the folder, in upper case, such as TEXTURE.056.
	/// </summary>
	public IEnumerable<string> Names => _files.Keys;

	/// <summary>
	///		Reads a whole file into memory, or returns null if the folder doesn't have it. The name's case doesn't matter.
	/// </summary>
	public byte[]? Read(string name)
	{
		return _files.TryGetValue(name.ToUpperInvariant(), out var file) ? File.ReadAllBytes(file) : null;
	}
}
