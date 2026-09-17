#region Using Directives

using System.Text.Json;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class PerformanceInputTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow("chordbook://command/7/NextViewport", true)]
	[DataRow("chordbook://command/7/ToggleMetronome", true)]
	[DataRow("chordbook://command/6/NextSong", false)]
	[DataRow("chordbook://command/7/a/NextSong", false)]
	[DataRow("chordbook://command/7/0", false)]
	[DataRow("chordbook://command/7/NextSong?extra", false)]
	[DataRow("https://command/7/NextSong", false)]
	[DataRow("chordbook://command/7/Unknown", false)]
	public void OnlyExactCurrentCommandsAreAccepted(string url, bool expected)
		=> PerformanceInputService.TryReadCommand(url, 7, out _).ShouldBe(expected);

	[TestMethod]
	public void LearnedGesturesPreserveModifiersAndRejectInvalidKeys()
	{
		PerformanceKeyGesture gesture = new("ArrowRight", Control: true, Shift: true);
		string json = JsonSerializer.Serialize(gesture, JsonSerializerOptions.Web);
		PerformanceInputService.TryReadLearnedGesture("chordbook://learn/" + Uri.EscapeDataString(json), out var parsed).ShouldBeTrue();
		parsed.ShouldBe(gesture);
		foreach (string key in new[] { "Escape", "Tab", "Control", string.Empty, "\n", "Unidentified" })
		{
			string invalid = JsonSerializer.Serialize(new PerformanceKeyGesture(key), JsonSerializerOptions.Web);
			PerformanceInputService.TryReadLearnedGesture("chordbook://learn/" + Uri.EscapeDataString(invalid), out _).ShouldBeFalse();
		}

		PerformanceInputService.TryReadLearnedGesture("chordbook://learn/invalid", out _).ShouldBeFalse();
	}

	[TestMethod]
	public void DefaultsAreIndependentAndBindingsAreBoundedAndUnambiguous()
	{
		var defaults = PerformanceKeyBinding.CreateDefaults();
		defaults.Clear();
		PerformanceKeyBinding.CreateDefaults().Count.ShouldBe(7);
		var binding = new PerformanceKeyBinding(new(" "), PerformanceCommand.NextViewport);
		Should.Throw<ArgumentException>(() => PerformanceInputService.Validate([binding, binding]));
		Should.Throw<ArgumentException>(() => PerformanceInputService.Validate(
			[.. Enumerable.Range(0, 33).Select(index => new PerformanceKeyBinding(new("Key" + index), PerformanceCommand.NextSong))]));
		PerformanceInputService.Validate([binding, binding with { Gesture = new(" ", Control: true) }]);
	}

	[TestMethod]
	public async Task BindingsPersistWithBookSettingsAndRejectStaleEdits()
	{
		var token = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid device = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Keys", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid song = await session.CreateSongAsync("Song", [], [], "[C]Words", device, token);
		var row = session.Search(null).Single();
		PerformanceInputService service = new(session);
		var original = service.GetBindings();
		PerformanceKeyBinding binding = new(new("F9"), PerformanceCommand.ToggleMetronome);
		await service.SaveBindingsAsync(original, [binding], device, token);
		session.Search(null).Single().ShouldBeSameAs(row);
		await Should.ThrowAsync<InvalidOperationException>(() => service.SaveBindingsAsync(original, [], device, token));
		await session.ReloadAsync(token);
		service.GetBindings().Bindings.Single().ShouldBe(binding);
		(await session.GetSongEditAsync(song, token)).Text.ShouldBe("[C]Words");
	}

	#endregion
}
