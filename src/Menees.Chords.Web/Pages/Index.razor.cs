namespace Menees.Chords.Web.Pages;

#region Using Directives

using Blazored.LocalStorage;
using Menees.Chords.Formatters;
using Menees.Chords.Parsers;
using Menees.Chords.Transformers;
using Menees.Chords.Web.Client;
using Microsoft.AspNetCore.Components;

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
	private CopyState copyState = new("Copy", "oi oi-clipboard");

	#endregion

	#region Public Injected Properties

	[Inject]
	public ISyncLocalStorageService Storage { get; set; } = null!; // Set by DI.

	[Inject]
	public ClipboardService Clipboard { get; set; } = null!; // Set by DI.

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
			this.StateHasChanged();
		}
	}

	private async Task CopyToClipboardAsync()
	{
		// Writing to the clipboard may be denied, so you must handle the exception
		var temp = this.copyState;
		try
		{
			this.copyState = new("Copied", "oi oi-check", IsDisabled: true);
			await this.Clipboard.WriteTextAsync(this.output);
			await Task.Delay(TimeSpan.FromSeconds(1), this.cts.Token);
		}
#pragma warning disable CA1031 // Do not catch general exception types. Figure out JS exception type.
		catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
		{
			// TODO: Figure out JS exception type. [Bill, 8/28/2023]
			Console.WriteLine($"Cannot write text to clipboard: {ex}");
		}
		finally
		{
			this.copyState = temp;
		}
	}

	private void SaveAs()
	{
		// TODO: Finish implementation. [Bill, 8/27/2023]
		this.GetHashCode();
	}

	#endregion

	#region Private Types

	private sealed record CopyState(string Text, string ClassName, bool IsDisabled = false);

	#endregion
}