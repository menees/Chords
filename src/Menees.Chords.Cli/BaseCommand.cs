namespace Menees.Chords.Cli;

#region Using Directives

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Threading.Tasks;

#endregion

internal abstract class BaseCommand
{
	#region Constructors

	protected BaseCommand(InvocationContext context)
	{
		this.Context = context;
	}

	#endregion

	#region Public Properties

	public int ExitCode
	{
		get => this.Context.ExitCode;
		set
		{
			Environment.ExitCode = value;
			this.Context.ExitCode = value;
		}
	}

	#endregion

	#region Protected Properties

	protected InvocationContext Context { get; }

	#endregion

	#region Public Methods

	public async Task<int> ExecuteAsync()
	{
		await this.OnExecuteAsync().ConfigureAwait(false);
		int result = this.ExitCode;
		return result;
	}

	#endregion

	#region Protected Methods

	protected static Option<T> GetOption<T>(Command command, string name)
	=> command.Options.OfType<Option<T>>().First(option => option.Name == name);

	protected static T? GetOptionValue<T>(InvocationContext context, string name)
		=> GetOptionValue(context, GetOption<T>(context.ParseResult.CommandResult.Command, name));

	protected static T? GetOptionValue<T>(InvocationContext context, Option<T> option)
		=> context.ParseResult.GetValueForOption(option);

	protected static Argument<T> GetArgument<T>(Command command, string name)
		=> command.Arguments.OfType<Argument<T>>().First(argument => argument.Name == name);

	protected static T? GetArgumentValue<T>(InvocationContext context, string name)
		=> GetArgumentValue(context, GetArgument<T>(context.ParseResult.CommandResult.Command, name));

	protected static T? GetArgumentValue<T>(InvocationContext context, Argument<T> argument)
		=> context.ParseResult.GetValueForArgument(argument);

	protected abstract Task OnExecuteAsync();

	protected void Write(string? value)
	{
		if (value != null)
		{
			this.Context.Console.Out.Write(value);
		}
	}

	protected void WriteLine(string? value = null)
		=> this.Context.Console.Out.WriteLine(value ?? string.Empty);

	protected void WriteErrorLine(string? value)
	{
		if (value != null)
		{
			this.Context.Console.Error.WriteLine(value);
		}
	}

	#endregion
}
