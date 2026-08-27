namespace Menees.Chords.Db;

/// <summary>Represents a book-store operation failure.</summary>
public class BookStoreException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="BookStoreException"/> class.</summary>
	public BookStoreException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="BookStoreException"/> class.</summary>
	public BookStoreException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
