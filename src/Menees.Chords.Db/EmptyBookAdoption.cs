namespace Menees.Chords.Db;

/// <summary>Limits replacing a placeholder book's identity to books with no content or deletion history.</summary>
public static class EmptyBookAdoption
{
	/// <summary>Returns whether a book has no songs, sheets, setlists, assignments, custom tabs or deletion history.</summary>
	public static bool IsEligible(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		return database.Songs.Count == 0 && database.SongFiles.Count == 0 && database.Setlists.Count == 0
			&& database.SongInstrumentSettings.Count == 0 && database.Tombstones.Count == 0 && database.CustomTabs.Count == 0;
	}

	internal static void Validate(BookLocation location, string expectedJson, Guid replicaBookId)
	{
		ChordDatabase original = DatabaseJson.Deserialize(expectedJson);
		if (original.Id != location.Token || !IsEligible(original) || replicaBookId == Guid.Empty || replicaBookId == original.Id)
		{
			throw new BookStoreValidationException("Only an unchanged empty placeholder book can adopt a different cloud book.");
		}
	}
}