using Engine.Shared.Serialization;

namespace Engine.Shared.Prototypes;

/// <summary>
///		A template for spawning an entity.
/// </summary>
/// <example>
/// <code>
/// - type: entity
///   id: Goblin
///   name: goblin
///   description: A small, green and very rude creature.
///   components:
///   - type: Health
///     max: 30
/// </code>
/// </example>
[Prototype("entity")]
public sealed class EntityPrototype : IInheritingPrototype
{
	[DataField("id", Required = true)]
	public string ID { get; private set; } = default!;

	[DataField("parent")]
	public string[]? Parents { get; private set; }

	[DataField]
	public bool Abstract { get; private set; }

	[DataField]
	public string Name { get; private set; } = "";

	[DataField]
	public string Description { get; private set; } = "";

	[DataField]
	public ComponentRegistry Components { get; private set; } = new();
}
