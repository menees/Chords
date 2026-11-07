using System.IO;

namespace Menees.Chords.Db;

/// <summary>A selected source that could not be read or analyzed; it is never eligible for import.</summary>
/// <param name="SourcePath">The selected source path, retained to identify and repair the file.</param>
/// <param name="Message">The source-specific read or analysis error.</param>
public sealed record BookImportFailure(string SourcePath, string Message)
{
	/// <summary>Gets the original filename.</summary>
	public string Name => Path.GetFileName(this.SourcePath);
}
