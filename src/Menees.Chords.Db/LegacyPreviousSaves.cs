using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Retires automatic editor copies from older books without touching manual sheets or sync recovery.</summary>
public static class LegacyPreviousSaves
{
	/// <summary>Identifies a sheet still marked as an automatic editor copy.</summary>
	public static bool IsAutomaticCopy(SongFile file) => file.IsArchived && file.SavedVersion is not null && file.RecoveryVersion is null;

	/// <summary>Records removals and clears preferences for the retired sheets in a detached catalog.</summary>
	public static void Retire(ChordDatabase database, IReadOnlyDictionary<Guid, SongFile> copies, Guid deviceId, DateTimeOffset now)
	{
		if (copies.Count > 0)
		{
			database.SongFiles.RemoveAll(file => copies.ContainsKey(file.Id));
			HashSet<Guid> deleted = [.. database.Tombstones.Where(item => item.EntityType == nameof(SongFile)).Select(item => item.EntityId)];
			foreach (SongFile file in copies.Values)
			{
				if (deleted.Add(file.Id))
				{
					database.Tombstones.Add(new()
					{
						EntityId = file.Id, EntityType = nameof(SongFile), Revision = Next(file.Revision, deviceId, now),
					});
				}
			}

			foreach (SongInstrumentSetting setting in database.SongInstrumentSettings)
			{
				if (setting.PreferredSongFileId is Guid fileId && copies.ContainsKey(fileId))
				{
					setting.PreferredSongFileId = null;
					setting.Revision = Next(setting.Revision, deviceId, now);
				}
			}

			foreach (Setlist setlist in database.Setlists)
			{
				bool changed = false;
				foreach (SetlistEntry entry in setlist.Entries)
				{
					if (entry.PreferredSongFileId is Guid fileId && copies.ContainsKey(fileId))
					{
						entry.PreferredSongFileId = null;
						changed = true;
					}
				}

				if (changed)
				{
					setlist.Revision = Next(setlist.Revision, deviceId, now);
				}
			}
		}
	}

	internal static async Task<string> CommitAsync(
		IBookStore store,
		BookLocation location,
		string expectedJson,
		ChordDatabase database,
		IReadOnlyDictionary<Guid, SongFile> copies,
		Guid deviceId,
		CancellationToken cancellationToken)
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		Retire(database, copies, deviceId, now);
		database.Revision = Next(database.Revision, deviceId, now);
		await using IStagedBookWrite write = await store.StageWriteAsync(location, expectedJson, cancellationToken).ConfigureAwait(false);
		foreach (Guid id in copies.Keys)
		{
			await write.DeleteManagedAssetAsync(id, cancellationToken).ConfigureAwait(false);
		}

		string updatedJson = DatabaseJson.Serialize(database);
		await write.WriteDatabaseJsonAsync(updatedJson, cancellationToken).ConfigureAwait(false);
		await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		return updatedJson;
	}

	private static RevisionStamp Next(RevisionStamp stamp, Guid deviceId, DateTimeOffset now)
		=> new() { Revision = checked(stamp.Revision + 1), DeviceId = deviceId, ModifiedUtc = now };
}
