namespace Menees.Chords.Parsers;

#region Using Directives

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

#endregion

/// <summary>
/// Provides the <see cref="DocumentParser"/>'s current line and parsing context.
/// </summary>
public sealed class LineContext
{
	#region Private Data Members

	private const string EndOfLineAnnotationPattern = """
		(?imnx) # Apply this with RegexOptions.RightToLeft
		^.* # Ignore anything to the beginning of the line
		(((?<parencomment>\(.*?\))|(?<starcomment>\*\*.*?\*\*)) # EOL comments can be surrounded by ( ) or ** **
		|(\s+(?<repeatcomment>(\d{1,2}x|x(\d{1,2})))) # EOL comment can be xN or Nx repeats.
		|((?<chord>[A-GIV1-79][a-z1-79#\-\+\^/]*[*~←↑↓→]?)\s*[=:]?\s*)?(?<definition>[\d_x](\-?[\d_x]){3,})(\s*[,;]\s*)?) # [Chord [=]] x or digit...
		\s*$ # Ignore trailing whitespace
		""";

	private static readonly Regex EndOfLineAnnotation = new(EndOfLineAnnotationPattern, RegexOptions.RightToLeft | RegexOptions.Compiled);

	private Dictionary<string, object>? state;
	private Lexer? lexer;
	private IReadOnlyList<Entry>? annotations;
	private Lexer? unannotatedLexer;

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

	/// <summary>
	/// Gets a newly initialized <see cref="Lexer"/> for the current line text after
	/// <paramref name="annotations"/> have been removed from the end of it.
	/// </summary>
	/// <param name="annotations">Returns any annotation entries that were removed from the end of the text line.</param>
	/// <returns>A new lexer over the text after annotations are removed.</returns>
	public Lexer CreateLexer(out IReadOnlyList<Entry> annotations)
	{
		// We'll cache these for future requests since it's not cheap to split these off.
		if (this.annotations == null || this.unannotatedLexer == null)
		{
			this.annotations = this.SplitAnnotations(out int annotationStartIndex);
			string unannotated = this.LineText[0..annotationStartIndex];
			this.unannotatedLexer = new(unannotated);
		}

		annotations = this.annotations;
		Lexer result = this.unannotatedLexer;
		result.Reset();
		return result;
	}

#endregion

	#region Internal Methods

	internal void SetLine(string lineText)
	{
		// Make line number 1-based.
		this.LineNumber++;
		this.LineText = lineText;

		// We'll need new lexers on the next request.
		this.lexer = null;
		this.unannotatedLexer = null;
		this.annotations = null;
	}

	#endregion

	#region Private Methods

	private static Comment CreateComment(string text, string start, string end)
	{
		string comment = text[start.Length..^end.Length];
		string trimStart = comment.TrimStart();
		string trimmed = trimStart.TrimEnd();
		string prefix = start + comment[..^trimStart.Length];
		string suffix = comment[^(trimStart.Length - trimmed.Length)..] + end;
		Comment result = new(trimmed, prefix, suffix);
		return result;
	}

	private List<Entry> SplitAnnotations(out int annotationStartIndex)
	{
		List<Entry> result = [];
		annotationStartIndex = this.LineText.Length;

		bool tryComment = this.Parser.TryParseComment;
		bool tryDefinition = this.Parser.TryChordDefinitions;

		Match match;
		while ((tryComment || tryDefinition) &&
			(match = EndOfLineAnnotation.Match(this.LineText.Substring(0, annotationStartIndex))).Success)
		{
			if (match.Groups.Count <= 1)
			{
				break;
			}

			Group group;
			if (tryComment && (group = match.Groups["parencomment"]).Success)
			{
				result.Add(CreateComment(group.Value, "(", ")"));
				annotationStartIndex = group.Index;
			}
			else if (tryComment && (group = match.Groups["starcomment"]).Success)
			{
				result.Add(CreateComment(group.Value, "**", "**"));
				annotationStartIndex = group.Index;
			}
			else if (tryComment && (group = match.Groups["repeatcomment"]).Success)
			{
				result.Add(CreateComment(group.Value, string.Empty, string.Empty));
				annotationStartIndex = group.Index;
			}
			else if (tryDefinition && (group = match.Groups["definition"]).Success)
			{
				Group chord = match.Groups["chord"];
				ChordDefinition? chordDefinition;
				if (chord.Success && (chordDefinition = ChordDefinition.TryParse(chord.Value, group.Value)) != null)
				{
					List<ChordDefinition> definitions = [chordDefinition];
					ChordDefinitions? previousDefinitionEntry = result.Count > 0 ? result[^1] as ChordDefinitions : null;
					if (previousDefinitionEntry != null)
					{
						definitions.AddRange(previousDefinitionEntry.Definitions);
						result[^1] = new ChordDefinitions(definitions);
					}
					else
					{
						result.Add(new ChordDefinitions(definitions));
					}

					annotationStartIndex = chord.Index;
				}
				else
				{
					// We'll stop if we see an unnamed definition because it might just be a short tab notation. (e.g., 1-2-3).
					break;
				}
			}
			else
			{
				// If we hit this, then the regex was probably updated without updating the code above.
				throw new InvalidOperationException($"End of line annotation matched {match.Value} but without a support group.");
			}
		}

		result.Reverse();
		return result;
	}

	#endregion
}
