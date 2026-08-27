namespace Menees.Chords.Db;

/// <summary>Implements book-identity rules for clone and replacement restore operations.</summary>
public static class DatabaseRestore
{
	/// <summary>Clones user content under a new book identity and fresh revision history.</summary>
	public static ChordDatabase CloneAsNew(ChordDatabase source, Guid deviceId, DateTimeOffset? now = null)
	{
		DatabaseValidation.ThrowIfInvalid(source);
		ChordDatabase clone = DatabaseJson.Deserialize(DatabaseJson.Serialize(source));
		DateTimeOffset modifiedUtc = now ?? DateTimeOffset.UtcNow;
		clone.Id = Guid.CreateVersion7(modifiedUtc);
		clone.Tombstones.Clear();
		ResetRevisions(clone, deviceId, modifiedUtc);
		return clone;
	}

	/// <summary>Ensures a replacement backup has the same identity as the current book.</summary>
	public static void RequireReplacementIdentity(ChordDatabase current, ChordDatabase replacement)
	{
		DatabaseValidation.ThrowIfInvalid(current);
		DatabaseValidation.ThrowIfInvalid(replacement);
		if (current.Id != replacement.Id)
		{
			throw new DatabaseIdentityMismatchException(current.Id, replacement.Id);
		}
	}

	private static void ResetRevisions(ChordDatabase database, Guid deviceId, DateTimeOffset modifiedUtc)
	{
		database.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		database.BookSettings.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		foreach (Song song in database.Songs)
		{
			song.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		}

		foreach (SongFile file in database.SongFiles)
		{
			file.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
			file.ContentRevision = 1;
			file.RecoveryVersion = null;
		}

		foreach (InstrumentProfile profile in database.InstrumentProfiles)
		{
			profile.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		}

		foreach (SongInstrumentSetting setting in database.SongInstrumentSettings)
		{
			setting.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		}

		foreach (Setlist setlist in database.Setlists)
		{
			setlist.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		}

		foreach (CustomTab customTab in database.CustomTabs)
		{
			customTab.Revision = RevisionStamp.Initial(deviceId, modifiedUtc);
		}
	}
}
