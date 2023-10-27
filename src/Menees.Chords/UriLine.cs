namespace Menees.Chords;

#region Using Directives

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A line that parses with <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> as an absolute URI.
/// </summary>
public sealed class UriLine : TextEntry
{
	#region Constructors

	private UriLine(string line, Uri uri, IEnumerable<Entry>? annotations = null)
		: base(line, annotations)
	{
		this.Uri = uri;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the parsed <see cref="Uri"/> for the current line.
	/// </summary>
	public Uri Uri { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse a header line from the current context.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if <paramref name="context"/>'s line was parsed. Null otherwise.</returns>
	public static UriLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		UriLine? result = null;

		Lexer lexer = context.CreateLexer(out IReadOnlyList<Entry> annotations);
		if (lexer.Read(skipLeadingWhiteSpace: true) && lexer.Token.Type == TokenType.Text)
		{
			// We have to pull the token text out here because we're about to move the lexer forward.
			string uriText = lexer.Token.Text;

			// Since annotations were removed earlier, make sure there's nothing else on the line.
			// And make sure the header text isn't a chord. We won't look for specific header values
			// since Ultimate Guitar has many header variations:
			// Intro, Outro, Verse, Verse #, Chorus, Interlude, Bridge, Pre-Chorus, Solo, Solo #, Break, Post-Chorus, Pre-Verse
			if ((!lexer.Read() || string.IsNullOrEmpty(lexer.ReadToEnd(skipTrailingWhiteSpace: true)))
				&& Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
			{
				result = new(uriText, uri, annotations);
			}
		}

		return result;
	}

	#endregion

	#region Protected Methods

	/// <summary>
	/// Writes <see cref="Uri"/>.
	/// </summary>
	/// <param name="writer">Used to write output.</param>
	protected override void WriteWithoutAnnotations(TextWriter writer)
	{
		Conditions.RequireNonNull(writer);
		writer.Write(this.Uri);
	}

	#endregion
}
