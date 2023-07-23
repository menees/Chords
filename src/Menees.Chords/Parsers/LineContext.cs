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

	private readonly List<Entry> entries = new();
	private Dictionary<string, object>? state;

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

	/// <summary>
	/// Gets a case-insensitive dictionary to store custom parsing state information.
	/// </summary>
	/// <remarks>
	/// This can be used to store information that needs to be shared between multiple
	/// line parsers. For example, if a start_of_grid <see cref="ChordProDirective"/> is
	/// parsed, it could store that state so <see cref="ChordProGridLine"/> can know a grid
	/// is active, and an end_of_grid <see cref="ChordProDirective"/> can clear that state.
	/// </remarks>
	public IDictionary<string, object> State => this.state ??= new(StringComparer.CurrentCultureIgnoreCase);

	#endregion

	#region Internal Methods

	internal void SetLine(string lineText)
	{
		// Make line number 1-based.
		this.LineNumber++;
		this.LineText = lineText;
	}

	internal void Add(Entry entry)
	{
		this.entries.Add(entry);
	}

	#endregion
}
