namespace Menees.Chords.Db;

/// <summary>Compares setlist content, including complete ordered entries, independently of revision stamps.</summary>
public static class SetlistContent
{
	/// <summary>Compares all setlist content, ignoring root revision stamps.</summary>
	public static bool AreEqual(Setlist left, Setlist right)
	{
		ArgumentNullException.ThrowIfNull(left);
		ArgumentNullException.ThrowIfNull(right);
		bool equal = left.Id == right.Id && left.Name == right.Name && left.Notes == right.Notes
			&& left.Date == right.Date && left.IsArchived == right.IsArchived && left.Entries.Count == right.Entries.Count;
		for (int index = 0; equal && index < left.Entries.Count; index++)
		{
			equal = EntriesEqual(left.Entries[index], right.Entries[index]);
		}

		return equal;
	}

	/// <summary>Compares entry identity, song and every performance preference.</summary>
	public static bool EntriesEqual(SetlistEntry left, SetlistEntry right)
		=> left.Id == right.Id && left.SongId == right.SongId && left.PreferredSongFileId == right.PreferredSongFileId
			&& left.InstrumentProfileId == right.InstrumentProfileId && left.TransposeSemitones == right.TransposeSemitones;
}
