using Engine.Shared.Objects;
using Engine.Shared.Serialization;

namespace Content.Shared.Base.Mesh.Components;

/// <summary>
///		A 3D model and the textures to paint it with.
/// </summary>
/// <remarks>
///		The model file holds only geometry, split into parts (glTF primitives). <see cref="Textures"/> goes with
///		them in order: the first texture is drawn on the first part, and so on. Paths are relative to the folder
///		the Importer writes to.
/// </remarks>
[RegisterComponent]
public sealed class MeshComponent : Component
{
	/// <summary>
	///		The model file, such as <c>models/62305.glb</c>.
	/// </summary>
	[DataField(Required = true)]
	public string Mesh { get; private set; } = string.Empty;

	/// <summary>
	///		One texture per part of the model, such as <c>056/2</c> for <c>textures/056/2.png</c>.
	/// </summary>
	[DataField]
	public List<string> Textures { get; private set; } = new();
}
