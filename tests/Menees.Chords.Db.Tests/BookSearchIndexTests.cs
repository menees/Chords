#region Using Directives

using System.Diagnostics;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookSearchIndexTests
{
	#region Private Data

	private const int GeneratedSongCount = 10_000;
	private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(100);

	#endregion

	#region Public Methods

	[TestMethod]
	public void RefreshOneSongReordersOnlyItsSnapshotAndKeepsOtherEntriesDetached()
	{
		ChordDatabase database = CreateGeneratedDatabase(3);
		Song changed = database.Songs[0], unrelated = database.Songs[1];
		changed.Title = "Zebra";
		unrelated.Title = "Middle";
		unrelated.Artists = ["Original artist"];
		BookSearchIndex index = new(database);
		unrelated.Title = "Uncommitted change";
		unrelated.Artists = ["Uncommitted artist"];
		changed.Title = "Aardvark";
		changed.Artists = ["Edited artist"];
		changed.Tags = ["Edited tag"];
		SongFile recovery = new() { Id = Guid.NewGuid(), SongId = changed.Id, IsArchived = true, RecoveryVersion = new() };
		index.RefreshSong(changed, [recovery]);
		index.Search(string.Empty)[0].SongId.ShouldBe(changed.Id);
		index.Search("artist:edited tag:edited recovery:true archivedfiles:true").Single().SongId.ShouldBe(changed.Id);
		index.Search("Zebra").ShouldBeEmpty();
		index.Search("Middle").Single().Artists.ShouldBe(["Original artist"]);
		index.Search("Uncommitted").ShouldBeEmpty();
		index.RefreshSong(changed, []);
		index.Search("recovery:true").ShouldBeEmpty();
	}

	[TestMethod]
	public void SearchIsCaseAndDiacriticInsensitive()
	{
		ChordDatabase database = CreateGeneratedDatabase(3);
		database.Songs[1].Title = "Déjà Vu";
		BookSearchIndex index = new(database);

		IReadOnlyList<BookSearchHit> matches = index.Search("DEJA");

		matches.Single().Title.ShouldBe("Déjà Vu");
	}

	[TestMethod]
	public void SearchSupportsPhrasesFieldsAndFilePredicatesWithoutReReadingMutableEntities()
	{
		ChordDatabase database = CreateGeneratedDatabase(3);
		Song song = database.Songs[0];
		song.Title = "Blue Moon Tonight";
		song.Artists = ["Renée Jones"];
		song.DurationSeconds = 268;
		song.DisplayOverride = new() { FontSize = 20 };
		song.MetronomeOverride = new() { BeatsPerMinute = 90 };
		song.SourceMetadata["key"] = [new() { Value = "C" }];
		song.SourceMetadata["capo"] = [new() { Value = "2" }];
		song.SourceMetadata["genre"] = [new() { Value = "Folk Rock" }];
		database.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = song.Id });
		database.SongFiles.Add(new() { Id = Guid.NewGuid(), SongId = song.Id });
		BookSearchIndex index = new(database);
		string query = "\"blue moon\" artist:\"renee jones\" key:c genre:folk capo:2 duration:4:28 display:true metronome:true multiple:true archived:false";
		index.Search(query).Single().SongId.ShouldBe(song.Id);
		index.Search("duration:268").Single().SongId.ShouldBe(song.Id);
		index.Search("\"moon blue\"").ShouldBeEmpty();
		index.Search("key:cm").ShouldBeEmpty();
		index.Search("duration:26").ShouldBeEmpty();
		index.Search("artist:blue").ShouldBeEmpty();
		index.Search("title:").ShouldBeEmpty();
		index.Search("recovery:true").ShouldBeEmpty();
		index.Search("display:false").Count.ShouldBe(2);
		index.Search("\"blue moon").Single().SongId.ShouldBe(song.Id);
		song.Artists.Clear();
		song.SourceMetadata.Clear();
		index.Search(query).Single().Artists.ShouldBe(["Renée Jones"]);
		song.IsArchived = true;
		song.MetronomeOverride = null;
		index.RefreshSongPredicates(song);
		index.Search("archived:true metronome:false artist:renee").Single().SongId.ShouldBe(song.Id);
		index.Search("key:c").Single().SongId.ShouldBe(song.Id);
	}

	[TestMethod]
	public void AlbumAndTimeSearchUseEffectiveDetachedMetadataAndIncrementalRefresh()
	{
		ChordDatabase database = CreateGeneratedDatabase(3);
		Song song = database.Songs[0];
		song.SourceMetadata["album"] = [new() { Value = "Source album" }];
		song.SourceMetadata["time"] = [new() { Value = "3/4" }];
		song.MetadataOverrides["album"] = ["Déjà Blue", "Live; Sessions"];
		song.MetadataOverrides["time"] = ["6/8"];
		BookSearchIndex index = new(database);
		index.Search("album:\"deja blue\" time:6/8").Single().SongId.ShouldBe(song.Id);
		index.Search("album:\"live; sessions\"").Single().SongId.ShouldBe(song.Id);
		index.Search("album:source").ShouldBeEmpty();
		index.Search("time:3/4").ShouldBeEmpty();
		index.Search("time:6").ShouldBeEmpty();
		song.MetadataOverrides["album"] = [];
		song.MetadataOverrides["time"] = ["12/8"];
		index.Search("album:deja time:6/8").Single().SongId.ShouldBe(song.Id);
		index.RefreshSong(song, []);
		index.Search("album:deja").ShouldBeEmpty();
		index.Search("album:source").ShouldBeEmpty();
		index.Search("time:6/8").ShouldBeEmpty();
		index.Search("time:12/8").Single().SongId.ShouldBe(song.Id);
	}

	[TestMethod]
	public void GenreSearchUsesExtractedMetaDirectiveIndependentlyOfTags()
	{
		SongFileAnalysis analysis = SongFileAnalyzer.Analyze(
			System.Text.Encoding.UTF8.GetBytes("{title: Genre example}\n{meta: genre Folk}\n{tag: Christian}\n[C]Song"), "Genre.cho");
		ChordDatabase database = CreateGeneratedDatabase(1);
		Song song = database.Songs[0];
		song.SourceMetadata = new(analysis.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()), StringComparer.Ordinal);
		song.Tags = ["Christian"];
		BookSearchIndex index = new(database);
		index.Search("genre:folk tag:Christian").Single().SongId.ShouldBe(song.Id);
		index.Search("genre:Christian").ShouldBeEmpty();
		index.Search("tag:folk").ShouldBeEmpty();
	}

	[TestMethod]
	public void FieldExclusionsCombineWithOtherTermsAndIncludeMissingValues()
	{
		ChordDatabase database = CreateGeneratedDatabase(4);
		database.Songs[0].Tags = ["LyricOnly", "Christian"];
		database.Songs[1].Tags = ["Christian"];
		database.Songs[2].Tags = [];
		database.Songs[3].Tags = ["LýricOnly", "Practice"];
		database.Songs[0].Artists = ["Renée Jones"];
		BookSearchIndex index = new(database);
		index.Search("tag:!lyriconly").Select(hit => hit.SongId).ShouldBe([database.Songs[1].Id, database.Songs[2].Id]);
		index.Search("tag:Christian tag:!LyricOnly").Single().SongId.ShouldBe(database.Songs[1].Id);
		index.Search("artist:!\"Renee Jones\"").Count.ShouldBe(3);
		index.Search("tag:!").ShouldBeEmpty();
		index.Search("key:!c").Count.ShouldBe(4);
		database.Songs[1].Tags.Add("LyricOnly");
		index.RefreshSong(database.Songs[1], []);
		index.Search("tag:Christian tag:!LyricOnly").ShouldBeEmpty();
		index.Search("archived:!true").Count.ShouldBe(4);
		index.Search("!Christian").ShouldBeEmpty();
	}

	[TestMethod]
	[DataRow("Christian", "tag:*", true)]
	[DataRow("Christian", "tag:!*", false)]
	[DataRow("Christian", "tag:Christ*", true)]
	[DataRow("Christian", "tag:Ch?istian", true)]
	[DataRow("Christian", "tag:Ch?stian", false)]
	[DataRow("Christian", "tag:rist*", false)]
	[DataRow("Christian", "tag:*rist*", true)]
	[DataRow("Christian", "tag:rist", true)]
	[DataRow("Christian", "tag:***", true)]
	[DataRow("Christian", "tag:*?*?*?*", true)]
	[DataRow("Christian", "tag:*.*", false)]
	[DataRow("Christian", "tag:[Cc]hristian", false)]
	[DataRow("[Cc]hristian", "tag:[Cc]hristian", true)]
	[DataRow("*", @"tag:\*", true)]
	[DataRow("*", @"tag:!\*", false)]
	[DataRow("Star*value", @"tag:Star\*value", true)]
	[DataRow("Star*value", @"tag:Star\**", true)]
	[DataRow("Star*value", "tag:Star?value", true)]
	[DataRow("What?", @"tag:What\?", true)]
	[DataRow("!Important", @"tag:\!Important", true)]
	[DataRow("!Important", "tag:\"!Important\"", true)]
	[DataRow("!Important", @"tag:!\!Important", false)]
	[DataRow("Not Important", @"tag:\!Important", false)]
	[DataRow("Live Sessions", "tag:\"Live *\"", true)]
	[DataRow("Live Sessions", @"tag:Live\ Sessions", true)]
	[DataRow("Live Sessions", "tag:\"Live *", true)]
	[DataRow("Live \"Special\"", "tag:\"Live \\\"Special\\\"\"", true)]
	[DataRow(@"Path\Name", @"tag:Path\\Name", true)]
	[DataRow(@"Path\Name", @"tag:*\\Name", true)]
	[DataRow(@"Path\Name", @"tag:*\\N?me", true)]
	[DataRow("Path" + @"\", @"tag:Path\", true)]
	[DataRow("Caf\u00e9", "TAG:caf?", true)]
	[DataRow("A.B+$()", "tag:A.B+$()", true)]
	[DataRow("Christian", "tag:", false)]
	[DataRow("Christian", "tag:!", false)]
	[DataRow("Christian", "tag:!\"\"", false)]
	public void SimpleWildcardGrammarPreservesLiteralOperatorsAndPunctuation(string tag, string query, bool expected)
	{
		ChordDatabase database = CreateGeneratedDatabase(1);
		database.Songs[0].Tags = [tag];
		BookSearchIndex index = new(database);
		(index.Search(query).Count == 1).ShouldBe(expected);
	}

	[TestMethod]
	public void PresenceExclusionsAndWildcardTermsWorkPerValueAndIncrementalRefresh()
	{
		ChordDatabase database = CreateGeneratedDatabase(4);
		database.Songs[0].Tags = ["Christian", "Live"];
		database.Songs[1].Tags = [];
		database.Songs[2].Tags = [string.Empty, "  "];
		database.Songs[3].Tags = ["Practice"];
		BookSearchIndex index = new(database);
		index.Search("tag:*").Select(hit => hit.SongId).ShouldBe([database.Songs[0].Id, database.Songs[3].Id]);
		index.Search("tag:!*").Select(hit => hit.SongId).ShouldBe([database.Songs[1].Id, database.Songs[2].Id]);
		index.Search("tag:* tag:!Christ*").Single().SongId.ShouldBe(database.Songs[3].Id);
		index.Search("tag:Christ* tag:L?ve").Single().SongId.ShouldBe(database.Songs[0].Id);
		index.Search("tag:Christian*Live").ShouldBeEmpty();
		index.Search("key:!*").Count.ShouldBe(4);
		index.Search("artist:Artist*").Count.ShouldBe(4);
		database.Songs[1].Tags.Add("Christian");
		index.RefreshSong(database.Songs[1], []);
		index.Search("tag:!*").Single().SongId.ShouldBe(database.Songs[2].Id);
		index.Search("Christ*L?ve").Single().SongId.ShouldBe(database.Songs[0].Id);
	}

	[TestMethod]
	public void WildcardsRemainFastForTenThousandSongsAndLongNonmatchingPatterns()
	{
		ChordDatabase database = CreateGeneratedDatabase(GeneratedSongCount);
		database.Songs[^1].Tags = ["Christian", new string('a', 2048)];
		BookSearchIndex index = new(database);
		_ = index.Search("tag:Christ* artist:*99");
		Stopwatch stopwatch = Stopwatch.StartNew();
		index.Search("tag:Christ* artist:*99").Single().SongId.ShouldBe(database.Songs[^1].Id);
		stopwatch.Stop();
		stopwatch.Elapsed.ShouldBeLessThan(SearchBudget);
		string adversarial = "tag:" + string.Concat(Enumerable.Repeat("*a", 40)) + "b";
		index.Search(adversarial).ShouldBeEmpty();
		index.Search("tag:" + new string('*', 4096)).Count.ShouldBe(GeneratedSongCount);
	}

	[TestMethod]
	public void TenThousandSongSearchMeetsBudget()
	{
		ChordDatabase database = CreateGeneratedDatabase(GeneratedSongCount);
		database.Songs[^1].Title = "Résumé Finale";
		BookSearchIndex index = new(database);
		_ = index.Search("warmup");
		Stopwatch stopwatch = Stopwatch.StartNew();

		IReadOnlyList<BookSearchHit> matches = index.Search("RESUME FINALE");

		stopwatch.Stop();
		matches.Single().Title.ShouldBe("Résumé Finale");
		stopwatch.Elapsed.ShouldBeLessThan(SearchBudget);
	}

	#endregion

	#region Private Methods

	private static ChordDatabase CreateGeneratedDatabase(int count)
	{
		Guid deviceId = Guid.NewGuid();
		ChordDatabase result = ChordDatabase.Create("Generated", deviceId);
		for (int index = 0; index < count; index++)
		{
			result.Songs.Add(new()
			{
				Id = Guid.CreateVersion7(),
				Title = $"Generated Song {index:D5}",
				Artists = [$"Artist {index % 100:D2}"],
				Tags = [index % 2 == 0 ? "even" : "odd"],
				Revision = RevisionStamp.Initial(deviceId),
			});
		}

		return result;
	}

	#endregion
}
