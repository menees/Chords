namespace Menees.Chords.Cli;

using System.CommandLine;
using System.Threading.Tasks;

internal static class Program
{
	private static async Task Main(string[] args)
	{
		RootCommand root = new("Commands for working with chord sheet files.");

		Command convert = ConvertCommand.Create();
		root.Add(convert);

		await root.InvokeAsync(args);
	}
}
