namespace Menees.Chords.Book.Application;

/// <summary>A bounded device-local recent list. Recording an access performs no storage work.</summary>
public sealed class RecentSongHistory
{
	private const int MaximumSongs = 100;
	private readonly List<Guid> songs;

	public RecentSongHistory(IEnumerable<Guid>? saved = null)
	{
		this.songs = [.. (saved ?? []).Where(id => id != Guid.Empty).Distinct().Take(MaximumSongs)];
		this.SongIds = this.songs.AsReadOnly();
	}

	public IReadOnlyList<Guid> SongIds { get; }

	public bool IsDirty { get; private set; }

	public void Record(Guid songId)
	{
		if (songId != Guid.Empty && (this.songs.Count == 0 || this.songs[0] != songId))
		{
			this.songs.Remove(songId);
			this.songs.Insert(0, songId);
			if (this.songs.Count > MaximumSongs)
			{
				this.songs.RemoveAt(MaximumSongs);
			}

			this.IsDirty = true;
		}
	}

	/// <summary>Records a completed batch in source order, with the last song most recent and no storage work.</summary>
	public void Record(IEnumerable<Guid> songIds)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		foreach (Guid songId in songIds)
		{
			this.Record(songId);
		}
	}

	public void MarkSaved() => this.IsDirty = false;
}
