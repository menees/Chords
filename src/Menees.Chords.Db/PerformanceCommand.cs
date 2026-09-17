using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>Identifies supported performance actions independently of physical input.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PerformanceCommand>))]
public enum PerformanceCommand
{
	/// <summary>Executes the NextViewport action.</summary>
	NextViewport,

	/// <summary>Executes the PreviousViewport action.</summary>
	PreviousViewport,

	/// <summary>Executes the NextSong action.</summary>
	NextSong,

	/// <summary>Executes the PreviousSong action.</summary>
	PreviousSong,

	/// <summary>Executes the ToggleMetronome action.</summary>
	ToggleMetronome,
}
