#region Using Directives

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class BookSyncCoordinatorTests
{
	#region Private Data

	private readonly string root = Path.Combine(Path.GetTempPath(), "ChordCoordinator-" + Guid.NewGuid().ToString("N"));
	private readonly Guid deviceA = Guid.NewGuid();
	private readonly Guid deviceB = Guid.NewGuid();
	private readonly FaultingCloudReplica cloud = new();
	private FileSystemBookStore storeA = null!;
	private FileSystemBookStore storeB = null!;
	private BookLocation bookA = null!;
	private BookLocation bookB = null!;

	#endregion

	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	public async Task InitializeAsync(CancellationToken token)
	{
		this.storeA = new(Path.Combine(this.root, "a"));
		this.storeB = new(Path.Combine(this.root, "b"));
		this.bookA = await this.storeA.CreateBookAsync("Disposable", this.deviceA, token);
		using MemoryStream source = new(Encoding.UTF8.GetBytes("{title: Original}\r\n[C]Original bytes\r\n"));
		await BookImportService.ImportAsync(this.storeA, this.bookA, "song.cho", source, this.deviceA, token);
		await this.cloud.AuthenticateAsync(token);
	}

	[TestCleanup]
	public void Cleanup()
	{
		this.storeA?.Dispose();
		this.storeB?.Dispose();
		if (Directory.Exists(this.root))
		{
			Directory.Delete(this.root, recursive: true);
		}
	}

	[TestMethod]
	public async Task FirstPublicationDownloadAndAlternatingEditsConverge()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		ChordDatabase b = await this.ReadAsync(this.storeB, this.bookB);
		b.Id.ShouldBe(a.Id);
		(await this.TextAsync(this.storeB, this.bookB, b.SongFiles.Single().Id)).ShouldBe(
			"{title: Original}\r\n[C]Original bytes\r\n");
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "music room bytes");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		(await this.TextAsync(this.storeA, this.bookA, a.SongFiles.Single().Id)).ShouldBe("music room bytes");
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "office bytes");
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		(await this.TextAsync(this.storeB, this.bookB, b.SongFiles.Single().Id)).ShouldBe("office bytes");
	}

	[TestMethod]
	public async Task UploadedVersionsUseShortIdsWithoutRenamingUnchangedSheets()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		using MemoryStream source = new(Encoding.UTF8.GetBytes("[G]Second song"));
		await BookImportService.ImportAsync(this.storeA, this.bookA, "second.cho", source, this.deviceA, token);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase original = await this.ReadAsync(this.storeA, this.bookA);
		List<string> versions = [];
		foreach (SongFile file in original.SongFiles)
		{
			PortableManagedFileName.TryGetSongFileId(file.RelativePath, out Guid id).ShouldBeTrue();
			id.ShouldBe(file.Id);
			string description = file.RelativePath[..file.RelativePath.LastIndexOf(" [", StringComparison.Ordinal)];
			string version = description[(description.LastIndexOf(' ') + 1)..];
			version.Length.ShouldBe(16);
			version.ShouldAllBe(character => char.IsAsciiHexDigitLower(character));
			versions.Add(version);
		}

		versions.Distinct().Count().ShouldBe(1);
		SongFile changed = original.SongFiles[0];
		SongFile untouched = original.SongFiles[1];
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "[D]Updated first song", changed.Id);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase updated = await this.ReadAsync(this.storeA, this.bookA);
		updated.SongFiles.Single(file => file.Id == changed.Id).RelativePath.ShouldNotBe(changed.RelativePath);
		updated.SongFiles.Single(file => file.Id == untouched.Id).RelativePath.ShouldBe(untouched.RelativePath);
		CloudChangeSet listing = await this.cloud.ListOrGetChangesAsync(null, token);
		listing.Items.ShouldContain(item => item.Name == changed.RelativePath);
		this.bookB = await this.DownloadAsync();
		DatabaseJson.Serialize(await this.ReadAsync(this.storeB, this.bookB)).ShouldBe(DatabaseJson.Serialize(updated));
	}

	[TestMethod]
	public async Task ConcurrentTextEditsRetainBothExactVersions()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "office concurrent");
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "music concurrent");
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		BookSyncResult result = await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		result.RecoveredSheets.ShouldBe(1);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		foreach ((FileSystemBookStore store, BookLocation location) in new[] { (this.storeA, this.bookA), (this.storeB, this.bookB) })
		{
			ChordDatabase database = await this.ReadAsync(store, location);
			database.SongFiles.Count.ShouldBe(2);
			database.SongFiles.Count(file => file.IsArchived && file.RecoveryVersion is not null).ShouldBe(1);
			List<string> versions = [];
			foreach (SongFile file in database.SongFiles)
			{
				versions.Add(await this.TextAsync(store, location, file.Id));
			}

			versions.ShouldContain("office concurrent");
			versions.ShouldContain("music concurrent");
		}
	}

	[TestMethod]
	public async Task UploadWithLostResponseIsVerifiedInsteadOfDuplicated()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		bool failed = false;
		this.cloud.AfterMutation = action =>
		{
			if (!failed && action.StartsWith("Create:", StringComparison.Ordinal) && action != "Create:database.json")
			{
				failed = true;
				throw new CloudRequestException(503, TimeSpan.Zero);
			}
		};
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		failed.ShouldBeTrue();
		this.cloud.Mutations.Count(action => action.StartsWith("Create:", StringComparison.Ordinal)).ShouldBe(2);
		this.cloud.AssetDownloads.ShouldBe(1);
	}

	[TestMethod]
	public async Task CatalogWithLostResponseIsReconciledWithoutSecondCommit()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "changed");
		bool failed = false;
		this.cloud.AfterMutation = action =>
		{
			if (!failed && action == "Replace:database.json")
			{
				failed = true;
				throw new CloudRequestException(503, TimeSpan.Zero);
			}
		};
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		failed.ShouldBeTrue();
		this.cloud.Mutations.Count(action => action == "Replace:database.json").ShouldBe(1);
	}

	[TestMethod]
	public async Task CancellationAfterCatalogCommitResumesAfterLeaseReopen()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		this.cloud.AfterMutation = action =>
		{
			if (action == "Create:database.json")
			{
				throw new OperationCanceledException();
			}
		};
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		int mutations = this.cloud.Mutations.Count;
		this.cloud.AfterMutation = null;
		BookSyncPreview pending = await this.PreviewAsync();
		pending.IsResuming.ShouldBeTrue();
		pending.HasChanges.ShouldBeTrue();
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.cloud.Mutations.Count.ShouldBe(mutations);
		(await this.storeA.ReadDatabaseJsonAsync(this.bookA, this.TestContext.CancellationToken))
			.ShouldBe(await this.cloud.ReadCatalogAsync(this.TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MetadataOnlySyncLeavesLockedLocalSheetAndCloudBytesUntouched()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase database = await this.ReadAsync(this.storeA, this.bookA);
		string original = await this.storeA.ReadDatabaseJsonAsync(this.bookA, this.TestContext.CancellationToken);
		database.Songs[0].Title = "Catalog title";
		database.Songs[0].Revision = RevisionStamp.Initial(this.deviceA);
		database.Songs[0].Revision.Revision++;
		await this.storeA.CommitMetadataAsync(this.bookA, original, database, this.TestContext.CancellationToken);
		string assetPath = Path.Combine(this.storeA.GetDirectory(this.bookA), database.SongFiles.Single().RelativePath);
		using FileStream locked = new(assetPath, FileMode.Open, FileAccess.Read, FileShare.None);
		int before = this.cloud.Mutations.Count;
		int downloads = this.cloud.AssetDownloads;
		BookSyncPreview preview = await this.PreviewAsync();
		preview.HasChanges.ShouldBeTrue();
		preview.UploadCount.ShouldBe(0);
		preview.DownloadCount.ShouldBe(0);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.cloud.Mutations.Count.ShouldBe(before + 1);
		this.cloud.AssetDownloads.ShouldBe(downloads);
	}

	[TestMethod]
	public async Task CorruptLocalBytesPreventAllCloudMutation()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		ChordDatabase database = await this.ReadAsync(this.storeA, this.bookA);
		string path = Path.Combine(this.storeA.GetDirectory(this.bookA), database.SongFiles.Single().RelativePath);
		await File.WriteAllTextAsync(path, "corrupt external bytes", this.TestContext.CancellationToken);
		await Should.ThrowAsync<InvalidDataException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.Mutations.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task StaleCloudReviewCannotOverwriteConcurrentCatalog()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		using ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(a.Id, this.deviceA, this.cloud.Identity));
		BookSyncPreview review = await BookSyncCoordinator.PreviewAsync(this.storeA, this.bookA, this.cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "concurrent cloud");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		string cloudJson = await this.cloud.ReadCatalogAsync(token);
		int mutations = this.cloud.Mutations.Count;
		await Should.ThrowAsync<SyncMergeException>(() => BookSyncCoordinator.ExecuteAsync(
			this.storeA, this.bookA, this.cloud, lease, review, cancellationToken: token));
		this.cloud.Mutations.Count.ShouldBe(mutations);
		(await this.cloud.ReadCatalogAsync(token)).ShouldBe(cloudJson);
	}

	[TestMethod]
	public async Task StaleReviewCanBeRetiredAndComparedAgain()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		using ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(a.Id, this.deviceA, this.cloud.Identity));
		BookSyncPreview review = await BookSyncCoordinator.PreviewAsync(
			this.storeA, this.bookA, this.cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "new cloud");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		await Should.ThrowAsync<SyncMergeException>(() => BookSyncCoordinator.ExecuteAsync(
			this.storeA, this.bookA, this.cloud, lease, review, cancellationToken: token));
		(await BookSyncRecovery.RefreshReviewAsync(this.storeA, this.bookA, this.cloud, lease, token)).ShouldBe(0);
		BookSyncPreview refreshed = await BookSyncCoordinator.PreviewAsync(
			this.storeA, this.bookA, this.cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		refreshed.IsResuming.ShouldBeFalse();
		await BookSyncCoordinator.ExecuteAsync(this.storeA, this.bookA, this.cloud, lease, refreshed, cancellationToken: token);
		(await this.TextAsync(this.storeA, this.bookA, a.SongFiles.Single().Id)).ShouldBe("new cloud");
	}

	[TestMethod]
	public async Task RefreshAfterLocalEditPreservesPreviouslyCapturedBytes()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		this.cloud.AfterMutation = action =>
		{
			if (action.StartsWith("Create:", StringComparison.Ordinal))
			{
				throw new OperationCanceledException();
			}
		};
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.AfterMutation = null;
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "new local after interruption");
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		using (ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(a.Id, this.deviceA, this.cloud.Identity)))
		{
			(await BookSyncRecovery.RefreshReviewAsync(this.storeA, this.bookA, this.cloud, lease, token)).ShouldBe(1);
			(await BookSyncRecovery.RefreshReviewAsync(this.storeA, this.bookA, this.cloud, lease, token)).ShouldBe(0);
		}

		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase result = await this.ReadAsync(this.storeA, this.bookA);
		result.SongFiles.Count.ShouldBe(2);
		SongFile recovered = result.SongFiles.Single(file => file.RecoveryVersion is not null);
		(await this.TextAsync(this.storeA, this.bookA, recovered.Id)).ShouldBe("{title: Original}\r\n[C]Original bytes\r\n");
		(await this.TextAsync(this.storeA, this.bookA, result.SongFiles.Single(file => !file.IsArchived).Id))
			.ShouldBe("new local after interruption");
	}

	[TestMethod]
	public async Task InterruptedSyncAutomaticallyPreservesCapturedBytesAfterLocalEdit()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		this.cloud.AfterMutation = _ => throw new OperationCanceledException();
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.AfterMutation = null;
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "edited after interruption");
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase result = await this.ReadAsync(this.storeA, this.bookA);
		SongFile recovery = result.SongFiles.Single(file => file.RecoveryVersion is not null);
		(await this.TextAsync(this.storeA, this.bookA, recovery.Id)).ShouldBe("{title: Original}\r\n[C]Original bytes\r\n");
		(await this.TextAsync(this.storeA, this.bookA, result.SongFiles.Single(file => !file.IsArchived).Id))
			.ShouldBe("edited after interruption");
		Directory.EnumerateFiles(this.root, "transfer.json", SearchOption.AllDirectories).ShouldBeEmpty();
	}

	[TestMethod]
	public async Task InterruptedCommittedSyncAutomaticallyMergesNewerCloudEdits()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "interrupted office edit");
		this.cloud.AfterMutation = action =>
		{
			if (action == "Replace:database.json")
			{
				throw new OperationCanceledException();
			}
		};
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.AfterMutation = null;
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "newer music room edit");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase result = await this.ReadAsync(this.storeA, this.bookA);
		List<string> versions = [];
		foreach (SongFile file in result.SongFiles)
		{
			versions.Add(await this.TextAsync(this.storeA, this.bookA, file.Id));
		}

		versions.ShouldContain("interrupted office edit");
		versions.ShouldContain("newer music room edit");
		DatabaseJson.Serialize(result).ShouldBe(DatabaseJson.Serialize(DatabaseJson.Deserialize(
			await this.cloud.ReadCatalogAsync(this.TestContext.CancellationToken))));
	}

	[TestMethod]
	public async Task InterruptedSyncAutomaticallyHonorsNewDirectionAndRecoveryEpoch()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "captured local edit");
		this.cloud.AfterMutation = _ => throw new OperationCanceledException();
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.AfterMutation = null;
		int mutations = this.cloud.Mutations.Count;
		ChordDatabase database = await this.ReadAsync(this.storeA, this.bookA);
		using ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(database.Id, this.deviceA, this.cloud.Identity));
		Guid epoch = Guid.NewGuid();
		BookSyncPreview prepared = await BookSyncCoordinator.PrepareAsync(
			this.storeA, this.bookA, this.cloud, lease, SyncDirection.UpdateCloud, epoch, token);
		prepared.IsResuming.ShouldBeFalse();
		prepared.Direction.ShouldBe(SyncDirection.UpdateCloud);
		prepared.RecoveryEpoch.ShouldBe(epoch);
		await BookSyncCoordinator.ExecuteAsync(this.storeA, this.bookA, this.cloud, lease, prepared, cancellationToken: token);
		this.cloud.Mutations.Count.ShouldBeGreaterThan(mutations);
		(await this.ReadAsync(this.storeA, this.bookA)).SongFiles.ShouldContain(file => file.IsArchived);
	}

	[TestMethod]
	public async Task UpdateThisDevicePreservesLocalLoserWithoutCloudWrites()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "local loser");
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "cloud winner");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		int mutations = this.cloud.Mutations.Count;
		string cloudJson = await this.cloud.ReadCatalogAsync(token);
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		using (ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(a.Id, this.deviceA, this.cloud.Identity)))
		{
			BookSyncPreview review = await BookSyncCoordinator.PreviewAsync(
				this.storeA, this.bookA, this.cloud, lease, SyncDirection.UpdateThisDevice, Guid.Empty, token);
			review.UploadCount.ShouldBe(0);
			await BookSyncCoordinator.ExecuteAsync(this.storeA, this.bookA, this.cloud, lease, review, cancellationToken: token);
		}

		this.cloud.Mutations.Count.ShouldBe(mutations);
		(await this.cloud.ReadCatalogAsync(token)).ShouldBe(cloudJson);
		ChordDatabase result = await this.ReadAsync(this.storeA, this.bookA);
		(await this.TextAsync(this.storeA, this.bookA, a.SongFiles.Single().Id)).ShouldBe("cloud winner");
		(await this.TextAsync(this.storeA, this.bookA, result.SongFiles.Single(file => file.RecoveryVersion is not null).Id)).ShouldBe("local loser");
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		(await this.ReadAsync(this.storeB, this.bookB)).SongFiles.Count.ShouldBe(2);
	}

	[TestMethod]
	public async Task UpdateCloudPreservesRemoteLoserBeforeReplacingCatalog()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.bookB = await this.DownloadAsync();
		await this.EditAsync(this.storeA, this.bookA, this.deviceA, "local winner");
		await this.EditAsync(this.storeB, this.bookB, this.deviceB, "cloud loser");
		await this.SyncAsync(this.storeB, this.bookB, this.deviceB);
		ChordDatabase a = await this.ReadAsync(this.storeA, this.bookA);
		using ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(a.Id, this.deviceA, this.cloud.Identity));
		BookSyncPreview review = await BookSyncCoordinator.PreviewAsync(
			this.storeA, this.bookA, this.cloud, lease, SyncDirection.UpdateCloud, Guid.Empty, token);
		review.RecoveryCount.ShouldBe(1);
		await BookSyncCoordinator.ExecuteAsync(this.storeA, this.bookA, this.cloud, lease, review, cancellationToken: token);
		ChordDatabase result = await this.ReadAsync(this.storeA, this.bookA);
		(await this.TextAsync(this.storeA, this.bookA, a.SongFiles.Single().Id)).ShouldBe("local winner");
		(await this.TextAsync(this.storeA, this.bookA, result.SongFiles.Single(file => file.RecoveryVersion is not null).Id)).ShouldBe("cloud loser");
	}

	[TestMethod]
	public async Task CorruptCapturedCatalogStopsResumeWithoutCloudMutation()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		this.cloud.AfterMutation = _ => throw new OperationCanceledException();
		await Should.ThrowAsync<OperationCanceledException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.AfterMutation = null;
		int mutations = this.cloud.Mutations.Count;
		string captured = Directory.EnumerateFiles(this.root, "*.catalog", SearchOption.AllDirectories).First();
		await File.WriteAllTextAsync(captured, "{}", token);
		await Should.ThrowAsync<InvalidDataException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		this.cloud.Mutations.Count.ShouldBe(mutations);
		File.Exists(captured).ShouldBeTrue();
		Directory.EnumerateFiles(this.root, "transfer.json", SearchOption.AllDirectories).Count().ShouldBe(1);
	}

	[TestMethod]
	public async Task UnchangedBookSyncDoesNotUploadOrReadSheets()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		int mutations = this.cloud.Mutations.Count;
		int downloads = this.cloud.AssetDownloads;
		string original = await this.storeA.ReadDatabaseJsonAsync(this.bookA, token);
		(await this.PreviewAsync()).HasChanges.ShouldBeFalse();
		BookSyncResult result = await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		result.UploadedSheets.ShouldBe(0);
		result.DownloadedSheets.ShouldBe(0);
		this.cloud.Mutations.Count.ShouldBe(mutations);
		this.cloud.AssetDownloads.ShouldBe(downloads);
		(await this.storeA.ReadDatabaseJsonAsync(this.bookA, token)).ShouldBe(original);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task VerboseCatalogFormattingDoesNotCountAsAChange(bool cloudFormatting)
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		ChordDatabase database = await this.ReadAsync(this.storeA, this.bookA);
		JsonSerializerOptions verbose = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true,
			Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
		};
		string json = JsonSerializer.Serialize(database, verbose);
		if (cloudFormatting)
		{
			CloudBookSnapshot snapshot = (await CloudBookReader.ReadAsync(this.cloud, token))!;
			using MemoryStream bytes = new(Encoding.UTF8.GetBytes(json));
			await this.cloud.ReplaceAsync(snapshot.DatabaseItem.Id, snapshot.DatabaseItem.Version, bytes, token);
		}
		else
		{
			await File.WriteAllTextAsync(Path.Combine(this.storeA.GetDirectory(this.bookA), "database.json"), json, token);
		}

		int mutations = this.cloud.Mutations.Count;
		int downloads = this.cloud.AssetDownloads;
		BookSyncPreview preview = await this.PreviewAsync();
		preview.HasChanges.ShouldBeFalse();
		preview.UploadCount.ShouldBe(0);
		preview.DownloadCount.ShouldBe(0);
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		this.cloud.Mutations.Count.ShouldBe(mutations);
		this.cloud.AssetDownloads.ShouldBe(downloads);
	}

	[TestMethod]
	public async Task MutatedUploadedSheetCannotBeCommitted()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		string original = await this.storeA.ReadDatabaseJsonAsync(this.bookA, token);
		this.cloud.AfterMutation = action =>
		{
			if (action.StartsWith("Create:", StringComparison.Ordinal) && action != "Create:database.json")
			{
				this.cloud.AfterMutation = null;
				CloudChangeSet items = this.cloud.ListOrGetChangesAsync(null, token).GetAwaiter().GetResult();
				CloudReplicaItem item = items.Items.Single();
				using MemoryStream changed = new(Encoding.UTF8.GetBytes("unexpected external edit"));
				this.cloud.ReplaceAsync(item.Id, item.Version, changed, token).GetAwaiter().GetResult();
			}
		};
		await Should.ThrowAsync<SyncMergeException>(() => this.SyncAsync(this.storeA, this.bookA, this.deviceA));
		CloudChangeSet remaining = await this.cloud.ListOrGetChangesAsync(null, token);
		remaining.Items.Any(item => item.Name == "database.json").ShouldBeFalse();
		(await this.storeA.ReadDatabaseJsonAsync(this.bookA, token)).ShouldBe(original);
	}

	[TestMethod]
	public async Task DownloadRejectsChangedSelectedBookIdentity()
	{
		await this.InitializeAsync(this.TestContext.CancellationToken);
		CancellationToken token = this.TestContext.CancellationToken;
		await this.SyncAsync(this.storeA, this.bookA, this.deviceA);
		await Should.ThrowAsync<SyncMergeException>(() => CloudBookDownloader.DownloadAsync(
			this.storeB,
			this.cloud,
			this.deviceB,
			this.States(this.deviceB),
			Path.Combine(this.root, "download-stage"),
			cancellationToken: token,
			expectedBookId: Guid.NewGuid()));
		Directory.EnumerateDirectories(Path.Combine(this.root, "b")).ShouldBeEmpty();
	}

	#endregion

	#region Private Methods

	private ReplicaStateStore States(Guid device) => new(Path.Combine(this.root, "state-" + device.ToString("N")));

	private async Task<BookLocation> DownloadAsync()
		=> await CloudBookDownloader.DownloadAsync(
			this.storeB,
			this.cloud,
			this.deviceB,
			this.States(this.deviceB),
			Path.Combine(this.root, "download-stage"),
			cancellationToken: this.TestContext.CancellationToken);

	private async Task<BookSyncPreview> PreviewAsync()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		ChordDatabase database = await this.ReadAsync(this.storeA, this.bookA);
		using ReplicaStateLease lease = this.States(this.deviceA).Acquire(new(database.Id, this.deviceA, this.cloud.Identity));
		return await BookSyncCoordinator.PreviewAsync(
			this.storeA, this.bookA, this.cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
	}

	private async Task<BookSyncResult> SyncAsync(FileSystemBookStore store, BookLocation location, Guid device)
	{
		CancellationToken token = this.TestContext.CancellationToken;
		ChordDatabase database = await this.ReadAsync(store, location);
		using ReplicaStateLease lease = this.States(device).Acquire(new(database.Id, device, this.cloud.Identity));
		BookSyncPreview review = await BookSyncCoordinator.PrepareAsync(store, location, this.cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		return await BookSyncCoordinator.ExecuteAsync(store, location, this.cloud, lease, review, cancellationToken: token);
	}

	private async Task<ChordDatabase> ReadAsync(FileSystemBookStore store, BookLocation location)
		=> DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, this.TestContext.CancellationToken));

	private async Task<string> TextAsync(FileSystemBookStore store, BookLocation location, Guid fileId)
	{
		await using Stream stream = await store.OpenManagedAssetAsync(location, fileId, this.TestContext.CancellationToken);
		using StreamReader reader = new(stream);
		return await reader.ReadToEndAsync(this.TestContext.CancellationToken);
	}

	private async Task EditAsync(FileSystemBookStore store, BookLocation location, Guid device, string text, Guid? fileId = null)
	{
		CancellationToken token = this.TestContext.CancellationToken;
		string original = await store.ReadDatabaseJsonAsync(location, token);
		ChordDatabase database = DatabaseJson.Deserialize(original);
		SongFile file = database.SongFiles.Single(file => fileId.HasValue ? file.Id == fileId : !file.IsArchived);
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		file.ContentHash = SongFileAnalyzer.Hash(bytes);
		file.ObservedLength = bytes.Length;
		file.Revision = new()
		{
			Revision = file.Revision.Revision + 1, ModifiedUtc = DateTimeOffset.UtcNow, DeviceId = device,
		};
		await using IStagedBookWrite write = await store.StageWriteAsync(location, original, token);
		using MemoryStream source = new(bytes);
		await write.WriteManagedAssetAsync(file.Id, file.RelativePath, source, token);
		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), token);
		await write.CommitAsync(token);
	}

	#endregion
}
