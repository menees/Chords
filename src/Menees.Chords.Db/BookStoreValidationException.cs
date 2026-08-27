namespace Menees.Chords.Db;

/// <summary>Indicates that staged database metadata and assets were inconsistent.</summary>
public sealed class BookStoreValidationException : BookStoreException
{
	/// <summary>Initializes a new instance of the <see cref="BookStoreValidationException"/> class.</summary>
	public BookStoreValidationException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="BookStoreValidationException"/> class.</summary>
	public BookStoreValidationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
