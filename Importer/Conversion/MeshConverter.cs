using System.Numerics;
using Importer.Readers;

namespace Importer.Conversion;

/// <summary>
///		A run of triangles that all use the same texture. Becomes one draw call, and one glTF primitive.
/// </summary>
/// <param name="TextureArchive">Which TEXTURE.xxx file.</param>
/// <param name="TextureRecord">Which image inside it.</param>
/// <param name="IndexStart">First index in <see cref="ModelMesh.Indices"/>.</param>
/// <param name="IndexCount">How many indices (3 per triangle).</param>
public readonly record struct SubMesh(int TextureArchive, int TextureRecord, int IndexStart, int IndexCount);

/// <summary>
///		A model ready for the GPU: triangles in meters, Y up, counter-clockwise front faces.
/// </summary>
/// <remarks>
///		Every face corner gets its own vertex, even where faces share a point. Faces are flat, so a shared corner
///		still needs a different normal (and later a different UV) on each face it belongs to.
/// </remarks>
public sealed record ModelMesh(Vector3[] Positions, Vector3[] Normals, Vector2[] UVs, uint[] Indices, SubMesh[] SubMeshes)
{
	public Vector3 BoundsMin => Positions.Length == 0 ? Vector3.Zero : Positions.Aggregate(Vector3.Min);
	public Vector3 BoundsMax => Positions.Length == 0 ? Vector3.Zero : Positions.Aggregate(Vector3.Max);
}

/// <summary>
///		Turns a raw <see cref="DfMesh"/> into a <see cref="ModelMesh"/>.
/// </summary>
/// <remarks>
///		Checked against all 10,251 models:
///		<list type="bullet">
///			<item>Points and normals are fixed-point: the stored integer ÷ 256.</item>
///			<item>
///				Daggerfall's Y axis points down. Turning the model 180° around X (negating Y and Z) makes Y point up
///				and keeps "forward" as -Z, the way <c>Camera</c> expects. A rotation, unlike flipping only Y, doesn't
///				mirror the model, so the winding stays as it is.
///			</item>
///			<item>
///				Every face's corners run counter-clockwise around its stored normal, which is what OpenGL and glTF
///				treat as the front. So corners keep their stored order.
///			</item>
///			<item>UVs: see <see cref="FaceUVs"/>.</item>
///		</list>
/// </remarks>
public static class MeshConverter
{
	/// <summary>
	///		Fixed-point scale of points and normals: the stored integer ÷ 256 is the real value.
	/// </summary>
	private const float FixedPoint = 1f / 256f;

	/// <summary>
	///		UVs are stored × 16: the stored integer ÷ 16 is the position in texels.
	/// </summary>
	private const float UVFixedPoint = 1f / 16f;

	/// <summary>
	///		Models below this ID store their first UVs packed, and need <see cref="UnpackUV"/>. From DFU.
	/// </summary>
	private const uint PackedUVBelowId = 905;

	/// <summary>
	///		Size used when a face's texture can't be found, so it still gets sensible UVs.
	/// </summary>
	private const int FallbackTextureSize = 64;

	/// <summary>
	///		Daggerfall units per meter. A first guess, from DFU's 1/40 world scale; to be checked in the viewer
	///		against something of known size, like a door.
	/// </summary>
	public const float UnitsPerMeter = 40f;

	/// <param name="mesh">The raw model.</param>
	/// <param name="modelId">Its ARCH3D record ID; a few old models store UVs differently.</param>
	/// <param name="textureSize">Width and height of a texture (archive, record) in pixels, or null if it doesn't exist.</param>
	public static ModelMesh Convert(DfMesh mesh, uint modelId, Func<int, int, (int Width, int Height)?> textureSize)
	{
		var positions = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<uint>();
		var subMeshes = new List<SubMesh>();

		// Group faces by texture, so each texture is one contiguous run of indices.
		var byTexture = mesh.Faces
			.Select((face, i) => (face, normal: mesh.Normals[i]))
			.GroupBy(f => (f.face.TextureArchive, f.face.TextureRecord))
			.OrderBy(g => g.Key);

		foreach (var group in byTexture)
		{
			var start = indices.Count;
			var (width, height) = textureSize(group.Key.TextureArchive, group.Key.TextureRecord)
			                      ?? (FallbackTextureSize, FallbackTextureSize);

			foreach (var (face, rawNormal) in group)
			{
				var normal = Vector3.Normalize(ToEngine(rawNormal) * FixedPoint);
				var first = (uint) positions.Count;
				var facePositions = face.Corners.Select(c => mesh.Points[c.PointIndex]).ToArray();

				foreach (var p in facePositions)
				{
					positions.Add(ToEngine(p) * (FixedPoint / UnitsPerMeter));
					normals.Add(normal);
				}

				// Texels to 0-1 across the texture. Values outside 0-1 are fine: the texture repeats.
				foreach (var texel in FaceUVs(face, facePositions, modelId < PackedUVBelowId))
					uvs.Add(new Vector2(texel.X / width, texel.Y / height));

				// Fan triangulation: corner 0 joins every other neighbouring pair. A 5-corner face gives
				// (0,1,2) (0,2,3) (0,3,4). This is only correct for convex polygons; the viewer will show if any aren't.
				for (var c = 1; c < face.Corners.Length - 1; c++)
				{
					indices.Add(first);
					indices.Add(first + (uint) c);
					indices.Add(first + (uint) c + 1);
				}
			}

			subMeshes.Add(new SubMesh(group.Key.TextureArchive, group.Key.TextureRecord, start, indices.Count - start));
		}

		return new ModelMesh(positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(), subMeshes.ToArray());
	}

	/// <summary>
	///		Works out every corner's UV, in texels.
	/// </summary>
	/// <remarks>
	///		Daggerfall only stores real UVs for the first three corners, and even those are packed:
	///		corner 0 is absolute, corner 1 is relative to corner 0, and corner 2 relative to corner 1.
	///		The rest of the corners' stored UVs aren't usable. Since a face is flat and its texture is mapped
	///		without bending, the texture is an affine map (a stretch, turn and shift) of the face's plane.
	///		Three corners pin that map down, so every other corner's UV follows from its position.
	///		V counts down from the top row of the image, which is also how glTF and the GPU upload see it.
	/// </remarks>
	private static Vector2[] FaceUVs(DfFace face, DfVector[] positions, bool packed)
	{
		var uvs = new Vector2[face.Corners.Length];

		// Corners 0-2: unpack, then add up the deltas.
		for (var c = 0; c < 3; c++)
		{
			int u = face.Corners[c].U, v = face.Corners[c].V;
			if (packed)
			{
				u = UnpackUV(u);
				v = UnpackUV(v);
			}

			uvs[c] = new Vector2(u, v) * UVFixedPoint;
			if (c > 0)
				uvs[c] += uvs[c - 1];
		}

		if (face.Corners.Length == 3)
			return uvs;

		// Corners 3+: write each as p0 + a·e1 + b·e2, using the edges from corner 0, and give it the same mix of UVs.
		var p0 = ToVector(positions[0]);
		var e1 = ToVector(positions[1]) - p0;
		var e2 = ToVector(positions[2]) - p0;

		// Solving for a and b: dot both sides with e1 and with e2 gives two equations in two unknowns.
		var d11 = Vector3.Dot(e1, e1);
		var d12 = Vector3.Dot(e1, e2);
		var d22 = Vector3.Dot(e2, e2);
		var determinant = d11 * d22 - d12 * d12;

		for (var c = 3; c < uvs.Length; c++)
		{
			// The first three corners in a line can't define a plane. Rare; fall back to the start UV.
			if (MathF.Abs(determinant) < 1e-6f * d11 * d22)
			{
				uvs[c] = uvs[0];
				continue;
			}

			var d = ToVector(positions[c]) - p0;
			var de1 = Vector3.Dot(d, e1);
			var de2 = Vector3.Dot(d, e2);
			var a = (de1 * d22 - de2 * d12) / determinant;
			var b = (de2 * d11 - de1 * d12) / determinant;

			uvs[c] = uvs[0] + a * (uvs[1] - uvs[0]) + b * (uvs[2] - uvs[0]);
		}

		return uvs;
	}

	/// <summary>
	///		Old models (ID below 905) add a multiple of 8192 to some UVs. This removes the nearest one. From DFU.
	/// </summary>
	private static int UnpackUV(int value)
	{
		if (value is > -14336 and < 14336 && value != -7168)
			return value;

		var next = (((value - 1) >> 13) + 1) << 13;
		var previous = next - 8192;
		return value - (value - previous < next - value ? previous : next);
	}

	/// <summary>
	///		Raw position as floats, for UV maths. Units don't matter there, only ratios.
	/// </summary>
	private static Vector3 ToVector(DfVector v) => new(v.X, v.Y, v.Z);

	/// <summary>
	///		Daggerfall's axes to the engine's: 180° around X, so Y goes up and Z flips with it.
	/// </summary>
	private static Vector3 ToEngine(DfVector v) => new(v.X, -v.Y, -v.Z);
}
