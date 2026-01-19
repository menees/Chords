namespace Menees.Chords.Cli;

/// <summary>
/// This is parsed similar to a <c>bool?</c>. However, it's an enum,
/// so the generated help will show the allowed values.
/// </summary>
/// <remarks>
/// System.CommandLine supports <c>bool?</c>, but it doesn't support a "null" literal.
/// It also doesn't support other common true/false values like 1/0 or yes/no. This
/// type handles both thanks to <see cref="NullableBoolExtensions"/>.
/// </remarks>
/// <seealso href="https://github.com/commandlineparser/commandline/issues/702"/>
internal enum NullableBool
{
	Null,
	False,
	True,
}
