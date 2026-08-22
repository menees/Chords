namespace Menees.Chords.Cli;

internal sealed class TransposeOptionValue
{
	public TransposeOptionValue(sbyte halfSteps, AccidentalPreference accidentalPreference)
	{
		this.HalfSteps = halfSteps;
		this.AccidentalPreference = accidentalPreference;
	}

	public AccidentalPreference AccidentalPreference { get; }

	public sbyte HalfSteps { get; }
}
