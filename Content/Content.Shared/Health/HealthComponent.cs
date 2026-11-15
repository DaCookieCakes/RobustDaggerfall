using Engine.Shared.Objects;
using Engine.Shared.Serialization;

namespace Content.Shared.Health;

/// <summary>
///		How much damage an entity can take.
/// </summary>
[RegisterComponent]
public sealed class HealthComponent : Component
{
	[DataField]
	public int Max { get; private set; } = 100;
}
