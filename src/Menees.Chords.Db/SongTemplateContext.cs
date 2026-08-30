namespace Menees.Chords.Db;

/// <summary>Supplies normalized song values to a compiled display template.</summary>
public sealed class SongTemplateContext
{
	private readonly Dictionary<string, IReadOnlyList<string>> values = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance of the <see cref="SongTemplateContext"/> class.</summary>
	/// <param name="song">The song whose values should be exposed.</param>
	/// <param name="instrumentSetting">The optional active instrument setting.</param>
	public SongTemplateContext(Song song, SongInstrumentSetting? instrumentSetting = null)
	{
		ArgumentNullException.ThrowIfNull(song);
		this.Add("title", [song.Title]);
		this.Add("artist", song.Artists);
		this.Add("artists", song.Artists);
		this.Add("tag", song.Tags);
		this.Add("tags", song.Tags);
		if (song.DurationSeconds is int durationSeconds)
		{
			this.Add("duration", [TimeSpan.FromSeconds(durationSeconds).ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture)]);
		}

		foreach ((string name, List<SourceMetadataValue> metadata) in song.SourceMetadata)
		{
			IEnumerable<string> metadataValues = metadata.Select(value => value.Value);
			this.Add(name, metadataValues);
			if (!name.EndsWith('s'))
			{
				this.Add(name + "s", metadataValues);
			}
		}

		if (instrumentSetting?.CapoFret is int capoFret)
		{
			this.Add("capo", [capoFret.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
		}
	}

	/// <summary>Gets the normalized values for a field.</summary>
	/// <param name="fieldName">The case-insensitive field name.</param>
	/// <returns>The field values, or an empty list when the field is unknown.</returns>
	public IReadOnlyList<string> GetValues(string fieldName)
		=> this.values.TryGetValue(fieldName, out IReadOnlyList<string>? result) ? result : [];

	private void Add(string name, IEnumerable<string> items)
	{
		string[] nonempty = [.. items.Where(item => !string.IsNullOrWhiteSpace(item))];
		if (nonempty.Length > 0)
		{
			this.values[name] = nonempty;
		}
	}
}
