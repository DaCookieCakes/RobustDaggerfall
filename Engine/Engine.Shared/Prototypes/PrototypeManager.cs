using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Engine.Shared.Serialization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Engine.Shared.Prototypes;

/// <summary>
///		Loads prototypes from YAML and looks them up by ID.
/// </summary>
public sealed class PrototypeManager
{
	private static readonly YamlScalarNode TypeKey = new("type");
	private static readonly YamlScalarNode IdKey = new("id");

	private readonly SerializationManager _serialization;

	/// <summary>
	///		YAML <c>type:</c> value → prototype class, e.g. "entity" → EntityPrototype.
	/// </summary>
	private readonly Dictionary<string, Type> _kinds = new();

	/// <summary>
	///		Prototype class → (ID → prototype). One inner dictionary per kind, so IDs only clash within a kind.
	/// </summary>
	private readonly Dictionary<Type, Dictionary<string, IPrototype>> _prototypes = new();

	public PrototypeManager(SerializationManager serialization)
	{
		_serialization = serialization;

		// The engine's own kinds, like EntityPrototype. Game code registers its assemblies the same way.
		RegisterKindsFrom(typeof(PrototypeManager).Assembly);
	}

	/// <summary>
	///		Registers every class marked with <see cref="PrototypeAttribute"/> in the assembly.
	/// </summary>
	public void RegisterKindsFrom(Assembly assembly)
	{
		foreach (var type in assembly.GetTypes())
		{
			if (type.IsDefined(typeof(PrototypeAttribute), false))
				RegisterKind(type);
		}
	}

	/// <summary>
	///		Registers a single prototype class.
	/// </summary>
	/// <exception cref="ArgumentException">The type isn't a usable prototype class.</exception>
	/// <exception cref="InvalidOperationException">Another class already uses the same kind name.</exception>
	public void RegisterKind(Type type)
	{
		if (type.GetCustomAttribute<PrototypeAttribute>() is not { } attribute
			|| !typeof(IPrototype).IsAssignableFrom(type)
			|| type.IsAbstract)
			throw new ArgumentException($"{type.Name} must be a concrete IPrototype with a [Prototype] attribute.", nameof(type));

		if (_kinds.TryGetValue(attribute.Kind, out var existing))
		{
			// Registering the same class twice is harmless.
			if (existing == type)
				return;

			throw new InvalidOperationException($"Prototype kind '{attribute.Kind}' is used by both {existing.Name} and {type.Name}.");
		}

		_kinds[attribute.Kind] = type;
		_prototypes[type] = new Dictionary<string, IPrototype>();
	}

	/// <summary>
	///		Loads prototypes from YAML text. If anything in it is invalid, none of its prototypes are added.
	/// </summary>
	/// <param name="yaml">A YAML document whose top level is a list of prototypes.</param>
	/// <param name="source">Shown at the start of error messages; usually the file path.</param>
	/// <exception cref="PrototypeLoadException">The YAML is malformed or a prototype is invalid.</exception>
	public void LoadString(string yaml, string source = "<string>")
	{
		var stream = new YamlStream();
		try
		{
			stream.Load(new StringReader(yaml));
		}
		catch (YamlException e)
		{
			// Broken YAML syntax (bad indentation, unclosed brackets...), caught by YamlDotNet itself.
			throw new PrototypeLoadException($"{source}: {Location(e.Start)}{e.Message}", e);
		}

		// Read everything into a separate list first, and only store it once all of it is valid.
		var loaded = new List<IPrototype>();

		foreach (var document in stream.Documents)
		{
			if (document.RootNode is not YamlSequenceNode root)
				throw Error(source, document.RootNode.Start, "The top level must be a list of prototypes.");

			foreach (var entry in root)
				loaded.Add(ReadEntry(entry, source));
		}

		foreach (var prototype in loaded)
		{
			var kind = _prototypes[prototype.GetType()];
			if (kind.ContainsKey(prototype.ID) || loaded.Count(p => p.GetType() == prototype.GetType() && p.ID == prototype.ID) > 1)
				throw new PrototypeLoadException($"{source}: Duplicate {KindName(prototype.GetType())} '{prototype.ID}'.");
		}

		foreach (var prototype in loaded)
			_prototypes[prototype.GetType()][prototype.ID] = prototype;
	}

	/// <summary>
	///		Loads one YAML file. Error messages start with its path.
	/// </summary>
	/// <exception cref="PrototypeLoadException">The YAML is malformed or a prototype is invalid.</exception>
	public void LoadFile(string path)
	{
		LoadString(File.ReadAllText(path), path);
	}

	/// <summary>
	///		Loads every <c>.yml</c> file in a directory and its subdirectories, in alphabetical order.
	/// </summary>
	/// <remarks>
	///		Each file is loaded on its own: if one fails, files loaded before it stay loaded.
	/// </remarks>
	/// <exception cref="PrototypeLoadException">A file is malformed or contains an invalid prototype.</exception>
	public void LoadDirectory(string path)
	{
		// Sorted so the load order (and which file reports a duplicate) is the same on every machine.
		var files = Directory.EnumerateFiles(path, "*.yml", SearchOption.AllDirectories)
			.Order(StringComparer.Ordinal);

		foreach (var file in files)
			LoadFile(file);
	}

	/// <exception cref="KeyNotFoundException">No such prototype.</exception>
	public T Index<T>(string id) where T : class, IPrototype
	{
		return TryIndex<T>(id, out var prototype)
			? prototype
			: throw new KeyNotFoundException($"No {typeof(T).Name} with ID '{id}'.");
	}

	public bool TryIndex<T>(string id, [NotNullWhen(true)] out T? prototype) where T : class, IPrototype
	{
		if (Kind<T>().TryGetValue(id, out var found))
		{
			prototype = (T) found;
			return true;
		}

		prototype = null;
		return false;
	}

	public bool HasIndex<T>(string id) where T : class, IPrototype => Kind<T>().ContainsKey(id);

	public IEnumerable<T> EnumeratePrototypes<T>() where T : class, IPrototype => Kind<T>().Values.Cast<T>();

	/// <summary>
	///		Turns one <c>- type: ...</c> entry into a prototype object.
	/// </summary>
	private IPrototype ReadEntry(YamlNode node, string source)
	{
		if (node is not YamlMappingNode mapping)
			throw Error(source, node.Start, "Each prototype must be a mapping.");

		if (!mapping.Children.TryGetValue(TypeKey, out var typeNode) || typeNode is not YamlScalarNode { Value: { } kindName })
			throw Error(source, node.Start, "Prototype is missing 'type'.");

		if (!_kinds.TryGetValue(kindName, out var kind))
			throw Error(source, typeNode.Start, $"Unknown prototype type '{kindName}'.");

		// "type" is for us, not a data field on the prototype class, so pass everything else on.
		var data = new YamlMappingNode();
		foreach (var (key, value) in mapping)
		{
			if (!key.Equals(TypeKey))
				data.Add(key, value);
		}

		try
		{
			return (IPrototype) _serialization.Read(kind, data)!;
		}
		catch (YamlReadException e)
		{
			var id = mapping.Children.TryGetValue(IdKey, out var idNode) && idNode is YamlScalarNode { Value: { } text } ? text : "?";
			throw new PrototypeLoadException($"{source}: {kindName} '{id}': {e.Message}", e);
		}
	}

	private Dictionary<string, IPrototype> Kind<T>()
	{
		return _prototypes.TryGetValue(typeof(T), out var dict)
			? dict
			: throw new InvalidOperationException($"{typeof(T).Name} is not a registered prototype kind.");
	}

	private string KindName(Type type) => _kinds.First(pair => pair.Value == type).Key;

	private static string Location(Mark mark) => mark.Line > 0 ? $"({mark.Line}:{mark.Column}) " : "";

	private static PrototypeLoadException Error(string source, Mark mark, string message) =>
		new($"{source}: {Location(mark)}{message}");
}

/// <summary>
///		Thrown when prototype YAML can't be loaded. The message starts with the source (usually the file path).
/// </summary>
public sealed class PrototypeLoadException(string message, Exception? inner = null) : Exception(message, inner);
