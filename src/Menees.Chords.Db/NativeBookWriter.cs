#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Writes complete books efficiently through the portable staged-write contract.</summary>
public static class NativeBookWriter
{
	#region Public API

	/// <summary>Creates and populates a native book in one bulk operation.</summary>
	/// <param name="store">The destination store.</param>
	/// <param name="sourceDatabase">The complete source database. It is not modified.</param>
	/// <param name="assets">Exactly one stream source for each managed song file.</param>
	/// <param name="deviceId">The destination device identifier.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The committed database and opaque destination location.</returns>
	public static async Task<NativeBookWriteResult> CreateAsync(
		IBookStore store,
		ChordDatabase sourceDatabase,
		IReadOnlyCollection<NativeBookAsset> assets,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sourceDatabase);
		ArgumentNullException.ThrowIfNull(assets);
		ValidateAssetIds(sourceDatabase, assets);
		BookLocation location = await store.CreateBookAsync(sourceDatabase.Name, deviceId, cancellationToken).ConfigureAwait(false);
		try
		{
			ChordDatabase created = DatabaseJson.Deserialize(
				await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
			ChordDatabase database = DatabaseJson.Deserialize(DatabaseJson.Serialize(sourceDatabase));
			database.Id = created.Id;
			await WriteAsync(store, location, database, assets, cancellationToken).ConfigureAwait(false);
			return new NativeBookWriteResult(location, database);
		}
#pragma warning disable CA1031 // Preserve the original write failure when best-effort cleanup also fails.
		catch
		{
			try
			{
				await store.DeleteBookAsync(location, CancellationToken.None).ConfigureAwait(false);
			}
			catch
			{
			}

			throw;
		}
#pragma warning restore CA1031
	}

	/// <summary>Replaces an existing book with complete database and asset content.</summary>
	/// <param name="store">The destination store.</param>
	/// <param name="location">The destination's opaque location.</param>
	/// <param name="database">The complete destination database.</param>
	/// <param name="assets">Exactly one stream source for each managed song file.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public static async Task WriteAsync(
		IBookStore store,
		BookLocation location,
		ChordDatabase database,
		IReadOnlyCollection<NativeBookAsset> assets,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(assets);
		ValidateAssetIds(database, assets);
		Dictionary<Guid, NativeBookAsset> assetsById = assets.ToDictionary(asset => asset.SongFileId);
		await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
		foreach (SongFile file in database.SongFiles)
		{
			await using Stream content = await assetsById[file.Id].OpenReadAsync(cancellationToken).ConfigureAwait(false);
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, content, cancellationToken).ConfigureAwait(false);
		}

		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
		await write.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Private Methods

	private static void ValidateAssetIds(ChordDatabase database, IReadOnlyCollection<NativeBookAsset> assets)
	{
		HashSet<Guid> fileIds = [.. database.SongFiles.Select(file => file.Id)];
		HashSet<Guid> assetIds = [.. assets.Select(asset => asset.SongFileId)];
		if (assetIds.Count != assets.Count || !fileIds.SetEquals(assetIds))
		{
			throw new BookStoreValidationException(
				"Bulk assets must contain exactly one stream source for every database song file.");
		}
	}

	#endregion
}
