using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Importer;

public static class Importer
{
	/// <summary>
	///		How many folders deep under a Daggerfall install ARENA2 may be.
	///		Steam's is 2 below the game folder (DF/DAGGER/ARENA2), so 3 leaves room for other layouts.
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
			Console.WriteLine($"Pack is up to date (made {existing.CreatedAt} from {existing.SourcePath}).");
		}
		else
		{
			RunPacker(arena2, destination);
		}

		return 0;
	}

	private static void RunPacker(string arena2, string destination)
	{
		Console.WriteLine($"Packing to {destination}");
			
		try
		{
			var manifest = Packer.Pack(arena2, destination);
			Console.WriteLine($"Packager finished: {manifest.FileCount} files.");
		}
		catch (Exception e)
		{
			Console.WriteLine($"Packager failed: {e.Message}.");
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
