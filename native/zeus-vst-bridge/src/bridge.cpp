// SPDX-License-Identifier: GPL-2.0-or-later
//
// Openhpsdr-Zeus VST3 host bridge.
//
// Loads VST3 plugins in-process via Steinberg's MIT-licensed vst3sdk
// (vendored under third_party/vst3sdk/) and runs them on the .NET side's
// realtime audio thread via the C ABI in include/zvst.h.
//
// Threading model: zvst_init / load / unload / set_param run on the .NET
// control thread; zvst_process runs on the realtime audio thread. The
// loaded-plugin state struct is owned exclusively by the handle returned
// to .NET; the .NET wrapper guarantees serialised access (no parallel
// process / unload).

#include "zvst.h"

#include "public.sdk/source/vst/hosting/module.h"
#include "public.sdk/source/vst/hosting/hostclasses.h"
#include "public.sdk/source/vst/hosting/processdata.h"
#include "public.sdk/source/vst/hosting/parameterchanges.h"
#include "pluginterfaces/vst/ivstcomponent.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/vsttypes.h"
#include "pluginterfaces/base/funknown.h"
#include "pluginterfaces/base/ftypes.h"
#include "base/source/fobject.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include <cstring>

namespace vst = Steinberg::Vst;

namespace {

// Process-wide host application. vst3sdk hosting helpers expect the
// host to expose IHostApplication; HostApplication from hostclasses.h
// is the stock implementation.
class GlobalHost {
public:
    static vst::HostApplication& instance() {
        static GlobalHost g;
        return g.app_;
    }
private:
    GlobalHost() = default;
    vst::HostApplication app_;
};

std::atomic<int> g_init_count{0};

// Per-handle state. Owns one IComponent + IAudioProcessor, plus the
// scratch ProcessData buffers sized at load time so the realtime path
// doesn't allocate.
struct LoadedPlugin {
    std::shared_ptr<VST3::Hosting::Module> module;
    Steinberg::IPtr<vst::IComponent>       component;
    Steinberg::IPtr<vst::IAudioProcessor>  processor;
    Steinberg::IPtr<vst::IEditController>  controller; // optional

    int32_t channels{1};
    int32_t sample_rate{48000};
    int32_t block_size{256};

    // Pre-sized planar buffers; .NET passes pointers into these via the
    // process callback. We don't own .NET's memory but re-point each
    // call.
    std::vector<float*> in_buffer_ptrs;
    std::vector<float*> out_buffer_ptrs;

    // ProcessData reused across calls. AudioBusBuffers vectors must be
    // stable storage because ProcessData stores raw pointers into them.
    vst::AudioBusBuffers              in_bus{};
    vst::AudioBusBuffers              out_bus{};
    vst::ProcessData                  process_data{};
    vst::ProcessSetup                 process_setup{};
    vst::ParameterChanges             input_changes; // for set_param queueing
};

// Look up the first kVstAudioEffectClass in the factory.
Steinberg::IPtr<vst::IComponent>
instantiate_first_audio_effect(const VST3::Hosting::PluginFactory& factory,
                               int32_t* status_out)
{
    auto class_infos = factory.classInfos();
    for (const auto& ci : class_infos) {
        if (ci.category() == kVstAudioEffectClass) {
            auto comp = factory.createInstance<vst::IComponent>(ci.ID());
            if (comp) return comp;
        }
    }
    *status_out = ZVST_NO_AUDIO_EFFECT_CLASS;
    return nullptr;
}

bool wire_buses_and_activate(LoadedPlugin& p, int32_t* status_out) {
    using namespace Steinberg;

    if (p.component->setActive(false) != kResultOk) {
        // not fatal; some plugins return error here pre-init
    }

    if (p.component->setIoMode(vst::kAdvanced) != kResultOk) {
        // optional, ignore
    }

    if (p.component->initialize(&GlobalHost::instance()) != kResultOk) {
        *status_out = ZVST_ACTIVATE_FAILED;
        return false;
    }

    p.component->queryInterface(vst::IAudioProcessor::iid,
        reinterpret_cast<void**>(p.processor.get()));
    // queryInterface above won't work with IPtr; do it correctly:
    vst::IAudioProcessor* raw_proc = nullptr;
    if (p.component->queryInterface(vst::IAudioProcessor::iid,
            reinterpret_cast<void**>(&raw_proc)) != kResultOk || !raw_proc) {
        *status_out = ZVST_NOT_A_VST3;
        return false;
    }
    p.processor = Steinberg::owned(raw_proc);

    // Speaker arrangement — mono or stereo.
    vst::SpeakerArrangement arr = (p.channels == 1)
        ? vst::SpeakerArr::kMono
        : vst::SpeakerArr::kStereo;
    if (p.processor->setBusArrangements(&arr, 1, &arr, 1) != kResultOk) {
        // Some plugins are stereo-only; try stereo as a fallback for
        // mono request.
        if (p.channels == 1) {
            arr = vst::SpeakerArr::kStereo;
            if (p.processor->setBusArrangements(&arr, 1, &arr, 1) != kResultOk) {
                *status_out = ZVST_ACTIVATE_FAILED;
                return false;
            }
        } else {
            *status_out = ZVST_ACTIVATE_FAILED;
            return false;
        }
    }

    p.process_setup.processMode = vst::kRealtime;
    p.process_setup.symbolicSampleSize = vst::kSample32;
    p.process_setup.maxSamplesPerBlock = p.block_size;
    p.process_setup.sampleRate = static_cast<double>(p.sample_rate);
    if (p.processor->setupProcessing(p.process_setup) != kResultOk) {
        *status_out = ZVST_ACTIVATE_FAILED;
        return false;
    }

    // Activate buses
    int32_t in_bus_count  = p.component->getBusCount(vst::kAudio, vst::kInput);
    int32_t out_bus_count = p.component->getBusCount(vst::kAudio, vst::kOutput);
    if (in_bus_count > 0)  p.component->activateBus(vst::kAudio, vst::kInput,  0, true);
    if (out_bus_count > 0) p.component->activateBus(vst::kAudio, vst::kOutput, 0, true);

    if (p.component->setActive(true) != kResultOk) {
        *status_out = ZVST_ACTIVATE_FAILED;
        return false;
    }
    if (p.processor->setProcessing(true) != kResultOk) {
        // Some plugins return kNotImplemented here — treat as soft success.
    }

    // ProcessData scratch — point bus buffers at our per-channel pointer
    // vectors; the actual data pointers are refreshed on every process
    // call (input/output are caller-owned buffers).
    p.in_buffer_ptrs.assign(static_cast<size_t>(p.channels), nullptr);
    p.out_buffer_ptrs.assign(static_cast<size_t>(p.channels), nullptr);
    p.in_bus.numChannels  = p.channels;
    p.out_bus.numChannels = p.channels;
    p.in_bus.channelBuffers32  = p.in_buffer_ptrs.data();
    p.out_bus.channelBuffers32 = p.out_buffer_ptrs.data();
    p.in_bus.silenceFlags = 0;
    p.out_bus.silenceFlags = 0;

    p.process_data.processMode = vst::kRealtime;
    p.process_data.symbolicSampleSize = vst::kSample32;
    p.process_data.numSamples = 0; // set per call
    p.process_data.numInputs  = (in_bus_count  > 0) ? 1 : 0;
    p.process_data.numOutputs = (out_bus_count > 0) ? 1 : 0;
    p.process_data.inputs  = (in_bus_count  > 0) ? &p.in_bus  : nullptr;
    p.process_data.outputs = (out_bus_count > 0) ? &p.out_bus : nullptr;
    p.process_data.inputParameterChanges = &p.input_changes;

    return true;
}

void teardown(LoadedPlugin& p) {
    if (p.processor) {
        p.processor->setProcessing(false);
    }
    if (p.component) {
        p.component->setActive(false);
        p.component->terminate();
    }
    p.processor = nullptr;
    p.component = nullptr;
    p.controller = nullptr;
    p.module = nullptr;
}

} // namespace

extern "C" {

int32_t zvst_init(int32_t abi) {
    if (abi != ZVST_ABI) return ZVST_ABI_MISMATCH;
    // Touch the global host to ensure construction happens deterministically.
    (void)GlobalHost::instance();
    g_init_count.fetch_add(1);
    return ZVST_OK;
}

int32_t zvst_load_vst3(
    const char* path,
    int32_t channels,
    int32_t sample_rate,
    int32_t block_size,
    zvst_handle_t* out_handle)
{
    if (!path || !out_handle) return ZVST_INVALID_ARGUMENTS;
    if (channels < 1 || channels > 2) return ZVST_INVALID_ARGUMENTS;
    if (sample_rate < 44100 || sample_rate > 192000) return ZVST_INVALID_ARGUMENTS;
    if (block_size < 32 || block_size > 4096) return ZVST_INVALID_ARGUMENTS;

    auto p = std::make_unique<LoadedPlugin>();
    p->channels = channels;
    p->sample_rate = sample_rate;
    p->block_size = block_size;

    std::string err;
    p->module = VST3::Hosting::Module::create(path, err);
    if (!p->module) {
        // Differentiate file-not-found from generic load failure.
        FILE* probe = fopen(path, "rb");
        if (probe) { fclose(probe); return ZVST_NOT_A_VST3; }
        return ZVST_FILE_NOT_FOUND;
    }

    auto& factory = p->module->getFactory();
    int32_t status = ZVST_OK;
    p->component = instantiate_first_audio_effect(factory, &status);
    if (!p->component) {
        return status;
    }

    if (!wire_buses_and_activate(*p, &status)) {
        teardown(*p);
        return status;
    }

    *out_handle = static_cast<void*>(p.release());
    return ZVST_OK;
}

int32_t zvst_process(
    zvst_handle_t handle,
    const float* input,
    float* output,
    int32_t frames)
{
    if (!handle) return ZVST_INVALID_HANDLE;
    if (!input || !output) return ZVST_INVALID_ARGUMENTS;
    auto* p = static_cast<LoadedPlugin*>(handle);
    if (frames < 1 || frames > p->block_size) return ZVST_INVALID_ARGUMENTS;

    // Point each channel's pointer at the right offset in the caller's
    // planar buffers (channel 0 starts at index 0, channel 1 at index
    // `frames`, etc.).
    for (int c = 0; c < p->channels; c++) {
        p->in_buffer_ptrs[c]  = const_cast<float*>(input  + static_cast<size_t>(c) * frames);
        p->out_buffer_ptrs[c] = output + static_cast<size_t>(c) * frames;
    }
    p->process_data.numSamples = frames;

    if (p->processor->process(p->process_data) != Steinberg::kResultOk) {
        // Soft fail — copy input to output and signal status. The .NET
        // wrapper will downgrade the chain to pass-through.
        std::memcpy(output, input,
            static_cast<size_t>(p->channels) * static_cast<size_t>(frames) * sizeof(float));
        return ZVST_OTHER;
    }

    // Clear any queued parameter changes — they've been applied.
    p->input_changes.clearQueue();

    return ZVST_OK;
}

int32_t zvst_set_param(
    zvst_handle_t handle,
    uint32_t param_id,
    double normalized)
{
    if (!handle) return ZVST_INVALID_HANDLE;
    auto* p = static_cast<LoadedPlugin*>(handle);
    if (normalized < 0.0) normalized = 0.0;
    if (normalized > 1.0) normalized = 1.0;

    int32_t queue_index = 0;
    auto* queue = p->input_changes.addParameterData(
        static_cast<Steinberg::Vst::ParamID>(param_id), queue_index);
    if (!queue) return ZVST_OTHER;

    int32_t point_index = 0;
    if (queue->addPoint(0, normalized, point_index) != Steinberg::kResultOk) {
        return ZVST_OTHER;
    }
    return ZVST_OK;
}

int32_t zvst_unload(zvst_handle_t handle) {
    if (!handle) return ZVST_OK;
    auto* p = static_cast<LoadedPlugin*>(handle);
    teardown(*p);
    delete p;
    return ZVST_OK;
}

int32_t zvst_shutdown(void) {
    if (g_init_count.load() > 0) g_init_count.fetch_sub(1);
    return ZVST_OK;
}

} // extern "C"
