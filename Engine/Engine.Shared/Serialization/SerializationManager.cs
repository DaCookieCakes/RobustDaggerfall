using System.Globalization;
using System.Reflection;
using Engine.Shared.Objects;
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

	private readonly Dictionary<Type, DataField[]> _fieldCache = new();

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
			|| type.IsSubclassOf(typeof(Component));
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
