// SPDX-License-Identifier: GPL-2.0-or-later
//
// Phase 2b acceptance gate: ZeusHost.Build must register NativeAudioSink (not
// WebSocketAudioSink) when HostMode=Desktop, and must also register
// NativeMicCapture as an IHostedService so the OS mic feeds TxAudioIngest in
// place of the browser MicPcm WS frames. Server mode must keep
// WebSocketAudioSink and never register the native services.
//
// We resolve via WebApplication.Services (DI introspection) WITHOUT calling
// StartAsync, so the hosted services don't actually open audio devices on
// the test runner. The audio I/O path is exercised separately by RX-only
// smoke on the dev box.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Zeus.Server;

namespace Zeus.Server.Tests;

public class NativeAudioSinkRegistrationTests
{
    [Fact]
    public async Task ServerMode_RegistersWebSocketAudioSink_AndNoNativeServices()
    {
        var opts = new ZeusHostOptions
        {
            HostMode = ZeusHostMode.Server,
            HttpPort = 0,
            BindAllInterfaces = false,
            UseHttpsLanCert = false,
            PrintConsoleBanner = false,
        };
        var app = ZeusHost.Build(Array.Empty<string>(), opts);

        var sinks = app.Services.GetServices<IRxAudioSink>().ToArray();
        Assert.Single(sinks);
        // Server mode keeps the WS sink — bit-for-bit equivalent of the
        // pre-seam direct hub broadcast.
        Assert.Equal("WebSocketAudioSink", sinks[0].GetType().Name);

        // Native capture must never be registered in server mode.
        var hosted = app.Services.GetServices<IHostedService>().ToArray();
        Assert.DoesNotContain(hosted, h => h.GetType().Name == "NativeAudioSink");
        Assert.DoesNotContain(hosted, h => h.GetType().Name == "NativeMicCapture");

        await app.DisposeAsync();
    }

    [Fact]
    public async Task DesktopMode_RegistersNativeAudioSink_AndNativeMicCaptureHostedService()
    {
        var opts = new ZeusHostOptions
        {
            HostMode = ZeusHostMode.Desktop,
            HttpPort = 0,
            BindAllInterfaces = false,
            UseHttpsLanCert = false,
            PrintConsoleBanner = false,
        };
        var app = ZeusHost.Build(Array.Empty<string>(), opts);

        var sinks = app.Services.GetServices<IRxAudioSink>().ToArray();
        Assert.Single(sinks);
        // Desktop mode swaps in the native sink so RX audio goes straight to
        // the OS default output device (Phase 2b).
        Assert.Equal("NativeAudioSink", sinks[0].GetType().Name);

        // Same NativeAudioSink instance must also be wired as a hosted
        // service so its StartAsync opens the playback device.
        var hosted = app.Services.GetServices<IHostedService>().ToArray();
        Assert.Contains(hosted, h => h.GetType().Name == "NativeAudioSink");
        Assert.Contains(hosted, h => h.GetType().Name == "NativeMicCapture");

        await app.DisposeAsync();
    }

    [Fact]
    public void MiniAudioInterop_NativeLibraryLoadsAndExposesVersionString()
    {
        // Forces NativeLibrary.SetDllImportResolver + the runtimes/<rid>/native/
        // probe to actually resolve `libminiaudio.{dylib,so,dll}`. Failure here
        // means the native build wasn't staged into Zeus.Dsp/runtimes/, which
        // is the most likely break shape for this phase.
        MiniAudioInterop.EnsureResolverRegistered();
        string v = MiniAudioInterop.Version();
        Assert.StartsWith("zeus-miniaudio ", v);
        // Vendored 0.11.x at the time of writing — assert the prefix not the
        // exact patch so a future re-vendor doesn't flap this test.
        Assert.Contains("0.11.", v);
    }
}
