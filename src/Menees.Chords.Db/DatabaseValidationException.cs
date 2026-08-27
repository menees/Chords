namespace Menees.Chords.Db;

/// <summary>Indicates that a chord database contains one or more validation failures.</summary>
public sealed class DatabaseValidationException : DatabaseFormatException
{
	/// <summary>Initializes a new instance of the <see cref="DatabaseValidationException"/> class.</summary>
	public DatabaseValidationException(IReadOnlyList<ValidationProblem> problems)
		: base("The chord database failed validation: " + string.Join("; ", problems.Select(problem => $"{problem.Path}: {problem.Message}")))
	{
		this.Problems = problems;
	}

	/// <summary>Gets the validation failures.</summary>
	public IReadOnlyList<ValidationProblem> Problems { get; }
}
