namespace Menees.Chords.Db;

/// <summary>Indicates that a staged writer was based on a stale book version.</summary>
public sealed class BookStoreConcurrencyException : BookStoreException
{
	/// <summary>Initializes a new instance of the <see cref="BookStoreConcurrencyException"/> class.</summary>
	public BookStoreConcurrencyException()
		: base("The book changed after this write was staged.")
	{
	}
}
