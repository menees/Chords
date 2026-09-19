namespace Menees.Chords.Book.Application;

internal sealed class RenderedSongCache
{
	private const int MaximumEntries = 8;
	private const int MaximumCharacters = 2_000_000;
	private readonly Dictionary<string, LinkedListNode<(string Key, RenderedSong Song)>> entries = new(StringComparer.Ordinal);
	private readonly LinkedList<(string Key, RenderedSong Song)> recency = new();
	private int characters;

	public RenderedSong? Get(string key)
	{
		RenderedSong? result = null;
		lock (this.entries)
		{
			if (this.entries.TryGetValue(key, out var node))
			{
				this.recency.Remove(node);
				this.recency.AddFirst(node);
				result = node.Value.Song;
			}
		}

		return result;
	}

	public void Put(string key, RenderedSong song)
	{
		if (song.Html.Length <= MaximumCharacters)
		{
			lock (this.entries)
			{
				if (!this.entries.ContainsKey(key))
				{
					while (this.entries.Count >= MaximumEntries || this.characters + song.Html.Length > MaximumCharacters)
					{
						var last = this.recency.Last!;
						this.characters -= last.Value.Song.Html.Length;
						this.entries.Remove(last.Value.Key);
						this.recency.RemoveLast();
					}

					this.entries.Add(key, this.recency.AddFirst((key, song)));
					this.characters += song.Html.Length;
				}
			}
		}
	}
}
