namespace Engine.Shared.Prototypes;

/// <summary>
///		A named piece of game data loaded from YAML. Implementations need a <see cref="PrototypeAttribute"/>,
///		a parameterless constructor, and their <see cref="ID"/> marked <c>[DataField("id", Required = true)]</c>.
/// </summary>
public interface IPrototype
{
	/// <summary>
	///		Unique among prototypes of the same kind.
	/// </summary>
	string ID { get; }
}

/// <summary>
///		A prototype that can inherit data from another prototype of the same kind.
/// </summary>
/// <remarks>
///		Not implemented yet: both fields are read from YAML but nothing acts on them.
/// </remarks>
public interface IInheritingPrototype : IPrototype
{
	/// <summary>
	///		IDs of the prototypes to inherit from, or null for none.
	/// </summary>
	string[]? Parents { get; }

	/// <summary>
	///		Whether this prototype only exists to be inherited from, and can't be used directly.
	/// </summary>
	bool Abstract { get; }
}

/// <summary>
///		Sets the YAML <c>type:</c> a prototype class is loaded from, e.g. <c>[Prototype("entity")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PrototypeAttribute(string kind) : Attribute
{
	public string Kind { get; } = kind;
}
