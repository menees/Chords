#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class InstrumentSettingsTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task QuickDisplayAdjustmentsDoNotChangeSourceOrPersistedSettings()
	{
		var token = this.TestContext.CancellationToken;
		var (session, _, store, location, _, song, _) = await CreateAsync(token);
		string before = await store.ReadDatabaseJsonAsync(location, token);
		BookSongPresentation original = await session.GetPresentationAsync(song, cancellationToken: token);
		BookSongPresentation transposed = await session.GetPresentationAsync(song, transposeOffset: 2, cancellationToken: token);
		transposed.Html!.ShouldContain(">D</span>");
		BookSongPresentation numbered = await session.GetPresentationAsync(song, notationOverride: "Roman", cancellationToken: token);
		numbered.Html!.ShouldContain(">I</span>");
		(await session.GetPresentationAsync(song, cancellationToken: token)).Html.ShouldBe(original.Html);
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldBe(before);
		(await session.GetSongEditAsync(song, token)).Text.ShouldBe("{key: C}\r\n[C]Words");
	}

	[TestMethod]
	public async Task EntryTransposeReplacesSongOffsetAndCapoOnlyChangesShapesWhenRequested()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, store, location, device, song, guitar) = await CreateAsync(token);
		SongInstrumentSnapshot original = service.GetSongSettings(song, guitar);
		await service.SaveSongSettingsAsync(original, new(2, 2, CapoBehavior.DisplayOnly), device, cancellationToken: token);
		BookSongPresentation sounding = await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token);
		sounding.Html!.ShouldContain(">D</span>");
		sounding.PerformanceDescription!.ShouldContain("Capo 2 (display only)");
		await service.SaveSongSettingsAsync(
			service.GetSongSettings(song, guitar), new(2, 2, CapoBehavior.AffectsShownChords), device, cancellationToken: token);
		(await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token)).Html!.ShouldContain(">C</span>");
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		Guid entry = await session.AddSongToSetlistAsync(list, song, device, token);
		Guid repeat = await session.AddSongToSetlistAsync(list, song, device, token);
		await session.SaveSetlistEntrySettingsAsync(session.GetSetlistEntrySettings(list, entry), null, 5, guitar, device, token);
		(await session.GetPresentationAsync(song, list, entry, cancellationToken: token)).Html!.ShouldContain(">D#</span>");
		(await session.GetPresentationAsync(song, list, repeat, guitar, token)).Html!.ShouldContain(">C</span>");
		await session.ReloadAsync(token);
		service.GetSongSettings(song, guitar).Options.CapoBehavior.ShouldBe(CapoBehavior.AffectsShownChords);
		(await session.GetSongEditAsync(song, token)).Text.ShouldBe("{key: C}\r\n[C]Words");
		(await store.ReadDatabaseJsonAsync(location, token)).ShouldContain("Guitar");
	}

	[TestMethod]
	public async Task PromotionClearsOnlySelectedEntryAndRejectsStaleAtomicSave()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, _, _, device, song, guitar) = await CreateAsync(token);
		Guid list = await session.CreateSetlistAsync("Set", device, token);
		Guid entry = await session.AddSongToSetlistAsync(list, song, device, token);
		Guid repeat = await session.AddSongToSetlistAsync(list, song, device, token);
		await session.SaveSetlistEntrySettingsAsync(session.GetSetlistEntrySettings(list, entry), null, 5, guitar, device, token);
		SetlistEntrySettings stale = session.GetSetlistEntrySettings(list, entry);
		await session.SaveSetlistEntrySettingsAsync(session.GetSetlistEntrySettings(list, repeat), null, -3, guitar, device, token);
		SongInstrumentSnapshot original = service.GetSongSettings(song, guitar);
		await Should.ThrowAsync<InvalidOperationException>(() => service.SaveSongSettingsAsync(original, new(2), device, stale, token));
		service.GetSongSettings(song, guitar).SettingId.ShouldBeNull();
		await service.SaveSongSettingsAsync(service.GetSongSettings(song, guitar), new(2), device, session.GetSetlistEntrySettings(list, entry), token);
		session.GetSetlistEntrySettings(list, entry).TransposeSemitones.ShouldBeNull();
		session.GetSetlistEntrySettings(list, repeat).TransposeSemitones.ShouldBe(-3);
		service.GetSongSettings(song, guitar).Options.TransposeSemitones.ShouldBe(2);
		await Should.ThrowAsync<InvalidOperationException>(() => service.SaveSongSettingsAsync(original, new(4), device, cancellationToken: token));
	}

	[TestMethod]
	public async Task ProfileAndSettingDeletionUseTombstonesAndRejectReferences()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, _, _, device, song, guitar) = await CreateAsync(token);
		InstrumentProfileInfo profile = service.GetProfiles().Single();
		await service.SaveProfileAsync(profile, "Acoustic", device, token);
		await Should.ThrowAsync<InvalidOperationException>(() => service.SaveProfileAsync(profile, "Stale", device, token));
		await Should.ThrowAsync<ArgumentException>(() => service.SaveProfileAsync(null, "acoustic", device, token));
		await service.SaveSongSettingsAsync(service.GetSongSettings(song, guitar), new(1), device, cancellationToken: token);
		Guid settingId = service.GetSongSettings(song, guitar).SettingId!.Value;
		await Should.ThrowAsync<InvalidOperationException>(() => service.DeleteProfileAsync(service.GetProfiles().Single(), device, token));
		await service.SaveSongSettingsAsync(service.GetSongSettings(song, guitar), null, device, cancellationToken: token);
		await service.DeleteProfileAsync(service.GetProfiles().Single(), device, token);
		service.GetProfiles().ShouldBeEmpty();
		session.Database!.Tombstones.Any(item => item.EntityId == settingId && item.EntityType == nameof(SongInstrumentSetting)).ShouldBeTrue();
		session.Database.Tombstones.Any(item => item.EntityId == guitar && item.EntityType == nameof(InstrumentProfile)).ShouldBeTrue();
	}

	[TestMethod]
	public async Task InstrumentEditsAvoidAssetIOAndInvalidateOnlyEffectiveRender()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			Guid device = Guid.NewGuid();
			using FileSystemBookStore store = new(root);
			BookLocation location = await store.CreateBookAsync("Locked", device, token);
			BookApplicationSession session = new();
			await session.ActivateAsync(store, location, token);
			Guid song = await session.CreateSongAsync("Song", [], [], "{key: C}\r\n[C]Words", device, token);
			Guid other = await session.CreateSongAsync("Other", [], [], "[G]Words", device, token);
			InstrumentSettingsService service = new(session);
			Guid guitar = await service.SaveProfileAsync(null, "Guitar", device, token);
			string? html = (await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token)).Html;
			SongCatalogItem row = session.Search("Other").Single();
			List<FileStream> locks = [];
			try
			{
				foreach (SongFile file in session.Database!.SongFiles)
				{
					locks.Add(new FileStream(Path.Combine(store.GetDirectory(location), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None));
				}

				await service.SaveSongSettingsAsync(
					service.GetSongSettings(song, guitar), new(0, 4, CapoBehavior.DisplayOnly), device, cancellationToken: token);
				(await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token)).Html.ShouldBeSameAs(html);
				session.Search("Other").Single().ShouldBeSameAs(row);
				await service.SaveSongSettingsAsync(service.GetSongSettings(song, guitar), new(1), device, cancellationToken: token);
				await Should.ThrowAsync<IOException>(() => session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token));
			}
			finally
			{
				foreach (FileStream stream in locks)
				{
					stream.Dispose();
				}
			}

			(await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token)).Html!.ShouldContain(">C#</span>");
			(await session.GetSongEditAsync(other, token)).Text.ShouldBe("[G]Words");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public async Task InvalidSettingsAreRejectedBeforeMutatingAndDuplicatesAreInvalid()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, _, _, device, song, guitar) = await CreateAsync(token);
		SongInstrumentSnapshot original = service.GetSongSettings(song, guitar);
		await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.SaveSongSettingsAsync(original, new(25), device, cancellationToken: token));
		await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.SaveSongSettingsAsync(original, new(0, -1), device, cancellationToken: token));
		await Should.ThrowAsync<ArgumentException>(
			() => service.SaveSongSettingsAsync(original, new(PreferredSongFileId: Guid.NewGuid()), device, cancellationToken: token));
		service.GetSongSettings(song, guitar).SettingId.ShouldBeNull();
		await service.SaveSongSettingsAsync(service.GetSongSettings(song, guitar), new(), device, cancellationToken: token);
		session.Database!.SongInstrumentSettings.Add(new() { Id = Guid.NewGuid(), SongId = song, InstrumentProfileId = guitar });
		DatabaseValidation.Validate(session.Database).Any(problem => problem.Message.Contains("only one setting")).ShouldBeTrue();
	}

	[TestMethod]
	public async Task SpellingAndLyricOnlyProjectionAreSafeAndExplicit()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, _, _, device, song, guitar) = await CreateAsync(token);
		await service.SaveSongSettingsAsync(
			service.GetSongSettings(song, guitar), new(1, Spelling: AccidentalPreference.Flats), device, cancellationToken: token);
		(await session.GetPresentationAsync(song, activeInstrumentId: guitar, cancellationToken: token)).Html!.ShouldContain(">Db</span>");
		Guid lyrics = await session.CreateSongAsync("Lyrics", [], [], "Words without chords", device, token);
		await service.SaveSongSettingsAsync(
			service.GetSongSettings(lyrics, guitar), new(4, Spelling: AccidentalPreference.Flats), device, cancellationToken: token);
		(await session.GetPresentationAsync(lyrics, activeInstrumentId: guitar, cancellationToken: token)).Html!.ShouldContain("Words without chords");
	}

	[TestMethod]
	public async Task PreferredSheetFallsBackFromArchivedEntryToInstrument()
	{
		var token = this.TestContext.CancellationToken;
		var (session, service, _, _, device, song, guitar) = await CreateAsync(token);
		Guid defaultFile = session.GetSongFiles(song).Single().Id;
		string source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cho");
		try
		{
			await File.WriteAllTextAsync(source, "[G]Alternate", token);
			await session.ImportSongFilesAsync(song, [source], device, token);
			Guid alternate = session.GetSongFiles(song).Single(file => file.Id != defaultFile).Id;
			await service.SaveSongSettingsAsync(
				service.GetSongSettings(song, guitar), new(PreferredSongFileId: alternate), device, cancellationToken: token);
			Guid list = await session.CreateSetlistAsync("Set", device, token);
			Guid entry = await session.AddSongToSetlistAsync(list, song, device, token);
			await session.SaveSetlistEntrySettingsAsync(session.GetSetlistEntrySettings(list, entry), defaultFile, null, guitar, device, token);
			(await session.GetPresentationAsync(song, list, entry, cancellationToken: token)).SongFileId.ShouldBe(defaultFile);
			await session.SetSongFileArchivedAsync(song, defaultFile, true, device, token);
			(await session.GetPresentationAsync(song, list, entry, cancellationToken: token)).SongFileId.ShouldBe(alternate);
		}
		finally
		{
			File.Delete(source);
		}
	}
	#endregion

	#region Private Methods

	private static async Task<(BookApplicationSession Session, InstrumentSettingsService Service, InMemoryBookStore Store,
		BookLocation Location, Guid Device, Guid Song, Guid Guitar)> CreateAsync(CancellationToken token)
	{
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Instruments", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid song = await session.CreateSongAsync("Song", [], [], "{key: C}\r\n[C]Words", device, token);
		InstrumentSettingsService service = new(session);
		Guid guitar = await service.SaveProfileAsync(null, "Guitar", device, token);
		return (session, service, store, location, device, song, guitar);
	}

	#endregion
}
