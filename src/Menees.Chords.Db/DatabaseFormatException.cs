namespace Menees.Chords.Db;

/// <summary>Indicates that database JSON cannot be read as a supported valid document.</summary>
public class DatabaseFormatException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="DatabaseFormatException"/> class.</summary>
	public DatabaseFormatException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="DatabaseFormatException"/> class.</summary>
	public DatabaseFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
