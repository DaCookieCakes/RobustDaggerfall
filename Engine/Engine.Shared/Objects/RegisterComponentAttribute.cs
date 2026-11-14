namespace Engine.Shared.Objects;

/// <summary>
///		Makes a component available to <see cref="ComponentFactory.RegisterAssembly"/>, and therefore to YAML.
///		The YAML name is the class name without the "Component" suffix (<c>HealthComponent</c> → <c>Health</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisterComponentAttribute : Attribute;
