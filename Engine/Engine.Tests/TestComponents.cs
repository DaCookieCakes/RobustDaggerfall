using Engine.Shared.Objects;

// TODO: Remove once we have proper components in YML.
namespace Engine.Tests;

/// <summary>
///		Example game content, used only by tests.
/// </summary>
public sealed class HealthComponent : Component
{
	public int Current = 100;
}

/// <summary>
///		A second component type, for tests that need two different kinds.
/// </summary>
public sealed class NameComponent : Component
{
	public string Name = "";
}
