namespace Menees.Chords;

#region Using Directives

using System.IO;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A "name: argument" pair of metadata (e.g., title, artist, key, capo).
/// </summary>
/// <remarks>
/// This can be on a line by itself, or it can be part of another line (e.g, a <see cref="TitleLine"/>).
/// This is similar to a <see cref="ChordProDirectiveLine"/> except it's not surrounded by braces,
/// the <see cref="Argument"/> is required, and only a few known <see cref="Name"/>s are supported.
/// <see cref="Argument"/> is required because name-only metadata will be treated as a
/// <see cref="Comment"/>.
/// </remarks>
/// <seealso href="https://www.chordpro.org/chordpro/chordpro-directives/#meta-data-directives"/>
public sealed class MetadataEntry : Entry
{
	#region Private Data Members

	// This list is from https://www.chordpro.org/chordpro/chordpro-directives/#meta-data-directives.
	private static readonly HashSet<string> AllowedNames = new(ChordParser.Comparer)
	{
		"album",
		"artist",
		"capo",
		"composer",
		"copyright",
		"duration",
		"key",
		"lyricist",
		"meta",
		"sorttitle",
		"st",
		"subtitle",
		"t",
		"tempo",
		"time",
		"title",
		"year",
	};

	private static readonly Dictionary<string, string> TranslatedNames = new(ChordParser.Comparer)
	{
		["bpm"] = "tempo",
	};

	#endregion

	#region Constructors

	internal MetadataEntry(string name, string argument)
	{
		// We can allow any name here since we're not parsing.
		// Presumably, the caller has verified the name (and argument).
		this.Name = name.ToLower();
		this.Argument = argument;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the directive's name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the directive's argument (i.e., the part after the colon separator).
	/// </summary>
	public string Argument { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a "name: argument" metadata entry.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the current trimmed line is in "name: argument" form
	/// with a known metadata name.</returns>
	public static MetadataEntry? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		MetadataEntry? result = null;

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		if (lexer.Read(skipLeadingWhiteSpace: true)
			&& lexer.Token.Type == TokenType.Text)
		{
			string line = lexer.ReadToEnd(skipTrailingWhiteSpace: true);
			result = TryParse(line, annotations);
		}

		return result;
	}

	#endregion

	#region Internal Methods

	internal static MetadataEntry? TryParse(string text, IReadOnlyList<Entry>? annotations = null)
	{
		MetadataEntry? result = null;

		string[] parts = text.Split(':');
		if (parts.Length == 2)
		{
			string name = parts[0].Trim();
			string argument = parts[1].Trim();

			// When parsing an arbitrary line, we need to restrict to a known set of allowed names
			// to avoid parsing any line that contains a single colon as a MetadataEntry.
			if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(argument))
			{
				if (AllowedNames.Contains(name))
				{
					result = new(name, argument);
				}
				else if (TranslatedNames.TryGetValue(name, out string? translation))
				{
					result = new(translation, argument);
				}
			}
		}

		if (annotations != null)
		{
			result?.AddAnnotations(annotations);
		}

		return result;
	}

	#endregion

	#region Protected Methods

	/// <inheritdoc/>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		writer.Write(this.Name);
		writer.Write(": ");
		writer.Write(this.Argument);
	}

	#endregion
}
