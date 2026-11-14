using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Engine.Shared.Objects;

/// <summary>
///		Maps component names used in YAML (<c>type: Health</c>) to component classes.
/// </summary>
public sealed class ComponentFactory
{
	private const string Suffix = "Component";

	private readonly Dictionary<string, Type> _types = new();
	private readonly Dictionary<Type, string> _names = new();

	/// <summary>
	///		Registers every class marked with <see cref="RegisterComponentAttribute"/> in the assembly.
	/// </summary>
	public void RegisterAssembly(Assembly assembly)
	{
		foreach (var type in assembly.GetTypes())
		{
			if (type.IsDefined(typeof(RegisterComponentAttribute), false))
				Register(type);
		}
	}

	/// <summary>
	///		Registers a single component type.
	/// </summary>
	/// <exception cref="ArgumentException">The type can't be a registered component.</exception>
	/// <exception cref="InvalidOperationException">Another component already uses the same name.</exception>
	public void Register(Type type)
	{
		if (!type.IsSubclassOf(typeof(Component)) || type.IsAbstract)
			throw new ArgumentException($"{type.Name} is not a concrete Component.", nameof(type));

		if (type.GetConstructor(Type.EmptyTypes) is null)
			throw new ArgumentException($"{type.Name} needs a public parameterless constructor.", nameof(type));

		// Registering the same type twice is harmless.
		if (_names.ContainsKey(type))
			return;

		var name = NameFor(type);
		if (!_types.TryAdd(name, type))
			throw new InvalidOperationException($"Component name '{name}' is used by both {_types[name].Name} and {type.Name}.");

		_names[type] = name;
	}

	/// <summary>
	///		Gets the component class registered under a YAML name.
	/// </summary>
	/// <returns>true if found</returns>
	public bool TryGetComponentType(string name, [NotNullWhen(true)] out Type? type) => _types.TryGetValue(name, out type);

	/// <summary>
	///		Gets the YAML name of a registered component class.
	/// </summary>
	/// <exception cref="KeyNotFoundException">The type isn't registered.</exception>
	public string GetComponentName(Type type)
	{
		return _names.TryGetValue(type, out var name)
			? name
			: throw new KeyNotFoundException($"{type.Name} is not a registered component.");
	}

	/// <summary>
	///		Creates a new, default instance of the named component.
	/// </summary>
	/// <exception cref="KeyNotFoundException">No component has that name.</exception>
	public Component Create(string name)
	{
		if (!_types.TryGetValue(name, out var type))
			throw new KeyNotFoundException($"No component is registered as '{name}'.");

		return (Component) Activator.CreateInstance(type)!;
	}

	/// <summary>
	///		HealthComponent → Health. A class named just "Component" or without the suffix keeps its full name.
	/// </summary>
	private static string NameFor(Type type)
	{
		var name = type.Name;
		return name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal)
			? name[..^Suffix.Length]
			: name;
	}
}
