namespace Menees.Chords.Db;

internal sealed record ReconciledFileObservation(string Name, long Length, DateTime WriteUtc, string Hash);
