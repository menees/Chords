#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SongFileOpeningTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task IdenticalBytesOpenExistingSongDespiteChangedFilenameWithoutReadingManagedAssetsOrWritingCatalog()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		SongFile managed = fixture.App.Database!.SongFiles.Single();
		string incoming = fixture.Source("Renamed Unicode Ω.cho", Fixture.Original);
		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using FileStream locked = File.Open(
			Path.Combine(fixture.Store.GetDirectory(fixture.Book), managed.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None);
		using SongFileOpenResult result = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		result.SongId.ShouldBe(fixture.Song);
		result.SongFileId.ShouldBe(managed.Id);
		result.Imported.ShouldBeFalse();
		result.Review.ShouldBeNull();
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
		File.ReadAllText(incoming).ShouldBe(Fixture.Original);
	}

	[TestMethod]
	public async Task NewSongImportsCapturedBytesOnceThenRepeatedOpenMatchesWithoutAnotherImport()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		const string Text = "{title: A new song}\r\n{artist: Synthetic Artist}\r\n[G]New bytes";
		string incoming = fixture.Source("new.cho", Text);
		using SongFileOpenResult first = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		first.Imported.ShouldBeTrue();
		first.SongId.ShouldNotBeNull();
		fixture.App.Database!.Songs.Count.ShouldBe(2);
		fixture.App.Database.SongFiles.Count.ShouldBe(2);
		SongFile managed = fixture.App.Database.SongFiles.Single(file => file.Id == first.SongFileId);
		File.ReadAllBytes(Path.Combine(fixture.Store.GetDirectory(fixture.Book), managed.RelativePath)).ShouldBe(File.ReadAllBytes(incoming));
		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using SongFileOpenResult repeated = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		repeated.SongId.ShouldBe(first.SongId);
		repeated.Imported.ShouldBeFalse();
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task MatchingTitleAndArtistWithDifferentBytesRequiresReviewAndDefaultsToSkip()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		string incoming = fixture.Source("different.cho", Fixture.Original + "\n[G]Different content");
		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using SongFileOpenResult result = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		result.SongId.ShouldBeNull();
		result.Imported.ShouldBeFalse();
		BookImportReview review = result.Review.ShouldNotBeNull();
		BookImportItem item = review.Items.Single();
		item.Conflict.ShouldBe(BookImportConflict.PossibleVersion);
		item.DefaultDecision.Action.ShouldBe(BookImportAction.Skip);
		item.Candidates.Single().SongId.ShouldBe(fixture.Song);
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
		(await fixture.App.ApplyImportAsync(review, [item.DefaultDecision], fixture.Device, token)).ShouldBeEmpty();
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task ConflictReviewUsesCapturedSourceAndOnlyAddsASheetAfterExplicitChoice()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		string text = Fixture.Original + "\n[G]Reviewed bytes";
		string incoming = fixture.Source("reviewed.cho", text);
		using SongFileOpenResult result = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		BookImportReview review = result.Review.ShouldNotBeNull();
		File.WriteAllText(incoming, "Changed outside the app");
		BookImportItem item = review.Items.Single();
		IReadOnlyList<BookImportResult> imported = await fixture.App.ApplyImportAsync(
			review, [new(item.Id, BookImportAction.AddSheet, fixture.Song)], fixture.Device, token);
		fixture.App.Database!.Songs.Count.ShouldBe(1);
		fixture.App.Database.SongFiles.Count.ShouldBe(2);
		string path = Path.Combine(fixture.Store.GetDirectory(fixture.Book), imported.Single().RelativePath);
		File.ReadAllText(path).ShouldBe(text);
	}

	[TestMethod]
	public async Task MultipleSongsWithIdenticalBytesReturnChoicesAndDoNotCreateAnotherSong()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		string incoming = fixture.Source("duplicate.cho", Fixture.Original);
		using (BookImportReview review = await fixture.App.ReviewImportAsync([incoming], token))
		{
			await fixture.App.ApplyImportAsync(review, [new(review.Items.Single().Id, BookImportAction.ImportNewSong)], fixture.Device, token);
		}

		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using SongFileOpenResult result = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		result.SongId.ShouldBeNull();
		result.HashMatches.Count.ShouldBe(2);
		result.Review.ShouldBeNull();
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task MatchingPreviousSaveCanBeDisplayedWithoutChangingTheDefaultOrAnyCatalogBytes()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		SongEditDocument edit = await fixture.App.GetSongEditAsync(fixture.Song, token);
		await fixture.App.SaveSongEditAsync(edit, new(edit.Title, edit.Artists, edit.Tags), Fixture.Original + "\nNew verse", fixture.Device, token);
		string incoming = fixture.Source("previous.cho", Fixture.Original);
		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using SongFileOpenResult result = await SongFileOpening.OpenAsync(fixture.App, incoming, fixture.Device, token);
		SongFile old = fixture.App.Database!.SongFiles.Single(file => file.SavedVersion is not null);
		result.SongFileId.ShouldBe(old.Id);
		BookSongPresentation shown = await fixture.App.GetPresentationAsync(fixture.Song, sheetOverride: old.Id, cancellationToken: token);
		shown.SongFileId.ShouldBe(old.Id);
		shown.Html!.ShouldNotContain("New verse");
		(await fixture.App.GetPresentationAsync(fixture.Song, cancellationToken: token)).SongFileId.ShouldNotBe(old.Id);
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
	}

	[TestMethod]
	public async Task MissingInvalidCanceledAndStaleFilesNeverMutateTheBook()
	{
		using Fixture fixture = new();
		var token = this.TestContext.CancellationToken;
		await fixture.InitializeAsync(token);
		string before = await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token);
		using SongFileOpenResult missing = await SongFileOpening.OpenAsync(fixture.App, Path.Combine(fixture.Root, "missing.cho"), fixture.Device, token);
		missing.Imported.ShouldBeFalse();
		missing.Review!.Items.ShouldBeEmpty();
		missing.Review.Failures.Single().Name.ShouldBe("missing.cho");
		string invalid = fixture.Source("invalid.cho", "binary\0content");
		using SongFileOpenResult rejected = await SongFileOpening.OpenAsync(fixture.App, invalid, fixture.Device, token);
		rejected.Imported.ShouldBeFalse();
		rejected.Review!.Failures.Single().SourcePath.ShouldBe(invalid);
		rejected.Review.Failures.Single().Message.ShouldContain("U+0000");
		await Should.ThrowAsync<OperationCanceledException>(() => SongFileOpening.OpenAsync(fixture.App, invalid, fixture.Device, new(true)));
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(before);
		ChordDatabase changed = DatabaseJson.Deserialize(before);
		changed.Name = "External change";
		string external = await fixture.Store.CommitMetadataAsync(fixture.Book, before, changed, token);
		string identical = fixture.Source("stale.cho", Fixture.Original);
		await Should.ThrowAsync<BookStoreConcurrencyException>(() => SongFileOpening.OpenAsync(fixture.App, identical, fixture.Device, token));
		(await fixture.Store.ReadDatabaseJsonAsync(fixture.Book, token)).ShouldBe(external);
	}

	#endregion

	#region Private Types

	private sealed class Fixture : IDisposable
	{
		public const string Original = "{title: Original}\n{artist: Synthetic Artist}\n[C]Synthetic body";

		public Fixture() => this.Store = new(Path.Combine(this.Root, "books"));

		public string Root { get; } = Path.Combine(Path.GetTempPath(), "ChordFileOpen-" + Guid.NewGuid().ToString("N"));

		public Guid Device { get; } = Guid.NewGuid();

		public FileSystemBookStore Store { get; }

		public BookApplicationSession App { get; } = new();

		public BookLocation Book { get; private set; } = null!;

		public Guid Song { get; private set; }

		public async Task InitializeAsync(CancellationToken token)
		{
			this.Book = await this.Store.CreateBookAsync("File open", this.Device, token);
			await this.App.ActivateAsync(this.Store, this.Book, token);
			this.Song = await this.App.CreateSongAsync("Original", ["Synthetic Artist"], [], Original, this.Device, token);
		}

		public string Source(string name, string text)
		{
			string path = Path.Combine(this.Root, name);
			File.WriteAllText(path, text, new UTF8Encoding(false));
			return path;
		}

		public void Dispose()
		{
			this.Store.Dispose();
			if (Directory.Exists(this.Root))
			{
				Directory.Delete(this.Root, recursive: true);
			}
		}
	}

	#endregion
}
