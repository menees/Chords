namespace Menees.Chords.Db;

/// <summary>Indicates that a database was written by a newer unsupported schema.</summary>
public sealed class UnsupportedSchemaVersionException : DatabaseFormatException
{
	/// <summary>Initializes a new instance of the <see cref="UnsupportedSchemaVersionException"/> class.</summary>
	public UnsupportedSchemaVersionException(int version)
		: base($"Schema version {version} is not supported. The newest supported version is {ChordDatabase.CurrentSchemaVersion}.")
	{
		this.Version = version;
	}

	/// <summary>Gets the unsupported schema version.</summary>
	public int Version { get; }
}
