using System.IO.Compression;
using System.Text.Json;

namespace Importer;

/// <summary>
///		Writes 'daggerfall.pack': a zip of the game's converted files (models as .glb, textures as .png) plus a
///		manifest.json describing how it was made. What goes in is up to the caller; see <see cref="Pack"/>.
/// </summary>
/// <remarks>
///		The pack is written to a temporary file first, and only renamed to daggerfall.pack once it's complete.
///		A crash or power cut mid-way leaves the old pack in place, never a half-written one.
/// </remarks>
public static class Packer
{
	/// <summary>
	///		Bump this whenever what goes into the pack changes, so existing packs get rebuilt.
	///		1: the raw ARENA2 files. 2: converted models and textures.
	/// </summary>
	public const int PackerImporterVersion = 2;

	public const string PackName = "daggerfall.pack";
	public const string ManifestName = "manifest.json";

	/// <summary>
	///		What the manifest.json holds. A record gives us a type with these properties in one line.
	/// </summary>
	/// <param name="ImporterVersion">Which version of the Importer made the pack.</param>
	/// <param name="CreatedAt">When the pack was made, in UTC.</param>
	/// <param name="SourcePath">The ARENA2 folder it was made from, for support questions.</param>
	/// <param name="FileCount">How many files are in the pack, not counting the manifest.</param>
	public sealed record Manifest(int ImporterVersion, DateTime CreatedAt, string SourcePath, int FileCount);

	/// <summary>
	///		The folder packs are kept in: %LOCALAPPDATA%\RobustDaggerfall on Windows, ~/.local/share/RobustDaggerfall on Linux.
	/// </summary>
	public static string PackFolder => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobustDaggerfall");

	/// <summary>
	///		Makes a new pack at <paramref name="destination"/>, with <paramref name="writeContent"/> adding the files.
	///		Replaces an existing pack only once the new one is complete. If writeContent throws, the old pack stays.
	/// </summary>
	/// <param name="arena2">The ARENA2 folder the content is made from, recorded in the manifest.</param>
	/// <returns>The manifest written into the pack.</returns>
	public static Manifest Pack(string arena2, string destination, Action<PackWriter> writeContent)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

		// The temp file goes in the SAME folder as the destination, so the final rename is a quick, all-or-nothing move.
		var temp = destination + ".tmp";

		// A crash on a previous run may have left one behind.
		File.Delete(temp);

		try
		{
			Manifest manifest;
			using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
			{
				var writer = new PackWriter(zip);
				writeContent(writer);

				manifest = new Manifest(PackerImporterVersion, DateTime.UtcNow, arena2, writer.Count);
				using var json = new StreamWriter(zip.CreateEntry(ManifestName).Open());
				json.Write(JsonSerializer.Serialize(manifest, JsonOptions));
			}

			File.Move(temp, destination, overwrite: true);
			return manifest;
		}
		catch
		{
			File.Delete(temp);
			throw;
		}
	}

	// Shared by writing and reading, so the manifest is always read back with the names it was written with.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};
	
	public static Manifest? ReadManifest(string packPath)
	{
		if (!File.Exists(packPath))
			return null;

		using var zip = ZipFile.OpenRead(packPath);

		var entry = zip.GetEntry(ManifestName);
		if (entry is null)
			return null;

		using var stream = entry.Open();
		return JsonSerializer.Deserialize<Manifest>(stream, JsonOptions);
	}
}

/// <summary>
///		Adds files to a pack while <see cref="Packer.Pack"/> writes it.
/// </summary>
/// <remarks>
///		A zip being created can't list its own entries, so this counts them for the manifest.
/// </remarks>
public sealed class PackWriter(ZipArchive zip)
{
	/// <summary>How many files have been added so far.</summary>
	public int Count { get; private set; }

	/// <summary>
	///		Adds a file under <paramref name="name"/>, such as models/62305.glb.
	/// </summary>
	public void Add(string name, byte[] data)
	{
		using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
		stream.Write(data);
		Count++;
	}
}
