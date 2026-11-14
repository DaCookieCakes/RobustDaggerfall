using Engine.Shared.Objects;

namespace Engine.Shared.Prototypes;

/// <summary>
///		An entity prototype's components, keyed by component name ("Health" → HealthComponent).
/// </summary>
/// <remarks>
///		Read from a YAML list of <c>- type: Name</c> entries. Keying by name means each component appears once,
///		and lets inheritance replace a parent's component by name later.
/// </remarks>
public sealed class ComponentRegistry : Dictionary<string, Component>;
