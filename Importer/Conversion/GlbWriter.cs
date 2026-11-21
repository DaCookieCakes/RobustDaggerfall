using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Importer.Conversion;

/// <summary>
///		Writes a <see cref="ModelMesh"/> as a binary glTF (.glb) file holding only geometry, no textures.
/// </summary>
/// <remarks>
///		<list type="bullet">
///			<item>
///				Axes and units already match glTF: Y up, meters, right-handed, counter-clockwise front faces.
///				UVs too: glTF's (0, 0) is the top-left of the image, same as ours.
///			</item>
///			<item>
///				Each <see cref="SubMesh"/> becomes one primitive, in the same order. Which texture goes on which
///				primitive is decided outside the file (the entity's mesh component lists them by primitive index).
///			</item>
///			<item>
///				SharpGLTF only keeps primitives apart when their materials differ, so each one gets an empty material
///				named after its original texture (such as <c>056/2</c>). Renderers ignore it; it's there so any glTF
///				tool shows which texture a primitive was made for.
///			</item>
///		</list>
/// </remarks>
public static class GlbWriter
{
	/// <param name="name">Stored as the mesh name, such as the model ID.</param>
	public static ModelRoot Build(ModelMesh mesh, string name)
	{
		var builder = new MeshBuilder<VertexPositionNormal, VertexTexture1>(name);

		foreach (var sub in mesh.SubMeshes)
		{
			var primitive = builder.UsePrimitive(new MaterialBuilder(TextureName(sub.TextureArchive, sub.TextureRecord)));

			for (var i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i += 3)
			{
				// Degenerate triangles (zero area) are dropped by SharpGLTF, which is what we want.
				primitive.AddTriangle(
					Vertex(mesh, mesh.Indices[i]),
					Vertex(mesh, mesh.Indices[i + 1]),
					Vertex(mesh, mesh.Indices[i + 2]));
			}
		}

		var scene = new SceneBuilder(name);
		scene.AddRigidMesh(builder, Matrix4x4.Identity);
		return scene.ToGltf2();
	}

	/// <summary>
	///		How a texture is referred to: <c>056/2</c> is record 2 of TEXTURE.056.
	/// </summary>
	public static string TextureName(int archive, int record) => $"{archive:D3}/{record}";

	private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(ModelMesh mesh, uint index)
	{
		return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
			new VertexPositionNormal(mesh.Positions[index], mesh.Normals[index]),
			new VertexTexture1(mesh.UVs[index]));
	}
}
