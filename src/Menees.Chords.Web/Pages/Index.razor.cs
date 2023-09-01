namespace Menees.Chords.Web.Pages;

#region Using Directives

using System.Linq;
using Blazored.LocalStorage;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

#endregion

public sealed partial class Index : IDisposable
{
	#region Private Data Members

	private const int TextAreaRows = 30;

	private string fromType = "General";
	private string toType = "ChordPro";
	private string input = string.Empty;
	private string output = string.Empty;
	private bool whenTyping = true;
	private CancellationTokenSource cts = new();
	private CopyState copyState = new("Copy", "oi oi-clipboard", "btn-secondary");
	private string? title;

	#endregion

	#region Public Injected Properties

	[Inject]
	public ISyncLocalStorageService Storage { get; set; } = null!; // Set by DI.

	[Inject]
	public IJSRuntime JavaScript { get; set; } = null!; // Set by DI.

	#endregion

	#region Public Properties

	public string FromType
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

	public string ToType
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

	#region Private Properties

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

	protected override void OnInitialized()
	{
		if (this.Storage.ContainKey(nameof(this.fromType)))
		{
			this.fromType = this.Storage.GetItem<string>(nameof(this.fromType));
		}

		if (this.Storage.ContainKey(nameof(this.toType)))
		{
			this.toType = this.Storage.GetItem<string>(nameof(this.toType));
		}

		if (this.Storage.ContainKey(nameof(this.whenTyping)))
		{
			this.whenTyping = this.Storage.GetItem<bool>(nameof(this.whenTyping));
		}

		if (this.Storage.ContainKey(nameof(this.input)))
		{
			this.input = this.Storage.GetItem<string>(nameof(this.input));
			this.ConvertInput();
		}
	}

	#endregion

	#region Private Methods

	private void ConvertInput()
	{
		if (string.IsNullOrWhiteSpace(this.input))
		{
			this.output = string.Empty;
		}
		else
		{
			DocumentParser parser = new(this.fromType == "ChordPro"
				? DocumentParser.ChordProLineParsers
				: DocumentParser.DefaultLineParsers);
			Document inputDocument = Document.Parse(this.input, parser);
			ChordProTransformer transformer = this.toType == "MobileSheets"
				? new MobileSheetsTransformer(inputDocument)
				: new ChordProTransformer(inputDocument);
			Document outputDocument = transformer.ToChordPro().Document;
			TextFormatter formatter = new(outputDocument);
			this.output = formatter.ToString();
			this.title = DocumentTransformer.Flatten(outputDocument.Entries)
				.OfType<ChordProDirectiveLine>()
				.FirstOrDefault(directive => directive.LongName.Equals(nameof(this.title), ChordParser.Comparison))?.Argument;
			this.StateHasChanged();
		}
	}

	private async Task CopyToClipboardAsync()
	{
		// Writing to the clipboard may be denied, so you must handle the exception
		var temp = this.copyState;
		try
		{
			this.copyState = new("Copied", "oi oi-check", "btn-success", IsDisabled: true);
			await this.JavaScript.InvokeVoidAsync("navigator.clipboard.writeText", this.output);
		}
		catch (JSException ex)
		{
			Console.WriteLine($"Cannot write text to clipboard: {ex}");
			this.copyState = new("Failed", "oi oi-warning", "btn-danger", IsDisabled: true);
		}
		finally
		{
			await Task.Delay(TimeSpan.FromSeconds(1), this.cts.Token);
			this.copyState = temp;
		}
	}

	private async void DownloadAsync()
	{
		// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
		byte[] fileBytes = System.Text.Encoding.UTF8.GetBytes(this.output);
		string fileName = string.IsNullOrEmpty(this.title) ? $"{this.toType}.cho" : $"{this.title}.cho";
		await this.JavaScript.InvokeVoidAsync("BlazorDownloadFile", fileName, "text/plain", fileBytes);
	}

	#endregion

	#region Private Types

	private sealed record CopyState(string Text, string ImageClass, string ButtonClass, bool IsDisabled = false);

	#endregion
}