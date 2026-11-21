namespace Importer.Readers;

/// <summary>
///		A position or direction in Daggerfall's own integer units, exactly as stored.
/// </summary>
public readonly record struct DfVector(int X, int Y, int Z);

/// <summary>
///		One corner of a face: which point it uses, and its raw texture coordinate.
/// </summary>
/// <param name="PointIndex">Index into <see cref="DfMesh.Points"/>.</param>
/// <param name="U">Raw U, in texels. Not yet usable as-is; see the remarks on <see cref="MeshReader"/>.</param>
/// <param name="V">Raw V, in texels.</param>
public readonly record struct DfCorner(int PointIndex, short U, short V);

/// <summary>
///		One face (a flat polygon with 3 or more corners) and the texture drawn on it.
/// </summary>
/// <param name="TextureArchive">Which TEXTURE.xxx file, e.g. 34 means TEXTURE.034.</param>
/// <param name="TextureRecord">Which image inside that file.</param>
/// <param name="Corners">The corners, in order around the polygon.</param>
public sealed record DfFace(int TextureArchive, int TextureRecord, DfCorner[] Corners);

/// <summary>
///		One model from ARCH3D.BSA, as stored: no scaling, axis flips or UV fixes applied yet.
/// </summary>
/// <param name="Version">"v2.7", "v2.6" or "v2.5".</param>
/// <param name="Points">Every corner position. Faces share these by index.</param>
/// <param name="Faces">The polygons.</param>
/// <param name="Normals">One per face, same order as <see cref="Faces"/>. Raw fixed-point values.</param>
public sealed record DfMesh(string Version, DfVector[] Points, DfFace[] Faces, DfVector[] Normals);

/// <summary>
///		Reads one ARCH3D.BSA record (a Daggerfall 3D model) into a <see cref="DfMesh"/>.
/// </summary>
/// <remarks>
///		Layout, from DFU's Arch3dFile.cs, checked against all 10,251 records:
///		<code>
///			[64-byte header]
///			  0  char[4] version          "v2.7" / "v2.6" / "v2.5"
///			  4  Int32   point count
///			  8  Int32   face count
///			 12  Int32   radius
///			 16  8 bytes zero
///			 24  Int32   plane data offset  (not used here)
///			 28  Int32   object data offset (not used here)
///			 32  Int32   object data count
///			 36  4 bytes unknown
///			 40  8 bytes zero
///			 48  Int32   point list offset
///			 52  Int32   normal list offset
///			 56  4 bytes unknown
///			 60  Int32   face list offset
///
///			[point list]  point count × (Int32 x, Int32 y, Int32 z)
///			[face list]   face count  × face, each:
///			                Byte   corner count
///			                Byte   unknown
///			                UInt16 texture: archive in the top 9 bits, record in the low 7
///			                4 bytes unknown
///			                corner count × (Int32 point offset, Int16 u, Int16 v)
///			[normal list] face count  × (Int32 x, Int32 y, Int32 z)
///		</code>
///		All offsets are from the start of the record. Sections usually sit back to back, but not always,
///		so each one is found by its offset rather than assumed to follow the last.
///
///		A corner's "point offset" is a byte offset into the point list in v2.7 and v2.6 (so ÷ 12 gives the index),
///		but in v2.5 it's index × 4 (÷ 4).
///
///		The raw UVs are not final: in Daggerfall only the first corners' UVs are meaningful and later ones are
///		relative or unused. Turning them into real texture coordinates is a later step, once textures are read.
/// </remarks>
public static class MeshReader
{
	private const int HeaderSize = 64;
	private const int VectorSize = 12;
	private const int FaceHeaderSize = 8;
	private const int CornerSize = 8;

	/// <summary>
	///		Parses one ARCH3D record.
	/// </summary>
	/// <exception cref="InvalidDataException">The bytes aren't a valid model.</exception>
	public static DfMesh Read(byte[] data)
	{
		if (data.Length < HeaderSize)
			throw new InvalidDataException($"Record is {data.Length} bytes, smaller than the {HeaderSize}-byte header.");

		using var reader = new BinaryReader(new MemoryStream(data));

		var version = System.Text.Encoding.ASCII.GetString(data, 0, 4);
		var pointDivisor = version switch
		{
			"v2.7" or "v2.6" => VectorSize,
			"v2.5" => 4,
			_ => throw new InvalidDataException($"Unknown model version \"{version}\"."),
		};

		reader.BaseStream.Position = 4;
		var pointCount = reader.ReadInt32();
		var faceCount = reader.ReadInt32();

		reader.BaseStream.Position = 48;
		var pointListOffset = reader.ReadInt32();
		var normalListOffset = reader.ReadInt32();
		reader.BaseStream.Position = 60;
		var faceListOffset = reader.ReadInt32();

		var points = ReadVectors(reader, pointListOffset, pointCount, "point");
		var normals = ReadVectors(reader, normalListOffset, faceCount, "normal");

		var faces = new DfFace[faceCount];
		reader.BaseStream.Position = faceListOffset;
		for (var i = 0; i < faceCount; i++)
		{
			Require(reader, FaceHeaderSize, $"face {i}");

			var cornerCount = reader.ReadByte();
			reader.ReadByte(); // unknown
			var texture = reader.ReadUInt16();
			reader.ReadUInt32(); // unknown

			if (cornerCount < 3)
				throw new InvalidDataException($"Face {i} has {cornerCount} corners; a polygon needs at least 3.");

			Require(reader, cornerCount * CornerSize, $"face {i}'s corners");

			var corners = new DfCorner[cornerCount];
			for (var c = 0; c < cornerCount; c++)
			{
				var offset = reader.ReadInt32();
				var u = reader.ReadInt16();
				var v = reader.ReadInt16();

				var index = offset / pointDivisor;
				if (offset < 0 || offset % pointDivisor != 0 || index >= pointCount)
					throw new InvalidDataException($"Face {i} corner {c} points at offset {offset}, which isn't one of the {pointCount} points.");

				corners[c] = new DfCorner(index, u, v);
			}

			// Top 9 bits: which TEXTURE.xxx file. Low 7 bits: which image in it.
			faces[i] = new DfFace(texture >> 7, texture & 0x7F, corners);
		}

		return new DfMesh(version, points, faces, normals);
	}

	private static DfVector[] ReadVectors(BinaryReader reader, int offset, int count, string what)
	{
		if (count < 0)
			throw new InvalidDataException($"Negative {what} count ({count}).");

		reader.BaseStream.Position = offset;
		Require(reader, (long) count * VectorSize, $"the {what} list");

		var vectors = new DfVector[count];
		for (var i = 0; i < count; i++)
			vectors[i] = new DfVector(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

		return vectors;
	}

	/// <summary>
	///		Throws a clear error, rather than an EndOfStreamException, if fewer than <paramref name="bytes"/> remain.
	/// </summary>
	private static void Require(BinaryReader reader, long bytes, string what)
	{
		var stream = reader.BaseStream;
		if (stream.Position < 0 || stream.Position + bytes > stream.Length)
			throw new InvalidDataException($"The record ends before {what}.");
	}
}
