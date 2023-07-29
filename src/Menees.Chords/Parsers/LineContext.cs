namespace Menees.Chords.Parsers;

#region Using Directives

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

#endregion

/// <summary>
/// Provides the <see cref="DocumentParser"/>'s current line and parsing context.
/// </summary>
public sealed class LineContext
{
	#region Private Data Members

	private Dictionary<string, object>? state;
	private Lexer? lexer;

	#endregion

	#region Constructors

	internal LineContext(DocumentParser parser)
	{
		this.Parser = parser;
		this.LineText = string.Empty;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the associated document parser.
	/// </summary>
	public DocumentParser Parser { get; }

	/// <summary>
	/// Gets the text for the line currently being parsed.
	/// </summary>
	public string LineText { get; private set; }

	/// <summary>
	/// Gets the 1-based index for the line currently being parsed.
	/// </summary>
	public int LineNumber { get; private set; }

	/// <summary>
	/// Gets a case-insensitive dictionary to store custom parsing state information.
	/// </summary>
	/// <remarks>
	/// This can be used to store information that needs to be shared between multiple
	/// line parsers. For example, if a start_of_grid <see cref="ChordProDirectiveLine"/> is
	/// parsed, it could store that state so <see cref="ChordProGridLine"/> can know a grid
	/// is active, and an end_of_grid <see cref="ChordProDirectiveLine"/> can clear that state.
	/// </remarks>
	public IDictionary<string, object> State => this.state ??= new(ChordParser.Comparer);

	#endregion

	#region Public Methods

	/// <summary>
	/// Gets a newly initialized <see cref="Lexer"/> for the current <see cref="LineText"/>.
	/// </summary>
	public Lexer CreateLexer()
	{
		// We'll cache the lexer for use by multiple line parsers, but we'll Reset before returning the lexer.
		// This way each line parser starts from the beginning of the text.
		this.lexer ??= new(this.LineText);
		this.lexer.Reset();
		return this.lexer;
	}

	#endregion

	#region Internal Methods

	internal void SetLine(string lineText)
	{
		// Make line number 1-based.
		this.LineNumber++;
		this.LineText = lineText;

		// We'll need a new lexer on the next request.
		this.lexer = null;
	}

	#endregion
}
