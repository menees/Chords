namespace Menees.Chords.Web;

#region Using Directives

using System.Linq;
using System.Text;
using System.Xml.Linq;
using Blazored.LocalStorage;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

#endregion

public sealed partial class Index : IDisposable
{
	#region Private Data Members

	private const string MetaFileName = "filename";
	private const int MaximumPreviewTranspose = 11;
	private const int MinimumDoubleDigitOffset = 10;

	private static readonly Encoding UTF8 = Encoding.UTF8;

	private readonly CancellationTokenSource cts = new();

	private Parser fromType = Parser.General;
	private Transformer toType = Transformer.ChordPro;
	private string input = string.Empty;
	private string output = string.Empty;
	private bool whenTyping = true;
	private bool longNames = true;
	private CopyState copyState = new("Copy", IconName.Copy, "btn-secondary");
	private ElementReference? inputElement;
	private ElementReference htmlView;
	private Document? outputDocument;
	private Document? previewDocument;
	private Key? previewKey;
	private Notation previewNotation = Notation.Name;
	private int previewTranspose;
	private string previewHtml = string.Empty;
	private string previewPrintHtml = string.Empty;
	private string? previewMessage;
	private bool showHtmlPreview;
	private bool focusHtmlView;

	#endregion

	#region Private Enums

	private enum Parser
	{
		General,
		ChordPro,
	}

	#endregion

	#region Public Injected Properties

	[Inject]
	public ISyncLocalStorageService Storage { get; set; } = null!; // Set by DI.

	[Inject]
	public IJSRuntime JavaScript { get; set; } = null!; // Set by DI.

	[Inject]
	public HttpClient Http { get; set; } = null!; // Set by DI.

	#endregion

	#region Public Properties

	public string Input
	{
		get => this.input;
		set
		{
			if (this.input != value)
			{
				this.input = value;
				this.Storage.SetItem(nameof(this.input), this.input);
				if (this.whenTyping)
				{
					this.ConvertInput();
				}
			}
		}
	}

	public bool LongNames
	{
		get => this.longNames;
		set
		{
			if (this.longNames != value)
			{
				this.longNames = value;
				this.Storage.SetItem(nameof(this.longNames), this.longNames);
				this.ConvertInput();
			}
		}
	}

	public bool WhenTyping
	{
		get => this.whenTyping;
		set
		{
			if (this.whenTyping != value)
			{
				this.whenTyping = value;
				this.Storage.SetItem(nameof(this.whenTyping), this.whenTyping);
				if (this.whenTyping)
				{
					this.ConvertInput();
				}
			}
		}
	}

	#endregion

	#region Internal Properties

	internal Transformer ToType
	{
		get => this.toType;
		set
		{
			if (this.toType != value)
			{
				this.toType = value;
				this.Storage.SetItem(nameof(this.toType), this.toType);
				this.ConvertInput();
			}
		}
	}

	#endregion

	#region Private Properties

	private Parser FromType
	{
		get => this.fromType;
		set
		{
			if (this.fromType != value)
			{
				this.fromType = value;
				this.Storage.SetItem(nameof(this.fromType), this.fromType);
				this.ConvertInput();
			}
		}
	}

	private Notation PreviewNotation
	{
		get => this.previewNotation;
		set
		{
			if (this.previewNotation != value)
			{
				this.previewNotation = value;
				this.Storage.SetItem(nameof(this.previewNotation), value);
				this.RefreshHtmlPreview();
			}
		}
	}

	private int PreviewTranspose
	{
		get => this.previewTranspose;
		set
		{
			if (this.previewTranspose != value)
			{
				this.previewTranspose = value;
				this.Storage.SetItem(nameof(this.previewTranspose), value);
				this.RefreshHtmlPreview();
			}
		}
	}

	// IntelliSense kept showing an error if this was inlined in the @bind:event syntax.
	private string InputChangeEvent => this.whenTyping ? "oninput" : "onchange";

	#endregion

	#region Public Methods

	public void Dispose()
	{
		this.cts.Cancel(); // Cancel Task.Delay
		this.cts.Dispose();
	}

	#endregion

	#region Protected Methods

	protected override async Task OnInitializedAsync()
	{
		this.fromType = this.GetStoredEnum(nameof(this.fromType), this.fromType);
		this.toType = this.GetStoredEnum(nameof(this.toType), this.toType);
		this.previewNotation = this.GetStoredEnum(nameof(this.previewNotation), this.previewNotation);

		if (this.Storage.ContainKey(nameof(this.previewTranspose)))
		{
			int storedTranspose = this.Storage.GetItem<int>(nameof(this.previewTranspose));
			this.previewTranspose = Math.Clamp(
				storedTranspose,
				-MaximumPreviewTranspose,
				MaximumPreviewTranspose);
		}

		if (this.Storage.ContainKey(nameof(this.longNames)))
		{
			this.longNames = this.Storage.GetItem<bool>(nameof(this.longNames));
		}

		if (this.Storage.ContainKey(nameof(this.whenTyping)))
		{
			this.whenTyping = this.Storage.GetItem<bool>(nameof(this.whenTyping));
		}

		if (this.Storage.ContainKey(nameof(this.input)))
		{
			this.input = this.Storage.GetItem<string>(nameof(this.input)) ?? this.input;
		}
		else
		{
			this.input = await this.Http.GetStringAsync("Default.crd");
		}

		this.ConvertInput();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		await base.OnAfterRenderAsync(firstRender);
		if (this.focusHtmlView)
		{
			this.focusHtmlView = false;
			await this.htmlView.FocusAsync();
		}
	}

	#endregion

	#region Private Methods

	private static string CreatePrintHtml(XDocument html)
	{
		StringBuilder result = new();
		foreach (XElement style in html.Root!.Element("head")!.Elements("style"))
		{
			XElement printStyle = new(style);
			printStyle.SetAttributeValue("media", "print");
			result.Append(printStyle.ToString(SaveOptions.DisableFormatting));
		}

		foreach (XElement element in html.Root.Element("body")!.Elements().Where(element => element.Name != "script"))
		{
			result.Append(element.ToString(SaveOptions.DisableFormatting));
		}

		return result.ToString();
	}

	private void ConvertInput()
	{
		if (string.IsNullOrWhiteSpace(this.input))
		{
			this.outputDocument = null;
			this.output = string.Empty;
		}
		else
		{
			DocumentParser parser = new(this.fromType == Parser.ChordPro
				? DocumentParser.ChordProLineParsers
				: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Parse(this.input, parser);
			DocumentTransformer transformer = this.toType switch
			{
				Transformer.MobileSheets => new MobileSheetsTransformer(inputDocument, this.longNames),
				Transformer.ChordOverLyric => new ChordOverLyricTransformer(inputDocument),
				_ => new ChordProTransformer(inputDocument, this.longNames),
			};
			this.outputDocument = transformer.Transform().Document;
			TextFormatter formatter = new(this.outputDocument);
			this.output = formatter.ToString();
		}

		this.StateHasChanged();
	}

	private async Task CopyToClipboardAsync(string text, string? elementId)
	{
		// Writing to the clipboard may be denied, so we must handle the exception
		var temp = this.copyState;
		try
		{
			this.copyState = new("Copied", IconName.Success, "btn-success", IsDisabled: true);
			await this.JavaScript.InvokeVoidAsync("CopyToClipboard", text, elementId);
		}
		catch (JSException ex)
		{
			Console.WriteLine($"Cannot write text to clipboard: {ex}");
			this.copyState = new("Failed", IconName.Warning, "btn-danger", IsDisabled: true);

			// Blazor seems to call StateHasChanged implicitly before invoking the JavaScript,
			// so if we don't do this here, then only the successful "Copied" state will show
			// until the state changes again after the Task.Delay. This forces "Failed" to show.
			this.StateHasChanged();
		}
		finally
		{
			await Task.Delay(TimeSpan.FromSeconds(1), this.cts.Token);
			this.copyState = temp;
		}
	}

	private Task CopyOutputToClipboardAsync()
		=> this.CopyToClipboardAsync(this.output, "output");

	private Task CopyFileNameToClipboardAsync()
		=> this.CopyToClipboardAsync(this.GetFileName(), null);

	private async Task SaveAsync()
	{
		// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
		byte[] fileBytes = UTF8.GetBytes(this.output);
		await this.JavaScript.InvokeVoidAsync("BlazorDownloadFile", this.GetFileName(), "text/plain", fileBytes);
	}

	private async Task ViewHtmlAsync()
	{
		DocumentParser parser = new(this.toType == Transformer.ChordOverLyric
			? DocumentParser.DefaultLineParsers
			: DocumentParser.ChordProLineParsers);
		this.previewDocument = Document.Parse(this.output, parser);
		this.previewKey = Key.Find(this.previewDocument, DetectKey.FirstChord);
		this.showHtmlPreview = true;
		this.focusHtmlView = true;
		this.RefreshHtmlPreview();
		await this.JavaScript.InvokeVoidAsync("SetHtmlViewOpen", true);
	}

	private async Task CloseHtmlPreviewAsync()
	{
		this.showHtmlPreview = false;
		await this.JavaScript.InvokeVoidAsync("SetHtmlViewOpen", false);
	}

	private void RefreshHtmlPreview()
	{
		if (this.previewDocument is not null)
		{
			Document document = this.previewDocument;
			this.previewMessage = null;
			if (this.previewKey is null)
			{
				this.previewMessage = "A key could not be detected, so notation and transposition are unavailable.";
			}
			else
			{
				if (this.previewNotation == Notation.Name && this.previewTranspose != 0)
				{
					document = new TransposeTransformer(document, (sbyte)this.previewTranspose, this.previewKey).Transform().Document;
				}

				document = new NotationTransformer(document, this.previewNotation, DetectKey.FirstChord).Transform().Document;
			}

			HtmlFormatter formatter = new(document);
			XDocument html = formatter.ToXDocument();
			html.Root!.Element("body")!.Add(new XElement("script", new XAttribute("src", "HtmlView.js"), string.Empty));
			this.previewHtml = HtmlFormatter.Serialize(html);
			this.previewPrintHtml = CreatePrintHtml(html);
		}
	}

	private async Task HandleHtmlViewKeyDownAsync(KeyboardEventArgs e)
	{
		if (e.Key == "Escape")
		{
			await this.CloseHtmlPreviewAsync();
		}
	}

	private string GetTransposeLabel(int offset)
	{
		string offsetText = offset switch
		{
			<= -MinimumDoubleDigitOffset => $"−{-offset}",
			< 0 => $" −{-offset}",
			>= MinimumDoubleDigitOffset => $"+{offset}",
			> 0 => $" +{offset}",
			_ => "  0",
		};

		string targetKey = string.Empty;
		if (this.previewKey is not null)
		{
			Chord keyChord = Chord.Parse(this.previewKey.Name);
			targetKey = $" → {keyChord.Transpose((sbyte)offset).Name}";
		}

		return offsetText + targetKey;
	}

	private TEnum GetStoredEnum<TEnum>(string name, TEnum defaultValue)
		where TEnum : struct, Enum
	{
		TEnum result = defaultValue;
		if (this.Storage.ContainKey(name))
		{
			// Old versions stored enums as strings; new versions use integers. Reading the JSON value
			// as text and parsing it supports both representations.
			string? value = this.Storage.GetItem<string>(name);
			if (Enum.TryParse(value, out TEnum parsed) && Enum.IsDefined(parsed))
			{
				result = parsed;
			}
		}

		return result;
	}

	private string GetFileName()
	{
		StringBuilder sb = new();
		if (this.outputDocument != null)
		{
			IReadOnlyList<Entry> flattenedOutputEntries = DocumentTransformer.Flatten(this.outputDocument.Entries);
			List<ChordProDirectiveLine> directives = [.. flattenedOutputEntries.OfType<ChordProDirectiveLine>()];
			const StringComparison Comparison = ChordParser.Comparison;

			string? inputFileName = directives.Select(d => MetadataEntry.TryParse(d) is MetadataEntry meta
					&& meta.Name.Equals(MetaFileName, Comparison) ? meta.Argument : null).FirstOrDefault();
			if (!string.IsNullOrEmpty(inputFileName) && Path.GetFileNameWithoutExtension(inputFileName) is string nameOnly)
			{
				sb.Append(nameOnly);
			}

			if (sb.Length == 0)
			{
				static string? TryGetDirectiveArgument(List<ChordProDirectiveLine> directives, string longName)
					=> directives.FirstOrDefault(directive => directive.LongName.Equals(longName, Comparison))?.Argument;

				string? title = TryGetDirectiveArgument(directives, nameof(title));
				sb.Append(title);

				string? artist = TryGetDirectiveArgument(directives, nameof(artist));
				if (!string.IsNullOrEmpty(artist))
				{
					if (sb.Length > 0)
					{
						sb.Append(" - ");
					}

					sb.Append(artist);
				}
			}

			if (sb.Length == 0 && flattenedOutputEntries.Count > 0)
			{
				// If there was a usable DirectiveLine or TitleLine, the logic above would have used it.
				string? firstLyrics = flattenedOutputEntries.Select(entry => entry switch
				{
					ChordProLyricLine chordProLyricLine => chordProLyricLine.Split().Lyrics,
					LyricLine lyricLine => lyricLine,
					_ => null,
				}).FirstOrDefault(line => line is not null)?.Text.Trim();

				sb.Append(firstLyrics);
			}
		}

		if (sb.Length == 0)
		{
			sb.Append(this.toType);
		}

		sb.Append(this.toType == Transformer.ChordOverLyric ? ".txt" : ".cho");

		string result = sb.ToString();
		return result;
	}

	private async Task CleanInputAsync()
	{
		Cleaner cleaner = new(this.Input);
		this.Input = cleaner.CleanText;
		if (this.inputElement != null)
		{
			await this.inputElement.Value.FocusAsync();
		}
	}

	private async Task OpenAsync(InputFileChangeEventArgs e)
	{
		IBrowserFile file = e.File;
		string fileName = file.Name;
		string newLine = Environment.NewLine;

		try
		{
			// Enforce a max size to avoid out-of-memory on WASM.
			const long MaxFileBytes = 128 * 1024;
			using var stream = file.OpenReadStream(MaxFileBytes);
			using var reader = new StreamReader(stream, UTF8, detectEncodingFromByteOrderMarks: true);
			string text = await reader.ReadToEndAsync();
			this.Input = string.IsNullOrWhiteSpace(fileName)
				? text
				: $"{{meta: {MetaFileName} {fileName}}}{newLine}{text}";
		}
		catch (IOException ex)
		{
			this.Input = $"Error uploading {fileName}:{newLine}{ex.Message}{newLine}({ex.GetType().Name})";
		}

		this.StateHasChanged();
	}

	#endregion

	#region Private Types

	private sealed record CopyState(string Text, IconName IconName, string ButtonClass, bool IsDisabled = false);

	#endregion
}
