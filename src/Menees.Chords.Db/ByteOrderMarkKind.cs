namespace Menees.Chords.Db;

/// <summary>Identifies a text file's byte-order mark.</summary>
public enum ByteOrderMarkKind
{
	/// <summary>No byte-order mark is present.</summary>
	None,

	/// <summary>A UTF-8 byte-order mark is present.</summary>
	Utf8,

	/// <summary>A little-endian UTF-16 byte-order mark is present.</summary>
	Utf16LittleEndian,

	/// <summary>A big-endian UTF-16 byte-order mark is present.</summary>
	Utf16BigEndian,

	/// <summary>A little-endian UTF-32 byte-order mark is present.</summary>
	Utf32LittleEndian,

	/// <summary>A big-endian UTF-32 byte-order mark is present.</summary>
	Utf32BigEndian,
}
