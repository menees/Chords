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
	private static readonly StrategyBasedComWrappers BufferWrappers = new();
	private readonly Lock gate = new();
	private readonly Stopwatch clock = new();
	private AudioGraph? graph;
	private AudioFrameInputNode? input;
	private MetronomeSettings settings = new();
	private float[] samples = [];
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

	public bool IsRunning => Volatile.Read(ref this.running);

	public int CurrentBeat => !this.IsRunning || this.clock.Elapsed.TotalSeconds < WarmupSeconds ? 0
		: 1 + (int)(Math.Floor((this.clock.Elapsed.TotalSeconds - WarmupSeconds)
			* this.settings.BeatsPerMinute / SecondsPerMinute) % this.settings.BeatsPerMeasure);

	public async Task StartAsync(MetronomeSettings settings, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		BookApplicationSession.ValidateMetronome(settings);
		if (this.IsRunning && BookApplicationSession.MetronomeSettingsEqual(this.settings, settings))
		{
			return;
		}

		this.Stop();
		int version = this.generation;
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
			if (version == this.generation)
			{
				this.graph = nextGraph;
				this.settings = settings;
				this.sampleRate = (int)nextGraph.EncodingProperties.SampleRate;
				var encoding = nextGraph.EncodingProperties;
				encoding.ChannelCount = 1;
				this.input = nextGraph.CreateFrameInputNode(encoding);
				this.input.AddOutgoingConnection(output.DeviceOutputNode);
				this.input.QuantumStarted += this.HandleQuantum;
				this.samplePosition = 0;
				this.clock.Reset();
				this.running = true;
				nextGraph.Start();
			}
			else
			{
				nextGraph.Dispose();
			}
		}
		catch
		{
			nextGraph.Dispose();
			this.graph = null;
			this.running = false;
			throw;
		}
	}

	public void Stop()
	{
		this.generation++;
		lock (this.gate)
		{
			this.running = false;
		}

		this.input?.QuantumStarted -= this.HandleQuantum;
		this.input = null;

		this.graph?.Stop();
		this.graph?.Dispose();
		this.graph = null;
		this.clock.Reset();
	}

	public void Dispose() => this.Stop();

	#endregion

	#region Private Methods

	private void HandleQuantum(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
	{
		lock (this.gate)
		{
			int count = args.RequiredSamples;
			if (this.running && count > 0)
			{
				if (!this.clock.IsRunning)
				{
					// Establish the clock only after device activation and the first audio request.
					this.clock.Start();
				}

				long elapsed = (long)(this.clock.Elapsed.TotalSeconds * this.sampleRate);
				if (elapsed > this.samplePosition + count)
				{
					// Drop missed audio rather than submitting an overdue burst of clicks.
					this.samplePosition = elapsed;
				}

				if (this.samples.Length < count)
				{
					this.samples = new float[count];
				}

				for (int i = 0; i < count; i++)
				{
					long position = this.samplePosition++ - (long)(this.sampleRate * WarmupSeconds);
					this.samples[i] = position < 0 || !this.settings.AudioEnabled ? 0 : MetronomePulse.Sample(
						position,
						this.sampleRate,
						this.settings.BeatsPerMinute,
						this.settings.BeatsPerMeasure,
						this.settings.Volume,
						this.settings.AccentFirstBeat);
				}

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
