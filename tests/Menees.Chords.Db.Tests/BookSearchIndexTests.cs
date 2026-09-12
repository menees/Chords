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
