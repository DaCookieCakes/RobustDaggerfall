using Engine.Shared.Objects;
using Engine.Shared.Serialization;

namespace Content.Shared.Base.Mesh.Components;

/// <summary>
///		A 3D model and the textures to paint it with.
/// </summary>
/// <remarks>
///		The model file holds only geometry, split into parts (glTF primitives). <see cref="Textures"/> goes with
///		them in order: the first texture is drawn on the first part, and so on.
///		Paths starting with <c>$</c> point into daggerfall.pack, which the Importer makes from the player's game files.
///		The pack folder is implied by the field: <see cref="Mesh"/> is inside models/, <see cref="Textures"/> inside textures/.
/// </remarks>
[RegisterComponent]
public sealed class MeshComponent : Component
{
	/// <summary>
	///		The model file, such as <c>$62305.glb</c> (the pack's models/62305.glb).
	/// </summary>
	[DataField(Required = true)]
	public string Mesh { get; private set; } = string.Empty;

	/// <summary>
	///		One texture per part of the model, such as <c>$056/2.png</c> (the pack's textures/056/2.png).
	/// </summary>
	[DataField]
	public List<string> Textures { get; private set; } = new();
}
