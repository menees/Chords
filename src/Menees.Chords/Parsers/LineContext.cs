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

	#region Private Methods

	/// <summary>
	/// Converts tabs to spaces in the specified <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to expand tabs in.</param>
	/// <param name="tabWidth">The number of spaces for a single tab character.</param>
	/// <returns><paramref name="text"/> with tabs expanded as spaces.</returns>
	[return: NotNullIfNotNull(nameof(text))]
	public static string? ConvertTabsToSpaces(string? text, int tabWidth)
	{
		string? result = text;

#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'. 'string.Contains' isn't in .NET Framework 4.8.
		if (text != null && text.IndexOf('\t') >= 0)
		{
			StringBuilder sb = new(text.Length);
			foreach (char ch in text)
			{
				if (ch == '\t')
				{
					// A tab always expands to at least one character.
					sb.Append(' ');

					// And it may expand up to TabWidth characters.
					while (sb.Length % tabWidth != 0)
					{
						sb.Append(' ');
					}
				}
				else
				{
					sb.Append(ch);
				}
			}

			result = sb.ToString();
		}
#pragma warning restore CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'

		return result;
	}

	#endregion

	#region Internal Methods

	internal void SetLine(string lineText)
	{
		// Make line number 1-based.
		this.LineNumber++;

		if (this.Parser.TabWidth > 0)
		{
			this.LineText = ConvertTabsToSpaces(lineText, this.Parser.TabWidth);
		}
		else
		{
			this.LineText = lineText;
		}
	}

	internal void Add(Entry entry)
	{
		this.entries.Add(entry);
	}

	#endregion
}
