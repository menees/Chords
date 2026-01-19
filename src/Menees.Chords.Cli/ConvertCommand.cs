namespace Menees.Chords.Cli;

#region Using Directives

using System;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;

#endregion

internal sealed class ConvertCommand : BaseCommand
{
	#region Private Data Members

	private const string ReadStdIn = "-";

	private readonly Parsers parsers;
	private readonly Transformer transformer;
	private readonly Encoding inputEncoding;
	private readonly Encoding outputEncoding;
	private readonly FileInfo? input;
	private readonly FileInfo? output;
	private readonly bool overwrite;
	private readonly Formats format;
	private readonly bool clean;
	private readonly bool? preferLongNames;

	#endregion

	#region Constructors

	private ConvertCommand(
		ParseResult parseResult,
		FileInfo? input,
		Parsers parsers,
		Transformer transformers,
		Encoding[] encodings,
		FileInfo? output,
		bool overwrite,
		Formats format,
		bool clean,
		bool? preferLongNames)
		: base(parseResult)
	{
		this.input = input;
		this.parsers = parsers;
		this.transformer = transformers;
		this.inputEncoding = encodings[0];
		this.outputEncoding = encodings.Length > 1 ? encodings[1] : this.inputEncoding;
		this.output = output;
		this.overwrite = overwrite;
		this.format = format;
		this.clean = clean;
		this.preferLongNames = preferLongNames;
	}

	#endregion

	#region Public Methods

	public static Command Create()
	{
		Command result = new("convert", "Converts a chord sheet file from one format to another.");

		Argument<FileInfo?> inputArgument = new(nameof(input))
		{
			Description = $"The file to convert. Use \"{ReadStdIn}\" to read from stdin.",
			CustomParser = argumentResult =>
			{
				// https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial#add-subcommands-and-custom-validation
				FileInfo? fileInfo = null;
				string? filePath = argumentResult.Tokens.Single().Value;
				if (filePath != ReadStdIn)
				{
					if (File.Exists(filePath))
					{
						fileInfo = new FileInfo(filePath);
					}
					else
					{
						argumentResult.AddError("Input file does not exist.");
					}
				}

				return fileInfo;
			},
		};
		result.Add(inputArgument);

		Option<bool> cleanOption = new("--clean", "-c") { Description = "Clean the input lines before parsing." };
		result.Add(cleanOption);

		Option<Parsers> parseOption = new("--parse", "-p")
		{
			DefaultValueFactory = _ => Parsers.Default,
			Description = "How the input file should be parsed.",
		};
		result.Add(parseOption);

		Option<Transformer> transformOption = new("--transform", "-t")
		{
			DefaultValueFactory = _ => Transformer.ChordPro,
			Description = "How the input should be transformed in memory.",
		};
		result.Add(transformOption);

		Option<NullableBool> longOption = new("--longNames", "-l")
		{
			DefaultValueFactory = _ => NullableBool.Null,
			Description = "Prefer long ChordPro directive names.",
			CustomParser = NullableBoolExtensions.ToNullableBool,
		};
		result.Add(longOption);

		Option<Formats> formatOption = new("--format", "-f")
		{
			DefaultValueFactory = _ => Formats.Text,
			Description = "How the output should be formatted.",
		};
		result.Add(formatOption);

		Option<FileInfo> outputOption = new("--output", "-o") { Description = "The output file name. Omit to write to stdout." };
		result.Add(outputOption);

		Option<bool> overwriteOption = new("--overwrite", "-y") { Description = "Overwrite the output file if it already exists." };
		result.Add(overwriteOption);

		const string DefaultEncoding = "UTF-8";
		Option<string[]?> encodingOption = new("--encoding", "-e")
		{
			DefaultValueFactory = _ => [DefaultEncoding],
			Description = "How the input and output text are encoded. Takes 1 or 2 encoding names.",
			AllowMultipleArgumentsPerToken = true,
		};
		encodingOption.Validators.Add(result =>
		{
			if (result.GetValue(encodingOption) is string[] array && array.Length > 2)
			{
				result.AddError("No more than two encodings can be specified.");
			}
		});

		result.Add(encodingOption);

		result.SetAction((parseResult, cancellationToken) =>
		{
			FileInfo? input = GetArgumentValue(parseResult, inputArgument);
			Parsers parsers = GetOptionValue(parseResult, parseOption);
			Transformer transformers = GetOptionValue(parseResult, transformOption);
			Encoding[] encodings = [.. (GetOptionValue(parseResult, encodingOption) ?? [DefaultEncoding]).Select(e => Encoding.GetEncoding(e))];
			FileInfo? output = GetOptionValue(parseResult, outputOption);
			bool overwrite = GetOptionValue(parseResult, overwriteOption);
			Formats format = GetOptionValue(parseResult, formatOption);
			bool clean = GetOptionValue(parseResult, cleanOption);
			bool? preferLongNames = GetOptionValue(parseResult, longOption).ToStandardType();

			ConvertCommand command = new(parseResult, input, parsers, transformers, encodings, output, overwrite, format, clean, preferLongNames);
			return command.ExecuteAsync(cancellationToken);
		});

		return result;
	}

	#endregion

	#region Protected Methods

	protected override Task<int?> OnExecuteAsync(CancellationToken cancellationToken)
	{
		Document inputDocument = this.ParseInput();
		Document outputDocument = this.TransformInMemory(inputDocument);
		string outputText = this.FormatOutputText(outputDocument);
		this.WriteOutput(outputText);
		return Task.FromResult(this.ExitCode);
	}

	#endregion

	#region Private Methods

	private Document ParseInput()
	{
		bool readStdIn = this.input is null;
		TextReader reader = readStdIn ? Console.In : new StreamReader(this.input!.FullName, this.inputEncoding, true);
		try
		{
			DocumentParser parser = new(
				this.parsers == Parsers.ChordPro
					? DocumentParser.ChordProLineParsers
					: DocumentParser.DefaultLineParsers);

			Document inputDocument;
			if (this.clean)
			{
				Cleaner cleaner = new(reader.ReadToEnd());
				inputDocument = Document.Parse(cleaner.CleanText, parser);
			}
			else
			{
				inputDocument = Document.Load(reader, parser);
			}

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
		DocumentTransformer transformer = this.transformer switch
		{
			Transformer.MobileSheets => new MobileSheetsTransformer(inputDocument, this.preferLongNames),
			Transformer.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
			_ => new ChordProTransformer(inputDocument, this.preferLongNames),
		};
		Document outputDocument = transformer.Transform().Document;
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
			File.WriteAllText(this.output.FullName, outputText, this.outputEncoding);
		}
		else
		{
			this.WriteErrorLine("The specified output file already exists, and the --overwrite option was not used.");
			this.ExitCode = 1;
		}
	}

	#endregion
}
