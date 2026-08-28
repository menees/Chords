namespace Menees.Chords.Db;

/// <summary>Lists the SHA-256 hash of every payload in a <c>.mcbbak</c> archive.</summary>
public sealed class BookBackupManifest
{
	#region Public Properties

	/// <summary>Gets or sets the backup format version.</summary>
	public int FormatVersion { get; set; } = 1;

	/// <summary>Gets or sets payload paths and lowercase SHA-256 hashes.</summary>
	public SortedDictionary<string, string> Entries { get; set; } = new(StringComparer.Ordinal);

	#endregion
}
