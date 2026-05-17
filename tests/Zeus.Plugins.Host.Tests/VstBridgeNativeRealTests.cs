using System.Runtime.InteropServices;
using Zeus.Plugins.Host.Audio;

namespace Zeus.Plugins.Host.Tests;

/// <summary>
/// Exercises <see cref="VstBridgeNative"/> against the actually-built
/// native bridge dylib (when present). The dylib is produced by the
/// CMake build under <c>native/zeus-vst-bridge/</c>; the test project's
/// CopyVstBridgeDylib MSBuild target copies it next to the test binary
/// on each build. If the dylib is absent (the operator hasn't run
/// cmake yet), every test in this class is skipped so the dotnet test
/// suite remains green out of the box.
/// </summary>
public class VstBridgeNativeRealTests
{
    private static string NativeFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return "libzeus-vst-bridge.dylib";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return "libzeus-vst-bridge.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "zeus-vst-bridge.dll";
        return "";
    }

    private static bool NativeAvailable()
    {
        var name = NativeFileName();
        if (string.IsNullOrEmpty(name)) return false;
        var path = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(path);
    }

    private static readonly bool SkipBecauseNoNative = !NativeAvailable();
    private const string SkipReason =
        "Native zeus-vst-bridge not built — run `cmake -B native/zeus-vst-bridge/build && cmake --build native/zeus-vst-bridge/build`.";

    [SkippableFact]
    public void Init_WithCurrentAbi_ReturnsOk()
    {
        Skip.If(SkipBecauseNoNative, SkipReason);
        var bridge = new VstBridgeNative();
        Assert.Equal(VstBridgeStatus.Ok, bridge.Init(VstBridgeAbi.Current));
        bridge.Shutdown();
    }

    [SkippableFact]
    public void Init_WithWrongAbi_ReturnsAbiMismatch()
    {
        Skip.If(SkipBecauseNoNative, SkipReason);
        var bridge = new VstBridgeNative();
        Assert.Equal(VstBridgeStatus.AbiMismatch, bridge.Init(VstBridgeAbi.Current + 1000));
    }

    [SkippableFact]
    public void LoadVst3_NonExistentPath_ReturnsFileNotFound()
    {
        Skip.If(SkipBecauseNoNative, SkipReason);
        var bridge = new VstBridgeNative();
        Assert.Equal(VstBridgeStatus.Ok, bridge.Init(VstBridgeAbi.Current));
        try
        {
            var status = bridge.LoadVst3(
                "/definitely/does/not/exist.vst3",
                channels: 1, sampleRate: 48000, blockSize: 256,
                out var handle);
            Assert.Equal(VstBridgeStatus.FileNotFound, status);
            Assert.Equal(IntPtr.Zero, handle);
        }
        finally
        {
            bridge.Shutdown();
        }
    }

    [SkippableFact]
    public void LoadVst3_InvalidChannelCount_ReturnsInvalidArguments()
    {
        Skip.If(SkipBecauseNoNative, SkipReason);
        var bridge = new VstBridgeNative();
        Assert.Equal(VstBridgeStatus.Ok, bridge.Init(VstBridgeAbi.Current));
        try
        {
            var status = bridge.LoadVst3(
                "/tmp/whatever.vst3",
                channels: 99,    // > 2 — rejected
                sampleRate: 48000, blockSize: 256,
                out _);
            Assert.Equal(VstBridgeStatus.InvalidArguments, status);
        }
        finally
        {
            bridge.Shutdown();
        }
    }

    [SkippableFact]
    public void Unload_OnNullHandle_IsNoOp()
    {
        Skip.If(SkipBecauseNoNative, SkipReason);
        var bridge = new VstBridgeNative();
        Assert.Equal(VstBridgeStatus.Ok, bridge.Init(VstBridgeAbi.Current));
        Assert.Equal(VstBridgeStatus.Ok, bridge.Unload(IntPtr.Zero));
        bridge.Shutdown();
    }
}
