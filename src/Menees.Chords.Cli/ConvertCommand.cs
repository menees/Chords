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

	private const string ReadStdIn = "-";

	#endregion

	#region Private Data Members

	private readonly Parsers parsers;
	private readonly Transformers transformers;
	private readonly Encoding encoding;
	private readonly FileInfo? input;
	private readonly FileInfo? output;
	private readonly bool overwrite;
	private readonly Formats format;

	#endregion

	#region Constructors

	private ConvertCommand(
		InvocationContext context,
		FileInfo? input,
		Parsers parsers,
		Transformers transformers,
		Encoding encoding,
		FileInfo? output,
		bool overwrite,
		Formats format)
		: base(context)
	{
		this.input = input;
		this.parsers = parsers;
		this.transformers = transformers;
		this.encoding = encoding;
		this.output = output;
		this.overwrite = overwrite;
		this.format = format;
	}

	#endregion

	#region Public Methods

	public static Command Create()
	{
		Command result = new("convert", "Converts a chord sheet file from one format to another.");

		Argument<FileInfo?> inputArgument = new(
			nameof(input),
			description: $"The file to convert. Use \"{ReadStdIn}\" to read from stdin.",
			parse: parseResult =>
			{
				// https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial#add-subcommands-and-custom-validation
				FileInfo? fileInfo = null;
				string? filePath = parseResult.Tokens.Single().Value;
				if (filePath != ReadStdIn)
				{
					if (File.Exists(filePath))
					{
						fileInfo = new FileInfo(filePath);
					}
					else
					{
						parseResult.ErrorMessage = "Input file does not exist.";
					}
				}

				return fileInfo;
			});
		result.Add(inputArgument);

		Option<FileInfo> outputOption = new(new[] { "--output", "-o" }, "The output file name. Omit to write to stdout.");
		result.Add(outputOption);

		Option<bool> overwriteOption = new(new[] { "--overwrite", "-y" }, "Whether to overwrite the output file if it already exists.");
		result.Add(overwriteOption);

		Option<Parsers> parseOption = new(new[] { "--parse", "-p" }, () => Parsers.Default, "How the input file should be parsed.");
		result.Add(parseOption);

		Option<Transformers> transformOption = new(
			new[] { "--transform", "-t" }, () => Transformers.ChordPro, "How the input should be transformed in memory.");
		result.Add(transformOption);

		Option<Formats> formatOption = new(new[] { "--format", "-f" }, () => Formats.Text, "How the output should be formatted.");
		result.Add(formatOption);

		const string DefaultEncoding = "UTF-8";
		Option<string?> encodingOption = new(new[] { "--encoding", "-e" }, () => DefaultEncoding, "How the input and output text is encoded.");
		result.Add(encodingOption);

		result.SetHandler(context =>
		{
			FileInfo? input = GetArgumentValue(context, inputArgument);
			Parsers parsers = GetOptionValue(context, parseOption);
			Transformers transformers = GetOptionValue(context, transformOption);
			Encoding encoding = Encoding.GetEncoding(GetOptionValue(context, encodingOption) ?? DefaultEncoding);
			FileInfo? output = GetOptionValue(context, outputOption);
			bool overwrite = GetOptionValue(context, overwriteOption);
			Formats format = GetOptionValue(context, formatOption);

			ConvertCommand command = new(context, input, parsers, transformers, encoding, output, overwrite, format);
			return command.ExecuteAsync();
		});

		return result;
	}

	#endregion

	#region Protected Methods

	protected override Task OnExecuteAsync()
	{
		Document inputDocument = this.ParseInput();
		Document outputDocument = this.TransformInMemory(inputDocument);
		string outputText = this.FormatOutputText(outputDocument);
		this.WriteOutput(outputText);
		return Task.CompletedTask;
	}

	#endregion

	#region Private Methods

	private Document ParseInput()
	{
		bool readStdIn = this.input is null;
		TextReader reader = readStdIn ? Console.In : new StreamReader(this.input!.FullName, this.encoding, true);
		try
		{
			DocumentParser parser = new(
				this.parsers == Parsers.ChordPro
					? DocumentParser.ChordProLineParsers
					: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Load(reader, parser);
			return inputDocument;
		}
		finally
		{
			if (!readStdIn)
			{
				reader.Dispose();
			}
		}
	}

	private Document TransformInMemory(Document inputDocument)
	{
		ChordProTransformer transformer = this.transformers == Transformers.MobileSheets
			? new MobileSheetsTransformer(inputDocument)
			: new ChordProTransformer(inputDocument);
		Document outputDocument = transformer.ToChordPro().Document;
		return outputDocument;
	}

	private string FormatOutputText(Document outputDocument)
	{
		ContainerFormatter formatter = this.format == Formats.Xml
			? new XmlFormatter(outputDocument)
			: new TextFormatter(outputDocument);
		string outputText = formatter.ToString();
		return outputText;
	}

	private void WriteOutput(string outputText)
	{
		if (this.output is null)
		{
			this.Write(outputText);
		}
		else if (this.overwrite || !this.output.Exists)
		{
			File.WriteAllText(this.output.FullName, outputText, this.encoding);
		}
		else
		{
			this.WriteErrorLine("The specified output file already exists, and the --overwrite option was not used.");
			this.ExitCode = 1;
		}
	}

	#endregion
}
