#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;
using Menees.Chords.Sync;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class SyncReportingTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task TransferAndInitialDownloadReportsSortSongTitlesArtistsAndSetlistNames()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = new();
		await fixture.InitializeAsync(token);
		await fixture.App.CreateSongAsync("alpha", ["Zebra"], [], "[C]Second artist", fixture.DeviceA, token);
		await fixture.App.CreateSongAsync("alpha", ["Able"], [], "[D]First artist", fixture.DeviceA, token);
		await fixture.App.CreateSetlistAsync("Zebra list", fixture.DeviceA, token);
		await fixture.App.CreateSetlistAsync("Able list", fixture.DeviceA, token);

		BookSyncPreview preview = await fixture.PreviewAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		Verify(BookSyncChangeReport.Format(preview.Changes, completed: false));
		BookSyncResult uploaded = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		Verify(BookSyncChangeReport.Format(uploaded.Changes!, completed: true));
		BookLocation downloaded = await fixture.DownloadAsync(token);
		ChordDatabase database = DatabaseJson.Deserialize(await fixture.B.ReadDatabaseJsonAsync(downloaded, token));
		Verify(BookSyncChangeReport.Format(BookSyncChanges.FromDownloadedBook(database), completed: true));

		static void Verify(string report)
		{
			string[] rows = [.. report.Split('\n').Where(line => line.StartsWith("  ", StringComparison.Ordinal))];
			rows.Length.ShouldBe(5);
			rows[0].ShouldContain("alpha — Able");
			rows[1].ShouldContain("alpha — Zebra");
			rows[2].ShouldContain("Synthetic song");
			rows[3].ShouldContain("Able list");
			rows[4].ShouldContain("Zebra list");
		}
	}

	[TestMethod]
	public async Task EditedSongAppearsOnceWithNameAndArtistInBothDirections()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = new();
		await fixture.InitializeAsync(token);
		await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		fixture.BookB = await fixture.DownloadAsync(token);
		SongEditDocument edit = await fixture.App.GetSongEditAsync(fixture.Song, token);
		await fixture.App.SaveSongEditAsync(
			edit,
			new SongEditMetadata(edit.Title, edit.Artists, edit.Tags),
			edit.Text + "\n[G]Changed",
			fixture.DeviceA,
			token);
		List<FileStream> locks = [];
		BookSyncPreview review;
		try
		{
			foreach (SongFile file in fixture.App.Database!.SongFiles)
			{
				locks.Add(File.Open(
					Path.Combine(fixture.A.GetDirectory(fixture.BookA), file.RelativePath),
					FileMode.Open,
					FileAccess.Read,
					FileShare.None));
			}

			review = await fixture.PreviewAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
			review.UploadCount.ShouldBe(1);
			review.Changes.UploadedSheets.Count.ShouldBe(1);
			review.Changes.UploadedSheets.ShouldAllBe(sheet => sheet.Title == "Synthetic song"
				&& sheet.Artists.Count == 1 && sheet.Artists[0] == "Test Artist");
			review.Changes.UploadedSheets.Single().Kind.ShouldBe("Current sheet");
			string report = BookSyncChangeReport.Format(review.Changes, completed: false);
			report.ShouldContain("Sheets to upload (1)");
			report.ShouldContain("Synthetic song — Test Artist");
			report.ShouldNotContain("Previous save");
			report.ShouldNotContain(fixture.App.Database.SongFiles[0].Id.ToString("D"));
		}
		finally
		{
			foreach (FileStream file in locks)
			{
				file.Dispose();
			}
		}

		BookSyncResult uploaded = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		uploaded.Changes!.UploadedSheets.Count.ShouldBe(uploaded.UploadedSheets);
		BookSyncResult downloaded = await fixture.SyncAsync(fixture.B, fixture.BookB!, fixture.DeviceB, token);
		downloaded.Changes!.DownloadedSheets.Count.ShouldBe(downloaded.DownloadedSheets);
		downloaded.Changes.DownloadedSheets.Count.ShouldBe(1);
		BookSyncChangeReport.Format(downloaded.Changes, completed: true).ShouldContain("Downloaded sheets (1)");
		review.Changes.UploadedSheets.ShouldAllBe(sheet => sheet.Artists.Single() == "Test Artist");
	}

	[TestMethod]
	public async Task LegacyCloudCopiesAreSkippedByDownloadAndRemovedByWritableSync()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = new();
		await fixture.InitializeAsync(token);
		await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		CloudBookSnapshot original = (await CloudBookReader.ReadAsync(fixture.Cloud, token))!;
		ChordDatabase legacy = original.Database;
		SongFile active = legacy.SongFiles.Single();
		Guid automaticId = Guid.NewGuid();
		SongFile automatic = new()
		{
			Id = automaticId, SongId = active.SongId, IsArchived = true, ContentHash = active.ContentHash,
			ObservedLength = active.ObservedLength, SavedVersion = new() { SourceFileId = active.Id },
			RelativePath = PortableManagedFileName.Create("Previous Synthetic song", automaticId, ".txt"),
		};
		legacy.SongFiles.Add(automatic);
		using Stream source = await fixture.Cloud.DownloadAsync(original.ManagedItems[active.Id].Id, token);
		await fixture.Cloud.CreateAsync(automatic.RelativePath, source, token);
		string catalog = DatabaseJson.Serialize(legacy).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal);
		using MemoryStream json = new(Encoding.UTF8.GetBytes(catalog));
		await fixture.Cloud.ReplaceAsync(original.DatabaseItem.Id, original.DatabaseItem.Version, json, token);

		fixture.BookB = await fixture.DownloadAsync(token);
		ChordDatabase firstDownload = DatabaseJson.Deserialize(await fixture.B.ReadDatabaseJsonAsync(fixture.BookB, token));
		firstDownload.SongFiles.Single().Id.ShouldBe(active.Id);
		firstDownload.Tombstones.Single().EntityId.ShouldBe(automaticId);
		int mutations = fixture.Cloud.MutationLog.Count;
		BookSyncResult readOnly = await fixture.SyncAsync(fixture.B, fixture.BookB, fixture.DeviceB, token, SyncDirection.UpdateThisDevice);
		readOnly.DownloadedSheets.ShouldBe(0);
		fixture.Cloud.MutationLog.Count.ShouldBe(mutations);

		BookSyncPreview preview = await fixture.PreviewAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		preview.CloudRemovedSheetCount.ShouldBe(1);
		preview.DownloadCount.ShouldBe(0);
		preview.UploadCount.ShouldBe(0);
		await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		CloudBookSnapshot after = (await CloudBookReader.ReadAsync(fixture.Cloud, token))!;
		after.Database.SongFiles.Single().Id.ShouldBe(active.Id);
		after.Database.Tombstones.Single().EntityId.ShouldBe(automaticId);
		after.DatabaseJson.ShouldContain("\"schemaVersion\": 2");
		(await fixture.PreviewAsync(fixture.A, fixture.BookA, fixture.DeviceA, token)).HasChanges.ShouldBeFalse();
	}

	[TestMethod]
	public async Task MetadataOnlySetlistChangesReportNamesCountsAndNoAssetAccessAndNoOpHasNoDetails()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = new();
		await fixture.InitializeAsync(token);
		Guid list = await fixture.App.CreateSetlistAsync("Practice", fixture.DeviceA, token);
		await fixture.App.AddSongsToSetlistAsync(list, [fixture.Song, fixture.Song], fixture.DeviceA, token);
		BookSyncResult initial = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		initial.Changes!.UploadedSetlists.Single().Name.ShouldBe("Practice");
		fixture.BookB = await fixture.DownloadAsync(token);
		ChordDatabase downloadedBook = DatabaseJson.Deserialize(await fixture.B.ReadDatabaseJsonAsync(fixture.BookB, token));
		BookSyncChanges allDownloaded = BookSyncChanges.FromDownloadedBook(downloadedBook);
		allDownloaded.DownloadedSheets.Single().Title.ShouldBe("Synthetic song");
		allDownloaded.DownloadedSheets.Single().Artists.ShouldBe(["Test Artist"]);
		allDownloaded.DownloadedSetlists.Single().Name.ShouldBe("Practice");
		allDownloaded.UploadedSheets.ShouldBeEmpty();
		BookSyncChangeReport.Format(allDownloaded, completed: true).ShouldContain("Downloaded setlists (1)");
		SetlistEdit draft = fixture.App.GetSetlistEdit(list);
		draft.Value.Name = "Changed setlist";
		draft.SetOrder([.. draft.Value.Entries.Select(entry => entry.Id).Reverse()]);
		await fixture.App.SaveSetlistEditAsync(draft, fixture.DeviceA, token);
		SongFile file = fixture.App.Database!.SongFiles.Single();
		using FileStream fileLock = File.Open(
			Path.Combine(fixture.A.GetDirectory(fixture.BookA), file.RelativePath),
			FileMode.Open,
			FileAccess.Read,
			FileShare.None);
		BookSyncResult upload = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		upload.UploadedSheets.ShouldBe(0);
		upload.DownloadedSheets.ShouldBe(0);
		upload.Changes!.UploadedSetlists.Single().Name.ShouldBe("Changed setlist");
		upload.Changes.DownloadedSetlists.ShouldBeEmpty();
		BookSyncChangeReport.Format(upload.Changes, completed: true).ShouldContain("Uploaded setlists (1)");
		BookSyncResult download = await fixture.SyncAsync(fixture.B, fixture.BookB!, fixture.DeviceB, token);
		download.DownloadedSheets.ShouldBe(0);
		download.Changes!.DownloadedSetlists.Single().Name.ShouldBe("Changed setlist");
		BookSyncResult noOp = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		noOp.Changes!.UploadedSetlists.ShouldBeEmpty();
		noOp.Changes.DownloadedSetlists.ShouldBeEmpty();
		noOp.Changes.UploadedSheets.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task UpdateThisDeviceReportsSetlistDownloadsAndDeletionsWithoutCloudWrites()
	{
		var token = this.TestContext.CancellationToken;
		using Fixture fixture = new();
		await fixture.InitializeAsync(token);
		Guid list = await fixture.App.CreateSetlistAsync("Cloud setlist", fixture.DeviceA, token);
		await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		fixture.BookB = await fixture.DownloadAsync(token);
		await fixture.App.RenameSetlistAsync(list, "Renamed remotely", fixture.DeviceA, token);
		await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		int mutations = fixture.Cloud.MutationLog.Count;
		BookSyncResult download = await fixture.SyncAsync(fixture.B, fixture.BookB!, fixture.DeviceB, token, SyncDirection.UpdateThisDevice);
		download.Changes!.UploadedSetlists.ShouldBeEmpty();
		download.Changes.DownloadedSetlists.Single().Name.ShouldBe("Renamed remotely");
		fixture.Cloud.MutationLog.Count.ShouldBe(mutations);
		await fixture.App.ReloadAsync(token);
		SetlistEdit draft = fixture.App.GetSetlistEdit(list);
		draft.Value.IsArchived = true;
		draft.RequestDeletion();
		await fixture.App.SaveSetlistEditAsync(draft, fixture.DeviceA, token);
		BookSyncResult removed = await fixture.SyncAsync(fixture.A, fixture.BookA, fixture.DeviceA, token);
		removed.Changes!.RemovedCloudSetlists.Single().Name.ShouldBe("Renamed remotely");
		download = await fixture.SyncAsync(fixture.B, fixture.BookB!, fixture.DeviceB, token, SyncDirection.UpdateThisDevice);
		download.Changes!.RemovedLocalSetlists.Single().Name.ShouldBe("Renamed remotely");
	}

	#endregion

	#region Private Types

	private sealed class Fixture : IDisposable
	{
		private readonly string root = Path.Combine(Path.GetTempPath(), "ChordSyncReporting-" + Guid.NewGuid().ToString("N"));

		public Fixture()
		{
			this.A = new(Path.Combine(this.root, "a"));
			this.B = new(Path.Combine(this.root, "b"));
		}

		public Guid DeviceA { get; } = Guid.NewGuid();

		public Guid DeviceB { get; } = Guid.NewGuid();

		public FileSystemBookStore A { get; }

		public FileSystemBookStore B { get; }

		public InMemoryCloudReplica Cloud { get; } = new(new("test", "account", "folder"));

		public BookApplicationSession App { get; } = new();

		public BookLocation BookA { get; private set; } = null!;

		public BookLocation? BookB { get; set; }

		public Guid Song { get; private set; }

		public async Task InitializeAsync(CancellationToken token)
		{
			this.BookA = await this.A.CreateBookAsync("Reporting", this.DeviceA, token);
			await this.App.ActivateAsync(this.A, this.BookA, token);
			this.Song = await this.App.CreateSongAsync("Synthetic song", ["Test Artist"], [], "[C]Original", this.DeviceA, token);
			await this.Cloud.AuthenticateAsync(token);
		}

		public async Task<BookSyncPreview> PreviewAsync(FileSystemBookStore store, BookLocation book, Guid device, CancellationToken token)
		{
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(book, token));
			using ReplicaStateLease lease = this.States(device).Acquire(new(database.Id, device, this.Cloud.Identity));
			return await BookSyncCoordinator.PrepareAsync(store, book, this.Cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		}

		public async Task<BookSyncResult> SyncAsync(
			FileSystemBookStore store, BookLocation book, Guid device, CancellationToken token, SyncDirection direction = SyncDirection.TwoWay)
		{
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(book, token));
			using ReplicaStateLease lease = this.States(device).Acquire(new(database.Id, device, this.Cloud.Identity));
			BookSyncPreview review = await BookSyncCoordinator.PrepareAsync(store, book, this.Cloud, lease, direction, Guid.Empty, token);
			BookSyncResult result = await BookSyncCoordinator.ExecuteAsync(store, book, this.Cloud, lease, review, cancellationToken: token);
			if (ReferenceEquals(store, this.A))
			{
				await this.App.ReloadAsync(token);
			}

			return result;
		}

		public Task<BookLocation> DownloadAsync(CancellationToken token)
			=> CloudBookDownloader.DownloadAsync(
				this.B,
				this.Cloud,
				this.DeviceB,
				this.States(this.DeviceB),
				Path.Combine(this.root, "staging"),
				cancellationToken: token);

		public void Dispose()
		{
			this.A.Dispose();
			this.B.Dispose();
			if (Directory.Exists(this.root))
			{
				Directory.Delete(this.root, recursive: true);
			}
		}

		private ReplicaStateStore States(Guid device) => new(Path.Combine(this.root, device.ToString("N")));
	}

	#endregion
}
