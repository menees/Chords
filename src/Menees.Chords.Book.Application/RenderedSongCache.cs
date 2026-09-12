namespace Menees.Chords.Book.Application;

internal sealed class RenderedSongCache
{
	private const int MaximumEntries = 8;
	private const int MaximumCharacters = 2_000_000;
	private readonly Dictionary<string, LinkedListNode<(string Key, string Html)>> entries = new(StringComparer.Ordinal);
	private readonly LinkedList<(string Key, string Html)> recency = new();
	private int characters;

	public string? Get(string key)
	{
		string? result = null;
		lock (this.entries)
		{
			if (this.entries.TryGetValue(key, out var node))
			{
				this.recency.Remove(node);
				this.recency.AddFirst(node);
				result = node.Value.Html;
			}
		}

		return result;
	}

	public void Put(string key, string html)
	{
		if (html.Length <= MaximumCharacters)
		{
			lock (this.entries)
			{
				if (!this.entries.ContainsKey(key))
				{
					while (this.entries.Count >= MaximumEntries || this.characters + html.Length > MaximumCharacters)
					{
						var last = this.recency.Last!;
						this.characters -= last.Value.Html.Length;
						this.entries.Remove(last.Value.Key);
						this.recency.RemoveLast();
					}

					this.entries.Add(key, this.recency.AddFirst((key, html)));
					this.characters += html.Length;
				}
			}
		}
	}
}
