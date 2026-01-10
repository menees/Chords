namespace Menees.Chords.Cli.RootActions;

#region Using Directives

using System;
using System.CommandLine;
using System.CommandLine.Help;
using System.Threading;
using System.Threading.Tasks;

#endregion

internal sealed class HelpOptionAction : RootOptionAction
{
	#region Private Data Members

	private readonly HelpAction helpAction;

	#endregion

	#region Constructors

	public HelpOptionAction(HelpAction helpAction)
	{
		this.helpAction = helpAction;
	}

	#endregion

	#region Protected Methods

	protected override RootOptionCommand CreateCommand(ParseResult parseResult)
		=> new HelpCommand(parseResult, this.helpAction);

	#endregion

	#region Private Types

	private sealed class HelpCommand : RootOptionCommand
	{
		#region Private Data Members

		private readonly HelpAction helpAction;

		#endregion

		#region Constructors

		public HelpCommand(ParseResult parseResult, HelpAction helpAction)
			: base(parseResult)
		{
			this.helpAction = helpAction;
		}

		#endregion

		#region Protected Methods

		protected override async Task<int?> OnExecuteAsync(CancellationToken cancellationToken)
		{
			int? baseResult = await base.OnExecuteAsync(cancellationToken).ConfigureAwait(false);
			int helpResult = this.helpAction.Invoke(this.ParseResult);
			int result = baseResult is null ? helpResult : Math.Max(baseResult.Value, helpResult);
			return result;
		}

		#endregion
	}

	#endregion
}
