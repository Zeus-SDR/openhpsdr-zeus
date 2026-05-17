/* SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Openhpsdr-Zeus — In-process VST3 host bridge.
 * C ABI consumed by Zeus.Plugins.Host.Audio.VstBridgeNative (P/Invoke).
 *
 * Stability contract: this header is the single source of truth for the
 * .NET ↔ native boundary. Adding new functions is forward-compatible;
 * removing or changing existing signatures REQUIRES bumping ZVST_ABI.
 * The .NET side checks the ABI on init and refuses on mismatch, so a
 * wire-format drift cannot silently corrupt audio.
 */

#ifndef OPENHPSDR_ZEUS_ZVST_H
#define OPENHPSDR_ZEUS_ZVST_H

#include <stdint.h>

/* Export the C ABI. The shared library is compiled with hidden
 * default visibility on Unix to keep vst3sdk's internals out of the
 * dylib's exported-symbol table; each entry point below is then
 * re-exported explicitly. Windows uses __declspec(dllexport). */
#if defined(_WIN32) || defined(__CYGWIN__)
#  define ZVST_EXPORT __declspec(dllexport)
#elif defined(__GNUC__) || defined(__clang__)
#  define ZVST_EXPORT __attribute__((visibility("default")))
#else
#  define ZVST_EXPORT
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Bridge ABI version. Bump when any function below changes shape. */
#define ZVST_ABI 1

/* Status codes — must match VstBridgeStatus in C#. */
typedef enum zvst_status_t {
    ZVST_OK                    = 0,
    ZVST_ABI_MISMATCH          = 1,
    ZVST_FILE_NOT_FOUND        = 2,
    ZVST_NOT_A_VST3            = 3,
    ZVST_NO_AUDIO_EFFECT_CLASS = 4,
    ZVST_ACTIVATE_FAILED       = 5,
    ZVST_INVALID_HANDLE        = 6,
    ZVST_INVALID_ARGUMENTS     = 7,
    ZVST_NOT_IMPLEMENTED       = 8,
    ZVST_OTHER                 = 255
} zvst_status_t;

/* Opaque plugin handle. The .NET side treats this as a void* / nint. */
typedef void* zvst_handle_t;

/*
 * Initialise the bridge. abi MUST equal ZVST_ABI; the bridge returns
 * ZVST_ABI_MISMATCH otherwise. Idempotent: safe to call multiple times
 * from independent loaders.
 */
ZVST_EXPORT int32_t zvst_init(int32_t abi);

/*
 * Load a VST3 plugin from `path` and prepare it to process audio at
 * the supplied geometry. On success, *out_handle is set to a non-NULL
 * value and the return is ZVST_OK.
 *
 * `path` is a UTF-8 absolute path to either a .vst3 bundle directory
 * (the common case) or a single .vst3 file (some flat-file Linux
 * builds). `channels` is 1 or 2; `sample_rate` 44100..192000;
 * `block_size` 32..4096.
 *
 * The handle is owned by the bridge until zvst_unload is called.
 */
ZVST_EXPORT int32_t zvst_load_vst3(
    const char* path,
    int32_t channels,
    int32_t sample_rate,
    int32_t block_size,
    zvst_handle_t* out_handle);

/*
 * Process `frames` of audio. `input` and `output` are planar float32
 * buffers of length channels * frames (channel-major layout — channel
 * 0's frames first, then channel 1's). In-place call (input == output)
 * is permitted.
 *
 * Realtime contract: this function MUST NOT allocate, lock, or
 * perform IO. If the plugin internally violates this contract, the
 * operator sees a glitch but the host stays up.
 */
ZVST_EXPORT int32_t zvst_process(
    zvst_handle_t handle,
    const float* input,
    float* output,
    int32_t frames);

/*
 * Set parameter `param_id` to `normalized` (clamped to [0,1] by the
 * bridge). Safe to call from the control thread; the VST3 controller
 * is required by spec to be reentrant relative to the audio thread.
 */
ZVST_EXPORT int32_t zvst_set_param(
    zvst_handle_t handle,
    uint32_t param_id,
    double normalized);

/*
 * Release the loaded plugin. The handle is invalid after this call.
 * Idempotent on a NULL handle (returns ZVST_OK).
 */
ZVST_EXPORT int32_t zvst_unload(zvst_handle_t handle);

/*
 * Release any process-wide bridge resources. Safe to call multiple
 * times; matched call counting against zvst_init.
 */
ZVST_EXPORT int32_t zvst_shutdown(void);

#ifdef __cplusplus
}
#endif

#endif /* OPENHPSDR_ZEUS_ZVST_H */
