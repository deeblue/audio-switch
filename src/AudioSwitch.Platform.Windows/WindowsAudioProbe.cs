using NAudio.CoreAudioApi;
using NAudio.Wave;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

/// <summary>Plays a short shared-mode tone on one endpoint and reads endpoint capture peak.</summary>
public sealed class WindowsAudioProbe : IAudioProbe
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _captureDevice;
    private WasapiRecorder? _capture;
    private string? _captureEndpointId;
    private TaskCompletionSource? _captureStopped;
    private bool _disposed;

    public async Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(duration));

        using var device = _enumerator.GetDevice(endpointId);
        if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
            throw new ArgumentException(Loc.T("試音只能指定作用中的播放端點。"), nameof(endpointId));

        using var output = new WasapiPlayerBuilder().WithDevice(device).WithSharedMode().WithEventSync().WithLatency(100).Build();
        var provider = new SineWaveProvider(duration);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is null) stopped.TrySetResult();
            else stopped.TrySetException(args.Exception);
        };
        output.Init(provider);
        output.Play();
        try
        {
            await stopped.Task.WaitAsync(duration + TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            output.Stop();
        }
    }

    public void StartPeakSampling(string endpointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        if (_capture is not null)
            throw new InvalidOperationException("Peak sampling is already active.");

        var captureDevice = _enumerator.GetDevice(endpointId);
        if (captureDevice.DataFlow != DataFlow.Capture || captureDevice.State != DeviceState.Active)
        {
            captureDevice.Dispose();
            throw new ArgumentException(Loc.T("峰值只能讀取作用中的錄音端點。"), nameof(endpointId));
        }

        WasapiRecorder? capture = null;
        try
        {
            capture = new WasapiRecorderBuilder().WithDevice(captureDevice).WithSharedMode().WithEventSync().WithBufferLength(100).Build();
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is null) stopped.TrySetResult();
                else stopped.TrySetException(args.Exception);
            };
            _captureDevice = captureDevice;
            _capture = capture;
            _captureEndpointId = endpointId;
            _captureStopped = stopped;
            capture.StartRecording();
        }
        catch
        {
            capture?.Dispose();
            captureDevice.Dispose();
            ClearCaptureState();
            throw;
        }
    }

    public float ReadPeakLevel(string endpointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        if (_capture is null || _captureDevice is null ||
            !_captureEndpointId!.Equals(endpointId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Peak sampling is not active for the requested capture endpoint.");
        if (_captureStopped?.Task.IsCompleted == true)
            throw new InvalidOperationException("The capture stream stopped unexpectedly.", _captureStopped.Task.Exception);
        return _captureDevice.AudioMeterInformation.MasterPeakValue;
    }

    public async Task StopPeakSamplingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var capture = _capture;
        if (capture is null) return;
        try
        {
            capture.StopRecording();
            await _captureStopped!.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCapture(capture);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_capture is { } capture)
        {
            try { capture.StopRecording(); }
            finally { ReleaseCapture(capture); }
        }
        _enumerator.Dispose();
    }

    private void ReleaseCapture(WasapiRecorder capture)
    {
        capture.Dispose();
        _captureDevice?.Dispose();
        ClearCaptureState();
    }

    private void ClearCaptureState()
    {
        _capture = null;
        _captureDevice = null;
        _captureEndpointId = null;
        _captureStopped = null;
    }

    private sealed class SineWaveProvider(TimeSpan duration) : IWaveProvider
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;
        private readonly long _totalSamples = (long)(duration.TotalSeconds * SampleRate * Channels);
        private long _position;
        public WaveFormat WaveFormat { get; } = new(SampleRate, 16, Channels);

        public int Read(Span<byte> buffer)
        {
            var samples = Math.Min(buffer.Length / 2, _totalSamples - _position);
            for (long index = 0; index < samples; index++)
            {
                var frame = (_position + index) / Channels;
                var value = (short)(Math.Sin(2 * Math.PI * 440 * frame / SampleRate) * short.MaxValue * 0.1);
                buffer[(int)(index * 2)] = (byte)value;
                buffer[(int)(index * 2 + 1)] = (byte)(value >> 8);
            }
            _position += samples;
            return (int)samples * 2;
        }
    }
}
