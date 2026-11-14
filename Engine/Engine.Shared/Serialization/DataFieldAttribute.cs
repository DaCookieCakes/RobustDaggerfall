namespace Engine.Shared.Serialization;

/// <summary>
///		Marks a field or property as readable from YAML.
/// </summary>
/// <remarks>
///		If no name is given, the member name in camelCase is used (<c>MaxHealth</c> → <c>maxHealth</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DataFieldAttribute : Attribute
{
	/// <summary>
	///		The YAML key, or null to derive it from the member name.
	/// </summary>
	public string? Name { get; }

	/// <summary>
	///		Whether reading fails when the key is missing.
	/// </summary>
	public bool Required { get; init; }

	public DataFieldAttribute(string? name = null)
	{
		Name = name;
	}
}

/// <summary>
///		Marks a class or struct (that is not a component) as readable from a YAML mapping.
///		Its <see cref="DataFieldAttribute"/> members are filled in from the mapping's keys.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DataDefinitionAttribute : Attribute;
