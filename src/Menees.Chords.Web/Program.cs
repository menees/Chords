namespace Menees.Chords.Web;

using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

public class Program
{
#pragma warning disable CC0061 // Asynchronous method can be terminated with the 'Async' keyword. The entry point must be Main.
	public static async Task Main(string[] args)
#pragma warning restore CC0061 // Asynchronous method can be terminated with the 'Async' keyword.
	{
		var builder = WebAssemblyHostBuilder.CreateDefault(args);
		builder.RootComponents.Add<App>("#app");
		builder.RootComponents.Add<HeadOutlet>("head::after");

		builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
		builder.Services.AddBlazoredLocalStorage();

		await builder.Build().RunAsync();
	}
}
