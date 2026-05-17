# zeus-vst-bridge

Native, in-process VST3 host for Openhpsdr-Zeus. Linked as a shared
library and called via P/Invoke from `Zeus.Plugins.Host.Audio.VstBridgeNative`.

## Status (2026-05-17)

**Iter 6** lands the C ABI (`include/zvst.h`) plus a stub
implementation that pretends every load succeeds and pass-through
processes every block. The point is to lock the wire shape so the
.NET wrapper, `VstHostAudioPlugin`, and `AudioChain` can be tested
without a real Steinberg toolchain on the build machine.

**Iter 7** replaces the stub with:

- [Steinberg `vst3sdk`](https://github.com/steinbergmedia/vst3sdk) (MIT
  since October 2025) — `Module::create(path)` → factory walk →
  instantiate `kVstAudioEffectClass` → `initialise` / `setActive` /
  `setProcessing` / `ProcessData`.
- Optionally [CLAP SDK](https://github.com/free-audio/clap) — MIT.

VST2 is **not** in scope (Steinberg withdrew distribution rights for
new hosts in 2024 — see `docs/proposals/plugin-system-v2.md`).

## Build

```bash
cd native/zeus-vst-bridge
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
```

Output:

- Linux:   `build/libzeus-vst-bridge.so`
- macOS:   `build/libzeus-vst-bridge.dylib`
- Windows: `build/Release/zeus-vst-bridge.dll`

Place it next to the Zeus executable (or anywhere on the runtime load
path — `LD_LIBRARY_PATH`, `DYLD_LIBRARY_PATH`, `PATH`).

## ABI

`include/zvst.h` is the single source of truth. The .NET side checks
`ZVST_ABI` on init via `zvst_init` and refuses to proceed on mismatch.
Bump `ZVST_ABI` in lockstep with any breaking change.

## License

GPL-2.0-or-later (matches Zeus core). Statically linkable against the
MIT-licensed `vst3sdk` and CLAP SDK.
