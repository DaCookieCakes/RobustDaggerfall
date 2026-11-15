using System.Reflection;
using System.Runtime.Loader;
using Engine.Shared.Objects;
using Engine.Shared.Prototypes;
using Engine.Shared.Serialization;

namespace Engine.Client;

/// <summary>
///		Owns the engine's managers and runs the game from startup to exit.
/// </summary>
public sealed class GameController
{
	public ComponentFactory Components { get; } = new();
	public SerializationManager Serialization { get; }
	public PrototypeManager Prototypes { get; }

	public GameController()
	{
		// Each manager needs the one before it, so they're created in this order.
		Serialization = new SerializationManager(Components);
		Prototypes = new PrototypeManager(Serialization);
	}

	/// <returns>The process exit code: 0 on success.</returns>
	public int Run()
	{
		Console.WriteLine("RobustDaggerfall starting.");

		if (!Startup())
			return 1;

		foreach (var entity in Prototypes.EnumeratePrototypes<EntityPrototype>())
			Console.WriteLine($"  entity {entity.ID}: {entity.Name} [{string.Join(", ", entity.Components.Keys)}]");

		return 0;
	}

	/// <summary>
	///		Registers types and loads resources. Prints the reason and returns false if anything fails.
	/// </summary>
	private bool Startup()
	{
		var root = FindRoot();
		if (root is null)
		{
			Console.Error.WriteLine("Could not find the Resources folder.");
			return false;
		}

		var resources = Path.Combine(root, "Resources");
		Console.WriteLine($"Resources: {resources}");

		var content = LoadContent(root);
		if (content is null)
			return false;

		Console.WriteLine($"Content: {content.FullName}");

		// The factory must know the components before any YAML is read, so register first.
		// Engine first, then content, so content can't quietly take a name the engine already uses.
		Components.RegisterAssembly(typeof(Component).Assembly);
		Components.RegisterAssembly(content);
		Prototypes.RegisterKindsFrom(content);

		try
		{
			Prototypes.LoadDirectory(Path.Combine(resources, "Prototypes"));
		}
		catch (PrototypeLoadException e)
		{
			Console.Error.WriteLine($"Failed to load prototypes: {e.Message}");
			return false;
		}

		return true;
	}

	/// <summary>
	///		Loads the content DLL that Content.Shared builds into bin/Content.Shared/.
	/// </summary>
	/// <returns>The loaded assembly, or null (after printing why) if it's missing.</returns>
	private static Assembly? LoadContent(string root)
	{
		var path = Path.Combine(root, "bin", "Content.Shared", "Content.Shared.dll");
		if (!File.Exists(path))
		{
			Console.Error.WriteLine($"Could not find the content DLL at {path}. Build the solution first.");
			return null;
		}

		// A separate load context keeps content apart from the engine. When content asks for a DLL this context
		// doesn't have, like Engine.Shared, .NET falls back to the default context, so content gets the engine's
		// own copy and its Component is the same type as ours.
		var context = new AssemblyLoadContext("Content");
		return context.LoadFromAssemblyPath(path);
	}

	/// <summary>
	///		Walks up from the exe's folder until it finds the repo root: the folder containing Resources/Prototypes.
	/// </summary>
	/// <remarks>
	///		The exe lives in Engine/Engine.Client/bin/Debug/net10.0/, so this works
	///		no matter which folder the game was started from.
	/// </remarks>
	private static string? FindRoot()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "Resources", "Prototypes")))
				return dir.FullName;
		}

		return null;
	}
}
