namespace Menees.Chords.Cli.RootActions;

#region Using Directives

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

#endregion

internal abstract class RootOptionAction : AsynchronousCommandLineAction
{
	#region Private Data Members

	// From https://www.asciiart.eu/text-to-ascii-art using Standard font.
	private const string Banner = """
		 __  __                             ____ _                   _       ____ _ _
		|  \/  | ___ _ __   ___  ___  ___  / ___| |__   ___  _ __ __| |___  / ___| (_)
		| |\/| |/ _ \ '_ \ / _ \/ _ \/ __|| |   | '_ \ / _ \| '__/ _` / __|| |   | | |
		| |  | |  __/ | | |  __/  __/\__ \| |___| | | | (_) | | | (_| \__ \| |___| | |
		|_|  |_|\___|_| |_|\___|\___||___(_)____|_| |_|\___/|_|  \__,_|___(_)____|_|_|
		""";

	#endregion

	#region Constructors

	protected RootOptionAction()
	{
	}

	#endregion

	#region Public Methods

	public sealed override Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
	{
		RootOptionCommand command = this.CreateCommand(parseResult);
		return command.ExecuteAsync(cancellationToken);
	}

	#endregion

	#region Protected Methods

	protected abstract RootOptionCommand CreateCommand(ParseResult parseResult);

	#endregion

	#region Private Types

	protected class RootOptionCommand : BaseCommand
	{
		#region Constructors

		public RootOptionCommand(ParseResult parseResult)
			: base(parseResult)
		{
		}

		#endregion

		protected override Task<int?> OnExecuteAsync(CancellationToken cancellationToken)
		{
			this.WriteLine(Banner);
			this.WriteLine();
			return Task.FromResult<int?>(null);
		}
	}

	#endregion
}
