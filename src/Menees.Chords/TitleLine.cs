namespace Menees.Chords;

#region Using Directives

using System.Text.RegularExpressions;
using Menees.Chords.Parsers;

#endregion

/// <summary>
/// A human-friendly title line at the top of a document.
/// </summary>
/// <remarks>
/// A title line can contain other <see cref="Metadata"/> like artist, key, capo, etc.
/// </remarks>
public sealed class TitleLine : TextEntry
{
	#region Private Data Members

	private const string TitleRegexPattern = """
		(?inx)^\s* # Ignore leading whitespace
		(?<title>\S.*?)(\s+[\-–—]\s+(?<artist>\S.*?))? # Title with optional artist
		([\.,;][\t ](?<metadata>\S.*?))* # Optional other info. Change last * to ? to see all captures as one.
		[\.;]?\s*$ # Optional trailing "separator" and whitespace
		""";

	private static readonly Regex TitleRegex = new(TitleRegexPattern, RegexOptions.Compiled);

	#endregion

	#region Constructors

	private TitleLine(string text, IReadOnlyList<MetadataEntry> metadata)
		: base(text)
	{
		this.Metadata = metadata;
	}

	#endregion

	#region Public Properties

	/// <summary>
	/// Gets the ordered metadata parsed from the title line.
	/// </summary>
	public IReadOnlyList<MetadataEntry> Metadata { get; }

	#endregion

	#region Public Methods

	/// <summary>
	/// Tries to parse the current line as a title line.
	/// </summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A new instance if the current line appears to be a title line.</returns>
	public static TitleLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);

		TitleLine? result = null;

		if (context.LineNumber == 1)
		{
			Match match = TitleRegex.Match(context.LineText);
			if (match.Success && match.Groups.Count >= 2)
			{
				Group titleGroup = match.Groups["title"];
				if (titleGroup.Success)
				{
					List<MetadataEntry> metadata = [new("title", titleGroup.Value),];

					Group artistGroup = match.Groups["artist"];
					if (artistGroup.Success)
					{
						metadata.Add(new("artist", artistGroup.Value));
					}

					Group metadataGroup = match.Groups[nameof(metadata)];
					if (metadataGroup.Success)
					{
						foreach (Capture capture in metadataGroup.Captures.Cast<Capture>())
						{
							// We'll fallback to a "comment" directive, which is technically a formatting
							// directive not a metadata directive. But we want to return a MetadataEntry
							// instead of ChordProDirectiveLine because the former requires an argument.
							// https://www.chordpro.org/chordpro/chordpro-directives/#formatting-directives
							MetadataEntry entry = MetadataEntry.TryParse(capture.Value, null)
								?? new MetadataEntry("comment", capture.Value);
							metadata.Add(entry);
						}
					}

					result = new(context.LineText, metadata);
				}
			}
		}

		return result;
	}

	#endregion
}
