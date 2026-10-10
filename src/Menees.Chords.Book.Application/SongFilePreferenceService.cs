using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Redirects preferences after one related sheet replaces another.</summary>
internal static class SongFilePreferenceService
{
	public static void Replace(ChordDatabase database, Guid deletedId, Guid keptId, Guid deviceId, DateTimeOffset now)
	{
		foreach (SongInstrumentSetting setting in database.SongInstrumentSettings.Where(setting => setting.PreferredSongFileId == deletedId))
		{
			setting.PreferredSongFileId = keptId;
			setting.Revision = new() { Revision = checked(setting.Revision.Revision + 1), DeviceId = deviceId, ModifiedUtc = now };
		}

		foreach (Setlist setlist in database.Setlists)
		{
			bool changed = false;
			foreach (SetlistEntry entry in setlist.Entries.Where(entry => entry.PreferredSongFileId == deletedId))
			{
				entry.PreferredSongFileId = keptId;
				changed = true;
			}

			if (changed)
			{
				setlist.Revision = new() { Revision = checked(setlist.Revision.Revision + 1), DeviceId = deviceId, ModifiedUtc = now };
			}
		}
	}
}
