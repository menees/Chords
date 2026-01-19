namespace Menees.Chords.Cli;

#region Using Directives

using System;
using System.CommandLine.Parsing;

#endregion

internal static class NullableBoolExtensions
{
	#region Private Data Members

	private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

	private static readonly HashSet<string> TrueValues = new(
		["True", "Yes", "T", "Y", "1", "On"],
		Comparer);

	private static readonly HashSet<string> FalseValues = new(
		["False", "No", "F", "N", "0", "Off"],
		Comparer);

	private static readonly HashSet<string> NullValues = new(
		["Null", "-"],
		Comparer);

	#endregion

	#region Public Methods

	public static bool? ToStandardType(this NullableBool value)
		=> value switch
		{
			NullableBool.False => false,
			NullableBool.True => true,
			_ => null,
		};

	public static NullableBool ToNullableBool(this ArgumentResult argumentResult)
	{
		NullableBool result = NullableBool.Null;

		if (argumentResult.Tokens.Count != 1)
		{
			argumentResult.AddError($"{argumentResult.Argument.Name} requires one argument.");
		}
		else
		{
			string token = argumentResult.Tokens[0].Value;
			if (TrueValues.Contains(token))
			{
				result = NullableBool.True;
			}
			else if (FalseValues.Contains(token))
			{
				result = NullableBool.False;
			}
			else if (!NullValues.Contains(token))
			{
				argumentResult.AddError($"{token} is not a supported value for option {argumentResult.Argument.Name}.");
			}
		}

		return result;
	}

	#endregion
}
