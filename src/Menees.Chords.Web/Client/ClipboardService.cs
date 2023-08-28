namespace Menees.Chords.Web.Client;

using System.Threading.Tasks;
using Microsoft.JSInterop;

// https://www.meziantou.net/copying-text-to-clipboard-in-a-blazor-application.htm
public sealed class ClipboardService
{
	private readonly IJSRuntime jsRuntime;

	public ClipboardService(IJSRuntime jsRuntime)
	{
		this.jsRuntime = jsRuntime;
	}

	public ValueTask WriteTextAsync(string text)
		=> this.jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
}