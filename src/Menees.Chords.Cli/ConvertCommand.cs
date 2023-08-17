namespace Menees.Chords.Cli;

#region Using Directives

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;

#endregion

internal sealed class ConvertCommand : BaseCommand
{
	#region Private Data Members

	private readonly Parsers parsers;
	private readonly Transformers transformers;
	private readonly Encoding encoding;
	private readonly FileInfo input;
	private readonly FileInfo? output;
	private readonly bool force;

	#endregion

	#region Constructors

	private ConvertCommand(
		InvocationContext context,
		FileInfo input,
		Parsers parsers,
		Transformers transformers,
		Encoding encoding,
		FileInfo? output,
		bool force)
		: base(context)
	{
		this.input = input;
		this.parsers = parsers;
		this.transformers = transformers;
		this.encoding = encoding;
		this.output = output;
		this.force = force;
	}

	#endregion

	#region Public Methods

	public static Command Create()
	{
		Command result = new("convert", "Converts a chord sheet file from one format to another.");

		Argument<FileInfo?> inputArgument = new(nameof(input), description: "The file to convert.");
		result.Add(inputArgument);

		Option<FileInfo> outputOption = new(new[] { "--output", "-o" }, "The output file name. Omit to write to stdout.");
		result.Add(outputOption);

		Option<bool> forceOption = new(new[] { "--force", "-f" }, "Whether to overwrite the output file if it already exists.");
		result.Add(forceOption);

		Option<Parsers> parseOption = new(new[] { "--parse", "-p" }, "How the input file should be parsed.");
		result.Add(parseOption);

		Option<Transformers> transformOption = new(new[] { "--transform", "-t" }, "How the input should be transformed.");
		result.Add(transformOption);

		Option<string?> encodingOption = new(new[] { "--encoding", "-e" }, "How the input file is encoded.");
		result.Add(encodingOption);

		result.SetHandler(context =>
		{
			FileInfo input = GetArgumentValue(context, inputArgument) ?? throw new ArgumentException("An input file is required.");
			Parsers parsers = GetOptionValue(context, parseOption);
			Transformers transformers = GetOptionValue(context, transformOption);
			Encoding encoding = Encoding.GetEncoding(GetOptionValue(context, encodingOption) ?? "UTF-8");
			FileInfo? output = GetOptionValue(context, outputOption);
			bool force = GetOptionValue(context, forceOption);

			ConvertCommand command = new(context, input, parsers, transformers, encoding, output, force);
			return command.ExecuteAsync();
		});

		return result;
	}

	#endregion

	#region Protected Methods

	protected override Task OnExecuteAsync()
	{
		if (this.output is not null && this.output.Exists && !this.force)
		{
			this.WriteErrorLine("The specified output file already exists and the --force option was not specified.");
			this.ExitCode = 1;
		}
		else
		{
			using TextReader reader = new StreamReader(this.input.FullName, this.encoding, true);
			DocumentParser parser = new(
				this.parsers == Parsers.ChordPro
					? DocumentParser.ChordProLineParsers
					: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Load(reader, parser);

			ChordProTransformer transformer = this.transformers == Transformers.MobileSheets
				? new MobileSheetsTransformer(inputDocument)
				: new ChordProTransformer(inputDocument);
			Document outputDocument = transformer.ToChordPro().Document;

			TextFormatter formatter = new(outputDocument);
			string outputText = formatter.ToString();
			if (this.output is null)
			{
				this.WriteLine(outputText);
			}
			else
			{
				File.WriteAllText(this.output.FullName, outputText, this.encoding);
			}
		}

		return Task.CompletedTask;
	}

	#endregion
}
