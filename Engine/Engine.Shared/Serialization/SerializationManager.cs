using System.Collections;
using System.Globalization;
using System.Reflection;
using Engine.Shared.Objects;
using Engine.Shared.Prototypes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Engine.Shared.Serialization;

/// <summary>
///		Reads YAML nodes into C# objects.
/// </summary>
public sealed class SerializationManager
{
	/// <summary>
	///		Instance members, public or not, declared on exactly the class being asked (not inherited).
	/// </summary>
	private const BindingFlags MemberFlags =
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

	private static readonly YamlScalarNode TypeKey = new("type");

	private readonly ComponentFactory _components;
	private readonly Dictionary<Type, DataField[]> _fieldCache = new();

	public SerializationManager(ComponentFactory components)
	{
		_components = components;
	}

	public T Read<T>(YamlNode node) => (T) Read(typeof(T), node)!;

	/// <summary>
	///		Reads a node as the given type.
	/// </summary>
	/// <exception cref="YamlReadException">The node doesn't fit the type.</exception>
	public object? Read(Type type, YamlNode node)
	{
		if (IsNull(node))
		{
			// null is fine for classes (string) and nullable structs (int?), but not for plain structs (int).
			if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
				return null;

			throw new YamlReadException($"Expected {type.Name}, got null.", node.Start);
		}

		// From here on, int? is read the same way as int.
		type = Nullable.GetUnderlyingType(type) ?? type;

		if (type == typeof(string))
			return Scalar(node, type).Value;

		if (type.IsEnum)
		{
			var scalar = Scalar(node, type);
			return Enum.TryParse(type, scalar.Value, ignoreCase: false, out var result)
				? result
				: throw new YamlReadException($"'{scalar.Value}' is not a {type.Name}.", node.Start);
		}

		// int, float, bool, double, long, byte, char... plus decimal, which isn't a "primitive" to .NET.
		if (type.IsPrimitive || type == typeof(decimal))
			return ReadPrimitive(type, Scalar(node, type));

		// Collections read each item with Read again, so a List<List<int>> or a list of data definitions just works.
		if (type.IsArray)
			return ReadArray(type.GetElementType()!, Sequence(node, type));

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
			return ReadList(type, Sequence(node, type));

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
			return ReadDictionary(type, Mapping(node, type));

		// The field only says "some Component", so the YAML has to say which one: - type: Health
		if (type == typeof(Component))
			return ReadComponent(Mapping(node, type));

		if (type == typeof(ComponentRegistry))
			return ReadComponentRegistry(Sequence(node, type));

		// A class with data fields: make a fresh one with its C# defaults, then overwrite what the YAML sets.
		// Populate calls Read for each field, so nested objects are handled by this same branch.
		if (IsDataDefinition(type))
		{
			if (type.IsAbstract)
				throw new YamlReadException($"Can't create abstract type {type.Name}.", node.Start);

			var instance = Activator.CreateInstance(type, nonPublic: true)!;
			Populate(instance, Mapping(node, type));
			return instance;
		}

		throw new YamlReadException($"Don't know how to read {type.Name}. Is it missing [DataDefinition]?", node.Start);
	}

	/// <summary>
	///		Types read from a mapping via their [DataField] members.
	/// </summary>
	private static bool IsDataDefinition(Type type)
	{
		return type.IsDefined(typeof(DataDefinitionAttribute), false)
			|| type.IsSubclassOf(typeof(Component))
			|| typeof(IPrototype).IsAssignableFrom(type);
	}

	/// <summary>
	///		Fills in an existing object's data fields from a mapping. Keys missing from the YAML keep their C# defaults.
	/// </summary>
	/// <exception cref="YamlReadException">A key has no matching data field, a value doesn't fit, or a required field is missing.</exception>
	public void Populate(object target, YamlMappingNode node)
	{
		var type = target.GetType();
		var fields = GetDataFields(type);
		var seen = new HashSet<string>();

		foreach (var (keyNode, valueNode) in node)
		{
			var key = Scalar(keyNode, typeof(string)).Value!;

			var field = Array.Find(fields, f => f.Name == key)
				?? throw new YamlReadException($"{type.Name} has no data field '{key}'.", keyNode.Start);

			field.Set(target, Read(field.Type, valueNode));
			seen.Add(key);
		}

		foreach (var field in fields)
		{
			if (field.Required && !seen.Contains(field.Name))
				throw new YamlReadException($"{type.Name} is missing required data field '{field.Name}'.", node.Start);
		}
	}

	/// <summary>
	///		Finds every [DataField] member on a type, including private ones on base classes. Cached per type.
	/// </summary>
	private DataField[] GetDataFields(Type type)
	{
		if (_fieldCache.TryGetValue(type, out var cached))
			return cached;

		var fields = new List<DataField>();

		// GetMembers on a derived type doesn't return private members of its base classes,
		// so walk up the hierarchy and ask each class for only the members it declares itself.
		for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
		{
			foreach (var member in t.GetMembers(MemberFlags))
			{
				if (member.GetCustomAttribute<DataFieldAttribute>() is not { } attribute)
					continue;

				var field = member switch
				{
					FieldInfo f => new DataField(
						attribute.Name ?? DefaultName(f.Name), f.FieldType, attribute.Required, f.SetValue),

					// SetMethod is non-null for { get; set; }, { get; private set; } and { get; init; }.
					PropertyInfo { SetMethod: not null } p => new DataField(
						attribute.Name ?? DefaultName(p.Name), p.PropertyType, attribute.Required, p.SetValue),

					PropertyInfo p => throw new InvalidOperationException(
						$"Data field property {type.Name}.{p.Name} needs a setter (private or init is fine)."),

					_ => throw new InvalidOperationException($"Unexpected data field member {member.Name}."),
				};

				if (fields.Exists(x => x.Name == field.Name))
					throw new InvalidOperationException($"{type.Name} has two data fields named '{field.Name}'.");

				fields.Add(field);
			}
		}

		return _fieldCache[type] = fields.ToArray();
	}

	/// <summary>
	///		MaxHealth → maxHealth, _speed → speed.
	/// </summary>
	private static string DefaultName(string memberName)
	{
		var name = memberName.TrimStart('_');
		return name.Length == 0 ? memberName : char.ToLowerInvariant(name[0]) + name[1..];
	}

	/// <summary>
	///		An unquoted <c>null</c>, <c>~</c> or empty value. A quoted <c>"null"</c> is the string "null".
	/// </summary>
	private static bool IsNull(YamlNode node)
	{
		return node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
			&& scalar.Value is null or "" or "~" or "null";
	}

	private static object ReadPrimitive(Type type, YamlScalarNode node)
	{
		var value = node.Value!;
		try
		{
			// Convert.ChangeType accepts "True"/"False" for bools; bool.Parse is the same but clearer.
			// InvariantCulture: "1.5" must mean one and a half on every machine, not 15 on a German one.
			return type == typeof(bool)
				? bool.Parse(value)
				: Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
		}
		catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
		{
			throw new YamlReadException($"'{value}' is not a valid {type.Name}.", node.Start, e);
		}
	}

	/// <summary>
	///		<c>type: Health</c> plus that component's data fields → a new HealthComponent.
	/// </summary>
	private Component ReadComponent(YamlMappingNode node)
	{
		if (!node.Children.TryGetValue(TypeKey, out var typeNode) || typeNode is not YamlScalarNode { Value: { } name })
			throw new YamlReadException("Component is missing 'type'.", node.Start);

		if (!_components.TryGetComponentType(name, out var componentType))
			throw new YamlReadException($"Unknown component '{name}'.", typeNode.Start);

		// "type" picks the class; it isn't a data field on it, so pass everything else on.
		var data = new YamlMappingNode();
		foreach (var (key, value) in node)
		{
			if (!key.Equals(TypeKey))
				data.Add(key, value);
		}

		return (Component) Read(componentType, data)!;
	}

	/// <summary>
	///		A list of <c>- type: Name</c> entries → a registry keyed by component name. Each component may appear once.
	/// </summary>
	private ComponentRegistry ReadComponentRegistry(YamlSequenceNode node)
	{
		var registry = new ComponentRegistry();

		foreach (var item in node)
		{
			var component = ReadComponent(Mapping(item, typeof(Component)));
			var name = _components.GetComponentName(component.GetType());

			if (!registry.TryAdd(name, component))
				throw new YamlReadException($"Duplicate component '{name}'.", item.Start);
		}

		return registry;
	}

	/// <summary>
	///		<c>[a, b]</c> or a <c>- a</c> block list → T[].
	/// </summary>
	private Array ReadArray(Type elementType, YamlSequenceNode node)
	{
		var array = Array.CreateInstance(elementType, node.Children.Count);
		for (var i = 0; i < node.Children.Count; i++)
			array.SetValue(Read(elementType, node.Children[i]), i);

		return array;
	}

	/// <summary>
	///		<c>[a, b]</c> or a <c>- a</c> block list → List&lt;T&gt;.
	/// </summary>
	private IList ReadList(Type listType, YamlSequenceNode node)
	{
		var elementType = listType.GetGenericArguments()[0];

		// List<T> also implements the non-generic IList, which lets us call Add without knowing T at compile time.
		var list = (IList) Activator.CreateInstance(listType)!;
		foreach (var item in node)
			list.Add(Read(elementType, item));

		return list;
	}

	/// <summary>
	///		A mapping → Dictionary&lt;TKey, TValue&gt;. Keys are read with Read too, so int or enum keys work.
	/// </summary>
	private IDictionary ReadDictionary(Type dictType, YamlMappingNode node)
	{
		var args = dictType.GetGenericArguments();
		var dict = (IDictionary) Activator.CreateInstance(dictType)!;

		foreach (var (keyNode, valueNode) in node)
		{
			// Read the key as an object so "1" and "01" both become the int 1, then check for clashes.
			var key = Read(args[0], keyNode)
				?? throw new YamlReadException("Dictionary keys can't be null.", keyNode.Start);

			if (dict.Contains(key))
				throw new YamlReadException($"Duplicate key '{key}'.", keyNode.Start);

			dict.Add(key, Read(args[1], valueNode));
		}

		return dict;
	}

	private static YamlSequenceNode Sequence(YamlNode node, Type expected)
	{
		return node as YamlSequenceNode
			?? throw new YamlReadException($"Expected a list for {expected.Name}, got {node.NodeType}.", node.Start);
	}

	private static YamlScalarNode Scalar(YamlNode node, Type expected)
	{
		return node as YamlScalarNode
			?? throw new YamlReadException($"Expected a value for {expected.Name}, got {node.NodeType}.", node.Start);
	}

	private static YamlMappingNode Mapping(YamlNode node, Type expected)
	{
		return node as YamlMappingNode
			?? throw new YamlReadException($"Expected a mapping for {expected.Name}, got {node.NodeType}.", node.Start);
	}

	/// <summary>
	///		One [DataField] member: its YAML key, its C# type, and how to assign it.
	/// </summary>
	private sealed record DataField(string Name, Type Type, bool Required, Action<object, object?> Set);
}
