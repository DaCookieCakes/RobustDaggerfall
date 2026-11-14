using Engine.Shared.Objects;

// TODO: Remove once we have proper components in YML.
namespace Engine.Tests;

/// <summary>
///		Example game content, used only by tests.
/// </summary>
public sealed class DamageableComponent : Component
{
	public int CurrentHealth = 100;
}

/// <summary>
///		A second component type, for tests that need two different kinds.
/// </summary>
public sealed class MetaDataComponent : Component
{
	public string Name = "";
}

/// <summary>
///		A magic seal that blocks every damage type except the one it's vulnerable to.
/// </summary>
public sealed class DamageBlockerComponent : Component
{
	public string VulnerableTo = "Fire";
}

public sealed class BreakableComponent : Component
{
	public bool Broken = false;
}

/// <summary>
///		Raised when something is damaged.
/// </summary>
public record struct DamageEvent(int Amount, string DamageType);

/// <summary>
///		Raised when an entity is destroyed.
/// </summary>
public record struct BreakEvent;
