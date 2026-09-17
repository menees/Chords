#region Using Directives

using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class MetronomeRendererTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void TempoChangesPreserveBeatFractionAndImmediatelyChangeRemainingDuration()
	{
		const int Rate = 48000;
		MetronomeRenderer renderer = new(new() { BeatsPerMinute = 120 }, Rate);
		const int HalfwayThroughSecondBeat = 36000;
		renderer.GetBeatPosition(HalfwayThroughSecondBeat).ShouldBe(1.5);
		renderer.Update(new() { BeatsPerMinute = 60 }, HalfwayThroughSecondBeat);
		renderer.GetBeatPosition(HalfwayThroughSecondBeat).ShouldBe(1.5);
		renderer.GetBeatPosition(60000).ShouldBe(2);
		float[] samples = new float[480];
		renderer.Render(samples, 60000);
		samples.Any(value => value != 0).ShouldBeTrue();
		renderer.Update(new() { BeatsPerMinute = 240 }, 60000);
		renderer.GetBeatPosition(72000).ShouldBe(3);
	}

	[TestMethod]
	public void SoundChangesWaitForTheNextWholeBeatAndKeepTheCurrentTail()
	{
		const int Rate = 48000;
		MetronomeSettings initial = new() { Sound = "Click", Subdivision = 2, AccentFirstBeat = false, Volume = 1 };
		MetronomeRenderer renderer = new(initial, Rate);
		MetronomeRenderer original = new(initial, Rate);
		float[] actual = new float[480];
		float[] expected = new float[480];
		renderer.Update(new() { Sound = "KickHiHat", Subdivision = 2, AccentFirstBeat = false, Volume = 1 }, 100);
		renderer.Render(actual, 100);
		original.Render(expected, 100);
		actual.ShouldBe(expected);
		renderer.Render(actual, Rate / 4);
		original.Render(expected, Rate / 4);
		actual.ShouldBe(expected);
		renderer.Render(actual, Rate / 2);
		new MetronomeRenderer(new() { Sound = "HiHat", AccentFirstBeat = false, Volume = 1 }, Rate).Render(expected, 0);
		actual.ShouldBe(expected);
	}

	[TestMethod]
	[DataRow("Click")]
	[DataRow("Kick")]
	[DataRow("HiHat")]
	[DataRow("Cowbell")]
	[DataRow("Woodblock")]
	public void AccentUsesADistinctPitchAndStaysWithinAudioHeadroom(string sound)
	{
		const int Rate = 48000;
		MetronomeRenderer renderer = new(new() { Sound = sound, AccentFirstBeat = true, Volume = 1 }, Rate);
		float[] accented = new float[Rate / 10];
		float[] normal = new float[accented.Length];
		renderer.Render(accented, 0);
		renderer.Render(normal, Rate / 2);
		accented.SequenceEqual(normal).ShouldBeFalse();
		accented.All(value => float.IsFinite(value) && Math.Abs(value) <= 1).ShouldBeTrue();
	}

	[TestMethod]
	public void LiveUpdatesReuseSamplesWithoutAllocatingAndMuteImmediately()
	{
		const int Rate = 48000;
		MetronomeRenderer renderer = new(new(), Rate);
		MetronomeSettings settings = new() { AudioEnabled = false, Sound = "Cowbell" };
		float[] buffer = new float[480];
		renderer.Update(settings, 0);
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < 100; index++)
		{
			renderer.Update(settings, index * buffer.Length);
			renderer.Render(buffer, index * buffer.Length);
		}

		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
		buffer.All(value => value == 0).ShouldBeTrue();
	}

	[TestMethod]
	public void DefaultPatternUsesOnlyKickOnTheAccentedBeatAndOnlyHiHatOtherwise()
	{
		const int Rate = 48000;
		MetronomeSettings settings = new() { Volume = 1 };
		MetronomeRenderer pattern = new(settings, Rate);
		float[] first = new float[Rate / 10];
		float[] second = new float[first.Length];
		float[] expected = new float[first.Length];
		pattern.Render(first, 0);
		pattern.Render(second, Rate / 2);
		new MetronomeRenderer(new() { Sound = "Kick", AccentFirstBeat = false, Volume = 1 }, Rate).Render(expected, 0);
		first.ShouldBe(expected);
		new MetronomeRenderer(new() { Sound = "HiHat", AccentFirstBeat = false, Volume = 1 }, Rate).Render(expected, 0);
		second.ShouldBe(expected);
		settings.AccentFirstBeat = false;
		new MetronomeRenderer(settings, Rate).Render(first, 0);
		first.ShouldBe(second);
	}

	[TestMethod]
	[DataRow("Kick")]
	[DataRow("HiHat")]
	[DataRow("Woodblock")]
	[DataRow("Cowbell")]
	[DataRow("Click")]
	public void BundledSoundsHaveHeadroomAndEqualUnaccentedBeats(string sound)
	{
		const int Rate = 48000;
		MetronomeRenderer renderer = new(new() { Sound = sound, AccentFirstBeat = false, Volume = 1 }, Rate);
		float[] first = new float[Rate / 10];
		float[] second = new float[first.Length];
		renderer.Render(first, 0);
		renderer.Render(second, Rate / 2);
		first.ShouldBe(second);
		first.Any(value => value != 0).ShouldBeTrue();
		first.All(value => float.IsFinite(value) && Math.Abs(value) <= 0.7f).ShouldBeTrue();
		Math.Sqrt(first.Sum(value => value * value) / first.Length).ShouldBeInRange(0.09, 0.121);
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[DataRow(3)]
	[DataRow(4)]
	public void FractionalDeadlinesAreIndependentOfBufferSizeAndDoNotAccumulateDrift(int subdivision)
	{
		const int Rate = 44100;
		const int Tempo = 137;
		const int Beats = 7;
		MetronomeSettings settings = new() { BeatsPerMinute = Tempo, BeatsPerMeasure = Beats, BeatUnit = 8, Subdivision = subdivision };
		MetronomeRenderer renderer = new(settings, Rate);
		float[] whole = new float[Rate * 2];
		float[] chunks = new float[whole.Length];
		renderer.Render(whole, 0);
		for (int offset = 0; offset < chunks.Length; offset += Rate / 100)
		{
			renderer.Render(chunks.AsSpan(offset, Rate / 100), offset);
		}

		chunks.ShouldBe(whole);
		const int Measures = 100_000;
		long lateMeasure = (long)Math.Ceiling(Measures * Beats * (double)Rate * 60 / Tempo);
		float[] late = new float[Rate / 100];
		renderer.Render(late, lateMeasure);
		late.ShouldBe([.. whole.Take(late.Length)]);
		for (int click = 1; click < subdivision; click++)
		{
			long start = (long)Math.Ceiling(click * (double)Rate * 60 / (Tempo * subdivision));
			renderer.Render(late, start);
			late.Any(value => value != 0).ShouldBeTrue();
		}
	}

	[TestMethod]
	public void LateAudioSkipsTheExpiredClickAcrossBuffersAndResumesAtTheNextDeadline()
	{
		const int Rate = 48000;
		MetronomeRenderer renderer = new(new(), Rate);
		float[] samples = new float[Rate / 100];
		renderer.Render(samples, 100, discontinuity: true);
		samples.All(value => value == 0).ShouldBeTrue();
		renderer.Render(samples, 100 + samples.Length);
		samples.All(value => value == 0).ShouldBeTrue();
		renderer.Render(samples, Rate / 2);
		samples.Any(value => value != 0).ShouldBeTrue();
	}

	[TestMethod]
	public void RenderHasNoManagedAllocationAndDoesNotReadMutableSettings()
	{
		const int Rate = 48000;
		const int Iterations = 1000;
		MetronomeSettings settings = new();
		MetronomeRenderer renderer = new(settings, Rate);
		float[] samples = new float[Rate / 100];
		renderer.Render(samples, 0);
		float[] expected = [.. samples];
		settings.Volume = 0;
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < Iterations; index++)
		{
			renderer.Render(samples, 0);
		}

		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
		samples.ShouldBe(expected);
	}

	[TestMethod]
	public void VisualPulsesUseTheSameSubdivisionClockAndAccentPolicy()
	{
		MetronomeVisualState.At(-0.1, 120, 3, 2, true).ShouldBeNull();
		MetronomeVisualState.At(0, 120, 3, 2, true).ShouldBe(new(1, true));
		MetronomeVisualState.At(0.15, 120, 3, 2, true).ShouldBeNull();
		MetronomeVisualState.At(0.25, 120, 3, 2, true).ShouldBe(new(1, false));
		MetronomeVisualState.At(0.5, 120, 3, 2, true).ShouldBe(new(2, false));
		MetronomeVisualState.At(1.5, 120, 3, 2, true).ShouldBe(new(1, true));
		MetronomeVisualState.At(1.5, 120, 3, 2, false).ShouldBe(new(1, false));
	}

	[TestMethod]
	public async Task BookTransitionPolicyAndExpandedSettingsRoundTrip()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Metronome", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		session.Database!.BookSettings.StopMetronomeOnSetlistTransition.ShouldBeTrue();
		MetronomeSettings settings = new() { Sound = "Cowbell", Subdivision = 3, BeatUnit = 8, BeatsPerMeasure = 6 };
		await session.SaveBookMetronomeAsync(settings, false, device, token);
		await session.ActivateAsync(store, location, token);
		session.Database!.BookSettings.StopMetronomeOnSetlistTransition.ShouldBeFalse();
		BookApplicationSession.MetronomeSettingsEqual(settings, session.GetMetronomeSettings()).ShouldBeTrue();
		Should.Throw<ArgumentException>(() => BookApplicationSession.ValidateMetronome(new() { Sound = "Missing" }));
		Should.Throw<ArgumentException>(() => BookApplicationSession.ValidateMetronome(new() { Subdivision = 5 }));
	}

	#endregion
}
