namespace Menees.Chords;

#region Using Directives

using System.Globalization;
using System.Linq;
using System.Text;
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
		(?<title>\S.*?)(\s+([\-–—]|(((Chords|Tab|Official|Bass|Ukulele|Power|Guitar\s+Pro)\s+)?by))\s+(?<artist>\S.*?))? # Title with optional artist
		([\.,;][\t ](?<metadata>\S.*?))* # Optional other info. Change last * to ? to see all captures as one.
		[\.;]?\s*$ # Optional trailing "separator" and whitespace
		""";

	private const string TitleMetadataName = "title";
	private const string ArtistMetadataName = "artist";
	private const string CommentMetadataName = "comment";
	private const string SpaceEnDashSpace = " – ";

	private static readonly Regex TitleRegex = new(TitleRegexPattern, RegexOptions.Compiled);

	private static readonly string[] OmitWords = [nameof(Chords), "Tab", "Tabs", "Official", "Bass", "Ukulele", "Power", "Guitar Pro"];

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
				Group titleGroup = match.Groups[TitleMetadataName];
				if (titleGroup.Success)
				{
					List<MetadataEntry> metadata = [new(TitleMetadataName, titleGroup.Value)];

					Group artistGroup = match.Groups[ArtistMetadataName];
					if (artistGroup.Success)
					{
						metadata.Add(new(ArtistMetadataName, artistGroup.Value));
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
								?? new MetadataEntry(CommentMetadataName, capture.Value);
							metadata.Add(entry);
						}
					}

					result = new(context.LineText, metadata);
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Tries to parse the URI as a title line.
	/// </summary>
	/// <param name="uri">The URI to parse.</param>
	/// <returns>A new instance if the URI contains title information.</returns>
	public static TitleLine? TryParse(Uri uri)
	{
		TitleLine? result = null;

		if (uri.Scheme == "https" || uri.Scheme == "http")
		{
			const StringComparison Compare = StringComparison.OrdinalIgnoreCase;
			string host = uri.Host;
			bool MatchHostPath(string match, byte pathParts, out string[] path)
			{
				bool result = false;
				path = [];

				if (host.Equals(match, Compare) || host.EndsWith($".{match}", Compare))
				{
					string[] parts = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped)
						.Split('/', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length == pathParts)
					{
						path = parts;
						result = true;
					}
				}

				return result;
			}

			static string[] SplitWords(string text) => text.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(ToTitleCase).ToArray();

			const int ThreePathParts = 3;
			if (MatchHostPath("ultimate-guitar.com", ThreePathParts, out string[] path) && path[0].Equals("tab", Compare))
			{
				// https://tabs.ultimate-guitar.com/tab/led-zeppelin/how-many-more-times-official-3714365
				result = TryCreate(SplitWords(path[2]), token => uint.TryParse(token, out _), OmitWords, SplitWords(path[1]));
			}
			else if (MatchHostPath("chordu.com", 1, out path))
			{
				// https://chordu.com/chords-tabs-dinah-washington-make-me-a-present-of-you-id_9txY8qbaLrE
				const string Prefix = "chords-tabs-";
				if (path[0].StartsWith(Prefix, Compare))
				{
					result = TryCreate(SplitWords(path[0].Substring(Prefix.Length)), token => token.Any(char.IsDigit));
				}
			}
			else if (MatchHostPath("yalp.io", 2, out path) && path[0].Equals("chords", Compare))
			{
				// https://www.yalp.io/chords/dinah-washington-make-me-a-present-of-you-a1f9
				result = TryCreate(SplitWords(path[1]), token => token.Any(char.IsDigit));
			}
			else if (MatchHostPath("e-chords.com", ThreePathParts, out path))
			{
				// https://m.e-chords.com/chords/lynyrd-skynyrd/freebird
				// https://www.e-chords.com/tabs/lynyrd-skynyrd/free-bird
				if (path[0].Equals("chords", Compare) || path[0].Equals("tabs"))
				{
					result = TryCreate(SplitWords(path[2]), null, [], artistWords: SplitWords(path[1]));
				}
			}
			else if (MatchHostPath("songsterr.com", ThreePathParts, out path) && path[0].Equals("a", Compare) && path[1].Equals("wsa", Compare))
			{
				// https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-chords-s21
				// https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-tab-s21
				// https://www.songsterr.com/a/wsa/lynyrd-skynyrd-free-bird-tab-s21t0
				result = TryCreate(SplitWords(path[2]), token => token.Any(char.IsDigit), OmitWords);
			}
			else if (MatchHostPath("guitartabsexplorer.com", 2, out path))
			{
				// https://www.guitartabsexplorer.com/lynyrd-skynyrd-Tabs/free-bird-tab.php
				const string Extension = ".php";
				if (path[1].EndsWith(Extension, Compare))
				{
					result = TryCreate(SplitWords(path[1][0..^Extension.Length]), null, OmitWords, SplitWords(path[0]), OmitWords);
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Gets the text representation of the title using the <see cref="Metadata"/> entries.
	/// </summary>
	public string ToMetadataString()
	{
		StringBuilder sb = new();

		// The title metadata must be first.
		sb.Append(this.Metadata[0].Argument);
		int skipCount = 1;

		if (this.Metadata.Count > 1)
		{
			if (this.Metadata[1].Name == ArtistMetadataName)
			{
				sb.Append(SpaceEnDashSpace);
				sb.Append(this.Metadata[1].Argument);
				skipCount++;
			}

			sb.Append('.');
		}

		foreach (MetadataEntry metadata in this.Metadata.Skip(skipCount))
		{
			sb.Append(' ');
			if (metadata.Name != CommentMetadataName)
			{
				string name = ToTitleCase(MetadataEntry.Untranslate(metadata.Name));
				sb.Append(name);
				sb.Append(": ");
			}

			sb.Append(metadata.Argument);
			sb.Append('.');
		}

		string result = sb.ToString();
		return result;
	}

	#endregion

	#region Internal Methods

	internal static string ToTitleCase(string word) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word);

	#endregion

	#region Private Methods

	private static TitleLine? TryCreate(
		string[] titleWords,
		Predicate<string>? isTitleId,
		IEnumerable<string>? omitTitleWords = null,
		string[]? artistWords = null,
		IEnumerable<string>? omitArtistWords = null)
	{
		TitleLine? result = null;

		static string[] OmitWords(string[] input, IEnumerable<string>? omitWords = null, int skipFinal = 0)
		{
			int finalLength = input.Length - skipFinal;
			omitWords ??= [];
			while (finalLength >= 1 && omitWords.Contains(input[finalLength - 1]))
			{
				finalLength--;
			}

			string[] result = input.Take(finalLength).ToArray();
			return result;
		}

		// Every URL format we support ends with a unique ID token.
		if (isTitleId == null || (titleWords.Length >= 2 && isTitleId(titleWords[^1])))
		{
			titleWords = OmitWords(titleWords, omitTitleWords, isTitleId == null ? 0 : 1);
			string title = string.Join(" ", titleWords);

			StringBuilder lineText = new(title);
			List<MetadataEntry> metadata = [new(TitleMetadataName, title)];
			if (artistWords != null)
			{
				artistWords = OmitWords(artistWords, omitArtistWords);
				string artist = string.Join(" ", artistWords);
				lineText.Append(SpaceEnDashSpace);
				lineText.Append(artist);
				metadata.Add(new(ArtistMetadataName, artist));
			}

			result = new(lineText.ToString(), metadata);
		}

		return result;
	}

	#endregion
}
