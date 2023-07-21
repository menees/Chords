namespace Menees.Chords.Parsers;

#region Using Directives

using System.Collections.Generic;

#endregion

/// <summary>
/// Provides the <see cref="DocumentParser"/>'s current line and parsing context.
/// </summary>
public sealed class LineContext
{
	#region Private Data Members

	private readonly List<Entry> entries = new();

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
	/// Gets the entries that have already been parsed.
	/// </summary>
	/// <remarks>
	/// This can be used for advanced lookback scenarios to see if the current line is in
	/// a specific section type (e.g., preceded by start_of_grid and not end_of_grid).
	/// </remarks>
	public IReadOnlyList<Entry> Entries => this.entries;

	#endregion

	#region Internal Methods

	internal void SetLineInfo(string lineText, int lineNumber)
	{
		this.LineText = lineText;
		this.LineNumber = lineNumber;
	}

	internal void Add(Entry entry)
	{
		this.entries.Add(entry);
	}

	#endregion
}
