#region Using Directives

using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class MetadataDirectiveImportTests
{
	#region Public Methods

	[TestMethod]
	public void UpdatesOnlySelectedValueAndPreservesEveryOtherCharacter()
	{
		const string Source = "\t{T  :   Old title  } \r\n{unknown: untouched}\n# comment\rC    G\nWords";
		MetadataDirectiveImport import = new(Source, new("New title", [], []));
		string changed = import.Apply([new("title", 0)]);
		changed.ShouldBe(Source.Replace("Old title", "New title", StringComparison.Ordinal));
		EditorTextChange change = import.Preview([new("title", 0)]).Single();
		change.Before.ShouldBe("Old title");
		change.After.ShouldBe("New title");
		change.LineNumber.ShouldBe(1);
	}

	[TestMethod]
	public void InsertsAfterLeadingDirectivesButBeforeTheFirstEnvironment()
	{
		const string Source = "{title: Same}\r\n{unknown: keep}\r\n{start_of_verse: Verse}\r\nC  G\r\nWords";
		SongEditMetadata metadata = new("Same", [], []) { Scalars = new Dictionary<string, IReadOnlyList<string>> { ["tempo"] = ["110"] } };
		MetadataDirectiveImport import = new(Source, metadata);
		import.Apply([new("tempo", null)]).ShouldBe(Source.Replace("{start_of_verse", "{tempo: 110}\r\n{start_of_verse", StringComparison.Ordinal));
	}

	[TestMethod]
	[DataRow("C  G\nWords", "{title: New}\nC  G\nWords")]
	[DataRow("\n# comment\nWords", "{title: New}\n\n# comment\nWords")]
	[DataRow("", "{title: New}")]
	public void PureTextRequiresExplicitSelectionAndRetainsBody(string source, string expected)
	{
		MetadataDirectiveImport import = new(source, new("New", [], []));
		import.Apply([]).ShouldBe(source);
		import.Apply([new("title", null)]).ShouldBe(expected);
	}

	[TestMethod]
	public void LastLeadingDirectiveWithoutNewlineGetsASeparatorButNoTrailingNewline()
	{
		MetadataDirectiveImport import = new("{title: Existing}", new("Existing", ["Artist"], []));
		import.Apply([new("artist", null)]).ShouldBe("{title: Existing}" + Environment.NewLine + "{artist: Artist}");
	}

	[TestMethod]
	public void DuplicateDirectivesRequireSelectionAndDoNotDeleteOtherOccurrences()
	{
		MetadataDirectiveImport import = new("{t: One}\n{title: Two}\n[C]Body", new("Chosen", [], []));
		import.Proposals.Single().Occurrences.Count.ShouldBe(2);
		Should.Throw<InvalidOperationException>(() => import.Apply([new("title", null)]));
		import.Apply([new("title", 1)]).ShouldBe("{t: One}\n{title: Chosen}\n[C]Body");
	}

	[TestMethod]
	public void NormalizesMetaNamesAndPreservesMetaSyntax()
	{
		SongEditMetadata metadata = new(string.Empty, [], []) { Scalars = new Dictionary<string, IReadOnlyList<string>> { ["duration"] = ["4:28"] } };
		MetadataDirectiveImport import = new("{meta: duration 180}\r\n[C]Body", metadata);
		import.Apply([new("duration", 0)]).ShouldBe("{meta: duration 4:28}\r\n[C]Body");
	}

	[TestMethod]
	public void ImportsEveryArtistAndMapsTagsToMetaDirectives()
	{
		MetadataDirectiveImport import = new("[C]Body", new(string.Empty, ["One", "Two"], ["Practice"]));
		import.Apply([new("artist", null), new("tag", null)]).ShouldBe(
			string.Join(Environment.NewLine, "{artist: One}", "{artist: Two}", "{meta: tag Practice}", "[C]Body"));
	}

	[TestMethod]
	public void RejectsSourceInjectionAndConditionalOrStructuredReplacement()
	{
		MetadataDirectiveImport injection = new("[C]Body", new("Bad}\n{title: injected", [], []));
		Should.Throw<InvalidOperationException>(() => injection.Apply([new("title", null)]));
		MetadataDirectiveImport conditional = new("{title-guitar: Conditional}", new("New", [], []));
		Should.Throw<InvalidOperationException>(() => conditional.Apply([new("title", 0)]));
		MetadataDirectiveImport structured = new("{meta: name=title value=Old}", new("New", [], []));
		Should.Throw<InvalidOperationException>(() => structured.Apply([new("title", 0)]));
		Should.Throw<InvalidOperationException>(() => new MetadataDirectiveImport(
			"<song><title>XML</title><lyrics>[C]Words</lyrics></song>", new("New", [], [])));
	}

	[TestMethod]
	[DataRow("268", true, 268)]
	[DataRow("4:28", true, 268)]
	[DataRow("0", true, 0)]
	[DataRow("4:60", false, 0)]
	[DataRow("4:8", false, 0)]
	[DataRow("-1", false, -1)]
	[DataRow("999999999:00", false, 0)]
	public void DurationParsingDoesNotGuess(string value, bool valid, int expected)
	{
		SongMetadata.TryParseDuration(value, out int seconds).ShouldBe(valid);
		if (valid)
		{
			seconds.ShouldBe(expected);
		}
	}

	#endregion
}
