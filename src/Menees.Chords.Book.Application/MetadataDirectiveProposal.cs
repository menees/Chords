namespace Menees.Chords.Book.Application;

/// <summary>One mapped catalog field and all existing occurrences that require an explicit choice when ambiguous.</summary>
public sealed record MetadataDirectiveProposal(
	string Name, IReadOnlyList<string> Values, IReadOnlyList<MetadataDirectiveOccurrence> Occurrences);
