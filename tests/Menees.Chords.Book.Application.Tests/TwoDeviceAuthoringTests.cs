#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;
using Menees.Chords.Sync;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class TwoDeviceAuthoringTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ActualEditorSavesRecoveryResolutionAndNextSyncConvergeAcrossTwoFilesystemBooks()
	{
		CancellationToken token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), "ChordTwoDevice-" + Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore storeA = new(Path.Combine(root, "a"));
			using FileSystemBookStore storeB = new(Path.Combine(root, "b"));
			Guid deviceA = Guid.NewGuid(), deviceB = Guid.NewGuid();
			ReplicaStateStore statesA = new(Path.Combine(root, "states-a")), statesB = new(Path.Combine(root, "states-b"));
			InMemoryCloudReplica cloud = new(new("OneDrive", "disposable-account", "disposable-folder"));
			await cloud.AuthenticateAsync(token);
			BookLocation bookA = await storeA.CreateBookAsync("Two device fixture", deviceA, token);
			BookApplicationSession appA = new(), appB = new();
			await appA.ActivateAsync(storeA, bookA, token);
			Guid song = await appA.CreateSongAsync("Fixture", [], [], "Original\r\n[C]Words\n", deviceA, token);
			await SyncAsync(storeA, bookA, statesA, deviceA, cloud, token);
			BookLocation bookB = await CloudBookDownloader.DownloadAsync(
				storeB, cloud, deviceB, statesB, Path.Combine(root, "download"), cancellationToken: token);
			await appB.ActivateAsync(storeB, bookB, token);
			await appA.ReloadAsync(token);
			await EditAsync(appA, song, "Office\r\n[D]Exact\n", deviceA, token);
			await EditAsync(appB, song, "Music room\n[G]Exact\r\n", deviceB, token);
			await SyncAsync(storeA, bookA, statesA, deviceA, cloud, token);
			await SyncAsync(storeB, bookB, statesB, deviceB, cloud, token);
			await SyncAsync(storeA, bookA, statesA, deviceA, cloud, token);
			await appB.ReloadAsync(token);
			SongFile active = appB.Database!.SongFiles.Single(file => !file.IsArchived);
			SongFile recovered = appB.Database.SongFiles.Single(file => file.RecoveryVersion is not null);
			SongVersionComparison comparison = await appB.CompareSongVersionsAsync(song, active.Id, recovered.Id, token);
			string?[] versions = [comparison.Left.Text, comparison.Right.Text];
			versions.ShouldContain("Office\r\n[D]Exact\n");
			versions.ShouldContain("Music room\n[G]Exact\r\n");
			appB.Database.SongFiles.Count(file => file.SavedVersion is not null).ShouldBe(2);
			await appB.ResolveSongVersionsAsync(comparison, SongVersionChoice.KeepRight, deviceB, token);
			using (ReplicaStateLease lease = statesB.Acquire(new(appB.Database.Id, deviceB, cloud.Identity)))
			{
				BookSyncPreview preview = await BookSyncCoordinator.PreviewAsync(
					storeB, bookB, cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
				preview.LocalRemovedSheetCount.ShouldBe(0);
				preview.CloudRemovedSheetCount.ShouldBe(1);
				await BookSyncCoordinator.ExecuteAsync(storeB, bookB, cloud, lease, preview, cancellationToken: token);
			}

			await SyncAsync(storeA, bookA, statesA, deviceA, cloud, token);
			await appA.ReloadAsync(token);
			appA.Database!.SongFiles.Single(file => !file.IsArchived).Id.ShouldBe(recovered.Id);
			appA.Database.Tombstones.Any(item => item.EntityId == active.Id).ShouldBeTrue();
			(await appA.GetSongEditAsync(song, token)).Text.ShouldBe(comparison.Right.Text);
			await SyncAsync(storeA, bookA, statesA, deviceA, cloud, token);
			await SyncAsync(storeB, bookB, statesB, deviceB, cloud, token);
			await appB.ReloadAsync(token);
			appB.Database!.SongFiles.Any(file => file.Id == active.Id).ShouldBeFalse();
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

	#region Private Methods

	private static async Task EditAsync(BookApplicationSession app, Guid song, string text, Guid device, CancellationToken token)
	{
		SongEditDocument edit = await app.GetSongEditAsync(song, token);
		await app.SaveSongEditAsync(edit, edit.Title, edit.Artists, edit.Tags, text, device, token);
	}

	private static async Task SyncAsync(
		FileSystemBookStore store, BookLocation book, ReplicaStateStore states, Guid device, InMemoryCloudReplica cloud, CancellationToken token)
	{
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(book, token));
		using ReplicaStateLease lease = states.Acquire(new(database.Id, device, cloud.Identity));
		BookSyncPreview preview = await BookSyncCoordinator.PreviewAsync(store, book, cloud, lease, SyncDirection.TwoWay, Guid.Empty, token);
		await BookSyncCoordinator.ExecuteAsync(store, book, cloud, lease, preview, cancellationToken: token);
	}

	#endregion
}
