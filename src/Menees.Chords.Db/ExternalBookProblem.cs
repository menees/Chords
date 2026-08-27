namespace Menees.Chords.Db;

/// <summary>Describes a recoverable externally observed book problem.</summary>
/// <param name="RelativePath">The affected relative path.</param>
/// <param name="Message">The human-readable problem.</param>
public sealed record ExternalBookProblem(string RelativePath, string Message);
