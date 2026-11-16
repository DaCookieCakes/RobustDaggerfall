namespace Importer.Readers;

/// <summary>
///		One file inside a BSA archive.
/// </summary>
/// <param name="Name">The record's name. Number-type archives (like ARCH3D.BSA) have no names, so this is the ID as text.</param>
/// <param name="Id">The record's ID, or null in a name-type archive.</param>
/// <param name="Size">Length in bytes.</param>
/// <param name="Offset">Where the record's data starts, in bytes from the start of the BSA.</param>
public readonly record struct BsaRecord(string Name, uint? Id, int Size, long Offset);

/// <summary>
///		Reads Daggerfall's BSA archives: ARCH3D.BSA (models), BLOCKS.BSA, MAPS.BSA, MONSTER.BSA and others.
///		A BSA is a container; a very simple zip without compression that only knows where each record is.
/// </summary>
/// <remarks>
///		Layout, from DFU's BsaFile.cs:
///		<code>
///			[Int16  record count]
///			[UInt16 directory type: 0x0100 = records have names, 0x0200 = records have numbers]
///			[record 0 data][record 1 data]...						starts at byte 4, back to back
///			[directory: one entry per record]						at the very END of the file
///		</code>
///		Name entries are 18 bytes: a 14-byte name (padded with zero bytes) + Int32 size.
///		Number entries are 8 bytes: UInt32 ID + Int32 size.
///		The directory stores no offsets, so each record's offset is the sum of the sizes before it, starting at 4.
/// </remarks>
public sealed class BSAReader : IDisposable
{
	private const ushort NameDirectory = 0x0100;
	private const ushort NumberDirectory = 0x0200;

	private const int HeaderSize = 4;
	private const int NameEntrySize = 18;
	private const int NameLength = 14;
	private const int NumberEntrySize = 8;

	private readonly Stream _stream;
	private readonly BinaryReader _reader;

	/// <summary>
	///		Every record, in the order they're stored.
	/// </summary>
	public IReadOnlyList<BsaRecord> Records { get; }

	/// <summary>
	///		Reads the archive's header and directory. Record data is only read when asked for, with <see cref="ReadRecord"/>.
	/// </summary>
	/// <param name="stream">The BSA's bytes. Must support seeking, because the directory is at the end.</param>
	/// <exception cref="ArgumentException">The stream can't seek.</exception>
	/// <exception cref="InvalidDataException">The file isn't a valid BSA.</exception>
	public BSAReader(Stream stream)
	{
		// Zip entries are read front to back only, so they can't jump to the end.
		// Copy one into a MemoryStream first.
		if (!stream.CanSeek)
			throw new ArgumentException("A BSA can only be read from a seekable stream. Copy zip entries into a MemoryStream first.", nameof(stream));

		_stream = stream;
		_reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

		Records = ReadDirectory();
	}

	/// <summary>
	///		Opens a BSA file from disk.
	/// </summary>
	public static BSAReader Open(string path) => new(File.OpenRead(path));

	/// <summary>
	///		Reads one record's bytes.
	/// </summary>
	public byte[] ReadRecord(int index)
	{
		var record = Records[index];
		_stream.Position = record.Offset;
		return _reader.ReadBytes(record.Size);
	}

	public void Dispose()
	{
		_reader.Dispose();
		_stream.Dispose();
	}

	private List<BsaRecord> ReadDirectory()
	{
		if (_stream.Length < HeaderSize)
			throw new InvalidDataException("File is too small to be a BSA.");

		_stream.Position = 0;
		var count = _reader.ReadInt16();
		var type = _reader.ReadUInt16();

		var entrySize = type switch
		{
			NameDirectory => NameEntrySize,
			NumberDirectory => NumberEntrySize,
			_ => throw new InvalidDataException($"Unknown BSA directory type 0x{type:X4}."),
		};

		// The directory is the last count × entrySize bytes of the file.
		var directoryStart = _stream.Length - (long) count * entrySize;
		if (count < 0 || directoryStart < HeaderSize)
			throw new InvalidDataException($"BSA claims {count} records, which don't fit in a {_stream.Length}-byte file.");

		_stream.Position = directoryStart;

		var records = new List<BsaRecord>(count);
		long offset = HeaderSize;

		for (var i = 0; i < count; i++)
		{
			string name;
			uint? id = null;

			if (type == NameDirectory)
			{
				// 14 bytes, padded with zeros after the name. Cut at the first zero.
				var bytes = _reader.ReadBytes(NameLength);
				var end = Array.IndexOf(bytes, (byte) 0);
				name = System.Text.Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
			}
			else
			{
				id = _reader.ReadUInt32();
				name = id.Value.ToString();
			}

			var size = _reader.ReadInt32();

			// Every record must end before the directory starts. If not, the file is damaged or not a BSA.
			if (size < 0 || offset + size > directoryStart)
				throw new InvalidDataException($"Record {i} ({name}) runs past the end of the data.");

			records.Add(new BsaRecord(name, id, size, offset));
			offset += size;
		}

		return records;
	}
}
