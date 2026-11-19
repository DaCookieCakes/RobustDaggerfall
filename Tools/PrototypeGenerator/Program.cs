using System.Text;
using Importer;
using Importer.Conversion;
using Importer.Readers;

namespace PrototypeGenerator;

/// <summary>
///		Writes one entity prototype per Daggerfall model, each with a Mesh component pointing at its .glb and listing
///		its textures in primitive order. A starting point: the output is meant to be renamed, moved and edited by hand.
/// </summary>
/// <remarks>
///		Usage: <c>dotnet run --project Tools/PrototypeGenerator [output folder]</c>, from the repo root.
///		Needs the pack the Importer makes. Overwrites the output files, so rerunning loses hand edits made to them.
/// </remarks>
public static class Program
{
	private const string DefaultOutput = "Resources/Prototypes/Base/Entities/Models";

	/// <summary>
	///		Models per file. All ~10,000 in one file would be awkward to open and diff.
	/// </summary>
	private const int ModelsPerFile = 1000;

	public static int Main(string[] args)
	{
		var output = args.Length > 0 ? args[0] : DefaultOutput;

		if (!File.Exists(Pack.DefaultPath))
		{
			Console.WriteLine($"No pack at {Pack.DefaultPath}. Run the Importer first.");
			return 1;
		}

		using var pack = new Pack(Pack.DefaultPath);
		using var arch3D = new BSAReader(new MemoryStream(pack.Read("ARCH3D.BSA")!));
		var textures = new TextureLibrary(pack);

		// Same choices as the Importer: skip later copies of duplicate IDs, so each entry matches the .glb on disk.
		var seen = new HashSet<uint>();
		var models = new List<(uint Id, List<string> Textures)>();

		for (var i = 0; i < arch3D.Records.Count; i++)
		{
			if (arch3D.Records[i].Id is not { } id || !seen.Add(id))
				continue;

			// Converting is the only way to get the exact submesh order the .glb was written with.
			var mesh = MeshConverter.Convert(MeshReader.Read(arch3D.ReadRecord(i)), id, textures.Size);
			var names = mesh.SubMeshes.Select(s => GlbWriter.TextureName(s.TextureArchive, s.TextureRecord)).ToList();
			models.Add((id, names));
		}

		models.Sort((a, b) => a.Id.CompareTo(b.Id));

		Directory.CreateDirectory(output);
		var files = 0;
		foreach (var chunk in models.Chunk(ModelsPerFile))
		{
			var path = Path.Combine(output, $"models_{files:D2}.yml");
			File.WriteAllText(path, Write(chunk));
			files++;
		}

		Console.WriteLine($"Wrote {models.Count} model prototypes to {files} files in {output}");
		return 0;
	}

	/// <summary>
	///		The YAML for a list of models, in the same layout as the hand-written prototypes.
	/// </summary>
	private static string Write(IEnumerable<(uint Id, List<string> Textures)> models)
	{
		var yaml = new StringBuilder();

		foreach (var (id, textures) in models)
		{
			if (yaml.Length > 0)
				yaml.Append('\n');

			yaml.Append("- type: entity\n");
			yaml.Append($"  id: Model{id}\n");
			yaml.Append("  components:\n");
			yaml.Append("  - type: Mesh\n");
			yaml.Append($"    mesh: models/{id}.glb\n");
			yaml.Append("    textures:\n");
			foreach (var texture in textures)
				yaml.Append($"    - {texture}\n");
		}

		return yaml.ToString();
	}
}
