#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class CloudBookComparisonTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ComparisonReadsOnlyDatabaseAndIgnoresUnmanagedAssets()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		ChordDatabase local = ChordDatabase.Create("Test", device);
		local.Songs.Add(new() { Id = Guid.NewGuid(), Title = "Original" });
		Guid fileId = Guid.NewGuid();
		local.SongFiles.Add(new()
		{
			Id = fileId,
			SongId = local.Songs[0].Id,
			RelativePath = PortableManagedFileName.Create("Song", fileId, ".txt"),
			ContentHash = new string('A', 64),
		});
		BookMergeBase baseline = BookSyncMerger.CreateBase(local);
		ChordDatabase cloud = DatabaseJson.Deserialize(DatabaseJson.Serialize(local));
		cloud.Songs[0].Title = "From cloud";
		InMemoryCloudReplica storage = new(new("Test", "account", "folder"));
		await storage.AuthenticateAsync(token);
		CloudReplicaItem database = await AddAsync(storage, "database.json", DatabaseJson.Serialize(cloud), token);
		await AddAsync(storage, cloud.SongFiles[0].RelativePath, "sheet", token);
		await AddAsync(storage, "README.md", "unmanaged", token);
		ReadOnlyReplica replica = new(storage);
		CloudBookComparison result = await CloudBookComparisonService.CompareAsync(
			local, replica, baseline, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, token);
		result.Merge.Database.Songs.Single().Title.ShouldBe("From cloud");
		result.ManagedItems.Keys.ShouldBe([fileId]);
		result.DatabaseItem.Version.ShouldBe(database.Version);
		replica.Downloads.ShouldBe([database.Id]);
		replica.Listings.ShouldBe(1);
		storage.MutationLog.Count.ShouldBe(3);
		local.Songs[0].Title.ShouldBe("Original");
	}

	[TestMethod]
	public async Task MissingCloudSheetPreventsComparisonWithoutAnyMutation()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		ChordDatabase database = ChordDatabase.Create("Test", device);
		database.Songs.Add(new() { Id = Guid.NewGuid(), Title = "Song" });
		Guid id = Guid.NewGuid();
		database.SongFiles.Add(new()
		{
			Id = id,
			SongId = database.Songs[0].Id,
			RelativePath = PortableManagedFileName.Create("Missing", id, ".txt"),
		});
		InMemoryCloudReplica storage = new(new("Test", "account", "folder"));
		await storage.AuthenticateAsync(token);
		await AddAsync(storage, "database.json", DatabaseJson.Serialize(database), token);
		await Should.ThrowAsync<SyncMergeException>(() => CloudBookComparisonService.CompareAsync(
			database, new ReadOnlyReplica(storage), null, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, token));
		storage.MutationLog.Count.ShouldBe(1);
	}

	[TestMethod]
	public async Task MissingCloudDatabaseDoesNotImplicitlyInitializeTheFolder()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryCloudReplica storage = new(new("Test", "account", "folder"));
		await storage.AuthenticateAsync(token);
		await Should.ThrowAsync<SyncMergeException>(() => CloudBookComparisonService.CompareAsync(
			ChordDatabase.Create("Test", device), new ReadOnlyReplica(storage), null, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, token));
		storage.MutationLog.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task PortableFilenameCollisionsAreRejectedBeforeDownloading()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryCloudReplica storage = new(new("Test", "account", "folder"));
		await storage.AuthenticateAsync(token);
		await AddAsync(storage, "database.json", "{}", token);
		await AddAsync(storage, "DATABASE.JSON", "{}", token);
		ReadOnlyReplica replica = new(storage);
		await Should.ThrowAsync<SyncMergeException>(() => CloudBookComparisonService.CompareAsync(
			ChordDatabase.Create("Test", device), replica, null, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, token));
		replica.Downloads.ShouldBeEmpty();
	}

	[TestMethod]
	public async Task CancellationAndSignedOutAccountsNeverOpenAuthenticationUi()
	{
		Guid device = Guid.NewGuid();
		ChordDatabase database = ChordDatabase.Create("Test", device);
		ReadOnlyReplica replica = new(new(new("Test", "account", "folder")));
		await Should.ThrowAsync<InvalidOperationException>(() => CloudBookComparisonService.CompareAsync(
			database, replica, null, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, this.TestContext.CancellationToken));
		using CancellationTokenSource canceled = new();
		await canceled.CancelAsync();
		await Should.ThrowAsync<OperationCanceledException>(() => CloudBookComparisonService.CompareAsync(
			database, replica, null, SyncDirection.TwoWay, device, DateTimeOffset.UtcNow, canceled.Token));
		replica.Listings.ShouldBe(0);
	}

	#endregion

	#region Private Methods

	private static async Task<CloudReplicaItem> AddAsync(InMemoryCloudReplica replica, string name, string text, CancellationToken cancellationToken)
	{
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(text));
		return await replica.CreateAsync(name, stream, cancellationToken);
	}

	#endregion

	#region Private Types

	private sealed class ReadOnlyReplica(InMemoryCloudReplica storage) : ICloudReplica
	{
		public CloudReplicaIdentity Identity => storage.Identity;

		public CloudReplicaCapabilities Capabilities => storage.Capabilities;

		public bool IsAuthenticated => storage.IsAuthenticated;

		public int Listings { get; private set; }

		public List<ProviderItemId> Downloads { get; } = [];

		public Task AuthenticateAsync(CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task DisconnectAsync(CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<CloudChangeSet> ListOrGetChangesAsync(string? changeToken, CancellationToken cancellationToken)
		{
			changeToken.ShouldBeNull();
			this.Listings++;
			return storage.ListOrGetChangesAsync(changeToken, cancellationToken);
		}

		public Task<Stream> DownloadAsync(ProviderItemId itemId, CancellationToken cancellationToken)
		{
			this.Downloads.Add(itemId);
			return storage.DownloadAsync(itemId, cancellationToken);
		}

		public Task<CloudReplicaItem> CreateAsync(string name, Stream content, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<CloudReplicaItem> ReplaceAsync(
			ProviderItemId itemId, ProviderItemVersion expectedVersion, Stream content, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<CloudReplicaItem> RenameAsync(
			ProviderItemId itemId, ProviderItemVersion expectedVersion, string name, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task DeleteAsync(ProviderItemId itemId, ProviderItemVersion expectedVersion, CancellationToken cancellationToken)
			=> throw new NotSupportedException();
	}

	#endregion
}
