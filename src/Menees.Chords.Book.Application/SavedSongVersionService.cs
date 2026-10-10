#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Stages bounded previous-save history in the same transaction as the new text.</summary>
internal static class SavedSongVersionService
{
	#region Public Methods

	public static async Task StageAsync(
		IStagedBookWrite write,
		ChordDatabase database,
		SongFile original,
		Stream originalBytes,
		Guid deviceId,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		// Only archived automatic copies are replaced. An explicitly restored copy is an ordinary retained alternative.
		SongFile[] priorCopies = [.. database.SongFiles.Where(file =>
			file.IsArchived && file.SavedVersion?.SourceFileId == original.Id)];
		foreach (SongFile prior in priorCopies)
		{
			await write.DeleteManagedAssetAsync(prior.Id, cancellationToken).ConfigureAwait(false);
			database.SongFiles.Remove(prior);
			database.Tombstones.Add(new()
			{
				EntityId = prior.Id,
				EntityType = nameof(SongFile),
				Revision = new() { Revision = checked(prior.Revision.Revision + 1), DeviceId = deviceId, ModifiedUtc = now },
			});
			SongFilePreferenceService.Replace(database, prior.Id, original.Id, deviceId, now);
		}

		SongFile saved = JsonSerializer.Deserialize<SongFile>(JsonSerializer.SerializeToUtf8Bytes(original))!;
		saved.Id = Guid.NewGuid();
		saved.RelativePath = PortableManagedFileName.Create(
			"Previous " + database.Songs.Single(song => song.Id == original.SongId).Title, saved.Id, Path.GetExtension(original.RelativePath));
		saved.IsArchived = true;
		saved.DisplayPriority = int.MinValue;
		saved.RecoveryVersion = null;
		saved.SavedVersion = new() { SourceFileId = original.Id, SavedUtc = now, OriginalModifiedUtc = original.Revision.ModifiedUtc };
		saved.Revision = RevisionStamp.Initial(deviceId, now);
		database.SongFiles.Add(saved);
		original.SavedVersion = null;
		originalBytes.Position = 0;
		await write.WriteManagedAssetAsync(saved.Id, saved.RelativePath, originalBytes, cancellationToken).ConfigureAwait(false);
	}

	#endregion
}
