# native/miniaudio/ — vendored audio I/O for Zeus desktop mode

Single-header audio I/O library by David Reid (mackron), dual-licensed
public-domain / MIT-0. Used by Zeus desktop mode to:

- play the demodulated RX audio frames out of the default OS output device
  (`NativeAudioSink : IRxAudioSink`), bypassing the WebSocket fan-out
  entirely; and
- capture the operator's microphone from the default OS input device for the
  TX uplink (`NativeMicCapture`), bypassing the browser's `getUserMedia`
  path.

Server mode (the browser-served Zeus.Server build) does not use this library
at all — the WS sink and the browser mic path are unchanged.

## Vendor info

| | |
|---|---|
| Upstream    | https://github.com/mackron/miniaudio |
| Version     | v0.11.25 (commit/tag `master` 2026-03-04) |
| License     | MIT-0 / public-domain (compatible with Zeus's GPL-2-or-later) |
| Fetched via | `curl https://raw.githubusercontent.com/mackron/miniaudio/master/miniaudio.h` |

The single `miniaudio.h` is compiled by `zeus_miniaudio.c` (this directory),
which `#define`s `MINIAUDIO_IMPLEMENTATION` once and adds a thin Zeus C ABI on
top of miniaudio's struct-heavy native API. The Zeus C ABI is what C#
P/Invoke binds to; nothing in the .NET side touches a `ma_device`/`ma_device_config`
struct directly.

## Layout

```
native/miniaudio/
  miniaudio.h          # vendored single-header (do not edit)
  LICENSE              # MIT-0 / public-domain
  zeus_miniaudio.c     # Zeus C ABI on top of miniaudio (the only .c file)
  zeus_miniaudio.h     # function prototypes shared with the C# P/Invoke
  CMakeLists.txt       # builds libminiaudio.{dylib,so,dll} per RID
  README.md            # this file
```

## Build

`native/build.sh` runs miniaudio's build right after WDSP and stages
`libminiaudio.{dylib,so}` into `Zeus.Dsp/runtimes/<rid>/native/` alongside
`libwdsp` — same convention, same loader, no extra runtime config.

```sh
./native/build.sh                # Release; auto-detects RID
./native/build.sh Debug          # optional
```

On macOS miniaudio targets CoreAudio (default backend); Linux uses ALSA +
PulseAudio + JACK (compile-time); Windows uses WASAPI. Backend selection is
runtime-fallback within miniaudio — Zeus does not pin a backend.

## Why a wrapper C file rather than direct P/Invoke on miniaudio.h

miniaudio's public API takes `ma_device_config` and `ma_device` structs whose
fields and layout differ across versions. Marshalling those to C# would
require pinning a struct layout that we can't promise will survive a future
re-vendoring. The Zeus C ABI in `zeus_miniaudio.h` is small, stable, and
hides every miniaudio-internal struct behind an opaque `void*`, so swapping
the vendored upstream is a recompile, not a coordinated C#-side change.

## Trimmed features

`zeus_miniaudio.c` defines `MA_NO_DECODING`, `MA_NO_ENCODING`,
`MA_NO_GENERATION`, `MA_NO_RESOURCE_MANAGER`, and `MA_NO_ENGINE` before the
`MINIAUDIO_IMPLEMENTATION` include. Zeus only uses raw device playback /
capture; the file-decode and high-level engine layers stay out of the binary.
