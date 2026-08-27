#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Validates canonical database JSON and every explicitly managed asset without modifying the book.</summary>
public static class BookValidator
{
	#region Public Methods

	/// <summary>Validates a complete book using only portable store operations.</summary>
	public static async Task<BookValidationReport> ValidateAsync(
		IBookStore store,
		BookLocation location,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(location);

		List<BookValidationIssue> issues = [];
		ChordDatabase? database = await ReadDatabaseAsync(store, location, issues, cancellationToken).ConfigureAwait(false);
		if (database is not null)
		{
			Dictionary<Guid, ManagedAssetDescriptor> descriptors = await ReadDescriptorsAsync(
				store,
				location,
				issues,
				cancellationToken).ConfigureAwait(false);
			Dictionary<Guid, SongFile> files = database.SongFiles.ToDictionary(file => file.Id);
			foreach (SongFile file in database.SongFiles)
			{
				if (descriptors.TryGetValue(file.Id, out ManagedAssetDescriptor? descriptor))
				{
					await ValidateAssetAsync(store, location, file, descriptor, issues, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					issues.Add(new(
						BookValidationIssueKind.MissingAsset,
						"The database-referenced managed asset is missing.",
						file.Id,
						file.RelativePath));
				}
			}

			foreach (ManagedAssetDescriptor descriptor in descriptors.Values.Where(item => !files.ContainsKey(item.SongFileId)))
			{
				issues.Add(new(
					BookValidationIssueKind.UnexpectedManagedAsset,
					"The store exposed an asset that is not referenced by the database.",
					descriptor.SongFileId,
					descriptor.RelativePath));
			}
		}

		return new(database, issues);
	}

	#endregion

	#region Private Methods

	private static async Task<ChordDatabase?> ReadDatabaseAsync(
		IBookStore store,
		BookLocation location,
		List<BookValidationIssue> issues,
		CancellationToken cancellationToken)
	{
		ChordDatabase? result = null;
		try
		{
			string json = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			result = DatabaseJson.Deserialize(json);
		}
		catch (DatabaseFormatException exception)
		{
			issues.Add(new(BookValidationIssueKind.InvalidDatabase, exception.Message));
		}
		catch (BookStoreException exception)
		{
			issues.Add(new(BookValidationIssueKind.InvalidDatabase, exception.Message));
		}

		return result;
	}

	private static async Task<Dictionary<Guid, ManagedAssetDescriptor>> ReadDescriptorsAsync(
		IBookStore store,
		BookLocation location,
		List<BookValidationIssue> issues,
		CancellationToken cancellationToken)
	{
		Dictionary<Guid, ManagedAssetDescriptor> result = [];
		await foreach (ManagedAssetDescriptor descriptor in store.EnumerateManagedAssetsAsync(location, cancellationToken).ConfigureAwait(false))
		{
			if (!result.TryAdd(descriptor.SongFileId, descriptor))
			{
				issues.Add(new(
					BookValidationIssueKind.UnexpectedManagedAsset,
					"The store exposed the same song-file identity more than once.",
					descriptor.SongFileId,
					descriptor.RelativePath));
			}
		}

		return result;
	}

	private static async Task ValidateAssetAsync(
		IBookStore store,
		BookLocation location,
		SongFile file,
		ManagedAssetDescriptor descriptor,
		List<BookValidationIssue> issues,
		CancellationToken cancellationToken)
	{
		if (!StringComparer.Ordinal.Equals(file.RelativePath, descriptor.RelativePath))
		{
			issues.Add(new(BookValidationIssueKind.PathMismatch, "The managed path differs from database.json.", file.Id, file.RelativePath));
		}

		if (file.ObservedLength is long expectedLength && expectedLength != descriptor.Length)
		{
			issues.Add(new(BookValidationIssueKind.LengthMismatch, "The managed asset length differs from database.json.", file.Id, file.RelativePath));
		}

		try
		{
			using Stream content = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken).ConfigureAwait(false);
			byte[] hashBytes = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
			string actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
			if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, descriptor.ContentHash)
				|| (!string.IsNullOrEmpty(file.ContentHash) && !StringComparer.OrdinalIgnoreCase.Equals(actualHash, file.ContentHash)))
			{
				issues.Add(new(BookValidationIssueKind.HashMismatch, "The managed asset content hash is inconsistent.", file.Id, file.RelativePath));
			}
		}
		catch (IOException exception)
		{
			issues.Add(new(BookValidationIssueKind.UnreadableAsset, exception.Message, file.Id, file.RelativePath));
		}
		catch (UnauthorizedAccessException exception)
		{
			issues.Add(new(BookValidationIssueKind.UnreadableAsset, exception.Message, file.Id, file.RelativePath));
		}
		catch (BookStoreException exception)
		{
			issues.Add(new(BookValidationIssueKind.UnreadableAsset, exception.Message, file.Id, file.RelativePath));
		}
	}

	#endregion
}
