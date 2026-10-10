using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SnapGit.Platform.Recording;

/// <summary>
/// Captures system audio (WASAPI loopback) and/or the default microphone and mixes them into one
/// 48 kHz / stereo / 16-bit PCM stream, read in fixed chunks. Silence fills any gap, so the stream always
/// stays aligned with the wall clock even when nothing is playing.
/// </summary>
internal sealed class AudioMixer : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;

    private readonly List<(IWaveIn Capture, BufferedWaveProvider Buffer, ISampleProvider Source)> _inputs = new();

    public AudioMixer(bool systemAudio, bool microphone)
    {
        if (systemAudio) Add(new WasapiLoopbackCapture());
        if (microphone)
        {
            try { Add(new WasapiCapture()); }
            catch (Exception) when (systemAudio) { /* no microphone: keep recording system audio */ }
        }
    }

    public bool HasInputs => _inputs.Count > 0;

    private void Add(IWaveIn capture)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            ReadFully = true,                 // empty buffer => silence, never a short read
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
        };
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        ISampleProvider source = buffer.ToSampleProvider();
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        else if (source.WaveFormat.Channels > 2)
        {
            var mux = new MultiplexingSampleProvider([source], 2);
            mux.ConnectInputToOutput(0, 0);
            mux.ConnectInputToOutput(1, 1);
            source = mux;
        }
        if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);

        _inputs.Add((capture, buffer, source));
    }

    public void Start()
    {
        foreach (var input in _inputs) input.Capture.StartRecording();
    }

    /// <summary>Reads exactly <paramref name="frames"/> sample frames (frames × 2 channels × 2 bytes) of mixed PCM16.</summary>
    public byte[] Read(int frames)
    {
        var mix = new float[frames * Channels];
        var temp = new float[mix.Length];

        foreach (var (_, _, source) in _inputs)
        {
            var read = source.Read(temp, 0, temp.Length);
            for (var i = 0; i < read; i++) mix[i] += temp[i];
        }

        var pcm = new byte[mix.Length * 2];
        for (var i = 0; i < mix.Length; i++)
        {
            var value = (short)(Math.Clamp(mix[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return pcm;
    }

    public void Dispose()
    {
        foreach (var (capture, _, _) in _inputs)
        {
            try { capture.StopRecording(); } catch (InvalidOperationException) { /* never started */ }
            capture.Dispose();
        }
        _inputs.Clear();
    }
}
