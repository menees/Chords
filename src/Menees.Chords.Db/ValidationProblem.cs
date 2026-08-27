namespace Menees.Chords.Db;

/// <summary>Describes one database validation failure.</summary>
/// <param name="Path">The schema path containing the problem.</param>
/// <param name="Message">The human-readable explanation.</param>
public sealed record ValidationProblem(string Path, string Message);
