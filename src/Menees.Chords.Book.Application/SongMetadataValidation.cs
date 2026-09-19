using System.Globalization;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>Validation for explicitly edited catalog fields; imported source observations are retained verbatim.</summary>
public static class SongMetadataValidation
{
	public static string? GetError(string name, string value)
	{
		const int MaximumTempo = 300;
		const int YearDigits = 4;
		const int MinimumYear = 1000;
		const int MaximumCapo = 24;
		bool integer = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number);
		return name switch
		{
			"key" when !Key.TryParse(value, out _) => "Key must be a major or minor key, e.g. C, F# or Bbm.",
			"tempo" when !integer || number is < 1 or > MaximumTempo => "Tempo must be a whole number from 1 to 300 BPM.",
			"capo" when !integer || number is < 1 or > MaximumCapo => "Capo must be a whole number from 1 to 24; leave blank for none.",
			"duration" when !value.Contains(':') || !SongMetadata.TryParseDuration(value, out _) => "Duration must be minutes:seconds, e.g. 4:08.",
			"year" when !integer || value.Length != YearDigits || number < MinimumYear || number > DateTime.UtcNow.ToLocalTime().Year
				=> "Year must be between 1000 and the current year.",
			_ => null,
		};
	}

	public static void Validate(
		IReadOnlyDictionary<string, IReadOnlyList<string>> values, IReadOnlyDictionary<string, IReadOnlyList<string>>? original = null)
	{
		foreach ((string name, IReadOnlyList<string> entries) in values)
		{
			if (original is null || !original.TryGetValue(name, out IReadOnlyList<string>? prior) || !entries.SequenceEqual(prior))
			{
				foreach (string value in entries)
				{
					if (GetError(name, value) is string error)
					{
						throw new ArgumentException(error);
					}
				}
			}
		}
	}
}
