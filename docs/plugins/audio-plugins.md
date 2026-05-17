# Audio plugins

Plugins contribute to the Zeus audio chain by implementing
`IAudioPlugin` (custom C# DSP) **or** by declaring `audio.vst3Path` in
their manifest (in-process VST3 hosting via the native bridge).

Both routes feed into the same `AudioChain` — an 8-slot serial chain
with a master enable flag and per-slot bypass. When the master is
off, the chain is a single `Span.CopyTo` — bit-identical to "no chain
at all".

## Contract

```csharp
public interface IAudioPlugin
{
    string DisplayName { get; }
    AudioPluginRequirements Requirements { get; }   // sample rate, channels, block size

    Task InitializeAudioAsync(IAudioHost host, CancellationToken ct);
    void Process(ReadOnlySpan<float> input, Span<float> output, AudioBlockContext ctx);
    Task ShutdownAudioAsync(CancellationToken ct);
}
```

### `Requirements`

```csharp
public sealed record AudioPluginRequirements(
    int SampleRate,
    int Channels,
    int BlockSize);
```

The host honours these and refuses to load the plugin if they can't
be satisfied by the current TX/RX path. For v1, `SampleRate` is
48 kHz and `BlockSize` is 256 frames; future hardware-dependent rates
will negotiate via `IAudioHost`.

### Realtime contract for `Process()`

`Process()` runs on the audio thread. It MUST NOT:

- allocate (`new`, `string` formatting, `List<T>.Add` past capacity)
- lock (no `Monitor.Enter`, `SemaphoreSlim.Wait`, etc.)
- perform IO (file, network, console)
- call into any code that does the above (e.g. logging, EF Core, JSON
  serialisation)

It MAY:

- read/write the `input`/`output` Spans
- read its own pre-allocated state arrays
- call inline math (no virtual dispatch unless you've cached the
  vtable)

Misbehaviour will glitch the on-air audio. The host catches
exceptions thrown from `Process()` and pass-throughs the block, but
it can't catch deadlocks or slow allocations.

In-place processing is supported (call site guarantees `input` and
`output` don't overlap on the chain entry, but slots downstream see
the same buffer on both sides — your plugin should be tolerant of
`input.CopyTo(output); /* mutate output */`).

### Bypass

When the host bypasses a slot, your `Process()` is NOT called. Don't
do anything special — the chain handles the skip.

## Route 1: bundle a VST3, write no C# audio code

Drop a `.vst3` bundle into your plugin zip and reference it:

```json
"audio": {
  "vst3Path": "vst3/MyEffect.vst3",
  "slot": "tx.post-leveler",
  "channels": 1,
  "sampleRate": 48000
}
```

The host synthesises a `VstHostAudioPlugin` that:

1. Calls `zvst_load_vst3(absPath, channels, sampleRate, 256, &handle)`
   on the native bridge.
2. Maps `Process(input, output, ctx)` to `zvst_process(handle, ...)`
   with channel-major planar layout.
3. Calls `zvst_unload(handle)` on shutdown.

The native bridge (under `native/zeus-vst-bridge/`) handles
`Module::create` → factory walk → `IComponent` activation →
`IAudioProcessor::process`. All in-process; no IPC.

## Route 2: implement `IAudioPlugin` in C#

```csharp
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Contracts.Extensions;

namespace Example;

public sealed class GainPlugin : IZeusPlugin, IAudioPlugin
{
    private float _gain = 1.0f;

    // IZeusPlugin
    public Task InitializeAsync(IPluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;

    // IAudioPlugin
    public string DisplayName => "Gain";
    public AudioPluginRequirements Requirements => new(48000, 1, 256);

    public Task InitializeAudioAsync(IAudioHost host, CancellationToken ct)
        => Task.CompletedTask;

    public void Process(ReadOnlySpan<float> input, Span<float> output, AudioBlockContext ctx)
    {
        var g = _gain;
        for (int i = 0; i < input.Length; i++) output[i] = input[i] * g;
    }

    public Task ShutdownAudioAsync(CancellationToken ct) => Task.CompletedTask;
}
```

The plugin loader notices the `IAudioPlugin` implementation and
slots it into the chain at the manifest's declared `audio.slot`.
You'd typically expose a parameter via `IBackendPlugin` so the
operator can tweak `_gain` from a panel.

## Chain mechanics

`AudioChain` is the host-owned orchestrator:

- 8 slots, indexed 0..7.
- Master enable (`MasterEnabled`): when false, the chain is a single
  `input.CopyTo(output)` — no per-slot dispatch, no allocation.
- Per-slot `Bypassed` flag — toggle without unloading the plugin.
- Slot mutation methods (`SetSlot`, `ClearSlot`, `SetSlotBypass`) run
  on the control thread, never inside `Process()`.

Internally the chain ping-pongs between the caller's `output` Span
and a pre-allocated scratch buffer so N stages cost exactly N
`Process()` calls plus at most one final copy back to `output`. No
per-block allocation.

## TX-path wiring (post-rebuild status)

In iter 7 of the plugin-system rebuild, `AudioChain` is fully
self-contained — it processes blocks correctly and is unit-tested,
but the WDSP TX path doesn't yet dispatch into it. The seam was
removed during iter 1 and the new one will land after maintainer
review of the chain shape (the prior seam coupled `Zeus.Dsp` to
`Zeus.PluginHost` directly; the new one uses a delegate to keep that
boundary clean).

For now: `IAudioPlugin`-implementing plugins load and appear in
**Settings → Plugins**, but audio doesn't flow through them on a
real radio yet. The Plugin Browser correctly identifies them via the
`audio` badge on the card.

## Native bridge build

The native VST3 bridge is not built by `dotnet build`. Build once:

```bash
cd native/zeus-vst-bridge
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j 4
```

Output: `libzeus-vst-bridge.{so,dylib,dll}` (~280KB on macOS
arm64). The test project's `CopyVstBridgeDylib` MSBuild target
copies it next to the test binary so P/Invoke resolution finds it.
Operators running Zeus pre-built install the library alongside the
Zeus executable; the release pipeline does this automatically.

## Tests

- `AudioChainTests.cs` — 11 tests: empty chain pass-through, master
  disable, single + composed slots, bypass, gap slots, out-of-range
  guards.
- `VstHostAudioPluginTests.cs` — 5 tests: init happy path, missing
  vst3 file, ABI mismatch propagation, pre-init pass-through, bridge
  failure pass-through. Uses an in-process fake `IVstBridgeNative`
  — no native lib required.
- `VstBridgeNativeRealTests.cs` — 5 tests against the actually-built
  dylib. Skip with a friendly message if the bridge isn't built.
