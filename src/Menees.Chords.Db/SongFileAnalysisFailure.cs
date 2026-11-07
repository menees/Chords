using System.Text.Json.Serialization;

namespace Menees.Chords.Db;

/// <summary>A completed failed analyzer check, reusable only for the same analyzer version and catalog content hash.</summary>
public sealed record SongFileAnalysisFailure(int AnalyzerVersion, string ContentHash, string Message)
{
	/// <summary>Gets the device scope for a read/access failure; source-content failures apply to every device.</summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Guid? DeviceId { get; init; }
}
