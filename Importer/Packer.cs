using System.IO.Compression;
using System.Text.Json;

namespace Importer;

/// <summary>
///		Zips an ARENA2 folder into 'daggerfall.pack' with a manifest.json describing how it was made.
/// </summary>
/// <remarks>
///		The pack is written to a temporary file first, and only renamed to daggerfall.pack once it's complete.
///		A crash or power cut mid-way leaves the old pack (or no pack) in place, never a half-written one.
/// </remarks>
public static class Packer
{
	public const int PackerImporterVersion = 1;

	public const string PackName = "daggerfall.pack";
	public const string ManifestName = "manifest.json";

	/// <summary>
	///		What the manifest.json holds. A record gives us a type with these properties in one line.
	/// </summary>
	/// <param name="ImporterVersion">Which version of the Importer made the pack.</param>
	/// <param name="CreatedAt">When the pack was made, in UTC.</param>
	/// <param name="SourcePath">The ARENA2 folder it was made from, for support questions.</param>
	/// <param name="FileCount">How many game files are in the pack, not counting the manifest.</param>
	public sealed record Manifest(int ImporterVersion, DateTime CreatedAt, string SourcePath, int FileCount);

	/// <summary>
	///		The folder packs are kept in: %LOCALAPPDATA%\RobustDaggerfall on Windows, ~/.local/share/RobustDaggerfall on Linux.
	/// </summary>
	public static string PackFolder => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobustDaggerfall");

	/// <summary>
	///		Zips every file in <paramref name="arena2"/>, including its subfolders, into <paramref name="destination"/>.
	///		Replaces an existing pack only once the new one is complete.
	/// </summary>
	/// <returns>The manifest written into the pack.</returns>
	public static Manifest Pack(string arena2, string destination)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

		// The temp file goes in the SAME folder as the destination.
		var temp = destination + ".tmp";

		// A crash on a previous run may have left one behind.
		File.Delete(temp);

		try
		{
			var manifest = WriteZip(arena2, temp);
			
			File.Move(temp, destination, overwrite: true);
			return manifest;
		}
		catch
		{
			File.Delete(temp);
			throw;
		}
	}

	/// <summary>
	///		Writes the zip itself. Every file is stored under its path inside ARENA2, in upper case.
	/// </summary>
	private static Manifest WriteZip(string arena2, string zipPath)
	{
		// Sorted so the pack's contents are always in the same order.
		var files = Directory.EnumerateFiles(arena2, "*", SearchOption.AllDirectories)
			.Order(StringComparer.Ordinal)
			.ToList();

		using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		for (var i = 0; i < files.Count; i++)
		{
			// Upper case, so the game can look files up by one exact name.
			// We show progress every 100 files, plus at the end.
			var entryName = Path.GetRelativePath(arena2, files[i]).Replace('\\', '/').ToUpperInvariant();
			zip.CreateEntryFromFile(files[i], entryName, CompressionLevel.Optimal);
				
			if ((i + 1) % 100 == 0 || i + 1 == files.Count)
				Console.WriteLine($"  Packed {i + 1}/{files.Count}");
		}

		var manifest = new Manifest(PackerImporterVersion, DateTime.UtcNow, arena2, files.Count);
		var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		});

		using var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open());
		writer.Write(json);

		return manifest;
	}

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
