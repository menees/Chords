namespace Menees.Chords.Cli;

#region Using Directives

using System.CommandLine.Parsing;

#endregion

internal static class CommandLineParsers
{
	#region Internal Methods

	internal static TEnum ParseEnum<TEnum>(ArgumentResult argumentResult)
		where TEnum : struct, Enum
	{
		TEnum result = default;
		if (argumentResult.Tokens.Count != 1)
		{
			argumentResult.AddError($"{argumentResult.Argument.Name} requires one argument.");
		}
		else if (!Enum.TryParse(argumentResult.Tokens[0].Value, ignoreCase: true, out result)
			|| !Enum.IsDefined(result))
		{
			argumentResult.AddError($"{argumentResult.Tokens[0].Value} is not a supported value for option {argumentResult.Argument.Name}.");
		}

		return result;
	}

	internal static TEnum? ParseNullableEnum<TEnum>(ArgumentResult argumentResult)
		where TEnum : struct, Enum
		=> ParseEnum<TEnum>(argumentResult);

	internal static Key? ParseKey(ArgumentResult argumentResult)
	{
		Key? result = null;
		if (argumentResult.Tokens.Count != 1)
		{
			argumentResult.AddError($"{argumentResult.Argument.Name} requires one argument.");
		}
		else if (!Key.TryParse(argumentResult.Tokens[0].Value, out result))
		{
			argumentResult.AddError($"{argumentResult.Tokens[0].Value} is not a valid key.");
		}

		return result;
	}

	internal static TransposeOptionValue? ParseTranspose(ArgumentResult argumentResult)
	{
		TransposeOptionValue? result = null;
		if (argumentResult.Tokens.Count is < 1 or > 2)
		{
			argumentResult.AddError($"{argumentResult.Argument.Name} requires a signed byte and an optional accidental preference.");
		}
		else if (!sbyte.TryParse(argumentResult.Tokens[0].Value, out sbyte halfSteps))
		{
			argumentResult.AddError($"{argumentResult.Tokens[0].Value} is not a signed byte.");
		}
		else
		{
			AccidentalPreference preference = AccidentalPreference.Default;
			if (argumentResult.Tokens.Count == 1
				|| (Enum.TryParse(argumentResult.Tokens[1].Value, ignoreCase: true, out preference)
					&& Enum.IsDefined(preference)))
			{
				result = new(halfSteps, preference);
			}
			else
			{
				argumentResult.AddError($"{argumentResult.Tokens[1].Value} is not a supported accidental preference.");
			}
		}

		return result;
	}

	#endregion
}
