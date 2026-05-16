// SPDX-License-Identifier: GPL-2.0-or-later
//
// Zeus — OpenHPSDR Protocol-1 / Protocol-2 client.
// Copyright (C) 2026 Brian Keating (EI6LF) and contributors.
//
// Desktop-mode TX mic capture: replaces the browser → WS MicPcm uplink with
// a direct miniaudio capture device feed into TxAudioIngest. The two paths
// share the existing TxAudioIngest entry point (OnMicPcmBytes) so the WDSP
// TXA chain, IQ ring, and protocol packers don't know which transport
// supplied the audio.
//
// Frame shape: f32le little-endian, 960 samples mono @ 48 kHz (20 ms
// blocks), matching the browser worklet's MicPcm payload exactly. The
// AudioWorklet on the SPA emits the same shape; this just produces it from
// the OS default input device instead of getUserMedia.

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Zeus.Server;

/// <summary>
/// Desktop-mode hosted service that opens the OS default input device via
/// miniaudio and feeds 960-sample (20 ms @ 48 kHz mono) f32le blocks into
/// <see cref="TxAudioIngest.OnMicPcmBytes"/>. Service mode never registers
/// this service so the .dylib never loads from the server-only build.
///
/// <para>The mic device runs continuously once started; gating on MOX /
/// monitor state happens downstream in <see cref="TxAudioIngest"/>, exactly
/// as the existing browser path does. This keeps the capture warm so the
/// first key-up doesn't lose the first ~20 ms of audio waiting for the OS
/// to spin up the input device.</para>
///
/// <para>Downmix: when the OS hands us a stereo / multichannel input
/// (USB mics often default to stereo), we average across channels into a
/// mono sample. Resample: miniaudio's internal resampler converts to 48 kHz
/// if the device's native rate is something else (44.1 kHz common on Mac
/// laptops). Both happen behind the data callback so this code only deals
/// with 48 kHz mono frames.</para>
/// </summary>
internal sealed class NativeMicCapture : IHostedService, IDisposable
{
    private const int MicBlockSamples = 960;        // 20 ms @ 48 kHz, matches browser worklet
    private const int MicBlockBytes = MicBlockSamples * 4;

    private readonly TxAudioIngest _ingest;
    private readonly ILogger<NativeMicCapture> _log;

    private MiniAudioInput? _input;
    private readonly float[] _accum = new float[MicBlockSamples];
    private int _accumFill;
    // Pre-allocated payload buffer reused for every flush; OnMicPcmBytes
    // takes ReadOnlyMemory<byte> but doesn't retain it, so reuse is safe.
    private readonly byte[] _payload = new byte[MicBlockBytes];

    private long _totalSamplesIn;
    private long _totalBlocksOut;

    public NativeMicCapture(TxAudioIngest ingest, ILogger<NativeMicCapture> log)
    {
        _ingest = ingest;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _input = new MiniAudioInput(
                onFrames: OnCaptureData,
                onNotify: OnDeviceNotification,
                preferSampleRate: 48_000,
                preferChannels: 1,
                periodFrames: 480,
                periods: 2);
            _input.Start();
            _log.LogInformation(
                "audio.native.tx mic open rate={Rate}Hz channels={Channels}",
                _input.SampleRate, _input.Channels);
        }
        catch (Exception ex)
        {
            // Don't kill the host — desktop mode without a working mic should
            // still RX. The operator may not even have a mic plugged in.
            _log.LogWarning(ex, "audio.native.tx mic open failed; TX uplink disabled");
            _input = null;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try { _input?.Stop(); }
        catch (Exception ex) { _log.LogWarning(ex, "audio.native.tx stop threw"); }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _input?.Dispose(); }
        catch (Exception ex) { _log.LogWarning(ex, "audio.native.tx dispose threw"); }
        _input = null;
    }

    private void OnCaptureData(ReadOnlySpan<float> input, uint frameCount, uint channels)
    {
        int frames = (int)frameCount;
        int ch = (int)channels;
        if (frames == 0 || ch == 0) return;

        // Downmix to mono on the fly. For ch==1 this is a straight copy
        // through the accumulator.
        int srcIdx = 0;
        while (srcIdx < frames)
        {
            int needBeforeFlush = MicBlockSamples - _accumFill;
            int take = Math.Min(needBeforeFlush, frames - srcIdx);

            if (ch == 1)
            {
                input.Slice(srcIdx, take).CopyTo(new Span<float>(_accum, _accumFill, take));
            }
            else
            {
                // Average across channels for each frame in this batch.
                float invCh = 1.0f / ch;
                for (int i = 0; i < take; i++)
                {
                    float sum = 0f;
                    int baseIdx = (srcIdx + i) * ch;
                    for (int c = 0; c < ch; c++) sum += input[baseIdx + c];
                    _accum[_accumFill + i] = sum * invCh;
                }
            }

            _accumFill += take;
            srcIdx += take;
            _totalSamplesIn += take;

            if (_accumFill >= MicBlockSamples)
            {
                FlushBlock();
                _accumFill = 0;
            }
        }
    }

    private void FlushBlock()
    {
        // Encode f32le. On x64 / arm64 .NET targets, the host float endianness
        // matches the wire format (little-endian), so MemoryMarshal.AsBytes
        // produces the wanted payload directly. BinaryPrimitives is the safe
        // alternative if we ever target a big-endian runtime, but Zeus targets
        // none today.
        var srcBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<float>(_accum, 0, MicBlockSamples));
        srcBytes.CopyTo(_payload);

        try
        {
            _ingest.OnMicPcmBytes(new ReadOnlyMemory<byte>(_payload));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "audio.native.tx ingest threw on flush");
        }
        _totalBlocksOut++;
    }

    private void OnDeviceNotification(int kind)
    {
        string label = kind switch
        {
            1 => "started",
            2 => "stopped",
            3 => "rerouted (default device changed)",
            4 => "interruption_began",
            5 => "interruption_ended",
            6 => "unlocked",
            _ => $"kind={kind}",
        };
        _log.LogInformation("audio.native.tx event {Event}", label);
    }
}
