#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync.Tests;

[TestClass]
public sealed class BookSyncScaleTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task TenThousandSheetsMetadataSyncUsesBoundedListingsAndNoAssetReads()
	{
		const int SongCount = 10_000;
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), "ChordSyncScale-" + Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation location = await store.CreateBookAsync("Scale fixture", device, token);
			ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, token));
			FaultingCloudReplica cloud = new();
			await cloud.AuthenticateAsync(token);
			string emptyHash = SongFileAnalyzer.Hash([]);
			string directory = store.GetDirectory(location);
			for (int index = 0; index < SongCount; index++)
			{
				Song song = new() { Id = Guid.NewGuid(), Title = "Song " + index, Revision = RevisionStamp.Initial(device) };
				SongFile file = new()
				{
					Id = Guid.NewGuid(),
					SongId = song.Id,
					RelativePath = index + ".cho",
					ContentHash = emptyHash,
					ObservedLength = 0,
					Revision = RevisionStamp.Initial(device),
				};
				database.Songs.Add(song);
				database.SongFiles.Add(file);
				await File.WriteAllBytesAsync(Path.Combine(directory, file.RelativePath), [], token);
				using MemoryStream empty = new();
				await cloud.SeedAsync(file.RelativePath, empty, token);
			}

			string original = DatabaseJson.Serialize(database);
			await File.WriteAllTextAsync(Path.Combine(directory, "database.json"), original, token);
			using MemoryStream catalog = new(Encoding.UTF8.GetBytes(original));
			await cloud.SeedAsync("database.json", catalog, token);
			CloudReplicaKey key = new(database.Id, device, cloud.Identity);
			using ReplicaStateLease lease = new ReplicaStateStore(Path.Combine(root, "replicas")).Acquire(key);
			CloudReplicaState state = new(key) { MergeBase = JsonSerializer.Serialize(BookSyncMerger.CreateBase(database)) };
			await lease.SaveStateAsync(state, token);
			database.Songs[^1].Title = "One changed title";
			database.Songs[^1].Revision.Revision++;
			await store.CommitMetadataAsync(location, original, database, token);
			using FileStream locked = new(Path.Combine(directory, database.SongFiles[0].RelativePath), FileMode.Open, FileAccess.Read, FileShare.None);
			int mutations = cloud.Mutations.Count;
			long allocated = GC.GetTotalAllocatedBytes();
			Stopwatch timer = Stopwatch.StartNew();
			BookSyncPreview review = await BookSyncCoordinator.PreviewAsync(store, location, cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
			long reviewedAllocations = GC.GetTotalAllocatedBytes() - allocated;
			long reviewMilliseconds = timer.ElapsedMilliseconds;
			await BookSyncCoordinator.ExecuteAsync(store, location, cloud, lease, review, cancellationToken: token);
			timer.Stop();
			cloud.AssetDownloads.ShouldBe(0);
			cloud.Mutations.Count.ShouldBe(mutations + 1);
			const int MaximumListings = 4;
			cloud.ListCount.ShouldBeLessThanOrEqualTo(MaximumListings);
			review.UploadCount.ShouldBe(0);
			review.DownloadCount.ShouldBe(0);
			this.TestContext.WriteLine(
				$"10,000 songs/sheets; metadata review+sync {timer.ElapsedMilliseconds:N0} ms; "
				+ $"process allocations {GC.GetTotalAllocatedBytes() - allocated:N0} bytes; {cloud.ListCount} listings; zero asset transfers. "
				+ $"Review alone: {reviewMilliseconds:N0} ms, {reviewedAllocations:N0} bytes; catalog {original.Length:N0} characters.");
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}
	#endregion
}
