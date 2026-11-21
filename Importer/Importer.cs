using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

using Importer.Conversion;
using Importer.Readers;

namespace Importer;

public static class Importer
{
	/// <summary>
	///		How many folders deep under the ARENA2 data files may be.
	///		Steam's Daggerfall install is 2 below the game folder, so 3 leaves room for other layouts.
	/// </summary>
	private const int Arena2SearchDepth = 3;

	public static int Main(string[] args)
	{
		string? arena2 = null;
		if (args.Length == 0)
		{
			string? steam = null;
			if (OperatingSystem.IsWindows())
				steam = FindSteamOnWindows();
			else if (OperatingSystem.IsLinux())
				steam = FindSteamOnLinux();
			else if (OperatingSystem.IsMacOS())
				steam = FindSteamOnMacOS();

			if (steam is not null)
			{
				Console.WriteLine($"Steam: {steam}");
				arena2 = FindDaggerfallInSteam(steam);
			}
			else
			{
				Console.WriteLine("Steam: not found");
			}

			// If GOG isn't found, search for it separately. This is windows only.
			if (arena2 is null && OperatingSystem.IsWindows() && FindGOGPathOnWindows() is { } gog &&
			    Directory.Exists(gog))
			{
				Console.WriteLine($"GOG: {gog}");
				arena2 = FindFolder(gog, "arena2", Arena2SearchDepth);
			}
		}
		else
		{
			var path = Path.TrimEndingDirectorySeparator(args[0]);
			if (!Directory.Exists(path))
			{
				Console.WriteLine($"Folder not found: {path}");
				return 1;
			}

			// Accept the ARENA2 folder itself, or any folder with ARENA2 somewhere inside it.
			arena2 = string.Equals(Path.GetFileName(path), "arena2", StringComparison.OrdinalIgnoreCase)
				? path
				: FindFolder(path, "arena2", Arena2SearchDepth);
		}

		if (arena2 is null)
		{
			Console.WriteLine("ARENA2: not found");
			return 1;
		}

		Console.WriteLine($"ARENA2: {arena2}");

		// VALIDATOR //
		var problems = Validator.CheckDfFiles(arena2);
		if (problems.Count > 0)
		{
			Console.WriteLine("This doesn't look like a complete Daggerfall install:");
			foreach (var problem in problems)
				Console.WriteLine($"  {problem}");

			return 1;
		}

		Console.WriteLine("All required files found.");

		// PACKAGER //
		var destination = Path.Combine(Packer.PackFolder, Packer.PackName);
		var existing = Packer.ReadManifest(destination);
		if (existing is not null && existing.ImporterVersion == Packer.PackerImporterVersion)
		{
			Console.WriteLine($".pack is up to date. (Made {existing.CreatedAt} from {existing.SourcePath})");
			return 0;
		}

		return RunPacker(arena2, destination) ? 0 : 1;
	}

	/// <summary>
	///		Parses every model in ARCH3D.BSA and prints a summary, so a reader bug shows up as a count, not a crash.
	/// </summary>
	/// <returns>true if every record parsed.</returns>
	private static bool ReadAllMeshes(BSAReader arch3D)
	{
		var failed = 0;
		long points = 0, faces = 0;
		var maxCorners = 0;

		for (var i = 0; i < arch3D.Records.Count; i++)
		{
			try
			{
				var mesh = MeshReader.Read(arch3D.ReadRecord(i));
				points += mesh.Points.Length;
				faces += mesh.Faces.Length;
				foreach (var face in mesh.Faces)
					maxCorners = Math.Max(maxCorners, face.Corners.Length);
			}
			catch (InvalidDataException e)
			{
				// Only show the first few, so one systematic bug doesn't print 10,000 lines.
				if (failed < 10)
					Console.WriteLine($"  Model {arch3D.Records[i].Name}: {e.Message}");
				failed++;
			}
		}

		Console.WriteLine($"Models read: {arch3D.Records.Count - failed}/{arch3D.Records.Count} " +
		                  $"({points} points, {faces} faces, up to {maxCorners} corners per face).");
		return failed == 0;
	}

	/// <summary>
	///		Decodes every TEXTURE.xxx in ARENA2 and prints a summary, like <see cref="ReadAllMeshes"/>.
	/// </summary>
	/// <returns>true if every file decoded.</returns>
	private static bool ReadAllTextures(Arena2Folder source)
	{
		var files = source.Names.Where(n => Regex.IsMatch(n, @"^TEXTURE\.\d{3}$")).Order().ToList();
		var failed = 0;
		int images = 0, frames = 0;

		foreach (var name in files)
		{
			try
			{
				foreach (var texture in TextureReader.Read(source.Read(name)!))
				{
					if (texture is null)
						continue;
					images++;
					frames += texture.Frames.Length;
				}
			}
			catch (InvalidDataException e)
			{
				if (failed < 10)
					Console.WriteLine($"  {name}: {e.Message}");
				failed++;
			}
		}

		Console.WriteLine($"Texture files read: {files.Count - failed}/{files.Count} ({images} images, {frames} frames).");
		return failed == 0;
	}

	/// <summary>
	///		Converts every ARCH3D model to a .glb file.
	/// </summary>
	/// <param name="used">Filled with every texture the written models use.</param>
	/// <returns>true if every model was written.</returns>
	private static bool ExportAllModels(BSAReader arch3D, TextureLibrary textures, HashSet<(int Archive, int Record)> used, PackWriter pack)
	{
		Console.WriteLine("Converting models");

		var timer = System.Diagnostics.Stopwatch.StartNew();
		var failed = 0;
		long bytes = 0;

		// A few IDs appear more than once, mostly with different contents. Keep the first: DFU's Arch3dFile looks
		// models up by searching from the start of the archive, so the later copies are never used there either.
		var written = new HashSet<string>();
		var duplicates = 0;

		for (var i = 0; i < arch3D.Records.Count; i++)
		{
			var record = arch3D.Records[i];
			if (!written.Add(record.Name))
			{
				duplicates++;
				continue;
			}

			try
			{
				var mesh = MeshConverter.Convert(MeshReader.Read(arch3D.ReadRecord(i)), record.Id ?? 0, textures.Size);
				var glb = GlbWriter.Build(mesh, record.Name).WriteGLB().ToArray();
				pack.Add(Pack.ModelEntry(record.Id ?? 0), glb);
				bytes += glb.Length;

				foreach (var sub in mesh.SubMeshes)
					used.Add((sub.TextureArchive, sub.TextureRecord));
			}
			catch (Exception e)
			{
				// Anything can go wrong in conversion or writing; report it and keep going with the rest.
				if (failed < 10)
					Console.WriteLine($"  Model {record.Name}: {e.GetType().Name}: {e.Message}");
				failed++;
			}
		}

		Console.WriteLine($"Models converted: {written.Count - failed}/{written.Count} " +
		                  $"({bytes / (1024.0 * 1024.0):0.0} MB in {timer.Elapsed.TotalSeconds:0.0} s, " +
		                  $"{duplicates} later copies of duplicate IDs skipped).");
		return failed == 0;
	}

	/// <summary>
	///		Writes each texture in <paramref name="used"/> once, as a PNG into the pack.
	///		Models only hold texture names, so this is the one copy every model shares.
	/// </summary>
	/// <returns>true if every texture was found and written.</returns>
	private static bool ExportTextures(TextureLibrary textures, HashSet<(int Archive, int Record)> used, PackWriter pack)
	{
		Console.WriteLine("Converting textures");

		var missing = 0;
		long bytes = 0;

		foreach (var (archive, record) in used.Order())
		{
			if (textures.Rgba(archive, record) is not { } image)
			{
				// The model still loads; the renderer shows its stand-in for this texture.
				if (missing < 10)
					Console.WriteLine($"  Texture {GlbWriter.TextureName(archive, record)} not found.");
				missing++;
				continue;
			}

			var png = PngWriter.Encode(image.Width, image.Height, image.Pixels);
			pack.Add(Pack.TextureEntry(archive, record), png);
			bytes += png.Length;
		}

		Console.WriteLine($"Textures converted: {used.Count - missing}/{used.Count} ({bytes / (1024.0 * 1024.0):0.0} MB).");
		return missing == 0;
	}

	/// <summary>
	///		Reads the game files from ARENA2, converts them, and writes the result as a new pack.
	/// </summary>
	/// <returns>true if the pack was written. On any failure the old pack, if there is one, is left as it was.</returns>
	private static bool RunPacker(string arena2, string destination)
	{
		var source = new Arena2Folder(arena2);
		if (source.Read("ARCH3D.BSA") is not { } arch3DData)
		{
			Console.WriteLine("ARCH3D.BSA could not be found.");
			return false;
		}

		using var arch3D = new BSAReader(new MemoryStream(arch3DData));
		Console.WriteLine($"ARCH3D.BSA loaded. Records found: {arch3D.Records.Count}");

		// Check everything parses before converting, so a reader bug shows up as a count, not half a pack.
		var meshesOk = ReadAllMeshes(arch3D);
		var texturesOk = ReadAllTextures(source);
		if (!meshesOk || !texturesOk)
			return false;

		Console.WriteLine($"Packing to {destination}");

		try
		{
			var manifest = Packer.Pack(arena2, destination, pack =>
			{
				var library = new TextureLibrary(source);
				var used = new HashSet<(int Archive, int Record)>();

				// Each step reports its own failures. Throwing makes the Packer drop the unfinished pack.
				if (!ExportAllModels(arch3D, library, used, pack) || !ExportTextures(library, used, pack))
					throw new InvalidDataException("Some files could not be converted.");
			});

			Console.WriteLine($"Pack finished: {manifest.FileCount} files.");
			return true;
		}
		catch (Exception e)
		{
			Console.WriteLine($"Packing failed: {e.Message}");
			return false;
		}
	}

	#region Shared

	/// <summary>
	///		The user's home folder: C:\Users\you on Windows, /home/you on Linux, /Users/you on macOS.
	/// </summary>
	private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	/// <summary>
	///		Returns the first of the given folders that exists, or null if none do.
	/// </summary>
	private static string? FirstExisting(params string[] folders)
	{
		return folders.FirstOrDefault(folder => Directory.Exists(folder));
	}

	/// <summary>
	///		Checks every Steam library on this machine for Daggerfall, and returns the first ARENA2 found.
	/// </summary>
	private static string? FindDaggerfallInSteam(string steamRoot)
	{
		foreach (var library in FindSteamLibraries(steamRoot))
		{
			Console.WriteLine($"  Library: {library}");

			var found = FindDaggerfallInLibrary(library);
			if (found is not null)
				return found;
		}

		return null;
	}

	/// <summary>
	///		Reads every library path from Steam's libraryfolders.vdf. The main Steam folder is always included.
	/// </summary>
	/// <remarks>
	///		Only the main Steam folder has this file. Libraries on other drives have a libraryfolder.vdf (singular),
	///		which holds just a label, not the list.
	/// </remarks>
	private static List<string> FindSteamLibraries(string steamRoot)
	{
		// Start with the main folder, in case the file is missing or broken. It's normally listed as "0" anyway.
		var libraries = new List<string> { steamRoot };

		var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(vdf))
			return libraries;

		var text = File.ReadAllText(vdf);
		foreach (Match m in Regex.Matches(text, @"""path""\s*""([^""]*)""", RegexOptions.IgnoreCase))
		{
			// VDF writes a backslash as two, like C# strings do: "D:\\SteamLibrary" means D:\SteamLibrary.
			var path = m.Groups[1].Value.Replace(@"\\", @"\");

			// Skip ones already in the list, like the main folder (written with a trailing slash on some setups).
			var tidy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			if (!libraries.Contains(tidy, StringComparer.OrdinalIgnoreCase))
				libraries.Add(tidy);
		}

		return libraries;
	}

	/// <summary>
	///		Looks for Daggerfall in a Steam library using its appmanifest,
	///			then searches that folder for ARENA2.
	/// </summary>
	/// <param name="library">A library folder.</param>
	/// <returns>the ARENA2 path, or null if Daggerfall isn't in this library.</returns>
	private static string? FindDaggerfallInLibrary(string library)
	{
		var steamapps = Path.Combine(library, "steamapps");
		var manifest = Path.Combine(steamapps, "appmanifest_1812390.acf");

		// No manifest means Daggerfall isn't installed in this library.
		if (!File.Exists(manifest))
			return null;

		var text = File.ReadAllText(manifest);
		var m = Regex.Match(text, @"""installdir""\s*""([^""]*)""", RegexOptions.IgnoreCase);
		if (!m.Success)
			return null;

		var game = Path.Combine(steamapps, "common", m.Groups[1].Value);
		if (!Directory.Exists(game))
			return null;

		return FindFolder(game, "arena2", Arena2SearchDepth);
	}


	/// <summary>
	///		Searches <paramref name="root"/> and the folders inside it for a folder named <paramref name="name"/>, ignoring case.
	/// </summary>
	/// <param name="root">Where to start.</param>
	/// <param name="name">The folder name to find, such as "arena2".</param>
	/// <param name="depth">How many levels further down to look. 0 means only check root's own children.</param>
	/// <returns>The full path of the first match, or null if there's none within the depth.</returns>
	private static string? FindFolder(string root, string name, int depth)
	{
		if (depth < 0)
			return null;

		// IgnoreInaccessible: skip folders we aren't allowed to open instead of crashing.
		var options = new EnumerationOptions { IgnoreInaccessible = true };
		var children = Directory.EnumerateDirectories(root, "*", options).Order(StringComparer.Ordinal).ToList();

		// First, is the folder right here? Checking this level before going deeper finds the shallowest match.
		foreach (var child in children)
		{
			if (string.Equals(Path.GetFileName(child), name, StringComparison.OrdinalIgnoreCase))
				return child;
		}

		// If not, search inside each child - one level less deep.
		foreach (var child in children)
		{
			if (new DirectoryInfo(child).LinkTarget is not null)
				continue;

			var found = FindFolder(child, name, depth - 1);
			if (found is not null)
				return found;
		}

		return null;
	}

	#endregion

	#region Windows

	/// <summary>
	///		Asks the registry where steam is installed on Windows.
	/// </summary>
	/// <returns>steam path, or null if not found</returns>
	[SupportedOSPlatform("windows")]
	private static string? FindSteamOnWindows()
	{
		using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
		var path = key?.GetValue("SteamPath") as string;
		return path is null ? null : Path.GetFullPath(path);
	}

	[SupportedOSPlatform("windows")]
	private static string? FindGOGPathOnWindows()
	{
		using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
		using var key = hklm.OpenSubKey(@"SOFTWARE\GOG.com\Games\1435829353");
		return key?.GetValue("path") as string;
	}

	#endregion

	#region Linux

	/// <summary>
	///		Checks the places Steam is usually installed on Linux.
	/// </summary>
	/// <returns>steam path, or null if not found</returns>
	[SupportedOSPlatform("linux")]
	private static string? FindSteamOnLinux()
	{
		var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
		if (string.IsNullOrEmpty(dataHome))
			dataHome = Path.Combine(Home, ".local", "share");

		var steam = FirstExisting(
			Path.Combine(Home, ".steam", "steam"),
			Path.Combine(dataHome, "Steam"),
			Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
			Path.Combine(Home, "snap", "steam", "common", ".local", "share", "Steam"));

		if (steam is not null && new DirectoryInfo(steam).ResolveLinkTarget(returnFinalTarget: true) is { } target)
			steam = target.FullName;

		return steam is null ? null : Path.TrimEndingDirectorySeparator(steam);
	}

	#endregion

	#region MacOS

	/// <summary>
	///		Checks where Steam is installed on macOS. There's no registry on macOS, and Steam always uses this one folder.
	/// </summary>
	/// <returns>steam path, or null if not found</returns>
	[SupportedOSPlatform("macos")]
	private static string? FindSteamOnMacOS()
	{
		return FirstExisting(Path.Combine(Home, "Library", "Application Support", "Steam"));
	}

	#endregion
}
