namespace Importer;

public static class Validator
{
	/// <summary>
	///		The files ARENA2 must contain to count as a Daggerfall install.
	/// </summary>
	private static readonly (string Pattern, int ExactCount)[] RequiredFiles =
	[
		("TEXTURE.???", 472),
		("ARCH3D.BSA", 1),
		("BLOCKS.BSA", 1),
		("MAPS.BSA", 1),
		("DAGGER.SND", 1),
		("WOODS.WLD", 1),
		("*.VID", 17),
	];

	/// <summary>
	///		Matching file names ignoring case.
	/// </summary>
	private static readonly EnumerationOptions AnyCase = new() { MatchCasing = MatchCasing.CaseInsensitive };

	/// <summary>
	///		This checks that an ARENA2 folder has every required file.
	/// </summary>
	/// <param name="arena2">The ARENA2 folder itself.</param>
	/// <returns>a message for each problem. An empty list means everything is there.</returns>
	public static List<string> CheckDfFiles(string arena2)
	{
		var problems = new List<string>();

		foreach (var (pattern, minCount) in RequiredFiles)
		{
			var count = Directory.EnumerateFiles(arena2, pattern, AnyCase).Count();
			if (count >= minCount)
				continue;
			
			problems.Add(minCount == 1
				? $"Missing {pattern}"
				: $"Found {count} of {minCount} {pattern} files");
		}

		return problems;
	}
}
