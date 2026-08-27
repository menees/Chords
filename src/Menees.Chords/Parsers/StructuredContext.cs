namespace Menees.Chords.Parsers;

/// <summary>
/// Provides information for parsing a structured document representation.
/// </summary>
public abstract class StructuredContext
{
	#region Constructors

	internal StructuredContext(DocumentParser parser)
	{
		this.Parser = parser;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the associated document parser.
	/// </summary>
	public DocumentParser Parser { get; }

	#endregion
}
