namespace Menees.Chords.Cli;

using System.CommandLine;
using System.Threading.Tasks;

internal static class Program
{
	private static async Task Main(string[] args)
	{
		RootCommand root = new("Commands for working with chord sheet files.");

		// LONG-TERM-TODO: Other possible commands:
		// transpose - +N use sharps. -N use flats. Like https://www.chordpro.org/chordpro/using-chordpro/#transpose
		// normalize - Call Chord.Normalize() for every parsed Chord in a document.
		// compact - Replace identical start/end_of_chorus sections with {chorus} directive. One blank line per section.
		// format - sub-command instead of as an option on the convert command. See Formats enum.
		Command convert = ConvertCommand.Create();
		root.Add(convert);

		await root.InvokeAsync(args);
	}
}
