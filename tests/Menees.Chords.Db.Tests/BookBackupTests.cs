#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookBackupTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RestoreAsNewPreservesEntitiesAndBytesButChangesBookIdentity()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid sourceDevice = Guid.NewGuid();
		BookLocation source = await store.CreateBookAsync("Source", sourceDevice, cancellationToken);
		BookImportResult imported = await BookImportService.ImportAsync(
			store,
			source,
			"extensionless opensong",
			new MemoryStream(TestData.OpenSongBytes(), writable: false),
			sourceDevice,
			cancellationToken);
		ChordDatabase sourceDatabase = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(source, cancellationToken));
		using MemoryStream backup = new();
		await BookBackup.CreateAsync(store, source, backup, cancellationToken);
		backup.Position = 0;

		Guid restoreDevice = Guid.NewGuid();
		BookLocation restoredLocation = await BookBackup.RestoreAsNewAsync(
			store,
			backup,
			restoreDevice,
			"Restored",
			cancellationToken);

		ChordDatabase restored = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(restoredLocation, cancellationToken));
		restored.Id.ShouldNotBe(sourceDatabase.Id);
		restored.Name.ShouldBe("Restored");
		restored.Songs.Single().Id.ShouldBe(sourceDatabase.Songs.Single().Id);
		restored.SongFiles.Single().Id.ShouldBe(imported.SongFileId);
		restored.Revision.DeviceId.ShouldBe(restoreDevice);
		using Stream restoredContent = await store.OpenManagedAssetAsync(restoredLocation, imported.SongFileId, cancellationToken);
		using MemoryStream copy = new();
		await restoredContent.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
	}

	#endregion
}
