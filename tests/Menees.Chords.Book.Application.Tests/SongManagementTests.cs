#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongManagementTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow("{title: Source title}\r\n[C]A new song\n", SourceFormat.ChordPro)]
	[DataRow("C        G\r\nA new song\r\n", SourceFormat.ChordOverText)]
	[DataRow("{title: Source title}\r\nC        G\r\nA new song\r\n", SourceFormat.Mixed)]
	public async Task NewSongPreservesPastedSyntaxAndIndependentCatalogMetadata(string text, SourceFormat format)
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, existingSong) = await CreateAsync(token);
		SongFile existingFile = session.Database!.SongFiles.Single();
		Guid id = await session.CreateSongAsync("  Catalog title  ", [" Composer "], [" Practice "], text, device, token);
		session.Search("Catalog title").Single().Id.ShouldBe(id);
		await session.ActivateAsync(store, location, token);
		Song song = session.Database!.Songs.Single(item => item.Id == id);
		song.Title.ShouldBe("Catalog title");
		song.Artists.ShouldBe(["Composer"]);
		song.Tags.ShouldBe(["Practice"]);
		SongFile file = session.Database.SongFiles.Single(item => item.SongId == id);
		file.SourceFormat.ShouldBe(format);
		file.RelativePath.ShouldStartWith("Catalog title");
		using Stream stream = await store.OpenManagedAssetAsync(location, file.Id, token);
		using MemoryStream bytes = new();
		await stream.CopyToAsync(bytes, token);
		bytes.ToArray().ShouldBe(Encoding.UTF8.GetBytes(text));
		(await session.GetSongEditAsync(id, token)).Text.ShouldBe(text);
		(await session.GetPresentationAsync(id, cancellationToken: token)).Html.ShouldNotBeNullOrWhiteSpace();
		session.Database.SongFiles.Single(item => item.SongId == existingSong).ContentHash.ShouldBe(existingFile.ContentHash);
		DatabaseValidation.Validate(session.Database).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task NewSongRejectsUnsupportedOrEmptyTextWithoutSaving()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, _) = await CreateAsync(token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<ArgumentException>(() => session.CreateSongAsync(" ", [], [], "[C]Song", device, token));
		await Should.ThrowAsync<ArgumentException>(() => session.CreateSongAsync("Song", [], [], " ", device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => session.CreateSongAsync("PDF", [], [], "%PDF-1.4", device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => session.CreateSongAsync(
			"XML", [], [], "<song><title>XML</title><lyrics>.C\n Song</lyrics></song>", device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		DatabaseJson.Serialize(session.Database!).ShouldBe(before);
	}

	[TestMethod]
	public async Task MetadataFailuresRestoreSessionAndConcurrentEditsSerialize()
	{
		var token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		await Task.WhenAll(session.AddSongToSetlistAsync(list, song, device, token), session.AddSongToSetlistAsync(list, song, device, token));
		session.GetSetlistEntries(list).Count.ShouldBe(2);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<InvalidOperationException>(() => session.SetSetlistOrderAsync(list, [Guid.NewGuid()], device, token));
		DatabaseJson.Serialize(session.Database!).ShouldBe(before);
		ChordDatabase external = DatabaseJson.Deserialize(before);
		external.Name = "External winner";
		await store.CommitMetadataAsync(location, before, external, token);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => session.RenameSetlistAsync(list, "Stale change", device, token));
		session.Database!.Setlists.Single().Name.ShouldBe("Set");
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token)).Name.ShouldBe("External winner");
		await session.ReloadAsync(token);
		await session.RenameSetlistAsync(list, "Fresh change", device, token);
		session.Database!.Name.ShouldBe("External winner");
	}

	[TestMethod]
	public async Task NativeReorderRejectsMissingDuplicateAndUnknownEntryIds()
	{
		var token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		IReadOnlyList<Guid> ids = await session.AddSongsToSetlistAsync(list, [song, song, song], device, token);
		await session.SetSetlistOrderAsync(list, [ids[2], ids[0], ids[1]], device, token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<InvalidOperationException>(() => session.SetSetlistOrderAsync(list, [ids[0], ids[1]], device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => session.SetSetlistOrderAsync(list, [ids[0], ids[0], ids[1]], device, token));
		await Should.ThrowAsync<InvalidOperationException>(() => session.SetSetlistOrderAsync(list, [ids[0], ids[1], Guid.NewGuid()], device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		await session.ActivateAsync(store, location, token);
		session.GetSetlistEntries(list).Select(entry => entry.EntryId).ShouldBe([ids[2], ids[0], ids[1]]);
	}

	[TestMethod]
	public async Task RepositionRetainsDuplicateOccurrencesAndRejectsInvalidPositions()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		IReadOnlyList<Guid> ids = await session.AddSongsToSetlistAsync(list, [song, song, song], device, token);
		await session.SetSetlistEntryPositionAsync(list, ids[2], 1, device, token);
		session.GetSetlistEntries(list).Select(entry => entry.EntryId).ShouldBe([ids[2], ids[0], ids[1]]);
		await session.SetSetlistEntryPositionAsync(list, ids[2], 3, device, token);
		await session.ActivateAsync(store, location, token);
		session.GetSetlistEntries(list).Select(entry => entry.EntryId).ShouldBe(ids);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		await Should.ThrowAsync<ArgumentOutOfRangeException>(() => session.SetSetlistEntryPositionAsync(list, ids[0], 0, device, token));
		await Should.ThrowAsync<ArgumentOutOfRangeException>(() => session.SetSetlistEntryPositionAsync(list, ids[0], 4, device, token));
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task PermanentDeletionRequiresArchiveAndRemovesAssetsAndAllOccurrences()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		await session.AddSongsToSetlistAsync(list, [song, song], device, token);
		Guid file = session.Database!.SongFiles.Single().Id;
		await Should.ThrowAsync<InvalidOperationException>(() => session.DeleteArchivedSongsAsync([song], device, token));
		await session.SetSongsArchivedAsync([song], true, device, token);
		session.Search(string.Empty).ShouldBeEmpty();
		session.Search(string.Empty, true).Single().IsArchived.ShouldBeTrue();
		await session.DeleteArchivedSongsAsync([song], device, token);
		await session.ActivateAsync(store, location, token);
		session.Database!.Songs.ShouldBeEmpty();
		session.Database.SongFiles.ShouldBeEmpty();
		session.GetSetlistEntries(list).ShouldBeEmpty();
		session.Database.Tombstones.Select(item => item.EntityId).Order().ShouldBe(new[] { song, file }.Order());
		DatabaseValidation.Validate(session.Database).ShouldBeEmpty();
		List<ManagedAssetDescriptor> assets = [];
		await foreach (ManagedAssetDescriptor asset in store.EnumerateManagedAssetsAsync(location, token))
		{
			assets.Add(asset);
		}

		assets.ShouldBeEmpty();
		await Should.ThrowAsync<InvalidOperationException>(() => session.DeleteArchivedSetlistAsync(list, device, token));
		await session.SetSetlistArchivedAsync(list, true, device, token);
		await session.DeleteArchivedSetlistAsync(list, device, token);
		session.Database.Tombstones.Single(item => item.EntityId == list).EntityType.ShouldBe(nameof(Setlist));
		session.GetSetlists(true).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task EditorPreservesEncodingAndRejectsStaleMetadata()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		SongEditDocument original = await session.GetSongEditAsync(song, token);
		original.Text.ShouldBe("{title: Café}\r\n[C]Original\r\n");
		await session.SaveSongEditAsync(original, "Renamed", ["Artist"], ["Practice"], original.Text!.Replace("Original", "Edited"), device, token);
		await session.ActivateAsync(store, location, token);
		SongFile file = session.Database!.SongFiles.Single();
		using Stream stream = await store.OpenManagedAssetAsync(location, file.Id, token);
		using MemoryStream bytes = new();
		await stream.CopyToAsync(bytes, token);
		byte[] expected = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{title: Café}\r\n[C]Edited\r\n")];
		bytes.ToArray().ShouldBe(expected);
		file.ContentHash.ShouldBe(SongFileAnalyzer.Hash(expected));
		session.Search("Renamed").Single().Title.ShouldBe("Renamed");
		await Should.ThrowAsync<InvalidOperationException>(() => session.SaveSongEditAsync(original, "Stale", [], [], original.Text, device, token));
	}

	[TestMethod]
	public async Task MetadataOnlySaveDoesNotRewriteSourceBytes()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, _, _, device, song) = await CreateAsync(token);
		SongEditDocument original = await session.GetSongEditAsync(song, token);
		SongFile file = session.Database!.SongFiles.Single();
		long contentRevision = file.ContentRevision;
		await session.SaveSongEditAsync(original, "Metadata", [], [], original.Text, device, token);
		session.Database!.SongFiles.Single().ContentRevision.ShouldBe(contentRevision);
		session.Database.SongFiles.Single().ContentHash.ShouldBe(file.ContentHash);
		(await session.GetSongEditAsync(song, token)).Text.ShouldBe(original.Text);
	}

	[TestMethod]
	public async Task MetronomeOverridesAreExplicitAndResettable()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		var (session, store, location, device, song) = await CreateAsync(token);
		MetronomeSettings defaults = session.GetMetronomeSettings(song);
		await session.SaveSongMetronomeAsync(song, new() { BeatsPerMinute = 87, BeatsPerMeasure = 3, AudioEnabled = false }, device, token);
		session.Search("metronome:true").Single().Id.ShouldBe(song);
		await session.ActivateAsync(store, location, token);
		session.GetMetronomeSettings(song).BeatsPerMinute.ShouldBe(87);
		session.GetMetronomeSettings(song).AudioEnabled.ShouldBeFalse();
		await session.SaveSongMetronomeAsync(song, null, device, token);
		session.Search("metronome:true").ShouldBeEmpty();
		session.GetMetronomeSettings(song).BeatsPerMinute.ShouldBe(defaults.BeatsPerMinute);
		session.Database!.Songs.Single().MetronomeOverride.ShouldBeNull();
		MetronomeSettings bookDefaults = new() { BeatsPerMinute = 96 };
		await session.SaveBookMetronomeAsync(bookDefaults, device, token);
		bookDefaults.BeatsPerMinute = 100;
		session.GetMetronomeSettings(song).BeatsPerMinute.ShouldBe(96);
		await session.ReloadAsync(token);
		BookApplicationSession.MetronomeSettingsEqual(session.GetMetronomeSettings(), session.GetMetronomeSettings(song)).ShouldBeTrue();
		Should.Throw<ArgumentException>(() => BookApplicationSession.ValidateMetronome(new() { BeatsPerMinute = 0 }));
		Should.Throw<ArgumentException>(() => BookApplicationSession.ValidateMetronome(new() { AudioEnabled = false, VisualEnabled = false }));
	}

	[TestMethod]
	public void AudioPulseRetainsTimingAndAccentAfterLongPlayback()
	{
		const int SampleRate = 48000;
		const int Tempo = 120;
		const int Meter = 4;
		const long SamplesPerBeat = 24000;
		float accent = MetronomePulse.Sample(10, SampleRate, Tempo, Meter, 1, true);
		MetronomePulse.Sample((SamplesPerBeat * 40000) + 10, SampleRate, Tempo, Meter, 1, true).ShouldBe(accent);
		MetronomePulse.Sample(SamplesPerBeat + 10000, SampleRate, Tempo, Meter, 1, true).ShouldBe(0);
		MetronomePulse.Sample(SamplesPerBeat + 10, SampleRate, Tempo, Meter, 1, false)
			.ShouldBe(MetronomePulse.Sample(10, SampleRate, Tempo, Meter, 1, false));
		MetronomePulse.Sample(10, SampleRate, Tempo, Meter, 0, true).ShouldBe(0);
	}

	#endregion

	#region Private Methods

	private static async Task<(BookApplicationSession Session, InMemoryBookStore Store, BookLocation Location, Guid Device, Guid Song)> CreateAsync(
		CancellationToken token)
	{
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Management Tests", device, token);
		byte[] content = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{title: Café}\r\n[C]Original\r\n")];
		using MemoryStream source = new(content);
		await BookImportService.ImportAsync(store, location, "Café.cho", source, device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		return (session, store, location, device, session.Database!.Songs.Single().Id);
	}

	#endregion
}
