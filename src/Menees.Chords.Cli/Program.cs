namespace Menees.Chords.Cli;

using Menees.Shell;

internal static class Program
{
	private static void Main(string[] args)
	{
		try
		{
			CommandLine commandLine = new();
			ConvertCommand? command = ConvertCommand.TryCreate(commandLine, args);
			if (command == null)
			{
				commandLine.WriteMessage();
			}
			else
			{
				command.Execute();
			}
		}
#pragma warning disable CA1031 // Do not catch general exception types. Main must catch all exceptions.
		catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
		{
			Console.WriteLine(ex.ToString());
			Environment.ExitCode = 100;
		}
	}
}
