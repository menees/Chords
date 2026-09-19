using System.Globalization;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Reports questionable source metadata without normalizing or rejecting authored text.</summary>
public static class SongEditorWarnings
{
	private static readonly string[] ScalarNames = ["title", "t", "key", "tempo", "capo", "time", "duration"];

	public static IReadOnlyList<string> Get(SongFileAnalysis analysis)
	{
		List<string> result = [];
		foreach ((string name, IReadOnlyList<SourceMetadataValue> values) in analysis.Metadata)
		{
			if (ScalarNames.Contains(name, StringComparer.Ordinal) && values.Select(value => value.Value).Distinct(StringComparer.Ordinal).Skip(1).Any())
			{
				result.Add($"Multiple {name} values are present. All source values will be retained.");
			}

			foreach (SourceMetadataValue value in values)
			{
				if (name == "duration" && !SongMetadata.TryParseDuration(value.Value, out _))
				{
					result.Add($"Duration '{value.Value}' is not whole seconds or minutes:seconds.");
				}
				else if (name is "tempo" or "capo"
					&& (!int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < (name == "tempo" ? 1 : 0)))
				{
					result.Add($"{name}: '{value.Value}' is not a valid whole-number value.");
				}
			}
		}

		return result;
	}
}
