namespace Menees.Chords.Cli;

#region Using Directives

using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

#endregion

internal abstract class BaseCommand
{
	#region Constructors

	protected BaseCommand(ParseResult parseResult)
	{
		this.ParseResult = parseResult;
	}

	#endregion

	#region Public Properties

	public int? ExitCode { get; set; }

	#endregion

	#region Protected Properties

	protected ParseResult ParseResult { get; }

	#endregion

	#region Public Methods

	public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
	{
		int? exitCode = await this.OnExecuteAsync(cancellationToken).ConfigureAwait(false);
		exitCode ??= this.ExitCode ?? 0;
		return exitCode.Value;
	}

	#endregion

	#region Protected Methods

	protected static Option<T> GetOption<T>(Command command, string name)
	=> command.Options.OfType<Option<T>>().First(option => option.Name == name);

	protected static T? GetOptionValue<T>(ParseResult parseResult, string name)
		=> GetOptionValue(parseResult, GetOption<T>(parseResult.CommandResult.Command, name));

	protected static T? GetOptionValue<T>(ParseResult parseResult, Option<T> option)
		=> parseResult.GetValue(option);

	protected static Argument<T> GetArgument<T>(Command command, string name)
		=> command.Arguments.OfType<Argument<T>>().First(argument => argument.Name == name);

	protected static T? GetArgumentValue<T>(ParseResult parseResult, string name)
		=> GetArgumentValue(parseResult, GetArgument<T>(parseResult.CommandResult.Command, name));

	protected static T? GetArgumentValue<T>(ParseResult parseResult, Argument<T> argument)
		=> parseResult.GetValue(argument);

	protected abstract Task<int?> OnExecuteAsync(CancellationToken cancellationToken);

	protected void Write(string? value)
	{
		if (value != null)
		{
			this.ParseResult.InvocationConfiguration.Output.Write(value);
		}
	}

	protected void WriteLine(string? value = null)
		=> this.ParseResult.InvocationConfiguration.Output.WriteLine(value ?? string.Empty);

	protected void WriteErrorLine(string? value)
	{
		if (value != null)
		{
			this.ParseResult.InvocationConfiguration.Error.WriteLine(value);
		}
	}

	#endregion
}
