namespace Menees.Chords.Parsers;

/// <summary>
/// Provides strongly typed data for parsing a structured document representation.
/// </summary>
/// <typeparam name="T">The type of structured data.</typeparam>
public sealed class StructuredContext<T> : StructuredContext
	where T : notnull
{
	#region Constructors

	internal StructuredContext(DocumentParser parser, T data)
		: base(parser)
	{
		this.Data = data;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the structured data to parse.
	/// </summary>
	public T Data { get; }

	#endregion
}
