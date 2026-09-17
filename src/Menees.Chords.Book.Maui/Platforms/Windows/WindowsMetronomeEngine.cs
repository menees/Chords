#region Using Directives

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Services;
using Menees.Chords.Db;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Render;
using WinRT;

#endregion

namespace Menees.Chords.Book.Maui.Platforms.Windows;

public sealed partial class WindowsMetronomeEngine : IMetronomeEngine
{
	#region Private Data

	private const double WarmupSeconds = 0.1;
	private const double SecondsPerMinute = 60;
	private const int BufferDivisor = 10;
	private const int VisualSampleRate = 48000;
	private static readonly StrategyBasedComWrappers BufferWrappers = new();
	private readonly Lock gate = new();
	private readonly Stopwatch clock = new();
	private AudioGraph? graph;
	private AudioFrameInputNode? input;
	private MetronomeSettings settings = new();
	private float[] samples = [];
	private MetronomeRenderer? renderer;
	private long samplePosition;
	private int sampleRate;
	private int generation;
	private bool running;

	#endregion

	#region Private Types

	[GeneratedComInterface]
	[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IMemoryBufferByteAccess
	{
		void GetBuffer(out IntPtr buffer, out uint capacity);
	}

	#endregion

	#region Public API

	public MetronomeSettings CurrentSettings => CopySettings(this.settings);

	public bool IsRunning => Volatile.Read(ref this.running);

	public int BeatsPerMeasure => this.settings.BeatsPerMeasure;

	public bool VisualEnabled => this.settings.VisualEnabled;

	public bool AccentFirstBeat => this.settings.AccentFirstBeat;

	public int CurrentBeat
	{
		get
		{
			lock (this.gate)
			{
				double beat = this.GetBeatPosition();
				return this.running && beat >= 0
					? 1 + (int)(Math.Floor(beat) % this.settings.BeatsPerMeasure) : 0;
			}
		}
	}

	public MetronomeVisualState? VisualPulse
	{
		get
		{
			lock (this.gate)
			{
				return this.running && this.settings.VisualEnabled
					? MetronomeVisualState.At(
						this.GetBeatPosition() * SecondsPerMinute / this.settings.BeatsPerMinute,
						this.settings.BeatsPerMinute,
						this.settings.BeatsPerMeasure,
						this.settings.Subdivision,
						this.settings.AccentFirstBeat) : null;
			}
		}
	}

	public async Task StartAsync(MetronomeSettings settings, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		BookApplicationSession.ValidateMetronome(settings);

		// Snapshot caller-owned mutable settings before awaiting platform initialization.
		settings = CopySettings(settings);
		bool preserveClock;
		bool updated = false;
		AudioGraph? previousGraph = null;
		AudioFrameInputNode? previousInput = null;
		lock (this.gate)
		{
			preserveClock = this.running;
			if (preserveClock && (this.input is not null || !settings.AudioEnabled))
			{
				long position = Math.Max(this.samplePosition, (long)(this.clock.Elapsed.TotalSeconds * this.sampleRate));
				++this.generation;
				this.renderer!.Update(settings, Math.Max(0, position - (long)(this.sampleRate * WarmupSeconds)));
				this.settings = settings;
				if (!settings.AudioEnabled)
				{
					previousGraph = this.graph;
					previousInput = this.input;
					this.graph = null;
					this.input = null;
					this.samples = [];

					// Preserve visual time even if audio was disabled before its first callback.
					this.clock.Start();
				}

				updated = true;
			}
		}

		if (updated)
		{
			this.ReleaseAudio(previousGraph, previousInput);
			return;
		}

		int version;
		lock (this.gate)
		{
			version = preserveClock ? ++this.generation : this.generation;
		}

		if (!preserveClock)
		{
			version = this.Reset();
		}

		if (!settings.AudioEnabled)
		{
			MetronomeRenderer visualRenderer = await Task.Run(() => new MetronomeRenderer(settings, VisualSampleRate), cancellationToken).ConfigureAwait(true);
			lock (this.gate)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (version == this.generation)
				{
					this.settings = settings;
					this.renderer = visualRenderer;
					this.sampleRate = VisualSampleRate;
					this.samplePosition = 0;
					this.clock.Restart();
					this.running = true;
				}
			}
		}
		else
		{
			await this.StartAudioAsync(settings, version, preserveClock, cancellationToken).ConfigureAwait(true);
		}
	}

	public void Stop() => this.Reset();

	public void Dispose() => this.Stop();

	#endregion

	#region Private Methods

	private static MetronomeSettings CopySettings(MetronomeSettings settings) => new()
	{
		BeatsPerMinute = settings.BeatsPerMinute, BeatsPerMeasure = settings.BeatsPerMeasure, BeatUnit = settings.BeatUnit,
		Subdivision = settings.Subdivision, Sound = settings.Sound, Volume = settings.Volume,
		AccentFirstBeat = settings.AccentFirstBeat, AudioEnabled = settings.AudioEnabled, VisualEnabled = settings.VisualEnabled,
	};

	private async Task StartAudioAsync(MetronomeSettings settings, int version, bool preserveClock, CancellationToken cancellationToken)
	{
		CreateAudioGraphResult result = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media));
		if (result.Status != AudioGraphCreationStatus.Success)
		{
			throw new InvalidOperationException($"Could not initialize metronome audio: {result.Status}.");
		}

		AudioGraph nextGraph = result.Graph;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			CreateAudioDeviceOutputNodeResult output = await nextGraph.CreateDeviceOutputNodeAsync();
			if (output.Status != AudioDeviceNodeCreationStatus.Success)
			{
				throw new InvalidOperationException($"Could not open the audio output: {output.Status}.");
			}

			cancellationToken.ThrowIfCancellationRequested();
			int rate = (int)nextGraph.EncodingProperties.SampleRate;
			MetronomeRenderer nextRenderer = await Task.Run(() => new MetronomeRenderer(settings, rate), cancellationToken).ConfigureAwait(true);
			var encoding = nextGraph.EncodingProperties;
			encoding.ChannelCount = 1;
			AudioFrameInputNode nextInput = nextGraph.CreateFrameInputNode(encoding);
			nextInput.AddOutgoingConnection(output.DeviceOutputNode);
			bool installed = false;
			lock (this.gate)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (version == this.generation)
				{
					double previousBeat = Math.Max(0, this.GetBeatPosition());
					this.graph = nextGraph;
					this.input = nextInput;
					this.renderer = nextRenderer;
					this.settings = settings;
					this.sampleRate = rate;
					this.samples = new float[Math.Max(nextGraph.SamplesPerQuantum, rate / BufferDivisor)];
					nextInput.QuantumStarted += this.HandleQuantum;
					if (preserveClock)
					{
						this.samplePosition = (long)(this.clock.Elapsed.TotalSeconds * rate);
						nextRenderer.SetBeatPosition(previousBeat, Math.Max(0, this.samplePosition - (long)(rate * WarmupSeconds)));
					}
					else
					{
						this.samplePosition = 0;
						this.clock.Reset();
					}

					this.running = true;
					nextGraph.Start();
					installed = true;
				}
			}

			if (!installed)
			{
				nextGraph.Dispose();
			}
		}
		catch
		{
			nextGraph.Dispose();
			lock (this.gate)
			{
				if (ReferenceEquals(this.graph, nextGraph))
				{
					this.graph = null;
					this.input = null;
					this.running = false;
					this.clock.Reset();
				}
			}

			throw;
		}
	}

	private double GetBeatPosition()
		=> this.renderer?.GetBeatPosition((long)((this.clock.Elapsed.TotalSeconds - WarmupSeconds) * this.sampleRate)) ?? -1;

	private int Reset()
	{
		AudioGraph? previousGraph;
		AudioFrameInputNode? previousInput;
		int version;
		lock (this.gate)
		{
			version = ++this.generation;
			this.running = false;
			previousGraph = this.graph;
			previousInput = this.input;
			this.graph = null;
			this.input = null;
			this.renderer = null;
			this.clock.Reset();
		}

		this.ReleaseAudio(previousGraph, previousInput);
		return version;
	}

	private void ReleaseAudio(AudioGraph? previousGraph, AudioFrameInputNode? previousInput)
	{
		previousInput?.QuantumStarted -= this.HandleQuantum;
		previousGraph?.Stop();
		previousGraph?.Dispose();
	}

	private void HandleQuantum(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
	{
		lock (this.gate)
		{
			int count = args.RequiredSamples;
			if (this.running && ReferenceEquals(sender, this.input) && count > 0)
			{
				if (!this.clock.IsRunning)
				{
					// Establish the clock only after device activation and the first audio request.
					this.clock.Start();
				}

				long elapsed = (long)(this.clock.Elapsed.TotalSeconds * this.sampleRate);
				bool discontinuity = elapsed > this.samplePosition + count;
				if (discontinuity)
				{
					// Drop missed audio rather than submitting an overdue burst of clicks.
					this.samplePosition = elapsed;
				}

				if (this.samples.Length < count)
				{
					this.samples = new float[count];
				}

				long position = this.samplePosition - (long)(this.sampleRate * WarmupSeconds);
				int silence = (int)Math.Min(count, Math.Max(0, -position));
				this.samples.AsSpan(0, silence).Clear();
				if (silence < count)
				{
					this.renderer!.Render(this.samples.AsSpan(silence, count - silence), Math.Max(0, position), discontinuity);
				}

				this.samplePosition += count;

				using AudioFrame frame = new((uint)(count * sizeof(float)));
				using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
				using (var reference = buffer.CreateReference())
				{
					var access = (IMemoryBufferByteAccess)BufferWrappers.GetOrCreateObjectForComInstance(
						((IWinRTObject)reference).NativeObject.ThisPtr, CreateObjectFlags.None);
					access.GetBuffer(out IntPtr pointer, out uint capacity);
					if (capacity >= count * sizeof(float))
					{
						Marshal.Copy(this.samples, 0, pointer, count);
					}
				}

				sender.AddFrame(frame);
			}
		}
	}

	#endregion

}
