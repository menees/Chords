#region Using Directives

using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>Validates local resume hints against current metadata without opening song assets.</summary>
public static class PerformanceSessionRecovery
{
	#region Public Constants

	public const int MaximumSongs = 10000;
	public const int MaximumJsonCharacters = 500_000;

	#endregion

	#region Public Methods

	public static PerformanceResume? Resolve(ChordDatabase database, PerformanceSessionContext? context, PerformanceSessionCursor? cursor)
	{
		ArgumentNullException.ThrowIfNull(database);
		PerformanceResume? result = null;
		if (context is not null && cursor is not null && context.BookId == database.Id && context.Id != Guid.Empty
			&& context.Id == cursor.ContextId && context.SongIds is not null && context.SongIds.Count <= MaximumSongs)
		{
			HashSet<Guid> available = [.. database.SongFiles.Where(file => !file.IsArchived && file.RecoveryVersion is null).Select(file => file.SongId)];
			available.IntersectWith(database.Songs.Where(song => !song.IsArchived).Select(song => song.Id));
			List<Guid> songs = [];
			List<Guid> entries = [];
			string? name = context.Name;
			if (context.SetlistId is Guid setlistId)
			{
				Setlist? setlist = database.Setlists.Find(item => item.Id == setlistId && !item.IsArchived);
				if (setlist is not null && setlist.Entries.Count <= MaximumSongs)
				{
					name = setlist.Name;
					foreach (SetlistEntry entry in setlist.Entries)
					{
						if (available.Contains(entry.SongId))
						{
							songs.Add(entry.SongId);
							entries.Add(entry.Id);
						}
					}
				}
			}
			else
			{
				HashSet<Guid> seen = [];
				foreach (Guid id in context.SongIds)
				{
					if (available.Contains(id) && seen.Add(id))
					{
						songs.Add(id);
					}
				}
			}

			int index = context.SetlistId.HasValue && cursor.EntryId is Guid entryId ? entries.IndexOf(entryId)
				: !context.SetlistId.HasValue && cursor.EntryId is null ? songs.IndexOf(cursor.SongId) : -1;
			if (index >= 0 && songs[index] == cursor.SongId)
			{
				result = new(context.SetlistId, songs, entries, name, index);
			}
		}

		return result;
	}

	#endregion
}
