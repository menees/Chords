namespace Menees.Chords;

#region Using Directives

using System.Text.RegularExpressions;
using Menees.Chords.Parsers;

#endregion

/// <summary>A source line preserved verbatim inside a delegated ChordPro environment.</summary>
internal sealed class ChordProDelegateLine : TextEntry
{
	#region Constructors

	/// <summary>Creates an opaque delegated-environment source line.</summary>
	/// <param name="text">The source text.</param>
	internal ChordProDelegateLine(string text)
		: base(text, allowWhitespace: true)
	{
	}

	#endregion

	#region Public Methods

	/// <summary>Returns an opaque line when a delegated environment is active.</summary>
	/// <param name="context">The current parsing context.</param>
	/// <returns>A delegated source line, except for the matching end directive.</returns>
	internal static ChordProDelegateLine? TryParse(LineContext context)
	{
		Conditions.RequireNonNull(context);
		ChordProDelegateLine? result = null;
		if (context.State.TryGetValue(ChordProDirectiveLine.DelegateStateKey, out object? state)
			&& state is string environmentName
			&& !IsEndDirective(context.LineText, environmentName))
		{
			result = new(context.LineText);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private static bool IsEndDirective(string line, string environmentName)
	{
		string pattern = @"^\s*\{\s*end_of_" + Regex.Escape(environmentName) + @"\s*\}\s*$";
		return Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	#endregion
}
