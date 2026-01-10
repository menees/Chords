namespace Menees.Chords.Cli;

using System.CommandLine;
using System.CommandLine.Help;
using System.Threading.Tasks;
using Menees.Chords.Cli.RootActions;

internal static class Program
{
#pragma warning disable CC0068 // Unused Method. False positive for async Main.
#pragma warning disable CC0061 // Asynchronous method can be terminated with the 'Async' keyword. Can't use 'Async' suffix on Main.
	private static async Task<int> Main(string[] args)
#pragma warning restore CC0061 // Asynchronous method can be terminated with the 'Async' keyword.
#pragma warning restore CC0068 // Unused Method
	{
		try
		{
			RootCommand root = new("Commands for working with chord sheet files.");

			// LONG-TERM-TODO: Other possible commands (or convert options):
			// transpose - +N use sharps. -N use flats. Requires song to have a key. Like https://www.chordpro.org/chordpro/using-chordpro/#transpose
			// normalize - Call Chord.Normalize() for every parsed Chord in a document.
			// compact - Replace identical start/end_of_chorus sections with {chorus} directive. Use short directives, e.g. {sov}.
			// format - sub-command instead of as an option on the convert command. See Formats enum.
			// scrape - Download and parse chord sheets from popular websites.
			Command convert = ConvertCommand.Create();
			root.Subcommands.Add(convert);

			// Customize the built-in --version option.
			if (root.Options.OfType<VersionOption>().FirstOrDefault() is { } versionOption)
			{
				versionOption.Action = new VersionOptionAction();
			}

			// Customize the built-in --help option.
			// https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-customize-help#add-sections-to-help-output
			if (root.Options.OfType<HelpOption>().FirstOrDefault() is { } helpOption)
			{
				helpOption.Action = new HelpOptionAction((HelpAction)helpOption.Action!);
			}

			ParseResult parseResult = root.Parse(args);
			Environment.ExitCode = await parseResult.InvokeAsync();
		}
#pragma warning disable CA1031 // Do not catch general exception types. Main must catch all exceptions.
		catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
		{
			Console.WriteLine(ex.ToString());
			Environment.ExitCode = 100;
		}

		return Environment.ExitCode;
	}
}
