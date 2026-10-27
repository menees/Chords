#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Loads two related versions and atomically resolves them without changing authored bytes.</summary>
public static class SongVersionComparisonService
{
	#region Public Methods

	public static async Task<SongVersionComparison> ReadAsync(
		IBookStore store,
		BookLocation location,
		Guid songId,
		Guid leftId,
		Guid rightId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		if (leftId == rightId)
		{
			throw new ArgumentException("Select two different sheets.");
		}

		string json = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(json);
		SongFile left = database.SongFiles.Single(file => file.SongId == songId && file.Id == leftId);
		SongFile right = database.SongFiles.Single(file => file.SongId == songId && file.Id == rightId);
		SongVersionSide leftSide = await ReadSideAsync(store, location, left, cancellationToken).ConfigureAwait(false);
		SongVersionSide rightSide = await ReadSideAsync(store, location, right, cancellationToken).ConfigureAwait(false);
		return new(database.Id, songId, json, leftSide, rightSide);
	}

	public static async Task<string> ResolveAsync(
		IBookStore store,
		BookLocation location,
		SongVersionComparison comparison,
		SongVersionChoice choice,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(comparison);
		if (!comparison.CanResolve || !Enum.IsDefined(choice) || deviceId == Guid.Empty)
		{
			throw new ArgumentException("Choose a valid resolution and device identity.");
		}

		if (await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false) != comparison.ExpectedJson)
		{
			throw new BookStoreConcurrencyException();
		}

		ChordDatabase database = DatabaseJson.Deserialize(comparison.ExpectedJson);
		SongFile left = database.SongFiles.Single(file => file.SongId == comparison.SongId && file.Id == comparison.Left.Id);
		SongFile right = database.SongFiles.Single(file => file.SongId == comparison.SongId && file.Id == comparison.Right.Id);
		List<SongFile> ordered = [.. database.SongFiles.Where(file => file.SongId == comparison.SongId)
			.OrderByDescending(file => file.DisplayPriority).ThenBy(file => file.MediaKind).ThenBy(file => file.Id)];
		SongFile? priorDefault = ordered.FirstOrDefault(file => !file.IsArchived && file.RecoveryVersion is null);
		SongFile kept = choice == SongVersionChoice.KeepRight ? right : left;
		SongFile? deleted = choice == SongVersionChoice.KeepBoth ? null : kept == left ? right : left;
		if (choice == SongVersionChoice.KeepBoth && (priorDefault == left || priorDefault == right))
		{
			kept = priorDefault;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (SongFile file in new[] { left, right }.Where(file => file != deleted))
		{
			file.IsArchived = false;
			file.RecoveryVersion = null;
			file.SavedVersion = null;
			file.Revision = Next(file.Revision, deviceId, now);
		}

		ordered.Remove(kept);
		ordered.Insert(0, kept);
		for (int index = 0; index < ordered.Count; index++)
		{
			SongFile file = ordered[index];
			int priority = ordered.Count - index;
			if (file.DisplayPriority != priority)
			{
				file.DisplayPriority = priority;
				file.Revision = Next(file.Revision, deviceId, now);
			}
		}

		database.Revision = Next(database.Revision, deviceId, now);
		string result;
		if (deleted is null)
		{
			await VerifySideAsync(store, location, comparison.Left, cancellationToken).ConfigureAwait(false);
			await VerifySideAsync(store, location, comparison.Right, cancellationToken).ConfigureAwait(false);
			result = await store.CommitMetadataAsync(location, comparison.ExpectedJson, database, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await using IStagedBookWrite write = await store.StageWriteAsync(location, comparison.ExpectedJson, cancellationToken).ConfigureAwait(false);
			write.VerifyOriginalAssets = true;

			// Re-stage exactly the retained bytes so the commit validates both compared originals, including an external edit during copying.
			await using Stream source = await store.OpenManagedAssetAsync(location, kept.Id, cancellationToken).ConfigureAwait(false);
			await write.WriteManagedAssetAsync(kept.Id, kept.RelativePath, source, cancellationToken).ConfigureAwait(false);
			await write.DeleteManagedAssetAsync(deleted.Id, cancellationToken).ConfigureAwait(false);
			database.SongFiles.Remove(deleted);
			database.Tombstones.Add(new() { EntityId = deleted.Id, EntityType = nameof(SongFile), Revision = Next(deleted.Revision, deviceId, now) });
			SongFilePreferenceService.Replace(database, deleted.Id, kept.Id, deviceId, now);
			result = DatabaseJson.Serialize(database);
			await write.WriteDatabaseJsonAsync(result, cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		}

		return result;
	}

	#endregion

	#region Internal Methods

	internal static async Task<SongVersionSide> ReadSideAsync(
		IBookStore store, BookLocation location, SongFile file, CancellationToken cancellationToken)
	{
		string? text = file.MediaKind == MediaKind.Text
			? await ReadTextAsync(store, location, file, cancellationToken).ConfigureAwait(false) : null;
		return new(
			file.Id,
			file.RelativePath,
			file.MediaKind,
			file.SavedVersion?.OriginalModifiedUtc ?? file.Revision.ModifiedUtc,
			file.IsArchived,
			file.RecoveryVersion?.RecoveredFromSyncUtc,
			file.ContentHash,
			text,
			file.SavedVersion?.SavedUtc);
	}

	internal static async Task<string> ReadTextAsync(Stream source, string? textEncoding, string contentHash, CancellationToken cancellationToken)
	{
		const int MaximumBytes = 4 * 1024 * 1024;
		const int BufferSize = 8192;
		using MemoryStream bytes = new();
		byte[] buffer = new byte[BufferSize];
		int count;
		while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
		{
			if (bytes.Length > MaximumBytes - count)
			{
				throw new InvalidOperationException("Text comparison supports sheets up to 4 MB each.");
			}

			bytes.Write(buffer, 0, count);
		}

		string hash = SongFileAnalyzer.Hash(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
		if (!StringComparer.OrdinalIgnoreCase.Equals(hash, contentHash))
		{
			throw new InvalidOperationException("A sheet changed outside ChordBook. Reconcile the book before comparing it.");
		}

		bytes.Position = 0;
		Encoding encoding = Encoding.GetEncoding(textEncoding ?? "utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
		using StreamReader reader = new(bytes, encoding, detectEncodingFromByteOrderMarks: true);
		return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Private Methods

	private static async Task<string> ReadTextAsync(IBookStore store, BookLocation location, SongFile file, CancellationToken cancellationToken)
	{
		await using Stream source = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken).ConfigureAwait(false);
		return await ReadTextAsync(source, file.TextEncoding, file.ContentHash, cancellationToken).ConfigureAwait(false);
	}

	private static RevisionStamp Next(RevisionStamp prior, Guid deviceId, DateTimeOffset now)
		=> new() { Revision = checked(prior.Revision + 1), DeviceId = deviceId, ModifiedUtc = now };

	private static async Task VerifySideAsync(IBookStore store, BookLocation location, SongVersionSide side, CancellationToken cancellationToken)
	{
		await using Stream stream = await store.OpenManagedAssetAsync(location, side.Id, cancellationToken).ConfigureAwait(false);
		string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
		if (!StringComparer.OrdinalIgnoreCase.Equals(side.ContentHash, actual))
		{
			throw new InvalidOperationException("A compared sheet changed. Close and reopen the comparison before keeping versions.");
		}
	}

	#endregion
}
